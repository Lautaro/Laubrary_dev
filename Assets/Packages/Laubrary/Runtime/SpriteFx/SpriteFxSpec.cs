using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// A persisted, reusable SpriteFx recipe — a "SpriteFx Stack": a stateless colour/mask stack (the gather-free
    /// PixelModifier family) plus the timeline that plays it. Authored ONCE as an asset and referenced by any
    /// <see cref="SpriteFxFilter"/> (or a future runtime player), so the same effect — a hurt flash, a dissolve, a
    /// tint pulse — can be reused across many entities and browsed/tagged like every other Laubrary asset instead of
    /// being hand-copied inline onto each component.
    ///
    /// (Named by Laubrary's asset convention — <c>ChunkSpec</c>, <c>PyrePlusSpec</c> — so it does not collide with
    /// the static stack-runner <see cref="SpriteFxStack"/>; the user-facing name everywhere is "SpriteFx Stack".)
    ///
    /// The field set mirrors <see cref="SpriteFxFilter"/>'s own inline fields exactly, so a filter can source them
    /// from an asset with NO behavioural change: the static <see cref="SpriteFxFilter.Apply"/> already takes an
    /// <c>IReadOnlyList&lt;PixelModifier&gt;</c>, so an asset-sourced list drives the identical code path.
    ///
    /// Only the "shaped" gather-free pixel effects actually run at runtime — <see cref="SpriteFxStack.IsShaped"/>
    /// is the authoritative set and <see cref="SpriteFxStack.Resolve"/> skips anything else — which is why the
    /// list is typed to <c>PixelModifier</c> rather than the whole PyreModifier family.
    ///
    /// Ships ZERO assets (the Laubrary rule): the package never contains a SpriteFx Stack asset; a host project
    /// creates its own via Assets ▸ Create ▸ Laubrary ▸ SpriteFx ▸ Stack.
    [CreateAssetMenu(fileName = "New SpriteFx Stack", menuName = "Laubrary/SpriteFx/Stack", order = 1)]
    public class SpriteFxSpec : ScriptableObject
    {
        [Tooltip("The stateless colour/mask effects applied in order while the stack plays — order matters, " +
                 "because these effects clamp and so do not commute. Only the gather-free pixel family runs " +
                 "here (the Add menu offers exactly that set); each effect's animatable values are resolved at " +
                 "the current life every frame.")]
        [SerializeReference] public List<PixelModifier> modifiers = new List<PixelModifier>();

        [Tooltip("How long one play-through lasts, in seconds.")]
        [Min(0.001f)] public float duration = 0.15f;

        [Tooltip("Optional easing/remap of raw progress (0->1 over Duration) into the LIFE value fed to every " +
                 "effect's animatable curves. Identity by default (life = progress); a triangle (0->1->0) turns a " +
                 "monotonic effect curve into a pulse, or an ease softens the ends.")]
        [HideInInspector, System.Obsolete("Authoring moved to envelopePoints; kept so old assets keep their shape.")]
        public AnimationCurve envelope = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Optional easing/remap of raw progress (0->1 over Duration) into the LIFE value fed to every " +
                 "effect's animatable curves. Identity by default (life = progress); a triangle (0->1->0) turns a " +
                 "monotonic effect curve into a pulse, or an ease softens the ends.")]
        public List<ZUIEnvelopePoint> envelopePoints = new List<ZUIEnvelopePoint>();

        /// The envelope as POINTS, upgraded on first use from the frozen AnimationCurve below.
        ///
        /// The curve was drawn by a native Unity CurveField, which is the one control ZUI has a standing rule
        /// against — a ZUI window must not open a foreign editor for its own data. ZuiEnvelope edits points,
        /// so the data moved to points. The legacy field keeps its name and its value and is never written
        /// again: an asset authored before this migration is sampled into points the first time it is asked
        /// for, so nothing loses its shape and nothing needs a re-author pass.
        public List<ZUIEnvelopePoint> Envelope
        {
            get
            {
                if (envelopePoints == null) envelopePoints = new List<ZUIEnvelopePoint>();
                if (envelopePoints.Count == 0) SpriteFxEnvelopeMigration.Seed(envelopePoints, envelope);
                return envelopePoints;
            }
        }

        [Tooltip("Seed for any hashing effect (LayerDissolve scatter, AlphaMask noise). Irrelevant for a plain " +
                 "Brightness/Tint flash.")]
        public int seed = 12345;

        [Tooltip("Own clock: how many times per second this stack's time advances while it plays. 0 (default) = " +
                 "continuous — the effect re-evaluates every rendered frame, riding whatever animation drives it. " +
                 "Set a rate to step the effect on its own fixed grid instead, independent of the animation's " +
                 "fps — a fast brightness flicker over a slow 4-fps reel, or a deliberately chunky retro fade. " +
                 "Hashing effects re-roll per STEP (not per rendered frame), which is what makes a dither/dissolve " +
                 "read as a flicker at this rate.")]
        [Min(0f)] public float targetFps = 0f;

        /// Sample the life-remap envelope at a raw progress in [0,1] (identity if no curve). Mirrors
        /// <see cref="SpriteFxFilter"/>'s own SampleEnvelope so an asset-driven filter and an inline one behave
        /// identically.
        public float SampleEnvelope(float progress01)
        {
            var pts = Envelope;
            return pts.Count == 0 ? progress01
                                  : ZUIEnvelopeEvaluator.Evaluate(pts, Mathf.Clamp01(progress01), 1f);
        }
    }
}
