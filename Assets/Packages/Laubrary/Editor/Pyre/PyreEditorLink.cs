using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Pyre.Editor
{
    /// Registers Open (jump into PyreWindow) + Create (make a fresh Pyre) for Pyre with the shared LauAssetEditors
    /// registry, so a Pyre picked through any LauAsset field/browser gets a working ✎ Edit pen and New ▾ button —
    /// same [InitializeOnLoad] shape as ChunkSpecEditorLink / SpriteFxStackEditorLink. (This is what made the old
    /// PyreChunksFxDrawer's bespoke "Preview in Pyre" button redundant.)
    [InitializeOnLoad]
    static class PyreEditorLink
    {
        static PyreEditorLink()
        {
            LauAssetEditors.RegisterOpen<Pyre>(PyreWindow.OpenFor);
            LauAssetEditors.RegisterCreate<Pyre>((name, folder) => AssetLibrary<Pyre>.Create(name, folder));
        }
    }
}
