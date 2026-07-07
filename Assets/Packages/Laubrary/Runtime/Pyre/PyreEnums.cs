namespace Laubrary.Pyre
{
    /// The primitive a Layer stamps. Kept deliberately small and chunky so bakes read as pixel art.
    public enum LayerShape
    {
        Disc,           // filled circle → ring via the Thickness param (0 = 1px border, 1 = full disc)
        Crescent,       // a disc with a second offset disc masked out
        SparkleField,   // random single lit pixels scattered inside a circle
        Bars,           // a symmetric row of forward-growing bars streaming off an edge (directional blast)
        Sprite,         // stamps a supplied sprite as particles (a mini particle system)
        MetaBlob        // click-placed orbs that fuse (SDF metaballs) into one gradient-shaded shape
    }

    /// How a shape's colour gradient is applied (solid shapes — Disc / Crescent).
    public enum ColorMode
    {
        OverLife,     // one colour for the whole shape, sampled from the gradient at the shape's life 0→1
        Fill,         // the gradient fills the shape spatially (centre → edge), constant over life
        FlowingFill   // a spatial fill whose gradient scrolls through its spectrum over the shape's life
    }

    /// How a Bars layer ends: contract back (size envelope shrinks) or hold + dissolve from the centre outward.
    public enum BarDecay
    {
        Contract,   // forward reach / width follow their envelope down again (grow then shrink)
        Dissolve    // forward reach / width expand and HOLD; bars then fade out from the centre bar outward
    }
}
