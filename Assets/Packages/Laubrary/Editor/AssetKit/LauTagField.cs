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
                GUILayout.FlexibleSpace();
                bool open = GUILayout.Button("Tags…", GUILayout.Width(56));
                Rect r = GUILayoutUtility.GetLastRect();
                if (open)
                    LauTagPicker.Show(r, ids, newIds => lib.SetTagIds(guid, newIds), asset.name);
            }
        }
    }
}
