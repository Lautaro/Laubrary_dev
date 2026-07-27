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
        float dirYaw = -55f, dirPitch = 38f, dirDist = 3.5f;
        float rangeLo = 0.3f, rangeHi = 0.7f;
        bool menuChecked, menuOverlay = true;
        int menuMode;

        Label readout;

        static readonly string[] SegLabels = { "Area", "Ring", "Rosing" };
        static readonly string[] FlagLabels = { "Alpha", "Hue", "Blur", "Warp" };
        static readonly string[] CycleLabels = { "Once", "Loop", "Ping-pong" };
        static readonly string[] MenuModes = { "Off", "Draft", "Final" };
        // Optional per-option icons (task #69) — one name per label; a null entry stays text-only.
        static readonly string[] SegIcons = { "square", "circle", "star" };
        static readonly string[] CycleIcons = { "play", "repeat", "arrows-left-right" };

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
                "button-style toggles, and segmented single/multi selectors.",
                "paint-brush");

            custom.Add(Z.Field("MicroSlider", "Fill IS the value; label + value sit inside; no thumb. Drag it, " +
                "double-click to reset to 0.4.",
                Z.MicroSlider("Zoom", micro, 0f, 1f, "The fill's edge is the handle.",
                    v => { micro = v; Report($"MicroSlider = {v:0.###}"); }, 160f, defaultValue: 0.4f)));

            custom.Add(Z.Field("MicroSlider (no value)", "A bare fill-track for values whose exact number " +
                "doesn't matter — label only.",
                Z.MicroSlider("Size", microBare, 0f, 64f, "Just the fill, label only.",
                    v => { microBare = v; Report($"MicroSlider(bare) = {v:0}"); }, 160f, showValue: false)));

            // Toggle buttons carrying an optional leading icon (task #69) — the glyph reads at a glance in a
            // row of feature flags, so you don't have to read every label.
            custom.Add(Z.Row(
                Z.ToggleButton("Enabled", "A button that stays pressed when on — no checkmark.", tbut,
                    v => { tbut = v; Report($"ToggleButton = {v}"); }, icon: "eye"),
                Z.ToggleButton("Loop", "Another button toggle, with an icon.", false,
                    v => Report($"Loop = {v}"), icon: "repeat"),
                Z.ToggleButton("Mirror", "And another.", true,
                    v => Report($"Mirror = {v}"), icon: "flip-horizontal")));

            custom.Add(Z.Field("Segmented (icons)", "A joined button row, single-select — each segment carries " +
                "an optional icon (task #69), so the options read apart without leaning on the labels.",
                Z.Segmented(seg, SegLabels, "Pick one.", i => { seg = i; Report($"Segmented = {SegLabels[i]}"); },
                    icons: SegIcons)));

            custom.Add(Z.Field("Segmented (multi)", "Independently-latching segments — the right control for a " +
                "flag set (e.g. Pyre's matte channels).",
                Z.SegmentedMulti(i => (flags & (1 << i)) != 0, FlagLabels, "Tick any combination.",
                    (i, on) => { if (on) flags |= 1 << i; else flags &= ~(1 << i);
                                 Report($"Flags = {System.Convert.ToString(flags, 2).PadLeft(4, '0')}"); })));
            body.Add(custom);

            // ── sliders & values ─────────────────────────────────────────────────────
            var vals = Z.Box("Sliders & values", "Unity's controls, skinned.", "sliders-horizontal");
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
            var pick = Z.Box("Toggles, radios & pickers", "The rest of the set.", "check-square");
            pick.Add(Z.Row(
                Z.Toggle("Checkbox", "Vanilla checkbox toggle.", tog1, v => { tog1 = v; Report($"Toggle = {v}"); }),
                Z.HSpace(),
                Z.Toggle("Another", "A second checkbox.", tog2, v => { tog2 = v; Report($"Toggle2 = {v}"); })));
            pick.Add(Z.Field("MiniRadio", "The USS-skinned segmented radio (text only).",
                Z.MiniRadio(seg, SegLabels, "Pick one.", i => { seg = i; Report($"MiniRadio = {SegLabels[i]}"); })));
            pick.Add(Z.Field("MiniRadio (icons)", "The same radio with an optional icon per option (task #69) — " +
                "icon + label, so the choices differentiate at a glance.",
                Z.MiniRadio(seg, SegLabels, "Pick one.", i => { seg = i; Report($"MiniRadio = {SegLabels[i]}"); },
                    icons: SegIcons)));
            pick.Add(Z.Field("CycleButton (icons)", "One button that cycles through options on click — its icon " +
                "swaps with the option.",
                Z.CycleButton(cycle, CycleLabels, "Click to cycle.", i => { cycle = i; Report($"Cycle = {CycleLabels[i]}"); },
                    icons: CycleIcons)));
            pick.Add(Z.Field("Colour", "A colour field.",
                Z.Color(col, "A colour.", v => { col = v; Report("Colour changed"); }, 90f)));
            body.Add(pick);

            // ── 2D & spatial ─────────────────────────────────────────────────────────
            var spatial = Z.Box("2D & spatial", "For a spatial value, a 2D pad — never two separate 1D fields.",
                "crosshair-simple");
            spatial.Add(Z.Field("Pad", "Drag the dot to set an X/Y offset.",
                Z.Pad(pad, new Rect(-1f, -1f, 2f, 2f), "A 2D offset.",
                    v => { pad = v; Report($"Pad = ({v.x:0.##}, {v.y:0.##})"); }, 80f)));

            // A 3D direction/orientation (yaw + pitch + distance) on a draggable lit sphere, wrapped in the
            // titled Z.Frame primitive. Drag the sphere or type; hover it (or hit Pin) for a larger 3D preview.
            spatial.Add(Z.Frame("Light direction",
                "Z.Direction3D — a reusable yaw/pitch(/distance) control on a draggable LIT SPHERE, wrapped in " +
                "the non-folding Z.Frame. The lit hotspot is the readout; hover or pin for a larger preview.",
                Z.Direction3D(dirYaw, dirPitch, dirDist,
                    "Aim a direction on the sphere — yaw (left/right), pitch (up/down), distance (in radii).",
                    (y, p, d) => { dirYaw = y; dirPitch = p; dirDist = d;
                                   Report($"Direction = yaw {y:0.#}° / pitch {p:0.#}° / dist {d:0.##}"); },
                    new ZuiDirection3D.Options { showDistance = true })));
            body.Add(spatial);

            // ── gradients ────────────────────────────────────────────────────────────
            var gradBox = Z.Box("Gradients",
                "ZuiGradient — a base gradient plus non-destructive transform knobs (reverse / hue / sat / " +
                "brightness / contrast / quantise / cycle). The strip on top is a LIVE preview painted from " +
                "ToLut(), the exact ramp the runtime + palette-cycle shader use, so it can never lie.",
                "palette");
            var demoGrad = new ZuiGradient();
            var fg = new Gradient();
            fg.SetKeys(
                new[] { new GradientColorKey(new Color(0.6f, 0f, 0f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0f), 0.5f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            demoGrad.gradient = fg;
            gradBox.Add(Z.Gradient(demoGrad,
                "Dial the transforms — the preview strip up top always shows the true evaluated result.",
                () => Report("Gradient changed")));
            body.Add(gradBox);

            // ── popovers & menus ─────────────────────────────────────────────────────
            var pop = Z.Box("Popovers & menus",
                "ZuiPopover is a floating ZUI card anchored to a control — it flips/clamps to stay on-screen " +
                "and dismisses on an outside click or Esc. Z.Menu composes the GenericMenu stand-in over it: " +
                "sections, icon items, checkmarks, separators, and persistent toggle / radio rows.",
                "list");

            Button menuBtn = null;
            menuBtn = Z.Button("Open menu ▾", "Every ZuiMenu row type in one menu — icon items, a checkmark, a " +
                "disabled row, a separator, a persistent toggle and a radio group.",
                () => Z.Menu(menuBtn)
                    .Section("Actions")
                    .IconItem("pencil", "Rename", "Rename this item.", () => Report("Menu → Rename"))
                    .IconItem("copy", "Duplicate", "Duplicate this item.", () => Report("Menu → Duplicate"))
                    .IconItem("trash", "Delete", "Delete this item.", () => Report("Menu → Delete"))
                    .Separator()
                    .Item("Snap to grid", "A one-shot checkmark item — closes on click, GenericMenu-style.",
                        () => { menuChecked = !menuChecked; Report($"Menu → Snap = {menuChecked}"); }, @checked: menuChecked)
                    .Item("Unavailable", "A disabled row greys out and ignores clicks.", null, enabled: false)
                    .Section("Live settings")
                    .Toggle("Show overlay", "A persistent toggle row — the menu stays open so several settings " +
                        "can be flipped in one visit.", menuOverlay,
                        v => { menuOverlay = v; Report($"Menu → Overlay = {v}"); })
                    .Radio("Mode", MenuModes, menuMode, "A MiniRadio group living inside the menu.",
                        i => { menuMode = i; Report($"Menu → Mode = {MenuModes[i]}"); })
                    .Show());

            Button flyBtn = null;
            flyBtn = Z.Button("Flyout…", "A bespoke Z.Popover holding arbitrary ZUI controls — not a menu.",
                () => Z.Popover(flyBtn, panel =>
                {
                    panel.Add(Z.Text("Quick settings", ZuiText.Section, "A flyout can hold any ZUI controls."));
                    panel.Add(Z.Field("Opacity", "Preview opacity.",
                        Z.Slider(0.8f, 0f, 1f, "Opacity.", v => Report($"Flyout → Opacity = {v:0.##}"), 150f)));
                    panel.Add(Z.MicroSlider("Zoom", 0.5f, 0f, 1f, "Zoom the preview.",
                        v => Report($"Flyout → Zoom = {v:0.##}"), 150f));
                }, new ZuiPopover.Options { minWidth = 200f }));

            pop.Add(Z.Row(menuBtn, flyBtn));
            body.Add(pop);

            root.Add(scroll);
        }

        void Report(string s)
        {
            if (readout != null) readout.text = s;
        }
    }
}
