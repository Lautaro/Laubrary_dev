// ChunkWindow.TrailCard — the card for a Trail (a modifier).
//
// Placeholder: only the target row is live. See ChunkWindow.TrajectoryCard.cs.
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildTrailCard(VisualElement body, ChunkSpec c, Trail cap)
        {
            body.Add(TargetRow(c, cap));
            body.Add(Pending(cap));
        }
    }
}
