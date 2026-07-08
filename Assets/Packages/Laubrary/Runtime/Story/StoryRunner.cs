using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Story
{
    // Walks the active Screenplays: each holds one or more Bookmarks that advance Page → Page along edges.
    // Instant pages chain within a single frame; waiting pages park until their condition resolves. Owns a
    // patch stack so every rule mutation a screenplay made is unwound on teardown (so Story never leaks into
    // the next battle). Game-agnostic: the game injects the bridge / presenter / conditions before running.
    public class StoryRunner : MonoBehaviour
    {
        public IRuleBridge RuleBridge;
        public IMessagePresenter Presenter;
        public IConditionSource Conditions;
        public Journal Journal = new Journal();

        class Play
        {
            public Screenplay Sp;
            public readonly List<Bookmark> Bookmarks = new List<Bookmark>();
        }

        readonly List<Play> _plays = new List<Play>();
        readonly List<Action> _undo = new List<Action>();
        readonly List<IDisposable> _disposables = new List<IDisposable>();
        const int MaxStepsPerFrame = 256;

        // ── Traversal trace (for the editor's live visualisation) ─────────────────────────────────────
        // Page ids unique (GUIDs), so flat global sets are unambiguous even across multiple screenplays.
        readonly HashSet<string> _visited = new HashSet<string>();   // pages a bookmark has entered
        readonly HashSet<string> _edges = new HashSet<string>();     // edges a bookmark has followed
        readonly HashSet<string> _current = new HashSet<string>();   // pages a bookmark sits on right now

        public bool WasVisited(string pageId) => pageId != null && _visited.Contains(pageId);
        public bool IsCurrent(string pageId) => pageId != null && _current.Contains(pageId);
        public bool WasEdgeTraversed(string from, string port, string to) => _edges.Contains(EdgeKey(from, port, to));
        static string EdgeKey(string from, string port, string to) => from + "|" + port + "|" + to;

        public bool AnyRunning => _plays.Count > 0;

        // Reversible rule mutations record their undo here; injected rules register their disposal.
        public void PushUndo(Action a) { if (a != null) _undo.Add(a); }
        public void RegisterDisposable(IDisposable d) { if (d != null) _disposables.Add(d); }

        public void Run(Screenplay sp)
        {
            if (sp == null) return;
            string entry = sp.ResolveEntry();
            if (entry == null) { Debug.LogWarning("[Story] Screenplay has no entry page: " + sp.name); return; }
            var play = new Play { Sp = sp };
            play.Bookmarks.Add(new Bookmark { PageId = entry, Parked = false });
            _plays.Add(play);
        }

        void Update() => Step();

        // One advance pass over every active screenplay. Driven by Update each frame; also callable directly
        // for deterministic stepping (tests, tooling).
        public void Step()
        {
            for (int pi = _plays.Count - 1; pi >= 0; pi--)
            {
                StepPlay(_plays[pi]);
                if (_plays[pi].Bookmarks.Count == 0) _plays.RemoveAt(pi);
            }
            _current.Clear();
            foreach (var play in _plays)
                foreach (var bm in play.Bookmarks)
                    _current.Add(bm.PageId);
        }

        void StepPlay(Play play)
        {
            var ctx = new StoryContext
            {
                Runner = this,
                Screenplay = play.Sp,
                Journal = Journal,
                Rules = RuleBridge,
                Presenter = Presenter,
                Conditions = Conditions,
            };

            for (int bi = play.Bookmarks.Count - 1; bi >= 0; bi--)
            {
                if (bi >= play.Bookmarks.Count) continue; // list may have grown (Fork)
                var bm = play.Bookmarks[bi];
                ctx.Bookmark = bm;
                bool alive = true;
                int steps = 0;

                while (alive && steps++ < MaxStepsPerFrame)
                {
                    var page = play.Sp.GetPage(bm.PageId);
                    if (page == null) { alive = false; break; } // dangling reference → end this bookmark

                    string port;
                    if (bm.Parked)
                    {
                        port = page.Tick(ctx);
                        if (port == null) break;       // still waiting
                        page.Exit(ctx);
                        bm.Parked = false;
                    }
                    else
                    {
                        _visited.Add(bm.PageId); // a bookmark has entered this page
                        port = page.Enter(ctx);
                        if (port == null) { bm.Parked = true; break; } // park
                    }

                    if (port == Page.Stop) { alive = false; break; }

                    if (port == Page.All)
                    {
                        var outs = new List<Edge>(play.Sp.OutEdges(bm.PageId));
                        if (outs.Count == 0) { alive = false; break; }
                        for (int k = 0; k < outs.Count; k++) _edges.Add(EdgeKey(bm.PageId, outs[k].Port, outs[k].To));
                        for (int k = 1; k < outs.Count; k++)
                            play.Bookmarks.Add(new Bookmark { PageId = outs[k].To, Parked = false });
                        bm.PageId = outs[0].To;
                        continue; // Enter the (first) next page this frame
                    }

                    string next = null;
                    foreach (var e in play.Sp.OutEdges(bm.PageId))
                        if (e.Port == port) { next = e.To; break; }
                    if (next == null) { alive = false; break; } // no matching edge → end this bookmark

                    _edges.Add(EdgeKey(bm.PageId, port, next));
                    bm.PageId = next; // advance and continue (Enter next page)
                }

                if (!alive) play.Bookmarks.RemoveAt(bi);
            }
        }

        void OnDisable() => Teardown();

        // Unwind every rule mutation (reverse order) and stop all screenplays. Safe to call repeatedly.
        public void Teardown()
        {
            for (int i = _undo.Count - 1; i >= 0; i--) { try { _undo[i]?.Invoke(); } catch { } }
            _undo.Clear();
            for (int i = _disposables.Count - 1; i >= 0; i--) { try { _disposables[i]?.Dispose(); } catch { } }
            _disposables.Clear();
            _plays.Clear();
            _current.Clear();
        }
    }
}
