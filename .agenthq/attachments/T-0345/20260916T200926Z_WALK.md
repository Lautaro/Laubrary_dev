# UC2 walk: spawn Pyres and fling them (T-0345)

Walked 2026-09-16 on the live Laubrary Dev editor, right after UC1 (T-0344), with the same harness. The harness drove the window's real elements: buttons, menu rows, sliders, toggles, the Pyre picker through its own callback, and Save Project. Recipe saved as `Assets/Demos/ChunksDemo/UseCases/UC2 Flung Pyres.asset`.

**The owner's sentence:** "Spawn Pyres and fling them."

## What was built

- **Pyre Blast "Six blasts":**
  - Blast: HollowBlast Plus.
  - Pattern: Ring. Count 6, Radius 0.5, Stagger 0.05.
  - On screen: 1.33 (see G2).
- **Trajectory:**
  - Target: Everything.
  - Speed 5–9, Upward bias 3.
  - Direction: inherited from the recipe's Burst direction, 90 (up). Spread 60.
  - Gravity 14, Drag 0.4, Face velocity on.
  - Until target ends: on.

The clock runs 1.58 s. There is no Timing section, correctly: a Trajectory occupies no time, so there is only one timed card.

## The walk

1. **New → "UC2 Flung Pyres" → Create**, with UC1 open. The file landed next to UC1, in `UseCases/`.
2. **Add capability….** Trajectory was greyed, with the reason "Nothing to fly yet — a trajectory moves what a Pyre Blast spawned, so add a Pyre Blast first". That reason is the only thing in the window that connects "fly" to Pyres.
   - The word "fling" appears in exactly one place: Fragment Fracture's tooltip ("Cuts a picture into pieces and **flings** them").
3. **Pyre Blast → pick HollowBlast Plus → Pattern Ring, Count 6, Radius 0.5, Stagger 0.05** (`01`).
   - At radius 0.5 the six footprint discs overlap into one blob.
4. **Add capability….** Trajectory was now enabled (`02`). Picked it.
   - The card opens with defaults that already fling: speed 3–7, gravity 20, drag 0.6, a 20° cone around the inherited direction (`03`).
5. **Tuned the flight:** Speed 5–9, Upward bias 3, Spread 60, Gravity 14, Drag 0.4, Face velocity on.
   - Toggling "Inherit burst direction" off makes a Direction dial appear on the card (`04`); I toggled it back on.
   - With it on, the direction actually used is the `Burst direction` dial under the preview, on the other pane.
6. **Scrubbed to 0.05 / 0.25 / 0.45 / 0.70 s** (`05`–`08`, montage `09`).
   - The ring points go off in order, each blast leaves a thin arc from its launch point, and the arcs bend down under gravity.
   - At 0.70 s the highest blast is cut by the stage's top edge. Blast 3 has already ended, but its number is still drawn at the end of its arc.
7. **Real burst in Play mode** (unsaved stage scene, `ChunkEmitter.Burst()`, timeScale 0.25; `20-play-a..e`).
   - Six HollowBlast players spawned 0.05 s apart, flew up and out, peaked, and fell (for example one went (0.5, 0) → (2.5, 2.9) → (5.4, 1.3)).
   - They were still flying at 1.23 s of game time. That is **past the recipe's 0.85 s clock**.
   - A second burst (`21-play2-*`) went mostly **left**: seed 0 rerolls every burst, while the preview shows one fixed roll.
8. **Measured the Pyre.** HollowBlast Plus is 16 frames at 12 fps = **1.33 s**, but the Pyre Blast card's "On screen" default is 0.6 s. So the preview's flights (which use "until target ends" = On screen) stop at about half the real distance.
9. **Found and changed "On screen" to 1.33 on the Pyre Blast card** (it is on the other card, not on the Trajectory card).
   - The clock went 0.85 → 1.58 s, and the arcs now climb, turn over and land below the origin like the real burst (`10`, `11`; before/after montage `12`).
   - The stage re-zoomed when this dial changed.
10. **Save Project.** Saved clean. The window had also come back on UC2 at the same playhead after the Play-mode domain reload.

**Step count for a human, from an open window:** about **24 actions**:

| Part | Actions |
|---|---|
| New / name / Create | 3 |
| Add + Pyre Blast | 2 |
| Chip + pick | 2 |
| Pattern, Count, Radius, Stagger | 4 |
| Add + Trajectory | 2 |
| Speed (2 handles) | 2 |
| Bias, Spread, Gravity, Drag, Face velocity | 5 |
| On screen | 1 |
| Play + save | 2 |
| Name (optional) | 1 |

The "On screen" step is only reached by someone who noticed the preview flights end early and knew where the number lives.

## Judging points from the task

- **How discoverable is "fling"?** Middling.
  - The kind is called Trajectory, and the menu greys it with a helpful reason until a Pyre Blast exists.
  - But the one "fling" in the window points at Fragment Fracture.
  - The fling dials sit on a second card rather than on the Pyre Blast card.
  - The direction the fling uses by default is a dial on the other pane (G4).
- **Does the preview show the flight?** Yes: every blast leaves its arc, and the discs ride along it over time.
  - But the flight length comes from a typed guess on the other card (G2), so with defaults the preview shows about half the real flight.
- **Do gravity and spin read naturally?**
  - Gravity does: the arcs bend and fall.
  - Spin does not exist for a flung Pyre. There is only Face velocity, and it is invisible on the round footprint discs (G5).

## Friction log

| # | Trying to | Had to | Should have | Severity | Rule |
|---|---|---|---|---|---|
| G1 | find "fling" | reason my way from "Trajectory" (greyed, with a reason) while the only "flings" in the window is Fragment Fracture's tooltip | Trajectory's menu tooltip and card title say what it does in the user's word: "Flings what a Pyre Blast spawned along an arc" | **MAJOR** | ui-rules §2 tooltip = effect; handover walk "how did they know to click it" |
| G2 | see how far the Pyres really fly | notice the arcs stop early, work out that "until target ends" means the "On screen" guess on the OTHER card (0.6 s), and type the Pyre's real length (1.33 s) myself | the Pyre's own play length used by default (Pyre knows frames/fps), or at least shown beside "On screen"; the Trajectory card saying which length it flies for | **MAJOR** | design §6 "time is real"; preview must match the burst |
| G3 | fling a ring's blasts outward, away from its centre | accept one shared cone for every point (the only workaround is Spread 180, which is random) | a direction mode "Burst / Fixed / Outward from pattern centre" | **MAJOR** | owner use case wording; design §3 Trajectory |
| G4 | change which way things fling | find that "Inherit burst direction" means the `Burst direction` dial under the preview, on the other pane; the card hides its own Direction dial while inheriting, and the tooltip does not say where the inherited value lives | show the inherited value on the card (read-only, or a link to the dial), and name the dial's location in the tooltip | **MAJOR** | handover walk step 3 reachability |
| G5 | make flung Pyres tumble / check Face velocity | nothing: there is no spin for a flung Pyre, and Face velocity cannot be seen on a round disc | a Spin band on Trajectory, and an orientation tick on each blast disc | MINOR | design §8.2 (spin visible) |
| G6 | trust the preview's directions | discover in Play mode that seed 0 rerolls every burst (two bursts went right, then left) while the preview shows one fixed roll | the Seed field saying "0 = different every burst; the preview shows one example", or a reroll button on the stage | MINOR | ui-rules §2 conditional tooltips |
| G7 | watch the whole flight | accept a blast leaving the top of the stage (`08`); changing "On screen" re-zooms everything (`12`) | framing that keeps every blast's peak inside, and zoom changes that do not happen mid-look | MINOR | design §8.17 |
| G8 | read the stage after a blast ends | ignore its number floating at the end of the arc (`08`, "3") | the label goes with its disc | MINOR | — |
| G9 | read a tight ring | see six Pyre-sized discs overlap into one blob with stacked numbers (`05`) | numbers offset or placed at the ring point rather than the disc centre | MINOR | ui-rules §3 legible |
| G10 | scan the Trajectory card | read Spread, Inherit, Face velocity and Until target ends each on its own row | toggles share a row; Spread sits beside Direction | MINOR | ui-rules §3 short fields share a row |
| G11 | nothing | see a Target choice "Everything / Six blasts" when there is exactly one possible target | show Target only when there are two or more candidates | MINOR | ui-rules §7 |

UC1's frictions F3 (no numbers on the Offset pad), F4 (disc↔card identity), F6 (discs, not pictures), F9–F14 all apply here unchanged. They are not repeated.

## Verdict: buildable with workarounds

The fling itself works with the defaults and runs correctly in Play mode. The preview only tells the truth once "On screen" is corrected by hand (G2), and flinging outward from a pattern is not possible (G3).

## Verified by probe
- The recipe read back after every edit.
- The clock was 0.85 → 1.58 s after On screen = 1.33. No Timing section with Pyre Blast + Trajectory.
- The window came back on UC2 at the same playhead after the Play-mode domain reload.
- In Play mode, six Pyre players spawned 0.05 s apart (draw orders 500–505), then rose, peaked and fell, and were still alive at 1.23 s of game time.
- A second burst rolled different directions.
- HollowBlast Plus = 16 frames @ 12 fps = 1.33 s.
- Save wrote the UC2 file clean.

## Verified by eye
- **Window captures** `01`–`04`: ring with no flight; Add menu with Trajectory enabled; Trajectory defaults; the Direction dial appearing when inherit is off.
- **Stage over time** `05`–`09`: arcs, ordered launches, clipping at the top edge, the leftover label.
- **On-screen comparison** `12`: half flight vs full flight.
- **Play-mode renders** `20-play-*`: six blasts flung up and out.

## Not verified
- No human mouse (sliders, pad, lanes).
- Mirage handoff was not run: it opens MirageStage, which has uncommitted owner edits.
- The burst was fired from code in an unsaved scene at timeScale 0.25.
- Face velocity's visual effect on the real Pyre sprite was not examined (HollowBlast is round).
