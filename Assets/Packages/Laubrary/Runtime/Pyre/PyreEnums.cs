namespace Laubrary.Pyre
{
    /// The primitive a Layer stamps. Kept deliberately small and chunky so bakes read as pixel art.
    public enum LayerShape
    {
        Disc,           // filled circle → ring via the Thickness param (0 = 1px border, 1 = full disc)
        Crescent,       // a disc with a second offset disc masked out
        SparkleField,   // random single lit pixels scattered inside a circle
        Bars,           // a symmetric row of forward-growing bars streaming off an edge (directional blast)
        Sprite          // stamps a supplied sprite as particles (a mini particle system)
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
}
