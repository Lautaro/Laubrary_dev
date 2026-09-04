// ZuiRampAdjust.cs
// The NON-DESTRUCTIVE knobs a colour ramp is adjusted by at sample time — position (reverse / phase / quantise)
// and colour (hue / saturation / brightness / contrast) — plus the arithmetic that applies them.
//
// Why this exists as its own type. ZuiGradient has carried these knobs since it was written, but a raw ramp
// (Pyre's PyreRamp) had nowhere to store them, so the same author holding the same colours got a different set of
// controls depending on which type happened to hold the ramp. This is the storage a raw ramp folds in, and
// ZuiRampMath is the ONE implementation of the maths: ZuiGradient.Evaluate calls it too, so a hue shift means the
// same thing on both sides rather than in two arithmetics that can drift.
//
// IDENTITY IS THE DEFAULT, and that is load-bearing: every field's default leaves a ramp exactly as it was
// authored, so adding this to an existing serialized type re-renders every already-authored asset byte-identically.
// IsIdentity is what a baker tests to skip the whole pass rather than trusting float round-trips to be exact.
//
// ZuiGradient keeps its own fields rather than holding one of these: its knobs are ANIMATABLE over life
// ([SerializeReference] ZUIValue companions), which is a strictly richer storage a plain-float struct cannot hold,
// and renaming its serialized fields would break every authored gradient. The maths is shared; the storage is not.
//
// Runtime-safe, deterministic, global-namespace [Serializable] — the same idiom as ZuiFill / ZuiGradient / ZUIValue.

using System;
using UnityEngine;

/// <summary>The non-destructive transform knobs applied on top of a ramp's authored stops. Every default is the
/// identity, so a fresh instance renders the ramp exactly as authored.</summary>
[Serializable]
public class ZuiRampAdjust
{
    // ── position transforms (shape WHERE the ramp is sampled) ──
    [Tooltip("Sample the ramp backwards (1-t).")]
    public bool reverse = false;

    [Tooltip("Scroll the ramp along its length, 0..2. 0→1 plays it forward, 1→2 plays it back reversed, so 2 lands exactly where 0 did.")]
    public float phase = 0f;

    [Min(0), Tooltip("Snap the sample position to N discrete bands (0 = smooth) — the ramp Posterize.")]
    public int quantiseSteps = 0;

    // ── colour transforms (change WHAT colour comes back) ──
    [Range(-1f, 1f), Tooltip("Rotate the hue of the whole ramp. ±1 = ±180°.")]
    public float hueShift = 0f;

    [Range(0f, 2f), Tooltip("Multiply saturation across the whole ramp (1 = unchanged).")]
    public float saturation = 1f;

    [Range(0f, 2f), Tooltip("Multiply brightness (value) across the whole ramp (1 = unchanged).")]
    public float brightness = 1f;

    [Range(0f, 2f), Tooltip("Contrast around mid-grey (1 = unchanged).")]
    public float contrast = 1f;

    // ── cycling (an authored intent a driver reads; it changes no still sample) ──
    [Tooltip("This ramp wants to colour-cycle. A driver advances the phase at runtime; a still frame is unaffected.")]
    public bool cycle = false;

    [Tooltip("Cycle speed (phase units per second) a driver should use by default.")]
    public float cycleSpeed = 0.5f;

    /// <summary>True when every knob is at its default, so sampling through this can be skipped entirely — which is
    /// what keeps an already-authored asset byte-identical rather than merely arithmetically equal. `cycle` and
    /// `cycleSpeed` are deliberately NOT part of this: they are an instruction to a runtime driver and change no
    /// still sample, so a ramp marked to cycle still bakes identically until something advances its phase.</summary>
    public bool IsIdentity =>
        !reverse && phase == 0f && quantiseSteps <= 0
        && hueShift == 0f && saturation == 1f && brightness == 1f && contrast == 1f;

    /// <summary>Where to read the ramp for output position <paramref name="t"/>. <paramref name="extraPhase"/> is a
    /// driver's live cycle phase, which ADDS on top of the authored one.</summary>
    public float Position(float t, float extraPhase = 0f) =>
        ZuiRampMath.Position(t, reverse, phase + extraPhase, quantiseSteps);

    /// <summary>The colour knobs applied to one already-sampled colour. Alpha is never touched — opacity is the
    /// ramp's own authored ceiling, not something a hue rotation may quietly rewrite.</summary>
    public Color Apply(Color c) => ZuiRampMath.Adjust(c, hueShift, saturation, brightness, contrast);

    /// <summary>The whole pass in one call: read <paramref name="raw"/> at the transformed position, then adjust the
    /// colour that came back. `raw` is the ramp's own unadjusted evaluation, so this can never drift from it.</summary>
    public Color Sample(Func<float, Color> raw, float t, float extraPhase = 0f) => Apply(raw(Position(t, extraPhase)));

    public ZuiRampAdjust Clone() => (ZuiRampAdjust)MemberwiseClone();
}

/// <summary>The ramp-adjust arithmetic, as free functions so both a plain <see cref="ZuiRampAdjust"/> and
/// ZuiGradient's animatable per-life knobs run the SAME code over their different storage.</summary>
public static class ZuiRampMath
{
    /// <summary>Position transform: reverse, then scroll by <paramref name="phase"/>, then quantise to
    /// <paramref name="steps"/> bands. A zero phase clamps instead of wrapping, because a plain sample at 1 must
    /// stay at the ramp's END — PingPong would fold it back to 0 and make the default non-identity.</summary>
    public static float Position(float t, bool reverse, float phase, int steps)
    {
        float u = reverse ? 1f - t : t;
        // A scroll MIRRORS the ramp (0→1→0) so an animated phase loops seamlessly instead of jumping at the wrap.
        u = phase != 0f ? Mathf.PingPong(u + phase, 1f) : Mathf.Clamp01(u);
        if (steps > 0) u = Mathf.Floor(u * steps) / Mathf.Max(1, steps - 1);
        return Mathf.Clamp01(u);
    }

    /// <summary>Colour transform: hue/saturation/brightness in HSV, then contrast around mid-grey. Alpha passes
    /// through untouched.</summary>
    public static Color Adjust(Color c, float hueShift, float saturation, float brightness, float contrast)
    {
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

    /// <summary>A stable hash of the knobs, for a LUT cache key. Folded by a baker that caches its LUT so a hue drag
    /// invalidates it the same way a moved stop does.</summary>
    public static int Hash(int h, ZuiRampAdjust a)
    {
        unchecked
        {
            if (a == null) return (h ^ 0) * 16777619;
            h = (h ^ (a.reverse ? 1 : 0)) * 16777619;
            h = (h ^ a.phase.GetHashCode()) * 16777619;
            h = (h ^ a.quantiseSteps) * 16777619;
            h = (h ^ a.hueShift.GetHashCode()) * 16777619;
            h = (h ^ a.saturation.GetHashCode()) * 16777619;
            h = (h ^ a.brightness.GetHashCode()) * 16777619;
            h = (h ^ a.contrast.GetHashCode()) * 16777619;
            return h;
        }
    }
}
