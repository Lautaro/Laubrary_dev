using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// The one "pick a LauAsset" thumbnail-grid popup, constrained to a type/interface — generalizes what
    /// BackSplashPicker was (BackSplash-only) to any constraint, so every field that needs a Recall popup uses
    /// this instead of writing its own. Candidates are every concrete type satisfying the constraint
    /// (AssetLibraryUntyped.ConcreteTypesSatisfying — TypeCache-backed, so unregistered types still show up),
    /// not just types with a LauAssetEditors registration — that registry is for editor ACTIONS, not for
    /// deciding what's a valid pick.
    public class LauAssetPicker : PopupWindowContent
    {
        const int Columns = 4;
        const float CellSize = 104f;
        const float ThumbSize = 92f;

        readonly Action<Object> _onPick;
        readonly Object _current;
        readonly List<Object> _entries;
        readonly Dictionary<Object, Texture2D> _thumbCache = new Dictionary<Object, Texture2D>();
        readonly LauTagLibrary _tagLib;
        readonly HashSet<int> _filterTagIds = new HashSet<int>();
        readonly Action<string> _onCreateNew;
        readonly string _pickHint;
        string _newName = "";
        bool _showTagFilter;
        Vector2 _scroll;

        // Type filter — separate from the tag filter above, same "▾ toggles a checklist row" shape and
        // placement (shares the header row, immediately left of Tags ▾). Only meaningful when the candidate
        // set spans more than one concrete type — the curated multi-type union pickers (e.g. Mirage's own
        // Zoe+Pyre "Add Previewable" browser) are the case this exists for; a single-type-constrained
        // picker (the common case) has nothing to filter by type, so the control hides itself entirely.
        readonly HashSet<Type> _filterTypes = new HashSet<Type>();
        bool _showTypeFilter;
        List<Type> _distinctTypes;
        List<Type> DistinctTypes => _distinctTypes ??= _entries.Select(o => o.GetType()).Distinct().OrderBy(t => t.Name).ToList();

        public static void Show(Rect activatorRect, Type constraint, Action<Object> onPick, Object current,
            Action<string> onCreateNew = null, string pickHint = null) =>
            PopupWindow.Show(activatorRect, new LauAssetPicker(
                AssetLibraryUntyped.ConcreteTypesSatisfying(constraint).SelectMany(t => AssetLibraryUntyped.Enumerate(t)),
                onPick, current, onCreateNew, pickHint));

        /// For a curated, non-interface union of types that don't share one constraint (e.g. Mirage's
        /// previewable picker: Zoe + Pyre + Sprite have nothing in common) — pass the pre-enumerated
        /// candidates directly instead of a Type.
        ///
        /// onCreateNew, when given, adds a name field + "Save as new" row above the grid — turns this from a
        /// pure picker into a save-target picker: click an existing thumbnail to act on it (the picked-object
        /// meaning is entirely up to onPick — "load this," "overwrite this," whatever the caller needs), or
        /// type a name to create a new one instead. pickHint overrides the default "{N} asset(s)" header —
        /// useful when picking means something other than loading (e.g. "Click an existing preset to
        /// overwrite it").
        public static void Show(Rect activatorRect, IEnumerable<Object> items, Action<Object> onPick, Object current,
            Action<string> onCreateNew = null, string pickHint = null) =>
            PopupWindow.Show(activatorRect, new LauAssetPicker(items, onPick, current, onCreateNew, pickHint));

        LauAssetPicker(IEnumerable<Object> items, Action<Object> onPick, Object current, Action<string> onCreateNew, string pickHint)
        {
            _onPick = onPick;
            _current = current;
            _onCreateNew = onCreateNew;
            _pickHint = pickHint;
            _entries = items.Where(o => o != null).Distinct().OrderBy(o => o.name).ToList();
            _tagLib = LauTagLibraryProvider.Get();
        }

        public override Vector2 GetWindowSize()
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt(_entries.Count / (float)Columns));
            float filterHeight = _tagLib.Tags.Count > 0 ? 20f * Mathf.Min(_tagLib.Tags.Count, 6) : 46f; // 46 fits the "no tags yet" HelpBox
            float typeFilterHeight = _showTypeFilter && DistinctTypes.Count > 1 ? 20f * DistinctTypes.Count + 10f : 0f;
            float height = 28f + (_onCreateNew != null ? 24f : 0f) + (_showTagFilter ? filterHeight + 10f : 0f) + typeFilterHeight
                          + Mathf.Min(rows, 4) * (CellSize + 8f) + 12f;
            return new Vector2(Columns * (CellSize + 6f) + 16f, height);
        }

        public override void OnGUI(Rect rect)
        {
            if (_onCreateNew != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _newName = EditorGUILayout.TextField(new GUIContent("", "Name for a brand new asset."), _newName, GUILayout.Width(140));
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newName)))
                        if (GUILayout.Button(new GUIContent("Save as new", "Create a brand new asset with this name instead of overwriting an existing one."), GUILayout.Width(90)))
                        {
                            _onCreateNew(_newName.Trim());
                            editorWindow.Close();
                            GUIUtility.ExitGUI();
                        }
                }
                EditorGUILayout.Space(2f);
            }

            var filtered = FilteredEntries();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(_pickHint ?? $"{filtered.Count} asset(s)", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                // Only shown when the candidate set actually spans more than one concrete type — a
                // single-type-constrained picker (the common case) has nothing for this to filter.
                if (DistinctTypes.Count > 1)
                {
                    bool typeActive = _filterTypes.Count > 0;
                    if (ZUI.Button(new GUIContent("Type ▾", "Filter the grid below to only the selected asset type(s)."),
                            typeActive ? ZUI.Style.Active : ZUI.Style.Default, GUILayout.Width(58)))
                        _showTypeFilter = !_showTypeFilter;
                    GUILayout.Space(4f);
                }
                // Always shown (even with zero tags in the library yet) so the filter is discoverable —
                // it used to only appear once tags existed, which meant a first-time user saw no tag UI
                // here at all and went looking elsewhere (found LaubraryAssetWindow's per-document
                // "Tags…" toolbar button instead, which edits the wrong thing while picking).
                bool active = _filterTagIds.Count > 0;
                if (ZUI.Button(new GUIContent("Tags ▾", "Filter the grid below to only assets carrying the selected tag(s)."),
                        active ? ZUI.Style.Active : ZUI.Style.Default, GUILayout.Width(55)))
                    _showTagFilter = !_showTagFilter;
            }

            if (_showTypeFilter && DistinctTypes.Count > 1)
            {
                foreach (var t in DistinctTypes)
                {
                    bool on = _filterTypes.Contains(t);
                    bool newOn = EditorGUILayout.ToggleLeft(ObjectNames.NicifyVariableName(t.Name), on);
                    if (newOn != on) { if (newOn) _filterTypes.Add(t); else _filterTypes.Remove(t); }
                }
                EditorGUILayout.Space(2f);
            }

            if (_showTagFilter)
            {
                if (_tagLib.Tags.Count == 0)
                {
                    EditorGUILayout.HelpBox("No tags yet. Tag an asset from its own editor window (the " +
                                             "\"Tags…\" button on its toolbar), then filter by it here.", MessageType.None);
                }
                foreach (var tag in _tagLib.Tags.OrderBy(t => t.name))
                {
                    bool on = _filterTagIds.Contains(tag.id);
                    bool newOn = EditorGUILayout.ToggleLeft(tag.name, on);
                    if (newOn != on) { if (newOn) _filterTagIds.Add(tag.id); else _filterTagIds.Remove(tag.id); }
                }
                EditorGUILayout.Space(2f);
            }

            if (filtered.Count == 0)
            {
                GUILayout.Label("None match.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            LauAssetGridGUI.DrawGrid(rect.width, filtered, _current, (item, clickCount) =>
            {
                _onPick?.Invoke(item);
                editorWindow.Close();
                GUIUtility.ExitGUI();
            }, _thumbCache, cellSize: CellSize, thumbSize: ThumbSize);
            GUILayout.EndScrollView();
        }

        List<Object> FilteredEntries()
        {
            IEnumerable<Object> result = _entries;
            if (_filterTypes.Count > 0)
                result = result.Where(o => _filterTypes.Contains(o.GetType()));
            if (_filterTagIds.Count > 0)
            {
                var expanded = LauTagFilter.ExpandSelection(_filterTagIds, _tagLib);
                result = result.Where(o =>
                {
                    string path = AssetDatabase.GetAssetPath(o);
                    if (string.IsNullOrEmpty(path)) return false;
                    var ids = _tagLib.GetTagIds(AssetDatabase.AssetPathToGUID(path));
                    return LauTagFilter.Matches(ids, expanded);
                });
            }
            return result.ToList();
        }

        public override void OnClose() => LauAssetGridGUI.ClearCache(_thumbCache);
    }
}
