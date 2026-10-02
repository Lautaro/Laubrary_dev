using System.Collections.Generic;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.MetaMapper.Editor
{
    /// <summary>
    /// THE STAGE — the subject picture with the authored marks on top, and the surface every authoring
    /// gesture happens on.
    ///
    /// Behaviour set inherited wholesale from <c>TilesetBuilderWindow.SheetStage</c> (its twin; the design
    /// permitted either extraction or a copy-with-twin-comment, and the two differ enough — one paints cell
    /// selections on a sheet grid, this one paints map-space markers through an arbitrary affine — that a
    /// shared base would be mostly parameters):
    ///   • a CHECKERBOARD behind the art, because "transparent" rendered against a dark stage reads as BLACK
    ///     ART and is indistinguishable from actually-black pixels (a real user question, 2026-08-01);
    ///   • the image as a CHILD with every overlay draw on its own Painter2D overlay child kept IN FRONT —
    ///     Painter2D content draws BENEATH an element's children, a trap this codebase has paid for twice;
    ///   • a SELF-EXPLAINING guide label, because an empty void teaches nothing;
    ///   • the DEGENERATE-GEOMETRY guard, here inherited free from <see cref="ZuiPanZoom"/>, whose Layout
    ///     refuses to run against un-laid-out geometry so the pan write-back can never destroy the view.
    /// Wheel-zoom-toward-pointer and middle-drag pan are <see cref="ZuiPanZoom"/>'s, not forked.
    ///
    /// COORDINATES. Three of them, and keeping them straight is the whole job:
    ///   • MAP space — what the data stores. +y UP, origin at the subject's own origin.
    ///   • TEXTURE pixels — +y UP from the picture's bottom-left. <c>visual.MapToTexture</c> is the bridge.
    ///   • LOCAL (GUI) pixels — +y DOWN from this element's top-left, scaled and panned by the view.
    /// The y flip lives HERE and nowhere else: the §7 contract is stated entirely in +y-up terms.
    /// </summary>
    internal class MetaStage : VisualElement
    {
        readonly MetaMapperWindow w;
        readonly Image image;
        readonly VisualElement checker;
        readonly VisualElement overlay;
        readonly VisualElement maskHost;
        readonly Label guide;
        readonly ZuiPanZoom view;
        Texture2D checkerTex;

        /// One baked mask texture per (layer, frame), kept until its content changes. Twin of
        /// <c>AnimationBuilderWindow.MaskTexture</c>'s cache — the same idea for the same data.
        class MaskEntry { public int hash; public Texture2D tex; }
        readonly Dictionary<long, MaskEntry> maskCache = new Dictionary<long, MaskEntry>();
        readonly List<Image> maskViews = new List<Image>();

        enum Gesture { None, MoveMark, AimMark, Paint, Erase, DrawShape, MoveShape, ResizeShape }
        Gesture gesture;
        int gestureMark = -1;
        Vector2 gestureAnchor;           // DrawShape/ResizeShape: the fixed point; MoveShape: the press point
        Vector2 gestureStartCenter;      // MoveShape: where the shape was when the drag began
        Vector2 lastMapPos;
        bool hasHover;
        Vector2 hoverMap;

        /// The mark the last gesture touched — what Delete removes and what the overlay rings.
        internal int SelectedMark { get; private set; } = -1;

        /// The shape the last gesture touched on a Shapes layer — what Delete removes, what shows resize
        /// handles, and what the tool row's name field edits.
        internal int SelectedShape { get; private set; } = -1;

        public MetaStage(MetaMapperWindow window)
        {
            w = window;
            AddToClassList("zui-stage");
            style.flexGrow = 1f;
            style.minHeight = 0f;
            style.overflow = Overflow.Hidden;
            focusable = true;                       // Delete has to reach us
            tooltip = "The subject, with this map's marks on it. Wheel zooms toward the pointer; middle-drag " +
                      "pans while zoomed. What a click does depends on the ACTIVE LAYER's kind — see the hint " +
                      "under the canvas.";

            checker = new VisualElement { pickingMode = PickingMode.Ignore };
            checker.style.position = Position.Absolute;
            Add(checker);

            image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute;
            Add(image);

            // Painted masks are BAKED TEXTURES, not Painter2D geometry — see MaskTexture. They live between
            // the subject and the marker overlay, so masks sit on the art and marks sit on the masks.
            maskHost = new VisualElement { pickingMode = PickingMode.Ignore };
            maskHost.style.position = Position.Absolute;
            maskHost.style.left = maskHost.style.top = maskHost.style.right = maskHost.style.bottom = 0f;
            Add(maskHost);

            // Painter2D content draws BENEATH children, so every marker draw lives on this overlay child,
            // added AFTER the image and kept in front — never on the stage itself.
            overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute;
            overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0f;
            overlay.generateVisualContent += PaintOverlay;
            Add(overlay);

            guide = new Label { pickingMode = PickingMode.Ignore };
            guide.style.position = Position.Absolute;
            guide.style.left = guide.style.right = 0f;
            guide.style.top = 10f;
            guide.style.unityTextAlign = TextAnchor.MiddleCenter;
            guide.style.fontSize = 12f;
            guide.style.color = new Color(1f, 1f, 1f, 0.55f);
            guide.style.whiteSpace = WhiteSpace.Normal;
            Add(guide);

            view = new ZuiPanZoom(this);
            // The view position belongs to the WINDOW, not to this element: switching layer or frame rebuilds
            // the stage, and throwing the canvas back to fit each time would be maddening mid-authoring.
            view.Zoom = w.StageZoom;
            view.Pan = w.StagePan;
            view.ViewChanged += () => { w.StageZoom = view.Zoom; w.StagePan = view.Pan; Refresh(); };

            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerLeaveEvent>(_ => { hasHover = false; overlay.MarkDirtyRepaint(); });
            RegisterCallback<KeyDownEvent>(OnKey);
        }

        /// Zoom actually in force — the number the status line reports.
        public float DisplayScale => view.Scale;

        public void Refresh()
        {
            Layout();
            SyncMaskViews();
            guide.text = GuideText();
            overlay.MarkDirtyRepaint();
            maskHost.BringToFront();
            overlay.BringToFront();
            guide.BringToFront();
        }

        string GuideText()
        {
            var vis = w.Visual;
            if (vis == null) return "No subject — open a MetaMap or hand one over from a tool.";
            // Drift belongs OVER THE CANVAS, not only in the left pane: this is the surface the author is
            // looking at, and anything placed before the drift is resolved is authored against the old shape.
            if (vis.drift != null)
                return "⚠ " + vis.drift.what + "\nNothing has moved yet — choose Re-anchor or Keep coords " +
                       "under Subject, on the left.";
            var layer = w.ActiveLayer;
            // A character's map has no picture corner to read the origin off: say what the amber cross is.
            string root = w.Space == MapSpace.LocalUnits ? "Amber cross = the root (map origin) · " : "";
            if (layer == null)
                return root + "Add a layer on the left.\nA layer that merely EXISTS is already the answer to " +
                       "\"is this thing a loot shelf?\" — no marks required.";
            switch (layer.kind)
            {
                case LayerKind.Shapes:
                    return root + "Drag on empty space to draw a " +
                           (w.NewShapeKind == ShapeKind.Circle ? "circle" : "rectangle") +
                           " · drag a shape to move it · drag a white handle to resize · Delete removes · " +
                           "Alt places freely";
                case LayerKind.Points:
                    return root + (w.Space == MapSpace.GridCells
                        ? "Click to place a point · drag one to move it · Delete removes · hold Alt for free placement"
                        : "Click to place a point · drag one to move it · Delete removes");
                case LayerKind.Directions:
                    return root + "Press and drag to place-and-aim · drag a head to re-aim, a base to move · Delete removes";
                case LayerKind.Mask:
                    return root + "Drag to paint · right-drag (or Alt) erases · brush size and value are above";
                default:
                    return "";
            }
        }

        void Layout()
        {
            var vis = w.Visual;
            var tex = vis?.FrameAt(w.Frame);
            if (vis == null || tex == null)
            {
                image.style.display = DisplayStyle.None;
                checker.style.display = DisplayStyle.None;
                return;
            }
            if (!view.Layout(vis.TextureSize)) return;   // degenerate geometry: leave the view alone
            w.StagePan = view.Pan;                       // Layout clamps and writes back; keep the window in step

            float pw = vis.TextureSize.x * view.Scale, ph = vis.TextureSize.y * view.Scale;
            image.style.display = DisplayStyle.Flex;
            image.image = tex;
            image.style.left = view.Origin.x;
            image.style.top = view.Origin.y;
            image.style.width = pw;
            image.style.height = ph;

            EnsureChecker();
            checker.style.display = DisplayStyle.Flex;
            checker.style.left = view.Origin.x;
            checker.style.top = view.Origin.y;
            checker.style.width = pw;
            checker.style.height = ph;

            PositionMaskViews();
        }

        // ── painted masks: BAKED TEXTURES, not geometry ─────────────────────────────
        //
        // The first cut of this drew one Painter2D quad per painted cell, batched by value into ≤10 Fill
        // calls. That fixed the CALL count and not the QUAD count: a 64×64 mask is 4096 quads re-tessellated
        // on every PointerMoveEvent of a brush stroke, which is exactly the sizes this editor is FOR.
        // Launimator had already solved it for the identical data (AnimationBuilderWindow.MaskTexture): bake
        // the mask into one point-filtered Texture2D, cache it against a content hash, and draw it as a
        // single image. Repainting an unchanged mask then costs nothing at all. Adopted here verbatim in
        // spirit — MetaMapper is the model Launimator is meant to adopt, so it must not be the worse of the
        // two. The MetaPalette.CellColor ramp is untouched, so the picture is pixel-identical.

        /// Reconcile one Image per mask layer with the model, rebaking only what changed.
        void SyncMaskViews()
        {
            var data = w.Data;
            var vis = w.Visual;
            int used = 0;
            if (data?.layers != null && vis != null)
            {
                var active = w.ActiveLayer;
                for (int i = 0; i < data.layers.Count; i++)
                {
                    var L = data.layers[i];
                    if (L == null || L.kind != LayerKind.Mask || !w.IsLayerVisible(L)) continue;
                    bool isActive = ReferenceEquals(L, active);
                    if (!w.ShowAllLayers && !isActive) continue;
                    var entry = data.EntryAt(L, w.Frame);
                    if (entry == null || !entry.MaskUsable) continue;
                    var tex = MaskTexture(i, w.Frame, L, entry);
                    if (tex == null) continue;

                    var img = ViewAt(used++);
                    img.image = tex;
                    img.tintColor = new Color(1f, 1f, 1f, isActive ? 0.62f : 0.30f);
                    img.style.display = DisplayStyle.Flex;
                }
            }
            for (int i = used; i < maskViews.Count; i++) maskViews[i].style.display = DisplayStyle.None;
            PositionMaskViews();
        }

        Image ViewAt(int index)
        {
            while (maskViews.Count <= index)
            {
                var img = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                img.style.position = Position.Absolute;
                maskHost.Add(img);
                maskViews.Add(img);
            }
            return maskViews[index];
        }

        /// Place every live mask image over its own map-space extent. Rects only — never adds or removes a
        /// child, because this also runs from inside generateVisualContent, where mutating the tree is not
        /// allowed. KEEP ITS FILTER IDENTICAL to SyncMaskViews': the two walk the layer list in lockstep and
        /// index the same pool, so one of them skipping a layer the other kept would put a mask on the wrong
        /// rect. (MaskTexture never returns null, which is the one difference that would otherwise diverge.)
        void PositionMaskViews()
        {
            var data = w.Data;
            var vis = w.Visual;
            if (data?.layers == null || vis == null) return;
            var active = w.ActiveLayer;
            int used = 0;
            for (int i = 0; i < data.layers.Count && used < maskViews.Count; i++)
            {
                var L = data.layers[i];
                if (L == null || L.kind != LayerKind.Mask || !w.IsLayerVisible(L)) continue;
                if (!w.ShowAllLayers && !ReferenceEquals(L, active)) continue;
                var entry = data.EntryAt(L, w.Frame);
                if (entry == null || !entry.MaskUsable) continue;

                // Anchored at the FOOTPRINT's lower-left, not at map (0,0): a clump's map origin is its
                // top-left cell, so its picture runs into negative y and a zero-anchored mask would draw a
                // whole footprint too high.
                Vector2 cs = data.MaskCellToMapScale(entry);
                var lo = MapToLocal(data.footprintMin);
                var hi = MapToLocal(data.footprintMin + new Vector2(entry.maskW * cs.x, entry.maskH * cs.y));
                var img = maskViews[used++];
                img.style.left = Mathf.Min(lo.x, hi.x);
                img.style.top = Mathf.Min(lo.y, hi.y);
                img.style.width = Mathf.Abs(hi.x - lo.x);
                img.style.height = Mathf.Abs(hi.y - lo.y);
            }
        }

        /// The baked mask, cached against a content hash of (size, colour, every cell). Rebuilt only when one
        /// of those actually changed — so a repaint that changed nothing does no work, and a brush stroke
        /// costs one SetPixels32 instead of thousands of tessellated quads.
        Texture2D MaskTexture(int layerIndex, int frame, MetaMapLayer L, MetaEntry e)
        {
            long key = ((long)layerIndex << 40) ^ ((long)(uint)frame << 8);
            int hash;
            unchecked
            {
                hash = 17;
                hash = hash * 31 + e.maskW;
                hash = hash * 31 + e.maskH;
                hash = hash * 31 + L.color.GetHashCode();
                int n = e.maskW * e.maskH;
                for (int i = 0; i < n; i++) hash = hash * 31 + e.mask[i];
            }
            if (maskCache.TryGetValue(key, out var cached) && cached.hash == hash && cached.tex != null)
                return cached.tex;
            if (cached != null && cached.tex != null) Object.DestroyImmediate(cached.tex);

            // Bottom-left origin on both sides — a Color32[] row 0 IS the bottom row, which is the mask's own
            // convention, so the bake needs no flip. (The GUI's y-down flip stays in MapToLocal, alone.)
            var tex = new Texture2D(e.maskW, e.maskH, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[e.maskW * e.maskH];
            for (int i = 0; i < px.Length; i++) px[i] = MetaPalette.CellColor(L.color, e.mask[i]);
            tex.SetPixels32(px);
            tex.Apply();
            maskCache[key] = new MaskEntry { hash = hash, tex = tex };
            return tex;
        }

        void ClearMaskCache()
        {
            foreach (var e in maskCache.Values) if (e?.tex != null) Object.DestroyImmediate(e.tex);
            maskCache.Clear();
        }

        /// Lazy so a dock/tab detach that killed the texture heals on the next layout. Local copy rather than
        /// Cartographer's TilesetGridView.StyleAsChecker — MetaMapper must not reference Cartographer.
        void EnsureChecker()
        {
            if (checkerTex == null)
            {
                var dark = new Color32(52, 52, 52, 255);
                var light = new Color32(68, 68, 68, 255);
                checkerTex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat,
                };
                checkerTex.SetPixels32(new[] { dark, light, light, dark });
                checkerTex.Apply();
            }
            checker.style.backgroundImage = Background.FromTexture2D(checkerTex);
            checker.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
            checker.style.backgroundSize =
                new BackgroundSize(new Length(16f, LengthUnit.Pixel), new Length(16f, LengthUnit.Pixel));
        }

        public void Dispose()
        {
            if (checkerTex != null) { Object.DestroyImmediate(checkerTex); checkerTex = null; }
            ClearMaskCache();
        }

        // ── coordinates ─────────────────────────────────────────────────────────────

        float TexH => w.Visual != null ? w.Visual.TextureSize.y : 0f;

        /// Texture pixels (+y UP) → this element's local GUI pixels (+y down). The ONE place the flip lives.
        Vector2 TexToLocal(Vector2 texPx)
            => new Vector2(view.Origin.x + texPx.x * view.Scale,
                           view.Origin.y + (TexH - texPx.y) * view.Scale);

        Vector2 LocalToTex(Vector2 local)
            => new Vector2((local.x - view.Origin.x) / Mathf.Max(0.0001f, view.Scale),
                           TexH - (local.y - view.Origin.y) / Mathf.Max(0.0001f, view.Scale));

        internal Vector2 MapToLocal(Vector2 mapPos)
            => w.Visual != null ? TexToLocal(w.Visual.MapToTexture(mapPos)) : Vector2.zero;

        internal Vector2 LocalToMap(Vector2 local)
            => w.Visual != null ? w.Visual.TextureToMap(LocalToTex(local)) : Vector2.zero;

        /// Quarter-cell tidiness in GridCells (PropSpot's mid-cell freedom made everyday), one picture pixel in
        /// LocalUnits (a character's art is the only grid it has), whole pixels for a SHAPE in SpritePixels (a
        /// box edge between pixels is what a collider wants); Alt for free placement. Marks in SpritePixels
        /// never snap: a pixel IS the grid, and a mark's fraction there has always been the author's own.
        Vector2 Snap(Vector2 mapPos, bool alt)
        {
            if (alt || !w.SnapQuarter) return mapPos;
            float step;
            switch (w.Space)
            {
                case MapSpace.GridCells: step = 0.25f; break;
                case MapSpace.LocalUnits: step = PictureStep; break;
                default:
                    step = w.ActiveLayer != null && w.ActiveLayer.kind == LayerKind.Shapes ? 1f : 0f;
                    break;
            }
            if (step <= 0f) return mapPos;
            return new Vector2(Mathf.Round(mapPos.x / step) * step, Mathf.Round(mapPos.y / step) * step);
        }

        /// One picture pixel in map units — the natural grain of a LocalUnits subject.
        float PictureStep => w.Visual != null && w.Visual.pixelsPerUnit > 0f ? 1f / w.Visual.pixelsPerUnit : 0.0625f;

        /// The smallest sensible shape extent, and the size a click-without-drag gets, per space.
        float MinShapeExtent => w.Space == MapSpace.GridCells ? 0.25f
            : w.Space == MapSpace.LocalUnits ? PictureStep : 1f;

        float DefaultShapeExtent => w.Space == MapSpace.GridCells ? 1f
            : w.Space == MapSpace.LocalUnits ? 0.5f : 8f;

        // ── painting ────────────────────────────────────────────────────────────────

        const float MarkRadius = 4.5f;
        const float ArrowLen = 26f;      // screen px — a facing must stay grabbable at any zoom
        const float GrabPx = 11f;

        void PaintOverlay(MeshGenerationContext ctx)
        {
            var vis = w.Visual;
            var data = w.Data;
            if (vis == null || data == null) return;
            Layout();
            var p = ctx.painter2D;

            if (w.ShowGrid) PaintGrid(p, vis);
            if (w.Space == MapSpace.LocalUnits) PaintRootMarker(p);

            var layers = data.layers;
            if (layers == null) return;
            var active = w.ActiveLayer;

            for (int i = 0; i < layers.Count; i++)
            {
                var L = layers[i];
                if (L == null || !w.IsLayerVisible(L)) continue;
                bool isActive = ReferenceEquals(L, active);
                if (!w.ShowAllLayers && !isActive) continue;
                var entry = data.EntryAt(L, w.Frame);
                if (entry == null) continue;

                // Masks are NOT painted here — they are baked textures on maskHost (see SyncMaskViews).
                // Only marks, arrows, the grid and the brush cursor are geometry.
                if (L.kind == LayerKind.Shapes) PaintShapes(p, L, entry, isActive);
                else if (L.kind != LayerKind.Mask) PaintMarks(p, L, entry, isActive);
            }

            if (hasHover && active != null && active.kind == LayerKind.Mask) PaintBrushCursor(p);
        }

        void PaintGrid(Painter2D p, MetaSubjectVisual vis)
        {
            // One map unit per line in GridCells; in SpritePixels a per-pixel grid is only legible once a
            // pixel is several screen px, and drawing it otherwise turns the art into mush.
            float step = vis.pixelsPerUnit * view.Scale;
            if (step < 5f) return;
            var size = vis.TextureSize;
            int nx = Mathf.CeilToInt(size.x / Mathf.Max(0.001f, vis.pixelsPerUnit));
            int ny = Mathf.CeilToInt(size.y / Mathf.Max(0.001f, vis.pixelsPerUnit));
            if (nx > 512 || ny > 512) return;               // a 4k sheet's pixel grid is not a feature

            // DISCONNECTED SEGMENTS, one open path, one stroke: Painter2D's miter-join tessellation crashes
            // on manually-closed subpaths in bulk (the 2026-08-01 launch-crash loop). Never bulk-stroke
            // closed shapes here.
            p.strokeColor = new Color(1f, 1f, 1f, 0.13f);
            p.lineWidth = 1f;
            p.BeginPath();
            for (int c = 0; c <= nx; c++)
            {
                var a = TexToLocal(new Vector2(c * vis.pixelsPerUnit, 0f));
                var b = TexToLocal(new Vector2(c * vis.pixelsPerUnit, size.y));
                p.MoveTo(a); p.LineTo(b);
            }
            for (int r = 0; r <= ny; r++)
            {
                var a = TexToLocal(new Vector2(0f, r * vis.pixelsPerUnit));
                var b = TexToLocal(new Vector2(size.x, r * vis.pixelsPerUnit));
                p.MoveTo(a); p.LineTo(b);
            }
            p.Stroke();

            // The map ORIGIN, so a negative-offset subject (the clump case) is never guessed at. LocalUnits
            // draws its own, larger root marker whether or not the grid is on (PaintRootMarker).
            if (w.Space == MapSpace.LocalUnits) return;
            var o = MapToLocal(Vector2.zero);
            p.strokeColor = new Color(1f, 0.85f, 0.2f, 0.5f);
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.MoveTo(new Vector2(o.x - 7f, o.y)); p.LineTo(new Vector2(o.x + 7f, o.y));
            p.MoveTo(new Vector2(o.x, o.y - 7f)); p.LineTo(new Vector2(o.x, o.y + 7f));
            p.Stroke();
        }

        void PaintMarks(Painter2D p, MetaMapLayer L, MetaEntry entry, bool isActive)
        {
            if (entry.marks == null) return;
            float r = isActive ? MarkRadius : MarkRadius - 1.2f;
            for (int i = 0; i < entry.marks.Count; i++)
            {
                var m = entry.marks[i];
                if (m == null) continue;
                var at = MapToLocal(m.pos);

                if (L.kind == LayerKind.Directions)
                {
                    Vector2 d = m.dir.sqrMagnitude > 0f ? m.dir.normalized : Vector2.up;
                    var head = at + new Vector2(d.x, -d.y) * ArrowLen;   // -y: map is +y up, GUI is +y down
                    p.strokeColor = new Color(0f, 0f, 0f, 0.65f);
                    p.lineWidth = 3.5f;
                    p.BeginPath(); p.MoveTo(at); p.LineTo(head); p.Stroke();
                    p.strokeColor = L.color;
                    p.lineWidth = 1.6f;
                    p.BeginPath(); p.MoveTo(at); p.LineTo(head); p.Stroke();

                    Vector2 n = new Vector2(-(-d.y), d.x);               // perpendicular in GUI space
                    p.fillColor = L.color;
                    p.BeginPath();
                    p.MoveTo(head);
                    p.LineTo(head - new Vector2(d.x, -d.y) * 8f + n * 4f);
                    p.LineTo(head - new Vector2(d.x, -d.y) * 8f - n * 4f);
                    p.ClosePath();
                    p.Fill();
                }

                Disc(p, at, r + 1.6f, new Color(0f, 0f, 0f, 0.75f));
                Disc(p, at, r, L.color);
                if (isActive && i == SelectedMark)
                {
                    p.strokeColor = Color.white;
                    p.lineWidth = 1.4f;
                    Ring(p, at, r + 3.5f);
                }
            }
        }

        /// The ROOT of a LocalUnits subject — map (0,0), the point the game positions the character by. Always
        /// drawn, not only with the grid: on a character every shape is authored relative to it ("a foot circle
        /// centred just above the root"), so it is the one landmark the author can never be without.
        void PaintRootMarker(Painter2D p)
        {
            var o = MapToLocal(Vector2.zero);
            for (int pass = 0; pass < 2; pass++)
            {
                p.strokeColor = pass == 0 ? new Color(0f, 0f, 0f, 0.7f) : new Color(1f, 0.78f, 0.15f, 0.95f);
                p.lineWidth = pass == 0 ? 3.5f : 1.6f;
                p.BeginPath();
                p.MoveTo(new Vector2(o.x - 11f, o.y)); p.LineTo(new Vector2(o.x + 11f, o.y));
                p.MoveTo(new Vector2(o.x, o.y - 11f)); p.LineTo(new Vector2(o.x, o.y + 11f));
                p.Stroke();
                Ring(p, o, 5f);
            }
        }

        // ── shapes ──────────────────────────────────────────────────────────────────

        const float HandlePx = 4f;
        const int CircleHandle = 4;

        /// A shape's box in local GUI pixels (+y down), from its map-space centre and full extent.
        Rect LocalRect(MetaShape sh)
        {
            var a = MapToLocal(sh.center - sh.size * 0.5f);
            var b = MapToLocal(sh.center + sh.size * 0.5f);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        float LocalRadius(MetaShape sh)
            => Vector2.Distance(MapToLocal(sh.center), MapToLocal(sh.center + new Vector2(sh.Radius, 0f)));

        /// Handle positions in local pixels: a rect's four corners in map order (BL, BR, TR, TL — map is +y up,
        /// so the "top" corners have the SMALLER gui y), a circle's single radius handle on its right edge.
        Vector2 HandleLocal(MetaShape sh, int handle)
            => sh.kind == ShapeKind.Circle
                ? MapToLocal(sh.center + new Vector2(sh.Radius, 0f))
                : MapToLocal(CornerMap(sh, handle));

        static Vector2 CornerMap(MetaShape sh, int corner)
        {
            Vector2 h = sh.size * 0.5f;
            switch (corner)
            {
                case 0: return sh.center + new Vector2(-h.x, -h.y);
                case 1: return sh.center + new Vector2(h.x, -h.y);
                case 2: return sh.center + new Vector2(h.x, h.y);
                default: return sh.center + new Vector2(-h.x, h.y);
            }
        }

        void PaintShapes(Painter2D p, MetaMapLayer L, MetaEntry entry, bool isActive)
        {
            if (entry.shapes == null) return;
            var fill = L.color; fill.a = isActive ? 0.24f : 0.12f;
            var edge = L.color; edge.a = isActive ? 1f : 0.6f;
            var shadow = new Color(0f, 0f, 0f, 0.6f);
            for (int i = 0; i < entry.shapes.Count; i++)
            {
                var sh = entry.shapes[i];
                if (sh == null) continue;
                bool selected = isActive && i == SelectedShape;

                if (sh.kind == ShapeKind.Circle)
                {
                    var c = MapToLocal(sh.center);
                    float r = Mathf.Max(1f, LocalRadius(sh));
                    Disc(p, c, r, fill, 40);
                    p.strokeColor = shadow; p.lineWidth = 3f; Ring(p, c, r, 40);
                    p.strokeColor = selected ? Color.white : edge; p.lineWidth = selected ? 1.8f : 1.4f;
                    Ring(p, c, r, 40);
                    Disc(p, c, 2f, edge);                 // the centre — what a collider's offset will be
                }
                else
                {
                    var r = LocalRect(sh);
                    p.fillColor = fill;
                    p.BeginPath();
                    p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin));
                    p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax));
                    p.ClosePath();
                    p.Fill();
                    p.strokeColor = shadow; p.lineWidth = 3f; Box(p, r);
                    p.strokeColor = selected ? Color.white : edge; p.lineWidth = selected ? 1.8f : 1.4f;
                    Box(p, r);
                }

                if (!selected) continue;
                int handles = sh.kind == ShapeKind.Circle ? 1 : 4;
                for (int h = 0; h < handles; h++)
                {
                    var at = HandleLocal(sh, sh.kind == ShapeKind.Circle ? CircleHandle : h);
                    var hr = new Rect(at.x - HandlePx, at.y - HandlePx, HandlePx * 2f, HandlePx * 2f);
                    p.fillColor = Color.white;
                    p.BeginPath();
                    p.MoveTo(hr.min); p.LineTo(new Vector2(hr.xMax, hr.yMin));
                    p.LineTo(hr.max); p.LineTo(new Vector2(hr.xMin, hr.yMax));
                    p.ClosePath();
                    p.Fill();
                    p.strokeColor = new Color(0f, 0f, 0f, 0.85f); p.lineWidth = 1f; Box(p, hr);
                }
            }
        }

        /// An open rectangle outline, closed back by hand (see PaintGrid's note on closed subpaths).
        static void Box(Painter2D p, Rect r)
        {
            p.BeginPath();
            p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin));
            p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax)); p.LineTo(r.min);
            p.Stroke();
        }

        /// The selected shape's handle under the pointer, or -1. Only the SELECTED shape shows handles, so only
        /// it can be resized — a crowded layer never resizes the wrong neighbour.
        int HandleAt(MetaEntry entry, Vector2 local)
        {
            if (entry.shapes == null || SelectedShape < 0 || SelectedShape >= entry.shapes.Count) return -1;
            var sh = entry.shapes[SelectedShape];
            if (sh == null) return -1;
            if (sh.kind == ShapeKind.Circle)
                return Vector2.Distance(HandleLocal(sh, CircleHandle), local) <= GrabPx ? CircleHandle : -1;
            for (int i = 0; i < 4; i++)
                if (Vector2.Distance(HandleLocal(sh, i), local) <= GrabPx) return i;
            return -1;
        }

        /// The TOPMOST shape under the pointer (the last drawn), with a few pixels' grace on its edge so a thin
        /// footprint is still grabbable.
        int ShapeAt(MetaEntry entry, Vector2 local)
        {
            if (entry.shapes == null) return -1;
            const float grace = 3f;
            for (int i = entry.shapes.Count - 1; i >= 0; i--)
            {
                var sh = entry.shapes[i];
                if (sh == null) continue;
                if (sh.kind == ShapeKind.Circle)
                {
                    if (Vector2.Distance(MapToLocal(sh.center), local) <= LocalRadius(sh) + grace) return i;
                }
                else
                {
                    var r = LocalRect(sh);
                    r.xMin -= grace; r.yMin -= grace; r.xMax += grace; r.yMax += grace;
                    if (r.Contains(local)) return i;
                }
            }
            return -1;
        }

        /// A shape spanning two map points: a rect corner-to-corner, a circle centred on the first.
        static void SpanShape(MetaShape sh, Vector2 a, Vector2 b)
        {
            if (sh.kind == ShapeKind.Circle)
            {
                float d = (b - a).magnitude * 2f;
                sh.center = a;
                sh.size = new Vector2(d, d);
                return;
            }
            sh.center = (a + b) * 0.5f;
            sh.size = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        }

        void SelectShape(int index)
        {
            if (SelectedShape == index) return;
            SelectedShape = index;
            w.OnShapeSelectionChanged();
        }

        void PaintBrushCursor(Painter2D p)
        {
            var vis = w.Visual;
            if (vis == null) return;
            int size = Mathf.Max(1, w.BrushSize);
            var fm = w.Data != null ? w.Data.footprintMin : Vector2.zero;
            var cell = MaskCellAt(hoverMap, fm);
            int half = (size - 1) / 2;
            var lo = MapToLocal(fm + new Vector2(cell.x - half, cell.y - half));
            var hi = MapToLocal(fm + new Vector2(cell.x - half + size, cell.y - half + size));
            var r = Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y),
                                    Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y));
            p.strokeColor = new Color(1f, 1f, 1f, 0.8f);
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin));
            p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax)); p.LineTo(r.min);
            p.Stroke();
        }

        /// A filled polygon disc. Deliberately NOT Painter2D.Arc: a 12-gon is indistinguishable at these
        /// radii and keeps every path in this file to plain MoveTo/LineTo.
        static void Disc(Painter2D p, Vector2 c, float radius, Color color, int segments = 12)
        {
            p.fillColor = color;
            p.BeginPath();
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var v = new Vector2(c.x + Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.Fill();
        }

        /// An OPEN 12-gon outline — closed back to its first point by hand rather than via ClosePath, so the
        /// miter-join tessellation never sees a closed subpath (see PaintGrid's note).
        static void Ring(Painter2D p, Vector2 c, float radius, int segments = 12)
        {
            p.BeginPath();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var v = new Vector2(c.x + Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.Stroke();
        }

        // ── gestures ────────────────────────────────────────────────────────────────

        void OnDown(PointerDownEvent e)
        {
            if (e.button == 2) return;                       // middle belongs to ZuiPanZoom
            Focus();
            var layer = w.ActiveLayer;
            var entry = w.ActiveEntry(createIfMissing: true);
            if (w.Visual == null || layer == null || entry == null) return;

            Vector2 mapPos = Snap(LocalToMap(e.localPosition), e.altKey);
            lastMapPos = mapPos;

            switch (layer.kind)
            {
                case LayerKind.Points:
                {
                    if (e.button != 0) return;
                    int hit = NearestMark(entry, e.localPosition, GrabPx, out _);
                    if (hit >= 0)
                    {
                        SelectedMark = hit;
                        w.Session.Begin("Move meta mark");
                        gesture = Gesture.MoveMark; gestureMark = hit;
                    }
                    else
                    {
                        w.Session.Begin("Place meta mark");
                        entry.marks.Add(new MetaMark { pos = mapPos });
                        SelectedMark = entry.marks.Count - 1;
                        gesture = Gesture.MoveMark; gestureMark = SelectedMark;
                    }
                    break;
                }
                case LayerKind.Directions:
                {
                    if (e.button != 0) return;
                    int head = NearestHead(entry, e.localPosition, GrabPx);
                    if (head >= 0)
                    {
                        SelectedMark = head;
                        w.Session.Begin("Aim meta direction");
                        gesture = Gesture.AimMark; gestureMark = head;
                        break;
                    }
                    int baseHit = NearestMark(entry, e.localPosition, GrabPx, out _);
                    if (baseHit >= 0)
                    {
                        SelectedMark = baseHit;
                        w.Session.Begin("Move meta direction");
                        gesture = Gesture.MoveMark; gestureMark = baseHit;
                        break;
                    }
                    // Press places, drag-before-release aims — the one-gesture idiom.
                    w.Session.Begin("Place meta direction");
                    entry.marks.Add(new MetaMark { pos = mapPos, dir = Vector2.zero });
                    SelectedMark = entry.marks.Count - 1;
                    gesture = Gesture.AimMark; gestureMark = SelectedMark;
                    break;
                }
                case LayerKind.Shapes:
                {
                    if (e.button != 0) return;
                    if (entry.shapes == null) entry.shapes = new List<MetaShape>();
                    int handle = HandleAt(entry, e.localPosition);
                    if (handle >= 0)
                    {
                        var sh = entry.shapes[SelectedShape];
                        w.Session.Begin("Resize meta shape");
                        gesture = Gesture.ResizeShape; gestureMark = SelectedShape;
                        // The corner OPPOSITE the grabbed one stays put; a circle resizes about its centre.
                        gestureAnchor = sh.kind == ShapeKind.Circle ? sh.center : CornerMap(sh, (handle + 2) % 4);
                        break;
                    }
                    int hit = ShapeAt(entry, e.localPosition);
                    if (hit >= 0)
                    {
                        SelectShape(hit);
                        w.Session.Begin("Move meta shape");
                        gesture = Gesture.MoveShape; gestureMark = hit;
                        gestureAnchor = mapPos;
                        gestureStartCenter = entry.shapes[hit].center;
                        break;
                    }
                    // Empty space: the press starts a new shape and the drag gives it its size.
                    w.Session.Begin("Draw meta shape");
                    entry.shapes.Add(new MetaShape { kind = w.NewShapeKind, center = mapPos, size = Vector2.zero });
                    gesture = Gesture.DrawShape; gestureMark = entry.shapes.Count - 1;
                    gestureAnchor = mapPos;
                    SelectShape(gestureMark);
                    break;
                }
                case LayerKind.Mask:
                {
                    bool erase = e.button == 1 || e.altKey;
                    w.Session.Begin(erase ? "Erase meta mask" : "Paint meta mask");
                    gesture = erase ? Gesture.Erase : Gesture.Paint;
                    PaintAt(layer, entry, mapPos, erase);
                    break;
                }
            }

            this.CapturePointer(e.pointerId);
            SyncMaskViews();          // hash-checked: a no-op unless this gesture actually changed a mask
            overlay.MarkDirtyRepaint();
            w.UpdateStatus();
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (w.Visual != null)
            {
                hasHover = true;
                hoverMap = LocalToMap(e.localPosition);
                if (gesture == Gesture.None) { w.UpdateStatus(); overlay.MarkDirtyRepaint(); }
            }
            if (gesture == Gesture.None) return;

            var layer = w.ActiveLayer;
            var entry = w.ActiveEntry(createIfMissing: false);
            if (layer == null || entry == null) return;
            Vector2 mapPos = Snap(LocalToMap(e.localPosition), e.altKey);

            switch (gesture)
            {
                case Gesture.MoveMark:
                    if (gestureMark >= 0 && gestureMark < entry.marks.Count && entry.marks[gestureMark] != null)
                        entry.marks[gestureMark].pos = mapPos;
                    break;
                case Gesture.AimMark:
                    if (gestureMark >= 0 && gestureMark < entry.marks.Count && entry.marks[gestureMark] != null)
                    {
                        var m = entry.marks[gestureMark];
                        var d = mapPos - m.pos;
                        if (d.sqrMagnitude > 1e-6f) m.dir = d.normalized;
                    }
                    break;
                case Gesture.DrawShape:
                case Gesture.ResizeShape:
                    if (entry.shapes != null && gestureMark >= 0 && gestureMark < entry.shapes.Count
                        && entry.shapes[gestureMark] != null)
                        SpanShape(entry.shapes[gestureMark], gestureAnchor, mapPos);
                    break;
                case Gesture.MoveShape:
                    if (entry.shapes != null && gestureMark >= 0 && gestureMark < entry.shapes.Count
                        && entry.shapes[gestureMark] != null)
                        entry.shapes[gestureMark].center = gestureStartCenter + (mapPos - gestureAnchor);
                    break;
                case Gesture.Paint:
                case Gesture.Erase:
                    // Interpolate along the segment: a fast drag otherwise leaves gaps between samples.
                    PaintSegment(layer, entry, lastMapPos, mapPos, gesture == Gesture.Erase);
                    break;
            }
            lastMapPos = mapPos;
            if (gesture == Gesture.Paint || gesture == Gesture.Erase) SyncMaskViews();
            overlay.MarkDirtyRepaint();
            w.UpdateStatus();
        }

        void OnUp(PointerUpEvent e)
        {
            if (gesture == Gesture.None) return;

            // A Direction with no facing is a lie — an aim gesture that never moved gets the +y default.
            if (gesture == Gesture.AimMark)
            {
                var entry = w.ActiveEntry(createIfMissing: false);
                if (entry != null && gestureMark >= 0 && gestureMark < entry.marks.Count
                    && entry.marks[gestureMark] != null && entry.marks[gestureMark].dir.sqrMagnitude <= 1e-6f)
                    entry.marks[gestureMark].dir = Vector2.up;
            }

            // A shape with no size is invisible and ungrabbable: a click-without-drag gets a sensible default,
            // and a drag that collapsed one axis gets the smallest extent the space can show.
            if (gesture == Gesture.DrawShape || gesture == Gesture.ResizeShape)
            {
                var entry = w.ActiveEntry(createIfMissing: false);
                if (entry?.shapes != null && gestureMark >= 0 && gestureMark < entry.shapes.Count
                    && entry.shapes[gestureMark] != null)
                {
                    var sh = entry.shapes[gestureMark];
                    float min = MinShapeExtent;
                    if (gesture == Gesture.DrawShape && sh.size.x < min && sh.size.y < min)
                        sh.size = Vector2.one * DefaultShapeExtent;
                    sh.size = new Vector2(Mathf.Max(min, sh.size.x), Mathf.Max(min, sh.size.y));
                    sh.Normalize();
                }
            }

            bool shapeGesture = gesture == Gesture.DrawShape || gesture == Gesture.MoveShape
                                || gesture == Gesture.ResizeShape;
            gesture = Gesture.None;
            gestureMark = -1;
            this.ReleasePointer(e.pointerId);
            w.Session?.End();
            w.AfterEdit();
            if (shapeGesture) w.OnShapeSelectionChanged();   // the readouts now describe the shape as dropped
            e.StopPropagation();
        }

        void OnKey(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Delete && e.keyCode != KeyCode.Backspace) return;
            var layer = w.ActiveLayer;
            var entry = w.ActiveEntry(createIfMissing: false);
            if (layer == null || entry == null || layer.kind == LayerKind.Mask) return;
            if (layer.kind == LayerKind.Shapes)
            {
                if (entry.shapes == null || SelectedShape < 0 || SelectedShape >= entry.shapes.Count) return;
                int doomed = SelectedShape;
                w.Session.Edit("Delete meta shape", () => entry.shapes.RemoveAt(doomed));
                SelectedShape = -1;
                w.AfterEdit();
                w.OnShapeSelectionChanged();
                e.StopPropagation();
                return;
            }
            if (SelectedMark < 0 || SelectedMark >= entry.marks.Count) return;

            w.Session.Edit("Delete meta mark", () => entry.marks.RemoveAt(SelectedMark));
            SelectedMark = -1;
            w.AfterEdit();
            e.StopPropagation();
        }

        int NearestMark(MetaEntry entry, Vector2 local, float maxPx, out float dist)
        {
            dist = float.MaxValue;
            int best = -1;
            if (entry.marks == null) return -1;
            for (int i = 0; i < entry.marks.Count; i++)
            {
                var m = entry.marks[i];
                if (m == null) continue;
                float d = Vector2.Distance(MapToLocal(m.pos), local);
                if (d < dist) { dist = d; best = i; }
            }
            return dist <= maxPx ? best : -1;
        }

        int NearestHead(MetaEntry entry, Vector2 local, float maxPx)
        {
            int best = -1; float dist = float.MaxValue;
            if (entry.marks == null) return -1;
            for (int i = 0; i < entry.marks.Count; i++)
            {
                var m = entry.marks[i];
                if (m == null) continue;
                Vector2 d = m.dir.sqrMagnitude > 0f ? m.dir.normalized : Vector2.up;
                var head = MapToLocal(m.pos) + new Vector2(d.x, -d.y) * ArrowLen;
                float dd = Vector2.Distance(head, local);
                if (dd < dist) { dist = dd; best = i; }
            }
            return dist <= maxPx ? best : -1;
        }

        // ── mask painting ───────────────────────────────────────────────────────────

        void PaintSegment(MetaMapLayer layer, MetaEntry entry, Vector2 from, Vector2 to, bool erase)
        {
            float steps = Mathf.Max(1f, (to - from).magnitude);
            int n = Mathf.Clamp(Mathf.CeilToInt(steps), 1, 256);
            for (int i = 1; i <= n; i++)
                PaintAt(layer, entry, Vector2.Lerp(from, to, i / (float)n), erase);
        }

        /// Which MASK cell a map position falls in. Mask cell (0,0) is the FOOTPRINT's lower-left corner, not
        /// map (0,0) — see MetaMapData.footprintMin — and the mask is kept 1:1 with map units by MaskSize.
        static Vector2Int MaskCellAt(Vector2 mapPos, Vector2 footprintMin)
            => new Vector2Int(Mathf.FloorToInt(mapPos.x - footprintMin.x),
                              Mathf.FloorToInt(mapPos.y - footprintMin.y));

        void PaintAt(MetaMapLayer layer, MetaEntry entry, Vector2 mapPos, bool erase)
        {
            var size = w.MaskSize;
            if (size.x <= 0 || size.y <= 0) return;
            if (!entry.MaskUsable || entry.maskW != size.x || entry.maskH != size.y)
                entry.EnsureMaskSize(size.x, size.y);

            var fm = w.Data != null ? w.Data.footprintMin : Vector2.zero;
            var c = MaskCellAt(mapPos, fm);

            // pointHint is an AUTHORING hint and nothing more (MetaLayerMode.Point, verbatim): the paint tool
            // behaves single-cell, clearing what was there before instead of accumulating a blob.
            if (layer.pointHint && !erase)
            {
                System.Array.Clear(entry.mask, 0, entry.mask.Length);
                entry.MaskSet(c.x, c.y, w.BrushValue);
                return;
            }

            int cx = c.x, cy = c.y;
            int brush = Mathf.Max(1, w.BrushSize);
            int half = (brush - 1) / 2;
            for (int y = cy - half; y < cy - half + brush; y++)
                for (int x = cx - half; x < cx - half + brush; x++)
                    entry.MaskSet(x, y, erase ? 0 : w.BrushValue);
        }

        /// The hovered position, for the status line. NaN-free: false when the pointer is elsewhere.
        internal bool TryGetHover(out Vector2 mapPos)
        {
            mapPos = hoverMap;
            return hasHover && w.Visual != null;
        }

        internal void ClearSelection() { SelectedMark = -1; SelectedShape = -1; }
    }
}
