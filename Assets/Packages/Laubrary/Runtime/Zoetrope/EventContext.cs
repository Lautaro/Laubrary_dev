using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>Which of the event's direction in-params an effect reads.</summary>
    public enum DirectionParam
    {
        /// The hit's push direction (attacker → target). Omni-directional (NaN) when the event carried none.
        HitDirection,
        /// No direction — the effect fires omni-directionally.
        None,
        /// The angle from the effect's spawn POSITION toward the Zoe's visual CENTRE — so an effect that lands on a
        /// side angles inward (a glancing look). NaN when the spawn point IS the centre. (Appended last to keep the
        /// existing serialized HitDirection=0 / None=1 values stable.)
        CentreAngle,
        /// A fresh random angle each time the effect fires. For a knockback that should scatter rather than
        /// read as a consistent shove — the difference between a crowd being pushed apart and a crowd being
        /// pushed in formation. (Appended last to keep existing serialized values stable.)
        Random,
    }

    /// <summary>Which of the event's scalar in-params an effect reads.</summary>
    public enum ScalarParam
    {
        /// The event's amount (e.g. the damage dealt), so an effect can size itself to the hit.
        Amount,
        /// No scalar (zero).
        None,
    }

    /// <summary>
    /// The typed in-params of a Zoe event, filled by the trigger (<see cref="ReactionFxPlayer"/> from a
    /// <see cref="Combat2D.DamageInfo"/> on Hit/Death) and read by every <see cref="IEffect"/> the event fires.
    /// It holds the target Zoe's live data — its <see cref="Transform"/>, <see cref="Health"/>, the current lauminary
    /// frame's <see cref="Renderer"/> (for colour-sampling / bounds-centre placement) and its animated
    /// <see cref="View"/> (for named meta-layer points) — plus the raw event params (hit position/direction,
    /// amount). This is a FIXED per-event schema (Hit carries hitPosition + hitDirection + amount), per the
    /// locked design — NOT a generic named-param bag.
    ///
    /// An effect's picked position / direction / scalar param is resolved against this context via
    /// <see cref="TryResolvePosition"/> / <see cref="ResolveDirectionDeg"/> / <see cref="ResolveScalar"/>; the
    /// resolved values are stamped into <see cref="Position"/> / <see cref="DirectionDeg"/> / <see cref="Scalar"/>
    /// just before <see cref="IEffect.Apply"/>, so an <see cref="ICombatFx"/> spawns at exactly the same point
    /// its <see cref="FxPlacementType"/> resolved to before this generalization.
    /// </summary>
    public sealed class EventContext
    {
        // ── target Zoe live data (present when driven by ReactionFxPlayer; null for a bare point-play) ──
        /// The Zoe's transform (its registration anchor). Null for a bare <see cref="ReactionFx.Play"/>.
        public Transform Transform;
        /// The Zoe's Health. May be null.
        public Health Health;
        /// The Zoe's live body renderer — the CURRENT lauminary frame — for colour-sampling and bounds-centre placement.
        public SpriteRenderer Renderer;
        /// The Zoe's animated view, for sampling named meta-layer points. Null for a plain SpriteView Zoe.
        public IAnimatedView View;

        // ── raw event params (the typed in-params) ──
        /// The event's world point, already fallback-resolved to the Zoe's own position when the hit had none.
        public Vector2 HitPosition;
        /// The event's push direction (attacker → target), normalised; zero when the event carried none.
        public Vector2 HitDirection;
        /// The event's scalar magnitude (e.g. the damage amount).
        public float Amount;
        /// Seconds the driving event has left at the moment the current effect fires — the armed reaction
        /// clip's remaining play time, re-stamped by <see cref="ReactionFxPlayer"/> before every fire (full
        /// length for Immediate entries, what's left for OnFrame ones). 0 when unknown: no clip, no animated
        /// view, or a clip with no fixed end (a zoned strip) — timed playback bindings (a Body-SpriteFx card's
        /// Loop / RunAtEnd / PingPong) then degrade to a single play.
        public float EventSecondsRemaining;

        // ── resolved-for-the-current-effect (stamped from the effect's picked params just before Apply) ──
        /// The world position the current effect's picked position param resolved to.
        public Vector2 Position;
        /// The aim (degrees) the current effect's picked direction param resolved to; NaN = omni-directional.
        public float DirectionDeg = float.NaN;
        /// The value the current effect's picked scalar param resolved to.
        public float Scalar;

        /// The Zoe's transform anchor (falls back to the hit point when there's no transform).
        public Vector2 ZoePosition => Transform != null ? (Vector2)Transform.position : HitPosition;
        /// The Zoe's current sprite-bounds centre (a visual mid-point), falling back to its anchor.
        public Vector2 SpriteCenter => Renderer != null ? (Vector2)Renderer.bounds.center : ZoePosition;

        /// <summary>Resolve a position PICKER (<see cref="FxPlacementType"/>) against this context — the promoted
        /// form of the old placement resolution, mirroring it exactly: HitPosition → the (fallback-resolved) hit
        /// point; TargetOrigin → the transform anchor; TargetPosition → the sprite-bounds centre; MetaPoint → a
        /// named meta-layer's live world point (false when nothing is painted / there's no view).
        /// <paramref name="metaLayerId"/> is only read for MetaPoint.</summary>
        public bool TryResolvePosition(FxPlacementType placement, string metaLayerId, out Vector2 pos)
        {
            switch (placement)
            {
                case FxPlacementType.HitPosition:    pos = HitPosition;  return true;
                case FxPlacementType.TargetOrigin:   pos = ZoePosition;  return true;
                case FxPlacementType.TargetPosition: pos = SpriteCenter; return true;
                case FxPlacementType.MetaPoint:
                    if (View != null && View.TryGetMetaPoint(metaLayerId, out var w)) { pos = w; return true; }
                    pos = default;
                    return false;
                default:
                    pos = default;
                    return false;
            }
        }

        /// <summary>Resolve a direction PICKER to an aim in degrees (NaN = omni-directional), matching the legacy
        /// direction handling exactly (atan2 of the hit direction, NaN when the hit carried none).</summary>
        public float ResolveDirectionDeg(DirectionParam param)
        {
            if (param == DirectionParam.None) return float.NaN;
            // Rolled per FIRING, not per effect, so two Zoes hit by the same shot scatter independently.
            if (param == DirectionParam.Random) return UnityEngine.Random.Range(0f, 360f);
            if (param == DirectionParam.CentreAngle)
            {
                // Angle from the (already-stamped) spawn Position toward the Zoe's visual centre.
                Vector2 toCentre = SpriteCenter - Position;
                return toCentre.sqrMagnitude > 1e-6f ? Mathf.Atan2(toCentre.y, toCentre.x) * Mathf.Rad2Deg : float.NaN;
            }
            return HitDirection.sqrMagnitude > 1e-6f
                ? Mathf.Atan2(HitDirection.y, HitDirection.x) * Mathf.Rad2Deg
                : float.NaN;
        }

        /// <summary>Resolve a scalar PICKER to a value.</summary>
        public float ResolveScalar(ScalarParam param) => param == ScalarParam.Amount ? Amount : 0f;

        /// <summary>Build a context for a bare "just play the effect at this point" call — no live Zoe data, the
        /// position and direction taken as given (used by <see cref="ReactionFx.Play"/>, which has no view to
        /// resolve placement against and so spawns every effect at the supplied point, exactly as before).</summary>
        public static EventContext ForPoint(Vector2 worldPos, float directionDeg = float.NaN)
            => new EventContext { HitPosition = worldPos, Position = worldPos, DirectionDeg = directionDeg };
    }
}
