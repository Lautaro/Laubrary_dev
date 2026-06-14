using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Rulesets
{
    // The single MonoBehaviour that owns and drives the live rules. At Awake it clones its source RuleSet
    // into a runtime list (so play edits don't dirty the asset); each frame it reconciles every rule's
    // Active flag against its running state (firing OnActivate/OnDeactivate on transitions) and ticks the
    // running ones. Runs very early so the runtime rule list exists before other systems boot.
    [DefaultExecutionOrder(-2000)]
    public class RulesHost : MonoBehaviour
    {
        public static RulesHost Active { get; private set; }

        [Tooltip("The ruleset asset this run uses. Cloned at boot into the live list below.")]
        public RuleSet SourceSet;

        [System.NonSerialized] public List<GameRule> Rules = new List<GameRule>();

        bool _started;

        void Awake()
        {
            Active = this;
            Rules = new List<GameRule>();
            if (SourceSet != null)
            {
                var clone = SourceSet.Clone();
                if (clone != null && clone.Rules != null)
                    foreach (var r in clone.Rules)
                        if (r != null) Rules.Add(r);
            }
            for (int i = 0; i < Rules.Count; i++) Rules[i]?.HostLoad();
        }

        void Start()
        {
            _started = true;
            Reconcile();
        }

        void Update()
        {
            if (!_started) return;
            Reconcile();
            float dt = Time.deltaTime;
            for (int i = 0; i < Rules.Count; i++)
                Rules[i]?.HostTick(dt);
        }

        void Reconcile()
        {
            for (int i = 0; i < Rules.Count; i++)
            {
                var r = Rules[i];
                if (r == null) continue;
                if (r.Active && !r.IsRunning) r.HostActivate();
                else if (!r.Active && r.IsRunning) r.HostDeactivate();
            }
        }

        void OnDisable()
        {
            for (int i = 0; i < Rules.Count; i++)
                if (Rules[i] != null && Rules[i].IsRunning) Rules[i].HostDeactivate();
            for (int i = 0; i < Rules.Count; i++) Rules[i]?.HostUnload();
            if (Active == this) Active = null;
        }

        // ---- Authoring / config helpers ----

        public T Get<T>() where T : GameRule
        {
            for (int i = 0; i < Rules.Count; i++) if (Rules[i] is T t) return t;
            return null;
        }

        public IEnumerable<GameRule> OfTypeName(string typeName)
        {
            for (int i = 0; i < Rules.Count; i++)
                if (Rules[i] != null && Rules[i].GetType().Name == typeName) yield return Rules[i];
        }

        public void Add(GameRule r) { if (r != null) Rules.Add(r); }

        // Swap in a different ruleset at runtime: deactivate the current rules, clone the new set, and let
        // the next Reconcile activate them.
        public void LoadSet(RuleSet set)
        {
            for (int i = 0; i < Rules.Count; i++)
            {
                if (Rules[i] == null) continue;
                if (Rules[i].IsRunning) Rules[i].HostDeactivate();
                Rules[i].HostUnload();
            }
            Rules.Clear();
            SourceSet = set;
            if (set != null)
            {
                var clone = set.Clone();
                if (clone != null && clone.Rules != null)
                    foreach (var r in clone.Rules)
                        if (r != null) Rules.Add(r);
            }
            for (int i = 0; i < Rules.Count; i++) Rules[i]?.HostLoad();
            if (_started) Reconcile();
        }

        // Convenience static lookup used by systems/rules that need a tunable.
        public static T Rule<T>() where T : GameRule => Active != null ? Active.Get<T>() : null;
    }
}
