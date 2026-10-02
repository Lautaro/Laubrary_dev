// LazorWindow.Layers.cs — the left panel, built as retained UI Toolkit elements with ZUI's standard controls
// (Z.Section / Z.Field / Z.Button / Z.Toggle / Z.Slider / Z.EnumDropdown / Z.MiniRadio / Z.Color): tool +
// snap/grid toggles and the grid resolution, the layer stack (add / reorder / toggle / rename / delete,
// top layer first), and the selected layer's style and per-layer mirror/symmetry.
//
// Every mutation records Undo first. A pure VALUE edit calls EditedValue() (dirty the asset, redraw the
// canvas); a STRUCTURAL edit — anything that changes the layer list or which layer is selected — calls
// EditedStructure(), which rebuilds the panel so the retained controls are re-read from the new list. That
// distinction matters: rebuilding on a value edit would yank focus out of the name field mid-keystroke.

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Lazor;
using Laubrary.Zui;

namespace Laubrary.Lazor.Editor
{
    public partial class LazorWindow
    {
        static readonly string[] ToolLabels = { "Pen", "Edit", "Erase" };

        VisualElement BuildLeftPanel(LazorShape asset)
        {
            var panel = new VisualElement();
            panel.style.width = leftWidth;
            panel.style.flexShrink = 0f;
            panel.style.minHeight = 0f;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.Add(BuildToolsSection(asset));
            scroll.Add(BuildLayerList(asset));
            scroll.Add(BuildSelectedLayer(asset));
            panel.Add(scroll);
            return panel;
        }

        ZuiSection BuildToolsSection(LazorShape asset)
        {
            var section = Z.Section("Tools", "What a click on the canvas does, and how the grid behaves.", "lazor.tools");

            // Pen / Edit / Erase as a ZUI radio. Leaving Pen finalizes any in-progress stroke.
            section.Add(Z.MiniRadio((int)tool, ToolLabels,
                "Pen draws new strokes, Edit drags existing vertices, Erase removes them.",
                i =>
                {
                    if (tool == Tool.Pen && (Tool)i != Tool.Pen) FinishStroke();
                    tool = (Tool)i;
                    canvasHost?.MarkDirtyRepaint();
                }));

            var penHelp = Z.Text("Pen: click to place points, click the first to close, right-click/Enter to finish.",
                ZuiText.Small, "How the Pen tool is operated on the canvas.");
            penHelp.style.whiteSpace = WhiteSpace.Normal;   // the panel is narrow — wrap instead of clipping mid-word
            section.Add(penHelp);

            section.Add(Z.Row(
                Z.Toggle("Snap", "Snap placed points to whole grid cells (hold Ctrl on the canvas to invert this).",
                    snap, v => { snap = v; canvasHost?.MarkDirtyRepaint(); }),
                Z.Toggle("Grid", "Draw the grid lines and the design frame behind the shape.",
                    showGrid, v => { showGrid = v; canvasHost?.MarkDirtyRepaint(); })));

            const string gridTip = "How many cells across the design grid is — the unit strokes are authored in.";
            section.Add(Z.Field("Grid cells", gridTip,
                Z.SliderInt(asset.gridResolution, 4, 64, gridTip, v =>
                {
                    RecordShape("Edit Lazor grid");
                    asset.gridResolution = v;
                    EditedValue();
                })));

            return section;
        }

        ZuiSection BuildLayerList(LazorShape asset)
        {
            var section = Z.Section("Layers (back → front)",
                "The shape's draw stack — the bottom entry here is drawn last, on top of the others.", "lazor.layers");

            section.Add(Z.Row(
                Z.Flexible(),
                Z.Button("+ Add", "Add a new empty layer and select it.", () =>
                {
                    RecordShape("Add Lazor layer");
                    asset.layers.Add(new LazorLayer($"Layer {asset.layers.Count + 1}"));
                    layerSel = asset.layers.Count - 1;
                    activePath = -1;
                    EditedStructure();
                })));

            for (int i = asset.layers.Count - 1; i >= 0; i--)   // top layer shown first
            {
                int idx = i;                                    // capture: every callback below fires after the loop
                var layer = asset.layers[idx];
                bool sel = idx == layerSel;

                var enabledToggle = Z.Toggle("✓", $"Draw '{layer.name}' — off hides it everywhere, canvas included.",
                    layer.enabled, v =>
                    {
                        RecordShape("Toggle Lazor layer");
                        layer.enabled = v;
                        EditedValue();
                    }).W(26f);

                var selectButton = Z.Button(sel ? "●" : "○",
                    sel ? "This is the layer being drawn on." : $"Draw on '{layer.name}' instead.", () =>
                    {
                        if (sel) return;
                        layerSel = idx;
                        activePath = -1;
                        EditedStructure();
                    }).W(24f);

                var nameField = Z.TextInput(layer.name, "This layer's name — for your own reference only.",
                    v =>
                    {
                        RecordShape("Rename Lazor layer");
                        layer.name = v;
                        EditedValue();   // never rebuild here: it would kill focus after the first keystroke
                    }, 110f);
                // The name is the ONLY elastic thing in the row: everything else is a fixed-width glyph button, and
                // the panel can be dragged narrow, so something has to absorb the slack or the ✕ slides off the
                // edge. It cannot be the TextField itself — a stretching BaseField is the "fills whatever's left"
                // class ZuiToolkit.uss forbids and ZuiAudit flags. So a plain container does the stretching and the
                // field just fills it.
                nameField.style.width = Length.Percent(100f);
                nameField.style.flexGrow = 0f;
                var nameCell = new VisualElement();
                nameCell.style.flexGrow = 1f;
                nameCell.style.flexShrink = 1f;
                nameCell.style.minWidth = 36f;
                nameCell.Add(nameField);

                // Swatch so a layer is identifiable at a glance (read-only — the editable colour is in Layer below).
                var swatch = new VisualElement
                {
                    tooltip = $"'{layer.name}' draws in this colour — change it in the Layer section below."
                };
                swatch.style.width = 14f;
                swatch.style.height = 14f;
                swatch.style.flexShrink = 0f;
                swatch.style.backgroundColor = layer.color;

                var up = Z.Button("▲", "Move this layer one step toward the front.", () =>
                {
                    RecordShape("Reorder Lazor layer");
                    Swap(idx, idx + 1);
                    layerSel = idx + 1;
                    EditedStructure();
                }).W(24f);
                up.SetEnabled(idx != asset.layers.Count - 1);

                var down = Z.Button("▼", "Move this layer one step toward the back.", () =>
                {
                    RecordShape("Reorder Lazor layer");
                    Swap(idx, idx - 1);
                    layerSel = idx - 1;
                    EditedStructure();
                }).W(24f);
                down.SetEnabled(idx != 0);

                var del = Z.Button("✕", $"Delete '{layer.name}' and every stroke on it.", () =>
                {
                    RecordShape("Delete Lazor layer");
                    asset.layers.RemoveAt(idx);
                    layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, asset.layers.Count - 1));
                    activePath = -1;
                    EditedStructure();
                }).W(24f);

                var row = Z.Row(enabledToggle, selectButton, nameCell, swatch, up, down, del);
                foreach (var fixedCtl in new VisualElement[] { enabledToggle, selectButton, swatch, up, down, del })
                    fixedCtl.style.flexShrink = 0f;      // never let the glyph buttons be squeezed out of the panel
                if (sel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);
                section.Add(row);
            }

            return section;
        }

        void Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= shape.layers.Count || b >= shape.layers.Count) return;
            (shape.layers[a], shape.layers[b]) = (shape.layers[b], shape.layers[a]);
        }

        ZuiSection BuildSelectedLayer(LazorShape asset)
        {
            if (asset.layers.Count == 0)
            {
                var empty = Z.Section("Layer", "Style and mirror settings for the layer you're drawing on.", "lazor.layer");
                empty.Add(Z.Text("Add a layer to start drawing.", ZuiText.Small,
                    "A Lazor shape needs at least one layer before the canvas will accept strokes."));
                return empty;
            }

            // The selected layer's NAME is the section title — a section called "Layer" wrapping a heading called
            // "Layer — Hull" said the same thing twice and buried the one piece of information that varies.
            var layer = asset.layers[layerSel];
            var section = Z.Section($"Layer — {layer.name}",
                "Style and mirror settings for the layer you're drawing on.", "lazor.layer");

            const string colorTip = "The stroke colour this layer draws in.";
            section.Add(Z.Field("Color", colorTip, Z.Color(layer.color, colorTip, v =>
            {
                RecordShape("Edit Lazor layer style");
                layer.color = v;
                EditedValue();
            })));

            const string thickTip = "Stroke width as a fraction of the design grid, so it scales with the shape.";
            section.Add(Z.Field("Thickness", thickTip, Z.Slider(layer.thickness, 0.002f, 0.2f, thickTip, v =>
            {
                RecordShape("Edit Lazor layer style");
                layer.thickness = v;
                EditedValue();
            })));

            const string capTip = "How an open stroke's ends are finished.";
            section.Add(Z.Field("Caps", capTip, Z.EnumDropdown(layer.cap, capTip, v =>
            {
                RecordShape("Edit Lazor layer style");
                layer.cap = v;
                EditedValue();
            })));

            const string joinTip = "How two segments meet at a corner.";
            section.Add(Z.Field("Joins", joinTip, Z.EnumDropdown(layer.join, joinTip, v =>
            {
                RecordShape("Edit Lazor layer style");
                layer.join = v;
                EditedValue();
            })));

            const string blendTip = "How this layer composites over what's already drawn (Additive glows).";
            section.Add(Z.Field("Blend", blendTip, Z.EnumDropdown(layer.blend, blendTip, v =>
            {
                RecordShape("Edit Lazor layer style");
                layer.blend = v;
                EditedValue();
            })));

            const string facingTip = "Whether the strokes stay flat in 2D or turn to face the camera in 3D.";
            section.Add(Z.Field("Facing", facingTip, Z.EnumDropdown(layer.facing, facingTip, v =>
            {
                RecordShape("Edit Lazor layer style");
                layer.facing = v;
                EditedValue();
            })));

            section.Add(Z.Divider("Mirror / Symmetry",
                "Repeat this layer's strokes radially around the shape's origin."));

            section.Add(Z.Toggle("Enabled", "Mirror this layer's strokes around the origin.",
                layer.symmetryEnabled, v =>
                {
                    RecordShape("Edit Lazor mirror");
                    layer.symmetryEnabled = v;
                    EditedStructure();   // the three dials below are enabled/disabled by this — rebuild to refresh them
                }));

            const string sectionsTip = "How many copies the strokes are repeated into around the origin.";
            const string angleTip = "Rotation of the whole mirror pattern, in degrees.";
            const string reflectTip = "On: alternate copies are mirrored, giving a kaleidoscope instead of a pinwheel.";
            var symDeps = Z.Column(
                Z.Field("Sections", sectionsTip, Z.SliderInt(layer.symmetryCount, 2, 8, sectionsTip, v =>
                {
                    RecordShape("Edit Lazor mirror");
                    layer.symmetryCount = v;
                    EditedValue();
                })),
                Z.Field("Angle", angleTip, Z.Slider(layer.symmetryAngle, 0f, 360f, angleTip, v =>
                {
                    RecordShape("Edit Lazor mirror");
                    layer.symmetryAngle = v;
                    EditedValue();
                })),
                Z.Toggle("Reflect (kaleidoscope)", reflectTip, layer.symmetryReflect, v =>
                {
                    RecordShape("Edit Lazor mirror");
                    layer.symmetryReflect = v;
                    EditedValue();
                }));
            symDeps.SetEnabled(layer.symmetryEnabled);   // the UITK equivalent of an EditorGUI.DisabledScope
            section.Add(symDeps);

            int strokes = layer.paths.Count;
            var footer = Z.Row(
                Z.Text($"{strokes} stroke{(strokes == 1 ? "" : "s")}", ZuiText.Small,
                    "How many separate strokes this layer holds."),
                Z.Flexible());
            if (strokes > 0)
                footer.Add(Z.Button("Clear strokes", $"Remove every stroke from '{layer.name}' (undoable).", () =>
                {
                    RecordShape("Clear Lazor layer");
                    layer.paths.Clear();
                    activePath = -1;
                    EditedStructure();
                }));
            section.Add(footer);

            return section;
        }
    }
}
