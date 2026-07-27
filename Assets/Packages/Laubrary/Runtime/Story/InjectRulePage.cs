using UnityEngine;
using Laubrary.Rulesets;

namespace Laubrary.Story
{
    // Injects a fully-authored GameRule instance into the live RulesHost when a bookmark reaches it,
    // optionally replacing the active single-instance rule of the same type (restored when the injection is
    // disposed, e.g. on Screenplay teardown). Only depends on the canonical GameRule type — unlike a project's
    // OWN rule subclasses, this page itself is game-agnostic and belongs in Story's own palette, not a game's.
    [System.Serializable]
    public class InjectRulePage : Page
    {
        [SerializeReference] public GameRule Rule;
        [Tooltip("Replace the currently-active single-instance rule of the same type (restored when disposed).")]
        public bool ReplaceCurrent = true;

        public override string Enter(StoryContext ctx)
        {
            if (ctx.Rules != null && Rule != null)
            {
                // Clone so we never mutate the Screenplay asset's own authored instance at runtime.
                GameRule clone = null;
                try { clone = (GameRule)JsonUtility.FromJson(JsonUtility.ToJson(Rule), Rule.GetType()); } catch { }
                var d = ctx.Rules.Inject(clone ?? Rule, ReplaceCurrent);
                ctx.Runner?.RegisterDisposable(d);
            }
            return "out";
        }

        public override string Describe() => $"Inject rule: {(Rule != null ? Rule.GetType().Name : "<none>")}";
    }
}
