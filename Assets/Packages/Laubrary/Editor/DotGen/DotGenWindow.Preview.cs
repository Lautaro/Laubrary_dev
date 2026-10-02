// DotGenWindow.Preview — the picture, and everything about looking at it.
//
// The preview is the one sanctioned IMGUI island in this window (a bespoke 2D stage, not a control), drawn
// exactly like Pyre's: the shared BackSplash behind, the rendered frame on top, then whatever overlays the
// gizmo pass wants. Zoom and pan are properties of a person looking at a document, never of the document, so
// they live here and are not persisted.
//
// COORDINATE CONVENTION (what a gizmo pass must know):
//   The evaluator works in frame-normalized coordinates — (0,0) is the frame's TOP-LEFT corner, (1,1) its
//   bottom-right, y increasing DOWNWARD, matching the reference implementation. `frameRect` is where that
//   unit square currently sits on screen and `frameScale` is its side length in points, so
//   `FrameToScreen(x, y)` is the only conversion anything should use. Both are refreshed on EVERY event,
//   not only on repaint, so a hit test taken during a drag agrees with the last thing drawn.

using System.Collections.Generic;
using Laubrary.BackSplash.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.DotGen.Editor
{
    public partial class DotGenWindow
    {
        // ── the island ───────────────────────────────────────────────────────────────────────
        IMGUIContainer preview;
        [SerializeField] float previewHeight = 380f;
        const float PreviewHeightMin = 140f;
        const float PreviewHeightMax = 900f;

        // ── view state (never persisted — POC §17) ───────────────────────────────────────────
        float previewZoom = 1f;
        Vector2 previewPan = Vector2.zero;
        bool panning;
        const float ZoomMin = 0.25f;
        const float ZoomMax = 8f;
        const float ZoomStep = 1.25f;
        // The reference's wheel factor is exp(-deltaY * 0.0015) against a browser's ~100-per-notch deltas.
        // Unity reports ~3 per notch, so the exponent is rescaled to keep ONE NOTCH the same 0.86x/1.16x step
        // it is in the reference rather than an imperceptible one.
        const float WheelFactor = 0.05f;
        const float ViewMargin = 16f;
        const float FitPadding = 32f;

        // ── the cached picture ───────────────────────────────────────────────────────────────
        Texture2D frameTex;
        DotGenResult result;
        bool renderDirty = true;
        int renderedSize = -1;

        /// The last evaluation, so an overlay can show what a module did without running the pipeline again.
        /// Null until the preview has repainted once.
        public DotGenResult Result => result;

        /// Where the unit frame currently sits on screen, and its side length in points. See the header.
        Rect frameRect;
        float frameScale = 1f;
        Rect lastPreviewView;

        // ── chrome ───────────────────────────────────────────────────────────────────────────
        VisualElement chromeRow, legendHost, backdropHost;
        Label zoomLabel, gizmoLabel;

        Laubrary.BackSplash.BackSplashSettings backSplash
        {
            get
            {
                if (doc == null) return null;
                doc.previewBackSplash ??= new Laubrary.BackSplash.BackSplashSettings();
                return doc.previewBackSplash;
            }
        }

        void BuildPreviewPane(VisualElement rightPane, DotGen d)
        {
            preview = new IMGUIContainer(DrawPreview);
            preview.style.height = Mathf.Clamp(previewHeight, PreviewHeightMin, PreviewHeightMax);
            preview.AddToClassList("lau-tool-shell__stage");
            preview.focusable = true;   // so the four zoom keys reach it
            preview.AddToClassList("zui-stage");
            rightPane.Add(preview);
            rightPane.Add(BuildPreviewResizeBar());

            // A fixed-height, never-wrapping row holding every viewing control at all times: nothing here
            // appears or disappears, so the picture above it can never be shoved around by its own chrome.
            chromeRow = Z.Row();
            chromeRow.AddToClassList("lau-tool-shell__preview-toolbar");

            var zoomOut = Z.Button("−", "Zoom out.", () => SetZoom(previewZoom / ZoomStep, lastPreviewView));
            zoomOut.AddToClassList("lau-tool-shell__zoom-action");
            chromeRow.Add(zoomOut);
            zoomLabel = Z.Text("100%", ZuiText.Body, "The preview's current magnification.");
            zoomLabel.AddToClassList("lau-tool-shell__zoom-readout");
            chromeRow.Add(zoomLabel);
            var zoomIn = Z.Button("+", "Zoom in.", () => SetZoom(previewZoom * ZoomStep, lastPreviewView));
            zoomIn.AddToClassList("lau-tool-shell__zoom-action");
            chromeRow.Add(zoomIn);
            chromeRow.Add(Z.Button("Fit", "Show the whole frame, centred.", FitView));

            gizmoLabel = Z.Text("", ZuiText.Small,
                "Which module's process overlays are drawn — change it in the Frame section.");
            gizmoLabel.AddToClassList("lau-tool-shell__gizmo-readout");
            chromeRow.Add(gizmoLabel);

            // Variable-width content goes LAST in its row, so nothing sits after it to be pushed when it grows.
            legendHost = Z.Row();
            legendHost.AddToClassList("lau-tool-shell__legend");
            chromeRow.Add(legendHost);

            rightPane.Add(chromeRow);

            backdropHost = new VisualElement();
            rightPane.Add(backdropHost);
            FillBackdropPanel();

            RefreshChrome();
        }

        VisualElement BuildPreviewResizeBar()
        {
            var bar = new VisualElement { tooltip = "Drag to resize the preview vertically." };
            bar.AddToClassList("lau-tool-shell__resize-grip");
            bar.AddToClassList("lau-tool-shell__resize-grip--horizontal");
            bar.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { bar.CapturePointer(e.pointerId); e.StopPropagation(); } });
            bar.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!bar.HasPointerCapture(e.pointerId)) return;
                previewHeight = Mathf.Clamp(previewHeight + e.deltaPosition.y, PreviewHeightMin, PreviewHeightMax);
                if (preview != null) preview.style.height = previewHeight;
                e.StopPropagation();
            });
            bar.RegisterCallback<PointerUpEvent>(e => { if (bar.HasPointerCapture(e.pointerId)) bar.ReleasePointer(e.pointerId); });
            return bar;
        }

        void FillBackdropPanel()
        {
            if (backdropHost == null) return;
            backdropHost.Clear();
            backdropHost.Add(BackSplashZui.Build(backSplash, "Preview backdrop",
                "A cosmetic backdrop for the preview only — a solid colour plus one optional image. It is never "
                + "part of the rendered frame or the export, and is kept per document, so closing and reopening "
                + "this window keeps it.",
                onChanged: () => preview?.MarkDirtyRepaint(),
                onStructureChanged: () => { preview?.MarkDirtyRepaint(); FillBackdropPanel(); },
                icon: "eye", owner: doc));
        }

        // ── the render cache ─────────────────────────────────────────────────────────────────

        void InvalidateRender() => renderDirty = true;

        void ReleaseRender()
        {
            if (frameTex != null) DestroyImmediate(frameTex);
            frameTex = null;
            result = null;
            renderedSize = -1;
            renderDirty = true;
        }

        /// Evaluate and render at most once per repaint, and only when something actually changed. The whole
        /// pipeline is a few milliseconds, but a scroll that repaints the pane must not pay for it.
        void EnsureRender()
        {
            if (doc == null) return;
            int size = Mathf.Clamp(doc.frameSize, 64, 2048);
            if (!renderDirty && frameTex != null && renderedSize == size) return;

            result = DotGenEvaluator.Evaluate(doc);
            var tex = DotGenRenderer.Render(doc, result, size, withDots: true);
            if (frameTex != null) DestroyImmediate(frameTex);
            frameTex = tex;
            renderedSize = size;
            renderDirty = false;

            RefreshReadouts(result);
        }

        // ── navigation (POC §17) ─────────────────────────────────────────────────────────────

        float FitSize(Rect view)
            => Mathf.Max(64f, Mathf.Min(view.width - FitPadding, view.height - FitPadding));

        void ConstrainPan(Rect view)
        {
            float scaled = FitSize(view) * previewZoom;
            float maxX = Mathf.Max(0f, (scaled - view.width) * 0.5f + ViewMargin);
            float maxY = Mathf.Max(0f, (scaled - view.height) * 0.5f + ViewMargin);
            previewPan.x = Mathf.Clamp(previewPan.x, -maxX, maxX);
            previewPan.y = Mathf.Clamp(previewPan.y, -maxY, maxY);
        }

        void SetZoom(float next, Rect view, bool hasAnchor = false, Vector2 anchor = default)
        {
            next = Mathf.Clamp(next, ZoomMin, ZoomMax);
            if (Mathf.Abs(next - previewZoom) < 0.0001f) return;

            float fit = FitSize(view);
            if (hasAnchor && fit > 0.0001f)
            {
                // Keep the point under the cursor exactly where it is: find it in frame coordinates first,
                // then put the pan back so it lands on the same screen point at the new size.
                Vector2 centre = view.center + previewPan;
                float old = fit * previewZoom;
                Vector2 f = old > 0.0001f ? (anchor - centre) / old : Vector2.zero;
                previewZoom = next;
                previewPan = anchor - view.center - f * (fit * next);
            }
            else previewZoom = next;

            ConstrainPan(view);
            RefreshChrome();
            preview?.MarkDirtyRepaint();
        }

        void FitView()
        {
            previewZoom = 1f;
            previewPan = Vector2.zero;
            RefreshChrome();
            preview?.MarkDirtyRepaint();
        }

        void ResetView()
        {
            previewZoom = 1f;
            previewPan = Vector2.zero;
            panning = false;
        }

        void HandleNavigation(Event e, Rect view)
        {
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (!view.Contains(e.mousePosition)) return;
                    SetZoom(previewZoom * Mathf.Exp(-e.delta.y * WheelFactor), view, true, e.mousePosition);
                    e.Use();
                    break;

                case EventType.MouseDown:
                    if (e.button != 0 || !view.Contains(e.mousePosition)) return;
                    preview?.Focus();
                    if (e.clickCount == 2) FitView();
                    else panning = true;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (!panning) return;
                    previewPan += e.delta;
                    ConstrainPan(view);
                    preview?.MarkDirtyRepaint();
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (!panning) return;
                    panning = false;
                    e.Use();
                    break;

                case EventType.KeyDown:
                    if (HandleZoomKey(e.keyCode, view)) e.Use();
                    break;
            }
        }

        /// The preview's four keys. Split out so the shortcut set can be exercised without synthesizing a
        /// focused IMGUI key event.
        bool HandleZoomKey(KeyCode key, Rect view)
        {
            switch (key)
            {
                case KeyCode.Plus:
                case KeyCode.KeypadPlus:
                case KeyCode.Equals:
                    SetZoom(previewZoom * ZoomStep, view);
                    return true;
                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    SetZoom(previewZoom / ZoomStep, view);
                    return true;
                case KeyCode.Alpha0:
                case KeyCode.Keypad0:
                    FitView();
                    return true;
                default:
                    return false;
            }
        }

        // ── drawing ──────────────────────────────────────────────────────────────────────────

        void DrawPreview()
        {
            var view = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (doc == null) return;
            lastPreviewView = view;

            var e = Event.current;
            ComputeFrameRect(view);
            HandleNavigation(e, view);
            ComputeFrameRect(view);   // navigation may have moved it; a hit test must agree with the last draw

            if (e.type != EventType.Repaint) return;

            DrawBackdrop(view);
            EnsureRender();

            if (frameTex != null)
            {
                // Point sampling once the pixels are big enough to read as pixels; bilinear below that, where
                // point sampling only makes a small picture look broken.
                frameTex.filterMode = previewZoom >= 2f ? FilterMode.Point : FilterMode.Bilinear;
                GUI.DrawTexture(frameRect, frameTex, ScaleMode.StretchToFill, true);
            }

            DrawGizmos(frameRect, frameScale);
        }

        void ComputeFrameRect(Rect view)
        {
            ConstrainPan(view);
            float scaled = FitSize(view) * previewZoom;
            frameScale = scaled;
            frameRect = new Rect(
                view.center.x - scaled * 0.5f + previewPan.x,
                view.center.y - scaled * 0.5f + previewPan.y,
                scaled, scaled);
        }

        void DrawBackdrop(Rect view)
            => BackSplashPainter.Draw(view, backSplash, new Color(0.1f, 0.1f, 0.12f));

        /// Frame coordinates (0..1, y downward from the frame's top edge) → screen points. The ONE mapping any
        /// overlay should use; it can never disagree with the picture because it reads the same rect the blit did.
        Vector2 FrameToScreen(float x, float y)
            => new Vector2(frameRect.x + x * frameScale, frameRect.y + y * frameScale);

        Vector2 FrameToScreen(Vector2 p) => FrameToScreen(p.x, p.y);

        /// Implemented by DotGenWindow.Gizmos.cs (W2.3). Called on Repaint only, AFTER the frame texture is
        /// blitted, with the frame's on-screen rect and its side length in points. Left as a partial method so
        /// the gizmo pass is one new file with no edit to this one — and so a build without it costs nothing:
        /// an unimplemented partial method call is removed by the compiler.
        partial void DrawGizmos(Rect frame, float scale);

        // ── chrome refresh ───────────────────────────────────────────────────────────────────

        /// The zoom readout, the gizmo-mode readout and the legend. Text and legend chips only — the row's
        /// geometry never changes, so refreshing it can never move the picture above it.
        void RefreshChrome()
        {
            if (zoomLabel != null) zoomLabel.text = Mathf.RoundToInt(previewZoom * 100f) + "%";
            if (gizmoLabel != null && doc != null) gizmoLabel.text = "Gizmos: " + doc.gizmoMode;

            if (legendHost == null) return;
            legendHost.Clear();
            if (doc == null) return;

            var flat = doc.Tree().Flatten();
            for (int i = 0; i < flat.Count; i++)
            {
                var g = flat[i];
                if (g == null || !g.enabled || !g.showDots) continue;
                legendHost.Add(BuildLegendEntry(g));
            }
        }

        VisualElement BuildLegendEntry(DotGenerator g)
        {
            var entry = Z.Row();
            entry.AddToClassList("lau-tool-shell__legend-entry");

            var chip = new VisualElement { tooltip = "Dots drawn in this colour come from " + g.name + "." };
            chip.AddToClassList("zui-row__swatch");
            chip.AddToClassList("zui-row__swatch--legend");
            chip.style.backgroundColor = g.color;
            entry.Add(chip);

            var label = Z.Text(g.name, ZuiText.Small, "Dots drawn in this colour come from " + g.name + ".");
            label.AddToClassList("lau-tool-shell__legend-label");
            entry.Add(label);
            return entry;
        }
    }
}
