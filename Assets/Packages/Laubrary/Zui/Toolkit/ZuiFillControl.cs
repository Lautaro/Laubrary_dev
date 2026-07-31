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
    /// What SHAPE the consumer actually paints this fill onto, so the swatch outlines the right thing. A text
    /// line is a Box; a blast, a disc or a radial burst is a Circle, and outlining a square around one would
    /// misdescribe where the fill lands as badly as showing nothing.
    public enum ZuiSubjectShape { Box, Circle }

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

            // Show the Fit dial (Uniform / Stretch) on spatial fills. OFF by default and deliberately opt-in:
            // `fit` only means something to a consumer that normalizes through ZuiFill.Normalize, and a dial that
            // does nothing in the tool you are looking at is worse than no dial. Turn it on in a tool that
            // honours it — TextSplash does.
            public bool showFit = false;

            // The half-extents of the box the consumer will actually sample this fill over — a text line's box, a
            // sprite's box. When set, the swatch draws that box as an OUTLINE on top of the fill's own -1..1
            // domain, which is the one picture that makes `space`, `fit`, `zoom` and `centre` legible: you see the
            // gradient's real size AND how much of it the subject actually covers. Zero (the default) means "the
            // consumer did not say", and the swatch falls back to showing the domain alone as it always did.
            public Vector2 subjectHalf = Vector2.zero;

            // Whether that box is the subject itself (a text line) or merely bounds it (a disc, a blast).
            public ZuiSubjectShape subjectShape = ZuiSubjectShape.Box;

            // The box ONE REPETITION covers when `space` is Stamped — one glyph, one swarm particle — because a
            // stamped fill is normalized against that unit, not against the whole subject. Without it the swatch
            // would outline the whole line while the renderer was actually fitting the gradient onto each letter,
            // which is precisely the question the picture exists to answer. Zero = the consumer has no smaller
            // unit (its stamped and fixed boxes are the same thing), and the subject values above are used.
            public Vector2 stampedHalf = Vector2.zero;
            public ZuiSubjectShape stampedShape = ZuiSubjectShape.Box;

            public Options WithWidth(float w) { controlWidth = w; return this; }
            public Options WithFit() { showFit = true; return this; }
            public Options WithSubject(Vector2 half, ZuiSubjectShape shape = ZuiSubjectShape.Box)
            { subjectHalf = half; subjectShape = shape; return this; }
            public Options WithStamped(Vector2 half, ZuiSubjectShape shape = ZuiSubjectShape.Box)
            { stampedHalf = half; stampedShape = shape; return this; }
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
            // No ⋯ button — the fill/texture menu opens on right-click; hint it on the row tooltip.
            this.tooltip = string.IsNullOrEmpty(tooltip) ? "Right-click to choose a fill / texture." : tooltip + "  (right-click to choose fill / texture)";

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
            // Right-click anywhere on the control opens the fill/texture menu (matches ZuiValueControl — no ⋯
            // button). Bubble phase, so a child that owns its own right-click still wins on itself.
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return;
                ShowMenu(this);
                e.StopPropagation();
            });
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
                    _content.Add(GradientSection(null));
                    break;

                case ZuiFill.Mode.Linear:
                    _content.Add(GradientSection(
                        Z.Box("Placement", PlacementTip,
                            MakeRow(AngleSlider("Ang", "Rotation of the linear fill axis, in degrees.", _fill.angleDeg, v => _fill.angleDeg = v),
                                    SpaceField()),
                            FitField(), ZoomVal(), CenterVal())));
                    break;

                case ZuiFill.Mode.Radial:
                    _content.Add(GradientSection(
                        Z.Box("Placement", PlacementTip, MakeRow(SpaceField()), FitField(),
                              ZoomVal(), CenterVal())));
                    break;
            }
        }

        // A gradient fill as a bounded, COLLAPSIBLE section whose HEADER stays visible when folded (per the mockup):
        //   header row : [square][Fill label][OBJECTIVE output strip][⋯]   — always visible
        //   source row : the editable SOURCE ramp                          — always visible
        //   body (fold): the "Adjust" transforms box + the Placement box   — hides when collapsed
        // So the square + label + BOTH ramps (objective output + editable source) stay on screen even collapsed,
        // and only the editing controls fold away. Uses the shared ZuiGradientEditor pieces + ZuiFoldCard (a fold
        // that keeps a custom header, unlike ZuiBox's whole-body title-fold).
        VisualElement GradientSection(VisualElement placement)
        {
            var ed = new ZuiGradientEditor(_fill.gradientAnim ?? SeedGradientAnim(), _tooltip)
            {
                OnBeforeMutate = () => OnBeforeMutate?.Invoke(),
                OnChanged = () => { _swatch?.Refresh(); OnChanged?.Invoke(); },
            };

            var box = Z.Box(null, null);   // untitled bordered card — the section boundary; the header names it

            // Header, per the mockup:
            //   row 1 : [tall square] [Fill label] [OBJECTIVE output strip →] [⋯]
            //   row 2 :               [ editable SOURCE ramp → (full column width) ]
            // The square spans both rows on the left; "Fill" + the objective ramp share the top row (the ⋯ pinned
            // right); the editable source ramp fills the row beneath. All of it stays visible when the body folds,
            // and NO fold caret is drawn (the header itself folds on click — per the #2 feedback).
            var header = new VisualElement();
            header.AddToClassList("zui-row");
            header.style.alignItems = Align.Stretch;   // let the square stretch to the two-row column height
            if (WantSwatch())
            {
                _swatch = new FillSwatch(_fill, 46f, SwatchTip(), _opt);
                _swatch.style.height = StyleKeyword.Auto;   // stretch drives the height so it spans both ramp rows
                _swatch.style.alignSelf = Align.Stretch;
                header.Add(_swatch);
            }
            var col = new VisualElement();
            col.style.flexGrow = 1f;
            col.style.flexDirection = FlexDirection.Column;

            // Row 1: label · objective output strip (grows)
            var row1 = new VisualElement();
            row1.AddToClassList("zui-row");
            row1.style.alignItems = Align.Center;
            row1.Add(FieldLabel(_label ?? "Fill"));
            ed.Output.style.flexGrow = 1f; ed.Output.style.flexShrink = 1f;
            ed.Output.style.marginLeft = 6f; ed.Output.style.marginRight = 6f;
            row1.Add(ed.Output);   // objective output preview (read-only)
            col.Add(row1);

            // Row 2: the editable SOURCE ramp, full column width.
            ed.Source.style.flexGrow = 1f; ed.Source.style.flexShrink = 1f;
            ed.Source.style.marginTop = 3f;
            col.Add(ed.Source);

            header.Add(col);
            box.Add(header);

            var body = new VisualElement();
            body.Add(ed.Adjust);
            if (placement != null) body.Add(placement);
            box.Add(body);

            // Fold the body (Adjust + Placement) from the header — no caret (#2). The editable source ramp must
            // never fold (right-click the header opens the mode/texture menu). Keyed per-fill so it persists across rebuilds.
            ZuiFoldCard.Wire(_fill, header, body, showCaret: false, ed.Source);
            return box;
        }

        // A ZuiGradient companion is required for the editor; seed one from the legacy gradient if the migration
        // hasn't run yet (mirrors ZuiFill.EnsureGradientAnim, which EvalGrad also falls back through).
        ZuiGradient SeedGradientAnim()
        {
            _fill.EnsureGradient();
            _fill.EnsureGradientAnim();
            return _fill.gradientAnim;
        }

        // The whole fill/gradient as ONE titled bordered SECTION (named by the fill's label), so it is clear where it
        // starts and ends — a host stacking its own dials (Alpha / Size / …) after this control reads them as
        // separate, not as more of the fill. The box's first row carries the live preview swatch + the ⋯ mode/texture
        // menu; the mode content (the gradient with its "Adjust" transforms sub-box, and a "Placement" sub-box) fills
        // the rest. Only for the multi-control modes — a Solid fill is one colour field and stays inline (unboxed).
        ZuiBox SectionBox()
        {
            var box = Z.Box(string.IsNullOrEmpty(_label) ? "Fill" : _label, _tooltip);
            var top = new VisualElement();
            top.AddToClassList("zui-row");
            top.style.alignItems = Align.FlexStart;
            if (WantSwatch())
            {
                _swatch = new FillSwatch(_fill, 44f, SwatchTip(), _opt);
                top.Add(_swatch);
            }
            box.Add(top);
            return box;
        }

        // The four texture faces — each in the SAME titled SectionBox as a gradient fill (a clearly-bounded unit),
        // then that texture's own params inside it.
        void RebuildTextureFace()
        {
            var box = SectionBox();
            switch (_fill.texture)
            {
                case ZuiFill.TextureKind.Sprite:
                {
                    const string sprTip = "The sprite stamped across the fill's -1..1 box (point-sampled, honouring "
                        + "its rect). Its texture must have Read/Write enabled to sample; otherwise the fill falls "
                        + "back to the tint colour.";
                    var obj = Z.Object<Sprite>(_fill.textureSprite, sprTip,
                        v => Mutate(() => _fill.textureSprite = v), Mathf.Min(_opt.controlWidth, 150f));
                    box.Add(Z.Field("Sprite", sprTip, obj));
                    box.Add(MakeRow(TintColor("Tint", "Multiplies the sprite's colours (alpha too). White = the sprite's raw colours."),
                        SpaceField()));
                    break;
                }

                case ZuiFill.TextureKind.Noise:
                {
                    box.Add(GradientControl());
                    box.Add(Z.Box("Pattern", "The noise shape, and how it's anchored / scaled / centred.",
                        MakeRow(NoiseKindField(), SpaceField()), ZoomVal(), CenterVal()));
                    break;
                }

                case ZuiFill.TextureKind.Grid:
                {
                    box.Add(MakeRow(
                        TintColor("Ink", "The grid line colour (alpha carries the mask — off-line pixels are transparent)."),
                        SpaceField()));
                    box.Add(AngleSlider("Angle", "Rotation of the grid, in degrees.", _fill.gridAngle,
                        v => _fill.gridAngle = v));
                    box.Add(MakeRow(
                        Scrub("Space", "Cell size in the fill's local units (the -1..1 box is 2 units across).",
                            _fill.gridSpacing, v => _fill.gridSpacing = Mathf.Max(1e-4f, v)),
                        Scrub("Width", "Line thickness as a fraction of the spacing (0..1).", _fill.gridLineWidth,
                            v => _fill.gridLineWidth = Mathf.Clamp01(v))));
                    box.Add(MakeRow(
                        Toggle("Vert", "Draw the vertical lines.", _fill.gridVertical, v => _fill.gridVertical = v),
                        Toggle("Horiz", "Draw the horizontal lines.", _fill.gridHorizontal, v => _fill.gridHorizontal = v)));
                    break;
                }

                case ZuiFill.TextureKind.Dots:
                {
                    box.Add(MakeRow(
                        TintColor("Ink", "The dot colour (alpha carries the mask — the gaps between dots are transparent)."),
                        SpaceField()));
                    box.Add(MakeRow(
                        Scrub("Size", "Disc diameter as a fraction of the cell (0..1+).", _fill.dotSize,
                            v => _fill.dotSize = Mathf.Max(0f, v)),
                        Scrub("Space", "Cell size in the fill's local units (the -1..1 box is 2 units across).",
                            _fill.dotSpacing, v => _fill.dotSpacing = Mathf.Max(1e-4f, v)),
                        Toggle("Stagger", "Offset alternate rows by half a cell (a brick / hex pattern).",
                            _fill.dotStagger, v => _fill.dotStagger = v)));
                    break;
                }
            }
            _content.Add(box);
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
                _swatch = new FillSwatch(_fill, 44f, SwatchTip(), _opt);
                row.Add(_swatch);
            }
            if (!string.IsNullOrEmpty(label)) row.Add(FieldLabel(label));
            if (body != null)
            {
                if (_opt.grow) { body.style.flexGrow = 1f; body.style.flexShrink = 1f; }
                else body.style.flexShrink = 0f;
                row.Add(body);
            }
            // No inline body → the gradient sits in its OWN section below (see RebuildAll's gradient modes).
            _content.Add(row);
        }

        // A compact, wrapping second row of secondary params (pad / angle / zoom / toggles). Grows nothing —
        // each child sizes itself — and wraps to a further line on a narrow pane (ui-layout-rules: pack rows,
        // but a control may grow a second row rather than overflow).
        VisualElement MakeRow(params VisualElement[] kids)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.FlexStart;
            foreach (var k in kids) if (k != null) row.Add(k);
            return row;
        }

        void ExtraRow(params VisualElement[] kids) => _content.Add(MakeRow(kids));

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
            const string tip = "The gradient's centre, in the SUBJECT BOX's own -1..1 space — the box outlined on "
                + "the swatch. Linear: the fill axis passes through it. Radial: the gradient's middle sits here. "
                + "Under Uniform fit both axes are scaled by the box's LONGER side, so on a wide subject a small "
                + "vertical offset moves a long way; Stretch makes one unit mean one half-box on each axis. "
                + "Static holds it; switch to a Curve (⋯) to animate it over the fill's life.";
            var o = new ZuiValue2DControl.Options().WithRange(-1f, 1f, -1f, 1f).WithDefault(Vector2.zero);
            return Z.Value2D("Centre", _fill.centerXAnim, _fill.centerYAnim, o, tip,
                () => { _swatch?.Refresh(); OnChanged?.Invoke(); },
                () => OnBeforeMutate?.Invoke());
        }

        // A rotation-angle MicroSlider (0..360°, whole degrees) — replaces the old scrub Z.Float per the UI guide
        // (a bounded scalar belongs in the track, not a bare number field). Shared by the Linear axis + the Grid.
        VisualElement AngleSlider(string caption, string tip, float value, Action<float> set)
            => Z.MicroSlider(caption, value, 0f, 360f, tip, v => Mutate(() => set(v)), 150f, decimals: 0);

        // Zoom (Linear proj scale / Radial / Noise) — an ANIMATABLE ZUIValue (zoomAnim), so an author can curve the
        // spatial scale over the fill's life. A Static value reproduces the legacy `zoom` exactly (byte-identical).
        VisualElement ZoomVal()
        {
            const string tip = "How BIG the pattern is: higher spreads the gradient further, lower packs it "
                + "tighter around the centre. 1 = the ramp spans the whole -1..1 fill box. Static holds it; "
                + "switch to a Curve (⋯) to animate it over the fill's life.";
            var o = new ZuiValueControl.Options
            {
                absMin = 0.05f, absMax = 10f,
                hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                controlWidth = _opt.controlWidth, grow = _opt.grow,
            };
            return Z.Value("Size", _fill.zoomAnim, o, tip,
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

        /// <summary>The Fit dial: where the ramp's far end lands on a box that is not square. Returns an empty
        /// element unless the hosting tool opted in — see <see cref="Options.showFit"/>.
        ///
        /// The tooltip states the CONSEQUENCE rather than the mechanism, because the mechanism (which half-extent
        /// divides the coordinates) is not something an author should have to hold in their head to explain why
        /// only a slice of their gradient is showing.</summary>
        VisualElement FitField()
        {
            if (!_opt.showFit) return new VisualElement();

            // Restated in place rather than by rebuilding: Mutate does not rebuild the control, so a tooltip
            // captured once would go on describing the mode the author just switched AWAY from.
            ZuiSegmented seg = null;
            Label label = null;
            VisualElement field = null;

            void Restate()
            {
                string t = FitTip();
                if (seg != null) seg.tooltip = t;
                if (label != null) label.tooltip = t;
                if (field != null) field.tooltip = t;
            }

            seg = Z.Segmented((int)_fill.fit, FitLabels, FitTip(),
                v => { Mutate(() => _fill.fit = (ZuiFill.FillFit)v); Restate(); });
            field = Z.Field("Fit", FitTip(), seg);
            label = field.Q<Label>(className: "zui-field__label");
            return field;
        }

        string FitTip() => _fill.fit == ZuiFill.FillFit.Stretch
            ? "Fit: STRETCH — the ramp runs end to end along BOTH axes, so a gradient is fully used whichever way "
            + "it points. A Radial fill becomes an ellipse fitted to the box. Right when the BOX is the subject, "
            + "e.g. a wide line of text."
            : "Fit: UNIFORM — one scale for both axes, so a Radial fill stays a true circle. The SHORT axis never "
            + "reaches the end of the ramp: on a line 5.6x wider than it is tall, a vertical gradient shows only "
            + "the middle 18% of your colours. Switch to Stretch if you authored a ramp and can only see part "
            + "of it.";

        static readonly string[] FitLabels = { "Uniform", "Stretch" };

        /// The swatch's tooltip. It has to explain the OUTLINE when there is one, because a rectangle drawn over
        /// a gradient is meaningless until you know it is the subject's own box.
        string SwatchTip()
        {
            bool spatial = _fill.texture != ZuiFill.TextureKind.None
                           || (_fill.mode != ZuiFill.Mode.Solid && _fill.mode != ZuiFill.Mode.OverLife);
            bool stamped = _fill.space == ZuiFill.FillSpace.Stamped
                           && _opt.stampedHalf.x > 0f && _opt.stampedHalf.y > 0f;
            Vector2 named = stamped ? _opt.stampedHalf : _opt.subjectHalf;
            if (!spatial || named.x <= 0f || named.y <= 0f)
                return "Live preview of the fill's own pattern (no shape). Over-life shows left→right over its "
                     + "life; spatial modes show the -1..1 fill box.";

            return "Live preview of the fill over its whole -1..1 box, with the OUTLINE showing where "
                 + (stamped ? "ONE REPETITION sits — Anchor is Stamped, so the gradient is fitted onto each "
                            + "letter/particle separately, and the outline is one of them."
                            : "your whole subject sits in it.")
                 + " A thin outline across the middle means most of the gradient falls outside it and you only see "
                 + "the slice inside — switch Fit to Stretch, or raise Size, to bring the ramp into it.";
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
            AddFillItem(menu, "Over life", "A gradient sampled left→right over the subject's life.", ZuiFill.Mode.OverLife);
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
            readonly Options _opt;           // holds both the whole-subject and the stamped-unit boxes
            Texture2D _tex;
            Color32[] _buf;

            public FillSwatch(ZuiFill fill, float size, string tooltip, Options opt = null)
            {
                _fill = fill;
                _opt = opt ?? new Options();
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
                if (!overLife) DrawSubjectBox();

                _tex.SetPixels32(_buf);
                _tex.Apply(false);
                style.backgroundImage = Background.FromTexture2D(_tex);
            }

            /// <summary>Outline where the consumer's box lands inside the fill's own -1..1 domain.
            ///
            /// The corners come from <see cref="ZuiFill.Normalize"/> — the SAME call the renderer makes — so this
            /// outline cannot drift from what actually gets painted, and it moves the instant `fit` changes:
            /// Uniform on a wide box draws a thin wide rectangle across the middle of a big circle (which is
            /// precisely why only a slice of the ramp ever shows), Stretch draws it filling the square.</summary>
            void DrawSubjectBox()
            {
                // WHICH box depends on the fill's own anchor, and it is read here rather than captured so that
                // toggling Stamped/Fixed redraws immediately: Stamped normalizes against ONE repetition (a glyph,
                // a particle), Fixed against the whole subject.
                bool stamped = _fill.space == ZuiFill.FillSpace.Stamped
                               && _opt.stampedHalf.x > 0f && _opt.stampedHalf.y > 0f;
                Vector2 half = stamped ? _opt.stampedHalf : _opt.subjectHalf;
                ZuiSubjectShape shape = stamped ? _opt.stampedShape : _opt.subjectShape;
                if (half.x <= 0f || half.y <= 0f) return;

                Vector2 corner = _fill.Normalize(half, Vector2.zero, half);
                // The domain spans -1..1 across the swatch; convert the corner into texel coordinates.
                int x0 = UvToPx(-corner.x), x1 = UvToPx(corner.x);
                int y0 = UvToPx(-corner.y), y1 = UvToPx(corner.y);
                if (x1 <= x0 || y1 <= y0) return;

                // ALWAYS drawn, even when the subject covers the whole domain. Skipping it there was a mistake:
                // under Stretch the box always coincides with the frame, so half the placement combinations drew
                // no outline at all and read as "the preview is broken" rather than as "your subject covers the
                // entire gradient" — which is a real and useful answer. Pulled one texel inside so it reads as a
                // marker rather than merging into the swatch's own border.
                if (x0 <= 0 && y0 <= 0 && x1 >= _px - 1 && y1 >= _px - 1)
                {
                    x0 = 1; y0 = 1; x1 = _px - 2; y1 = _px - 2;
                }

                if (shape == ZuiSubjectShape.Circle) StrokeEllipse(x0, y0, x1, y1);
                else StrokeRect(x0, y0, x1, y1);

                MarkCentre();
            }

            /// <summary>Mark where the gradient's CENTRE actually landed.
            ///
            /// This is the control users misread, and the reason is genuine rather than a naming slip: moving the
            /// centre UP moves the gradient up, which makes a subject that stays put show the colours BELOW that
            /// centre — so the subject appears to shift the opposite way to the dial. Both things are true at
            /// once, and without a marker the only visible one is the confusing one.</summary>
            void MarkCentre()
            {
                Vector2 c = _fill.CenterAt(0f);
                int cx = UvToPx(c.x), cy = UvToPx(c.y);

                // A small open cross, not a dot: it stays readable on top of whatever colour it lands on and
                // cannot be mistaken for part of the gradient.
                for (int d = 1; d <= 3; d++)
                {
                    Ink(cx - d, cy); Ink(cx + d, cy);
                    Ink(cx, cy - d); Ink(cx, cy + d);
                }
            }

            void StrokeRect(int x0, int y0, int x1, int y1)
            {
                for (int x = x0; x <= x1; x++) { Ink(x, y0); Ink(x, y1); }
                for (int y = y0; y <= y1; y++) { Ink(x0, y); Ink(x1, y); }
            }

            /// The ellipse inscribed in the subject's bounds — a true circle when the subject is square and the
            /// fit is Uniform, and squashed exactly as the fill itself is squashed otherwise, since both come
            /// from the same normalized corner.
            void StrokeEllipse(int x0, int y0, int x1, int y1)
            {
                float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
                float a = (x1 - x0) * 0.5f, b = (y1 - y0) * 0.5f;
                if (a <= 0f || b <= 0f) return;

                // Step by the larger semi-axis so the samples are never more than a texel apart on either side.
                int steps = Mathf.Max(32, Mathf.CeilToInt(Mathf.Max(a, b) * 8f));
                for (int i = 0; i < steps; i++)
                {
                    float t = i / (float)steps * Mathf.PI * 2f;
                    Ink(Mathf.RoundToInt(cx + a * Mathf.Cos(t)), Mathf.RoundToInt(cy + b * Mathf.Sin(t)));
                }
            }

            int UvToPx(float t) => Mathf.Clamp(Mathf.RoundToInt((t + 1f) * 0.5f * (_px - 1)), 0, _px - 1);

            /// A readable outline over an arbitrary fill: alternate light/dark by position so the line survives
            /// whatever colour it crosses, instead of vanishing into a matching gradient stop.
            void Ink(int x, int y)
            {
                if (x < 0 || y < 0 || x >= _px || y >= _px) return;
                bool light = ((x + y) & 2) == 0;
                _buf[y * _px + x] = light ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
            }
        }
    }
}
