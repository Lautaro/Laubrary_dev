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
public class ZuiFill : ISerializationCallbackReceiver
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

    // How a SPATIAL fill's box is normalized into the -1..1 (u,v) Evaluate expects — i.e. WHERE the ramp's far
    // end lands on a box that is not square. This is the answer to "why do I only ever see part of my gradient".
    //
    //   Uniform  ONE divisor for both axes: the box's LARGER half-extent. A Radial fill therefore stays a true
    //            CIRCLE, and a Linear axis keeps its aspect. The cost is that the SHORT axis never reaches +-1,
    //            so a ramp laid across it is only partly used — on a text line 5.6x wider than it is tall, a
    //            vertical gradient shows the middle 18% of the ramp and nothing else.
    //   Stretch  Each axis divided by its OWN half-extent, so both span exactly -1..1 and the ramp always runs
    //            end to end whichever way it points. A Radial fill becomes an ELLIPSE fitted to the box, which
    //            is usually what you want when the box is the subject (text) and rarely what you want when the
    //            circle is the subject (a disc, a blast).
    //
    // Uniform is the default because it is what every existing asset already renders as. Like FillSpace this is
    // plain paint DATA: ZuiFill never sees the box, so the CONSUMER normalizes — call Normalize below rather
    // than dividing by hand, or the choice silently does nothing in that tool.
    public enum FillFit { Uniform, Stretch }

    // ── fill ──────────────────────────────────────────────────────────────────────────
    public Mode mode = Mode.Solid;
    public Color color = Color.white;   // Solid — alpha-capable like every mode; also the Sprite tint / Grid+Dots ink
    public Gradient gradient;           // legacy base + frozen migration source (see gradientAnim)
    // ── animatable gradient companion (ZuiGradient migration) ───────────────────────────────
    // Wraps the legacy `gradient` above (now the frozen SOURCE + the BASE) with ZuiGradient's non-destructive
    // transforms (reverse / hue / sat / brightness / contrast / quantise / cycle). [SerializeReference] so NULL =
    // "not yet migrated" (an old asset loads it null; OnAfterDeserialize seeds { gradient = legacy }, all transforms
    // default ⇒ ZuiGradient.Evaluate(t,0) == legacy.Evaluate(Clamp01(t)) ⇒ BYTE-IDENTICAL). A null companion (an
    // in-memory fill not yet seeded) falls back to the legacy gradient in EvalGrad, so it renders identically either
    // way. The editor edits gradientAnim.gradient going forward; the legacy scalar stays the frozen source.
    [SerializeReference] public ZuiGradient gradientAnim;
    public float angleDeg = 0f;         // Linear: rotation of the fill axis, in degrees
    // LEGACY spatial scale (Linear proj scale + Radial + Noise). KEPT as the serialized migration SOURCE: an old
    // asset's authored zoom loads here, and OnAfterDeserialize seeds `zoomAnim` from it (see below). Evaluate never
    // reads this directly any more — it reads the animatable companion, whose Static value == this exactly for a
    // migrated fill (⇒ byte-identical). Renaming/removing it would DROP the old data, so it stays, same name/type.
    // NOTE THE SEMANTICS, which changed at fillVersion 1: this is now a SIZE (how far the pattern spreads), not a
    // frequency. Bigger = a bigger gradient, which is what the dial always claimed and never did — the maths used
    // to multiply by it, so raising "zoom" made the pattern SMALLER. Evaluate now takes its reciprocal (see
    // SpatialFrequency) and a one-time migration inverted every stored value, so existing assets render
    // identically. The FIELD NAME is deliberately unchanged: Unity matches serialized data by name, and renaming
    // it would silently drop every authored value in every consumer project.
    [Min(0.05f)] public float zoom = 1f;
    // LEGACY gradient centre in the shape's local -1..1 space (Linear + Radial + Noise + Grid + Dots). Same role as
    // `zoom` above: the serialized migration source, seeded into centerXAnim/centerYAnim on load, then never read
    // directly by Evaluate. Linear: the fill axis passes THROUGH this point (projection of (uv − center)). Radial:
    // the gradient's middle sits here, drifting off-centre toward a border. Default (0,0) reproduces v1 byte-for-byte.
    public Vector2 center = Vector2.zero;

    // ── animatable spatial companions (task #64) ────────────────────────────────────────
    // zoom + centre, promoted to ZUIValue so an author can CURVE them over the fill's 0..1 life (like every other
    // ZUIValue in the fill). These are the values Evaluate actually reads. SerializeReference so a NULL is a real
    // "not yet migrated" sentinel Unity preserves (a plain [Serializable] field would deserialise to a non-null
    // default, indistinguishable from an authored Static(1)); an OLD asset predating these fields loads them NULL,
    // and OnAfterDeserialize SEEDS them as Static(legacy) ⇒ each evaluates to the exact legacy value ⇒ byte-identical.
    // A brand-new fill defaults zoom=1 / centre=(0,0) ⇒ seeds Static(1) / Static(0). Should a companion ever null out
    // (the SerializeReference-broken-assembly hazard), OnAfterDeserialize RE-SEEDS it from the durable legacy scalar,
    // and Evaluate's EvalCompanion falls back to that scalar too — so a nulled companion still renders the legacy value.
    [SerializeReference] public ZUIValue zoomAnim;
    [SerializeReference] public ZUIValue centerXAnim;
    [SerializeReference] public ZUIValue centerYAnim;
    // Coordinate space for the spatial modes (Linear / Radial) AND every texture (see FillSpace). Default Stamped
    // reproduces v1 exactly — the consumer feeds shape-local (u,v). Fixed asks the consumer to feed canvas-anchored
    // (u,v) instead, so the pattern stays put while the shape moves through it. Solid / OverLife ignore it.
    public FillSpace space = FillSpace.Stamped;
    // How a non-square box maps onto -1..1 (see FillFit). Uniform reproduces v1 exactly. Solid / OverLife ignore
    // it, and so does any consumer whose sampling box is square (there is nothing to choose between).
    public FillFit fit = FillFit.Uniform;

    /// <summary>Normalize a point into the -1..1 (u,v) <see cref="Evaluate"/> expects, honouring <see cref="fit"/>.
    ///
    /// Every consumer of a spatial fill should call THIS rather than dividing by hand — the divisor IS the
    /// setting, so a tool that rolls its own normalization silently ignores the dial. (That is exactly how the
    /// codebase ended up with two conventions: TextSplash divided by the larger half-extent, PyrePlus's
    /// background divided per axis, and neither was a choice anyone could see or make.)</summary>
    /// <param name="p">The point, in the same units as <paramref name="half"/>.</param>
    /// <param name="origin">The box's centre.</param>
    /// <param name="half">The box's half-extents.</param>
    public Vector2 Normalize(Vector2 p, Vector2 origin, Vector2 half)
    {
        Vector2 d = p - origin;
        if (fit == FillFit.Stretch)
            return new Vector2(d.x / Mathf.Max(0.0001f, half.x), d.y / Mathf.Max(0.0001f, half.y));

        // One divisor for both axes keeps a Radial fill a circle instead of an ellipse stretched to the box.
        float s = Mathf.Max(0.0001f, Mathf.Max(half.x, half.y));
        return new Vector2(d.x / s, d.y / s);
    }

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
        if (texture != TextureKind.None) return EvaluateTexture(life, u, v);

        switch (mode)
        {
            case Mode.Solid:
                return color;

            case Mode.OverLife:
                return HasGrad ? EvalGrad(Mathf.Clamp01(life), life) : color;

            case Mode.Linear:
            {
                if (!HasGrad) return color;
                float rad = angleDeg * Mathf.Deg2Rad;
                // The animatable companions, evaluated at THIS life. Static ⇒ the exact legacy value (byte-identical);
                // Curve ⇒ animates over life. `z`/`cx`/`cy` sit exactly where v1 read `zoom`/`center.x`/`center.y`.
                float z = SpatialFrequency(life);   // reciprocal of the authored SIZE
                float cx = EvalCompanion(centerXAnim, life, center.x);
                float cy = EvalCompanion(centerYAnim, life, center.y);
                // BYTE-IDENTITY GATE: at centre (0,0) & zoom 1 run v1's arithmetic verbatim. (The general form
                // below is already bitwise-identical there — subtracting 0f and multiplying by 1f are exact IEEE
                // identities — but the explicit guard removes all doubt for the orchestrator's hash check.)
                if (cx == 0f && cy == 0f && z == 1f)
                {
                    float proj0 = u * Mathf.Cos(rad) + v * Mathf.Sin(rad);
                    return EvalGrad(Mathf.Clamp01((proj0 + 1f) * 0.5f), life);
                }
                // The axis passes through the centre; zoom scales the projection before the [-1,1]→[0,1] remap.
                float proj = (u - cx) * Mathf.Cos(rad) + (v - cy) * Mathf.Sin(rad);
                float t = Mathf.Clamp01((proj * z + 1f) * 0.5f);
                return EvalGrad(t, life);
            }

            case Mode.Radial:
            {
                if (!HasGrad) return color;
                float z = SpatialFrequency(life);   // reciprocal of the authored SIZE
                float cx = EvalCompanion(centerXAnim, life, center.x);
                float cy = EvalCompanion(centerYAnim, life, center.y);
                // BYTE-IDENTITY GATE: at centre (0,0) run v1's arithmetic verbatim (zoom already applied in v1).
                if (cx == 0f && cy == 0f)
                {
                    float r0 = Mathf.Sqrt(u * u + v * v) * z;
                    return EvalGrad(Mathf.Clamp01(r0), life);
                }
                // Distance is measured FROM the centre, so the gradient's middle drifts off-centre toward a border.
                float du = u - cx, dv = v - cy;
                float r = Mathf.Sqrt(du * du + dv * dv) * z;
                return EvalGrad(Mathf.Clamp01(r), life);
            }

            default:
                return color;
        }
    }

    // ── gradient companion access (byte-identical to the pre-migration `gradient` path) ─────
    /// <summary>True when there's a gradient to sample — the ZuiGradient companion's base if seeded, else the
    /// legacy gradient (an in-memory fill whose companion hasn't been seeded yet).</summary>
    bool HasGrad => (gradientAnim != null ? gradientAnim.gradient : gradient) != null;

    /// <summary>Sample the effective gradient at <paramref name="t"/>: the ZuiGradient companion (transforms
    /// applied, phase 0) if seeded, else the legacy gradient directly. At default transforms
    /// ZuiGradient.Evaluate(t,0) == gradient.Evaluate(Clamp01(t)); every caller already clamps t ⇒ byte-identical.</summary>
    // `life` threads to the gradient companion so its animatable colour transforms (hue/sat/brightness/contrast)
    // sample over the shape's life. Static transforms ignore it ⇒ a migrated fill stays byte-identical.
    Color EvalGrad(float t, float life) => gradientAnim != null ? gradientAnim.Evaluate(t, 0f, life) : gradient.Evaluate(Mathf.Clamp01(t));

    // ── texture dispatch (never recursive — a texture never samples a ZuiFill) ──────────────
    // `life` threads through to the kinds that read the animatable centre/zoom companions (Noise reads both;
    // Grid + Dots read centre). Sprite reads neither, so it ignores `life`.
    Color EvaluateTexture(float life, float u, float v)
    {
        switch (texture)
        {
            case TextureKind.Sprite: return EvaluateSprite(u, v);
            case TextureKind.Noise:  return EvaluateNoise(life, u, v);
            case TextureKind.Grid:   return EvaluateGrid(life, u, v);
            case TextureKind.Dots:   return EvaluateDots(life, u, v);
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
    Color EvaluateNoise(float life, float u, float v)
    {
        if (!HasGrad) return color;
        float z = SpatialFrequency(life);
        float cx = EvalCompanion(centerXAnim, life, center.x);
        float cy = EvalCompanion(centerYAnim, life, center.y);
        float n = ValueNoise2Octave((u - cx) * z * 3f, (v - cy) * z * 3f);
        switch (noiseKind)
        {
            case NoiseKind.Ridged: n = 1f - Mathf.Abs(2f * n - 1f); break;
            case NoiseKind.Steps:  n = Mathf.Floor(n * 4f) / 3f; break;
            // Value: n unchanged (byte-identical to v1's value noise).
        }
        return EvalGrad(Mathf.Clamp01(n), life);
    }

    // Grid: work in CELL SPACE — rotate (u,v)−center by −gridAngle, then divide by spacing so 1 unit = 1 cell.
    // A line sits at every integer cell coordinate; a pixel is inked by how close it is to the nearest such line
    // on each enabled axis. Ink = color; the alpha carries the mask (off-line = transparent), with a proper
    // edge-smoothstep soft rim. Combine the enabled axes by max; both axes off ⇒ mask 0.
    //   Worked check (the case that used to render flat): spacing 0.3, lineWidth 0.18 → halfW 0.09,
    //   soft 0.1·0.09+0.02 = 0.029. At a CELL CENTRE the line distance d = 0.5, far past halfW+soft = 0.119, so
    //   Smoothstep01(0.09, 0.119, 0.5) = 1 → mask = 1−1 = 0 (a transparent gap). On a line d≈0 → mask ≈ 1.
    Color EvaluateGrid(float life, float u, float v)
    {
        float rad = -gridAngle * Mathf.Deg2Rad;
        float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
        float du = u - EvalCompanion(centerXAnim, life, center.x), dv = v - EvalCompanion(centerYAnim, life, center.y);
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
    Color EvaluateDots(float life, float u, float v)
    {
        float rad = -gridAngle * Mathf.Deg2Rad;
        float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
        float du = u - EvalCompanion(centerXAnim, life, center.x), dv = v - EvalCompanion(centerYAnim, life, center.y);
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

    // ── animatable spatial-companion migration (zoom / centre → ZUIValue) ────────────────
    /// <summary>Seed the animatable zoom/centre companions from the legacy scalar fields when they are missing
    /// (an old asset predating them deserialises the [SerializeReference] fields as NULL; an in-memory fill built
    /// by an object initializer likewise has null companions). A Static seed reproduces the legacy value EXACTLY
    /// ⇒ byte-identical. Idempotent and never overwrites an already-authored companion. Called from
    /// OnAfterDeserialize (so a loaded/round-tripped fill is ready and persists its companions) and by the editor
    /// control before it binds them; Evaluate does NOT depend on it — EvalCompanion falls back to the legacy scalar
    /// when a companion is still null, so the render is correct even before seeding runs.</summary>
    public void EnsureSpatialAnim()
    {
        if (zoomAnim == null)    zoomAnim    = new ZUIValue(zoom);
        if (centerXAnim == null) centerXAnim = new ZUIValue(center.x);
        if (centerYAnim == null) centerYAnim = new ZUIValue(center.y);
    }

    /// <summary>Seed the ZuiGradient companion from the legacy gradient when missing (the migration). Default
    /// transforms ⇒ byte-identical. Only seeds when there's a gradient to wrap (a Solid fill keeps it null).
    /// Idempotent; never overwrites an authored companion.</summary>
    public void EnsureGradientAnim()
    {
        if (gradientAnim == null && gradient != null) gradientAnim = new ZuiGradient { gradient = gradient };
    }

    // ISerializationCallbackReceiver: seed the companions on load (the migration). No Unity API is touched here, so
    // it is safe on Unity's deserialize thread. OnBeforeSerialize intentionally does nothing — the legacy scalars are
    // the frozen migration SOURCE, never written back (a Curve companion can't collapse to a scalar without loss).
    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        // BEFORE the companions are seeded: the seed copies the legacy scalar, so inverting afterwards would
        // either miss the copy or invert it twice.
        MigrateZoomToSize();
        EnsureSpatialAnim();
        EnsureGradientAnim();
    }

    public const int CurrentFillVersion = 1;

    // Which generation of this fill's data is on disk. Initialized to CURRENT so a fill created in code is born
    // up to date; an asset written before the field existed has no entry for it and deserializes as 0, which is
    // exactly how the migration below tells "old data" from "new data". (Same mechanism as SplashPixelation's
    // version — a C# initializer never reaches data already on disk, which is the whole point.)
    [SerializeField, HideInInspector] int fillVersion = CurrentFillVersion;

    /// <summary>v0 → v1: `zoom` stopped being a FREQUENCY and became a SIZE, so every stored value is inverted
    /// once and the renderer takes the reciprocal. Net effect on an existing asset: nothing — it renders exactly
    /// as before, and the dial finally moves the way its name says.
    ///
    /// A Curve companion is inverted key by key, which is exact AT the keys and approximate between them (the
    /// reciprocal of a lerp is not the lerp of the reciprocals). That is the honest cost of the change and it is
    /// reported, because a silently reshaped animation curve is worse than a warned one.</summary>
    void MigrateZoomToSize()
    {
        if (fillVersion >= CurrentFillVersion) return;
        fillVersion = CurrentFillVersion;

        zoom = Invert(zoom);
        if (zoomAnim == null) return;

        switch (zoomAnim.mode)
        {
            case ZUIValue.Mode.Static:
                zoomAnim.staticValue = Invert(zoomAnim.staticValue);
                break;

            case ZUIValue.Mode.MinMax:
            {
                // Inverting flips the ORDER, so the bounds swap to stay min <= max.
                float lo = Invert(zoomAnim.max), hi = Invert(zoomAnim.min);
                zoomAnim.min = lo; zoomAnim.max = hi;
                break;
            }

            case ZUIValue.Mode.Curve:
                if (zoomAnim.points != null)
                    for (int i = 0; i < zoomAnim.points.Count; i++)
                    {
                        var p = zoomAnim.points[i];
                        p.value = Invert(p.value);
                        zoomAnim.points[i] = p;
                    }
                break;
        }
    }

    static float Invert(float v) => 1f / Mathf.Max(0.0001f, v);

    /// <summary>Where the gradient's CENTRE actually sits, in the same -1..1 space Normalize produces, with the
    /// animatable companions resolved at <paramref name="life"/>. Public so an editor preview can MARK it: the
    /// centre dial is the placement control users misread most, because moving the gradient one way makes a fixed
    /// subject appear to shift the other, and a marker showing where the centre landed is what tells the two
    /// apart.</summary>
    public Vector2 CenterAt(float life)
        => new Vector2(EvalCompanion(centerXAnim, life, center.x),
                       EvalCompanion(centerYAnim, life, center.y));

    /// <summary>The reciprocal of the authored SIZE — what the projection/radius maths actually multiplies by.
    /// One helper so the Linear, Radial and Noise paths cannot disagree about the conversion or the floor.</summary>
    float SpatialFrequency(float life)
        => 1f / Mathf.Max(0.05f, EvalCompanion(zoomAnim, life, zoom));

    /// <summary>Evaluate an animatable spatial companion (zoom / centre) over the 0..1 <paramref name="life"/> clock.
    /// Mirrors PyrePlusRenderer.Eval's over-life convention (which this fill is sampled through): Static returns its
    /// constant (⇒ a migrated fill is byte-identical), Curve samples its points DIRECTLY at clamped life (NOT via
    /// duration/warmup — that is ZUIValue's runtime-seconds API, which would sweep only the curve's first quarter).
    /// A null companion (not-yet-seeded in-memory fill) returns <paramref name="fallback"/> (the legacy scalar), so a
    /// pristine fill renders the legacy value whether or not the companion has been seeded yet. MinMax has no per-pixel
    /// seed here — a per-pixel random would shatter a spatial fill — so it holds its stable midpoint.</summary>
    static float EvalCompanion(ZUIValue v, float life, float fallback)
    {
        if (v == null) return fallback;
        switch (v.mode)
        {
            case ZUIValue.Mode.Static: return v.staticValue;
            case ZUIValue.Mode.Curve:  return ZUIEnvelopeEvaluator.Evaluate(v.points, Mathf.Clamp01(life), v.yMax);
            case ZUIValue.Mode.MinMax: return (v.min + v.max) * 0.5f;
            default:                   return v.staticValue;
        }
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
