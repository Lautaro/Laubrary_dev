using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The one picker for audio clips and sounds (owner, 2026-10-08: "a rethinking and visual overhaul of the file and
    /// zounds picker windows"). A call site hands it a <see cref="ZoundPickerRequest"/>; the window shows the listing in a
    /// virtualised list (thousands of rows cost nothing), searched as you type with a forgiving match, filtered by kind,
    /// favourites, recents and "unused", sorted by name / recent / most used / length / folder, grouped by folder, tag or
    /// kind, in a compact, list or grid view, with a details pane beside it (waveform, format, where it lives, who uses
    /// it) so picking never scrolls the details out of view. Arrow keys move, Enter picks, Space auditions, Esc closes;
    /// several can be picked at once; rows can be dragged into a Zequence. Its state (filters, view, size and place,
    /// favourites, recent picks) is remembered per listing and project.
    /// </summary>
    public sealed class ZoundPickerWindowTK : ZuiWindow {

        // ─────────────────────────── state ───────────────────────────

        ZoundPickerRequest request;
        ZoundPickerMemory memory;
        readonly List<ZoundPickerItem> shown = new List<ZoundPickerItem>();   // after filter and sort, in display order
        readonly List<Row> rows = new List<Row>();                              // what the list shows: headers, items or tile rows
        readonly HashSet<string> selected = new HashSet<string>();
        int cursor = -1, anchor = -1;
        string playingKey;

        ListView list;
        TextField search;
        Label count, selectedCount;
        VisualElement details, top;
        Button pick;
        ZuiSegmented kindSeg, sortSeg, groupSeg, viewSeg;
        float lastWidth;
        int columns = 1;

        sealed class Row { public string header; public int headerCount; public ZoundPickerItem item; public List<ZoundPickerItem> tiles; }

        static readonly string[] SortNames = { "Name", "Recent", "Used", "Length", "Folder" };
        static readonly string[] GroupNames = { "None", "Folder", "Tag", "Kind" };
        static readonly string[] ViewNames = { "Compact", "List", "Grid" };
        const int ViewCompact = 0, ViewList = 1, ViewGrid = 2;
        const float RowCompact = 20f, RowList = 30f, TileH = 74f, TileW = 150f, HeaderH = 20f;

        static ZoundPickerWindowTK open;

        // ─────────────────────────── opening ───────────────────────────

        /// <summary>Shows the picker for <paramref name="req"/>; one picker at a time (a second request replaces the first).</summary>
        public static ZoundPickerWindowTK Open(ZoundPickerRequest req) {
            if (req == null) return null;
            if (open != null) { try { open.Close(); } catch { } }
            var w = CreateInstance<ZoundPickerWindowTK>();
            w.request = req;
            w.memory = ZoundPickerMemory.Load(req.stateKey);
            w.titleContent = new GUIContent(req.title);
            w.minSize = new Vector2(640f, 360f);
            var r = w.memory.window;
            bool remembered = r.width >= 640f && r.height >= 360f && OnScreen(r);
            if (!remembered) {
                var m = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
                r = new Rect(m.x - 480f, m.y + 12f, 980f, 620f);
            }
            w.ShowUtility();
            w.position = r;
            open = w;
            return w;
        }

        static bool OnScreen(Rect r) {
            // Any corner on some display is good enough; a window remembered on an unplugged monitor comes back near the mouse.
            var main = new Rect(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);
            return main.Overlaps(r) || r.x > -8000f && r.x < 8000f && r.y > -4000f && r.y < 4000f;
        }

        protected override string PresentationTool => "zounds";

        protected override void OnDisable() {
            base.OnDisable();
            StopAudition();
            if (memory != null && request != null) { memory.window = position; memory.search = search != null ? search.value : memory.search; memory.Save(request.stateKey); }
            if (open == this) open = null;
        }

        // ─────────────────────────── building ───────────────────────────

        protected override void BuildUI(VisualElement root) {
            if (!ZS.EditorStylesReady) { root.schedule.Execute(Rebuild).StartingIn(100); return; }
            // The request lives only for the session that opened the picker: after a script reload there is nothing to
            // pick for, so the window closes itself rather than stand empty.
            if (request == null) { root.schedule.Execute(Close).StartingIn(10); return; }
            ZS.Attach(root);
            root.AddToClassList("zs-picker__root");
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);

            top = new VisualElement(); top.AddToClassList("zs-picker__top");
            root.Add(top);
            top.Add(SearchRow());
            top.Add(FilterRow());

            var body = new VisualElement(); body.AddToClassList("zs-picker__body");
            root.Add(body);
            var listHost = new VisualElement(); listHost.AddToClassList("zs-picker__list-host");
            // Dynamic heights: a group header is a short line while the rows under it are the view's height.
            list = new ListView { virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight, selectionType = SelectionType.None, showBorder = false, reorderable = false, horizontalScrollingEnabled = false };
            list.AddToClassList("zs-picker__list");
            list.makeItem = MakeRow;
            list.bindItem = BindRow;
            list.itemsSource = rows;
            list.RegisterCallback<GeometryChangedEvent>(_ => { float w = list.resolvedStyle.width; if (Mathf.Abs(w - lastWidth) > 1f) { lastWidth = w; if (memory.view == ViewGrid) Refresh(false); } });
            listHost.Add(list);
            body.Add(listHost);

            details = new VisualElement(); details.AddToClassList("zs-picker__details");
            body.Add(details);
            details.style.display = memory.detailsShown ? DisplayStyle.Flex : DisplayStyle.None;

            root.Add(BottomRow());

            Refresh(true);
            root.schedule.Execute(() => { search?.Focus(); search?.SelectAll(); }).StartingIn(30);
        }

        VisualElement SearchRow() {
            var r = new VisualElement(); r.AddToClassList("zs-picker__row");
            search = new TextField { value = memory.search ?? "", tooltip = "Type to find: name, folder, tags and file name. Letters may be skipped (\"ftstp\" finds \"footstep\"); several words must all match." };
            search.AddToClassList("zs-search"); search.AddToClassList("zs-picker__search");
            var ghost = new Label("Search name, folder, tags…") { pickingMode = PickingMode.Ignore };
            ghost.AddToClassList("zs-search__ghost");
            ghost.style.display = string.IsNullOrEmpty(search.value) ? DisplayStyle.Flex : DisplayStyle.None;
            search.Add(ghost);
            search.RegisterValueChangedCallback(e => { ghost.style.display = string.IsNullOrEmpty(e.newValue) ? DisplayStyle.Flex : DisplayStyle.None; memory.search = e.newValue; Refresh(true); });
            r.Add(search);
            var clear = ZS.Button("×", "Clear the search.", "RichButton", () => search.value = "", ZUICornerMask.All, 22f, 26f);
            clear.AddToClassList("zs-picker__clear");
            r.Add(clear);
            count = new Label { tooltip = "How many of the listing match the search and filters." };
            count.AddToClassList("zs-lbl"); count.AddToClassList("zs-picker__count");
            r.Add(count);
            var audition = ZS.Toggle("Play on select", "On: selecting a row plays it at once. Off: play with the row's ▶ or Space.", memory.auditionOnSelect, v => { memory.auditionOnSelect = v; memory.Save(request.stateKey); }, "RichToggle", ZUICornerMask.All, 100f, 26f);
            audition.AddToClassList("zs-picker__flag");
            r.Add(audition);
            var det = ZS.Toggle("Details", "Show or hide the details pane (waveform, format, place, who uses it).", memory.detailsShown, v => { memory.detailsShown = v; details.style.display = v ? DisplayStyle.Flex : DisplayStyle.None; memory.Save(request.stateKey); }, "RichToggle", ZUICornerMask.All, 70f, 26f);
            det.AddToClassList("zs-picker__flag");
            r.Add(det);
            r.Add(Gap());
            r.Add(Lbl("View", "How dense the list is."));
            viewSeg = Z.Segmented(Mathf.Clamp(memory.view, 0, 2), ViewNames, "Compact: one line per item, no waveform. List: a row with a waveform. Grid: tiles, as many across as fit.", i => { memory.view = i; Refresh(true); });
            viewSeg.AddToClassList("zs-picker__seg");
            r.Add(viewSeg);
            return r;
        }

        VisualElement FilterRow() {
            var r = new VisualElement(); r.AddToClassList("zs-picker__row"); r.AddToClassList("zs-picker__filters");
            bool clips = request.listing == ZoundPickerRequest.Listing.Clips;
            string[] kinds = clips ? new[] { "All", "Library", "Sources" } : new[] { "All", "Klips", "Zequences" };
            r.Add(Lbl("Show", "Which part of the listing to show."));
            kindSeg = Z.Segmented(Mathf.Clamp(memory.kindFilter, 0, 2), kinds, clips ? "All clips, only the library's, or only the sources folder's." : "All sounds, only Klips, or only Zequences.", i => { memory.kindFilter = i; Refresh(true); });
            kindSeg.AddToClassList("zs-picker__seg");
            r.Add(kindSeg);
            var fav = ZS.Toggle("★ Favourites", "Only the items you starred.", memory.onlyFavourites, v => { memory.onlyFavourites = v; Refresh(true); }, "RichToggle", ZUICornerMask.Left, 96f, 20f);
            var rec = ZS.Toggle("Recent", "Only items picked here before, newest first.", memory.onlyRecent, v => { memory.onlyRecent = v; Refresh(true); }, "RichToggle", ZUICornerMask.None, 64f, 20f);
            var unused = ZS.Toggle("Unused", clips ? "Only clips no Klip plays yet." : "Only sounds no Zequence plays yet.", memory.onlyUnused, v => { memory.onlyUnused = v; Refresh(true); }, "RichToggle", ZUICornerMask.Right, 64f, 20f);
            var flags = new VisualElement(); flags.AddToClassList("zs-picker__flags");
            flags.Add(fav); flags.Add(rec); flags.Add(unused);
            r.Add(flags);
            r.Add(Gap());
            r.Add(Lbl("Sort", "The order of the list. With a search typed, the best matches come first within it."));
            sortSeg = Z.Segmented(Mathf.Clamp(memory.sort, 0, SortNames.Length - 1), SortNames, "By name; most recently picked here first; most used in the project first; longest first; by folder then name.", i => { memory.sort = i; Refresh(true); });
            sortSeg.AddToClassList("zs-picker__seg");
            r.Add(sortSeg);
            r.Add(Gap());
            r.Add(Lbl("Group", "Headers between groups of the list."));
            groupSeg = Z.Segmented(Mathf.Clamp(memory.group, 0, GroupNames.Length - 1), GroupNames, "No headers; a header per folder; per tag (an item with several tags is listed under each); per kind.", i => { memory.group = i; Refresh(true); });
            groupSeg.AddToClassList("zs-picker__seg");
            r.Add(groupSeg);
            return r;
        }

        /// <summary>The four mode controls always show the mode the list is in, whatever set it (a click, remembered state, code).</summary>
        void SyncModeControls() {
            kindSeg?.SetOn(i => i == memory.kindFilter);
            sortSeg?.SetOn(i => i == memory.sort);
            groupSeg?.SetOn(i => i == memory.group);
            viewSeg?.SetOn(i => i == memory.view);
        }

        VisualElement BottomRow() {
            var r = new VisualElement(); r.AddToClassList("zs-picker__row"); r.AddToClassList("zs-picker__bottom");
            foreach (var a in request.actions) {
                var act = a;
                var b = ZS.Button(act.label, act.tooltip, "RichButton", () => { if (act.closes) Close(); act.run?.Invoke(); }, ZUICornerMask.All, -1f, 22f);
                b.AddToClassList("zs-picker__action");
                r.Add(b);
            }
            r.Add(Gap(true));
            var cancel = ZS.Button("Cancel", "Close without picking (Esc).", "RichButton", Close, ZUICornerMask.Left, 70f, 22f);
            r.Add(cancel);
            pick = ZS.Button(request.pickLabel, "Use the selected item(s) (Enter, or double-click a row).", "RichButton", PickSelected, ZUICornerMask.Right, 80f, 22f);
            pick.AddToClassList("zs-picker__pick");
            r.Add(pick);
            selectedCount = new Label { tooltip = "How many rows are selected. Ctrl-click adds one, Shift-click a range, Ctrl+A all." };
            selectedCount.AddToClassList("zs-lbl"); selectedCount.AddToClassList("zs-picker__selected");
            r.Add(selectedCount);
            return r;
        }

        static Label Lbl(string text, string tip) { var l = new Label(text) { tooltip = tip }; l.AddToClassList("zs-lbl"); l.AddToClassList("zs-picker__label"); return l; }
        static VisualElement Gap(bool grow = false) { var e = new VisualElement(); e.AddToClassList(grow ? "zs-picker__grow" : "zs-picker__gap"); return e; }

        float RowHeight() => memory.view == ViewGrid ? TileH : memory.view == ViewCompact ? RowCompact : RowList;

        // ─────────────────────────── the listing ───────────────────────────

        /// <summary>Filters, sorts and groups the request's items into the rows the list shows. Keeps the scroll where it was.</summary>
        void Refresh(bool contentChanged) {
            if (list == null) return;
            var sv = list.Q<ScrollView>();
            var keep = sv != null ? sv.scrollOffset : Vector2.zero;
            bool clips = request.listing == ZoundPickerRequest.Listing.Clips;
            string q = (search != null ? search.value : memory.search ?? "").Trim().ToLowerInvariant();
            string cursorKey = cursor >= 0 && cursor < shown.Count ? shown[cursor].key : null;
            shown.Clear();
            var scores = new Dictionary<ZoundPickerItem, int>();
            foreach (var it in request.items) {
                if (memory.kindFilter == 1 && (clips ? it.folder.StartsWith("Sources/") || it.folder.StartsWith("Work/") : it.kind != ZoundPickerItem.Kind.Klip)) continue;
                if (memory.kindFilter == 2 && (clips ? !it.folder.StartsWith("Sources/") : it.kind != ZoundPickerItem.Kind.Zequence)) continue;
                if (memory.onlyFavourites && !memory.IsFavourite(it.key)) continue;
                it.lastPicked = memory.RecentTicks(it.key);
                if (memory.onlyRecent && it.lastPicked == 0) continue;
                if (memory.onlyUnused && it.usedBy.Count > 0) continue;
                int s = ZoundPickerFuzzy.Score(q, it);
                if (s <= 0) continue;
                scores[it] = s;
                shown.Add(it);
            }
            Comparison<ZoundPickerItem> by;
            switch (memory.sort) {
                case 1: by = (a, b) => b.lastPicked != a.lastPicked ? b.lastPicked.CompareTo(a.lastPicked) : Name(a, b); break;
                case 2: by = (a, b) => b.usedBy.Count != a.usedBy.Count ? b.usedBy.Count.CompareTo(a.usedBy.Count) : Name(a, b); break;
                case 3: by = (a, b) => b.lengthSeconds != a.lengthSeconds ? b.lengthSeconds.CompareTo(a.lengthSeconds) : Name(a, b); break;
                case 4: by = (a, b) => { int c = string.Compare(a.folder, b.folder, StringComparison.OrdinalIgnoreCase); return c != 0 ? c : Name(a, b); }; break;
                default: by = Name; break;
            }
            if (q.Length > 0) {
                var inner = by;
                by = (a, b) => { int c = scores[b].CompareTo(scores[a]); return c != 0 ? c : inner(a, b); };
            }
            if (request.alreadyPicked != null && request.alreadyPicked.Count > 0) {
                var inner = by;
                by = (a, b) => { bool pa = request.alreadyPicked.Contains(a.key), pb = request.alreadyPicked.Contains(b.key); return pa != pb ? (pa ? -1 : 1) : inner(a, b); };
            }
            shown.Sort(by);

            rows.Clear();
            columns = memory.view == ViewGrid ? Mathf.Max(1, Mathf.FloorToInt((Mathf.Max(lastWidth, 300f) - 12f) / TileW)) : 1;
            if (memory.group == 0) AddRows(shown, null);
            else {
                var groups = new List<(string name, List<ZoundPickerItem> items)>();
                var index = new Dictionary<string, List<ZoundPickerItem>>();
                void Put(string g, ZoundPickerItem it) { if (!index.TryGetValue(g, out var l)) { l = new List<ZoundPickerItem>(); index[g] = l; groups.Add((g, l)); } l.Add(it); }
                foreach (var it in shown) {
                    if (memory.group == 1) Put(string.IsNullOrEmpty(it.folder) ? (clips ? "Library" : "") : it.folder.TrimEnd('/'), it);
                    else if (memory.group == 2) { if (it.tags.Length == 0) Put("Untagged", it); else foreach (var t in it.tags) Put(t, it); }
                    else Put(it.kind == ZoundPickerItem.Kind.Clip ? "Clips" : it.kind == ZoundPickerItem.Kind.Klip ? "Klips" : "Zequences", it);
                }
                groups.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                foreach (var g in groups) { rows.Add(new Row { header = g.name, headerCount = g.items.Count }); AddRows(g.items, g.name); }
            }
            if (contentChanged) {
                selected.RemoveWhere(k => shown.Find(i => i.key == k) == null);
                // The cursor follows its item through a re-sort or re-filter; it is only lost when the item is.
                cursor = cursorKey != null ? shown.FindIndex(i => i.key == cursorKey) : -1;
                if (cursor < 0) anchor = -1;
            }
            count.text = shown.Count == request.items.Count ? shown.Count + " items" : shown.Count + " of " + request.items.Count;
            SyncModeControls();
            list.itemsSource = rows;
            list.Rebuild();
            if (sv != null) list.schedule.Execute(() => { var s2 = list.Q<ScrollView>(); if (s2 != null) s2.scrollOffset = keep; });
            SyncSelection();
            ShowDetails(cursor >= 0 && cursor < shown.Count ? shown[cursor] : null);
        }

        static int Name(ZoundPickerItem a, ZoundPickerItem b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);

        void AddRows(List<ZoundPickerItem> items, string group) {
            if (memory.view != ViewGrid) { foreach (var it in items) rows.Add(new Row { item = it }); return; }
            for (int i = 0; i < items.Count; i += columns) {
                var tiles = new List<ZoundPickerItem>(columns);
                for (int k = i; k < items.Count && k < i + columns; k++) tiles.Add(items[k]);
                rows.Add(new Row { tiles = tiles });
            }
        }

        // ─────────────────────────── rows ───────────────────────────

        VisualElement MakeRow() {
            var e = new VisualElement(); e.AddToClassList("zs-picker__rowhost");
            var header = new Label(); header.AddToClassList("zs-lbl"); header.AddToClassList("zs-picker__header"); header.style.display = DisplayStyle.None;
            e.Add(header);
            var item = BuildItemRow(); item.style.display = DisplayStyle.None;
            e.Add(item);
            var tiles = new VisualElement(); tiles.AddToClassList("zs-picker__tiles"); tiles.style.display = DisplayStyle.None;
            e.Add(tiles);
            return e;
        }

        void BindRow(VisualElement e, int i) {
            var row = rows[i];
            var header = e[0]; var item = e[1]; var tiles = e[2];
            header.style.display = row.header != null ? DisplayStyle.Flex : DisplayStyle.None;
            item.style.display = row.item != null ? DisplayStyle.Flex : DisplayStyle.None;
            tiles.style.display = row.tiles != null ? DisplayStyle.Flex : DisplayStyle.None;
            e.style.height = row.header != null ? HeaderH + 2f : RowHeight();
            if (row.header != null) ((Label)header).text = row.header + "  ·  " + row.headerCount;
            else if (row.item != null) BindItemRow(item, row.item);
            else {
                while (tiles.childCount < row.tiles.Count) tiles.Add(BuildTile());
                for (int k = 0; k < tiles.childCount; k++) {
                    var t = tiles[k];
                    bool on = k < row.tiles.Count;
                    t.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                    if (on) BindTile(t, row.tiles[k]);
                }
            }
        }

        sealed class ItemParts { public ZoundPickerItem item; public Label star, name, kind, len, meta, used, picked; public ZoundPickerThumbTK thumb; public Button play; }

        VisualElement BuildItemRow() {
            var r = new VisualElement(); r.AddToClassList("zs-picker__item");
            var p = new ItemParts();
            p.star = new Label("☆"); p.star.AddToClassList("zs-lbl"); p.star.AddToClassList("zs-picker__star");
            p.star.RegisterCallback<ClickEvent>(e => { if (p.item == null) return; memory.ToggleFavourite(p.item.key); memory.Save(request.stateKey); SetStar(p.star, p.item); if (memory.onlyFavourites) Refresh(true); e.StopPropagation(); });
            r.Add(p.star);
            p.picked = new Label("✓") { tooltip = "Already a track of this Zequence." }; p.picked.AddToClassList("zs-lbl"); p.picked.AddToClassList("zs-picker__picked");
            r.Add(p.picked);
            p.thumb = new ZoundPickerThumbTK();
            r.Add(p.thumb);
            p.name = new Label(); p.name.AddToClassList("zs-lbl"); p.name.AddToClassList("zs-picker__name");
            r.Add(p.name);
            p.kind = new Label(); p.kind.AddToClassList("zs-lbl"); p.kind.AddToClassList("zs-picker__kind");
            r.Add(p.kind);
            p.len = new Label(); p.len.AddToClassList("zs-lbl"); p.len.AddToClassList("zs-picker__len");
            r.Add(p.len);
            p.meta = new Label(); p.meta.AddToClassList("zs-lbl"); p.meta.AddToClassList("zs-picker__meta");
            r.Add(p.meta);
            p.used = new Label(); p.used.AddToClassList("zs-lbl"); p.used.AddToClassList("zs-picker__used");
            r.Add(p.used);
            p.play = ZS.Button("▶", "Play this (Space). Click again to stop.", "RichButton", () => { if (p.item != null) Audition(p.item); }, ZUICornerMask.All, 22f, 18f);
            p.play.AddToClassList("zs-picker__play");
            p.play.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            r.Add(p.play);
            r.userData = p;
            WireRow(r, () => p.item);
            return r;
        }

        void BindItemRow(VisualElement r, ZoundPickerItem it) {
            var p = (ItemParts)r.userData;
            p.item = it;
            SetStar(p.star, it);
            p.picked.style.display = request.alreadyPicked != null && request.alreadyPicked.Contains(it.key) ? DisplayStyle.Flex : DisplayStyle.None;
            bool compact = memory.view == ViewCompact;
            p.thumb.style.display = compact ? DisplayStyle.None : DisplayStyle.Flex;
            if (!compact) p.thumb.Bind(it);
            p.name.text = it.name;
            p.name.tooltip = string.IsNullOrEmpty(it.assetPath) ? it.name : it.assetPath;
            p.kind.text = it.KindLabel;
            p.kind.EnableInClassList("zs-picker__kind--zeq", it.kind == ZoundPickerItem.Kind.Zequence);
            p.kind.EnableInClassList("zs-picker__kind--klip", it.kind == ZoundPickerItem.Kind.Klip);
            p.len.text = it.LengthText;
            string meta = it.kind == ZoundPickerItem.Kind.Clip ? (string.IsNullOrEmpty(it.folder) ? "Library" : it.folder.TrimEnd('/')) : string.Join(", ", it.tags);
            if (it.missing) meta = "missing · " + meta;
            p.meta.text = meta; p.meta.tooltip = it.kind == ZoundPickerItem.Kind.Clip ? "Folder" : "Tags";
            p.meta.style.display = compact ? DisplayStyle.None : DisplayStyle.Flex;
            p.used.text = it.usedBy.Count > 0 ? "×" + it.usedBy.Count : "";
            p.used.tooltip = it.usedBy.Count > 0 ? "Used by: " + string.Join(", ", it.usedBy) : "";
            p.play.text = playingKey == it.key ? "■" : "▶";
            p.play.SetEnabled(!it.missing && (it.clip != null || it.zound != null));
            r.EnableInClassList("zs-picker__item--selected", selected.Contains(it.key));
            r.EnableInClassList("zs-picker__item--cursor", cursor >= 0 && cursor < shown.Count && ReferenceEquals(shown[cursor], it));
            r.EnableInClassList("zs-picker__item--missing", it.missing);
            r.style.height = RowHeight() - 2f;
        }

        void SetStar(Label star, ZoundPickerItem it) {
            bool on = memory.IsFavourite(it.key);
            star.text = on ? "★" : "☆";
            star.tooltip = on ? "A favourite. Click to remove the star." : "Click to star it as a favourite.";
            star.EnableInClassList("zs-picker__star--on", on);
        }

        VisualElement BuildTile() {
            var t = new VisualElement(); t.AddToClassList("zs-picker__tile");
            var p = new ItemParts();
            p.thumb = new ZoundPickerThumbTK(); p.thumb.AddToClassList("zs-picker__tile-thumb");
            t.Add(p.thumb);
            var over = new VisualElement(); over.AddToClassList("zs-picker__tile-over");
            p.star = new Label("☆"); p.star.AddToClassList("zs-lbl"); p.star.AddToClassList("zs-picker__star");
            p.star.RegisterCallback<ClickEvent>(e => { if (p.item == null) return; memory.ToggleFavourite(p.item.key); memory.Save(request.stateKey); SetStar(p.star, p.item); if (memory.onlyFavourites) Refresh(true); e.StopPropagation(); });
            over.Add(p.star);
            p.len = new Label(); p.len.AddToClassList("zs-lbl"); p.len.AddToClassList("zs-picker__tile-len");
            over.Add(p.len);
            p.play = ZS.Button("▶", "Play this (Space). Click again to stop.", "RichButton", () => { if (p.item != null) Audition(p.item); }, ZUICornerMask.All, 20f, 16f);
            p.play.AddToClassList("zs-picker__play");
            p.play.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            over.Add(p.play);
            t.Add(over);
            p.name = new Label(); p.name.AddToClassList("zs-lbl"); p.name.AddToClassList("zs-picker__tile-name");
            t.Add(p.name);
            p.meta = new Label(); p.meta.AddToClassList("zs-lbl"); p.meta.AddToClassList("zs-picker__tile-meta");
            t.Add(p.meta);
            t.userData = p;
            WireRow(t, () => p.item);
            return t;
        }

        void BindTile(VisualElement t, ZoundPickerItem it) {
            var p = (ItemParts)t.userData;
            p.item = it;
            p.thumb.Bind(it);
            SetStar(p.star, it);
            p.len.text = it.LengthText;
            p.name.text = it.name; p.name.tooltip = string.IsNullOrEmpty(it.assetPath) ? it.name : it.assetPath;
            string meta = it.KindLabel + (it.kind == ZoundPickerItem.Kind.Clip ? " · " + (string.IsNullOrEmpty(it.folder) ? "Library" : it.folder.TrimEnd('/')) : it.tags.Length > 0 ? " · " + string.Join(", ", it.tags) : "");
            if (it.usedBy.Count > 0) meta += " · ×" + it.usedBy.Count;
            p.meta.text = it.missing ? "missing · " + meta : meta;
            p.meta.tooltip = it.usedBy.Count > 0 ? "Used by: " + string.Join(", ", it.usedBy) : "";
            p.play.text = playingKey == it.key ? "■" : "▶";
            p.play.SetEnabled(!it.missing && (it.clip != null || it.zound != null));
            t.EnableInClassList("zs-picker__tile--selected", selected.Contains(it.key));
            t.EnableInClassList("zs-picker__tile--cursor", cursor >= 0 && cursor < shown.Count && ReferenceEquals(shown[cursor], it));
            t.EnableInClassList("zs-picker__item--missing", it.missing);
            t.style.width = TileW - 6f;
        }

        /// <summary>Click selects (Ctrl adds, Shift ranges), double-click picks, a drag carries the selection out.</summary>
        void WireRow(VisualElement r, Func<ZoundPickerItem> itemOf) {
            Vector2 downAt = Vector2.zero; bool dragging = false; int pressedButton = -1;
            r.RegisterCallback<PointerDownEvent>(e => {
                var it = itemOf(); if (it == null) return;
                pressedButton = e.button; downAt = e.position; dragging = false;
                if (e.button != 0) return;
                rootVisualElement.Focus();
                int idx = shown.IndexOf(it);
                if (e.clickCount == 2) { if (!selected.Contains(it.key)) { selected.Clear(); selected.Add(it.key); } PickSelected(); e.StopPropagation(); return; }
                if (e.shiftKey && request.multi && anchor >= 0) { selected.Clear(); for (int k = Mathf.Min(anchor, idx); k <= Mathf.Max(anchor, idx); k++) selected.Add(shown[k].key); }
                else if (e.ctrlKey && request.multi) { if (!selected.Remove(it.key)) selected.Add(it.key); anchor = idx; }
                else if (!selected.Contains(it.key) || selected.Count > 1) { selected.Clear(); selected.Add(it.key); anchor = idx; }
                else anchor = idx;
                cursor = idx;
                SyncSelection();
                ShowDetails(it);
                if (memory.auditionOnSelect && playingKey != it.key) Audition(it);
            });
            r.RegisterCallback<PointerMoveEvent>(e => {
                if (pressedButton != 0 || dragging || (e.pressedButtons & 1) == 0) return;
                if ((e.position - (Vector3)downAt).sqrMagnitude < 36f) return;
                var it = itemOf(); if (it == null) return;
                dragging = true;
                if (!selected.Contains(it.key)) { selected.Clear(); selected.Add(it.key); SyncSelection(); }
                var items = SelectedItems();
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(DragKey, items);
                var objs = new List<UnityEngine.Object>();
                foreach (var s in items) if (s.clip != null) objs.Add(s.clip);
                DragAndDrop.objectReferences = objs.ToArray();
                DragAndDrop.StartDrag(items.Count == 1 ? items[0].name : items.Count + " items");
            });
            r.RegisterCallback<PointerUpEvent>(_ => { pressedButton = -1; dragging = false; });
        }

        /// <summary>The key a drop target reads the dragged items under (a List of <see cref="ZoundPickerItem"/>).</summary>
        public const string DragKey = "ZoundPicker.Items";

        void SyncSelection() {
            if (list == null) return;
            list.RefreshItems();
            selectedCount.text = selected.Count == 0 ? "" : selected.Count + " selected";
            pick.SetEnabled(selected.Count > 0);
            pick.text = selected.Count > 1 ? request.pickLabel + " " + selected.Count : request.pickLabel;
        }

        List<ZoundPickerItem> SelectedItems() {
            var l = new List<ZoundPickerItem>();
            foreach (var it in shown) if (selected.Contains(it.key)) l.Add(it);
            return l;
        }

        // ─────────────────────────── details ───────────────────────────

        void ShowDetails(ZoundPickerItem it) {
            if (details == null) return;
            details.Clear();
            if (it == null) {
                var none = new Label(shown.Count == 0 ? (request.items.Count == 0 ? "Nothing to list yet." : "Nothing matches.") : "Select a row to see it here.");
                none.AddToClassList("zs-lbl"); none.AddToClassList("zs-subtle"); none.AddToClassList("zs-picker__details-none");
                details.Add(none);
                if (request.items.Count == 0 && request.actions.Count > 0) {
                    var hint = new Label("Use the buttons below to import or create one.");
                    hint.AddToClassList("zs-lbl"); hint.AddToClassList("zs-subtle"); hint.AddToClassList("zs-picker__details-none");
                    details.Add(hint);
                }
                return;
            }
            var name = new Label(it.name) { tooltip = it.name }; name.AddToClassList("zs-lbl"); name.AddToClassList("zs-bold"); name.AddToClassList("zs-picker__details-name");
            details.Add(name);
            if (it.clip != null) { var big = new ZoundPickerThumbTK(); big.AddToClassList("zs-picker__details-wave"); big.Bind(it); details.Add(big); }
            void Line(string label, string value, string tip = null) {
                if (string.IsNullOrEmpty(value)) return;
                var row = new VisualElement(); row.AddToClassList("zs-picker__details-line");
                var l = new Label(label); l.AddToClassList("zs-lbl"); l.AddToClassList("zs-picker__details-label");
                var v = new Label(value) { tooltip = tip ?? value }; v.AddToClassList("zs-lbl"); v.AddToClassList("zs-picker__details-value");
                row.Add(l); row.Add(v); details.Add(row);
            }
            Line("Kind", it.kind == ZoundPickerItem.Kind.Clip ? "Audio clip" : it.kind == ZoundPickerItem.Kind.Klip ? "Klip" : "Zequence");
            Line("Length", it.LengthText + (it.kind == ZoundPickerItem.Kind.Klip ? " (trimmed)" : ""));
            Line("Format", it.FormatText);
            Line("Where", it.kind == ZoundPickerItem.Kind.Clip ? (string.IsNullOrEmpty(it.folder) ? "Library" : it.folder.TrimEnd('/')) : it.folder, it.assetPath);
            if (!string.IsNullOrEmpty(it.assetPath)) Line("File", System.IO.Path.GetFileName(it.assetPath), it.assetPath);
            if (it.tags.Length > 0) Line("Tags", string.Join(", ", it.tags));
            Line("Used by", it.usedBy.Count == 0 ? "nothing yet" : string.Join(", ", it.usedBy));
            if (it.missing) Line("Problem", "The audio file is not on this machine.");
            var acts = new VisualElement(); acts.AddToClassList("zs-picker__details-actions");
            var play = ZS.Button(playingKey == it.key ? "■ Stop" : "▶ Play", "Play this (Space). Click again to stop.", "RichButton", () => Audition(it), ZUICornerMask.Left, 70f, 20f);
            play.SetEnabled(!it.missing && (it.clip != null || it.zound != null));
            acts.Add(play);
            if (it.clip != null) acts.Add(ZS.Button("Show file", "Highlights the clip in the Project window.", "RichButton", () => EditorGUIUtility.PingObject(it.clip), ZUICornerMask.None, 70f, 20f));
            if (it.zound is Klip k) acts.Add(ZS.Button("Open…", "Opens this Klip's editor.", "RichButton", () => KlipEditorWindowTK.Open(k, false), ZUICornerMask.None, 60f, 20f));
            else if (it.zound is Zequence z) acts.Add(ZS.Button("Open…", "Opens this Zequence's editor.", "RichButton", () => ZequenceEditorWindowTK.Open(z, false), ZUICornerMask.None, 60f, 20f));
            bool fav = memory.IsFavourite(it.key);
            acts.Add(ZS.Button(fav ? "★ Starred" : "☆ Star", fav ? "A favourite. Click to remove the star." : "Star it as a favourite.", "RichButton", () => { memory.ToggleFavourite(it.key); memory.Save(request.stateKey); Refresh(memory.onlyFavourites); ShowDetails(it); }, ZUICornerMask.Right, 76f, 20f));
            details.Add(acts);
        }

        // ─────────────────────────── audition ───────────────────────────

        void Audition(ZoundPickerItem it) {
            if (it == null) return;
            if (playingKey == it.key) { StopAudition(); SyncSelection(); ShowDetails(it); return; }
            StopAudition();
            if (it.clip != null && it.kind == ZoundPickerItem.Kind.Clip) AudioPreviewUtility.PlayPreviewClip(it.clip, this, request.previewOwner);
            else if (it.zound != null) ZoundPreviewPlayback.Play(request.previewOwner != null ? request.previewOwner : this, it.zound, control: it.key, toggle: false, secondaryOwner: this);
            else return;
            playingKey = it.key;
            SyncSelection();
            ShowDetails(it);
            // A clip's preview ends on its own; forget it then so the button reads ▶ again.
            if (it.clip != null && it.kind == ZoundPickerItem.Kind.Clip) rootVisualElement.schedule.Execute(() => { if (playingKey == it.key) { playingKey = null; SyncSelection(); } }).StartingIn((long)(it.lengthSeconds * 1000f) + 100);
        }

        void StopAudition() {
            if (playingKey == null) return;
            AudioPreviewUtility.StopPreviewClip(this);
            try { ZoundPreviewPlayback.StopControl(request.previewOwner != null ? request.previewOwner : this, playingKey); ZoundPreviewPlayback.StopControl(this, playingKey); } catch { }
            playingKey = null;
        }

        // ─────────────────────────── keys and picking ───────────────────────────

        void OnKey(KeyDownEvent e) {
            bool typing = search != null && (search.focusController?.focusedElement is VisualElement f) && (f == search || search.Contains(f));
            switch (e.keyCode) {
                case KeyCode.Escape: Close(); e.StopPropagation(); return;
                case KeyCode.Return: case KeyCode.KeypadEnter:
                    if (selected.Count == 0 && cursor >= 0 && cursor < shown.Count) selected.Add(shown[cursor].key);
                    if (selected.Count == 0 && shown.Count > 0) { cursor = 0; selected.Add(shown[0].key); }
                    PickSelected(); e.StopPropagation(); return;
                case KeyCode.DownArrow: Move(memory.view == ViewGrid ? columns : 1, e.shiftKey); e.StopPropagation(); return;
                case KeyCode.UpArrow: Move(memory.view == ViewGrid ? -columns : -1, e.shiftKey); e.StopPropagation(); return;
                case KeyCode.RightArrow: if (memory.view == ViewGrid && !typing) { Move(1, e.shiftKey); e.StopPropagation(); } return;
                case KeyCode.LeftArrow: if (memory.view == ViewGrid && !typing) { Move(-1, e.shiftKey); e.StopPropagation(); } return;
                case KeyCode.PageDown: Move(10, e.shiftKey); e.StopPropagation(); return;
                case KeyCode.PageUp: Move(-10, e.shiftKey); e.StopPropagation(); return;
                case KeyCode.Home: if (!typing) { cursor = -1; Move(1, false); e.StopPropagation(); } return;
                case KeyCode.End: if (!typing) { cursor = shown.Count; Move(-1, false); e.StopPropagation(); } return;
                case KeyCode.Space: if (!typing && cursor >= 0 && cursor < shown.Count) { Audition(shown[cursor]); e.StopPropagation(); } return;
                case KeyCode.A: if (e.ctrlKey && request.multi) { selected.Clear(); foreach (var it in shown) selected.Add(it.key); SyncSelection(); e.StopPropagation(); } return;
                case KeyCode.F: if (e.ctrlKey) { search?.Focus(); search?.SelectAll(); e.StopPropagation(); } return;
            }
        }

        void Move(int delta, bool extend) {
            if (shown.Count == 0) return;
            int next = Mathf.Clamp(cursor < 0 ? (delta > 0 ? 0 : shown.Count - 1) : cursor + delta, 0, shown.Count - 1);
            if (extend && request.multi) { if (anchor < 0) anchor = cursor < 0 ? next : cursor; selected.Clear(); for (int k = Mathf.Min(anchor, next); k <= Mathf.Max(anchor, next); k++) selected.Add(shown[k].key); }
            else { selected.Clear(); selected.Add(shown[next].key); anchor = next; }
            cursor = next;
            SyncSelection();
            ShowDetails(shown[cursor]);
            ScrollToCursor();
            if (memory.auditionOnSelect) Audition(shown[cursor]);
        }

        void ScrollToCursor() {
            if (cursor < 0 || cursor >= shown.Count) return;
            var it = shown[cursor];
            for (int r = 0; r < rows.Count; r++) {
                var row = rows[r];
                if (ReferenceEquals(row.item, it) || (row.tiles != null && row.tiles.Contains(it))) { list.ScrollToItem(r); return; }
            }
        }

        void PickSelected() {
            var items = SelectedItems();
            if (items.Count == 0) return;
            if (!request.multi && items.Count > 1) items = new List<ZoundPickerItem> { items[0] };
            foreach (var it in items) memory.NotePicked(it.key);
            memory.Save(request.stateKey);
            StopAudition();
            var act = request.onPick;
            Close();
            act?.Invoke(items);
        }
    }
}
