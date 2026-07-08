// LazorWindow.Layers.cs — the left panel, drawn with ZUI (Box / Label / Button / Toggle / Slider / EnumPopup):
// tool + snap/grid toggles and the grid resolution, the layer stack (add / reorder / toggle / rename / delete,
// back-to-front), and the selected layer's style and per-layer mirror/symmetry. Structural edits go through
// Undo; value edits just dirty the asset. Lives inside a fixed-width scrolled area carved out by DrawAsset.

using UnityEditor;
using UnityEngine;
using Laubrary.Lazor;

namespace Laubrary.Lazor.Editor
{
    public partial class LazorWindow
    {
        static readonly string[] ToolLabels = { "Pen", "Edit", "Erase" };

        void DrawLeftPanel(Rect r)
        {
            using (new GUILayout.AreaScope(r))
            using (ScrollView(ref leftScroll))
            {
                DrawToolsSection();
                VerticalSpace();
                DrawLayerList();
                VerticalSpace();
                DrawSelectedLayer();
            }
        }

        void DrawToolsSection()
        {
            using (Box("Tools"))
            {
                // Pen / Edit / Erase as a ZUI radio. Leaving Pen finalizes any in-progress stroke.
                int t = MiniRadio((int)tool, ToolLabels);
                if (t != (int)tool)
                {
                    if (tool == Tool.Pen) FinishStroke();
                    tool = (Tool)t;
                }
                Label("Pen: click to place points, click the first to close, right-click/Enter to finish.",
                      ZUI.ZTextStyle.Small);

                EditorGUILayout.BeginHorizontal();
                snap = Toggle(snap, "Snap");
                showGrid = Toggle(showGrid, "Grid");
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                int res = IntSlider("Grid cells", shape.gridResolution, 4, 64);
                if (EditorGUI.EndChangeCheck()) { shape.gridResolution = res; MarkDirty(); }
            }
        }

        void DrawLayerList()
        {
            using (Box())
            {
                EditorGUILayout.BeginHorizontal();
                Label("Layers (back → front)", ZUI.ZTextStyle.SectionHeader);
                GUILayout.FlexibleSpace();
                if (Button("+ Add", ZUI.Style.Default, GUILayout.Width(54)))
                {
                    RecordShape("Add Lazor layer");
                    shape.layers.Add(new LazorLayer($"Layer {shape.layers.Count + 1}"));
                    layerSel = shape.layers.Count - 1; activePath = -1; MarkDirty();
                }
                EditorGUILayout.EndHorizontal();

                int remove = -1, moveUp = -1, moveDown = -1;
                for (int i = shape.layers.Count - 1; i >= 0; i--)  // top layer shown first
                {
                    var layer = shape.layers[i];
                    bool sel = i == layerSel;

                    Rect row = EditorGUILayout.BeginHorizontal();
                    if (sel && Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(row, new Color(0.35f, 0.55f, 0.95f, 0.18f));

                    EditorGUI.BeginChangeCheck();
                    bool en = Toggle(layer.enabled, layer.enabled ? "✓" : "", ZUI.Style.Default, GUILayout.Width(26));
                    if (EditorGUI.EndChangeCheck()) { layer.enabled = en; MarkDirty(); }

                    if (Button(sel ? "●" : "○", ZUI.Style.Default, GUILayout.Width(24)) && !sel)
                    { layerSel = i; activePath = -1; }

                    EditorGUI.BeginChangeCheck();
                    string nm = EditorGUILayout.TextField(layer.name, GUILayout.MinWidth(50));
                    if (EditorGUI.EndChangeCheck()) { layer.name = nm; MarkDirty(); }

                    // Swatch so a layer is identifiable at a glance.
                    Rect sw = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14));
                    EditorGUI.DrawRect(sw, layer.color);

                    using (new EditorGUI.DisabledScope(i == shape.layers.Count - 1))
                        if (Button("▲", ZUI.Style.Default, GUILayout.Width(24))) moveUp = i;
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (Button("▼", ZUI.Style.Default, GUILayout.Width(24))) moveDown = i;
                    if (Button("✕", ZUI.Style.Default, GUILayout.Width(24))) remove = i;

                    EditorGUILayout.EndHorizontal();
                }

                if (moveUp >= 0) { RecordShape("Reorder Lazor layer"); Swap(moveUp, moveUp + 1); layerSel = moveUp + 1; MarkDirty(); }
                if (moveDown >= 0) { RecordShape("Reorder Lazor layer"); Swap(moveDown, moveDown - 1); layerSel = moveDown - 1; MarkDirty(); }
                if (remove >= 0)
                {
                    RecordShape("Delete Lazor layer");
                    shape.layers.RemoveAt(remove);
                    layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, shape.layers.Count - 1));
                    activePath = -1; MarkDirty();
                }
            }
        }

        void Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= shape.layers.Count || b >= shape.layers.Count) return;
            (shape.layers[a], shape.layers[b]) = (shape.layers[b], shape.layers[a]);
        }

        void DrawSelectedLayer()
        {
            if (shape.layers.Count == 0)
            {
                InfoBox("Add a layer to start drawing.");
                return;
            }
            var layer = shape.layers[layerSel];

            using (Box())
            {
                Label($"Layer — {layer.name}", ZUI.ZTextStyle.SectionHeader);

                EditorGUI.BeginChangeCheck();
                layer.color = ColorField("Color", layer.color);
                layer.thickness = Slider(layer.thickness, 0.002f, 0.2f, "Thickness");
                layer.cap = EnumPopup("Caps", layer.cap);
                layer.join = EnumPopup("Joins", layer.join);
                layer.blend = EnumPopup("Blend", layer.blend);
                layer.facing = EnumPopup("Facing", layer.facing);
                if (EditorGUI.EndChangeCheck()) MarkDirty();

                VerticalSpace();
                Label("Mirror / Symmetry", ZUI.ZTextStyle.SectionHeader);
                EditorGUI.BeginChangeCheck();
                layer.symmetryEnabled = Toggle(layer.symmetryEnabled, "Enabled");
                using (new EditorGUI.DisabledScope(!layer.symmetryEnabled))
                {
                    layer.symmetryCount = IntSlider("Sections", layer.symmetryCount, 2, 8);
                    layer.symmetryAngle = Slider(layer.symmetryAngle, 0f, 360f, "Angle");
                    layer.symmetryReflect = Toggle(layer.symmetryReflect, "Reflect (kaleidoscope)");
                }
                if (EditorGUI.EndChangeCheck()) MarkDirty();

                VerticalSpace();
                int strokes = layer.paths.Count;
                EditorGUILayout.BeginHorizontal();
                Label($"{strokes} stroke{(strokes == 1 ? "" : "s")}", ZUI.ZTextStyle.Small);
                GUILayout.FlexibleSpace();
                if (strokes > 0 && Button("Clear strokes", ZUI.Style.Default, GUILayout.Width(110)))
                {
                    RecordShape("Clear Lazor layer");
                    layer.paths.Clear(); activePath = -1; MarkDirty();
                }
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
