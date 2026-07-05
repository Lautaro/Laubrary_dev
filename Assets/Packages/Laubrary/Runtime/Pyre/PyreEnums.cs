namespace Laubrary.Pyre
{
    /// The primitive a Layer stamps. Kept deliberately small and chunky so bakes read as pixel art.
    public enum LayerShape
    {
        Disc,           // filled circle
        Ring,           // hollow annulus (shockwave)
        DissolvingDisc, // filled circle eaten away by a growing hole
        SparkleField,   // random single lit pixels scattered inside a circle
        Crescent        // a disc with a second offset disc masked out
    }

    /// How a Layer places and moves its shapes.
    public enum EmissionMode
    {
        Radial,      // shapes scatter within a disc around the centre and grow outward (a normal explosion)
        Directional  // shapes start on a (bendable) origin line/surface and stream one way across the frame
    }
}
