// ZuiGallery — a live showcase of every Zui control, custom-drawn and vanilla, in one window.
//
// Purpose is twofold: a visual reference for what the toolkit offers (so a tool author picks the right
// control instead of reaching for raw IMGUI), and a proving ground — every control here is interactive,
// so a regression in any of them shows up the moment this window is opened. Opened from
// Laubrary/ZUI Control Gallery.
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiGallery : ZuiWindow
    {
        [MenuItem("Laubrary/ZUI Control Gallery")]
        public static void Open() => GetWindow<ZuiGallery>("ZUI Gallery");

        // Live demo state.
        float micro = 0.4f, microBare = 12f, vSlider = 0.6f;
        bool tog1 = true, tog2, tbut = true;
        int seg = 1, cycle;
        int flags = 0b0101;   // for the multi-segmented demo
        float fval = 1.5f;
        int ival = 4;
        Color col = new Color(1f, 0.5f, 0.1f);
        Vector2 pad = new Vector2(0.2f, -0.1f);
        float rangeLo = 0.3f, rangeHi = 0.7f;

        Label readout;

        static readonly string[] SegLabels = { "Area", "Ring", "Rosing" };
        static readonly string[] FlagLabels = { "Alpha", "Hue", "Blur", "Warp" };
        static readonly string[] CycleLabels = { "Once", "Loop", "Ping-pong" };

        protected override void BuildUI(VisualElement root)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            var body = scroll.contentContainer;

            body.Add(Z.Text("ZUI control gallery", ZuiText.Section,
                "Every control the Laubrary.Zui toolkit offers, live. Custom-drawn controls recreate the old " +
                "IMGUI ZUI look; the rest are Unity controls skinned by ZuiToolkit.uss."));
            readout = Z.Text("(interact with a control)", ZuiText.Subtle, "The last value any control reported.");
            body.Add(readout);

            // ── custom-drawn (old ZUI look) ──────────────────────────────────────────
            var custom = Z.Box("Custom-drawn (old ZUI look)",
                "Painter2D controls that recreate the IMGUI ZUI aesthetic — a filled MicroSlider with no thumb, " +
                "button-style toggles, and segmented single/multi selectors.");

            custom.Add(Z.Field("MicroSlider", "Fill IS the value; label + value sit inside; no thumb. Drag it, " +
                "double-click to reset to 0.4.",
                Z.MicroSlider("Zoom", micro, 0f, 1f, "The fill's edge is the handle.",
                    v => { micro = v; Report($"MicroSlider = {v:0.###}"); }, 160f, defaultValue: 0.4f)));

            custom.Add(Z.Field("MicroSlider (no value)", "A bare fill-track for values whose exact number " +
                "doesn't matter — label only.",
                Z.MicroSlider("Size", microBare, 0f, 64f, "Just the fill, label only.",
                    v => { microBare = v; Report($"MicroSlider(bare) = {v:0}"); }, 160f, showValue: false)));

            custom.Add(Z.Row(
                Z.ToggleButton("Enabled", "A button that stays pressed when on — no checkmark.", tbut,
                    v => { tbut = v; Report($"ToggleButton = {v}"); }),
                Z.ToggleButton("Loop", "Another button toggle.", false, v => Report($"Loop = {v}")),
                Z.ToggleButton("Mirror", "And another.", true, v => Report($"Mirror = {v}"))));

            custom.Add(Z.Field("Segmented (radio)", "A joined button row, single-select — the themed twin of " +
                "MiniRadio.",
                Z.Segmented(seg, SegLabels, "Pick one.", i => { seg = i; Report($"Segmented = {SegLabels[i]}"); })));

            custom.Add(Z.Field("Segmented (multi)", "Independently-latching segments — the right control for a " +
                "flag set (e.g. Pyre's matte channels).",
                Z.SegmentedMulti(i => (flags & (1 << i)) != 0, FlagLabels, "Tick any combination.",
                    (i, on) => { if (on) flags |= 1 << i; else flags &= ~(1 << i);
                                 Report($"Flags = {System.Convert.ToString(flags, 2).PadLeft(4, '0')}"); })));
            body.Add(custom);

            // ── sliders & values ─────────────────────────────────────────────────────
            var vals = Z.Box("Sliders & values", "Unity's controls, skinned.");
            vals.Add(Z.Field("Slider", "Vanilla Slider with inline value field.",
                Z.Slider(vSlider, 0f, 1f, "Standard slider.", v => { vSlider = v; Report($"Slider = {v:0.###}"); }, 170f)));
            vals.Add(Z.Field("MinMax", "A numeric-flanked range slider.",
                Z.MinMax(rangeLo, rangeHi, 0f, 1f, "A low/high range.",
                    (lo, hi) => { rangeLo = lo; rangeHi = hi; Report($"Range = {lo:0.##}..{hi:0.##}"); })));
            vals.Add(Z.Row(
                Z.Field("Float", "A float field.", Z.Float(fval, "A float.", v => { fval = v; Report($"Float = {v}"); }, 70f)),
                Z.HSpace(),
                Z.Field("Int", "An int field.", Z.Int(ival, "An int.", v => { ival = v; Report($"Int = {v}"); }, 60f))));
            body.Add(vals);

            // ── toggles, radios, pickers ─────────────────────────────────────────────
            var pick = Z.Box("Toggles, radios & pickers", "The rest of the set.");
            pick.Add(Z.Row(
                Z.Toggle("Checkbox", "Vanilla checkbox toggle.", tog1, v => { tog1 = v; Report($"Toggle = {v}"); }),
                Z.HSpace(),
                Z.Toggle("Another", "A second checkbox.", tog2, v => { tog2 = v; Report($"Toggle2 = {v}"); })));
            pick.Add(Z.Field("MiniRadio", "The USS-skinned segmented radio.",
                Z.MiniRadio(seg, SegLabels, "Pick one.", i => { seg = i; Report($"MiniRadio = {SegLabels[i]}"); })));
            pick.Add(Z.Field("CycleButton", "One button that cycles through options on click.",
                Z.CycleButton(cycle, CycleLabels, "Click to cycle.", i => { cycle = i; Report($"Cycle = {CycleLabels[i]}"); })));
            pick.Add(Z.Field("Colour", "A colour field.",
                Z.Color(col, "A colour.", v => { col = v; Report("Colour changed"); }, 90f)));
            body.Add(pick);

            // ── 2D & spatial ─────────────────────────────────────────────────────────
            var spatial = Z.Box("2D & spatial", "For a spatial value, a 2D pad — never two separate 1D fields.");
            spatial.Add(Z.Field("Pad", "Drag the dot to set an X/Y offset.",
                Z.Pad(pad, new Rect(-1f, -1f, 2f, 2f), "A 2D offset.",
                    v => { pad = v; Report($"Pad = ({v.x:0.##}, {v.y:0.##})"); }, 80f)));
            body.Add(spatial);

            root.Add(scroll);
        }

        void Report(string s)
        {
            if (readout != null) readout.text = s;
        }
    }
}
