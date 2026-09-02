// ChunkWindow.DebrisScatterCard — the card for a Debris Scatter.
//
// Placeholder: the card framework, the delay row and the header are already live; the dials themselves land
// with this capability's own task. Copy ChunkWindow.PyreBlastCard.cs for the row shapes and the Dial /
// DialAndRebuildCard discipline.
using Laubrary.Zui;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildDebrisScatterCard(VisualElement body, ChunkSpec c, DebrisScatter cap)
        {
            body.Add(Pending(cap));
        }

        // One honest line rather than an empty card: an empty body reads as a capability with nothing to
        // author, which is a different (and wrong) statement from "its dials are not here yet".
        static VisualElement Pending(ChunkCapability cap)
            => Z.Text("Dials not built yet.", ZuiText.Subtle,
                      $"This {cap.KindName} is authored and will fire; its own controls arrive with its card.");
    }
}
