// ZuiContextExtras — lets the code that BUILT a control add its own items to that control's right-click menu.
//
// A ZUI control that owns a right-click menu (ZuiValueControl's mode menu, ZuiMicroSlider's display menu) appends the
// extras registered for it; a ZuiMicroSlider with no menu of its own shows a menu of just the extras. The host knows
// what the value IS (whose parameter, which asset), the control does not, so this is how a host offers actions about
// the value ("give it a command id", "copy its address") without each control growing a hook per tool.
//
// Usage:  ZuiContextExtras.Add(slider, menu => menu.Item("Command…", "Give this setting a command id.", Open));
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public static class ZuiContextExtras
    {
        static readonly ConditionalWeakTable<VisualElement, List<Action<ZuiMenu>>> extras = new ConditionalWeakTable<VisualElement, List<Action<ZuiMenu>>>();

        /// <summary>Adds items to <paramref name="control"/>'s right-click menu. Called each time the menu opens.</summary>
        public static T Add<T>(T control, Action<ZuiMenu> contribute) where T : VisualElement
        {
            if (control == null || contribute == null) return control;
            extras.GetOrCreateValue(control).Add(contribute);
            return control;
        }

        public static bool Has(VisualElement control) => control != null && extras.TryGetValue(control, out var list) && list.Count > 0;

        /// <summary>Appends the extras for <paramref name="control"/> to an open menu, after a separator when the menu has items already.</summary>
        public static void AppendTo(VisualElement control, ZuiMenu menu, bool separator)
        {
            if (control == null || menu == null || !extras.TryGetValue(control, out var list) || list.Count == 0) return;
            if (separator) menu.Separator();
            foreach (var contribute in list) contribute(menu);
        }
    }
}
