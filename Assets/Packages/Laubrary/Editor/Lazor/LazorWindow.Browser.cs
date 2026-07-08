// LazorWindow.Browser.cs — a grid of every LazorShape in the project with live rasterized thumbnails
// (rendered through the same LazorGeometry the game draws). Click to open one; the top-bar CRUD acts on it.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Laubrary.Lazor;

namespace Laubrary.Lazor.Editor
{
    public partial class LazorWindow
    {
        bool browsing;
        string[] browseGuids;
        readonly Dictionary<string, Texture2D> browseThumbs = new Dictionary<string, Texture2D>();
        Vector2 browseScroll;

        static readonly Color ThumbBg = new Color(0.05f, 0.06f, 0.08f, 1f);

        void RefreshBrowse()
        {
            browseGuids = AssetDatabase.FindAssets("t:LazorShape");
            ClearBrowseThumbs();
        }

        void ClearBrowseThumbs()
        {
            foreach (var t in browseThumbs.Values)
                if (t != null) DestroyImmediate(t);
            browseThumbs.Clear();
        }

        Texture2D Thumb(string guid)
        {
            if (browseThumbs.TryGetValue(guid, out var tex) && tex != null) return tex;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var s = AssetDatabase.LoadAssetAtPath<LazorShape>(path);
            tex = s != null ? LazorRasterizer.Render(s, 96, ThumbBg) : null;
            browseThumbs[guid] = tex;
            return tex;
        }

        void DrawBrowser()
        {
            if (browseGuids == null) RefreshBrowse();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"{browseGuids.Length} shape(s)", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Refresh", "Rescan the project for Lazor Shapes."), EditorStyles.toolbarButton, GUILayout.Width(64)))
                    RefreshBrowse();
                if (GUILayout.Button(new GUIContent("Close", "Return to the editor."), EditorStyles.toolbarButton, GUILayout.Width(50)))
                    browsing = false;
            }

            const float cell = 112f, thumb = 96f;
            int cols = Mathf.Max(1, Mathf.FloorToInt(position.width / cell));

            browseScroll = EditorGUILayout.BeginScrollView(browseScroll);
            int i = 0;
            while (i < browseGuids.Length)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < cols && i < browseGuids.Length; c++, i++)
                    {
                        string guid = browseGuids[i];
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        string niceName = System.IO.Path.GetFileNameWithoutExtension(path);

                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(cell)))
                        {
                            Rect tr = GUILayoutUtility.GetRect(thumb, thumb, GUILayout.Width(thumb), GUILayout.Height(thumb));
                            EditorGUI.DrawRect(tr, ThumbBg);
                            var t = Thumb(guid);
                            if (t != null) GUI.DrawTexture(tr, t, ScaleMode.ScaleToFit);

                            bool isCurrent = shape != null && AssetDatabase.GetAssetPath(shape) == path;
                            if (isCurrent) DrawSelectionBorder(tr);

                            HandleThumbClick(tr, path);
                            GUILayout.Label(niceName, EditorStyles.miniLabel, GUILayout.Width(thumb));
                        }
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void HandleThumbClick(Rect tr, string path)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && tr.Contains(e.mousePosition))
            {
                shape = AssetDatabase.LoadAssetAtPath<LazorShape>(path);
                layerSel = 0; activePath = -1;
                if (e.clickCount == 2) browsing = false;
                e.Use();
            }
        }

        static void DrawSelectionBorder(Rect r)
        {
            Color c = new Color(0.3f, 0.7f, 1f, 1f);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 2, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 2, r.y, 2, r.height), c);
        }
    }
}
