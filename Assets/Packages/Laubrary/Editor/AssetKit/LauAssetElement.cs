using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// The one "reference to a LauAsset" control: a ZuiChip. Left-click (OnActivate) opens the shared
    /// LauAssetBrowser to pick a different one; right-click (OnContext) opens New / Edit / Clear. Also
    /// accepts a drag-and-drop of a matching asset from the Project window.
    ///
    /// Replaces the older always-visible "thumbnail swatch + Recall… + New ▾ + ✎" button row per
    /// LAUASSET_PICKER_SWEEP.md's own rules (no inline button row stealing a row of width; a reference reads
    /// as a chip; everything besides picking lives on a right-click context card) — that doc claimed every
    /// chip site was already rewritten this way, but this control itself never actually became one.
    public static class LauAssetElement
    {
        /// <param name="onPick">Receives the newly assigned (or cleared, via the context menu) asset.</param>
        /// <param name="tooltip">What this field is FOR — required, like every Z control.</param>
        public static VisualElement Build(Object current, Action<Object> onPick, Type constraint,
            Dictionary<Object, Texture2D> thumbCache, string suggestedName, string folder, string tooltip)
        {
            var chip = new ZuiChip(current != null ? current.name : null, tooltip, empty: current == null);
            if (current != null)
            {
                var tex = LauAssetGridGUI.GetThumbnail(current, null, thumbCache);
                if (tex != null) chip.Thumbnail = tex;
            }

            chip.OnActivate = c =>
            {
                var wb = c.worldBound;
                LauAssetBrowser.Show(new Rect(wb.x, wb.y, wb.width, wb.height), constraint,
                    picked => onPick?.Invoke(picked), current);
            };
            chip.OnContext = _ => ShowContextMenu(current, onPick, constraint, suggestedName, folder);
            chip.Accepts = o => o != null && constraint.IsInstanceOfType(o);
            chip.OnDrop = o => onPick?.Invoke(o);
            return chip;
        }

        static void ShowContextMenu(Object current, Action<Object> onPick, Type constraint, string suggestedName, string folder)
        {
            var menu = new GenericMenu();
            var creatable = LauAssetEditors.RegisteredTypesFor(constraint).Where(LauAssetEditors.CanCreate).ToList();
            if (creatable.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("New"));
            }
            else
            {
                foreach (var t in creatable)
                {
                    var concrete = t;
                    string label = creatable.Count == 1 ? "New" : "New/" + concrete.Name;
                    menu.AddItem(new GUIContent(label), false, () => CreateAndAssign(concrete, suggestedName, folder, onPick));
                }
            }

            if (current != null && LauAssetEditors.CanOpen(current))
                menu.AddItem(new GUIContent("Edit"), false, () => LauAssetEditors.Open(current));
            else
                menu.AddDisabledItem(new GUIContent("Edit"));

            if (current != null)
                menu.AddItem(new GUIContent("Clear"), false, () => onPick?.Invoke(null));
            else
                menu.AddDisabledItem(new GUIContent("Clear"));

            menu.ShowAsContext();
        }

        static void CreateAndAssign(Type concrete, string suggestedName, string folder, Action<Object> onPick)
        {
            var made = LauAssetEditors.Create(concrete, suggestedName, folder);
            if (made == null) return;
            onPick?.Invoke(made);
            if (LauAssetEditors.CanOpen(made)) LauAssetEditors.Open(made);
        }
    }
}
