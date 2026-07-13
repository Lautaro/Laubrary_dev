using UnityEditor;
using Laubrary.Chunks.Editor;

namespace Laubrary.Pyre.Editor
{
    /// Registers "Edit" for a BlastSpecChunkAnimation asset picked into a Chunks ChunkSpec — jumps straight into
    /// Pyre on the wrapped BlastSpec. Pyre.Editor references Chunks.Editor for this; Chunks itself never
    /// references Pyre.
    [InitializeOnLoad]
    static class BlastSpecChunkAnimationEditorLink
    {
        static BlastSpecChunkAnimationEditorLink()
        {
            ChunkAnimationEditors.Register<BlastSpecChunkAnimation>(a => PyreWindow.OpenFor(a.spec));
        }
    }
}
