using System.Collections.Generic;

namespace Laubrary.Loom
{
    // ── Loom: a reusable node-graph engine ────────────────────────────────────────────────────────────────
    // The shared foundation under Laubrary's graph tools: Story (narrative/scenario flow) and Daemon (per-agent
    // AI). A graph is nodes + directed, named-port edges; a GraphRunner walks it with one or more Bookmarks. The
    // runtime lifecycle (Enter/Tick/Exit) is generic over a per-tool context (TCtx), so Story hangs Rules/Presenter
    // off its context and Daemon hangs a behaviour-bridge/body off its own — same engine, different payload.

    // A directed edge: leaving node `From` through output port `Port`, arrive at node `To`.
    [System.Serializable]
    public struct Edge
    {
        public string From;
        public string Port;
        public string To;

        public Edge(string from, string port, string to) { From = from; Port = port; To = to; }
    }

    // Sentinel port-return values the runner understands (any node lifecycle may return these).
    public static class GraphPort
    {
        public const string Stop = "__stop__";   // end this bookmark
        public const string All  = "__all__";    // fork: spawn a bookmark down every outgoing edge
    }

    // A moving cursor walking a graph's nodes. A fork spawns more. Carries per-walk transient state (`Locals`) so
    // shared node data stays re-entrant — a parked wait stores its flags here, not on the node.
    public class Bookmark
    {
        public string NodeId;
        public bool Parked;
        public readonly Dictionary<string, object> Locals = new Dictionary<string, object>();
    }

    // The per-step bag a node receives must expose the bookmark being advanced (the runner points it here each
    // step). Each tool subclasses its own context with tool-specific services and implements this.
    public interface IGraphContext
    {
        Bookmark Bookmark { get; set; }
    }

    // A node's runtime lifecycle over a context TCtx.
    //   Enter(ctx) → the output PORT to advance along, or null to PARK (wait).
    //   Tick(ctx)  → polled each frame while parked; a port to advance, or null to keep waiting.
    //   Exit(ctx)  → once, just before leaving after a parked wait resolves.
    // Return GraphPort.Stop to end the bookmark, GraphPort.All to fork down every edge.
    public interface IGraphNode<TCtx>
    {
        string Id { get; }
        string Enter(TCtx ctx);
        string Tick(TCtx ctx);
        void Exit(TCtx ctx);
    }

    // What the runner needs to WALK an asset (strongly-typed nodes). Kept separate from the editor's structural
    // view so the runner stays generic without the editor's mutation surface.
    public interface IRunnableGraph<TNode>
    {
        TNode GetNode(string id);
        IEnumerable<Edge> OutEdges(string fromId);
        string ResolveEntry();
    }

    // Traversal trace the graph editor polls to light up the live graph while playing.
    public interface IGraphRunnerViz
    {
        bool IsCurrent(string nodeId);
        bool WasVisited(string nodeId);
        bool WasEdgeTraversed(string from, string port, string to);
    }
}
