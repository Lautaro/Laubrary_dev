// PyrePlusWindow — the authoring window for the PyrePlus prototype (see PYREPLUS_DESIGN.md).
//
// Built directly on the UI Toolkit toolkit (ZuiAssetWindow + Z.* controls + ZuiSection folding), NOT on
// Zolumn — the UITK migration already gives collapsible bordered sections and grow-to-fill layout, so the
// design's Zolumn dependency is dropped. SLICE 1: the Shape section + a live preview. Swarm and Modifiers
// sections follow. Deliberately a separate menu item and asset type from real Pyre, which is untouched.
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyrePlus.Editor
{
    public class PyrePlusWindow : ZuiAssetWindow<PyrePlusSpec>
    {
        [MenuItem("Laubrary/Pyre Plus")]
        public static void Open() => GetWindow<PyrePlusWindow>("Pyre Plus");

        PyrePlusSpec spec => Current;
        protected override string TypeLabel => "Pyre Plus";
        protected override string NewAssetName => "New Pyre Plus";
        protected override string DefaultFolder => "Assets/PyrePlus";

        protected override Texture2D RenderThumbnail(PyrePlusSpec item)
        {
            int mid = Mathf.Clamp(item.frameCount / 2, 0, Mathf.Max(0, item.frameCount - 1));
            return PyrePlusRenderer.RenderFrameTexture(item, mid);
        }
        protected override bool AnimateThumbnails => true;
        protected override void UpdateAnimatedThumbnail(PyrePlusSpec item, Texture2D tex, double time)
        {
            if (item == null || item.frameCount <= 1) return;
            float fps = Mathf.Max(1f, item.previewFps);
            int f = Mathf.FloorToInt((float)(time * fps)) % item.frameCount;
            tex.SetPixels32(PyrePlusRenderer.RenderFrame(item, f));
            tex.Apply();
        }

        // preview state
        IMGUIContainer preview;
        Texture2D previewTex;
        double lastTime;
        float acc;
        bool playing = true;
        int frame;

        protected override void OnEnable()
        {
            base.OnEnable();
            EditorApplication.update += Tick;
        }
        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= Tick;
            if (previewTex != null) { DestroyImmediate(previewTex); previewTex = null; }
        }
        protected override void OnAssetChanged() { frame = 0; previewDirty = true; }

        void Tick()
        {
            if (!playing || spec == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTime), 0f, 0.1f);
            lastTime = now;
            acc += dt * Mathf.Max(1f, spec.previewFps);
            bool advanced = false;
            while (acc >= 1f) { acc -= 1f; frame = (frame + 1) % Mathf.Max(1, spec.frameCount); advanced = true; }
            if (advanced) { previewDirty = true; preview?.MarkDirtyRepaint(); }
        }

        bool previewDirty = true;
        int lastRenderedFrame = -1;

        void MarkDirty() { previewDirty = true; preview?.MarkDirtyRepaint(); }

        protected override void BuildAsset(VisualElement root, PyrePlusSpec s)
        {
            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;

            // ── left: dials ──────────────────────────────────────────────────────
            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.width = 360f;
            left.style.flexShrink = 0f;
            var dials = left.contentContainer;

            BuildCanvas(dials, s);
            BuildShape(dials, s);

            // ── right: preview ───────────────────────────────────────────────────
            preview = new IMGUIContainer(() => DrawPreview(s));
            preview.style.flexGrow = 1f;
            preview.style.minWidth = 200f;
            preview.AddToClassList("zui-stage");

            split.Add(left);
            split.Add(preview);
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            root.Add(split);
        }

        void BuildCanvas(VisualElement root, PyrePlusSpec s)
        {
            var box = Z.Box("Canvas", "The output resolution, frame count, seed and backdrop.");
            box.Add(WrapRow(
                Z.Field("Size", "Square canvas size in pixels.",
                    Z.Int(s.canvasSize, "Square canvas size in pixels.", v => Dirty(() => s.canvasSize = Mathf.Max(1, v)), 60f)),
                Z.Field("PPU", "Pixels per unit for the baked sprite.",
                    Z.Float(s.pixelsPerUnit, "Pixels per unit.", v => Dirty(() => s.pixelsPerUnit = Mathf.Max(1f, v)), 60f))));
            box.Add(WrapRow(
                Z.Field("Frames", "How many frames the animation bakes to.",
                    Z.SliderInt(s.frameCount, 1, 64, "Frame count.", v => Dirty(() => s.frameCount = v), 150f)),
                Z.Field("Seed", "Random seed — every particle's randomness derives from it.",
                    Z.Int(s.seed, "Random seed.", v => Dirty(() => s.seed = v), 70f))));
            root.Add(box);
        }

        void BuildShape(VisualElement root, PyrePlusSpec s)
        {
            var sec = Z.Section("Shape", "The particle's own look — colour, opacity and size over its life.");
            s.alpha ??= new ZUIValue(1f);
            sec.Add(Z.Field("Colour", "Colour over the particle's life (0 = birth, 1 = death).",
                GradientField("Colour", () => s.colorOverLife, g => Dirty(() => s.colorOverLife = g))));
            sec.Add(Val("Alpha", "Opacity over the particle's own life.", s.alpha, 0f, 1f));
            sec.Add(Val("Size (px)", "Radius in pixels over the particle's own life.", s.size, 0f, 32f));
            sec.Add(Z.Field("Edge", "Soft rim (1) vs a hard pixel edge (0).",
                Z.MicroSlider("Edge", s.edgeSoftness, 0f, 1f, "Soft rim vs hard edge.",
                    v => Dirty(() => s.edgeSoftness = v), 150f, showValue: true)));
            root.Add(sec);
        }

        // ── helpers ──────────────────────────────────────────────────────────────
        static VisualElement WrapRow(params VisualElement[] kids)
        {
            var r = Z.Row(kids); r.style.flexWrap = Wrap.Wrap; return r;
        }

        VisualElement Val(string label, string tooltip, ZUIValue v, float lo, float hi)
        {
            var o = new ZuiValueControl.Options
            {
                absMin = lo, absMax = hi,
                hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                controlWidth = 170f, grow = true,
            };
            return Z.Value(label, v, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre Plus"));
        }

        VisualElement GradientField(string label, System.Func<Gradient> get, System.Action<Gradient> set)
        {
            var gf = new UnityEditor.UIElements.GradientField { value = get() ?? new Gradient(), tooltip = label };
            gf.style.width = 200f;
            gf.RegisterValueChangedCallback(e => set(e.newValue));
            return gf;
        }

        void Dirty(System.Action apply)
        {
            if (spec == null) return;
            Undo.RecordObject(spec, "Edit Pyre Plus");
            apply();
            EditorUtility.SetDirty(spec);
            MarkDirty();
        }

        void DrawPreview(PyrePlusSpec s)
        {
            var view = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type != EventType.Repaint || s == null) return;
            EditorGUI.DrawRect(view, new Color(0.1f, 0.1f, 0.12f));

            int cur = Mathf.Clamp(frame, 0, Mathf.Max(0, s.frameCount - 1));
            if (previewDirty || cur != lastRenderedFrame || previewTex == null)
            {
                if (previewTex != null) DestroyImmediate(previewTex);
                previewTex = PyrePlusRenderer.RenderFrameTexture(s, cur);
                lastRenderedFrame = cur;
                previewDirty = false;
            }
            float zoom = Mathf.Max(1f, s.previewZoom);
            float w = s.Width * zoom, h = s.Height * zoom;
            var r = new Rect(view.center.x - w * 0.5f, view.center.y - h * 0.5f, w, h);
            GUI.DrawTexture(r, previewTex, ScaleMode.StretchToFill, true);
            GUI.Label(new Rect(view.x + 6, view.yMax - 20, 200, 18), $"frame {cur + 1}/{s.frameCount}", EditorStyles.whiteMiniLabel);
        }
    }
}
