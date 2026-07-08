using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Daemon
{
    // One configurable unit of "what the agent does" (Chase / Melee / LobProjectile / HaulWall…). The per-agent
    // analogue of a GameRule: a [SerializeReference] data object with defaults + an activate/tick/deactivate
    // lifecycle. Games subclass it in their own assembly and drive the body through AgentContext.Body. The graph
    // flips Active (a State node) and tweaks fields (a BehaviourTwist) via the bridge; the host ticks the active
    // ones. Behaviours are referenced by Id (their type name) — single-instance per type per agent.
    [Serializable]
    public abstract class AgentBehaviour
    {
        [Tooltip("Whether this behaviour is currently running. The Brain graph flips this; the host ticks active ones.")]
        public bool Active;

        // Bridge id — the short type name (e.g. ChaseBehaviour → \"ChaseBehaviour\"). One live instance per type.
        public virtual string Id => GetType().Name;

        public virtual void OnActivate(AgentContext ctx) { }
        public virtual void Tick(AgentContext ctx) { }
        public virtual void OnDeactivate(AgentContext ctx) { }

        public virtual string Describe() => GetType().Name;

        // Per-agent clone (the host clones the authored set so a brain's tweaks/swaps never touch the asset).
        // MemberwiseClone copies value fields; override to deep-copy reference fields if a behaviour holds any.
        public virtual AgentBehaviour Clone() => (AgentBehaviour)MemberwiseClone();
    }

    // The authored default behaviour set for an enemy TYPE (the per-agent analogue of a RuleSet). Attach one to a
    // spawn/config; the host clones it per agent.
    [CreateAssetMenu(menuName = "Laubrary/Daemon/Behaviour Set", fileName = "BehaviourSet")]
    public class BehaviourSet : ScriptableObject
    {
        [SerializeReference] public List<AgentBehaviour> Behaviours = new List<AgentBehaviour>();
    }

    // Per-agent live behaviour set + the bridge the graph mutates. Clones the authored set on Load, drives each
    // behaviour's lifecycle on Enable/Disable, ticks the active ones each frame. Bound to the agent's context so
    // OnActivate/OnDeactivate get a context.
    public class BehaviourHost : IBehaviourBridge
    {
        readonly List<AgentBehaviour> _live = new List<AgentBehaviour>();
        AgentContext _ctx;

        public IReadOnlyList<AgentBehaviour> Live => _live;
        public void Bind(AgentContext ctx) => _ctx = ctx;

        public void Load(BehaviourSet set)
        {
            _live.Clear();
            if (set != null && set.Behaviours != null)
                foreach (var b in set.Behaviours)
                    if (b != null) _live.Add(b.Clone());
        }

        public AgentBehaviour Get(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < _live.Count; i++) if (_live[i] != null && _live[i].Id == id) return _live[i];
            return null;
        }

        public void TickActive(AgentContext ctx)
        {
            for (int i = 0; i < _live.Count; i++)
                if (_live[i] != null && _live[i].Active) _live[i].Tick(ctx);
        }

        public void DeactivateAll()
        {
            for (int i = 0; i < _live.Count; i++)
                if (_live[i] != null && _live[i].Active) { _live[i].Active = false; _live[i].OnDeactivate(_ctx); }
        }

        // ── IBehaviourBridge ──────────────────────────────────────────────────────────────────────────────
        public bool IsActive(string id) { var b = Get(id); return b != null && b.Active; }

        public object GetParam(string id, string field)
        {
            var b = Get(id); if (b == null) return null;
            var f = b.GetType().GetField(field);
            return f?.GetValue(b);
        }

        public void SetActive(string id, bool on)
        {
            var b = Get(id);
            if (b == null || b.Active == on) return;
            b.Active = on;
            if (on) b.OnActivate(_ctx); else b.OnDeactivate(_ctx);
        }

        public void SetParam(string id, string field, object value)
        {
            var b = Get(id); if (b == null) return;
            var f = b.GetType().GetField(field); if (f == null) return;
            try { f.SetValue(b, Convert.ChangeType(value, f.FieldType)); }
            catch { try { f.SetValue(b, value); } catch { } }
        }

        public void Swap(string id, AgentBehaviour replacement)
        {
            if (replacement == null) return;
            var b = Get(id);
            bool wasActive = b != null && b.Active;
            if (b != null) { if (b.Active) b.OnDeactivate(_ctx); _live.Remove(b); }
            _live.Add(replacement);
            if (wasActive) { replacement.Active = true; replacement.OnActivate(_ctx); }
        }

        public IDisposable Inject(AgentBehaviour behaviour)
        {
            if (behaviour == null) return null;
            _live.Add(behaviour);
            return new Remover(this, behaviour);
        }

        class Remover : IDisposable
        {
            BehaviourHost _h; AgentBehaviour _b;
            public Remover(BehaviourHost h, AgentBehaviour b) { _h = h; _b = b; }
            public void Dispose()
            {
                if (_h == null || _b == null) return;
                if (_b.Active) _b.OnDeactivate(_h._ctx);
                _h._live.Remove(_b);
                _h = null; _b = null;
            }
        }
    }
}
