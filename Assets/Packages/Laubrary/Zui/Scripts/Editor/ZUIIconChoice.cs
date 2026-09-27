// ZUIIconChoice.cs
// A small set of mutually exclusive choices shown as ICONS rather than words — ZUI.IconChoice(rect, value, icons, tips).
// Plus generated waveform icons, ZUIWaveIcons.Get(ZUIWave.Sine), for any tool that picks a wave shape.
//
// Why: a choice between shapes is read faster as the shapes themselves, and four words of text take four times the room.
// The name of each option lives in its tooltip, so nothing is lost for someone who needs the word. Built into ZUI rather
// than drawn inside one tool because the shape recurs — any oscillator, any curve preset, any "pick one of these looks".

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    /// <summary>Width of one icon option at the default size. Callers laying out rows use this to reserve room.</summary>
    public const float IconChoiceCellWidth = 30f;

    /// <summary>
    /// One latching icon button per option, joined into a strip; returns the chosen index. Each button carries its option's
    /// tooltip, so hovering names what the icon means.
    /// </summary>
    public static int IconChoice(Rect rect, int value, Texture[] icons, string[] tooltips, string style = Style.RichToggle)
    {
        int n = icons != null ? icons.Length : 0;
        if (n == 0) return value;
        float w = rect.width / n;
        for (int i = 0; i < n; i++)
        {
            var r = new Rect(rect.x + i * w, rect.y, w, rect.height);
            var corner = n == 1 ? ZUICornerMask.All : i == 0 ? ZUICornerMask.Left : i == n - 1 ? ZUICornerMask.Right : ZUICornerMask.None;
            bool on = value == i;
            string tip = tooltips != null && i < tooltips.Length ? tooltips[i] : null;
            if (Toggle(r, on, new GUIContent(icons[i], tip), style, null, corner) && !on) value = i;
        }
        return value;
    }
}

/// <summary>Shapes <see cref="ZUIWaveIcons"/> can draw.</summary>
public enum ZUIWave { Sine, Triangle, Saw, Square, Random }

/// <summary>
/// Small white waveform icons, drawn once in code and kept. No image files to ship or lose, and they are drawn at the
/// screen's real pixel density so the line stays crisp on a high-density display.
/// </summary>
public static class ZUIWaveIcons
{
    static readonly Dictionary<ZUIWave, Texture2D> cache = new Dictionary<ZUIWave, Texture2D>();

    public static Texture2D Get(ZUIWave wave)
    {
        if (cache.TryGetValue(wave, out var tex) && tex != null) return tex;
        tex = Draw(wave);
        cache[wave] = tex;
        return tex;
    }

    static float Sample(ZUIWave wave, float t)
    {
        switch (wave)
        {
            case ZUIWave.Triangle: return t < 0.25f ? t * 4f : t < 0.75f ? 2f - t * 4f : t * 4f - 4f;
            case ZUIWave.Saw:      { float u = (t + 0.5f) % 1f; return u * 2f - 1f; }
            case ZUIWave.Square:   return t < 0.5f ? 1f : -1f;
            case ZUIWave.Random:
            {
                // A few held-then-gliding random levels: what the random oscillator mode does.
                float[] lv = { 0.1f, 0.8f, -0.6f, 0.4f, -0.9f, 0.2f };
                float x = t * (lv.Length - 1); int i = Mathf.Min((int)x, lv.Length - 2); float f = x - i;
                float s = f < 0.5f ? 0f : (f - 0.5f) * 2f;
                return Mathf.Lerp(lv[i], lv[i + 1], s * s * (3f - 2f * s));
            }
            default: return Mathf.Sin(t * Mathf.PI * 2f);
        }
    }

    static Texture2D Draw(ZUIWave wave)
    {
        float scale = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
        int w = Mathf.RoundToInt(22f * scale), h = Mathf.RoundToInt(12f * scale);
        float stroke = 1.1f * scale, pad = stroke + 1f;
        var px = new Color[w * h];

        // The shape as a polyline; a vertical jump (square, saw) is part of the line, so it is drawn as a segment too.
        var pts = new List<Vector2>();
        int steps = 96;
        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            float y = Sample(wave, t);
            if (s > 0 && (wave == ZUIWave.Square || wave == ZUIWave.Saw))
            {
                float prev = Sample(wave, (float)(s - 1) / steps);
                if (Mathf.Abs(prev - y) > 1f) pts.Add(new Vector2(t, prev));
            }
            pts.Add(new Vector2(t, y));
        }
        for (int i = 0; i < pts.Count; i++)
            pts[i] = new Vector2(pad + pts[i].x * (w - 2f * pad), pad + (pts[i].y * 0.5f + 0.5f) * (h - 2f * pad));

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d = float.MaxValue;
            for (int i = 1; i < pts.Count; i++) d = Mathf.Min(d, SegmentDistance(p, pts[i - 1], pts[i]));
            float a = Mathf.Clamp01(stroke * 0.5f + 0.5f - d);
            px[y * w + x] = new Color(1f, 1f, 1f, a);
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a; float len = ab.sqrMagnitude;
        float t = len > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }
}
