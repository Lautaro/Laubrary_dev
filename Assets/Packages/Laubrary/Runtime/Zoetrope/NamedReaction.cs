using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// A reaction the character plays under a name of your choosing — "teleport-in", "spawn", "taunt".
    ///
    /// Deliberately IN PARALLEL to Zoe.hit and Zoe.death rather than replacing them. Those two are not merely
    /// named reactions: hit carries typed combat params and the invulnerability window, and death gates
    /// ZoeState.CanAct and drives deathDisposal. Folding them into a string-keyed list would mean
    /// special-casing two entries anyway, and would let a typo silently disable dying.
    ///
    /// Everything else, though, has no typed semantics — spawn and shoot are just "play this, here" — so
    /// they belong here as conventional ids rather than as new fixed fields. Otherwise the model grows a tail
    /// of half-special cases, each needing its own plumbing.
    [System.Serializable]
    public class NamedReaction
    {
        [Tooltip("The name this reaction is raised by. Typed ONCE, here; every consumer picks from the "+
                 "declared list instead of retyping it. Keep it stable — renaming it breaks whatever raises it.")]
        public string id = "";

        [Tooltip("What playing it does: the clip, an optional SpriteFx on the body, and the effect list — "+
                 "the same shape hit and death use.")]
        public ReactionFx reaction = new ReactionFx();
    }
}
