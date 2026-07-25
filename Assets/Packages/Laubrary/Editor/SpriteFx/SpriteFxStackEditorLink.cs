using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.SpriteFx.Editor
{
    /// Registers Open (jump into SpriteFxStackWindow) and Create (make a fresh SpriteFxSpec) for SpriteFxSpec
    /// with the shared LauAssetEditors registry — same [InitializeOnLoad] shape as ChunkSpecEditorLink and
    /// friends. This is what makes a SpriteFxSpec pickable AND creatable from another inspector's LauAssetField
    /// (needed by slice 5), without that field's module hard-referencing this editor. Create returns the saved
    /// asset (AssetLibrary handles folder creation + SaveAssets); SpriteFxSpec's own field initializers give it
    /// sane defaults, so no extra seeding is needed here.
    [InitializeOnLoad]
    static class SpriteFxStackEditorLink
    {
        static SpriteFxStackEditorLink()
        {
            LauAssetEditors.RegisterOpen<SpriteFxSpec>(SpriteFxStackWindow.OpenFor);
            LauAssetEditors.RegisterCreate<SpriteFxSpec>((name, folder) => AssetLibrary<SpriteFxSpec>.Create(name, folder));
        }
    }
}
