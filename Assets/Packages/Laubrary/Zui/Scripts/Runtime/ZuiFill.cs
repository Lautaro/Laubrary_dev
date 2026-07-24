// ZuiFill.cs
// A configurable colour-or-fill: a single solid colour, or one of several gradient-driven fills —
// over-life (the gradient sampled by a 0..1 lifetime clock), or a SPATIAL gradient (linear / radial)
// sampled by the consumer's local (u,v) point — OR a TEXTURE. A texture (sprite / noise / grid / dots)
// is a SEPARATE group that REPLACES the fill mode entirely: it is *instead of* a fill, never a *kind* of
// fill, so nothing here is ever recursive (a texture never re-samples a ZuiFill). Any colour tool that only
// ever needed a swatch can adopt this and gain "make it a gradient / a texture" for free; a ZuiFill left in
// Solid mode with no texture IS just a colour picker (with alpha, everywhere).
//
// Runtime-safe (no editor deps) and DETERMINISTIC — the Noise texture uses an internal FNV-hash value noise,
// never UnityEngine.Random — so the same (life, u, v) always yields the same colour on every machine and
// every frame. The Sprite texture reads the sprite's own pixels: still pure — the texture content is an INPUT
// (like the seed), memoized in a per-texture cache; a non-readable/missing texture falls back to `color`.
// Authored by ZuiFillControl (editor, Z.Fill); evaluated here at play/bake time.
//
// Idiom mirrors ZUIValue (global namespace, [Serializable], a pure Evaluate) — but ZuiFill's fields are
// public: it is plain paint data with no clamping or external-resolver machinery to hide behind properties.

using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ZuiFill
{
    // The FILL modes — a flat colour, a gradient over life, or a SPATIAL gradient (linear / radial). Noise is
    // NOT a mode any more: it moved into the Texture group below (a texture, not a fill). Removing it is free —
    // no PyrePlus/ZuiFill assets exist to migrate.
    public enum Mode { Solid, OverLife, Linear, Radial }

    // The TEXTURE group. When `texture` != None it REPLACES the fill mode entirely (Evaluate dispatches into the
    // texture path and the Mode is ignored). This is deliberately NOT a Mode value — a texture is *instead of* a
    // fill, so it can never nest a ZuiFill inside itself, keeping evaluation strictly non-recursive.
    public enum TextureKind { None, Sprite, Noise, Grid, Dots }

    // The Noise texture's shape. Value = the plain FNV value noise (v1's Noise). Ridged = 1−|2t−1| (creased
    // ridges). Steps = floor(t·4)/3 (a 4-band posterization). All three then map through `gradient`.
    public enum NoiseKind { Value, Ridged, Steps }

    // Where a SPATIAL fill (Linear / Radial) or a TEXTURE (sprite / noise / grid / dots) is ANCHORED — i.e. the
    // coordinate space the CONSUMER feeds its (u,v) in. Stamped (the default) = the shape's OWN local coords, so
    // the pattern rotates / spins / travels WITH the shape (stamped onto it). Fixed = canvas-anchored coords, so
    // the shape moves THROUGH a stationary pattern that stays put on the canvas (mask-like — the shape becomes a
    // window onto a fixed backdrop). This is plain paint DATA: ZuiFill never sees the coordinates, so the consumer
    // decides what "local" vs "canvas" mean and passes the right (u,v). A non-spatial fill (Solid / OverLife)
    // ignores it entirely (Evaluate never reads (u,v) in those modes).
    public enum FillSpace { Stamped, Fixed }

    // ── fill ──────────────────────────────────────────────────────────────────────────
    public Mode mode = Mode.Solid;
    public Color color = Color.white;   // Solid — alpha-capable like every mode; also the Sprite tint / Grid+Dots ink
    public Gradient gradient;           // every non-Solid mode + the Noise texture (alpha comes from the gradient)
    public float angleDeg = 0f;         // Linear: rotation of the fill axis, in degrees
    [Min(0.05f)] public float zoom = 1f; // Linear (proj scale) + Radial + Noise: spatial scale (higher zooms in)
    // Gradient centre in the shape's local -1..1 space (Linear + Radial + Noise). Linear: the fill axis passes
    // THROUGH this point (the projection is of (uv − center)). Radial: the gradient's middle sits here, so it
    // drifts off-centre toward a border. Default (0,0) is an exact no-op reproducing v1's arithmetic byte-for-byte.
    public Vector2 center = Vector2.zero;
    // Coordinate space for the spatial modes (Linear / Radial) AND every texture (see FillSpace). Default Stamped
    // reproduces v1 exactly — the consumer feeds shape-local (u,v). Fixed asks the consumer to feed canvas-anchored
    // (u,v) instead, so the pattern stays put while the shape moves through it. Solid / OverLife ignore it.
    public FillSpace space = FillSpace.Stamped;

    // ── texture (replaces the fill when != None) ────────────────────────────────────────
    public TextureKind texture = TextureKind.None;
    // Sprite: point-sampled across the local -1..1 box (respecting sprite.rect), tinted × `color` (alpha too).
    public Sprite textureSprite;
    // Noise: the FNV value noise, shaped by noiseKind, mapped through `gradient`; `zoom` + `center` move the sample.
    public NoiseKind noiseKind = NoiseKind.Value;
    // Grid: a rotated line grid. spacing = cell size in local units; lineWidth = line thickness as a fraction of
    // spacing; V/H pick which axes draw lines. Ink = `color`; off-line pixels are transparent (alpha carries the mask).
    public float gridAngle = 0f;
    public float gridSpacing = 0.5f;
    public float gridLineWidth = 0.08f;
    public bool gridVertical = true;
    public bool gridHorizontal = true;
    // Dots: a (rotated, reusing gridAngle) cell grid with a disc at each cell centre. size = disc diameter as a
    // fraction of the cell; spacing = cell size in local units; stagger offsets alternate rows half a cell. Ink =
    // `color`; the gaps are transparent.
    public float dotSize = 0.5f;
    public float dotSpacing = 0.35f;
    public bool dotStagger = true;

    public ZuiFill() { }
    public ZuiFill(Color c) { color = c; }

    /// <summary>
    /// Pure, deterministic evaluation of the fill's colour at a point.
    /// <paramref name="life"/> is a 0..1 over-life clock; <paramref name="u"/>/<paramref name="v"/> are the
    /// consumer's normalized local point, -1..1 across the filled shape. When <see cref="texture"/> != None the
    /// fill mode is IGNORED and the texture path runs instead (never recursive). Any non-Solid mode / the Noise
    /// texture with a null gradient falls back to <see cref="color"/>, and a null/unreadable sprite falls back to
    /// <see cref="color"/> too, so a half-configured fill never renders empty.
    /// </summary>
    public Color Evaluate(float life, float u, float v)
    {
        if (texture != TextureKind.None) return EvaluateTexture(u, v);

        switch (mode)
        {
            case Mode.Solid:
                return color;

            case Mode.OverLife:
                return gradient != null ? gradient.Evaluate(Mathf.Clamp01(life)) : color;

            case Mode.Linear:
            {
                if (gradient == null) return color;
                float rad = angleDeg * Mathf.Deg2Rad;
                // BYTE-IDENTITY GATE: at centre (0,0) & zoom 1 run v1's arithmetic verbatim. (The general form
                // below is already bitwise-identical there — subtracting 0f and multiplying by 1f are exact IEEE
                // identities — but the explicit guard removes all doubt for the orchestrator's hash check.)
                if (center.x == 0f && center.y == 0f && zoom == 1f)
                {
                    float proj0 = u * Mathf.Cos(rad) + v * Mathf.Sin(rad);
                    return gradient.Evaluate(Mathf.Clamp01((proj0 + 1f) * 0.5f));
                }
                // The axis passes through `center`; zoom scales the projection before the [-1,1]→[0,1] remap.
                float proj = (u - center.x) * Mathf.Cos(rad) + (v - center.y) * Mathf.Sin(rad);
                float t = Mathf.Clamp01((proj * zoom + 1f) * 0.5f);
                return gradient.Evaluate(t);
            }

            case Mode.Radial:
            {
                if (gradient == null) return color;
                // BYTE-IDENTITY GATE: at centre (0,0) run v1's arithmetic verbatim (zoom already applied in v1).
                if (center.x == 0f && center.y == 0f)
                {
                    float r0 = Mathf.Sqrt(u * u + v * v) * Mathf.Max(0.05f, zoom);
                    return gradient.Evaluate(Mathf.Clamp01(r0));
                }
                // Distance is measured FROM `center`, so the gradient's middle drifts off-centre toward a border.
                float du = u - center.x, dv = v - center.y;
                float r = Mathf.Sqrt(du * du + dv * dv) * Mathf.Max(0.05f, zoom);
                return gradient.Evaluate(Mathf.Clamp01(r));
            }

            default:
                return color;
        }
    }

    // ── texture dispatch (never recursive — a texture never samples a ZuiFill) ──────────────
    Color EvaluateTexture(float u, float v)
    {
        switch (texture)
        {
            case TextureKind.Sprite: return EvaluateSprite(u, v);
            case TextureKind.Noise:  return EvaluateNoise(u, v);
            case TextureKind.Grid:   return EvaluateGrid(u, v);
            case TextureKind.Dots:   return EvaluateDots(u, v);
            default:                 return color;
        }
    }

    // Sprite: point-sample the sprite's pixels mapped across the local -1..1 box, honouring sprite.rect, tinted
    // × color (alpha multiplies). Non-readable / missing sprite ⇒ fall back to `color` (like a null gradient).
    Color EvaluateSprite(float u, float v)
    {
        if (textureSprite == null) return color;
        var tex = textureSprite.texture;
        if (tex == null) return color;
        Color32[] px = GetTexturePixels(tex);
        if (px == null) return color;   // non-readable — cached null, so we never re-throw per pixel

        int tw = tex.width, th = tex.height;
        if (tw <= 0 || th <= 0) return color;

        Rect rect = textureSprite.rect;
        int rx = Mathf.Clamp(Mathf.RoundToInt(rect.x), 0, tw - 1);
        int ry = Mathf.Clamp(Mathf.RoundToInt(rect.y), 0, th - 1);
        int rw = Mathf.Clamp(Mathf.RoundToInt(rect.width), 1, tw - rx);
        int rh = Mathf.Clamp(Mathf.RoundToInt(rect.height), 1, th - ry);

        // -1..1 → 0..1 → a sub-rect pixel. v-up matches GetPixels32's bottom-up rows, so +v is the image top.
        float su = Mathf.Clamp01((u + 1f) * 0.5f);
        float sv = Mathf.Clamp01((v + 1f) * 0.5f);
        int lx = Mathf.Clamp((int)(su * rw), 0, rw - 1);
        int ly = Mathf.Clamp((int)(sv * rh), 0, rh - 1);

        Color c = px[(ry + ly) * tw + (rx + lx)];
        return new Color(c.r * color.r, c.g * color.g, c.b * color.b, c.a * color.a);
    }

    // Noise: the FNV value noise (shaped by noiseKind) mapped through `gradient`. zoom + center move the sample
    // coords, exactly like v1's Noise mode did (which this texture replaces). Null gradient ⇒ fall back to color.
    Color EvaluateNoise(float u, float v)
    {
        if (gradient == null) return color;
        float z = Mathf.Max(0.05f, zoom);
        float n = ValueNoise2Octave((u - center.x) * z * 3f, (v - center.y) * z * 3f);
        switch (noiseKind)
        {
            case NoiseKind.Ridged: n = 1f - Mathf.Abs(2f * n - 1f); break;
            case NoiseKind.Steps:  n = Mathf.Floor(n * 4f) / 3f; break;
            // Value: n unchanged (byte-identical to v1's value noise).
        }
        return gradient.Evaluate(Mathf.Clamp01(n));
    }

    // Grid: work in CELL SPACE — rotate (u,v)−center by −gridAngle, then divide by spacing so 1 unit = 1 cell.
    // A line sits at every integer cell coordinate; a pixel is inked by how close it is to the nearest such line
    // on each enabled axis. Ink = color; the alpha carries the mask (off-line = transparent), with a proper
    // edge-smoothstep soft rim. Combine the enabled axes by max; both axes off ⇒ mask 0.
    //   Worked check (the case that used to render flat): spacing 0.3, lineWidth 0.18 → halfW 0.09,
    //   soft 0.1·0.09+0.02 = 0.029. At a CELL CENTRE the line distance d = 0.5, far past halfW+soft = 0.119, so
    //   Smoothstep01(0.09, 0.119, 0.5) = 1 → mask = 1−1 = 0 (a transparent gap). On a line d≈0 → mask ≈ 1.
    Color EvaluateGrid(float u, float v)
    {
        float rad = -gridAngle * Mathf.Deg2Rad;
        float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
        float du = u - center.x, dv = v - center.y;
        float sp = Mathf.Max(0.02f, gridSpacing);
        float px = (du * cs - dv * sn) / sp;   // cell-space coords (1 unit = 1 cell)
        float py = (du * sn + dv * cs) / sp;

        float halfW = gridLineWidth * 0.5f;    // line half-width, in CELL units (lineWidth = fraction of a cell)
        float soft = 0.1f * halfW + 0.02f;     // soft edge band, also cell units

        float mask = 0f;
        if (gridVertical)   mask = Mathf.Max(mask, 1f - Smoothstep01(halfW, halfW + soft, LineDist(px)));
        if (gridHorizontal) mask = Mathf.Max(mask, 1f - Smoothstep01(halfW, halfW + soft, LineDist(py)));
        return new Color(color.r, color.g, color.b, color.a * mask);
    }

    // Dots: CELL SPACE (as Grid, reusing gridAngle) with a disc at each cell centre. Distance is measured from the
    // cell centre (frac−0.5 per axis) in cell units; a pixel is inked when it's inside the disc radius. Staggered
    // rows shift x by half a cell on odd rows. Ink = color; the gaps are transparent, with an edge-smoothstep rim.
    //   Worked check: spacing 0.22, size 0.65 → r 0.325, soft 0.15·0.325+0.02 = 0.069. At a cell centre d = 0 →
    //   Smoothstep01(0.256, 0.325, 0) = 0 → mask = 1 (a dot); at a cell corner d = √0.5 ≈ 0.707 ≫ r → mask 0.
    Color EvaluateDots(float u, float v)
    {
        float rad = -gridAngle * Mathf.Deg2Rad;
        float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
        float du = u - center.x, dv = v - center.y;
        float sp = Mathf.Max(0.02f, dotSpacing);
        float px = (du * cs - dv * sn) / sp;   // cell-space coords
        float py = (du * sn + dv * cs) / sp;

        float r = Mathf.Clamp01(dotSize) * 0.5f;   // disc radius, cell units (dotSize = diameter as a fraction of the cell)
        if (r <= 0f) return new Color(color.r, color.g, color.b, 0f);   // no disc → fully transparent

        // Staggered rows shift x by half a cell on odd rows (row index = floor(py)).
        if (dotStagger && (Mathf.Abs(Mathf.Floor(py)) % 2f) >= 1f) px += 0.5f;

        float dx = Frac(px) - 0.5f;   // offset from the cell centre, cell units
        float dy = Frac(py) - 0.5f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        float soft = 0.15f * r + 0.02f;
        float mask = 1f - Smoothstep01(r - soft, r, d);
        return new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(mask));
    }

    // Distance to the nearest integer grid line, in cell units (0 on a line, up to 0.5 mid-cell). frac handles negatives.
    static float LineDist(float x) { float f = Frac(x); return Mathf.Min(f, 1f - f); }

    // A REAL edge smoothstep (GLSL semantics): 0 below edge0, 1 above edge1, a smooth Hermite ramp between. This is
    // NOT UnityEngine.Mathf.SmoothStep — that one clamps its 3rd arg to [0,1] and returns a smoothed LERP between
    // `from` and `to`, so using it as a threshold (the v1 bug) left the mask hovering near the tiny edge values
    // and every pixel read ≈ opaque, i.e. a flat solid fill.
    static float Smoothstep01(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0) return x < edge0 ? 0f : 1f;   // degenerate band → a hard step
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    static float Frac(float x) => x - Mathf.Floor(x);

    /// <summary>Give this fill a gradient if it lacks one — so a freshly-switched non-Solid mode (or a Noise
    /// texture) has something to show (mirrors ZUIValue.EnsureCurveDefaults). Never overwrites an existing gradient.</summary>
    public void EnsureGradient()
    {
        if (gradient == null) gradient = DefaultGradient();
    }

    /// <summary>A flat white→white gradient (alpha 1). Switching into a gradient mode with this seeded means
    /// the field is never blank, and a flat gradient reads identically to a white swatch until the user edits it.</summary>
    public static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    // ── Sprite pixel cache (purity via memoization) ──────────────────────────────────────
    // Texture content is an INPUT to the fill (like a seed), so caching each texture's pixels once is a pure
    // memoization — identical output for identical inputs. A non-readable texture throws from GetPixels32; we
    // catch it and cache NULL so we never re-throw per pixel (and Evaluate falls back to `color`). Keyed by the
    // Texture2D instance; the cache lives for the domain's lifetime (cleared on a script reload, which also
    // re-reads any texture whose readability changed).
    static readonly Dictionary<Texture2D, Color32[]> _texCache = new Dictionary<Texture2D, Color32[]>();

    static Color32[] GetTexturePixels(Texture2D tex)
    {
        if (tex == null) return null;
        if (_texCache.TryGetValue(tex, out var cached)) return cached;
        Color32[] px;
        try { px = tex.GetPixels32(); }
        catch { px = null; }   // non-readable (no Read/Write) — cache the null so we stop retrying
        _texCache[tex] = px;
        return px;
    }

    // ── deterministic value noise (FNV-1a hashed lattice, bilinear + smoothstep, 2 octaves) ─────────────
    // Pure static math, no UnityEngine.Random — identical output for identical inputs, always.

    static float ValueNoise2Octave(float x, float y)
    {
        float a = ValueNoise(x, y);
        // Second octave: doubled frequency, offset so the two layers don't align, half amplitude.
        float b = ValueNoise(x * 2f + 31.7f, y * 2f + 17.3f);
        return (a + b * 0.5f) / 1.5f;   // normalize by total amplitude → stays in [0,1]
    }

    static float ValueNoise(float x, float y)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float fx = x - x0;
        float fy = y - y0;
        // Smoothstep fades so lattice cells blend without visible creasing.
        float sx = fx * fx * (3f - 2f * fx);
        float sy = fy * fy * (3f - 2f * fy);

        float n00 = Hash01(x0, y0);
        float n10 = Hash01(x0 + 1, y0);
        float n01 = Hash01(x0, y0 + 1);
        float n11 = Hash01(x0 + 1, y0 + 1);

        float nx0 = Mathf.Lerp(n00, n10, sx);
        float nx1 = Mathf.Lerp(n01, n11, sx);
        return Mathf.Lerp(nx0, nx1, sy);
    }

    /// <summary>FNV-1a hash of a lattice coordinate → a well-mixed value in [0,1).</summary>
    static float Hash01(int x, int y)
    {
        unchecked
        {
            const uint FnvOffset = 2166136261u;
            const uint FnvPrime = 16777619u;
            uint h = FnvOffset;
            h = (h ^ (uint)x) * FnvPrime;
            h = (h ^ (uint)y) * FnvPrime;
            // Extra avalanche so adjacent cells don't correlate.
            h ^= h >> 15; h *= 2246822519u;
            h ^= h >> 13; h *= 3266489917u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0x1000000;   // 24-bit → [0,1)
        }
    }
}
