// ZuiFillControl — the UI Toolkit control that edits a ZuiFill: one labelled header row whose compact body
// changes with the fill's ACTIVE thing, plus (for the richer kinds) a second row of secondary controls, and a
// "⋯" button opening a two-section menu. The menu's top section picks a FILL (Solid / Over life / Linear /
// Radial); a separator; the bottom section picks a TEXTURE (Sprite / Noise / Grid / Dots), which REPLACES the
// fill entirely ("a texture is instead of a fill, not a kind of fill"). Acts as a plain colour picker in Solid
// mode (alpha shown), grows a gradient + a centre pad + angle/zoom in the spatial modes, and swaps to a
// texture's own face when a texture is chosen.
//
// Mirrors ZuiValueControl's conventions: an EXTERNAL label to the left (via FieldLabel), a right-aligned ⋯
// MenuButton, a menu with the ACTIVE item checked, and a full face rebuild on any switch. Operates on the SAME
// runtime ZuiFill the consumer owns; every edit fires OnBeforeMutate → apply → OnChanged (the Undo pair).
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiFillControl : VisualElement
    {
        /// Minimal layout options — mirrors the packed-row / grow surface of ZuiValueControl.Options.
        public class Options
        {
            public float controlWidth = 170f;   // width of the colour/gradient body (packed-row support)
            // Grow to fill the available width up to maxWidthFactor × controlWidth, instead of sitting at a
            // fixed width and leaving a wide row empty. Off by default so callers keep their exact layout.
            public bool grow = false;
            public float maxWidthFactor = 3.2f;

            public Options WithWidth(float w) { controlWidth = w; return this; }
            public Options WithGrow(float maxFactor = 2.4f) { grow = true; maxWidthFactor = maxFactor; return this; }
            public Options Clone() => (Options)MemberwiseClone();
        }

        readonly ZuiFill _fill;
        readonly Options _opt;
        readonly string _label;
        readonly string _tooltip;
        readonly VisualElement _content;

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation (colour edits, gradient edits, angle/zoom drags, mode / texture changes).
        public Action OnChanged;

        public ZuiFillControl(string label, ZuiFill fill, Options options, string tooltip)
        {
            _fill = fill ?? throw new ArgumentNullException(nameof(fill));
            _opt = options ?? new Options();
            _label = label;
            AddToClassList("zui-fill");   // row-level class → uniform bottom spacing (see ZuiToolkit.uss)
            _tooltip = tooltip;
            this.tooltip = tooltip;

            if (_opt.grow)
            {
                style.flexGrow = 1f;
                style.flexShrink = 1f;
                style.minWidth = _opt.controlWidth;
                style.maxWidth = _opt.controlWidth * Mathf.Max(1f, _opt.maxWidthFactor);
            }

            _content = new VisualElement();
            Add(_content);

            RebuildAll();
        }

        void Mutate(Action apply) { OnBeforeMutate?.Invoke(); apply(); OnChanged?.Invoke(); }

        Button MenuButton() => Z.Button("⋯",
            "Choose a FILL (solid colour, over-life, or a spatial gradient) or a TEXTURE (sprite / noise / grid / "
            + "dots). A texture replaces the fill entirely — it's instead of a fill, not a kind of fill.",
            ShowMenu).W(24f);

        Label FieldLabel(string text)
        {
            var l = new Label(text) { tooltip = _tooltip };
            l.AddToClassList("zui-field__label");
            l.style.marginTop = 3f;
            return l;
        }

        // ── full rebuild (on construction and every fill / texture switch) ────────────────────
        void RebuildAll()
        {
            _content.Clear();

            // A texture, when active, REPLACES the fill mode — its own face is drawn instead of the mode faces.
            if (_fill.texture != ZuiFill.TextureKind.None)
            {
                RebuildTextureFace();
                return;
            }

            switch (_fill.mode)
            {
                case ZuiFill.Mode.Solid:
                {
                    // Just a colour picker — alpha shown, filling the row. This IS the "acts as a plain colour
                    // field when neither a fill nor a texture is selected" case (unchanged from v1).
                    var cf = Z.Color(_fill.color, _tooltip,
                        c => Mutate(() => _fill.color = c), _opt.controlWidth, showAlpha: true);
                    AddHeaderRow(_label, cf);
                    break;
                }

                case ZuiFill.Mode.OverLife:
                    AddHeaderRow(_label, BuildGradient(_opt.controlWidth));
                    break;

                case ZuiFill.Mode.Linear:
                    AddHeaderRow(_label, BuildGradient(_opt.controlWidth));
                    ExtraRow(CenterPad(), ExtraAngle(), ExtraZoom(), SpaceField());
                    break;

                case ZuiFill.Mode.Radial:
                    AddHeaderRow(_label, BuildGradient(_opt.controlWidth));
                    ExtraRow(CenterPad(), ExtraZoom(), SpaceField());
                    break;
            }
        }

        // The four texture faces. Each keeps the header row's [label][body][⋯] shape, then a compact second row
        // (wrapping) of that texture's own params.
        void RebuildTextureFace()
        {
            switch (_fill.texture)
            {
                case ZuiFill.TextureKind.Sprite:
                {
                    const string sprTip = "The sprite stamped across the fill's -1..1 box (point-sampled, honouring "
                        + "its rect). Its texture must have Read/Write enabled to sample; otherwise the fill falls "
                        + "back to the tint colour.";
                    var obj = Z.Object<Sprite>(_fill.textureSprite, sprTip,
                        v => Mutate(() => _fill.textureSprite = v), Mathf.Min(_opt.controlWidth, 150f));
                    AddHeaderRow(_label, obj);
                    ExtraRow(TintColor("Tint", "Multiplies the sprite's colours (alpha too). White = the sprite's raw colours."),
                        SpaceField());
                    break;
                }

                case ZuiFill.TextureKind.Noise:
                {
                    AddHeaderRow(_label, BuildGradient(_opt.controlWidth));
                    ExtraRow(NoiseKindField(), ExtraZoom(), CenterPad(), SpaceField());
                    break;
                }

                case ZuiFill.TextureKind.Grid:
                {
                    AddHeaderRow(_label, TintColor(null,
                        "The grid line colour (alpha carries the mask — off-line pixels are transparent)."));
                    ExtraRow(
                        Scrub("Angle", "Rotation of the grid, in degrees.", _fill.gridAngle,
                            v => _fill.gridAngle = v),
                        Scrub("Space", "Cell size in the fill's local units (the -1..1 box is 2 units across).",
                            _fill.gridSpacing, v => _fill.gridSpacing = Mathf.Max(1e-4f, v)),
                        Scrub("Width", "Line thickness as a fraction of the spacing (0..1).", _fill.gridLineWidth,
                            v => _fill.gridLineWidth = Mathf.Clamp01(v)));
                    ExtraRow(
                        Toggle("Vert", "Draw the vertical lines.", _fill.gridVertical, v => _fill.gridVertical = v),
                        Toggle("Horiz", "Draw the horizontal lines.", _fill.gridHorizontal, v => _fill.gridHorizontal = v),
                        SpaceField());
                    break;
                }

                case ZuiFill.TextureKind.Dots:
                {
                    AddHeaderRow(_label, TintColor(null,
                        "The dot colour (alpha carries the mask — the gaps between dots are transparent)."));
                    ExtraRow(
                        Scrub("Size", "Disc diameter as a fraction of the cell (0..1+).", _fill.dotSize,
                            v => _fill.dotSize = Mathf.Max(0f, v)),
                        Scrub("Space", "Cell size in the fill's local units (the -1..1 box is 2 units across).",
                            _fill.dotSpacing, v => _fill.dotSpacing = Mathf.Max(1e-4f, v)),
                        Toggle("Stagger", "Offset alternate rows by half a cell (a brick / hex pattern).",
                            _fill.dotStagger, v => _fill.dotStagger = v),
                        SpaceField());
                    break;
                }
            }
        }

        void AddHeaderRow(string label, VisualElement body)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.style.alignItems = Align.FlexStart;
            if (!string.IsNullOrEmpty(label)) row.Add(FieldLabel(label));
            if (_opt.grow) { body.style.flexGrow = 1f; body.style.flexShrink = 1f; }
            else body.style.flexShrink = 0f;
            row.Add(body);
            row.Add(MenuButton());
            _content.Add(row);
        }

        // A compact, wrapping second row of secondary params (pad / angle / zoom / toggles). Grows nothing —
        // each child sizes itself — and wraps to a further line on a narrow pane (ui-layout-rules: pack rows,
        // but a control may grow a second row rather than overflow).
        void ExtraRow(params VisualElement[] kids)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.FlexStart;
            foreach (var k in kids) if (k != null) row.Add(k);
            _content.Add(row);
        }

        GradientField BuildGradient(float width)
        {
            _fill.EnsureGradient();
            // get/set form re-reads the live gradient — the Undo-restored / paste-swapped instance shows up.
            return Z.Gradient(_tooltip,
                () => _fill.gradient,
                g => Mutate(() => _fill.gradient = g ?? ZuiFill.DefaultGradient()),
                width);
        }

        // The gradient centre pad (Linear / Radial / Noise) — a plain Vector2 in -1..1 local space. flipY so
        // dragging up raises the centre's v, matching the renderer's v-up sampling.
        VisualElement CenterPad()
        {
            const string tip = "The gradient's centre in the shape's local space (-1..1). Linear: the fill axis "
                + "passes through it. Radial: the gradient's middle sits here, drifting off-centre toward a border.";
            var pad = Z.Pad(_fill.center, new Rect(-1f, -1f, 2f, 2f), tip,
                v => Mutate(() => _fill.center = v), 44f);
            return Z.Field("Ctr", tip, pad);
        }

        VisualElement ExtraAngle()
        {
            const string tip = "Rotation of the linear fill axis, in degrees.";
            var f = Z.Float(_fill.angleDeg, tip, v => Mutate(() => _fill.angleDeg = v), 42f);
            return Z.Field("Ang", tip, f);
        }

        VisualElement ExtraZoom()
        {
            const string tip = "Spatial scale of the fill — higher zooms the pattern in (min 0.05).";
            var f = Z.Float(_fill.zoom, tip, v => Mutate(() => _fill.zoom = Mathf.Max(0.05f, v)), 42f);
            return Z.Field("Zoom", tip, f);
        }

        VisualElement NoiseKindField()
        {
            const string tip = "Noise shape: Value (plain), Ridged (creased ridges), or Steps (4-band posterized). "
                + "All three map through the gradient.";
            var d = Z.EnumDropdown(_fill.noiseKind, tip, v => Mutate(() => _fill.noiseKind = v), 74f);
            return Z.Field("Kind", tip, d);
        }

        static readonly string[] SpaceLabels = { "Stamped", "Fixed" };

        // The fill's coordinate SPACE (see ZuiFill.FillSpace) — shown only for the SPATIAL kinds (Linear / Radial
        // gradients and every texture); a Solid / Over-life fill ignores (u,v), so it never appears there. Stamped
        // (the default) locks the pattern to the SHAPE (it rotates / spins / travels WITH it); Fixed pins it to the
        // CANVAS, so the shape moves THROUGH a stationary pattern (a mask / window). Labelled "Anchor", NOT "Space",
        // to avoid colliding with the Grid/Dots cell-spacing scrub that is already captioned "Space".
        VisualElement SpaceField()
        {
            const string tip = "Where the pattern is anchored. Stamped = it sticks to the shape and rotates / spins / "
                + "travels WITH it (stamped on). Fixed = it's pinned to the canvas, so the shape slides THROUGH a "
                + "stationary pattern (a mask/window). Only affects spatial gradients and textures.";
            var seg = Z.Segmented((int)_fill.space, SpaceLabels, tip,
                v => Mutate(() => _fill.space = (ZuiFill.FillSpace)v));
            return Z.Field("Anchor", tip, seg);
        }

        // A colour field bound to _fill.color (the Solid swatch / Sprite tint / Grid+Dots ink). `caption` null =
        // no leading label (the header FieldLabel already names the row).
        VisualElement TintColor(string caption, string tip)
        {
            var cf = Z.Color(_fill.color, tip, c => Mutate(() => _fill.color = c),
                caption == null ? _opt.controlWidth : 60f, showAlpha: true);
            return caption == null ? (VisualElement)cf : Z.Field(caption, tip, cf);
        }

        // A compact scrub-float param wrapped with its own caption — the packed idiom the angle/zoom fields use.
        VisualElement Scrub(string caption, string tip, float value, Action<float> set)
        {
            var f = Z.Float(value, tip, v => Mutate(() => set(v)), 42f);
            return Z.Field(caption, tip, f);
        }

        VisualElement Toggle(string caption, string tip, bool value, Action<bool> set)
            => Z.Toggle(caption, tip, value, v => Mutate(() => set(v)));

        // ── the two-section ⋯ menu (Fill section, separator, Texture section — a texture replaces the fill) ──
        void ShowMenu()
        {
            var menu = new GenericMenu();
            // Fill section — a fill item is active (checked) when NO texture is set and this is the current mode.
            AddFillItem(menu, "Solid colour", ZuiFill.Mode.Solid);
            AddFillItem(menu, "Over life", ZuiFill.Mode.OverLife);
            AddFillItem(menu, "Linear gradient", ZuiFill.Mode.Linear);
            AddFillItem(menu, "Radial gradient", ZuiFill.Mode.Radial);
            menu.AddSeparator("");   // divides the Fill section (above) from the Texture section (below)
            // Texture section — a texture REPLACES the fill; active when this kind is set (the mode is ignored).
            AddTextureItem(menu, "Texture · Sprite", ZuiFill.TextureKind.Sprite);
            AddTextureItem(menu, "Texture · Noise", ZuiFill.TextureKind.Noise);
            AddTextureItem(menu, "Texture · Grid", ZuiFill.TextureKind.Grid);
            AddTextureItem(menu, "Texture · Dots", ZuiFill.TextureKind.Dots);
            menu.ShowAsContext();
        }

        void AddFillItem(GenericMenu menu, string label, ZuiFill.Mode mode)
            => menu.AddItem(new GUIContent(label),
                _fill.texture == ZuiFill.TextureKind.None && _fill.mode == mode,
                () => SetFill(mode));

        void AddTextureItem(GenericMenu menu, string label, ZuiFill.TextureKind kind)
            => menu.AddItem(new GUIContent(label), _fill.texture == kind, () => SetTexture(kind));

        // Pick a FILL: clear any texture, set the mode, seed a gradient for the non-Solid modes.
        void SetFill(ZuiFill.Mode mode)
        {
            Mutate(() =>
            {
                _fill.texture = ZuiFill.TextureKind.None;
                _fill.mode = mode;
                if (mode != ZuiFill.Mode.Solid) _fill.EnsureGradient();   // seed so the field is never blank
            });
            RebuildAll();
        }

        // Pick a TEXTURE: set the kind (the mode is left as-is but ignored while a texture is active). Noise maps
        // through the gradient, so seed one.
        void SetTexture(ZuiFill.TextureKind kind)
        {
            Mutate(() =>
            {
                _fill.texture = kind;
                if (kind == ZuiFill.TextureKind.Noise) _fill.EnsureGradient();
            });
            RebuildAll();
        }
    }
}
