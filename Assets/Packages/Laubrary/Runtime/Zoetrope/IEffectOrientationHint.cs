namespace Laubrary.Zoetrope
{
    /// <summary>Optional capability on an <see cref="IEffect"/>: a one-line note about how the effect's OWN
    /// asset will orient itself when the event rotates it — e.g. a Pyre whose author set a Vector anchor, so
    /// "Face event direction" points that anchor along the event instead of assuming the art faces +X. The
    /// Zoe window shows it beside the rotation picker; null means nothing to say. Zoetrope core stays free of
    /// any specific VFX module — the effect answers, the window only prints.</summary>
    public interface IEffectOrientationHint
    {
        string OrientationHint { get; }
    }
}
