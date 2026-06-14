using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Rulesets
{
    // A ruleset is ONE data asset: a polymorphic list of GameRule instances (via [SerializeReference]).
    // RulesHost clones one of these at boot and drives it. Saved/loaded by RuleSetManager (Resources/Rulesets).
    [CreateAssetMenu(menuName = "Laubrary/Rule Set", fileName = "RuleSet")]
    public class RuleSet : ScriptableObject
    {
        [SerializeReference] public List<GameRule> Rules = new List<GameRule>();

        // Deep copy (clones the SerializeReference rule instances) so play-mode edits to the live rules
        // don't dirty the source asset.
        public RuleSet Clone() => Instantiate(this);
    }
}
