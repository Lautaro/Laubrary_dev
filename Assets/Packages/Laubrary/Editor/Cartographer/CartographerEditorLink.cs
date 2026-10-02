using Laubrary.AssetKit.Editor;
using UnityEditor;
using UnityEditor.Callbacks;

namespace Laubrary.Cartographer.Editor
{
    /// Wires Cartographer's asset types into the rest of the editor — the same `*EditorLink` shape every
    /// other tool has, which Cartographer simply never got.
    ///
    /// Why it matters, in the form the user met it: a LauAsset field draws a ✎ pen and a New button for any
    /// type that has registered an opener and a creator, and GREYS them out for any type that has not. The
    /// openers registry held fifteen types and not one of them was Cartographer's, so every Tileset, Level
    /// and Prop picker in the package showed two dead controls with no explanation — "what about the
    /// disabled pen?" (2026-08-03). The controls were never broken; nothing had ever told them what to do.
    ///
    /// Registering here rather than inside each window keeps the knowledge in one place, and keeps the
    /// windows unaware of AssetKit.
    [InitializeOnLoad]
    static class CartographerEditorLink
    {
        static CartographerEditorLink()
        {
            LauAssetEditors.RegisterOpen<Tileset>(t => TilesetBuilderWindow.OpenFor(t));
            LauAssetEditors.RegisterOpen<LevelAsset>(l => CartographerWindow.OpenFor(l));
            LauAssetEditors.RegisterOpen<Prop>(p => PropWindow.OpenFor(p));

            LauAssetEditors.RegisterCreate<Tileset>((name, folder) => AssetLibrary<Tileset>.Create(name, folder));
            LauAssetEditors.RegisterCreate<LevelAsset>((name, folder) => AssetLibrary<LevelAsset>.Create(name, folder));
            LauAssetEditors.RegisterCreate<Prop>((name, folder) => AssetLibrary<Prop>.Create(name, folder));
        }

        /// Double-click a Cartographer asset in the Project window and its own tool opens, rather than the
        /// inspector being the only way in.
        [OnOpenAsset]
        static bool OnOpen(int instanceId, int line)
        {
            // EntityIdToObject, not the obsolete InstanceIDToObject — EntityId converts implicitly from the
            // int this callback is still handed.
            var obj = EditorUtility.EntityIdToObject(instanceId);
            switch (obj)
            {
                case Tileset t: TilesetBuilderWindow.OpenFor(t); return true;
                case LevelAsset l: CartographerWindow.OpenFor(l); return true;
                case Prop p: PropWindow.OpenFor(p); return true;
                default: return false;
            }
        }
    }
}
