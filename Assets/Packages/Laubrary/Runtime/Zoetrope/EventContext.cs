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
        /// Straight ahead for the body as it is drawn right now: right, or left when mirrored. For moves that
        /// carry the character forward (a lunging chop, a roll) whichever way it happens to face.
        /// Appended to preserve existing data.
        Facing,
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
        /// <summary>An ONGOING source for this event's direction, when the thing that raised it has one that keeps
        /// answering after the raise — a weapon's live aim, which is what a muzzle flash still riding the barrel
        /// needs to point along. Null (the default, and what a hit or a death supplies) means the direction
        /// stamped at raise time is final, exactly as before this existed. A source that answers zero is treated
        /// as "nothing to say right now" and falls back to <see cref="Direction"/>, so a weapon that stops
        /// reporting an aim never silently snaps an effect to a meaningless angle.</summary>
        public System.Func<Vector2> DirectionSource;

        /// The event's direction AS OF NOW: the ongoing source's answer when there is one, else the direction the
        /// event was raised with. This is what direction pickers resolve against, so re-asking a resolver while an
        /// effect plays gives the current answer rather than the one frozen at the raise.
        public Vector2 CurrentDirection
        {
            get
            {
                if (DirectionSource == null) return Direction;
                var live = DirectionSource();
                return live.sqrMagnitude > 1e-6f ? live : Direction;
            }
        }
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
                    // This overload carries no part name or offset — those live on the FxEntry (see the entry
                    // overload below). Placed at the sprite centre, the same "still spawns somewhere" fallback an
                    // unknown part gets there, rather than refusing to spawn.
                    pos = SpriteCenter;
                    return true;
                default:
                    pos = default;
                    return false;
            }
        }

        /// Resolves an entry's complete placement, including its optional declared body-part anchor and offset.
        public bool TryResolvePosition(FxEntry entry, out Vector2 pos)
        {
            if (entry == null) return TryResolvePosition(FxPlacementType.TargetOrigin, "", out pos);
            if (entry.placement == FxPlacementType.BodyPart)
                return TryResolveBodyPart(entry.bodyPart, entry.localOffset, entry.mirrorOffsetWithFacing, out pos);
            return TryResolvePosition(entry.placement, entry.metaLayerId, out pos);
        }

        /// <summary>Is an INTERFACE-typed live reference still usable? `!= null` on an interface never binds
        /// Unity's fake-null operator — that overload only exists for a static type of <c>UnityEngine.Object</c> —
        /// so a destroyed MonoBehaviour reached through <see cref="IAnimatedView"/> or <see cref="IPartLookup"/>
        /// still reads as non-null and throws MissingReference on the next call into it.
        ///
        /// <para>Harmless while these were only ever read ONCE, synchronously, on the frame the event fired: the
        /// Zoe that fired it was provably alive. A FOLLOWED effect re-asks them every frame for as long as it
        /// plays, and by then the Zoe can be gone — shot down mid-flash — so the check has to be the real one.</para></summary>
        static bool Alive(object o)
        {
            if (o == null) return false;
            // Static type is right here, so this IS Unity's fake-null comparison.
            if (o is UnityEngine.Object uo) return uo != null;
            return true;   // a plain C# implementation lives as long as it is referenced
        }

        /// A declared body part's pivot plus a local offset (optionally mirrored with the part's facing). An
        /// undeclared / unknown part falls back to the sprite centre — the effect still spawns SOMEWHERE, the
        /// same rule MetaPoint applies to an unpainted layer.
        bool TryResolveBodyPart(string partName, Vector2 localOffset, bool mirrorWithFacing, out Vector2 pos)
        {
            var part = Alive(PartLookup) && !string.IsNullOrEmpty(partName) ? PartLookup.FindPartTransform(partName) : null;
            if (part == null) { pos = SpriteCenter; return true; }
            var offset = localOffset;
            var renderer = part.GetComponent<SpriteRenderer>();
            // TransformPoint already carries a transform-scale mirror. SpriteRenderer.flipX is draw-time only,
            // so it alone needs to mirror the local authored offset here.
            if (mirrorWithFacing && renderer != null && renderer.flipX) offset.x = -offset.x;
            pos = part.TransformPoint(offset);
            return true;
        }

        /// Does the body (or the entry's chosen part) currently face LEFT — a mirrored view, a mirrored
        /// renderer, or a negative X scale.
        bool BodyFacesLeft(FxEntry entry)
        {
            Transform anchor = entry != null && !string.IsNullOrEmpty(entry.bodyPart) && Alive(PartLookup)
                ? PartLookup.FindPartTransform(entry.bodyPart) : Transform;
            var renderer = anchor != null ? anchor.GetComponent<SpriteRenderer>() : null;
            if (renderer == null) renderer = Renderer;
            return (Alive(View) ? (View as IFlippableView)?.FlipX == true : false)
                   || (renderer != null && renderer.flipX) || (anchor != null && anchor.lossyScale.x < 0f);
        }

        /// <summary>Resolve how an entry's spawned visual is ORIENTED, in one go: the world angle its forward
        /// should point at (NaN = upright) and whether it is mirrored. One call, because a Random direction is
        /// rolled per firing and the two answers must agree.
        ///
        /// <para>The flip rule: with a resolved rotation, "flip with facing" mirrors whenever that rotation
        /// points LEFT — the effect then draws mirrored at a small angle instead of the right-facing art
        /// rotated 180° and upside down (the effect compensates the angle for the mirror itself, e.g. through
        /// its Pyre anchor). With no rotation, it mirrors with the body's own facing.</para></summary>
        public void ResolveOrientation(FxEntry entry, out float rotationDeg, out bool flipX)
        {
            rotationDeg = float.NaN;
            flipX = false;
            if (entry == null) return;
            if (entry.rotation == FxRotationMode.FixedAngle) rotationDeg = entry.fixedAngleDeg;
            else if (entry.rotation == FxRotationMode.FaceEventDirection)
            {
                float aim = ResolveDirectionDeg(entry.direction);
                if (!float.IsNaN(aim)) rotationDeg = aim + entry.angleOffsetDeg;
            }
            if (!entry.flipWithFacing) return;
            // Strictly left — a straight-up / straight-down result (cos ≈ 0 in float) stays unmirrored.
            flipX = float.IsNaN(rotationDeg) ? BodyFacesLeft(entry)
                                             : Mathf.Cos(rotationDeg * Mathf.Deg2Rad) < -1e-4f;
        }

        /// The mirror half of <see cref="ResolveOrientation"/>.
        public bool ResolveFlipX(FxEntry entry) { ResolveOrientation(entry, out _, out bool flip); return flip; }

        /// The rotation half of <see cref="ResolveOrientation"/> (NaN = upright).
        public float ResolveRotationDeg(FxEntry entry) { ResolveOrientation(entry, out float rot, out _); return rot; }

        /// Ask the DATA which kind of layer this is (Point or Vector — never both), then sample it via whichever
        /// *Nearest lookup falls back across frames, exactly the convention MuzzleTracker/MuzzleVectorTracker and
        /// ZoeMetaPositionResolver already use for the same "Muzzle" id. False only when nothing is authored on
        /// this layer anywhere on the view (or there's no view at all).
        bool TryResolveMetaPoint(string metaLayerId, out Vector2 worldPos)
        {
            worldPos = default;
            if (!Alive(View) || string.IsNullOrEmpty(metaLayerId)) return false;
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
            if (param == DirectionParam.Facing) return BodyFacesLeft(null) ? 180f : 0f;
            if (param == DirectionParam.CentreAngle)
            {
                // Angle from the (already-stamped) spawn Position toward the Zoe's visual centre.
                Vector2 toCentre = SpriteCenter - Position;
                return toCentre.sqrMagnitude > 1e-6f ? Mathf.Atan2(toCentre.y, toCentre.x) * Mathf.Rad2Deg : float.NaN;
            }
            // CurrentDirection, not the raise-time field: an effect that re-asks while it plays (a followed muzzle
            // flash) must get where the gun points NOW. With no ongoing source the two are the same value.
            var dir = CurrentDirection;
            return dir.sqrMagnitude > 1e-6f
                ? Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg
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
