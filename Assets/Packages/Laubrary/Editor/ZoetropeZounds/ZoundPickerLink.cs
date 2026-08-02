using UnityEditor;
using Laubrary.Zoetrope.Editor;
using Laubrary.LaunimatorZounds.Editor;

namespace Laubrary.ZoetropeZounds.Editor
{
    /// Fills in Zoetrope's Zound-picker hook with the picker Zounds already has, so a PlayZoundEffect's
    /// zoundName field becomes a browser button instead of a text box.
    ///
    /// Reuses ZoundPickerPopup rather than writing a second picker — it already wraps Zounds' own
    /// search/select popup, and a parallel implementation would drift from whatever the Zounds browser does
    /// next. Same [InitializeOnLoad] registration shape as PyreEditorLink / ChunkSpecEditorLink.
    [InitializeOnLoad]
    static class ZoundPickerLink
    {
        static ZoundPickerLink()
        {
            ZoundPickerHook.Show = (pos, onPicked) => ZoundPickerPopup.Show(pos, onPicked);
            ZoundPickerHook.Preview = name =>
            {
                if (!string.IsNullOrEmpty(name)) Laubrary.Zounds.ZoundEngine.PlayZound(name);
            };
        }
    }
}
