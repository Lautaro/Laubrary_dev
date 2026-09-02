// ChunkWindow.HitsCard — the card for Hits (a modifier that lets what a producer throws deal damage).
//
// Damage has no honest stable ceiling — it is a game's own balance number, not a physical quantity with a
// natural range — so per ui-layout-rules ("input if you'd only be guessing the cap") it is a plain scrub
// field, not a slider. Radius scale DOES have one (the runtime field itself carries [Range(0.1, 3)]), so it
// is a MicroSlider.
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildHitsCard(VisualElement body, ChunkSpec c, Hits cap)
        {
            body.Add(TargetRow(c, cap));

            body.Add(Z.HGroup(
                Z.Field("Damage",
                    "Damage one piece deals, once per target.",
                    Z.Float(cap.damage, "Damage one piece deals, once per target.",
                            v => Dial("Edit Hits Damage", () => cap.damage = Mathf.Max(0f, v)), 70f)),
                Z.MicroSlider("Radius scale", cap.radiusScale, 0.1f, 3f,
                    "Collider radius as a multiple of the piece's own current size, so it shrinks as the piece does.",
                    v => Dial("Edit Hits Radius Scale", () => cap.radiusScale = v),
                    170f, showValue: true, decimals: 2)));
        }
    }
}
