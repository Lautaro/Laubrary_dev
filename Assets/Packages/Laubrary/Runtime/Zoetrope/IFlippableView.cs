namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Optional capability a spawned character's view MAY provide (discovered via GetComponent / interface
    /// cast, same pattern as <see cref="IAnimatedView"/> and <c>ICueSink</c>) — lets a direction resolver
    /// (<see cref="MotionPoseAnimator"/>) write the built-in horizontal mirror a <see cref="LauminationSet"/>
    /// resolves without knowing which concrete animation system is underneath. A view with no mirror concept
    /// (a rotation-only 3D-ish view, a view that never authors a Mirror/Members set) simply doesn't implement
    /// this, and the resolver's flip write is silently a no-op for it.
    /// </summary>
    public interface IFlippableView
    {
        /// Mirror the current frame horizontally.
        bool FlipX { get; set; }
    }
}
