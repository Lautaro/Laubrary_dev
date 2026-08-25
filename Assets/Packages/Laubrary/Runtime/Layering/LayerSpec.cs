using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Layering
{
    /// One shared answer to the question "which sortingOrder does THIS piece of the effect get?", for an
    /// effect that is composed of several renderers rather than one. Today that draw order is hand-rolled as
    /// a single flat int sortingOrder in three separate places — ZoetropePyre/SpawnPyreFx.cs,
    /// ZoetropePyre/PyreChunksFx.cs and Chunks/ChunkEmitter.cs each carry their own field and stamp it onto
    /// whatever SpriteRenderer they spawn — so there is no way to say "the smoke sits behind the flash, which
    /// sits behind the debris" as an authored order; every piece just gets one number somebody typed. A
    /// LayerSpec turns "which NAMED slot" into "which CONCRETE sortingOrder", in one place every tool can
    /// share: one Unity Sorting Layer for the whole composed effect, an ordered list of named slots inside
    /// it, and a step between consecutive slots wide enough that a caller can still sub-order several
    /// renderers within a single slot through the offset argument.
    /// The resolver deliberately DEGRADES rather than throwing: an unknown, renamed, null or empty layer name
    /// resolves to baseOrder instead of raising. This runs inside spawn loops, where a stale name left behind
    /// by an authoring rename must cost that one piece its depth — never kill the whole effect mid-burst.
    /// Zero dependencies by design, so any Laubrary tool can reference it without pulling in another module.
    [System.Serializable]
    public class LayerSpec
    {
        [Tooltip("The ONE Unity Sorting Layer the whole composed effect lives in. The named slots below only " +
                 "order renderers WITHIN this layer. Leave it empty to mean 'don't touch the renderer's " +
                 "sorting layer at all'.")]
        public string sortingLayerName = "Default";

        [Tooltip("sortingOrder of the first (index 0) layer. Also the fallback an unknown layer name " +
                 "degrades to.")]
        public int baseOrder = 0;

        [Tooltip("sortingOrder distance between consecutive layers. The gap is deliberate: it leaves room for " +
                 "a caller to sub-order several renderers inside one named layer via the offset argument.")]
        public int step = 10;

        [Tooltip("The ordered named slots. Index 0 draws furthest back; each following entry draws one step " +
                 "in front of the one before it.")]
        public List<string> layers = new List<string>();

        /// How many named slots exist. 0 when the list was never created.
        public int Count => layers != null ? layers.Count : 0;

        /// Index of layerName — case-sensitive exact match, first match wins. -1 for a null/empty name, an
        /// unknown name, or a null list.
        public int IndexOf(string layerName)
        {
            if (layers == null || string.IsNullOrEmpty(layerName)) return -1;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] == layerName) return i;
            return -1;
        }

        /// Whether layerName names one of the slots.
        public bool Has(string layerName) => IndexOf(layerName) >= 0;

        /// The core resolver: the concrete sortingOrder for a named slot, plus an optional offset for
        /// sub-ordering several renderers inside that one slot. An unknown/null/empty name degrades to
        /// baseOrder + offset rather than throwing.
        public int OrderOf(string layerName, int offset = 0) => OrderAt(IndexOf(layerName), offset);

        /// The same maths as OrderOf, addressed by raw index. An index outside [0, Count) degrades to
        /// baseOrder + offset, exactly like an unknown name does. The offset is confined to the slot's own
        /// band — see ClampToSlot below for why.
        public int OrderAt(int index, int offset = 0)
        {
            // The offset sub-orders renderers WITHIN one slot, so it must stay inside that slot's band: step
            // is only 10 by default while a caller's offset is typically a raw spawn index, and a formation
            // of up to 64 points would otherwise bleed straight through the next slot and silently INVERT the
            // depth the user authored (spawn 12 of the "behind" layer drawing in front of the "front" one).
            // Clamping into [0, step-1] keeps the authored stack order true and merely flattens the sub-order
            // of the overflow, which is the far smaller lie. step <= 1 leaves no room to sub-order at all.
            long order = (long)baseOrder + ClampToSlot(offset);
            if (index >= 0 && index < Count) order += (long)index * step;
            return ClampToSortingRange(order);
        }

        /// An offset confined to one slot's band: [0, step-1], or 0 when the step leaves no room.
        int ClampToSlot(int offset)
        {
            if (step <= 1) return 0;
            if (offset <= 0) return 0;
            return offset < step ? offset : step - 1;
        }

        /// Applies both halves of the spec to a renderer: its sortingOrder always, and its sorting layer only
        /// when this spec names a real one. No-op for a null renderer.
        public void Apply(Renderer renderer, string layerName, int offset = 0)
        {
            if (renderer == null) return;
            // An empty field means "leave it alone", and assigning an unknown sorting layer name is a Unity
            // warning we must not cause — so the renderer keeps its existing layer in both cases.
            if (!string.IsNullOrEmpty(sortingLayerName) && IsValidSortingLayer(sortingLayerName))
                renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = OrderOf(layerName, offset);
        }

        /// Whether name is a Sorting Layer that actually exists in Project Settings. NameToID returns 0 for an
        /// unknown name and 0 is also the id of "Default", so the name round-trip is what really validates it
        /// — and it avoids allocating the SortingLayer.layers array.
        public static bool IsValidSortingLayer(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return SortingLayer.IDToName(SortingLayer.NameToID(name)) == name;
        }

        /// desired if no slot already uses it, else "desired 2", "desired 3", … — the first free one. A
        /// null/empty desired falls back to "Layer". Never returns null.
        public string UniqueName(string desired)
        {
            string root = string.IsNullOrEmpty(desired) ? "Layer" : desired;
            if (!Has(root)) return root;
            for (int n = 2; n < int.MaxValue; n++)
            {
                string candidate = root + " " + n;
                if (!Has(candidate)) return candidate;
            }
            return root;
        }

        /// Moves one slot from one position to another (drag-reorder). False, and nothing changed, when the
        /// list is null, either index is outside [0, Count), or the move would be a no-op.
        public bool Move(int from, int to)
        {
            if (layers == null) return false;
            int count = layers.Count;
            if (from < 0 || from >= count || to < 0 || to >= count || from == to) return false;
            string moved = layers[from];
            layers.RemoveAt(from);
            layers.Insert(to, moved);
            return true;
        }

        /// Clamps into Unity's valid renderer sorting range, so an absurd step can never yield an
        /// out-of-range sortingOrder.
        static int ClampToSortingRange(long order)
        {
            if (order < short.MinValue) return short.MinValue;
            if (order > short.MaxValue) return short.MaxValue;
            return (int)order;
        }
    }
}
