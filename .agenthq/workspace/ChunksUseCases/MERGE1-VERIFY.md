# Merge 1 verification — T-0350/T-0352 (done), T-0355, T-0358, T-0361, T-0354, T-0357

All merged into `dev` at 7b6ab52c and compiled clean (editor + offline compile of 43 assemblies). PM already saw by eye (capture `scratchpad`, UC3): coloured card edges + number chips, ≡ grip, duplicate button, X/Y fields beside the Offset pad, lanes in card colours, numbered discs on the stage. Everything below is behaviour over time and still unverified. Test on the UseCases recipes or duplicates of them — never on the owner's `Assets/Demos/ChunksDemo/*.asset`.

## T-0355 — Timing band drag (use UC3 Timed Sequence)
1. Drag band C right ~0.3 s: band follows the pointer, is outlined, its start shows on the ruler; card C's Delay field and the stage update live; ruler scale does not jump.
2. Release, one Ctrl+Z (Undo.PerformUndo) restores the delay in one step; redo works.
3. Drag on the ruler / empty lane area scrubs and leaves bands alone; a click on a band without moving jumps the playhead.
4. Drag a band left past 0 → stops at 0.00.
5. Card Off → its dim band can't be dragged (press scrubs instead).
6. Hover: sideways-arrow cursor; band tooltip ends "Drag the band sideways to change when it starts."
7. Name column only as wide as the longest name; ruler 0.00s under the bars' left edge; a long card name widens it up to 120 pt then ellipsis.
8. Drag a band past the end of the clock, release → ruler lengthens on release.

## T-0358 — exact positions
1. Pyre Blast Offset row: pad + X/Y fields; scrub and type both; pad clamps at ±8.
2. On the stage, drag a blast disc (and a not-yet-fired ring): the card's X/Y update live; one Ctrl+Z undoes the whole drag.
3. The 8 other `Z.Pad` users look/behave as before (BackSplash position row, Mirage aim/position pads, Pyre fire-emitter offset, Zoetrope muzzle offset) — a capture of each is enough.

## T-0361 — New shows where it creates
1. Chunks → New: the folder label renders middle-elided with the full path on hover; the row doesn't wrap or overflow.
2. "Folder…" → pick a folder inside Assets → Create uses it. (The folder dialog is a native modal — drive it only if you can do it safely; otherwise set the remembered EditorPrefs value the way the code does and say so.)
3. A folder outside Assets is rejected with a console warning; previous folder kept.
4. Close/reopen → New still shows the chosen folder.
5. PM question to answer: after choosing a folder once, then opening an asset that lives in a DIFFERENT folder and pressing New — which folder is offered? Is that what a user expects?
6. Undo of a New create still works. Run ZuiAudit on the open create row.

## T-0354 — card identity
1. UC3: each card has a coloured left edge + number chip 1–4; its lane and stage outline share the colour.
2. Drag D's grip above C: D keeps its colour on card, lane and stage; one Ctrl+Z restores the order; clicking/dragging the grip never folds the card.
3. Duplicate a card: copy right after it, new colour, same values and Pyre; one Ctrl+Z removes it. On a Debris Scatter with modifiers (make one on a scratch recipe), editing the copy's modifier leaves the original unchanged.
4. UC2 Flung Pyres: every blast numbered; chips fade with their discs; no number floating at the end of an arc; at t=0.05 numbers not stacked.
5. Click a lane name → left pane scrolls to that card, unfolds and highlights it; ruler click still scrubs; band drag still works.
6. New recipe with one Pyre Blast, add a second: card 1's Name row does not move.
7. ZuiAudit on the fully expanded window: no card header wraps.

## T-0357 — Fling
1. Add capability menu: the entry reads "Fling" (greyed with a fling-worded reason when there is no Pyre Blast; real tooltip once there is).
2. Pyre Blast Ring ×6 + Fling: Direction Burst → Fixed → Outward. Burst mode shows a disabled "Burst direction" row with the live recipe value. Outward: scrubbing shows the ring's blasts flying away from the ring's own centre.
3. Spin range non-zero, Face velocity on and off: a short tick on each disc rotates over time in both cases.
4. UC2 (made before this change) still loads and plays the same (its old "inherit burst direction" became Direction = Burst).
5. A real Play-mode burst of an Outward + Spin recipe agrees with the preview (directions and visible rotation).

## Also
- Console clean of new errors/warnings through the whole walk.
- Report per check: pass / fail (with capture) / could not test (why).
