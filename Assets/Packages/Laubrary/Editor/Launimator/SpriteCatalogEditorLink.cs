using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Launimator.Editor
{
    /// Registers Open (jump into SpriteCatalogWindow) for SpriteCatalog with the shared LauAssetEditors
    /// registry — same shape as MirageEditorLink and friends. No Create registration — SpriteCatalog's own
    /// "+ New" already lives in SpriteCatalogWindow's own browse toolbar.
    [InitializeOnLoad]
    static class SpriteCatalogEditorLink
    {
        static SpriteCatalogEditorLink()
        {
            LauAssetEditors.RegisterOpen<SpriteCatalog>(SpriteCatalogWindow.OpenFor);
        }
    }
}
