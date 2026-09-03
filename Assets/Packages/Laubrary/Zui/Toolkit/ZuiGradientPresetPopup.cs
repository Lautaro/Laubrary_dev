// ZuiGradientPresetPopup — T-0205, unlimited stops since T-0221. The project's saved-gradient library picker:
// ONE popup opened from the "★" button on ZuiGradientEditor's Output row (so every ZuiGradient site — Fill's
// Gradient fill, RampByQuantity, OverPhase, Procedural noise — reaches it identically, since they all already
// compose through ZuiGradientEditor/Z.Gradient) AND from ZuiRampControl's "★" (an IZuiRamp field — Pyre's
// PyreRamp — via ZuiRampGradientBridge). Both callers browse and save into the SAME ZuiGradientPresetLibrary
// asset, which is literally the owner's ask: "Gradients saved in the gradient control used in Fill will hold
// gradients the user has saved for this project."
//
// It trades in ZuiGradient rather than UnityEngine.Gradient, so a 10-stop Pyre ramp saves and reloads whole
// instead of being subsampled to 8 keys on the way in.
//
// UI Toolkit throughout (Z.Popover / Z.TextInput / Z.Button), matching ZuiRampControl's own popover — no
// PopupWindowContent / IMGUI, unlike the older ZUIEnvelopePresetPopup this mirrors structurally.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public static class ZuiGradientPresetPopup
    {
        /// Opens the picker anchored to <paramref name="anchor"/>. <paramref name="current"/> supplies the
        /// gradient a "Save" click stores (read lazily, at Save time, so it always saves whatever is live at
        /// that moment). <paramref name="apply"/> receives a FRESH ZuiGradient when a saved entry is picked —
        /// never a shared reference into the library's own copy.
        public static void Show(VisualElement anchor, Func<ZuiGradient> current, Action<ZuiGradient> apply)
        {
            var lib = ZuiGradientPresetLibrary.Load();
            string newName = "";
            ScrollView list = null;
            TextField nameField = null;

            Z.Popover(anchor, panel =>
            {
                panel.style.width = 260f;
                panel.tooltip = "This project's saved gradients — the same library every ramp and every "
                              + "gradient field in Shaper and Pyre draws from.";

                var saveRow = new VisualElement();
                saveRow.style.flexDirection = FlexDirection.Row;
                saveRow.style.alignItems = Align.Center;
                saveRow.style.marginBottom = 6f;

                nameField = Z.TextInput("", "Name this project's saved gradient.", v => newName = v, 180f);
                saveRow.Add(nameField);

                var saveBtn = Z.Button("Save", "Add the CURRENT ramp — every stop, whatever the count — to this "
                                             + "project's saved library.", () =>
                {
                    if (string.IsNullOrWhiteSpace(newName)) return;
                    lib.Add(newName, current?.Invoke());
                    newName = "";
                    nameField.value = "";
                    Rebuild();
                });
                saveBtn.style.marginLeft = 6f;
                saveRow.Add(saveBtn);
                panel.Add(saveRow);

                list = new ScrollView();
                list.style.maxHeight = 260f;
                panel.Add(list);

                Rebuild();

                void Rebuild()
                {
                    list.Clear();
                    if (lib.presets.Count == 0)
                    {
                        var empty = new Label("No saved gradients yet — name one above and hit Save.");
                        empty.style.whiteSpace = WhiteSpace.Normal;
                        empty.style.opacity = 0.7f;
                        list.Add(empty);
                        return;
                    }
                    for (int i = 0; i < lib.presets.Count; i++)
                    {
                        int idx = i;
                        var entry = lib.presets[idx];
                        var resolved = entry.Resolve();

                        var row = new VisualElement();
                        row.style.flexDirection = FlexDirection.Row;
                        row.style.alignItems = Align.Center;
                        row.style.marginBottom = 3f;

                        var swatch = new RampSwatch(resolved);
                        swatch.style.width = 80f;
                        swatch.style.height = 20f;
                        swatch.style.marginRight = 6f;
                        row.Add(swatch);

                        var pick = Z.Button(entry.name,
                            $"Apply this saved gradient ({resolved.Count} stops).", () => apply(entry.Resolve()));
                        pick.style.flexGrow = 1f;
                        row.Add(pick);

                        var del = Z.Button("×", "Delete this project's saved gradient.", () =>
                        {
                            lib.RemoveAt(idx);
                            Rebuild();
                        });
                        del.style.marginLeft = 4f;
                        del.style.width = 20f;
                        row.Add(del);

                        list.Add(row);
                    }
                }
            }, new ZuiPopover.Options { minWidth = 260f });
        }

        /// A small read-only painted strip previewing a saved ramp — a raw EvalRamp per column, cheap at swatch
        /// width, no LUT bake needed (unlike ZuiGradientEditor.Output, which bakes the full transform stack; a
        /// saved entry stores stops only, so its raw ramp IS the whole entry).
        sealed class RampSwatch : VisualElement
        {
            readonly ZuiGradient _g;
            public RampSwatch(ZuiGradient g)
            {
                _g = g;
                generateVisualContent += OnGenerate;
            }
            void OnGenerate(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (r.width <= 1f || _g == null) return;
                var p = mgc.painter2D;
                int cols = Mathf.Max(1, Mathf.CeilToInt(r.width));
                for (int c = 0; c < cols; c++)
                {
                    float x0 = c, x1 = Mathf.Min(c + 1f, r.width);
                    if (x1 <= x0) continue;
                    p.fillColor = _g.EvalRamp((c + 0.5f) / r.width);
                    p.BeginPath();
                    p.MoveTo(new Vector2(x0, 0)); p.LineTo(new Vector2(x1, 0));
                    p.LineTo(new Vector2(x1, r.height)); p.LineTo(new Vector2(x0, r.height));
                    p.ClosePath(); p.Fill();
                }
            }
        }
    }
}
