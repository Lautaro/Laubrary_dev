// ChunkWindow.PaletteSplashCard — the card for a Palette Splash.
//
// Placeholder: see ChunkWindow.DebrisScatterCard.cs. Copy ChunkWindow.PyreBlastCard.cs for the row shapes.
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildPaletteSplashCard(VisualElement body, ChunkSpec c, PaletteSplash cap)
        {
            body.Add(Pending(cap));
            var slot = LayerSlotRow(c, () => cap.layerName, v => cap.layerName = v);
            if (slot != null) body.Add(slot);
        }
    }
}
