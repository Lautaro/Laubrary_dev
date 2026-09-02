// ChunkWindow.TrailCard — the card for a Trail (a modifier that leaves puffs behind what a producer throws).
//
// Three fields, in the order the design decides them: what this rides on (Target), what a puff actually IS
// (an IChunkTrailSource asset — always a picker, per the "never type a reference string" rule, never the
// PyreBlastCard alternates-pool shape since a Trail only ever has one source), and how often it drops one.
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildTrailCard(VisualElement body, ChunkSpec c, Trail cap)
        {
            body.Add(TargetRow(c, cap));

            body.Add(Z.Field("Trail source",
                "What each puff is — an asset that can spawn one (a Pyre Blast Trail Source, a fire→smoke blast).",
                AssetPicker(cap.trailSource, o => Dial("Set Trail Source", () => cap.trailSource = o),
                            typeof(IChunkTrailSource), "Trail Source",
                            "What each puff is — an asset that can spawn one.")));

            body.Add(Z.MicroSlider("Interval", cap.interval, 0.01f, 2f,
                "Seconds between puffs. A small interval reads near-continuous; a large one reads as discrete puffs.",
                v => Dial("Edit Trail Interval", () => cap.interval = Mathf.Max(0.01f, v)),
                150f, showValue: true, decimals: 2));
        }
    }
}
