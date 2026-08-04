using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// The "Recent ▾" quick-menu for the Laumination Builder: a popup listing every sheet under
    /// <see cref="SheetLibrary.Folder"/> (downloaded or previously sliced). Clicking a row loads that sheet;
    /// the <b>x</b> deletes it (image + slicing sidecar + display name) after a confirm. Used sheets are
    /// tagged and sorted first.
    /// </summary>
    internal class RecentSheetsPopup : PopupWindowContent
    {
        private readonly Action<Texture2D> _onPick;
        private readonly Texture2D _current;
        private List<SheetLibrary.SheetEntry> _entries;
        private Vector2 _scroll;

        public RecentSheetsPopup(Action<Texture2D> onPick, Texture2D current)
        {
            _onPick = onPick;
            _current = current;
            Reload();
        }

        private void Reload() => _entries = SheetLibrary.EnumerateSheets();

        public override Vector2 GetWindowSize()
        {
            int rows = Mathf.Clamp(_entries.Count, 1, 14);
            return new Vector2(360, 40 + rows * 22);
        }

        public override void OnGUI(Rect rect)
        {
            GUILayout.Label("Sheets in Assets/SpriteSheets", EditorStyles.boldLabel);
            if (_entries.Count == 0)
            {
                GUILayout.Label("None yet. Download one, or drop images into the folder.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            var rowStyle = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft };
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var entry in _entries.ToArray())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var bg = GUI.backgroundColor;
                    if (entry.tex == _current) GUI.backgroundColor = new Color(0.40f, 0.60f, 1f);
                    string label = entry.used ? $"{entry.displayName}   ·used" : entry.displayName;
                    if (GUILayout.Button(new GUIContent(label, AssetDatabase.GetAssetPath(entry.tex)), rowStyle))
                    {
                        _onPick?.Invoke(entry.tex);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                    GUI.backgroundColor = bg;

                    if (GUILayout.Button(new GUIContent("x", "Delete this sheet (image + slicing data)."), GUILayout.Width(22)))
                    {
                        if (EditorUtility.DisplayDialog("Delete sheet",
                            $"Delete '{entry.displayName}' from {SheetLibrary.Folder}?\n\n" +
                            "Removes the image and its slicing data. Lauminaries already baked from it keep working; " +
                            "re-editing one of their animations would need the sheet again.", "Delete", "Cancel"))
                        {
                            if (!SheetLibrary.DeleteSheet(entry.tex, out string err))
                                EditorUtility.DisplayDialog("Delete failed", err, "OK");
                            Reload();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
