using UnityEditor;
using Laubrary.Chunks.Editor;

namespace Laubrary.Pyre.Editor
{
    /// Registers "Edit" for a PyreChunkAnimation asset picked into a Chunks ChunkSpec — jumps straight into
    /// Pyre on the wrapped Pyre. Pyre.Editor references Chunks.Editor for this; Chunks itself never
    /// references Pyre.
    [InitializeOnLoad]
    static class PyreChunkAnimationEditorLink
    {
        static PyreChunkAnimationEditorLink()
        {
            ChunkAnimationEditors.Register<PyreChunkAnimation>(a => PyreWindow.OpenFor(a.spec));
        }
    }
}
