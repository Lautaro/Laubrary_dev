// ShaperMockFillControl — the "Shaper-native fill control" the design doc calls for at §C2/§I2 priority 2:
// NOT a reuse of ZuiFillControl, which is contractually bound to the OLD ZuiFill runtime type, not
// ShaperFillDef (see that file's own header comment and SHAPER-UI-VISION-AND-DESIGN.md §C2).
//
// DELIBERATELY KEPT MOCK-SCOPED rather than landed in shared ZUI (unlike ZuiBreadcrumb): the design doc's
// own J4 #2 leaves "a new ZuiShaperFillControl vs. a generalised Z.Fill<T> with an adapter" open for the
// project owner to decide — committing a shape to shared ZUI here would be guessing an answer this task
// was not asked to give. Promote or generalise it once that question actually gets decided.
//
// T-0131 fills in the remaining five of the seven kinds (§C1) plus the shared palette-quantise post-stage
// (§C1, "applies to any fill kind, not just TapestrySteel"). Mirrors ZuiFillControl's proven shape: a
// compact swatch + label header, right-click for the mode menu, a packed-row body that rebuilds on switch.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zui;

namespace ShaperMock.Editor
{
    public sealed class ShaperMockFillControl : VisualElement
    {
        readonly ShaperMockFill _fill;
        readonly string _label;
        readonly string _tooltip;
        readonly VisualElement _content;
        Swatch _swatch;

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook (mirrors
        /// ZuiFillControl.OnBeforeMutate / Pyre's FillRow).
        public Action OnBeforeMutate;
        /// Fires after every mutation.
        public Action OnChanged;

        public ShaperMockFillControl(string label, ShaperMockFill fill, string tooltip)
        {
            _fill = fill ?? throw new ArgumentNullException(nameof(fill));
            _label = label;
            _tooltip = tooltip;
            AddToClassList("zui-fill");
            this.tooltip = string.IsNullOrEmpty(tooltip)
                ? "Right-click to choose the fill kind."
                : tooltip + "  (right-click to choose the fill kind)";

            _content = new VisualElement();
            Add(_content);
            RebuildAll();

            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return;
                ShowMenu();
                e.StopPropagation();
            });
        }

        void Mutate(Action apply)
        {
            OnBeforeMutate?.Invoke();
            apply();
            _swatch?.Refresh();
            OnChanged?.Invoke();
        }

        void RebuildAll()
        {
            _content.Clear();
            _swatch = null;

            switch (_fill.kind)
            {
                case ShaperMockFillKind.Solid:
                {
                    var cf = Z.Color(_fill.solidColor, _tooltip,
                        c => Mutate(() => _fill.solidColor = c), 170f, showAlpha: true);
                    var row = Z.Row(FieldLabel(_label ?? "Fill"), cf);
                    row.style.alignItems = Align.FlexStart;
                    _content.Add(row);
                    break;
                }

                case ShaperMockFillKind.Gradient:
                {
                    if (ShaperMockFill.IsUnseeded(_fill.gradient)) _fill.gradient = ShaperMockFill.DefaultGradient();

                    _swatch = new Swatch(SampleColor, 40f,
                        "Live preview of the gradient — left is the ramp's start, right its end.");
                    var header = Z.Row(_swatch, FieldLabel(_label ?? "Fill"));
                    header.style.alignItems = Align.Center;
                    _content.Add(header);

                    var grad = Z.Gradient(_tooltip, () => _fill.gradient,
                        g => Mutate(() => _fill.gradient = g), 200f);
                    _content.Add(grad);

                    _content.Add(Z.MicroSlider("Angle", _fill.gradientAngleDegrees, 0f, 360f,
                        "Rotation of the gradient's axis across the shape, in degrees.",
                        v => Mutate(() => _fill.gradientAngleDegrees = v), 150f, decimals: 0));
                    break;
                }

                case ShaperMockFillKind.RampByQuantity:
                {
                    _swatch = new Swatch(SampleColor, 40f, "Colour ramp preview.");
                    var header = Z.Row(_swatch, FieldLabel(_label ?? "Fill"));
                    header.style.alignItems = Align.Center;
                    _content.Add(header);

                    // The shape publishes no quantity in this mock — greyed with the reason (§B4's own
                    // literal "greyed out with the reason shown, not hidden" case), the mode selector
                    // itself stays a legitimate, always-visible choice.
                    var picker = Z.MiniRadio((int)_fill.rampQuantity,
                        new[] { "Heat", "Density", "Height" },
                        "Which published quantity the ramp reads. None of these are published by the "
                        + "current shape in this mock, so the ramp body below is greyed out.",
                        v => Mutate(() => _fill.rampQuantity = (ShaperMockRampQuantity)v));
                    _content.Add(Z.Field("Quantity", "Which published quantity drives the ramp.", picker));

                    var body = new VisualElement();
                    body.Add(Z.Field("Tint", "The ramp's colour.",
                        Z.Color(_fill.rampTint, "The ramp's colour.", c => Mutate(() => _fill.rampTint = c), 150f)));
                    body.Add(Z.HGroup(
                        Z.MicroSlider("Low", _fill.rampInputLow, 0f, 1f, "Input value the ramp starts at.",
                            v => Mutate(() => _fill.rampInputLow = v), 120f),
                        Z.MicroSlider("High", _fill.rampInputHigh, 0f, 1f, "Input value the ramp ends at.",
                            v => Mutate(() => _fill.rampInputHigh = v), 120f)));
                    body.SetEnabled(false);
                    body.tooltip = "Greyed out: this shape does not publish a "
                        + _fill.rampQuantity.ToString().ToLowerInvariant() + " quantity for the ramp to read.";
                    _content.Add(body);
                    _content.Add(Z.Help(
                        "This shape publishes no " + _fill.rampQuantity.ToString().ToLowerInvariant()
                        + " quantity, so the ramp has nothing to sample yet — the kind is still a "
                        + "legitimate choice to pre-author.", HelpBoxMessageType.Info));
                    break;
                }

                case ShaperMockFillKind.Texture:
                {
                    _swatch = new Swatch(SampleColor, 40f, "A flat proxy swatch standing in for the texture thumbnail.");
                    var header = Z.Row(_swatch, FieldLabel(_label ?? "Fill"));
                    header.style.alignItems = Align.Center;
                    _content.Add(header);

                    var picker = Z.MiniRadio(_fill.textureSourceIndex, ShaperMockFill.TextureSourceNames,
                        "Which source texture paints this fill — a picker, never a typed path.",
                        v => Mutate(() => _fill.textureSourceIndex = v), wrap: true);
                    _content.Add(Z.Field("Source", "The source texture.", picker));

                    // One packed row of five scalars instead of three stacked rows plus an orphaned field.
                    _content.Add(Z.HGroup(
                        Z.MicroSlider("Tiles X", _fill.textureTilesX, 0.1f, 8f, "Horizontal tile repeats.",
                            v => Mutate(() => _fill.textureTilesX = v), 120f),
                        Z.MicroSlider("Tiles Y", _fill.textureTilesY, 0.1f, 8f, "Vertical tile repeats.",
                            v => Mutate(() => _fill.textureTilesY = v), 120f),
                        Z.MicroSlider("Offset U", _fill.textureOffsetU, 0f, 1f, "Horizontal offset.",
                            v => Mutate(() => _fill.textureOffsetU = v), 120f),
                        Z.MicroSlider("Offset V", _fill.textureOffsetV, 0f, 1f, "Vertical offset.",
                            v => Mutate(() => _fill.textureOffsetV = v), 120f),
                        Z.MicroSlider("Angle", _fill.textureAngleDegrees, 0f, 360f,
                            "Rotation of the texture across the shape, in degrees.",
                            v => Mutate(() => _fill.textureAngleDegrees = v), 150f, decimals: 0)));
                    _content.Add(Z.Field("Tint", "Multiplies the sampled texture colour.",
                        Z.Color(_fill.textureTint, "Multiplies the sampled texture colour.",
                            c => Mutate(() => _fill.textureTint = c), 150f)));
                    break;
                }

                case ShaperMockFillKind.IndexedStrip:
                {
                    _swatch = new Swatch(SampleColor, 40f, "Blend of the strip's first slots.");
                    var header = Z.Row(_swatch, FieldLabel(_label ?? "Fill"));
                    header.style.alignItems = Align.Center;
                    _content.Add(header);

                    var modePicker = Z.MiniRadio((int)_fill.stripMode, new[] { "Angular", "Projection" },
                        "How the strip is swept across the shape.",
                        v => Mutate(() => _fill.stripMode = (ShaperMockStripMode)v));
                    _content.Add(Z.Field("Mode", "How the strip is swept.", modePicker));

                    // One packed row of four instead of two stacked rows of two.
                    _content.Add(Z.HGroup(
                        Z.MicroSlider("Repeats", _fill.stripRepeats, 0.25f, 8f, "How many times the strip repeats.",
                            v => Mutate(() => _fill.stripRepeats = v), 120f),
                        Z.MicroSlider("Orient.", _fill.stripOrientationDegrees, 0f, 360f, "The strip's orientation, in degrees.",
                            v => Mutate(() => _fill.stripOrientationDegrees = v), 120f, decimals: 0),
                        Z.MicroSlider("Offset", _fill.stripOffset, 0f, 1f, "Shifts the strip along its sweep.",
                            v => Mutate(() => _fill.stripOffset = v), 120f),
                        Z.MicroSlider("Reach", _fill.stripReach, 0f, 1f, "How far the strip's slots reach across the shape.",
                            v => Mutate(() => _fill.stripReach = v), 120f)));
                    _content.Add(Z.Field("Plain colour", "Fallback colour outside every slot's reach.",
                        Z.Color(_fill.stripPlainColor, "Fallback colour outside every slot's reach.",
                            c => Mutate(() => _fill.stripPlainColor = c), 150f)));

                    _content.Add(BuildStripSlotEditor());
                    break;
                }

                case ShaperMockFillKind.HeightField:
                {
                    _swatch = new Swatch(SampleColor, 40f, "A flat proxy swatch standing in for the preset thumbnail.");
                    var header = Z.Row(_swatch, FieldLabel(_label ?? "Fill"));
                    header.style.alignItems = Align.Center;
                    _content.Add(header);

                    _content.Add(BuildHeightFieldGrid());

                    _content.Add(Z.HGroup(
                        Z.MicroSlider("Scale", _fill.heightFieldScale, 0.1f, 4f, "Scales the height field's relief.",
                            v => Mutate(() => _fill.heightFieldScale = v), 150f),
                        Z.Field("Tint", "Multiplies the height field's colour.",
                            Z.Color(_fill.heightFieldTint, "Multiplies the height field's colour.",
                                c => Mutate(() => _fill.heightFieldTint = c), 150f))));
                    break;
                }

                case ShaperMockFillKind.TapestrySteel:
                {
                    _swatch = new Swatch(SampleColor, 40f, "Blend of the steel's low/high base colours.");
                    var header = Z.Row(_swatch, FieldLabel(_label ?? "Fill"));
                    header.style.alignItems = Align.Center;
                    _content.Add(header);

                    // One packed row of four noise scalars, one row of three colours (a natural sub-group,
                    // so it keeps its own row rather than joining the numeric row), one row of two rust
                    // amount/reach scalars — five stacked rows collapsed to three.
                    _content.Add(Z.HGroup(
                        Z.MicroSlider("Cells", _fill.steelCells, 1f, 32f, "Cellular-noise cell count.",
                            v => Mutate(() => _fill.steelCells = v), 120f, decimals: 0),
                        Z.MicroSlider("Octaves", _fill.steelOctaves, 1f, 6f, "Noise octave count.",
                            v => Mutate(() => _fill.steelOctaves = v), 120f, decimals: 0),
                        Z.MicroSlider("Seed", _fill.steelSeed, 0f, 999f, "Random seed for the noise.",
                            v => Mutate(() => _fill.steelSeed = v), 120f, decimals: 0),
                        Z.MicroSlider("Grain", _fill.steelGrain, 0f, 1f, "Fine surface grain amount.",
                            v => Mutate(() => _fill.steelGrain = v), 120f)));
                    _content.Add(Z.HGroup(
                        Z.Field("Low", "The steel's darkest base colour.",
                            Z.Color(_fill.steelBaseLow, "The steel's darkest base colour.",
                                c => Mutate(() => _fill.steelBaseLow = c), 90f)),
                        Z.Field("High", "The steel's brightest base colour.",
                            Z.Color(_fill.steelBaseHigh, "The steel's brightest base colour.",
                                c => Mutate(() => _fill.steelBaseHigh = c), 90f)),
                        Z.Field("Rust colour", "The rust patch colour.",
                            Z.Color(_fill.steelRustColor, "The rust patch colour.",
                                c => Mutate(() => _fill.steelRustColor = c), 90f))));
                    _content.Add(Z.HGroup(
                        Z.MicroSlider("Rust amount", _fill.steelRustAmount, 0f, 1f, "How much of the surface rusts.",
                            v => Mutate(() => _fill.steelRustAmount = v), 120f),
                        Z.MicroSlider("Rust reach", _fill.steelRustReachPixels, 0f, 16f, "How far rust bleeds, in pixels.",
                            v => Mutate(() => _fill.steelRustReachPixels = v), 120f)));
                    break;
                }
            }

            // Shared palette-quantise post-stage (§C1) — applies after every fill kind above, so it sits
            // once at the bottom of the body rather than being duplicated per-kind.
            _content.Add(BuildQuantiseRow());
        }

        VisualElement BuildQuantiseRow()
        {
            bool on = _fill.quantiseLevels > 0;
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.Add(Z.Toggle("Quantise to palette", "Snap this fill's colours down to N discrete levels — a shared "
                + "post-stage every fill kind carries, not a property of any one kind.", on, v =>
            {
                Mutate(() => _fill.quantiseLevels = v ? Mathf.Max(2, _fill.quantiseLevels == 0 ? 6 : _fill.quantiseLevels) : 0);
                RebuildAll();
            }));
            if (on)
                row.Add(Z.MicroSlider("Levels", _fill.quantiseLevels, 2, 32,
                    "How many discrete colour levels to quantise to.",
                    v => Mutate(() => _fill.quantiseLevels = Mathf.RoundToInt(v)), 130f, decimals: 0));
            return row;
        }

        VisualElement BuildStripSlotEditor()
        {
            var host = new VisualElement();
            void RebuildSlots()
            {
                host.Clear();
                var listHost = new VisualElement();
                host.Add(listHost);
                for (int i = 0; i < _fill.stripSlots.Count; i++)
                {
                    int idx = i;
                    var slot = _fill.stripSlots[i];
                    var row = new VisualElement();
                    row.AddToClassList("zui-row");

                    var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this slot.");
                    grip.style.unityFontStyleAndWeight = FontStyle.Bold;
                    grip.style.width = 16f;
                    ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
                    {
                        Mutate(() =>
                        {
                            var s = _fill.stripSlots[from];
                            _fill.stripSlots.RemoveAt(from);
                            _fill.stripSlots.Insert(to, s);
                        });
                        RebuildSlots();
                    });
                    row.Add(grip);

                    row.Add(Z.Color(slot.color, "This slot's colour.", c => Mutate(() => slot.color = c), 60f));
                    var heightSlider = Z.MicroSlider("Height", slot.height, 0f, 1f,
                        "This slot's height along the strip.",
                        v => Mutate(() => slot.height = v), 110f);
                    row.Add(heightSlider);

                    var remove = Z.Button("×", "Remove this slot (undoable).", () =>
                    {
                        Mutate(() => _fill.stripSlots.RemoveAt(idx));
                        RebuildSlots();
                    }).W(20f);
                    remove.SetEnabled(_fill.stripSlots.Count > 1);
                    row.Add(remove);

                    listHost.Add(row);
                }

                host.Add(Z.Button("+ Add slot", "Append a colour/height slot to the strip.", () =>
                {
                    Mutate(() => _fill.stripSlots.Add(new ShaperMockStripSlot
                    { color = Color.HSVToRGB(UnityEngine.Random.value, 0.6f, 0.9f), height = 0.5f }));
                    RebuildSlots();
                }).W(90f));
            }
            RebuildSlots();
            return host;
        }

        VisualElement BuildHeightFieldGrid()
        {
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            var names = ShaperMockFill.HeightFieldPresetNames;
            var tints = ShaperMockFill.HeightFieldPresetTints;
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                bool sel = _fill.heightFieldPresetIndex == idx;
                var tile = new VisualElement { tooltip = names[i] + " — click to apply this height-field preset." };
                tile.style.width = 40f;
                tile.style.height = 40f;
                tile.style.marginRight = 4f;
                tile.style.marginBottom = 4f;
                tile.style.backgroundColor = tints[i];
                tile.style.borderTopLeftRadius = tile.style.borderTopRightRadius =
                    tile.style.borderBottomLeftRadius = tile.style.borderBottomRightRadius = 3f;
                var bw = sel ? 3f : 1f;
                var bc = sel ? new Color(0.4f, 0.75f, 1f) : new Color(0f, 0f, 0f, 0.4f);
                tile.style.borderTopWidth = tile.style.borderBottomWidth =
                    tile.style.borderLeftWidth = tile.style.borderRightWidth = bw;
                tile.style.borderTopColor = tile.style.borderBottomColor =
                    tile.style.borderLeftColor = tile.style.borderRightColor = bc;
                tile.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    Mutate(() => _fill.heightFieldPresetIndex = idx);
                    RebuildAll();
                    e.StopPropagation();
                });
                grid.Add(tile);
            }
            return grid;
        }

        static Label FieldLabel(string text)
        {
            var l = new Label(text);
            l.AddToClassList("zui-field__label");
            l.style.marginTop = 3f;
            return l;
        }

        void ShowMenu()
        {
            Z.Menu(this)
                .Section("Fill kind")
                .Item("Solid colour", "A single flat colour.",
                    () => SetKind(ShaperMockFillKind.Solid), _fill.kind == ShaperMockFillKind.Solid)
                .Item("Gradient", "A colour ramp swept across the shape.",
                    () => SetKind(ShaperMockFillKind.Gradient), _fill.kind == ShaperMockFillKind.Gradient)
                .Item("Ramp by quantity", "A colour ramp driven by a published quantity (heat/density/height).",
                    () => SetKind(ShaperMockFillKind.RampByQuantity), _fill.kind == ShaperMockFillKind.RampByQuantity)
                .Item("Texture", "A tiled source texture.",
                    () => SetKind(ShaperMockFillKind.Texture), _fill.kind == ShaperMockFillKind.Texture)
                .Item("Indexed strip", "A hand-authored strip of colour/height slots.",
                    () => SetKind(ShaperMockFillKind.IndexedStrip), _fill.kind == ShaperMockFillKind.IndexedStrip)
                .Item("Height field", "A relief preset from the height-field library.",
                    () => SetKind(ShaperMockFillKind.HeightField), _fill.kind == ShaperMockFillKind.HeightField)
                .Item("Tapestry steel", "A procedural brushed/rusted steel surface.",
                    () => SetKind(ShaperMockFillKind.TapestrySteel), _fill.kind == ShaperMockFillKind.TapestrySteel)
                .Show();
        }

        void SetKind(ShaperMockFillKind kind)
        {
            Mutate(() =>
            {
                _fill.kind = kind;
                if (kind == ShaperMockFillKind.Gradient && ShaperMockFill.IsUnseeded(_fill.gradient))
                    _fill.gradient = ShaperMockFill.DefaultGradient();
            });
            RebuildAll();
        }

        /// A cheap representative colour for the header swatch, per kind — not a real render of the fill,
        /// just enough to make "which kind is this" scannable at a glance (mirrors ZuiFillControl's own
        /// FillSwatch reasoning).
        Color32 SampleColor()
        {
            switch (_fill.kind)
            {
                case ShaperMockFillKind.Solid: return _fill.solidColor;
                case ShaperMockFillKind.Gradient:
                    return ShaperMockFill.IsUnseeded(_fill.gradient) ? (Color32)Color.magenta : (Color32)_fill.gradient.Evaluate(0.5f);
                case ShaperMockFillKind.RampByQuantity: return _fill.rampTint;
                case ShaperMockFillKind.Texture: return _fill.textureTint;
                case ShaperMockFillKind.IndexedStrip:
                    return _fill.stripSlots.Count > 0 ? (Color32)_fill.stripSlots[0].color : (Color32)_fill.stripPlainColor;
                case ShaperMockFillKind.HeightField: return _fill.heightFieldTint;
                case ShaperMockFillKind.TapestrySteel: return Color32.Lerp(_fill.steelBaseLow, _fill.steelBaseHigh, 0.5f);
            }
            return Color.magenta;
        }

        // ── live swatch — a solid tint, or the gradient sampled left→right for Gradient mode ───────────
        sealed class Swatch : VisualElement
        {
            readonly Func<Color32> _sample;
            readonly bool _isGradient;
            readonly ShaperMockFill _gradFill;

            public Swatch(Func<Color32> sample, float size, string tip)
            {
                _sample = sample;
                tooltip = tip;
                style.width = size;
                style.height = size;
                style.flexShrink = 0f;
                style.marginRight = 6f;
                var bc = new Color(0f, 0f, 0f, 0.4f);
                style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1f;
                style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = bc;
                style.borderTopLeftRadius = style.borderTopRightRadius =
                    style.borderBottomLeftRadius = style.borderBottomRightRadius = 2f;
                generateVisualContent += Paint;
            }

            public void Refresh() => MarkDirtyRepaint();

            void Paint(MeshGenerationContext mgc)
            {
                var view = contentRect;
                if (view.width < 2f || view.height < 2f) return;
                var p = mgc.painter2D;
                Fill(p, view, _sample());
            }

            static void Fill(Painter2D p, Rect r, Color c)
            {
                p.fillColor = c;
                p.BeginPath();
                p.MoveTo(new Vector2(r.x, r.y));
                p.LineTo(new Vector2(r.xMax, r.y));
                p.LineTo(new Vector2(r.xMax, r.yMax));
                p.LineTo(new Vector2(r.x, r.yMax));
                p.ClosePath();
                p.Fill();
            }
        }
    }
}
