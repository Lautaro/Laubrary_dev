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
    /// The retained-mode twin of <see cref="LauAssetField"/>: the same "pick or create a LauAsset" row —
    /// thumbnail swatch, asset name, Recall (the shared <see cref="LauAssetPicker"/> browser), New ▾ (sourced
    /// from LauAssetEditors' registered creators for the constraint) and an Edit pen — built as
    /// <see cref="VisualElement"/>s so a UI Toolkit window gets it without an IMGUI island.
    ///
    /// The picker itself stays IMGUI (it is a <c>PopupWindowContent</c>) and is opened from the Recall button's
    /// own <c>worldBound</c>, the same way every other ported window anchors an IMGUI popup.
    public static class LauAssetElement
    {
        /// <param name="onPick">Receives the newly assigned asset (from Recall or New). The caller rebuilds.</param>
        /// <param name="tooltip">What this field is FOR — required, like every Z control.</param>
        public static VisualElement Build(Object current, Action<Object> onPick, Type constraint,
            Dictionary<Object, Texture2D> thumbCache, string suggestedName, string folder, string tooltip,
            float swatchSize = 40f)
        {
            var row = new VisualElement { tooltip = tooltip };
            row.AddToClassList("zui-row");

            var swatch = new VisualElement { tooltip = tooltip };
            swatch.AddToClassList("zui-cell__thumb");
            swatch.style.width = swatchSize;
            swatch.style.height = swatchSize;
            swatch.style.flexShrink = 0f;
            var tex = current != null ? LauAssetGridGUI.GetThumbnail(current, null, thumbCache) : null;
            if (tex != null)
            {
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                img.style.width = swatchSize - 4f;
                img.style.height = swatchSize - 4f;
                swatch.Add(img);
            }
            row.Add(swatch);

            var name = Z.Text(current != null ? current.name : "· none ·", ZuiText.Small,
                current != null ? $"{current.name} ({current.GetType().Name}) — the asset assigned here." : "No asset assigned yet.");
            name.style.maxWidth = 160f;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(name);

            var recall = Z.Button("Recall…", "Pick an existing asset from a thumbnail browser.", null);
            recall.clicked += () =>
            {
                var wb = recall.worldBound;
                LauAssetPicker.Show(new Rect(wb.x, wb.y, wb.width, wb.height), constraint,
                    picked => onPick?.Invoke(picked), current);
            };
            recall.style.width = 64f;
            row.Add(recall);

            var creatable = LauAssetEditors.RegisteredTypesFor(constraint).Where(LauAssetEditors.CanCreate).ToList();
            var makeNew = Z.Button("New ▾", "Create a brand new asset and assign it here.", null);
            makeNew.style.width = 52f;
            makeNew.SetEnabled(creatable.Count > 0);
            makeNew.clicked += () =>
            {
                if (creatable.Count == 1) { CreateAndAssign(creatable[0], suggestedName, folder, onPick); return; }
                var menu = new GenericMenu();
                foreach (var t in creatable)
                {
                    var concrete = t;
                    menu.AddItem(new GUIContent(concrete.Name), false,
                        () => CreateAndAssign(concrete, suggestedName, folder, onPick));
                }
                menu.ShowAsContext();
            };
            row.Add(makeNew);

            var edit = Z.Button("✎", "Edit — open this asset in its own editor.",
                () => { if (current != null) LauAssetEditors.Open(current); });
            edit.style.width = 26f;
            edit.SetEnabled(current != null && LauAssetEditors.CanOpen(current));
            row.Add(edit);

            return row;
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
