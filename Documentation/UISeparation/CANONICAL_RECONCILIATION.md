# Canonical reconciliation — 30 September 2026 (AHQ T-0550, Phase 2.5)

The shared UI-separation foundation that was built and accepted in the separate working copy now also lives in the canonical project. This record says exactly what was brought across, what was deliberately left behind, how each collision with canonical's own concurrent work was decided, and what was re-measured here rather than assumed from the earlier run. It sits beside `ACCEPTANCE.md` (the separate copy's acceptance record, which is unchanged and still describes where the original verification happened) and does not replace it.

## What was brought across

The net effect of the separate copy's whole implementation history was taken as one change set and replayed here. It divides into four groups:

- **The shared foundation itself** — the control and factory code that no longer writes its own static appearance, and the nine new stylesheets that now carry it (ordinary factory defaults, card and header parts, generated-field layout, responsive columns, the shared asset-browser shell, the standard/band/envelope presentation profiles, and the small root sheet). Also the new per-window presentation snapshot used by detached surfaces, and the envelope presentation adapter.
- **The comparison harness** — the frozen copies of the original controls, their original stylesheets and their own independent copies of the skin textures, plus the two pilot windows and the two frozen consumer windows that make a side-by-side comparison possible at all.
- **The documentation and the source guard** — the authoring contract, the surface register and migration classification, the per-area notes, and the check that fails when new static appearance is written back into migrated code.
- **The visual-regression artefacts** — every reference image, comparison result and probe result from the original run, kept as the frozen record it is.

Nothing else came across. In particular: no project settings, no package manifest change, no audio project data, no editor build configuration, and not one of the several hundred files the owner already had modified in this project. That was verified rather than assumed — the change set's entire path list is confined to the package, the harness folder and the documentation folder, so the rest was structurally unreachable.

## Collisions with canonical's own work, and how each was decided

Canonical had moved on independently while the foundation was being built, so three files could not simply be copied over.

1. **The project's own agent instructions.** The separate copy carried an extra section at the top telling every agent that it *is* the separate copy and must never drive the canonical editor. That instruction is only true there, so it was deliberately not brought across. Canonical's instructions are untouched.

2. **The package changelog.** Both sides had added a new section at the very top of the unreleased notes — canonical a block of Pyre work, the separate copy the two UI-separation blocks. Both were kept: canonical's Pyre block stays first, the two UI-separation blocks follow it, and their headings were reworded from "isolated copy" to "canonical reconciliation" because that wording is no longer true. (Note for whoever reads the history: while this reconciliation was in progress, another session committed unrelated Pyre work and swept the already-reconciled changelog in with it, so the changelog half of this work is recorded under a Pyre commit message rather than this one. The text itself is correct and complete.)

3. **The generated-field layout code.** This is the only file both sides genuinely edited. The separate copy replaced a handful of hand-written layout instructions with style classes; canonical had separately added the "double-click a value to put it back to its starting number" behaviour. The two changes sit in different parts of the file and do not depend on each other, so both were kept in full. An independent review reconstructed all three versions and confirmed the result is an exact union with nothing dropped from either side, and the canonical editor then confirmed the double-click reset still works on the very controls whose appearance moved into stylesheets.

One smaller decision: the separate copy's folder-identity file for the editor scripts folder was not used. Canonical's own version was restored instead, so canonical keeps its own identity for that folder and no asset in either project gets re-pointed.

## Re-measured here, not inherited

The earlier acceptance was evidence from a different editor instance, so the same checks were run again in this project, and the earlier reference images were reused unchanged as the thing to match. They reproduce the earlier run's numbers exactly:

- Nine appearance comparisons that must be identical — both presentation modes at three window widths, plus numeric entry and the empty state — each measured **zero** differing pixels, with a zero-difference control proving the capture itself is noise-free. The one comparison that is *supposed* to differ (a window that asked for its own overriding style) differs by the same amount as before, to the pixel.
- The fourteen interaction checks pass, twice: once plainly and once with an overriding style active. The eight foundation checks pass. Two independently styled windows still coexist without leaking into each other, and a detached pop-up still carries its window's styling.
- The real, un-frozen asset window that the foundation touches is pixel-identical to a frozen copy of its old self, and still browses, edits, saves, undoes and reopens correctly.
- The source guard passes, and still fails its own self-test when new static appearance is deliberately injected.

A human then operated both pilot windows cold from a reopened state, twice, and drove a real task through to a visible result each time — change a value and watch the preview follow, toggle a setting, edit a range, a band strip and a curve, undo each, double-click to reset, switch presentation, resize down to the narrowest layout and back up to the widest, and return. A round trip through every one of those ends pixel-identical to where it started. Two real tools were also opened and looked at, and neither changed appearance.

Everything measured in this project is recorded separately under `Evidence/Canonical/`, so the original run's frozen evidence in `Evidence/` stays exactly as it was.

## Known limits of this reconciliation

- **The reproduction snippet that opens the pilot windows used to refuse to run outside the separate copy.** It now accepts both projects. Every other script in this folder was already written to work wherever it sits.
- **On this machine one comparison script needs Python warnings silenced** before it will run to completion, because a harmless library warning on the error stream is treated as failure by the shell. This is a machine quirk, not a defect in the work.
- **The appearance checks are order-sensitive.** Running the interaction checks immediately before a screenshot leaves hover state on a control and produces a small, real difference in a few dozen pixels around it. The procedure already requires reopening the windows before appearance checks for exactly this reason; do that rather than widening any tolerance.
- **The earlier acceptance's own limits still stand:** other display scales, other operating systems, consumer projects, runtime interfaces, and the great majority of individual tool windows are still later work. Several public calls that pass explicit sizes or geometry remain deliberate compatibility bridges, not finished migrations.
- Nothing was published to the two already-completed publishing scripts in this folder; they are kept as a record of the original delivery and should not be re-run.
