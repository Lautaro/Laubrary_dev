// ChunkCardColors — the one colour each capability card wears, wherever that card is mentioned: its card
// header, its timing lane and its outline on the preview stage.
//
// The colour belongs to the CARD, not to its place in the stack: a colour that follows position changes owner
// the moment anything is reordered, and "which card is this?" is exactly the question the colour is there to
// answer. So the slot is stored on the capability (ChunkCapability.colorSlot) and only resolved here.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks.Editor
{
    internal static class ChunkCardColors
    {
        /// Eight colours that stay apart from each other on the dark stage and on the lanes' dark track. A
        /// recipe with more cards than this wraps round; by then the numbers on the stage carry identity.
        internal static readonly Color[] Palette =
        {
            new Color(0.36f, 0.62f, 0.92f), new Color(0.95f, 0.62f, 0.25f),
            new Color(0.45f, 0.83f, 0.52f), new Color(0.86f, 0.45f, 0.72f),
            new Color(0.85f, 0.82f, 0.36f), new Color(0.62f, 0.55f, 0.95f),
            new Color(0.35f, 0.80f, 0.80f), new Color(0.92f, 0.42f, 0.40f),
        };

        static readonly HashSet<int> Taken = new HashSet<int>();

        internal static Color For(ChunkSpec spec, ChunkCapability cap)
            => Palette[SlotOf(spec, cap) % Palette.Length];

        /// The capability's stored slot, or — for one authored before slots existed — the slot it WILL be given
        /// on the next structural edit: the lowest one free, handed out in stack order. Resolving without
        /// writing keeps simply opening an old recipe from dirtying it, and resolving in the same order the
        /// write uses means the colour on screen does not change when the write finally happens.
        internal static int SlotOf(ChunkSpec spec, ChunkCapability cap)
        {
            if (cap == null) return 0;
            if (cap.colorSlot >= 0) return cap.colorSlot;

            var stack = spec != null ? spec.capabilities : null;
            if (stack == null) return 0;

            Taken.Clear();
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] != null && stack[i].colorSlot >= 0) Taken.Add(stack[i].colorSlot);

            for (int i = 0; i < stack.Count; i++)
            {
                var other = stack[i];
                if (other == null || other.colorSlot >= 0) continue;
                int slot = LowestFree();
                if (ReferenceEquals(other, cap)) return slot;
                Taken.Add(slot);
            }
            return 0;
        }

        /// Write every unassigned slot, in the order SlotOf resolves them. Called inside a structural edit's
        /// own undo record, never on its own.
        internal static void AssignMissing(ChunkSpec spec)
        {
            var stack = spec != null ? spec.capabilities : null;
            if (stack == null) return;
            for (int i = 0; i < stack.Count; i++)
            {
                var cap = stack[i];
                if (cap != null && cap.colorSlot < 0) cap.colorSlot = SlotOf(spec, cap);
            }
        }

        /// The slot a card joining this recipe gets: the lowest one no card in it wears. Assigns the missing
        /// slots first, so an old card's resolved colour is never handed to the newcomer as well.
        internal static int NextFree(ChunkSpec spec)
        {
            AssignMissing(spec);
            Taken.Clear();
            var stack = spec != null ? spec.capabilities : null;
            if (stack != null)
                for (int i = 0; i < stack.Count; i++)
                    if (stack[i] != null && stack[i].colorSlot >= 0) Taken.Add(stack[i].colorSlot);
            return LowestFree();
        }

        static int LowestFree()
        {
            int slot = 0;
            while (Taken.Contains(slot)) slot++;
            return slot;
        }

        /// Text that reads on top of a palette colour.
        internal static Color InkOn(Color c)
            => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f > 0.55f
                ? new Color(0.08f, 0.08f, 0.1f)
                : Color.white;
    }
}
