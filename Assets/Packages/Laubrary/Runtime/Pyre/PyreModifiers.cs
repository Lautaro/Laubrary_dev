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
        SwipeV     // a vertical wipe (bottom → top)
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

    [Serializable]
    public class SquashModifier : GeometryModifier
    {
        [Tooltip("Horizontal squash/stretch about the centre. 1 = none, <1 tall & thin, >1 wide & flat. Animatable.")]
        public ZUIValue amount = new ZUIValue(1f);
        float sq;
        public override string DisplayName => "Squash";
        public override void Prepare(Func<ZUIValue, int, float> e) { sq = e(amount, 0); if (Mathf.Approximately(sq, 0f)) sq = 1f; }
        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx) { off.x /= sq; return off; }
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
        public readonly float crossFrac;   // 0..1 across the shape (centre→edge, or bar back→tip)
        public readonly float life;        // the shape's own life, 0..1
        public readonly int hash;          // a stable per-pixel seed
        public readonly int W, H;
        public PixelInfo(int x, int y, int frame, float crossFrac, float life, int hash, int W, int H)
        { this.x = x; this.y = y; this.frame = frame; this.crossFrac = crossFrac; this.life = life; this.hash = hash; this.W = W; this.H = H; }
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

        float prog, siz, rotRad;
        public override string DisplayName => "Alpha mask";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            prog = Mathf.Clamp01(e(progress, 0));
            siz = Mathf.Max(0.01f, e(size, 1));
            rotRad = e(rotation, 2) * Mathf.Deg2Rad;
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

            float field;
            switch (shape)
            {
                case MaskShape.SwipeH: field = (nx / siz) * 0.5f + 0.5f; break;   // 0..1 left→right
                case MaskShape.SwipeV: field = (ny / siz) * 0.5f + 0.5f; break;   // 0..1 bottom→top
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
}
