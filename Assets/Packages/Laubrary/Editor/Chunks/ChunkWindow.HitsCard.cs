// ChunkWindow.HitsCard — the card for Hits (a modifier).
//
// Placeholder: only the target row is live. See ChunkWindow.TrajectoryCard.cs.
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildHitsCard(VisualElement body, ChunkSpec c, Hits cap)
        {
            body.Add(TargetRow(c, cap));
            body.Add(Pending(cap));
        }
    }
}
