using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// A search-driven, capped popup for browsing potentially THOUSANDS of raw Unity assets (Sprites, Textures,
    /// any built-in type) that LauAssetPicker can't help with — a Sprite isn't a LauAsset, so there's no
    /// TypeCache-discoverable concrete type for it to search by. This exists specifically so a field that needs
    /// "pick any Sprite in the project" is NOT forced onto Unity's native ObjectField/object picker (which has
    /// no per-asset filtering hook at all) just because the candidate set is too large for LauAssetPicker's
    /// eager, unfiltered list.
    ///
    /// Never loads or lists a single candidate until the user types a search term, and even then caps how many
    /// get their thumbnails loaded — so unlike a naive `AssetDatabase.FindAssets("t:Sprite")` scan followed by
    /// eagerly loading every match (which flooded a picker with 1000+ "every icon in the project" results,
    /// 2026-07-21, see MirageAssetPicker.cs's own doc comment on that incident), this can never front-load the
    /// whole project's asset content. The type-only `FindAssets` + `GUIDToAssetPath` pass is an index lookup —
    /// no asset loading — and stays fast even at 1000+ matches; the search term then filters those PATHS by
    /// substring in code (Unity's own `FindAssets("t:X term")` name search is token-based, not substring — it
    /// does NOT match "Green_Lantern.png" against a search for "Green", confirmed live, too strict for a real
    /// search box). Only the paths that survive both the substring filter and the exclude predicate ever get
    /// `LoadAllAssetsAtPath` called on them, capped at MaxResults — an excluded asset's texture data is never
    /// touched at all.
    ///
    /// Excludes derived bake output (PyrePlusBaker/BlastBaker's own sliced sub-sprites — already reachable
    /// through their proper pick path, a PyrePlusSpec/Pyre spec via its IChunkAnimation adapter) by DEFAULT, via
    /// the shared BakedMarkerPrefix convention below — no per-tool wiring needed at any Show() call site. Pass
    /// a different excludePath predicate only if a caller needs something beyond that default.
    ///
    /// SCOPE, NOT A MANDATE (Lautaro, 2026-08-23): this only covers whatever's actually been routed through it
    /// (currently MirageWindow's two Sprite fields — see its own comments). It's completely fine for some other
    /// raw-asset-picking spot to keep using Unity's native ObjectField/object picker, even with the same
    /// filtering gap this exists to close — swap it to LauBrowser opportunistically, when someone's actually
    /// touching that code, not as a sweep. Native picking isn't wrong, it just can't filter; that only matters
    /// where filtering is actually needed.
    public class LauBrowser : PopupWindowContent
    {
        const int Columns = 4;
        const float CellSize = 104f;
        const float ThumbSize = 92f;
        const int MaxResults = 60;

        /// Shared convention: any baker that writes a derived sprite sheet (PyrePlusBaker, BlastBaker, …) can
        /// mark its TextureImporter.userData with this prefix + a short tag identifying itself, and every
        /// LauBrowser automatically excludes it (see IsMarkedBaked/DefaultExclude below) — no per-tool wiring
        /// needed at the call site, and no reference from AssetKit.Editor back to any of those tools.
        public const string BakedMarkerPrefix = "LaubraryBaked:";

        /// True if the asset at `path` carries the shared baked-output marker. Exposed so a caller building
        /// its own excludePath predicate can compose it with additional exclusions.
        public static bool IsMarkedBaked(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var importer = AssetImporter.GetAtPath(path);
            return importer != null && !string.IsNullOrEmpty(importer.userData)
                && importer.userData.StartsWith(BakedMarkerPrefix);
        }

        /// The default exclude predicate every Show() call uses when the caller doesn't supply its own —
        /// filters out anything carrying the shared baked marker, so a plain Sprite/Texture browse never needs
        /// bespoke per-tool exclusion logic to stay clean.
        public static readonly Func<string, bool> DefaultExclude = IsMarkedBaked;

        readonly string _unityTypeFilter;   // e.g. "t:Sprite" — combined into the FindAssets query verbatim
        readonly Action<Object> _onPick;
        readonly Object _current;
        readonly Func<string, bool> _excludePath;
        readonly Dictionary<Object, Texture2D> _thumbCache = new Dictionary<Object, Texture2D>();
        readonly List<Object> _results = new List<Object>();
        string _search = "";
        int _totalMatched;   // before the MaxResults cap, so the header can say "60 of 240 — narrow your search"
        Vector2 _scroll;

        /// excludePath defaults to DefaultExclude (skip anything carrying the shared baked-output marker) —
        /// pass a different predicate to compose additional exclusions, or `p => false` to disable filtering
        /// entirely (rare; only if a caller genuinely wants baked output included).
        public static void Show(Rect activatorRect, string unityTypeFilter, Action<Object> onPick, Object current,
            Func<string, bool> excludePath = null) =>
            PopupWindow.Show(activatorRect, new LauBrowser(unityTypeFilter, onPick, current, excludePath ?? DefaultExclude));

        LauBrowser(string unityTypeFilter, Action<Object> onPick, Object current, Func<string, bool> excludePath)
        {
            _unityTypeFilter = unityTypeFilter;
            _onPick = onPick;
            _current = current;
            _excludePath = excludePath;
        }

        public override Vector2 GetWindowSize()
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt(_results.Count / (float)Columns));
            float gridHeight = string.IsNullOrEmpty(_search) ? 0f : Mathf.Min(rows, 4) * (CellSize + 8f) + 20f;
            return new Vector2(Columns * (CellSize + 6f) + 16f, 34f + gridHeight + 12f);
        }

        public override void OnGUI(Rect rect)
        {
            var searchTip = "Type at least part of an asset's name — nothing loads until you search, so this "
                + "never has to scan or list every matching asset in the project up front.";
            EditorGUI.BeginChangeCheck();
            var newSearch = EditorGUILayout.TextField(new GUIContent("", searchTip), _search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck() && newSearch != _search)
            {
                _search = newSearch;
                RefreshResults();
            }

            if (string.IsNullOrEmpty(_search))
            {
                GUILayout.Label("Type to search.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            string header = _totalMatched > _results.Count
                ? $"{_results.Count} of {_totalMatched} shown — narrow your search"
                : $"{_results.Count} asset(s)";
            GUILayout.Label(header, EditorStyles.boldLabel);

            if (_results.Count == 0)
            {
                GUILayout.Label("None match.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            LauAssetGridGUI.DrawGrid(rect.width, _results, _current, (item, clickCount) =>
            {
                _onPick?.Invoke(item);
                editorWindow.Close();
                GUIUtility.ExitGUI();
            }, _thumbCache, cellSize: CellSize, thumbSize: ThumbSize);
            GUILayout.EndScrollView();
        }

        void RefreshResults()
        {
            _results.Clear();
            _totalMatched = 0;
            if (string.IsNullOrEmpty(_search)) return;

            // Type-only FindAssets, THEN a substring filter on the path string in code. Unity's own
            // FindAssets("t:X term") name search is TOKEN-based, not substring — "Green" does not match
            // "Green_Lantern.png" at all (underscore isn't a token boundary), confirmed live — too strict for
            // a real search box. The type-only FindAssets + GUIDToAssetPath pass below is an index lookup (no
            // asset loading) and stays fast even at 1000+ matches (measured ~15ms for every Sprite in this
            // project) — only LoadAllAssetsAtPath (below, capped at MaxResults) actually opens a file, and by
            // then the substring filter has already narrowed the set.
            var guids = AssetDatabase.FindAssets(_unityTypeFilter);
            var seenPaths = new HashSet<string>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                // FindAssets can return one GUID per matching sub-asset that all share the same container path
                // (e.g. several sprites in one multi-sprite texture) — load each matching file's sub-assets once.
                if (string.IsNullOrEmpty(path) || !seenPaths.Add(path)) continue;
                if (path.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                // Checked BEFORE loading — an excluded asset's texture data is never touched at all.
                if (_excludePath != null && _excludePath(path)) continue;

                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (obj == null || obj is GameObject) continue;
                    _totalMatched++;
                    if (_results.Count < MaxResults) _results.Add(obj);
                }
            }
        }

        public override void OnClose() => LauAssetGridGUI.ClearCache(_thumbCache);
    }
}
