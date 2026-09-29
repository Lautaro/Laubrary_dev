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
    /// row says what was asked for, of which sound, how many times, and when it was last seen; Open goes to that sound's
    /// editor, Forget drops the row (it comes back if it happens again). The list lives for the editor session: it is not
    /// saved, and nothing in it ever stopped a sound from playing, since a miss only ever does nothing.
    /// </summary>
    internal class ProblemsTabTK : VisualElement {

        const float RowH = 22f, KindW = 150f, ZoundW = 180f, DetailW = 150f, CountW = 60f, AgoW = 70f, BtnW = 56f;

        readonly VisualElement box, listHost;
        readonly Label summary;
        int builtRevision = -1;
        readonly List<(ZoundDiagnostics.Entry entry, Label count, Label ago)> live = new List<(ZoundDiagnostics.Entry, Label, Label)>();
        int builtCount = -1;

        public ProblemsTabTK() {
            style.flexShrink = 1;
            box = new VisualElement();
            box.AddToClassList("zs-box-default");
            Add(box);

            const float h = 30f;
            var bar = new VisualElement();
            bar.AddToClassList("zs-toolbar");
            bar.style.flexDirection = FlexDirection.Row; bar.style.flexShrink = 0; bar.style.height = h;
            bar.Add(ZequenceEditorWindowTK.Gap(5f));
            var clear = ZS.Button("Clear", "Empties the list. Anything that happens again comes back, with its console warning.", "Flat",
                () => ZoundDiagnostics.Clear(), ZUICornerMask.All, 70f, h);
            clear.AddToClassList("zs-toolbarbutton");
            bar.Add(clear);
            bar.Add(ZequenceEditorWindowTK.Gap(ZS.MediumSpacing));
            summary = new Label();
            summary.AddToClassList("zs-lbl");
            summary.style.unityTextAlign = TextAnchor.MiddleLeft; summary.style.flexGrow = 1;
            bar.Add(summary);
            box.Add(bar);
            box.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));

            listHost = new VisualElement();
            box.Add(listHost);
            Build();
        }

        internal void Tick() {
            if (ZoundDiagnostics.Entries.Count != builtCount) { Build(); return; }
            if (ZoundDiagnostics.Revision == builtRevision) return;
            builtRevision = ZoundDiagnostics.Revision;
            float now = Time.realtimeSinceStartup;
            foreach (var l in live) { l.count.text = CountText(l.entry); l.ago.text = Ago(now - l.entry.lastSeen); }
        }

        void Build() {
            listHost.Clear(); live.Clear();
            var entries = ZoundDiagnostics.Entries;
            builtCount = entries.Count; builtRevision = ZoundDiagnostics.Revision;
            summary.text = entries.Count == 0
                ? "Nothing yet. Game code asking for a sound, ZPOC id, track or snapshot that does not exist is listed here, once per problem."
                : entries.Count + (entries.Count == 1 ? " problem" : " problems") + " this session, newest first. Each is also warned about once in the console.";
            if (entries.Count == 0) return;
            listHost.Add(Header());
            float now = Time.realtimeSinceStartup;
            for (int i = entries.Count - 1; i >= 0; i--) listHost.Add(Row(entries[i], now));
        }

        VisualElement Header() {
            var r = RowBox();
            r.Add(Cell("Problem", KindW, "What kind of request found nothing."));
            r.Add(Cell("Sound", ZoundW, "The sound the request was made of. Empty for a project-wide ZPOC value."));
            r.Add(Cell("Asked for", DetailW, "Exactly what game code asked for: the name, id, track number or snapshot name."));
            r.Add(Cell("Times", CountW, "How many times it happened. A request made every frame is still one row."));
            r.Add(Cell("Last", AgoW, "How long ago it last happened."));
            foreach (var c in r.Children()) c.AddToClassList("zs-mini");
            return r;
        }

        VisualElement Row(ZoundDiagnostics.Entry e, float now) {
            var r = RowBox();
            r.tooltip = e.message;
            r.Add(Cell(KindText(e.kind), KindW, KindHelp(e.kind)));
            string zoundText = !string.IsNullOrEmpty(e.zound) ? e.zound
                             : e.kind == ZoundDiagnostics.Kind.UndeclaredGlobalZpoc ? "(project-wide)" : "–";
            r.Add(Cell(zoundText, ZoundW, e.message));
            r.Add(Cell(e.shown ?? e.detail, DetailW, e.message));
            var count = Cell(CountText(e), CountW, null);
            var ago = Cell(Ago(now - e.lastSeen), AgoW, null);
            r.Add(count); r.Add(ago);
            // A missing sound that has been created since (the browser's Missing list drops it then) says so, and opens.
            bool nowExists = e.kind == ZoundDiagnostics.Kind.MissingZound && ZoundsProject.isJSONLoaded && ZoundDictionary.TryGetZoundByName(e.detail, out _);
            string target = nowExists ? e.detail : e.zound;
            bool exists = nowExists || (!string.IsNullOrEmpty(e.zound) && e.kind != ZoundDiagnostics.Kind.MissingZound
                          && ZoundsProject.isJSONLoaded && ZoundDictionary.TryGetZoundByName(e.zound, out _));
            if (nowExists) { ((Label)r.ElementAt(0)).text = "Sound added since"; r.ElementAt(0).tooltip = "Game code asked for this sound before it existed. A sound by that name exists now."; }
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
            r.style.flexDirection = FlexDirection.Row; r.style.flexShrink = 0; r.style.height = RowH; r.style.alignItems = Align.Center;
            r.style.paddingLeft = 6f;
            return r;
        }

        static Label Cell(string text, float w, string tip) {
            var l = new Label(text) { tooltip = tip };
            l.AddToClassList("zs-lbl");
            l.style.width = w; l.style.flexShrink = 0; l.style.overflow = Overflow.Hidden; l.style.textOverflow = TextOverflow.Ellipsis;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
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
