using System;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zui
{
    /// <summary>
    /// The one implementation of the "origin dot + aim arrow" marker drawn and dragged on an IMGUI preview
    /// canvas — Launimator's Vector meta-layer editor and Pyre's anchor both use it, so a fix here reaches both.
    /// Coordinates: the marker's <c>origin01</c> is normalized 0..1 across the image, BOTTOM-LEFT origin;
    /// <c>direction</c> is a local unit vector, (0,1) = up. The canvas describes where the image sits on screen.
    ///
    /// Gestures (all inside the canvas box): left-click on empty space places the origin (and, when an arrow
    /// is allowed, starts aiming immediately); drag the origin dot to move it; drag the arrowhead to aim;
    /// right-click erases (when erasing is allowed). Middle-drag panning stays with the host — it belongs to
    /// the canvas, not the marker.
    /// </summary>
    public static class ZuiVectorMarker
    {
        /// Where the image is on screen: <see cref="box"/> is the whole canvas area (local coordinates, the
        /// space <c>Event.current.mousePosition</c> is in), <see cref="ox"/>/<see cref="oy"/> the image's
        /// top-left inside it, <see cref="zoom"/> screen px per image px, <see cref="w"/>/<see cref="h"/> the
        /// image size in px.
        public struct Canvas
        {
            public Rect box;
            public float ox, oy, zoom;
            public int w, h;

            public Canvas(Rect box, float ox, float oy, float zoom, int w, int h)
            { this.box = box; this.ox = ox; this.oy = oy; this.zoom = zoom; this.w = w; this.h = h; }

            /// The image placed at <paramref name="image"/> inside <paramref name="box"/>, with square pixels.
            public static Canvas FromRect(Rect box, Rect image, int w, int h)
                => new Canvas(box, image.x, image.y, w > 0 ? image.width / w : 1f, w, h);
        }

        /// The marker's data, passed by ref so the host keeps ownership of where it is stored.
        public struct Data
        {
            public bool authored;
            public Vector2 origin01;
            public Vector2 direction;
            public float length;
        }

        /// What the host allows on this marker.
        public struct Options
        {
            /// Draw and drag an aim arrow. Off = a plain position marker (dot only).
            public bool arrow;
            /// The arrow's length is authored (drag the head further out); off = a fixed legible length.
            public bool length;
            /// Right-click clears the marker (authored = false).
            public bool erase;
            /// Aim can only land on one of this many evenly-spaced angles around the circle — 16 = one every
            /// 22.5°. 0 (the default) = off, aim anywhere; every host that doesn't set it is unaffected.
            /// The ring is measured from East, so it contains up/down/left/right exactly when the count is a
            /// multiple of 4 — the same convention <c>EvenDirectionsAimer</c> snaps a live aim with.
            public int angleSnapDivisions;
            public Color color;
        }

        const float HitRadius = 10f;          // click hit radius for the dot and the arrowhead (px)
        const float ArrowHeadLength = 10f;
        const float ArrowHeadHalfWidth = 5f;
        const float ArrowBaseFraction = 0.35f; // fixed arrow length as a fraction of the shorter box side

        public static Vector2 OriginToScreen(Vector2 origin01, in Canvas c)
            => new Vector2(c.ox + origin01.x * c.w * c.zoom, c.oy + (c.h - origin01.y * c.h) * c.zoom);

        public static Vector2 ScreenToOrigin(Vector2 s, in Canvas c)
            => new Vector2(Mathf.Clamp01((s.x - c.ox) / (c.w * c.zoom)), Mathf.Clamp01(1f - (s.y - c.oy) / (c.h * c.zoom)));

        /// Arrow tip in screen px: a fixed, legible length when length isn't authorable, or scaled from the
        /// authored length (1 = ~35% of the shorter box dimension) when it is.
        public static Vector2 TipScreen(Vector2 originScreen, Vector2 direction, float length, in Canvas c, bool allowLength)
        {
            float px = BaseLength(c) * (allowLength ? Mathf.Max(0.05f, length) : 1f);
            Vector2 dirScreen = direction.sqrMagnitude > 0.0001f ? new Vector2(direction.x, -direction.y).normalized : Vector2.up;
            return originScreen + dirScreen * px;
        }

        static float BaseLength(in Canvas c) => Mathf.Min(c.box.width, c.box.height) * ArrowBaseFraction;

        /// <summary>Round <paramref name="dir"/> onto the nearest of <paramref name="divisions"/> evenly-spaced
        /// directions. <paramref name="divisions"/> &lt; 2 returns <paramref name="dir"/> untouched (snapping
        /// off). Measured from East (+X) plus <paramref name="offsetDeg"/>, so the default ring contains
        /// 0/90/180/270 — right, up, left, down — exactly whenever the count is a multiple of 4. This is the
        /// same rounding <c>EvenDirectionsAimer.Resolve</c> snaps a live aim with, kept identical on purpose so
        /// a painted direction and a geometrically-aimed one land on the same ring.
        /// <para><paramref name="offsetDeg"/> exists so "let the first position be any number" is a one-line
        /// follow-up; nothing passes it yet.</para></summary>
        public static Vector2 SnapDirection(Vector2 dir, int divisions, float offsetDeg = 0f)
        {
            if (divisions < 2 || dir.sqrMagnitude < 1e-8f) return dir;
            float step = 360f / divisions;
            float deg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float snapped = Mathf.Round((deg - offsetDeg) / step) * step + offsetDeg;
            float rad = snapped * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        /// Draw the marker (or the placement hint when nothing is authored yet).
        public static void Draw(in Canvas c, in Data d, in Options o)
        {
            if (!d.authored)
            {
                GUI.Label(c.box, o.arrow ? "Click to place the origin, then drag to aim.\nRight-click erases."
                                         : "Click to place the anchor.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
                return;
            }
            Vector2 originScreen = OriginToScreen(d.origin01, c);

            Handles.BeginGUI();
            if (o.arrow)
            {
                Vector2 tipScreen = TipScreen(originScreen, d.direction, d.length, c, o.length);
                Handles.color = o.color;
                Handles.DrawAAPolyLine(3f, originScreen, tipScreen);
                Vector2 back = (originScreen - tipScreen).normalized;
                Vector2 perp = new Vector2(-back.y, back.x);
                Handles.DrawAAConvexPolygon(tipScreen,
                    tipScreen + back * ArrowHeadLength + perp * ArrowHeadHalfWidth,
                    tipScreen + back * ArrowHeadLength - perp * ArrowHeadHalfWidth);
            }
            // Origin dot = exactly ONE source pixel at the current zoom — big enough to see, small enough to
            // pinpoint a pixel with, in the marker's own colour/opacity (not a fixed oversized blob).
            float dotR = Mathf.Max(1f, c.zoom * 0.5f);
            Handles.color = o.color;
            Handles.DrawSolidDisc(originScreen, Vector3.forward, dotR);
            Handles.color = new Color(0f, 0f, 0f, o.color.a);
            Handles.DrawWireDisc(originScreen, Vector3.forward, dotR);
            Handles.EndGUI();
        }

        /// <summary>Handle the current IMGUI event against the marker. <paramref name="dragMode"/> is the host's
        /// per-gesture state (0 idle, 1 moving the origin, 2 aiming) — keep it in a field. Returns true when
        /// <paramref name="d"/> changed; <paramref name="beginEdit"/> is called with an Undo label BEFORE the
        /// first mutation of a gesture, so the host records Undo there.</summary>
        public static bool Handle(in Canvas c, ref Data d, in Options o, ref int dragMode, Action<string> beginEdit)
        {
            var e = Event.current;
            Vector2 m = e.mousePosition;
            bool inside = c.box.Contains(m);

            if (e.type == EventType.MouseUp && dragMode != 0) { dragMode = 0; e.Use(); return false; }

            if (e.type == EventType.MouseDrag && dragMode != 0)
            {
                if (dragMode == 1) d.origin01 = ScreenToOrigin(m, c);
                else if (dragMode == 2)
                {
                    Vector2 originScreen = OriginToScreen(d.origin01, c);
                    Vector2 fromOrigin = m - originScreen;
                    if (fromOrigin.sqrMagnitude > 4f)
                    {
                        // Snapped HERE, inside the drag, not on release: the stored direction is the snapped one
                        // from the first moved pixel on, so the drawn arrow visibly detents between the allowed
                        // angles while the mouse is still down instead of jumping once at the end.
                        d.direction = SnapDirection(new Vector2(fromOrigin.x, -fromOrigin.y).normalized,
                                                    o.angleSnapDivisions);
                        if (o.length) d.length = Mathf.Max(0.05f, fromOrigin.magnitude / BaseLength(c));
                    }
                }
                e.Use();
                return true;
            }

            if (!inside || e.type != EventType.MouseDown) return false;

            if (e.button == 1)
            {
                if (!o.erase || !d.authored) return false;
                beginEdit?.Invoke("Clear marker");
                d.authored = false;
                e.Use();
                return true;
            }
            if (e.button != 0) return false;

            if (d.authored)
            {
                Vector2 originScreen = OriginToScreen(d.origin01, c);
                if ((m - originScreen).sqrMagnitude <= HitRadius * HitRadius)
                { beginEdit?.Invoke("Move marker origin"); dragMode = 1; e.Use(); return false; }
                if (o.arrow)
                {
                    Vector2 tipScreen = TipScreen(originScreen, d.direction, d.length, c, o.length);
                    if ((m - tipScreen).sqrMagnitude <= HitRadius * HitRadius)
                    { beginEdit?.Invoke("Aim marker"); dragMode = 2; e.Use(); return false; }
                }
            }
            beginEdit?.Invoke("Place marker");
            d.authored = true;
            d.origin01 = ScreenToOrigin(m, c);
            if (o.arrow)
            {
                // Snapped too, so a click that never drags can't leave an off-ring direction on a division
                // count that doesn't contain straight up (anything not a multiple of 4).
                d.direction = SnapDirection(Vector2.up, o.angleSnapDivisions);
                if (o.length) d.length = 1f;
                dragMode = 2;   // start aiming immediately: "click to place, drag to aim"
            }
            else dragMode = 1;  // a plain position: keep dragging it until release
            e.Use();
            return true;
        }
    }
}
