using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.TextSplash.Editor
{
    /// Registers Open (jump into TextSplashWindow) + Create for TextSplash with the shared LauAssetEditors registry,
    /// so a TextSplash picked from any LauAsset field gets a working ✎ Edit pen and New ▾ button — same shape as
    /// ChunkSpecEditorLink / PyreEditorLink.
    [InitializeOnLoad]
    static class TextSplashEditorLink
    {
        static TextSplashEditorLink()
        {
            LauAssetEditors.RegisterOpen<TextSplash>(TextSplashWindow.OpenFor);
            LauAssetEditors.RegisterCreate<TextSplash>((name, folder) => AssetLibrary<TextSplash>.Create(name, folder));
        }
    }
}
