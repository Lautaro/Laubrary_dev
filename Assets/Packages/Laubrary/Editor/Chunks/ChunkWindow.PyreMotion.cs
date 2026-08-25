// ChunkWindow.PyreMotion — the Pyre Movement module's section (AgentHQ T-0035, design doc "Standalone modules"
// #3). Same shape as ChunkWindow.PyreSpawn.cs: one switchable Z.Section, every dial through Dial/DialAndRebuild.
//
// Unlike a module with nothing to say when off, this one's HEADER stays meaningful even while its own toggle is
// off (a user turning it on needs to see what it will do), and its BODY stays meaningful even when neither the
// Pyre Spawner nor the Spawn Formation is switched on elsewhere in the spec — this module only ever moves what
// one of those two spawns, so with neither on it has nothing to launch yet. Per the UI guide's "Stable
// workspace" rule that state is never expressed by the section vanishing (that reflows every panel below it
// every time a sibling toggle flips) — it's expressed by the section's own tooltip explaining the situation and
// by greying out its body, while the section itself and its header toggle stay exactly where they always are.
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildPyreMotion(VisualElement root, ChunkSpec c)
        {
            var m = c.pyreMotion;
            // T-0081 D-21: a spec whose ONLY enabled blasts live in c.blastGroups used to grey this whole
            // section out — c.pyreSpawn.enabled/c.spawnFormation.enabled say nothing about the extra groups.
            // That is wrong: PyreSpawnModule.SpawnOne calls ctx.Spec?.pyreMotion?.Apply(...) for every fired
            // PyreSpawnModule instance, and every entry of blastGroups is dispatched through that same Fire→
            // SpawnOne path (ChunkModules.Run, ChunkModules.cs:57-59) — motion applies to them exactly as it
            // does to the first blast. The gate must say so, or the UI claims a working configuration is dead.
            // The runtime's own predicate, not a copy of it — this section claims what will actually spawn.
            bool hasSpawner = c.pyreSpawn.enabled || c.spawnFormation.enabled ||
                              ChunkModules.AnyBlastGroupEnabled(c);

            var s = Z.Section("Pyre Movement",
                hasSpawner
                    ? "Gives each spawned blast real physical motion — velocity, gravity, drag, an arc — " +
                      "instead of playing it where it was spawned."
                    : "Gives each spawned blast real physical motion — velocity, gravity, drag, an arc — " +
                      "instead of playing it where it was spawned. Has nothing to move yet: turn on Pyre Spawn, " +
                      "Spawn Formation, or a blast group (below) so this has a blast to launch.",
                "chunks.pyremotion");
            s.SetHeaderToggle(m.enabled,
                "Give each spawned blast real physical motion instead of playing it where it was spawned.",
                v => DialAndRebuild("Pyre movement", () => m.enabled = v));
            root.Add(s);

            // Off = nothing below it means anything, so don't build it at all (the Cherry Framing / Pyre Spawn
            // shape). This early-out is about the module's OWN toggle, not the sibling-spawner state above —
            // that one is expressed inside the body, once there IS a body to grey out.
            if (!m.enabled) return;

            var body = new VisualElement();
            body.SetEnabled(hasSpawner);   // greyed, not hidden — see the file header comment
            s.Add(body);

            // ── launch ────────────────────────────────────────────────────────────
            body.Add(Z.Row(
                Z.Field("Speed",
                    "Slowest and fastest initial launch speed, world units per second.",
                    Z.MinMax(m.speedMin, m.speedMax, 0f, 30f,
                        "Slowest and fastest initial launch speed.",
                        (lo, hi) => Dial("Pyre motion speed", () => { m.speedMin = lo; m.speedMax = hi; }),
                        Wide, lowDefault: 3f, highDefault: 7f)),
                Z.HSpace(),
                Int2("Seed",
                    "Fixes the random speed and cone angle so every play resolves identically. 0 = reroll " +
                    "every time.",
                    m.seed, v => m.seed = v)));

            body.Add(Z.Row(
                Z.Toggle("Inherit Burst Direction",
                    "Cone centres on the burst's own direction instead of the Direction ° below, so a " +
                    "directional burst throws its blasts the same way it throws its debris.",
                    m.inheritBurstDirection,
                    v => DialAndRebuild("Pyre motion inherit direction", () => m.inheritBurstDirection = v)),
                Z.HSpace(),
                Z.MicroSlider("Spread °", m.spreadDeg, 0f, 180f,
                    "Cone half-angle around the direction. 0 = a tight jet; 180 = a full circle. Same meaning " +
                    "as a Chunk Spec's own Spread °.",
                    v => Dial("Pyre motion spread", () => m.spreadDeg = v), Wide, showValue: true, decimals: 0)));

            // Only meaningful (and only shown) while NOT inheriting — mirrors BuildPyreSpawnRotation's own
            // mode-conditional extra field.
            if (!m.inheritBurstDirection)
                body.Add(Z.MicroSlider("Direction °", m.directionDeg, 0f, 360f,
                    "Centre direction of the launch cone. 0 = right, 90 = up.",
                    v => Dial("Pyre motion direction", () => m.directionDeg = v), Wide, showValue: true,
                    decimals: 0));

            body.Add(Z.Row(
                Num2("Upward Bias", "Extra initial upward velocity added on top of the launch.",
                    m.upwardBias, v => m.upwardBias = v),
                Z.HSpace(),
                Num2("Gravity", "Downward acceleration. Higher = snappier arcs that fall fast.",
                    m.gravity, v => m.gravity = v)));

            // ── flight ────────────────────────────────────────────────────────────
            body.Add(Z.Row(
                Z.MicroSlider("Drag", m.drag, 0f, 5f,
                    "Air resistance: per-second exponential damping of velocity. 0 = none, ~1 = noticeable, " +
                    "~3 = soupy.",
                    v => Dial("Pyre motion drag", () => m.drag = v), Wide, showValue: true),
                Z.HSpace(),
                Z.Toggle("Face Velocity",
                    "Rotate the blast to point along its travel direction instead of keeping its spawned " +
                    "rotation.",
                    m.faceVelocity, v => Dial("Pyre motion face velocity", () => m.faceVelocity = v))));

            // ── duration ──────────────────────────────────────────────────────────
            body.Add(Z.Field("Duration",
                "Drive the motion until the blast ends itself, or for a fixed number of seconds.",
                Z.Segmented(m.untilTargetEnds ? 0 : 1, new[] { "Until it ends", "Fixed" },
                    "Until it ends = the runner lets go the moment the blast's own lifetime/pool release " +
                    "fires — the right choice for a blast that already knows when it's done. Fixed = an " +
                    "authored duration overrides that.",
                    v => DialAndRebuild("Pyre motion duration mode", () => m.untilTargetEnds = v == 0))));

            if (!m.untilTargetEnds)
                body.Add(Z.MicroSlider("Life (s)", m.lifeSeconds, 0.1f, 10f,
                    "Fixed-duration mode: how many seconds the launch motion runs before the runner lets go.",
                    v => Dial("Pyre motion life", () => m.lifeSeconds = Mathf.Max(0.01f, v)), Wide,
                    showValue: true, decimals: 2));
        }

    }
}
