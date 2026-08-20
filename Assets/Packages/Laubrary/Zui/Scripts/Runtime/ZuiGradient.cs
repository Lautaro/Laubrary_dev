// ZuiGradient.cs
// Unity's Gradient with the things it lacks: NON-DESTRUCTIVE transform knobs applied at sample time
// (reverse, hue shift, saturation, brightness, contrast), a QUANTISE (fewer discrete bands), and a
// PHASE (the hook for colour cycling). Every knob is a dial you can turn back — the base Gradient is
// never rewritten. ToLut() bakes the result to a 1-D texture for the palette-cycle shader.
//
// The four COLOUR transforms (hue / saturation / brightness / contrast) are ANIMATABLE (ZUIValue /
// MultiCont): each can be a Static constant, a Min-Max spread, or a Curve over the 0..1 life clock — so
// e.g. brightness can pulse or hue can drift over a particle's life. This mirrors ZuiFill's zoom/centre
// migration EXACTLY: the legacy float fields are the frozen serialized SOURCE, a [SerializeReference]
// ZUIValue companion is seeded Static(legacy) on load, and Evaluate reads the companion — a Static
// companion evaluates to the legacy constant, so a migrated/default gradient is BYTE-IDENTICAL. A null
// companion (an in-memory gradient not yet seeded) falls back to the legacy float, so it renders correctly
// either way. Position transforms (reverse / quantise / phase) stay plain fields — they shape the sample
// POSITION, and an animated band count / reverse reads as flicker, not motion.
//
// Runtime-safe, deterministic, global-namespace [Serializable] — the same idiom as ZuiFill / ZUIValue.

using System;
using UnityEngine;

[Serializable]
public class ZuiGradient : ISerializationCallbackReceiver
{
    [Tooltip("The base gradient. The transforms below are applied on top at sample time — the base is never rewritten.")]
    public Gradient gradient = DefaultGradient();

    // ── position transforms (shape the sample POSITION; plain fields — not animated) ──
    [Tooltip("Sample the gradient backwards (1-t).")]
    public bool reverse = false;
    [Min(0), Tooltip("Snap the sample position to N discrete bands (0 = smooth). The gradient equivalent of Posterize.")]
    public int quantiseSteps = 0;

    // ── colour transforms — LEGACY floats: the frozen serialized SOURCE + the fallback for a null companion ──
    [Range(-1f, 1f), Tooltip("Rotate the hue of the whole ramp. ±1 = ±180°.")]
    public float hueShift = 0f;
    [Range(0f, 2f), Tooltip("Multiply saturation across the whole ramp (1 = unchanged).")]
    public float saturation = 1f;
    [Range(0f, 2f), Tooltip("Multiply brightness (value) across the whole ramp (1 = unchanged).")]
    public float brightness = 1f;
    [Range(0f, 2f), Tooltip("Contrast around mid-grey (1 = unchanged).")]
    public float contrast = 1f;

    // ── animatable colour-transform companions (ZUIValue / MultiCont) — the values Evaluate actually reads ──
    // [SerializeReference] so NULL is a real "not yet migrated" sentinel Unity preserves; an old asset loads them
    // null and OnAfterDeserialize seeds each Static(legacy) ⇒ byte-identical. EvalTransform falls back to the legacy
    // float when a companion is still null, so a pristine in-memory gradient renders correctly before seeding runs.
    [SerializeReference] public ZUIValue hueShiftAnim;
    [SerializeReference] public ZUIValue saturationAnim;
    [SerializeReference] public ZUIValue brightnessAnim;
    [SerializeReference] public ZUIValue contrastAnim;
    // PHASE — a manual SCROLL of the ramp (0..1), animatable over life: the basic "move the gradient" control that
    // works with no shader. 0 = no offset (byte-identical default). A rising Curve over life scrolls the ramp
    // through once; the shader/cycle driver's own phase (Evaluate's `phase` arg) ADDS on top of this authored one.
    [SerializeReference] public ZUIValue phaseAnim;

    // ── cycling (authored default cadence; a driver on the target turns time into phase) ──
    [Tooltip("This ramp wants to colour-cycle. A ZuiPaletteCycle driver advances the phase; Evaluate/ToLut take it as a parameter.")]
    public bool cycle = false;
    [Tooltip("Cycle speed (phase units per second) the driver should use by default.")]
    public float cycleSpeed = 0.5f;

    /// <summary>The transformed colour at position <paramref name="t"/> (0..1), offset by <paramref name="phase"/>
    /// (the cycle), with the colour transforms sampled at 0..1 <paramref name="life"/>. Non-destructive: reads the
    /// base gradient and applies the knobs. A Static/default gradient at life 0 is byte-identical to the pre-migration
    /// path.</summary>
    public Color Evaluate(float t, float phase = 0f, float life = 0f)
    {
        float u = reverse ? 1f - t : t;
        // The authored scroll (animatable over life) plus any external cycle phase the caller passed.
        float ph = phase + EvalTransform(phaseAnim, life, 0f);
        // With a scroll, MIRROR the ramp (append its reverse: 0→1→0) via PingPong so it loops SEAMLESSLY — the
        // colour at the wrap matches instead of jumping from the end back to the start. No offset ⇒ Clamp01 (a
        // plain Evaluate(1) must stay at the ramp END; PingPong/Repeat(1,1) would fold/wrap it to 0), so the
        // default (ph==0) is byte-identical to the pre-phase path.
        u = ph != 0f ? Mathf.PingPong(u + ph, 1f) : Mathf.Clamp01(u);
        if (quantiseSteps > 0)
            u = Mathf.Floor(u * quantiseSteps) / Mathf.Max(1, quantiseSteps - 1);
        u = Mathf.Clamp01(u);

        Color c = gradient != null ? gradient.Evaluate(u) : Color.white;

        // The animatable colour transforms, evaluated at THIS life. Static ⇒ the exact legacy constant (byte-identical);
        // Curve ⇒ animates over life. A null companion falls back to the legacy float — same value again.
        float hueShiftV    = EvalTransform(hueShiftAnim,    life, hueShift);
        float saturationV  = EvalTransform(saturationAnim,  life, saturation);
        float brightnessV  = EvalTransform(brightnessAnim,  life, brightness);
        float contrastV    = EvalTransform(contrastAnim,    life, contrast);

        if (hueShiftV != 0f || saturationV != 1f || brightnessV != 1f)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + hueShiftV * 0.5f, 1f);   // ±1 → ±180°
            s = Mathf.Clamp01(s * saturationV);
            v = Mathf.Clamp01(v * brightnessV);
            float a = c.a;
            c = Color.HSVToRGB(h, s, v); c.a = a;
        }
        if (contrastV != 1f)
        {
            c.r = Mathf.Clamp01((c.r - 0.5f) * contrastV + 0.5f);
            c.g = Mathf.Clamp01((c.g - 0.5f) * contrastV + 0.5f);
            c.b = Mathf.Clamp01((c.b - 0.5f) * contrastV + 0.5f);
        }
        return c;
    }

    /// <summary>Bake the transformed ramp to a 1-D LUT texture (for the palette-cycle shader, and for the editor's
    /// objective preview). Repeat-wrap + bilinear so a shader's frac(t+phase) cycles smoothly. Bake at phase 0; the
    /// shader scrolls the phase. The colour transforms are sampled at <paramref name="life"/> (0 = a life-0
    /// snapshot, the default) — UNLESS <paramref name="lifeFollowsPosition"/> is set, in which case each texel's
    /// `life` tracks its own ramp position instead of the fixed <paramref name="life"/>. That matches an OverLife
    /// ZuiFill, where the ramp position IS the life (see ZuiFill.EvalGrad) — without this, a texel-independent life
    /// would sample every colour-transform (hue/sat/BRIGHTNESS/contrast) at the SAME instant, so an authored Curve
    /// would bake as one flat multiplier smeared across the whole strip instead of the per-position value it will
    /// actually render — reading as "the transform does nothing, it just recolours the gradient" (task T-0030). The
    /// caller owns the returned texture.</summary>
    public Texture2D ToLut(int width = 256, float life = 0f, bool lifeFollowsPosition = false)
    {
        width = Mathf.Max(2, width);
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = "ZuiGradient LUT",
        };
        var px = new Color[width];
        for (int i = 0; i < width; i++)
        {
            float t = i / (float)(width - 1);
            px[i] = Evaluate(t, 0f, lifeFollowsPosition ? t : life);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>Seed the animatable colour-transform companions from the legacy float fields when missing (the
    /// migration). A Static seed reproduces the legacy constant EXACTLY ⇒ byte-identical. Idempotent; never
    /// overwrites an authored companion. Called from OnAfterDeserialize and by the editor before it binds them;
    /// Evaluate does NOT depend on it — EvalTransform falls back to the legacy float when a companion is null.</summary>
    public void EnsureTransformAnim()
    {
        if (hueShiftAnim == null)   hueShiftAnim   = new ZUIValue(hueShift);
        if (saturationAnim == null) saturationAnim = new ZUIValue(saturation);
        if (brightnessAnim == null) brightnessAnim = new ZUIValue(brightness);
        if (contrastAnim == null)   contrastAnim   = new ZUIValue(contrast);
        if (phaseAnim == null)      phaseAnim      = new ZUIValue(0f);   // no scroll by default
    }

    // ISerializationCallbackReceiver: seed the companions on load (the migration). No Unity API is touched here,
    // so it is safe on Unity's deserialize thread. OnBeforeSerialize does nothing — the legacy floats are the frozen
    // migration SOURCE, never written back (a Curve companion can't collapse to a scalar without loss).
    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize() => EnsureTransformAnim();

    /// <summary>Evaluate an animatable colour transform over the 0..1 <paramref name="life"/> clock. Mirrors
    /// ZuiFill.EvalCompanion: Static ⇒ its constant (a migrated transform is byte-identical), Curve ⇒ its points
    /// sampled directly at clamped life, MinMax ⇒ its stable midpoint (a per-eval random would shimmer a shared
    /// gradient), null ⇒ <paramref name="fallback"/> (the legacy float).</summary>
    static float EvalTransform(ZUIValue v, float life, float fallback)
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

    static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
