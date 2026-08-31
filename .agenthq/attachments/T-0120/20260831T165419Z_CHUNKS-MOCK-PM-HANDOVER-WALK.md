# Chunks mock — PM Handover Walk and programme state (2026-08-31)

Programme: T-0120 · stages T-0121 (done), T-0122 (done), T-0123 (**never ran** — see "Programme state" at the end).

This is not another code review. Stages 1 and 2 were reviewed by reading the source and driving the window through probes; both reviews ended with the same sentence — *"still nobody has clicked this window with a real mouse"*. This document closes that gap. Everything below happened through Win32 synthetic mouse input (`SetCursorPos` + `mouse_event`, DPI-aware) against the real floating window, captured with `PrintWindow`, exactly as a human's clicks and drags arrive. Where a claim is measured rather than seen, it says so.

Mock under test: `D:\UNITY\Laubrary Dev\Assets\ChunksMock\Editor\ChunksMockWindow.cs` (691 lines, unchanged since T-0122). Window: `Laubrary/Chunks Mock (Prototype)`.

## The user's sentence

> *"I want to build a crate-smash chunk: some debris layers, a ring of smoke puffs and a spark burst — and I want to see roughly where they land and in what order."*

The walk starts from "they have the editor open" and nothing else: no type names, no folder conventions, no knowledge of anything said in any earlier session.

## Walk 1 — cold, from the empty state, mouse only

Opened cold through the menu item with every prior instance closed. Every step below is a real click or a real drag; nothing was set programmatically.

| # | What they see | What they click | How they knew |
|---|---|---|---|
| 1 | `Recipe [Untitled Chunk] [Recipes…]`, a `Capabilities` heading, one button, an empty grid on the right | — | Empty state is legible; the one button is the only thing to press |
| 2 | A menu of four capability kinds | `Add capability…` → `Pyre Formation` | The button names the act, the menu names the choices |
| 3 | A card: Pyre picker, Count, Stagger, Pattern, Direction, Delay, Duration | `Green Lantern…` | Trailing `…` says it opens a chooser |
| 4 | Three mock Pyres, a check-mark on the current one | `Smoke Puff` | Check-mark tells them where they are |
| 5 | Radius replaces Direction; a ring of dots appears on the right | `Ring` | Two-option segmented, the current one latched |
| 6 | Count reads 11; the ring gains dots | drag the `Count` track | The value sits inside the track it drags |
| 7 | A second card with `Add layer` and nothing else | `Add capability…` → `Layer Plan` | Same route as step 2 |
| 8 | Two layer rows: name, opacity, blend, reorder, remove | `Add layer` ×2 | Same shape as step 7 |
| 9 | A third card, **and a `Timing` bar appears below the grid** | `Add capability…` → `Generic Particle Burst` | Nothing told them; it arrived because two capabilities now compete for time |
| 10 | The playhead moves, the readout says `0.54s` | drag the playhead | Triangle marker on a band |
| 11 | The burst card greys, the cone leaves the grid, **the `Timing` bar disappears** | the burst's `On` | Latched button, tooltip flips wording |

**Everything in that table was seen on screen.** The three-capability state is `pm-walk-built-by-mouse.png`.

## Walk 2 — the absence workflow, and re-entry

`Recipes…` → `Barrel Pop` (a recipe that needs one capability). Measured, not assumed: the window contains **one** Pyre Formation card, and there is **no** Layer Plan surface, **no** Palette surface, **no** Particle surface, and **no `Timing` section anywhere**. That is the question this whole exploration exists to answer, and it holds. Evidence: `pm-walk-barrel-pop.png`.

Then a forced domain reload (`EditorUtility.RequestScriptReload`). The window survived, rebuilt into the correct empty state, reported no compile errors, and was immediately operable — I loaded `Crate Smash` with the mouse straight afterwards. The recipe resets to empty, which is deliberate and documented in the source.

Flyout mechanics, both exercised by hand: **Escape dismisses** a flyout, and **clicking away dismisses** it. Both leave the window in its prior state.

## Mechanical audit

`ZuiAudit` after `ExpandAll`: **0 findings**, `ExpandAll` opened nothing (nothing was folded), `foldedSkipped = 13`. A non-zero skip count normally makes the result meaningless, so I enumerated every skipped subtree: all 15 `display:none` subtrees in the tree are a `Z.MicroSlider`'s alternate value display (the numeric-entry field that swaps with the value label) or an unused `ScrollView` scroller. **Nothing foldable was hidden, so the clean result is real.** Also measured: no horizontal scrollbar at 1000×720, and the widest row in the window (a layer row, 437.8pt) fits inside the left pane's 460pt minimum.

## Findings

Ordered by how much they should change the blueprint. P1–P3 are the ones that matter.

### P1 — the timing panel puts a RELATIVE dial against an ABSOLUTE ruler

Confirms the earlier E2, and it is worse than that review said: it shows with **untouched defaults**, not only with tuned values.

- New recipe, a formation and a burst, both `Delay 0.00`: the burst's band starts at **0.50s**.
- `Crate Smash`: the burst card reads `Delay 0.15` and its band starts at **0.65s** (`pm-walk-crate-smash-timing.png`).

Both readings are self-consistent only under a *sequential* model, where the delay is a gap after the previous capability ends. But the section is ruled in absolute seconds (`0.00s · 0.50s · 0.65s · 1.45s`), and the dial's own tooltip says *"Seconds of dead time before this capability begins"* — which, read against that ruler, is false. This breaches the guide's **"Label = action"** rule: a control must do what its label says.

The blueprint has to pick one, and this is a design decision, not a bug fix:

- **(a) Sequential.** Rename the dial to a sequential word (`Gap`, `After previous`), fix the tooltip, and label the ruler relative — capabilities are a chain and cannot overlap.
- **(b) Real multi-track clock.** `Delay` means an absolute start, capabilities may overlap, and the timeline needs one lane per capability rather than one shared strip.

(b) is the more capable model and the more expensive one. Whichever is chosen, the mock currently promises (b) with the ruler and delivers (a) with the layout.

### P2 — pressing "Add layer" while scrolled down throws the pane to the top and moves the button out from under the pointer

Confirms E3, which the previous review flagged as unreproducible at the size it tested. Reproduced and measured here at 900×480 with `Crate Smash`: scroll offset **44.44 → 0.00** on the click, and the `Add layer` button moved from y=136.4 to y=207.1 in content space. The user presses a button and it runs away.

Cause: every mutation calls `Rebuild()`, which recreates the whole left `ScrollView`. Breaches **"Stable workspace — contextual UI must NEVER move what the user is working on"**. For the blueprint: rebuild only the card that changed, or restore the scroll offset after a rebuild. Not optional in a real tool, where stacks are longer than a mock's.

### P3 — the Timing bar coming and going resizes the spatial guide, so the picture jumps

New. Turning the burst off removed the `Timing` section, the guide grew into the freed height, and the ring re-centred about 31pt lower — the thing the user is reading moved while they were reading it. Same rule as P2. Fix: reserve the timing lane's height permanently (toggle `visibility`, which keeps layout space, never `display`), or give the guide a fixed height and let the lane sit under it.

### P4 — single-control rows waste two thirds of the pane

Confirms E1, re-measured at the shipped 480pt split. The Formation's `Pattern` row uses ~145pt of the pane and the `Direction °`/`Radius` row ~155pt, each on a full row of its own; the Burst's `Shape` row uses ~200pt. Those pairs belong on one line. Breaches **"Space economy — pack rows, don't stack by default"**.

### P5 — the split position persists across a cold open, and there is no way back to the default

New. The window was opened cold, through the menu item, with every prior instance closed — and came up with a left pane of 710.7pt instead of the 480pt the code passes, because `Z.Split` restores its keyed view state from an earlier session. Harmless in a disposable mock (I dragged it back), but it means **"cold open" does not reproduce first-run**, and a real tool needs a reset: a user who drags the divider to an extreme currently has no route back except finding the divider again.

### P6 — a lone capability still shows Delay and Duration that dial nothing visible

Confirms E4. `Barrel Pop` has one timed capability, so there is no timeline — yet the card still offers `Delay` and `Duration`. Decide for the blueprint: hide them until a second timed capability exists, or keep them and draw a single-band timeline so they mean something.

### P7 — the timeline's fixed tick label is occluded by the playhead readout

New, low, and **not the mock's bug — it is in `ZuiTimeline`**, so fixing it there benefits every future tool. Measured with the playhead at 0.54s: the `0.50s` tick occupies x 722.2–752.9 and the playhead readout 744.4–775.5; the tick is present in the tree and invisible on screen.

### P8 — two layers added in a row are both called "New Layer"

New, low. The name is correctly a text field — that IS the declaration, and the rule about never typing a *reference* string does not apply. But the default collides on the second add, so a plan of five layers is five identical names. Number the default.

### P9 — no Undo anywhere

Unchanged from E5, and correct for a disposable mock. The blueprint must carry Undo-safety as an explicit requirement, because Laubrary mandates it for every tool and it is much cheaper to design in than to retrofit.

## Isolation

Re-verified in the working tree at the end of this session: `Assets/Packages/…` (the whole Laubrary package), `Packages/`, `Assets/Demos/` and every `CHANGELOG.md` are **unmodified**. The only working-tree change this programme has produced is the untracked `Assets/ChunksMock/` folder. `ChunkSpec`, the Chunks runtime and the production asset formats are untouched — the mock's own assembly (`ChunksMock.Editor`) references only the three ZUI assemblies, so it cannot name a production Chunks type even by accident.

## Programme state — what is still owed

**T-0123 has never run.** Between 15:31Z and 16:29Z it was dispatched eleven times and every dispatch died in about three seconds on `api_error_status=429 — monthly spend limit`. Its eleven sessions total 2.1 active minutes, it posted no handover, and it changed nothing: the only trace it left is a mouse-input harness in its workspace folder, which this walk reused rather than rewriting. The quota reset at 18:30 Europe/Stockholm and the fleet works again — this session is the proof — so T-0123 is expected to be dispatched normally as soon as this programme task stops being the in-progress task in the group.

What T-0123 still owes, unchanged by this walk except that P1–P8 above replace its guesswork with measurements: apply the row packing (P4), settle the timing model (P1), fix the scroll jump (P2) and the guide jump (P3), decide the lone-capability case (P6), and write the implementation blueprint including the Undo requirement (P9).

**The programme cannot close until that stage runs and is gated.** This document does not gate it; it removes the last excuse for not being able to.

## Verification buckets

**Verified by eye** (real window, real mouse, screenshots): the whole of walk 1 (empty state → three-capability recipe), the Pyre and Recipes flyouts with their check-marks, Escape and click-away dismissal, a MicroSlider drag, the pane splitter drag, the timeline playhead drag, the enable toggle actually disabling, the absence workflow under `Barrel Pop`, and the `Crate Smash` timing contradiction.

**Verified by measurement** (geometry read out of the live window): `ZuiAudit` clean with every skipped subtree accounted for, no horizontal scrollbar, the widest row inside its pane minimum, the scroll-offset reset in P2, the guide's re-centre in P3, the wasted row widths in P4, the persisted split in P5, and the label overlap in P7.

**Not verified:** nothing in the walk was left unverified. What is genuinely unknown is everything T-0123 has not built yet — none of the refinements, and no blueprint exists.
