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
        FillSwatch _swatch;   // live preview square, rebuilt per face; null for a plain Solid fill (no swatch)

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

        void Mutate(Action apply)
        {
            OnBeforeMutate?.Invoke();
            apply();
            _swatch?.Refresh();   // keep the preview live while a colour / gradient / param is being tweaked
            OnChanged?.Invoke();
        }

        // A swatch is worth showing for every spatial / gradient / texture fill — its pattern isn't visible
        // anywhere else. A plain Solid fill is redundant (the colour field already shows it), so no swatch.
        bool WantSwatch() => _fill.texture != ZuiFill.TextureKind.None || _fill.mode != ZuiFill.Mode.Solid;

        Button MenuButton()
        {
            Button btn = null;
            btn = Z.Button("⋯",
                "Choose a FILL (solid colour, over-life, or a spatial gradient) or a TEXTURE (sprite / noise / grid / "
                + "dots). A texture replaces the fill entirely — it's instead of a fill, not a kind of fill.",
                () => ShowMenu(btn)).W(24f);
            return btn;
        }

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
            _swatch = null;   // the old one detached with _content.Clear() (it destroys its own texture then)
            _fill.EnsureSpatialAnim();   // seed the animatable zoom/centre companions from the legacy scalars if unset

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
                    // Just the gradient (sampled over life) — no placement, so no divider: the header names it
                    // and the collapsible gradient section sits directly beneath.
                    AddHeaderRow(_label, null);
                    _content.Add(GradientControl());
                    break;

                case ZuiFill.Mode.Linear:
                    AddHeaderRow(_label, null);
                    _content.Add(GradientControl());
                    _content.Add(Z.Divider("Placement", PlacementTip));
                    ExtraRow(ExtraAngle(), SpaceField());
                    _content.Add(ZoomVal());
                    _content.Add(CenterVal());
                    break;

                case ZuiFill.Mode.Radial:
                    AddHeaderRow(_label, null);
                    _content.Add(GradientControl());
                    _content.Add(Z.Divider("Placement", PlacementTip));
                    ExtraRow(SpaceField());
                    _content.Add(ZoomVal());
                    _content.Add(CenterVal());
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
                    AddHeaderRow(_label, null);
                    _content.Add(GradientControl());
                    _content.Add(Z.Divider("Pattern", "The noise shape, and how it's anchored / scaled / centred."));
                    ExtraRow(NoiseKindField(), SpaceField());
                    _content.Add(ZoomVal());
                    _content.Add(CenterVal());
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
            // A live preview square, left of the label, showing the FILL ITSELF (no shape) so the author sees
            // the gradient / noise / texture / grid while tweaking it. Only for non-Solid fills (see WantSwatch).
            if (WantSwatch())
            {
                _swatch = new FillSwatch(_fill, 44f,
                    "Live preview of the fill's own pattern (no shape). "
                    + "Over-life shows left→right over the particle's life; spatial modes show the -1..1 fill box.");
                row.Add(_swatch);
            }
            if (!string.IsNullOrEmpty(label)) row.Add(FieldLabel(label));
            if (body != null)
            {
                if (_opt.grow) { body.style.flexGrow = 1f; body.style.flexShrink = 1f; }
                else body.style.flexShrink = 0f;
                row.Add(body);
            }
            // No inline body → the gradient sits in its OWN section below (see RebuildAll's gradient modes); a
            // flexible spacer keeps the ⋯ pinned to the right so the header still reads as [swatch][label][⋯].
            else row.Add(Z.Flexible());
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

        // Caption for the divider that separates the gradient section from the fill's spatial-placement controls.
        const string PlacementTip = "How the gradient is anchored, scaled and centred across the shape.";

        // The gradient's own SECTION: the shared ZuiGradientControl — a live preview strip that stays visible
        // even when its base gradient + transform knobs are collapsed (the envelope-style collapse-with-preview,
        // implemented once in that control). This is the ONE gradient editor every fill mode reuses, replacing
        // the old raw GradientField that could only edit the base ramp and left the ZuiGradient's transform knobs
        // (reverse / hue / sat / brightness / contrast / quantise) unauthorable even though the runtime samples
        // them (see ZuiFill.EvalGrad). It edits _fill.gradientAnim in place; a full rebuild (on undo / mode swap)
        // reconstructs it against the live instance, so an Undo-restored / paste-swapped gradient shows up.
        ZuiGradientControl GradientControl()
        {
            _fill.EnsureGradient();
            _fill.EnsureGradientAnim();   // edit the ZuiGradient companion going forward (legacy stays frozen)
            return new ZuiGradientControl(_fill.gradientAnim, _tooltip)
            {
                OnBeforeMutate = () => OnBeforeMutate?.Invoke(),
                OnChanged = () => { _swatch?.Refresh(); OnChanged?.Invoke(); },
            };
        }

        // The gradient centre (Linear / Radial / Noise) — an ANIMATABLE synced XY pair (ZUIValue centerXAnim /
        // centerYAnim) in -1..1 local space, so an author can curve the centre over the fill's life. A Static pair
        // reproduces the legacy `center` exactly (byte-identical). Same Undo/refresh wiring as every other edit.
        VisualElement CenterVal()
        {
            const string tip = "The gradient's centre in the shape's local space (-1..1), over the particle's life. "
                + "Linear: the fill axis passes through it. Radial: the gradient's middle sits here, drifting "
                + "off-centre toward a border. Static holds it; switch to a Curve (⋯) to animate the centre.";
            var o = new ZuiValue2DControl.Options().WithRange(-1f, 1f, -1f, 1f).WithDefault(Vector2.zero);
            return Z.Value2D("Ctr", _fill.centerXAnim, _fill.centerYAnim, o, tip,
                () => { _swatch?.Refresh(); OnChanged?.Invoke(); },
                () => OnBeforeMutate?.Invoke());
        }

        VisualElement ExtraAngle()
        {
            const string tip = "Rotation of the linear fill axis, in degrees.";
            var f = Z.Float(_fill.angleDeg, tip, v => Mutate(() => _fill.angleDeg = v), 42f);
            return Z.Field("Ang", tip, f);
        }

        // Zoom (Linear proj scale / Radial / Noise) — an ANIMATABLE ZUIValue (zoomAnim), so an author can curve the
        // spatial scale over the fill's life. A Static value reproduces the legacy `zoom` exactly (byte-identical).
        VisualElement ZoomVal()
        {
            const string tip = "Spatial scale of the fill over the particle's life — higher zooms the pattern in "
                + "(min 0.05). Static holds it; switch to a Curve (⋯) to animate the zoom.";
            var o = new ZuiValueControl.Options
            {
                absMin = 0.05f, absMax = 10f,
                hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                controlWidth = _opt.controlWidth, grow = _opt.grow,
            };
            return Z.Value("Zoom", _fill.zoomAnim, o, tip,
                () => { _swatch?.Refresh(); OnChanged?.Invoke(); },
                () => OnBeforeMutate?.Invoke());
        }

        static readonly string[] NoiseKindLabels = { "Value", "Ridged", "Steps" };

        VisualElement NoiseKindField()
        {
            const string tip = "Noise shape: Value (plain), Ridged (creased ridges), or Steps (4-band posterized). "
                + "All three map through the gradient.";
            // Wrapped radio, not a native dropdown (ui-layout-rules: enum → radio/segmented) — and it matches the
            // Segmented Anchor field packed beside it in the same noise row.
            var seg = Z.Segmented((int)_fill.noiseKind, NoiseKindLabels, tip,
                v => Mutate(() => _fill.noiseKind = (ZuiFill.NoiseKind)v));
            return Z.Field("Kind", tip, seg);
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
        // A ZUI popover anchored to the ⋯ button: the old flat GenericMenu's separator becomes two Section
        // headings, and the "Texture · " label prefix drops (the "Texture" heading now names that group).
        void ShowMenu(VisualElement anchor)
        {
            var menu = Z.Menu(anchor);
            // Fill section — a fill item is active (checked) when NO texture is set and this is the current mode.
            menu.Section("Fill");
            AddFillItem(menu, "Solid colour", "A single flat colour (alpha shown).", ZuiFill.Mode.Solid);
            AddFillItem(menu, "Over life", "A gradient sampled left→right over the particle's life.", ZuiFill.Mode.OverLife);
            AddFillItem(menu, "Linear gradient", "A gradient projected along an angled axis across the shape.", ZuiFill.Mode.Linear);
            AddFillItem(menu, "Radial gradient", "A gradient radiating out from a centre point.", ZuiFill.Mode.Radial);
            menu.Separator();   // divides the Fill section (above) from the Texture section (below)
            // Texture section — a texture REPLACES the fill; active when this kind is set (the mode is ignored).
            menu.Section("Texture");
            AddTextureItem(menu, "Sprite", "Stamp a sprite across the fill box.", ZuiFill.TextureKind.Sprite);
            AddTextureItem(menu, "Noise", "A procedural noise pattern mapped through the gradient.", ZuiFill.TextureKind.Noise);
            AddTextureItem(menu, "Grid", "A ruled grid of lines.", ZuiFill.TextureKind.Grid);
            AddTextureItem(menu, "Dots", "A dot / halftone pattern.", ZuiFill.TextureKind.Dots);
            menu.Show();
        }

        void AddFillItem(ZuiMenu menu, string label, string tooltip, ZuiFill.Mode mode)
            => menu.Item(label, tooltip, () => SetFill(mode),
                @checked: _fill.texture == ZuiFill.TextureKind.None && _fill.mode == mode);

        void AddTextureItem(ZuiMenu menu, string label, string tooltip, ZuiFill.TextureKind kind)
            => menu.Item(label, tooltip, () => SetTexture(kind), @checked: _fill.texture == kind);

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

        // ── live fill-preview swatch (editor-only) ────────────────────────────────────────────
        // A small square that renders the fill's OWN pattern with no shape, by sampling ZuiFill.Evaluate the
        // way the renderer does (u,v across the -1..1 box; life across x for the over-life gradient). Backed by
        // a Texture2D it rebuilds on demand and destroys when it leaves the panel (so a rebuild never leaks).
        sealed class FillSwatch : VisualElement
        {
            readonly ZuiFill _fill;
            readonly int _px;
            Texture2D _tex;
            Color32[] _buf;

            public FillSwatch(ZuiFill fill, float size, string tooltip)
            {
                _fill = fill;
                _px = Mathf.Max(8, Mathf.RoundToInt(size));   // 1 texel per display px is plenty at this size
                this.tooltip = tooltip;
                style.width = size;
                style.height = size;
                style.flexShrink = 0f;
                style.marginRight = 5f;
                style.marginTop = 1f;
                // A hairline border so the square's bounds read even when the fill is transparent at the edges.
                var bc = new Color(0f, 0f, 0f, 0.4f);
                style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1f;
                style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = bc;
                style.borderTopLeftRadius = style.borderTopRightRadius =
                    style.borderBottomLeftRadius = style.borderBottomRightRadius = 2f;
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    if (_tex != null) { UnityEngine.Object.DestroyImmediate(_tex); _tex = null; }
                });
                Refresh();
            }

            public void Refresh()
            {
                if (_tex == null)
                {
                    _tex = new Texture2D(_px, _px, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                    _buf = new Color32[_px * _px];
                }
                // Over-life is the one mode Evaluate reads via `life`, not (u,v): show it left→right over life.
                bool overLife = _fill.texture == ZuiFill.TextureKind.None && _fill.mode == ZuiFill.Mode.OverLife;
                for (int y = 0; y < _px; y++)
                {
                    // Texture2D row 0 is the BOTTOM; map it to v = -1 so up on screen is +v (the pads' flipY).
                    float fy = _px == 1 ? 0f : y / (float)(_px - 1);
                    float v = Mathf.Lerp(-1f, 1f, fy);
                    for (int x = 0; x < _px; x++)
                    {
                        float fx = _px == 1 ? 0f : x / (float)(_px - 1);
                        _buf[y * _px + x] = overLife ? _fill.Evaluate(fx, 0f, 0f)
                                                     : _fill.Evaluate(0f, Mathf.Lerp(-1f, 1f, fx), v);
                    }
                }
                _tex.SetPixels32(_buf);
                _tex.Apply(false);
                style.backgroundImage = Background.FromTexture2D(_tex);
            }
        }
    }
}
