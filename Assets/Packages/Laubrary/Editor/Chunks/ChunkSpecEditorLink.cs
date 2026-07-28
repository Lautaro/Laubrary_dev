using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Chunks.Editor
{
    /// Registers Open (jump into ChunkWindow) + Create (make a fresh ChunkSpec) for ChunkSpec with the shared
    /// LauAssetEditors registry — same shape as SpriteFxStackEditorLink. Create lights up the New ▾ button when a
    /// ChunkSpec is picked from another inspector's LauAsset field.
    [InitializeOnLoad]
    static class ChunkSpecEditorLink
    {
        static ChunkSpecEditorLink()
        {
            LauAssetEditors.RegisterOpen<ChunkSpec>(ChunkWindow.OpenFor);
            LauAssetEditors.RegisterCreate<ChunkSpec>((name, folder) => AssetLibrary<ChunkSpec>.Create(name, folder));
        }
    }
}
