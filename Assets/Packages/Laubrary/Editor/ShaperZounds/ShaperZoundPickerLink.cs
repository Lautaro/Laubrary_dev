using UnityEditor;
using Laubrary.Shaper.Editor;
using Laubrary.LaunimatorZounds.Editor;

namespace Laubrary.ShaperZounds.Editor
{
    /// Fills in Shaper's Zound-picker hook with the picker Zounds already has, so the cherry panel's Zound
    /// cue becomes a browser button instead of a text box. Reuses ZoundPickerPopup rather than writing a
    /// second picker -- it already wraps Zounds' own search/select popup, and a parallel implementation would
    /// drift from whatever the Zounds browser does next. Same [InitializeOnLoad] registration shape as
    /// ZoundPickerLink (ZoetropeZounds) / ChunkZoundPickerHook's own bridge.
    [InitializeOnLoad]
    static class ShaperZoundPickerLink
    {
        static ShaperZoundPickerLink()
        {
            ShaperZoundPickerHook.Show = (pos, onPicked) => ZoundPickerPopup.Show(pos, onPicked);
            ShaperZoundPickerHook.Preview = name =>
            {
                if (!string.IsNullOrEmpty(name)) Laubrary.Zounds.ZoundEngine.PlayZound(name);
            };
        }
    }
}
