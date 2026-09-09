namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Optional capability a directional part animator MAY provide (discovered via GetComponent, same pattern
    /// as <see cref="IAnimatedView"/> and <see cref="IPartLookup"/>) — reports WHICH authored facing that part
    /// resolved for the frame currently on screen, so something else can measure itself against the drawing
    /// without re-deriving the direction table.
    ///
    /// <para>It exists for aiming techniques that refine a coarse facing into a finer one: a free/continuous
    /// aim can only rotate a sprite by "the bit the artwork doesn't already cover" if it knows the angle the
    /// artwork is actually drawn at. Deriving that a second time (an aimer with its own "16 directions,
    /// 22.5° apart" table) would be a copy that silently disagrees the moment the sheet is re-baked with a
    /// different number of directions, or the set switches to mirrored members — which is exactly the class of
    /// bug a shared answer removes.</para>
    ///
    /// <para>A part with no directional animator simply has no component implementing this, and a consumer
    /// treats that as "nothing to refine" — it must degrade quietly, never assume.</para>
    /// </summary>
    public interface IResolvedFacing
    {
        /// <summary>The facing this part is currently DRAWN at, in the project's screen convention
        /// (0° = up, increasing clockwise) — already the EFFECTIVE world facing, so a member picked through a
        /// built-in horizontal mirror reports the mirrored angle it visually faces, not the authored one.
        ///
        /// <paramref name="baseRotationDeg"/> is the transform rotation that facing already asks for in the
        /// same convention (non-zero only for a set that rotates its art), so a consumer that writes rotation
        /// itself can write an absolute value rather than stacking a delta onto whatever it finds.
        ///
        /// Returns false before anything has been resolved at all.</summary>
        bool TryGetResolvedFacing(out float facingAngleDeg, out float baseRotationDeg);
    }
}
