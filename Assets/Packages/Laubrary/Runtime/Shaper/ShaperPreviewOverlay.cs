// ShaperPreviewOverlay — the capability interface a Shaper feature implements when it needs its own marks on
// the preview, plus the pixel canvas those marks are drawn into.
//
// ── Why this is an interface and not a toggle in the preview chrome ────────────────────────────────────────
// The checklist row this closes (6.2) is "with Swarm on, the preview draws a spawn outline and a dot at every
// real spawn point". The cheap way to get that is a "Show swarm" toggle in the transport row. That toggle then
// sits there forever, on every document with no swarm in it, and the next feature that wants a mark adds a
// second one beside it — the window ends up knowing each feature by name and the chrome grows linearly with
// them. authoring.md §15 states the rule and SpriteFx is its worked example: the FEATURE declares that it has
// something to show, the host DISCOVERS it, and the toggle appears and disappears with the feature.
//
// ISpriteFxPreviewOverlay is deliberately not reused here — §15 says so explicitly ("do not stretch it across
// tools"). What is reused is its SHAPE: a small interface in this tool's Runtime asmdef (unguarded by
// UNITY_EDITOR, because the things that implement it are runtime data and a runtime assembly cannot reference
// editor code), a static editor-side collector, and a host that reserves one toggle strip.
//
// ── Why the canvas is pixels and not Handles ──────────────────────────────────────────────────────────────
// Pyre draws its own overlay with Handles into an IMGUI viewport. ShaperPreviewStage is UI Toolkit throughout
// and getting it back an IMGUI island purely to draw a circle would undo T-0146's own work. Drawing into the
// Color32[] the stage is about to display instead is what SpriteFx already does, costs no new element, and
// puts the marks in exactly the space the thing being marked was rendered in — a mark computed in canvas units
// and blitted in canvas units cannot drift from its subject when the stage is resized or zoomed.
//
// The overlay NEVER reaches a bake, and that is structural rather than a matter of discipline: nothing in
// Runtime's render path calls this, and the editor host draws into a COPY of the cached frame (see
// ShaperPreviewOverlays), so even the preview's own frame cache never holds a pixel an overlay wrote.
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The buffer a preview overlay draws into, plus the canvas→pixel mapping and the sampling context
    /// (phase and seed) the marks have to be computed at.
    ///
    /// <para>Coordinates are CANVAS UNITS, centred on the canvas, +Y up — the same space
    /// <see cref="ShaperTransformBlock"/> and every dial that measures a position speak, so an implementer
    /// converts nothing. Row 0 of the buffer is the bottom row, matching the renderer's own convention (see
    /// ShaperPreviewStage's header for why no flip happens anywhere in this tool).</para>
    /// </summary>
    public sealed class ShaperOverlayCanvas
    {
        public Color32[] pixels;
        public int width, height;
        /// <summary>Canvas units per sample — <see cref="ShaperDocument.pixelSize"/>.</summary>
        public float pixelSize = 1f;

        /// <summary>The document phase the marks must be sampled at, so an overlay over a playing document
        /// moves with the animation instead of freezing at phase 0.</summary>
        public float phase01;
        /// <summary>The document seed, so a deterministic draw drawn as a mark lands where the renderer's own
        /// deterministic draw put it.</summary>
        public uint seed;

        /// <summary>Where the owning node's content is centred, in canvas units — everything an overlay draws
        /// is relative to this, because a swarm's offsets are the node's offsets and not the canvas's.</summary>
        public Vector2 origin;

        public void Set(Color32[] px, int w, int h, float ps, float phase, uint documentSeed)
        {
            pixels = px; width = w; height = h; pixelSize = Mathf.Max(1e-4f, ps);
            phase01 = phase; seed = documentSeed; origin = Vector2.zero;
        }

        /// <summary>A canvas-unit point to a buffer column/row. Fractional on purpose — the callers round.</summary>
        public Vector2 ToPixel(Vector2 canvasPoint)
        {
            float halfW = 0.5f * (width - 1) * pixelSize, halfH = 0.5f * (height - 1) * pixelSize;
            return new Vector2((canvasPoint.x + halfW) / pixelSize, (canvasPoint.y + halfH) / pixelSize);
        }

        /// <summary>Write one pixel, blended over what is already there. Out-of-bounds is a no-op rather than
        /// an exception: an overlay marking something the author has dragged off the canvas is a normal state
        /// to be in, not a bug to throw on.</summary>
        public void Plot(int x, int y, Color32 c)
        {
            if (pixels == null || x < 0 || y < 0 || x >= width || y >= height) return;
            int i = y * width + x;
            var dst = pixels[i];
            float a = c.a / 255f;
            // Straight-alpha "over". The marks are diagnostics on top of a picture that may be fully
            // transparent, so a plain lerp toward the mark colour is what keeps them readable on both a bright
            // fill and on nothing at all.
            pixels[i] = new Color32(
                (byte)Mathf.RoundToInt(dst.r + (c.r - dst.r) * a),
                (byte)Mathf.RoundToInt(dst.g + (c.g - dst.g) * a),
                (byte)Mathf.RoundToInt(dst.b + (c.b - dst.b) * a),
                (byte)Mathf.Max(dst.a, c.a));
        }

        /// <summary>A filled disc of radius <paramref name="r"/> BUFFER pixels at a canvas-unit centre —
        /// the "here is a real spawn point" mark.</summary>
        public void Dot(Vector2 canvasCentre, float r, Color32 c)
        {
            var p = ToPixel(canvasCentre);
            int lo = Mathf.FloorToInt(-r), hi = Mathf.CeilToInt(r);
            for (int dy = lo; dy <= hi; dy++)
                for (int dx = lo; dx <= hi; dx++)
                    if (dx * dx + dy * dy <= r * r)
                        Plot(Mathf.RoundToInt(p.x) + dx, Mathf.RoundToInt(p.y) + dy, c);
        }

        /// <summary>A one-pixel line between two canvas-unit points (Bresenham over the buffer).</summary>
        public void Line(Vector2 aCanvas, Vector2 bCanvas, Color32 c)
        {
            var a = ToPixel(aCanvas);
            var b = ToPixel(bCanvas);
            int x0 = Mathf.RoundToInt(a.x), y0 = Mathf.RoundToInt(a.y);
            int x1 = Mathf.RoundToInt(b.x), y1 = Mathf.RoundToInt(b.y);
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            // Bounded: a wildly off-canvas endpoint would otherwise walk for as many steps as it is far away,
            // and an authored offset has no upper limit.
            for (int guard = 0; guard < 4096; guard++)
            {
                Plot(x0, y0, c);
                if (x0 == x1 && y0 == y1) return;
                int e2 = err * 2;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>An unfilled circle outline of <paramref name="radiusCanvas"/> canvas units. Stepped by
        /// arc length rather than by a fixed segment count, so a big circle does not come out dotted.</summary>
        public void Circle(Vector2 centreCanvas, float radiusCanvas, Color32 c)
        {
            float rp = Mathf.Max(1f, radiusCanvas / pixelSize);
            int steps = Mathf.Clamp(Mathf.CeilToInt(rp * 6f), 12, 720);
            Vector2 prev = default;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps * Mathf.PI * 2f;
                var p = centreCanvas + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radiusCanvas;
                if (i > 0) Line(prev, p, c);
                prev = p;
            }
        }

        /// <summary>An unfilled axis-aligned box outline, given half-extents in canvas units.</summary>
        public void Box(Vector2 centreCanvas, Vector2 halfExtents, Color32 c)
        {
            var a = centreCanvas + new Vector2(-halfExtents.x, -halfExtents.y);
            var b = centreCanvas + new Vector2(halfExtents.x, -halfExtents.y);
            var d = centreCanvas + new Vector2(halfExtents.x, halfExtents.y);
            var e = centreCanvas + new Vector2(-halfExtents.x, halfExtents.y);
            Line(a, b, c); Line(b, d, c); Line(d, e, c); Line(e, a, c);
        }
    }

    /// <summary>
    /// Implemented by any Shaper feature that has something to MARK on the preview. The host collects every
    /// implementer reachable from the document, offers one toggle per instance, and calls
    /// <see cref="DrawPreviewOverlay"/> for the ones switched on — see this file's header for why a window
    /// toggle is the wrong shape for the same job.
    /// </summary>
    public interface IShaperPreviewOverlay
    {
        /// <summary>A short noun for the thing being marked ("Swarm spawns"), not a verb and not the feature's
        /// own name — it becomes a segment label in a strip the user reads at a glance.</summary>
        string OverlayLabel { get; }

        /// <summary>What the marks mean and what they are for. Written for someone who has never seen them.</summary>
        string OverlayTooltip { get; }

        /// <summary>Whether THIS instance has anything to show right now, given its CONFIGURATION — not merely
        /// whether it is enabled. The instance decides; the host never guesses from the type. Answering true
        /// when nothing would be drawn is the real bug, because it offers a toggle that does nothing.</summary>
        bool WantsPreviewOverlay { get; }

        /// <summary>Draw this instance's marks into <paramref name="canvas"/>. Marks THIS instance only — the
        /// host hands out one entry per instance, so scanning for siblings would double-draw.</summary>
        void DrawPreviewOverlay(ShaperOverlayCanvas canvas);
    }
}
