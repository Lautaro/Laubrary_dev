// ChunkWindow.CuesCard — the card for Cues (a coordinator).
//
// Placeholder: see ChunkWindow.DebrisScatterCard.cs. The marker list lands with this capability's own task;
// its markers already draw on the timing ruler, so a cue authored elsewhere shows up there today.
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildCuesCard(VisualElement body, ChunkSpec c, Cues cap)
        {
            body.Add(Pending(cap));
        }
    }
}
