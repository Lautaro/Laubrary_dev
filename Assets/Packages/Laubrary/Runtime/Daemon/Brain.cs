using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Loom;

namespace Laubrary.Daemon
{
    // A Brain is the agent's LOGIC graph (a Loom graph). Its nodes orchestrate the behaviour set — enable/disable
    // a behaviour (State nodes), tweak/swap them (BehaviourTwist), branch on conditions — but never move or attack
    // themselves; the Behaviours do that. Nodes are [SerializeReference] so games add their own. Implements both
    // Loom interfaces: IRunnableGraph (the DaemonRunner walks it) + IGraphAsset (the shared graph editor authors it).
    [CreateAssetMenu(menuName = "Laubrary/Daemon/Brain", fileName = "Brain")]
    public class Brain : ScriptableObject, IRunnableGraph<BrainNode>, IGraphAsset
    {
        [SerializeReference] public List<BrainNode> Nodes = new List<BrainNode>();
        public List<Edge> Edges = new List<Edge>();
        public string EntryId;

        // ── IRunnableGraph<BrainNode> (runner) ──────────────────────────────────────────────────────────────
        public BrainNode GetNode(string id)
        {
            if (string.IsNullOrEmpty(id) || Nodes == null) return null;
            for (int i = 0; i < Nodes.Count; i++)
                if (Nodes[i] != null && Nodes[i].Id == id) return Nodes[i];
            return null;
        }

        public IEnumerable<Edge> OutEdges(string fromId)
        {
            if (Edges == null) yield break;
            for (int i = 0; i < Edges.Count; i++)
                if (Edges[i].From == fromId) yield return Edges[i];
        }

        public string ResolveEntry()
        {
            if (!string.IsNullOrEmpty(EntryId) && GetNode(EntryId) != null) return EntryId;
            if (Nodes != null)
            {
                for (int i = 0; i < Nodes.Count; i++) if (Nodes[i] is EntryNode) return Nodes[i].Id;
                if (Nodes.Count > 0 && Nodes[0] != null) return Nodes[0].Id;
            }
            return null;
        }

        // ── IGraphAsset (editor) ────────────────────────────────────────────────────────────────────────────
        IReadOnlyList<INode> IGraphAsset.Nodes => Nodes;   // covariant: List<BrainNode> → IReadOnlyList<INode>
        List<Edge> IGraphAsset.Edges { get => Edges; set => Edges = value; }
        string IGraphAsset.EntryId { get => EntryId; set => EntryId = value; }
        INode IGraphAsset.GetNodeUntyped(string id) => GetNode(id);
        Type IGraphAsset.NodeBaseType => typeof(BrainNode);
        bool IGraphAsset.IsEntryNode(INode node) => node is EntryNode;
        UnityEngine.Object IGraphAsset.AssetObject => this;

        INode IGraphAsset.AddNode(Type nodeType, string id, Vector2 pos)
        {
            var n = (BrainNode)Activator.CreateInstance(nodeType);
            n.Id = id; n.GraphPos = pos;
            Nodes.Add(n);
            return n;
        }

        void IGraphAsset.RebuildNodes(IReadOnlyList<INode> nodesInOrder)
        {
            Nodes = new List<BrainNode>(nodesInOrder.Count);
            foreach (var n in nodesInOrder) if (n is BrainNode bn) Nodes.Add(bn);
        }
    }

    // A node in a Brain graph. Implements both the editor's structural view (INode) and the runtime lifecycle
    // (IGraphNode<AgentContext>). Return null from Enter/Tick to PARK — a State node always parks, which also
    // guarantees a brain cycle can't busy-spin (Loom needs ≥1 parking node per cycle).
    [Serializable]
    public abstract class BrainNode : INode, IGraphNode<AgentContext>
    {
        [SerializeField] string m_id;
        public string Id { get => m_id; set => m_id = value; }
        [SerializeField] Vector2 m_graphPos;
        public Vector2 GraphPos { get => m_graphPos; set => m_graphPos = value; }
        public string Title;

        public string DisplayTitle => string.IsNullOrEmpty(Title) ? Pretty(GetType().Name) : Title;

        static readonly string[] DefaultPorts = { "out" };
        public virtual IReadOnlyList<string> Ports => DefaultPorts;

        public const string Stop = GraphPort.Stop;

        public abstract string Enter(AgentContext ctx);
        public virtual string Tick(AgentContext ctx) => null;
        public virtual void Exit(AgentContext ctx) { }
        public virtual string Describe() => Pretty(GetType().Name);

        static string Pretty(string n) => n.EndsWith("Node") ? n.Substring(0, n.Length - 4) : n;
    }

    // ── built-in node palette ─────────────────────────────────────────────────────────────────────────────

    // Where a brain starts. Passthrough.
    [Serializable]
    public class EntryNode : BrainNode
    {
        public override string Enter(AgentContext ctx) => "out";
        public override string Describe() => "Entry";
    }

    // One transition out of a State: a named output port taken when Condition is true.
    [Serializable]
    public class Transition
    {
        public string Port = "next";
        [Tooltip("Condition expression evaluated by the game's IAgentConditions (e.g. \"targetInRange\", \"hp<0.3\").")]
        public string Condition;
    }

    // A STATE: on enter, enable a behaviour (by id) and park; each frame check the transitions and, when one's
    // condition fires, exit (disable the behaviour) and advance along that port. This is the FSM state — the
    // behaviour does the acting while the node parks here.
    [Serializable]
    public class StateNode : BrainNode
    {
        [Tooltip("Behaviour to enable while in this state (its Id = type name). Blank = a pure wait/branch state.")]
        public string BehaviourId;
        public List<Transition> Transitions = new List<Transition>();

        public override IReadOnlyList<string> Ports
        {
            get
            {
                if (Transitions == null || Transitions.Count == 0) return Array.Empty<string>();
                var arr = new string[Transitions.Count];
                for (int i = 0; i < Transitions.Count; i++)
                    arr[i] = string.IsNullOrEmpty(Transitions[i].Port) ? "t" + i : Transitions[i].Port;
                return arr;
            }
        }

        public override string Enter(AgentContext ctx)
        {
            if (!string.IsNullOrEmpty(BehaviourId)) ctx.Bridge?.SetActive(BehaviourId, true);
            return null;   // always park in the state; Tick checks the exits (so Exit always runs on transition)
        }

        public override string Tick(AgentContext ctx)
        {
            if (Transitions != null)
                for (int i = 0; i < Transitions.Count; i++)
                    if (ctx.Check(Transitions[i].Condition))
                        return string.IsNullOrEmpty(Transitions[i].Port) ? "t" + i : Transitions[i].Port;
            return null;
        }

        public override void Exit(AgentContext ctx)
        {
            if (!string.IsNullOrEmpty(BehaviourId)) ctx.Bridge?.SetActive(BehaviourId, false);
        }

        public override string Describe() => string.IsNullOrEmpty(BehaviourId) ? "State" : "State: " + BehaviourId;
    }

    public enum TwistKind { Enable, Disable, SetParam, Swap }

    [Serializable]
    public class TwistOp
    {
        public TwistKind Kind = TwistKind.Enable;
        public string BehaviourId;
        public string Field;                          // SetParam
        public string Value;                          // SetParam (parsed to the field type)
        [SerializeReference] public AgentBehaviour Replacement;   // Swap
    }

    // Instantly mutate the behaviour set (enable/disable/set-param/swap) then continue — the Daemon analogue of
    // Story's PlotTwist. Use it for one-off changes that aren't a whole state (e.g. buff speed, swap the attack).
    [Serializable]
    public class BehaviourTwistNode : BrainNode
    {
        public List<TwistOp> Ops = new List<TwistOp>();

        public override string Enter(AgentContext ctx)
        {
            var br = ctx.Bridge;
            if (br != null && Ops != null)
                foreach (var op in Ops)
                {
                    if (op == null || string.IsNullOrEmpty(op.BehaviourId)) continue;
                    switch (op.Kind)
                    {
                        case TwistKind.Enable:  br.SetActive(op.BehaviourId, true); break;
                        case TwistKind.Disable: br.SetActive(op.BehaviourId, false); break;
                        case TwistKind.SetParam: br.SetParam(op.BehaviourId, op.Field, op.Value); break;
                        case TwistKind.Swap: if (op.Replacement != null) br.Swap(op.BehaviourId, op.Replacement.Clone()); break;
                    }
                }
            return "out";
        }

        public override string Describe() => "Twist (" + (Ops?.Count ?? 0) + " op)";
    }

    // Instant 2-way branch on a condition.
    [Serializable]
    public class ConditionBranchNode : BrainNode
    {
        public string Condition;
        static readonly string[] TrueFalse = { "true", "false" };
        public override IReadOnlyList<string> Ports => TrueFalse;
        public override string Enter(AgentContext ctx) => ctx.Check(Condition) ? "true" : "false";
        public override string Describe() => "If [" + Condition + "]";
    }

    // Park until a condition becomes true, then advance.
    [Serializable]
    public class WaitConditionNode : BrainNode
    {
        public string Condition;
        public override string Enter(AgentContext ctx) => ctx.Check(Condition) ? "out" : null;
        public override string Tick(AgentContext ctx) => ctx.Check(Condition) ? "out" : null;
        public override string Describe() => "Wait until [" + Condition + "]";
    }

    // Park until a GLOBAL Notifyer string event fires (e.g. "WaveStarted", "PlayerSpotted"). For PER-AGENT
    // reactions (this enemy got hit) prefer a Fork + WaitCondition on a blackboard flag the combat adapter sets.
    [Serializable]
    public class WaitEventNode : BrainNode
    {
        public string EventId;
        public override string Enter(AgentContext ctx)
        {
            var bm = ctx.Bookmark;
            bm.Locals[Id + ":fired"] = false;
            UnityEngine.Events.UnityAction h = () => bm.Locals[Id + ":fired"] = true;
            bm.Locals[Id + ":handler"] = h;
            Laubrary.Notifyer.Notifyer.Subscribe(EventId, h);
            return null;
        }
        public override string Tick(AgentContext ctx) => ctx.GetLocal(Id + ":fired", false) ? "out" : null;
        public override void Exit(AgentContext ctx)
        {
            var h = ctx.GetLocal<UnityEngine.Events.UnityAction>(Id + ":handler", null);
            if (h != null) Laubrary.Notifyer.Notifyer.Unsubscribe(EventId, h);
            ctx.ClearLocal(Id + ":handler");
        }
        public override string Describe() => "Wait event [" + EventId + "]";
    }

    // Spawn a bookmark down EVERY outgoing edge — run concurrent sub-graphs (e.g. a main FSM plus a "flee when
    // hurt" watchdog). Loom's runner tracks each bookmark independently.
    [Serializable]
    public class ForkNode : BrainNode
    {
        public override string Enter(AgentContext ctx) => GraphPort.All;
        public override string Describe() => "Fork";
    }

    // Write a per-agent blackboard value (a flag/counter conditions can read: "hasTarget", "alertLevel"…).
    [Serializable]
    public class SetBlackboardNode : BrainNode
    {
        public string Key;
        public string Value;
        public override string Enter(AgentContext ctx) { ctx.Blackboard?.Set(Key, Value); return "out"; }
        public override string Describe() => "Set " + Key + " = " + Value;
    }

    // Push a status label onto the body (debug overlay / animation cue).
    [Serializable]
    public class SetStatusNode : BrainNode
    {
        public string Label;
        public override string Enter(AgentContext ctx) { ctx.Body?.SetStatus(Label); return "out"; }
        public override string Describe() => "Status: " + Label;
    }
}
