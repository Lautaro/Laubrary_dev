using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// The result of resolving a <see cref="LauminationSet"/> against a direction angle. Pure value
    /// type — no allocation, no side effects. The runtime calls <see cref="LauminationSetResolver.Resolve"/>
    /// each frame it needs to update animation, and applies the three fields to its part's renderer /
    /// animator / transform.
    /// </summary>
    public readonly struct LauminationResolution
    {
        /// The laumination that should play. May be null if the set has no resolvable member.
        public readonly Laumination Laumination;
        /// True if the runtime should mirror the sprite horizontally (SpriteRenderer.flipX = true).
        /// Only ever true for Mirror and Members-with-built-in-mirror modes.
        public readonly bool FlipX;
        /// The rotation in degrees the runtime should write to the part's transform.
        /// Zero for non-rotation modes (the laumination's own pivot decides facing).
        /// For Rotate and MembersRotate modes this is the snap-stepped or free angle.
        public readonly float RotationDeg;
        /// The angle this resolution was actually authored for. For nearest-member modes this is the
        /// nearest authored member's angle. Useful for "did the direction actually change?" debouncing
        /// — when ResolvedAngleDeg matches the last resolution, the runtime can skip the Play() call.
        public readonly float ResolvedAngleDeg;
        /// Empty (default) = play Laumination normally, from frame 0, looping. Non-empty = the winning
        /// member opted into "rotation sheet" mode (see LauminationSetMember.zoneName) — after selecting
        /// Laumination, jump to this named zone instead of playing from the start. Purely additive: existing
        /// callers that ignore this field see exactly today's behaviour.
        public readonly string ZoneName;
        // Stored offset by one so that `default` (i.e. LauminationResolution.None) yields FrameIndex = -1,
        // "no frame requested". A bare int field would default to 0 — a perfectly VALID frame — and any
        // consumer that checked HasFrame before checking Laumination would silently hold frame 0 of nothing.
        readonly int _frameIndexPlusOne;

        /// -1 = play Laumination normally; the caller decides the frame. >= 0 = the set is a
        /// <see cref="DirectionMode.Rotation"/> sheet and THIS frame of it is the resolved direction, so the
        /// caller should hold that exact frame. Replaces the zone-name indirection for the regular case:
        /// nothing needs naming when the frame index IS the direction.
        public int FrameIndex => _frameIndexPlusOne - 1;
        /// Whether this resolution names an explicit frame to hold (a Rotation sheet).
        public bool HasFrame => _frameIndexPlusOne > 0;

        public LauminationResolution(Laumination l, bool flipX, float rotDeg, float resolvedAngle,
                                     string zoneName = "", int frameIndex = -1)
        {
            Laumination = l; FlipX = flipX; RotationDeg = rotDeg; ResolvedAngleDeg = resolvedAngle;
            ZoneName = zoneName; _frameIndexPlusOne = frameIndex + 1;
        }

        public static LauminationResolution None => default;
    }

    /// <summary>
    /// Pure resolver: given a <see cref="LauminationSet"/> and a direction angle, returns which
    /// laumination to play, whether to mirror it, and how much to rotate it. No state, no allocations,
    /// no Unity dependencies beyond <see cref="Mathf"/> — fully unit-testable.
    ///
    /// Angle convention: 0° = up (+Y), increasing clockwise in screen space (Unity 2D). This matches
    /// <see cref="Vector2.ToDir8"/>/<see cref="Vector2.ToDir16"/> in Laubrary.Cookbook2D, so a Zoe's
    /// <c>aimDirection</c> / <c>heading</c> can be passed in directly without a conversion step.
    ///
    /// Latching (§8.1-break-3 in the movement design: "a stationary tank has no heading") is the
    /// caller's responsibility, NOT the resolver's — the resolver is a pure function. Use
    /// <see cref="LatchedDirection"/> to track the last non-zero direction and pass that into Resolve.
    /// </summary>
    public static class LauminationSetResolver
    {
        /// Resolve <paramref name="set"/> for the given angle. Returns <see cref="LauminationResolution.None"/>
        /// when the set is null, has no members, or the picked member has no laumination. Caller decides
        /// what "no resolution" means (skip the Play call, fall back to idle, etc).
        public static LauminationResolution Resolve(LauminationSet set, float angleDeg)
        {
            if (set == null || set.members == null || set.members.Count == 0)
                return LauminationResolution.None;

            // Wrap the angle to 0..360 once.
            float a = angleDeg;
            while (a < 0f) a += 360f;
            while (a >= 360f) a -= 360f;

            switch (set.directionMode)
            {
                case DirectionMode.None:
                    return ResolveNone(set, a);

                case DirectionMode.Mirror:
                    return ResolveMirror(set, a);

                case DirectionMode.Members:
                    return ResolveMembers(set, a);

                case DirectionMode.Rotate:
                    return ResolveRotate(set, a);

                case DirectionMode.MembersRotate:
                    return ResolveMembersRotate(set, a);

                case DirectionMode.Rotation:
                    return ResolveRotationSheet(set, a);

                default:
                    return LauminationResolution.None;
            }
        }

        static LauminationResolution ResolveNone(LauminationSet set, float angleDeg)
        {
            // One laumination, no direction, no mirror, no rotation.
            var m = FirstValid(set);
            return m == null
                ? LauminationResolution.None
                : new LauminationResolution(m.laumination, false, 0f, 0f, m.zoneName);
        }

        static LauminationResolution ResolveMirror(LauminationSet set, float angleDeg)
        {
            // One laumination, mirrored across the vertical axis. angles 180..360 → flipX.
            var m = FirstValid(set);
            if (m == null) return LauminationResolution.None;
            bool flip = angleDeg > 180f;
            return new LauminationResolution(m.laumination, flip, 0f, flip ? 180f : 0f, m.zoneName);
        }

        static LauminationResolution ResolveMembers(LauminationSet set, float angleDeg)
        {
            // Pick the nearest authored member, with optional built-in mirroring for odd-N sets.
            var m = NearestMember(set, angleDeg, out bool mirrored, out float resolvedAngle);
            if (m == null) return LauminationResolution.None;
            return new LauminationResolution(m.laumination, mirrored, 0f, resolvedAngle, m.zoneName);
        }

        static LauminationResolution ResolveRotate(LauminationSet set, float angleDeg)
        {
            // One laumination, transform rotates to angle (optionally snap-stepped).
            var m = FirstValid(set);
            if (m == null) return LauminationResolution.None;
            float rot = set.snapStepDeg > 0f ? Mathf.Round(angleDeg / set.snapStepDeg) * set.snapStepDeg
                                              : angleDeg;
            return new LauminationResolution(m.laumination, false, rot, angleDeg, m.zoneName);
        }

        static LauminationResolution ResolveMembersRotate(LauminationSet set, float angleDeg)
        {
            // Nearest member + rotation delta to the exact angle. Allows smooth-looking interpolation
            // between coarse members.
            var m = NearestMember(set, angleDeg, out bool mirrored, out float resolvedAngle);
            if (m == null) return LauminationResolution.None;
            float delta = Mathf.DeltaAngle(resolvedAngle, angleDeg);
            return new LauminationResolution(m.laumination, mirrored, delta, resolvedAngle, m.zoneName);
        }

        /// <summary>One sheet whose FRAMES are the directions. Frame i faces i × (360/N), clockwise from up,
        /// so the whole mapping is arithmetic — nothing per-direction is authored, named or stored.
        ///
        /// N is read from the sheet's own <c>frames.Count</c> rather than a count field on the set, so the
        /// two can never disagree: re-bake the sheet with 32 directions and the set follows with no edit.
        /// Returns <see cref="LauminationResolution.None"/> for an empty sheet (0 frames) — the modulo would
        /// divide by zero, and "no frames" genuinely has no direction to resolve.</summary>
        static LauminationResolution ResolveRotationSheet(LauminationSet set, float angleDeg)
        {
            var m = FirstValid(set);
            if (m == null) return LauminationResolution.None;

            var sheet = m.laumination;
            int n = sheet != null && sheet.frames != null ? sheet.frames.Count : 0;
            if (n <= 0) return LauminationResolution.None;

            float step = 360f / n;
            // Round (not floor) so each frame owns the half-step either side of its own angle — the same
            // "nearest direction wins" rule Members mode uses, just computed instead of searched. The extra
            // +n before the modulo keeps the wrap at 360° landing on frame 0, not on n.
            int idx = ((Mathf.RoundToInt(angleDeg / step) % n) + n) % n;
            return new LauminationResolution(sheet, false, 0f, idx * step, "", idx);
        }

        // ── helpers ──

        static LauminationSetMember FirstValid(LauminationSet set)
        {
            for (int i = 0; i < set.members.Count; i++)
            {
                var m = set.members[i];
                if (m != null && m.laumination != null) return m;
            }
            return null;
        }

        /// <summary>
        /// Pick the authored member nearest to <paramref name="angleDeg"/>. If
        /// <c>set.mirrorBuiltIn</c> is true and the set has members only in 0°..180°, angles in
        /// 180°..360° resolve to the mirror of the nearest member (flipX = true). If the authored set
        /// already covers all 360° (16-member set, no mirroring needed), the mirror flag stays false.
        /// </summary>
        static LauminationSetMember NearestMember(LauminationSet set, float angleDeg,
                                                  out bool mirrored, out float resolvedAngle)
        {
            // First pass: find the nearest authored member regardless of mirror.
            LauminationSetMember best = null;
            float bestDelta = float.MaxValue;
            bool anyIn180Plus = false;
            for (int i = 0; i < set.members.Count; i++)
            {
                var m = set.members[i];
                if (m == null || m.laumination == null) continue;
                if (m.angleDegrees > 180f) anyIn180Plus = true;
                float delta = Mathf.Abs(Mathf.DeltaAngle(m.angleDegrees, angleDeg));
                if (delta < bestDelta) { bestDelta = delta; best = m; }
            }
            if (best == null) { mirrored = false; resolvedAngle = 0f; return null; }

            // If mirroring is built-in AND the set has no authored member in the half the request
            // lives in, fall through to the mirrored half. We approximate "set has no member in
            // this half" by "no member has an authored angle in [180, 360)" — this is the common
            // authoring pattern (e.g. 9 members at 0°, 22.5°, ..., 180°) and gives the correct
            // result for both the symmetric case (16 authored, no mirror needed) and the asymmetric
            // case (9 authored, mirror built in).
            if (set.mirrorBuiltIn && !anyIn180Plus && angleDeg > 180f)
            {
                // The angle is in the mirrored half. Find the equivalent angle in the authored
                // half and re-search. Mirroring is reflection about the VERTICAL axis, not a
                // 180° rotation: in this file's convention (0° = up, increasing clockwise), a
                // direction θ mirrors to 360° − θ (e.g. 90°/East ↔ 270°/West, 315°/NW ↔ 45°/NE),
                // not θ ± 180° (which would be the OPPOSITE direction, e.g. North ↔ South). The
                // transform is its own inverse, so the same formula undoes it below.
                float mirroredAngle = 360f - angleDeg;
                LauminationSetMember mirroredBest = null;
                float mirroredBestDelta = float.MaxValue;
                for (int i = 0; i < set.members.Count; i++)
                {
                    var m = set.members[i];
                    if (m == null || m.laumination == null) continue;
                    if (m.angleDegrees > 180f) continue; // ignore stray over-180 entries
                    float delta = Mathf.Abs(Mathf.DeltaAngle(m.angleDegrees, mirroredAngle));
                    if (delta < mirroredBestDelta) { mirroredBestDelta = delta; mirroredBest = m; }
                }
                if (mirroredBest != null)
                {
                    mirrored = true;
                    resolvedAngle = 360f - mirroredBest.angleDegrees; // inverse of the mirror above; for change-detection on caller side
                    return mirroredBest;
                }
            }

            mirrored = false;
            resolvedAngle = best.angleDegrees;
            return best;
        }
    }

    /// <summary>
    /// Tracks the last non-zero direction so a character with no current heading (a stationary
    /// tank, an idle player) keeps facing the direction it last moved in. The movement design doc
    /// §8.1-break-3 calls this out explicitly: "a stationary tank has no heading" — the resolver is
    /// pure, so the latching lives in the caller.
    ///
    /// Usage:
    /// <code>
    /// Vector2 heading = player.GetCurrentHeading(); // zero when stationary
    /// float angleDeg = latch.Update(heading.HasDirection() ? heading.ToDir16().AngleDeg() : float.NaN);
    /// var resolution = LauminationSetResolver.Resolve(set, angleDeg);
    /// </code>
    /// Pass <c>float.NaN</c> when there is no direction (idle, stationary). The latch holds its
    /// last value and the resolver keeps resolving against it.
    /// </summary>
    public class LatchedDirection
    {
        // A sparse Members set (e.g. Legs' 3-member N/E/S-mirrored-to-W walk set) has its nearest-member
        // boundaries sitting exactly on the diagonals (45°/135°/225°/315°) — precisely where two-key diagonal
        // input lands. NearestMember has no hysteresis of its own (it's a pure per-call function), so with no
        // deadband here, any frame-to-frame sub-degree jitter in the sampled angle (MotionStateSource measures
        // heading from a position DELTA, not the raw input vector) flips the discrete member pick back and
        // forth across the tie every frame — each flip restarts the clip, reading as rapid re-triggering.
        // Suppressing sub-threshold angle changes here (rather than in the resolver, which stays pure per its
        // own doc comment) fixes it for every caller without touching member-resolution logic at all.
        const float DeadbandDeg = 1.5f;

        /// Last valid angle in degrees (0..360). Initial value is 0 (Up) so a never-moved character
        /// faces Up — matches the project's standing convention (Cookbook2D.Dir8.Up, Dir16.Up).
        public float AngleDeg { get; private set; }

        public LatchedDirection(float initialDeg = 0f) { AngleDeg = Normalize(initialDeg); }

        /// Pass the current heading direction angle, or float.NaN to indicate "no current direction".
        /// The latch updates only on finite inputs, and only when the change clears a small deadband —
        /// see DeadbandDeg above.
        public float Update(float currentAngleDeg)
        {
            if (!float.IsNaN(currentAngleDeg) && !float.IsInfinity(currentAngleDeg))
            {
                float next = Normalize(currentAngleDeg);
                if (Mathf.Abs(Mathf.DeltaAngle(AngleDeg, next)) >= DeadbandDeg) AngleDeg = next;
            }
            return AngleDeg;
        }

        /// Same as <see cref="Update"/> but takes a 2D vector. Zero vectors count as "no direction".
        public float Update(Vector2 v)
        {
            if (v.sqrMagnitude < 1e-6f) return AngleDeg;
            return Update(Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg);
        }

        static float Normalize(float deg)
        {
            while (deg < 0f) deg += 360f;
            while (deg >= 360f) deg -= 360f;
            return deg;
        }
    }
}
