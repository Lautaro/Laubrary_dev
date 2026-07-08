using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Loom;

namespace Laubrary.Daemon
{
    // ── Daemon: per-agent AI as a Loom graph over configurable Behaviours ──────────────────────────────────
    // A Brain (a Loom graph) is the LOGIC; Behaviours are the "what the agent does" (configurable data). The
    // graph enables/disables behaviours, tweaks their params, or swaps instances via IBehaviourBridge — exactly
    // as Story's PlotTwist mutates Rules. The game supplies IAgentBody (the actuators/sensors) + IAgentConditions.

    // The game-implemented facade the agent drives: movement/attack/sensing live in the GAME (Colosseum/NavGrid/…).
    // Laubrary keeps only the minimum every brain needs; game behaviours cast this to their concrete body type.
    public interface IAgentBody
    {
        Vector2 Position { get; }
        void SetStatus(string label);   // debug/label overlay ("Chasing", "Attacking"…)
    }

    // Optional polled conditions for transitions ("targetInRange", "hp<0.3"). The game answers them against the
    // live world; passing the context lets it read the body / blackboard.
    public interface IAgentConditions
    {
        bool Evaluate(string expression, AgentContext ctx);
    }

    // Per-agent runtime key/value store (flags, timers, the current target id…). Not serialized into the Brain.
    public class Blackboard
    {
        readonly Dictionary<string, object> _m = new Dictionary<string, object>();
        public bool Has(string key) => key != null && _m.ContainsKey(key);
        public void Set(string key, object value) { if (key != null) _m[key] = value; }
        public void Clear(string key) { if (key != null) _m.Remove(key); }
        public T Get<T>(string key, T def = default) => key != null && _m.TryGetValue(key, out var v) && v is T t ? t : def;
    }

    // Mutates the agent's live behaviour set (mirrors Story's IRuleBridge, plus Swap). The BehaviourHost implements
    // it; the graph's State / BehaviourTwist nodes call it. Mutations can register undo on the runner so a brain
    // that's torn down leaves no residue.
    public interface IBehaviourBridge
    {
        bool IsActive(string behaviourId);
        object GetParam(string behaviourId, string field);
        void SetActive(string behaviourId, bool on);
        void SetParam(string behaviourId, string field, object value);
        void Swap(string behaviourId, AgentBehaviour replacement); // replace an instance (possibly a different type)
        IDisposable Inject(AgentBehaviour behaviour);              // add a bespoke instance; dispose removes it
    }

    // Everything a Brain node OR a Behaviour receives per step. Implements IGraphContext so Loom's GraphRunner can
    // point Bookmark at the bookmark being advanced. One context per agent (reused each frame).
    public class AgentContext : IGraphContext
    {
        public DaemonRunner Runner;
        public IAgentBody Body;
        public BehaviourHost Host;
        public IBehaviourBridge Bridge;      // == Host
        public IAgentConditions Conditions;
        public Blackboard Blackboard;
        public float DeltaTime;
        public Bookmark Bookmark { get; set; }

        public bool Check(string expression)
            => !string.IsNullOrEmpty(expression) && Conditions != null && Conditions.Evaluate(expression, this);

        public T GetLocal<T>(string key, T def = default)
            => Bookmark != null && Bookmark.Locals.TryGetValue(key, out var v) && v is T t ? t : def;
        public void SetLocal(string key, object value) { if (Bookmark != null) Bookmark.Locals[key] = value; }
        public void ClearLocal(string key) { if (Bookmark != null) Bookmark.Locals.Remove(key); }
    }
}
