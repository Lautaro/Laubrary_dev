using System.Collections.Generic;
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
        /// The direction supplied by a general named event such as Fire. Appended to preserve existing data.
        EventDirection,
    }

    /// <summary>How an effect's visual is oriented when its event fires.</summary>
    public enum FxRotationMode { FaceEventDirection, None, FixedAngle }

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
        /// The Zoe body's declared renderers. A simple Zoe has its one renderer here; a composite Zoe has exactly
        /// its declared part renderers, never arbitrary descendants such as equipped items or transient FX.
        public IReadOnlyList<SpriteRenderer> BodyRenderers;
        /// Optional composite-part capability used by effects that deliberately target one named body part.
        public IPartLookup PartLookup;
        /// The Zoe's animated view, for sampling named meta-layer points. Null for a plain SpriteView Zoe.
        public IAnimatedView View;

        // ── raw event params (the typed in-params) ──
        /// The event's world point, already fallback-resolved to the Zoe's own position when the hit had none.
        public Vector2 HitPosition;
        /// The event's push direction (attacker → target), normalised; zero when the event carried none.
        public Vector2 Direction;
        /// Legacy name for a direction carried by hit/death. General events use <see cref="Direction"/> too.
        public Vector2 HitDirection { get => Direction; set => Direction = value; }
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
        /// Whether the current effect requested a horizontal mirror from its selected body part.
        public bool FlipX;

        /// The Zoe's transform anchor (falls back to the hit point when there's no transform).
        public Vector2 ZoePosition => Transform != null ? (Vector2)Transform.position : HitPosition;
        /// The Zoe's current sprite-bounds centre (a visual mid-point), falling back to its anchor.
        public Vector2 SpriteCenter => Renderer != null ? (Vector2)Renderer.bounds.center : ZoePosition;

        /// <summary>Resolve a position PICKER (<see cref="FxPlacementType"/>) against this context — the promoted
        /// form of the old placement resolution, mirroring it exactly: HitPosition → the (fallback-resolved) hit
        /// point; TargetOrigin → the transform anchor; TargetPosition → the sprite-bounds centre; MetaPoint → a
        /// named meta-layer's live world point, falling back to the sprite-bounds centre when the layer has
        /// nothing authored anywhere (or there's no view) — same "an unpainted frame costs accuracy, not the
        /// whole effect" rule <see cref="MuzzleTracker"/>/<see cref="ZoeMetaPositionResolver"/> already apply, so
        /// an effect placed at MetaPoint always spawns SOMEWHERE instead of silently never firing.
        /// <paramref name="metaLayerId"/> is only read for MetaPoint.</summary>
        public bool TryResolvePosition(FxPlacementType placement, string metaLayerId, out Vector2 pos)
        {
            switch (placement)
            {
                case FxPlacementType.HitPosition:    pos = HitPosition;  return true;
                case FxPlacementType.TargetOrigin:   pos = ZoePosition;  return true;
                case FxPlacementType.TargetPosition: pos = SpriteCenter; return true;
                case FxPlacementType.MetaPoint:
                    pos = TryResolveMetaPoint(metaLayerId, out var w) ? w : SpriteCenter;
                    return true;
                case FxPlacementType.BodyPart:
                    pos = ZoePosition;
                    return false;
                default:
                    pos = default;
                    return false;
            }
        }

        /// Resolves an entry's complete placement, including its optional declared body-part anchor and offset.
        public bool TryResolvePosition(FxEntry entry, out Vector2 pos)
        {
            if (entry != null && entry.placement == FxPlacementType.BodyPart)
            {
                var part = PartLookup != null ? PartLookup.FindPartTransform(entry.bodyPart) : null;
                if (part == null) { pos = SpriteCenter; return true; }
                var offset = entry.localOffset;
                var renderer = part.GetComponent<SpriteRenderer>();
                // TransformPoint already carries a transform-scale mirror. SpriteRenderer.flipX is draw-time only,
                // so it alone needs to mirror the local authored offset here.
                if (entry.mirrorOffsetWithFacing && renderer != null && renderer.flipX) offset.x = -offset.x;
                pos = part.TransformPoint(offset);
                return true;
            }
            return TryResolvePosition(entry != null ? entry.placement : FxPlacementType.TargetOrigin,
                entry != null ? entry.metaLayerId : "", out pos);
        }

        public bool ResolveFlipX(FxEntry entry)
        {
            if (entry == null || !entry.flipWithFacing) return false;
            Transform anchor = !string.IsNullOrEmpty(entry.bodyPart) && PartLookup != null
                ? PartLookup.FindPartTransform(entry.bodyPart) : Transform;
            var renderer = anchor != null ? anchor.GetComponent<SpriteRenderer>() : null;
            if (renderer == null) renderer = Renderer;
            return (View as IFlippableView)?.FlipX == true || (renderer != null && renderer.flipX) || (anchor != null && anchor.lossyScale.x < 0f);
        }

        public float ResolveRotationDeg(FxEntry entry)
        {
            if (entry == null || entry.rotation == FxRotationMode.None) return float.NaN;
            if (entry.rotation == FxRotationMode.FixedAngle) return entry.fixedAngleDeg;
            float angle = ResolveDirectionDeg(entry.direction);
            return float.IsNaN(angle) ? angle : angle + entry.angleOffsetDeg;
        }

        /// Ask the DATA which kind of layer this is (Point or Vector — never both), then sample it via whichever
        /// *Nearest lookup falls back across frames, exactly the convention MuzzleTracker/MuzzleVectorTracker and
        /// ZoeMetaPositionResolver already use for the same "Muzzle" id. False only when nothing is authored on
        /// this layer anywhere on the view (or there's no view at all).
        bool TryResolveMetaPoint(string metaLayerId, out Vector2 worldPos)
        {
            worldPos = default;
            if (View == null || string.IsNullOrEmpty(metaLayerId)) return false;
            switch (View.GetMetaLayerKind(metaLayerId))
            {
                case MetaLayerKind.Point:  return View.TryGetMetaPointNearest(metaLayerId, out worldPos);
                case MetaLayerKind.Vector: return View.TryGetMetaVectorNearest(metaLayerId, out worldPos, out _, out _);
                default: return false;
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
            return Direction.sqrMagnitude > 1e-6f
                ? Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg
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
