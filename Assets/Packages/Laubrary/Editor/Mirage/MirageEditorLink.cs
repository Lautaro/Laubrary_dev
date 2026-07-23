using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Mirage.Editor
{
    /// Registers Open (jump into MirageWindow) for MirageView with the shared LauAssetEditors registry. No
    /// Create registration — a MirageView's own New button (via LaubraryAssetWindow's toolbar) already covers
    /// that; there's no other tool's field that would want to spin up a brand new view inline.
    [InitializeOnLoad]
    static class MirageEditorLink
    {
        static MirageEditorLink()
        {
            LauAssetEditors.RegisterOpen<MirageView>(MirageWindow.OpenFor);
        }
    }
}
