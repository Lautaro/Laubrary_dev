#if ADDRESSABLES_INSTALLED
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Compares the <see cref="ZoundDependencyAnalyzer"/> snapshot (which uses
    /// <see cref="ZoundsClipClassifier"/> as its single source of truth) against
    /// the actual Addressables group. Surfaces discrepancies between what SHOULD
    /// ship and what Unity WILL package.
    ///
    /// This class does not make any inclusion decisions itself — it only reads
    /// from the analyzer and from the Addressables group.
    /// </summary>
    public sealed class ZoundsBuildReport {

        // ── Result data ────────────────────────────────────────────────

        /// <summary>The analyzer snapshot this report is based on.</summary>
        public ZoundDependencyAnalyzer analyzer { get; private set; }

        /// <summary>Clips in the Addressables group that the analyzer says should NOT ship.</summary>
        public List<ClipEntry> staleEntries { get; private set; } = new List<ClipEntry>();

        /// <summary>Clips the analyzer says should ship but are NOT in the Addressables group.</summary>
        public List<ClipEntry> missingEntries { get; private set; } = new List<ClipEntry>();

        /// <summary>Clips correctly in the Addressables group and should be.</summary>
        public List<ClipEntry> validEntries { get; private set; } = new List<ClipEntry>();

        /// <summary>Entries in the Zounds group that aren't AudioClips or have broken paths.</summary>
        public List<ClipEntry> invalidEntries { get; private set; } = new List<ClipEntry>();

        public int brokenRefCount;
        public int orphanCount;

        // Aggregates over validEntries
        public long estimatedBuildSizeBytes;
        public int libraryClipCount;
        public int renderedClipCount;
        public long librarySizeBytes;
        public long renderedSizeBytes;

        public bool hasDiscrepancies => staleEntries.Count > 0 || missingEntries.Count > 0 || invalidEntries.Count > 0;
        public bool hasBrokenRefs => brokenRefCount > 0;
        public bool hasIssues => hasDiscrepancies || hasBrokenRefs || orphanCount > 0;

        public struct ClipEntry {
            public string guid;
            public string assetPath;
            public string fileName;
        }

        // ── Construction ────────────────────────────────────────────────

        /// <summary>
        /// Generates a report from a fresh analyzer snapshot.
        /// </summary>
        public static ZoundsBuildReport Generate() {
            return Generate(ZoundDependencyAnalyzer.Analyze());
        }

        /// <summary>
        /// Generates a report from an existing analyzer snapshot.
        /// </summary>
        public static ZoundsBuildReport Generate(ZoundDependencyAnalyzer existingAnalyzer) {
            var report = new ZoundsBuildReport();
            report.analyzer = existingAnalyzer;
            report.Run();
            return report;
        }

        private void Run() {
            brokenRefCount = analyzer.brokenZounds.Count;
            orphanCount = analyzer.orphanClips.Count;

            // Build the set of paths the analyzer says should ship
            // (includedInBuild was set by ZoundsClipClassifier via the analyzer)
            var shouldShipPaths = new HashSet<string>();
            foreach (var cn in analyzer.buildClips) {
                shouldShipPaths.Add(cn.assetPath);
            }

            // Scan the actual Addressables group
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var addressedPaths = new HashSet<string>();

            if (settings != null) {
                var zoundsGroup = settings.FindGroup("Zounds Default Local Group");
                if (zoundsGroup != null) {
                    foreach (var entry in zoundsGroup.entries.ToList()) {
                        string path = AssetDatabase.GUIDToAssetPath(entry.guid);

                        if (string.IsNullOrEmpty(path) || AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(AudioClip)) {
                            invalidEntries.Add(new ClipEntry {
                                guid = entry.guid,
                                assetPath = string.IsNullOrEmpty(path) ? entry.address : path,
                                fileName = string.IsNullOrEmpty(path) ? entry.guid : System.IO.Path.GetFileName(path)
                            });
                            continue;
                        }

                        addressedPaths.Add(path);

                        var clipEntry = new ClipEntry {
                            guid = entry.guid,
                            assetPath = path,
                            fileName = System.IO.Path.GetFileName(path)
                        };

                        if (shouldShipPaths.Contains(path)) {
                            validEntries.Add(clipEntry);
                            AccumulateSize(clipEntry);
                        } else {
                            staleEntries.Add(clipEntry);
                        }
                    }
                }
            }

            // Find clips that should ship but aren't in the Addressables group
            foreach (string path in shouldShipPaths) {
                if (!addressedPaths.Contains(path)) {
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    missingEntries.Add(new ClipEntry {
                        guid = guid,
                        assetPath = path,
                        fileName = System.IO.Path.GetFileName(path)
                    });
                }
            }

            // Sort all lists alphabetically by filename
            validEntries.Sort((a, b) => string.Compare(a.fileName, b.fileName, System.StringComparison.OrdinalIgnoreCase));
            staleEntries.Sort((a, b) => string.Compare(a.fileName, b.fileName, System.StringComparison.OrdinalIgnoreCase));
            missingEntries.Sort((a, b) => string.Compare(a.fileName, b.fileName, System.StringComparison.OrdinalIgnoreCase));
        }

        private void AccumulateSize(ClipEntry entry) {
            var projectSettings = ZoundsProject.Instance.projectSettings;
            long size = EstimateClipSize(entry.assetPath);
            estimatedBuildSizeBytes += size;

            if (entry.assetPath.StartsWith(projectSettings.libraryFolderPath)) {
                libraryClipCount++;
                librarySizeBytes += size;
            } else {
                renderedClipCount++;
                renderedSizeBytes += size;
            }
        }

        // ── Remediation ─────────────────────────────────────────────────

        /// <summary>
        /// Removes stale/invalid entries and adds missing ones.
        /// Returns the number of changes made.
        /// </summary>
        public int Reconcile() {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return 0;

            int changes = 0;

            foreach (var entry in staleEntries) {
                if (settings.FindAssetEntry(entry.guid) != null) {
                    settings.RemoveAssetEntry(entry.guid);
                    changes++;
                }
            }

            foreach (var entry in invalidEntries) {
                if (!string.IsNullOrEmpty(entry.guid) && settings.FindAssetEntry(entry.guid) != null) {
                    settings.RemoveAssetEntry(entry.guid);
                    changes++;
                }
            }

            if (missingEntries.Count > 0) {
                var group = settings.FindGroup("Zounds Default Local Group");
                if (group == null) {
                    group = settings.CreateGroup("Zounds Default Local Group", false, false, false, null,
                        typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema),
                        typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));
                }

                foreach (var missing in missingEntries) {
                    if (!string.IsNullOrEmpty(missing.guid)) {
                        var addrEntry = settings.CreateOrMoveEntry(missing.guid, group);
                        if (addrEntry != null) {
                            addrEntry.address = missing.assetPath;
                            changes++;
                        }
                    }
                }
            }

            if (changes > 0) {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true);
                AssetDatabase.SaveAssets();
            }

            return changes;
        }

        // ── Console logging ─────────────────────────────────────────────

        /// <summary>
        /// Logs a single summary line. Only logs if there are issues.
        /// </summary>
        public void LogToConsole() {
            if (!hasIssues) return;

            var parts = new List<string>();
            if (staleEntries.Count > 0) parts.Add($"{staleEntries.Count} stale");
            if (missingEntries.Count > 0) parts.Add($"{missingEntries.Count} missing");
            if (invalidEntries.Count > 0) parts.Add($"{invalidEntries.Count} invalid");
            if (brokenRefCount > 0) parts.Add($"{brokenRefCount} broken refs");
            if (orphanCount > 0) parts.Add($"{orphanCount} orphans");

            string summary = $"[Zounds] Build audit: {string.Join(", ", parts)}. " +
                             $"({validEntries.Count} clips OK, ~{FormatBytes(estimatedBuildSizeBytes)})";

            Debug.LogWarning(summary);
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static long EstimateClipSize(string assetPath) {
            try {
                string fullPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.dataPath),
                    assetPath);
                if (System.IO.File.Exists(fullPath)) {
                    return new System.IO.FileInfo(fullPath).Length;
                }
            } catch { }
            return 0;
        }

        public static string FormatBytes(long bytes) {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
#endif
