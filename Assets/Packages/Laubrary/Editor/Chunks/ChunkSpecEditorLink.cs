using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Chunks.Editor
{
    /// Registers Open (jump into ChunkWindow) for ChunkSpec with the shared LauAssetEditors registry — same
    /// shape as MirageEditorLink and friends. No Create registration — ChunkSpec's own "+ New" already lives
    /// in ChunkWindow's own browse toolbar.
    [InitializeOnLoad]
    static class ChunkSpecEditorLink
    {
        static ChunkSpecEditorLink()
        {
            LauAssetEditors.RegisterOpen<ChunkSpec>(ChunkWindow.OpenFor);
        }
    }
}
