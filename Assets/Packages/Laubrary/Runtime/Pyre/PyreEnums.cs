namespace Laubrary.Pyre
{
    /// The primitive a Layer stamps. Kept deliberately small and chunky so bakes read as pixel art.
    public enum LayerShape
    {
        Disc,           // filled circle
        Ring,           // hollow annulus (shockwave)
        DissolvingDisc, // filled circle eaten away by a growing hole
        SparkleField,   // random single lit pixels scattered inside a circle
        Crescent,       // a disc with a second offset disc masked out
        Bars            // a symmetric row of forward-growing bars streaming off an edge (directional blast)
    }

    /// How a Layer places and moves its shapes.
    public enum EmissionMode
    {
        Radial,      // shapes scatter within a disc around the centre and grow outward (a normal explosion)
        Directional  // shapes start on a (bendable) origin line/surface and stream one way across the frame
    }

    /// How a Bars layer ends: contract back (size envelope shrinks) or hold + dissolve from the centre outward.
    public enum BarDecay
    {
        Contract,   // forward reach / width follow their envelope down again (grow then shrink)
        Dissolve    // forward reach / width expand and HOLD; bars then fade out from the centre bar outward
    }

    /// How the `spreadCount` copies of the blast are arranged.
    public enum SpreadMode
    {
        Orbit,      // rotate the whole stack around the centre — copies sit around a ring, bars pointing INWARD
        Radiate     // arms share the centre origin and reach OUTWARD (an asterisk of bar-combs / a star); canvas auto-fits
    }
}
