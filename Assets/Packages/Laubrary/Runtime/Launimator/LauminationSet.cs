using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// How a <see cref="LauminationSet"/> picks which member laumination to play for a given angle.
    /// Designed so the same data shape serves 1-frame directional sprites (the player's torso),
    /// multi-frame walk cycles (the player's legs), smooth-rotation sprites (Asteroid+ ships),
    /// and single-part no-direction Zoes (today's Locomotion — works unchanged via DirectionMode.None).
    ///
    /// All resolution is a pure function: <c>(set, angle) → (laumination, flipX, rotationDeg)</c>.
    /// No state, no graph, no transitions to author — previewable in Mirage, deterministic in tests.
    /// </summary>
    public enum DirectionMode
    {
        /// One laumination, no direction. The single-part Zoe case (e.g. a hovering disc). The
        /// resolver always returns the same member regardless of angle. Mirrors today's flat
        /// <c>Locomotion</c> behaviour exactly — every existing Zoe keeps working with no authoring.
        None,

        /// One laumination plus its horizontal flip — the 2-way case. Mirroring is done at render time
        /// via <c>SpriteRenderer.flipX</c>, no second member needed. The set's <c>members</c> list
        /// contains exactly one entry.
        Mirror,

        /// N explicitly-authored members tagged by angle. Resolution picks the nearest member, with
        /// optional built-in horizontal mirroring: if N is odd and the authored angles span 0°..180°,
        /// the resolver reflects a request in the 180°..360° range onto the mirrored member instead of
        /// picking the nearest. This is the authoring economics the design docs cite: "16 aim directions
        /// come from 9 authored members, 4 leg directions from 3". <c>members</c> may contain 1..16 entries.
        Members,

        /// One laumination, the transform rotates to the angle. Use for ships, turrets and any smooth-360
        /// sprite that doesn't have per-direction art. <c>snapStepDeg</c> on the set, if non-zero,
        /// quantises the rotation to multiples of that step (pixel sprites shimmer under fractional
        /// rotation; snapping to e.g. 22.5° gives a 16-step smooth look from one sprite).
        Rotate,

        /// Members for coarse sectors, rotation for the remainder. Coarse sector = nearest member;
        /// the resolver returns that member plus the angle delta to the requested angle so the runtime
        /// can rotate the transform for the in-between motion. The technique that buys smooth aiming
        /// from few sprites (e.g. 8 members + rotation reads as 32 directions to the eye).
        MembersRotate
    }

    /// One authored member of a <see cref="LauminationSet"/>: which laumination, tagged at which angle,
    /// optionally with a per-member pivot override. The pivot override matters when different members
    /// of the same set want different registration points (rare, but useful when the set mixes walk
    /// frames with idle frames of different sizes).
    [System.Serializable]
    public class LauminationSetMember
    {
        [Tooltip("Direction this member is authored for, in degrees. Convention: 0° = up (+Y), increasing " +
                 "clockwise in screen space (Unity 2D). For Members mode the resolver picks the nearest; " +
                 "for Mirror mode this is ignored. Range 0..360.")]
        public float angleDegrees;

        [Tooltip("The laumination played when this member is picked.")]
        public Laumination laumination;

        [Tooltip("Optional pivot override for THIS member. Leave at default (0.5, 0) to inherit the " +
                 "set's frame pivot. Useful when members of the same set want different registration " +
                 "points (rare).")]
        public Vector2 pivotOverride = new Vector2(0.5f, 0f);
    }

    /// <summary>
    /// A directional laumination set: one laumination per direction, per gait, per phase.
    /// First-class sibling of <c>LauminaryVersion.animations</c> — an event picks a SET, the runtime
    /// picks the MEMBER by angle. Mirroring is built in so 16 directions come from ~9 authored members.
    ///
    /// Why a Set rather than a naming convention ("Shoot_NE"): reintroduces exactly the typed-string
    /// failure the project just banned. A set is data, never a string — consumers reference by
    /// <see cref="name"/>, picked from the version's declared sets, not typed.
    /// </summary>
    [System.Serializable]
    public class LauminationSet
    {
        [Tooltip("Human-readable identifier. Must be unique within the LauminaryVersion. Consumers " +
                 "reference sets by this name (never by index).")]
        public string name = "NewSet";

        [Tooltip("How the resolver picks a member for a given angle.")]
        public DirectionMode directionMode = DirectionMode.None;

        [Tooltip("Authored members. Required count depends on directionMode: " +
                 "None = 1 (or 0, in which case resolution returns null); " +
                 "Mirror = 1; " +
                 "Members = 1..16 (typical 9 for a 16-way mirrored set, 3 for a 4-way mirrored set); " +
                 "Rotate = 1; " +
                 "MembersRotate = 2..16.")]
        public List<LauminationSetMember> members = new List<LauminationSetMember>();

        [Tooltip("When true (only meaningful for Members mode), the resolver mirrors across the vertical " +
                 "axis: an angle in 180°..360° resolves to the mirror of the nearest member in 0°..180°. " +
                 "This is the authoring economy the design doc cites — 9 authored members serve 16 " +
                 "directions, 3 authored members serve 4 directions. Off = strict nearest-member, no mirror.")]
        public bool mirrorBuiltIn = true;

        [Tooltip("Quantise rotation to multiples of this many degrees. 0 = free rotation. Typical values: " +
                 "22.5 (= 16 steps), 11.25 (= 32 steps). Pixel sprites shimmer under fractional rotation; " +
                 "snap-stepping gives a smooth-looking quantized rotation from one sprite. Only used by " +
                 "Rotate and MembersRotate modes.")]
        public float snapStepDeg = 0f;

        [Tooltip("Shared registration pivot for all members unless overridden by LauminationSetMember.pivotOverride.")]
        public Vector2 framePivot = new Vector2(0.5f, 0f);
    }
}
