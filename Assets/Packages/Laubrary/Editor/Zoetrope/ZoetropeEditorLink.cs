using UnityEditor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Zoetrope.Editor
{
    /// Registers Open (jump into their own editor window) for Zoe, WeaponDef and AmmoDef with the shared
    /// LauAssetEditors registry — same shape as MirageEditorLink/BackSplashEditorLink. This is what makes
    /// LauAssetField's Edit (✎) button actually do something for these types wherever a field uses one (e.g.
    /// Mirage's own entry Content/Weapon fields). No Create registration here — all three already have their
    /// own dedicated "+ New" flow in their own browse window (AmmoDef's own `visual` sub-field is the one spot
    /// that already got a lightweight inline-create story, registered separately per visual kind).
    [InitializeOnLoad]
    static class ZoetropeEditorLink
    {
        static ZoetropeEditorLink()
        {
            LauAssetEditors.RegisterOpen<Zoe>(ZoeWindow.OpenFor);
            LauAssetEditors.RegisterOpen<WeaponDef>(WeaponDefWindow.OpenFor);
            LauAssetEditors.RegisterOpen<AmmoDef>(AmmoDefWindow.OpenFor);
        }
    }
}
