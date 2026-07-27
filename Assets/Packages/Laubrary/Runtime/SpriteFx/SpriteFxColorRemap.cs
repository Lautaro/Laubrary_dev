using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// How a <see cref="ColorRemapRegion"/> turns the source pixels it matches into their new colour.
    public enum ColorRemapMode
    {
        /// A FLAT region → one swatch. Every matched pixel becomes the swatch colour (source shading is discarded —
        /// use this for areas the art already treats as a single flat fill).
        Swatch,
        /// A SHADED region → a gradient, indexed by the source pixel's own LUMA. A cluster of related shades
        /// (e.g. water in ten blues) maps to ONE gradient; each pixel's brightness picks where it lands on the
        /// ramp, so the original shading is PRESERVED and it stays one assignment, never flattened to a single tone.
        Gradient
    }

    /// One source-colour → target mapping inside a <see cref="ColorRemapModifier"/>. Keys on COLOUR (a hue cluster,
    /// or a grey's value), never on pixel POSITION — so it follows an animated Reel automatically with no per-frame
    /// masks (the decisive advantage over a painted-mask remap). See ZUI_COLOR_DESIGN.md → "Recolouring existing
    /// sprites / Reels", Form A.
    [Serializable]
    public class ColorRemapRegion
    {
        [Tooltip("Author label for this region (e.g. \"blues\", \"skin\") — cosmetic, shown in the editor list.")]
        public string label = "region";

        [Tooltip("A representative source colour for this region — the centre of the cluster to grab. For a shaded " +
                 "cluster, pick any mid shade of it; matching is by HUE, so it grabs the whole light-to-dark range.")]
        public Color source = Color.white;

        [Range(0f, 1f)]
        [Tooltip("How wide a cluster around Source to grab. For a chromatic source this is a HUE window (1 = the " +
                 "full ±180° colour wheel, ~0.08 ≈ ±14°); for a near-grey source it is a VALUE window. Widen it to " +
                 "pull in more neighbouring shades, tighten it to isolate one hue from an adjacent one.")]
        public float tolerance = 0.08f;

        [Range(0f, 1f)]
        [Tooltip("Saturation floor. A source at or below this counts as GREY and matches near-greys by brightness; " +
                 "a chromatic source ignores pixels below this (so muted/near-grey pixels aren't swept into a " +
                 "colour cluster). Lower it to catch muted, desaturated art.")]
        public float minSaturation = 0.10f;

        [Tooltip("Flat swatch, or a luma-indexed gradient (shading preserved). See ColorRemapMode.")]
        public ColorRemapMode mode = ColorRemapMode.Swatch;

        [Tooltip("Swatch mode target — a plain colour or a named SwatchPalette swatch (change the swatch, every " +
                 "remap using it updates).")]
        public ZuiSwatchRef swatch = new ZuiSwatchRef(Color.white);

        [Tooltip("Gradient mode target — the source pixel's luma (remapped through the Luma window below) picks the " +
                 "position on this ramp, so the region's shading is preserved as a smooth re-spectrum.")]
        public ZuiGradient gradient = new ZuiGradient();

        [Range(0f, 1f)]
        [Tooltip("Gradient mode: the source luma that maps to the START (position 0) of the gradient. Set Low/High " +
                 "to the darkest/brightest shade in the cluster so its full shade range fills the ramp end to end.")]
        public float lumaLow = 0f;
        [Range(0f, 1f)]
        [Tooltip("Gradient mode: the source luma that maps to the END (position 1) of the gradient.")]
        public float lumaHigh = 1f;

        [Tooltip("This region colour-cycles. On the static bake / managed path the gradient phase advances with the " +
                 "shape's own life; the live shader path (later) scrolls it by real time. No effect in Swatch mode.")]
        public bool cycle = false;
        [Tooltip("Cycle speed (gradient phase units per unit of life/second) when Cycle is on.")]
        public float cycleSpeed = 0.5f;

        /// Rec.601 luma of an RGB colour (ignoring alpha) — the shade ordering used to index a Gradient region.
        public static float Luma(in Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        /// Does this region claim the given pixel? On a hit, `score` is a lower-is-closer distance so the modifier
        /// can disambiguate a pixel that falls inside two overlapping regions (the nearer cluster wins).
        public bool TryScore(float ph, float ps, float pv, out float score)
        {
            score = 0f;
            Color.RGBToHSV(source, out float sh, out float ss, out float sv);

            if (ss <= minSaturation)   // grey source: match near-greys by brightness proximity
            {
                if (ps > minSaturation) return false;          // a chromatic pixel isn't part of a grey cluster
                float d = Mathf.Abs(pv - sv);
                if (d > Mathf.Max(0.0001f, tolerance)) return false;
                score = d;
                return true;
            }

            // chromatic source: match by HUE window, brightness-agnostic (the gradient handles brightness → shade)
            if (ps < minSaturation) return false;              // near-grey pixel can't belong to a colour cluster
            float hueDist = Mathf.Abs(Mathf.DeltaAngle(ph * 360f, sh * 360f)) / 360f;   // 0..0.5
            float window = tolerance * 0.5f;                   // tolerance 1 → the full ±180° wheel
            if (hueDist > window) return false;
            score = hueDist;
            return true;
        }

        /// The remapped colour for a matched pixel (RGB only; alpha is handled by the modifier). `life` drives a
        /// cycling gradient's phase.
        public Color Resolve(in Color src, float life)
        {
            if (mode == ColorRemapMode.Swatch) return swatch.Resolve();
            float t = lumaHigh > lumaLow
                ? Mathf.Clamp01((Luma(src) - lumaLow) / (lumaHigh - lumaLow))
                : Luma(src);
            float phase = cycle ? Mathf.Repeat(life * cycleSpeed, 1f) : 0f;
            return gradient != null ? gradient.Evaluate(t, phase) : src;
        }

        public ColorRemapRegion Clone()
        {
            var c = (ColorRemapRegion)MemberwiseClone();   // value fields + the ZuiSwatchRef struct copy by value;
            c.gradient = CloneGradient(gradient);          // the ZuiGradient reference must be deep-copied
            return c;
        }

        static ZuiGradient CloneGradient(ZuiGradient s)
        {
            if (s == null) return null;
            return new ZuiGradient
            {
                gradient = Sfx.CloneGradient(s.gradient),
                reverse = s.reverse, hueShift = s.hueShift, saturation = s.saturation,
                brightness = s.brightness, contrast = s.contrast, quantiseSteps = s.quantiseSteps,
                cycle = s.cycle, cycleSpeed = s.cycleSpeed,
            };
        }
    }

    /// Recolours an EXISTING sprite / Reel by remapping its source colours (Form A of the recolour design). Each
    /// <see cref="ColorRemapRegion"/> grabs a colour cluster — a hue (all its shades) or a grey range — and maps it
    /// to a flat swatch or a luma-indexed gradient. Because it keys on colour, not position, it rides an animation
    /// with no per-frame masks: the same table recolours every frame automatically.
    ///
    /// This is the STATIC-recolour path — a plain managed <see cref="PixelModifier"/> the Pyre baker / a Reel bake
    /// runs per pixel. It is deliberately NOT in <c>SpriteFxStack.IsShaped</c>, so the runtime Burst/op filter
    /// (SpriteFxFilter) simply skips it for now; a shaped op that bakes the region table into a remap LUT (for live,
    /// cycling recolour on the GPU) is the #78-adjacent follow-up. Non-destructive of the silhouette: it replaces
    /// each matched pixel's RGB and, by default, leaves alpha (the shape) untouched.
    [Serializable]
    public class ColorRemapModifier : PixelModifier
    {
        [Tooltip("The source-colour → target regions, tried in order (a pixel inside two overlapping regions goes " +
                 "to the nearer cluster). Colours outside every region are left as-is unless Drop unmatched is on.")]
        public List<ColorRemapRegion> regions = new List<ColorRemapRegion>();

        [Tooltip("On = pixels that match NO region are dropped (made transparent), masking the sprite down to only " +
                 "the remapped regions. Off (default) = leave unmatched pixels exactly as they were (recolour only " +
                 "the regions you named, keep outlines / everything else intact).")]
        public bool dropUnmatched = false;

        [Tooltip("Fold the target swatch/gradient's own alpha into the pixel — a semi-transparent target stop can " +
                 "then knock its region back. Off (default) = keep the sprite's original alpha, so the silhouette " +
                 "is preserved exactly and only the colour changes.")]
        public bool applyTargetAlpha = false;

        public override string DisplayName => "Colour remap";

        public override bool ApplyPixel(ref Color col, ref float alpha, in PixelInfo info)
        {
            if (regions == null || regions.Count == 0) return true;
            if (alpha <= 0f) return true;   // a fully-transparent source pixel carries no colour to remap

            Color.RGBToHSV(col, out float ph, out float ps, out float pv);

            int best = -1; float bestScore = float.MaxValue;
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (r == null) continue;
                if (r.TryScore(ph, ps, pv, out float score) && score < bestScore)
                {
                    bestScore = score; best = i;
                }
            }

            if (best < 0)
            {
                if (!dropUnmatched) return true;
                alpha = 0f;
                return false;
            }

            Color outc = regions[best].Resolve(col, info.life);
            col.r = outc.r; col.g = outc.g; col.b = outc.b;   // replace RGB; the silhouette (alpha) stays
            if (applyTargetAlpha) alpha *= outc.a;
            return true;
        }

        // Deep-copy the region list (the base Clone only special-cases ZUIValue / Gradient / envelope-point fields,
        // so a List<ColorRemapRegion> would otherwise be shared by reference and a Dup would bleed into the original).
        public override PyreModifier Clone()
        {
            var m = (ColorRemapModifier)base.Clone();
            m.regions = new List<ColorRemapRegion>(regions?.Count ?? 0);
            if (regions != null)
                foreach (var r in regions) m.regions.Add(r?.Clone());
            return m;
        }
    }
}
