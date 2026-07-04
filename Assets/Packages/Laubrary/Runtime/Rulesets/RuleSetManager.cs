using System.Linq;
using UnityEngine;

namespace Laubrary.Rulesets
{
    // Save/load of rule PRESETS, backed by RuleSet ScriptableObject assets (Resources/Rulesets/<name>).
    // Loading works at runtime (Resources) so a session can pick a ruleset: Load swaps the live RulesHost
    // over to the named asset. Authoring/saving the asset is an editor action (Rules Editor window).
    public static class RuleSetManager
    {
        public const string ResourceDir = "Rulesets";
        public const string Default = "Standard";

        // The GLOBAL ruleset: rules that apply under EVERY ruleset/scenario. A single-instance rule type the
        // chosen ruleset ENABLES overrides the global one (RulesHost merges; see BuildLive). Optional — null
        // if not authored.
        public const string GlobalName = "Global";
        public static RuleSet LoadGlobalAsset() => Resources.Load<RuleSet>(ResourceDir + "/" + GlobalName);

        // Names of every saved ruleset asset (excluding the Global set, which isn't a selectable ruleset).
        public static string[] List() =>
            Resources.LoadAll<RuleSet>(ResourceDir).Select(p => p.name).Where(n => n != GlobalName).Distinct().ToArray();

        public static RuleSet LoadAsset(string name) => Resources.Load<RuleSet>(ResourceDir + "/" + name);

        // Swap the live host over to the named ruleset. Returns true if found. Runtime-safe (Resources).
        public static bool Load(string name)
        {
            var set = LoadAsset(name);
            if (set == null)
            {
                Debug.LogWarning($"[RuleSet] '{name}' not found in Resources/{ResourceDir}.");
                return false;
            }
            if (RulesHost.Active != null) RulesHost.Active.LoadSet(set);
            return true;
        }
    }
}
