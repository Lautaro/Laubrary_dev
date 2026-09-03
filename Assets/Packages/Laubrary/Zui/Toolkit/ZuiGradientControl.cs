// ZuiGradientControl — the STANDALONE gradient editor (used by ZuiReflect for a reflected ZuiGradient field and by
// Z.Gradient). It is a thin composer of the shared ZuiGradientEditor pieces, stacked: the objective OUTPUT
// preview strip, the editable SOURCE ramp, then the collapsible "Adjust" transforms box. (ZuiFillControl uses the
// same ZuiGradientEditor but arranges the pieces into the Fill header instead.)
//
// The SOURCE ramp is Unity's own GradientField (ZuiRampControl since T-0223) — the same control every Pyre ramp
// field now uses, so there is one colour editor in the package and it is the familiar one. Storage still holds an
// unbounded stop list, so a >8-stop palette applied from the "★" library is kept and rendered whole.

using System;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiGradientControl : VisualElement
    {
        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation.
        public Action OnChanged;

        /// <param name="collapsible">Reserved (the transforms always live in the collapsible "Adjust" box).</param>
        public ZuiGradientControl(ZuiGradient g, string tooltip = null, bool collapsible = true)
        {
            AddToClassList("zui-gradient-control");
            style.flexDirection = FlexDirection.Column;
            if (!string.IsNullOrEmpty(tooltip)) this.tooltip = tooltip;

            var ed = new ZuiGradientEditor(g, tooltip)
            {
                OnBeforeMutate = () => OnBeforeMutate?.Invoke(),
                OnChanged = () => OnChanged?.Invoke(),
            };
            ed.Output.style.marginBottom = 4;
            var outputRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            ed.Output.style.flexGrow = 1f;
            ed.Output.style.flexShrink = 1f;
            outputRow.Add(ed.Output);
            ed.Library.style.marginLeft = 4f;
            outputRow.Add(ed.Library);
            Add(outputRow);
            ed.Source.style.marginBottom = 4;   // air between the editable ramp row and the Adjust box
            Add(ed.Source);
            Add(ed.Adjust);
        }
    }
}
