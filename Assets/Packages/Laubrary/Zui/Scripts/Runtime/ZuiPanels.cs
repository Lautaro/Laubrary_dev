// Panels that CANNOT be off-screen. A panel is requested by anchor + size-in-points; the rect is
// scaled and then clamped into Screen bounds by construction, so "half the menu is outside the
// viewport" is inexpressible. Panel() returns the padded content rect, ready for a ZuiStack.

using System;
using UnityEngine;

namespace ZuiRuntime
{
    public enum ZuiAnchor { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }

    public static partial class Zui
    {
        /// <summary>Shifts (and if necessary shrinks) a rect so it lies fully inside the screen.</summary>
        public static Rect ClampToScreen(Rect r)
        {
            r.width = Mathf.Min(r.width, Screen.width);
            r.height = Mathf.Min(r.height, Screen.height);
            r.x = Mathf.Clamp(r.x, 0f, Screen.width - r.width);
            r.y = Mathf.Clamp(r.y, 0f, Screen.height - r.height);
            return r;
        }

        /// <summary>
        /// Draws a solid panel of <paramref name="wPts"/>×<paramref name="hPts"/> points anchored to a
        /// screen corner/edge, clamped on-screen, and returns its padded content rect.
        /// </summary>
        public static Rect Panel(ZuiAnchor anchor, float wPts, float hPts, Color bg,
            float marginPts = 12f, float padPts = 10f)
        {
            float w = UIScale.S(wPts), h = UIScale.S(hPts), m = UIScale.S(marginPts);
            float x = anchor switch
            {
                ZuiAnchor.TopLeft or ZuiAnchor.MiddleLeft or ZuiAnchor.BottomLeft => m,
                ZuiAnchor.TopRight or ZuiAnchor.MiddleRight or ZuiAnchor.BottomRight => Screen.width - w - m,
                _ => (Screen.width - w) * 0.5f,
            };
            float y = anchor switch
            {
                ZuiAnchor.TopLeft or ZuiAnchor.TopCenter or ZuiAnchor.TopRight => m,
                ZuiAnchor.BottomLeft or ZuiAnchor.BottomCenter or ZuiAnchor.BottomRight => Screen.height - h - m,
                _ => (Screen.height - h) * 0.5f,
            };
            return Panel(new Rect(x, y, w, h), bg, padPts);
        }

        /// <summary>Draws a solid panel at an explicit rect (clamped on-screen); returns the padded content rect.</summary>
        public static Rect Panel(Rect rect, Color bg, float padPts = 10f)
        {
            rect = ClampToScreen(rect);
            FillRect(rect, bg);
            ZuiAudit.Record(new ZuiDrawRecord { Kind = "panel", Rect = rect });
            float p = UIScale.S(padPts);
            return new Rect(rect.x + p, rect.y + p, rect.width - 2f * p, rect.height - 2f * p);
        }

        /// <summary>
        /// Draws a SHEET-STYLED panel (9-slice background, if the style defines one) at an explicit rect,
        /// clamped on-screen; returns the padded content rect. Additive to the flat-color <see cref="Panel(Rect,Color,float)"/>
        /// above — leave <paramref name="sheet"/> null to use <see cref="DefaultSheet"/> (a Resources-loaded
        /// runtime sheet, editable live via the Style Editor). If no sheet resolves (none assigned, no
        /// shipped Resources asset), this draws nothing and returns the rect un-padded — a missing style
        /// asset shouldn't throw in a build. <paramref name="padPts"/> defaults to the resolved sheet's own
        /// <c>contentPadding</c> rather than a hardcoded number.
        /// </summary>
        public static Rect Panel(Rect rect, string styleName, ZUIStyleSheetAsset sheet = null, float? padPts = null)
        {
            rect = ClampToScreen(rect);
            var resolvedSheet = sheet ?? DefaultSheet;
            if (resolvedSheet == null) return rect;
            ZUISheet.DrawBox(resolvedSheet, styleName, rect);
            ZuiAudit.Record(new ZuiDrawRecord { Kind = "panel", Rect = rect });
            float p = UIScale.S(padPts ?? resolvedSheet.contentPadding);
            return new Rect(rect.x + p, rect.y + p, rect.width - 2f * p, rect.height - 2f * p);
        }

        /// <summary>GUI.color scope: <c>using (Zui.Tint(color)) { ... }</c> — cannot leak.</summary>
        public static TintScope Tint(Color color) => new TintScope(color);

        public readonly struct TintScope : IDisposable
        {
            readonly Color _prev;
            public TintScope(Color c) { _prev = GUI.color; GUI.color = c; }
            public void Dispose() => GUI.color = _prev;
        }
    }
}
