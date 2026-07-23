using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Lazor.Editor
{
    /// Registers Open (jump into LazorWindow) for LazorShape with the shared LauAssetEditors registry — same
    /// shape as MirageEditorLink and friends. No Create registration — LazorShape's own "+ New" already lives
    /// in LazorWindow's own browse toolbar.
    [InitializeOnLoad]
    static class LazorEditorLink
    {
        static LazorEditorLink()
        {
            LauAssetEditors.RegisterOpen<LazorShape>(LazorWindow.OpenFor);
        }
    }
}
