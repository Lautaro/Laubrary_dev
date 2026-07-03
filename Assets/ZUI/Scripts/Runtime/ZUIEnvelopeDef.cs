// ZUIEnvelopeDef.cs
// Style definition for ZUI.Envelope. Follows the same shape as ZUISliderDef:
// one Def that lives on the style sheet, resolved by name, with sub-defs per
// visual sub-element. Does NOT hold per-instance envelope data (domain,
// loop fields, callbacks) — those are runtime args on the call site.

using System;
using UnityEngine;

// ── ZUIEnvelopeHandleDef ─────────────────────────────────────────────────────
// Visual variant for one point-handle state (Editable / XEditable / YEditable /
// NotEditable). Rendered as a disc with an optional border ring. Hover uses
// the same fields with different color fills.

[Serializable]
public class ZUIEnvelopeHandleDef
{
    /// <summary>Visual radius when not hovered. Kept small so the curve reads clearly; hit area is widened separately via ZUIEnvelopeDef.hitRadiusExtra.</summary>
    public float       radius          = 2f;
    /// <summary>Visual radius when hovered. Should be noticeably larger than `radius` for clear affordance.</summary>
    public float       hoverRadius     = 4f;
    public ZUIColorRef fillColor       = new ZUIColorRef(Color.white);
    public ZUIColorRef hoverFillColor  = new ZUIColorRef(new Color(1f, 0.9f, 0.5f));
    public ZUIColorRef borderColor     = new ZUIColorRef(new Color(0f, 0f, 0f, 0f));
    public float       borderWidth     = 0f;

    public ZUIEnvelopeHandleDef() { }
    public ZUIEnvelopeHandleDef(float radius, Color fill, Color hoverFill,
                                 Color border = default, float borderWidth = 0f)
    {
        this.radius         = radius;
        this.hoverRadius    = radius * 2f;
        this.fillColor      = new ZUIColorRef(fill);
        this.hoverFillColor = new ZUIColorRef(hoverFill);
        this.borderColor    = new ZUIColorRef(border);
        this.borderWidth    = borderWidth;
    }
}

// ── ZUIEnvelopeDef ───────────────────────────────────────────────────────────
// The style-sheet entry. Named, resolvable via sheet.FindEnvelope("Name").
// Curve color is NOT stored here — it's supplied by the consumer per call,
// typically as a palette-referenced ZUIColorRef so the end user can theme
// volume vs pitch (etc.) via palette skins.

[Serializable]
public class ZUIEnvelopeDef
{
    public string name = "New Envelope Style";

    /// <summary>The sheet that owns this def. Wired by ZUIStyleSheetAsset.WireOwnerSheet.</summary>
    [NonSerialized] public ZUIStyleSheetAsset ownerSheet;

    // ── Background / grid ────────────────────────────────────────────────────
    public ZUIColor background = new ZUIColor(new Color(0.12f, 0.12f, 0.12f, 1f));
    public ZUIBorderDef border    = new ZUIBorderDef(new Color(1f, 1f, 1f, 0.06f), 1f);
    public ZUIColorRef  gridColor = new ZUIColorRef(new Color(0.2f, 0.2f, 0.2f, 0.4f));
    public int          gridRows  = 4;

    // ── Inner padding ────────────────────────────────────────────────────────
    // Pixels between the outer envelope rect (background + border) and the
    // actual curve drawing area. Handles at the edges of the time/value range
    // draw inside this inset so they never overflow the border, and there's
    // breathing room between the frame and the curve. Per-edge so callers can
    // allocate extra room at top/bottom for value labels without widening the
    // left/right.
    public float paddingTop    = 6f;
    public float paddingRight  = 6f;
    public float paddingBottom = 6f;
    public float paddingLeft   = 6f;

    // ── Curve ────────────────────────────────────────────────────────────────
    public float curveThickness      = 1.5f;
    public float curveHoverThickness = 3f;

    // ── Handle hit-test slop ────────────────────────────────────────────────
    // Hit-test radius = handle.radius + hitRadiusExtra, independent of visual
    // size. Keeps handles small on screen while staying easy to grab.
    public float hitRadiusExtra = 4f;

    // ── Vertical markers (loop / trim / play head) ──────────────────────────
    public ZUIColorRef markerColor      = new ZUIColorRef(new Color(0.2f, 0.9f, 0.3f, 0.8f));
    public ZUIColorRef markerHoverColor = new ZUIColorRef(new Color(0.5f, 1f,   0.6f, 1f));
    public float       markerThickness       = 2f;
    public float       markerHoverThickness  = 4f;
    public float       markerHitPadding      = 3f;  // extra pixels either side of the line for hit-test
    [Range(0f, 1f)] public float markerFillAlphaScale = 0.08f;

    // ── Point handles — one visual variant per edit state ───────────────────
    // Normal (non-hovered / non-selected) fill is a muted grey so the curve
    // reads as the primary element; the handle only jumps to white/accent on
    // hover or selection. NotEditable points get a distinctly smaller, darker
    // disc to communicate "you can't grab this."
    public ZUIEnvelopeHandleDef editable    = new ZUIEnvelopeHandleDef(
        2f, new Color(0.7f, 0.7f, 0.7f), new Color(1f, 0.9f, 0.5f));
    public ZUIEnvelopeHandleDef xEditable   = new ZUIEnvelopeHandleDef(
        2f, new Color(0.7f, 0.7f, 0.7f), new Color(1f, 0.9f, 0.5f),
        new Color(1f, 1f, 1f, 0.6f), 1f);
    public ZUIEnvelopeHandleDef yEditable   = new ZUIEnvelopeHandleDef(
        2f, new Color(0.7f, 0.7f, 0.7f), new Color(1f, 0.9f, 0.5f),
        new Color(1f, 1f, 1f, 0.6f), 1f);
    public ZUIEnvelopeHandleDef notEditable = new ZUIEnvelopeHandleDef(
        1.25f, new Color(0.28f, 0.28f, 0.28f), new Color(0.28f, 0.28f, 0.28f),
        new Color(0f, 0f, 0f, 0f), 0f);

    /// <summary>Overlay color used on top of the state fill when a point is selected.</summary>
    public ZUIColorRef selectedColor = new ZUIColorRef(new Color(1f, 0.8f, 0.2f));

    public ZUIEnvelopeHandleDef GetHandle(ZUIEnvelopeEditState state)
    {
        switch (state)
        {
            case ZUIEnvelopeEditState.XEditable:   return xEditable;
            case ZUIEnvelopeEditState.YEditable:   return yEditable;
            case ZUIEnvelopeEditState.NotEditable: return notEditable;
            default:                               return editable;
        }
    }

    public void Invalidate()
    {
        // No cached GUIStyles here yet. Method kept for parity with ZUISliderDef /
        // future text sub-defs so BumpVersion() can call it without special-casing.
    }
}
