namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Optional capability a spawned character's view MAY provide (discovered via GetComponent/`as`, same
    /// pattern as <see cref="IFlippableView"/>/<see cref="IMotionPoseHost"/>) — lets a caller jump to a named
    /// zone WITHIN the currently-playing clip, without restarting it. Exists so a single multi-frame "rotation
    /// sheet" clip (e.g. 16 idle directions as one clip, each direction a single-frame zone) can stand in for
    /// what used to be N separate single-frame clips — a <see cref="Laubrary.Launimator.LauminationSetMember"/>
    /// with a non-empty zone name resolves to (shared clip, jump to this zone) instead of (its own clip, play
    /// from frame 0). A view with no zone concept (a plain SpriteView, or a Launimator view whose current clip
    /// isn't zoned) simply has no component implementing this, or <c>TryEnterZone</c> returns false — silently
    /// a no-op, not an error, the same rule every other optional-capability host in this codebase follows.
    /// </summary>
    public interface IZonedView
    {
        /// Jump to the named zone within whatever clip is currently loaded. Returns false if this view isn't
        /// zoned, the current clip has no zones, or the zone name is unknown.
        bool TryEnterZone(string zoneName);
    }
}
