using UnityEditor;
using Laubrary.AssetKit.Editor;
using Laubrary.Pyre;

namespace Laubrary.Pyre.Editor
{
    /// Registers Open + Create for **Pyre itself** with the shared LauAssetEditors registry — same
    /// [InitializeOnLoad] shape as every other tool's *EditorLink.cs.
    ///
    /// T-0381: only PyreSpawnSource (the three-field wrapper AROUND a Pyre) was registered, so a field that
    /// takes a Pyre DIRECTLY had a dead "Edit" — and a Chunks Blast slot does exactly that, because Pyre
    /// implements IChunkEffectSpawner itself (Runtime/Pyre/PyreChunksDirect.cs), so a real recipe picks the
    /// Pyre straight in and never goes through the wrapper. The registry is keyed by CONCRETE type, so the
    /// wrapper's registration could never cover it; the single most common value in that field was the one
    /// value whose Edit was greyed out.
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
