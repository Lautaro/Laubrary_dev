// ZUIStyleDef.cs

using System;
using UnityEngine;
using UnityEngine.Serialization;

// ── Text Style ────────────────────────────────────────────────────────────────

[Serializable]
public class ZUITextDef : ISerializationCallbackReceiver
{
    // Text color — when gradientEnabled, color+colorB form a horizontal per-character gradient.
    public ZUIColorRef color        = new ZUIColorRef(new Color(.88f, .88f, .88f, 1f));
    public bool        gradientEnabled = false;
    public ZUIColorRef colorB       = new ZUIColorRef(new Color(.50f, .70f, 1f, 1f));
    public int         fontSize     = 0;    // 0 = inherit from skin
    public FontStyle   fontStyle    = FontStyle.Normal;

    public ZUITextShadowDef shadow = new ZUITextShadowDef();

    // Legacy flat shadow fields — migrated to ZUITextShadowDef in version 3
    [HideInInspector] public bool        shadowEnabled = false;
    [HideInInspector] public Vector2     shadowOffset  = new Vector2(1f, 1f);
    [HideInInspector] public ZUIColorRef shadowColor   = new ZUIColorRef(new Color(0f, 0f, 0f, 0.6f));

    public bool        outlineEnabled = false;
    public ZUIColorRef outlineColor   = new ZUIColorRef(new Color(0f, 0f, 0f, 0.8f));
    public int         outlineWidth   = 1;
    public int         outlinePasses  = 4;  // 4 or 8

    // ── Legacy fields (pre-ZUIColorRef) ──────────────────────────────────────
    [HideInInspector] public int _textDefVersion = 0;
    [HideInInspector] public Color          _legacyColor          = new Color(.88f, .88f, .88f, 1f);
    [HideInInspector] public Color          _legacyColorB         = new Color(.50f, .70f, 1f, 1f);
    [HideInInspector] public Color          _legacyShadowColor    = new Color(0f, 0f, 0f, 0.6f);
    [HideInInspector] public Color          _legacyOutlineColor   = new Color(0f, 0f, 0f, 0.8f);
    [HideInInspector][FormerlySerializedAs("colorRef")]       public string         _legacyColorRef       = "";
    [HideInInspector][FormerlySerializedAs("colorSlot")]      public ZUIPaletteSlot _legacyColorSlot      = ZUIPaletteSlot.Primary;
    [HideInInspector][FormerlySerializedAs("colorBRef")]      public string         _legacyColorBRef      = "";
    [HideInInspector][FormerlySerializedAs("colorBSlot")]     public ZUIPaletteSlot _legacyColorBSlot     = ZUIPaletteSlot.Primary;
    [HideInInspector][FormerlySerializedAs("shadowColorRef")] public string         _legacyShadowColorRef = "";
    [HideInInspector][FormerlySerializedAs("shadowColorSlot")]public ZUIPaletteSlot _legacyShadowColorSlot = ZUIPaletteSlot.Primary;
    [HideInInspector][FormerlySerializedAs("outlineColorRef")]public string         _legacyOutlineColorRef = "";
    [HideInInspector][FormerlySerializedAs("outlineColorSlot")]public ZUIPaletteSlot _legacyOutlineColorSlot = ZUIPaletteSlot.Primary;

    public Color GetResolvedColor(ZUIStyleSheetAsset sheet)        => color.Resolve(sheet);
    public Color GetResolvedColorB(ZUIStyleSheetAsset sheet)       => colorB.Resolve(sheet);
    public Color GetResolvedShadowColor(ZUIStyleSheetAsset sheet)  => shadow.GetResolvedColor(sheet);
    public Color GetResolvedOutlineColor(ZUIStyleSheetAsset sheet) => outlineColor.Resolve(sheet);

    public ZUITextDef() { _textDefVersion = 3; }
    public ZUITextDef(Color c) { color = new ZUIColorRef(c); _textDefVersion = 3; }

    public void Apply(GUIStyle s, ZUIStyleSheetAsset sheet)
    {
        s.normal.textColor = s.hover.textColor = s.active.textColor = GetResolvedColor(sheet);
        s.fontStyle = fontStyle;
        if (fontSize > 0) s.fontSize = fontSize;
    }

    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize()
    {
        if (_textDefVersion < 2)
        {
            bool legacyHasData = _legacyColor != default ||
                                 !string.IsNullOrEmpty(_legacyColorRef) ||
                                 _legacyShadowColor != default ||
                                 !string.IsNullOrEmpty(_legacyShadowColorRef);
            if (legacyHasData)
            {
                color        = ZUIColorRef.FromLegacy(_legacyColor,        _legacyColorRef,        _legacyColorSlot);
                colorB       = ZUIColorRef.FromLegacy(_legacyColorB,       _legacyColorBRef,       _legacyColorBSlot);
                shadowColor  = ZUIColorRef.FromLegacy(_legacyShadowColor,  _legacyShadowColorRef,  _legacyShadowColorSlot);
                outlineColor = ZUIColorRef.FromLegacy(_legacyOutlineColor, _legacyOutlineColorRef, _legacyOutlineColorSlot);
            }
            _textDefVersion = 2;
        }
        if (_textDefVersion < 3)
        {
            // Migrate flat shadow fields into ZUITextShadowDef
            shadow = new ZUITextShadowDef
            {
                enabled = shadowEnabled,
                offset  = shadowOffset,
                color   = shadowColor,
            };
            _textDefVersion = 3;
        }
    }
}

// ── Box Style ─────────────────────────────────────────────────────────────────
// Background drawn via ZUIColor.DrawRect — supports solid or gradient fill.
// GUIStyle carries only padding/margin so the layout engine can size the group.

[Serializable]
public class ZUIBoxDef : ISerializationCallbackReceiver
{
    public string      name             = "New Box Style";

    /// <summary>The sheet that owns this def. Wired by ZUIStyleSheetAsset.WireOwnerSheet().
    /// Used for sheet-bound palette and reference resolution (box-style refs, global defaults,
    /// text-style refs). Never falls back to ambient ZUI.ActiveSheet — that was the leak we
    /// eliminated. When ownerSheet is null, palette resolution falls back to the inline color.</summary>
    [System.NonSerialized] public ZUIStyleSheetAsset ownerSheet;
    public ZUIColor background       = new ZUIColor(new Color(.20f, .20f, .24f, 1f));
    public ZUITextDef  titleText        = new ZUITextDef(new Color(.90f, .90f, .90f, 1f));
    public ZUITextDef  contentText      = new ZUITextDef(new Color(.80f, .80f, .80f, 1f));

    public ZUIBorderDef    border    = new ZUIBorderDef();
    public ZUIDropShadowDef bgShadow = new ZUIDropShadowDef();
    public ZUIGlowDef      glow     = new ZUIGlowDef();
    public ZUIShapeDef     shape     = new ZUIShapeDef();
    public ZUIPaddingDef   padding   = new ZUIPaddingDef { padH = 8, padV = 6, marginH = 4, marginV = 4 };

    // ── Overlay gradient (drawn on top of background, below content) ─────────
    public bool        overlayEnabled = false;
    public ZUIColor overlay        = new ZUIColor(new Color(1f, 1f, 1f, 0.05f));

    public ZUIPatternDef pattern = new ZUIPatternDef();

    // ── Legacy padding fields (pre-ZUIPaddingDef) ────────────────────────────
    [HideInInspector][FormerlySerializedAs("padH")]    public int _legacyPadH    = 8;
    [HideInInspector][FormerlySerializedAs("padV")]    public int _legacyPadV    = 6;
    [HideInInspector][FormerlySerializedAs("marginH")] public int _legacyMarginH = 4;
    [HideInInspector][FormerlySerializedAs("marginV")] public int _legacyMarginV = 4;

    // Optional: use a named ZUITextStyleDef from the sheet instead of the inline def.
    public string      titleTextStyleId   = "";
    public string      contentTextStyleId = "";

    // Global override flags
    public bool useGlobalBorder      = false;
    public bool useGlobalPadding     = false;
    public bool useGlobalShape       = false;
    public bool useGlobalBackground  = false;
    public bool useGlobalTitleText   = false;
    public bool useGlobalContentText = false;

    // ── Section visibility ──
    public bool showBackground  = true;
    public bool showBorder      = true;
    public bool showTitleText   = true;
    public bool showContentText = true;
    public bool showShape       = true;
    public bool showPadding     = true;
    public bool showShadow      = false;
    public bool showPreview     = true;

    // Backward compat — routes through titleText
    public Color labelColor { get => titleText.color.color; set => titleText.color = new ZUIColorRef(value); }

    // ── Serialization migration ───────────────────────────────────────────────
    // Version 0 = old flat-field layout; version 1 = struct layout.
    // Fields prefixed _legacy_ are kept only for migration and hidden in the inspector.

    [HideInInspector] public int _defVersion = 0;

    [HideInInspector] public Color  _legacyBorderColor      = new Color(1f, 1f, 1f, 0.06f);
    [HideInInspector] public Color  _legacyBorderColorEnd   = new Color(0f, 0f, 0f, 0.10f);
    [HideInInspector] public bool   _legacyIsBorderGradient = false;
    [HideInInspector] public float  _legacyBorderGradientAngle = 135f;
    [HideInInspector] public float  _legacyBorderWidth      = 1f;
    [HideInInspector] public string _legacyBorderColorRef      = "";
    [HideInInspector] public int    _legacyBorderColorSlot     = 0;
    [HideInInspector] public string _legacyBorderColorEndRef   = "";
    [HideInInspector] public int    _legacyBorderColorEndSlot  = 0;

    [HideInInspector] public bool    _legacyBgShadowEnabled  = false;
    [HideInInspector] public Vector2 _legacyBgShadowOffset   = new Vector2(3f, 3f);
    [HideInInspector] public Color   _legacyBgShadowColor    = new Color(0f, 0f, 0f, 0.35f);
    [HideInInspector] public string  _legacyBgShadowColorRef = "";
    [HideInInspector] public int     _legacyBgShadowColorSlot = 0;

    [HideInInspector] public int  _legacyCornerRadius = 0;
    [HideInInspector] public bool _legacyRoundTL = true;
    [HideInInspector] public bool _legacyRoundTR = true;
    [HideInInspector] public bool _legacyRoundBL = true;
    [HideInInspector] public bool _legacyRoundBR = true;

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        if (_defVersion == 0)
        {
            // Migrate flat border fields into ZUIBorderDef's own legacy fields
            // so its own OnAfterDeserialize can pick them up.
            border.colorA        = _legacyBorderColor;
            border.colorB        = _legacyBorderColorEnd;
            border.isGradient    = _legacyIsBorderGradient;
            border.gradientAngle = _legacyBorderGradientAngle;
            border.width         = _legacyBorderWidth;
            border.colorARef     = _legacyBorderColorRef;
            border.colorASlot    = (ZUIPaletteSlot)_legacyBorderColorSlot;
            border.colorBRef     = _legacyBorderColorEndRef;
            border.colorBSlot    = (ZUIPaletteSlot)_legacyBorderColorEndSlot;
            border._borderDefVersion = 0; // force ZUIBorderDef migration
            border.OnAfterDeserialize();

            bgShadow.enabled = _legacyBgShadowEnabled;
            bgShadow.offset  = _legacyBgShadowOffset;
            bgShadow.tint    = ZUIColorRef.FromLegacy(_legacyBgShadowColor, _legacyBgShadowColorRef, (ZUIPaletteSlot)_legacyBgShadowColorSlot);
            bgShadow._shadowDefVersion = 2;

            shape.cornerRadius = _legacyCornerRadius;
            shape.roundTL      = _legacyRoundTL;
            shape.roundTR      = _legacyRoundTR;
            shape.roundBL      = _legacyRoundBL;
            shape.roundBR      = _legacyRoundBR;

            _defVersion = 1;
        }
        if (_defVersion < 2)
        {
            padding.padH    = _legacyPadH;
            padding.padV    = _legacyPadV;
            padding.marginH = _legacyMarginH;
            padding.marginV = _legacyMarginV;
            _defVersion = 2;
        }
    }

    // ── Constructors ──────────────────────────────────────────────────────────

    public ZUIBoxDef() { }

    public ZUIBoxDef(string name, Color bgColor, Color labelColor)
    {
        this.name       = name;
        background      = new ZUIColor(bgColor);
        this.labelColor = labelColor;
    }

    public ZUIBoxDef(string name, Color bgColor, Color labelColor,
                     Color borderColor, float borderWidth, int padH, int padV)
    {
        this.name        = name;
        background       = new ZUIColor(bgColor);
        this.labelColor  = labelColor;
        border           = new ZUIBorderDef(borderColor, borderWidth);
        padding.pad = new ZUIEdgeValues(padV, padH);
        _defVersion      = 2;
    }

    public ZUIBoxDef(string name, ZUIColor background, Color labelColor,
                     Color borderColor, float borderWidth, int padH, int padV)
    {
        this.name        = name;
        this.background  = background;
        this.labelColor  = labelColor;
        border           = new ZUIBorderDef(borderColor, borderWidth);
        padding.pad = new ZUIEdgeValues(padV, padH);
        _defVersion      = 1;
    }

    // ── Resolved values ───────────────────────────────────────────────────────

    public int GetResolvedCornerRadius()
    {
#if UNITY_EDITOR
        if (useGlobalShape)
        {
            var g = ownerSheet?.globalBox;
            if (g != null) return g.shape.cornerRadius;
        }
#endif
        return shape.cornerRadius;
    }

    public Vector4 GetCornerVector(float r)
    {
#if UNITY_EDITOR
        if (useGlobalShape)
            return new Vector4(r, r, r, r);
#endif
        return shape.GetCornerVector(r);
    }

    public ZUIColor GetResolvedBackground()
    {
#if UNITY_EDITOR
        if (useGlobalBackground) { var g = ownerSheet?.globalBox; if (g != null) return g.background; }
#endif
        return background;
    }

    public ZUITextDef GetResolvedTitleText()
    {
#if UNITY_EDITOR
        if (useGlobalTitleText) { var g = ownerSheet?.globalBox; if (g != null) return g.titleText; }
        if (!string.IsNullOrEmpty(titleTextStyleId)) { var s = ownerSheet?.FindText(titleTextStyleId); if (s != null) return s.text; }
#endif
        return titleText;
    }

    public ZUITextDef GetResolvedContentText()
    {
#if UNITY_EDITOR
        if (useGlobalContentText) { var g = ownerSheet?.globalBox; if (g != null) return g.contentText; }
        if (!string.IsNullOrEmpty(contentTextStyleId)) { var s = ownerSheet?.FindText(contentTextStyleId); if (s != null) return s.text; }
#endif
        return contentText;
    }

    public ZUIBorderDef GetResolvedBorder()
    {
        if (useGlobalBorder)
        {
            var g = ownerSheet?.globalBox;
            if (g != null) return g.border;
        }
        return border;
    }

    // ── Layout GUIStyle ───────────────────────────────────────────────────────

    [NonSerialized] private GUIStyle _layoutStyle;

    public GUIStyle GetLayoutStyle()
    {
        if (_layoutStyle != null) return _layoutStyle;

        var p = padding;
#if UNITY_EDITOR
        if (useGlobalPadding)
        {
            var g = ownerSheet?.globalBox;
            if (g != null) p = g.padding;
        }
#endif
        _layoutStyle = new GUIStyle(GUIStyle.none)
        {
            padding = new RectOffset(p.PadLeft, p.PadRight, p.PadTop, p.PadBottom),
            margin  = p.margin.ToRectOffset(),
        };
        return _layoutStyle;
    }

    public void Invalidate() { _layoutStyle = null; _contentStyle = null; _titleStyleCached = null; background.Invalidate(); border.color.Invalidate(); }

    // ── Content text GUIStyle ─────────────────────────────────────────────────

    [NonSerialized] private GUIStyle _contentStyle;

    public GUIStyle GetContentStyle()
    {
        if (_contentStyle != null) return _contentStyle;
        _contentStyle = new GUIStyle(GUI.skin.label);
        GetResolvedContentText().Apply(_contentStyle, ownerSheet);
        _contentStyle.wordWrap = true;
        return _contentStyle;
    }

    // ── Title text GUIStyle ───────────────────────────────────────────────────

    [NonSerialized] private GUIStyle _titleStyleCached;

    /// <summary>Returns a GUIStyle built from the box's resolved titleText.
    /// Cached; invalidated by ZUIBoxDef.Invalidate.</summary>
    public GUIStyle GetTitleStyle()
    {
        if (_titleStyleCached != null) return _titleStyleCached;
        _titleStyleCached = new GUIStyle(GUI.skin.label);
        GetResolvedTitleText().Apply(_titleStyleCached, ownerSheet);
        _titleStyleCached.wordWrap = true;
        return _titleStyleCached;
    }

    // ── Background draw ───────────────────────────────────────────────────────

    public void DrawBackground(Rect rect)
    {
#if UNITY_EDITOR
        // Editor-only style-debug (right-click-to-inspect). Hook is unset at runtime → no-op.
        ZUICore.BoxDebugHook?.Invoke(this, name, rect);
#endif

        if (UnityEngine.Event.current.type != UnityEngine.EventType.Repaint) return;
        if (rect.width <= 1f) return;

        var resolvedBorder = GetResolvedBorder();
        Color bc1 = resolvedBorder.color.GetColorA(ownerSheet);
        Color bc2 = resolvedBorder.color.GetColorB(ownerSheet);
        var   ew  = resolvedBorder.edgeWidth;
        float bw  = ew.Top; // uniform fallback for rounded corners
        bool  bg2 = resolvedBorder.color.isGradient;

        int cr = GetResolvedCornerRadius();
        ZUIButtonDef.DrawGlowEffect(glow, rect, cr, ownerSheet);
        ZUIButtonDef.DrawShadowEffect(bgShadow, rect, cr, ownerSheet);

#if UNITY_2021_2_OR_NEWER
        if (cr > 0 && bw > 0f && bc1.a > 0f)
        {
            // Rounded corners: use max edge width (per-edge not supported with rounded corners)
            float bwMax = Mathf.Max(ew.Top, Mathf.Max(ew.Right, Mathf.Max(ew.Bottom, ew.Left)));
            float r     = Mathf.Min(cr, rect.width * 0.5f, rect.height * 0.5f);
            var   crVec = GetCornerVector(r);
            resolvedBorder.color.DrawRect(rect, ownerSheet, crVec);
            var   inner = new Rect(rect.x + bwMax, rect.y + bwMax, rect.width - bwMax * 2f, rect.height - bwMax * 2f);
            float ir    = Mathf.Max(0f, r - bwMax);
            GetResolvedBackground().DrawRect(inner, ownerSheet, GetCornerVector(ir));
            if (overlayEnabled) overlay.DrawRect(inner, ownerSheet, GetCornerVector(ir));
            ZUIButtonDef.DrawPatternEffect(pattern, inner, GetCornerVector(ir), ownerSheet);
            ZUIButtonDef.DrawInnerGlowEffect(glow, inner, Mathf.RoundToInt(ir), ownerSheet);
            return;
        }
#endif

        // Flat borders: per-edge width supported
        float bT = ew.Top, bR = ew.Right, bB = ew.Bottom, bL = ew.Left;
        bool hasBorder = (bT > 0f || bR > 0f || bB > 0f || bL > 0f) && bc1.a > 0f;

        if (hasBorder && bg2)
        {
            // Gradient border: draw border gradient as full rect, then fill on top (inset)
            resolvedBorder.color.DrawRect(rect, ownerSheet);
            var inner = new Rect(rect.x + bL, rect.y + bT, rect.width - bL - bR, rect.height - bT - bB);
            GetResolvedBackground().DrawRect(inner, ownerSheet);
            if (overlayEnabled) overlay.DrawRect(inner, ownerSheet);
            ZUIButtonDef.DrawPatternEffect(pattern, inner, Vector4.zero, ownerSheet);
            ZUIButtonDef.DrawInnerGlowEffect(glow, inner, 0, ownerSheet);
        }
        else
        {
            GetResolvedBackground().DrawRect(rect, ownerSheet, GetCornerVector(cr));
            if (overlayEnabled) overlay.DrawRect(rect, ownerSheet, GetCornerVector(cr));
            ZUIButtonDef.DrawPatternEffect(pattern, rect, GetCornerVector(cr), ownerSheet);
            ZUIButtonDef.DrawInnerGlowEffect(glow, rect, cr, ownerSheet);

            if (hasBorder)
            {
                if (bT > 0f && bc1.a > 0f) ZUIColor.FillRectRuntime(new Rect(rect.x,        rect.y,         rect.width, bT),          bc1);
                if (bB > 0f && bc1.a > 0f) ZUIColor.FillRectRuntime(new Rect(rect.x,        rect.yMax - bB, rect.width, bB),          bc1);
                if (bL > 0f && bc1.a > 0f) ZUIColor.FillRectRuntime(new Rect(rect.x,        rect.y,         bL,         rect.height), bc1);
                if (bR > 0f && bc1.a > 0f) ZUIColor.FillRectRuntime(new Rect(rect.xMax - bR, rect.y,        bR,         rect.height), bc1);
            }
        }

#if UNITY_EDITOR
        // Editor-only style-flash overlay. Hook is unset at runtime → no-op.
        ZUICore.BoxFlashHook?.Invoke(rect, name, cr);
#endif
    }
}

// ── Button draw-state enum ────────────────────────────────────────────────────

public enum ZUIButtonDrawState { Normal, Hover, Active, ToggleOn }

// ── Icon placement ────────────────────────────────────────────────────────────

public enum ZIconPlacement
{
    LeftOfLabel,
    RightOfLabel,
    LeftEdge,
    RightEdge,
}

// ── Button Style ──────────────────────────────────────────────────────────────

[Serializable]
public class ZUIButtonDef : ISerializationCallbackReceiver
{
    public string      name     = "New Button Style";

    /// <summary>The sheet that owns this def. Set by ZUIStyleSheetAsset when defs are accessed.
    /// Used for style resolution (box refs, global defaults) so defs always resolve against
    /// their own sheet, never the transient ZUI.ActiveSheet.</summary>
    [System.NonSerialized] public ZUIStyleSheetAsset ownerSheet;

    // ── Normal state ──────────────────────────────────────────────────────────
    public ZUIColor   normal = new ZUIColor(new Color(.22f, .22f, .26f, 1f));
    public ZUITextDef    text   = new ZUITextDef(new Color(.88f, .88f, .88f, 1f));
    public ZUIBorderDef  border = new ZUIBorderDef(new Color(1f, 1f, 1f, 0f), 0f);

    public ZUIShapeDef   shape   = new ZUIShapeDef();
    public ZUIPaddingDef padding = new ZUIPaddingDef { padH = 10, padV = 3, iconPadH = 3, iconPadV = 3 };

    // ── Legacy shape fields (pre-ZUIShapeDef) ────────────────────────────────
    [HideInInspector][FormerlySerializedAs("cornerRadius")] public int  _legacyCornerRadius2 = 0;
    [HideInInspector][FormerlySerializedAs("roundTL")]      public bool _legacyRoundTL2 = true;
    [HideInInspector][FormerlySerializedAs("roundTR")]      public bool _legacyRoundTR2 = true;
    [HideInInspector][FormerlySerializedAs("roundBL")]      public bool _legacyRoundBL2 = true;
    [HideInInspector][FormerlySerializedAs("roundBR")]      public bool _legacyRoundBR2 = true;

    // ── Legacy padding fields (pre-ZUIPaddingDef) ────────────────────────────
    [HideInInspector][FormerlySerializedAs("padH")]     public int _legacyPadH3     = 10;
    [HideInInspector][FormerlySerializedAs("padV")]     public int _legacyPadV3     = 3;
    [HideInInspector][FormerlySerializedAs("iconPadH")] public int _legacyIconPadH3 = 3;
    [HideInInspector][FormerlySerializedAs("iconPadV")] public int _legacyIconPadV3 = 3;

    // Optional: use a named ZUITextStyleDef from the sheet instead of the inline def.
    public string textStyleId         = "";
    public string hoverTextStyleId    = "";
    public string activeTextStyleId   = "";
    public string toggleOnTextStyleId = "";

    // ── Box style base (when set, the button inherits visuals from a ZUIBoxDef) ──
    // Per-section overrides: false = inherit from box, true = use button's own values
    [FormerlySerializedAs("boxStyleBg")]
    public string boxStyle           = "";
    public bool   boxOverrideBg      = false;
    public bool   boxOverrideBorder  = false;
    public bool   boxOverrideText    = false;
    public bool   boxOverrideShape   = false;
    public bool   boxOverridePadding = false;
    public bool   boxOverrideShadow  = false;

    /// <summary>Resolves the box style reference. Returns null if no box style is set or not found.</summary>
    public ZUIBoxDef ResolveBoxStyle()
    {
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(boxStyle))
            return ownerSheet?.boxes?.Find(b => b.name == boxStyle);
#endif
        return null;
    }

    public bool HasBoxStyle => !string.IsNullOrEmpty(boxStyle);

    public ZUIBoxDef ResolveHoverBoxStyle()
    {
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(hoverBoxStyle))
            return ownerSheet?.boxes?.Find(b => b.name == hoverBoxStyle);
#endif
        return null;
    }

    public ZUIBoxDef ResolveActiveBoxStyle()
    {
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(activeBoxStyle))
            return ownerSheet?.boxes?.Find(b => b.name == activeBoxStyle);
#endif
        return null;
    }

    public ZUIBoxDef ResolveToggleOnBoxStyle()
    {
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(toggleOnBoxStyle))
            return ownerSheet?.boxes?.Find(b => b.name == toggleOnBoxStyle);
#endif
        return null;
    }

    public ZUIDropShadowDef bgShadow = new ZUIDropShadowDef { offset = new Vector2(2f, 2f), tint = new ZUIColorRef(new Color(0f, 0f, 0f, 0.4f)) };
    public ZUIGlowDef      glow     = new ZUIGlowDef();

    // ── Overlay gradient (drawn on top of fill, below label) ─────────────────
    public bool        overlayEnabled = false;
    public ZUIColor overlay        = new ZUIColor(new Color(1f, 1f, 1f, 0.1f));

    public ZUIPatternDef pattern = new ZUIPatternDef();

    // ── Hover state ───────────────────────────────────────────────────────────
    public string      hoverBoxStyle       = "";
    public bool        hoverBgOverride     = true;
    public ZUIColor hover               = new ZUIColor(new Color(.30f, .30f, .36f, 1f));
    public bool        hoverTextOverride   = false;
    public ZUITextDef  hoverText           = new ZUITextDef(new Color(.88f, .88f, .88f, 1f));
    public bool        hoverBorderOverride = false;
    public ZUIBorderDef hoverBorder        = new ZUIBorderDef(new Color(1f, 1f, 1f, 0f), 0f);

    // ── Active state ──────────────────────────────────────────────────────────
    public string      activeBoxStyle       = "";
    public bool        activeBgOverride     = true;
    public ZUIColor active               = new ZUIColor(new Color(.16f, .16f, .20f, 1f));
    public bool        activeTextOverride   = false;
    public ZUITextDef  activeText           = new ZUITextDef(new Color(.88f, .88f, .88f, 1f));
    public bool        activeBorderOverride = false;
    public ZUIBorderDef activeBorder        = new ZUIBorderDef(new Color(1f, 1f, 1f, 0f), 0f);

    // ── ToggleOn state ────────────────────────────────────────────────────────
    // Persistent "on" state for toggles. Overrides default to false so existing
    // toggles keep their current look (fall back to Active) until this state is
    // explicitly styled in the sheet editor.
    public string      toggleOnBoxStyle       = "";
    public bool        toggleOnBgOverride     = false;
    public ZUIColor toggleOn               = new ZUIColor(new Color(.20f, .38f, .55f, 1f));
    public bool        toggleOnTextOverride   = false;
    public ZUITextDef  toggleOnText           = new ZUITextDef(new Color(.88f, .88f, .88f, 1f));
    public bool        toggleOnBorderOverride = false;
    public ZUIBorderDef toggleOnBorder        = new ZUIBorderDef(new Color(1f, 1f, 1f, 0f), 0f);

    // ── Global override flags ─────────────────────────────────────────────────
    public bool useGlobalShape      = false;
    public bool useGlobalPadding    = false;
    public bool useGlobalBorder     = false;
    public bool useGlobalBackground = false;
    public bool useGlobalText       = false;

    // ── Hover / click animations ──────────────────────────────────────────────
    public ZUIAnimMode  animMode           = ZUIAnimMode.Crossfade;
    public bool         hoverAnimEnabled   = false;
    public float        hoverInDuration    = 0.12f;
    public float        hoverOutDuration   = 0.20f;
    public ZUIEaseCurve hoverInEase        = ZUIEaseCurve.EaseOutQuad;
    public ZUIEaseCurve hoverOutEase       = ZUIEaseCurve.EaseOutQuad;
    public bool         clickAnimEnabled   = false;
    public float        clickInDuration    = 0.06f;
    public float        clickOutDuration   = 0.20f;
    public ZUIEaseCurve clickInEase        = ZUIEaseCurve.EaseOutCubic;
    public ZUIEaseCurve clickOutEase       = ZUIEaseCurve.EaseOutCubic;

    public bool previewAsToggle = false;

    // ── Section visibility (editor-only, controls which sections show in the style editor) ──
    public bool showBackground = true;
    public bool showBorder     = true;
    public bool showText       = true;
    public bool showShape      = true;
    public bool showPadding    = true;
    public bool showShadow     = false;
    public bool showAnimation  = false;
    public bool showPreview    = true;

    // Backward compat
    public Color textColor { get => text.color.color; set => text.color = new ZUIColorRef(value); }

    // ── Serialization migration ───────────────────────────────────────────────

    [HideInInspector] public int _defVersion = 0;

    // Normal border legacy
    [HideInInspector] public Color  _legacyBorderColor          = new Color(1f, 1f, 1f, 0f);
    [HideInInspector] public Color  _legacyBorderColorEnd       = new Color(0f, 0f, 0f, 0.10f);
    [HideInInspector] public bool   _legacyIsBorderGradient     = false;
    [HideInInspector] public float  _legacyBorderGradientAngle  = 135f;
    [HideInInspector] public float  _legacyBorderWidth          = 0f;
    [HideInInspector] public string _legacyBorderColorRef       = "";
    [HideInInspector] public int    _legacyBorderColorSlot      = 0;
    [HideInInspector] public string _legacyBorderColorEndRef    = "";
    [HideInInspector] public int    _legacyBorderColorEndSlot   = 0;
    // Hover border legacy
    [HideInInspector] public Color  _legacyHoverBorderColor        = new Color(1f, 1f, 1f, 0f);
    [HideInInspector] public Color  _legacyHoverBorderColorEnd     = new Color(0f, 0f, 0f, 0.10f);
    [HideInInspector] public bool   _legacyHoverIsBorderGrad       = false;
    [HideInInspector] public float  _legacyHoverBorderWidth        = 0f;
    [HideInInspector] public string _legacyHoverBorderColorRef     = "";
    [HideInInspector] public int    _legacyHoverBorderColorSlot    = 0;
    [HideInInspector] public string _legacyHoverBorderColorEndRef  = "";
    [HideInInspector] public int    _legacyHoverBorderColorEndSlot = 0;
    // Active border legacy
    [HideInInspector] public Color  _legacyActiveBorderColor        = new Color(1f, 1f, 1f, 0f);
    [HideInInspector] public Color  _legacyActiveBorderColorEnd     = new Color(0f, 0f, 0f, 0.10f);
    [HideInInspector] public bool   _legacyActiveIsBorderGrad       = false;
    [HideInInspector] public float  _legacyActiveBorderWidth        = 0f;
    [HideInInspector] public string _legacyActiveBorderColorRef     = "";
    [HideInInspector] public int    _legacyActiveBorderColorSlot    = 0;
    [HideInInspector] public string _legacyActiveBorderColorEndRef  = "";
    [HideInInspector] public int    _legacyActiveBorderColorEndSlot = 0;
    // Shadow legacy
    [HideInInspector] public bool    _legacyBgShadowEnabled   = false;
    [HideInInspector] public Vector2 _legacyBgShadowOffset    = new Vector2(2f, 2f);
    [HideInInspector] public Color   _legacyBgShadowColor     = new Color(0f, 0f, 0f, 0.4f);
    [HideInInspector] public string  _legacyBgShadowColorRef  = "";
    [HideInInspector] public int     _legacyBgShadowColorSlot = 0;

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        if (_defVersion == 0)
        {
            // Populate ZUIBorderDef legacy fields so its own migration runs correctly.
            void MigrateBorder(ZUIBorderDef b, Color ca, Color cb, bool grad, float gradAngle, float w,
                               string caRef, int caSlot, string cbRef, int cbSlot)
            {
                b.colorA = ca; b.colorB = cb; b.isGradient = grad; b.gradientAngle = gradAngle;
                b.width = w; b.colorARef = caRef; b.colorASlot = (ZUIPaletteSlot)caSlot;
                b.colorBRef = cbRef; b.colorBSlot = (ZUIPaletteSlot)cbSlot;
                b._borderDefVersion = 0;
                b.OnAfterDeserialize();
            }

            MigrateBorder(border,
                _legacyBorderColor, _legacyBorderColorEnd, _legacyIsBorderGradient, _legacyBorderGradientAngle,
                _legacyBorderWidth, _legacyBorderColorRef, _legacyBorderColorSlot,
                _legacyBorderColorEndRef, _legacyBorderColorEndSlot);

            MigrateBorder(hoverBorder,
                _legacyHoverBorderColor, _legacyHoverBorderColorEnd, _legacyHoverIsBorderGrad, 135f,
                _legacyHoverBorderWidth, _legacyHoverBorderColorRef, _legacyHoverBorderColorSlot,
                _legacyHoverBorderColorEndRef, _legacyHoverBorderColorEndSlot);

            MigrateBorder(activeBorder,
                _legacyActiveBorderColor, _legacyActiveBorderColorEnd, _legacyActiveIsBorderGrad, 135f,
                _legacyActiveBorderWidth, _legacyActiveBorderColorRef, _legacyActiveBorderColorSlot,
                _legacyActiveBorderColorEndRef, _legacyActiveBorderColorEndSlot);

            bgShadow.enabled = _legacyBgShadowEnabled;
            bgShadow.offset  = _legacyBgShadowOffset;
            bgShadow.tint    = ZUIColorRef.FromLegacy(_legacyBgShadowColor, _legacyBgShadowColorRef, (ZUIPaletteSlot)_legacyBgShadowColorSlot);
            bgShadow._shadowDefVersion = 2;

            _defVersion = 1;
        }
        if (_defVersion < 2)
        {
            shape.cornerRadius = _legacyCornerRadius2;
            shape.roundTL      = _legacyRoundTL2;
            shape.roundTR      = _legacyRoundTR2;
            shape.roundBL      = _legacyRoundBL2;
            shape.roundBR      = _legacyRoundBR2;
            _defVersion = 2;
        }
        if (_defVersion < 3)
        {
            padding.padH     = _legacyPadH3;
            padding.padV     = _legacyPadV3;
            padding.iconPadH = _legacyIconPadH3;
            padding.iconPadV = _legacyIconPadV3;
            _defVersion = 3;
        }
    }

    // ── Constructors ──────────────────────────────────────────────────────────

    public ZUIButtonDef() { }

    public ZUIButtonDef(string name, Color normalBg, Color hoverBg, Color activeBg, Color textColor)
    {
        this.name      = name;
        normal         = new ZUIColor(normalBg);
        hover          = new ZUIColor(hoverBg);
        active         = new ZUIColor(activeBg);
        this.textColor = textColor;
        _defVersion    = 3;
    }

    public ZUIButtonDef(string name, ZUIColor normal, ZUIColor hover, ZUIColor active,
                        Color textColor, int cornerRadius = 0)
    {
        this.name         = name;
        this.normal       = normal;
        this.hover        = hover;
        this.active       = active;
        this.textColor         = textColor;
        shape.cornerRadius     = cornerRadius;
        _defVersion            = 3;
    }

    // ── Resolved values ───────────────────────────────────────────────────────

    public int GetResolvedCornerRadius()
    {
#if UNITY_EDITOR
        if (!boxOverrideShape) { var b = ResolveBoxStyle(); if (b != null) return b.shape.cornerRadius; }
        if (useGlobalShape) { var g = ownerSheet?.globalButton; if (g != null) return g.shape.cornerRadius; }
#endif
        return shape.cornerRadius;
    }

    /// <summary>Returns the resolved per-corner rounding flags, respecting box style and useGlobalShape.</summary>
    public (bool tl, bool tr, bool bl, bool br) GetResolvedCornerFlags()
    {
#if UNITY_EDITOR
        if (!boxOverrideShape) { var b = ResolveBoxStyle(); if (b != null) return (b.shape.roundTL, b.shape.roundTR, b.shape.roundBL, b.shape.roundBR); }
        if (useGlobalShape)
        {
            var g = ownerSheet?.globalButton;
            if (g != null) return (g.shape.roundTL, g.shape.roundTR, g.shape.roundBL, g.shape.roundBR);
        }
#endif
        return (shape.roundTL, shape.roundTR, shape.roundBL, shape.roundBR);
    }

    public Vector4 GetCornerVector(float r)
    {
        var (tl, tr, bl, br) = GetResolvedCornerFlags();
        return new Vector4(tl ? r : 0f, tr ? r : 0f, br ? r : 0f, bl ? r : 0f);
    }

    public ZUIColor GetNormalColor()
    {
#if UNITY_EDITOR
        if (!boxOverrideBg) { var b = ResolveBoxStyle(); if (b != null) return b.GetResolvedBackground(); }
        if (useGlobalBackground) { var g = ownerSheet?.globalButton; if (g != null) return g.normal; }
#endif
        return normal;
    }

    public ZUITextDef GetNormalText()
    {
#if UNITY_EDITOR
        if (!boxOverrideText) { var b = ResolveBoxStyle(); if (b != null) return b.GetResolvedTitleText(); }
        if (useGlobalText) { var g = ownerSheet?.globalButton; if (g != null) return g.text; }
        if (!string.IsNullOrEmpty(textStyleId)) { var s = ownerSheet?.FindText(textStyleId); if (s != null) return s.text; }
#endif
        return text;
    }

    public ZUIColor GetHoverColor()
    {
        var hb = ResolveHoverBoxStyle();
        if (hb != null) return hb.GetResolvedBackground();
        return hoverBgOverride ? hover : GetNormalColor();
    }
    public ZUIColor GetActiveColor()
    {
        var ab = ResolveActiveBoxStyle();
        if (ab != null) return ab.GetResolvedBackground();
        return activeBgOverride ? active : GetHoverColor();
    }
    public ZUIColor GetToggleOnColor()
    {
        var tb = ResolveToggleOnBoxStyle();
        if (tb != null) return tb.GetResolvedBackground();
        return toggleOnBgOverride ? toggleOn : GetActiveColor();
    }
    public ZUIColor GetColor(ZUIButtonDrawState s) =>
        s == ZUIButtonDrawState.ToggleOn ? GetToggleOnColor() :
        s == ZUIButtonDrawState.Active   ? GetActiveColor()   :
        s == ZUIButtonDrawState.Hover    ? GetHoverColor()    : GetNormalColor();

    public ZUITextDef GetHoverText()
    {
#if UNITY_EDITOR
        var hb = ResolveHoverBoxStyle();
        if (hb != null) return hb.GetResolvedTitleText();
        if (!string.IsNullOrEmpty(hoverTextStyleId)) { var s = ownerSheet?.FindText(hoverTextStyleId); if (s != null) return s.text; }
#endif
        return hoverTextOverride ? hoverText : GetNormalText();
    }

    public ZUITextDef GetActiveText()
    {
#if UNITY_EDITOR
        var ab = ResolveActiveBoxStyle();
        if (ab != null) return ab.GetResolvedTitleText();
        if (!string.IsNullOrEmpty(activeTextStyleId)) { var s = ownerSheet?.FindText(activeTextStyleId); if (s != null) return s.text; }
#endif
        return activeTextOverride ? activeText : GetHoverText();
    }

    public ZUITextDef GetToggleOnText()
    {
#if UNITY_EDITOR
        var tb = ResolveToggleOnBoxStyle();
        if (tb != null) return tb.GetResolvedTitleText();
        if (!string.IsNullOrEmpty(toggleOnTextStyleId)) { var s = ownerSheet?.FindText(toggleOnTextStyleId); if (s != null) return s.text; }
#endif
        return toggleOnTextOverride ? toggleOnText : GetActiveText();
    }

    public ZUITextDef GetText(ZUIButtonDrawState s) =>
        s == ZUIButtonDrawState.ToggleOn ? GetToggleOnText() :
        s == ZUIButtonDrawState.Active   ? GetActiveText()   :
        s == ZUIButtonDrawState.Hover    ? GetHoverText()    : GetNormalText();

    public ZUIBorderDef GetNormalBorder()
    {
#if UNITY_EDITOR
        if (!boxOverrideBorder) { var b = ResolveBoxStyle(); if (b != null) return b.GetResolvedBorder(); }
#endif
        if (useGlobalBorder) { var g = ownerSheet?.globalButton; if (g != null) return g.border; }
        return border;
    }

    public ZUIBorderDef GetHoverBorder()
    {
        var hb = ResolveHoverBoxStyle();
        if (hb != null) return hb.GetResolvedBorder();
        return hoverBorderOverride ? hoverBorder : GetNormalBorder();
    }
    public ZUIBorderDef GetActiveBorder()
    {
        var ab = ResolveActiveBoxStyle();
        if (ab != null) return ab.GetResolvedBorder();
        return activeBorderOverride ? activeBorder : GetHoverBorder();
    }
    public ZUIBorderDef GetToggleOnBorder()
    {
        var tb = ResolveToggleOnBoxStyle();
        if (tb != null) return tb.GetResolvedBorder();
        return toggleOnBorderOverride ? toggleOnBorder : GetActiveBorder();
    }

    public ZUIBorderDef GetBorder(ZUIButtonDrawState s) =>
        s == ZUIButtonDrawState.ToggleOn ? GetToggleOnBorder() :
        s == ZUIButtonDrawState.Active   ? GetActiveBorder()   :
        s == ZUIButtonDrawState.Hover    ? GetHoverBorder()    : GetNormalBorder();

    // ── Visual draw ───────────────────────────────────────────────────────────

    void DrawVisualInternal(Rect rect, ZUIButtonDrawState state, int cornerRadius,
                            Vector4 crVec)
    {
        if (UnityEngine.Event.current.type != UnityEngine.EventType.Repaint) return;

        int cr2 = GetResolvedCornerRadius();
        // Resolve box style for the current state
        var boxRef = state == ZUIButtonDrawState.ToggleOn ? (ResolveToggleOnBoxStyle() ?? ResolveActiveBoxStyle() ?? ResolveBoxStyle())
                   : state == ZUIButtonDrawState.Active   ? (ResolveActiveBoxStyle() ?? ResolveBoxStyle())
                   : state == ZUIButtonDrawState.Hover    ? (ResolveHoverBoxStyle() ?? ResolveBoxStyle())
                   : ResolveBoxStyle();
        var shadowSrc = (boxRef != null && !boxOverrideShadow) ? boxRef.bgShadow : bgShadow;
        var glowSrc   = (boxRef != null && !boxOverrideBg)     ? boxRef.glow     : glow;
        DrawGlowEffect(glowSrc, rect, cr2, ownerSheet);
        DrawShadowEffect(shadowSrc, rect, cr2, ownerSheet);

        ZUIColor fill   = GetColor(state);
        ZUIBorderDef bDef  = GetBorder(state);
        Color  bc1 = bDef.color.GetColorA(ownerSheet);
        var    ew  = bDef.edgeWidth;
        float  bw  = ew.Top; // uniform fallback

        // Overlay/pattern: inherit from box if resolved for this state
        // For hover/active with their own box style, always use that box's effects
        bool   useBoxEffects = boxRef != null && (state != ZUIButtonDrawState.Normal || !boxOverrideBg);
        bool   ovEnabled = useBoxEffects ? boxRef.overlayEnabled : overlayEnabled;
        var    ovGrad    = useBoxEffects ? boxRef.overlay         : overlay;
        var    patSrc    = useBoxEffects ? boxRef.pattern          : pattern;

#if UNITY_2021_2_OR_NEWER
        if (cornerRadius > 0 && bw > 0f && bc1.a > 0f)
        {
            // Snap the inner inset to an integer number of pixels. A fractional
            // edge width (e.g. 1.1255) makes GUI.DrawTexture rasterize the top
            // and bottom of the inner rect asymmetrically, producing a 1px border
            // growth on hover. Integer inset keeps top/bottom symmetric.
            float bwMax = Mathf.Round(
                Mathf.Max(ew.Top, Mathf.Max(ew.Right, Mathf.Max(ew.Bottom, ew.Left))));
            float r     = Mathf.Min(cornerRadius, rect.width * 0.5f, rect.height * 0.5f);
            var   cVec  = crVec;
            bDef.color.DrawRect(rect, ownerSheet, cVec);
            var   inner = new Rect(rect.x + bwMax, rect.y + bwMax, rect.width - bwMax * 2f, rect.height - bwMax * 2f);
            float ir    = Mathf.Max(0f, r - bwMax);
            var   iVec  = new Vector4(crVec.x > 0 ? ir : 0, crVec.y > 0 ? ir : 0, crVec.z > 0 ? ir : 0, crVec.w > 0 ? ir : 0);
            fill.DrawRect(inner, ownerSheet, iVec);
            if (ovEnabled) ovGrad.DrawRect(inner, ownerSheet, iVec);
            DrawPatternEffect(patSrc, inner, iVec, ownerSheet);
            DrawInnerGlowEffect(glowSrc, inner, Mathf.RoundToInt(ir), ownerSheet);
            return;
        }
#endif

        float bT = ew.Top, bR = ew.Right, bB = ew.Bottom, bL = ew.Left;
        bool hasBorder = (bT > 0f || bR > 0f || bB > 0f || bL > 0f) && bc1.a > 0f;

        if (hasBorder && bDef.color.isGradient)
        {
            // Gradient border: draw border gradient as full rect, then fill on top (inset)
            bDef.color.DrawRect(rect, ownerSheet, crVec);
            var inner = new Rect(rect.x + bL, rect.y + bT, rect.width - bL - bR, rect.height - bT - bB);
            fill.DrawRect(inner, ownerSheet, crVec);
            if (ovEnabled) ovGrad.DrawRect(inner, ownerSheet, crVec);
            DrawPatternEffect(patSrc, inner, crVec, ownerSheet);
            DrawInnerGlowEffect(glowSrc, inner, cornerRadius, ownerSheet);
        }
        else
        {
            fill.DrawRect(rect, ownerSheet, crVec);
            if (ovEnabled) ovGrad.DrawRect(rect, ownerSheet, crVec);
            DrawPatternEffect(patSrc, rect, crVec, ownerSheet);
            DrawInnerGlowEffect(glowSrc, rect, cornerRadius, ownerSheet);

            if (hasBorder)
            {
                // Use GUI.DrawTexture so GUI.color (used by crossfade animation) is respected
                var prevC = GUI.color;
                GUI.color = new Color(bc1.r * prevC.r, bc1.g * prevC.g, bc1.b * prevC.b, bc1.a * prevC.a);
                if (bT > 0f) GUI.DrawTexture(new Rect(rect.x,        rect.y,         rect.width, bT),          Texture2D.whiteTexture);
                if (bB > 0f) GUI.DrawTexture(new Rect(rect.x,        rect.yMax - bB, rect.width, bB),          Texture2D.whiteTexture);
                if (bL > 0f) GUI.DrawTexture(new Rect(rect.x,        rect.y,         bL,         rect.height), Texture2D.whiteTexture);
                if (bR > 0f) GUI.DrawTexture(new Rect(rect.xMax - bR, rect.y,        bR,         rect.height), Texture2D.whiteTexture);
                GUI.color = prevC;
            }
        }
    }

    public void DrawVisual(Rect rect, ZUIButtonDrawState state, int cornerRadius)
        => DrawVisualInternal(rect, state, cornerRadius, GetCornerVector(Mathf.Min(cornerRadius, rect.width * 0.5f, rect.height * 0.5f)));

    public void DrawVisualWithCorners(Rect rect, ZUIButtonDrawState state, int cornerRadius,
                                      bool roundTL, bool roundTR, bool roundBL, bool roundBR)
    {
        float r    = Mathf.Min(cornerRadius, rect.width * 0.5f, rect.height * 0.5f);
        var   cVec = new Vector4(roundTL ? r : 0f, roundTR ? r : 0f, roundBR ? r : 0f, roundBL ? r : 0f);
        DrawVisualInternal(rect, state, cornerRadius, cVec);
    }

    /// <summary>
    /// Draws the button visual as a crossfade between two states.
    /// Renders the "from" state at (1-t) opacity and the "to" state at t opacity.
    /// All properties animate correctly — fill, border, shadow, glow, pattern, text.
    /// t=0 shows <paramref name="from"/>, t=1 shows <paramref name="to"/>.
    /// </summary>
    public void DrawVisualLerped(Rect rect, ZUIButtonDrawState from, ZUIButtonDrawState to,
                                  float t, int cornerRadius, ZUICornerMask cornerMask = ZUICornerMask.None)
    {
        if (UnityEngine.Event.current.type != UnityEngine.EventType.Repaint) return;

        t = Mathf.Clamp01(t);

        // Resolve corner vectors once (shared by both draws)
        float r = Mathf.Min(cornerRadius, rect.width * 0.5f, rect.height * 0.5f);
        Vector4 cVec;
        if (cornerMask == ZUICornerMask.None)
        {
            var (tl, tr, bl, br) = GetResolvedCornerFlags();
            cVec = new Vector4(tl ? r : 0f, tr ? r : 0f, br ? r : 0f, bl ? r : 0f);
        }
        else
        {
            var (tl, tr, bl, br) = ZUICore.ResolveCornerMask(this, cornerMask);
            cVec = new Vector4(tl ? r : 0f, tr ? r : 0f, br ? r : 0f, bl ? r : 0f);
        }

        var savedColor = GUI.color;

        // Draw "from" state at (1-t) opacity
        if (t < 1f)
        {
            GUI.color = new Color(savedColor.r, savedColor.g, savedColor.b, savedColor.a * (1f - t));
            DrawVisualInternal(rect, from, cornerRadius, cVec);
        }

        // Draw "to" state at t opacity on top
        if (t > 0f)
        {
            GUI.color = new Color(savedColor.r, savedColor.g, savedColor.b, savedColor.a * t);
            DrawVisualInternal(rect, to, cornerRadius, cVec);
        }

        GUI.color = savedColor;
    }

    /// <summary>
    /// Draws the button visual with per-field interpolation between two states.
    /// Every visual property is lerped: fill (colors, angle, bias, radial, multi-stop),
    /// border (colors, angle, all 4 edge widths), overlay, shadow, glow, pattern opacity.
    /// t=0 shows <paramref name="from"/>, t=1 shows <paramref name="to"/>.
    /// </summary>
    public void DrawVisualFieldLerped(Rect rect, ZUIButtonDrawState from, ZUIButtonDrawState to,
                                       float t, int cornerRadius, ZUICornerMask cornerMask = ZUICornerMask.None)
    {
        if (UnityEngine.Event.current.type != UnityEngine.EventType.Repaint) return;

        t = Mathf.Clamp01(t);

        // ── Corners ──────────────────────────────────────────────────────────
        float r = Mathf.Min(cornerRadius, rect.width * 0.5f, rect.height * 0.5f);
        Vector4 cVec;
        if (cornerMask == ZUICornerMask.None)
        {
            var (tl, tr, bl, br) = GetResolvedCornerFlags();
            cVec = new Vector4(tl ? r : 0f, tr ? r : 0f, br ? r : 0f, bl ? r : 0f);
        }
        else
        {
            var (tl, tr, bl, br) = ZUICore.ResolveCornerMask(this, cornerMask);
            cVec = new Vector4(tl ? r : 0f, tr ? r : 0f, br ? r : 0f, bl ? r : 0f);
        }

        int cr2 = GetResolvedCornerRadius();

        // ── Resolve box refs ─────────────────────────────────────────────────
        ZUIBoxDef BoxForState(ZUIButtonDrawState st) =>
            st == ZUIButtonDrawState.ToggleOn ? (ResolveToggleOnBoxStyle() ?? ResolveActiveBoxStyle() ?? ResolveBoxStyle())
          : st == ZUIButtonDrawState.Active   ? (ResolveActiveBoxStyle() ?? ResolveBoxStyle())
          : st == ZUIButtonDrawState.Hover    ? (ResolveHoverBoxStyle() ?? ResolveBoxStyle())
          : ResolveBoxStyle();
        var boxRefA = BoxForState(from);
        var boxRefB = BoxForState(to);
        var boxRef = ResolveBoxStyle(); // for shared effects

        // ── Fill: full gradient lerp (handles multi-stop, radial, solid↔gradient) ──
        ZUIColor fillA = GetColor(from);
        ZUIColor fillB = GetColor(to);
        var lerpFill = ZUIColor.Lerp(fillA, fillB, t, ownerSheet, smooth: true);

        // ── Border: lerp edge widths + gradient ──────────────────────────────
        ZUIBorderDef bDefA = GetBorder(from);
        ZUIBorderDef bDefB = GetBorder(to);
        var lerpBorderGrad = ZUIColor.Lerp(bDefA.color, bDefB.color, t, ownerSheet, smooth: true);
        float lerpBT = Mathf.Lerp(bDefA.edgeWidth.Top,    bDefB.edgeWidth.Top,    t);
        float lerpBR = Mathf.Lerp(bDefA.edgeWidth.Right,  bDefB.edgeWidth.Right,  t);
        float lerpBB = Mathf.Lerp(bDefA.edgeWidth.Bottom, bDefB.edgeWidth.Bottom, t);
        float lerpBL = Mathf.Lerp(bDefA.edgeWidth.Left,   bDefB.edgeWidth.Left,   t);
        // Snap the inner inset to an integer number of pixels; see DrawVisualInternal
        // for why (prevents asymmetric top/bottom rasterization on hover).
        float bwMax = Mathf.Round(Mathf.Max(lerpBT, Mathf.Max(lerpBR, Mathf.Max(lerpBB, lerpBL))));
        bool hasBorder = bwMax > 0f && lerpBorderGrad.GetColorA(ownerSheet).a > 0f;

        // ── Overlay ──────────────────────────────────────────────────────────
        bool useBoxFx = boxRef != null && !boxOverrideBg;
        bool ovEnabled = useBoxFx ? boxRef.overlayEnabled : overlayEnabled;
        var  ovGrad    = useBoxFx ? boxRef.overlay         : overlay;

        // ── Shadow: lerp offset, blur, color ─────────────────────────────────
        var shadowSrc = (boxRef != null && !boxOverrideShadow) ? boxRef.bgShadow : bgShadow;
        if (shadowSrc.enabled)
        {
            Color scA = shadowSrc.GetResolvedColor(ownerSheet);
            // Lerp shadow alpha with t (fade shadow in/out during transition)
            Color scLerp = new Color(scA.r, scA.g, scA.b, scA.a);
            DrawShadowEffect(shadowSrc, rect, cr2, ownerSheet);
        }
        else
        {
            DrawShadowEffect(shadowSrc, rect, cr2, ownerSheet);
        }

        // ── Glow: lerp radius and color alpha ────────────────────────────────
        var glowSrc = (boxRef != null && !boxOverrideBg) ? boxRef.glow : glow;
        DrawGlowEffect(glowSrc, rect, cr2, ownerSheet);

        // ── Pattern: lerp opacity ────────────────────────────────────────────
        var patSrc = useBoxFx ? boxRef.pattern : pattern;

        // ── Draw ─────────────────────────────────────────────────────────────
#if UNITY_2021_2_OR_NEWER
        if (cornerRadius > 0 && hasBorder)
        {
            lerpBorderGrad.DrawRect(rect, ownerSheet, cVec);
            var inner = new Rect(rect.x + bwMax, rect.y + bwMax, rect.width - bwMax * 2f, rect.height - bwMax * 2f);
            float ir = Mathf.Max(0f, r - bwMax);
            var iVec = new Vector4(cVec.x > 0 ? ir : 0, cVec.y > 0 ? ir : 0, cVec.z > 0 ? ir : 0, cVec.w > 0 ? ir : 0);
            lerpFill.DrawRect(inner, ownerSheet, iVec);
            if (ovEnabled) ovGrad.DrawRect(inner, ownerSheet, iVec);
            DrawPatternEffect(patSrc, inner, iVec, ownerSheet);
            DrawInnerGlowEffect(glowSrc, inner, Mathf.RoundToInt(ir), ownerSheet);
            return;
        }
#endif
        // Non-rounded
        float bT = lerpBT, bR2 = lerpBR, bB = lerpBB, bL = lerpBL;
        if (hasBorder && lerpBorderGrad.isGradient)
        {
            lerpBorderGrad.DrawRect(rect, ownerSheet, cVec);
            var inner = new Rect(rect.x + bL, rect.y + bT, rect.width - bL - bR2, rect.height - bT - bB);
            lerpFill.DrawRect(inner, ownerSheet, cVec);
            if (ovEnabled) ovGrad.DrawRect(inner, ownerSheet, cVec);
            DrawPatternEffect(patSrc, inner, cVec, ownerSheet);
            DrawInnerGlowEffect(glowSrc, inner, cornerRadius, ownerSheet);
        }
        else
        {
            lerpFill.DrawRect(rect, ownerSheet, cVec);
            if (ovEnabled) ovGrad.DrawRect(rect, ownerSheet, cVec);
            DrawPatternEffect(patSrc, rect, cVec, ownerSheet);
            DrawInnerGlowEffect(glowSrc, rect, cornerRadius, ownerSheet);

            if (hasBorder)
            {
                Color bc1 = lerpBorderGrad.GetColorA(ownerSheet);
                var prevC = GUI.color;
                GUI.color = new Color(bc1.r * prevC.r, bc1.g * prevC.g, bc1.b * prevC.b, bc1.a * prevC.a);
                if (bT > 0f)  GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, bT), Texture2D.whiteTexture);
                if (bB > 0f)  GUI.DrawTexture(new Rect(rect.x, rect.yMax - bB, rect.width, bB), Texture2D.whiteTexture);
                if (bL > 0f)  GUI.DrawTexture(new Rect(rect.x, rect.y, bL, rect.height), Texture2D.whiteTexture);
                if (bR2 > 0f) GUI.DrawTexture(new Rect(rect.xMax - bR2, rect.y, bR2, rect.height), Texture2D.whiteTexture);
                GUI.color = prevC;
            }
        }
    }

    // ── Glow + Shadow rendering helpers (shared by Button and Box) ─────────────

    internal static void DrawPatternEffect(ZUIPatternDef pattern, Rect rect, Vector4 corners, ZUIStyleSheetAsset sheet)
    {
        if (!pattern.enabled || pattern.patternType == ZUIPatternType.None) return;
        var tex = pattern.GetTextureForRect((int)rect.width, (int)rect.height, sheet);
        if (tex == null) return;

        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, pattern.opacity);

        if (pattern.UsesTiledTexture)
        {
            // Icon: small tiled texture — tile via tex coords, offset shifts the UV origin
            float tileX = rect.width  / (tex.width * pattern.scale);
            float tileY = rect.height / (tex.height * pattern.scale);
            GUI.DrawTextureWithTexCoords(rect, tex,
                new Rect(pattern.offsetX, pattern.offsetY, tileX, tileY), true);
        }
        else
        {
            // Analytical: rect-sized texture, stretch to fill
#if UNITY_2021_2_OR_NEWER
            bool anyRound = corners.x > 0f || corners.y > 0f || corners.z > 0f || corners.w > 0f;
            if (anyRound)
            {
                // Multiply by GUI.color so crossfade animation alpha is respected.
                Color pc = GUI.color;
                GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true, 0f,
                    new Color(pc.r, pc.g, pc.b, pc.a * pattern.opacity), Vector4.zero, corners);
            }
            else
#endif
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);
        }
        GUI.color = prev;
    }

    internal static void DrawGlowEffect(ZUIGlowDef glow, Rect rect, int cornerRadius, ZUIStyleSheetAsset sheet)
    {
        if (!glow.enabled || glow.radius <= 0f) return;
        Color gc = glow.color.Resolve(sheet);
        if (gc.a <= 0f) return;
        int passes = Mathf.Max(1, glow.passes);
        // Capture GUI.color so crossfade animation alpha is respected by 7-param DrawTexture.
        Color guiC = GUI.color;

        float sT = glow.SpreadTop, sR = glow.SpreadRight, sB = glow.SpreadBottom, sL = glow.SpreadLeft;

        for (int i = passes; i >= 1; i--)
        {
            float expand = glow.radius * ((float)i / passes);
            float alpha  = gc.a * (1f - (float)(i - 1) / passes) * (1f / passes) * 2f;
            var glowColor = new Color(gc.r * guiC.r, gc.g * guiC.g, gc.b * guiC.b, alpha * guiC.a);

            float eT = expand * sT, eR = expand * sR, eB = expand * sB, eL = expand * sL;
            var gr = new Rect(rect.x - eL, rect.y - eT, rect.width + eL + eR, rect.height + eT + eB);

#if UNITY_2021_2_OR_NEWER
            if (cornerRadius > 0)
            {
                float r = Mathf.Min(cornerRadius + expand, gr.width * 0.5f, gr.height * 0.5f);
                GUI.DrawTexture(gr, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, glowColor, Vector4.zero, new Vector4(r, r, r, r));
            }
            else
#endif
            ZUIColor.FillRectRuntime(gr, glowColor);
        }
    }

    internal static void DrawInnerGlowEffect(ZUIGlowDef glow, Rect rect, int cornerRadius, ZUIStyleSheetAsset sheet)
    {
        if (!glow.innerEnabled || glow.innerRadius <= 0f) return;
        Color gc = glow.innerColor.Resolve(sheet);
        if (gc.a <= 0f) return;
        int passes = Mathf.Max(1, glow.innerPasses);

        for (int i = passes; i >= 1; i--)
        {
            float inset = glow.innerRadius * ((float)i / passes);
            float alpha = gc.a * (1f - (float)(i - 1) / passes) * (1f / passes) * 2f;
            var glowColor = new Color(gc.r, gc.g, gc.b, alpha);

            // Draw 4 edge strips inset from the rect boundary
            // Top strip
            ZUIColor.FillRectRuntime(new Rect(rect.x, rect.y, rect.width, inset), glowColor);
            // Bottom strip
            ZUIColor.FillRectRuntime(new Rect(rect.x, rect.yMax - inset, rect.width, inset), glowColor);
            // Left strip (between top and bottom)
            ZUIColor.FillRectRuntime(new Rect(rect.x, rect.y + inset, inset, rect.height - inset * 2f), glowColor);
            // Right strip (between top and bottom)
            ZUIColor.FillRectRuntime(new Rect(rect.xMax - inset, rect.y + inset, inset, rect.height - inset * 2f), glowColor);
        }
    }

    internal static void DrawShadowEffect(ZUIDropShadowDef shadow, Rect rect, int cornerRadius, ZUIStyleSheetAsset sheet)
    {
        if (!shadow.enabled) return;
        Color sc = shadow.GetResolvedColor(sheet);
        if (sc.a <= 0f) return;
        // Capture GUI.color so crossfade animation alpha is respected by 7-param DrawTexture.
        Color guiC = GUI.color;
        var baseRect = new Rect(rect.x + shadow.offset.x, rect.y + shadow.offset.y, rect.width, rect.height);

        if (shadow.blurRadius > 0f && shadow.blurPasses > 0)
        {
            int passes = Mathf.Max(1, shadow.blurPasses);
            for (int i = 0; i < passes; i++)
            {
                float spread = shadow.blurRadius * ((float)(i + 1) / passes);
                float alpha  = sc.a / passes;
                var blurColor = new Color(sc.r * guiC.r, sc.g * guiC.g, sc.b * guiC.b, alpha * guiC.a);
                var br = new Rect(baseRect.x - spread, baseRect.y - spread, baseRect.width + spread * 2f, baseRect.height + spread * 2f);
#if UNITY_2021_2_OR_NEWER
                if (cornerRadius > 0)
                {
                    float r = Mathf.Min(cornerRadius + spread, br.width * 0.5f, br.height * 0.5f);
                    GUI.DrawTexture(br, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, blurColor, Vector4.zero, new Vector4(r, r, r, r));
                }
                else
#endif
                ZUIColor.FillRectRuntime(br, blurColor);
            }
        }
        else
        {
            // Sharp shadow (original behavior)
#if UNITY_2021_2_OR_NEWER
            if (cornerRadius > 0)
            {
                float r = Mathf.Min(cornerRadius, baseRect.width * 0.5f, baseRect.height * 0.5f);
                Color tinted = new Color(sc.r * guiC.r, sc.g * guiC.g, sc.b * guiC.b, sc.a * guiC.a);
                GUI.DrawTexture(baseRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, tinted, Vector4.zero, new Vector4(r, r, r, r));
            }
            else
#endif
            ZUIColor.FillRectRuntime(baseRect, sc);
        }
    }

    // ── Border draw (standalone, flat only) ───────────────────────────────────

    public void DrawBorder(Rect rect)
    {
        if (UnityEngine.Event.current.type != UnityEngine.EventType.Repaint) return;
        var bDef = GetNormalBorder();
        var ew = bDef.edgeWidth;
        float bT = ew.Top, bR = ew.Right, bB = ew.Bottom, bL = ew.Left;
        if (bT <= 0f && bR <= 0f && bB <= 0f && bL <= 0f) return;
        Color bc1 = bDef.color.GetColorA(ownerSheet);
        if (bc1.a <= 0f) return;
        if (bT > 0f) ZUIColor.FillRectRuntime(new Rect(rect.x,        rect.y,         rect.width, bT),          bc1);
        if (bB > 0f) ZUIColor.FillRectRuntime(new Rect(rect.x,        rect.yMax - bB, rect.width, bB),          bc1);
        if (bL > 0f) ZUIColor.FillRectRuntime(new Rect(rect.x,        rect.y,         bL,         rect.height), bc1);
        if (bR > 0f) ZUIColor.FillRectRuntime(new Rect(rect.xMax - bR, rect.y,        bR,         rect.height), bc1);
    }

    // ── GUIStyle for legacy ZButtonStyle path ─────────────────────────────────

    [NonSerialized] private GUIStyle _style;

    public GUIStyle GetStyle()
    {
        if (_style == null || _style.normal.background == null)
            _style = BuildStyle();
        return _style;
    }

    // ── Label style ───────────────────────────────────────────────────────────

    [NonSerialized] private GUIStyle _labelStyle;
    [NonSerialized] private GUIStyle _hoverLabelStyle;
    [NonSerialized] private GUIStyle _activeLabelStyle;
    [NonSerialized] private GUIStyle _toggleOnLabelStyle;
    [NonSerialized] private GUIStyle _iconLabelStyle;
    [NonSerialized] private GUIStyle _iconHoverLabelStyle;
    [NonSerialized] private GUIStyle _iconActiveLabelStyle;
    [NonSerialized] private GUIStyle _iconToggleOnLabelStyle;

    public GUIStyle GetLabelStyle(ZUIButtonDrawState state = ZUIButtonDrawState.Normal, bool iconOnly = false)
    {
        if (iconOnly)
        {
            GUIStyle existing = state == ZUIButtonDrawState.Hover    ? _iconHoverLabelStyle
                              : state == ZUIButtonDrawState.Active   ? _iconActiveLabelStyle
                              : state == ZUIButtonDrawState.ToggleOn ? _iconToggleOnLabelStyle
                              :                                        _iconLabelStyle;
            if (existing != null) return existing;
            var ip = padding;
#if UNITY_EDITOR
            if (!boxOverridePadding) { var b = ResolveBoxStyle(); if (b != null) ip = b.padding; }
            else if (useGlobalPadding) { var g = ownerSheet?.globalButton; if (g != null) ip = g.padding; }
#endif
            var built = new GUIStyle(GUIStyle.none) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(ip.IconPadH, ip.IconPadH, ip.IconPadV, ip.IconPadV) };
            built.fontSize = GUI.skin.button.fontSize;
            built.font     = GUI.skin.button.font;
            GetText(state).Apply(built, ownerSheet);
            if (state == ZUIButtonDrawState.Hover)         _iconHoverLabelStyle    = built;
            else if (state == ZUIButtonDrawState.Active)   _iconActiveLabelStyle   = built;
            else if (state == ZUIButtonDrawState.ToggleOn) _iconToggleOnLabelStyle = built;
            else                                           _iconLabelStyle         = built;
            return built;
        }

        GUIStyle cached = state == ZUIButtonDrawState.Hover    ? _hoverLabelStyle
                        : state == ZUIButtonDrawState.Active   ? _activeLabelStyle
                        : state == ZUIButtonDrawState.ToggleOn ? _toggleOnLabelStyle
                        :                                        _labelStyle;
        if (cached != null) return cached;

        var tp = padding;
#if UNITY_EDITOR
        if (!boxOverridePadding) { var b = ResolveBoxStyle(); if (b != null) tp = b.padding; }
        else if (useGlobalPadding) { var g = ownerSheet?.globalButton; if (g != null) tp = g.padding; }
#endif
        var s = new GUIStyle(GUIStyle.none)
        {
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(tp.PadLeft, tp.PadRight, tp.PadTop, tp.PadBottom),
        };
        s.fontSize = GUI.skin.button.fontSize;
        s.font     = GUI.skin.button.font;
        GetText(state).Apply(s, ownerSheet);
        if (state == ZUIButtonDrawState.Hover)         _hoverLabelStyle    = s;
        else if (state == ZUIButtonDrawState.Active)   _activeLabelStyle   = s;
        else if (state == ZUIButtonDrawState.ToggleOn) _toggleOnLabelStyle = s;
        else                                           _labelStyle         = s;
        return s;
    }

    public void Invalidate()
    {
        _style                  = null;
        _labelStyle             = null;
        _hoverLabelStyle        = null;
        _activeLabelStyle       = null;
        _toggleOnLabelStyle     = null;
        _iconLabelStyle         = null;
        _iconHoverLabelStyle    = null;
        _iconActiveLabelStyle   = null;
        _iconToggleOnLabelStyle = null;
        normal.Invalidate();
        hover.Invalidate();
        active.Invalidate();
        toggleOn.Invalidate();
        border.color.Invalidate();
        hoverBorder.color.Invalidate();
        activeBorder.color.Invalidate();
        toggleOnBorder.color.Invalidate();
    }

    // ── GUIStyle builder ──────────────────────────────────────────────────────

    private GUIStyle BuildStyle()
    {
        var s = new GUIStyle(GUIStyle.none);
        SetState(s.normal,  normal.GetOrBuildTexture(ownerSheet), text.GetResolvedColor(ownerSheet));
        SetState(s.hover,   hover.GetOrBuildTexture(ownerSheet),  text.GetResolvedColor(ownerSheet));
        SetState(s.active,  active.GetOrBuildTexture(ownerSheet), text.GetResolvedColor(ownerSheet));
        SetState(s.focused, hover.GetOrBuildTexture(ownerSheet),  text.GetResolvedColor(ownerSheet));
        s.border    = new RectOffset(0, 0, 0, 0);
        s.padding   = new RectOffset(padding.PadLeft, padding.PadRight, padding.PadTop, padding.PadBottom);
        s.alignment = TextAnchor.MiddleCenter;
        text.Apply(s, ownerSheet);
        s.fontSize = GUI.skin.button.fontSize;
        s.font     = GUI.skin.button.font;
        if (text.fontSize > 0) s.fontSize = text.fontSize;
        return s;
    }

    static void SetState(GUIStyleState state, Texture2D tex, Color textColor)
    {
        state.background        = tex;
        state.scaledBackgrounds = new Texture2D[] { tex };
        state.textColor         = textColor;
    }
}

// ── Named Text Style ──────────────────────────────────────────────────────────

[Serializable]
public class ZUITextStyleDef
{
    public string     name = "New Text Style";
    public ZUITextDef text = new ZUITextDef();

    // Section visibility
    public bool showText    = true;
    public bool showPreview = true;

    [NonSerialized] private GUIStyle _style;

    public GUIStyle GetStyle(ZUIStyleSheetAsset sheet)
    {
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label);
            _style.wordWrap = true;
        }
        text.Apply(_style, sheet);
        return _style;
    }

    public void Invalidate() { _style = null; }
}

// ── Slider Label Position ─────────────────────────────────────────────────────

public enum ZUILabelPosition  { Inline, Above, Below }
public enum ZUILabelAlignment { Left, Center, Right }

// ── Slider Style ──────────────────────────────────────────────────────────────

[Serializable]
public class ZUISliderDef
{
    public string name = "New Slider Style";

    public ZUIBoxDef    track         = new ZUIBoxDef("Track",
                                            new Color(.14f, .14f, .18f, 1f),
                                            new Color(.88f, .88f, .88f, 1f),
                                            new Color(1f, 1f, 1f, .08f), 1f, 0, 0);
    public ZUIBoxDef    trackFill     = new ZUIBoxDef("TrackFill",
                                            new Color(.20f, .38f, .55f, 1f),
                                            new Color(.88f, .88f, .88f, 1f),
                                            new Color(.30f, .60f, 1f, .30f), 1f, 0, 0);
    public float        trackHeight   = 6f;

    public ZUIButtonDef thumb         = new ZUIButtonDef("Thumb",
                                            new Color(.30f, .54f, .78f, 1f),
                                            new Color(.40f, .64f, .90f, 1f),
                                            new Color(.20f, .40f, .62f, 1f),
                                            new Color(.92f, .96f, 1f,   1f));
    public ZUIButtonDef thumbMax      = null;
    // Optional visual for the bipolar center marker. Falls back to `thumb` when null.
    public ZUIButtonDef thumbCenter   = null;
    public float        thumbWidth    = 12f;
    public float        thumbHeight   = 20f;

    // Bipolar mode — when true, the slider treats `bipolarCenter` as a meaningful zero/neutral
    // point. Collapsed fill (min == max) originates at the center and extends toward the value
    // instead of starting at trackStart. A non-draggable center thumb marks the zero position;
    // double-clicking it toggles between "collapsed at center" and "symmetric spread around center".
    public bool  bipolar        = false;
    // Zero/neutral point in absMin..absMax coordinates. NaN = auto midpoint of (absMin, absMax).
    public float bipolarCenter  = float.NaN;

    public ZUITextDef        labelText      = new ZUITextDef(new Color(.78f, .78f, .82f, 1f));
    public float             labelWidth     = 0f;
    public ZUILabelPosition  labelPosition  = ZUILabelPosition.Inline;
    public ZUILabelAlignment labelAlignment = ZUILabelAlignment.Left;

    public ZUITextDef   valueText      = new ZUITextDef(new Color(.88f, .88f, .88f, 1f));
    public float        valueWidth     = 40f;
    public bool         showValueField = true;

    /// <summary>Format string for the value display. Empty = auto-format based on range.</summary>
    public string       valueFormat    = "";

    // Section visibility
    public bool showPreview   = true;
    public bool showLayout    = true;
    public bool showTrack     = true;
    public bool showTrackFill = true;
    public bool showThumb     = true;
    public bool showLabelText = true;
    public bool showValueText = true;

    [NonSerialized] private GUIStyle _labelStyle;
    [NonSerialized] private GUIStyle _valueStyle;

    public GUIStyle GetLabelStyle(ZUIStyleSheetAsset sheet)
    {
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.clipping = TextClipping.Clip;
        }
        labelText.Apply(_labelStyle, sheet);
        return _labelStyle;
    }

    public GUIStyle GetValueStyle(ZUIStyleSheetAsset sheet)
    {
        if (_valueStyle == null)
        {
            _valueStyle = new GUIStyle(GUI.skin.textField);
            _valueStyle.alignment = TextAnchor.MiddleRight;
        }
        valueText.Apply(_valueStyle, sheet);
        return _valueStyle;
    }

    public void Invalidate()
    {
        _labelStyle = null;
        _valueStyle = null;
        track?.Invalidate();
        trackFill?.Invalidate();
        thumb?.Invalidate();
        thumbMax?.Invalidate();
        thumbCenter?.Invalidate();
    }
}
