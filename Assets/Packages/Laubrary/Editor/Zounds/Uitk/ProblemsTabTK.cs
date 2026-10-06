using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The Zounds window's Problems tab (T-0490): every request game code made that found nothing, one row per distinct
    /// problem however often it happened — a Zound name that does not exist, a ZPOC id no part of the play declares, a
    /// track number past the last track, a snapshot name nobody has, a project-wide ZPOC value no sound listens to. Each
    /// row says what was asked for, of which sound, how many times, and when it was last seen. Click a column heading to
    /// sort by it; click a sound's name to hear it; Open goes to that sound's editor, Forget drops the row (it comes back
    /// if it happens again). The list lives for the editor session: it is not saved, and nothing in it ever stopped a
    /// sound from playing, since a miss only ever does nothing.
    /// </summary>
    internal class ProblemsTabTK : VisualElement {

        const float RowH = 22f, KindW = 150f, ZoundW = 180f, DetailW = 150f, CountW = 60f, AgoW = 70f, BtnW = 56f;

        enum Sort { Newest, Kind, Sound, Times }
        static Sort s_sort = Sort.Newest;

        readonly VisualElement box, listHost;
        readonly ScrollView scroll;
        readonly Label summary;
        int builtRevision = -1;
        readonly List<(ZoundDiagnostics.Entry entry, Label count, Label ago)> live = new List<(ZoundDiagnostics.Entry, Label, Label)>();
        int builtCount = -1;
        Sort builtSort;

        const string SummaryTip = "Every request game code made this session that found nothing: a sound name, a ZPOC id, a track, a snapshot, " +
                                  "or a project-wide value no sound listens to. One row per problem, however often it happens; each is also " +
                                  "warned about once in the console. Nothing here ever stopped a sound: a miss only does nothing. Not saved.";

        readonly EditorWindow previewOwner;

        public ProblemsTabTK(EditorWindow previewOwner) {
            this.previewOwner = previewOwner;
            AddToClassList("zs-problems__root");
            box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.AddToClassList("zs-problems__box");
            Add(box);

            const float h = 30f;
            var bar = new VisualElement();
            bar.AddToClassList("zs-toolbar");
            bar.AddToClassList("zs-problems__bar"); bar.AddToClassList("zs-problems__toolbar-height");
            bar.Add(ZequenceEditorWindowTK.Gap(5f));
            var clear = ZS.Button("Clear", "Empties the list. Anything that happens again comes back, with its console warning.", "Flat",
                () => ZoundDiagnostics.Clear(), ZUICornerMask.All, 70f, h);
            clear.AddToClassList("zs-toolbarbutton");
            bar.Add(clear);
            bar.Add(ZequenceEditorWindowTK.Gap(ZS.MediumSpacing));
            summary = new Label { tooltip = SummaryTip };
            summary.AddToClassList("zs-lbl");
            summary.AddToClassList("zs-problems__summary");
            bar.Add(summary);
            box.Add(bar);
            box.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));

            // The heading stays put above the scrolling rows; a rebuild keeps the scroll position.
            listHost = new VisualElement();
            box.Add(listHost);
            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("zs-problems__scroll");
            box.Add(scroll);
            Build();
        }

        internal void Tick() {
            if (ZoundDiagnostics.Entries.Count != builtCount || s_sort != builtSort) { Build(); return; }
            if (ZoundDiagnostics.Revision == builtRevision) return;
            builtRevision = ZoundDiagnostics.Revision;
            if (s_sort == Sort.Times) { Build(); return; }   // counts moved, so the order may have
            float now = Time.realtimeSinceStartup;
            foreach (var l in live) { l.count.text = CountText(l.entry); l.ago.text = Ago(now - l.entry.lastSeen); }
        }

        void Build() {
            var offset = scroll.scrollOffset;
            listHost.Clear(); scroll.Clear(); live.Clear();
            var entries = ZoundDiagnostics.Entries;
            builtCount = entries.Count; builtRevision = ZoundDiagnostics.Revision; builtSort = s_sort;
            summary.text = entries.Count == 0 ? "No problems" : entries.Count + (entries.Count == 1 ? " problem" : " problems");
            if (entries.Count == 0) return;
            listHost.Add(Header());
            var order = new List<ZoundDiagnostics.Entry>(entries);
            order.Reverse();   // newest first; the sorts below are stable so ties stay newest first
            switch (s_sort) {
                case Sort.Kind: StableSort(order, (a, b) => KindText(a.kind).CompareTo(KindText(b.kind))); break;
                case Sort.Sound: StableSort(order, (a, b) => string.Compare(SoundText(a), SoundText(b), System.StringComparison.OrdinalIgnoreCase)); break;
                case Sort.Times: StableSort(order, (a, b) => b.count.CompareTo(a.count)); break;
            }
            float now = Time.realtimeSinceStartup;
            foreach (var e in order) scroll.Add(Row(e, now));
            scroll.schedule.Execute(() => scroll.scrollOffset = offset);
        }

        static void StableSort(List<ZoundDiagnostics.Entry> list, System.Comparison<ZoundDiagnostics.Entry> cmp) {
            for (int i = 1; i < list.Count; i++) {
                var x = list[i]; int j = i - 1;
                while (j >= 0 && cmp(list[j], x) > 0) { list[j + 1] = list[j]; j--; }
                list[j + 1] = x;
            }
        }

        VisualElement Header() {
            var r = RowBox();
            r.Add(Heading("Problem", KindW, "What kind of request found nothing. Click to sort by it.", Sort.Kind));
            r.Add(Heading("Sound", ZoundW, "The sound the request was made of. Click a name to hear it; click here to sort by it.", Sort.Sound));
            r.Add(Cell("Asked for", DetailW, "Exactly what game code asked for: the name, id, track number or snapshot name, as it was first spelled."));
            r.Add(Heading("Times", CountW, "How many times it happened. A request made every frame is still one row. Click to sort, most first.", Sort.Times));
            r.Add(Heading("Last", AgoW, "How long ago it last happened. Click to sort newest first (the usual order).", Sort.Newest));
            foreach (var c in r.Children()) c.AddToClassList("zs-mini");
            return r;
        }

        static Label Heading(string text, float w, string tip, Sort sort) {
            var l = Cell(s_sort == sort ? text + " ▾" : text, w, tip);
            l.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { s_sort = sort; e.StopPropagation(); } });
            return l;
        }

        static string SoundText(ZoundDiagnostics.Entry e) =>
            !string.IsNullOrEmpty(e.zound) ? e.zound : e.kind == ZoundDiagnostics.Kind.UndeclaredGlobalZpoc ? "(project-wide)" : "–";

        VisualElement Row(ZoundDiagnostics.Entry e, float now) {
            var r = RowBox();
            // A missing sound that has been created since (the browser's Missing list drops it then) says so, and opens.
            bool nowExists = e.kind == ZoundDiagnostics.Kind.MissingZound && ZoundsProject.isJSONLoaded && ZoundDictionary.TryGetZoundByName(e.detail, out _);
            string target = nowExists ? e.detail : e.zound;
            Zound targetZound = null;
            bool exists = ZoundsProject.isJSONLoaded && !string.IsNullOrEmpty(target)
                          && (nowExists || e.kind != ZoundDiagnostics.Kind.MissingZound)
                          && ZoundDictionary.TryGetZoundByName(target, out targetZound);

            r.Add(nowExists ? Cell("Sound added since", KindW, "Game code asked for this sound before it existed. A sound by that name exists now.")
                            : Cell(KindText(e.kind), KindW, KindHelp(e.kind)));
            var sound = Cell(SoundText(e), ZoundW, exists ? e.message + "\nClick to hear this sound; click again to stop it." : e.message);
            if (exists && !nowExists) {
                sound.AddToClassList("zs-problems__sound");
                sound.RegisterCallback<PointerDownEvent>(ev => { if (ev.button == 0) { ZoundBrowserPlaybackVisuals.PlayOrStopFromBrowser(targetZound, previewOwner); ev.StopPropagation(); } });
            }
            sound.schedule.Execute(() => { if (exists) { sound.tooltip = ZoundPreviewPlayback.Tooltip(previewOwner, targetZound, e.message + "\nClick to hear this sound."); sound.style.backgroundColor = ZoundPreviewPlayback.IsLoopPlaying(previewOwner, targetZound) ? new Color(.22f,.34f,.52f,1f) : StyleKeyword.Null; } }).Every(33);
            r.Add(sound);
            r.Add(Cell(e.shown ?? e.detail, DetailW, e.message));
            var count = Cell(CountText(e), CountW, "How many times this happened this session.");
            var ago = Cell(Ago(now - e.lastSeen), AgoW, "How long ago it last happened.");
            r.Add(count); r.Add(ago);
            var open = ZS.Button("Open", exists ? "Opens this sound's editor, to add what was asked for or correct the name game code uses."
                                                : "There is no sound by this name to open: the name game code used is the problem.",
                "RichButton", () => OpenZound(target), ZUICornerMask.All, BtnW, RowH - 2f);
            open.SetEnabled(exists);
            r.Add(open);
            r.Add(ZequenceEditorWindowTK.Gap(3f));
            r.Add(ZS.Button("Forget", "Drops this row. It comes back if it happens again.", "RichButton",
                () => ZoundDiagnostics.Remove(e), ZUICornerMask.All, BtnW, RowH - 2f));
            live.Add((e, count, ago));
            return r;
        }

        static void OpenZound(string name) {
            if (!ZoundDictionary.TryGetZoundByName(name, out var z)) return;
            bool local = z.parentId != 0;
            if (z is Klip k) KlipEditorWindowTK.Open(k, local);
            else if (z is Zequence q) ZequenceEditorWindowTK.Open(q, local);
        }

        static VisualElement RowBox() {
            var r = new VisualElement();
            r.AddToClassList("zs-problems__row-box-row");
            r.AddToClassList("zs-problems__row-box-row");
            return r;
        }

        static Label Cell(string text, float w, string tip) {
            var l = new Label(text) { tooltip = tip };
            l.AddToClassList("zs-lbl");
            l.style.width = w; l.AddToClassList("zs-problems__cell");
            l.AddToClassList("zs-problems__cell");
            return l;
        }

        static string CountText(ZoundDiagnostics.Entry e) => e.count >= 10000 ? (e.count / 1000) + "k" : e.count.ToString();

        static string Ago(float s) {
            if (s < 2f) return "now";
            if (s < 60f) return Mathf.FloorToInt(s) + " s ago";
            if (s < 3600f) return Mathf.FloorToInt(s / 60f) + " min ago";
            return Mathf.FloorToInt(s / 3600f) + " h ago";
        }

        static string KindText(ZoundDiagnostics.Kind k) {
            switch (k) {
                case ZoundDiagnostics.Kind.MissingZound: return "No such sound";
                case ZoundDiagnostics.Kind.MissingZpoc: return "No such ZPOC id";
                case ZoundDiagnostics.Kind.MissingTrack: return "No such track";
                case ZoundDiagnostics.Kind.MissingSnapshot: return "No such snapshot";
                case ZoundDiagnostics.Kind.UndeclaredGlobalZpoc: return "Nobody listens";
                default: return k.ToString();
            }
        }

        static string KindHelp(ZoundDiagnostics.Kind k) {
            switch (k) {
                case ZoundDiagnostics.Kind.MissingZound: return "Game code played a sound by a name no sound has. Nothing played.";
                case ZoundDiagnostics.Kind.MissingZpoc: return "Game code set a ZPOC value on a play whose sound, and everything it plays, declares no such id. The value is kept on the token but changes nothing.";
                case ZoundDiagnostics.Kind.MissingTrack: return "Game code asked a play for a track number past its last track. Nothing changed.";
                case ZoundDiagnostics.Kind.MissingSnapshot: return "Game code asked a play to glide to a snapshot name no sound in it has. Nothing changed.";
                case ZoundDiagnostics.Kind.UndeclaredGlobalZpoc: return "Game code set a project-wide ZPOC value for an id no sound in the project declares, so nothing hears it.";
                default: return "";
            }
        }
    }
}
