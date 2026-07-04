// ZUIColor.cs
// Serialisable paint description used by ZUI style defs — the type edited by ZUI.Color().
// Supports single colour (isGradient = false), linear/radial gradient, and pixel-edge modes.
// DrawRect() renders with optional rounded corners (Unity 2021.2+ only; falls back silently).
// Pixel-edge mode: each active edge draws colorA → colorB over pixelLength pixels, then solid colorB.
// Adjacent active edges blend seamlessly via 2-D corner textures.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[Flags]
public enum ZUIPixelEdges
{
    None   = 0,
    Left   = 1,
    Right  = 2,
    Bottom = 4,
    Top    = 8,
}

[Serializable]
public class ZUIColor : ISerializationCallbackReceiver
{
    public bool         isGradient = false;
    public bool         isRadial   = false;   // radial gradient (colorA = centre, colorB = edge); only when isGradient
    public ZUIColorRef  colorA     = new ZUIColorRef(new Color(.3f, .3f, .3f, 1f));
    public ZUIColorRef  colorB     = new ZUIColorRef(new Color(.1f, .1f, .1f, 1f));
    public float        angle      = 90f;    // degrees; 0 = left→right, 90 = bottom→top (linear mode only)

    // 2D gradient options (used when isRadial = true)
    public int          radialShape    = 0;     // 0 = Elliptical, 1 = Square, 2 = Shape (follows host corners)
    public float        radialCenterX  = 0.5f;  // 0-1, center X position (0.5 = center)
    public float        radialCenterY  = 0.5f;  // 0-1, center Y position (0.5 = center)
    public float        scaleX         = 1f;    // gradient shape X scale (1 = fills host)
    public float        scaleY         = 1f;    // gradient shape Y scale (1 = fills host)
    public bool         clampToHost    = true;  // true = Fit (gradient always reaches edges), false = Free (moves with center, clips)
    public bool         radialCircular = false;  // legacy: true locks scaleX == scaleY

    // ── Legacy fields (pre-ZUIColorRef) ──────────────────────────────────────
    [HideInInspector] public int _gradientDefVersion = 0;
    [HideInInspector][FormerlySerializedAs("colorA")]     public Color          _legacyColorA     = new Color(.3f, .3f, .3f, 1f);
    [HideInInspector][FormerlySerializedAs("colorB")]     public Color          _legacyColorB     = new Color(.1f, .1f, .1f, 1f);
    [HideInInspector][FormerlySerializedAs("colorARef")]  public string         _legacyColorARef  = "";
    [HideInInspector][FormerlySerializedAs("colorASlot")] public ZUIPaletteSlot _legacyColorASlot = ZUIPaletteSlot.Primary;
    [HideInInspector][FormerlySerializedAs("colorBRef")]  public string         _legacyColorBRef  = "";
    [HideInInspector][FormerlySerializedAs("colorBSlot")] public ZUIPaletteSlot _legacyColorBSlot = ZUIPaletteSlot.Primary;

    public Color GetColorA(ZUIStyleSheetAsset sheet) => colorA.Resolve(sheet);

    public Color GetColorB(ZUIStyleSheetAsset sheet)
    {
        return colorB.Resolve(sheet);
    }

    // Pixel-edge mode: each active edge draws colorA → colorB over pixelLength pixels,
    // then solid colorB fills the rest. cornerRadius is ignored in pixel mode.
    public bool          usePixelLength = false;
    public int           pixelLength    = 32;
    public ZUIPixelEdges pixelEdges     = ZUIPixelEdges.Bottom;

    // ── Multi-stop gradient ──────────────────────────────────────────────────
    // When non-empty, overrides colorA/colorB for texture generation.
    // Stops are sorted by position (0-1). First stop at 0, last at 1.
    // When empty, falls back to the 2-color colorA/colorB model.
    public List<ZUIGradientStop> stops = new List<ZUIGradientStop>();

    /// <summary>True when this gradient uses multi-stop mode.</summary>
    public bool HasMultipleStops => stops != null && stops.Count > 2;

    /// <summary>Returns the effective stop list: the stops list if 2+, otherwise synthesized from colorA/colorB.</summary>
    public List<ZUIGradientStop> GetEffectiveStops()
    {
        if (stops != null && stops.Count >= 2) return stops;
        return new List<ZUIGradientStop>
        {
            new ZUIGradientStop(colorA, 0f, 0.5f),
            new ZUIGradientStop(colorB, 1f, 0.5f),
        };
    }

    // ── Serialization migration ──────────────────────────────────────────────
    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize()
    {
        // Version 1 may have bad data from initial migration bug — re-migrate from legacy if legacy has data.
        if (_gradientDefVersion < 2)
        {
            bool legacyHasData = !string.IsNullOrEmpty(_legacyColorARef) ||
                                 _legacyColorA != default ||
                                 !string.IsNullOrEmpty(_legacyColorBRef) ||
                                 _legacyColorB != default;
            if (legacyHasData)
            {
                colorA = ZUIColorRef.FromLegacy(_legacyColorA, _legacyColorARef, _legacyColorASlot);
                colorB = ZUIColorRef.FromLegacy(_legacyColorB, _legacyColorBRef, _legacyColorBSlot);
            }
            _gradientDefVersion = 2;
        }
    }

    // ── Constructors ─────────────────────────────────────────────────────────────

    public ZUIColor() { }

    public ZUIColor(Color solid) { colorA = new ZUIColorRef(solid); _gradientDefVersion = 2; }

    public ZUIColor(Color a, Color b, float angle = 90f)
    {
        isGradient  = true;
        colorA      = new ZUIColorRef(a);
        colorB      = new ZUIColorRef(b);
        this.angle  = angle;
        _gradientDefVersion = 2;
    }

    // ── Lerp ──────────────────────────────────────────────────────────────────────
    // Returns a new ZUIColor interpolated between a and b at t (0=a, 1=b).
    // Interpolates resolved colors and angle. Palette refs are not carried.

    /// <summary>Lerp between two gradients. When smooth=true, angle/radial params are lerped
    /// continuously (for FieldLerp animation). When smooth=false (default), they snap at t=0.5
    /// to avoid rebuilding textures every frame (for crossfade).</summary>
    public static ZUIColor Lerp(ZUIColor a, ZUIColor b, float t, ZUIStyleSheetAsset sheet, bool smooth = false)
    {
        t = Mathf.Clamp01(t);
        bool eitherGrad = a.isGradient || b.isGradient;

        var result = new ZUIColor
        {
            isGradient = eitherGrad,
            isRadial   = smooth ? (eitherGrad && (a.isRadial || b.isRadial)) : (t >= 0.5f ? b.isRadial : a.isRadial),
            angle      = smooth ? Mathf.Lerp(a.angle, b.angle, t) : (t >= 0.5f ? b.angle : a.angle),
            radialCenterX = smooth ? Mathf.Lerp(a.radialCenterX, b.radialCenterX, t) : (t >= 0.5f ? b.radialCenterX : a.radialCenterX),
            radialCenterY = smooth ? Mathf.Lerp(a.radialCenterY, b.radialCenterY, t) : (t >= 0.5f ? b.radialCenterY : a.radialCenterY),
            scaleX        = smooth ? Mathf.Lerp(a.scaleX, b.scaleX, t) : (t >= 0.5f ? b.scaleX : a.scaleX),
            scaleY        = smooth ? Mathf.Lerp(a.scaleY, b.scaleY, t) : (t >= 0.5f ? b.scaleY : a.scaleY),
        };

        // Multi-stop lerp: interpolate matching stops by index, bake resolved colors
        var stopsA = a.GetEffectiveStops();
        var stopsB = b.GetEffectiveStops();
        int maxStops = Mathf.Max(stopsA.Count, stopsB.Count);

        if (maxStops > 2 || (a.stops?.Count >= 2) || (b.stops?.Count >= 2))
        {
            result.stops = new List<ZUIGradientStop>(maxStops);
            for (int i = 0; i < maxStops; i++)
            {
                var sa = i < stopsA.Count ? stopsA[i] : stopsA[stopsA.Count - 1];
                var sb = i < stopsB.Count ? stopsB[i] : stopsB[stopsB.Count - 1];
                result.stops.Add(new ZUIGradientStop(
                    new ZUIColorRef(Color.Lerp(sa.color.Resolve(sheet), sb.color.Resolve(sheet), t)),
                    Mathf.Lerp(sa.position, sb.position, t),
                    Mathf.Lerp(sa.easing, sb.easing, t)));
            }
            result.colorA = result.stops[0].color;
            result.colorB = result.stops[result.stops.Count - 1].color;
        }
        else
        {
            // Simple 2-color lerp (fast path, no allocation)
            Color aA = a.GetColorA(sheet), aB = a.isGradient ? a.GetColorB(sheet) : aA;
            Color bA = b.GetColorA(sheet), bB = b.isGradient ? b.GetColorB(sheet) : bA;
            result.colorA = new ZUIColorRef(Color.Lerp(aA, bA, t));
            result.colorB = new ZUIColorRef(Color.Lerp(aB, bB, t));
        }

        return result;
    }

    // ── Clone ─────────────────────────────────────────────────────────────────────

    public ZUIColor Clone()
    {
        var c = new ZUIColor
        {
            isGradient     = isGradient,
            isRadial       = isRadial,
            colorA         = colorA,
            colorB         = colorB,
            angle          = angle,
            usePixelLength = usePixelLength,
            pixelLength    = pixelLength,
            pixelEdges     = pixelEdges,
        };
        if (stops != null && stops.Count > 0)
        {
            c.stops = new List<ZUIGradientStop>(stops.Count);
            foreach (var s in stops)
                c.stops.Add(new ZUIGradientStop(s.color, s.position, s.easing));
        }
        return c;
    }

    // ── Cache ─────────────────────────────────────────────────────────────────────

    [NonSerialized] private Texture2D _tex;
    [NonSerialized] private int       _texHash;

    // Pixel-edge strips (one per direction) + corner blend textures.
    // All 8 share the same hash; rebuilt together whenever the gradient params change.
    [NonSerialized] private Texture2D _texPL, _texPR, _texPB, _texPT;
    [NonSerialized] private Texture2D _texCornerBL, _texCornerBR, _texCornerTL, _texCornerTR;
    [NonSerialized] private int       _pixelStripHash;

    public void Invalidate()
    {
        _tex = null;
        _texPL = _texPR = _texPB = _texPT = null;
        _texCornerBL = _texCornerBR = _texCornerTL = _texCornerTR = null;
    }

    // ── Runtime rect fill ───────────────────────────────────────────────────────
    // Runtime-safe replacement for UnityEditor.EditorGUI.DrawRect, usable in players.
    // Only paints during EventType.Repaint (GUI.DrawTexture is a no-op otherwise).
    internal static void FillRectRuntime(Rect r, Color c)
    {
        var p = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = p;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────────

    public void DrawRect(Rect rect, ZUIStyleSheetAsset sheet, float cornerRadius = 0f)
        => DrawRect(rect, sheet, new Vector4(cornerRadius, cornerRadius, cornerRadius, cornerRadius));

    // Vector4 overload: (TL, TR, BL, BR) — each component is clamped to half the rect.
    public void DrawRect(Rect rect, ZUIStyleSheetAsset sheet, Vector4 corners)
    {
        if (UnityEngine.Event.current.type != UnityEngine.EventType.Repaint) return;
        if (rect.width <= 1f) return;

        float maxR = Mathf.Min(rect.width * 0.5f, rect.height * 0.5f);
        corners = new Vector4(
            Mathf.Min(corners.x, maxR),
            Mathf.Min(corners.y, maxR),
            Mathf.Min(corners.z, maxR),
            Mathf.Min(corners.w, maxR));
        bool anyRound = corners.x > 0f || corners.y > 0f || corners.z > 0f || corners.w > 0f;

        if (!isGradient)
        {
            Color c = GetColorA(sheet);
            // Multiply by GUI.color so crossfade animation alpha (set by DrawVisualLerped) is respected.
            // The 7-param GUI.DrawTexture overload uses its color param as a direct tint, bypassing GUI.color.
            Color gc = GUI.color;
            Color tinted = new Color(c.r * gc.r, c.g * gc.g, c.b * gc.b, c.a * gc.a);
#if UNITY_2021_2_OR_NEWER
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill,
                true, 0f, tinted, Vector4.zero, anyRound ? corners : Vector4.zero);
#else
            var prev = GUI.color;
            GUI.color = tinted;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
#endif
            return;
        }

        // ── Pixel-edge mode ───────────────────────────────────────────────────
        if (usePixelLength && pixelLength > 0 && pixelEdges != ZUIPixelEdges.None)
        {
            DrawPixelMultiEdge(rect, sheet);
            return;
        }

        // ── Normal gradient ───────────────────────────────────────────────────
        var tex = GetOrBuildTexture(sheet);
#if UNITY_2021_2_OR_NEWER
        if (anyRound)
        {
            // Multiply by GUI.color so crossfade animation alpha is respected (see solid fill comment above).
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill,
                true, 0f, GUI.color, Vector4.zero, corners);
            return;
        }
#endif
        GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);
    }

    // ── Pixel multi-edge ──────────────────────────────────────────────────────────
    // Each edge's strip is drawn only in the non-corner region.
    // Each active corner where two edges meet gets a 2-D blend texture so neither
    // strip cuts across the other.

    void DrawPixelMultiEdge(Rect rect, ZUIStyleSheetAsset sheet)
    {
        FillRectRuntime(rect, GetColorB(sheet));

        EnsurePixelStrips(sheet);

        bool hasL = (pixelEdges & ZUIPixelEdges.Left)   != 0;
        bool hasR = (pixelEdges & ZUIPixelEdges.Right)  != 0;
        bool hasB = (pixelEdges & ZUIPixelEdges.Bottom) != 0;
        bool hasT = (pixelEdges & ZUIPixelEdges.Top)    != 0;

        float gw = Mathf.Min(pixelLength, rect.width);
        float gh = Mathf.Min(pixelLength, rect.height);

        // ── Edge strips — clipped to exclude corner areas ─────────────────────

        // Left / Right strips: full height minus any active top/bottom corner strips
        float hTop = rect.y    + (hasT ? gh : 0f);
        float hBot = rect.yMax - (hasB ? gh : 0f);
        float hH   = hBot - hTop;
        if (hH > 0f)
        {
            if (hasL) GUI.DrawTexture(new Rect(rect.x,         hTop, gw, hH), _texPL, ScaleMode.StretchToFill, true);
            if (hasR) GUI.DrawTexture(new Rect(rect.xMax - gw, hTop, gw, hH), _texPR, ScaleMode.StretchToFill, true);
        }

        // Bottom / Top strips: full width minus any active left/right corner strips
        float vLeft  = rect.x    + (hasL ? gw : 0f);
        float vRight = rect.xMax - (hasR ? gw : 0f);
        float vW     = vRight - vLeft;
        if (vW > 0f)
        {
            if (hasB) GUI.DrawTexture(new Rect(vLeft, rect.yMax - gh, vW, gh), _texPB, ScaleMode.StretchToFill, true);
            if (hasT) GUI.DrawTexture(new Rect(vLeft, rect.y,         vW, gh), _texPT, ScaleMode.StretchToFill, true);
        }

        // ── Corners — 2-D blend textures ──────────────────────────────────────
        if (hasL && hasB) GUI.DrawTexture(new Rect(rect.x,         rect.yMax - gh, gw, gh), _texCornerBL, ScaleMode.StretchToFill, true);
        if (hasR && hasB) GUI.DrawTexture(new Rect(rect.xMax - gw, rect.yMax - gh, gw, gh), _texCornerBR, ScaleMode.StretchToFill, true);
        if (hasL && hasT) GUI.DrawTexture(new Rect(rect.x,         rect.y,         gw, gh), _texCornerTL, ScaleMode.StretchToFill, true);
        if (hasR && hasT) GUI.DrawTexture(new Rect(rect.xMax - gw, rect.y,         gw, gh), _texCornerTR, ScaleMode.StretchToFill, true);
    }

    // Builds 4 edge strips + 4 corner blend textures.
    // All share the same hash; rebuilt together whenever gradient params change.
    void EnsurePixelStrips(ZUIStyleSheetAsset sheet)
    {
        int h = ComputePixelStripHash(sheet);
        if (_pixelStripHash == h && _texPL != null) return;
        _pixelStripHash = h;

        _texPL = BuildStrip(pixelLength, 1, true, sheet);   // Left:   colorA on left  → colorB on right
        _texPR = BuildStrip(pixelLength, 1, false, sheet);  // Right:  colorB on left  → colorA on right
        _texPB = BuildStrip(1, pixelLength, true, sheet);   // Bottom: colorA at bottom → colorB at top
        _texPT = BuildStrip(1, pixelLength, false, sheet);  // Top:    colorB at bottom → colorA at top

        // mirrorX flips horizontal axis (true = colorA is on the right of the corner).
        // mirrorY flips vertical axis  (true = colorA is at the top of the corner).
        _texCornerBL = BuildCornerTex(false, false, sheet);  // Bottom-Left
        _texCornerBR = BuildCornerTex(true,  false, sheet);  // Bottom-Right
        _texCornerTL = BuildCornerTex(false, true, sheet);   // Top-Left
        _texCornerTR = BuildCornerTex(true,  true, sheet);   // Top-Right
    }

    // 2-D corner texture: each pixel gets min(tHorizontal, tVertical) so both adjacent
    // edge gradients converge smoothly. The corner itself is solid colorA; both gradients
    // fade away from it toward colorB.
    Texture2D BuildCornerTex(bool mirrorX, bool mirrorY, ZUIStyleSheetAsset sheet)
    {
        int size = Mathf.Max(pixelLength, 1);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
        };
        float maxIdx = Mathf.Max(size - 1, 1);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float tx = (float)x / maxIdx;
            float ty = (float)y / maxIdx;
            if (mirrorX) tx = 1f - tx;
            if (mirrorY) ty = 1f - ty;
            // For corners, take the min of both axes to create the 2D blend
            float t = Mathf.Min(tx, ty);
            tex.SetPixel(x, y, SampleColor(t, sheet));
        }
        tex.Apply();
        return tex;
    }

    int ComputePixelStripHash(ZUIStyleSheetAsset sheet)
    {
        unchecked
        {
            int h = 99991;
            h = h * 397 ^ GetColorA(sheet).GetHashCode();
            h = h * 397 ^ GetColorB(sheet).GetHashCode();
            h = h * 397 ^ pixelLength;
            return h;
        }
    }

    // ── Normal gradient texture ───────────────────────────────────────────────────

    public Texture2D GetOrBuildTexture(ZUIStyleSheetAsset sheet)
    {
        int h = ComputeHash(sheet);
        if (_tex != null && _texHash == h) return _tex;
        _tex     = BuildTexture(sheet);
        _texHash = h;
        return _tex;
    }

    Texture2D BuildTexture(ZUIStyleSheetAsset sheet)
    {
        if (!isGradient)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, GetColorA(sheet));
            t.Apply();
            return t;
        }

        if (isRadial) return BuildRadialTexture(sheet);

        float rad = angle * Mathf.Deg2Rad;
        float ax  = Mathf.Cos(rad);
        float ay  = Mathf.Sin(rad);

        if (Mathf.Abs(ay) < 0.001f) return BuildStrip(32, 1, ax >= 0f, sheet);  // horizontal
        if (Mathf.Abs(ax) < 0.001f) return BuildStrip(1, 32, ay >= 0f, sheet);  // vertical
        return Build2D(32, 32, ax, ay, sheet);                                    // diagonal
    }

    Texture2D BuildRadialTexture(ZUIStyleSheetAsset sheet)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
        };
        float maxIdx = size - 1;
        float cx = radialCenterX * maxIdx;
        float cy = radialCenterY * maxIdx;
        float sx = Mathf.Max(0.01f, scaleX);
        float sy = Mathf.Max(0.01f, radialCircular ? sx : scaleY);

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - cx) / (maxIdx * 0.5f * sx);
            float dy = (y - cy) / (maxIdx * 0.5f * sy);
            float t;

            switch (radialShape)
            {
                case 1: // Square — Chebyshev (max) distance
                    t = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    break;
                case 2: // Shape — rounded rect SDF (approximation using smoothed Chebyshev)
                    float adx = Mathf.Abs(dx), ady = Mathf.Abs(dy);
                    float cornerBlend = 0.3f; // controls how rounded the corners are
                    t = Mathf.Lerp(Mathf.Max(adx, ady), Mathf.Sqrt(adx * adx + ady * ady), cornerBlend);
                    break;
                default: // 0 = Elliptical — Euclidean distance
                    t = Mathf.Sqrt(dx * dx + dy * dy);
                    break;
            }

            if (clampToHost)
                t = Mathf.Clamp01(t);
            else
                t = Mathf.Clamp01(t); // Free mode: same clamp but gradient position shifts with center
            tex.SetPixel(x, y, SampleColor(t, sheet));
        }
        tex.Apply();
        return tex;
    }

    Texture2D BuildStrip(int w, int h, bool forward, ZUIStyleSheetAsset sheet)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
        };
        int len = Mathf.Max(w, h);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Mathf.Max(len - 1, 1);
            if (!forward) t = 1f - t;
            Color c = SampleColor(t, sheet);
            if (w > 1) tex.SetPixel(i, 0, c);
            else       tex.SetPixel(0, i, c);
        }
        tex.Apply();
        return tex;
    }

    Texture2D Build2D(int w, int h, float ax, float ay, ZUIStyleSheetAsset sheet)
    {
        float len = Mathf.Sqrt(ax * ax + ay * ay);
        ax /= len; ay /= len;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
        };
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float nx = (x + 0.5f) / w - 0.5f;
            float ny = (y + 0.5f) / h - 0.5f;
            float t  = Mathf.Clamp01(nx * ax + ny * ay + 0.5f);
            tex.SetPixel(x, y, SampleColor(t, sheet));
        }
        tex.Apply();
        return tex;
    }

    static float ApplyBias(float t, float bias)
    {
        if (Mathf.Approximately(bias, 0.5f)) return t;
        float b = Mathf.Clamp(bias, 0.001f, 0.999f);
        return Mathf.Pow(t, Mathf.Log(b) / Mathf.Log(0.5f));
    }

    /// <summary>Samples a color at position t (0-1), using multi-stop if available, otherwise 2-color.</summary>
    Color SampleColor(float t, ZUIStyleSheetAsset sheet)
    {
        var s = GetEffectiveStops();
        if (s.Count > 2 || (stops != null && stops.Count >= 2))
            return SampleStops(s, t, sheet);
        return Color.Lerp(GetColorA(sheet), GetColorB(sheet), t);
    }

    /// <summary>Samples a color at position t (0-1) from a list of stops with per-segment easing.</summary>
    static Color SampleStops(List<ZUIGradientStop> stops, float t, ZUIStyleSheetAsset sheet)
    {
        if (stops.Count == 0) return Color.clear;
        if (stops.Count == 1) return stops[0].color.Resolve(sheet);
        t = Mathf.Clamp01(t);
        // Find segment
        for (int i = 0; i < stops.Count - 1; i++)
        {
            if (t <= stops[i + 1].position || i == stops.Count - 2)
            {
                float segStart = stops[i].position;
                float segEnd   = stops[i + 1].position;
                float segLen   = segEnd - segStart;
                float segT     = segLen > 0.0001f ? (t - segStart) / segLen : 0f;
                segT = ApplyBias(segT, stops[i + 1].easing);
                return Color.Lerp(stops[i].color.Resolve(sheet), stops[i + 1].color.Resolve(sheet), segT);
            }
        }
        return stops[stops.Count - 1].color.Resolve(sheet);
    }

    int ComputeHash(ZUIStyleSheetAsset sheet)
    {
        unchecked
        {
            int h = isGradient ? 1231 : 1237;
            h = h * 397 ^ GetColorA(sheet).GetHashCode();
            if (!isGradient) return h;
            h = h * 397 ^ GetColorB(sheet).GetHashCode();
            if (stops != null && stops.Count >= 2)
            {
                h = h * 397 ^ stops.Count;
                foreach (var s in stops)
                {
                    h = h * 397 ^ s.color.Resolve(sheet).GetHashCode();
                    h = h * 397 ^ s.position.GetHashCode();
                    h = h * 397 ^ s.easing.GetHashCode();
                }
            }
            if (isRadial)
            {
                h = h * 397 ^ 9901;
                h = h * 397 ^ radialShape;
                h = h * 397 ^ radialCenterX.GetHashCode();
                h = h * 397 ^ radialCenterY.GetHashCode();
                h = h * 397 ^ scaleX.GetHashCode();
                h = h * 397 ^ scaleY.GetHashCode();
                h = h * 397 ^ (clampToHost ? 7717 : 3313);
                h = h * 397 ^ (radialCircular ? 4519 : 0);
                return h;
            }
            h = h * 397 ^ angle.GetHashCode();
            if (usePixelLength) { h = h * 397 ^ pixelLength; h = h * 397 ^ (int)pixelEdges; }
            return h;
        }
    }
}
