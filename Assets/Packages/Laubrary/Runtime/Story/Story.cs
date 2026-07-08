using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Rulesets;

namespace Laubrary.Story
{
    // The top-level scenario/scene object: a Story owns its branching Screenplay, sets up an initial ruleset,
    // and carries presentation metadata. It replaces "a ruleset IS the scenario" — now the Story is the
    // scenario and the ruleset is one of the things it configures.
    //
    // Rules can come from two places, merged (local overrides public):
    //   • PublicRuleSet — an optional shared, discoverable RuleSet asset (sets you reuse across many stories).
    //   • LocalRules    — story-only rules authored INLINE here (not a separate asset, not discoverable from
    //                     anywhere else, so tuning one story can't disturb another).
    //
    // Reusable: lives in Laubrary and references only Laubrary.Rulesets + the Screenplay — any Rules-based game
    // can adopt it. Running the Screenplay still needs game services (presenter / rule-bridge), which the game
    // wires when it boots the Story.
    [CreateAssetMenu(menuName = "Laubrary/Story/Story", fileName = "Story")]
    public class Story : ScriptableObject
    {
        [Header("Presentation")]
        public string DisplayName;
        [TextArea] public string Description;
        public Sprite Thumbnail;

        [Header("Flow")]
        [Tooltip("The branching Screenplay this story runs.")]
        public Screenplay Screenplay;

        [Header("Rules")]
        [Tooltip("Optional shared/discoverable ruleset used as the base for this story.")]
        public RuleSet PublicRuleSet;

        [Tooltip("Story-only rules, authored inline here — NOT a separate asset and not discoverable elsewhere. " +
                 "They merge OVER the public ruleset: a single-instance local rule replaces the public one of its type.")]
        [SerializeReference] public List<GameRule> LocalRules = new List<GameRule>();

        // The effective ruleset for this story = PublicRuleSet ∪ LocalRules (local single-instance rules override
        // public ones of the same type). Returned as a fresh runtime RuleSet that references the source rule
        // instances; RulesHost clones it on load, so the source assets are never mutated. The GLOBAL ruleset is
        // still merged on top by RulesHost.BuildLive.
        public RuleSet BuildRuleSet()
        {
            var rs = ScriptableObject.CreateInstance<RuleSet>();
            rs.name = string.IsNullOrEmpty(DisplayName) ? name : DisplayName;
            rs.Rules = new List<GameRule>();

            var localSingle = new HashSet<Type>();
            if (LocalRules != null)
                foreach (var r in LocalRules)
                    if (r != null && !r.AllowMultiple) localSingle.Add(r.GetType());

            if (PublicRuleSet != null && PublicRuleSet.Rules != null)
                foreach (var r in PublicRuleSet.Rules)
                {
                    if (r == null) continue;
                    if (!r.AllowMultiple && localSingle.Contains(r.GetType())) continue; // local wins
                    rs.Rules.Add(r);
                }

            if (LocalRules != null)
                foreach (var r in LocalRules)
                    if (r != null) rs.Rules.Add(r);

            return rs;
        }
    }
}
