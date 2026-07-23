namespace Laubrary.Pyre
{
    /// What a layer's rendered pixels are USED for. Orthogonal to its shape and its colour mode: any shape,
    /// with any fill, can be either drawn or used as a matte — which is the whole point. Every bit of motion
    /// Pyre can already author (a sweeping disc, a growing ring, a churning ball cloud, a noise fill, any
    /// modifier stack) becomes available as a mask without one line of new shape code.
    public enum LayerRole
    {
        Draw,    // normal: composite this layer onto the frame
        Matte,   // don't composite it — read its LUMINANCE as a mask that drives the layers above it
    }

    /// What a Matte layer's mask drives on the layers it covers. A FLAG set — any combination can act at
    /// once (mask alpha AND hue AND brightness together), applied in a fixed order (spatial first, then
    /// colour, then alpha) so a combination is deterministic regardless of which bits are set.
    ///
    /// Deliberately luminance-driven rather than requiring an authored black-to-white gradient: any existing
    /// layer can be flipped to Matte and simply work, it stays readable in colour while you author it, and it
    /// matches the standard meaning of a "luma matte". Mask = luminance × the layer's own alpha, so a shape's
    /// silhouette counts as well as its brightness.
    [System.Flags]
    public enum MatteChannel
    {
        None       = 0,
        Alpha      = 1 << 0,   // classic luma matte: mask multiplies the covered layers' opacity
        Brightness = 1 << 1,   // darken toward black where the mask is low — a shadow/light pass
        Saturation = 1 << 2,   // drain toward greyscale where the mask is low (ash, smoke, heat-death)
        Hue        = 1 << 3,   // rotate hue by the mask — heat shimmer, chemical burn, cold spots
        Blur       = 1 << 4,   // soften where the mask is high, sharp where it's low
        Displace   = 1 << 5,   // push pixels along the mask's own SLOPE — refraction, heat haze, lensing
    }

    /// How far up the stack a Matte layer reaches.
    public enum MatteScope
    {
        NextLayer,   // clip only the next drawn layer above it (Photoshop's clipping-mask behaviour)
        AllAbove,    // affect every layer above it, until another matte replaces it
    }

    /// The primitive a Layer stamps. Kept deliberately small and chunky so bakes read as pixel art.
    public enum LayerShape
    {
        Disc,           // filled circle → ring via the Thickness param (0 = 1px border, 1 = full disc)
        Crescent,       // a disc with a second offset disc masked out
        SparkleField,   // random single lit pixels scattered inside a circle
        Bars,           // a symmetric row of forward-growing bars streaming off an edge (directional blast)
        Sprite,         // stamps a supplied sprite as particles (a mini particle system)
        MetaBlob,       // click-placed orbs that fuse (SDF metaballs) into one gradient-shaded shape
        HeightBalls,    // a churning cloud of soft balls fused into density/heat/height fields, relief-lit and
                        // shaded through ONE gradient (low = smoke, high = fire); energy waves climb that gradient
        Fire,           // a directional flame: a heat/fuel grid advected by buoyancy + curl noise, reached by
                        // REPLAY rather than closed-form evaluation (see FireSim), with N radial arms
        Fire2,          // a cheaper cellular flame (doom-fire family): each frame is the previous one cooled
                        // and drawn inward toward the source, star-mirrored for an explosive burst (see Fire2Sim)
    }

    /// How a shape's colour gradient is applied (solid shapes — Disc / Crescent / MetaBlob).
    public enum ColorMode
    {
        OverLife,     // one colour for the whole shape, sampled from the gradient at the shape's life 0→1
        Fill,         // the gradient fills the shape spatially (centre → edge), constant over life
        FlowingFill,  // a spatial fill whose gradient scrolls through its spectrum over the shape's life
        NoiseFill     // the gradient is painted through a domain-warped noise field sampled inside the shape
                      // (the layer's own noise zoom/rotation/drift/warp/bands params below), for a
                      // cloudy/marbled interior instead of a clean radial fill
    }

    /// How a Bars layer ends: contract back (size envelope shrinks) or hold + dissolve from the centre outward.
    public enum BarDecay
    {
        Contract,   // forward reach / width follow their envelope down again (grow then shrink)
        Dissolve    // forward reach / width expand and HOLD; bars then fade out from the centre bar outward
    }

    /// Where a layer's shapes get scattered. Area = the existing uniform scatter filling the Spawn radius disc.
    /// Ring = placed along the RIM at Spawn radius instead, per RingOrder + the arc range. Rosing = an authored
    /// list of RINGS (Layer.roseRings), each with its own count/radius/timing, blooming outward over life like a
    /// rose — first ring a few shapes close in, later rings more shapes further out.
    public enum ScatterMode
    {
        Area,
        Ring,
        Rosing
    }

    /// Ring mode: how the Count shapes are laid out around the arc.
    public enum RingOrder
    {
        Sequential,   // shape #i sits at its own evenly-spaced slot around the arc, in index order
        Random        // each shape gets an independent random angle within the arc
    }

    /// Editor preview backdrop fill (Pyre window). Purely cosmetic — never read by BlastRenderer, never baked.
    public enum PreviewBgMode
    {
        Solid,
        Gradient,
        Image
    }
}
