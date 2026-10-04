using Laubrary.AssetKit.Editor;
using UnityEditor;

namespace Laubrary.GoreLab.Editor
{
    /// Registers Open (jump into the GoreLab window) and Create (a fresh rig) for GoreRig with the shared
    /// LauAssetEditors registry, so any LauAsset field showing a GoreRig gets Edit and New for free.
    [InitializeOnLoad]
    static class GoreLabEditorLink
    {
        static GoreLabEditorLink()
        {
            LauAssetEditors.RegisterOpen<GoreRig>(GoreLabWindow.OpenFor);
            LauAssetEditors.RegisterCreate<GoreRig>(GoreLabWindow.CreateRig);
        }
    }
}
