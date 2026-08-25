using UnityEditor;
using Laubrary.AssetKit.Editor;
using Laubrary.Pyre;

namespace Laubrary.Pyre.Editor
{
    /// Registers Create + Open for PyreSpawnSource with the shared LauAssetEditors registry, so a Pyre Spawn
    /// Source picked in a Chunks Pyre Spawn slot gets a working "New" and "Edit" on its chip instead of two
    /// dead buttons. Same shape as ChunkSpecEditorLink.
    ///
    /// Open deliberately jumps to the WRAPPED Pyre rather than to any editor of its own: a PyreSpawnSource is
    /// three fields around a Pyre, and what a user pressing Edit wants to change is the explosion, not the
    /// wrapper. Making them hunt for the real asset afterwards would be the "it needs instructions, so the
    /// affordance is missing" failure. When it wraps nothing yet there is nothing to open, so it does nothing
    /// rather than opening an empty window.
    [InitializeOnLoad]
    static class PyreSpawnSourceEditorLink
    {
        static PyreSpawnSourceEditorLink()
        {
            LauAssetEditors.RegisterCreate<PyreSpawnSource>(
                (name, folder) => AssetLibrary<PyreSpawnSource>.Create(name, folder));
            LauAssetEditors.RegisterOpen<PyreSpawnSource>(src =>
            {
                if (src != null && src.spec != null) PyreWindow.OpenFor(src.spec);
            });
        }
    }
}
