// SpriteFxRecolorEditor — the ZUI inspector for the SpriteFxRecolor component. Its scalar fields (stack / life /
// autoLife / lifeSeconds / seed) are drawn by the shared reflection drawer; the managed effect stack is drawn by
// the reusable SpriteFxStackView with includeColorRemap:true, so the non-shaped Colour remap effect is offered in
// the Add menu (a managed host runs every PixelModifier via ApplyPixel, so it can). Every edit records Undo, marks
// the component dirty, re-applies the recolour live and repaints the scene.

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zui;

namespace Laubrary.SpriteFx.Editor
{
    [CustomEditor(typeof(SpriteFxRecolor))]
    public class SpriteFxRecolorEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var comp = (SpriteFxRecolor)target;
            var root = new VisualElement();
            Z.Attach(root);   // ZUI stylesheet + zui-root class — an inspector is a root Zui doesn't own, so it must attach itself

            void Applied()
            {
                EditorUtility.SetDirty(comp);
                comp.Refresh();
                SceneView.RepaintAll();
            }

            // Scalar fields via the shared reflection drawer; the modifier list is drawn richer below, so skip it.
            ZuiReflect.BuildFields(root, comp, new ZuiReflect.Options
            {
                OnBeforeChange = () => Undo.RecordObject(comp, "Edit Recolor"),
                OnChanged = Applied,
                Skip = f => f.Name == "modifiers",
                ControlWidth = 150f,
            });

            var box = Z.Box("Recolour stack", "The managed effect stack applied to this sprite's pixels every frame.");
            var host = new SpriteFxStackView.Host
            {
                OnBeforeChange = () => Undo.RecordObject(comp, "Edit Recolor stack"),
                OnChanged = Applied,
                Rebuild = Applied,
                ControlWidth = 150f,
            };
            box.Add(SpriteFxStackView.Build(comp.modifiers, host, includeColorRemap: true));
            root.Add(box);
            return root;
        }
    }
}
