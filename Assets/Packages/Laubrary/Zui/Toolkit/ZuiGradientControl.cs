// ZuiGradientControl — the STANDALONE gradient editor (used by ZuiReflect for a reflected ZuiGradient field and by
// Z.Gradient). It is now a thin composer of the shared ZuiGradientEditor pieces, stacked: the objective OUTPUT
// preview strip, the editable SOURCE ramp, then the collapsible "Adjust" transforms box. (ZuiFillControl uses the
// same ZuiGradientEditor but arranges the pieces into the Fill header instead.)

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
            Add(ed.Output);
            Add(ed.Source);
            Add(ed.Adjust);
        }
    }
}
