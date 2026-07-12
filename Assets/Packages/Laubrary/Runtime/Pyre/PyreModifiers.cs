using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre
{
    /// The moving shape an Alpha-Mask modifier sweeps across the layer.
    public enum MaskShape
    {
        DiscOut,   // a disc that reveals from the centre outward as progress rises (grow from within)
        DiscIn,    // transparency grows from the edges inward, consuming the frame as progress rises
        SwipeH,    // a horizontal wipe (left → right), like a scene transition
        SwipeV,    // a vertical wipe (bottom → top)
        Wedge,     // a pac-man pie slice removed by angle: progress 0 = none, 0.25 = a quarter bite, 0.5 = half
        Noise      // an irregular cloud silhouette carved by domain-warped noise instead of a clean geometric edge
    }

    /// Shared deterministic noise, built entirely on <see cref="BlastRenderer.Hash01"/> (never UnityEngine.Random or
    /// Mathf.PerlinNoise) so it stays bit-identical across the editor preview, the baker and the runtime player.
    /// Two octaves of bilinear value-noise, the second sampled through a domain WARPED by the first — this is what
    /// makes the field read as churning/rolling rather than a static smooth blob. `warp` (0 = none) sets how much.
    internal static class PyreNoise
    {
        public static float Sample(float x, float y, int seed, float warp)
        {
            if (warp > 0.001f)
            {
                float wx = ValueNoise(x * 0.5f + 37.1f, y * 0.5f + 11.7f, seed ^ 0x51ED2701) * 2f - 1f;
                float wy = ValueNoise(x * 0.5f - 22.4f, y * 0.5f + 61.3f, seed ^ 0x2C1B3A45) * 2f - 1f;
                x += wx * warp * 4f;
                y += wy * warp * 4f;
            }
            float baseN = ValueNoise(x, y, seed);
            float detail = ValueNoise(x * 2.13f, y * 2.13f, seed ^ 0x7F4A7C15);
            return Mathf.Clamp01(baseN * 0.65f + detail * 0.35f);
        }

        // Bilinear-interpolated hash lattice (smoothstepped) — smooth, deterministic value noise, 0..1.
        static float ValueNoise(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float h00 = BlastRenderer.Hash01(seed, x0, y0);
            float h10 = BlastRenderer.Hash01(seed, x0 + 1, y0);
            float h01 = BlastRenderer.Hash01(seed, x0, y0 + 1);
            float h11 = BlastRenderer.Hash01(seed, x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), ty);
        }
    }

    /// How a Dissolve modifier eats pixels as its amount rises to 1 (everything gone).
    public enum DissolveMode
    {
        Erase,    // hard-remove a random `amount` fraction of pixels (stable holes)
        Fade,     // don't remove — drop every pixel's alpha by `amount` (all transparent at 1)
        Bleed,    // remove a random fraction AND leave the pixels next to the threshold semi-transparent (a soft edge)
        Scatter   // remove a random fraction, but the removed set is reshuffled every frame (a boiling churn)
    }

    /// An opt-in effect added to a layer or the whole blast. Serialized polymorphically ([SerializeReference]) so
    /// new effects are just new subclasses — the "clean but open for experimentation" seam. Two families:
    /// GeometryModifier warps the pixel grid (skew/rotate/squash/wobble); PixelModifier recolours / masks / removes
    /// pixels (tint/dissolve/…). Animatable params are ZUIValues resolved once per frame via Prepare().
    [Serializable]
    public abstract class PyreModifier
    {
        public bool enabled = true;

        /// Resolve this frame's animatable params to plain floats. eval(value, localFieldId) returns the value at
        /// the current progress; localFieldId (0,1,2…) keeps each param's Min-Max randomness independent.
        public virtual void Prepare(Func<ZUIValue, int, float> eval) { }

        /// Label shown in the editor's modifier list.
        public abstract string DisplayName { get; }

        /// Deep copy (for the editor's layer/modifier "Dup"). MemberwiseClone copies value fields; ZUIValue and
        /// Gradient reference fields are cloned so tweaking a copy never bleeds into the original.
        public virtual PyreModifier Clone()
        {
            var m = (PyreModifier)MemberwiseClone();
            foreach (var f in GetType().GetFields())
            {
                object v = f.GetValue(this);
                if (v is ZUIValue zv) f.SetValue(m, Layer.CloneVal(zv));
                else if (v is Gradient g) f.SetValue(m, Layer.CloneGradient(g));
                else if (v is List<ZUIEnvelopePoint> pts)
                    f.SetValue(m, pts.ConvertAll(p => new ZUIEnvelopePoint(p.time, p.value, p.exponent, p.editState)));
            }
            return m;
        }
    }

    // ── geometry: warps the coordinate grid (applied as an inverse map when rasterising) ──────────────────
    [Serializable]
    /// Everything a position-dependent geometry warp needs about the shape it's deforming, so warps can reason in
    /// the shape's own frame (locked to it) instead of the canvas. `center` is the shape centre as an offset from
    /// the canvas centre; `radius` its radius; `vHalf` the canvas half-height. For Bars (no single disc) radius is 0.
    public readonly struct GeoCtx
    {
        public readonly float hHalf;   // canvas half-width  (for direction-aware warps + pivots)
        public readonly float vHalf;   // canvas half-height
        public readonly Vector2 center;
        public readonly float radius;
        public GeoCtx(float hHalf, float vHalf, Vector2 center, float radius)
        { this.hHalf = hHalf; this.vHalf = vHalf; this.center = center; this.radius = radius; }
    }

    public abstract class GeometryModifier : PyreModifier
    {
        /// Undo this modifier's warp on a pixel offset from the canvas centre. phase = a per-frame wobble phase.
        public abstract Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx);

        /// Warps with a HIGHER pass are applied FIRST (they reframe the shape before shape-local warps like Profile
        /// read it). Default 0; Ground raises it so the base-anchor happens before the silhouette is measured.
        public virtual int WarpPass => 0;
    }

    [Serializable]
    public class SkewModifier : GeometryModifier
    {
        [Tooltip("Horizontal shear based on height — leans the layer. Animatable.")]
        public ZUIValue amount = new ZUIValue(0.4f);
        float a;
        public override string DisplayName => "Skew";
        public override void Prepare(Func<ZUIValue, int, float> e) => a = e(amount, 0);
        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx) { off.x -= a * off.y; return off; }
    }

    /// Which axis(es) a Scale modifier affects.
    public enum ScaleAxis
    {
        Vertical,
        Horizontal,
        Both   // one shared value scales both axes together (a uniform zoom), instead of two synced sliders
    }

    /// Scale/stretch about the centre. Axis picks which dimension(s) it affects: Vertical/Horizontal scale just
    /// that one axis (independently animatable); Both scales both axes together from a single shared value — a
    /// uniform zoom in/out — rather than needing to keep two sliders in sync by hand. All three values are
    /// MultiCont (ZUIValue), so any of them can be a flat constant, a random spread, or a full animation curve.
    [Serializable]
    public class ScaleModifier : GeometryModifier
    {
        [Tooltip("Which axis this scales. Vertical/Horizontal scale just that axis; Both scales both axes " +
                 "together from the single Both value (a uniform zoom) instead of needing two synced sliders.")]
        public ScaleAxis axis = ScaleAxis.Both;
        [Tooltip("Vertical scale about the centre. 1 = none, <1 shorter, 0 = collapsed to a hairline, >1 taller. Animatable.")]
        public ZUIValue vertical = new ZUIValue(1f);
        [Tooltip("Horizontal scale about the centre. 1 = none, <1 narrower, 0 = collapsed to a hairline, >1 wider. Animatable.")]
        public ZUIValue horizontal = new ZUIValue(1f);
        [Tooltip("Uniform scale applied to both axes at once (used when Axis = Both). 1 = none, 0 = collapsed to a point. Animatable.")]
        public ZUIValue both = new ZUIValue(1f);

        const float MinScale = 0.001f;   // clamp near-zero rather than snap to 1: 0 should COLLAPSE the shape, not no-op it
        float v, h;
        public override string DisplayName => "Scale";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            switch (axis)
            {
                case ScaleAxis.Vertical: v = e(vertical, 0); h = 1f; break;
                case ScaleAxis.Horizontal: v = 1f; h = e(horizontal, 1); break;
                default: v = h = e(both, 2); break;
            }
            if (Mathf.Abs(v) < MinScale) v = v >= 0f ? MinScale : -MinScale;
            if (Mathf.Abs(h) < MinScale) h = h >= 0f ? MinScale : -MinScale;
        }
        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            off.x /= h; off.y /= v;
            return off;
        }
    }

    [Serializable]
    public class RotateModifier : GeometryModifier
    {
        [Tooltip("Rotation about the pivot, in degrees. Animatable.")]
        public ZUIValue degrees = new ZUIValue(0f);
        [Tooltip("Pivot X in normalized canvas coords: -1 = left edge, 0 = centre, +1 = right edge.")]
        [Range(-1f, 1f)] public float pivotX;
        [Tooltip("Pivot Y in normalized canvas coords: -1 = bottom edge, 0 = centre, +1 = top edge.")]
        [Range(-1f, 1f)] public float pivotY;
        float rad;
        public override string DisplayName => "Rotate";
        public override void Prepare(Func<ZUIValue, int, float> e) => rad = -e(degrees, 0) * Mathf.Deg2Rad;   // inverse
        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (rad == 0f) return off;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            // Rotate about the pivot (default 0,0 = canvas centre): translate to pivot, rotate, translate back.
            Vector2 p = new Vector2(pivotX * ctx.hHalf, pivotY * ctx.vHalf);
            Vector2 d = off - p;
            return p + new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
        }
    }

    [Serializable]
    public class WobbleModifier : GeometryModifier
    {
        [Tooltip("Amplitude (px) of a vertical wobble that ripples the layer horizontally. Animatable.")]
        public ZUIValue amplitude = new ZUIValue(3f);
        [Tooltip("How many wobble ripples run up the canvas. Animatable.")]
        public ZUIValue frequency = new ZUIValue(1f);
        float amp, freq;
        public override string DisplayName => "Wobble";
        public override void Prepare(Func<ZUIValue, int, float> e) { amp = e(amplitude, 0); freq = e(frequency, 1); }
        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (amp != 0f) off.x -= amp * Mathf.Sin(off.y * freq * 0.1f + phase);
            return off;
        }
    }

    /// A radial ripple: displaces pixels along the direction AWAY from the shape centre, following a sine wave
    /// keyed to distance — unlike Wobble (a fixed, linear horizontal ripple), this is RADIAL, and animating Phase
    /// over life sends the ring(s) travelling outward (or inward) through whatever it's applied to, like a
    /// shockwave passing over a shape's own texture/shading.
    [Serializable]
    public class RingWaveModifier : GeometryModifier
    {
        [Tooltip("How far pixels are pushed along the radial direction, in pixels. Animatable.")]
        public ZUIValue amplitude = new ZUIValue(3f);
        [Tooltip("Ring spacing — distance in pixels between successive wave crests. Animatable.")]
        public ZUIValue wavelength = new ZUIValue(10f);
        [Tooltip("Phase position (in wavelengths). Animate this over life — a rising curve sends the ring(s) " +
                 "travelling outward from the centre.")]
        public ZUIValue phase = DefaultPhase();

        float amp, wl, ph;
        public override string DisplayName => "Ring wave";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amp = e(amplitude, 0);
            wl = Mathf.Max(1f, e(wavelength, 1));
            ph = e(phase, 2);
        }

        public override Vector2 InverseWarp(Vector2 off, float phaseSpin, in GeoCtx ctx)
        {
            if (Mathf.Abs(amp) < 0.01f) return off;
            Vector2 d = off - ctx.center;
            float dist = d.magnitude;
            if (dist < 0.01f) return off;
            Vector2 dir = d / dist;
            float wave = Mathf.Sin((dist / wl - ph) * Mathf.PI * 2f);
            return off + dir * (wave * amp);
        }

        static ZUIValue DefaultPhase() => Layer.CurveVal(3f, 0f, 0f, 1f, 3f);   // travels outward ~3 wavelengths over life
    }

    /// Silhouette molder: sets the horizontal WIDTH at each height, so a disc becomes a teardrop / flame /
    /// mushroom. widthByHeight is a spatial curve (0 = canvas bottom → 1 = top) of a width multiplier: a curve
    /// that falls from 1→~0 gives a flame; a narrow stem then a bump gives a mushroom cap; wide-narrow-wide an
    /// hourglass. Stack a couple of profiled layers (a wide "cap", a thin "stem") for real mushroom clouds.
    [Serializable]
    public class ProfileModifier : GeometryModifier
    {
        [Tooltip("Width multiplier vs height (0 = canvas bottom → 1 = top). Falling = flame/teardrop; a bump near " +
                 "the top = a mushroom cap; wide-narrow-wide = an hourglass.")]
        public List<ZUIEnvelopePoint> widthByHeight = DefaultProfile();
        [Tooltip("Blend the profile in (0 = off, 1 = full). Animatable — grow a disc into the profile over life.")]
        public ZUIValue strength = new ZUIValue(1f);

        float str;
        public override string DisplayName => "Profile";
        public override void Prepare(Func<ZUIValue, int, float> e) => str = Mathf.Clamp01(e(strength, 0));
        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (str <= 0.001f || ctx.radius <= 0.001f || widthByHeight == null || widthByHeight.Count == 0) return off;
            // Height is measured in the SHAPE's own frame (0 = its bottom, 1 = its top), NOT the canvas — so the
            // silhouette stays locked to the shape wherever it sits or however big it grows, and composes with Ground
            // (which reframes off.y to the surface before this runs). Width is a horizontal scale about the shape's x.
            float ny = Mathf.Clamp01((off.y - (ctx.center.y - ctx.radius)) / (2f * ctx.radius));
            float w = Mathf.Lerp(1f, Mathf.Max(0.02f, ZUIEnvelopeEvaluator.Evaluate(widthByHeight, ny, 1f)), str);
            off.x = ctx.center.x + (off.x - ctx.center.x) / Mathf.Max(0.02f, w);
            return off;
        }

        public static List<ZUIEnvelopePoint> DefaultProfile() => new()
        {
            new ZUIEnvelopePoint(0f, 1f), new ZUIEnvelopePoint(0.6f, 0.65f), new ZUIEnvelopePoint(1f, 0.12f)
        };
    }

    /// Anchors a shape's BASE to a flat surface line and grows it UP from there (like Bars stream off an edge),
    /// instead of the shape being locked to the canvas centre. `surface` places the line (−1 = bottom edge, 0 =
    /// centre, +1 = top). `stretch` scales the plume's height about that base — animate it 0→N and the shape shoots
    /// up out of the surface. `bury` sinks the base below the line (0 = base sits on it, 0.5 = a half-buried dome for
    /// a ground burst). Pair with Profile for grounded mushrooms / candles / campfires. Applied before Profile.
    [Serializable]
    public class GroundModifier : GeometryModifier
    {
        [Tooltip("Direction the shape grows: 0 = up, 90 = right, 180 = down, −90 = left. The base sits on a surface " +
                 "line perpendicular to this, and the plume shoots out along it. Animatable — sweep it over life.")]
        public ZUIValue angle = new ZUIValue(0f);
        [Tooltip("Where the base sits along the grow direction: −1 = the canvas edge behind it, 0 = centre, +1 = far edge.")]
        [Range(-1f, 1f)] public float surface = -1f;
        [Tooltip("Height multiplier along the grow direction, about the base. 1 = as tall as wide; animate 0→N to " +
                 "shoot out. Animatable.")]
        public ZUIValue stretch = new ZUIValue(1f);
        [Tooltip("Sink the base behind the surface: 0 = base on the line, 0.5 = centre on the line (a dome).")]
        [Range(0f, 1f)] public float bury;

        float k, deg;
        public override int WarpPass => 10;   // reframe the shape onto the surface before Profile measures its height
        public override string DisplayName => "Ground";
        public override void Prepare(Func<ZUIValue, int, float> e) { k = Mathf.Max(0.05f, e(stretch, 0)); deg = e(angle, 1); }
        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (ctx.radius <= 0.001f) return off;   // no disc extent to ground (e.g. Bars)

            float th = deg * Mathf.Deg2Rad;
            Vector2 dir  = new Vector2(Mathf.Sin(th),  Mathf.Cos(th));   // 0° = up (+y); grow direction
            Vector2 perp = new Vector2(Mathf.Cos(th), -Mathf.Sin(th));   // across the plume

            // Reach from the canvas centre to the edge along `dir`, so surface = −1 lands on that edge at any angle.
            float rx = Mathf.Abs(dir.x) > 1e-4f ? ctx.hHalf / Mathf.Abs(dir.x) : float.MaxValue;
            float ry = Mathf.Abs(dir.y) > 1e-4f ? ctx.vHalf / Mathf.Abs(dir.y) : float.MaxValue;
            float reach = Mathf.Min(rx, ry);

            float r = ctx.radius;
            float baseAlong = surface * reach - bury * 2f * r * k;
            float cw = Vector2.Dot(ctx.center, perp);   // shape's across offset — positionX/Y slide it along the surface

            // Decompose the pixel into along-grow / across, then rebuild it in the shape's canonical up-growing frame
            // (base → −r, top → +r along y; across → x). Profile + the disc hit-test then work unchanged, and the
            // whole molded plume ends up rooted on the surface and pointing along `dir`.
            float py = (Vector2.Dot(off, dir) - baseAlong) / k - r;
            float px = Vector2.Dot(off, perp) - cw;
            return ctx.center + new Vector2(px, py);
        }
    }

    // ── pixel: recolour / mask / remove ───────────────────────────────────────────────────────────────────
    /// Per-pixel context handed to a PixelModifier.
    public readonly struct PixelInfo
    {
        public readonly int x, y, frame;
        // Geometry-warped canvas position (sub-pixel) — where this pixel maps to AFTER any GeometryModifier
        // (Wobble/Skew/Rotate/Ground/Turbulence/Jagg/...) in this shape's own stack has run. Equals (x+0.5, y+0.5)
        // when no geometry modifier is active. A PixelModifier that hashes/samples a PATTERN by position (like
        // VoronoiCrackModifier) should read wx/wy, not x/y, so that pattern rides along with earlier geometry
        // warps instead of staying glued to the screen while the warped silhouette moves underneath it.
        public readonly float wx, wy;
        public readonly float crossFrac;   // 0..1 across the shape (centre→edge, or bar back→tip)
        public readonly float life;        // the shape's own life, 0..1
        public readonly int hash;          // a stable per-pixel seed
        public readonly int W, H;
        public PixelInfo(int x, int y, float wx, float wy, int frame, float crossFrac, float life, int hash, int W, int H)
        { this.x = x; this.y = y; this.wx = wx; this.wy = wy; this.frame = frame; this.crossFrac = crossFrac; this.life = life; this.hash = hash; this.W = W; this.H = H; }
    }

    [Serializable]
    public abstract class PixelModifier : PyreModifier
    {
        /// Recolour / fade the pixel; return false to drop it entirely.
        public abstract bool ApplyPixel(ref Color col, ref float alpha, in PixelInfo info);
    }

    [Serializable]
    public class TintModifier : PixelModifier
    {
        [Tooltip("Flat multiply tint over the whole layer.")]
        public Color tint = Color.white;
        [Tooltip("A gradient painted ACROSS each shape (centre→edge / bar back→tip) and multiplied in.")]
        public Gradient crossGradient = Layer.WhiteGradient();
        [Tooltip("How strongly the cross gradient applies (0 = off). Animatable.")]
        public ZUIValue crossAmount = new ZUIValue(1f);

        float amt;
        public override string DisplayName => "Tint";
        public override void Prepare(Func<ZUIValue, int, float> e) => amt = Mathf.Clamp01(e(crossAmount, 0));

        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            c = new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a);
            if (amt > 0.001f && crossGradient != null)
            {
                Color g = crossGradient.Evaluate(Mathf.Clamp01(p.crossFrac));
                c = new Color(c.r * Mathf.Lerp(1f, g.r, amt), c.g * Mathf.Lerp(1f, g.g, amt), c.b * Mathf.Lerp(1f, g.b, amt), c.a);
            }
            return true;
        }
    }

    /// Contrast (1 = unchanged). Its own opt-in modifier.
    [Serializable]
    public class ContrastModifier : PixelModifier
    {
        [Tooltip("Contrast. 1 = unchanged, >1 harder, <1 flatter. Animatable.")]
        public ZUIValue amount = new ZUIValue(1f);
        float v;
        public override string DisplayName => "Contrast";
        public override void Prepare(Func<ZUIValue, int, float> e) => v = e(amount, 0);
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            c = new Color(Mathf.Clamp01((c.r - 0.5f) * v + 0.5f), Mathf.Clamp01((c.g - 0.5f) * v + 0.5f),
                          Mathf.Clamp01((c.b - 0.5f) * v + 0.5f), c.a);
            return true;
        }
    }

    /// Brightness (1 = unchanged). Its own opt-in modifier.
    [Serializable]
    public class BrightnessModifier : PixelModifier
    {
        [Tooltip("Brightness multiplier. 1 = unchanged. Animatable.")]
        public ZUIValue amount = new ZUIValue(1f);
        float v;
        public override string DisplayName => "Brightness";
        public override void Prepare(Func<ZUIValue, int, float> e) => v = e(amount, 0);
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            c = new Color(Mathf.Clamp01(c.r * v), Mathf.Clamp01(c.g * v), Mathf.Clamp01(c.b * v), c.a);
            return true;
        }
    }

    /// Saturation (1 = unchanged, 0 = greyscale). Its own opt-in modifier.
    [Serializable]
    public class SaturationModifier : PixelModifier
    {
        [Tooltip("Saturation. 1 = unchanged, 0 = greyscale, >1 more vivid. Animatable.")]
        public ZUIValue amount = new ZUIValue(1f);
        float v;
        public override string DisplayName => "Saturation";
        public override void Prepare(Func<ZUIValue, int, float> e) => v = e(amount, 0);
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            c = new Color(Mathf.Clamp01(Mathf.Lerp(lum, c.r, v)), Mathf.Clamp01(Mathf.Lerp(lum, c.g, v)),
                          Mathf.Clamp01(Mathf.Lerp(lum, c.b, v)), c.a);
            return true;
        }
    }

    /// Radial ray / starburst brightness modulation — N alternating bright/dim spokes around the CANVAS centre
    /// (not the shape's own centre — this keeps it a pure function of the existing PixelInfo, no new plumbing),
    /// like a classic sunburst or a charging energy blast. `rays` sets the spoke count; `sharpness` how crisp the
    /// spokes read (soft sinusoidal glow vs hard alternating blades); `rotation` spins the whole pattern.
    [Serializable]
    public class SunburstModifier : PixelModifier
    {
        [Range(2, 32)]
        [Tooltip("Number of bright rays radiating from the canvas centre.")]
        public int rays = 8;
        [Tooltip("How much brighter the ray peaks get vs the troughs between them (0 = no effect). Animatable — " +
                 "pulse a charge-up.")]
        public ZUIValue strength = new ZUIValue(0.6f);
        [Range(0.5f, 8f)]
        [Tooltip("Ray crispness: 1 = a soft sinusoidal glow, higher = narrower, harder-edged blades.")]
        public float sharpness = 2f;
        [Tooltip("Rotates the whole ray pattern, in degrees. Animatable — spin the burst.")]
        public ZUIValue rotation = new ZUIValue(0f);

        float amt, rotRad;
        public override string DisplayName => "Sunburst";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amt = Mathf.Max(0f, e(strength, 0));
            rotRad = e(rotation, 1) * Mathf.Deg2Rad;
        }

        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            if (amt <= 0.001f) return true;
            float cx = p.W * 0.5f, cy = p.H * 0.5f;
            float dx = (p.x + 0.5f) - cx, dy = (p.y + 0.5f) - cy;
            float ang = Mathf.Atan2(dy, dx) - rotRad;
            float wave = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * rays * 0.5f)), Mathf.Max(0.5f, sharpness));
            float k = 1f + amt * (wave * 2f - 1f);
            c = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
            return true;
        }
    }

    /// Concentric rings of brightness travelling outward across the shape over its own life — a sonar-ping /
    /// energy-pulse look. A pure function of the shape's already-computed crossFrac (radial position) and life —
    /// no new plumbing needed, so it works on Disc, MetaBlob, Bars (back→tip), Sprite alike.
    [Serializable]
    public class PulseRingsModifier : PixelModifier
    {
        [Range(1, 12)]
        [Tooltip("Number of ring cycles across the shape's radius.")]
        public int rings = 4;
        [Tooltip("How fast the rings travel outward over the shape's life (cycles per full life). Animatable.")]
        public ZUIValue speed = new ZUIValue(1f);
        [Tooltip("How much brighter the ring peaks get (0 = no effect). Animatable — pulse it in/out.")]
        public ZUIValue strength = new ZUIValue(0.5f);

        float spd, amt;
        public override string DisplayName => "Pulse rings";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            spd = e(speed, 0);
            amt = Mathf.Max(0f, e(strength, 1));
        }

        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            if (amt <= 0.001f) return true;
            float wave = Mathf.Sin((p.crossFrac * rings - p.life * spd * rings) * Mathf.PI * 2f);
            float k = 1f + amt * wave;
            c = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
            return true;
        }
    }

    /// Quantizes colour (and optionally alpha) into a fixed number of discrete steps per channel — the single
    /// biggest lever for making a soft procedural gradient read as hand-painted banded shading instead of a smooth
    /// shader gradient. Levels is a plain int (not animatable) since a shifting band count reads as flickering, not
    /// motion — same reasoning as JaggModifier.arms.
    [Serializable]
    public class PosterizeModifier : PixelModifier
    {
        [Range(2, 16)]
        [Tooltip("Number of discrete shades per colour channel. Lower = chunkier, more hand-painted bands.")]
        public int levels = 5;
        [Tooltip("Also quantize alpha into the same number of steps (hard transparency bands instead of a smooth fade).")]
        public bool affectAlpha = false;

        public override string DisplayName => "Posterize";
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            int n = Mathf.Max(2, levels);
            float step = 1f / (n - 1);
            c = new Color(Quantize(c.r, step), Quantize(c.g, step), Quantize(c.b, step), c.a);
            if (affectAlpha) a = Quantize(a, step);
            return true;
        }
        static float Quantize(float v, float step) => Mathf.Clamp01(Mathf.Round(Mathf.Clamp01(v) / step) * step);
    }

    /// Converts smooth alpha (from Outer softness, a Bloom halo, a churned Turbulence edge, …) into a hard stipple
    /// using a 4x4 Bayer ORDERED matrix rather than uncorrelated per-pixel noise — an ordered matrix produces the
    /// diagonal crosshatch dither genuine 16/32-bit pixel art uses for shading bands, which reads as more
    /// deliberately hand-drawn than DissolveModifier's random speckle. Purely a function of (x, y, alpha) — no seed
    /// needed, so it's trivially deterministic.
    [Serializable]
    public class OrderedDitherModifier : PixelModifier
    {
        [Tooltip("How much the ordered dither replaces the smooth alpha. 0 = untouched; 1 = fully hard-dithered " +
                 "(a classic retro stipple edge). Animatable — rise it as a shape settles into its final silhouette.")]
        public ZUIValue strength = new ZUIValue(1f);

        static readonly float[] Bayer4x4 =
        {
            0f, 8f, 2f, 10f,
            12f, 4f, 14f, 6f,
            3f, 11f, 1f, 9f,
            15f, 7f, 13f, 5f,
        };

        float amt;
        public override string DisplayName => "Ordered dither";
        public override void Prepare(Func<ZUIValue, int, float> e) => amt = Mathf.Clamp01(e(strength, 0));

        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            if (amt <= 0.001f) return true;
            float threshold = (Bayer4x4[(p.y & 3) * 4 + (p.x & 3)] + 0.5f) / 16f;
            float hard = a >= threshold ? 1f : 0f;
            a = Mathf.Lerp(a, hard, amt);
            return a > 0.003f;
        }
    }

    /// Cellular (Worley/Voronoi) crack pattern — darkens/brightens pixels near the seams of a jittered feature-point
    /// grid, giving a shattered-crystal / cracked-earth / lightning-crackle look. A genuinely different visual
    /// family from PyreNoise's smooth domain-warped Perlin-style field — faceted and linear rather than blobby.
    /// Zoom/Rotation/Drift mirror NoiseField's own domain controls (same reasoning, applied to a cellular field
    /// instead of a smooth one); `seedOffset` is the crack-pattern twin of Layer.sparkleSeed — Static freezes the
    /// pattern, Min-Max re-rolls it every frame (a boiling/crackling reshuffle), a Curve jumps it between distinct
    /// patterns over life. Samples PixelInfo.wx/wy (the geometry-warped position), NOT the raw x/y, so the crack
    /// field rides along with any earlier GeometryModifier (Wobble, Turbulence, Ground, ...) in the same stack
    /// instead of staying glued to the screen while the warped silhouette deforms underneath it.
    [Serializable]
    public class VoronoiCrackModifier : PixelModifier
    {
        [UnityEngine.Serialization.FormerlySerializedAs("cellSize")]
        [Tooltip("Zoom of the cell grid, in pixels — bigger = fewer, larger facets/cracks (zoomed in); smaller = a " +
                 "finer, busier web (zoomed out). Same role as NoiseField's own Zoom, just over a cellular field " +
                 "instead of a smooth one. Animatable.")]
        public ZUIValue zoom = new ZUIValue(10f);
        [Tooltip("Rotates the cell grid about the blast's own Origin marker, in degrees. Animatable — spin the " +
                 "whole crack pattern.")]
        public ZUIValue rotation = new ZUIValue(0f);
        [Tooltip("Drifts the cell grid horizontally (along the grid's OWN, possibly-rotated axis), in pixels — " +
                 "the pattern visibly slides sideways. Animatable.")]
        public ZUIValue driftX = new ZUIValue(0f);
        [Tooltip("Drifts the cell grid vertically (along the grid's own axis). Animatable.")]
        public ZUIValue driftY = new ZUIValue(0f);
        [Tooltip("A sub-seed folded into the cell jitter, in whole-number STEPS (0.7 and 1.4 both land on step 1) " +
                 "— so a slowly-animated Curve jumps between a handful of distinct patterns over life instead of " +
                 "reshuffling into unrelated noise every frame. Static (default) freezes the pattern in place; " +
                 "Min-Max re-rolls a fresh step every frame for a boiling/crackling reshuffle (sparkleSeed's " +
                 "cellular twin). This does NOT pan the pattern smoothly — use Drift X/Y for that.")]
        public ZUIValue seedOffset = new ZUIValue(0f);
        [Tooltip("How much space the cracks eat vs. the cells' own untouched interiors — low leaves wide open " +
                 "cell faces with thin seams; high thickens the seams until the cell interiors shrink to nothing " +
                 "and the whole field reads as crack. Animatable — widen the cracks over life for a spreading-" +
                 "fracture look, or eat cells away entirely as the shape dies.")]
        public ZUIValue crackWidth = new ZUIValue(0.15f);
        [Tooltip("Over life = one flat tint for the whole crack pattern, sampled from the gradient at the blast's " +
                 "own life. Fill = the gradient is painted across each SEAM's own width instead (0 = away from a " +
                 "seam, 1 = right on it) — e.g. a bright core fading to a darker edge along every crack line.")]
        public ColorMode mode = ColorMode.OverLife;
        [Tooltip("Colour tinted into the crack lines (dark for shattered stone/crystal; bright for electric arcs). " +
                 "Read per Mode above — a flat two-stop gradient behaves like the old single flat tint colour.")]
        public Gradient crackTint = Black();
        [Tooltip("How strongly the crack tint applies (0 = off). Animatable — flicker or fade the cracks over life.")]
        public ZUIValue strength = new ZUIValue(1f);
        [Tooltip("Also tint each CELL's interior with a random per-cell shade (a faceted/stained-glass look) " +
                 "instead of leaving interiors untouched.")]
        public bool tintCells = false;
        [Tooltip("How strongly the per-cell interior shading applies (only used when Tint cells is on). " +
                 "Animatable — the per-cell shade PATTERN stays fixed (same hash), only how much of it shows " +
                 "ramps, so this animates smoothly rather than flickering.")]
        public ZUIValue cellShadeStrength = new ZUIValue(0.25f);

        float size, amt, rotRad, driftXv, driftYv, width, cellShade;
        int seedStep;
        Vector2 originPx;
        public override string DisplayName => "Voronoi crack";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            size = Mathf.Max(1f, e(zoom, 0));
            amt = Mathf.Clamp01(e(strength, 1));
            rotRad = e(rotation, 2) * Mathf.Deg2Rad;
            driftXv = e(driftX, 3);
            driftYv = e(driftY, 4);
            // Rounded to a whole STEP (not scaled up first) so a smoothly-animated Curve only jumps the pattern at
            // each integer crossing instead of reshuffling into unrelated noise on every tiny fractional change.
            seedStep = Mathf.RoundToInt(e(seedOffset, 5));
            width = Mathf.Clamp(e(crackWidth, 6), 0.01f, 3f);
            cellShade = Mathf.Clamp01(e(cellShadeStrength, 7));
        }

        /// The blast's own Origin marker (BlastSpec.origin), in canvas pixels — set once per frame by BlastRenderer
        /// (mirrors PinWarpModifier.SetFrame) so Rotation can pivot on the same point the preview's ✛ handle shows,
        /// instead of the canvas corner.
        internal void SetOrigin(Vector2 originPixels) => originPx = originPixels;

        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            if (amt <= 0.001f && !tintCells) return true;

            int hash = seedStep != 0 ? unchecked(p.hash + seedStep * 92821) : p.hash;

            // Rotate about the blast's Origin marker (not the canvas corner), then drift along the — now possibly
            // rotated — grid axes, matching NoiseField's own rotate-then-drift order.
            float rx = p.wx - originPx.x, ry = p.wy - originPx.y;
            if (rotRad != 0f)
            {
                float cr = Mathf.Cos(rotRad), sr = Mathf.Sin(rotRad);
                float nrx = rx * cr - ry * sr, nry = rx * sr + ry * cr;
                rx = nrx; ry = nry;
            }
            rx += driftXv; ry += driftYv;

            float x = rx / size, y = ry / size;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);

            float f1 = float.MaxValue, f2 = float.MaxValue;
            int bestCx = cx, bestCy = cy;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int gx = cx + ox, gy = cy + oy;
                    float jx = BlastRenderer.Hash01(hash, gx * 2, gy * 2);
                    float jy = BlastRenderer.Hash01(hash, gx * 2 + 1, gy * 2 + 1);
                    float fx = gx + jx, fy = gy + jy;
                    float d = (fx - x) * (fx - x) + (fy - y) * (fy - y);
                    if (d < f1) { f2 = f1; f1 = d; bestCx = gx; bestCy = gy; }
                    else if (d < f2) f2 = d;
                }
            f1 = Mathf.Sqrt(f1); f2 = Mathf.Sqrt(f2);

            if (tintCells && cellShade > 0.001f)
            {
                float shade = BlastRenderer.Hash01(unchecked(hash ^ 0x37A19E13), bestCx, bestCy);
                float k = 1f + (shade - 0.5f) * 2f * cellShade;
                c = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
            }

            if (amt > 0.001f)
            {
                float gap = f2 - f1;
                float k = 1f - Mathf.Clamp01(gap / width);   // 1 at the seam, 0 away from it
                if (k > 0.001f && crackTint != null)
                {
                    Color tint = crackTint.Evaluate(mode == ColorMode.Fill ? k : Mathf.Clamp01(p.life));
                    float t = k * amt;
                    c = new Color(Mathf.Lerp(c.r, c.r * tint.r, t), Mathf.Lerp(c.g, c.g * tint.g, t),
                                  Mathf.Lerp(c.b, c.b * tint.b, t), c.a);
                    a = Mathf.Lerp(a, a * tint.a, t);
                }
            }
            return a > 0.003f;
        }

        static Gradient Black()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }

    [Serializable]
    public class DissolveModifier : PixelModifier
    {
        [Tooltip("0 = nothing removed, 1 = everything gone. Animatable — the classic 'crumble away at the end' is " +
                 "this ramping 0→1 over the layer's life.")]
        public ZUIValue amount = DefaultAmount();
        [Tooltip("Erase = hard random holes; Fade = all pixels go transparent; Bleed = holes with a soft edge; " +
                 "Scatter = holes that reshuffle every frame (a boiling churn).")]
        public DissolveMode mode = DissolveMode.Erase;

        const float BleedBand = 0.14f;
        float amt;
        public override string DisplayName => "Dissolve";
        public override void Prepare(Func<ZUIValue, int, float> e) => amt = Mathf.Clamp01(e(amount, 0));

        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            if (amt <= 0.001f) return true;
            switch (mode)
            {
                case DissolveMode.Fade:
                    a *= 1f - amt;
                    return a > 0.003f;
                case DissolveMode.Scatter:
                {
                    float h = BlastRenderer.Hash01(unchecked(p.hash ^ (p.frame * 92821)), p.x, p.y);
                    return h >= amt;
                }
                case DissolveMode.Bleed:
                {
                    float h = BlastRenderer.Hash01(p.hash, p.x, p.y);
                    if (h < amt) return false;
                    a *= Mathf.Clamp01((h - amt) / BleedBand);   // pixels just above the cut fade out
                    return a > 0.003f;
                }
                default: // Erase
                {
                    float h = BlastRenderer.Hash01(p.hash, p.x, p.y);
                    return h >= amt;
                }
            }
        }

        static ZUIValue DefaultAmount() => Layer.CurveVal(1f, 0f, 0f, 0.6f, 0f, 1f, 1f);
    }

    /// A moving transparency mask: sweeps a soft-edged shape across the layer, multiplying alpha. A disc that
    /// reveals from the centre out or eats inward from the edges, or a horizontal / vertical wipe (scene-transition
    /// style). Animate `progress` (0→1) to drive the sweep; sharpness sets the edge hardness; size scales it;
    /// rotation + offset place it.
    [Serializable]
    public class AlphaMaskModifier : PixelModifier
    {
        public MaskShape shape = MaskShape.DiscOut;
        [Tooltip("0→1 sweep position. A rising envelope reveals the layer; a falling one hides it. Animatable.")]
        public ZUIValue progress = DefaultProgress();
        [Range(0f, 1f)]
        [Tooltip("Edge hardness: 1 = a crisp cut, 0 = a wide soft gradient.")]
        public float sharpness = 0.6f;
        [Tooltip("Mask scale. 1 = spans the half-canvas. Animatable.")]
        public ZUIValue size = new ZUIValue(1f);
        [Tooltip("Mask rotation in degrees (rotates the wipe direction / disc axis). Animatable.")]
        public ZUIValue rotation = new ZUIValue(0f);
        [Tooltip("Mask centre offset X, in half-canvas units (-1..1).")]
        public float offsetX = 0f;
        [Tooltip("Mask centre offset Y, in half-canvas units (-1..1).")]
        public float offsetY = 0f;

        [Tooltip("Noise shape only: domain-warp strength — how much the noise field bends on itself. 0 = plain " +
                 "smooth noise (a blobby cloud); higher = more churned, organic eddies.")]
        [Range(0f, 2f)] public float noiseWarp = 0.6f;
        [Tooltip("Noise shape only: extra X drift added to the noise sample position over the mask's progress, in " +
                 "half-canvas units. Animate it for a cloud that visibly rolls/billows sideways as it reveals.")]
        public ZUIValue noiseDriftX = new ZUIValue(0f);
        [Tooltip("Noise shape only: extra Y drift added to the noise sample position, in half-canvas units. Animatable.")]
        public ZUIValue noiseDriftY = new ZUIValue(0f);

        float prog, siz, rotRad, driftX, driftY;
        public override string DisplayName => "Alpha mask";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            prog = Mathf.Clamp01(e(progress, 0));
            siz = Mathf.Max(0.01f, e(size, 1));
            rotRad = e(rotation, 2) * Mathf.Deg2Rad;
            driftX = e(noiseDriftX, 3);
            driftY = e(noiseDriftY, 4);
        }

        public override bool ApplyPixel(ref Color col, ref float a, in PixelInfo p)
        {
            float halfW = p.W * 0.5f, halfH = p.H * 0.5f;
            float unit = Mathf.Max(1f, Mathf.Min(halfW, halfH));
            float nx = ((p.x + 0.5f) - halfW) / unit - offsetX;
            float ny = ((p.y + 0.5f) - halfH) / unit - offsetY;
            if (rotRad != 0f)
            {
                float c = Mathf.Cos(-rotRad), s = Mathf.Sin(-rotRad);
                float rx = nx * c - ny * s; ny = nx * s + ny * c; nx = rx;
            }

            if (shape == MaskShape.Wedge)
            {
                // Remove an angular slice of `prog`·360° (symmetric about +x; rotation aims the mouth). prog 0.25 =
                // a pac-man, 0.5 = a half. Sharpness feathers the two cut edges.
                float d = Mathf.Abs(Mathf.Atan2(ny, nx));            // 0 (+x) … π (−x)
                float half = Mathf.Clamp01(prog) * Mathf.PI;        // half-angle of the removed wedge
                float edge = Mathf.Max(0.0001f, (1f - sharpness) * 0.4f);
                a *= Mathf.Clamp01((d - half) / edge);              // inside the wedge → 0 (removed)
                return a > 0.003f;
            }

            float field;
            switch (shape)
            {
                case MaskShape.SwipeH: field = (nx / siz) * 0.5f + 0.5f; break;   // 0..1 left→right
                case MaskShape.SwipeV: field = (ny / siz) * 0.5f + 0.5f; break;   // 0..1 bottom→top
                case MaskShape.Noise:                                            // an irregular field instead of a clean edge
                    field = PyreNoise.Sample((nx + driftX) / siz, (ny + driftY) / siz, p.hash, noiseWarp);
                    break;
                default: field = Mathf.Sqrt(nx * nx + ny * ny) / siz; break;      // radial 0 = centre
            }

            float w = Mathf.Max(0.001f, (1f - sharpness) * 0.5f);
            float threshold = shape == MaskShape.DiscIn ? (1f - prog) : prog;     // visible where field < threshold
            // GLSL-style smoothstep(edge0, edge1, field) — Unity's Mathf.SmoothStep interpolates BETWEEN its args,
            // which is a different thing. 0 inside the threshold → fully visible; 1 outside → masked.
            float ss = Mathf.Clamp01((field - (threshold - w)) / (2f * w));
            ss = ss * ss * (3f - 2f * ss);
            a *= 1f - ss;
            return a > 0.003f;
        }

        static ZUIValue DefaultProgress() => Layer.CurveVal(1f, 0f, 0f, 1f, 1f);   // reveal over life
    }

    // ── post: whole-frame passes run AFTER compositing (neighbourhood effects a per-pixel modifier can't do) ──────
    /// A modifier that processes the finished frame buffer in place. Lives in the blast's GLOBAL modifier list and
    /// runs once per frame after every layer composites (in list order). This is how bloom/outline — which read a
    /// pixel's NEIGHBOURS — are possible at all, since geometry/pixel modifiers only see one pixel at a time.
    public abstract class PostModifier : PyreModifier
    {
        public abstract void Apply(Color32[] buf, int W, int H);

        protected static byte ToByte(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

        /// The blast's own progress (0..1) this frame, set by BlastRenderer right before Prepare/Apply — mirrors
        /// PinWarpModifier's SetFrame hook (a small, deliberately isolated addition rather than changing the
        /// shared Prepare contract). Lets a Post modifier offer an "Over life" option (one sampled value/colour for
        /// the whole frame) alongside its spatial one; most Post modifiers don't need it and can ignore it.
        protected float life;
        internal void SetLife(float l) => life = l;
    }

    /// Bloom / glow: bright pixels bleed a soft halo outward (additive), and the halo lifts alpha so it glows into
    /// the transparent surround. Essential for energy weapons/blasts. `threshold` picks what's "bright", `radius` how
    /// far it spreads, `intensity` how strong (animatable — pulse the glow).
    [Serializable]
    public class BloomModifier : PostModifier
    {
        [Range(0f, 1f)]
        [Tooltip("Brightness a pixel must exceed to bloom.")]
        public float threshold = 0.6f;
        [Range(0, 16)]
        [Tooltip("How far the glow spreads, in pixels.")]
        public int radius = 4;
        [Tooltip("Glow strength, added back additively. Animatable — pulse the glow.")]
        public ZUIValue intensity = new ZUIValue(1.2f);

        float inten;
        public override string DisplayName => "Bloom (glow)";
        public override void Prepare(Func<ZUIValue, int, float> e) => inten = Mathf.Max(0f, e(intensity, 0));

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (inten <= 0.001f || radius < 1) return;
            int n = W * H;
            var br = new float[n * 3];
            float denom = Mathf.Max(0.001f, 1f - threshold);
            for (int i = 0; i < n; i++)
            {
                var c = buf[i];
                float a = c.a * (1f / 255f);
                float lum = (c.r + c.g + c.b) * (1f / (3f * 255f)) * a;
                float k = (lum - threshold) / denom;
                if (k <= 0f) continue;
                k = Mathf.Clamp01(k);
                br[i * 3] = c.r * (1f / 255f) * k;
                br[i * 3 + 1] = c.g * (1f / 255f) * k;
                br[i * 3 + 2] = c.b * (1f / 255f) * k;
            }
            BoxBlur3(br, W, H, radius);
            for (int i = 0; i < n; i++)
            {
                float rr = br[i * 3], gg = br[i * 3 + 1], bb = br[i * 3 + 2];
                if (rr <= 0f && gg <= 0f && bb <= 0f) continue;
                var c = buf[i];
                float oldA = c.a * (1f / 255f);
                // Blend in PREMULTIPLIED space (old straight colour × its own alpha, plus the additive glow energy),
                // then divide back down by the NEW alpha to get straight colour again — buf[] is straight alpha
                // throughout this codebase (see BlastRenderer.Over), so skipping this step double-dims the glow
                // over any pixel that starts more transparent than it ends: storing straight colour ≈ glowValue at
                // alpha ≈ glowValue displays as glowValue², i.e. a DARK halo instead of a bright one.
                float addA = (rr + gg + bb) * (1f / 3f) * inten;
                float newA = Mathf.Clamp01(oldA + addA);
                float r = c.r * (1f / 255f), g = c.g * (1f / 255f), b = c.b * (1f / 255f);
                if (newA > 0.0001f)
                {
                    r = Mathf.Clamp01((r * oldA + rr * inten) / newA);
                    g = Mathf.Clamp01((g * oldA + gg * inten) / newA);
                    b = Mathf.Clamp01((b * oldA + bb * inten) / newA);
                }
                buf[i] = new Color32(ToByte(r), ToByte(g), ToByte(b), ToByte(newA));
            }
        }

        // Separable box blur on an interleaved rgb float buffer (O(n·radius) per axis).
        static void BoxBlur3(float[] rgb, int W, int H, int R)
        {
            var tmp = new float[rgb.Length];
            float inv = 1f / (2 * R + 1);
            for (int y = 0; y < H; y++)
                for (int ch = 0; ch < 3; ch++)
                    for (int x = 0; x < W; x++)
                    {
                        float s = 0f;
                        for (int dx = -R; dx <= R; dx++) { int xx = Mathf.Clamp(x + dx, 0, W - 1); s += rgb[(y * W + xx) * 3 + ch]; }
                        tmp[(y * W + x) * 3 + ch] = s * inv;
                    }
            for (int x = 0; x < W; x++)
                for (int ch = 0; ch < 3; ch++)
                    for (int y = 0; y < H; y++)
                    {
                        float s = 0f;
                        for (int dy = -R; dy <= R; dy++) { int yy = Mathf.Clamp(y + dy, 0, H - 1); s += tmp[(yy * W + x) * 3 + ch]; }
                        rgb[(y * W + x) * 3 + ch] = s * inv;
                    }
        }
    }

    /// Outline: draws a border in the transparent ring around the shape's silhouette. `mode` picks how `color` is
    /// read — Fill samples it across the thickness (inner edge → outer): a flat colour gives a sharp one-colour
    /// outline, a gradient fades/recolours outward (multiple stops = concentric bands). Over life instead samples
    /// ONE colour from the whole gradient, at the blast's own life — the same OverLife-vs-Fill split every spatial
    /// shape fill already offers (see ColorMode), reused here rather than inventing a parallel scheme. `size` is
    /// the thickness (animatable — grow it out).
    [Serializable]
    public class OutlineModifier : PostModifier
    {
        [Tooltip("Over life = one flat colour for the whole outline, sampled from the gradient at the blast's own " +
                 "life 0→1. Fill = the gradient is read across the outline's thickness (0 = inner edge, 1 = outer).")]
        public ColorMode mode = ColorMode.Fill;
        [Tooltip("Outline colour. Fill mode reads it across the outline's thickness (0 = inner edge, 1 = outer) — " +
                 "flat = a sharp one-colour outline, a gradient fades/recolours/bands outward. Over life mode " +
                 "samples the whole gradient once, at the blast's own life.")]
        public Gradient color = White();
        [Tooltip("Outline thickness in pixels. Animatable — grow the outline outward.")]
        public ZUIValue size = new ZUIValue(1f);
        [Range(0.01f, 1f)]
        [Tooltip("Alpha above which a pixel counts as part of the shape (the silhouette the outline hugs).")]
        public float alphaThreshold = 0.3f;
        [Range(0f, 4f)]
        [Tooltip("Fades the outline's OWN alpha near its INNER edge (right against the shape's silhouette) — 0 " +
                 "= a hard cutoff there (the default, crisp look), higher fades it in gradually moving away " +
                 "from the shape instead of starting at full strength immediately.")]
        public float innerSoftness = 0f;
        [Range(0f, 8f)]
        [Tooltip("Fades the outline's OWN alpha near its OUTER edge (furthest from the shape) — 0 = a hard " +
                 "cutoff exactly at Size (the default), higher fades it out gradually, extending the visible " +
                 "falloff a bit PAST Size. Capped further out than Inner softness since the outward fade " +
                 "typically wants to read as a longer glow/dissipation, while the inner edge (right against " +
                 "the shape) usually wants to stay crisp.")]
        public float outerSoftness = 0f;

        int sz;
        Color overLifeColor;
        public override string DisplayName => "Outline";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            sz = Mathf.Clamp(Mathf.RoundToInt(e(size, 0)), 0, 32);
            if (mode == ColorMode.OverLife && color != null) overLifeColor = color.Evaluate(Mathf.Clamp01(life));
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (sz < 1 || color == null) return;
            byte at = (byte)(alphaThreshold * 255f);
            var src = (Color32[])buf.Clone();
            int R = sz;
            // The outward fade can read a bit past the nominal thickness, so the neighbour search has to reach
            // that far too — otherwise pixels in the fade band beyond R would never find a shape pixel to
            // measure distance from and'd just be skipped.
            int searchR = Mathf.CeilToInt(R + outerSoftness);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    if (src[idx].a > at) continue;   // a shape pixel — the outline goes in the transparent ring only

                    int best2 = int.MaxValue;
                    for (int dy = -searchR; dy <= searchR; dy++)
                    {
                        int yy = y + dy; if (yy < 0 || yy >= H) continue;
                        for (int dx = -searchR; dx <= searchR; dx++)
                        {
                            int xx = x + dx; if (xx < 0 || xx >= W) continue;
                            if (src[yy * W + xx].a <= at) continue;
                            int d2 = dx * dx + dy * dy;
                            if (d2 < best2) best2 = d2;
                        }
                    }
                    float d = Mathf.Sqrt(best2);
                    if (d > R + outerSoftness) continue;   // beyond the thickness (+ its outward fade)

                    // Edge fade: inner (d=0, right at the shape) fades IN over innerSoftness px; outer (d=R)
                    // fades OUT over the next outerSoftness px past R. Independent of the colour gradient's own
                    // 0..1 fraction below, which is about WHICH colour, not how visible the outline is here.
                    float fadeA = 1f;
                    if (innerSoftness > 0.001f) fadeA *= Mathf.Clamp01(d / innerSoftness);
                    if (outerSoftness > 0.001f) fadeA *= Mathf.Clamp01((R + outerSoftness - d) / outerSoftness);
                    if (fadeA <= 0.003f) continue;

                    Color oc;
                    if (mode == ColorMode.OverLife) oc = overLifeColor;
                    else
                    {
                        float frac = R > 1 ? Mathf.Clamp01((Mathf.Min(d, R) - 1f) / (R - 1f)) : 0f;   // 0 inner edge → 1 outer
                        oc = color.Evaluate(frac);
                    }
                    buf[idx] = new Color32(ToByte(oc.r), ToByte(oc.g), ToByte(oc.b), ToByte(oc.a * fadeA));
                }
        }

        static Gradient White()
        {
            var g = new Gradient();
            g.colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) };
            g.alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
            return g;
        }
    }

    /// RGB channel split: samples the RED and BLUE channels from positions offset in opposite directions (radially
    /// outward from the canvas centre by default, or a flat direction), leaving GREEN in place — the classic
    /// energy/impact chromatic-fringe look. A whole-frame pass (it samples neighbouring pixels).
    [Serializable]
    public class ChromaticAberrationModifier : PostModifier
    {
        [Tooltip("How far the red/blue channels split apart, in pixels. Animatable — punch it in on impact, settle out.")]
        public ZUIValue amount = new ZUIValue(1.5f);
        [Tooltip("How much of the fringed result blends over the original image (0 = untouched, 1 = full effect). " +
                 "Animatable — fade the aberration in/out independently of Amount (the split distance itself).")]
        public ZUIValue alpha = new ZUIValue(1f);
        [Tooltip("Radial = split outward from the canvas centre (stronger toward the edges); off = a flat, " +
                 "uniform split along Angle.")]
        public bool radial = true;
        [Tooltip("Used when Radial is off: split direction, in degrees. Animatable — sweep the split direction.")]
        public ZUIValue angleDeg = new ZUIValue(0f);

        float amt, alp, angRad;
        public override string DisplayName => "Chromatic aberration";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amt = Mathf.Max(0f, e(amount, 0));
            alp = Mathf.Clamp01(e(alpha, 1));
            angRad = e(angleDeg, 2) * Mathf.Deg2Rad;
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (amt <= 0.01f || alp <= 0.003f) return;
            var src = (Color32[])buf.Clone();
            float cx = W * 0.5f, cy = H * 0.5f;
            float dirX = Mathf.Cos(angRad), dirY = Mathf.Sin(angRad);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float ox, oy;
                    if (radial)
                    {
                        float dx = (x + 0.5f) - cx, dy = (y + 0.5f) - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float inv = d > 0.01f ? 1f / d : 0f;
                        ox = dx * inv; oy = dy * inv;
                    }
                    else { ox = dirX; oy = dirY; }

                    int rx = Mathf.Clamp(Mathf.RoundToInt(x + ox * amt), 0, W - 1);
                    int ry = Mathf.Clamp(Mathf.RoundToInt(y + oy * amt), 0, H - 1);
                    int bx = Mathf.Clamp(Mathf.RoundToInt(x - ox * amt), 0, W - 1);
                    int by = Mathf.Clamp(Mathf.RoundToInt(y - oy * amt), 0, H - 1);

                    int idx = y * W + x;
                    Color32 rp = src[ry * W + rx], gp = src[idx], bp = src[by * W + bx];
                    // Alpha follows whichever channel-source has the strongest presence, so the fringe doesn't
                    // paint a solid halo outside the original silhouette.
                    byte outA = (byte)Mathf.Max(gp.a, Mathf.Max(rp.a, bp.a));
                    if (alp >= 0.999f) { buf[idx] = new Color32(rp.r, gp.g, bp.b, outA); continue; }

                    Color32 orig = src[idx];
                    buf[idx] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Lerp(orig.r, rp.r, alp)),
                        (byte)Mathf.RoundToInt(Mathf.Lerp(orig.g, gp.g, alp)),
                        (byte)Mathf.RoundToInt(Mathf.Lerp(orig.b, bp.b, alp)),
                        (byte)Mathf.RoundToInt(Mathf.Lerp(orig.a, outA, alp)));
                }
        }
    }

    /// A cheap, general "melt nearby shapes into one blob" effect: box-blur the whole (premultiplied) frame, then
    /// re-threshold alpha with a soft band so overlapping/nearby silhouettes' blurred halos cross the threshold
    /// together and read as fused, while an isolated shape mostly reconstitutes near its own edge. A pixel-space
    /// APPROXIMATION of MetaBlob's exact SDF-field fusion — much cheaper, and (unlike MetaBlob) works on ANY
    /// already-rendered pixels: any layer shape (even Bars/Sprite/NoiseField), any modifier stack, or — as a
    /// global modifier — several different layers melted together after they all composite. `colorBleed`
    /// separately controls how much colour blends across the fused seam, independent of the silhouette fusion.
    [Serializable]
    public class FuseModifier : PostModifier
    {
        [Tooltip("How far the fusing effect reaches, in pixels — bigger blends more distant shapes together. Animatable.")]
        public ZUIValue radius = new ZUIValue(4f);
        [Tooltip("Alpha level pixels must reach (after blurring) to stay solid — lower fuses more eagerly (thicker " +
                 "bridges between shapes); higher keeps shapes more separate (fuses only where they nearly touch). " +
                 "Animatable — rise it over life to pull fused shapes back apart.")]
        public ZUIValue threshold = new ZUIValue(0.5f);
        [Tooltip("Softness of the re-solidified edge — low is closer to a hard cutoff, high a wide soft gradient. Animatable.")]
        public ZUIValue softness = new ZUIValue(0.3f);
        [Tooltip("How much colour blends across the fused seam. 0 = each pixel keeps its own colour (only the " +
                 "silhouette fuses); 1 = colour is fully blurred too (a smooth blended melt). Animatable.")]
        public ZUIValue colorBleed = new ZUIValue(0.4f);

        int rad;
        float thr, soft, bleed;
        public override string DisplayName => "Fuse (blob melt)";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            rad = Mathf.Clamp(Mathf.RoundToInt(e(radius, 0)), 0, 24);
            thr = Mathf.Clamp01(e(threshold, 1));
            soft = Mathf.Max(0.02f, e(softness, 2));
            bleed = Mathf.Clamp01(e(colorBleed, 3));
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (rad < 1) return;

            int n = W * H;
            // Premultiplied straight-alpha buffer (r,g,b,a as floats) so the blur averages ENERGY, not straight
            // colour — same reasoning BloomModifier's blur uses, needed to avoid a dark seam where alpha is low.
            var pre = new float[n * 4];
            for (int i = 0; i < n; i++)
            {
                var c = buf[i];
                float a = c.a * (1f / 255f);
                pre[i * 4] = c.r * (1f / 255f) * a;
                pre[i * 4 + 1] = c.g * (1f / 255f) * a;
                pre[i * 4 + 2] = c.b * (1f / 255f) * a;
                pre[i * 4 + 3] = a;
            }
            BoxBlur4(pre, W, H, rad);

            var src = (Color32[])buf.Clone();
            for (int i = 0; i < n; i++)
            {
                float blurA = pre[i * 4 + 3];
                // GLSL-style smoothstep band centred on `threshold` — nearby/overlapping shapes' blurred halos
                // cross it together (a bridge appears between them); an isolated shape's own blur stays above it
                // out to roughly its original edge, so it doesn't visibly shrink on its own.
                float band = Mathf.Clamp01((blurA - (thr - soft)) / (2f * soft));
                float newA = band * band * (3f - 2f * band);
                if (newA <= 0.003f) { buf[i] = default; continue; }

                Color blurColor = blurA > 0.02f
                    ? new Color(pre[i * 4] / blurA, pre[i * 4 + 1] / blurA, pre[i * 4 + 2] / blurA)
                    : Color.white;
                var s = src[i];
                float origA = s.a * (1f / 255f);
                Color ownColor = origA > 0.02f ? new Color(s.r / 255f, s.g / 255f, s.b / 255f) : blurColor;
                // Where nothing was originally drawn (a newly-bridged gap between shapes), there's no "own"
                // colour to keep — fall back fully to the blurred colour regardless of colorBleed.
                float mix = origA > 0.02f ? bleed : 1f;
                Color fc = Color.Lerp(ownColor, blurColor, mix);
                buf[i] = new Color32(ToByte(fc.r), ToByte(fc.g), ToByte(fc.b), ToByte(newA));
            }
        }

        // Separable box blur on an interleaved rgba float buffer (O(n·radius) per axis) — the 4-channel twin of
        // BloomModifier's BoxBlur3 (that one skips alpha since Bloom only ever ADDS energy back in; Fuse needs
        // alpha blurred too, since alpha IS the silhouette being fused).
        static void BoxBlur4(float[] rgba, int W, int H, int R)
        {
            var tmp = new float[rgba.Length];
            float inv = 1f / (2 * R + 1);
            for (int y = 0; y < H; y++)
                for (int ch = 0; ch < 4; ch++)
                    for (int x = 0; x < W; x++)
                    {
                        float s = 0f;
                        for (int dx = -R; dx <= R; dx++) { int xx = Mathf.Clamp(x + dx, 0, W - 1); s += rgba[(y * W + xx) * 4 + ch]; }
                        tmp[(y * W + x) * 4 + ch] = s * inv;
                    }
            for (int x = 0; x < W; x++)
                for (int ch = 0; ch < 4; ch++)
                    for (int y = 0; y < H; y++)
                    {
                        float s = 0f;
                        for (int dy = -R; dy <= R; dy++) { int yy = Mathf.Clamp(y + dy, 0, H - 1); s += tmp[(yy * W + x) * 4 + ch]; }
                        rgba[(y * W + x) * 4 + ch] = s * inv;
                    }
        }
    }

    /// Jagg: pushes a circle out into an N-armed star by modulating its radius with the angle. `arms` = how many
    /// points; `strength` = how far the arms stick out AND how deep the valleys between them bite in (0 = circle,
    /// →1 = spiky); `twist` aims the points. A radial coordinate scale about the shape centre — soft edges come from
    /// the shape's own Outer softness.
    [Serializable]
    public class JaggModifier : GeometryModifier
    {
        [Range(2, 24)] public int arms = 5;
        [Tooltip("Arm length / valley depth (0 = circle, →1 = spiky star). Animatable.")]
        public ZUIValue strength = new ZUIValue(0.4f);
        [Tooltip("Rotate the star, in degrees. Animatable — spin the points.")]
        public ZUIValue twist = new ZUIValue(0f);

        float s, tw;
        public override string DisplayName => "Jagg (star)";
        public override void Prepare(Func<ZUIValue, int, float> e) { s = Mathf.Clamp(e(strength, 0), 0f, 0.95f); tw = e(twist, 1) * Mathf.Deg2Rad; }
        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (s <= 0.001f) return off;
            Vector2 d = off - ctx.center;
            float ang = Mathf.Atan2(d.y, d.x) - tw;
            float scale = 1f + s * Mathf.Cos(arms * ang);   // >1 at arms (pull the sample in → shape reaches out)
            return ctx.center + d / Mathf.Max(0.05f, scale);
        }
    }

    // ── edge: perturbs ONLY the outer silhouette test, never the fill/gradient sampling ─────────────────────
    /// A modifier that roughens a shape's OUTER boundary (Disc / Crescent / SparkleField, in RasterShape) without
    /// touching anything downstream that reads the shape's true geometry — colour fill (Fill/Flow fill/Noise fill),
    /// the gradient core, hole sizing. Unlike GeometryModifier (which warps the whole coordinate frame, so Wobble/
    /// Jagg/Turbulence also ripple the fill), this only nudges the hit-test radius per angle around the shape
    /// centre — so an asymmetric silhouette (torn, jagged, wavy) can sit over a perfectly clean interior gradient.
    public abstract class EdgeModifier : PyreModifier
    {
        /// Radius delta (px) added to the outer-boundary test at this angle (radians, in the shape's own —
        /// possibly Ring-rotated — frame). `hash` is a stable per-shape seed so scattered instances don't all
        /// share one identical bump pattern.
        public abstract float EdgeOffset(float angleRad, int hash, in GeoCtx ctx);

        /// Width (px) of a soft alpha fade band just inside the (possibly perturbed) boundary. 0 (the default) =
        /// a hard cutoff, matching every plain Disc/Crescent/SparkleField edge. Override to feather your own warp
        /// instead of relying on the layer's own Outer softness (which fades from the shape's TRUE, unperturbed
        /// radius — it doesn't know the boundary moved, so it doesn't track a jagged/wavy edge correctly).
        public virtual float EdgeSoftness(float angleRad, int hash, in GeoCtx ctx) => 0f;
    }

    /// Warps a shape's rim with a domain-warped noise pattern sampled around its circumference — smooth rounded
    /// bumps at Jaggedness 0, a hard faceted/torn edge at Jaggedness 1. A jagged-but-smoothly-shaded look (think a
    /// scorched or torn disc) that JaggModifier can't give, since Jagg warps the fill along with the silhouette.
    [Serializable]
    public class EdgeWarpModifier : EdgeModifier
    {
        [Tooltip("How far the edge bulges in/out at each bump, in pixels. Animatable.")]
        public ZUIValue amplitude = new ZUIValue(2f);
        [Tooltip("Roughly how many bumps run around the shape's rim. Animatable.")]
        public ZUIValue frequency = new ZUIValue(6f);
        [Tooltip("0 = a smooth, rounded, wavy edge. 1 = a hard, faceted, torn/jagged edge. Animatable — roughen up " +
                 "a silhouette over life.")]
        public ZUIValue jaggedness = new ZUIValue(0.5f);
        [Tooltip("Domain-warp strength on the underlying noise — higher makes the bump spacing less regular, more " +
                 "organic. Animatable.")]
        public ZUIValue warp = new ZUIValue(0.4f);
        [Tooltip("Feathers the warped boundary with its own soft alpha fade (0 = a hard cutoff, higher = a wider " +
                 "soft edge) — unlike the layer's own Outer softness, this fade correctly tracks the moved, " +
                 "jagged/wavy boundary rather than the shape's original circle. Animatable.")]
        public ZUIValue softness = new ZUIValue(0f);

        float amp, freq, jag, wrp, soft;
        public override string DisplayName => "Edge warp";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amp = e(amplitude, 0);
            freq = Mathf.Max(1f, e(frequency, 1));
            soft = Mathf.Max(0f, e(softness, 2));
            jag = Mathf.Clamp01(e(jaggedness, 3));
            wrp = Mathf.Clamp(e(warp, 4), 0f, 2f);
        }

        public override float EdgeOffset(float angleRad, int hash, in GeoCtx ctx)
        {
            if (Mathf.Abs(amp) < 0.01f) return 0f;
            // Sample noise at a point walking a circle of radius `freq` — since (cos,sin) is exactly periodic over
            // 2π, this is automatically seamless with no wrap artefact at the angle 0/2π boundary.
            float nx = Mathf.Cos(angleRad) * freq, ny = Mathf.Sin(angleRad) * freq;
            float n = PyreNoise.Sample(nx, ny, hash, wrp);
            float stepped = Mathf.Floor(n * 5f) / 5f;               // hard bands → a faceted/torn look
            float shaped = Mathf.Lerp(n, stepped, jag);
            return (shaped * 2f - 1f) * amp;
        }

        public override float EdgeSoftness(float angleRad, int hash, in GeoCtx ctx) => soft;
    }

    /// Domain-warped noise displacement — the churn/roll engine. Displaces the sampled coordinate by a 2D noise
    /// field whose own sampling domain can SPIN (Rotation) and DRIFT (Offset X/Y) over life, so whatever it's
    /// applied to visibly roils and turns rather than sitting on a static dent. Zoom sets the noise frequency
    /// (bigger = larger, slower-looking eddies); Amplitude how far pixels displace. Stack on a Disc/MetaBlob with
    /// a spatial fill (Fill / Flow fill) to churn the colour bands themselves (a churning fireball), or after
    /// Ground/Profile to roll an already-molded silhouette (a mushroom cloud's characteristic turning cap) — animate
    /// Rotation as a rising curve for an accelerating roll timed against the shape's own growth.
    [Serializable]
    public class TurbulenceModifier : GeometryModifier
    {
        [Tooltip("How far pixels are displaced by the noise field, in pixels. Animatable — rise it in as the shape matures.")]
        public ZUIValue amplitude = new ZUIValue(4f);
        [Tooltip("Noise frequency — bigger = larger, slower-looking eddies; smaller = fine, busy churn. Animatable.")]
        public ZUIValue zoom = new ZUIValue(24f);
        [Tooltip("Rotates the noise field's own sampling domain, in degrees — this is what makes the churn visibly " +
                 "SPIN in place (a mushroom cloud's roll). Animatable — a rising curve = an accelerating roll.")]
        public ZUIValue rotation = new ZUIValue(0f);
        [Tooltip("Scrolls the noise field horizontally over life, in pixels — the pattern itself drifts rather " +
                 "than the displacement just sitting still. Animatable.")]
        public ZUIValue offsetX = new ZUIValue(0f);
        [Tooltip("Scrolls the noise field vertically over life, in pixels. Animatable.")]
        public ZUIValue offsetY = new ZUIValue(0f);
        [Range(0f, 2f)]
        [Tooltip("Domain-warp strength — how much the noise bends on itself (0 = plain smooth noise, higher = " +
                 "more churned/organic eddies).")]
        public float warp = 0.6f;

        float amp, zm, rotRad, offX, offY;
        public override string DisplayName => "Turbulence";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amp = e(amplitude, 0);
            zm = Mathf.Max(1f, e(zoom, 1));
            rotRad = e(rotation, 2) * Mathf.Deg2Rad;
            offX = e(offsetX, 3);
            offY = e(offsetY, 4);
        }

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (Mathf.Abs(amp) < 0.01f) return off;
            Vector2 d = off - ctx.center;
            if (rotRad != 0f)
            {
                float c = Mathf.Cos(rotRad), s = Mathf.Sin(rotRad);   // spin the SAMPLING domain, not the pixel itself
                d = new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
            }
            float nx = (d.x + offX) / zm;
            float ny = (d.y + offY) / zm;
            // Per-shape-stable seed derived from the shape's own centre, so different scattered shapes churn with
            // different (but still deterministic) noise fields instead of an identical repeated dent.
            int seed = unchecked((Mathf.RoundToInt(ctx.center.x * 8f) * 92821) ^ (Mathf.RoundToInt(ctx.center.y * 8f) * 68111));
            float n1 = PyreNoise.Sample(nx, ny, seed, warp) * 2f - 1f;
            float n2 = PyreNoise.Sample(nx + 31.7f, ny - 17.3f, seed ^ 0x1234567, warp) * 2f - 1f;
            off.x += n1 * amp;
            off.y += n2 * amp;
            return off;
        }
    }

    /// One painted smear stroke: an ordered list of points in canvas-centre pixels (y up).
    [Serializable]
    public class SmudgeStroke
    {
        public List<Vector2> points = new List<Vector2>();
        public SmudgeStroke Clone() => new SmudgeStroke { points = points != null ? new List<Vector2>(points) : new List<Vector2>() };
    }

    /// Smudge: drags paint along PAINTED STROKES, like a finger pulled through wet paint. The user paints one or
    /// more strokes in the preview; each pixel within `size` px of a stroke is displaced BACKWARD along the stroke's
    /// local tangent (sampled from behind → paint streaks forward along the path). `grow` (0..1, animatable) advances
    /// a front along EACH stroke from its start — so the smear grows out over life — and every stroke grows in
    /// parallel (each normalised to its own length, so they all complete together regardless of length). Strokes are
    /// authored in canvas-centre pixels, the frame every shape rasterises in, so Smudge is type-agnostic — it smears
    /// Discs, Bars, MetaBlobs, sprites, whatever is under the stroke.
    [Serializable]
    public class SmudgeModifier : GeometryModifier
    {
        [Tooltip("The painted smear strokes. Paint each in the preview; they all grow in parallel driven by Grow.")]
        public List<SmudgeStroke> strokes = new List<SmudgeStroke>();
        [Tooltip("Brush radius — half the smear WIDTH, in pixels. Pixels this far from a stroke are dragged. Animatable.")]
        public ZUIValue size = new ZUIValue(12f);
        [Tooltip("How far paint is dragged ALONG a stroke, in pixels. Animatable.")]
        public ZUIValue strength = new ZUIValue(12f);
        [Tooltip("How far the smear has grown along each stroke, 0..1 (a front advancing from each stroke's start). " +
                 "Animate 0→1 (the default) so the smear draws itself out over life. All strokes grow in parallel.")]
        public ZUIValue grow = DefaultGrow();

        // Resolved once per frame in Prepare: every stroke's segments flattened, each tagged with its arc-length at the
        // segment start and its stroke's total length (so the Grow front can be normalised per stroke).
        struct Seg { public Vector2 a, dir; public float len, arc, total; }
        float rad, str, prog;
        Seg[] segs;

        public override string DisplayName => "Smudge";

        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            rad = Mathf.Max(0.5f, e(size, 0));
            str = e(strength, 1);
            prog = Mathf.Clamp01(e(grow, 2));

            var list = new List<Seg>();
            if (strokes != null)
                foreach (var s in strokes)
                {
                    var p = s != null ? s.points : null;
                    if (p == null || p.Count < 2) continue;
                    float total = 0f;
                    for (int i = 0; i < p.Count - 1; i++) total += (p[i + 1] - p[i]).magnitude;
                    if (total < 1e-4f) continue;
                    float arc = 0f;
                    for (int i = 0; i < p.Count - 1; i++)
                    {
                        Vector2 a = p[i], d = p[i + 1] - a;
                        float len = d.magnitude;
                        list.Add(new Seg { a = a, dir = len > 1e-4f ? d / len : Vector2.zero, len = len, arc = arc, total = total });
                        arc += len;
                    }
                }
            segs = list.Count > 0 ? list.ToArray() : null;
        }

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (segs == null || Mathf.Abs(str) < 0.01f || prog <= 0.0001f) return off;

            // Nearest point across ALL strokes' segments: its tangent, arc-length along its stroke, and that stroke's
            // total length (so the growth front is per-stroke).
            float best2 = float.MaxValue; Vector2 bestTan = Vector2.zero; float bestArc = 0f, bestTotal = 1f;
            for (int i = 0; i < segs.Length; i++)
            {
                Seg s = segs[i];
                float t = s.len > 1e-4f ? Mathf.Clamp(Vector2.Dot(off - s.a, s.dir), 0f, s.len) : 0f;
                Vector2 cp = s.a + s.dir * t;
                float d2 = (off - cp).sqrMagnitude;
                if (d2 < best2) { best2 = d2; bestTan = s.dir; bestArc = s.arc + t; bestTotal = s.total; }
            }
            if (best2 >= rad * rad) return off;

            // Growth gate: this stroke's front sits at grow·length. Paint behind the front is smeared (and stays);
            // a soft band feathers the leading edge so the smear draws out smoothly rather than snapping on.
            float front = prog * bestTotal;
            float feather = Mathf.Max(2f, rad);
            float gate = Mathf.Clamp01((front - bestArc) / feather);
            if (gate <= 0.0001f) return off;

            float fall = 1f - Mathf.Sqrt(best2) / rad; fall *= fall;   // perpendicular falloff to the brush edge
            return off - bestTan * (str * fall * gate);                // sample from behind → paint streaks forward
        }

        // The base Clone reflects over ZUIValue fields but not the stroke list — deep-copy the strokes so a duplicated
        // modifier gets its own paths instead of sharing the original's.
        public override PyreModifier Clone()
        {
            var m = (SmudgeModifier)base.Clone();
            m.strokes = new List<SmudgeStroke>();
            if (strokes != null) foreach (var s in strokes) m.strokes.Add(s != null ? s.Clone() : new SmudgeStroke());
            return m;
        }

        static ZUIValue DefaultGrow() => Layer.CurveVal(1f, 0f, 0f, 1f, 1f);   // draw the smear out over life
    }

    /// One frame-indexed position sample for a PinWarp dot.
    [Serializable]
    public class PinKeyframe
    {
        public int frame;
        public Vector2 pos;
        public PinKeyframe() { }
        public PinKeyframe(int frame, Vector2 pos) { this.frame = frame; this.pos = pos; }
        public PinKeyframe Clone() => new PinKeyframe(frame, pos);
    }

    /// One draggable pin: exists from its first keyframe's frame onward, at whatever position it's been
    /// dragged to on each keyframed frame — held constant before its first keyframe and after its last, and
    /// smoothly interpolated between consecutive keyframes elsewhere, so only the frames where its motion
    /// actually changes need keyframing at all.
    [Serializable]
    public class PinDot
    {
        public int id;
        [Tooltip("How far this pin's drag reaches, in pixels. Pixels within this radius of the pin's REST " +
                 "position (its very first keyframe) follow the drag, fading to none at the radius edge.")]
        public float radius = 16f;
        // Kept sorted by frame — the editor's add/move logic maintains this invariant.
        public List<PinKeyframe> keyframes = new List<PinKeyframe>();

        public PinDot Clone()
        {
            var d = new PinDot { id = id, radius = radius };
            d.keyframes = keyframes != null ? keyframes.ConvertAll(k => k.Clone()) : new List<PinKeyframe>();
            return d;
        }

        /// This pin's REST position — where it was first placed. Drag distance (and the radius-of-influence
        /// anchor) is always measured from here, never from wherever the pin has since moved to.
        public Vector2 RestPos => keyframes != null && keyframes.Count > 0 ? keyframes[0].pos : Vector2.zero;

        /// This pin's position at `frame`: held at the first keyframe's position before it, held at the last
        /// keyframe's position after it, smoothly interpolated between consecutive keyframes in between.
        public Vector2 Evaluate(int frame)
        {
            if (keyframes == null || keyframes.Count == 0) return Vector2.zero;
            if (keyframes.Count == 1 || frame <= keyframes[0].frame) return keyframes[0].pos;
            var last = keyframes[keyframes.Count - 1];
            if (frame >= last.frame) return last.pos;
            for (int i = 0; i < keyframes.Count - 1; i++)
            {
                var a = keyframes[i]; var b = keyframes[i + 1];
                if (frame >= a.frame && frame <= b.frame)
                {
                    float t = b.frame > a.frame ? (frame - a.frame) / (float)(b.frame - a.frame) : 0f;
                    t = t * t * (3f - 2f * t);   // smoothstep ease between keyframes
                    return Vector2.Lerp(a.pos, b.pos, t);
                }
            }
            return last.pos;
        }

        /// Insert a new keyframe at `frame`, or overwrite the existing one there, keeping the list frame-sorted.
        public void SetKeyframe(int frame, Vector2 pos)
        {
            keyframes ??= new List<PinKeyframe>();
            for (int i = 0; i < keyframes.Count; i++)
            {
                if (keyframes[i].frame == frame) { keyframes[i].pos = pos; return; }
                if (keyframes[i].frame > frame) { keyframes.Insert(i, new PinKeyframe(frame, pos)); return; }
            }
            keyframes.Add(new PinKeyframe(frame, pos));
        }
    }

    /// Hand-animated pin/lattice warp: place "pins" anywhere on a layer, then reposition each one on whichever
    /// frames matter — its OTHER frames interpolate automatically (held before its first keyframe, held after
    /// its last). This is the direct, hand-authored answer to "I want THIS exact bit of smoke to move THIS
    /// exact way right here" — a level of specific control no procedural noise/formula effect can give you.
    /// Nearby pixels (within a pin's radius, measured from its REST position — its very first keyframe) drag
    /// along with however far the pin has since moved, fading to no effect at the radius edge; multiple pins'
    /// drags simply add together. Very large drags relative to a pin's radius can fold/invert the warp (a known
    /// limitation of any simple point-warp, not particular to this one) — keep drags roughly within the radius
    /// for predictable results. Authored via the Pin warp box in the modifier's inspector (click the preview to
    /// add/select/drag pins) — see PyreWindow for the editor-side authoring tool.
    [Serializable]
    public class PinWarpModifier : GeometryModifier
    {
        [Tooltip("The pins. Add one via the Pin warp box below (click the preview), then scrub frames and drag " +
                 "it to set keyframes.")]
        public List<PinDot> dots = new List<PinDot>();

        int currentFrame;
        /// Called once per rendered frame by BlastRenderer — a small, PinWarp-specific hook (see BlastRenderer.
        /// CollectMods) so InverseWarp can evaluate each pin's keyframes without adding a frame-index parameter
        /// to the shared Prepare/InverseWarp contract every other modifier already relies on.
        public void SetFrame(int frame) => currentFrame = frame;

        public override string DisplayName => "Pin warp";

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (dots == null || dots.Count == 0) return off;
            Vector2 total = Vector2.zero;
            for (int i = 0; i < dots.Count; i++)
            {
                var dot = dots[i];
                if (dot == null || dot.keyframes == null || dot.keyframes.Count == 0) continue;
                Vector2 rest = dot.RestPos;
                float r = Mathf.Max(0.5f, dot.radius);
                float dist = Vector2.Distance(off, rest);
                if (dist >= r) continue;
                Vector2 cur = dot.Evaluate(currentFrame);
                float w = 1f - dist / r; w = w * w * (3f - 2f * w);   // smoothstep falloff: 1 at centre -> 0 at radius
                total += (cur - rest) * w;
            }
            return off - total;
        }

        // Deep-copy the dots (and their keyframe lists) so a duplicated modifier gets its own pins.
        public override PyreModifier Clone()
        {
            var m = (PinWarpModifier)base.Clone();
            m.dots = dots != null ? dots.ConvertAll(d => d?.Clone() ?? new PinDot()) : new List<PinDot>();
            return m;
        }
    }

    /// Drop shadow: a darkened, offset copy of the shape composited BEHIND it — grounds an orb / gives depth. A
    /// whole-frame post pass (it reads the silhouette). `offset` in px, `color` (usually black w/ alpha) the shadow.
    [Serializable]
    public class DropShadowModifier : PostModifier
    {
        [Tooltip("Shadow offset X in pixels (screen right).")]
        public float offsetX = 3f;
        [Tooltip("Shadow offset Y in pixels (screen DOWN is negative).")]
        public float offsetY = -3f;
        [Tooltip("Shadow colour (alpha = opacity). Animatable opacity via… (flat for now).")]
        public Color color = new Color(0f, 0f, 0f, 0.5f);
        [Range(0.01f, 1f)]
        [Tooltip("Alpha above which a pixel casts a shadow.")]
        public float alphaThreshold = 0.2f;

        public override string DisplayName => "Drop shadow";
        public override void Prepare(Func<ZUIValue, int, float> e) { }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (color.a <= 0.001f) return;
            int dx = Mathf.RoundToInt(offsetX), dy = Mathf.RoundToInt(offsetY);
            if (dx == 0 && dy == 0) return;
            byte at = (byte)(alphaThreshold * 255f);
            var src = (Color32[])buf.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int sx = x - dx, sy = y - dy;                         // the shape pixel that casts here
                    if (sx < 0 || sy < 0 || sx >= W || sy >= H) continue;
                    if (src[sy * W + sx].a <= at) continue;
                    // composite the existing (top) pixel OVER the shadow (bottom).
                    int idx = y * W + x;
                    Color32 top = src[idx];
                    float ta = top.a * (1f / 255f);
                    float sa = color.a * (src[sy * W + sx].a * (1f / 255f));   // shadow follows the caster's alpha
                    float outA = ta + sa * (1f - ta);
                    if (outA <= 0.001f) continue;
                    float r = (top.r * (1f / 255f) * ta + color.r * sa * (1f - ta)) / outA;
                    float g = (top.g * (1f / 255f) * ta + color.g * sa * (1f - ta)) / outA;
                    float b = (top.b * (1f / 255f) * ta + color.b * sa * (1f - ta)) / outA;
                    buf[idx] = new Color32(ToByte(r), ToByte(g), ToByte(b), ToByte(outA));
                }
        }
    }
}
