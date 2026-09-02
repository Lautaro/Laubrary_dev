// ChunkWindow.LayerPlanCard — the card for a Layer Plan (a coordinator; the recipe's named depth slots).
//
// A plain ordered list of names: LayerSpec carries no opacity/blend fields today (verified against source —
// CHUNKS-DESIGN-DECISIONS.md §8.10 / CHUNKS-BEHAVIOUR-CHECKLIST.md §10), so this card shows exactly name +
// reorder + remove + Add layer and nothing more. sortingLayerName/baseOrder/step exist on LayerSpec but are
// intentionally NOT exposed here — the design/checklist scope this card to the ordered name list only, and
// adding controls the checklist never asked for is exactly the "speculative surface" this programme forbids.
//
// Renaming a slot rewrites every producer's Layer slot that pointed at the old name, in the SAME undo step as
// the rename (design decision §8.7) — a plain List<string> reference is fragile to rename otherwise. Removing
// a slot resets referencing producers back to "(stack order)" rather than leaving them pointed at a name that
// no longer exists (§8.7's second half). Add / remove / reorder / rename are all structural enough to change
// what EVERY OTHER card's Layer slot picker offers (§10.1/§10.3), so this card is the one place in the
// programme that reaches for the shell's whole-stack rebuild (RebuildStack — the same one Add/Remove/Move
// capability already use, scroll position and playhead carried) instead of RebuildCard, which would only
// refresh this card's own body and leave every other picker's option list stale.
using Laubrary.Layering;
using Laubrary.Zui;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildLayerPlanCard(VisualElement body, ChunkSpec c, LayerPlan cap)
        {
            cap.layers ??= new LayerSpec();
            cap.layers.layers ??= new System.Collections.Generic.List<string>();
            var names = cap.layers.layers;

            var list = new VisualElement();
            for (int i = 0; i < names.Count; i++)
            {
                int index = i;
                var nameField = Z.TextInput(names[index],
                    "This layer's name. Every producer's Layer slot that points at it follows a rename " +
                    "automatically.",
                    v => RenameLayer(c, cap, index, v), 150f);
                nameField.isDelayed = true;   // commits on Enter/blur, not per keystroke

                list.Add(Z.Row(
                    nameField,
                    SmallButton("▲", "Move this layer one place earlier (further back).", index > 0,
                        () => ReorderLayer(cap, index, index - 1)),
                    SmallButton("▼", "Move this layer one place later (further forward).", index < names.Count - 1,
                        () => ReorderLayer(cap, index, index + 1)),
                    SmallButton("×", "Remove this layer. Anything drawing in it falls back to (stack order).",
                        true, () => RemoveLayer(c, cap, index))));
            }
            body.Add(list);

            var add = Z.Button("Add layer",
                "Add another named depth slot. Producers can then pick it as their Layer slot.",
                () => AddLayer(cap));
            add.style.width = 100f;
            add.style.alignSelf = Align.FlexStart;
            body.Add(add);
        }

        // ── structural edits — each can change what ANOTHER card's Layer slot picker offers, so each redraws
        // the whole stack rather than just this card. ────────────────────────────────────────────────────────

        void AddLayer(LayerPlan cap)
        {
            Dial("Add Layer", () => cap.layers.layers.Add(NextLayerName(cap.layers)));
            RebuildStack();
        }

        void RemoveLayer(ChunkSpec c, LayerPlan cap, int index)
        {
            var names = cap.layers.layers;
            if (index < 0 || index >= names.Count) return;
            Dial("Remove Layer", () =>
            {
                string name = names[index];
                names.RemoveAt(index);
                RewriteLayerReferences(c, name, "");
            });
            RebuildStack();
        }

        void ReorderLayer(LayerPlan cap, int from, int to)
        {
            Dial("Reorder Layer", () => cap.layers.Move(from, to));
            RebuildStack();
        }

        void RenameLayer(ChunkSpec c, LayerPlan cap, int index, string newName)
        {
            var names = cap.layers.layers;
            if (index < 0 || index >= names.Count) return;
            string oldName = names[index];
            if (string.IsNullOrWhiteSpace(newName)) newName = oldName;   // never author a blank slot name
            if (oldName == newName) return;
            Dial("Rename Layer", () =>
            {
                names[index] = newName;
                RewriteLayerReferences(c, oldName, newName);
            });
            RebuildStack();
        }

        static string NextLayerName(LayerSpec spec)
        {
            int n = spec.Count + 1;
            string name;
            do { name = "Layer " + n; n++; } while (spec.Has(name));
            return name;
        }

        /// The only place a producer's `layerName` field is written from outside its own card: every producer
        /// kind that carries one is listed here by hand, because LayerName is a read-only virtual property on
        /// the base — there is no shared settable surface to reach through instead.
        static void RewriteLayerReferences(ChunkSpec c, string oldName, string newName)
        {
            var stack = c != null ? c.capabilities : null;
            if (stack == null || string.IsNullOrEmpty(oldName)) return;
            foreach (var cap in stack)
            {
                switch (cap)
                {
                    case DebrisScatter d:    if (d.layerName == oldName) d.layerName = newName; break;
                    case FragmentFracture f: if (f.layerName == oldName) f.layerName = newName; break;
                    case PaletteSplash p:    if (p.layerName == oldName) p.layerName = newName; break;
                    case PyreBlast b:        if (b.layerName == oldName) b.layerName = newName; break;
                }
            }
        }
    }
}
