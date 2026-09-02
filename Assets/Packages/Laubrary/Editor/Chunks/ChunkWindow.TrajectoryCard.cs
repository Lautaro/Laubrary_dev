// ChunkWindow.TrajectoryCard — the card for a Trajectory (a modifier).
//
// Placeholder: only the target row is live, because a modifier that does not say what it acts on is not
// readable at all. The flight dials land with this capability's own task.
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildTrajectoryCard(VisualElement body, ChunkSpec c, Trajectory cap)
        {
            body.Add(TargetRow(c, cap));
            body.Add(Pending(cap));
        }
    }
}
