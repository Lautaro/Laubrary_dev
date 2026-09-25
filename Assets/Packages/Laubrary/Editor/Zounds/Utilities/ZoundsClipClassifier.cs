#if ADDRESSABLES_INSTALLED
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Pure classification of an AudioClip's role in the project. No side effects.
    /// This is the single source of truth for whether a clip ships in builds,
    /// is orphaned, or is unused. Used by the dependency analyzer, import pipeline,
    /// build report, and UI — no other code should independently decide clip status.
    ///
    /// Terminology:
    ///   "Edits" = changes requiring offline rendering (trim, envelopes, boost gain, EQ).
    ///   "Settings" = real-time playback parameters (volume, pitch, chance) — no rendering needed.
    ///   "Source clip" = original audio file (audioClipRef).
    ///   "Output clip" = what gets loaded at runtime. Equals source if no edits, or rendered clip if edits.
    /// </summary>
    public static class ZoundsClipClassifier {

        public enum ClipFolder {
            Library,
            Sources,
            Work,
            ZoundFiles,
            Unknown
        }

        public struct ClipStatus {
            /// <summary>This clip should be Addressable and included in builds.</summary>
            public bool shouldShip;
            /// <summary>This clip is in the Library folder.</summary>
            public bool isLibraryClip;
            /// <summary>This clip is in Work/ZoundFiles but not referenced — safe to delete.</summary>
            public bool isOrphan;
            /// <summary>This clip is in Sources but not used as an output clip by any Zound.</summary>
            public bool isUnusedSource;
            /// <summary>This clip is in Library but not referenced by any Zound (still ships — available by name).</summary>
            public bool isUnusedLibrary;
        }

        /// <summary>
        /// Classifies a clip given pre-computed facts. This is the single decision point.
        /// </summary>
        /// <param name="folder">Which managed folder the clip lives in.</param>
        /// <param name="isReferenced">True if any Zound references this clip (source or rendered).</param>
        /// <param name="isOutputClip">True if at least one Klip will load this clip at runtime.
        /// Compute via <see cref="IsOutputClip"/>.</param>
        public static ClipStatus Classify(ClipFolder folder, bool isReferenced, bool isOutputClip) {
            var status = new ClipStatus();

            switch (folder) {
                case ClipFolder.Library:
                    // Library clips always ship — available by name at runtime
                    // via TryPlayLibraryClipFallback (Addressables address-based lookup).
                    status.isLibraryClip = true;
                    status.shouldShip = true;
                    status.isUnusedLibrary = !isReferenced;
                    break;

                case ClipFolder.Sources:
                    // Sources ship ONLY when they are the output clip for a Klip
                    // (no edits → source = output, must be loadable at runtime).
                    status.shouldShip = isOutputClip;
                    status.isUnusedSource = !isReferenced;
                    break;

                case ClipFolder.Work:
                case ClipFolder.ZoundFiles:
                    // Rendered/work clips only ship if referenced.
                    status.shouldShip = isReferenced;
                    status.isOrphan = !isReferenced;
                    break;

                default:
                    status.shouldShip = false;
                    break;
            }

            return status;
        }

        /// <summary>
        /// Determines which managed folder a clip path belongs to.
        /// </summary>
        public static ClipFolder GetFolder(string assetPath, ZoundsProject.ProjectSettings settings) {
            if (assetPath.StartsWith(settings.libraryFolderPath)) return ClipFolder.Library;
            if (assetPath.StartsWith(settings.sourcesFolderPath)) return ClipFolder.Sources;
            if (assetPath.StartsWith(settings.workFolderPath)) return ClipFolder.Work;
            if (assetPath.StartsWith(settings.zoundFilesFolderPath)) return ClipFolder.ZoundFiles;
            return ClipFolder.Unknown;
        }

        /// <summary>
        /// Determines whether a clip (identified by GUID) is the output clip for any Klip or Zequence.
        /// This is the single function that answers "will any Zound load this clip at runtime?"
        /// Used by both the analyzer (batch) and the import pipeline (per-asset).
        /// </summary>
        public static bool IsOutputClip(string assetGuid, ZoundLibrary zoundLibrary) {
            bool isOutput = false;
            zoundLibrary.ForEachZound(z => {
                if (z is Klip klip) {
                    // Promoted output clip — the primary check after output promotion.
                    if (klip.outputClipRef != null && klip.outputClipRef.AssetGUID == assetGuid) {
                        isOutput = true;
                        return true;
                    }
                    // Legacy: rendered clip (for Klips not yet promoted)
                    if (klip.renderedClipRef != null && klip.renderedClipRef.AssetGUID == assetGuid) {
                        isOutput = true;
                        return true;
                    }
                    // Legacy: source clip used as output (un-promoted no-edit Klips)
                    if (klip.audioClipRef != null && klip.audioClipRef.AssetGUID == assetGuid) {
                        if (klip.outputClipRef == null || !klip.outputClipRef.RuntimeKeyIsValid()) {
                            if (!klip.HasActiveEdits()) {
                                isOutput = true;
                                return true;
                            }
                            if (klip.renderedClipRef == null || !klip.renderedClipRef.RuntimeKeyIsValid()) {
                                isOutput = true;
                                return true;
                            }
                        }
                    }
                }
                else if (z is Zequence zeq) {
                    if (zeq.renderedClipRef != null && zeq.renderedClipRef.AssetGUID == assetGuid) {
                        isOutput = true;
                        return true;
                    }
                }
                return false;
            });
            return isOutput;
        }

        /// <summary>
        /// Convenience for the import pipeline: determines if a single clip should be Addressable.
        /// Uses <see cref="Classify"/> and <see cref="IsOutputClip"/> — the same functions
        /// the analyzer uses in batch.
        /// </summary>
        public static bool ShouldBeAddressable(string assetGuid, bool inLibrary, bool inSources, bool inWork, bool inZoundFiles) {
            ClipFolder folder;
            if (inLibrary) folder = ClipFolder.Library;
            else if (inSources) folder = ClipFolder.Sources;
            else if (inWork) folder = ClipFolder.Work;
            else if (inZoundFiles) folder = ClipFolder.ZoundFiles;
            else return false;

            if (folder == ClipFolder.Library) {
                return true; // Classify always returns shouldShip=true for Library
            }

            var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
            bool isOutput = IsOutputClip(assetGuid, zoundLibrary);

            // For Work/ZoundFiles, isReferenced == isOutput (they're only referenced as rendered clips).
            // For Sources, isReferenced may be true (as source input) even if isOutput is false.
            // We pass isOutput for both since Classify handles the distinction per folder.
            bool isReferenced = isOutput;
            if (folder == ClipFolder.Sources && !isReferenced) {
                // Check if referenced at all (even if not as output) — for unused detection
                zoundLibrary.ForEachZound(z => {
                    if (z is Klip klip && klip.audioClipRef != null && klip.audioClipRef.AssetGUID == assetGuid) {
                        isReferenced = true;
                        return true;
                    }
                    return false;
                });
            }

            return Classify(folder, isReferenced, isOutput).shouldShip;
        }

        /// <summary>
        /// Whether a Klip needs an output clip of its own in ZoundFiles/ before it can ship.
        /// A Klip whose source already ships as-is (a Library clip) plays that source directly;
        /// its edits, if any, are handled by rendering, not by promotion. Only a source that cannot
        /// ship (an external file, or a clip in the non-shipping Sources/Work folders) needs a copy.
        /// The automatic save-time and pre-build sweeps must use this; a blanket "no output yet"
        /// check duplicated every untouched Library clip into ZoundFiles/.
        /// </summary>
        public static bool NeedsOutputClip(Klip klip, ZoundsProject.ProjectSettings settings) {
            if (klip == null) return false;
            if (klip.outputClipRef != null && klip.outputClipRef.RuntimeKeyIsValid()) return false;
            if (!string.IsNullOrEmpty(klip.externalSourcePath)) return true;
            if (klip.audioClipRef == null || !klip.audioClipRef.RuntimeKeyIsValid()) return false;
            string sourcePath = AssetDatabase.GUIDToAssetPath(klip.audioClipRef.AssetGUID);
            if (string.IsNullOrEmpty(sourcePath)) return false;
            return GetFolder(sourcePath, settings) != ClipFolder.Library;
        }
    }
}
#endif
