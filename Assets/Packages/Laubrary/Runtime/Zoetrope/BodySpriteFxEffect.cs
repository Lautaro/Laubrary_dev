using UnityEngine;
using Laubrary.SpriteFx;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A Zoe-event effect that applies a <see cref="SpriteFxSpec"/> (a "SpriteFx Stack") to the Zoe's OWN body
    /// renderer for the stack's duration — a hurt/death flash, tint or dissolve that RIDES the live animation via a
    /// <see cref="SpriteFxFilter"/> on the body's <see cref="SpriteRenderer"/>. This is the same behaviour as the
    /// top-level <see cref="ReactionFx.bodyFx"/> slot (KEPT and still fired by
    /// <see cref="ReactionFxPlayer.PlayBodyFx"/>; this effect is purely additive), now promoted to a first-class,
    /// list-orderable palette effect (ZOE_EVENTS_DESIGN.md step 3). Lives in Zoetrope CORE: Zoetrope depends DOWN
    /// on SpriteFx (a legal downward asmdef dependency — SpriteFx never references Zoetrope), the very edge
    /// <see cref="ReactionFxPlayer.PlayBodyFx"/> already uses.
    /// </summary>
    [System.Serializable]
    public class BodySpriteFxEffect : IEffect
    {
        [Tooltip("The SpriteFx Stack applied to the Zoe's own body sprite the instant this effect fires — a hurt/" +
                 "death flash, tint or dissolve riding on top of the live animation (via a SpriteFxFilter added to " +
                 "the body renderer). Empty = nothing.")]
        public SpriteFxSpec stack;

        public bool IsEmpty => stack == null;

        public void Apply(EventContext ctx)
        {
            if (stack == null) return;
            var sr = ctx.Renderer;
            if (sr == null) return;
            var filter = sr.GetComponent<SpriteFxFilter>();
            if (filter == null) filter = sr.gameObject.AddComponent<SpriteFxFilter>();
            filter.stack = stack;
            filter.Play();
        }
    }
}
