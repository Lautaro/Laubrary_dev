// ChunkWindow.FragmentFractureCard — the card for a Fragment Fracture.
//
// Placeholder: see ChunkWindow.DebrisScatterCard.cs. Copy ChunkWindow.PyreBlastCard.cs for the row shapes.
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildFragmentFractureCard(VisualElement body, ChunkSpec c, FragmentFracture cap)
        {
            body.Add(Pending(cap));
            var slot = LayerSlotRow(c, () => cap.layerName, v => cap.layerName = v);
            if (slot != null) body.Add(slot);
        }
    }
}
