# Chunks editor — implementation blueprint (2026-08-31)

This is the accepted mock turned into a build spec. It supersedes the mock as the thing to implement against: the mock (`D:\UNITY\Laubrary Dev\Assets\ChunksMock\Editor\ChunksMockWindow.cs`) is disposable and stays disposable, and nothing in it is production code. Where this document says a thing is DECIDED, the mock demonstrates it and it was operated with a real mouse; where it says REQUIRED, the mock does not have it and the real tool must.

Programme: T-0120 → T-0121 (design) → T-0122 (mock stage 2) → T-0123 (this). Prior evidence: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0120\CHUNKS-MOCK-PM-HANDOVER-WALK.md` (the real-mouse handover walk that produced findings P1–P9) and `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0120\CHUNKS-MOCK-STAGE-2-GATE-REVIEW.md`.

## 1. What the tool is

An editor window that authors a **chunk recipe**: an ordered **stack of capabilities**, each an independent unit of authoring with its own fields, its own place in the recipe's clock, and its own contribution to a spatial guide. The user's sentence it exists to serve is *"I want to build a crate-smash chunk: some debris layers, a ring of smoke puffs and a spark burst — and I want to see roughly where they land and in what order."*

**The load-bearing rule, and the whole reason the mock existed: a recipe shows only the surfaces its capabilities bring.** A recipe that needs one Pyre Formation and nothing else has no layer surface, no palette surface, no particle surface and no timing surface anywhere in the window — not greyed out, not folded, absent. This was verified by measurement and by eye on the `Barrel Pop` recipe in both the mock's stage 2 and after this stage's refinements.

## 2. Layout — DECIDED

- Two panes in a `Z.Split`, left fixed, controls left and workspace right, divider persisted per key.
- Left pane: a one-row recipe identity strip (name field + a `Recipes…` picker button), then a `Capabilities` section holding one card per capability in stack order, then a single `Add capability…` button at the bottom of the stack. The left pane is a `ScrollView`.
- Left pane minimum width 460pt. This is measured, not taste: the widest row the window can hold is the layer row (name · opacity · blend · reorder · remove) at 397.3pt of content inside a 465.8pt card body, and it wraps below that.
- Right pane: a `Spatial guide` section that grows to fill the pane, and — only when the recipe holds two or more time-occupying capabilities — a `Timing` section beneath it. Never above it: a surface that appears must not shove the guide down the pane.
- Window minimum 820×480. Verified at that minimum: nothing wraps, nothing clips, no horizontal scrollbar, and the timing ruler is fully legible.

## 3. The capability card — DECIDED

- One `Z.BoxKeyed` per capability, keyed by a per-instance id so two cards of the same kind fold independently.
- Header is the standard fold: caret · icon · NAME · gap · one universal control (`On`) · `×`. Nothing else goes in a header.
- `On` disables the capability: it stays in the recipe, contributes nothing to the guide, and its card body goes inert so nothing in it can be dialled while it does nothing. The fold caret hides the card; it does not disable it. Those are two different acts and must stay two different controls.
- Card bodies are **packed rows**, not one control per line. Concretely, as shipped by this stage: Pyre Formation is 3 rows (Pyre + Pattern / Count + Stagger + Direction-or-Radius / Delay + Duration), Generic Particle Burst is 3 rows (Shape + Count / Speed + Lifetime + Spread / Delay + Duration), Palette Splash is 2 rows (Palette + Swatches / Spread + Delay + Duration). Before this stage they were 5, 4 and 3 rows with two thirds of the pane empty on several of them.
- A capability that occupies no time (a Layer Plan) has no timing row at all.

## 4. Timing — DECIDED: an ABSOLUTE, MULTI-LANE clock

This is the one place the mock's stage 2 was wrong, and the decision is the most consequential thing in this document.

**The finding.** Stage 2 drew the recipe as a single strip of consecutive bands (`Z.Timeline`). Under that control a capability's `Delay` behaved as a gap *after the previous capability ended*, so a burst reading `Delay 0.15` had its band start at 0.65s, and two capabilities both reading `Delay 0.00` had the second one start at 0.50s. The ruler was printed in absolute seconds and the dial's tooltip said "seconds of dead time before this capability begins" — so the label and the picture contradicted each other. That breaches the guide's **Label = action** rule.

**The decision: absolute.** `Delay` means *seconds from the start of the recipe until this capability begins*, capabilities may overlap freely, and the clock draws **one lane per time-occupying capability**, all lanes on the same ruler. Sequential chaining was the cheaper fix and it is the wrong model for this domain: a crate smash's sparks and its debris fire together, and a chain would force the third element of a three-part chunk to wait out the first two.

**Shipped and seen.** The mock now draws exactly this. `Crate Smash` renders a Pyre Formation lane 0.00→0.50s and a Generic Particle Burst lane 0.15→0.95s, on one 0.95s ruler, with a draggable shared playhead — the delay dial and the picture now agree. Evidence: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0123\shots\r01-crate-smash.png` and `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0123\shots\r06-burst-off-timing.png`.

**REQUIRED — a new ZUI control.** `ZuiTimeline` is by its own contract a single strip of consecutive bands, and it is right to stay that way; it cannot express lanes. The mock's multi-lane clock is hand-rolled inside the mock's own file, which is legitimate for a disposable prototype and is **not** acceptable in a shipped tool under the ZUI-first rule. The production tool must be built against a new ZUI control — a lane list over a shared ruler with one playhead — and the mock's `MockTimingTracks` is the prototype to lift it from. Do not copy the hand-roll into the real Chunks window.

**Behaviour the clock must keep** (all three are shipped in the mock and measured):

- The ruler's length is computed from **every** timed capability, enabled or not. Otherwise disabling one rescales the ruler and every remaining band jumps sideways.
- A disabled capability **keeps its lane**, drawn dim. Removing the lane would change the surface's height and move the guide above it.
- The first and last tick numbers are pulled inside the bar rather than centred on their tick, or `0.00s` renders as `.00s` against the left edge.
- A tick number that would fall under the playhead's own readout is dropped, not drawn behind it. (This is also the fix `ZuiTimeline` itself needs — see §7.)

## 5. The lone-capability case — DECIDED: hide the dials

A recipe with only one time-occupying capability draws no clock, so `Delay` and `Duration` would dial something the window cannot show. **They are hidden until a second timed capability exists.** This is the same rule as the absence rule in §1, applied inside a card rather than across the window. Verified on `Barrel Pop`: the Formation card shows two rows and no timing pair, and there is no `Timing` section anywhere. Evidence: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0123\shots\r10-barrel-pop.png`.

Note for the production tool: if a capability's duration turns out to mean something on its own (a burst's own lifetime, say) that value belongs to the capability's own fields, not to the timing pair. The timing pair exists only to place a capability against the others.

## 6. Stable workspace — DECIDED, and the rule the real tool is most likely to break

The guide's rule is that contextual UI must never move what the user is working on. The mock breached it in three ways and all three are now fixed and measured:

- **Scroll position survives a mutation.** Pressing `Add layer` while scrolled down used to throw the pane to the top (offset 44.44 → 0.00) and move the button 70pt out from under the pointer. It now holds: measured with a real mouse click at offset 120.00 on an eleven-layer plan, offset after the click 120.00. The button still moves down by exactly one row height, which is the row it just created — that is legible and is not a defect.
- **Toggling a capability does not resize the guide.** Turning the burst off used to remove the whole `Timing` section, letting the guide grow and re-centring the picture by ~31pt. The guide's rect is now byte-identical before and after a real-mouse toggle: `(484.9, 68.4, 511.1, 578.7)` both times.
- **The playhead survives a rebuild.** Dragging the playhead to 0.69s and then toggling a capability used to reset it to 0.00s. It is now carried across.

**REQUIRED in production.** The mock achieves all of this by carrying state by hand across a whole-window `Rebuild()`. That is fine at mock scale and will not hold for a real stack. The production tool should **rebuild only the card that changed**, and keep the carry-across as the backstop for the cases that genuinely need a full rebuild.

## 7. Requirements the mock does NOT satisfy

These are the things a reader must not assume are done because they saw them working in the mock.

1. **Undo — mandatory, and cheaper to design in than to retrofit.** The mock has none. Laubrary mandates Undo-safety for every editor tool: `Undo.RecordObject(asset, "…")` before every serialized edit, inside a `BeginChangeCheck`/`EndChangeCheck`; `Undo.RegisterCreatedObjectUndo` for created assets. Every dial, every capability add/remove/reorder, every layer add/remove/reorder and every picker choice is a serialized edit and needs it. `ZuiWindow` already rebuilds on undo/redo, so the window half is free — the recording is not.
2. **A multi-lane timeline control in ZUI** (§4). Prototype: `MockTimingTracks` in the mock file.
3. **A layout reset.** `Z.Split` persists the divider in EditorPrefs, so a **cold open is not a first run** — the window opened through its menu item with every prior instance closed came up with a 710.7pt left pane instead of the 480pt the code asks for. A user who drags the divider to an extreme has no route back. Give the tool a reset (double-click the divider is the cheapest honest one) or give `Z.Split` one, which fixes it for every Laubrary tool at once.
4. **`ZuiTimeline`'s occluded tick label.** With the playhead at 0.54s the `0.50s` tick occupied x 722.2–752.9 and the playhead readout 744.4–775.5: the tick is in the tree and invisible on screen. This is a `ZuiTimeline` bug, not a Chunks bug — it was deliberately not fixed in this task because that file is production ZUI and this task's scope excludes production changes. The mock's own clock drops such a tick rather than drawing it behind the readout; port that behaviour.
5. **Real data.** Every name in the mock (`Green Lantern`, `Ember`, the four capability kinds) is a placeholder string. The production tool binds to real assets, and every one of those pickers is a reference — so by the project's own standing rule it is a picker, never a typed string. Only the recipe name and a layer name are DECLARATIONS and therefore text fields.
6. **Persistence.** The mock's recipe is in-memory and a domain reload resets it, deliberately. The real tool owns an asset and must survive a reload, which also means every one of the carried-across UI states in §6 needs to be view state, not model state.

## 8. Open for the owner

- **The capability kinds are placeholders.** `Pyre Formation`, `Layer Plan`, `Palette Splash` and `Generic Particle Burst` were chosen to prove that four *unlike* capabilities compose. Which kinds the real tool ships is a design decision this programme did not make.
- **Does a Layer Plan need a spatial contribution?** It currently draws nothing in the guide, which is honest (it is not spatial) but leaves the guide silent about the largest part of a crate smash.
- **How the recipe relates to `ChunkSpec`.** The mock was structurally forbidden from naming a production Chunks type, so nothing in this blueprint assumes an answer.

## 9. Traceability

| Finding | Source | Resolution |
|---|---|---|
| P1 / E2 — relative dial against an absolute ruler | handover walk | DECIDED absolute multi-lane; built in the mock; ZUI control REQUIRED (§4) |
| P2 / E3 — Add layer throws the pane to the top | handover walk | Fixed; measured with a real mouse (§6) |
| P3 — timing bar coming and going resizes the guide | handover walk | Fixed; guide rect identical across a real-mouse toggle (§6) |
| P4 / E1 — single-control rows waste two thirds of the pane | handover walk | Fixed; three card layouts repacked (§3) |
| P5 — split position persists, no way back | handover walk | REQUIRED in production; not fixed in the mock (§7.3) |
| P6 / E4 — a lone capability shows dials that dial nothing | handover walk | DECIDED hide; built and verified on `Barrel Pop` (§5) |
| P7 — `ZuiTimeline` tick occluded by the playhead readout | handover walk | Out of scope (production ZUI); recorded as REQUIRED, behaviour prototyped (§7.4) |
| P8 — two layers both called "New Layer" | handover walk | Fixed; defaults are numbered (`Layer 3`, `Layer 4`, …) |
| P9 / E5 — no Undo | handover walk | Recorded as the first production requirement (§7.1) |

## 10. Verification state of this stage

**Verified by eye** (real window, real mouse, PrintWindow captures): the repacked card layouts at 1000×720 and at the 820×480 minimum; the multi-lane clock on `Crate Smash` with both lanes on one ruler; a playhead drag to 0.90s and to 0.69s; a real-mouse `On` toggle disabling the burst and dimming its lane without moving the guide; a real-mouse `Add layer` press on an eleven-layer plan while scrolled down; numbered layer defaults up to `Layer 11`; the `Barrel Pop` absence workflow; the empty state.

**Verified by measurement** (geometry read out of the live window): the scroll offset held at 120.00 across the click; the guide rect identical across the toggle; the ruler length unchanged when a lane is disabled; the playhead carried at 0.693087 across a rebuild; row widths and free space per card; `ZuiAudit` clean with all 15 hidden subtrees enumerated and accounted for (13 MicroSlider alternate value displays, 2 unused scrollers); no wrap and no horizontal scrollbar at the window minimum.

**Not verified:** everything in §7 — none of it is built. Undo in particular has no implementation anywhere in this programme.
