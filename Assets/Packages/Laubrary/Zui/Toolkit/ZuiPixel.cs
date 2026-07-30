// ZuiPixel — draw a pixel-exact image inside a bespoke IMGUI preview canvas.
//
// THE TRAP, in one paragraph, because it is invisible on most machines and therefore ships:
// IMGUI measures everything in GUI POINTS. The editor RASTERIZES in device pixels.
// EditorGUIUtility.pixelsPerPoint is the ratio between the two, and on a 100%-scaled display it is exactly
// 1 — points ARE pixels, nothing below matters, and an author working at 100% will never see the bug they
// just shipped. On a scaled display it is 1.25, 1.5, 2, 2.25… and then an integer number of POINTS is a
// FRACTIONAL number of pixels: an authored block 6 points across at pixelsPerPoint 2.25 is 13.5 device
// pixels, so consecutive blocks come out alternately 13 and 14 pixels wide and a one-pixel sliver of the
// neighbouring colour lands INSIDE what should be one flat authored pixel. The buffer can be perfect pixel
// art, point-filtered, and still reach the screen looking like it is not.
//
// The cure is to decide every size and every position in DEVICE pixels and convert back to points only at
// the very last step — which is all this file does. Three parts of it matter and are easy to miss:
//  • the buffer size must come from the DEVICE-pixel viewport, not the point viewport;
//  • the fit must FLOOR, never round, or the image overflows the viewport and IMGUI clips it mid-block at
//    the container's edge (which is not a block boundary);
//  • the destination rect's corner must be snapped against the container's PANEL-space origin, because
//    where the IMGUI island itself sits inside the window is half of whether a local coordinate lands on a
//    whole device pixel.
//
// Reach for this through Z.PixelFit / Z.DrawPixels; ZuiPixel is the implementation behind them and is
// equally usable from a plain EditorWindow's OnGUI (pass Vector2.zero as the panel origin there).
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>Where one pixel-exact image lands and at what zoom — the "where do I draw it?" half of the
    /// problem, produced by <see cref="ZuiPixelFit.Place(int,int)"/> once the buffer actually exists.
    ///
    /// It is a struct rather than a bare Rect because <see cref="zoom"/> is a number the caller genuinely
    /// needs: the authored pixel size a user dials is a request in POINTS, and what reaches the screen is a
    /// whole number of DEVICE pixels that may not correspond to it. Handing back only a rect hides that
    /// discrepancy at exactly the moment someone is trying to work out why their preview looks off.</summary>
    public readonly struct ZuiPixelPlacement
    {
        /// <summary>The destination rect, back in the caller's own LOCAL GUI points — pass it straight to
        /// GUI.DrawTexture / Graphics.DrawTexture, or to <c>ZuiPixel.Draw</c> / <c>Z.DrawPixels</c>.
        ///
        /// Its corner is chosen so that AFTER the GUI multiplies by pixelsPerPoint on the way out, the
        /// rasterized rect begins on a whole device pixel and is exactly width × zoom device pixels across.
        /// It is therefore usually NOT a whole number of points, and the caller must not round it, snap it or
        /// nudge it — rounding in point space is precisely the bug this whole file exists to remove.</summary>
        public readonly Rect rect;

        /// <summary>Whole DEVICE pixels per authored pixel actually used. Never fractional, always ≥ 1, and
        /// possibly smaller than the zoom the fit asked for when the buffer that came back was bigger than
        /// the one requested. Worth logging when a preview looks wrong: it is the one number that says how
        /// much of an authored pixel the screen is really showing.</summary>
        public readonly int zoom;

        /// <summary>The buffer size this placement was computed against, in authored pixels. Kept so a caller
        /// can assert the placement still matches the texture it is about to draw.</summary>
        public readonly int width, height;

        /// <summary>Normally you get one from <see cref="ZuiPixelFit.Place(int,int)"/> rather than building it.
        /// Public only for a caller doing its own mapping that still wants the point-filtered, optionally
        /// premultiplied draw — pass a rect you have ALREADY snapped in device space.</summary>
        public ZuiPixelPlacement(Rect rect, int zoom, int width, int height)
        {
            this.rect = rect; this.zoom = zoom; this.width = width; this.height = height;
        }

        /// <summary>False when there is nothing to draw (no buffer, or a viewport too small to hold one whole
        /// authored pixel). Checked by <c>ZuiPixel.Draw</c>, so a caller normally need not.</summary>
        public bool IsValid => zoom > 0 && rect.width > 0f && rect.height > 0f;
    }

    /// <summary>The "what buffer should I render?" half of the problem: how big a low-resolution buffer the
    /// viewport can hold, and at what whole-device-pixel zoom it will be blown back up.
    ///
    /// This is deliberately SEPARATE from the placement above, and the two cannot be collapsed into one
    /// do-everything call. A caller that renders its own buffer (a PreviewRenderUtility scene, a CPU
    /// rasterizer, a compute pass) needs the size BEFORE it renders and the snapped destination rect only
    /// AFTER — and what it gets back may not be the size it asked for, in which case the placement has to be
    /// computed from the texture that actually arrived. So: <c>Z.PixelFit</c> once per repaint, render, then
    /// <see cref="Place(Texture)"/> on the result.</summary>
    public readonly struct ZuiPixelFit
    {
        /// <summary>The viewport exactly as the caller gave it: GUI points, LOCAL to the IMGUI island (this is
        /// what GUILayoutUtility.GetRect hands back, and it is routinely fractional).</summary>
        public readonly Rect viewport;

        /// <summary>Where that island's top-left sits in PANEL space, in points. Part of the mapping, not an
        /// optional refinement: a local coordinate only lands on a whole device pixel if the container it is
        /// local to does too. Vector2.zero for a plain EditorWindow's OnGUI, whose IMGUI space IS panel
        /// space.</summary>
        public readonly Vector2 panelOrigin;

        /// <summary>EditorGUIUtility.pixelsPerPoint as read at the moment of the fit. Read live and never
        /// assumed — it changes with the display the window is dragged onto, and hard-coding any particular
        /// value silently breaks on every other machine.</summary>
        public readonly float pixelsPerPoint;

        /// <summary>The viewport in DEVICE pixels. Every decision here is taken in this space; points are
        /// only ever an input and an output.</summary>
        public readonly Vector2 deviceSize;

        /// <summary>Whole DEVICE pixels per authored pixel, i.e. the zoom this fit is aiming for. Derived by
        /// ROUNDING the requested points-per-pixel into device pixels, so the authored size is honoured as
        /// closely as a whole number of pixels allows and never lands half way between two.</summary>
        public readonly int zoom;

        /// <summary>The buffer to render, in authored pixels — the viewport's device size divided by the zoom
        /// and FLOORED, so the blown-up image can never overflow the viewport and be clipped mid-block.
        /// A caller that renders its own buffer should render exactly this (and cap it if its renderer has a
        /// practical size ceiling).</summary>
        public readonly int width, height;

        internal ZuiPixelFit(Rect viewport, Vector2 panelOrigin, float pixelsPerPoint, Vector2 deviceSize,
            int zoom, int width, int height)
        {
            this.viewport = viewport; this.panelOrigin = panelOrigin; this.pixelsPerPoint = pixelsPerPoint;
            this.deviceSize = deviceSize; this.zoom = zoom; this.width = width; this.height = height;
        }

        /// <summary>False when the viewport is degenerate (zero-sized, or not laid out yet) — nothing sane can
        /// be rendered or drawn into it.</summary>
        public bool IsValid => deviceSize.x >= 1f && deviceSize.y >= 1f;

        /// <summary>Where to put a buffer of exactly this size: the destination rect in the caller's own local
        /// points, with its corner snapped so the rasterized result starts on a whole device pixel and every
        /// authored pixel is an identical whole-pixel block. Centred in the viewport.
        ///
        /// Pass the size of the buffer you ACTUALLY got, not the size you asked for. That is the whole reason
        /// this is a second call: a renderer that supersamples, or one that truncates its own scaling, hands
        /// back a texture a texel or two (or a whole factor) off the ask, and the placement has to be built
        /// from reality or the blocks go ragged again.
        ///
        /// The zoom is the SMALLER of this fit's own zoom and the largest whole zoom the buffer fits at, and
        /// the fit is FLOORED — a rounded fit would blow the image past the viewport, where IMGUI clips it at
        /// the container's edge, which is not a block boundary.</summary>
        public ZuiPixelPlacement Place(int bufferWidth, int bufferHeight)
        {
            int bw = Mathf.Max(1, bufferWidth), bh = Mathf.Max(1, bufferHeight);
            float ppp = Mathf.Max(0.01f, pixelsPerPoint);

            int fit = Mathf.Min(Mathf.FloorToInt(deviceSize.x / bw), Mathf.FloorToInt(deviceSize.y / bh));
            int z = Mathf.Max(1, Mathf.Min(zoom, fit));

            // Centre in DEVICE pixels and round the corner onto one, then take the rect back into points. The
            // GUI multiplies by pixelsPerPoint again on the way out, so what is rasterized is an integer
            // device rect exactly bw whole blocks wide — no half-pixel origin, no ragged block.
            float wDev = bw * z, hDev = bh * z;
            float xDev = Mathf.Round((panelOrigin.x + viewport.x) * ppp + (deviceSize.x - wDev) * 0.5f);
            float yDev = Mathf.Round((panelOrigin.y + viewport.y) * ppp + (deviceSize.y - hDev) * 0.5f);

            return new ZuiPixelPlacement(
                new Rect(xDev / ppp - panelOrigin.x, yDev / ppp - panelOrigin.y, wDev / ppp, hDev / ppp),
                z, bw, bh);
        }

        /// <summary>Place the buffer that came back, measured off the texture itself — the call to reach for,
        /// since the texture is the only honest record of what a renderer actually produced.</summary>
        public ZuiPixelPlacement Place(Texture buffer)
            => buffer == null ? default : Place(buffer.width, buffer.height);
    }

    /// <summary>Pixel-exact image drawing for a bespoke IMGUI preview canvas — the shared answer to "my
    /// pixel-art preview has ragged blocks on a scaled display." See the file header for the points-versus-
    /// device-pixels trap this exists to remove; the short version is that IMGUI speaks points, the screen
    /// speaks pixels, and an integer number of the former is a fractional number of the latter for most
    /// display scalings.
    ///
    /// Normally reached through <c>Z.PixelFit</c> and <c>Z.DrawPixels</c>. Nothing here touches
    /// UI Toolkit beyond reading a container's panel position, so it works just as well inside a plain
    /// EditorWindow's OnGUI — pass <see cref="Vector2.zero"/> as the panel origin there.</summary>
    public static class ZuiPixel
    {
        /// <summary>Plan a pixel-exact render: how big a buffer to draw into and how many whole device pixels
        /// each of its texels will occupy, for a viewport measured in GUI points.
        ///
        /// <paramref name="pointsPerPixel"/> is the authored pixel size as the USER dials it — in points,
        /// because that is the unit every other size in an editor window is in. It is converted here to a
        /// whole number of device pixels, which is why the on-screen block may end up very slightly larger or
        /// smaller than the number dialled: a block that is not a whole number of device pixels is exactly
        /// what produces the ragged edges.</summary>
        public static ZuiPixelFit Fit(Rect viewport, Vector2 panelOrigin, float pointsPerPixel)
            => Build(viewport, panelOrigin, Mathf.Max(1f, pointsPerPixel));

        /// <inheritdoc cref="Fit(Rect,Vector2,float)"/>
        /// <remarks>Takes the panel origin off the IMGUI container itself — the usual call from a ZuiWindow,
        /// whose preview canvas is an IMGUIContainer somewhere inside the window rather than at its corner.
        /// A null or not-yet-laid-out container degrades to a zero origin.</remarks>
        public static ZuiPixelFit Fit(Rect viewport, VisualElement container, float pointsPerPixel)
            => Fit(viewport, PanelOrigin(container), pointsPerPixel);

        /// <summary>A fit with NO authored pixel size: the zoom is left wide open, so
        /// <see cref="ZuiPixelFit.Place(int,int)"/> picks the largest whole-device-pixel zoom the buffer fits
        /// the viewport at.
        ///
        /// For the caller who already HAS a pixel-art texture (a baked sprite, a palette strip, a thumbnail)
        /// and simply wants it as big as it goes without a fractional block anywhere. Its
        /// <see cref="ZuiPixelFit.width"/>/<see cref="ZuiPixelFit.height"/> are meaningless — there is no
        /// buffer to render, only one to place.</summary>
        public static ZuiPixelFit FitLargest(Rect viewport, Vector2 panelOrigin)
            => Build(viewport, panelOrigin, 0f);

        /// <inheritdoc cref="FitLargest(Rect,Vector2)"/>
        public static ZuiPixelFit FitLargest(Rect viewport, VisualElement container)
            => Build(viewport, PanelOrigin(container), 0f);

        /// <summary>Put a placed buffer on screen with nearest-neighbour sampling.
        ///
        /// Restates FilterMode.Point (and a clamped wrap, so a Point-sampled edge texel cannot pull in the
        /// opposite edge) on every draw rather than trusting whoever allocated the texture: a bilinear buffer
        /// undoes the entire exercise, and a render target's filter mode is easily reset by whatever produced
        /// it. Call during a Repaint event only — Graphics.DrawTexture is a repaint-time API.
        ///
        /// <paramref name="material"/> is optional and exists for buffers that need a blend other than the
        /// GUI's plain SrcAlpha/OneMinusSrcAlpha — chiefly a PREMULTIPLIED buffer (anything a TMP or SDF
        /// shader rendered), which the default blend multiplies by alpha a second time and composites every
        /// soft rim too dark. A null material means the plain GUI blend, which is also the graceful fallback
        /// when a caller's own shader failed to load.</summary>
        public static void Draw(in ZuiPixelPlacement placement, Texture buffer, Material material = null)
        {
            if (buffer == null || !placement.IsValid) return;
            buffer.filterMode = FilterMode.Point;
            buffer.wrapMode = TextureWrapMode.Clamp;

            if (material == null)
            {
                GUI.DrawTexture(placement.rect, buffer, ScaleMode.StretchToFill, true);
                return;
            }
            Graphics.DrawTexture(placement.rect, buffer, new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0,
                Color.white, material);
        }

        /// <summary>Fit AND draw an existing pixel-art texture in one call: centred in the viewport, blown up
        /// by the largest whole number of device pixels it fits at, corner snapped onto a device pixel.
        ///
        /// The one-liner for a caller who is not rendering their own buffer. A caller who IS should use
        /// <see cref="Fit(Rect,VisualElement,float)"/> → render → <see cref="ZuiPixelFit.Place(Texture)"/> →
        /// the placement overload of <c>Draw</c> instead: the buffer size has to be known before the render.
        /// Returns the placement so the zoom is available to reason about or log.</summary>
        public static ZuiPixelPlacement Draw(Rect viewport, Vector2 panelOrigin, Texture buffer,
            Material material = null)
        {
            if (buffer == null) return default;
            var placement = FitLargest(viewport, panelOrigin).Place(buffer);
            Draw(placement, buffer, material);
            return placement;
        }

        /// <inheritdoc cref="Draw(Rect,Vector2,Texture,Material)"/>
        public static ZuiPixelPlacement Draw(Rect viewport, VisualElement container, Texture buffer,
            Material material = null)
            => Draw(viewport, PanelOrigin(container), buffer, material);

        /// <summary>An IMGUI island's top-left in PANEL space, NaN-guarded for the frames before UI Toolkit has
        /// laid it out. Exposed because a caller that caches a fit needs the same origin the fit used.</summary>
        public static Vector2 PanelOrigin(VisualElement container)
        {
            if (container == null) return Vector2.zero;
            var p = container.worldBound.position;
            return float.IsNaN(p.x) || float.IsNaN(p.y) ? Vector2.zero : p;
        }

        // The one place the mapping is computed. A `pointsPerPixel` of zero or less means "no authored size" —
        // the zoom is left open for Place() to clamp down to whatever the buffer fits at.
        static ZuiPixelFit Build(Rect viewport, Vector2 panelOrigin, float pointsPerPixel)
        {
            float ppp = Mathf.Max(0.01f, EditorGUIUtility.pixelsPerPoint);
            if (float.IsNaN(panelOrigin.x) || float.IsNaN(panelOrigin.y)) panelOrigin = Vector2.zero;

            var device = new Vector2(Mathf.Max(0f, viewport.width) * ppp, Mathf.Max(0f, viewport.height) * ppp);

            // One authored pixel is `pointsPerPixel` points across, which on this display is that many times
            // ppp screen pixels — rounded to a WHOLE number of them, because that is the block every buffer
            // texel has to fill exactly. With no authored size, leave the zoom open.
            int zoom = pointsPerPixel > 0f
                ? Mathf.Max(1, Mathf.RoundToInt(pointsPerPixel * ppp))
                : int.MaxValue;

            // FLOOR, so the blown-up image can never overflow the viewport and get clipped mid-block.
            int w = Mathf.Max(1, Mathf.FloorToInt(device.x / zoom));
            int h = Mathf.Max(1, Mathf.FloorToInt(device.y / zoom));

            return new ZuiPixelFit(viewport, panelOrigin, ppp, device, zoom, w, h);
        }
    }
}
