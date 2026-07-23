using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Choreographer.Editor
{
    /// Registers Open (jump into ChoreographerWindow) for Choreography with the shared LauAssetEditors registry
    /// — same shape as MirageEditorLink/ZoetropeEditorLink. No Create registration — Choreography's own "+ New"
    /// already lives in its own browse window.
    [InitializeOnLoad]
    static class ChoreographerEditorLink
    {
        static ChoreographerEditorLink()
        {
            LauAssetEditors.RegisterOpen<Choreography>(ChoreographerWindow.OpenFor);
        }
    }
}
