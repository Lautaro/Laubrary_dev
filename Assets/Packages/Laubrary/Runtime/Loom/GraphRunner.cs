using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Loom
{
    // Walks active graphs: each Play holds one or more Bookmarks advancing node → node along edges. Instant nodes
    // chain within a single frame; parked nodes wait until their condition resolves. Generic over the node type
    // and the per-step context — a subclass supplies the context (MakeContext) and its own Run entry points. Owns
    // a reversible patch/undo stack so mutations a graph made are unwound on teardown. This is the shared engine
    // under Story (narrative flow) and Daemon (per-agent AI). The stepper loops naturally: a cyclic graph with a
    // parking node per cycle runs indefinitely (a persistent brain), a graph that reaches a Stop ends.
    public abstract class GraphRunner<TNode, TCtx> : MonoBehaviour, IGraphRunnerViz
        where TNode : class, IGraphNode<TCtx>
        where TCtx : class, IGraphContext
    {
        protected class Play
        {
            public IRunnableGraph<TNode> Graph;
            public readonly List<Bookmark> Bookmarks = new List<Bookmark>();
        }

        readonly List<Play> _plays = new List<Play>();
        readonly List<Action> _undo = new List<Action>();
        readonly List<IDisposable> _disposables = new List<IDisposable>();
        const int MaxStepsPerFrame = 256;

        // ── Traversal trace (for the editor's live visualisation) ─────────────────────────────────────
        // Node ids are unique (GUIDs), so flat global sets are unambiguous even across multiple graphs.
        readonly HashSet<string> _visited = new HashSet<string>();
        readonly HashSet<string> _edges = new HashSet<string>();
        readonly HashSet<string> _current = new HashSet<string>();
        // The ONE edge each live bookmark actually arrived through (not "any edge into this node") — lets the
        // editor light up just the true path instead of every wire that happens to end at the current node.
        readonly HashSet<string> _currentEdges = new HashSet<string>();
        // Time.time of the last frame a node/edge was current — the editor uses "now minus this" to fade a
        // just-vacated glow out smoothly instead of it just switching off.
        readonly Dictionary<string, float> _lastActiveNode = new Dictionary<string, float>();
        readonly Dictionary<string, float> _lastActiveEdge = new Dictionary<string, float>();

        public bool WasVisited(string nodeId) => nodeId != null && _visited.Contains(nodeId);
        public bool IsCurrent(string nodeId) => nodeId != null && _current.Contains(nodeId);
        public bool WasEdgeTraversed(string from, string port, string to) => _edges.Contains(EdgeKey(from, port, to));
        public bool IsCurrentEdge(string from, string port, string to) => _currentEdges.Contains(EdgeKey(from, port, to));

        public float SecondsSinceActive(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || !_lastActiveNode.TryGetValue(nodeId, out var t)) return float.MaxValue;
            return Time.time - t;
        }

        public float SecondsSinceEdgeActive(string from, string port, string to)
        {
            if (!_lastActiveEdge.TryGetValue(EdgeKey(from, port, to), out var t)) return float.MaxValue;
            return Time.time - t;
        }

        static string EdgeKey(string from, string port, string to) => from + "|" + port + "|" + to;

        public bool AnyRunning => _plays.Count > 0;

        // Reversible mutations record their undo here; injected disposables register for teardown.
        public void PushUndo(Action a) { if (a != null) _undo.Add(a); }
        public void RegisterDisposable(IDisposable d) { if (d != null) _disposables.Add(d); }

        // Subclass builds its per-step context (Story fills Rules/Presenter/Conditions; Daemon fills the behaviour
        // bridge + agent body). The runner points ctx.Bookmark at the bookmark being advanced.
        protected abstract TCtx MakeContext(IRunnableGraph<TNode> graph);

        // Start walking a graph from its entry node.
        protected void RunGraph(IRunnableGraph<TNode> graph)
        {
            if (graph == null) return;
            string entry = graph.ResolveEntry();
            if (entry == null) { Debug.LogWarning("[Loom] graph has no entry node."); return; }
            var play = new Play { Graph = graph };
            play.Bookmarks.Add(new Bookmark { NodeId = entry, Parked = false });
            _plays.Add(play);
        }

        protected virtual void Update() => Step();

        // One advance pass over every active graph. Driven by Update each frame; also callable directly for
        // deterministic stepping (tests, tooling).
        public void Step()
        {
            for (int pi = _plays.Count - 1; pi >= 0; pi--)
            {
                StepPlay(_plays[pi]);
                if (_plays[pi].Bookmarks.Count == 0) _plays.RemoveAt(pi);
            }
            _current.Clear();
            _currentEdges.Clear();
            float now = Time.time;
            foreach (var play in _plays)
                foreach (var bm in play.Bookmarks)
                {
                    _current.Add(bm.NodeId);
                    _lastActiveNode[bm.NodeId] = now;
                    if (bm.ArrivedVia != null)
                    {
                        _currentEdges.Add(bm.ArrivedVia);
                        _lastActiveEdge[bm.ArrivedVia] = now;
                    }
                }
        }

        void StepPlay(Play play)
        {
            var ctx = MakeContext(play.Graph);

            for (int bi = play.Bookmarks.Count - 1; bi >= 0; bi--)
            {
                if (bi >= play.Bookmarks.Count) continue; // list may have grown (fork)
                var bm = play.Bookmarks[bi];
                ctx.Bookmark = bm;
                bool alive = true;
                int steps = 0;

                while (alive && steps++ < MaxStepsPerFrame)
                {
                    var node = play.Graph.GetNode(bm.NodeId);
                    if (node == null) { alive = false; break; } // dangling reference → end this bookmark

                    string port;
                    if (bm.Parked)
                    {
                        port = node.Tick(ctx);
                        if (port == null) break;       // still waiting
                        node.Exit(ctx);
                        bm.Parked = false;
                    }
                    else
                    {
                        _visited.Add(bm.NodeId);
                        port = node.Enter(ctx);
                        if (port == null) { bm.Parked = true; break; } // park
                    }

                    if (port == GraphPort.Stop) { alive = false; break; }

                    if (port == GraphPort.All)
                    {
                        var outs = new List<Edge>(play.Graph.OutEdges(bm.NodeId));
                        if (outs.Count == 0) { alive = false; break; }
                        string forkFrom = bm.NodeId;
                        for (int k = 0; k < outs.Count; k++) _edges.Add(EdgeKey(forkFrom, outs[k].Port, outs[k].To));
                        for (int k = 1; k < outs.Count; k++)
                            play.Bookmarks.Add(new Bookmark { NodeId = outs[k].To, Parked = false, ArrivedVia = EdgeKey(forkFrom, outs[k].Port, outs[k].To) });
                        bm.ArrivedVia = EdgeKey(forkFrom, outs[0].Port, outs[0].To);
                        bm.NodeId = outs[0].To;
                        continue; // enter the (first) next node this frame
                    }

                    string next = null;
                    foreach (var e in play.Graph.OutEdges(bm.NodeId))
                        if (e.Port == port) { next = e.To; break; }
                    if (next == null) { alive = false; break; } // no matching edge → end this bookmark

                    string arrivedVia = EdgeKey(bm.NodeId, port, next);
                    _edges.Add(arrivedVia);
                    bm.ArrivedVia = arrivedVia;
                    bm.NodeId = next; // advance and continue (enter next node)
                }

                if (!alive) play.Bookmarks.RemoveAt(bi);
            }
        }

        protected virtual void OnDisable() => Teardown();

        // Unwind every mutation (reverse order), dispose registered handles, stop all graphs. Safe to call repeatedly.
        public virtual void Teardown()
        {
            for (int i = _undo.Count - 1; i >= 0; i--) { try { _undo[i]?.Invoke(); } catch { } }
            _undo.Clear();
            for (int i = _disposables.Count - 1; i >= 0; i--) { try { _disposables[i]?.Dispose(); } catch { } }
            _disposables.Clear();
            _plays.Clear();
            _current.Clear();
            _currentEdges.Clear();
            _lastActiveNode.Clear();
            _lastActiveEdge.Clear();
        }
    }
}
