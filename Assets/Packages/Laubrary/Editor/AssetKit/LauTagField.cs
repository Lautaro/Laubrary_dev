using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// Inline "this asset's tags" row — chips for each assigned tag + a button opening LauTagPicker. Used by
    /// LaubraryAssetWindow&lt;T&gt;'s toolbar so every existing LauAsset CRUD window gets tag-editing for free,
    /// the same "solve once" shape as LauAssetField/LauAssetGridGUI.
    public static class LauTagField
    {
        public static void Draw(Object asset)
        {
            if (asset == null) return;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return;   // unsaved asset — nothing to key tags by yet
            string guid = AssetDatabase.AssetPathToGUID(path);
            var lib = LauTagLibraryProvider.Get();
            var ids = lib.GetTagIds(guid);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Tags", GUILayout.Width(34));
                if (ids.Count == 0)
                    GUILayout.Label("· none ·", EditorStyles.miniLabel);
                else
                {
                    var names = ids.Select(id => lib.Tags.FirstOrDefault(t => t.id == id)?.name).Where(n => n != null);
                    GUILayout.Label(string.Join(", ", names), EditorStyles.miniLabel, GUILayout.MaxWidth(240));
                }
                // T-0321 — the button sits BESIDE the names, and the leftover room trails after it. It used to
                // be pushed to the far right by a FlexibleSpace, which is only sane when the island is about as
                // wide as the row's content: Pyre parents this section above its split, so the island is the
                // whole 812pt window and the button landed ~780pt from the "Tags" label it belongs to, out over
                // the preview. "Variable-width content goes LAST in its row" — so the slack goes last, not the
                // control.
                GUILayout.Space(6f);
                bool open = GUILayout.Button("Tags…", GUILayout.Width(56));
                Rect r = GUILayoutUtility.GetLastRect();
                GUILayout.FlexibleSpace();
                if (open)
                    LauTagPicker.Show(r, ids, newIds => lib.SetTagIds(guid, newIds), asset.name);
            }
        }
    }
}
