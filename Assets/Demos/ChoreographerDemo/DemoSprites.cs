using System.Collections.Generic;
using UnityEngine;

/// Demo-only procedural placeholder sprites (white filled shapes, tint via SpriteRenderer.color). Swap these for
/// real art later — every demo object gets its sprite from here so there's one place to replace. No assets shipped.
public static class DemoSprites
{
    public enum Shape { Circle, Square, Triangle, Diamond }

    static readonly Dictionary<Shape, Sprite> cache = new();

    public static Sprite Get(Shape shape)
    {
        if (cache.TryGetValue(shape, out var s) && s != null) return s;
        s = Build(shape, 64);
        cache[shape] = s;
        return s;
    }

    static Sprite Build(Shape shape, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, name = "DemoShape_" + shape };
        var clear = new Color(1, 1, 1, 0);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                px[y * size + x] = Inside(shape, u, v) ? Color.white : clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size); // 1 world unit
    }

    static bool Inside(Shape shape, float u, float v)
    {
        switch (shape)
        {
            case Shape.Square: return u >= 0.12f && u <= 0.88f && v >= 0.12f && v <= 0.88f;
            case Shape.Diamond: return Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f) <= 0.42f;
            case Shape.Triangle:
                if (v < 0.15f || v > 0.85f) return false;
                float half = 0.5f * (0.85f - v) / 0.70f;   // point-up: full width at base, zero at apex
                return Mathf.Abs(u - 0.5f) <= half;
            default: // Circle
                float dx = u - 0.5f, dy = v - 0.5f;
                return dx * dx + dy * dy <= 0.42f * 0.42f;
        }
    }
}
