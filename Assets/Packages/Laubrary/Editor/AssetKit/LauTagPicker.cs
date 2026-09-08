using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Laubrary.AssetKit.Editor
{
    /// The multi-select tag popup — structured directly after Zounds' TagsEditorWindow (NOT ported code, per
    /// LauTagLibrary's own "fresh, independent implementation" rule, but the same information architecture,
    /// copied deliberately after two rounds of user feedback that a prior version was unclear):
    /// 1. A header naming WHOSE tags this is — Zounds' own window titles "Tags: {ZoundName}"; this popup had
    ///    no equivalent, which was the direct cause of "unclear if I'm editing the tags of the Zoe or what."
    /// 2. The asset's own current tags, as removable chips.
    /// 3. Labeled Key/Value creation fields + Add — mirrors Zounds' "Tag Name"/"Tag Value" pair.
    /// 4. Quick-pick chips for every existing key, then every value already used under the typed/picked key —
    ///    this IS Zounds' own full "browse everything" affordance; a SEPARATE generic filter+flat-list search
    ///    section (present in an earlier version of this file) doesn't exist in Zounds and was the other
    ///    reported confusion ("there's an input that says filter, what does it do") — removed, not fixed in
    ///    place, since Zounds' own answer is "you don't need one, the chips already cover it."
    /// Deleting a tag from the library entirely (Zounds doesn't expose this — it auto-removes unused tags
    /// instead, which LauTagLibrary doesn't do yet) is tucked behind a right-click on any chip, so it doesn't
    /// compete visually with the primary select/create flow the way a per-row "×" column did.
    /// Currently one caller (LauTagField, "which tags does THIS asset have" — targetName is the asset's own
    /// name), but built as a free multi-select (not Zounds' enforced "one value per key") since a future
    /// browser-filter caller ("which tags should the list narrow TO") would need matching EITHER of several
    /// values under the same key, which a one-per-key rule would fight.
    public class LauTagPicker : PopupWindowContent
    {
        readonly LauTagLibrary _lib;
        readonly HashSet<int> _selected;
        readonly Action<List<int>> _onChanged;
        readonly string _targetName;
        string _key = "";
        string _value = "";

        static GUIStyle s_chipSelected;
        static GUIStyle ChipSelected => s_chipSelected ??= new GUIStyle(GUI.skin.button)
        {
            normal = { textColor = new Color(0.55f, 0.85f, 1f) },
            fontStyle = FontStyle.Bold,
        };

        public static void Show(Rect activatorRect, List<int> currentIds, Action<List<int>> onChanged, string targetName = null) =>
            PopupWindow.Show(activatorRect, new LauTagPicker(currentIds, onChanged, targetName));

        LauTagPicker(List<int> currentIds, Action<List<int>> onChanged, string targetName)
        {
            _lib = LauTagLibraryProvider.Get();
            _selected = new HashSet<int>(currentIds ?? new List<int>());
            _onChanged = onChanged;
            _targetName = targetName;
        }

        public override Vector2 GetWindowSize() => new Vector2(300f, 260f);

        public override void OnGUI(Rect rect)
        {
            // Names WHOSE tags this is — the direct fix for "unclear if I'm editing the tags of the Zoe or
            // what," matching Zounds' own window-title convention ("Tags: {ZoundName}").
            GUILayout.Label(string.IsNullOrEmpty(_targetName) ? "Tags" : "Tags: " + _targetName, EditorStyles.boldLabel);

            DrawSelectedChips();

            EditorGUILayout.Space(6f);
            DrawSeparator();
            EditorGUILayout.Space(4f);
            DrawCreateRow();

            var keys = KeysInLibrary();
            if (keys.Count > 0)
            {
                EditorGUILayout.Space(6f);
                GUILayout.Label("Existing keys (right-click to delete)", EditorStyles.miniLabel);
                DrawChipRow(keys, k => string.Equals(k, _key, StringComparison.OrdinalIgnoreCase), k =>
                {
                    _key = k;
                    _value = "";
                    GUI.FocusControl(null);
                }, DeleteKey);
            }

            var values = ValuesForKey(_key);
            if (values.Count > 0)
            {
                EditorGUILayout.Space(6f);
                GUILayout.Label($"Existing values for \"{_key}\" (right-click to delete)", EditorStyles.miniLabel);
                DrawChipRow(values, v => string.Equals(v, _value, StringComparison.OrdinalIgnoreCase), v =>
                {
                    _value = v;
                    GUI.FocusControl(null);
                }, v => DeleteTagByName($"{_key}:{v}"));
            }
        }

        // ── selected-tags chip row: what's ON this asset right now, each removable in place (left-click) or
        // deletable from the library entirely (right-click) ────────────────────────────────────────────────
        void DrawSelectedChips()
        {
            if (_selected.Count == 0)
            {
                GUILayout.Label("· none ·", EditorStyles.miniLabel);
                return;
            }
            var names = _selected
                .Select(id => _lib.Tags.FirstOrDefault(t => t.id == id))
                .Where(t => t != null)
                .OrderBy(t => t.name)
                .ToList();

            float maxWidth = GetWindowSize().x - 12f;
            float x = 0f;
            bool open = false;
            foreach (var tag in names)
            {
                var content = new GUIContent(tag.name + "  ×", "Click to remove from this asset. Right-click to delete the tag entirely.");
                float w = GUI.skin.button.CalcSize(content).x;
                if (!open || x + w > maxWidth) { if (open) GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); open = true; x = 0f; }
                if (GUILayout.Button(content, GUILayout.ExpandWidth(false)))
                {
                    _selected.Remove(tag.id);
                    Commit();
                    GUIUtility.ExitGUI();
                }
                if (IsContextClick(GUILayoutUtility.GetLastRect())) DeleteTagById(tag.id);
                x += w + 4f;
            }
            if (open) GUILayout.EndHorizontal();
        }

        // ── labeled Key/Value creation row — the direct fix for "two unlabeled boxes": Zounds' own
        // TagsEditorWindow uses exactly this shape (a "Tag Name" field + a "Tag Value" field + Create). ─────
        void DrawCreateRow()
        {
            // Captured BEFORE either TextField draws: a focused TextField's own recycled editor consumes
            // (Event.Use()s) a Return keydown as part of committing/ending edit, which flips Event.current.
            // type to Used for the REST of this GUI pass — checking for KeyDown only after the fields have
            // already drawn (the previous shape) meant Return silently never reached the Add check at all,
            // regardless of which field had focus. Captured once up front, it survives that consumption.
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Key", GUILayout.Width(34));
                _key = EditorGUILayout.TextField(_key).Replace(":", "");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Value", GUILayout.Width(34));
                _value = EditorGUILayout.TextField(_value).Replace(":", "");

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_key)))
                {
                    if ((GUILayout.Button("Add", GUILayout.Width(48)) || (enter && !string.IsNullOrWhiteSpace(_key))))
                    {
                        string name = string.IsNullOrWhiteSpace(_value) ? _key.Trim() : $"{_key.Trim()}:{_value.Trim()}";
                        var t = _lib.GetOrCreateTag(name);
                        if (t != null) { _selected.Add(t.id); Commit(); }
                        _value = "";
                        EditorUtility.SetDirty(_lib);
                        GUI.FocusControl(null);
                        if (enter) Event.current.Use();
                    }
                }
            }
        }

        List<string> KeysInLibrary() =>
            _lib.Tags.Select(t => t.name.Split(':')[0]).Where(k => !string.IsNullOrEmpty(k))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();

        List<string> ValuesForKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return new List<string>();
            return _lib.Tags
                .Select(t => t.name.Split(new[] { ':' }, 2))
                .Where(parts => parts.Length == 2 && string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))
                .Select(parts => parts[1])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();
        }

        void DrawChipRow(List<string> labels, Func<string, bool> isSelected, Action<string> onClick, Action<string> onDelete)
        {
            float maxWidth = GetWindowSize().x - 12f;
            float x = 0f;
            bool open = false;
            foreach (var label in labels)
            {
                var content = new GUIContent(label);
                float w = GUI.skin.button.CalcSize(content).x;
                if (!open || x + w > maxWidth) { if (open) GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); open = true; x = 0f; }
                var style = isSelected(label) ? ChipSelected : GUI.skin.button;
                if (GUILayout.Button(content, style, GUILayout.ExpandWidth(false))) onClick(label);
                if (IsContextClick(GUILayoutUtility.GetLastRect())) onDelete(label);
                x += w + 4f;
            }
            if (open) GUILayout.EndHorizontal();
        }

        // Right-click-to-delete, shared by every chip row — a context-click on a control's own rect doesn't
        // come through GUILayout.Button's return value (that's left-click only), so this checks the raw event.
        static bool IsContextClick(Rect r)
        {
            var e = Event.current;
            if (e.type != EventType.ContextClick || !r.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }

        void DeleteKey(string key)
        {
            var toDelete = _lib.Tags.Where(t => string.Equals(t.name.Split(':')[0], key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (toDelete.Count == 0) return;
            if (!EditorUtility.DisplayDialog("Delete key", $"Delete '{key}' and every 'value' under it ({toDelete.Count} tag(s))? Removes them from every asset.", "Delete", "Cancel")) return;
            foreach (var t in toDelete) { _lib.DeleteTag(t.id); _selected.Remove(t.id); }
            if (string.Equals(_key, key, StringComparison.OrdinalIgnoreCase)) { _key = ""; _value = ""; }
            // T-0282 — flush THIS asset (the tag library), never the project. See Commit() below.
            EditorUtility.SetDirty(_lib);
            AssetDatabase.SaveAssetIfDirty(_lib);
            Commit();
        }

        void DeleteTagByName(string name)
        {
            var t = _lib.FindTag(name);
            if (t == null) return;
            DeleteTagById(t.id);
        }

        void DeleteTagById(int id)
        {
            var t = _lib.Tags.FirstOrDefault(x => x.id == id);
            if (t == null) return;
            if (!EditorUtility.DisplayDialog("Delete tag", $"Delete tag '{t.name}'? Removes it from every asset.", "Delete", "Cancel")) return;
            _lib.DeleteTag(id);
            _selected.Remove(id);
            // T-0282 — flush THIS asset (the tag library), never the project. See Commit() below.
            EditorUtility.SetDirty(_lib);
            AssetDatabase.SaveAssetIfDirty(_lib);
            Commit();
            GUIUtility.ExitGUI();
        }

        static void DrawSeparator()
        {
            var prev = GUI.color;
            GUI.color = Color.gray;
            var r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(r, EditorGUIUtility.whiteTexture);
            GUI.color = prev;
        }

        void Commit()
        {
            // T-0282 — flush THIS asset (the project's one LauTagLibrary), never the whole project.
            // AssetDatabase.SaveAssets() writes every dirty asset there is; every tag edit (add/remove/rename
            // a key or value on ANY asset) funnels through here, so it ran on every keystroke-adjacent commit.
            EditorUtility.SetDirty(_lib);
            AssetDatabase.SaveAssetIfDirty(_lib);
            _onChanged?.Invoke(_selected.ToList());
        }
    }
}
