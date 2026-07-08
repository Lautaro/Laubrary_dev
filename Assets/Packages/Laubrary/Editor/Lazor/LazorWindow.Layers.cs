// LazorWindow.Layers.cs — the left panel: tool + snap/grid toggles, the grid resolution, the layer stack
// (add / reorder / toggle / rename / delete, drawn back-to-front), and the selected layer's style and
// per-layer mirror/symmetry settings. Structural edits go through Undo; value edits just dirty the asset.

using UnityEditor;
using UnityEngine;
using Laubrary.Lazor;

namespace Laubrary.Lazor.Editor
{
    public partial class LazorWindow
    {
        void DrawLeftPanel(Rect r)
        {
            using (new GUILayout.AreaScope(r))
            {
                leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

                DrawToolsSection();
                EditorGUILayout.Space(6);
                DrawLayerList();
                EditorGUILayout.Space(6);
                DrawSelectedLayer();

                EditorGUILayout.EndScrollView();
            }
        }

        void DrawToolsSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Tools", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    ToolButton(Tool.Pen, "Pen", "Click to place vertices. Click the first point to close, right-click / Enter / Esc to finish.");
                    ToolButton(Tool.Edit, "Edit", "Drag existing vertices to move them.");
                    ToolButton(Tool.Erase, "Erase", "Click a vertex to remove it. Alt+click removes the whole stroke.");
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    snap = GUILayout.Toggle(snap, new GUIContent("Snap", "Snap points to grid intersections. Hold Ctrl to temporarily invert."), EditorStyles.miniButton);
                    showGrid = GUILayout.Toggle(showGrid, new GUIContent("Grid", "Show the authoring grid."), EditorStyles.miniButton);
                }

                EditorGUI.BeginChangeCheck();
                int res = EditorGUILayout.IntSlider(new GUIContent("Grid cells", "Resolution of the authoring grid. Only a drawing aid — shapes are resolution-independent."), shape.gridResolution, 4, 64);
                if (EditorGUI.EndChangeCheck()) { shape.gridResolution = res; MarkDirty(); }
            }
        }

        void ToolButton(Tool t, string label, string tip)
        {
            bool on = tool == t;
            if (GUILayout.Toggle(on, new GUIContent(label, tip), EditorStyles.miniButton) != on)
            {
                if (tool == Tool.Pen) FinishStroke();
                tool = t;
            }
        }

        void DrawLayerList()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Layers (back → front)", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("+ Add", "Add a new empty layer on top."), EditorStyles.miniButton, GUILayout.Width(54)))
                    {
                        RecordShape("Add Lazor layer");
                        shape.layers.Add(new LazorLayer($"Layer {shape.layers.Count + 1}"));
                        layerSel = shape.layers.Count - 1; activePath = -1; MarkDirty();
                    }
                }

                int remove = -1, moveUp = -1, moveDown = -1;
                for (int i = shape.layers.Count - 1; i >= 0; i--)  // top layer shown first
                {
                    var layer = shape.layers[i];
                    bool sel = i == layerSel;
                    using (new EditorGUILayout.HorizontalScope(sel ? EditorStyles.helpBox : GUIStyle.none))
                    {
                        EditorGUI.BeginChangeCheck();
                        bool en = GUILayout.Toggle(layer.enabled, GUIContent.none, GUILayout.Width(16));
                        if (EditorGUI.EndChangeCheck()) { layer.enabled = en; MarkDirty(); }

                        if (GUILayout.Toggle(sel, sel ? "◉" : "○", EditorStyles.label, GUILayout.Width(16)) && !sel)
                        { layerSel = i; activePath = -1; }

                        EditorGUI.BeginChangeCheck();
                        string nm = EditorGUILayout.TextField(layer.name);
                        if (EditorGUI.EndChangeCheck()) { layer.name = nm; MarkDirty(); }

                        // Swatch so a layer is identifiable at a glance.
                        Rect sw = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14));
                        EditorGUI.DrawRect(sw, layer.color);

                        using (new EditorGUI.DisabledScope(i == shape.layers.Count - 1))
                            if (GUILayout.Button(new GUIContent("▲", "Move layer up (toward front)."), EditorStyles.miniButtonLeft, GUILayout.Width(22))) moveUp = i;
                        using (new EditorGUI.DisabledScope(i == 0))
                            if (GUILayout.Button(new GUIContent("▼", "Move layer down (toward back)."), EditorStyles.miniButtonMid, GUILayout.Width(22))) moveDown = i;
                        if (GUILayout.Button(new GUIContent("✕", "Delete this layer."), EditorStyles.miniButtonRight, GUILayout.Width(22))) remove = i;
                    }
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
                EditorGUILayout.HelpBox("Add a layer to start drawing.", MessageType.Info);
                return;
            }
            var layer = shape.layers[layerSel];

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label($"Layer: {layer.name}", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                layer.color = EditorGUILayout.ColorField(new GUIContent("Color", "Stroke color. With an Additive/Screen blend, alpha is glow intensity."), layer.color);
                layer.thickness = EditorGUILayout.Slider(new GUIContent("Thickness", "Stroke width as a fraction of the shape's size, so it scales with the shape."), layer.thickness, 0.002f, 0.2f);
                layer.cap = (LazorCap)EditorGUILayout.EnumPopup(new GUIContent("Caps", "How the ends of open strokes are drawn."), layer.cap);
                layer.join = (LazorJoin)EditorGUILayout.EnumPopup(new GUIContent("Joins", "How corners between segments are joined."), layer.join);
                layer.blend = (LazorBlend)EditorGUILayout.EnumPopup(new GUIContent("Blend", "How the color composites. Additive/Screen give the glowing laser look."), layer.blend);
                layer.facing = (LazorFacing)EditorGUILayout.EnumPopup(new GUIContent("Facing", "Flat in the shape plane, or always facing the camera."), layer.facing);
                if (EditorGUI.EndChangeCheck()) MarkDirty();

                EditorGUILayout.Space(4);
                GUILayout.Label("Mirror / Symmetry", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                layer.symmetryEnabled = EditorGUILayout.Toggle(new GUIContent("Enabled", "Repeat this layer's strokes symmetrically around the shape center."), layer.symmetryEnabled);
                using (new EditorGUI.DisabledScope(!layer.symmetryEnabled))
                {
                    layer.symmetryCount = EditorGUILayout.IntSlider(new GUIContent("Sections", "How many symmetric sections (2 = a single mirror line, up to 8)."), layer.symmetryCount, 2, 8);
                    layer.symmetryAngle = EditorGUILayout.Slider(new GUIContent("Angle", "Rotate the symmetry. For 2 sections: 0 = left/right, 90 = top/bottom, 45 = diagonal."), layer.symmetryAngle, 0f, 360f);
                    layer.symmetryReflect = EditorGUILayout.Toggle(new GUIContent("Reflect", "On = adjacent sections mirror (kaleidoscope). Off = pure rotation (pinwheel)."), layer.symmetryReflect);
                }
                if (EditorGUI.EndChangeCheck()) MarkDirty();

                EditorGUILayout.Space(2);
                int strokes = layer.paths.Count;
                GUILayout.Label($"{strokes} stroke{(strokes == 1 ? "" : "s")}", EditorStyles.miniLabel);
                if (strokes > 0 && GUILayout.Button(new GUIContent("Clear strokes", "Remove all strokes on this layer."), EditorStyles.miniButton))
                {
                    RecordShape("Clear Lazor layer");
                    layer.paths.Clear(); activePath = -1; MarkDirty();
                }
            }
        }
    }
}
