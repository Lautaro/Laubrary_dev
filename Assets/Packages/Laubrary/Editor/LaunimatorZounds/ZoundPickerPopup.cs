using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;

namespace Laubrary.LaunimatorZounds.Editor
{
    /// <summary>
    /// Thin wrapper over Zounds' own <see cref="GenericMenuPopup"/> for picking a single Zound by name from the
    /// Animation Builder's frame-event rows — reuses the existing search/select popup rather than building a
    /// new picker, matching <c>CompositeZoundEditorWindow</c>'s own usage of it. Known UX quirk accepted as-is:
    /// the popup is built for multi-select-then-confirm, so picking one item is "click it, then click the
    /// bottom confirm button" rather than a single click.
    /// </summary>
    public static class ZoundPickerPopup
    {
        static string searchText = "";

        public static void Show(Vector2 position, Action<string> onPicked)
        {
            var project = ZoundsProject.Instance;
            if (project == null || project.zoundLibrary == null) return;

            var sorted = project.zoundLibrary.GetAllZounds().OrderBy(z => z.name).ToList();

            var menu = new GenericMenu();
            foreach (var z in sorted)
            {
                var zound = z;
                menu.AddItem(new GUIContent(zound.GetType().Name + "/" + zound.name), false, userData =>
                {
                    onPicked?.Invoke(((Zound)userData).name);
                }, zound);
            }

            GenericMenuPopup.Show(
                menu,
                "Pick a Zound",
                position,
                new List<string>(),
                searchText,
                newSearch => searchText = newSearch);
        }
    }
}
