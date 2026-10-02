using System.Collections.Generic;
using System.Text;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The UI Toolkit twin of the Zounds window's Monitor tab (T-0470): what the game triggered, newest first. The toolbar
    /// (Pause/Resume, Clear, Callers, the name filter, the playing/recorded count), then per sound a box holding its info
    /// line (frame, caller, and while it plays: state, instance count, progress and Stop) above the same row the Browser
    /// shows. The list, the live tokens and the filter are the old tab's own shared code.
    /// </summary>
    internal class MonitorTabTK : VisualElement, IZoundRowHost {

        const float FrameW = 56f, CallerMaxW = 320f, StateW = 60f, CountW = 32f, BarW = 140f, StopW = 50f;

        readonly ZoundsWindowTK win;
        static string s_filter = string.Empty;
        readonly HashSet<string> seenKeys = new HashSet<string>();
        readonly Dictionary<Zound, ZoundToken> liveTokens = new Dictionary<Zound, ZoundToken>();
        readonly Dictionary<Zound, int> liveCounts = new Dictionary<Zound, int>();
        readonly VisualElement box, listHost;
        readonly Label status, count;
        Button pause;
        ZuiToggleButton callers;
        string listSig;
        readonly List<ZoundListRowTK> rows = new List<ZoundListRowTK>();
        readonly List<System.Action> liveLines = new List<System.Action>();
        BrowserTab.ZoundListRowLayout rowLayout;

        public MonitorTabTK(ZoundsWindowTK win) {
            this.win = win;
            AddToClassList("zs-monitor__root");
            box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.AddToClassList("zs-monitor");
            Add(box);
            box.Add(Toolbar(out status));
            box.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));

            var head = new VisualElement();
            head.AddToClassList("zs-monitor__head");
            var recent = new Label("Recent");
            recent.AddToClassList("zs-boxtext");
            head.Add(recent);
            count = new Label();
            count.AddToClassList("zs-lbl"); count.AddToClassList("zs-mini"); count.AddToClassList("zs-monitor__count");
            head.Add(count);
            box.Add(head);

            listHost = new VisualElement();
            box.Add(listHost);
            BuildList();
        }

        // ─────────────────────────── toolbar ───────────────────────────

        VisualElement Toolbar(out Label statusLabel) {
            const float h = 30f;
            var r = new VisualElement();
            r.AddToClassList("zs-toolbar");
            r.AddToClassList("zs-monitor__toolbar-row"); r.AddToClassList("zs-monitor__toolbar-height");
            r.Add(ZequenceEditorWindowTK.Gap(5f));
            pause = ZS.Button("Pause", "", "Flat", () => { ZoundsRecentHistory.paused = !ZoundsRecentHistory.paused; SyncToolbar(); }, ZUICornerMask.All, 70f, h);
            r.Add(pause);
            r.Add(ZequenceEditorWindowTK.Gap(ZS.MediumSpacing));
            var clear = ZS.Button("Clear", "Clear the list. Does not stop anything currently playing.", "Flat", () => { ZoundsRecentHistory.ClearRecent(); BuildList(); },
                                  ZUICornerMask.All, -1f, h);
            clear.AddToClassList("zs-toolbarbutton");
            r.Add(clear);
            r.Add(ZequenceEditorWindowTK.Gap(ZS.MediumSpacing));
            callers = ZS.Toggle("Callers", "Record which game code triggered each Zound (a short stack walk per trigger). Turn off if triggers are very frequent and the cost matters.",
                ZoundsWindowProperties.Instance.captureCallers, v => {
                    var props = ZoundsWindowProperties.Instance;
                    Undo.RecordObject(props, "toggle Zounds Monitor caller capture");
                    props.captureCallers = v;
                    EditorUtility.SetDirty(props);
                }, "RichToggle", ZUICornerMask.None, 70f, h);
            r.Add(callers);
            r.Add(ZequenceEditorWindowTK.Gap(ZS.MediumSpacing));
            var filter = new TextField { value = s_filter, tooltip = "Filter the list by zound name." };
            filter.AddToClassList("zs-search"); filter.AddToClassList("zs-monitor__filter");
            filter.AddToClassList("zs-monitor__toolbar-filter"); filter.AddToClassList("zs-monitor__filter-height");
            var ghost = new Label("Filter by name...") { pickingMode = PickingMode.Ignore };
            ghost.AddToClassList("zs-search__ghost"); ghost.AddToClassList("zs-monitor__ghost");
            filter.Add(ghost);
            void Ghost() => ghost.style.display = string.IsNullOrEmpty(filter.value) && filter.focusController?.focusedElement != filter ? DisplayStyle.Flex : DisplayStyle.None;
            filter.RegisterValueChangedCallback(e => { s_filter = e.newValue; Ghost(); BuildList(); });
            filter.RegisterCallback<FocusInEvent>(_ => Ghost());
            filter.RegisterCallback<FocusOutEvent>(_ => Ghost());
            Ghost();
            r.Add(filter);
            r.Add(ZequenceEditorWindowTK.Flex());
            statusLabel = new Label();
            statusLabel.AddToClassList("zs-lbl");
            statusLabel.AddToClassList("zs-monitor__toolbar-status-label"); statusLabel.AddToClassList("zs-monitor__status-height");
            statusLabel.AddToClassList("zs-monitor__toolbar-status-label");
            r.Add(statusLabel);
            SyncToolbar();
            return r;
        }

        void SyncToolbar() {
            bool paused = ZoundsRecentHistory.paused;
            pause.text = paused ? "Resume" : "Pause";
            pause.tooltip = paused ? "Recording is paused — new game triggers are not added. Click to resume." : "Pause recording new game triggers (playback itself is unaffected).";
            callers.SetValueWithoutNotify(ZoundsWindowProperties.Instance.captureCallers);
            status.text = $"{MonitorTab.CountLiveTokens()} playing · {ZoundsRecentHistory.GetEntries().Count} recorded";
        }

        // ─────────────────────────── the list ───────────────────────────

        string Signature(List<ZoundsRecentHistory.Entry> filtered) {
            var sb = new StringBuilder();
            sb.Append(ZoundsRecentHistory.GetEntries().Count).Append('|');
            foreach (var e in filtered) sb.Append(e.name).Append('@').Append(e.frame).Append(';');
            var bs = ZoundsProject.Instance.browserSettings;
            sb.Append(bs.showVolume).Append(bs.showPitch).Append(bs.showChance).Append(bs.showNameField).Append(bs.showTags).Append(bs.tagsOnOwnRow)
              .Append(bs.showMute).Append(bs.showSolo).Append(bs.showOpenEditor).Append(bs.showConvertToZequence).Append(bs.showRouting)
              .Append(bs.showDuplicate).Append(bs.showRemove).Append(bs.vpcShowSliderType).Append(bs.vpcShowInputBoxes).Append(Application.isPlaying);
            return sb.ToString();
        }

        void BuildList() {
            listHost.Clear(); rows.Clear(); liveLines.Clear();
            var all = ZoundsRecentHistory.GetEntries();
            var filtered = MonitorTab.FilteredEntries(all, s_filter, seenKeys);
            listSig = Signature(filtered);
            count.text = $"({filtered.Count})";
            bool fill = all.Count > 0 && filtered.Count > 0;
            // The old tab's scroll view expands; with only a message the boxes end under it.
            style.flexGrow = fill ? 1 : 0; box.style.flexGrow = fill ? 1 : 0; listHost.style.flexGrow = fill ? 1 : 0;
            win.SetFill(fill);
            if (all.Count == 0) { listHost.Add(Info("Nothing recorded yet. Trigger a Zound from the game in Play mode; every game trigger appears here newest first.")); return; }
            if (filtered.Count == 0) { listHost.Add(Info("No entries match the filter.")); return; }

            MonitorTab.CollectLiveTokens(liveTokens, liveCounts);
            var candidates = new List<Zound>();
            var seen = new HashSet<Zound>();
            foreach (var e in filtered)
                if (!string.IsNullOrEmpty(e.name) && ZoundDictionary.TryGetZoundByName(e.name, out var z) && seen.Add(z)) candidates.Add(z);
            rowLayout = BrowserTab.PrepareListRowLayoutShared(candidates, ZS.ItemSpacing, ZS.MediumSpacing);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("zs-monitor__list-scroll");
            listHost.Add(scroll);
            for (int i = 0; i < filtered.Count; i++) {
                var entry = filtered[i];
                Zound zound = null;
                bool resolved = !string.IsNullOrEmpty(entry.name) && ZoundDictionary.TryGetZoundByName(entry.name, out zound);
                var card = new VisualElement();
                card.AddToClassList("zs-box-default"); card.AddToClassList("zs-monitor__card");
                card.Add(InfoLine(entry, resolved ? zound : null));
                if (resolved) {
                    var row = new ZoundListRowTK(zound, this, () => rowLayout);
                    rows.Add(row);
                    card.Add(row);
                }
                else {
                    var l = new Label((string.IsNullOrEmpty(entry.name) ? "(unknown)" : entry.name) + " — no longer in the library");
                    l.AddToClassList("zs-lbl");
                    l.AddToClassList("zs-monitor__list-label");
                    card.Add(l);
                }
                scroll.Add(card);
                if (i < filtered.Count - 1) scroll.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace * 0.5f));
            }
        }

        static VisualElement Info(string text) {
            var h = new HelpBox(text, HelpBoxMessageType.Info);
            h.AddToClassList("zs-infobox");
            return h;
        }

        /// <summary>DrawInfoLine: frame and caller on the left; on the right, at fixed widths whether or not it plays, the
        /// state, the instance count, the progress bar and Stop.</summary>
        VisualElement InfoLine(ZoundsRecentHistory.Entry entry, Zound zound) {
            var r = new VisualElement();
            r.AddToClassList("zs-monitor__info-line-row");
            var frame = Mini("#" + entry.frame); frame.AddToClassList("zs-monitor__info-line-frame"); r.Add(frame);
            string callerText = string.IsNullOrEmpty(entry.caller) ? "—" : entry.caller;
            var caller = Mini(callerText); caller.tooltip = callerText; caller.AddToClassList("zs-monitor__info-line-caller");
            r.Add(caller);
            r.Add(ZequenceEditorWindowTK.Flex());
            var live = new VisualElement();
            live.AddToClassList("zs-monitor__info-line-live");
            var state = Mini(""); state.AddToClassList("zs-monitor__info-line-state");
            var cnt = Mini(""); cnt.AddToClassList("zs-monitor__instance-count");
            var bar = new ProgressBar { lowValue = 0f, highValue = 1f };
            bar.AddToClassList("zs-progress");
            bar.AddToClassList("zs-monitor__info-line-bar");
            ZoundToken token = null;
            var stop = ZS.Button("Stop", "Stop this playing Zound now.", "Flat", () => {
                if (token != null) token.Kill(ZoundsProject.Instance.projectSettings.cullFadeDuration);
            }, ZUICornerMask.All, StopW, 18f);
            stop.AddToClassList("zs-toolbarbutton");
            live.Add(state); live.Add(cnt); live.Add(bar); live.Add(stop);
            r.Add(live);
            void Update() {
                token = null;
                if (zound != null) liveTokens.TryGetValue(zound, out token);
                bool on = token != null;
                foreach (var c in live.Children()) c.style.visibility = on ? Visibility.Visible : Visibility.Hidden;
                if (!on) return;
                state.text = token.state.ToString();
                int n = liveCounts.TryGetValue(token.zound, out int k) ? k : 1;
                cnt.text = n > 1 ? "×" + n : string.Empty;
                cnt.tooltip = n > 1 ? n + " instances of this Zound are playing." : string.Empty;
                float d = token.duration, t = token.time;
                // A Looper (T-0473) has no end to measure progress against; it says so rather than "∞s".
                bool loops = float.IsInfinity(d);
                bar.value = !loops && d > 0f ? Mathf.Clamp01(t / d) : 0f;
                bar.title = loops ? $"{t:0.0}s · looping" : $"{t:0.0} / {d:0.0}s";
            }
            liveLines.Add(Update);
            Update();
            return r;
        }

        static Label Mini(string text) {
            var l = new Label(text);
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-mini");
            l.AddToClassList("zs-monitor__mini-label");
            return l;
        }

        // ─────────────────────────── refresh ───────────────────────────

        public void Tick() {
            SyncToolbar();
            var filtered = MonitorTab.FilteredEntries(ZoundsRecentHistory.GetEntries(), s_filter, seenKeys);
            if (Signature(filtered) != listSig) BuildList();
            else foreach (var r in rows) r.Sync();
        }

        public void Live() {
            MonitorTab.CollectLiveTokens(liveTokens, liveCounts);
            foreach (var l in liveLines) l();
            foreach (var r in rows) r.Live();
        }

        public void OpenZoundEditor(Zound zound) => BrowserTabTK.OpenEditor(zound, this);
        public void ListChanged(Zound select = null) => BuildList();
    }
}
