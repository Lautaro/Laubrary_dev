// ZUISubDefs.cs
// Reusable sub-definition structs shared between ZUIBoxDef and ZUIButtonDef.

using System;
using UnityEngine;
using UnityEngine.Serialization;

// ── ZUIBorderDef ──────────────────────────────────────────────────────────────
// Encapsulates all border properties: a ZUIColor for colour + a width.
// Migration from the old flat-field layout is handled via OnAfterDeserialize.

[Serializable]
public class ZUIBorderDef : ISerializationCallbackReceiver
{
    [FormerlySerializedAs("gradient")]
    public ZUIColor color = new ZUIColor(new Color(1f, 1f, 1f, 0.06f));
    public ZUIEdgeValuesFloat edgeWidth = new ZUIEdgeValuesFloat(1f);

    // Legacy single width — migrated to edgeWidth
    [UnityEngine.HideInInspector] public float width = 1f;

    // ── Migration from pre-gradient layout ────────────────────────────────────
    [HideInInspector] public int _borderDefVersion = 0;

    [HideInInspector] public Color  colorA        = new Color(1f, 1f, 1f, 0.06f);
    [HideInInspector] public Color  colorB        = new Color(0f, 0f, 0f, 0.10f);
    [HideInInspector] public bool   isGradient    = false;
    [HideInInspector] public float  gradientAngle = 135f;

    [HideInInspector] public string         colorARef  = "";
    [HideInInspector] public ZUIPaletteSlot colorASlot = ZUIPaletteSlot.Primary;
    [HideInInspector] public string         colorBRef  = "";
    [HideInInspector] public ZUIPaletteSlot colorBSlot = ZUIPaletteSlot.Primary;

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        if (_borderDefVersion == 0)
        {
            // Migrate flat fields into the color sub-object.
            color.colorA     = ZUIColorRef.FromLegacy(colorA, colorARef, colorASlot);
            color.colorB     = ZUIColorRef.FromLegacy(colorB, colorBRef, colorBSlot);
            color.isGradient = isGradient;
            color.angle      = gradientAngle;
            color._gradientDefVersion = 2; // skip color's own migration
            _borderDefVersion   = 1;
        }
        // Migrate single width to edge width
        if (_borderDefVersion == 1)
        {
            if (edgeWidth.all == 0f && width > 0f)
                edgeWidth = new ZUIEdgeValuesFloat(width);
            _borderDefVersion = 2;
        }
    }

    // ── Constructors ──────────────────────────────────────────────────────────

    public ZUIBorderDef() { _borderDefVersion = 2; }

    public ZUIBorderDef(Color solidColor, float w = 1f)
    {
        color = new ZUIColor(solidColor);
        edgeWidth = new ZUIEdgeValuesFloat(w);
        width = w;
        _borderDefVersion = 2;
    }

    // ── Resolved values (kept for draw-path compatibility) ────────────────────

    public Color GetResolvedA(ZUIStyleSheetAsset sheet) => color.GetColorA(sheet);
    public Color GetResolvedB(ZUIStyleSheetAsset sheet) => color.GetColorB(sheet);
}

// ── ZUIDropShadowDef ──────────────────────────────────────────────────────────
// Background drop-shadow for boxes and buttons.

[Serializable]
public class ZUIDropShadowDef : ISerializationCallbackReceiver
{
    public bool         enabled = false;
    public Vector2      offset  = new Vector2(3f, 3f);
    public ZUIColorRef  tint    = new ZUIColorRef(new Color(0f, 0f, 0f, 0.35f));
    public float        blurRadius = 0f;   // 0 = sharp shadow, >0 = multi-pass blur approximation
    public int          blurPasses = 5;    // number of concentric passes (more = smoother, costlier)

    // ── Legacy fields (pre-ZUIColorRef) ──────────────────────────────────────
    [HideInInspector] public int _shadowDefVersion = 0;
    [HideInInspector][FormerlySerializedAs("color")]     public Color          _legacyColor     = new Color(0f, 0f, 0f, 0.35f);
    [HideInInspector][FormerlySerializedAs("colorRef")]  public string         _legacyColorRef  = "";
    [HideInInspector][FormerlySerializedAs("colorSlot")] public ZUIPaletteSlot _legacyColorSlot = ZUIPaletteSlot.Primary;

    public Color GetResolvedColor(ZUIStyleSheetAsset sheet) => tint.Resolve(sheet);

    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize()
    {
        if (_shadowDefVersion < 2)
        {
            if (_legacyColor != default || !string.IsNullOrEmpty(_legacyColorRef))
                tint = ZUIColorRef.FromLegacy(_legacyColor, _legacyColorRef, _legacyColorSlot);
            _shadowDefVersion = 2;
        }
    }
}

// ── ZUITextShadowDef ─────────────────────────────────────────────────────────
// Text drop-shadow: offset + color, no blur (IMGUI text shadow is a second draw pass).
// Structurally mirrors ZUIDropShadowDef but without blur fields.

[Serializable]
public class ZUITextShadowDef
{
    public bool         enabled = false;
    public Vector2      offset  = new Vector2(1f, 1f);
    public ZUIColorRef  color   = new ZUIColorRef(new Color(0f, 0f, 0f, 0.6f));

    public Color GetResolvedColor(ZUIStyleSheetAsset sheet) => color.Resolve(sheet);
}

// ── ZUIShapeDef ───────────────────────────────────────────────────────────────
// Corner radius + per-corner rounding flags.

[Serializable]
public class ZUIShapeDef
{
    public int  cornerRadius = 0;
    public bool roundTL      = true;
    public bool roundTR      = true;
    public bool roundBL      = true;
    public bool roundBR      = true;

    public int GetResolvedRadius() => cornerRadius;

    // Returns a Vector4(TL, TR, BR, BL) for GUI.DrawTexture's borderRadius parameter.
    public Vector4 GetCornerVector(float r) => new Vector4(
        roundTL ? r : 0f,
        roundTR ? r : 0f,
        roundBR ? r : 0f,
        roundBL ? r : 0f);
}

// ── ZUIEaseCurve ─────────────────────────────────────────────────────────────
public enum ZUIEaseCurve
{
    Linear,
    EaseInQuad,
    EaseOutQuad,
    EaseInOutQuad,
    EaseInCubic,
    EaseOutCubic,
    EaseInOutCubic,
    EaseOutBack,     // slight overshoot
    EaseOutElastic,  // springy
}

// ── ZUIAnimMode ─────────────────────────────────────────────────────────────
/// <summary>How button hover/click transitions render.</summary>
public enum ZUIAnimMode
{
    /// <summary>Draw state A at (1-t) opacity, state B at t opacity. Fast, simple.</summary>
    Crossfade,
    /// <summary>Interpolate every visual field (colors, angle, border, shadow). Richer result.</summary>
    FieldLerp,
}

public static class ZUIEasing
{
    public static float Evaluate(ZUIEaseCurve curve, float t)
    {
        t = Mathf.Clamp01(t);
        switch (curve)
        {
            case ZUIEaseCurve.EaseInQuad:      return t * t;
            case ZUIEaseCurve.EaseOutQuad:     return t * (2f - t);
            case ZUIEaseCurve.EaseInOutQuad:   return t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;
            case ZUIEaseCurve.EaseInCubic:     return t * t * t;
            case ZUIEaseCurve.EaseOutCubic:    { float f = t - 1f; return f * f * f + 1f; }
            case ZUIEaseCurve.EaseInOutCubic:  return t < 0.5f ? 4f * t * t * t : (t - 1f) * (2f * t - 2f) * (2f * t - 2f) + 1f;
            case ZUIEaseCurve.EaseOutBack:     { float f = t - 1f; return f * f * ((1.70158f + 1f) * f + 1.70158f) + 1f; }
            case ZUIEaseCurve.EaseOutElastic:
                if (t <= 0f) return 0f;
                if (t >= 1f) return 1f;
                return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - 0.075f) * (2f * Mathf.PI) / 0.3f) + 1f;
            default: return t; // Linear
        }
    }
}

// ── ZUIGlowDef ───────────────────────────────────────────────────────────────
// Glow effect: outer (behind shape) and/or inner (inside shape).
// Multi-pass concentric rects with decreasing alpha for a soft luminous edge.
// Per-edge spread lets you glow only specific sides (e.g. bottom glow = light source above).

[Serializable]
public class ZUIGlowDef
{
    public bool        enabled  = false;
    public ZUIColorRef color    = new ZUIColorRef(new Color(0.4f, 0.6f, 1f, 0.5f));
    public float       radius   = 6f;    // total glow spread in pixels
    public int         passes   = 4;     // number of concentric layers (more = smoother)

    // Per-edge spread multipliers (0 = no glow on that edge, 1 = full radius)
    public int edgeMode;                  // 0 = uniform, 1 = per-edge
    public float spreadTop    = 1f;
    public float spreadRight  = 1f;
    public float spreadBottom = 1f;
    public float spreadLeft   = 1f;

    // Inner glow
    public bool        innerEnabled = false;
    public ZUIColorRef innerColor   = new ZUIColorRef(new Color(1f, 1f, 1f, 0.3f));
    public float       innerRadius  = 4f;
    public int         innerPasses  = 3;

    public float SpreadTop    => edgeMode == 0 ? 1f : spreadTop;
    public float SpreadRight  => edgeMode == 0 ? 1f : spreadRight;
    public float SpreadBottom => edgeMode == 0 ? 1f : spreadBottom;
    public float SpreadLeft   => edgeMode == 0 ? 1f : spreadLeft;
}

// ── ZUIPatternDef ────────────────────────────────────────────────────────────
// Optional texture pattern drawn on top of the fill (after overlay).
// Can be a procedural pattern or a custom texture.

public enum ZUIPatternType { None, Stripes, Dots, Grid, Diagonal, Noise, Icon }

[Serializable]
public class ZUIPatternDef
{
    public bool           enabled     = false;
    public ZUIPatternType patternType = ZUIPatternType.Stripes;
    public ZUIColorRef    tint        = new ZUIColorRef(new Color(1f, 1f, 1f, 0.1f));
    public float          scale       = 1f;     // texture scale multiplier
    public float          rotation    = 0f;     // degrees
    public float          opacity     = 0.15f;  // 0-1
    public float          offsetX     = 0f;     // 0-1, fraction of cell size
    public float          offsetY     = 0f;     // 0-1, fraction of cell size

    // Icon pattern: name of an icon from the sheet's icon library
    public string         iconId      = "";

    // Custom texture (overrides procedural when set)
    public UnityEngine.Texture2D customTexture = null;

    [NonSerialized] private Texture2D _cachedTex;
    [NonSerialized] private int       _cachedHash;

    /// <summary>True if this pattern uses a small tiled texture (icon) rather than rect-sized (analytical).</summary>
    public bool UsesTiledTexture => patternType == ZUIPatternType.Icon;

    /// <summary>Gets the pattern texture. Analytical patterns are rect-sized; icon patterns are small tiles.</summary>
    public Texture2D GetTextureForRect(int width, int height, ZUIStyleSheetAsset sheet)
    {
        if (customTexture != null) return customTexture;
        if (patternType == ZUIPatternType.Icon)
        {
            // Icon: small tiled texture (fast)
            const int tileSize = 128;
            int h = ComputeHash(tileSize, tileSize, sheet);
            if (_cachedTex != null && _cachedHash == h) return _cachedTex;
            _cachedTex  = BuildProceduralTexture(tileSize, tileSize, sheet);
            _cachedHash = h;
            return _cachedTex;
        }
        // Analytical: rect-sized (seamless at any angle)
        width  = Mathf.Clamp(width, 4, 512);
        height = Mathf.Clamp(height, 4, 512);
        int hash = ComputeHash(width, height, sheet);
        if (_cachedTex != null && _cachedHash == hash) return _cachedTex;
        _cachedTex  = BuildProceduralTexture(width, height, sheet);
        _cachedHash = hash;
        return _cachedTex;
    }

    // Legacy — kept for any callers that don't pass a rect
    public Texture2D GetTexture(ZUIStyleSheetAsset sheet) => GetTextureForRect(128, 128, sheet);

    public void Invalidate() { _cachedTex = null; }

    int ComputeHash(int w, int h, ZUIStyleSheetAsset sheet)
    {
        unchecked
        {
            int hash = (int)patternType * 7919;
            hash = hash * 397 ^ tint.Resolve(sheet).GetHashCode();
            hash = hash * 397 ^ rotation.GetHashCode();
            hash = hash * 397 ^ scale.GetHashCode();
            hash = hash * 397 ^ w;
            hash = hash * 397 ^ h;
            if (!string.IsNullOrEmpty(iconId)) hash = hash * 397 ^ iconId.GetHashCode();
            hash = hash * 397 ^ offsetX.GetHashCode();
            hash = hash * 397 ^ offsetY.GetHashCode();
            return hash;
        }
    }

    Texture2D BuildProceduralTexture(int width, int height, ZUIStyleSheetAsset sheet)
    {
        bool tiled = patternType == ZUIPatternType.Icon;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
        };
        Color c = tint.Resolve(sheet);
        // Spacing in pixels — scale controls density
        float spacing = 8f * scale;
        float radius = spacing * 0.24f;

        float rad = rotation * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        // Resolve icon texture once before the pixel loop
        Texture2D iconTex = (patternType == ZUIPatternType.Icon) ? ResolveIconTexture() : null;

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // Project (x,y) onto rotated axes
            float d1 = x * cos + y * sin;
            float d2 = -x * sin + y * cos;

            float alpha = 0f;
            switch (patternType)
            {
                case ZUIPatternType.Stripes:
                case ZUIPatternType.Diagonal:
                    alpha = (Mathf.Repeat(d1 / spacing, 1f) < 0.5f) ? 1f : 0f;
                    break;
                case ZUIPatternType.Dots:
                    float dx = Mathf.Repeat(d1, spacing) - spacing * 0.5f;
                    float dy = Mathf.Repeat(d2, spacing) - spacing * 0.5f;
                    alpha = (dx * dx + dy * dy < radius * radius) ? 1f : 0f;
                    break;
                case ZUIPatternType.Grid:
                    float g1 = Mathf.Repeat(d1, spacing);
                    float g2 = Mathf.Repeat(d2, spacing);
                    alpha = (g1 < 1f || g2 < 1f) ? 1f : 0f;
                    break;
                case ZUIPatternType.Noise:
                    alpha = UnityEngine.Random.value;
                    break;
                case ZUIPatternType.Icon:
                {
                    if (iconTex != null)
                    {
                        int iw = iconTex.width, ih = iconTex.height;
                        // Icons per tile edge: scale 1 = 1 icon, scale 0.5 = 2, scale 0.25 = 4, etc.
                        // Round to integer count so icons always tile without partial draws
                        int iconsPerEdge = Mathf.Max(1, Mathf.RoundToInt(1f / scale));
                        float cellSize = (float)width / iconsPerEdge;
                        // Stagger: odd columns shift down by half a cell
                        int col = (int)Mathf.Floor(x / cellSize);
                        float staggerY = ((col % 2 + 2) % 2 == 1) ? cellSize * 0.5f : 0f;
                        // Find position within the grid cell (offset applied at draw time via tex coords)
                        float cx = Mathf.Repeat(x, cellSize);
                        float cy = Mathf.Repeat(y + staggerY, cellSize);
                        // Offset from cell center
                        float ox = cx - cellSize * 0.5f;
                        float oy = cy - cellSize * 0.5f;
                        // Rotate around cell center
                        float rx = ox * cos - oy * sin + cellSize * 0.5f;
                        float ry = ox * sin + oy * cos + cellSize * 0.5f;
                        // Map to icon pixel coords
                        int sx = (int)(rx / cellSize * iw);
                        int sy = (int)(ry / cellSize * ih);
                        // Outside the icon after rotation = transparent
                        if (sx >= 0 && sx < iw && sy >= 0 && sy < ih)
                        {
                            Color iconPixel = iconTex.GetPixel(sx, sy);
                            tex.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * iconPixel.a));
                        }
                        else
                        {
                            tex.SetPixel(x, y, new Color(c.r, c.g, c.b, 0f));
                        }
                        continue;
                    }
                    alpha = 0f;
                    break;
                }
            }
            tex.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * alpha));
        }
        tex.Apply();
        return tex;
    }

    [NonSerialized] private Texture2D _readableIcon;
    [NonSerialized] private string    _readableIconId;

    Texture2D ResolveIconTexture()
    {
#if UNITY_EDITOR
        Texture2D src = null;
        if (!string.IsNullOrEmpty(iconId))
            src = ZUIAssetLibrary.FindIcon(iconId);
        else
            src = customTexture;
        if (src == null) return null;

        // Return cached readable copy if still valid
        if (_readableIcon != null && _readableIconId == iconId) return _readableIcon;

        // Create a readable copy via RenderTexture blit
        var rt = UnityEngine.RenderTexture.GetTemporary(src.width, src.height, 0, UnityEngine.RenderTextureFormat.ARGB32);
        UnityEngine.Graphics.Blit(src, rt);
        var prev = UnityEngine.RenderTexture.active;
        UnityEngine.RenderTexture.active = rt;
        _readableIcon = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        _readableIcon.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        _readableIcon.Apply();
        UnityEngine.RenderTexture.active = prev;
        UnityEngine.RenderTexture.ReleaseTemporary(rt);
        _readableIconId = iconId;
        return _readableIcon;
#else
        return customTexture;
#endif
    }
}

// ── ZUIGradientStop ──────────────────────────────────────────────────────────
// A single color stop in a multi-stop gradient.

[Serializable]
public class ZUIGradientStop
{
    public ZUIColorRef color    = new ZUIColorRef(Color.white);
    public float       position = 0.5f;   // 0–1 along the gradient axis
    public float       easing   = 0.5f;   // bias for the transition TO this stop (0.5 = linear)

    public ZUIGradientStop() { }
    public ZUIGradientStop(ZUIColorRef color, float position, float easing = 0.5f)
    {
        this.color    = color;
        this.position = position;
        this.easing   = easing;
    }
}

// ── ZUIEdgeValues ────────────────────────────────────────────────────────────
// Represents 1, 2, or 4 edge values (like CSS shorthand: all / V+H / T+R+B+L).
// Mode 0 = uniform, 1 = vertical+horizontal, 2 = individual per edge.

[Serializable]
public class ZUIEdgeValues
{
    public int mode;  // 0=uniform, 1=VH, 2=individual
    public int all;
    public int v, h;
    public int top, right, bottom, left;

    public ZUIEdgeValues() { }
    public ZUIEdgeValues(int uniform) { all = uniform; }
    public ZUIEdgeValues(int v, int h) { mode = 1; this.v = v; this.h = h; all = v; }

    public int Top    => mode == 2 ? top    : mode == 1 ? v : all;
    public int Right  => mode == 2 ? right  : mode == 1 ? h : all;
    public int Bottom => mode == 2 ? bottom : mode == 1 ? v : all;
    public int Left   => mode == 2 ? left   : mode == 1 ? h : all;

    /// <summary>Initializes from legacy H/V pair if fields are at defaults.</summary>
    public void InitFromHV(int legacyH, int legacyV)
    {
        if (mode == 0 && all == 0 && v == 0 && h == 0)
        {
            if (legacyH == legacyV) { mode = 0; all = legacyH; }
            else { mode = 1; this.h = legacyH; this.v = legacyV; }
        }
    }

    /// <summary>Convenience: returns as RectOffset (top, right, bottom, left → left, right, top, bottom).</summary>
    public UnityEngine.RectOffset ToRectOffset()
        => new UnityEngine.RectOffset(Left, Right, Top, Bottom);
}

/// <summary>Float version of ZUIEdgeValues — used for border width and other sub-pixel values.</summary>
[Serializable]
public class ZUIEdgeValuesFloat
{
    public int   mode;  // 0=uniform, 1=VH, 2=individual
    public float all;
    public float v, h;
    public float top, right, bottom, left;

    public ZUIEdgeValuesFloat() { }
    public ZUIEdgeValuesFloat(float uniform) { all = uniform; }

    public float Top    => mode == 2 ? top    : mode == 1 ? v : all;
    public float Right  => mode == 2 ? right  : mode == 1 ? h : all;
    public float Bottom => mode == 2 ? bottom : mode == 1 ? v : all;
    public float Left   => mode == 2 ? left   : mode == 1 ? h : all;

    /// <summary>True if all edges resolve to the same value.</summary>
    public bool IsUniform => Top == Right && Right == Bottom && Bottom == Left;
}

// ── ZUIPaddingDef ────────────────────────────────────────────────────────────
// Padding and margin values. Used by both ZUIButtonDef and ZUIBoxDef.
// Fields that don't apply to a particular context (e.g. iconPad on boxes) are
// simply left at 0 — the editor hides them via the showIcon/showMargin flags.

[Serializable]
public class ZUIPaddingDef : ISerializationCallbackReceiver
{
    // ── New edge-based values ────────────────────────────────────────────────
    public ZUIEdgeValues pad    = new ZUIEdgeValues(8, 6);
    public ZUIEdgeValues iconPad = new ZUIEdgeValues();
    public ZUIEdgeValues margin  = new ZUIEdgeValues();

    // ── Legacy fields (kept for deserialization migration) ───────────────────
    [UnityEngine.HideInInspector] public int padH    = 8;
    [UnityEngine.HideInInspector] public int padV    = 6;
    [UnityEngine.HideInInspector] public int iconPadH = 0;
    [UnityEngine.HideInInspector] public int iconPadV = 0;
    [UnityEngine.HideInInspector] public int marginH  = 0;
    [UnityEngine.HideInInspector] public int marginV  = 0;
    [UnityEngine.HideInInspector] public int _paddingDefVersion = 0;

    // ── Convenience accessors (used by existing consumers) ─────────────────
    // These bridge old code that reads padH/padV to the new edge system.
    // For symmetric layouts they return H=Left and V=Top (which equal Right and Bottom in mode 0/1).
    public int PadH    => pad.Left;
    public int PadV    => pad.Top;
    public int PadLeft => pad.Left;
    public int PadRight => pad.Right;
    public int PadTop  => pad.Top;
    public int PadBottom => pad.Bottom;

    public int IconPadH => iconPad.Left;
    public int IconPadV => iconPad.Top;

    public int MarginH => margin.Left;
    public int MarginV => margin.Top;

    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize()
    {
        if (_paddingDefVersion == 0)
        {
            pad.InitFromHV(padH, padV);
            iconPad.InitFromHV(iconPadH, iconPadV);
            margin.InitFromHV(marginH, marginV);
            _paddingDefVersion = 1;
        }
    }
}
