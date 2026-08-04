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
        [Tooltip("The effects applied in order while the stack plays — order matters, because they clamp and " +
                 "warp and so do not commute. Colour/mask effects recolour pixels in place, geometry effects " +
                 "warp the picture, and whole-frame effects (outline, bloom, drop shadow) read a pixel's " +
                 "neighbours. Each effect's animatable values are resolved at the current life every frame.")]
        [SerializeReference] public List<PyreModifier> modifiers = new List<PyreModifier>();

        [Tooltip("FALLBACK length, used only when nothing else says how long this stack should take. A stack is a SHAPE over normalized life (0->1), not a schedule, so its host owns the timebase: a Zoe event with a clip runs the stack over that clip's length, an event without one uses the effect's own FX Seconds, and this value applies only when a stack is played from neither. Re-timing an animation therefore re-times the effect riding it, instead of the effect finishing early.")]
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

        /// A stack no longer keeps a clock of its own. This quantised the effect onto a fixed grid measured in
        /// absolute SECONDS, so the same stack came out with seven steps on a 0.7 s event and twenty on a 2 s
        /// one — the shape changed with the host's duration, which is the one thing a stack is not supposed to
        /// do. Kept serialized so authored assets load unchanged; never read.
        [HideInInspector, System.Obsolete("The host owns the timebase; a stack is a shape over normalized life.")]
        public float targetFps = 0f;

        /// Life IS progress. Nothing sits between the host's clock and the effects any more.
        ///
        /// There used to be a stack-wide "life remap" envelope here, and it was a trapdoor. Every effect's
        /// animation is sampled at the value this returns, so a remap that went flat pinned LIFE to a
        /// constant and every authored envelope in the stack silently collapsed to one value — the tool then
        /// looks exactly like a tool whose animation does not work, with the real cause three sections away
        /// and nothing pointing at it. That is what happened: an asset here sat at a flat 0.95, so every
        /// parameter returned its value at 0.95 forever. Worse, the control pinned its own end anchors, so it
        /// could not be dragged back to identity by hand.
        ///
        /// It also had no job left. Every parameter already carries its own envelope over the same 0→1, which
        /// is the whole model; a second global warp on top was a third opinion about time in a system where
        /// the HOST owns time and a stack owns only shape. The field stays serialized so no authored asset
        /// errors on load, and is simply never read again.
        public float SampleEnvelope(float progress01) => Mathf.Clamp01(progress01);
    }
}
