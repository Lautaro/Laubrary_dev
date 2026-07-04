namespace Laubrary.Pyre
{
    /// The primitive a Wave stamps. Kept deliberately small and chunky so bakes read as pixel art.
    public enum WaveShape
    {
        Disc,           // filled circle
        Ring,           // hollow annulus (shockwave)
        DissolvingDisc, // filled circle eaten away by a growing hole
        SparkleField,   // random single lit pixels scattered inside a circle
        Crescent        // a disc with a second offset disc masked out
    }

    /// How a shape appears at its start frame.
    public enum SpawnMode
    {
        Instant,  // full size / full alpha immediately
        FadeIn,   // alpha ramps up over the first slice of its life
        GrowIn    // radius ramps up over the first slice of its life
    }

    /// How a shape leaves at its end frame.
    public enum EndMode
    {
        Instant,      // pops out at the last frame
        FadeOut,      // alpha ramps down over the last slice of its life
        Disintegrate, // pixels randomly drop out (deterministically) as it ends
        Shrink        // radius ramps down to nothing over the last slice of its life
    }
}
