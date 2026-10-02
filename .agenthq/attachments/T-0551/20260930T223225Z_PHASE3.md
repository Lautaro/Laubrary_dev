# Phase 3 — simpler tools migrated, and the reference pair for phase 4

AHQ task T-0551, group `LaubraryUssStyling-2026-09-30`. This phase does two separate jobs: it moves five smaller tools onto the shared semantic stylesheet without changing how they look, and it freezes the two richest tools as the pictures that later phases are judged against.

## What changed, in plain terms

Until now, each tool window spelled out its own shape in code: "this side is 320 wide", "this preview is at least 220 tall", "this strip must not shrink", "a picked row is washed this particular blue". Every tool repeated the same handful of numbers, and because they were set directly on the elements, nothing outside the tool could change them — a stylesheet cannot outrank a value written straight onto an element.

Those repeated numbers now live once, in a stylesheet, behind names that say what the thing IS rather than what size it is: a tool shell, the controls side, the preview pane, the strip of chrome, a row's drag handle, a row's name field, a picked row. Each of the five tools also announces itself on its own outermost element, so a future stylesheet can say "in this one tool, sliders look like that" without any control ever having to know which tool it is sitting in.

Nothing about the five tools looks or behaves differently. That is the whole point of the phase, and it was measured rather than assumed.

## The five surfaces

| Surface | What it is | What moved |
| --- | --- | --- |
| BackSplash | Backdrop asset editor | Full-height shell, the scrolling controls column, the fixed-height preview strip, the clipping of the backdrop view and the free placement of the image inside it |
| Cabinets | A component inspector, not a window | The single breathing-room gap above its block |
| Choreographer | Group-movement authoring with a painted stage | Shell, the controls-beside-stage split, the fixed-width controls side, the scrolling column, the stage's clipping, and the free placement of its overlaid labels and sprites |
| Lathe | Solid authoring with a 3D preview | Shell, the controls side's floor width, the growing column, the preview pane and its minimum height, the non-shrinking chrome strip, and the row grip / row name / picked-row appearance |
| Lathe Mold | The mold variant of the same window | The same set, with a taller preview |
| Tapestry | Layered texture authoring | The same set as Lathe |

Deliberately left in code, and recorded as reviewed exceptions: anything positioned or sized from a measurement or an authored value (the backdrop image's placement under zoom, the stage's plotted labels and sprites), asset data that merely happens to be a colour (a backdrop's own colour, a preview's seeded subject matter), and the Choreographer stage's painter palette, which belongs to the later dense-surface phase. One row's name field in the mold window kept its inline sizing on purpose: the shared name rule also restores a shrink behaviour that window never had, so adopting it would have been a real change rather than a like-for-like move.

## How "it still looks the same" was actually checked

Two independent measurements per window state, fifteen states in all — each of the five surfaces populated and empty, and the wider ones also at a narrow width.

1. **Every element's resolved appearance** is dumped to a file: its position and size, and its resolved colours, borders, padding, margins, flex behaviour, minimums, font and alignment. If every element still resolves to the same numbers, the appearance is preserved by construction, and any drift names the exact element and the exact property rather than a smudge of pixels.
2. **A photograph of the window's real client area**, compared pixel for pixel.

The before-pictures were taken with the migration temporarily set aside and then restored, inside one warm editor session, so that a first-draw difference in shared chrome could not be mistaken for a migration difference. That mattered: the first attempt compared a cold run against a warm one, and a shared tag row that had not drawn yet on the cold run shifted everything below it, producing six percent of pixels changed that had nothing to do with this work.

**Result: 13 of the 15 states are identical — zero property differences and zero changed pixels.**

The two that are not identical are the Choreographer with an asset loaded, at both widths. Both differ only in the width of two pieces of live text (a numeric field and the stage's legend), because that window animates while it is open. A control run — the same build photographed twice against itself — reproduced the same differences at the same magnitude, which is what shows they are the window moving, not the migration. The same control run also reproduced the large pixel difference seen on one Lathe state, which is its 3D preview not rendering identically twice; that state's element measurements are identical.

## Pyre and Chunks as the reference pair

These two are the densest, most heavily used authoring surfaces in the library, and they are what "a Laubrary tool should look like this" means in practice. They were NOT changed in this phase. They were photographed and measured as they stand, so that the next phase — which does touch dense surfaces — has something concrete to be held against rather than a description.

Captured, at real window sizes (1400 and 900 wide, 900 tall):

- Pyre with a real effect loaded, wide and narrow; with nothing loaded; and with a different layer picked.
- Chunks with a real recipe loaded, wide and narrow; and with nothing loaded.

Each is both a picture and a full element-by-element measurement, so a later phase can compare numbers, not just eyeball a screenshot. The populated captures happen to contain, in one frame, most of the states the phase brief asks for: sections folded and unfolded, a picked item in a list, a chosen option in a group of alternatives, and a disabled control that is present but greyed.

**Not captured, and why:** a hovered control, a row mid-drag, and an error state. All three need a real pointer held in a real place, or a deliberately broken asset, and neither can be produced by the scripted route used here without staging something that would not be the tool's own behaviour. They remain a by-hand check.

Zounds is explicitly not part of this reference. It keeps its own Colorful skin and is judged against itself.

## Where the evidence is

- Measurements, pictures and the comparison record: `D:\UNITY\Laubrary Dev\Documentation\UISeparation\Evidence\Phase3\`, with the summary in `D:\UNITY\Laubrary Dev\Documentation\UISeparation\Evidence\Phase3\PARITY.txt`.
- The reference pair is every file in that folder whose name begins `ref-`.
- The comparison itself: `D:\UNITY\Laubrary Dev\Documentation\UISeparation\compare_phase3.py`.
- The capture harness: `D:\UNITY\Laubrary Dev\Assets\Editor\UISeparationPhase3\`. It is evidence apparatus, registers no menu items, and is not a surface to migrate.
- The new shared rules: `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Zui\Toolkit\ZuiFoundationToolShell.uss`.

## One thing worth knowing before the next phase

A semantic rule named after a single class can be silently outranked. The toolkit already pins every field to no-grow and no-shrink through a rule that reaches from the toolkit root, and a plain one-class rule cannot outrank that: the row-name rule applied its width and its minimum but not its stretch, and the window looked subtly wrong while every class was demonstrably present. The fix is to match that reach. Expect the same trap on any new rule that tries to give a field back a behaviour the toolkit took away, and check the resolved value rather than the class list.
