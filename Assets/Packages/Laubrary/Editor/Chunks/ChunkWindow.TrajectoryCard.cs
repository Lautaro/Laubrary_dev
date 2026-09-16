// ChunkWindow.TrajectoryCard — the card for a Fling (a Trajectory modifier that flies what a Pyre Blast spawned).
//
// Target first, per every modifier card's shape — which blast this flies. Then the same launch/flight shape
// PyreMotionModule always had: a launch cone (Burst / Fixed / Outward from pattern centre), gravity/drag/spin,
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
                    (lo, hi) => Dial("Edit Fling Speed",
                        () => { cap.speedMin = Mathf.Max(0f, lo); cap.speedMax = Mathf.Max(cap.speedMin, hi); }),
                    180f, showValue: true, decimals: 2),
                Z.MicroSlider("Upward bias", cap.upwardBias, -10f, 10f,
                    "Extra upward velocity on top of the launch, so even a shallow cone pops.",
                    v => Dial("Edit Fling Bias", () => cap.upwardBias = v), 150f, showValue: true, decimals: 2)));

            body.Add(Z.Field("Direction",
                "How the launch cone is aimed.",
                Z.Segmented((int)cap.directionMode, new[] { "Burst", "Fixed", "Outward" },
                    "Burst follows the recipe's own aim (mirrored below, read-only). Fixed uses the angle set " +
                    "below. Outward from pattern centre points each spawn away from where its OWN pattern " +
                    "started, so a ring flies out like an explosion instead of one shared direction.",
                    i => DialAndRebuildCard(id, "Set Fling Direction Mode",
                                            () => cap.directionMode = (TrajectoryDirectionMode)i))));

            if (cap.directionMode == TrajectoryDirectionMode.Burst)
            {
                // Read-only mirror of the recipe's own aim — before this it was invisible on the card, and
                // the only way to learn it existed was to notice the Direction dial disappear (WALK.md G4).
                var mirror = Z.MicroSlider("Burst direction", c.directionDeg, 0f, 360f,
                    "The recipe's own aim, read-only here — drag the 'Burst direction' dial under the " +
                    "preview, on the stage pane, to change what this flies along.",
                    _ => { }, 150f, showValue: true, decimals: 0);
                mirror.SetEnabled(false);
                body.Add(mirror);

                // The Burst-direction dial this mirrors lives on the Preview pane, not this card, so dragging
                // it Dials the whole window but never rebuilds THIS card — without a live hook the mirror kept
                // showing whatever value the card happened to be built with (T-0368/1). Every Dial() already
                // calls RefreshCardStates() for exactly this reason (see the source-state line above); reuse
                // that same per-card refresh hook here instead of inventing a second notification path.
                void RefreshMirror() => mirror.value = (Current != null ? Current : c).directionDeg;
                RefreshMirror();
                cardStates[id] = RefreshMirror;
            }
            else
            {
                // No mirror on this build of the card — drop any refresh left over from a previous build
                // where the mode WAS Burst, so RefreshCardStates() never calls into a detached element.
                cardStates.Remove(id);

                if (cap.directionMode == TrajectoryDirectionMode.Fixed)
                    body.Add(Z.MicroSlider("Direction", cap.directionDeg, 0f, 360f,
                        "Centre of the launch cone in degrees. 0 = right, 90 = up.",
                        v => Dial("Edit Fling Direction", () => cap.directionDeg = v),
                        150f, showValue: true, decimals: 0));
            }

            body.Add(Z.MicroSlider("Spread", cap.spreadDeg, 0f, 180f,
                "Cone half-angle around the direction. 0 = a tight jet; 180 = a full circle.",
                v => Dial("Edit Fling Spread", () => cap.spreadDeg = v), 150f, showValue: true, decimals: 0));

            body.Add(Z.HGroup(
                Z.MicroSlider("Gravity", cap.gravity, 0f, 40f,
                    "Downward acceleration, world units/sec².",
                    v => Dial("Edit Fling Gravity", () => cap.gravity = v), 150f, showValue: true, decimals: 1),
                Z.MicroSlider("Drag", cap.drag, 0f, 5f,
                    "Air resistance: per-second damping of velocity.",
                    v => Dial("Edit Fling Drag", () => cap.drag = v), 150f, showValue: true, decimals: 2)));

            body.Add(Z.MicroMinMax("Spin", cap.spinDegMin, cap.spinDegMax, -720f, 720f,
                "Rotation speed band the flight is drawn from, degrees/sec. 0-0 leaves the blast's spawned " +
                "rotation alone (or Face velocity's, if that is on) — the old, spinless behaviour.",
                (lo, hi) => Dial("Edit Fling Spin", () => { cap.spinDegMin = lo; cap.spinDegMax = hi; }),
                180f, showValue: true, decimals: 0));

            body.Add(Z.Toggle("Face velocity",
                "Point the blast along its travel direction instead of keeping its spawned rotation. Combines " +
                "with Spin above rather than replacing it.",
                cap.faceVelocity, v => Dial("Set Fling Face Velocity", () => cap.faceVelocity = v)));

            body.Add(Z.Toggle("Until target ends",
                "Fly until the blast ends itself, rather than for a fixed time. The right answer for anything " +
                "that already knows when it is done.",
                cap.untilTargetEnds,
                v => DialAndRebuildCard(id, "Set Fling Life Mode", () => cap.untilTargetEnds = v)));

            if (!cap.untilTargetEnds)
                body.Add(Z.MicroSlider("Life", cap.lifeSeconds, 0.01f, 10f,
                    "Seconds of flight. The blast may still be playing — this only stops it moving.",
                    v => Dial("Edit Fling Life", () => cap.lifeSeconds = Mathf.Max(0.01f, v)),
                    150f, showValue: true, decimals: 2));

            body.Add(SeedField(cap.seed, "the flight's speed, angle and spin", "Edit Fling Seed", v => cap.seed = v));
        }
    }
}
