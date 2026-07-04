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
}
