using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Larder.Editor
{
    /// Registers Open (jump into LarderWindow) for WareSpec with the shared LauAssetEditors registry — same
    /// shape as MirageEditorLink and friends. No Create registration — WareSpec's own "+ New" already lives
    /// in LarderWindow's own browse toolbar.
    [InitializeOnLoad]
    static class LarderEditorLink
    {
        static LarderEditorLink()
        {
            LauAssetEditors.RegisterOpen<WareSpec>(LarderWindow.OpenFor);
        }
    }
}
