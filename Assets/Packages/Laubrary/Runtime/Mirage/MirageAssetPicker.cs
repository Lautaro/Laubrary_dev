#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Pyre;

namespace Laubrary.Mirage
{
    /// <summary>
    /// The cross-type "what can I add as a previewable" browser — built once, shared by <c>MirageWindow</c>
    /// (Editor Window) and <c>MirageHud</c> (Play-mode Game View HUD) so the supported-kinds list never
    /// drifts between the two. No multi-type asset picker exists elsewhere in Laubrary (AssetKit's browser
    /// is hard single-type-generic) — this is new, small, and deliberately not folded into AssetKit itself.
    /// Editor-only: Mirage never ships, so this is free to use AssetDatabase directly.
    ///
    /// Deliberately Zoe + BlastSpec ONLY — both are real LauAsset-registered types (a bounded, intentional
    /// set of assets someone actually authored), so browsing them via <c>AssetDatabase.FindAssets("t:...")</c>
    /// is cheap and the result list stays small. A background Sprite is NOT a LauAsset (no LauAssetEditors
    /// registration) and used to be included here via <c>AssetDatabase.FindAssets("t:Sprite")</c> — that
    /// matches every Sprite sub-asset of every imported texture in the ENTIRE project (every icon, every UI
    /// image, every character sheet), which is exactly the "1000+ assets" flood reported 2026-07-21. Sprite
    /// stays reachable as a previewable (PreviewableEntry.content's own doc comment: "a Zoe, a BlastSpec, or
    /// a Sprite") via a SEPARATE native <c>EditorGUIUtility.ShowObjectPicker&lt;Sprite&gt;</c> button in
    /// MirageWindow — Unity's own object picker already handles a project-wide type search at scale (search
    /// field, virtualization), so it's the right tool for "browse literally every Sprite," not this one.
    /// </summary>
    public static class MirageAssetPicker
    {
        public struct Item
        {
            public Object asset;
            public string typeLabel;
        }

        /// The concrete types this picker's default (LauAsset) browse spans — shared with MirageWindow's own
        /// "New ▾" scoping so that button offers exactly these two kinds, not every LauAssetEditors-registered
        /// type in the project (a `typeof(Object)` constraint would otherwise match ChunkSpec/WareSpec/etc. too).
        public static readonly System.Type[] SupportedTypes = { typeof(Zoe), typeof(BlastSpec) };

        public static List<Item> FindAll()
        {
            var list = new List<Item>();
            AddType<Zoe>(list, "Zoe");
            AddType<BlastSpec>(list, "Blast");
            return list;
        }

        static void AddType<T>(List<Item> list, string label) where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (obj is T t) list.Add(new Item { asset = t, typeLabel = label });
            }
        }

        /// Builds a ready-to-show GenericMenu grouped by type label; `onPick` receives the chosen asset.
        public static GenericMenu BuildMenu(System.Action<Object> onPick)
        {
            var menu = new GenericMenu();
            var items = FindAll();
            if (items.Count == 0) { menu.AddDisabledItem(new GUIContent("No supported assets found")); return menu; }
            foreach (var item in items)
            {
                var captured = item.asset;
                menu.AddItem(new GUIContent($"{item.typeLabel}/{captured.name}"), false, () => onPick(captured));
            }
            return menu;
        }
    }
}
#endif
