using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Shaper.Editor
{
    /// Registers Open for ShaperClip with the shared LauAssetEditors registry, so a clip picked into a
    /// cross-tool slot (a Chunks effect-source field, a Zoe palette entry, a Mirage placement) gets a working
    /// "Edit" on its chip instead of a dead button. Same shape as PyreSpawnSourceEditorLink. No RegisterCreate:
    /// a ShaperClip is bake output, never hand-created (see ShaperClip's own class doc — "a clip is an output
    /// of baking, not something authored from scratch"), so there is no "New" story to wire up here.
    ///
    /// Open jumps to the SOURCE DOCUMENT, not to any editor of the clip itself — a ShaperClip has no
    /// authoring UI of its own, and what a user pressing Edit wants to change is the shape, not the bake.
    /// sourceDocumentName is provenance TEXT (ShaperClip.cs: "provenance for a human reading the asset, never
    /// parsed"), not a GUID, so resolution is a best-effort asset-name search; when it can't resolve (a
    /// renamed, moved, or deleted document) this does nothing rather than opening an empty/wrong window —
    /// same "nothing to open" behaviour PyreSpawnSourceEditorLink uses for an unassigned wrapper.
    [InitializeOnLoad]
    static class ShaperClipEditorLink
    {
        static ShaperClipEditorLink()
        {
            LauAssetEditors.RegisterOpen<ShaperClip>(clip =>
            {
                var doc = FindDocumentByName(clip);
                if (doc != null) ShaperWindow.OpenFor(doc);
            });
        }

        static ShaperDocument FindDocumentByName(ShaperClip clip)
        {
            if (clip == null || string.IsNullOrEmpty(clip.sourceDocumentName)) return null;

            foreach (string guid in AssetDatabase.FindAssets(
                $"t:{nameof(ShaperDocument)} {clip.sourceDocumentName}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var doc = AssetDatabase.LoadAssetAtPath<ShaperDocument>(path);
                if (doc != null && doc.name == clip.sourceDocumentName) return doc;
            }
            return null;
        }
    }
}
