// ChunkWindow.LayerPlanCard — the card for the recipe's DEPTH LIST (a coordinator: what draws in front of
// what, and nothing else).
//
// One row per thing the recipe draws, dragged into order. The TOP row draws first and therefore furthest
// back; reading down the list moves toward the viewer (owner's decision, T-0365 Q2).
//
// It replaced a list of typed slot NAMES that every producer card then picked from by name. Three things went
// with it and all three were the point (T-0349 D1/D7, ui-rules §1 and §4):
//   * nothing is typed. The rows come from the cards, so there is no name to invent, rename or mistype, and
//     no second place to pick it again. A row is added and removed by adding and removing a card.
//   * depth is per PIECE, not per card. A Fracture or a patterned Pyre Blast can be SPLIT into one row per
//     piece/point, which is the only way the owner's own layering (a fragment in front of one blast and
//     behind another, in one burst) can be authored at all.
//   * reordering is a DRAG, not ▲▼ buttons — which is what this card used to have, and which ui-rules §4
//     rules out outright: position N of M costs N clicks and the arrows eat a row's horizontal space.
//
// Rows are reconciled against the stack on every structural change (ChunkWindow.Recipe.FillStack →
// ReconcileDepth), so this card only ever has to draw what is already true.
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildLayerPlanCard(VisualElement body, ChunkSpec c, LayerPlan cap)
        {
            cap.rows ??= new List<DepthRow>();
            cap.Reconcile(c != null ? c.capabilities : null);

            var rows = cap.rows;
            if (rows.Count == 0)
            {
                // The empty state is a real screen, not a blank pane: it says why it is empty and where the
                // rows come from, since there is nothing to click here to make one.
                body.Add(Z.Text("No producers in this recipe yet — add a Debris Scatter, Fragment Fracture, " +
                                "Palette Splash or Pyre Blast and it gets a row here.", ZuiText.Body,
                                "Rows come from the recipe's cards; there is nothing to add here by hand."));
                return;
            }

            var list = new VisualElement();
            for (int i = 0; i < rows.Count; i++)
                list.Add(BuildDepthRow(list, c, cap, i));
            body.Add(list);
        }

        VisualElement BuildDepthRow(VisualElement listHost, ChunkSpec c, LayerPlan plan, int index)
        {
            var row = plan.rows[index];
            var producer = FindById(c, row.capabilityId);
            var host = Z.Row();
            host.style.marginBottom = 1f;

            var grip = Z.Text("≡", ZuiText.Body,
                "Drag to change what draws in front. The top row draws first and is furthest back; each row " +
                "below it draws in front of the one above.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 14f;
            grip.style.unityTextAlign = TextAnchor.MiddleCenter;
            ZuiReorder.MakeGrip(grip, host, listHost, (from, to) =>
            {
                Dial("Reorder Depth", () => plan.Move(from, to));
                RebuildStack();
            });
            host.Add(grip);

            // The row's own place, back to front. Every row's tooltip reads for ITS position, so "which end is
            // the front?" is answered wherever the pointer already is instead of by a caption over the list.
            var number = Z.Text((index + 1) + ".", ZuiText.Subtle,
                index == 0
                    ? "Row 1 of " + plan.RowCount + " — drawn first, so it is furthest BACK."
                    : index == plan.RowCount - 1
                        ? "Row " + plan.RowCount + " of " + plan.RowCount + " — drawn last, so it is in FRONT " +
                          "of everything else this recipe draws."
                        : "Row " + (index + 1) + " of " + plan.RowCount + ", counting from the back.");
            number.style.width = 22f;
            number.style.unityTextAlign = TextAnchor.MiddleRight;
            number.style.marginRight = 4f;
            host.Add(number);

            // The card's own colour — the same chip its header, its Timing lane and its outline on the preview
            // wear, which is how a row is matched to what it draws without a picture of its own.
            var colour = producer != null ? ChunkCardColors.For(c, producer) : Color.grey;
            var chip = new Label();
            chip.style.backgroundColor = colour;
            chip.style.width = 10f;
            chip.style.height = 10f;
            chip.style.flexShrink = 0f;
            chip.style.marginRight = 5f;
            chip.style.borderTopLeftRadius = 2f;
            chip.style.borderTopRightRadius = 2f;
            chip.style.borderBottomLeftRadius = 2f;
            chip.style.borderBottomRightRadius = 2f;
            chip.tooltip = "This card's colour, the same one its header, its Timing lane and its outlines on " +
                           "the preview wear.";
            host.Add(chip);

            string title = producer != null ? producer.Title : "(missing card)";
            string label = row.IsWholeCard ? title : title + " " + (row.instance + 1);
            var name = Z.Text(label, ZuiText.Body,
                row.IsWholeCard
                    ? "Everything " + title + " draws, on this one row. Click to jump to its card."
                    : "Piece " + (row.instance + 1) + " of " + title + " — numbered clockwise from the top of " +
                      "the picture, the same number the preview prints on it. Click to jump to its card.");
            name.style.flexGrow = 0f;
            name.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || producer == null) return;
                RevealCard(producer.id);
                e.StopPropagation();
            });
            host.Add(name);

            host.Add(Z.Flexible());

            // Split / Join, on every row of a card that can split, so the card can be joined from whichever of
            // its rows the pointer is already on. Cards whose output has no countable identity (a splash's
            // particles, a scatter's debris) never show it — the owner's answer to T-0365 Q4 was that those
            // never split, so an always-disabled control would be surface for a thing that cannot happen.
            if (producer != null && producer.DepthInstanceCount > 1)
            {
                bool split = plan.IsSplit(producer.id);
                int count = producer.DepthInstanceCount;
                var toggle = Z.Toggle("Split",
                    split
                        ? "Put this card's " + count + " pieces back on ONE row, at the backmost place they " +
                          "draw now."
                        : "Give each of this card's " + count + " pieces its own row, so something else can " +
                          "sit between them. They start where this row is, so nothing moves until you drag.",
                    split,
                    v =>
                    {
                        Dial(v ? "Split Depth Rows" : "Join Depth Rows",
                             () => { if (v) plan.Split(producer); else plan.Join(producer); });
                        RebuildStack();
                    });
                toggle.W(56f);
                host.Add(toggle);
            }

            return host;
        }

        /// The capability a row addresses, by id. Null when the row outlived its card — which Reconcile
        /// normally prevents, and which this still draws honestly rather than throwing.
        static ChunkCapability FindById(ChunkSpec c, string id)
        {
            var stack = c != null ? c.capabilities : null;
            if (stack == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] != null && stack[i].id == id) return stack[i];
            return null;
        }
    }
}
