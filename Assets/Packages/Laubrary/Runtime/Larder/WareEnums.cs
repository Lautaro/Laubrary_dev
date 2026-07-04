namespace Laubrary.Larder
{
    /// The kind of shelf product. Kind drives the extra "identity" decoration a Ware gets on top of the shared
    /// silhouette+fill pipeline (a book grows a spine + title patch, a can a wraparound label + rim, a crate its
    /// planks + X-brace + nails, a carton its taped seam + flap). Everything else (shape, fill, label, bands…) is
    /// orthogonal, so any kind can wear any combination — that's where the endless variety comes from.
    public enum WareKind { Book, Can, Box, Crate, Carton }

    /// The outline the Ware is cut from. Rectangular = hard box; RoundedRect = softened corners; Round = a vertical
    /// stadium/capsule (the classic can/cylinder seen front, rounded left & right); Spherical = an ellipse.
    public enum WareShape { Rectangular, RoundedRect, Round, Spherical }

    /// How the body colour is laid into the silhouette. Solid = flat body; Gradient = bodyLight (top) → bodyDark
    /// (bottom); InnerGlow = brighten toward the centre (a soft product sheen); InnerShadow = darken toward the edges.
    public enum FillMode { Solid, Gradient, InnerGlow, InnerShadow }

    /// The fake product label patch. None = bare body; Horizontal = a band of "text" across the middle; Diagonal = the
    /// same patch skewed; CenterPatch = a narrower centred patch (its width driven by WareSpec.labelWidth).
    public enum LabelStyle { None, Horizontal, Diagonal, CenterPatch }

    /// Optional corner treatment painted over the finished body. ColoredTriangle = an accent flag in a corner;
    /// Rounded = a lightened corner nibble; CutOff = a corner chopped away to transparent (a bevelled package).
    public enum CornerStyle { None, ColoredTriangle, Rounded, CutOff }

    /// Coloured stripe decoration in the accent colour: none, or a run of horizontal, vertical, or diagonal bands.
    public enum BandMode { None, Horizontal, Vertical, Diagonal }

    /// Spot/blob decoration: none, one big centred circle (a logo dot), or a scatter of small spots.
    public enum SpotMode { None, Circle, Spots }
}
