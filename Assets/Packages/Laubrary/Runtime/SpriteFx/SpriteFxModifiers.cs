using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// The moving shape an Alpha-Mask modifier sweeps across the layer.
    /// APPEND-ONLY: serialized as an int on every authored mask, so an existing shape must never change index.
    public enum MaskShape
    {
        DiscOut,   // a disc that reveals from the centre outward as progress rises (grow from within)
        DiscIn,    // transparency grows from the edges inward, consuming the frame as progress rises
        SwipeH,    // a horizontal wipe (left → right), like a scene transition
        SwipeV,    // a vertical wipe (bottom → top)
        Wedge,     // a pac-man pie slice removed by angle: progress 0 = none, 0.25 = a quarter bite, 0.5 = half
        Noise,     // an irregular cloud silhouette carved by domain-warped noise instead of a clean geometric edge
        Triangle,  // an apex-up triangle growing from the centre
        Square,    // an edge-on square growing from the centre
        Crescent   // a disc with a second disc bitten out of it — a moon
    }

    /// Shared deterministic noise, built entirely on <see cref="Sfx.Hash01"/> (never UnityEngine.Random or
    /// Mathf.PerlinNoise) so it stays bit-identical across the editor preview, the baker and the runtime player.
    /// Two octaves of bilinear value-noise, the second sampled through a domain WARPED by the first — this is what
    /// makes the field read as churning/rolling rather than a static smooth blob. `warp` (0 = none) sets how much.
    public static class PyreNoise
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

        /// The perpendicular of Sample()'s own spatial gradient (finite differences) — a divergence-free flow
        /// field: it swirls (rotates around high/low spots in the potential) without ever pulling toward or away
        /// from a point, unlike using the noise value itself as a displacement. This is the standard "curl noise"
        /// trick for faking coherent, fluid-like swirl cheaply, without an actual fluid simulation's per-frame
        /// advected state — CurlModifier is the only caller.
        public static Vector2 Curl(float x, float y, int seed, float warp, float eps = 0.6f)
        {
            float dPotY = Sample(x, y + eps, seed, warp) - Sample(x, y - eps, seed, warp);   // d/dy
            float dPotX = Sample(x + eps, y, seed, warp) - Sample(x - eps, y, seed, warp);   // d/dx
            float k = 1f / (2f * eps);
            return new Vector2(dPotY * k, -dPotX * k);   // rotate the gradient 90°
        }

        // Bilinear-interpolated hash lattice (smoothstepped) — smooth, deterministic value noise, 0..1.
        static float ValueNoise(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float h00 = Sfx.Hash01(seed, x0, y0);
            float h10 = Sfx.Hash01(seed, x0 + 1, y0);
            float h01 = Sfx.Hash01(seed, x0, y0 + 1);
            float h11 = Sfx.Hash01(seed, x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), ty);
        }

        // ── Gradient (Perlin-style) noise — an alternative to Sample() above, not a replacement ──────────────
        // Sample()'s VALUE noise interpolates raw random VALUES at each lattice corner, which is what gives it
        // that faintly "blobby, bumps sitting at grid points" look at low octave counts (reported: "I get the
        // feeling it could be sharper/smoother"). Gradient noise interpolates random DIRECTION vectors instead
        // (dotted with the offset to each corner) — the classic fix, since a pure direction field has no bias
        // toward a bump centred exactly on a lattice point. Paired with a QUINTIC fade (not cubic smoothstep)
        // for continuous second derivatives, same as Ken Perlin's own "improved noise". Exposed only through
        // PerlinTurbulenceModifier (a separate, opt-in modifier) rather than swapping Sample() in place, since
        // every existing Turbulence/Curl/Noise-fill/AlphaMask-noise asset is built on Sample()'s own shape —
        // changing it under them would silently reshape everything already saved.
        public static float SampleGradient(float x, float y, int seed, float warp)
        {
            if (warp > 0.001f)
            {
                float wx = GradientNoise(x * 0.5f + 37.1f, y * 0.5f + 11.7f, seed ^ 0x51ED2701);
                float wy = GradientNoise(x * 0.5f - 22.4f, y * 0.5f + 61.3f, seed ^ 0x2C1B3A45);
                x += wx * warp * 4f;
                y += wy * warp * 4f;
            }
            float baseN = GradientNoise(x, y, seed);
            float detail = GradientNoise(x * 2.13f, y * 2.13f, seed ^ 0x7F4A7C15);
            return Mathf.Clamp01((baseN * 0.65f + detail * 0.35f) * 0.5f + 0.5f);
        }

        static float GradientNoise(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            float u = Fade(tx), v = Fade(ty);
            float n00 = DotGrad(seed, x0, y0, tx, ty);
            float n10 = DotGrad(seed, x0 + 1, y0, tx - 1f, ty);
            float n01 = DotGrad(seed, x0, y0 + 1, tx, ty - 1f);
            float n11 = DotGrad(seed, x0 + 1, y0 + 1, tx - 1f, ty - 1f);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v);
        }

        // Hashes a lattice corner to a unit-length pseudo-random direction, then dots it with the offset from
        // that corner to the sample point — the standard Perlin "gradient dotted with distance" term.
        static float DotGrad(int seed, int gx, int gy, float dx, float dy)
        {
            float ang = Sfx.Hash01(seed, gx, gy) * (Mathf.PI * 2f);
            return Mathf.Cos(ang) * dx + Mathf.Sin(ang) * dy;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    /// How a Dissolve modifier eats pixels as its amount rises to 1 (everything gone).
    public enum DissolveMode
    {
        Erase,    // hard-remove a random `amount` fraction of pixels (stable holes)
        Scatter   // remove a random fraction, but the removed set is reshuffled every frame (a boiling churn)
    }

    /// An opt-in effect added to a layer or the whole blast. Serialized polymorphically ([SerializeReference]) so
    /// new effects are just new subclasses — the "clean but open for experimentation" seam. Two families:
    /// GeometryModifier warps the pixel grid (skew/rotate/squash/wobble); PixelModifier recolours / masks / removes
    /// pixels (tint/dissolve/…). Animatable params are ZUIValues resolved once per frame via Prepare().
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
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
                if (v is ZUIValue zv) f.SetValue(m, Sfx.CloneVal(zv));
                else if (v is Gradient g) f.SetValue(m, Sfx.CloneGradient(g));
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

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public abstract class GeometryModifier : PyreModifier
    {
        /// Undo this modifier's warp on a pixel offset from the canvas centre. phase = a per-frame wobble phase.
        public abstract Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx);

        /// Warps with a HIGHER pass are applied FIRST (they reframe the shape before shape-local warps like Profile
        /// read it). Default 0; Ground raises it so the base-anchor happens before the silhouette is measured.
        public virtual int WarpPass => 0;
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class SkewModifier : GeometryModifier
    {
        [Range(-2f, 2f)]
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class ScaleModifier : GeometryModifier
    {
        [Tooltip("Which axis this scales. Vertical/Horizontal scale just that axis; Both scales both axes " +
                 "together from the single Both value (a uniform zoom) instead of needing two synced sliders.")]
        public ScaleAxis axis = ScaleAxis.Both;
        [Range(0f, 3f)]
        [Tooltip("Vertical scale about the centre. 1 = none, <1 shorter, 0 = collapsed to a hairline, >1 taller. Animatable.")]
        public ZUIValue vertical = new ZUIValue(1f);
        [Range(0f, 3f)]
        [Tooltip("Horizontal scale about the centre. 1 = none, <1 narrower, 0 = collapsed to a hairline, >1 wider. Animatable.")]
        public ZUIValue horizontal = new ZUIValue(1f);
        [Range(0f, 3f)]
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class RotateModifier : GeometryModifier
    {
        [Range(-180f, 180f)]
        [Tooltip("Rotation about the pivot, in degrees. Animatable.")]
        public ZUIValue degrees = new ZUIValue(0f);
        [HideInInspector] public float pivotX;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Tooltip("Pivot X in normalized canvas coords: -1 = left edge, 0 = centre, +1 = right edge.")]
        [Range(-1f, 1f)] public ZUIValue pivotXValue = new ZUIValue(0f);
        [HideInInspector] public bool pivotXUpgraded;
        public ZUIValue PivotX { get { if (!pivotXUpgraded) { pivotXValue = new ZUIValue(pivotX); pivotXUpgraded = true; } return pivotXValue; } }
        [HideInInspector] public float pivotY;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Tooltip("Pivot Y in normalized canvas coords: -1 = bottom edge, 0 = centre, +1 = top edge.")]
        [Range(-1f, 1f)] public ZUIValue pivotYValue = new ZUIValue(0f);
        [HideInInspector] public bool pivotYUpgraded;
        public ZUIValue PivotY { get { if (!pivotYUpgraded) { pivotYValue = new ZUIValue(pivotY); pivotYUpgraded = true; } return pivotYValue; } }
        float rad, pvX, pvY;
        public override string DisplayName => "Rotate";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            rad = -e(degrees, 0) * Mathf.Deg2Rad;   // inverse
            pvX = Mathf.Clamp(e(PivotX, 1), -1f, 1f);
            pvY = Mathf.Clamp(e(PivotY, 2), -1f, 1f);
        }
        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (rad == 0f) return off;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            // Rotate about the pivot (default 0,0 = canvas centre): translate to pivot, rotate, translate back.
            Vector2 p = new Vector2(pvX * ctx.hHalf, pvY * ctx.vHalf);
            Vector2 d = off - p;
            return p + new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
        }
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class WobbleModifier : GeometryModifier
    {
        [Range(0f, 32f)]
        [Tooltip("Amplitude (px) of a vertical wobble that ripples the layer horizontally. Animatable.")]
        public ZUIValue amplitude = new ZUIValue(3f);
        [Range(0f, 8f)]
        [Tooltip("How many wobble ripples run up the canvas at once — the SHAPE of the distortion, not its " +
                 "motion. Animatable.")]
        public ZUIValue frequency = new ZUIValue(1f);

        [Range(0f, 8f)]
        [Tooltip("How fast the ripples TRAVEL: full turns of the wave across one play-through. Amplitude and " +
                 "Frequency set how the distortion looks; this is the only thing that makes it move. 0 holds a " +
                 "single frozen pose, 1 is one full cycle, higher ripples faster.")]
        public ZUIValue speed = new ZUIValue(1f);

        float amp, freq, spd;
        public override string DisplayName => "Wobble";
        public override void Prepare(Func<ZUIValue, int, float> e)
        { amp = e(amplitude, 0); freq = e(frequency, 1); spd = e(speed, 2); }
        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            // Default speed 1 leaves this exactly `+ phase`, so every baked Pyre asset is byte-identical.
            if (amp != 0f) off.x -= amp * Mathf.Sin(off.y * freq * 0.1f + phase * spd);
            return off;
        }
    }

    /// Wobble's radial sibling: instead of a fixed horizontal ripple keyed to VERTICAL position, this pushes
    /// pixels OUTWARD/INWARD along the ray from the shape's own centre, with the push amount keyed to ANGLE
    /// around that centre — the same "sin(x*freq + phase)" idiom Wobble uses, just walking around the
    /// circumference instead of up the canvas. Reads as wavy "sunbeams" radiating from the centre, each one
    /// reaching further out or pulling further in than its neighbours; `phase` (the shape's own life, same as
    /// every GeometryModifier gets automatically) sweeps that pattern round-and-round or pulses it in and out
    /// over life for free, exactly like Wobble's own animation already does.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class SunburstWobbleModifier : GeometryModifier
    {
        [Range(0f, 32f)]
        [Tooltip("How far pixels are pushed radially (in/out) at each beam's peak, in pixels. Animatable.")]
        public ZUIValue amplitude = new ZUIValue(3f);
        [Range(1f, 24f)]
        [Tooltip("How many wobbly \"sunbeams\" run around the shape's circumference. Animatable.")]
        public ZUIValue frequency = new ZUIValue(6f);
        [Range(-180f, 180f)]
        [Tooltip("Rotates the beam pattern, in degrees — spin the sunburst in place. Animatable.")]
        public ZUIValue rotation = new ZUIValue(0f);

        [Range(0f, 8f)]
        [Tooltip("How fast the beams PULSE: full turns of the wave across one play-through. Amplitude and " +
                 "Frequency set how the beams look; this is what makes them move. 0 holds one frozen pose.")]
        public ZUIValue speed = new ZUIValue(1f);

        float amp, freq, rotRad, spd;
        public override string DisplayName => "Sunburst wobble";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amp = e(amplitude, 0);
            freq = e(frequency, 1);
            rotRad = e(rotation, 2) * Mathf.Deg2Rad;
            spd = e(speed, 3);
        }

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (amp == 0f) return off;
            Vector2 d = off - ctx.center;
            float dist = d.magnitude;
            if (dist < 0.0001f) return off;
            Vector2 dir = d / dist;
            float ang = Mathf.Atan2(d.y, d.x) + rotRad;
            // Default speed 1 leaves this exactly `+ phase` — every baked Pyre asset stays byte-identical.
            float wobble = amp * Mathf.Sin(ang * freq + phase * spd);
            return off + dir * wobble;
        }
    }

    /// A radial ripple: displaces pixels along the direction AWAY from the shape centre, following a sine wave
    /// keyed to distance — unlike Wobble (a fixed, linear horizontal ripple), this is RADIAL, and animating Phase
    /// over life sends the ring(s) travelling outward (or inward) through whatever it's applied to, like a
    /// shockwave passing over a shape's own texture/shading.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class RingWaveModifier : GeometryModifier
    {
        [Range(0f, 32f)]
        [Tooltip("How far pixels are pushed along the radial direction, in pixels. Animatable.")]
        public ZUIValue amplitude = new ZUIValue(3f);
        [Range(1f, 32f)]
        [Tooltip("Ring spacing — distance in pixels between successive wave crests. Animatable.")]
        public ZUIValue wavelength = new ZUIValue(10f);
        [Range(-6f, 6f)]
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

        static ZUIValue DefaultPhase() => Sfx.CurveVal(3f, 0f, 0f, 1f, 3f);   // travels outward ~3 wavelengths over life
    }

    /// A single expanding shockwave from an arbitrary origin point, with an adjustable angular span: Arc = 360°
    /// reads as a circular explosion (every direction at once); a smaller Arc narrows it to a wedge/cone that
    /// widens with distance from the origin, same as a real blast cone; Arc = 0 collapses to a straight,
    /// CONSTANT-width rod running along Angle — a directional punch-through, like a bullet's exit force — rather
    /// than vanishing to nothing the way a literal zero-width wedge test would. All three are one continuous
    /// formula, not a special-cased branch: at any point a distance r from the origin, the wedge's own angular
    /// half-width is Arc/2, but never allowed to go NARROWER than the half-angle a fixed-width rod of Band width
    /// would subtend at that same distance (atan((Band width/2)/r)) — so as Arc shrinks toward 0, that floor is
    /// what takes over, and the shape it describes is exactly a straight rod of Band width, not a sliver.
    /// Unlike RingWave (a repeating, endlessly-travelling ripple centred on the SHAPE's own centre), this is a
    /// single travelling FRONT from its own independent origin — Radius (animate it rising over life) is how far
    /// that front has currently reached; the push is strongest right at Radius and fades over Band width on
    /// either side, so it reads as one blast sweeping outward through the shape, not a standing pattern.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class PointBlastModifier : GeometryModifier
    {
        [Range(-32f, 32f)]
        [Tooltip("Where the blast originates, in pixels off the shape's own centre. Animatable.")]
        public ZUIValue originX = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Where the blast originates, in pixels off the shape's own centre (vertical). Animatable.")]
        public ZUIValue originY = new ZUIValue(0f);
        [Range(-180f, 180f)]
        [Tooltip("Direction the blast points (the wedge/rod's own centre line), in degrees. Irrelevant at Arc " +
                 "360 (a full circular blast has no single direction). Animatable.")]
        public ZUIValue angleDeg = new ZUIValue(0f);
        [HideInInspector] public float arcDegrees = 360f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0f, 360f)]
        [Tooltip("Angular span. 360 = a full circular explosion in every direction at once. Smaller = a wedge/" +
                 "cone narrowing toward Angle, widening with distance from the origin. 0 = a straight, constant-" +
                 "width ROD along Angle instead of a vanishing sliver — a directional punch-through, like a " +
                 "bullet's exit force.")]
        public ZUIValue arcDegreesValue = new ZUIValue(360f);
        [HideInInspector] public bool arcDegreesUpgraded;
        public ZUIValue ArcDegrees { get { if (!arcDegreesUpgraded) { arcDegreesValue = new ZUIValue(arcDegrees); arcDegreesUpgraded = true; } return arcDegreesValue; } }
        [Range(0f, 1f)]
        [Tooltip("Softens the wedge/rod's own angular edges, as a fraction of its half-width — 0 = a hard cutoff, " +
                 "higher = a more gradual fade at the sides. Has no visible effect at Arc 360 (a full circle has " +
                 "no side edges).")]
        public ZUIValue arcSoftness = new ZUIValue(0.2f);
        [Range(0f, 32f)]
        [Tooltip("How far the blast FRONT has currently travelled from the origin, in pixels — this is the " +
                 "blast's own timeline. Animate it rising over life (the default) so the shockwave visibly " +
                 "expands outward through the shape.")]
        public ZUIValue radius = new ZUIValue(0f);
        [Range(1f, 16f)]
        [Tooltip("Thickness of the travelling shockwave band, in pixels — how far ahead of/behind the current " +
                 "Radius the push still reaches. In Arc 0 (line) mode this IS the rod's own constant width.")]
        public ZUIValue bandWidth = new ZUIValue(12f);
        [Range(-20f, 20f)]
        [Tooltip("Push strength — how far pixels shove outward (away from the origin) at the shockwave's own " +
                 "peak. Negative pulls inward instead. Animatable.")]
        public ZUIValue strength = new ZUIValue(6f);

        float ang, arcSoft, radiusV, band, amt, arcDeg;
        Vector2 originPx;
        public override string DisplayName => "Blast";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            originPx = new Vector2(e(originX, 0), e(originY, 1));
            ang = e(angleDeg, 2) * Mathf.Deg2Rad;
            arcSoft = Mathf.Clamp01(e(arcSoftness, 3));
            radiusV = Mathf.Max(0f, e(radius, 4));
            band = Mathf.Max(0.5f, e(bandWidth, 5));
            amt = e(strength, 6);
            // fid 7 fills this modifier's last slot of the nominal 8-wide per-modifier block — benign: the fid
            // only seeds a param's Min-Max RNG stream.
            arcDeg = Mathf.Clamp(e(ArcDegrees, 7), 0f, 360f);
        }

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (Mathf.Abs(amt) < 0.001f) return off;
            Vector2 origin = ctx.center + originPx;
            Vector2 d = off - origin;
            float r = d.magnitude;
            if (r < 0.001f) return off;
            Vector2 dir = d / r;

            float halfArc = arcDeg * 0.5f * Mathf.Deg2Rad;
            // The floor that turns Arc's own 0-limit into a constant-width ROD instead of a vanishing sliver —
            // see the class doc for the derivation. At Arc 360, halfArc is already Pi, so this floor can never
            // matter (nothing exceeds Pi radians from the centre line either way).
            float rodFloor = Mathf.Atan2(band * 0.5f, Mathf.Max(1f, r));
            float effectiveHalfArc = Mathf.Max(halfArc, rodFloor);

            float angDiff = Mathf.DeltaAngle(ang * Mathf.Rad2Deg, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float absAngDiff = Mathf.Abs(angDiff);
            if (absAngDiff > effectiveHalfArc) return off;

            // A full 360° circle has no seam at all — skip the edge-feathering entirely there. Without this,
            // the point exactly OPPOSITE Angle sits precisely at the [-halfArc,+halfArc] boundary (halfArc = π
            // at 360°) and the softness math below reads that as "right at the edge", fading it to zero even
            // though nothing should ever taper on a full circle (confirmed: every other sampled direction was
            // unaffected, only the exact-180°-opposite point silently zeroed).
            bool fullCircle = arcDeg >= 359.99f;
            float angFalloff = 1f;
            if (!fullCircle && arcSoft > 0.001f)
            {
                float feather = effectiveHalfArc * arcSoft;
                float distFromEdge = effectiveHalfArc - absAngDiff;
                angFalloff = Mathf.Clamp01(distFromEdge / Mathf.Max(0.0001f, feather));
                angFalloff = angFalloff * angFalloff * (3f - 2f * angFalloff);
            }

            // The travelling front: strongest right at the current Radius, fading over Band width either side —
            // a single sweeping shockwave, not a repeating ripple (see RingWave for that).
            float radialDist = Mathf.Abs(r - radiusV);
            float radialFalloff = Mathf.Clamp01(1f - radialDist / (band * 0.5f));
            radialFalloff = radialFalloff * radialFalloff * (3f - 2f * radialFalloff);

            float push = amt * angFalloff * radialFalloff;
            if (Mathf.Abs(push) < 0.0001f) return off;
            return off + dir * push;
        }
    }

    /// Silhouette molder: sets the horizontal WIDTH at each height, so a disc becomes a teardrop / flame /
    /// mushroom. widthByHeight is a spatial curve (0 = canvas bottom → 1 = top) of a width multiplier: a curve
    /// that falls from 1→~0 gives a flame; a narrow stem then a bump gives a mushroom cap; wide-narrow-wide an
    /// hourglass. Stack a couple of profiled layers (a wide "cap", a thin "stem") for real mushroom clouds.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class ProfileModifier : GeometryModifier
    {
        [Tooltip("Width multiplier vs height (0 = canvas bottom → 1 = top). Falling = flame/teardrop; a bump near " +
                 "the top = a mushroom cap; wide-narrow-wide = an hourglass.")]
        public List<ZUIEnvelopePoint> widthByHeight = DefaultProfile();
        [Range(0f, 1f)]
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class GroundModifier : GeometryModifier
    {
        [Range(-180f, 180f)]
        [Tooltip("Direction the shape grows: 0 = up, 90 = right, 180 = down, −90 = left. The base sits on a surface " +
                 "line perpendicular to this, and the plume shoots out along it. Animatable — sweep it over life.")]
        public ZUIValue angle = new ZUIValue(0f);
        [HideInInspector] public float surface = -1f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Tooltip("Where the base sits along the grow direction: −1 = the canvas edge behind it, 0 = centre, +1 = far edge.")]
        [Range(-1f, 1f)] public ZUIValue surfaceValue = new ZUIValue(-1f);
        [HideInInspector] public bool surfaceUpgraded;
        public ZUIValue Surface { get { if (!surfaceUpgraded) { surfaceValue = new ZUIValue(surface); surfaceUpgraded = true; } return surfaceValue; } }
        [Range(0f, 4f)]
        [Tooltip("Height multiplier along the grow direction, about the base. 1 = as tall as wide; animate 0→N to " +
                 "shoot out. Animatable.")]
        public ZUIValue stretch = new ZUIValue(1f);
        [HideInInspector] public float bury;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Tooltip("Sink the base behind the surface: 0 = base on the line, 0.5 = centre on the line (a dome).")]
        [Range(0f, 1f)] public ZUIValue buryValue = new ZUIValue(0f);
        [HideInInspector] public bool buryUpgraded;
        public ZUIValue Bury { get { if (!buryUpgraded) { buryValue = new ZUIValue(bury); buryUpgraded = true; } return buryValue; } }

        float k, deg, surfV, buryV;
        public override int WarpPass => 10;   // reframe the shape onto the surface before Profile measures its height
        public override string DisplayName => "Ground";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            k = Mathf.Max(0.05f, e(stretch, 0));
            deg = e(angle, 1);
            surfV = Mathf.Clamp(e(Surface, 2), -1f, 1f);
            buryV = Mathf.Clamp01(e(Bury, 3));
        }
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
            float baseAlong = surfV * reach - buryV * 2f * r * k;
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public abstract class PixelModifier : PyreModifier
    {
        /// Recolour / fade the pixel; return false to drop it entirely.
        public abstract bool ApplyPixel(ref Color col, ref float alpha, in PixelInfo info);

        // ── Burst bridge (slice #46) ─────────────────────────────────────────────────────────────────────────
        // A shaped modifier (Tint/Contrast/Brightness/Saturation/Posterize/OrderedDither/LayerDissolve/AlphaMask)
        // overrides ResolveSfxOp to pack its already-Prepared floats into a blittable SfxOp, so the SAME per-pixel
        // math runs in the Burst SfxStackJob without managed virtual dispatch. SpriteFxStack.Resolve calls this
        // ONLY on shaped, enabled modifiers (see SpriteFxStack.IsShaped) — the default here is inert. A modifier
        // whose op needs a per-pixel gradient LUT returns lutIndex == -2 (a request sentinel) and its gradient via
        // SfxGradient; Resolve then assigns the real slice index and bakes it. The managed ApplyPixel path is
        // untouched by all this (it still calls the identical kernels directly), so bakes stay byte-identical.
        public virtual SfxOp ResolveSfxOp() => default;
        public virtual Gradient SfxGradient() => null;

        // A modifier that is really a LIST of independent per-pixel steps (Colour replace's hue swaps) resolves
        // to several ops instead of one, so its steps stay ordinary members of the stack rather than needing a
        // variable-length payload inside the fixed-size blittable op. Everything else stays at one.
        public virtual int SfxOpCount => 1;
        public virtual SfxOp ResolveSfxOp(int index) => ResolveSfxOp();
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class TintModifier : PixelModifier
    {
        [Tooltip("Flat multiply tint over the whole layer.")]
        public Color tint = Color.white;
        [Tooltip("A gradient painted ACROSS each shape (centre→edge / bar back→tip) and multiplied in.")]
        public Gradient crossGradient = Sfx.WhiteGradient();
        [Range(0f, 1f)]
        [Tooltip("How strongly the cross gradient applies (0 = off). Animatable.")]
        public ZUIValue crossAmount = new ZUIValue(1f);

        float amt;
        public override string DisplayName => "Tint";
        public override void Prepare(Func<ZUIValue, int, float> e) => amt = Mathf.Clamp01(e(crossAmount, 0));

        TintP P() => new TintP { tr = tint.r, tg = tint.g, tb = tint.b, amt = amt,
                                 hasCross = (amt > 0.001f && crossGradient != null) ? 1 : 0 };

        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            var pp = P();
            Color grad = default;
            if (pp.hasCross != 0) grad = crossGradient.Evaluate(Mathf.Clamp01(p.crossFrac));
            return SfxKernels.KTint(pp, grad, ref c, ref a);
        }

        public override SfxOp ResolveSfxOp()
        {
            var pp = P();
            return new SfxOp { kind = SfxKernel.Tint, tint = pp, lutIndex = pp.hasCross != 0 ? -2 : -1 };
        }
        public override Gradient SfxGradient() => crossGradient;
    }

    /// Contrast (1 = unchanged). Its own opt-in modifier.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class ContrastModifier : PixelModifier
    {
        [Range(0f, 2f)]
        [Tooltip("Contrast. 1 = unchanged, >1 harder, <1 flatter. Animatable.")]
        public ZUIValue amount = new ZUIValue(1f);
        float v;
        public override string DisplayName => "Contrast";
        public override void Prepare(Func<ZUIValue, int, float> e) => v = e(amount, 0);
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
            => SfxKernels.KContrast(new ScalarP { v = v }, ref c, ref a);
        public override SfxOp ResolveSfxOp() => new SfxOp { kind = SfxKernel.Contrast, scalar = new ScalarP { v = v }, lutIndex = -1 };
    }

    /// Brightness (1 = unchanged). Its own opt-in modifier.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class BrightnessModifier : PixelModifier
    {
        [Range(0f, 2f)]
        [Tooltip("Brightness multiplier. 1 = unchanged. Animatable.")]
        public ZUIValue amount = new ZUIValue(1f);
        float v;
        public override string DisplayName => "Brightness";
        public override void Prepare(Func<ZUIValue, int, float> e) => v = e(amount, 0);
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
            => SfxKernels.KBrightness(new ScalarP { v = v }, ref c, ref a);
        public override SfxOp ResolveSfxOp() => new SfxOp { kind = SfxKernel.Brightness, scalar = new ScalarP { v = v }, lutIndex = -1 };
    }

    /// Saturation (1 = unchanged, 0 = greyscale). Its own opt-in modifier.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class SaturationModifier : PixelModifier
    {
        [Range(0f, 2f)]
        [Tooltip("Saturation. 1 = unchanged, 0 = greyscale, >1 more vivid. Animatable.")]
        public ZUIValue amount = new ZUIValue(1f);
        float v;
        public override string DisplayName => "Saturation";
        public override void Prepare(Func<ZUIValue, int, float> e) => v = e(amount, 0);
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
            => SfxKernels.KSaturation(new ScalarP { v = v }, ref c, ref a);
        public override SfxOp ResolveSfxOp() => new SfxOp { kind = SfxKernel.Saturation, scalar = new ScalarP { v = v }, lutIndex = -1 };
    }

    /// Radial ray / starburst SILHOUETTE modulation — N alternating spokes that genuinely reach further out than
    /// the shape's own radius, with the gaps between them pulled in — a proper star, not a tint. Was originally a
    /// PixelModifier that only brightened/darkened alternating wedges (a "dark pattern overlaid", not real spokes
    /// — reported). Reuses JaggModifier's exact mechanism (a radial coordinate scale about the shape centre: >1 at
    /// a ray shrinks the SAMPLE offset, so the shape reaches further out there; <1 between rays grows it, pulling
    /// the silhouette in) — the ray positions/sharpness math is unchanged from the old colour version, just now
    /// driving `d/scale` instead of a colour multiplier. `rays` sets the spoke count; `sharpness` how crisp the
    /// spokes read (soft rounded points vs narrow hard-edged blades — Jagg only ever gives the soft/rounded look,
    /// this is the sharper sibling); `rotation` spins the whole pattern.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class SunburstModifier : GeometryModifier
    {
        [Range(2, 32)]
        [Tooltip("Number of rays radiating from the shape's own centre.")]
        public int rays = 8;
        [Range(0f, 0.95f)]
        [Tooltip("How far the rays reach out (and the gaps pull in) — 0 = a plain circle. Animatable — pulse a " +
                 "charge-up.")]
        public ZUIValue strength = new ZUIValue(0.6f);
        [HideInInspector] public float sharpness = 2f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0.5f, 8f)]
        [Tooltip("Ray crispness: 1 = soft, rounded points (like Jagg); higher = narrower, harder-edged blades.")]
        public ZUIValue sharpnessValue = new ZUIValue(2f);
        [HideInInspector] public bool sharpnessUpgraded;
        public ZUIValue Sharpness { get { if (!sharpnessUpgraded) { sharpnessValue = new ZUIValue(sharpness); sharpnessUpgraded = true; } return sharpnessValue; } }
        [Range(-180f, 180f)]
        [Tooltip("Rotates the whole ray pattern, in degrees. Animatable — spin the burst.")]
        public ZUIValue rotation = new ZUIValue(0f);

        float amt, rotRad, shp;
        public override string DisplayName => "Sunburst";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amt = Mathf.Clamp(e(strength, 0), 0f, 0.95f);
            rotRad = e(rotation, 1) * Mathf.Deg2Rad;
            shp = Mathf.Clamp(e(Sharpness, 2), 0.5f, 8f);
        }

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (amt <= 0.001f) return off;
            Vector2 d = off - ctx.center;
            float ang = Mathf.Atan2(d.y, d.x) - rotRad;
            // Same ray-shaping curve as the old colour version: Pow(|cos|, sharpness) peaks (=1) exactly at each
            // ray line and bottoms out (=0) exactly midway between two rays. Remapped 0..1 -> -1..1 so a ray line
            // pushes OUT (scale > 1, d/scale shrinks -> samples closer in -> the shape reaches further there) and
            // a gap pulls IN (scale < 1, d/scale grows -> samples from beyond the shape's own edge).
            float wave = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * rays * 0.5f)), Mathf.Max(0.5f, shp));
            float scale = 1f + amt * (wave * 2f - 1f);
            return ctx.center + d / Mathf.Max(0.05f, scale);
        }
    }

    /// Concentric rings that genuinely PUSH pixels outward/inward, travelling across the shape over its own life
    /// — a sonar-ping / energy-pulse shockwave, not a tint (was originally a PixelModifier that only brightened/
    /// darkened alternating bands — reported as "just dark semitransparent rings"). Same push-along-the-radial-
    /// direction mechanism as RingWaveModifier, but ring count is RELATIVE TO THE SHAPE'S OWN RADIUS (like the
    /// old colour version's crossFrac) rather than a fixed pixel wavelength — so it keeps its own distinct
    /// authoring feel (rings scale with the shape as it grows/shrinks) instead of duplicating Ring wave outright.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class PulseRingsModifier : GeometryModifier
    {
        [Range(1, 12)]
        [Tooltip("Number of ring cycles across the shape's own radius.")]
        public int rings = 4;
        [Range(-4f, 4f)]
        [Tooltip("How fast the rings travel outward over the shape's life (cycles per full life). Animatable.")]
        public ZUIValue speed = new ZUIValue(1f);
        [Range(0f, 32f)]
        [Tooltip("How far pixels are pushed along the radial direction, in pixels. Animatable — pulse it in/out.")]
        public ZUIValue strength = new ZUIValue(3f);

        float spd, amt;
        public override string DisplayName => "Pulse rings";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            spd = e(speed, 0);
            amt = e(strength, 1);
        }

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (Mathf.Abs(amt) < 0.001f || ctx.radius <= 0.001f) return off;
            Vector2 d = off - ctx.center;
            float dist = d.magnitude;
            if (dist < 0.001f) return off;
            Vector2 dir = d / dist;
            float crossFrac = dist / ctx.radius;   // 0 at the shape's own centre, 1 at its own edge
            float wave = Mathf.Sin((crossFrac * rings - phase * spd * rings) * Mathf.PI * 2f);
            return off + dir * (wave * amt);
        }
    }

    /// Quantizes colour (and optionally alpha) into a fixed number of discrete steps per channel — the single
    /// biggest lever for making a soft procedural gradient read as hand-painted banded shading instead of a smooth
    /// shader gradient. Levels is a plain int (not animatable) since a shifting band count reads as flickering, not
    /// motion — same reasoning as JaggModifier.arms.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class PosterizeModifier : PixelModifier
    {
        [HideInInspector] public int levels = 5;
        [HideInInspector] public bool levelsUpgraded;
        [Range(2f, 16f)]
        [Tooltip("Number of discrete shades per colour channel. Lower = chunkier, more hand-painted bands. " +
                 "Animatable — collapse the shading down to a couple of bands over the event and the sprite " +
                 "posterises itself as it goes.")]
        public ZUIValue levelsValue = new ZUIValue(5f);
        public ZUIValue Levels { get { if (!levelsUpgraded) { levelsValue = new ZUIValue(levels); levelsUpgraded = true; } return levelsValue; } }

        [Tooltip("Also quantize alpha into the same number of steps (hard transparency bands instead of a smooth fade).")]
        public bool affectAlpha = false;

        int lv = 5;
        public override string DisplayName => "Posterize";
        // Rounded at resolve, not authored as an int: the count is discrete but the CURVE through it is not,
        // so an envelope sweeping 16 → 2 steps down through the whole band range instead of jumping.
        public override void Prepare(Func<ZUIValue, int, float> e)
            => lv = Mathf.Clamp(Mathf.RoundToInt(e(Levels, 0)), 2, 16);
        PosterizeP P() => new PosterizeP { levels = lv, affectAlpha = affectAlpha ? 1 : 0 };
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
            => SfxKernels.KPosterize(P(), ref c, ref a);
        public override SfxOp ResolveSfxOp() => new SfxOp { kind = SfxKernel.Posterize, poster = P(), lutIndex = -1 };
    }

    /// Converts smooth alpha (from Outer softness, a Bloom halo, a churned Turbulence edge, …) into a hard stipple
    /// using a 4x4 Bayer ORDERED matrix rather than uncorrelated per-pixel noise — an ordered matrix produces the
    /// diagonal crosshatch dither genuine 16/32-bit pixel art uses for shading bands, which reads as more
    /// deliberately hand-drawn than DissolveModifier's random speckle. Purely a function of (x, y, alpha) — no seed
    /// needed, so it's trivially deterministic.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class OrderedDitherModifier : PixelModifier
    {
        [Range(0f, 1f)]
        [Tooltip("How much the ordered dither replaces the smooth alpha. 0 = untouched; 1 = fully hard-dithered " +
                 "(a classic retro stipple edge). Animatable — rise it as a shape settles into its final silhouette.")]
        public ZUIValue strength = new ZUIValue(1f);

        float amt;
        public override string DisplayName => "Ordered dither";
        public override void Prepare(Func<ZUIValue, int, float> e) => amt = Mathf.Clamp01(e(strength, 0));

        // The exact 4x4 Bayer ordered matrix lives in SfxKernels.Bayer4x4 (a Burst-legal switch) so the managed
        // and Burst paths share one source; ApplyPixel below routes through the same kernel.
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
            => SfxKernels.KOrderedDither(new DitherP { amt = amt }, ref c, ref a, p);
        public override SfxOp ResolveSfxOp() => new SfxOp { kind = SfxKernel.OrderedDither, dither = new DitherP { amt = amt }, lutIndex = -1 };
    }

    /// How VoronoiCrackModifier's crack tint reveals spatially as Spread progress rises — a fracture "actively
    /// spreading" rather than appearing everywhere at once. CenterOut/EdgeIn mirror AlphaMaskModifier's own
    /// DiscOut/DiscIn naming for the same reason (grow from within vs. consumed from the edges).
    public enum CrackSpreadMode
    {
        Uniform,     // every seam tints at once, regardless of position (the original, unconditional behaviour)
        CenterOut,   // cracks nearest the shape's own centre light up first, spreading outward
        EdgeIn,      // cracks nearest the shape's own outer edge light up first, spreading inward
        Both         // lights up from the centre AND the edge simultaneously, meeting in the middle last
    }

    /// Cellular (Worley/Voronoi) crack pattern — darkens/brightens pixels near the seams of a jittered feature-point
    /// grid, giving a shattered-crystal / cracked-earth / lightning-crackle look. A genuinely different visual
    /// family from PyreNoise's smooth domain-warped Perlin-style field — faceted and linear rather than blobby.
    /// Zoom/Rotation/Drift mirror Noise fill's own domain controls (same reasoning, applied to a cellular field
    /// instead of a smooth one); `seedOffset` is the crack-pattern twin of Layer.sparkleSeed — Static freezes the
    /// pattern, Min-Max re-rolls it every frame (a boiling/crackling reshuffle), a Curve jumps it between distinct
    /// patterns over life. Samples PixelInfo.wx/wy (the geometry-warped position), NOT the raw x/y, so the crack
    /// field rides along with any earlier GeometryModifier (Wobble, Turbulence, Ground, ...) in the same stack
    /// instead of staying glued to the screen while the warped silhouette deforms underneath it.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class VoronoiCrackModifier : PixelModifier
    {
        [UnityEngine.Serialization.FormerlySerializedAs("cellSize")]
        [Range(1f, 32f)]
        [Tooltip("Zoom of the cell grid, in pixels — bigger = fewer, larger facets/cracks (zoomed in); smaller = a " +
                 "finer, busier web (zoomed out). Same role as Noise fill's own Zoom, just over a cellular field " +
                 "instead of a smooth one. Animatable.")]
        public ZUIValue zoom = new ZUIValue(10f);
        [Range(-720f, 720f)]
        [Tooltip("Rotates the cell grid about the blast's own Origin marker, in degrees. Animatable — spin the " +
                 "whole crack pattern.")]
        public ZUIValue rotation = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Drifts the cell grid horizontally (along the grid's OWN, possibly-rotated axis), in pixels — " +
                 "the pattern visibly slides sideways. Animatable.")]
        public ZUIValue driftX = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Drifts the cell grid vertically (along the grid's own axis). Animatable.")]
        public ZUIValue driftY = new ZUIValue(0f);
        [Range(-8f, 8f)]
        [Tooltip("A sub-seed folded into the cell jitter, in whole-number STEPS (0.7 and 1.4 both land on step 1) " +
                 "— so a slowly-animated Curve jumps between a handful of distinct patterns over life instead of " +
                 "reshuffling into unrelated noise every frame. Static (default) freezes the pattern in place; " +
                 "Min-Max re-rolls a fresh step every frame for a boiling/crackling reshuffle (sparkleSeed's " +
                 "cellular twin). This does NOT pan the pattern smoothly — use Drift X/Y for that.")]
        public ZUIValue seedOffset = new ZUIValue(0f);
        [Range(0.01f, 3f)]
        [Tooltip("How much space the cracks eat vs. the cells' own untouched interiors — low leaves wide open " +
                 "cell faces with thin seams; high thickens the seams until the cell interiors shrink to nothing " +
                 "and the whole field reads as crack. Animatable — widen the cracks over life for a spreading-" +
                 "fracture look, or eat cells away entirely as the shape dies.")]
        public ZUIValue crackWidth = new ZUIValue(0.15f);
        [Range(0.1f, 8f)]
        [Tooltip("How hard the seam's own edge is — separate from Crack width (which sets HOW WIDE the tinted " +
                 "band is, but always fades linearly across it, the same at any width). 1 = that plain linear " +
                 "fade (default, unchanged from before this field existed). Push it up for a crisp, clean seam " +
                 "with no soft bleed into the cell interior; pull it down for a softer, glowing/blurred crack. " +
                 "This is the control for a hard seam LINE — Spread softness below is a different thing " +
                 "entirely (how gradual the spreading REVEAL's front is over time/position, only in the three " +
                 "non-Uniform Spread modes — it doesn't touch how hard any one seam's own edge looks).")]
        public ZUIValue seamSharpness = new ZUIValue(1f);
        [Tooltip("Over life = one flat tint for the whole crack pattern, sampled from the gradient at the blast's " +
                 "own life. Fill = the gradient is painted across each SEAM's own width instead (0 = away from a " +
                 "seam, 1 = right on it) — e.g. a bright core fading to a darker edge along every crack line.")]
        public ColorMode mode = ColorMode.OverLife;
        [Tooltip("Colour tinted into the crack lines (dark for shattered stone/crystal; bright for electric arcs). " +
                 "Read per Mode above — a flat two-stop gradient behaves like the old single flat tint colour.")]
        public Gradient crackTint = Black();
        [Range(0f, 1f)]
        [Tooltip("How strongly the crack tint applies (0 = off). Animatable — flicker or fade the cracks over life.")]
        public ZUIValue strength = new ZUIValue(1f);
        [Tooltip("Also tint each CELL's interior with a random per-cell shade (a faceted/stained-glass look) " +
                 "instead of leaving interiors untouched.")]
        public bool tintCells = false;
        [Range(0f, 1f)]
        [Tooltip("How strongly the per-cell interior shading applies (only used when Tint cells is on). " +
                 "Animatable — the per-cell shade PATTERN stays fixed (same hash), only how much of it shows " +
                 "ramps, so this animates smoothly rather than flickering.")]
        public ZUIValue cellShadeStrength = new ZUIValue(0.25f);
        [Tooltip("How the crack tint reveals spatially as it spreads, instead of tinting every seam at once: " +
                 "Centre out = cracks nearest the shape's own centre light up first, spreading outward. Edge in " +
                 "= cracks nearest the outer edge light up first, spreading inward. Both = lights up from the " +
                 "centre AND the edge simultaneously, meeting in the middle last. Uniform (default) = the " +
                 "original, unconditional behaviour — every seam alike regardless of position.")]
        public CrackSpreadMode spreadMode = CrackSpreadMode.Uniform;
        [Range(0f, 1f)]
        [Tooltip("0→1 spread position (ignored while Spread mode is Uniform). Animatable — a rising envelope " +
                 "reads as the fracture actively spreading outward/inward/both over the shape's life.")]
        public ZUIValue spreadProgress = DefaultSpreadProgress();
        [HideInInspector] public float spreadSoftness = 0.2f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0f, 1f)]
        [Tooltip("Softness of the spreading reveal's own leading edge — 0 = a hard cutoff between lit and unlit " +
                 "cracks, higher blends more gradually across the front.")]
        public ZUIValue spreadSoftnessValue = new ZUIValue(0.2f);
        [HideInInspector] public bool spreadSoftnessUpgraded;
        public ZUIValue SpreadSoftness { get { if (!spreadSoftnessUpgraded) { spreadSoftnessValue = new ZUIValue(spreadSoftness); spreadSoftnessUpgraded = true; } return spreadSoftnessValue; } }

        float size, amt, rotRad, driftXv, driftYv, width, cellShade, spreadProg, sharpness, spreadSoft;
        int seedStep;
        Vector2 originPx;
        public override string DisplayName => "Voronoi crack";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            size = Mathf.Max(1f, e(zoom, 0));
            amt = Mathf.Clamp01(e(strength, 1));
            rotRad = e(rotation, 2) * Mathf.Deg2Rad;
            driftXv = e(driftX, 3);
            spreadProg = Mathf.Clamp01(e(spreadProgress, 8));
            driftYv = e(driftY, 4);
            // Rounded to a whole STEP (not scaled up first) so a smoothly-animated Curve only jumps the pattern at
            // each integer crossing instead of reshuffling into unrelated noise on every tiny fractional change.
            seedStep = Mathf.RoundToInt(e(seedOffset, 5));
            width = Mathf.Clamp(e(crackWidth, 6), 0.01f, 3f);
            cellShade = Mathf.Clamp01(e(cellShadeStrength, 7));
            sharpness = Mathf.Max(0.05f, e(seamSharpness, 9));
            // fid 10 extends past the nominal 8-wide per-modifier block (this modifier already occupies 0-9) —
            // benign: the fid only seeds a param's Min-Max RNG stream.
            spreadSoft = Mathf.Clamp01(e(SpreadSoftness, 10));
        }

        /// The blast's own Origin marker (Pyre.origin), in canvas pixels — set once per frame by BlastRenderer
        /// (mirrors PinWarpModifier.SetFrame) so Rotation can pivot on the same point the preview's ✛ handle shows,
        /// instead of the canvas corner.
        internal void SetOrigin(Vector2 originPixels) => originPx = originPixels;

        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            if (amt <= 0.001f && !tintCells) return true;

            int hash = seedStep != 0 ? unchecked(p.hash + seedStep * 92821) : p.hash;

            // Rotate about the blast's Origin marker (not the canvas corner), then drift along the — now possibly
            // rotated — grid axes, matching Noise fill's own rotate-then-drift order.
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
                    float jx = Sfx.Hash01(hash, gx * 2, gy * 2);
                    float jy = Sfx.Hash01(hash, gx * 2 + 1, gy * 2 + 1);
                    float fx = gx + jx, fy = gy + jy;
                    float d = (fx - x) * (fx - x) + (fy - y) * (fy - y);
                    if (d < f1) { f2 = f1; f1 = d; bestCx = gx; bestCy = gy; }
                    else if (d < f2) f2 = d;
                }
            f1 = Mathf.Sqrt(f1); f2 = Mathf.Sqrt(f2);

            if (tintCells && cellShade > 0.001f)
            {
                float shade = Sfx.Hash01(unchecked(hash ^ 0x37A19E13), bestCx, bestCy);
                float k = 1f + (shade - 0.5f) * 2f * cellShade;
                c = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
            }

            if (amt > 0.001f)
            {
                float gap = f2 - f1;
                float k = 1f - Mathf.Clamp01(gap / width);   // 1 at the seam, 0 away from it — a plain LINEAR
                // fade across the whole Crack width band. Raising it to Seam sharpness's power reshapes that
                // same 1→0 span into a harder step (sharpness > 1 — a clean seam with no soft bleed into the
                // cell interior) or a softer one (sharpness < 1), without changing Crack width's own footprint
                // (still 1 exactly at the seam, still 0 exactly at width's own edge, either way).
                if (Mathf.Abs(sharpness - 1f) > 0.001f) k = Mathf.Pow(k, sharpness);
                if (spreadMode != CrackSpreadMode.Uniform) k *= SpreadMask(p.crossFrac);
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

        // 0 at the reveal front (the fresh, just-lit edge) → 1 deep inside the already-revealed zone, 0 deep
        // inside the not-yet-revealed zone — a smoothstepped gate, same shape as AlphaMaskModifier's own edge.
        float SpreadMask(float crossFrac)
        {
            float w = Mathf.Max(0.001f, spreadSoft);
            switch (spreadMode)
            {
                case CrackSpreadMode.CenterOut:
                    return Smooth01((spreadProg - crossFrac) / w + 0.5f);
                case CrackSpreadMode.EdgeIn:
                    return Smooth01((crossFrac - (1f - spreadProg)) / w + 0.5f);
                case CrackSpreadMode.Both:
                {
                    // Each wavefront only covers HALF of spreadProg's range, so they meet exactly at the
                    // midpoint (crossFrac 0.5) when spreadProg reaches 1 — not immediately at spreadProg 0.5,
                    // which is what using the full range for both simultaneously would do.
                    float half = spreadProg * 0.5f;
                    float outMask = Smooth01((half - crossFrac) / w + 0.5f);
                    float inMask = Smooth01((crossFrac - (1f - half)) / w + 0.5f);
                    return Mathf.Max(outMask, inMask);
                }
                default: return 1f;
            }
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static ZUIValue DefaultSpreadProgress() => Sfx.CurveVal(1f, 0f, 0f, 1f, 1f);

        static Gradient Black()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }

    /// A PostModifier (not a per-pixel PixelModifier) specifically so `smoothness` can see real NEIGHBOUR
    /// pixels — a per-pixel effect has no way to know whether the pixel next door is also being dissolved.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class DissolveModifier : PostModifier
    {
        [Range(0f, 1f)]
        [Tooltip("0 = nothing removed, 1 = everything gone. Animatable — the classic 'crumble away at the end' is " +
                 "this ramping 0→1 over the layer's life.")]
        public ZUIValue amount = DefaultAmount();
        [Tooltip("Erase = stable random holes; Scatter = holes that reshuffle every frame (a boiling churn).")]
        public DissolveMode mode = DissolveMode.Erase;
        [Tooltip("0 = hard-edged holes (unchanged from before). Higher softens two ways: a pixel that just " +
                 "crossed the cut doesn't vanish outright — it fades out over several SUBSEQUENT frames as " +
                 "Amount keeps rising past its own threshold; and a still-solid pixel next to an already-hollowed " +
                 "one bleeds some of its own alpha toward it, so growing holes spread/soften into their neighbours " +
                 "instead of popping in as hard single-pixel speckle.")]
        [Range(0f, 1f)] public ZUIValue smoothness = new ZUIValue(0f);

        float amt, smooth;
        public override string DisplayName => "Dissolve";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amt = Mathf.Clamp01(e(amount, 0));
            smooth = Mathf.Clamp01(e(smoothness, 1));
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (amt <= 0.001f) return;
            int n = W * H;
            var keep = new float[n];   // fraction of this pixel's own alpha that survives, before neighbour bleed
            bool hard = smooth <= 0.0001f;
            float fadeSpan = Mathf.Lerp(0.02f, 0.6f, smooth);

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    float h = mode == DissolveMode.Scatter
                        ? Sfx.Hash01(unchecked(seed ^ (frame * 92821)), x, y)
                        : Sfx.Hash01(seed, x, y);
                    if (h >= amt) { keep[idx] = 1f; continue; }
                    if (hard) { keep[idx] = 0f; continue; }
                    float pastCut = amt - h;   // 0 right at the moment it crosses; grows as Amount keeps rising
                    keep[idx] = Mathf.Clamp01(1f - pastCut / fadeSpan);
                }

            if (!hard)
            {
                var bled = new float[n];
                float bleed = smooth * 0.5f;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int idx = y * W + x;
                        float minNeighbor = keep[idx];
                        if (x > 0) minNeighbor = Mathf.Min(minNeighbor, keep[idx - 1]);
                        if (x < W - 1) minNeighbor = Mathf.Min(minNeighbor, keep[idx + 1]);
                        if (y > 0) minNeighbor = Mathf.Min(minNeighbor, keep[idx - W]);
                        if (y < H - 1) minNeighbor = Mathf.Min(minNeighbor, keep[idx + W]);
                        bled[idx] = Mathf.Lerp(keep[idx], minNeighbor, bleed);
                    }
                keep = bled;
            }

            for (int i = 0; i < n; i++)
            {
                if (keep[i] >= 0.999f) continue;
                var c = buf[i];
                if (c.a == 0) continue;
                float newA = c.a * (1f / 255f) * keep[i];
                buf[i] = newA <= 0.003f ? default : new Color32(c.r, c.g, c.b, ToByte(newA));
            }
        }

        static ZUIValue DefaultAmount() => Sfx.CurveVal(1f, 0f, 0f, 0.6f, 0f, 1f, 1f);
    }

    /// Dissolve's shape-local sibling — for use in a LAYER's own modifier list (not the global list), where it
    /// can follow that SAME layer's own GeometryModifier stack (Sphere/Ground/Jagg/Wobble/...). Hashing on
    /// `p.wx`/`p.wy` (the geometry-WARPED position every PixelModifier already receives) instead of Dissolve's
    /// fixed screen (x, y) means a Sphere's fisheye bulge, for instance, genuinely drags the erase-dot pattern
    /// along with it, rather than the dots staying rigid in screen space while the silhouette distorts
    /// underneath them (reported: "having a Sphere after a Dissolve, I would expect the Erase dots to be
    /// affected by the sphere distortion"). The trade-off: as a PixelModifier (one pixel at a time, no
    /// neighbour access), Smoothness here can only do the self-fade half of Dissolve's own Smoothness (a pixel
    /// fades out over subsequent frames as Amount keeps rising past its own threshold) — the neighbour-bleed
    /// half needs whole-frame buffer access, exactly what Dissolve's own PostModifier conversion bought it and
    /// this modifier gives up in exchange for geometry-awareness. Global modifiers keep plain Dissolve (screen-
    /// space, full Smoothness) since there's no single shape/geometry stack to be "local" to across a whole
    /// composited frame; this one is offered only in a layer's own Add-modifier menu.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class LayerDissolveModifier : PixelModifier
    {
        [Range(0f, 1f)]
        [Tooltip("0 = nothing removed, 1 = everything gone. Animatable — the classic 'crumble away at the end' is " +
                 "this ramping 0→1 over the layer's life.")]
        public ZUIValue amount = Sfx.CurveVal(1f, 0f, 0f, 0.6f, 0f, 1f, 1f);
        [Tooltip("Erase = stable random holes; Scatter = holes that reshuffle every frame (a boiling churn).")]
        public DissolveMode mode = DissolveMode.Erase;
        [Tooltip("0 = hard-edged holes (unchanged from before). Higher makes a pixel that just crossed the cut " +
                 "fade out over several SUBSEQUENT frames as Amount keeps rising past its own threshold, instead " +
                 "of vanishing outright. (No neighbour-bleed half like Dissolve's own Smoothness — this is a " +
                 "per-pixel modifier with no access to neighbouring pixels.)")]
        [Range(0f, 1f)] public ZUIValue smoothness = new ZUIValue(0f);

        float amt, smooth;
        public override string DisplayName => "Layer dissolve";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amt = Mathf.Clamp01(e(amount, 0));
            smooth = Mathf.Clamp01(e(smoothness, 1));
        }

        DissolveP P() => new DissolveP { amt = amt, smooth = smooth, mode = (int)mode };
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
            => SfxKernels.KLayerDissolve(P(), ref c, ref a, p);
        public override SfxOp ResolveSfxOp() => new SfxOp { kind = SfxKernel.LayerDissolve, dissolve = P(), lutIndex = -1 };
    }

    /// A moving transparency mask: sweeps a soft-edged shape across the layer, multiplying alpha. A disc that
    /// reveals from the centre out or eats inward from the edges, or a horizontal / vertical wipe (scene-transition
    /// style). Animate `progress` (0→1) to drive the sweep; sharpness sets the edge hardness; size scales it;
    /// rotation + offset place it.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class AlphaMaskModifier : PixelModifier
    {
        [Tooltip("The form the mask sweeps: a disc opening outward or closing inward, a horizontal or vertical " +
                 "scene-transition swipe, a pac-man wedge eaten by angle, an irregular noise cloud, or a " +
                 "triangle / square / crescent growing from the centre.")]
        public MaskShape shape = MaskShape.DiscOut;
        [Range(0f, 1f)]
        [Tooltip("0→1 sweep position. A rising envelope reveals the layer; a falling one hides it. Animatable.")]
        public ZUIValue progress = DefaultProgress();
        [HideInInspector] public float sharpness = 0.6f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0f, 1f)]
        [Tooltip("Edge hardness: 1 = a crisp cut, 0 = a wide soft gradient.")]
        public ZUIValue sharpnessValue = new ZUIValue(0.6f);
        [HideInInspector] public bool sharpnessUpgraded;
        public ZUIValue Sharpness { get { if (!sharpnessUpgraded) { sharpnessValue = new ZUIValue(sharpness); sharpnessUpgraded = true; } return sharpnessValue; } }
        [Range(0.1f, 4f)]
        [Tooltip("Mask scale. 1 = spans the half-canvas. Animatable.")]
        public ZUIValue size = new ZUIValue(1f);
        [Range(-180f, 180f)]
        [Tooltip("Mask rotation in degrees (rotates the wipe direction / disc axis). Animatable.")]
        public ZUIValue rotation = new ZUIValue(0f);
        [HideInInspector] public float offsetX = 0f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [ZUIPair2D("offsetYValue", "Centre offset")]
        [Tooltip("Where the mask's centre sits, in half-canvas units: (-1,-1) is the bottom-left corner, " +
                 "(+1,+1) the top-right, (0,0) the middle.")]
        [Range(-1f, 1f)] public ZUIValue offsetXValue = new ZUIValue(0f);
        [HideInInspector] public bool offsetXUpgraded;
        public ZUIValue OffsetX { get { if (!offsetXUpgraded) { offsetXValue = new ZUIValue(offsetX); offsetXUpgraded = true; } return offsetXValue; } }
        [HideInInspector] public float offsetY = 0f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Tooltip("Mask centre offset Y, in half-canvas units (-1..1).")]
        [Range(-1f, 1f)] public ZUIValue offsetYValue = new ZUIValue(0f);
        [HideInInspector] public bool offsetYUpgraded;
        public ZUIValue OffsetY { get { if (!offsetYUpgraded) { offsetYValue = new ZUIValue(offsetY); offsetYUpgraded = true; } return offsetYValue; } }

        [HideInInspector] public float noiseWarp = 0.6f;
        [HideInInspector] public bool noiseWarpUpgraded;
        [ZUIShowIf("shape", "Noise")]
        [Tooltip("Domain-warp strength — how much the noise field bends on itself. 0 = plain smooth noise " +
                 "(a blobby cloud); higher = more churned, organic eddies. Animatable — ramp the churn up " +
                 "as the mask closes.")]
        [Range(0f, 2f)] public ZUIValue noiseWarpValue = new ZUIValue(0.6f);
        public ZUIValue NoiseWarp { get { if (!noiseWarpUpgraded) { noiseWarpValue = new ZUIValue(noiseWarp); noiseWarpUpgraded = true; } return noiseWarpValue; } }
        [ZUIShowIf("shape", "Noise")]
        [ZUIPair2D("noiseDriftY", "Noise drift")]
        [Range(-64f, 64f)]
        [Tooltip("Extra drift added to the noise sample position over the mask's progress, in half-canvas " +
                 "units. Animate it for a cloud that visibly rolls or billows as it reveals.")]
        public ZUIValue noiseDriftX = new ZUIValue(0f);
        [Range(-64f, 64f)]
        [Tooltip("Extra Y drift added to the noise sample position, in half-canvas units. Animatable.")]
        public ZUIValue noiseDriftY = new ZUIValue(0f);

        [HideInInspector] public float crescentBite = 0.9f;
        [HideInInspector] public bool crescentBiteUpgraded;
        [ZUIShowIf("shape", "Crescent")]
        [Tooltip("How far the bitten-out disc sits from the centre. Low = the bite swallows almost " +
                 "everything (a thin sliver); high = it barely clips the edge (an almost-full moon). " +
                 "Animatable.")]
        [Range(0f, 2f)] public ZUIValue crescentBiteValue = new ZUIValue(0.9f);
        public ZUIValue CrescentBite { get { if (!crescentBiteUpgraded) { crescentBiteValue = new ZUIValue(crescentBite); crescentBiteUpgraded = true; } return crescentBiteValue; } }

        [HideInInspector] public float crescentThickness = 1f;
        [HideInInspector] public bool crescentThicknessUpgraded;
        [ZUIShowIf("shape", "Crescent")]
        [Tooltip("The bitten-out disc's own radius. Bigger takes a deeper bite, leaving a thinner, more " +
                 "curved sliver. Animatable.")]
        [Range(0.1f, 2f)] public ZUIValue crescentThicknessValue = new ZUIValue(1f);
        public ZUIValue CrescentThickness { get { if (!crescentThicknessUpgraded) { crescentThicknessValue = new ZUIValue(crescentThickness); crescentThicknessUpgraded = true; } return crescentThicknessValue; } }

        float prog, siz, rotRad, driftX, driftY, offX, offY, sharp, warpV, biteV, thickV;
        public override string DisplayName => "Alpha mask";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            prog = Mathf.Clamp01(e(progress, 0));
            siz = Mathf.Max(0.01f, e(size, 1));
            rotRad = e(rotation, 2) * Mathf.Deg2Rad;
            driftX = e(noiseDriftX, 3);
            driftY = e(noiseDriftY, 4);
            offX = Mathf.Clamp(e(OffsetX, 5), -1f, 1f);
            offY = Mathf.Clamp(e(OffsetY, 6), -1f, 1f);
            sharp = Mathf.Clamp01(e(Sharpness, 7));
            warpV = e(NoiseWarp, 8);
            biteV = e(CrescentBite, 9);
            thickV = e(CrescentThickness, 10);
        }

        MaskP P() => new MaskP
        {
            shape = (int)shape, prog = prog, siz = siz, rotRad = rotRad, driftX = driftX, driftY = driftY,
            sharpness = sharp, offsetX = offX, offsetY = offY, noiseWarp = warpV,
            strength = 1f, fadeMode = SfxKernels.FadeEdge, fadeAngleRad = 0f,
            biteX = biteV, biteR = thickV
        };
        public override bool ApplyPixel(ref Color col, ref float a, in PixelInfo p)
            => SfxKernels.KAlphaMask(P(), ref col, ref a, p);
        public override SfxOp ResolveSfxOp() => new SfxOp { kind = SfxKernel.AlphaMask, mask = P(), lutIndex = -1 };

        static ZUIValue DefaultProgress() => Sfx.CurveVal(1f, 0f, 0f, 1f, 1f);   // reveal over life
    }

    /// The shape a Wipe reveals through. Its own enum rather than the whole <see cref="MaskShape"/> set, because
    /// a wipe is authored as "reveal through a form" — the swipes and the pac-man wedge belong to Alpha mask's
    /// scene-transition vocabulary, not this one.
    public enum WipeShape { Triangle, Square, Disc, Crescent }

    /// How a Wipe's boundary reads.
    public enum WipeEdge
    {
        Soft,        // the shape's own edge fades outward, evenly all the way round
        Solid,       // a hard cut exactly on the boundary — no gradient at all
        Directional  // a hard boundary, but the reveal itself fades along an authored angle across the shape
    }

    /// A shape REVEAL: a growing masked form that uncovers the sprite, either hard-edged or faded — faded evenly
    /// outward, or along any direction you choose. Everything that places or sizes it animates, and so does how
    /// strongly it masks, so a wipe can be brought in and taken back out without its edge popping.
    ///
    /// Deliberately the SAME machinery as <see cref="AlphaMaskModifier"/> — it packs into the same blittable
    /// <see cref="MaskP"/> and runs the same <see cref="SfxKernels.KAlphaMask"/> kernel — so the two never drift
    /// apart and the new shapes are available to both. What differs is the authoring surface, not the maths.
    [Serializable]
    public class WipeModifier : PixelModifier
    {
        [Tooltip("The form the reveal grows as: an apex-up triangle, an edge-on square, a disc, or a moon. " +
                 "Rotation turns it from there.")]
        public WipeShape shape = WipeShape.Disc;

        [Tooltip("The sweep: 0 hides everything, 1 reveals the whole sprite. Animatable — a rising envelope " +
                 "wipes in, a falling one wipes back out.")]
        [Range(0f, 1f)] public ZUIValue progress = Sfx.CurveVal(1f, 0f, 0f, 1f, 1f);

        [Tooltip("How large the shape is at full sweep, in half-canvas units — 1 just spans the sprite's shorter " +
                 "half. Animatable.")]
        [Range(0.05f, 4f)] public ZUIValue size = new ZUIValue(1f);

        [ZUIPair2D("offsetY", "Offset")]
        [Tooltip("Where the shape's centre sits, in half-canvas units: (-1,-1) is the bottom-left corner, " +
                 "(+1,+1) the top-right, (0,0) the middle. Animatable — slide the reveal across the sprite.")]
        [Range(-1.5f, 1.5f)] public ZUIValue offsetX = new ZUIValue(0f);

        [Tooltip("Where the shape's centre sits vertically, in half-canvas units: -1 is the bottom edge, " +
                 "+1 the top. Animatable.")]
        [Range(-1.5f, 1.5f)] public ZUIValue offsetY = new ZUIValue(0f);

        [Tooltip("Turns the shape, in degrees. Animatable — spin a triangle or a crescent as it opens.")]
        [Range(-180f, 180f)] public ZUIValue rotation = new ZUIValue(0f);

        [Tooltip("How much of the sprite the mask actually takes away: 1 removes it outright, 0.5 only halves it, " +
                 "0 leaves the sprite whole. Animatable — ease it up and down so the wipe's edge never pops in.")]
        [Range(0f, 1f)] public ZUIValue strength = new ZUIValue(1f);

        [Tooltip("Soft = the boundary blurs outward evenly; Solid = a hard cut with no gradient; Directional = a " +
                 "hard boundary with the reveal itself fading along the angle below.")]
        public WipeEdge edge = WipeEdge.Soft;

        // Frozen legacy source — never renamed/retyped, so an authored asset keeps its value; the ZUIValue
        // companion below is what the editor shows and what Prepare reads. Same upgrade-on-first-use pattern
        // as AlphaMask's sharpness.
        [HideInInspector] public float feather = 0.4f;
        [HideInInspector] public bool featherUpgraded;
        [ZUIShowIf("edge", "Soft")]
        [Tooltip("How wide the blur is. 0 is nearly a hard cut, 1 a very wide gradient. Animatable — harden " +
                 "the edge as the wipe lands.")]
        [Range(0f, 1f)] public ZUIValue featherValue = new ZUIValue(0.4f);
        public ZUIValue Feather { get { if (!featherUpgraded) { featherValue = new ZUIValue(feather); featherUpgraded = true; } return featherValue; } }

        [ZUIShowIf("edge", "Directional")]
        [Tooltip("The compass direction the fade runs along, in degrees. 0 fades away to the right, 90 " +
                 "upward. Animatable — sweep the fade around while the shape holds still.")]
        [Range(0f, 360f)] public ZUIValue fadeAngle = new ZUIValue(0f);

        [HideInInspector] public float crescentBite = 0.9f;
        [HideInInspector] public bool crescentBiteUpgraded;
        [ZUIShowIf("shape", "Crescent")]
        [Tooltip("How far the bitten-out disc sits from the centre. Low = the bite swallows almost " +
                 "everything (a thin sliver); high = it barely clips the edge (an almost-full moon). " +
                 "Animatable — open the moon as the wipe runs.")]
        [Range(0f, 2f)] public ZUIValue crescentBiteValue = new ZUIValue(0.9f);
        public ZUIValue CrescentBite { get { if (!crescentBiteUpgraded) { crescentBiteValue = new ZUIValue(crescentBite); crescentBiteUpgraded = true; } return crescentBiteValue; } }

        [HideInInspector] public float crescentThickness = 1f;
        [HideInInspector] public bool crescentThicknessUpgraded;
        [ZUIShowIf("shape", "Crescent")]
        [Tooltip("The bitten-out disc's own radius. Bigger takes a deeper bite, leaving a thinner, more " +
                 "curved sliver. Animatable.")]
        [Range(0.1f, 2f)] public ZUIValue crescentThicknessValue = new ZUIValue(1f);
        public ZUIValue CrescentThickness { get { if (!crescentThicknessUpgraded) { crescentThicknessValue = new ZUIValue(crescentThickness); crescentThicknessUpgraded = true; } return crescentThicknessValue; } }

        float prog, siz, rotRad, offX, offY, strengthV, fadeRad, featherV, biteV, thickV;
        public override string DisplayName => "Wipe";

        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            prog = Mathf.Clamp01(e(progress, 0));
            siz = Mathf.Max(0.01f, e(size, 1));
            rotRad = e(rotation, 2) * Mathf.Deg2Rad;
            offX = e(offsetX, 3);
            offY = e(offsetY, 4);
            strengthV = Mathf.Clamp01(e(strength, 5));
            fadeRad = e(fadeAngle, 6) * Mathf.Deg2Rad;
            featherV = Mathf.Clamp01(e(Feather, 7));
            biteV = e(CrescentBite, 8);
            thickV = e(CrescentThickness, 9);
        }

        static int ShapeId(WipeShape s)
        {
            switch (s)
            {
                case WipeShape.Triangle: return SfxKernels.MaskTriangle;
                case WipeShape.Square: return SfxKernels.MaskSquare;
                case WipeShape.Crescent: return SfxKernels.MaskCrescent;
                default: return SfxKernels.MaskDiscOut;
            }
        }

        static int FadeId(WipeEdge e)
        {
            switch (e)
            {
                case WipeEdge.Solid: return SfxKernels.FadeSolid;
                case WipeEdge.Directional: return SfxKernels.FadeDirectional;
                default: return SfxKernels.FadeEdge;
            }
        }

        MaskP P() => new MaskP
        {
            shape = ShapeId(shape),
            prog = prog, siz = siz, rotRad = rotRad,
            sharpness = 1f - featherV,   // the shared kernel speaks in edge HARDNESS
            offsetX = offX, offsetY = offY,
            strength = strengthV,
            fadeMode = FadeId(edge), fadeAngleRad = fadeRad,
            biteX = biteV, biteR = thickV
        };

        public override bool ApplyPixel(ref Color col, ref float a, in PixelInfo p)
            => SfxKernels.KAlphaMask(P(), ref col, ref a, p);
        public override SfxOp ResolveSfxOp() => new SfxOp { kind = SfxKernel.AlphaMask, mask = P(), lutIndex = -1 };
    }

    /// Wash every pixel TOWARD a colour. The plain Tint effect can only MULTIPLY, which can subtract colour but
    /// never add it — a red flash multiplied onto a green enemy turns it black, and onto an already-red one is
    /// invisible. This is the verb that actually paints: at amount 1 every pixel IS the colour, at 0 nothing
    /// changes, and everything between is the wash. Alpha is left alone, so a silhouette keeps its shape.
    [Serializable]
    public class ColorTintModifier : PixelModifier
    {
        // Frozen legacy colour — never renamed or retyped, so an authored asset keeps the colour it had. The
        // gradient below is what the editor shows and what Prepare reads.
        [HideInInspector] public Color color = Color.white;
        [HideInInspector] public bool colorUpgraded;

        [Tooltip("The colour the sprite is washed toward, ACROSS the play-through — read left to right, so the " +
                 "left end is the colour at the start and the right end the colour at the end. Unlike the " +
                 "multiply Tint effect this can brighten, and can push a pixel to a hue it does not already " +
                 "contain. A single flat colour still works: leave both ends the same.")]
        public ZuiGradient colorOverLife = new ZuiGradient();

        /// The gradient, seeded once from the frozen flat colour so an asset authored before this reads as
        /// the constant it always was rather than jumping to a default ramp.
        public ZuiGradient ColorOverLife
        {
            get
            {
                if (!colorUpgraded)
                {
                    colorOverLife = new ZuiGradient();
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                              new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a, 1f) });
                    colorOverLife.gradient = g;
                    colorUpgraded = true;
                }
                return colorOverLife;
            }
        }

        [Tooltip("How far each pixel travels toward the colour: 0 leaves it untouched, 1 replaces it outright. " +
                 "Animatable — spike it for a hit flash, then ramp it back down to bleed the glow out.")]
        [Range(0f, 1f)] public ZUIValue amount = new ZUIValue(1f);

        float amt;
        Color tint = Color.white;
        public override string DisplayName => "Colour tint";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amt = Mathf.Clamp01(e(amount, 0));
            // Sampled at the stack's own life, so the wash TRAVELS through the ramp as the event plays —
            // white-hot into red into black over a hit, rather than one colour held throughout.
            tint = ColorOverLife.Evaluate(ColorReplaceModifier.LifeOfEval(e));
        }

        ColorTintP P() => new ColorTintP { r = tint.r, g = tint.g, b = tint.b, amt = amt };
        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
            => SfxKernels.KColorTint(P(), ref c, ref a);
        public override SfxOp ResolveSfxOp() => new SfxOp { kind = SfxKernel.ColorTint, ctint = P(), lutIndex = -1 };
    }

    /// One band of the hue wheel and what to turn it into. Several of these live in a single
    /// <see cref="ColorReplaceModifier"/>, applied in list order, so a whole palette can be re-skinned in one
    /// effect — swap the armour to red AND the cloak to gold without stacking two effects.
    [Serializable]
    public class HueReplacement
    {
        // Every dial here animates. A recolour that can only hold still is a palette swap; one that can move
        // is a character turning to stone, a power-up washing through, a hue drifting as a shield charges —
        // and that is the whole point of a stack whose values are shapes over the event's life.
        //
        // Each keeps a frozen legacy float under its old name so authored assets load unchanged, with the
        // ZUIValue companion beside it and an upgrade-on-first-use property, exactly as Alpha mask's
        // sharpness has always done.
        [HideInInspector] public float targetHue = 0f;
        [HideInInspector] public bool targetHueUpgraded;
        [ZUIHue]
        [Tooltip("The hue this replacement looks for, in degrees around the colour wheel (0 red, 120 green, " +
                 "240 blue). Pick it off the swatch rather than guessing the number. Animatable — sweep the " +
                 "band around the wheel to catch different parts of the sprite over the event.")]
        [Range(0f, 360f)] public ZUIValue targetHueValue = new ZUIValue(0f);
        public ZUIValue TargetHue { get { if (!targetHueUpgraded) { targetHueValue = new ZUIValue(targetHue); targetHueUpgraded = true; } return targetHueValue; } }

        [HideInInspector] public float targetRange = 30f;
        [HideInInspector] public bool targetRangeUpgraded;
        [Tooltip("How far either side of that hue still counts as a match, in degrees. Small only catches an " +
                 "exact shade; 180 catches every colour there is. Animatable — widen the band to swallow the " +
                 "whole sprite.")]
        [Range(1f, 180f)] public ZUIValue targetRangeValue = new ZUIValue(30f);
        public ZUIValue TargetRange { get { if (!targetRangeUpgraded) { targetRangeValue = new ZUIValue(targetRange); targetRangeUpgraded = true; } return targetRangeValue; } }

        [HideInInspector] public float targetSmoothing = 0.5f;
        [HideInInspector] public bool targetSmoothingUpgraded;
        [Tooltip("How softly the match dies out at the band's rim: 0 stops dead at the edge (a visible seam " +
                 "where neighbouring hues are untouched), 1 fades the whole band so it blends into what is " +
                 "around it. Animatable.")]
        [Range(0f, 1f)] public ZUIValue targetSmoothingValue = new ZUIValue(0.5f);
        public ZUIValue TargetSmoothing { get { if (!targetSmoothingUpgraded) { targetSmoothingValue = new ZUIValue(targetSmoothing); targetSmoothingUpgraded = true; } return targetSmoothingValue; } }

        [Tooltip("Scales this whole replacement: 0 disables it, 1 applies it fully, and anything between blends " +
                 "the new colour with the original. Animatable — fade a recolour in over the effect's life.")]
        [Range(0f, 1f)] public ZUIValue amount = new ZUIValue(1f);

        [HideInInspector] public float replacementHue = 200f;
        [HideInInspector] public bool replacementHueUpgraded;
        [ZUIHue]
        [Tooltip("The hue matched pixels are moved to, in degrees around the colour wheel. Pick the colour " +
                 "you want them to become. Animatable — cycle it for a shifting, iridescent recolour.")]
        [Range(0f, 360f)] public ZUIValue replacementHueValue = new ZUIValue(200f);
        public ZUIValue ReplacementHue { get { if (!replacementHueUpgraded) { replacementHueValue = new ZUIValue(replacementHue); replacementHueUpgraded = true; } return replacementHueValue; } }

        [HideInInspector] public float replacementSmoothing = 1f;
        [HideInInspector] public bool replacementSmoothingUpgraded;
        [Tooltip("How much of the source's own hue variation survives: 0 flattens the whole band onto one flat " +
                 "hue, 1 keeps every pixel's offset so shading and highlights read as before. Animatable — " +
                 "collapse the shading to flat colour as something petrifies.")]
        [Range(0f, 1f)] public ZUIValue replacementSmoothingValue = new ZUIValue(1f);
        public ZUIValue ReplacementSmoothing { get { if (!replacementSmoothingUpgraded) { replacementSmoothingValue = new ZUIValue(replacementSmoothing); replacementSmoothingUpgraded = true; } return replacementSmoothingValue; } }

        [HideInInspector] public float brightness = 1f;
        [HideInInspector] public bool brightnessUpgraded;
        [Tooltip("Multiplies the brightness of matched pixels. 1 leaves it alone, below darkens, above lifts. " +
                 "Animatable — flare the matched colour up and back down on a hit.")]
        [Range(0f, 2f)] public ZUIValue brightnessValue = new ZUIValue(1f);
        public ZUIValue Brightness { get { if (!brightnessUpgraded) { brightnessValue = new ZUIValue(brightness); brightnessUpgraded = true; } return brightnessValue; } }

        [HideInInspector] public float saturation = 1f;
        [HideInInspector] public bool saturationUpgraded;
        [Tooltip("Multiplies the colourfulness of matched pixels. 1 leaves it alone, 0 makes them grey, above " +
                 "pushes them more vivid. Animatable — drain to grey as a character dies.")]
        [Range(0f, 2f)] public ZUIValue saturationValue = new ZUIValue(1f);
        public ZUIValue Saturation { get { if (!saturationUpgraded) { saturationValue = new ZUIValue(saturation); saturationUpgraded = true; } return saturationValue; } }

        [Tooltip("AUTHORING AID: pulse the pixels this replacement actually catches — through white, then " +
                 "through black, over and over — so the area it affects is unmistakable. A soft band rim " +
                 "pulses softly too, which is the part that is otherwise impossible to judge by eye. Turn it " +
                 "off before shipping: it changes what the effect draws.")]
        public bool highlight = false;

        [ZUIShowIf("highlight", "True")]
        [Tooltip("How strongly the pulse overrides the real colours. 1 drives matched pixels all the way to " +
                 "white and black; lower keeps more of the actual result visible underneath.")]
        [Range(0f, 1f)] public float highlightAmount = 1f;

        [ZUIShowIf("highlight", "True")]
        [Tooltip("How many full white-then-black pulses run across one play-through.")]
        [Range(0.25f, 8f)] public float highlightSpeed = 2f;

        // This frame's resolved values, filled by ColorReplaceModifier.Prepare.
        [System.NonSerialized] public float resolvedHlMix, resolvedHlTarget;
        [System.NonSerialized] public float resolvedAmount, resolvedTargetHue, resolvedTargetRange,
                                          resolvedTargetSmoothing, resolvedReplacementHue,
                                          resolvedReplacementSmoothing, resolvedBrightness, resolvedSaturation;
    }

    /// Re-skin a sprite by HUE: match one or more bands of the colour wheel and rewrite each to a new hue,
    /// brightness and saturation. Several replacements live in one effect and run in order, so a full palette
    /// swap is a single card rather than a stack of them. It keys on colour, never on position, so it follows an
    /// animated reel with no per-frame masks.
    [Serializable]
    public class ColorReplaceModifier : PixelModifier
    {
        [Tooltip("The hue bands this effect rewrites, applied in order. Add one per colour you want to swap.")]
        public List<HueReplacement> replacements = new List<HueReplacement> { new HueReplacement() };

        public override string DisplayName => "Colour replace";

        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            if (replacements == null) return;
            for (int i = 0; i < replacements.Count; i++)
            {
                var r = replacements[i];
                if (r == null) continue;
                // A distinct salt PER ENTRY AND PER FIELD, so two replacements' Min-Max randomness stays
                // independent and so does each dial's within one — sharing a salt would make a band's hue and
                // its width roll the same number.
                int s = i * 16;
                r.resolvedAmount = Mathf.Clamp01(e(r.amount, s));
                r.resolvedTargetHue = e(r.TargetHue, s + 1);
                r.resolvedTargetRange = Mathf.Max(0.01f, e(r.TargetRange, s + 2));
                r.resolvedTargetSmoothing = Mathf.Clamp01(e(r.TargetSmoothing, s + 3));
                r.resolvedReplacementHue = e(r.ReplacementHue, s + 4);
                r.resolvedReplacementSmoothing = Mathf.Clamp01(e(r.ReplacementSmoothing, s + 5));
                r.resolvedBrightness = e(r.Brightness, s + 6);
                r.resolvedSaturation = e(r.Saturation, s + 7);
                ResolveHighlight(r, LifeOf(e));
            }
        }

        /// The stack's current life, recovered through the evaluator itself.
        ///
        /// Prepare is handed an evaluator, not a life — every parameter asks it for a value and never needs to
        /// know where on the timeline it is. The highlight does: its pulse is a function of time, not of any
        /// authored dial. A plain 0→1 ramp evaluated by that same evaluator returns exactly the life it was
        /// built with, which gets the number without widening the Prepare contract for one diagnostic.
        static readonly ZUIValue s_lifeProbe = Sfx.CurveVal(1f, 0f, 0f, 1f, 1f);
        static float LifeOf(Func<ZUIValue, int, float> e) => Mathf.Clamp01(e(s_lifeProbe, 0));

        /// The same trick, shared: any effect whose output depends on WHERE it is on the timeline rather than
        /// on an authored dial needs the life, and Prepare is only handed an evaluator.
        public static float LifeOfEval(Func<ZUIValue, int, float> e) => LifeOf(e);

        /// Where in the white → black cycle the highlight currently is. Four quarters: fade up to white, back
        /// down, up to black, back down. Resolved once per frame because it is the same for every pixel.
        static void ResolveHighlight(HueReplacement r, float life)
        {
            if (!r.highlight || r.highlightAmount <= 0f) { r.resolvedHlMix = 0f; return; }
            float t = Mathf.Repeat(life * Mathf.Max(0.01f, r.highlightSpeed), 1f);
            float mix;
            if (t < 0.25f) { mix = t / 0.25f; r.resolvedHlTarget = 1f; }
            else if (t < 0.5f) { mix = 1f - (t - 0.25f) / 0.25f; r.resolvedHlTarget = 1f; }
            else if (t < 0.75f) { mix = (t - 0.5f) / 0.25f; r.resolvedHlTarget = 0f; }
            else { mix = 1f - (t - 0.75f) / 0.25f; r.resolvedHlTarget = 0f; }
            r.resolvedHlMix = mix * Mathf.Clamp01(r.highlightAmount);
        }

        ReplaceP P(HueReplacement r) => new ReplaceP
        {
            hue = r.resolvedTargetHue,
            range = r.resolvedTargetRange,
            smooth = r.resolvedTargetSmoothing,
            amt = r.resolvedAmount,
            bri = r.resolvedBrightness,
            sat = r.resolvedSaturation,
            outHue = r.resolvedReplacementHue,
            spread = r.resolvedReplacementSmoothing,
            hlMix = r.resolvedHlMix,
            hlTarget = r.resolvedHlTarget,
        };

        public override bool ApplyPixel(ref Color c, ref float a, in PixelInfo p)
        {
            if (replacements == null) return true;
            for (int i = 0; i < replacements.Count; i++)
            {
                var r = replacements[i];
                if (r == null) continue;
                SfxKernels.KColorReplace(P(r), ref c, ref a);
            }
            return true;
        }

        public override int SfxOpCount => replacements != null ? replacements.Count : 0;
        public override SfxOp ResolveSfxOp(int index)
        {
            var r = replacements[index];
            return r == null
                ? new SfxOp { kind = SfxKernel.ColorReplace, replace = default, lutIndex = -1 }
                : new SfxOp { kind = SfxKernel.ColorReplace, replace = P(r), lutIndex = -1 };
        }

        // The base Clone only deep-copies ZUIValue / Gradient / point-list FIELDS, so without this every copy
        // would share one replacement list and editing the copy would edit the original.
        public override PyreModifier Clone()
        {
            var m = (ColorReplaceModifier)base.Clone();
            m.replacements = new List<HueReplacement>();
            if (replacements != null)
                foreach (var r in replacements)
                {
                    if (r == null) { m.replacements.Add(null); continue; }
                    // Clone the UPGRADE FLAGS with the values: copying the animatable companions while
                    // leaving the flags false would make the copy re-seed itself from the frozen legacy
                    // floats on first use and silently throw away every curve that was just copied.
                    m.replacements.Add(new HueReplacement
                    {
                        targetHue = r.targetHue,
                        targetHueUpgraded = r.targetHueUpgraded,
                        targetHueValue = Sfx.CloneVal(r.targetHueValue),
                        targetRange = r.targetRange,
                        targetRangeUpgraded = r.targetRangeUpgraded,
                        targetRangeValue = Sfx.CloneVal(r.targetRangeValue),
                        targetSmoothing = r.targetSmoothing,
                        targetSmoothingUpgraded = r.targetSmoothingUpgraded,
                        targetSmoothingValue = Sfx.CloneVal(r.targetSmoothingValue),
                        amount = Sfx.CloneVal(r.amount),
                        replacementHue = r.replacementHue,
                        replacementHueUpgraded = r.replacementHueUpgraded,
                        replacementHueValue = Sfx.CloneVal(r.replacementHueValue),
                        replacementSmoothing = r.replacementSmoothing,
                        replacementSmoothingUpgraded = r.replacementSmoothingUpgraded,
                        replacementSmoothingValue = Sfx.CloneVal(r.replacementSmoothingValue),
                        brightness = r.brightness,
                        brightnessUpgraded = r.brightnessUpgraded,
                        brightnessValue = Sfx.CloneVal(r.brightnessValue),
                        saturation = r.saturation,
                        saturationUpgraded = r.saturationUpgraded,
                        saturationValue = Sfx.CloneVal(r.saturationValue),
                        highlight = r.highlight,
                        highlightAmount = r.highlightAmount,
                        highlightSpeed = r.highlightSpeed,
                    });
                }
            return m;
        }
    }

    // ── post: whole-frame passes run AFTER compositing (neighbourhood effects a per-pixel modifier can't do) ──────
    /// A modifier that processes the finished frame buffer in place. Lives in the blast's GLOBAL modifier list and
    /// runs once per frame after every layer composites (in list order). This is how bloom/outline — which read a
    /// pixel's NEIGHBOURS — are possible at all, since geometry/pixel modifiers only see one pixel at a time.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
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

        /// This blast's seed and the raw frame index, set by BlastRenderer right before Prepare/Apply — same
        /// mirrors-PinWarpModifier's-SetFrame pattern as `life` above. Lets a Post modifier hash per-pixel
        /// deterministically (DissolveModifier's Erase/Scatter masks) without needing PixelInfo's hash, which
        /// only per-pixel Geometry/PixelModifiers get.
        protected int seed, frame;
        internal void SetSeed(int s) => seed = s;
        internal void SetFrameIndex(int f) => frame = f;
    }

    /// Bloom / glow: bright pixels bleed a soft halo outward (additive), and the halo lifts alpha so it glows into
    /// the transparent surround. Essential for energy weapons/blasts. `threshold` picks what's "bright", `radius` how
    /// far it spreads, `intensity` how strong (animatable — pulse the glow).
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class BloomModifier : PostModifier
    {
        [HideInInspector] public float threshold = 0.6f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0f, 1f)]
        [Tooltip("Brightness a pixel must exceed to bloom.")]
        public ZUIValue thresholdValue = new ZUIValue(0.6f);
        [HideInInspector] public bool thresholdUpgraded;
        public ZUIValue Threshold { get { if (!thresholdUpgraded) { thresholdValue = new ZUIValue(threshold); thresholdUpgraded = true; } return thresholdValue; } }
        [Range(0, 16)]
        [Tooltip("How far the glow spreads, in pixels.")]
        public int radius = 4;   // stays a plain int — a perf knob (blur cost scales with it), not a creative dial
        [Range(0f, 3f)]
        [Tooltip("Glow strength, added back additively. Animatable — pulse the glow.")]
        public ZUIValue intensity = new ZUIValue(1.2f);

        float inten, thr;
        public override string DisplayName => "Bloom (glow)";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            inten = Mathf.Max(0f, e(intensity, 0));
            thr = Mathf.Clamp01(e(Threshold, 1));
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (inten <= 0.001f || radius < 1) return;
            int n = W * H;
            var br = new float[n * 3];
            float denom = Mathf.Max(0.001f, 1f - thr);
            for (int i = 0; i < n; i++)
            {
                var c = buf[i];
                float a = c.a * (1f / 255f);
                float lum = (c.r + c.g + c.b) * (1f / (3f * 255f)) * a;
                float k = (lum - thr) / denom;
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class OutlineModifier : PostModifier
    {
        [Tooltip("Over life = one flat colour for the whole outline, sampled from the gradient at the blast's own " +
                 "life 0→1. Fill = the gradient is read across the outline's thickness (0 = inner edge, 1 = outer).")]
        public ColorMode mode = ColorMode.Fill;
        [Tooltip("Outline colour. Fill mode reads it across the outline's thickness (0 = inner edge, 1 = outer) — " +
                 "flat = a sharp one-colour outline, a gradient fades/recolours/bands outward. Over life mode " +
                 "samples the whole gradient once, at the blast's own life.")]
        public Gradient color = White();
        [Range(0f, 12f)]
        [Tooltip("Outline thickness in pixels, measured OUTWARD from the shape's edge — 0 is only meaningful " +
                 "together with Inner softness (a pure inward glow with no outward ring at all); otherwise " +
                 "this is the ring you actually see, so it wants to start at 1. Animatable — grow it outward.")]
        public ZUIValue size = new ZUIValue(1f);
        [HideInInspector] public float alphaThreshold = 0.08f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0.01f, 1f)]
        [Tooltip("Coverage threshold: the alpha level a pixel needs to count as \"shape\" rather than \"background\" " +
                 "when tracing the outline — it sets WHERE the outline sits on any soft/partial edge (outer " +
                 "softness, a gradient fill's own fade, a low-opacity fill, a Crescent bite). Low (the default 0.08) " +
                 "outlines even a faint, semi-transparent fill; raise it to trace further IN toward only the solid " +
                 "core (so a low-opacity fill gets no outline). Tip: to outline a PyrePlus 2D shape's silhouette, its " +
                 "own first-class shape Border is cleaner than this post-pass.")]
        public ZUIValue alphaThresholdValue = new ZUIValue(0.08f);
        [HideInInspector] public bool alphaThresholdUpgraded;
        public ZUIValue AlphaThreshold { get { if (!alphaThresholdUpgraded) { alphaThresholdValue = new ZUIValue(alphaThreshold); alphaThresholdUpgraded = true; } return alphaThresholdValue; } }

        // Inner and Outer below are the two EDGES of one single outline ring (where it meets the shape, and
        // where it meets the background) — not two separate outlines. Same knobs on both sides: a softness
        // (how many px the fade spans) and a curve (how that fade is shaped), applied symmetrically.
        [HideInInspector] public float innerSoftness = 0f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0f, 16f)]
        [Tooltip("Spills the outline INWARD, into the shape's own silhouette, over this many pixels — 0 = the " +
                 "outline stays entirely outside the shape (the default, crisp look). Higher values blend the " +
                 "outline colour over the shape's own pixels near the boundary, fading from full strength right " +
                 "at the edge down to the shape's own colour this many pixels deep — an inset glow, not a gap.")]
        public ZUIValue innerSoftnessValue = new ZUIValue(0f);
        [HideInInspector] public bool innerSoftnessUpgraded;
        public ZUIValue InnerSoftness { get { if (!innerSoftnessUpgraded) { innerSoftnessValue = new ZUIValue(innerSoftness); innerSoftnessUpgraded = true; } return innerSoftnessValue; } }
        [HideInInspector] public float innerSoftnessCurve = 1f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0.2f, 5f)]
        [Tooltip("Shapes the Inner softness falloff curve — 1 = linear (the default). Higher holds full " +
                 "strength longer near the boundary then drops off sharply right at the tail (reads as a " +
                 "tighter, more contained inset glow); lower drops off quickly then lingers faintly deeper in. " +
                 "Has no effect while Inner softness is 0.")]
        public ZUIValue innerSoftnessCurveValue = new ZUIValue(1f);
        [HideInInspector] public bool innerSoftnessCurveUpgraded;
        public ZUIValue InnerSoftnessCurve { get { if (!innerSoftnessCurveUpgraded) { innerSoftnessCurveValue = new ZUIValue(innerSoftnessCurve); innerSoftnessCurveUpgraded = true; } return innerSoftnessCurveValue; } }
        [HideInInspector] public float outerSoftness = 0f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0f, 16f)]
        [Tooltip("Fades the outline's OWN alpha near its OUTER edge (furthest from the shape) — 0 = a hard " +
                 "cutoff exactly at Size (the default), higher fades it out gradually, extending the visible " +
                 "falloff a bit PAST Size.")]
        public ZUIValue outerSoftnessValue = new ZUIValue(0f);
        [HideInInspector] public bool outerSoftnessUpgraded;
        public ZUIValue OuterSoftness { get { if (!outerSoftnessUpgraded) { outerSoftnessValue = new ZUIValue(outerSoftness); outerSoftnessUpgraded = true; } return outerSoftnessValue; } }
        [HideInInspector] public float outerSoftnessCurve = 1f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0.2f, 5f)]
        [Tooltip("Shapes the Outer softness falloff curve — 1 = linear (the default). Higher stays near full " +
                 "strength longer then drops off sharply right at the tail (a tighter, more \"smoothed\" edge to " +
                 "the glow instead of a straight ramp); lower drops off quickly then lingers faintly for longer. " +
                 "Has no effect while Outer softness is 0.")]
        public ZUIValue outerSoftnessCurveValue = new ZUIValue(1f);
        [HideInInspector] public bool outerSoftnessCurveUpgraded;
        public ZUIValue OuterSoftnessCurve { get { if (!outerSoftnessCurveUpgraded) { outerSoftnessCurveValue = new ZUIValue(outerSoftnessCurve); outerSoftnessCurveUpgraded = true; } return outerSoftnessCurveValue; } }

        int sz;
        float thrV, innerSoftV, innerCurveV, outerSoftV, outerCurveV;
        Color overLifeColor;
        public override string DisplayName => "Outline";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            sz = Mathf.Clamp(Mathf.RoundToInt(e(size, 0)), 0, 32);
            innerSoftV = Mathf.Clamp(e(InnerSoftness, 1), 0f, 16f);
            outerSoftV = Mathf.Clamp(e(OuterSoftness, 2), 0f, 16f);
            innerCurveV = Mathf.Clamp(e(InnerSoftnessCurve, 3), 0.2f, 5f);
            outerCurveV = Mathf.Clamp(e(OuterSoftnessCurve, 4), 0.2f, 5f);
            thrV = Mathf.Clamp(e(AlphaThreshold, 5), 0.01f, 1f);
            if (mode == ColorMode.OverLife && color != null) overLifeColor = color.Evaluate(Mathf.Clamp01(life));
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if ((sz < 1 && innerSoftV < 0.001f) || color == null) return;
            byte at = (byte)(thrV * 255f);
            var src = (Color32[])buf.Clone();
            int R = sz;

            // ── outward ring: transparent pixels near the shape, within Size (+ its own outward fade) ──────
            if (sz >= 1)
            {
                // The outward fade can read a bit past the nominal thickness, so the neighbour search has to
                // reach that far too — otherwise pixels in the fade band beyond R would never find a shape
                // pixel to measure distance from and'd just be skipped.
                int searchOut = Mathf.CeilToInt(R + outerSoftV);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int idx = y * W + x;
                        if (src[idx].a > at) continue;   // a shape pixel — handled by the inward pass below instead

                        int best2 = int.MaxValue;
                        for (int dy = -searchOut; dy <= searchOut; dy++)
                        {
                            int yy = y + dy; if (yy < 0 || yy >= H) continue;
                            for (int dx = -searchOut; dx <= searchOut; dx++)
                            {
                                int xx = x + dx; if (xx < 0 || xx >= W) continue;
                                if (src[yy * W + xx].a <= at) continue;
                                int d2 = dx * dx + dy * dy;
                                if (d2 < best2) best2 = d2;
                            }
                        }
                        float d = Mathf.Sqrt(best2);
                        if (d > R + outerSoftV) continue;   // beyond the thickness (+ its outward fade)

                        float fadeA = 1f;
                        if (outerSoftV > 0.001f)
                        {
                            float lin = Mathf.Clamp01((R + outerSoftV - d) / outerSoftV);
                            fadeA = Mathf.Pow(lin, outerCurveV);
                        }
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

            // ── inward spill: shape pixels within Inner softness of the boundary get the outline colour ────
            // blended OVER their own colour (an inset glow) — full strength right at the edge, fading back to
            // the shape's own colour deeper in. A separate pass over the SAME src snapshot (not the ring pass's
            // partial results above) so the two never interfere with each other's distance search.
            if (innerSoftV > 0.001f)
            {
                int searchIn = Mathf.CeilToInt(innerSoftV);
                Color edgeColor = mode == ColorMode.OverLife ? overLifeColor : color.Evaluate(0f);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int idx = y * W + x;
                        if (src[idx].a <= at) continue;   // not a shape pixel — handled by the outward pass above

                        int best2 = int.MaxValue;
                        for (int dy = -searchIn; dy <= searchIn; dy++)
                        {
                            int yy = y + dy; if (yy < 0 || yy >= H) continue;
                            for (int dx = -searchIn; dx <= searchIn; dx++)
                            {
                                int xx = x + dx; if (xx < 0 || xx >= W) continue;
                                if (src[yy * W + xx].a > at) continue;   // looking for the nearest BACKGROUND pixel now
                                int d2 = dx * dx + dy * dy;
                                if (d2 < best2) best2 = d2;
                            }
                        }
                        if (best2 == int.MaxValue) continue;   // no background within reach — deep interior, untouched
                        float d = Mathf.Sqrt(best2);
                        if (d > innerSoftV) continue;

                        float lin = Mathf.Clamp01(1f - d / innerSoftV);   // full AT the boundary, fading inward
                        float fadeA = Mathf.Pow(lin, innerCurveV);
                        float outA = Mathf.Clamp01(edgeColor.a * fadeA);
                        if (outA <= 0.003f) continue;

                        // Straight alpha-over: outline colour over the shape's own existing pixel colour.
                        Color32 baseC = buf[idx];
                        float baseA = baseC.a / 255f;
                        float resultA = outA + baseA * (1f - outA);
                        if (resultA <= 0.0001f) { buf[idx] = new Color32(0, 0, 0, 0); continue; }
                        float inv = 1f - outA;
                        float r = (edgeColor.r * outA + (baseC.r / 255f) * baseA * inv) / resultA;
                        float g = (edgeColor.g * outA + (baseC.g / 255f) * baseA * inv) / resultA;
                        float b = (edgeColor.b * outA + (baseC.b / 255f) * baseA * inv) / resultA;
                        buf[idx] = new Color32(ToByte(r), ToByte(g), ToByte(b), ToByte(resultA));
                    }
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class ChromaticAberrationModifier : PostModifier
    {
        [Range(0f, 8f)]
        [Tooltip("How far the red/blue channels split apart, in pixels. Animatable — punch it in on impact, settle out.")]
        public ZUIValue amount = new ZUIValue(1.5f);
        [Range(0f, 1f)]
        [Tooltip("How much of the fringed result blends over the original image (0 = untouched, 1 = full effect). " +
                 "Animatable — fade the aberration in/out independently of Amount (the split distance itself).")]
        public ZUIValue alpha = new ZUIValue(1f);
        [Tooltip("Radial = split outward from the canvas centre (stronger toward the edges); off = a flat, " +
                 "uniform split along Angle.")]
        public bool radial = true;
        [Range(-180f, 180f)]
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

    /// A projectile tunnelling through the ALREADY-RENDERED frame as if it were a cloud of some density — the
    /// pixel data itself (alpha) IS the cloud, not a separately-authored field, so a dense (opaque) region
    /// genuinely resists the shot more than empty (transparent) space. Necessarily a whole-frame Post effect,
    /// not a per-shape GeometryModifier: only a Post pass sees the FINISHED pixels to read density from at all
    /// (a GeometryModifier's InverseWarp runs BEFORE its own shape's pixel is even sampled, so it has nothing
    /// real to read density from yet).
    ///
    /// Each frame, marches the projectile's travel line from the canvas edge (Depth 0) to its current tip
    /// (Depth 1 = the far edge) in fixed steps, sampling the cloud's own alpha along the centreline and
    /// integrating it into a running "how much medium has this shot already punched through" total — the
    /// remaining push force decays with that integral (Beer-Lambert-style absorption: exp(-integratedDensity ×
    /// Density)), so a shot that's already torn through a lot of dense cloud arrives at any given point with
    /// less force left than one that had a clear run. This is a single-frame SPATIAL integral (along the
    /// CURRENT frame's own line), not carried over between frames — Pyre bakes every frame independently.
    /// Density = 0 disables the resistance entirely (uniform full-strength push along the whole path, same as
    /// a shot moving through a vacuum).
    ///
    /// The actual push is a resample (like Sphere/the old rod modifiers' inverse-remap), reading from a
    /// snapshot of the frame taken before this modifier ran, so pixels the shot passes get their content pulled
    /// sideways out of the way — using the cloud's OWN pixels, not a synthetic stretch.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class CloudProjectileModifier : PostModifier
    {
        [Range(-180f, 180f)]
        [Tooltip("Direction the projectile travels, in degrees (0 = along +X). Animatable.")]
        public ZUIValue angleDeg = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Slides the travel line sideways (perpendicular to its own direction), in pixels off the " +
                 "canvas centre. Animatable.")]
        public ZUIValue offset = new ZUIValue(0f);
        [Range(0f, 1f)]
        [Tooltip("How far the projectile has travelled: 0 = hasn't entered yet (sitting at the canvas edge), " +
                 "1 = has travelled all the way across to the far edge. Animatable — default ramps 0→1 over life.")]
        public ZUIValue depth = Sfx.CurveVal(1f, 0f, 0f, 1f, 1f);
        [Range(1f, 32f)]
        [Tooltip("How far the push reaches perpendicular to the travel line, in pixels.")]
        public ZUIValue radius = new ZUIValue(10f);
        [Range(-20f, 20f)]
        [Tooltip("Push strength before any cloud resistance is applied. Animatable.")]
        public ZUIValue strength = new ZUIValue(6f);
        [Range(0f, 5f)]
        [Tooltip("How strongly the cloud's own density (its rendered alpha) resists the shot. 0 = no resistance " +
                 "at all — full strength the whole way through, like moving through a vacuum. Higher = force " +
                 "drops off faster the more (and denser) cloud the shot has already torn through, so it arrives " +
                 "at the far side with less punch than it started with. Animatable.")]
        public ZUIValue density = new ZUIValue(1f);

        const int Steps = 64;
        readonly float[] forceProfile = new float[Steps];

        float ang, offPx, depthV, rad, amt, densityScale;
        public override string DisplayName => "Cloud projectile";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            ang = e(angleDeg, 0) * Mathf.Deg2Rad;
            offPx = e(offset, 1);
            depthV = Mathf.Clamp01(e(depth, 2));
            rad = Mathf.Max(0.5f, e(radius, 3));
            amt = e(strength, 4);
            densityScale = Mathf.Max(0f, e(density, 5));
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (Mathf.Abs(amt) < 0.001f) return;
            var cloud = (Color32[])buf.Clone();

            Vector2 canvasCenter = new Vector2(W * 0.5f, H * 0.5f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 pivot = canvasCenter + perp * offPx;

            float reach = ComputeCanvasReach(ang, W, H);
            float startAlong = -reach;
            float tipAlong = Mathf.Lerp(startAlong, reach, depthV);

            // Precompute the "remaining force" profile along the centreline from Start to the current tip,
            // integrating the cloud's own alpha (density) as an absorption term (Beer-Lambert-style decay).
            float span = tipAlong - startAlong;
            float dsStep = span / Mathf.Max(1, Steps - 1);
            float integrated = 0f;
            for (int k = 0; k < Steps; k++)
            {
                float s = startAlong + k * dsStep;
                Vector2 p = pivot + dir * s;
                float localDensity = SampleAlpha(cloud, W, H, p);
                integrated += localDensity * Mathf.Abs(dsStep) * 0.02f * densityScale;
                forceProfile[k] = amt * Mathf.Exp(-integrated);
            }

            var result = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    Vector2 d = p - pivot;
                    float alongSigned = Vector2.Dot(d, dir);
                    float perpSigned = Vector2.Dot(d, perp);
                    float absPerp = Mathf.Abs(perpSigned);

                    if (absPerp >= rad || alongSigned < startAlong || alongSigned > tipAlong)
                    {
                        result[idx] = cloud[idx];
                        continue;
                    }

                    float tIdx = Mathf.Abs(dsStep) > 0.0001f ? (alongSigned - startAlong) / dsStep : 0f;
                    int k0 = Mathf.Clamp(Mathf.FloorToInt(tIdx), 0, Steps - 1);
                    int k1 = Mathf.Clamp(k0 + 1, 0, Steps - 1);
                    float frac = Mathf.Clamp01(tIdx - k0);
                    float localForce = Mathf.Lerp(forceProfile[k0], forceProfile[k1], frac);

                    float t = absPerp / rad;
                    float rSample = t * t * rad;
                    float rFinal = Mathf.LerpUnclamped(absPerp, rSample, localForce);
                    float sign = perpSigned >= 0f ? 1f : -1f;
                    Vector2 alongComp = d - perp * perpSigned;
                    Vector2 sourcePos = pivot + alongComp + perp * (sign * rFinal);
                    result[idx] = SampleNearest(cloud, W, H, sourcePos);
                }
            Array.Copy(result, buf, result.Length);
        }

        static float ComputeCanvasReach(float ang, int W, int H)
        {
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float hHalf = W * 0.5f, vHalf = H * 0.5f;
            float rx = Mathf.Abs(dir.x) > 1e-4f ? hHalf / Mathf.Abs(dir.x) : float.MaxValue;
            float ry = Mathf.Abs(dir.y) > 1e-4f ? vHalf / Mathf.Abs(dir.y) : float.MaxValue;
            return Mathf.Min(rx, ry);
        }

        static float SampleAlpha(Color32[] buf, int W, int H, Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, W - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, H - 1);
            return buf[y * W + x].a * (1f / 255f);
        }

        static Color32 SampleNearest(Color32[] buf, int W, int H, Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, W - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, H - 1);
            return buf[y * W + x];
        }
    }

    /// A richer sibling to CloudProjectileModifier, modelled on a reference pixel-fluid simulation (projectile
    /// tunnel + trailing shockwave rings + an alternating vortex street, advecting a density field through a
    /// velocity field). That reference is a genuine iterative simulation — each frame's density/velocity depend
    /// on the PREVIOUS frame's — which doesn't fit Pyre's bake-any-frame-independently model directly. The trick
    /// used here (same one Piercing used before it was removed) is to make every emitter's state a CLOSED-FORM
    /// function of "how far past its own spawn point the projectile now is", not an iterative accumulation:
    /// - The projectile spawns a shockwave every WaveSpacing (a FRACTION of the whole travel, not a raw pixel
    ///   distance) and a vortex every Vortex spacing, alternating spin by index parity (deterministic, unlike
    ///   the reference's mutable flip-each-spawn flag, but produces the identical alternating pattern).
    /// - Wave k's spawn point is at Depth = k×WaveSpacing; if the CURRENT Depth hasn't reached that yet, wave k
    ///   simply doesn't exist this frame. Its age (Depth − spawn Depth) directly gives its current radius
    ///   (Base + Expansion×age) and current strength (Strength × exp(−Decay×age)) — the reference's per-frame
    ///   `persistence ** (dt×60)` accumulation IS exactly this same exponential decay, just re-expressed in
    ///   closed form against elapsed age instead of iterated frame-by-frame.
    /// - Same idea for vortices, with their per-vortex jitter (position offset, drift, radius variance) drawn
    ///   from Sfx.Hash01 keyed on the vortex's own index — deterministic every time index k is asked
    ///   for, unlike the reference's real `random.uniform` calls, which Pyre's baked/scrubbable timeline can't
    ///   use (the same frame must always render identically).
    /// - The reference's stochastic child-vortex shedding (spawned via a per-frame random chance, an unbounded
    ///   and unpredictable branching tree) is the one piece left out — it's the least closed-form-friendly part
    ///   of the whole simulation. Everything else — the trailing pressure shell, the alternating swirl street,
    ///   the tunnel's own forward/sideways push and alpha erosion — carries over.
    /// - No persisted density/velocity GRID either: like CloudProjectileModifier, this resamples directly from
    ///   a snapshot of the already-rendered frame (the pixel data IS the cloud), rather than advecting a
    ///   separately-simulated field — the visible push at any pixel is the SUM of the projectile tunnel's own
    ///   push plus every currently-existing wave's and vortex's contribution, evaluated fresh each frame.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class BallisticShockwaveModifier : PostModifier
    {
        [Range(-180f, 180f)]
        [Tooltip("Direction the projectile travels, in degrees (0 = along +X). Animatable.")]
        public ZUIValue angleDeg = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Slides the travel line sideways (perpendicular to its own direction), in pixels off the " +
                 "canvas centre. Animatable.")]
        public ZUIValue offset = new ZUIValue(0f);
        [Range(0f, 1f)]
        [Tooltip("How far the projectile has travelled: 0 = hasn't entered yet (sitting at the canvas edge), " +
                 "1 = has travelled all the way across to the far edge. Animatable — default ramps 0→1 over life.")]
        public ZUIValue depth = Sfx.CurveVal(1f, 0f, 0f, 1f, 1f);
        [Range(0.5f, 10f)]
        [Tooltip("Radius of the projectile's own tunnel through the cloud, in pixels.")]
        public ZUIValue projectileRadius = new ZUIValue(3f);
        [Range(-20f, 20f)]
        [Tooltip("How hard the tunnel pushes material forward and to the sides. Animatable.")]
        public ZUIValue projectileForce = new ZUIValue(6f);
        [Range(0f, 1f)]
        [Tooltip("How much the tunnel's own core erases alpha outright (0 = pure push, nothing erased; 1 = a " +
                 "clean, fully-cleared core), on top of the push. Animatable.")]
        public ZUIValue erosion = new ZUIValue(0.75f);

        [Range(0.01f, 0.5f)]
        [Tooltip("How often a shockwave ring spawns, as a FRACTION of the whole travel (0.06 ≈ 16 rings across " +
                 "the full path). Smaller = more, denser trailing rings.")]
        public ZUIValue waveSpacing = new ZUIValue(0.06f);
        [Range(-20f, 20f)]
        [Tooltip("Each ring's push strength the moment it spawns. Animatable.")]
        public ZUIValue waveStrength = new ZUIValue(5f);
        [Range(0f, 32f)]
        [Tooltip("How far a ring's own radius grows over a full remaining traversal, in pixels. Animatable.")]
        public ZUIValue waveExpansion = new ZUIValue(30f);
        [Range(0f, 20f)]
        [Tooltip("How fast a ring's push fades as it ages (higher = shorter-lived rings). Animatable.")]
        public ZUIValue waveDecay = new ZUIValue(6f);
        [Range(0.5f, 8f)]
        [Tooltip("Thickness of the travelling pressure shell, in pixels — how far either side of a ring's own " +
                 "current radius the push still reaches.")]
        public ZUIValue waveThickness = new ZUIValue(2.5f);

        [Range(0.01f, 0.5f)]
        [Tooltip("How often a vortex spawns, as a FRACTION of the whole travel — alternates spin direction by " +
                 "index, the same alternating-eddy \"vortex street\" a real bluff body sheds.")]
        public ZUIValue vortexSpacing = new ZUIValue(0.05f);
        [Range(-20f, 20f)]
        [Tooltip("Each vortex's swirl strength the moment it spawns. Animatable.")]
        public ZUIValue vortexStrength = new ZUIValue(8f);
        [Range(0.5f, 16f)]
        [Tooltip("Each vortex's core radius, in pixels (jittered per-vortex). Animatable.")]
        public ZUIValue vortexRadius = new ZUIValue(8f);
        [Range(0f, 20f)]
        [Tooltip("How fast a vortex's swirl fades as it ages (higher = shorter-lived vortices). Animatable.")]
        public ZUIValue vortexDecay = new ZUIValue(5f);
        [Range(0f, 5f)]
        [Tooltip("Strength of each vortex's radial \"breathing\" pulse (in + out), on top of its tangential " +
                 "swirl — 0 = a clean, geometric spiral; higher = a less regular, pulsing one. Animatable.")]
        public ZUIValue vortexPulse = new ZUIValue(1f);

        // Hard cap on how many waves/vortices a single frame considers — an internal quality/perf knob (bounded
        // by how many WOULD have spawned by Depth 1 given Wave/Vortex spacing), not a creative param.
        const int MaxEmitters = 48;
        struct Wave { public float centerAlong, radius, strength; }
        struct Vortex { public Vector2 center; public float radius, strength, spin, phase; }
        readonly Wave[] waves = new Wave[MaxEmitters];
        readonly Vortex[] vortices = new Vortex[MaxEmitters];
        int waveCount, vortexCount;

        float ang, offPx, depthV, projRad, projForce, erosionAmt;
        float waveSpacingF, waveStr, waveExp, waveDecayV, waveThick;
        float vortexSpacingF, vortexStr, vortexRad, vortexDecayV, vortexPulseV;

        public override string DisplayName => "Ballistic shockwave";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            ang = e(angleDeg, 0) * Mathf.Deg2Rad;
            offPx = e(offset, 1);
            depthV = Mathf.Clamp01(e(depth, 2));
            projRad = Mathf.Max(0.5f, e(projectileRadius, 3));
            projForce = e(projectileForce, 4);
            erosionAmt = Mathf.Clamp01(e(erosion, 5));
            waveSpacingF = Mathf.Max(0.005f, e(waveSpacing, 6));
            waveStr = e(waveStrength, 7);
            waveExp = e(waveExpansion, 8);
            waveDecayV = Mathf.Max(0f, e(waveDecay, 9));
            waveThick = Mathf.Max(0.5f, e(waveThickness, 10));
            vortexSpacingF = Mathf.Max(0.005f, e(vortexSpacing, 11));
            vortexStr = e(vortexStrength, 12);
            vortexRad = Mathf.Max(0.5f, e(vortexRadius, 13));
            vortexDecayV = Mathf.Max(0f, e(vortexDecay, 14));
            vortexPulseV = e(vortexPulse, 15);
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (depthV <= 0.0001f) return;
            var cloud = (Color32[])buf.Clone();

            Vector2 canvasCenter = new Vector2(W * 0.5f, H * 0.5f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 pivot = canvasCenter + perp * offPx;

            float reach = ComputeCanvasReach(ang, W, H);
            float startAlong = -reach;
            float tipAlong = Mathf.Lerp(startAlong, reach, depthV);

            int hashBase = unchecked((Mathf.RoundToInt(pivot.x * 8f) * 92821) ^ (Mathf.RoundToInt(pivot.y * 8f) * 68111)
                ^ Mathf.RoundToInt(ang * 10000f));

            ResolveWaves(pivot, dir, startAlong, reach);
            ResolveVortices(pivot, dir, perp, startAlong, reach, hashBase);

            var result = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    Vector2 push = Vector2.zero;
                    float erosionFalloff = 0f;

                    // Projectile tunnel: perpendicular push away from the travel LINE plus a small forward
                    // component, strongest right at the tunnel wall, plus an alpha erosion at the core.
                    {
                        Vector2 d = p - pivot;
                        float alongSigned = Vector2.Dot(d, dir);
                        float nearestAlong = Mathf.Clamp(alongSigned, startAlong, tipAlong);
                        Vector2 toPixel = p - (pivot + dir * nearestAlong);
                        float distToPath = toPixel.magnitude;
                        if (distToPath < projRad)
                        {
                            float falloff = 1f - distToPath / projRad;
                            Vector2 sideDir = distToPath > 0.001f ? toPixel / distToPath : perp;
                            push += dir * (projForce * falloff * 0.4f);
                            push += sideDir * (projForce * falloff);
                            erosionFalloff = falloff;
                        }
                    }

                    // Trailing shockwave rings — each pushes OUTWARD from its own centre point on the travel
                    // line, strongest at its own current radius, fading over Wave thickness either side.
                    for (int i = 0; i < waveCount; i++)
                    {
                        var w = waves[i];
                        Vector2 waveCenter = pivot + dir * w.centerAlong;
                        Vector2 d = p - waveCenter;
                        float dist = d.magnitude;
                        if (dist < 0.001f) continue;
                        float distToRing = Mathf.Abs(dist - w.radius);
                        if (distToRing > waveThick) continue;
                        float shell = 1f - distToRing / waveThick;
                        push += (d / dist) * (w.strength * shell);
                    }

                    // Alternating vortex street — tangential swirl (direction set by each vortex's own spin)
                    // plus a radial breathing pulse, both fading smoothly with distance from the vortex core.
                    for (int i = 0; i < vortexCount; i++)
                    {
                        var v = vortices[i];
                        Vector2 d = p - v.center;
                        float dist = d.magnitude;
                        float reachV = v.radius * 1.5f;
                        if (dist < 0.001f || dist > reachV) continue;
                        float nd = dist / v.radius;
                        float falloff = Mathf.Exp(-nd * nd * 1.6f);
                        Vector2 normal = d / dist;
                        Vector2 tangent = new Vector2(-normal.y, normal.x) * v.spin;
                        push += tangent * (v.strength * falloff);
                        float pulse = Mathf.Sin(nd * Mathf.PI * 2f - v.phase) * vortexPulseV * falloff;
                        push += normal * pulse;
                    }

                    Vector2 sourcePos = p - push;
                    Color32 sampled = SampleNearestLocal(cloud, W, H, sourcePos);
                    if (erosionFalloff > 0f)
                        sampled.a = (byte)Mathf.RoundToInt(sampled.a * Mathf.Clamp01(1f - erosionAmt * erosionFalloff));
                    result[idx] = sampled;
                }
            Array.Copy(result, buf, result.Length);
        }

        // Closed-form: wave k's spawn point is Depth = k×WaveSpacing. If the current Depth hasn't reached that
        // yet, it doesn't exist this frame. Its age (current Depth minus its own spawn Depth) alone determines
        // its current radius/strength — no iteration, no history, fully re-derivable from THIS frame's Depth.
        void ResolveWaves(Vector2 pivot, Vector2 dir, float startAlong, float reach)
        {
            waveCount = 0;
            int maxK = Mathf.Min(MaxEmitters, Mathf.CeilToInt(1f / waveSpacingF) + 1);
            for (int k = 0; k < maxK; k++)
            {
                float spawnDepth = k * waveSpacingF;
                if (spawnDepth > depthV) break;
                float age = depthV - spawnDepth;
                float strength = waveStr * Mathf.Exp(-waveDecayV * age);
                if (strength < 0.02f) continue;
                float radius = 0.5f + waveExp * age;
                float centerAlong = Mathf.Lerp(startAlong, reach, spawnDepth);
                waves[waveCount++] = new Wave { centerAlong = centerAlong, radius = radius, strength = strength };
                if (waveCount >= MaxEmitters) break;
            }
        }

        void ResolveVortices(Vector2 pivot, Vector2 dir, Vector2 perp, float startAlong, float reach, int hashBase)
        {
            vortexCount = 0;
            int maxJ = Mathf.Min(MaxEmitters, Mathf.CeilToInt(1f / vortexSpacingF) + 1);
            for (int j = 0; j < maxJ; j++)
            {
                float spawnDepth = j * vortexSpacingF;
                if (spawnDepth > depthV) break;
                float age = depthV - spawnDepth;
                float spin = (j % 2 == 0) ? 1f : -1f;
                float strength = vortexStr * Mathf.Exp(-vortexDecayV * age);
                if (strength < 0.02f) continue;

                float jitterA = Sfx.Hash01(hashBase, j, 0);
                float jitterB = Sfx.Hash01(hashBase, j, 1);
                float jitterC = Sfx.Hash01(hashBase, j, 2);
                float vertOffset = spin * Mathf.Lerp(1f, 2.2f, jitterA);
                float driftAlong = Mathf.Lerp(0.15f, 0.35f, jitterB) * age * 20f;
                float driftPerp = spin * Mathf.Lerp(0.15f, 0.45f, jitterC) * age * 20f;
                float radius = vortexRad * Mathf.Lerp(0.8f, 1.2f, jitterA) + age * 2f;
                float centerAlong = Mathf.Lerp(startAlong, reach, spawnDepth) + driftAlong;
                Vector2 center = pivot + dir * centerAlong + perp * (vertOffset + driftPerp);
                float phase = age * 24f * spin;

                vortices[vortexCount++] = new Vortex { center = center, radius = radius, strength = strength, spin = spin, phase = phase };
                if (vortexCount >= MaxEmitters) break;
            }
        }

        static float ComputeCanvasReach(float ang, int W, int H)
        {
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float hHalf = W * 0.5f, vHalf = H * 0.5f;
            float rx = Mathf.Abs(dir.x) > 1e-4f ? hHalf / Mathf.Abs(dir.x) : float.MaxValue;
            float ry = Mathf.Abs(dir.y) > 1e-4f ? vHalf / Mathf.Abs(dir.y) : float.MaxValue;
            return Mathf.Min(rx, ry);
        }

        static Color32 SampleNearestLocal(Color32[] buf, int W, int H, Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, W - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, H - 1);
            return buf[y * W + x];
        }
    }

    /// A cheap, general "melt nearby shapes into one blob" effect: box-blur the whole (premultiplied) frame, then
    /// re-threshold alpha with a soft band so overlapping/nearby silhouettes' blurred halos cross the threshold
    /// together and read as fused, while an isolated shape mostly reconstitutes near its own edge. A pixel-space
    /// APPROXIMATION of MetaBlob's exact SDF-field fusion — much cheaper, and (unlike MetaBlob) works on ANY
    /// already-rendered pixels: any layer shape (even Bars/Sprite), any modifier stack, or — as a
    /// global modifier — several different layers melted together after they all composite. `colorBleed`
    /// separately controls how much colour blends across the fused seam, independent of the silhouette fusion.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class FuseModifier : PostModifier
    {
        [Range(0f, 16f)]
        [Tooltip("How far the fusing effect reaches, in pixels — bigger blends more distant shapes together. Animatable.")]
        public ZUIValue radius = new ZUIValue(4f);
        [Range(0f, 1f)]
        [Tooltip("Alpha level pixels must reach (after blurring) to stay solid — lower fuses more eagerly (thicker " +
                 "bridges between shapes); higher keeps shapes more separate (fuses only where they nearly touch). " +
                 "Animatable — rise it over life to pull fused shapes back apart.")]
        public ZUIValue threshold = new ZUIValue(0.5f);
        [Range(0.02f, 1f)]
        [Tooltip("Softness of the re-solidified edge — low is closer to a hard cutoff, high a wide soft gradient. Animatable.")]
        public ZUIValue softness = new ZUIValue(0.3f);
        [Range(0f, 1f)]
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class JaggModifier : GeometryModifier
    {
        [Range(2, 24)] public int arms = 5;
        [Range(0f, 0.95f)]
        [Tooltip("Arm length / valley depth (0 = circle, →1 = spiky star). Animatable.")]
        public ZUIValue strength = new ZUIValue(0.4f);
        [Range(-180f, 180f)]
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class EdgeWarpModifier : EdgeModifier
    {
        [Range(0f, 10f)]
        [Tooltip("How far the edge bulges in/out at each bump, in pixels. Animatable.")]
        public ZUIValue amplitude = new ZUIValue(2f);
        [Range(1f, 24f)]
        [Tooltip("Roughly how many bumps run around the shape's rim. Animatable.")]
        public ZUIValue frequency = new ZUIValue(6f);
        [Range(0f, 1f)]
        [Tooltip("0 = a smooth, rounded, wavy edge. 1 = a hard, faceted, torn/jagged edge. Animatable — roughen up " +
                 "a silhouette over life.")]
        public ZUIValue jaggedness = new ZUIValue(0.5f);
        [Range(0f, 2f)]
        [Tooltip("Domain-warp strength on the underlying noise — higher makes the bump spacing less regular, more " +
                 "organic. Animatable.")]
        public ZUIValue warp = new ZUIValue(0.4f);
        [Range(0f, 8f)]
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class TurbulenceModifier : GeometryModifier
    {
        [Range(0f, 32f)]
        [Tooltip("How far pixels are displaced by the noise field, in pixels. Animatable — rise it in as the shape matures.")]
        public ZUIValue amplitude = new ZUIValue(4f);
        [Range(1f, 64f)]
        [Tooltip("Noise frequency — bigger = larger, slower-looking eddies; smaller = fine, busy churn. Animatable.")]
        public ZUIValue zoom = new ZUIValue(24f);
        [Range(-720f, 720f)]
        [Tooltip("Rotates the noise field's own sampling domain, in degrees — this is what makes the churn visibly " +
                 "SPIN in place (a mushroom cloud's roll). Animatable — a rising curve = an accelerating roll.")]
        public ZUIValue rotation = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Scrolls the noise field horizontally over life, in pixels — the pattern itself drifts rather " +
                 "than the displacement just sitting still. Animatable.")]
        public ZUIValue offsetX = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Scrolls the noise field vertically over life, in pixels. Animatable.")]
        public ZUIValue offsetY = new ZUIValue(0f);
        [Range(0f, 2f)]
        [Tooltip("Domain-warp strength — how much the noise bends on itself (0 = plain smooth noise, higher = " +
                 "more churned/organic eddies). Animatable — e.g. ramp it up for a churn that gets more organic " +
                 "over life. Pushed high enough, the noise field can fold over itself and carve sharp notches " +
                 "into an otherwise smooth edge — pair with an Edge smooth modifier if that reads as too jagged.")]
        public ZUIValue warp = new ZUIValue(0.6f);

        float amp, zm, rotRad, offX, offY, wrp;
        public override string DisplayName => "Turbulence";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amp = e(amplitude, 0);
            zm = Mathf.Max(1f, e(zoom, 1));
            rotRad = e(rotation, 2) * Mathf.Deg2Rad;
            offX = e(offsetX, 3);
            offY = e(offsetY, 4);
            wrp = Mathf.Clamp(e(warp, 5), 0f, 2f);
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
            float n1 = PyreNoise.Sample(nx, ny, seed, wrp) * 2f - 1f;
            float n2 = PyreNoise.Sample(nx + 31.7f, ny - 17.3f, seed ^ 0x1234567, wrp) * 2f - 1f;
            off.x += n1 * amp;
            off.y += n2 * amp;
            return off;
        }
    }

    /// A twin of TurbulenceModifier using genuine 2D GRADIENT noise (PyreNoise.SampleGradient — interpolated
    /// random direction vectors, not raw values, plus a quintic fade) instead of TurbulenceModifier's bilinear
    /// VALUE noise. Value noise's hills sit visibly centred ON each lattice point, which is what gives it a
    /// faintly blobby/grid-aligned look at low octave counts; gradient noise doesn't have that bias and reads
    /// as sharper and more organic at the same frequency. Kept as a SEPARATE, opt-in modifier rather than
    /// swapping Turbulence's own sampler in place — that would silently reshape every asset already built on
    /// it (and on Curl/Noise fill/AlphaMask's Noise shape, all sharing the same PyreNoise.Sample) — so the two
    /// can be compared side by side instead.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class PerlinTurbulenceModifier : GeometryModifier
    {
        [Range(0f, 32f)]
        [Tooltip("How far pixels are displaced by the noise field, in pixels. Animatable — rise it in as the shape matures.")]
        public ZUIValue amplitude = new ZUIValue(4f);
        [Range(1f, 64f)]
        [Tooltip("Noise frequency — bigger = larger, slower-looking eddies; smaller = fine, busy churn. Animatable.")]
        public ZUIValue zoom = new ZUIValue(24f);
        [Range(-720f, 720f)]
        [Tooltip("Rotates the noise field's own sampling domain, in degrees — this is what makes the churn visibly " +
                 "SPIN in place (a mushroom cloud's roll). Animatable — a rising curve = an accelerating roll.")]
        public ZUIValue rotation = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Scrolls the noise field horizontally over life, in pixels — the pattern itself drifts rather " +
                 "than the displacement just sitting still. Animatable.")]
        public ZUIValue offsetX = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Scrolls the noise field vertically over life, in pixels. Animatable.")]
        public ZUIValue offsetY = new ZUIValue(0f);
        [Range(0f, 2f)]
        [Tooltip("Domain-warp strength — how much the noise bends on itself (0 = plain smooth noise, higher = " +
                 "more churned/organic eddies). Animatable. Pushed high enough, the noise field can fold over " +
                 "itself and carve sharp notches into an otherwise smooth edge — pair with an Edge smooth " +
                 "modifier if that reads as too jagged.")]
        public ZUIValue warp = new ZUIValue(0.6f);

        float amp, zm, rotRad, offX, offY, wrp;
        public override string DisplayName => "Perlin turbulence";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amp = e(amplitude, 0);
            zm = Mathf.Max(1f, e(zoom, 1));
            rotRad = e(rotation, 2) * Mathf.Deg2Rad;
            offX = e(offsetX, 3);
            offY = e(offsetY, 4);
            wrp = Mathf.Clamp(e(warp, 5), 0f, 2f);
        }

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            if (Mathf.Abs(amp) < 0.01f) return off;
            Vector2 d = off - ctx.center;
            if (rotRad != 0f)
            {
                float c = Mathf.Cos(rotRad), s = Mathf.Sin(rotRad);
                d = new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
            }
            float nx = (d.x + offX) / zm;
            float ny = (d.y + offY) / zm;
            int seed = unchecked((Mathf.RoundToInt(ctx.center.x * 8f) * 92821) ^ (Mathf.RoundToInt(ctx.center.y * 8f) * 68111));
            float n1 = PyreNoise.SampleGradient(nx, ny, seed, wrp) * 2f - 1f;
            float n2 = PyreNoise.SampleGradient(nx + 31.7f, ny - 17.3f, seed ^ 0x1234567, wrp) * 2f - 1f;
            off.x += n1 * amp;
            off.y += n2 * amp;
            return off;
        }
    }

    /// Softens jagged/torn silhouette edges — e.g. from Turbulence/Perlin turbulence's own Warp folded high
    /// enough to carve sharp notches into what should be a smooth edge. A standalone modifier rather than
    /// baked into Turbulence itself, since ANY jagged-edge source (Jagg, EdgeWarp, a wild Wobble) can use the
    /// same cleanup, and most of the time nothing needs it at all. Blurs ONLY alpha (a two-pass box blur, done
    /// in PREMULTIPLIED space so a rising edge doesn't blend in the arbitrary/garbage colour a fully-transparent
    /// pixel holds — the standard fix for the "black fringe" a naive straight-alpha blur produces) — colour
    /// stays exactly as rendered, so this reads as a softened edge, not an overall haze.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class EdgeSmoothModifier : PostModifier
    {
        [Range(0f, 16f)]
        [Tooltip("Blur radius, in pixels — how far the edge softening reaches. Animatable.")]
        public ZUIValue radius = new ZUIValue(2f);
        [Range(0f, 1f)]
        [Tooltip("How much of the blur blends back in — 0 = untouched (edges stay exactly as jagged as rendered), " +
                 "1 = fully softened. Animatable.")]
        public ZUIValue strength = new ZUIValue(1f);

        int rad;
        float amt;
        public override string DisplayName => "Edge smooth";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            rad = Mathf.Clamp(Mathf.RoundToInt(e(radius, 0)), 0, 16);
            amt = Mathf.Clamp01(e(strength, 1));
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (rad <= 0 || amt <= 0.001f) return;
            int n = W * H;
            // Only ALPHA needs blurring to soften the outer edge; RGB is blurred too (premultiplied, to avoid a
            // black fringe — see below) but ONLY EVER used where alpha is being extended into previously-empty
            // space. Blurring unconditionally (the previous version) smoothed the WHOLE image's colour, not just
            // the edge — any shape with a gradient/noise fill has real pixel-to-pixel colour variation all the
            // way through its interior, and blurring blended that everywhere, reported as "mostly smooths out
            // the whole image, not just the edges". This is meant to be an edge-aware OUTER SOFTNESS, so a pixel
            // that already has meaningful alpha keeps its EXACT original colour, always — only its alpha may
            // soften. Only pixels near the true boundary (low/zero original alpha) borrow a blurred colour.
            var pr = new float[n]; var pg = new float[n]; var pb = new float[n]; var pa = new float[n];
            for (int i = 0; i < n; i++)
            {
                var c = buf[i];
                float a = c.a * (1f / 255f);
                pr[i] = c.r * (1f / 255f) * a; pg[i] = c.g * (1f / 255f) * a; pb[i] = c.b * (1f / 255f) * a; pa[i] = a;
            }
            BoxBlur1(pr, W, H, rad); BoxBlur1(pg, W, H, rad); BoxBlur1(pb, W, H, rad); BoxBlur1(pa, W, H, rad);
            const float interiorThreshold = 0.05f;   // "already had meaningful alpha" cutoff
            for (int i = 0; i < n; i++)
            {
                var c = buf[i];
                float origA = c.a * (1f / 255f);
                float newA = Mathf.Lerp(origA, pa[i], amt);
                if (newA <= 0.001f) { buf[i] = default; continue; }
                if (origA > interiorThreshold)
                {
                    // Already visible here — keep the ORIGINAL colour untouched (whatever gradient/noise/fill
                    // detail it had), only alpha itself may have softened.
                    buf[i] = new Color32(c.r, c.g, c.b, ToByte(newA));
                }
                else
                {
                    // Extending alpha into previously near-empty space — borrow a colour from the premultiplied
                    // blur average (avoids the black-fringe bug a straight-alpha blur would produce here).
                    float rr = pa[i] > 0.001f ? pr[i] / pa[i] : 0f;
                    float gg = pa[i] > 0.001f ? pg[i] / pa[i] : 0f;
                    float bb = pa[i] > 0.001f ? pb[i] / pa[i] : 0f;
                    buf[i] = new Color32(ToByte(rr), ToByte(gg), ToByte(bb), ToByte(newA));
                }
            }
        }

        // Separable two-pass box blur on one channel, in place — same structure as BloomModifier's own BoxBlur3,
        // just one channel at a time (this modifier blurs 4 independent channels rather than 3 interleaved ones).
        static void BoxBlur1(float[] v, int W, int H, int R)
        {
            var tmp = new float[v.Length];
            float inv = 1f / (2 * R + 1);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float s = 0f;
                    for (int dx = -R; dx <= R; dx++) { int xx = Mathf.Clamp(x + dx, 0, W - 1); s += v[y * W + xx]; }
                    tmp[y * W + x] = s * inv;
                }
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    float s = 0f;
                    for (int dy = -R; dy <= R; dy++) { int yy = Mathf.Clamp(y + dy, 0, H - 1); s += tmp[yy * W + x]; }
                    v[y * W + x] = s * inv;
                }
        }
    }

    /// One discrete swirl centre for CurlModifier — position in canvas-centre pixels (the same space as
    /// MetaOrb/PinDot), authored by clicking the preview like MetaBlob's orbs / Pin warp's pins. Unlike the
    /// ambient curl-noise below (organic, all-over churn), a vortex is a clean localized whirlpool: everything
    /// within Radius spins around Pos, strongest at the centre, smoothly fading to nothing at the edge.
    [Serializable]
    public class VortexPoint
    {
        public Vector2 pos;
        [Range(2f, 32f)]
        [Tooltip("Zone of influence, in pixels — the swirl fades smoothly to nothing at this distance from the " +
                 "vortex's own centre. Animatable — e.g. grow the zone of influence over life.")]
        public ZUIValue radius = new ZUIValue(24f);
        [Range(0f, 50f)]
        [Tooltip("Swirl strength, in degrees — how far a pixel at the vortex's own centre rotates per full blast " +
                 "loop at Speed 1. Sweet spot is roughly 15-40 — below ~15 barely reads, above ~40 tends to over- " +
                 "rotate/tear rather than read as a tighter whirlpool. Animatable.")]
        public ZUIValue strength = new ZUIValue(25f);
        [Range(-4f, 4f)]
        [Tooltip("How fast this vortex's rotation accumulates over the blast's loop, relative to Strength's " +
                 "per-loop baseline — 2 = twice as fast (overshoots Strength and keeps going), 0 = no rotation " +
                 "at all (effectively disables this vortex without removing it). Animatable. Used by the regular " +
                 "Curl modifier; ignored by Vortex field (progress), which uses Progress below instead.")]
        public ZUIValue speed = new ZUIValue(1f);
        [Range(0f, 1f)]
        [Tooltip("Direct control over how far this vortex has rotated — 0 = no rotation, 1 = full Strength " +
                 "applied, beyond 1 over-rotates past it, negative reverses direction. Curve it to ease in, " +
                 "hold, pulse, or reverse — independent of the blast's own life fraction. Used by the standalone " +
                 "Vortex field (progress) modifier; ignored by the regular Curl modifier, which uses Speed above " +
                 "instead. Default is a straight 0→1 ramp over life, matching Curl's own default (Speed 1).")]
        public ZUIValue progress = Sfx.CurveVal(1f, 0f, 0f, 1f, 1f);
        [Tooltip("Spin direction: on = clockwise, off = counter-clockwise.")]
        public bool clockwise = false;

        public VortexPoint Clone() => new VortexPoint
        {
            pos = pos,
            radius = Sfx.CloneVal(radius),
            strength = Sfx.CloneVal(strength),
            speed = Sfx.CloneVal(speed),
            progress = Sfx.CloneVal(progress),
            clockwise = clockwise,
        };
    }

    /// Shared by any modifier that authors a list of placeable VortexPoints in the preview (click to add, drag
    /// to move, wire-circle + direction-tick gizmo) — lets PyreWindow's authoring code (built for CurlModifier)
    /// work for any other vortex-driven modifier too, without duplicating that click/drag/gizmo logic per modifier.
    public interface IVortexHost
    {
        List<VortexPoint> Vortices { get; }
    }

    /// Curl noise: fakes coherent, fluid-like swirl without an actual fluid simulation (which would need
    /// per-frame ADVECTED state carried across the bake — a poor fit for Pyre's fully deterministic, compute-
    /// any-frame-directly model). Two stackable layers: an AMBIENT domain-warped curl field (organic, all-over
    /// churn — a fireball's roll, a mushroom cloud's turning cap) plus discrete, placeable VORTICES (clean
    /// localized whirlpools with their own position/size/speed/strength). The shape this modifier sits on stays
    /// the alpha mask throughout — Curl only ever displaces WHERE texture is sampled from, never what's visible
    /// outside the shape's own silhouette (same as every other GeometryModifier).
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class CurlModifier : GeometryModifier, IVortexHost
    {
        [Range(0f, 24f)]
        [Tooltip("Ambient swirl displacement, in pixels — 0 = no ambient churn, just the vortices below (if any). Animatable.")]
        public ZUIValue strength = new ZUIValue(4f);
        [Range(1f, 32f)]
        [Tooltip("Ambient swirl noise frequency — bigger = larger, slower-looking eddies; smaller = fine, busy churn. Animatable.")]
        public ZUIValue zoom = new ZUIValue(24f);
        [Range(-4f, 4f)]
        [Tooltip("How fast the ambient swirl's own flow field evolves over the blast's loop — 0 = a static " +
                 "(non-animated) bend. Animatable.")]
        public ZUIValue speed = new ZUIValue(1f);
        [HideInInspector] public float warp = 0.6f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0f, 2f)]
        [Tooltip("Domain-warp strength on the underlying noise (0 = smooth eddies, higher = more churned/organic).")]
        public ZUIValue warpValue = new ZUIValue(0.6f);
        [HideInInspector] public bool warpUpgraded;
        public ZUIValue Warp { get { if (!warpUpgraded) { warpValue = new ZUIValue(warp); warpUpgraded = true; } return warpValue; } }

        [Tooltip("Discrete swirl centres, layered on top of the ambient swirl above — each spins everything " +
                 "within its own Radius around its own Pos. Add one via the box below (click the preview), " +
                 "like Pin warp's pins.")]
        public List<VortexPoint> vortices = new List<VortexPoint>();
        List<VortexPoint> IVortexHost.Vortices => vortices;

        // One vortex's ZUIValues resolved for the current frame — Prepare() does this once per frame for every
        // vortex (however many there are), not per-pixel, since Eval() itself (curve sampling / hashing) is
        // too costly to redo per-pixel. A plain struct, not VortexPoint itself, so the authored ZUIValues stay
        // untouched and only the resolved floats live here.
        struct ResolvedVortex { public Vector2 pos; public float radius, strength, speed; public bool clockwise; }
        readonly List<ResolvedVortex> resolved = new List<ResolvedVortex>();

        float amt, zm, spd, wrp;
        public override string DisplayName => "Curl (swirl)";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amt = e(strength, 0);
            zm = Mathf.Max(1f, e(zoom, 1));
            spd = e(speed, 2);
            // fid 3 overlaps the vortex-shared ids 3-7 below — benign: the fid only seeds a param's Min-Max RNG
            // stream, and this modifier's own 8-wide block (0-7) is fully occupied either way.
            wrp = Mathf.Clamp(e(Warp, 3), 0f, 2f);

            resolved.Clear();
            if (vortices == null) return;
            for (int i = 0; i < vortices.Count; i++)
            {
                var v = vortices[i];
                if (v == null) continue;
                // Field ids 3-7 (5 slots) are shared/wrapped across all vortices, not one unique id per vortex
                // per field — this modifier's own budget within BlastRenderer's per-modifier id spacing is only
                // 8 wide (ids 0-7), same as every other modifier, and a vortex list is unbounded. In practice
                // this only matters for MinMax mode (the wrapped id is what keeps that mode's per-field random
                // roll independent) — Static/Curve modes (the common case here) are unaffected either way, so
                // two vortices occasionally sharing a MinMax roll is a minor, acceptable tradeoff, not a
                // visible bug.
                int baseId = 3 + (i * 3) % 5;
                resolved.Add(new ResolvedVortex
                {
                    pos = v.pos,
                    radius = Mathf.Max(0.5f, e(v.radius, baseId)),
                    strength = e(v.strength, baseId + 1),
                    speed = e(v.speed, (baseId + 2) % 8),
                    clockwise = v.clockwise,
                });
            }
        }

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            Vector2 result = off;

            if (Mathf.Abs(amt) > 0.01f)
            {
                Vector2 d = result - ctx.center;
                float nx = d.x / zm + phase * spd;
                float ny = d.y / zm;
                // Per-shape-stable seed derived from the shape's own centre (Turbulence's own pattern), so
                // different scattered shapes churn with different, still-deterministic flow fields instead of
                // an identical repeated swirl.
                int seed = unchecked((Mathf.RoundToInt(ctx.center.x * 8f) * 92821) ^ (Mathf.RoundToInt(ctx.center.y * 8f) * 68111));
                Vector2 curl = PyreNoise.Curl(nx, ny, seed, wrp);
                result += curl * amt;
            }

            for (int i = 0; i < resolved.Count; i++)
            {
                var v = resolved[i];
                Vector2 d = result - v.pos;
                float dist = d.magnitude;
                float r = v.radius;
                if (dist >= r) continue;
                float w = 1f - dist / r; w = w * w * (3f - 2f * w);   // smoothstep falloff: 1 at centre -> 0 at radius
                float dirSign = v.clockwise ? -1f : 1f;
                float ang = dirSign * v.strength * Mathf.Deg2Rad * w * phase * v.speed;
                if (Mathf.Abs(ang) < 0.0001f) continue;
                // Inverse warp: rotate BACKWARD by the angle this vortex would have spun the pixel FORWARD by.
                float c = Mathf.Cos(-ang), s = Mathf.Sin(-ang);
                result = v.pos + new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
            }

            return result;
        }

        // Deep-copy the vortex list (PyreModifier.Clone's reflection pass only clones ZUIValue/Gradient/curve
        // fields, not custom list types) so a duplicated modifier gets its own vortices, same as PinWarp's dots.
        public override PyreModifier Clone()
        {
            var m = (CurlModifier)base.Clone();
            m.vortices = vortices != null ? vortices.ConvertAll(v => v?.Clone() ?? new VortexPoint()) : new List<VortexPoint>();
            return m;
        }
    }

    /// Standalone sibling to CurlModifier's vortices, added non-invasively (CurlModifier itself is untouched —
    /// this is a separate modifier you add alongside or instead of it) so the two driving models can be compared
    /// directly: instead of a vortex's rotation accumulating as Strength × life-phase × Speed (CurlModifier),
    /// here it's `Strength × Progress` where Progress is authored DIRECTLY as its own MultiCont value — 0 = no
    /// rotation, 1 = Strength fully applied, with no automatic tie to how far into its life the blast is. That
    /// decouples "how much has this vortex wound up" from "how much time has passed," so a Curve can ease in,
    /// hold at a plateau, pulse, or even reverse, independent of the blast's own life fraction (something Speed
    /// alone can't do cleanly, since Speed only ever scales a straight phase ramp). No ambient churn here — pair
    /// with CurlModifier's own ambient Strength/Zoom (set its own vortices' Strength to 0) if you want both.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class CurlProgressModifier : GeometryModifier, IVortexHost
    {
        [Tooltip("Discrete swirl centres — each spins everything within its own Radius around its own Pos, " +
                 "driven by Progress rather than Speed × life-phase. Add one via the box below (click the " +
                 "preview), like Curl's own vortices.")]
        public List<VortexPoint> vortices = new List<VortexPoint>();
        List<VortexPoint> IVortexHost.Vortices => vortices;

        struct ResolvedVortex { public Vector2 pos; public float radius, strength, progress; public bool clockwise; }
        readonly List<ResolvedVortex> resolved = new List<ResolvedVortex>();

        public override string DisplayName => "Vortex field (progress)";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            resolved.Clear();
            if (vortices == null) return;
            for (int i = 0; i < vortices.Count; i++)
            {
                var v = vortices[i];
                if (v == null) continue;
                // Same wrapped field-id scheme as CurlModifier's own vortices (see its Prepare for the full
                // rationale) — 5 slots (ids 3-7) shared/wrapped across an unbounded vortex list.
                int baseId = 3 + (i * 3) % 5;
                resolved.Add(new ResolvedVortex
                {
                    pos = v.pos,
                    radius = Mathf.Max(0.5f, e(v.radius, baseId)),
                    strength = e(v.strength, baseId + 1),
                    progress = e(v.progress, (baseId + 2) % 8),
                    clockwise = v.clockwise,
                });
            }
        }

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            Vector2 result = off;
            for (int i = 0; i < resolved.Count; i++)
            {
                var v = resolved[i];
                Vector2 d = result - v.pos;
                float dist = d.magnitude;
                float r = v.radius;
                if (dist >= r) continue;
                float w = 1f - dist / r; w = w * w * (3f - 2f * w);   // smoothstep falloff: 1 at centre -> 0 at radius
                float dirSign = v.clockwise ? -1f : 1f;
                float ang = dirSign * v.strength * Mathf.Deg2Rad * w * v.progress;
                if (Mathf.Abs(ang) < 0.0001f) continue;
                // Inverse warp: rotate BACKWARD by the angle this vortex would have spun the pixel FORWARD by.
                float c = Mathf.Cos(-ang), s = Mathf.Sin(-ang);
                result = v.pos + new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
            }
            return result;
        }

        // Deep-copy the vortex list, same reasoning as CurlModifier.Clone().
        public override PyreModifier Clone()
        {
            var m = (CurlProgressModifier)base.Clone();
            m.vortices = vortices != null ? vortices.ConvertAll(v => v?.Clone() ?? new VortexPoint()) : new List<VortexPoint>();
            return m;
        }
    }

    /// Fakes volumetric depth on a flat shape by remapping the radial sample position as if it were painted
    /// on an orthographically-viewed SPHERE (the standard "sphere impostor" projection). A unit sphere's depth
    /// at normalised radius r (0 = centre/pole, facing the viewer head-on; 1 = the silhouette edge, tangent to
    /// the view) is z = sqrt(1-r²); walking the sphere's own SURFACE ARC-LENGTH from the pole instead of the
    /// flat screen radius gives r' = asin(r)/(π/2) — near-identity at the centre (little foreshortening,
    /// facing you) but its slope races toward infinity as r→1, so texture detail crowds together toward the
    /// silhouette exactly like a real sphere's grazing-angle foreshortening: bulged/magnified at the centre,
    /// compressed toward the rim. Composes with Curl (swirl the flow, THEN bulge the result over the implied
    /// sphere) or any spatial fill (Fill/Flow fill/Noise fill — bulges the colour gradient itself). Radius
    /// always tracks the SHAPE's own current radius (ctx.radius, including its own animated Size), so the
    /// bulge automatically stays sized to a growing/shrinking fireball with no separate radius to keep in sync.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class SphereModifier : GeometryModifier
    {
        [Range(-5f, 5f)]
        [Tooltip("0 = no distortion (flat); 1 = the physically-correct sphere projection; beyond 1 exaggerates " +
                 "past it for a more extreme fisheye. Negative is a genuine MIRROR of the positive side (a true " +
                 "concave dimple, not just a smaller/bigger shape) — -1 is exactly as strong/characterful as +1, " +
                 "just pushed the other way. Animatable — e.g. ease the depth in as the shape matures.")]
        public ZUIValue strength = new ZUIValue(1f);
        [Range(-32f, 32f)]
        [Tooltip("Offsets the lens's own centre from the shape's centre, in pixels — so the fisheye/dimple " +
                 "doesn't have to sit dead-centre. Animatable.")]
        public ZUIValue originX = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Offsets the lens's own centre vertically, in pixels. Animatable.")]
        public ZUIValue originY = new ZUIValue(0f);
        [Range(0f, 32f)]
        [Tooltip("The lens's own radius, in pixels — how far the effect reaches before fading back to identity " +
                 "at its own edge. 0 (default) = auto, matching the shape's own current radius (ctx.radius) — " +
                 "same as before this field existed. A smaller radius makes a tight fisheye bubble that doesn't " +
                 "have to fill the whole shape; a larger one lets the effect extend past the shape's own edge. " +
                 "Animatable.")]
        public ZUIValue radius = new ZUIValue(0f);

        float amt, originXPx, originYPx, radiusOverride;
        public override string DisplayName => "Sphere (fake depth)";
        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            amt = e(strength, 0);
            originXPx = e(originX, 1);
            originYPx = e(originY, 2);
            radiusOverride = e(radius, 3);
        }

        public override Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)
        {
            float effRadius = radiusOverride > 0.001f ? radiusOverride : ctx.radius;
            if (Mathf.Abs(amt) < 0.001f || effRadius <= 0.001f) return off;
            Vector2 effCenter = ctx.center + new Vector2(originXPx, originYPx);
            Vector2 d = off - effCenter;
            float r = d.magnitude / effRadius;
            if (r <= 0.0001f) return off;
            float rClamped = Mathf.Clamp01(r);
            // Convex (bulge/fisheye, amt >= 0): ratio = rSphere/r, always in (2/π, 1] for r in (0,1] — 2/π is
            // asin(r)/r's own limit as r->0. Concave (dimple, amt < 0): the ACTUAL inverse function of rSphere
            // (rSphere(x) = asin(x)/(π/2), so its inverse is sin(x·π/2)), not just 1/(the convex ratio) — an
            // earlier version used the SAME convex ratio with a negative exponent, which only inverts that
            // ratio's own narrow (2/π, 1] range and stays close to 1 for any modest negative Strength, reading
            // as barely more than a plain size-scale (confirmed: at Strength -1 the shape only varied about 22%
            // corner-to-corner, vs +1's ~82% — nowhere near a comparable mirror). Using the TRUE inverse function
            // instead gives a ratio with the same order of dynamic range as the convex side, so -1 now reads as
            // genuinely as strong/characterful as +1, just concave instead of convex.
            float ratio = amt >= 0f
                ? (rClamped > 0.001f ? (Mathf.Asin(rClamped) / (Mathf.PI * 0.5f)) / rClamped : (2f / Mathf.PI))
                : (rClamped > 0.001f ? Mathf.Sin(rClamped * Mathf.PI * 0.5f) / rClamped : (Mathf.PI * 0.5f));
            // Exponentiating by |amt| (rather than linearly extrapolating the RADIUS past the amt=±1 curve)
            // keeps the whole family well-behaved for ANY amt: 0 -> ratio^0=1 -> identity; ±1 -> ratio^1=ratio ->
            // the (convex or concave) physical projection; beyond ±1 -> smoothly MORE extreme, but ratio^mag can
            // never overshoot past 0 or flip sign (Pow of a positive base is always positive). r=1 (the shape's
            // own edge) always has ratio=1 on EITHER branch (asin(1)/(π/2)=1, sin(π/2)=1), so the edge never
            // moves, for any Strength, either direction.
            float scale = Mathf.Pow(Mathf.Max(0.0001f, ratio), Mathf.Abs(amt));
            return effCenter + d * scale;
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class SmudgeModifier : GeometryModifier
    {
        [Tooltip("The painted smear strokes. Paint each in the preview; they all grow in parallel driven by Grow.")]
        public List<SmudgeStroke> strokes = new List<SmudgeStroke>();
        [Range(1f, 32f)]
        [Tooltip("Brush radius — half the smear WIDTH, in pixels. Pixels this far from a stroke are dragged. Animatable.")]
        public ZUIValue size = new ZUIValue(12f);
        [Range(0f, 32f)]
        [Tooltip("How far paint is dragged ALONG a stroke, in pixels. Animatable.")]
        public ZUIValue strength = new ZUIValue(12f);
        [Range(0f, 1f)]
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

        static ZUIValue DefaultGrow() => Sfx.CurveVal(1f, 0f, 0f, 1f, 1f);   // draw the smear out over life
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
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
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
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
    /// How a Kaleidoscope's arms relate to each other.
    public enum KaleidoMode
    {
        Rotate,   // every arm is the same image, turned — a pinwheel
        Mirror,   // alternate arms are reflected — true kaleidoscope symmetry, seams meet
        Vary,     // same image, but each arm gets its own seeded turn, flip and scale — organic, not symmetric
    }

    /// Kaleidoscope — repeat this layer into N arms around the centre.
    ///
    /// Deliberately a POST modifier, working on the layer's finished pixels, because that is the only place
    /// that is universal: Pyre's existing `star`/`spreadCount` does radial repeat too, but it lives inside the
    /// SCATTER path, so MetaBlob, Height balls and Fire never reach it. Operating on pixels means every shape
    /// gets this, including ones not written yet.
    ///
    /// The honest limitation of that choice is Vary. A truly independent arm would mean re-generating the
    /// layer with a different seed per arm, which no post modifier can do — it only ever sees one finished
    /// image. So Vary gives each arm its own seeded rotation offset, mirror flip and scale instead. That reads
    /// as "these arms are related but not identical", which is the intent, but it is NOT N independent
    /// simulations and should not be described as such.
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]
    public class KaleidoscopeModifier : PostModifier
    {
        public override string DisplayName => "Kaleidoscope";

        [Tooltip("How the arms relate. Rotate = the same image turned (a pinwheel). Mirror = alternate arms " +
                 "reflected, so neighbouring arms meet at a seam (true kaleidoscope symmetry). Vary = each arm " +
                 "gets its own seeded turn, flip and scale, so they read as related but not identical.")]
        public KaleidoMode mode = KaleidoMode.Mirror;

        [Min(1)]
        [Tooltip("How many arms radiate from the centre. 1 leaves the layer untouched.")]
        public int arms = 4;

        [Range(0f, 360f)]
        [Tooltip("Total arc the arms span, in degrees. 360 = evenly around the full circle; less bunches them " +
                 "into a fan. Animatable — sweep a fan open.")]
        public ZUIValue arcDegrees = new ZUIValue(360f);

        [Range(-360f, 360f)]
        [Tooltip("Turn the whole arrangement. Animatable — spin the kaleidoscope.")]
        public ZUIValue rotationDegrees = new ZUIValue(0f);

        [Range(0f, 1f)]
        [Tooltip("Vary only: how much each arm may differ, 0 = identical to Rotate, 1 = strongly varied " +
                 "(its own turn, flip and scale).")]
        public ZUIValue variation = new ZUIValue(0.5f);

        [Tooltip("Keep the ORIGINAL image as well as the arms. Off = the arms replace it, which is what you " +
                 "want when arm 0 already is the original.")]
        public bool keepOriginal = false;

        float arc, rot, vary;

        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            arc = e(arcDegrees, 0);
            rot = e(rotationDegrees, 1);
            vary = Mathf.Clamp01(e(variation, 2));
        }

        // Deterministic per-arm jitter: a pure function of (seed, arm), so scrubbing and baking agree.
        static float Hash01(int s, int arm, int salt)
        {
            unchecked
            {
                uint h = (uint)(s * 374761393 + arm * 668265263 + salt * 2246822519);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            int n = Mathf.Max(1, arms);
            if (n == 1) return;

            var src = (Color32[])buf.Clone();
            if (!keepOriginal)
                for (int i = 0; i < buf.Length; i++) buf[i] = new Color32(0, 0, 0, 0);

            float cx = W * 0.5f, cy = H * 0.5f;
            // Arms span `arc`, so a 360 arc puts them evenly around the circle and a smaller one fans them.
            float step = (Mathf.Abs(arc) < 0.001f ? 0f : arc / n) * Mathf.Deg2Rad;
            float baseRot = rot * Mathf.Deg2Rad;

            for (int arm = 0; arm < n; arm++)
            {
                float ang = baseRot + step * arm;
                bool flip = mode == KaleidoMode.Mirror && (arm & 1) == 1;
                float scale = 1f;

                if (mode == KaleidoMode.Vary)
                {
                    // Each arm's own turn (up to half an arm-step either way, so arms stay in their sectors),
                    // its own coin-flip mirror, and its own scale.
                    ang += (Hash01(seed, arm, 1) - 0.5f) * step * vary;
                    flip = Hash01(seed, arm, 2) < 0.5f * vary;
                    scale = 1f + (Hash01(seed, arm, 3) - 0.5f) * 0.5f * vary;
                }

                // Sample the SOURCE by rotating each destination pixel backwards — a gather, so no destination
                // pixel is ever left unwritten the way a scatter (rotate-and-splat) would leave holes.
                float cos = Mathf.Cos(-ang), sin = Mathf.Sin(-ang);
                float inv = scale > 0.001f ? 1f / scale : 1f;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float dx = (x + 0.5f - cx) * inv, dy = (y + 0.5f - cy) * inv;
                        float sx = dx * cos - dy * sin;
                        float sy = dx * sin + dy * cos;
                        if (flip) sx = -sx;
                        int ix = Mathf.FloorToInt(sx + cx), iy = Mathf.FloorToInt(sy + cy);
                        if (ix < 0 || iy < 0 || ix >= W || iy >= H) continue;
                        var c = src[iy * W + ix];
                        if (c.a == 0) continue;
                        int di = y * W + x;
                        var d = buf[di];
                        // Source-over, so overlapping arms stack rather than the last one winning.
                        float sa = c.a * (1f / 255f), da = d.a * (1f / 255f);
                        float outA = sa + da * (1f - sa);
                        if (outA <= 0.0001f) continue;
                        buf[di] = new Color32(
                            (byte)((c.r * sa + d.r * da * (1f - sa)) / outA),
                            (byte)((c.g * sa + d.g * da * (1f - sa)) / outA),
                            (byte)((c.b * sa + d.b * da * (1f - sa)) / outA),
                            ToByte(outA));
                    }
            }
        }
    }
}
