// ChunkWindow.TrajectoryCard — the card for a Trajectory (a modifier that flies what a Pyre Blast spawned).
//
// Target first, per every modifier card's shape — which blast this flies. Then the same launch/flight shape
// PyreMotionModule always had: a launch cone (inherit the recipe's own aim, or a fixed angle), gravity/drag,
// and either "ride the target's own duration" or a fixed flight time — the one field here that is DERIVED
// rather than authored, so its dial only appears once the alternative (a fixed Life) is actually chosen.
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildTrajectoryCard(VisualElement body, ChunkSpec c, Trajectory cap)
        {
            string id = cap.id;

            body.Add(TargetRow(c, cap));

            body.Add(Z.HGroup(
                Z.MicroMinMax("Speed", cap.speedMin, cap.speedMax, 0f, 40f,
                    "The launch speed band the flight is drawn from, in world units/sec.",
                    (lo, hi) => Dial("Edit Trajectory Speed",
                        () => { cap.speedMin = Mathf.Max(0f, lo); cap.speedMax = Mathf.Max(cap.speedMin, hi); }),
                    180f, showValue: true, decimals: 2),
                Z.MicroSlider("Upward bias", cap.upwardBias, -10f, 10f,
                    "Extra upward velocity on top of the launch, so even a shallow cone pops.",
                    v => Dial("Edit Trajectory Bias", () => cap.upwardBias = v), 150f, showValue: true, decimals: 2)));

            body.Add(Z.Toggle("Inherit burst direction",
                "Launch along the recipe's own direction (so an aimed burst throws its blasts the way it " +
                "throws everything else) instead of the angle set below.",
                cap.inheritBurstDirection,
                v => DialAndRebuildCard(id, "Set Trajectory Direction Mode", () => cap.inheritBurstDirection = v)));

            if (!cap.inheritBurstDirection)
                body.Add(Z.MicroSlider("Direction", cap.directionDeg, 0f, 360f,
                    "Centre of the launch cone in degrees. 0 = right, 90 = up.",
                    v => Dial("Edit Trajectory Direction", () => cap.directionDeg = v),
                    150f, showValue: true, decimals: 0));

            body.Add(Z.MicroSlider("Spread", cap.spreadDeg, 0f, 180f,
                "Cone half-angle around the direction. 0 = a tight jet; 180 = a full circle.",
                v => Dial("Edit Trajectory Spread", () => cap.spreadDeg = v), 150f, showValue: true, decimals: 0));

            body.Add(Z.HGroup(
                Z.MicroSlider("Gravity", cap.gravity, 0f, 40f,
                    "Downward acceleration, world units/sec².",
                    v => Dial("Edit Trajectory Gravity", () => cap.gravity = v), 150f, showValue: true, decimals: 1),
                Z.MicroSlider("Drag", cap.drag, 0f, 5f,
                    "Air resistance: per-second damping of velocity.",
                    v => Dial("Edit Trajectory Drag", () => cap.drag = v), 150f, showValue: true, decimals: 2)));

            body.Add(Z.Toggle("Face velocity",
                "Point the blast along its travel direction instead of keeping its spawned rotation.",
                cap.faceVelocity, v => Dial("Set Trajectory Face Velocity", () => cap.faceVelocity = v)));

            body.Add(Z.Toggle("Until target ends",
                "Fly until the blast ends itself, rather than for a fixed time. The right answer for anything " +
                "that already knows when it is done.",
                cap.untilTargetEnds,
                v => DialAndRebuildCard(id, "Set Trajectory Life Mode", () => cap.untilTargetEnds = v)));

            if (!cap.untilTargetEnds)
                body.Add(Z.MicroSlider("Life", cap.lifeSeconds, 0.01f, 10f,
                    "Seconds of flight. The blast may still be playing — this only stops it moving.",
                    v => Dial("Edit Trajectory Life", () => cap.lifeSeconds = Mathf.Max(0.01f, v)),
                    150f, showValue: true, decimals: 2));

            body.Add(Z.Field("Seed",
                "Fixes the random speed and angle so every play is identical. 0 rerolls.",
                Z.Int(cap.seed, "Fixes the random speed and angle so every play is identical.",
                      v => Dial("Edit Trajectory Seed", () => cap.seed = v), 70f)));
        }
    }
}
