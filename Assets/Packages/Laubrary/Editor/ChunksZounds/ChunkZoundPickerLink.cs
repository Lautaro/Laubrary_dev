using UnityEditor;
using Laubrary.Chunks.Editor;
using Laubrary.LaunimatorZounds.Editor;

namespace Laubrary.ChunksZounds.Editor
{
    /// Fills in Chunks' Zound-picker hook with the picker Zounds already has, so a timeline's Zound Event
    /// marker becomes a browser button instead of a text box.
    ///
    /// Reuses ZoundPickerPopup rather than writing a second picker — it already wraps Zounds' own
    /// search/select popup, and a parallel implementation would drift from whatever the Zounds browser does
    /// next. Same [InitializeOnLoad] registration shape as ZoundPickerLink / PyreEditorLink /
    /// ChunkSpecEditorLink.
    [InitializeOnLoad]
    static class ChunkZoundPickerLink
    {
        static ChunkZoundPickerLink()
        {
            ChunkZoundPickerHook.Show = (pos, onPicked) => ZoundPickerPopup.Show(pos, onPicked);
            ChunkZoundPickerHook.Preview = name =>
            {
                if (!string.IsNullOrEmpty(name)) Laubrary.Zounds.ZoundEngine.PlayZound(name);
            };
        }
    }
}
