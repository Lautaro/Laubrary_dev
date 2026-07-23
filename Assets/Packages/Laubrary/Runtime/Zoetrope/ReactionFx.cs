using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>When an FX entry spawns: right away at Placement (no animation needed), or in sync with a
    /// named FrameEvent as the reaction's <see cref="ReactionFx.clip"/> plays. A MetaLayer (Point or Shape)
    /// never fires anything itself — it's queryable data, only meaningful as a <see cref="FxPlacementType"/>.</summary>
    public enum FxTriggerType { Immediate, FrameEvent }

    /// <summary>Where an FX entry spawns.</summary>
    public enum FxPlacementType
    {
        /// The DamageInfo's world point (the collision point). A fixed point in space — Follow is meaningless here.
        HitPosition,
        /// The character's current sprite bounds centre (a visual mid-point, distinct from its registration anchor).
        TargetPosition,
        /// The character's transform.position (the registration anchor — "the crosshair in the Animation Builder").
        TargetOrigin,
        /// A named Point-mode MetaLayer's current pixel (see <see cref="FxEntry.metaLayerId"/>).
        MetaPoint,
    }

    /// <summary>One "spawn this effect, here, when this happens" binding inside a <see cref="ReactionFx"/>'s FX
    /// list. <see cref="fx"/> is pluggable (e.g. a <c>PyreChunksFx</c> bundling a blast + chunk burst) so core
    /// Zoetrope stays free of any specific VFX module, same as <see cref="ICombatFx"/> everywhere else.</summary>
    [System.Serializable]
    public class FxEntry
    {
        public FxTriggerType trigger = FxTriggerType.Immediate;
        [Tooltip("FrameEvent name to match, when trigger == FrameEvent.")]
        public string eventName = "";

        public FxPlacementType placement = FxPlacementType.HitPosition;
        [Tooltip("Point-mode MetaLayer id to sample, when placement == MetaPoint.")]
        public string metaLayerId = "";

        [Tooltip("Keep re-sampling Placement every frame and move the spawned effect with it, instead of " +
                 "spawning once and letting it live on its own. Meaningless for HitPosition (a fixed world " +
                 "point) — ignored there.")]
        public bool follow;

        [SerializeReference] public ICombatFx fx;
    }

    /// <summary>Replaces the old separate <c>Zoe.hit</c>/<c>Zoe.death</c> (VFX-only) + <c>Zoe.hitReaction</c>
    /// (clip-only) split: one reaction owns BOTH which clip plays AND the FX triggered off that same clip's
    /// authored events/meta-layers, so there's exactly one place to author "what happens when this character
    /// gets hurt" (or dies) instead of two disconnected sections that had to be kept in sync by hand.</summary>
    [System.Serializable]
    public class ReactionFx
    {
        [Tooltip("The clip this reaction plays (via the view's IAnimatedView, if it provides one) and sources " +
                 "FrameEvent/MetaLayer names from for the FX list below. Empty = no clip — Immediate-trigger " +
                 "FX still fire normally (this is what keeps a plain SpriteView Zoe's hit VFX working).")]
        public string clip = "";

        public List<FxEntry> fx = new List<FxEntry>();

        public bool IsEmpty => string.IsNullOrEmpty(clip) && (fx == null || fx.Count == 0);
    }
}
