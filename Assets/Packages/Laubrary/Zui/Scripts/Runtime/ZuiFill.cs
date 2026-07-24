// ZuiFill.cs
// A configurable colour-or-fill: a single solid colour, or one of several gradient-driven fills —
// over-life (the gradient sampled by a 0..1 lifetime clock), or a SPATIAL fill (linear / radial /
// noise) sampled by the consumer's local (u,v) point. Any colour tool that only ever needed a swatch
// can adopt this and gain "make it a gradient" for free; a ZuiFill left in Solid mode IS just a colour
// picker (with alpha, everywhere).
//
// Runtime-safe (no editor deps) and DETERMINISTIC — Noise uses an internal FNV-hash value noise, never
// UnityEngine.Random — so the same (life, u, v) always yields the same colour on every machine and every
// frame. Authored by ZuiFillControl (editor, Z.Fill); evaluated here at play/bake time.
//
// Idiom mirrors ZUIValue (global namespace, [Serializable], a pure Evaluate) — but ZuiFill's fields are
// public: it is plain paint data with no clamping or external-resolver machinery to hide behind
// properties.

using System;
using UnityEngine;

[Serializable]
public class ZuiFill
{
    public enum Mode { Solid, OverLife, Linear, Radial, Noise }

    public Mode mode = Mode.Solid;
    public Color color = Color.white;   // Solid — alpha-capable like every mode
    public Gradient gradient;           // every non-Solid mode (alpha comes from the gradient)
    public float angleDeg = 0f;         // Linear: rotation of the fill axis, in degrees
    [Min(0.05f)] public float zoom = 1f; // Radial + Noise: spatial scale (higher zooms the pattern in)

    public ZuiFill() { }
    public ZuiFill(Color c) { color = c; }

    /// <summary>
    /// Pure, deterministic evaluation of the fill's colour at a point.
    /// <paramref name="life"/> is a 0..1 over-life clock; <paramref name="u"/>/<paramref name="v"/> are the
    /// consumer's normalized local point, -1..1 across the filled shape. Any non-Solid mode with a null
    /// gradient falls back to <see cref="color"/> so a half-configured fill never renders empty.
    /// </summary>
    public Color Evaluate(float life, float u, float v)
    {
        switch (mode)
        {
            case Mode.Solid:
                return color;

            case Mode.OverLife:
                return gradient != null ? gradient.Evaluate(Mathf.Clamp01(life)) : color;

            case Mode.Linear:
            {
                if (gradient == null) return color;
                // Project (u,v) onto the axis at angleDeg; the projection runs ~[-1,1] across the shape,
                // remapped to the gradient's 0..1 domain.
                float rad = angleDeg * Mathf.Deg2Rad;
                float proj = u * Mathf.Cos(rad) + v * Mathf.Sin(rad);
                float t = Mathf.Clamp01((proj + 1f) * 0.5f);
                return gradient.Evaluate(t);
            }

            case Mode.Radial:
            {
                if (gradient == null) return color;
                float r = Mathf.Sqrt(u * u + v * v) * Mathf.Max(0.05f, zoom);
                return gradient.Evaluate(Mathf.Clamp01(r));
            }

            case Mode.Noise:
            {
                if (gradient == null) return color;
                float z = Mathf.Max(0.05f, zoom);
                float n = ValueNoise2Octave(u * z * 3f, v * z * 3f);
                return gradient.Evaluate(Mathf.Clamp01(n));
            }

            default:
                return color;
        }
    }

    /// <summary>Give this fill a gradient if it lacks one — so a freshly-switched non-Solid mode has
    /// something to show (mirrors ZUIValue.EnsureCurveDefaults). Never overwrites an existing gradient.</summary>
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
