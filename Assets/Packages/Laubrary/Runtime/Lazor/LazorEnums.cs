namespace Laubrary.Lazor
{
    // These mirror the corresponding Shapes plugin enums by member NAME, so the project-side Shapes
    // binding can map them one-to-one without Laubrary having to reference the Shapes assembly.

    /// <summary>How the two ends of an open stroke are drawn.</summary>
    public enum LazorCap
    {
        None,
        Square,
        Round,
    }

    /// <summary>How consecutive segments of a polyline are joined at a corner.</summary>
    public enum LazorJoin
    {
        Simple,
        Miter,
        Round,
        Bevel,
    }

    /// <summary>How the stroke color composites against what is behind it. Additive/Screen give the laser glow.</summary>
    public enum LazorBlend
    {
        Transparent,
        Additive,
        Screen,
        ColorDodge,
        Opaque,
    }

    /// <summary>Whether stroke width is measured in the shape's flat plane or always faces the camera.</summary>
    public enum LazorFacing
    {
        Flat2D,
        Billboard,
    }
}
