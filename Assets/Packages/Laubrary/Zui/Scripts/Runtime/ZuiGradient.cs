// ZuiGradient.cs
// Unity's Gradient with the things it lacks: NON-DESTRUCTIVE transform knobs applied at sample time
// (reverse, hue shift, saturation, brightness, contrast), a QUANTISE (fewer discrete bands), and a
// PHASE (the hook for colour cycling). Every knob is a dial you can turn back — the base Gradient is
// never rewritten. ToLut() bakes the result to a 1-D texture for the palette-cycle shader.
//
// Runtime-safe, deterministic, global-namespace [Serializable] — the same idiom as ZuiFill / ZUIValue.

using System;
using UnityEngine;

[Serializable]
public class ZuiGradient
{
    [Tooltip("The base gradient. The transforms below are applied on top at sample time — the base is never rewritten.")]
    public Gradient gradient = DefaultGradient();

    // ── transforms (all no-op at their defaults ⇒ Evaluate == the raw base gradient) ──
    [Tooltip("Sample the gradient backwards (1-t).")]
    public bool reverse = false;
    [Range(-1f, 1f), Tooltip("Rotate the hue of the whole ramp. ±1 = ±180°.")]
    public float hueShift = 0f;
    [Range(0f, 2f), Tooltip("Multiply saturation across the whole ramp (1 = unchanged).")]
    public float saturation = 1f;
    [Range(0f, 2f), Tooltip("Multiply brightness (value) across the whole ramp (1 = unchanged).")]
    public float brightness = 1f;
    [Range(0f, 2f), Tooltip("Contrast around mid-grey (1 = unchanged).")]
    public float contrast = 1f;
    [Min(0), Tooltip("Snap the sample position to N discrete bands (0 = smooth). The gradient equivalent of Posterize.")]
    public int quantiseSteps = 0;

    // ── cycling (authored default cadence; a driver on the target turns time into phase) ──
    [Tooltip("This ramp wants to colour-cycle. A ZuiPaletteCycle driver advances the phase; Evaluate/ToLut take it as a parameter.")]
    public bool cycle = false;
    [Tooltip("Cycle speed (phase units per second) the driver should use by default.")]
    public float cycleSpeed = 0.5f;

    /// <summary>The transformed colour at position <paramref name="t"/> (0..1), offset by <paramref name="phase"/>
    /// (the cycle). Non-destructive: reads the base gradient and applies the knobs.</summary>
    public Color Evaluate(float t, float phase = 0f)
    {
        float u = reverse ? 1f - t : t;
        // Wrap only when cycling — a plain Evaluate(1) must stay at the ramp END, not wrap to the start
        // (Mathf.Repeat(1,1)==0). The shader does its own frac(t+phase), so ToLut bakes the un-wrapped ramp.
        u = phase != 0f ? Mathf.Repeat(u + phase, 1f) : Mathf.Clamp01(u);
        if (quantiseSteps > 0)
            u = Mathf.Floor(u * quantiseSteps) / Mathf.Max(1, quantiseSteps - 1);
        u = Mathf.Clamp01(u);

        Color c = gradient != null ? gradient.Evaluate(u) : Color.white;

        if (hueShift != 0f || saturation != 1f || brightness != 1f)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + hueShift * 0.5f, 1f);   // ±1 → ±180°
            s = Mathf.Clamp01(s * saturation);
            v = Mathf.Clamp01(v * brightness);
            float a = c.a;
            c = Color.HSVToRGB(h, s, v); c.a = a;
        }
        if (contrast != 1f)
        {
            c.r = Mathf.Clamp01((c.r - 0.5f) * contrast + 0.5f);
            c.g = Mathf.Clamp01((c.g - 0.5f) * contrast + 0.5f);
            c.b = Mathf.Clamp01((c.b - 0.5f) * contrast + 0.5f);
        }
        return c;
    }

    /// <summary>Bake the transformed ramp to a 1-D LUT texture (for the palette-cycle shader). Repeat-wrap +
    /// bilinear so a shader's frac(t+phase) cycles smoothly. Bake at phase 0; the shader scrolls the phase.
    /// The caller owns the returned texture.</summary>
    public Texture2D ToLut(int width = 256)
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
            px[i] = Evaluate(i / (float)(width - 1));
        tex.SetPixels(px);
        tex.Apply();
        return tex;
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
