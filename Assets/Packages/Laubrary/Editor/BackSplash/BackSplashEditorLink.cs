using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.BackSplash.Editor
{
    /// Registers Open (jump into BackSplashWindow) for BackSplash with the shared LauAssetEditors registry, so
    /// any LauAsset picker showing a BackSplash gets an Edit action for free. No Create registration here — a
    /// BackSplash's own ★-to-save flow (BackSplashGUI.DrawInline) already covers "make a new one inline" in a
    /// shape the generic Create delegate doesn't fit (it edits fields live before ever saving).
    [InitializeOnLoad]
    static class BackSplashEditorLink
    {
        static BackSplashEditorLink()
        {
            LauAssetEditors.RegisterOpen<BackSplash>(BackSplashWindow.OpenFor);
        }
    }
}
