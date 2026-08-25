using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// The one "pick an asset" thumbnail-grid popup. Merges what used to be two separate, near-identical
    /// classes — LauAssetPicker (typed LauAssets) and LauBrowser (raw Unity assets like Sprite/Texture) —
    /// into one shell with a pluggable candidate SOURCE. Only "how do candidates get found" ever differed
    /// between them; the grid, search, sort, tag/type filters and hover-preview toggle were duplicated
    /// feature-for-feature across both.
    ///
    /// Three ways to get candidates:
    ///  - Type(constraint): every concrete type satisfying a constraint (AssetLibraryUntyped.
    ///    ConcreteTypesSatisfying — TypeCache-backed, so unregistered types still show up) — the common
    ///    LauAsset case (Pyre, Zoe, WeaponDef, ...). Loaded eagerly (LauAsset counts are small) and shown
    ///    immediately.
    ///  - Curated(items): a pre-enumerated, possibly cross-type union a caller already computed (e.g.
    ///    Mirage's own Zoe+Pyre previewable list, which have nothing in common to constrain by). Also eager.
    ///  - RawSearch(unityTypeFilter): raw Unity types with no TypeCache-discoverable concrete-type list
    ///    (Sprite, Texture, any built-in type) — LAZY, nothing loads until you type a search term, so this
    ///    can never flood the popup with every matching asset in the project up front (a real incident,
    ///    2026-07-21 — 1000+ "every icon in the project" results from an eager `t:Sprite` scan). The
    ///    type-only `FindAssets` + `GUIDToAssetPath` pass is an index lookup (no asset loading) and stays
    ///    fast even at 1000+ matches; the search term then filters those PATHS by substring in code — Unity's
    ///    own `FindAssets("t:X term")` name search is TOKEN-based, not substring ("Green" does not match
    ///    "Green_Lantern.png"), confirmed live, too strict for a real search box. Only paths surviving both
    ///    the substring filter and the exclude predicate ever get `LoadAllAssetsAtPath` called, capped at
    ///    RawSearchMaxResults — an excluded or overflow asset's texture data is never touched.
    public class LauAssetBrowser : PopupWindowContent
    {
        const int Columns = 4;
        const float CellSize = 104f;
        const float ThumbSize = 92f;
        const int RawSearchMaxResults = 60;

        /// Shared convention: any baker that writes a derived sprite sheet (PyreBaker, BlastBaker, …) can
        /// mark its TextureImporter.userData with this prefix + a short tag identifying itself, and RawSearch
        /// mode automatically excludes it — no per-tool wiring needed at the call site.
        public const string BakedMarkerPrefix = "LaubraryBaked:";

        public static bool IsMarkedBaked(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var importer = AssetImporter.GetAtPath(path);
            return importer != null && !string.IsNullOrEmpty(importer.userData)
                && importer.userData.StartsWith(BakedMarkerPrefix);
        }

        public static readonly Func<string, bool> DefaultExclude = IsMarkedBaked;

        enum SourceMode { TypeConstraint, Curated, RawSearch }
        enum SortMode { NameAsc, NameDesc, RecentFirst }
        static readonly GUIContent[] SortLabels =
        {
            new GUIContent("Name A-Z", "Sort alphabetically."),
            new GUIContent("Name Z-A", "Sort reverse-alphabetically."),
            new GUIContent("Most recent", "Sort by file modified time, newest first."),
        };

        readonly SourceMode _mode;
        readonly List<Object> _eagerEntries;          // Type/Curated modes — loaded once at construction
        readonly string _unityTypeFilter;              // RawSearch only — e.g. "t:Sprite"
        readonly Func<string, bool> _excludePath;       // RawSearch only

        readonly Action<Object> _onPick;
        readonly Object _current;
        readonly Dictionary<Object, Texture2D> _thumbCache = new Dictionary<Object, Texture2D>();
        readonly LauTagLibrary _tagLib;
        readonly HashSet<int> _filterTagIds = new HashSet<int>();
        readonly Action<string> _onCreateNew;
        readonly string _pickHint;
        string _newName = "";
        bool _showTagFilter;
        Vector2 _scroll;

        string _search = "";
        SortMode _sort = SortMode.NameAsc;
        List<Object> _rawResults = new List<Object>();  // RawSearch mode's current (capped) match set
        int _totalMatched;                              // RawSearch mode — before the cap

        // Off by default — a grid full of animatable assets shouldn't all animate at once. Off = static
        // thumbnails, hover one to preview it.
        bool _animateAll;
        Object _hoveredThumb;
        double _lastTick;

        readonly HashSet<Type> _filterTypes = new HashSet<Type>();
        bool _showTypeFilter;
        List<Type> _distinctTypes;
        List<Type> DistinctTypes => _distinctTypes ??= SourceEntries().Select(o => o.GetType()).Distinct().OrderBy(t => t.Name).ToList();

        public static void Show(Rect activatorRect, Type constraint, Action<Object> onPick, Object current,
            Action<string> onCreateNew = null, string pickHint = null) =>
            PopupWindow.Show(activatorRect, new LauAssetBrowser(SourceMode.TypeConstraint,
                AssetLibraryUntyped.ConcreteTypesSatisfying(constraint).SelectMany(t => AssetLibraryUntyped.Enumerate(t)),
                null, null, onPick, current, onCreateNew, pickHint));

        /// For a curated, non-interface union of types that don't share one constraint (e.g. Mirage's
        /// previewable picker: Zoe + Pyre + Sprite have nothing in common) — pass the pre-enumerated
        /// candidates directly instead of a Type.
        public static void Show(Rect activatorRect, IEnumerable<Object> items, Action<Object> onPick, Object current,
            Action<string> onCreateNew = null, string pickHint = null) =>
            PopupWindow.Show(activatorRect, new LauAssetBrowser(SourceMode.Curated, items, null, null, onPick, current, onCreateNew, pickHint));

        /// Raw Unity assets (Sprite, Texture, ...) — nothing loads until the user types a search term.
        /// excludePath defaults to DefaultExclude (skip anything carrying the shared baked-output marker).
        public static void Show(Rect activatorRect, string unityTypeFilter, Action<Object> onPick, Object current,
            Func<string, bool> excludePath = null) =>
            PopupWindow.Show(activatorRect, new LauAssetBrowser(SourceMode.RawSearch, null, unityTypeFilter,
                excludePath ?? DefaultExclude, onPick, current, null, null));

        LauAssetBrowser(SourceMode mode, IEnumerable<Object> items, string unityTypeFilter, Func<string, bool> excludePath,
            Action<Object> onPick, Object current, Action<string> onCreateNew, string pickHint)
        {
            _mode = mode;
            _eagerEntries = items?.Where(o => o != null).Distinct().ToList();
            _unityTypeFilter = unityTypeFilter;
            _excludePath = excludePath;
            _onPick = onPick;
            _current = current;
            _onCreateNew = onCreateNew;
            _pickHint = pickHint;
            _tagLib = LauTagLibraryProvider.Get();
        }

        List<Object> SourceEntries() => _mode == SourceMode.RawSearch ? _rawResults : _eagerEntries;

        Action _unwatchInvalidation;

        public override void OnOpen()
        {
            EditorApplication.update += Tick;
            // So an edited asset's thumbnail refreshes immediately instead of showing a stale render —
            // see LauAssetGridGUI.WatchInvalidation.
            _unwatchInvalidation = LauAssetGridGUI.WatchInvalidation(_thumbCache, () => editorWindow?.Repaint());
        }

        void Tick()
        {
            if (LauAssetGridGUI.TickAnimatedPreviews(_thumbCache, ref _lastTick, _animateAll, _hoveredThumb))
                editorWindow?.Repaint();
        }

        public override Vector2 GetWindowSize()
        {
            int count = _mode == SourceMode.RawSearch
                ? (string.IsNullOrEmpty(_search) ? 0 : _rawResults.Count)
                : SourceEntries().Count;
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)Columns));
            float filterHeight = _tagLib.Tags.Count > 0 ? 20f * Mathf.Min(_tagLib.Tags.Count, 6) : 46f;
            float typeFilterHeight = _showTypeFilter && DistinctTypes.Count > 1 ? 20f * DistinctTypes.Count + 10f : 0f;
            float height = 28f + 22f + (_onCreateNew != null ? 24f : 0f) + (_showTagFilter ? filterHeight + 10f : 0f) + typeFilterHeight
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

            using (new EditorGUILayout.HorizontalScope())
            {
                var searchTip = _mode == SourceMode.RawSearch
                    ? "Type at least part of an asset's name — nothing loads until you search, so this never "
                      + "has to scan or list every matching asset in the project up front."
                    : "Filter by name.";
                EditorGUI.BeginChangeCheck();
                var newSearch = EditorGUILayout.TextField(new GUIContent("", searchTip), _search, EditorStyles.toolbarSearchField);
                bool searchChanged = EditorGUI.EndChangeCheck() && newSearch != _search;
                _search = newSearch;

                EditorGUI.BeginChangeCheck();
                int newSort = EditorGUILayout.Popup((int)_sort, SortLabels, EditorStyles.toolbarPopup, GUILayout.Width(86));
                bool sortChanged = EditorGUI.EndChangeCheck() && newSort != (int)_sort;
                _sort = (SortMode)newSort;

                if (_mode == SourceMode.RawSearch && (searchChanged || sortChanged)) RefreshRawSearch();
            }

            if (_mode == SourceMode.RawSearch && string.IsNullOrEmpty(_search))
            {
                GUILayout.Label("Type to search.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            var filtered = FilteredEntries();
            using (new EditorGUILayout.HorizontalScope())
            {
                string header = _mode == SourceMode.RawSearch && _totalMatched > filtered.Count
                    ? $"{filtered.Count} of {_totalMatched} shown — narrow your search"
                    : (_pickHint ?? $"{filtered.Count} asset(s)");
                GUILayout.Label(header, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                bool newAll = GUILayout.Toggle(_animateAll,
                    new GUIContent("▶", "On: every animated preview plays at once. Off: previews stay static — "
                        + "hover one to preview it."), EditorStyles.toolbarButton, GUILayout.Width(24));
                if (newAll != _animateAll) { _animateAll = newAll; if (newAll) _hoveredThumb = null; }
                GUILayout.Space(4f);
                if (DistinctTypes.Count > 1)
                {
                    bool typeActive = _filterTypes.Count > 0;
                    if (ZUI.Button(new GUIContent("Type ▾", "Filter the grid below to only the selected asset type(s)."),
                            typeActive ? ZUI.Style.Active : ZUI.Style.Default, GUILayout.Width(58)))
                        _showTypeFilter = !_showTypeFilter;
                    GUILayout.Space(4f);
                }
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
            var hovered = LauAssetGridGUI.DrawGrid(rect.width, filtered, _current, (item, clickCount) =>
            {
                _onPick?.Invoke(item);
                editorWindow.Close();
                GUIUtility.ExitGUI();
            }, _thumbCache, cellSize: CellSize, thumbSize: ThumbSize);
            GUILayout.EndScrollView();

            if (!_animateAll && !ReferenceEquals(hovered, _hoveredThumb))
                LauAssetGridGUI.OnHoverChanged(_thumbCache, _hoveredThumb, hovered);
            _hoveredThumb = hovered;
        }

        /// Re-runs the type-only FindAssets + substring + exclude + load + sort + cap pass — only called in
        /// RawSearch mode, and only when the search text or sort mode actually changed.
        void RefreshRawSearch()
        {
            _rawResults.Clear();
            _totalMatched = 0;
            _distinctTypes = null;
            if (string.IsNullOrEmpty(_search)) return;

            var guids = AssetDatabase.FindAssets(_unityTypeFilter);
            var seenPaths = new HashSet<string>();
            var matched = new List<Object>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seenPaths.Add(path)) continue;
                if (path.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (_excludePath != null && _excludePath(path)) continue;

                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (obj != null && !(obj is GameObject)) matched.Add(obj);
            }

            // Sort the FULL matched set before capping, so "Most recent"/"Name Z-A" reflect the true top-N
            // across everything the search found, not just whatever order the first N happened to load in.
            ApplySort(matched);
            _totalMatched = matched.Count;
            for (int i = 0; i < matched.Count && i < RawSearchMaxResults; i++) _rawResults.Add(matched[i]);
        }

        void ApplySort(List<Object> list)
        {
            switch (_sort)
            {
                case SortMode.NameAsc: list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase)); break;
                case SortMode.NameDesc: list.Sort((a, b) => string.Compare(b.name, a.name, StringComparison.OrdinalIgnoreCase)); break;
                case SortMode.RecentFirst: list.Sort((a, b) => LastWriteTime(b).CompareTo(LastWriteTime(a))); break;
            }
        }

        static DateTime LastWriteTime(Object obj)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return DateTime.MinValue;
            return System.IO.File.GetLastWriteTimeUtc(path);
        }

        List<Object> FilteredEntries()
        {
            IEnumerable<Object> result = SourceEntries();
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

            if (_mode == SourceMode.RawSearch)
                return result.ToList();   // already substring-filtered, sorted, and capped by RefreshRawSearch

            if (!string.IsNullOrEmpty(_search))
                result = result.Where(o => o.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
            var list = result.ToList();
            ApplySort(list);
            return list;
        }

        public override void OnClose()
        {
            EditorApplication.update -= Tick;
            _unwatchInvalidation?.Invoke();
            LauAssetGridGUI.ClearCache(_thumbCache);
        }
    }
}
