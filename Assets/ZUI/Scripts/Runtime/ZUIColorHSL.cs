// ZUIColorHSL.cs
// HSL color space utilities for auto-palette generation.

using UnityEngine;

public static class ZUIColorHSL
{
    public static (float h, float s, float l, float a) RGBToHSL(Color c)
    {
        float r = c.r, g = c.g, b = c.b;
        float max = Mathf.Max(r, Mathf.Max(g, b));
        float min = Mathf.Min(r, Mathf.Min(g, b));
        float l = (max + min) * 0.5f;
        float h = 0f, s = 0f;

        if (max != min)
        {
            float d = max - min;
            s = l > 0.5f ? d / (2f - max - min) : d / (max + min);

            if (max == r)      h = (g - b) / d + (g < b ? 6f : 0f);
            else if (max == g) h = (b - r) / d + 2f;
            else               h = (r - g) / d + 4f;
            h /= 6f;
        }

        return (h, s, l, c.a);
    }

    public static Color HSLToRGB(float h, float s, float l, float a = 1f)
    {
        if (s < 0.001f)
            return new Color(l, l, l, a);

        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;

        float r = HueToRGB(p, q, h + 1f / 3f);
        float g = HueToRGB(p, q, h);
        float b = HueToRGB(p, q, h - 1f / 3f);

        return new Color(r, g, b, a);
    }

    static float HueToRGB(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 0.5f)    return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }

    /// <summary>
    /// Applies proportional HSV modifiers to a base color.
    /// Each mod is -1..+1: positive lerps toward max, negative lerps toward zero.
    /// Hue mod is mapped to ±60° (±1/6 of the wheel) and wraps.
    /// </summary>
    public static Color ApplyAutoColorMod(Color baseColor, float hueMod, float satMod, float valMod)
    {
        Color.RGBToHSV(baseColor, out float h, out float s, out float v);

        // Hue: circular offset, ±1 maps to ±60°
        h = (h + hueMod * (1f / 6f)) % 1f;
        if (h < 0f) h += 1f;

        // Saturation: proportional in remaining space
        s = ApplyProportionalMod(s, satMod);

        // Value: proportional in remaining space
        v = ApplyProportionalMod(v, valMod);

        var result = Color.HSVToRGB(h, s, v);
        result.a = baseColor.a;
        return result;
    }

    static float ApplyProportionalMod(float baseVal, float mod)
    {
        if (mod >= 0f)
            return baseVal + mod * (1f - baseVal);
        else
            return baseVal + mod * baseVal;
    }

}
