using System;
using System.Collections.Generic;
using System.Linq;
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
            BuildLive(SourceSet);
            for (int i = 0; i < Rules.Count; i++) Rules[i]?.HostLoad();
        }

        // Build the live rule list = CHOSEN ruleset + GLOBAL ruleset. Global rules (Resources/Rulesets/Global)
        // apply under every ruleset, EXCEPT that a single-instance type (AllowMultiple == false) the chosen
        // ruleset ENABLES overrides the global one — only one copy ever, the global is dropped. A merely-
        // present-but-disabled copy in the chosen set (e.g. an editor placeholder) does NOT override: the
        // global still applies. Multi-instance globals always coexist. Both sets are cloned so play-mode edits
        // don't dirty the source assets. If no Global asset exists this is just the chosen ruleset.
        void BuildLive(RuleSet chosen)
        {
            Rules = new List<GameRule>();
            var chosenRules = CloneRules(chosen);
            var globalRules = CloneRules(RuleSetManager.LoadGlobalAsset());

            // Single-instance types the GLOBAL provides — a chosen-set DISABLED copy of such a type yields to
            // the global (so an auto-added/disabled placeholder doesn't suppress a real global rule).
            var globalSingle = new HashSet<Type>(globalRules.Where(r => !r.AllowMultiple).Select(r => r.GetType()));

            foreach (var r in chosenRules)
            {
                if (!r.AllowMultiple && !r.Active && globalSingle.Contains(r.GetType())) continue; // global wins
                Rules.Add(r);
            }

            // Global rules: add unless that single-instance type is already covered (a chosen ENABLED override,
            // or a copy the global lacked). Multi-instance globals always coexist.
            foreach (var g in globalRules)
            {
                if (!g.AllowMultiple && HasRuleType(g.GetType())) continue;
                Rules.Add(g);
            }
        }

        static List<GameRule> CloneRules(RuleSet set)
        {
            var list = new List<GameRule>();
            if (set == null) return list;
            var c = set.Clone();
            if (c != null && c.Rules != null) foreach (var r in c.Rules) if (r != null) list.Add(r);
            return list;
        }

        bool HasRuleType(Type t)
        {
            for (int i = 0; i < Rules.Count; i++) if (Rules[i] != null && Rules[i].GetType() == t) return true;
            return false;
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
            SourceSet = set;
            BuildLive(set); // chosen + global (single-instance enabled chosen types override globals)
            for (int i = 0; i < Rules.Count; i++) Rules[i]?.HostLoad();
            if (_started) Reconcile();
        }

        // Convenience static lookup used by systems/rules that need a tunable.
        public static T Rule<T>() where T : GameRule => Active != null ? Active.Get<T>() : null;
    }
}
