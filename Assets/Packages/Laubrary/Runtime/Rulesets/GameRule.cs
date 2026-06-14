using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Laubrary.Rulesets
{
    // Implemented by anything that must be reset to its strict default before a config is applied.
    public interface IRuleDefaults
    {
        void ResetToDefault();
    }

    // Common base for game rules — a PLAIN SERIALIZABLE DATA CLASS (no GameObject). A ruleset is one
    // RuleSet asset holding a [SerializeReference] list of these; RulesHost owns the live list and drives
    // the lifecycle (OnActivate / Tick / OnDeactivate) in place of Unity's OnEnable/Update/OnDisable.
    //
    //   Active              — replaces MonoBehaviour.enabled. Serialized; the host activates/deactivates
    //                         the rule as this flips.
    //   DefaultActive       — is this rule on at the strict floor? Most are; opt-in extras override false.
    //   OnResetToDefault()  — reset this rule's OWN tunables to their strict-default literals.
    //
    // Lifecycle hooks (override as needed):
    //   OnLoad/OnUnload — run for EVERY rule regardless of Active (registry singletons other systems read).
    //   OnActivate()    — once when the rule starts running.
    //   Tick(dt)        — every frame while running. dt = Time.deltaTime.
    //   OnDeactivate()  — once when the rule stops running.
    [System.Serializable]
    public abstract class GameRule : IRuleDefaults
    {
        [SerializeField] bool m_active = true;
        public bool Active { get => m_active; set => m_active = value; }

        [System.NonSerialized] bool _running;
        public bool IsRunning => _running;

        protected virtual bool DefaultActive => true;
        public virtual bool AllowMultiple => false;
        protected virtual void OnResetToDefault() { }

        public void ResetToDefault()
        {
            m_active = DefaultActive;
            OnResetToDefault();
        }

        // ---- Lifecycle hooks (overridden by rules) ----
        protected virtual void OnLoad() { }
        protected virtual void OnUnload() { }
        protected virtual void OnActivate() { }
        protected virtual void Tick(float dt) { }
        protected virtual void OnDeactivate() { }

        // ---- Host-facing drivers (RulesHost, same assembly, reconciles Active <-> running) ----
        internal void HostLoad() => OnLoad();
        internal void HostUnload() => OnUnload();
        internal void HostActivate()    { if (_running) return; _running = true;  OnActivate(); }
        internal void HostTick(float dt) { if (_running) Tick(dt); }
        internal void HostDeactivate()  { if (!_running) return; _running = false; OnDeactivate(); }

        // MANDATORY: a one-line, value-aware summary of what this rule does right now (debug / editor).
        public abstract string Describe();
        public override string ToString() => Describe();

        // A live, value-aware status shown in the Rules editor while playing. Null = nothing to show.
        public virtual string RuntimeInfo() => null;

        // Every active rule in the live host, in stable order.
        public static IEnumerable<GameRule> ActiveRules() =>
            (RulesHost.Active != null ? (IEnumerable<GameRule>)RulesHost.Active.Rules : System.Array.Empty<GameRule>())
                .Where(r => r != null && r.Active)
                .OrderBy(r => r.GetType().Name);

        // ---- Player-facing briefing ----
        public virtual string Brief() => null;
        public virtual int BriefPriority => 0;

        public static List<string> GameBrief()
        {
            var lines = new List<string>();
            var seen = new HashSet<string>();
            var briefs = ActiveRules()
                .Select(r => (text: SafeBrief(r), prio: r.BriefPriority))
                .Where(b => !string.IsNullOrEmpty(b.text))
                .OrderByDescending(b => b.prio);
            foreach (var b in briefs)
                foreach (var ln in b.text.Split('\n'))
                {
                    var t = ln.Trim();
                    if (!string.IsNullOrWhiteSpace(t) && seen.Add(t)) lines.Add(t);
                }
            return lines;
        }

        static string SafeBrief(GameRule r) { try { return r.Brief(); } catch { return null; } }

        public static string ActiveSummary()
        {
            var sb = new StringBuilder();
            foreach (var r in ActiveRules()) sb.Append("• ").AppendLine(Safe(r));
            return sb.Length == 0 ? "(no rules active)" : sb.ToString().TrimEnd();
        }

        static string Safe(GameRule r)
        {
            try { return r.Describe(); }
            catch { return r.GetType().Name + " (description failed)"; }
        }

        // Live list of single-instance rule types with MORE than one active instance (they'd fight).
        public static List<string> SingletonWarnings()
        {
            var list = new List<string>();
            var dupes = ActiveRules()
                .Where(r => !r.AllowMultiple)
                .GroupBy(r => r.GetType())
                .Where(g => g.Count() > 1);
            foreach (var g in dupes)
                list.Add($"{g.Count()} active {g.Key.Name}s — only one allowed (they'll fight). Keep one.");
            return list;
        }

        public static void AuditSingletons()
        {
            foreach (var w in SingletonWarnings()) Debug.LogWarning("[Rules] " + w);
        }
    }
}
