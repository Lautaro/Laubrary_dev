// ColorRemapGpuEditor — the ZUI inspector for the ColorRemapGpu driver. Renders the same ColorRemap region table
// as SpriteFxRecolor (via ZuiReflect, so the swatch picker + gradient controls are reused), plus the driver's own
// scalar fields. Every edit records Undo, forces the driver to re-bake, and repaints. The GPU shader ignores the
// modifier's `enabled` / `applyTargetAlpha` flags (it always preserves source alpha), so those are hidden here.

using UnityEditor;
using UnityEngine.UIElements;
using Laubrary.Zui;

namespace Laubrary.SpriteFx.Editor
{
    [CustomEditor(typeof(ColorRemapGpu))]
    public class ColorRemapGpuEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var comp = (ColorRemapGpu)target;
            var root = new VisualElement();
            Z.Attach(root);   // ZUI stylesheet + zui-root class — an inspector is a root Zui doesn't own, so it must attach itself

            void Applied()
            {
                comp.MarkDirty();
                EditorUtility.SetDirty(comp);
                EditorApplication.QueuePlayerLoopUpdate();   // tick the [ExecuteAlways] driver so it re-bakes now
                SceneView.RepaintAll();
            }

            // Driver scalar fields (speed / watchPalette / lutWidth) — the ColorRemap is drawn richer below.
            ZuiReflect.BuildFields(root, comp, new ZuiReflect.Options
            {
                OnBeforeChange = () => Undo.RecordObject(comp, "Edit Color Remap (GPU)"),
                OnChanged = Applied,
                Skip = f => f.Name == "remap",
                ControlWidth = 150f,
            });

            if (comp.remap == null) comp.remap = new ColorRemapModifier();
            var box = Z.Box("Recolour regions", "The source-colour → swatch / gradient regions baked into the GPU shader.");
            ZuiReflect.BuildFields(box, comp.remap, new ZuiReflect.Options
            {
                OnBeforeChange = () => Undo.RecordObject(comp, "Edit Color Remap (GPU)"),
                OnChanged = Applied,
                OnStructureChanged = Applied,
                // The GPU path ignores these: `enabled` (the component always drives), `applyTargetAlpha` (the
                // shader always keeps source alpha). Hide them so the inspector only shows what actually takes effect.
                Skip = f => f.Name == "enabled" || f.Name == "applyTargetAlpha",
                ControlWidth = 150f,
            });
            root.Add(box);
            return root;
        }
    }
}
