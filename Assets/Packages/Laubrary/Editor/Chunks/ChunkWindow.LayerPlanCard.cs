// ChunkWindow.LayerPlanCard — the card for a Layer Plan (a coordinator).
//
// Placeholder: see ChunkWindow.DebrisScatterCard.cs. The slot list itself lands with this capability's own
// task — including the rename rule (renaming a slot rewrites every capability that referenced the old name,
// inside the same undo step), which is the one thing a plain list of names cannot get right by accident.
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildLayerPlanCard(VisualElement body, ChunkSpec c, LayerPlan cap)
        {
            body.Add(Pending(cap));
        }
    }
}
