#if ADDRESSABLES_INSTALLED
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Laubrary.Zounds {

    /// <summary>
    /// Builds a complete dependency graph of all Zounds, AudioClips, orphans, and broken references.
    /// Designed as an immutable snapshot: create a new instance to refresh.
    /// </summary>
    public sealed class ZoundDependencyAnalyzer {

        /// <summary>Represents one node in the dependency graph.</summary>
        public sealed class ZoundNode {
            public Zound zound;
            /// <summary>Zounds or AudioClips this Zound directly depends on (children / source clips).</summary>
            public List<Zound> dependsOn = new List<Zound>();
            /// <summary>Zounds that directly reference this Zound as a child entry.</summary>
            public List<Zound> dependedOnBy = new List<Zound>();
            /// <summary>AudioClip asset paths this Zound directly references (source + rendered).</summary>
            public List<string> clipPaths = new List<string>();
            /// <summary>True when at least one clip reference is missing / broken.</summary>
            public bool isBroken;
            /// <summary>Human-readable reason for being broken.</summary>
            public string brokenReason;
            /// <summary>Detailed info for fix UI.</summary>
            public List<BrokenRefInfo> brokenRefs = new List<BrokenRefInfo>();
        }

        public sealed class BrokenRefInfo {
            public string label;
            public string missingKey;
            public AssetReference clipRef;
            public string clipPath;
            public bool isSource;
            public AudioClip stagedFix;
            public int zoundId; // Added to help with fixing logic
        }

        /// <summary>Represents an AudioClip known to the project.</summary>
        public sealed class ClipNode {
            public string assetPath;
            public string fileName;
            public AudioClip clip;
            /// <summary>All Zounds that directly reference this clip (as source or rendered).</summary>
            public List<Zound> referencedBy = new List<Zound>();
            /// <summary>True when this clip lives in a build-included folder (Library, WorkFiles, ZoundFiles).</summary>
            public bool includedInBuild;
            /// <summary>True when this clip is in the Library folder (exportable by name).</summary>
            public bool isLibraryClip;
            /// <summary>True when this clip lives in WorkFiles/ZoundFiles but is not referenced by any Zound.</summary>
            public bool isOrphan;
            /// <summary>True when this clip is in the Sources folder but not referenced by any Zound.</summary>
            public bool isUnusedSource;
            /// <summary>True when this clip is in the Library folder but not referenced by any Zound.</summary>
            public bool isUnusedLibrary;


        }

        public Dictionary<int, ZoundNode> zoundNodes { get; private set; } = new Dictionary<int, ZoundNode>();
        public Dictionary<string, ClipNode> clipNodes { get; private set; } = new Dictionary<string, ClipNode>();
        public List<ZoundNode> brokenZounds { get; private set; } = new List<ZoundNode>();
        public Dictionary<string, BrokenGroup> brokenGroups { get; private set; } = new Dictionary<string, BrokenGroup>();
        public List<ClipNode> orphanClips { get; private set; } = new List<ClipNode>();
        public List<ClipNode> unusedSourceClips { get; private set; } = new List<ClipNode>();
        public List<ClipNode> unusedLibraryClips { get; private set; } = new List<ClipNode>();
        public List<ClipNode> buildClips { get; private set; } = new List<ClipNode>();

        public sealed class BrokenGroup {
            public string missingKey;
            public List<BrokenRefInfo> links = new List<BrokenRefInfo>();
            public AudioClip stagedFix;

            // Tracks the hierarchy of affected Zounds
            // Top Key: Parent/Root Name (e.g. "warriorspawn")
            // Value: Dictionary of (Child Path -> List of broken slot labels)
            public Dictionary<string, Dictionary<string, List<string>>> affectedHierarchy = new Dictionary<string, Dictionary<string, List<string>>>();
        }

        // ── Construction ────────────────────────────────────────────────

        /// <summary>
        /// Performs a full analysis of the current ZoundsProject. The result is an immutable snapshot.
        /// </summary>
        public static ZoundDependencyAnalyzer Analyze() {
            var analyzer = new ZoundDependencyAnalyzer();
            analyzer.Run();
            return analyzer;
        }

        private void Run() {
            var library = ZoundsProject.Instance.zoundLibrary;
            var settings = ZoundsProject.Instance.projectSettings;

            // ── Phase 1: Create a ZoundNode for every Zound ──
            library.ForEachZound(z => {
                if (z is ClipZound) return;
                if (zoundNodes.ContainsKey(z.id)) return;
                zoundNodes[z.id] = new ZoundNode { zound = z };
            });

            // ── Phase 2: Populate dependsOn / dependedOnBy for CompositeZounds ──
            foreach (var kvp in zoundNodes) {
                var node = kvp.Value;
                if (node.zound is CompositeZound composite) {
                    foreach (var entry in composite.zoundEntries) {
                        if (composite.TryGetEntryZound(entry, out var childZound)) {
                            if (childZound is ClipZound) continue;
                            node.dependsOn.Add(childZound);
                            if (zoundNodes.TryGetValue(childZound.id, out var childNode)) {
                                childNode.dependedOnBy.Add(node.zound);
                            }
                        }
                    }
                }
            }

            // ── Phase 3: Collect AudioClip references per Zound, detect broken refs ──
            foreach (var kvp in zoundNodes) {
                var node = kvp.Value;
                
                // If this is a local Klip, its path should include the parent's name
                string pathPrefix = "";
                if (node.zound.parentId != 0 && zoundNodes.TryGetValue(node.zound.parentId, out var parentNode)) {
                    pathPrefix = parentNode.zound.name;
                }
                
                CollectClipReferences(node, pathPrefix);
            }

            // ── Phase 4: Scan disk for all AudioClips in managed folders ──
            string[] managedFolders = GetExistingFolders(
                settings.libraryFolderPath,
                settings.sourcesFolderPath,
                settings.workFolderPath,
                settings.zoundFilesFolderPath
            );

            if (managedFolders.Length > 0) {
                string[] allGuids = AssetDatabase.FindAssets("t:AudioClip", managedFolders);
                foreach (string guid in allGuids) {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!clipNodes.ContainsKey(path)) {
                        clipNodes[path] = new ClipNode {
                            assetPath = path,
                            fileName = System.IO.Path.GetFileName(path),
                            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path)
                        };
                    }
                }
            }

            // ── Phase 5: Classify each clip via ZoundsClipClassifier (single source of truth) ──
            var referencedPaths = new HashSet<string>();
            foreach (var kvp in zoundNodes) {
                foreach (string p in kvp.Value.clipPaths) {
                    referencedPaths.Add(p);
                }
            }

            foreach (var kvp in clipNodes) {
                var cn = kvp.Value;
                var folder = ZoundsClipClassifier.GetFolder(cn.assetPath, settings);
                bool isReferenced = referencedPaths.Contains(cn.assetPath);
                string clipGuid = AssetDatabase.AssetPathToGUID(cn.assetPath);
                bool isOutputClip = ZoundsClipClassifier.IsOutputClip(clipGuid, library);

                var status = ZoundsClipClassifier.Classify(folder, isReferenced, isOutputClip);
                cn.isLibraryClip = status.isLibraryClip;
                cn.includedInBuild = status.shouldShip;
                cn.isOrphan = status.isOrphan;
                cn.isUnusedSource = status.isUnusedSource;
                cn.isUnusedLibrary = status.isUnusedLibrary;
            }

            // ── Phase 6: Populate results lists ──
            brokenZounds = zoundNodes.Values.Where(n => n.isBroken).ToList();
            orphanClips = clipNodes.Values.Where(c => c.isOrphan).OrderBy(c => c.assetPath).ToList();
            unusedSourceClips = clipNodes.Values.Where(c => c.isUnusedSource).OrderBy(c => c.assetPath).ToList();
            unusedLibraryClips = clipNodes.Values.Where(c => c.isUnusedLibrary).OrderBy(c => c.assetPath).ToList();
            buildClips = clipNodes.Values.Where(c => c.includedInBuild).OrderBy(c => c.assetPath).ToList();
        }

        private void CollectClipReferences(ZoundNode node, string parentPath = "") {
            var z = node.zound;
            string currentPath = string.IsNullOrEmpty(parentPath) ? z.name : $"{parentPath} / {z.name}";

            if (z is Klip klip) {
                // External-source Klips don't have audioClipRef — skip the source check for those.
                bool hasExternalSource = !string.IsNullOrEmpty(klip.externalSourcePath);
                if (!hasExternalSource) {
                    RegisterClipRef(node, klip.audioClipRef, klip.audioClipPath, "source clip", true, currentPath);
                }
                if (klip.HasActiveEdits()) {
                    RegisterClipRef(node, klip.renderedClipRef, klip.renderedClipPath, "rendered clip", false, currentPath);
                }
                if (klip.outputClipRef != null && !string.IsNullOrEmpty(klip.outputClipRef.AssetGUID)) {
                    RegisterClipRef(node, klip.outputClipRef, klip.outputClipPath, "output clip", false, currentPath);
                }
            }
            else if (z is Zequence zeq) {
                if (zeq.renderedClipRef != null && !string.IsNullOrEmpty(zeq.renderedClipRef.AssetGUID)) {
                    RegisterClipRef(node, zeq.renderedClipRef, zeq.renderedClipPath, "rendered clip", false, currentPath);
                }
            }
        }

        private void RegisterClipRef(ZoundNode node, AssetReference clipRef, string clipPath, string label, bool isSource, string displayPath) {
            bool isMissing = false;

            if (clipRef == null || string.IsNullOrEmpty(clipRef.AssetGUID)) {
                isMissing = true;
            }
            else {
                string resolvedPath = AssetDatabase.GUIDToAssetPath(clipRef.AssetGUID);
                if (string.IsNullOrEmpty(resolvedPath)) {
                    isMissing = true;
                }
                else {
                    var clipAsset = AssetDatabase.LoadAssetAtPath<AudioClip>(resolvedPath);
                    if (clipAsset == null) {
                        isMissing = true;
                    }
                    else {
                        // Ensure the ClipNode exists
                        if (!clipNodes.ContainsKey(resolvedPath)) {
                            clipNodes[resolvedPath] = new ClipNode {
                                assetPath = resolvedPath,
                                fileName = System.IO.Path.GetFileName(resolvedPath),
                                clip = clipAsset
                            };
                        }
                        clipNodes[resolvedPath].referencedBy.Add(node.zound);
                        node.clipPaths.Add(resolvedPath);
                    }
                }
            }

            // Also check by path fallback
            if (!isMissing) return;

            if (!string.IsNullOrEmpty(clipPath)) {
                var assetAtPath = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (assetAtPath != null) {
                    if (!clipNodes.ContainsKey(clipPath)) {
                        clipNodes[clipPath] = new ClipNode {
                            assetPath = clipPath,
                            fileName = System.IO.Path.GetFileName(clipPath),
                            clip = assetAtPath
                        };
                    }
                    clipNodes[clipPath].referencedBy.Add(node.zound);
                    node.clipPaths.Add(clipPath);
                    return; // recovered by path
                }
            }

            // Genuinely broken
            
            // Special case: Rendered/output clips shouldn't count as "broken" if we can re-render or re-promote them.
            if (!isSource && node.zound is Klip k) {
                // Internal source available — can re-render.
                if (k.audioClipRef != null && !string.IsNullOrEmpty(k.audioClipRef.AssetGUID)) {
                    string sourcePath = AssetDatabase.GUIDToAssetPath(k.audioClipRef.AssetGUID);
                    if (!string.IsNullOrEmpty(sourcePath) && AssetDatabase.LoadAssetAtPath<AudioClip>(sourcePath) != null) {
                        k.needsRender = true;
                        return;
                    }
                }
                // External source available — can re-promote.
                if (!string.IsNullOrEmpty(k.externalSourcePath) && System.IO.File.Exists(k.externalSourcePath)) {
                    return;
                }
            }

            node.isBroken = true;
            
            // CRITICAL: Determine the grouping key. 
            // If we have a GUID, that's the most stable key. If not, use the path.
            string key = (clipRef != null && !string.IsNullOrEmpty(clipRef.AssetGUID)) ? clipRef.AssetGUID : clipPath;
            if (string.IsNullOrEmpty(key)) key = isSource ? "[No Source]" : "[No Render]";

            node.brokenReason = $"Missing {label}: {key}";
            var info = new BrokenRefInfo {
                label = label,
                missingKey = key,
                clipRef = clipRef,
                clipPath = clipPath,
                isSource = isSource,
                zoundId = node.zound.id
            };
            node.brokenRefs.Add(info);

            // Determine the display hierarchy
            string rootName = node.zound.name;
            string childName = ""; // Empty if the Zound itself is the root of the broken reference

            if (node.zound.parentId != 0 && zoundNodes.TryGetValue(node.zound.parentId, out var parentNode)) {
                rootName = parentNode.zound.name;
                childName = node.zound.name;
            }

            // Grouping logic for the Broken tab
            if (!brokenGroups.TryGetValue(key, out var group)) {
                group = new BrokenGroup { missingKey = key };
                brokenGroups.Add(key, group);
            }
            group.links.Add(info);
            
            if (!group.affectedHierarchy.TryGetValue(rootName, out var children)) {
                children = new Dictionary<string, List<string>>();
                group.affectedHierarchy[rootName] = children;
            }
            if (!children.TryGetValue(childName, out var labels)) {
                labels = new List<string>();
                children[childName] = labels;
            }
            if (!labels.Contains(label)) {
                labels.Add(label);
            }
        }

        private static string[] GetExistingFolders(params string[] paths) {
            return paths.Where(p => AssetDatabase.IsValidFolder(p)).ToArray();
        }

        // ── Convenience queries ─────────────────────────────────────────

        /// <summary>Returns all Zounds (transitive) that would be affected if the given Zound changes.</summary>
        public List<Zound> GetTransitiveDependents(Zound zound) {
            var visited = new HashSet<int>();
            var result = new List<Zound>();
            CollectDependentsRecursive(zound.id, visited, result);
            return result;
        }

        private void CollectDependentsRecursive(int zoundId, HashSet<int> visited, List<Zound> result) {
            if (!zoundNodes.TryGetValue(zoundId, out var node)) return;
            foreach (var parent in node.dependedOnBy) {
                if (visited.Add(parent.id)) {
                    result.Add(parent);
                    CollectDependentsRecursive(parent.id, visited, result);
                }
            }
        }

        /// <summary>Returns all Zounds (transitive) that the given Zound depends on.</summary>
        public List<Zound> GetTransitiveDependencies(Zound zound) {
            var visited = new HashSet<int>();
            var result = new List<Zound>();
            CollectDependenciesRecursive(zound.id, visited, result);
            return result;
        }

        private void CollectDependenciesRecursive(int zoundId, HashSet<int> visited, List<Zound> result) {
            if (!zoundNodes.TryGetValue(zoundId, out var node)) return;
            foreach (var child in node.dependsOn) {
                if (visited.Add(child.id)) {
                    result.Add(child);
                    CollectDependenciesRecursive(child.id, visited, result);
                }
            }
        }

        /// <summary>Returns the ZoundNode for a given Zound, or null.</summary>
        public ZoundNode GetNode(Zound zound) {
            zoundNodes.TryGetValue(zound.id, out var node);
            return node;
        }
    }
}
#endif
