// ShaperPreviewStage — the window's live preview surface.
//
// It owns NO pixel path of its own. Every pixel comes from ShaperDocumentRenderer, which is the engine's
// single document renderer (Runtime/Shaper/ShaperDocumentRenderer.cs, T-0153). That is the whole reason the
// renderer was promoted out of the baker into Runtime: preview and bake CANNOT drift, because there is one
// implementation rather than two that agree today and diverge next month.
//
// Row order is deliberately NOT flipped here. ShaperBaker.BlitFrame copies the renderer's Color32[] rows
// straight into a sheet with no flip (ShaperBaker.cs:409-417) and hands that to SetPixels32, whose index 0 is
// bottom-left. Doing the same thing here is what makes "what you previewed is what you baked" literally true
// rather than approximately true; if the orientation is ever wrong it is wrong in BOTH, which is a renderer
// bug to fix once, not a preview bug to paper over with a flip that would then hide it.
using System;
using Laubrary.BackSplash.Editor;
using Laubrary.PyreShaper;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    internal sealed class ShaperPreviewStage : VisualElement
    {
        readonly Func<ShaperDocument> _doc;
        readonly Func<int> _frame;
        readonly BackSplashElement _backdrop;
        readonly VisualElement _image;
        readonly VisualElement _frameBorder;
        Texture2D _tex;

        // ── frame cache + background pre-baker (T-0165) ─────────────────────────────────────────────────────
        // See ShaperPreviewFrameCache.cs's own header for why this is a new pixel-level cache rather than the
        // old T-0115 Runtime geometry cache (since deleted, T-0253) the task named -- that cached a NODE's
        // distance field, not the finished picture RenderFrame actually costs ~30ms for.
        readonly ShaperPreviewFrameCache _frameCache = new ShaperPreviewFrameCache();
        readonly ShaperPreviewFramePrebaker _prebaker;

        /// <summary>Fires whenever the background pre-baker makes progress OR finishes, so the window can
        /// repaint its cache tick strip / "N/M cached" readout without touching the (expensive) preview
        /// image itself.</summary>
        public event Action CacheProgressed;

        public bool IsFrameCached(int frameIndex) => _frameCache.IsFrameCached(frameIndex);
        public int CountCachedFrames() => _frameCache.CountCachedFrames();
        public int CachedFrameCount => _frameCache.frameCount;

        /// Cosmetic view state, owned by the window and pushed in — see ShaperWindow.Preview.cs for why these
        /// live on the window rather than the document.
        public bool ShowFrameBorder;
        public float Zoom = 1f;

        // ── on-canvas position handle (T-0168) ──────────────────────────────────────────────────────────────
        // The node being edited is the window's business, so the stage asks for it rather than tracking a
        // selection of its own; the two callbacks are the window's Undo/dirty contract, handed in for the same
        // reason (this element must not know what an asset is).
        const float HandleSize = 13f;

        /// The node whose placement the handle drags, and the layer root it hangs under — the second is what
        /// lets the handle account for every ancestor transform between the two.
        public Func<ShaperNode> SelectedNode;
        public Func<ShaperNode> SelectedLayerRoot;
        /// Called ONCE per completed drag, immediately before the final value is written — this is what makes
        /// a whole drag a single Ctrl+Z rather than one entry per mouse-move.
        public Action RecordUndo;
        /// Called after every write, live during the drag: dirty the asset and repaint.
        public Action Changed;
        /// Called once, after the drag has settled — for anything too heavy to run per mouse-move.
        public Action DragCommitted;

        // ── preview overlays (T-0190) ───────────────────────────────────────────────────────────────────────
        // The stage knows nothing about what an overlay IS — which features can mark something, and which of
        // them are switched on, is the window's business (ShaperPreviewOverlays). All the stage owes them is
        // the buffer, and the guarantee that what they write NEVER reaches the frame cache: the marks are
        // painted into a scratch copy, so a cached frame stays exactly what the renderer produced and toggling
        // an overlay off cannot leave its marks behind on a frame that is never recomputed.
        public Func<bool> WantsOverlays;
        public Action<Color32[], int, int, int> DrawOverlays;
        Color32[] _overlayScratch;

        readonly VisualElement _handle;
        bool _dragging;
        // The handle and the pivot cross are only shown while the pointer is over the stage (or mid-drag), so
        // the picture is unobstructed the rest of the time — owner request, 2026-09-03. Visibility, not
        // display: the marks keep their layout slot and just stop painting (stable-workspace rule).
        bool _hover;
        Vector2 _dragStartTranslate;
        Vector2 _dragStartPointer;

        // ── the pivot cross (T-0220) ────────────────────────────────────────────────────────────────────────
        // A second, distinct mark from the yellow Translate handle above: the handle sits where the node's
        // local (0,0) landed, which DOES move under rotation/scale around a pivot elsewhere. The cross sits
        // where translate+origin landed instead — the one point ShaperTransformBlock.ToMatrix keeps fixed
        // regardless of rotation/scale/skew (T-0220 probe: verified invariant at identity R/S/K, and moves
        // only when R/S/K is non-identity, exactly the "what Rotation turns around" reading). It exists so the
        // Origin dial's tooltip claim — "moving it does not move the shape" — is something the owner can watch
        // happen rather than take on faith.
        const float CrossSize = 15f;
        readonly VisualElement _originCross;
        readonly VisualElement _originCrossH;
        readonly VisualElement _originCrossV;

        /// Whether the Transform card is the thing currently being looked at — set by the window from its own
        /// ZuiSection.IsOpen, never decided in here (the stage does not know what a "card" is).
        public Func<bool> ShowOrigin;

        public ShaperPreviewStage(Func<ShaperDocument> doc, Func<int> frame)
        {
            _doc = doc;
            _frame = frame;
            _prebaker = new ShaperPreviewFramePrebaker(_frameCache, _doc);
            _prebaker.Progressed += _ => CacheProgressed?.Invoke();
            _prebaker.Completed += _ => CacheProgressed?.Invoke();

            AddToClassList("zui-stage");
            style.overflow = Overflow.Hidden;
            style.minHeight = 180f;
            tooltip = "Live render of the whole document at the current frame — every enabled layer, "
                    + "composited. This is the same renderer the bake uses, so what you see here is what "
                    + "gets baked. The backdrop and frame border are preview-only and are never baked.";

            // ── Pyre's blit order — backdrop fill, then image, then the frame on top ─────────────────────
            // Pyre draws all three in ONE IMGUI pass (PyreWindow.Preview.cs DrawBackdrop). That cannot be
            // copied literally here because this stage is retained-mode: a VisualElement paints its own
            // background BEFORE its children, so a backdrop painted onto THIS element would sit behind the
            // frame only by accident, and any later restyle of the stage would silently reorder them.
            // Instead the order is made structural — sibling children, backdrop first, frame second, border
            // last — which is the retained-mode spelling of the same blit order and cannot be reordered by
            // styling.
            //
            // This is UI TOOLKIT throughout. It briefly was not: the backdrop was an IMGUIContainer running
            // BackSplashPainter, the only IMGUI left in an otherwise pure-UITK tool, imported purely to reuse
            // a shared painter whose API is Draw(Rect,...). The right fix was neither to keep the island nor
            // to re-implement the backdrop privately here (which would have been the FOURTH copy of those
            // twenty lines, and BackSplashPainter exists because the third one silently stopped reading
            // imageZoom/imagePos) — it was to add the UITK spelling to the SHARED module. That is
            // BackSplashElement, beside the painter, reading the same BackSplashSettings.
            //
            // The frame keeps its transparency, which is what makes the backdrop visible at all: the layer
            // walk clears its accumulator with Array.Clear and Encode writes STRAIGHT alpha, so every sample
            // outside the shape is genuinely alpha 0 rather than an opaque background colour.
            _backdrop = new BackSplashElement(new Color(0.08f, 0.08f, 0.10f));
            _backdrop.StretchToParentSize();
            Add(_backdrop);

            _image = new VisualElement { pickingMode = PickingMode.Ignore };
            _image.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            _image.StretchToParentSize();
            Add(_image);

            // The canvas-edge outline, as a styled border rather than four EditorGUI.DrawRect calls. Absolute
            // and pointer-transparent, positioned onto the fitted canvas rect whenever the stage resizes —
            // the border has to follow the PICTURE, not the panel, or it stops meaning "this is the canvas
            // edge". Cosmetic: no renderer or baker path can see it.
            _frameBorder = new VisualElement { pickingMode = PickingMode.Ignore };
            _frameBorder.style.position = Position.Absolute;
            _frameBorder.style.display = DisplayStyle.None;
            var edge = new Color(1f, 1f, 1f, 0.28f);
            _frameBorder.style.borderTopWidth = _frameBorder.style.borderBottomWidth = 1f;
            _frameBorder.style.borderLeftWidth = _frameBorder.style.borderRightWidth = 1f;
            _frameBorder.style.borderTopColor = _frameBorder.style.borderBottomColor = edge;
            _frameBorder.style.borderLeftColor = _frameBorder.style.borderRightColor = edge;
            Add(_frameBorder);

            // The position handle sits last so it paints over the picture, and it is the only pickable child —
            // everything below it is Ignore, so a click that misses the handle still falls through to the stage.
            _handle = new VisualElement { pickingMode = PickingMode.Position };
            _handle.style.position = Position.Absolute;
            _handle.style.display = DisplayStyle.None;
            _handle.style.width = HandleSize;
            _handle.style.height = HandleSize;
            _handle.style.borderTopWidth = _handle.style.borderBottomWidth = 2f;
            _handle.style.borderLeftWidth = _handle.style.borderRightWidth = 2f;
            _handle.style.borderTopLeftRadius = _handle.style.borderTopRightRadius = HandleSize * 0.5f;
            _handle.style.borderBottomLeftRadius = _handle.style.borderBottomRightRadius = HandleSize * 0.5f;
            Add(_handle);
            _handle.RegisterCallback<PointerDownEvent>(OnHandleDown);
            _handle.RegisterCallback<PointerMoveEvent>(OnHandleMove);
            _handle.RegisterCallback<PointerUpEvent>(OnHandleUp);

            // The cross itself: two thin bars in a plain container, ignore-picking throughout — it marks a
            // point, it is not draggable "unless trivially the same mechanism" (the task's own words), and
            // dragging the pivot is a materially different gesture (it would have to rewrite BOTH origin and
            // translate to hold the picture still) that nobody asked for here.
            _originCross = new VisualElement { pickingMode = PickingMode.Ignore };
            _originCross.style.position = Position.Absolute;
            _originCross.style.display = DisplayStyle.None;
            _originCross.style.width = CrossSize;
            _originCross.style.height = CrossSize;
            var crossColor = new Color(0.35f, 0.85f, 1f, 0.95f); // cyan — distinct from the yellow translate ring
            _originCrossH = new VisualElement { pickingMode = PickingMode.Ignore };
            _originCrossH.style.position = Position.Absolute;
            _originCrossH.style.left = 0f; _originCrossH.style.right = 0f;
            _originCrossH.style.top = CrossSize * 0.5f - 1f;
            _originCrossH.style.height = 2f;
            _originCrossH.style.backgroundColor = crossColor;
            _originCrossV = new VisualElement { pickingMode = PickingMode.Ignore };
            _originCrossV.style.position = Position.Absolute;
            _originCrossV.style.top = 0f; _originCrossV.style.bottom = 0f;
            _originCrossV.style.left = CrossSize * 0.5f - 1f;
            _originCrossV.style.width = 2f;
            _originCrossV.style.backgroundColor = crossColor;
            _originCross.Add(_originCrossH);
            _originCross.Add(_originCrossV);
            Add(_originCross);

            RegisterCallback<GeometryChangedEvent>(_ => { LayoutFrameBorder(); LayoutHandle(); LayoutOriginCross(); });
            ApplyHoverVisibility(); // start hidden: nothing paints until the pointer is over the stage
            RegisterCallback<PointerEnterEvent>(_ => { _hover = true; ApplyHoverVisibility(); });
            RegisterCallback<PointerLeaveEvent>(_ => { _hover = false; ApplyHoverVisibility(); });

            // A Texture2D is an unmanaged Unity object; a window rebuild drops this element and would leak it.
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            Refresh();
        }

        /// Places the canvas-edge outline onto the fitted canvas rect. Called on every geometry change and
        /// whenever the toggle or the document changes, because the fitted rect depends on the stage size and
        /// on the document's own aspect.
        void LayoutFrameBorder()
        {
            var doc = _doc?.Invoke();
            if (!ShowFrameBorder || doc == null)
            {
                _frameBorder.style.display = DisplayStyle.None;
                return;
            }

            float vw = resolvedStyle.width, vh = resolvedStyle.height;
            if (float.IsNaN(vw) || float.IsNaN(vh) || vw <= 0f || vh <= 0f) return;

            var r = FittedCanvasRect(new Rect(0f, 0f, vw, vh), doc);
            _frameBorder.style.display = DisplayStyle.Flex;
            _frameBorder.style.left = r.x;
            _frameBorder.style.top = r.y;
            _frameBorder.style.width = r.width;
            _frameBorder.style.height = r.height;
        }

        // ── the position handle ─────────────────────────────────────────────────────────────────────────────

        /// Where the picture actually is, INCLUDING zoom. Zoom is applied as a negative percentage inset on the
        /// image layer, so the image's own box is the stage grown by (zoom−1)/2 on every side; the canvas is
        /// then fitted inside that. Anything drawn over the picture has to use the same box or it drifts off
        /// the thing it is marking as soon as the user zooms.
        Rect ZoomedCanvasRect(ShaperDocument doc)
        {
            float vw = resolvedStyle.width, vh = resolvedStyle.height;
            if (float.IsNaN(vw) || float.IsNaN(vh) || vw <= 0f || vh <= 0f) return Rect.zero;
            float z = Mathf.Max(1f, Zoom);
            var view = new Rect(-(z - 1f) * 0.5f * vw, -(z - 1f) * 0.5f * vh, vw * z, vh * z);
            return FittedCanvasRect(view, doc);
        }

        /// A point in canvas units to a point in this element's own pixels. Canvas units are centred on the
        /// canvas and row 0 is the BOTTOM one (the renderer's own convention, see this file's header), so the
        /// Y axis flips on the way to screen space.
        static Vector2 CanvasToLocal(ShaperDocument doc, in Rect r, Vector2 canvasPoint)
        {
            int w = Mathf.Max(1, doc.canvasWidth), h = Mathf.Max(1, doc.canvasHeight);
            float ps = Mathf.Max(1e-4f, doc.pixelSize);
            float halfW = 0.5f * (w - 1) * ps, halfH = 0.5f * (h - 1) * ps;
            float u = ((canvasPoint.x + halfW) / ps + 0.5f) / w;
            float v = ((canvasPoint.y + halfH) / ps + 0.5f) / h;
            return new Vector2(r.x + u * r.width, r.yMax - v * r.height);
        }

        /// The matrix every ancestor between the layer root and the selected node contributes — the frame the
        /// node's own translate is expressed in. Identity when the node IS the layer root.
        static ShaperMatrix ParentForwardOf(ShaperNode root, ShaperNode target, float phase01, uint seed)
        {
            var m = ShaperMatrix.Identity;
            if (root == null || target == null || ReferenceEquals(root, target)) return m;
            Accumulate(root, m, out var found, out var result);
            return found ? result : ShaperMatrix.Identity;

            void Accumulate(ShaperNode node, in ShaperMatrix parent, out bool hit, out ShaperMatrix outMatrix)
            {
                hit = false; outMatrix = ShaperMatrix.Identity;
                if (node?.children == null) return;
                ShaperMatrix here = ShaperMatrix.Mul(parent, (node.transform ?? new ShaperTransformBlock()).ToMatrix(phase01, seed));
                for (int i = 0; i < node.children.Count; i++)
                {
                    var c = node.children[i];
                    if (c == null) continue;
                    if (ReferenceEquals(c, target)) { hit = true; outMatrix = here; return; }
                    Accumulate(c, here, out hit, out outMatrix);
                    if (hit) return;
                }
            }
        }

        /// True only when BOTH axes are plain static numbers. Dragging a Curve would have to overwrite the
        /// value the curve is scaled from, which reads as "the drag did nothing" — so an animated node shows
        /// the marker and refuses the drag rather than silently mis-editing it.
        static bool IsDraggable(ShaperNode node)
        {
            var t = node?.transform;
            if (t == null) return false;
            t.EnsureDials();
            return t.translateX.mode == ZUIValue.Mode.Static && t.translateY.mode == ZUIValue.Mode.Static;
        }

        void LayoutHandle()
        {
            var doc = _doc?.Invoke();
            var node = SelectedNode?.Invoke();
            var root = SelectedLayerRoot?.Invoke();
            if (doc == null || node == null || node.transform == null)
            {
                _handle.style.display = DisplayStyle.None;
                return;
            }

            var r = ZoomedCanvasRect(doc);
            if (r.width <= 0f || r.height <= 0f) { _handle.style.display = DisplayStyle.None; return; }

            float phase = doc.PhaseOfFrame(Mathf.Max(0, _frame?.Invoke() ?? 0));
            var parent = ParentForwardOf(root, node, phase, doc.seed);
            var forward = ShaperMatrix.Mul(parent, node.transform.ToMatrix(phase, doc.seed));
            // The node's own local origin, carried up to canvas space: applying the map to (0,0) is just its
            // translation column, so this is where the node's content is centred right now.
            var local = CanvasToLocal(doc, r, new Vector2(forward.m02, forward.m12));

            bool draggable = IsDraggable(node);
            var ring = draggable ? new Color(1f, 0.78f, 0.2f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
            _handle.style.borderTopColor = _handle.style.borderBottomColor = ring;
            _handle.style.borderLeftColor = _handle.style.borderRightColor = ring;
            _handle.style.backgroundColor = new Color(0f, 0f, 0f, draggable ? 0.25f : 0.12f);
            _handle.pickingMode = draggable ? PickingMode.Position : PickingMode.Ignore;
            _handle.tooltip = draggable
                ? "Drag to place \"" + node.name + "\" on the canvas. One drag is one undo step."
                : "\"" + node.name + "\" has an animated position, so its place on the canvas comes from its "
                  + "curve — set Translate back to a static value to drag it here.";

            _handle.style.display = DisplayStyle.Flex;
            _handle.style.left = local.x - HandleSize * 0.5f;
            _handle.style.top = local.y - HandleSize * 0.5f;
            ApplyHoverVisibility();
        }

        /// The marks paint only while the pointer is over the stage or a drag is in flight; a drag that
        /// wanders outside the stage keeps its handle visible until the pointer is released.
        void ApplyHoverVisibility()
        {
            var v = (_hover || _dragging) ? Visibility.Visible : Visibility.Hidden;
            _handle.style.visibility = v;
            _originCross.style.visibility = v;
        }

        /// Places the pivot cross at translate+origin, carried up through every ancestor transform the same
        /// way the handle is (ParentForwardOf) — a node inside a rotated/scaled bag needs its cross to follow
        /// that bag, or it stops marking the point it claims to mark.
        void LayoutOriginCross()
        {
            var doc = _doc?.Invoke();
            var node = SelectedNode?.Invoke();
            var root = SelectedLayerRoot?.Invoke();
            bool wants = ShowOrigin != null && ShowOrigin();
            if (!wants || doc == null || node?.transform == null)
            {
                _originCross.style.display = DisplayStyle.None;
                return;
            }

            var r = ZoomedCanvasRect(doc);
            if (r.width <= 0f || r.height <= 0f) { _originCross.style.display = DisplayStyle.None; return; }

            float phase = doc.PhaseOfFrame(Mathf.Max(0, _frame?.Invoke() ?? 0));
            var t = node.transform;
            var parent = ParentForwardOf(root, node, phase, doc.seed);
            var localToParent = t.ToMatrix(phase, doc.seed);
            // M(origin) — the one point ShaperTransformBlock.ToMatrix's T·origin·R·S·K·origin⁻¹ composition
            // holds fixed no matter what R/S/K sample to (T-0220 probe). NOT forward.m02/m12 (LayoutHandle's
            // point) — that is M(0,0), the local content origin, which is exactly the point that DOES move
            // under rotation/scale around this pivot; using it here would draw the cross on the wrong invariant.
            var canvasPoint = parent.TransformPoint(localToParent.TransformPoint(t.SampleOrigin(phase, doc.seed)));
            var local = CanvasToLocal(doc, r, canvasPoint);

            _originCross.style.display = DisplayStyle.Flex;
            ApplyHoverVisibility();
            _originCross.style.left = local.x - CrossSize * 0.5f;
            _originCross.style.top = local.y - CrossSize * 0.5f;
        }

        void OnHandleDown(PointerDownEvent evt)
        {
            var node = SelectedNode?.Invoke();
            if (evt.button != 0 || node == null || !IsDraggable(node)) return;
            _dragging = true;
            _dragStartTranslate = node.transform.translate;
            _dragStartPointer = this.WorldToLocal(evt.position);
            _handle.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnHandleMove(PointerMoveEvent evt)
        {
            if (!_dragging) return;
            ApplyDrag(this.WorldToLocal(evt.position));
            evt.StopPropagation();
        }

        void OnHandleUp(PointerUpEvent evt)
        {
            if (!_dragging) return;
            _dragging = false;
            _handle.ReleasePointer(evt.pointerId);

            var node = SelectedNode?.Invoke();
            if (node?.transform != null)
            {
                // One undo step for the whole gesture: the live drag wrote straight onto the node, so the
                // pre-drag value is put back FIRST, then recorded, then the final value written over it.
                Vector2 settled = node.transform.translate;
                node.transform.translate = _dragStartTranslate;
                RecordUndo?.Invoke();
                node.transform.translate = settled;
            }
            Changed?.Invoke();
            evt.StopPropagation();
            DragCommitted?.Invoke();
        }

        void ApplyDrag(Vector2 localPointer)
        {
            var doc = _doc?.Invoke();
            var node = SelectedNode?.Invoke();
            if (doc == null || node?.transform == null) return;

            var r = ZoomedCanvasRect(doc);
            if (r.width <= 0f || r.height <= 0f) return;

            int w = Mathf.Max(1, doc.canvasWidth), h = Mathf.Max(1, doc.canvasHeight);
            float ps = Mathf.Max(1e-4f, doc.pixelSize);
            Vector2 d = localPointer - _dragStartPointer;
            // Pixels back to canvas units, with the same Y flip CanvasToLocal applies going the other way.
            var canvasDelta = new Vector2(d.x * w * ps / r.width, -d.y * h * ps / r.height);

            // The drag is felt in CANVAS space but written into the node's PARENT frame, so it goes back down
            // through the ancestors' linear part; without this a node inside a rotated or scaled bag would run
            // away from the cursor.
            float phase = doc.PhaseOfFrame(Mathf.Max(0, _frame?.Invoke() ?? 0));
            var parent = ParentForwardOf(SelectedLayerRoot?.Invoke(), node, phase, doc.seed);
            Vector2 localDelta = canvasDelta;
            if (parent.TryInvert(out var inv))
                localDelta = new Vector2(inv.m00 * canvasDelta.x + inv.m01 * canvasDelta.y,
                                         inv.m10 * canvasDelta.x + inv.m11 * canvasDelta.y);

            node.transform.translate = _dragStartTranslate + localDelta;
            Changed?.Invoke();
        }

        /// Where the canvas actually lands inside `view` under ScaleToFit + Zoom — the border has to follow
        /// the picture, not the panel, or it stops meaning "this is the canvas edge".
        static Rect FittedCanvasRect(Rect view, ShaperDocument doc)
        {
            float cw = Mathf.Max(1, doc.canvasWidth), ch = Mathf.Max(1, doc.canvasHeight);
            float scale = Mathf.Min(view.width / cw, view.height / ch);
            float w = cw * scale, h = ch * scale;
            return new Rect(view.x + (view.width - w) * 0.5f, view.y + (view.height - h) * 0.5f, w, h);
        }

        public void Dispose()
        {
            _prebaker.Stop();
            if (_tex == null) return;
            UnityEngine.Object.DestroyImmediate(_tex);
            _tex = null;
        }

        /// <summary>Re-key every cached frame against the document as it now stands and start the background
        /// pre-baker on whatever genuinely has to be recomputed. Called by the window on every authored edit
        /// (never on a pure view change like scrubbing, zoom or the backdrop — see ShaperWindow.cs's
        /// Change/Val, which is where this is hooked).
        ///
        /// T-0194 — the document is passed in rather than the cache being emptied, which is what lets a
        /// composite-only edit (a layer toggled, a Z offset, the background) keep its frames resident and
        /// re-composite them from the per-layer buffers instead of flashing the whole strip.</summary>
        public void InvalidateFrameCache()
        {
            _frameCache.Invalidate(_doc?.Invoke());
            CacheProgressed?.Invoke();
            StartPrebakeIfNeeded();
        }

        void StartPrebakeIfNeeded()
        {
            var doc = _doc?.Invoke();
            if (doc != null && doc.frameCount > 1) _prebaker.Start();
        }

        public void Refresh()
        {
            var doc = _doc?.Invoke();
            // Retained-mode: the backdrop is a real element, so it is pointed at the current settings rather
            // than being told to repaint. (As an IMGUIContainer this needed a manual MarkDirtyRepaint — one
            // of the small frictions that came with the island.)
            _backdrop.SetSettings(doc?.previewBackSplash);
            LayoutFrameBorder();
            LayoutHandle();
            LayoutOriginCross();

            if (doc == null)
            {
                _image.style.backgroundImage = null;
                return;
            }

            // A BLANK CHERRY BEAT shows nothing — not frame 0. ShaperCherry.BlankFrame (-1) is a real
            // authored state: it is what plays during the loop delay, and what an empty cherry sequence
            // renders as. Falling back to frame 0 would silently turn a deliberate gap into a held frame,
            // and the gap is the whole point of authoring one. The backdrop stays visible underneath,
            // which is exactly what a gap should look like.
            int frame = _frame?.Invoke() ?? 0;
            if (frame < 0)
            {
                _image.style.backgroundImage = null;
                return;
            }

            int w = Mathf.Max(1, doc.canvasWidth);
            int h = Mathf.Max(1, doc.canvasHeight);

            // T-0165 — read through the frame cache rather than rendering unconditionally. EnsureShape drops
            // the cache itself when the canvas or frame count actually changed (a document edit already
            // called InvalidateFrameCache for content changes; this additionally catches the shape changing
            // under a resident cache). The applier is passed inside ComputeFrame for the same reason the
            // baker passes it (T-0156): without it a document's authored effects render as a no-op, and
            // passing it in both places is what keeps "preview is what you bake" true.
            _frameCache.EnsureShape(Mathf.Max(1, doc.frameCount), w, h);
            var px = _frameCache.ComputeFrame(frame, doc);
            StartPrebakeIfNeeded();

            // T-0188 — nothing to show yet: HOLD the picture already on screen. The pixels are obtained
            // BEFORE the texture is touched precisely so this early-out cannot blank the preview. Tearing the
            // texture down first (as this did) destroyed the very image the element was displaying, so a
            // canvas resize that raced a not-yet-rendered frame left the preview empty on exactly the path
            // where the user most needs to keep seeing something.
            if (px == null || px.Length != w * h) return;

            if (_tex == null || _tex.width != w || _tex.height != h)
            {
                // Only the texture is replaced, never the pre-baker: the cache has already been re-shaped
                // for the new canvas above, so stopping and restarting the bake here would cost a full
                // re-walk to arrive back where it already is.
                if (_tex != null) UnityEngine.Object.DestroyImmediate(_tex);
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    // Point + no mips: this is pixel art, and any filtering makes the preview lie about the
                    // bake, whose importer settings ShaperBaker pins to exactly this.
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            // The marks go on a COPY (see the WantsOverlays field's own comment). The copy is skipped entirely
            // when nothing is switched on, so the ordinary path costs exactly what it did before.
            var shown = px;
            if (WantsOverlays != null && DrawOverlays != null && WantsOverlays())
            {
                if (_overlayScratch == null || _overlayScratch.Length != px.Length)
                    _overlayScratch = new Color32[px.Length];
                Array.Copy(px, _overlayScratch, px.Length);
                DrawOverlays(_overlayScratch, w, h, frame);
                shown = _overlayScratch;
            }

            _tex.SetPixels32(shown);
            _tex.Apply(false);
            _image.style.backgroundImage = Background.FromTexture2D(_tex);

            // Zoom magnifies the fitted picture only. It is applied as a LAYOUT inset on the image layer, so
            // it cannot reach the renderer: the same Color32[] is displayed larger, never re-rendered at a
            // different size. That is what keeps zoom cosmetic and the bake unaffected.
            float inset = Zoom <= 1f ? 0f : -(Zoom - 1f) * 50f;
            _image.style.left = Length.Percent(inset);
            _image.style.right = Length.Percent(inset);
            _image.style.top = Length.Percent(inset);
            _image.style.bottom = Length.Percent(inset);
        }
    }
}
