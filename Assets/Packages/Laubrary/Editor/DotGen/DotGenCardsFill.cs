// DotGenCardsFill — the Fill drawer's paint, drawn by hand inside its card.
//
// Two things here cannot come from the reflection drawer, which is why this file exists at all:
//
//   • a `ZuiFill` has no reflected control, so a reflected `fill` field would come out as a nested box of
//     raw sub-fields (mode, gradient, angle, zoom, fit…) instead of the one fill editor every other
//     Laubrary tool uses. `Z.Fill` IS that editor — a colour picker in Solid mode, a gradient with an
//     angle in Linear mode — so the card calls it directly;
//
//   • the fill LIST is ordered and its last entry is protected. A generic list drawer knows neither, and
//     the two Add buttons have to arrive on the documented colours (POC §11.1) rather than on a blank
//     entry the author then has to fix.
//
// Both fill and list edits go through the window's own Record/Applied pair, so a drag inside the fill
// editor is one Undo step and re-renders the picture, exactly like a slider on the same card.

using System;
using Laubrary.Zui;
using UnityEngine.UIElements;

namespace Laubrary.DotGen.Editor
{
    public partial class DotGenWindow
    {
        /// The paint half of a Fill drawer's body: one fill, or the ordered list it picks from. Which of the
        /// two is shown follows the drawer's own Fill source dial, whose edit rebuilds the card (it gates
        /// other fields too, so the reflection drawer already reports it as a structural change).
        void BuildFillBody(VisualElement body, DotFillDrawer d, Action rebuild)
        {
            if (body == null || d == null) return;

            if (d.fillSource == DotFillSource.SingleFill)
            {
                if (d.fill == null)
                {
                    body.Add(Z.Text("No fill.", ZuiText.Subtle,
                        "This drawer has no paint assigned. Reopening the document restores the default fill."));
                    return;
                }

                body.Add(Z.Fill("Fill", d.fill,
                    "The paint used for every target this drawer paints. Right-click the swatch to switch it "
                    + "between a flat colour and a gradient.",
                    Applied, Record("Edit DotGen fill")));
                return;
            }

            if (d.fills == null || d.fills.Count == 0)
            {
                body.Add(Z.Text("No fills.", ZuiText.Subtle,
                    "This drawer's list is empty. Reopening the document restores the default three."));
                return;
            }

            // A nested box, so the list reads as one group rather than as loose rows trailing off the card.
            var box = Z.Box(null, null);
            bool last = d.fills.Count <= 1;

            for (int i = 0; i < d.fills.Count; i++)
            {
                int idx = i;
                var f = d.fills[idx];
                if (f == null) continue;

                var row = Z.Row();
                row.AddToClassList("zui-row--nowrap");

                row.Add(Z.Fill("Fill " + (idx + 1), f,
                    "One of the fills this drawer chooses between. Which target gets which is fixed by the "
                    + "variation seed above, so the same seed always paints the same picture.",
                    Applied, Record("Edit DotGen fill")));

                var up = Z.Button("▲",
                    idx > 0
                        ? "Move this fill one place earlier in the list."
                        : "Already first in the list.",
                    () => MoveFill(d, idx, idx - 1, rebuild));
                up.AddToClassList("zui-row__reorder-action");
                var down = Z.Button("▼",
                    idx < d.fills.Count - 1
                        ? "Move this fill one place later in the list."
                        : "Already last in the list.",
                    () => MoveFill(d, idx, idx + 1, rebuild));
                down.AddToClassList("zui-row__reorder-action");
                var del = Z.Button("×",
                    last
                        ? "The list keeps at least one fill — add another before removing this one."
                        : "Remove this fill from the list (undoable).",
                    () => RemoveFill(d, idx, rebuild));
                del.AddToClassList("zui-row__remove-action");

                up.SetEnabled(idx > 0);
                down.SetEnabled(idx < d.fills.Count - 1);
                del.SetEnabled(!last);

                row.Add(up);
                row.Add(down);
                row.Add(del);
                box.Add(row);
            }

            var addRow = Z.Row(
                Z.Button("Add flat", "Add a flat colour to the end of the list.",
                    () => AddFill(d, DotGenFills.NewFlat(), rebuild)),
                Z.Button("Add gradient", "Add a two-colour gradient to the end of the list.",
                    () => AddFill(d, DotGenFills.NewGradient(), rebuild)));
            addRow.AddToClassList("zui-row--nowrap");
            box.Add(addRow);

            body.Add(box);
        }

        void AddFill(DotFillDrawer d, ZuiFill fill, Action rebuild)
        {
            if (d == null || fill == null || d.fills == null) return;
            Dirty(() => d.fills.Add(fill), "Add DotGen fill");
            rebuild?.Invoke();
        }

        void MoveFill(DotFillDrawer d, int from, int to, Action rebuild)
        {
            if (d == null || d.fills == null) return;
            if (from < 0 || from >= d.fills.Count || to < 0 || to >= d.fills.Count || from == to) return;
            Dirty(() =>
            {
                var f = d.fills[from];
                d.fills.RemoveAt(from);
                d.fills.Insert(to, f);
            }, "Reorder DotGen fill");
            rebuild?.Invoke();
        }

        /// The list never empties: a drawer with no fill has nothing to paint and would silently disappear
        /// from the picture, which reads as a bug rather than as an edit.
        void RemoveFill(DotFillDrawer d, int idx, Action rebuild)
        {
            if (d == null || d.fills == null || d.fills.Count <= 1) return;
            if (idx < 0 || idx >= d.fills.Count) return;
            Dirty(() => d.fills.RemoveAt(idx), "Remove DotGen fill");
            rebuild?.Invoke();
        }
    }
}
