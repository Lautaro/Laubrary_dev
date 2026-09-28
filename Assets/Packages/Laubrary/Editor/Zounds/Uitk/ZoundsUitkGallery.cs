using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The foundation gallery for the Zounds UI Toolkit port (T-0457/T-0458): the same Zounds-styled controls drawn by
    /// the old IMGUI ZUI and by their UI Toolkit twins, at the same pixel positions, so <see cref="ZoundsUitkCompare"/>
    /// can diff one against the other. Positions are absolute on both sides on purpose: this compares how each control
    /// is drawn, not how two layout engines pack; layout parity is checked on the real windows.
    /// Opened from code (no menu item): <c>ZoundsUitkGallery.OpenPair()</c>.
    /// </summary>
    public static class ZoundsUitkGallery {

        public const float W = 620f, H = 192f;

        public static (EditorWindow old, EditorWindow uitk) OpenPair() {
            var a = ScriptableObject.CreateInstance<ImguiGallery>();
            a.titleContent = new GUIContent("Zounds Gallery IMGUI");
            a.ShowUtility();
            a.position = new Rect(120, 160, W, H);
            var b = ScriptableObject.CreateInstance<UitkGallery>();
            b.titleContent = new GUIContent("Zounds Gallery UITK");
            b.ShowUtility();
            b.position = new Rect(120 + W + 12, 160, W, H);
            return (a, b);
        }

        // The rows, shared by both sides so they cannot drift. Every coordinate and size is a multiple of 4 logical pixels,
        // i.e. a whole device pixel at the 225 % display scale used here: at a fractional device pixel IMGUI and UI Toolkit
        // round to opposite sides (measured: a control at x = 10, i.e. 22.5 px, landed one pixel apart), which would hide
        // real differences under a blanket of half-pixel ones.
        internal static readonly Rect Render = new Rect(8, 8, 60, 20), Remove = new Rect(72, 8, 72, 20), Convert = new Rect(148, 8, 100, 20);
        internal static readonly Rect LiveOff = new Rect(8, 40, 84, 20), KeepOn = new Rect(100, 40, 72, 20);
        internal static readonly Rect SegA = new Rect(180, 40, 60, 20), SegB = new Rect(240, 40, 72, 20);
        internal static readonly Rect Speed = new Rect(8, 72, 152, 16), Window = new Rect(168, 72, 120, 16), ValueSl = new Rect(292, 72, 152, 16);
        internal static readonly Rect CAll = new Rect(8, 104, 48, 20), CLeft = new Rect(64, 104, 48, 20), CNone = new Rect(120, 104, 48, 20), CRight = new Rect(176, 104, 48, 20);
        internal static readonly Rect Flat = new Rect(240, 104, 80, 20), ZBtn = new Rect(328, 104, 88, 24);
        internal const float SpeedT = 0.4363f, WindowV = 30f, ValueV = 0.35f;

        // ── the chain pair (T-0462..T-0466) ──
        public const float ChainW = 960f, ChainH = 900f;

        /// <summary>
        /// Opens the old chain editor and its twin side by side on one in-memory Klip (never added to the library, so the
        /// owner's project is untouched) whose chain exercises every kind of control: inline and wrapped effect rows,
        /// sliders (linear, log, integer), toggles, choice strips, a modulated slider, the four modifier kinds with
        /// wave icons, a two-handled range, a step list, and bindings in each combine mode.
        /// </summary>
        public static (EditorWindow old, EditorWindow uitk) OpenChainPair(int expandNode = 2) {
            var k = SampleKlip();
            var a = ScriptableObject.CreateInstance<ImguiChainGallery>();
            a.klip = k; a.expand = expandNode;
            a.titleContent = new GUIContent("Zounds Chain IMGUI");
            a.ShowUtility();
            a.position = new Rect(40, 60, ChainW, ChainH);
            var b = ScriptableObject.CreateInstance<UitkChainGallery>();
            b.klip = k; b.expand = expandNode;
            b.titleContent = new GUIContent("Zounds Chain UITK");
            b.ShowUtility();
            b.position = new Rect(40 + ChainW + 12, 60, ChainW, ChainH);
            return (a, b);
        }

        internal static Klip SampleKlip() {
            var k = new Klip(-777) { name = "Chain gallery" };
            var c = k.effectChain;
            c.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain));
            c.nodes.Add(new ZoundEffectNode(ZoundEffectType.LowPass));
            c.nodes.Add(new ZoundEffectNode(ZoundEffectType.EQ));
            c.nodes.Add(new ZoundEffectNode(ZoundEffectType.Distortion));
            c.nodes.Add(new ZoundEffectNode(ZoundEffectType.Delay) { enabled = false });
            c.nodes.Add(new ZoundEffectNode(ZoundEffectType.BitCrush));
            c.modifiers.Add(new ZoundModifier(ZoundModifierType.Envelope));
            c.modifiers.Add(new ZoundModifier(ZoundModifierType.Lfo) { name = "Wobble" });
            c.modifiers.Add(new ZoundModifier(ZoundModifierType.Random));
            var step = new ZoundModifier(ZoundModifierType.Step) { steps = new[] { 1f, -0.5f, 0.25f, 0.8f } };
            c.modifiers.Add(step);
            foreach (var n in c.nodes) n.EnsureParams();
            foreach (var m in c.modifiers) m.EnsureParams();
            c.bindings.Add(new ZoundModifierBinding { modifierIndex = 1, nodeIndex = 1, paramIndex = 0, combine = Dsp.ModulationCombine.Shift, depth = 0.6f, schema = Dsp.ChainModulationCompat.CURRENT_SCHEMA });
            c.bindings.Add(new ZoundModifierBinding { modifierIndex = 2, nodeIndex = 0, paramIndex = 0, combine = Dsp.ModulationCombine.Set, depth = 1f, schema = Dsp.ChainModulationCompat.CURRENT_SCHEMA });
            c.bindings.Add(new ZoundModifierBinding { modifierIndex = 3, nodeIndex = -1, paramIndex = 0, combine = Dsp.ModulationCombine.Shift, depth = 0.25f, schema = Dsp.ChainModulationCompat.CURRENT_SCHEMA });
            c.Touch();
            return k;
        }
    }

    internal class ImguiChainGallery : EditorWindow {
        internal Klip klip; internal int expand = -1;
        ChainEditorGUI editor;
        void OnDisable() => editor?.Dispose();
        void OnGUI() {
            if (klip == null) return;
            using var _ = ZUI.UseSheet("Zounds");
            if (editor == null) { editor = new ChainEditorGUI(); editor.SelectedNode = expand; }
            GUILayout.BeginArea(new Rect(8, 8, position.width - 16, position.height - 16));
            editor.Draw(klip);
            GUILayout.EndArea();
        }
    }

    internal class UitkChainGallery : ZuiWindow {
        internal Klip klip; internal int expand = -1;
        protected override void BuildUI(VisualElement root) {
            ZS.Attach(root);
            if (klip == null) return;
            var ed = new ChainEditorTK(klip) { SelectedNode = expand };
            ed.At(8, 8);
            ed.style.width = ZoundsUitkGallery.ChainW - 16f;
            root.Add(ed);
        }
    }

    internal class ImguiGallery : EditorWindow {
        void OnGUI() {
            using var _ = ZUI.UseSheet("Zounds");
            ZUI.Button(ZoundsUitkGallery.Render, "Render", ZUI.Style.RichButton, ZUICornerMask.All);
            ZUI.Button(ZoundsUitkGallery.Remove, "Remove", ZUI.Style.RichButton, ZUI.Tint.Danger, ZUICornerMask.All);
            ZUI.Button(ZoundsUitkGallery.Convert, "Convert to Zeq", ZUI.Style.RichButton, ZUICornerMask.All);
            ZUI.Toggle(ZoundsUitkGallery.LiveOff, false, "Live speed", ZUI.Style.RichToggle, null, ZUICornerMask.All);
            ZUI.Toggle(ZoundsUitkGallery.KeepOn, true, "Keep hits", ZUI.Style.RichToggle, null, ZUICornerMask.All);
            ZUI.Toggle(ZoundsUitkGallery.SegA, true, "WSOLA", ZUI.Style.RichToggle, null, ZUICornerMask.Left);
            ZUI.Toggle(ZoundsUitkGallery.SegB, false, "Granular", ZUI.Style.RichToggle, null, ZUICornerMask.Right);
            ZUI.MicroSlider(ZoundsUitkGallery.Speed, ZoundsUitkGallery.SpeedT, 0f, 1f, "Speed ×0.50", ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, 0.5f);
            ZUI.MicroSlider(ZoundsUitkGallery.Window, ZoundsUitkGallery.WindowV, 10f, 100f, "Window 30 ms", ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, 30f);
            ZUI.MicroSlider(ZoundsUitkGallery.ValueSl, ZoundsUitkGallery.ValueV, 0f, 1f, "Depth", ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelAndValue, 1f);
            ZUI.Button(ZoundsUitkGallery.CAll, "All", ZUI.Style.RichButton, ZUICornerMask.All);
            ZUI.Button(ZoundsUitkGallery.CLeft, "Left", ZUI.Style.RichButton, ZUICornerMask.Left);
            ZUI.Button(ZoundsUitkGallery.CNone, "None", ZUI.Style.RichButton, ZUICornerMask.None);
            ZUI.Button(ZoundsUitkGallery.CRight, "Right", ZUI.Style.RichButton, ZUICornerMask.Right);
            ZUI.Button(ZoundsUitkGallery.Flat, "Flat", ZUI.Style.Flat, ZUICornerMask.None);
            ZUI.Button(ZoundsUitkGallery.ZBtn, "ZoundBtn", ZUI.Style.ZoundBtn, ZUICornerMask.None);
        }
    }

    internal class UitkGallery : ZuiWindow {
        protected override void BuildUI(VisualElement root) {
            ZS.Attach(root);
            void Put(VisualElement e, Rect r) { e.At(r.x, r.y); e.style.width = r.width; e.style.height = r.height; root.Add(e); }
            Put(ZS.Button("Render", "tip", "RichButton", null, ZUICornerMask.All), ZoundsUitkGallery.Render);
            Put(ZS.Button("Remove", "tip", "RichButton", null, ZUICornerMask.All), ZoundsUitkGallery.Remove);
            Put(ZS.Button("Convert to Zeq", "tip", "RichButton", null, ZUICornerMask.All), ZoundsUitkGallery.Convert);
            Put(ZS.Toggle("Live speed", "tip", false, null, "RichToggle", ZUICornerMask.All), ZoundsUitkGallery.LiveOff);
            Put(ZS.Toggle("Keep hits", "tip", true, null, "RichToggle", ZUICornerMask.All), ZoundsUitkGallery.KeepOn);
            Put(ZS.Toggle("WSOLA", "tip", true, null, "RichToggle", ZUICornerMask.Left), ZoundsUitkGallery.SegA);
            Put(ZS.Toggle("Granular", "tip", false, null, "RichToggle", ZUICornerMask.Right), ZoundsUitkGallery.SegB);
            Put(ZS.Slider("Speed ×0.50", ZoundsUitkGallery.SpeedT, 0f, 1f, "tip", null, ZuiSkinSlider.LabelMode.LabelOnly, 0.5f), ZoundsUitkGallery.Speed);
            Put(ZS.Slider("Window 30 ms", ZoundsUitkGallery.WindowV, 10f, 100f, "tip", null, ZuiSkinSlider.LabelMode.LabelOnly, 30f), ZoundsUitkGallery.Window);
            Put(ZS.Slider("Depth", ZoundsUitkGallery.ValueV, 0f, 1f, "tip", null, ZuiSkinSlider.LabelMode.LabelAndValue, 1f), ZoundsUitkGallery.ValueSl);
            Put(ZS.Button("All", "tip", "RichButton", null, ZUICornerMask.All), ZoundsUitkGallery.CAll);
            Put(ZS.Button("Left", "tip", "RichButton", null, ZUICornerMask.Left), ZoundsUitkGallery.CLeft);
            Put(ZS.Button("None", "tip", "RichButton", null, ZUICornerMask.None), ZoundsUitkGallery.CNone);
            Put(ZS.Button("Right", "tip", "RichButton", null, ZUICornerMask.Right), ZoundsUitkGallery.CRight);
            Put(ZS.Button("Flat", "tip", "Flat", null, ZUICornerMask.None), ZoundsUitkGallery.Flat);
            Put(ZS.Button("ZoundBtn", "tip", "ZoundBtn", null, ZUICornerMask.None), ZoundsUitkGallery.ZBtn);
        }
    }
}
