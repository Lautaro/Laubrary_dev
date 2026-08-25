# Chunks UI — the iterative pass programme (T-0081)

The user's brief, verbatim: *"The ui screenshot. Why is there a section called blast 3? What if user wants less or more? Also i see textinputs where the user has to type in what appears to be names of assets? I suspect this UI is not fit for user workflow. Project manage iterative passes and compare to well developed UI in Pyre to make the UI user friendly."*

This document is the project-management record: what the passes are, why they are in that order, and what each one actually changed. The evidence behind it lives in three sibling reports written by read-only investigators before any code was touched — `CHUNKS_UI_AUDIT.md` (defect list, cold walk), `PYRE_UI_PATTERNS.md` (the sixteen patterns Pyre uses that Chunks should adopt), and `CHUNK_ASSET_INVENTORY.md` (which chunk assets are real and which were junk).

## The three things the user actually said, restated as testable claims

1. **"Why is there a section called blast 3?"** — the blast list numbers its cards, and starts the numbering at 2 because the first blast is not in the list at all. A user reading *Pyre Spawn, Blast 2, Blast 3* is right to be confused: there is no Blast 1.
2. **"What if user wants less or more?"** — add and remove exist, but are not discoverable. This is the sharper half of the complaint and it is a missing-affordance problem, not a missing-feature problem.
3. **"Textinputs where the user has to type in what appears to be names of assets"** — the user is looking at an asset PICKER and reading it as a text field. That turned out to be a genuine library fault, not a misreading.

## Root cause of complaint 3, found before any code was written

`ZuiChip` is the toolkit control whose whole job is to make a reference to another asset visually unmistakable. Its own header comment promises "a small accentuated pill". It applies its entire appearance through USS class names — `zui-chip`, `zui-chip__label`, `zui-chip__thumb`, `zui-chip__dot`, `zui-chip--empty`, `zui-chip--drop` — and sets no inline styles at all.

**None of those selectors exist anywhere in the project.** `Assets/Packages/Laubrary/Zui/Toolkit/ZuiToolkit.uss` is the only stylesheet in the repository and it contains no `zui-chip` rule of any kind.

The consequence, measured in the laid-out window rather than inferred:

```
Label [zui-field__label] text='Blast' w=58 h=15
ZuiChip [zui-chip] w=137 h=79
  Image [unity-image,zui-chip__thumb] w=137 h=64
  Label [zui-chip__label] text='Old School Explo 2 Plus' w=137 h=15
```

An unstyled `VisualElement` defaults to `flex-direction: column`, so the thumbnail stacks *above* the name instead of sitting beside it in a pill, and nothing draws a border, a background or a hover state. On screen that is a blank dark rectangle with a word floating underneath — indistinguishable from an empty text field. That is exactly what the user saw, and it affects every reference in every Laubrary tool, not just Chunks.

## Why the passes are ordered the way they are

The fix that costs one file and reaches every tool goes first; the fix that restructures one window goes second; the fixes that need the first two to be settled go last.

## Pass status — all six ran, all six landed, editor compiles clean

| Pass | What it addresses | Files | Outcome |
|---|---|---|---|
| 1 — references look like references | complaint 3, and every other Laubrary tool at once | `Zui/Toolkit/ZuiToolkit.uss`, `Zui/Toolkit/ZuiChip.cs` | Done. The missing `.zui-chip*` block was written to match the toolkit's existing conventions, with the cross-axis `align-self` guard the rulebook demands (the same guard whose absence stretched every toggle in ~15 tools once before) and the sanctioned `zui-audit-allow-stretch` opt-out preserved. A trailing `▾` was added in `ZuiChip.cs` so a chip announces that clicking it opens a picker; no call site changed and the public API is untouched. Blast radius is 23 hand-written call sites across 4 tools plus every reflected reference routed through `LauAssetHook`. |
| 2 — blasts become a real list | complaints 1 and 2 | `Editor/Chunks/ChunkWindow.PyreSpawn.cs` | Done. One `Blasts` section holding uniform cards, the spec's own first blast included and built by the same builder as the rest. Header is `▾ ≡ ☑ name … ×` with the name inline and never numbered; fold state keyed on the module instance so it survives rebuild, undo and reorder; grip drag-reorder; `+ Add blast…` inside the section. Also fixed a latent trap the audit found: the remove button used to live in the section BODY, which is only built when the blast is enabled, so un-ticking a blast made it permanently undeletable. |
| 3 — blast identity outside the blast section | complaint 1 where it leaked | `Editor/Chunks/ChunkWindow.Layers.cs`, `.Timeline.cs`, `.PyreMotion.cs` | Done. The same ordinal was being generated in the Layers "Modules" box and on the Timeline lanes; both now show the blast's own name. The persisted timeline key is a separate argument and was left byte-for-byte alone, so no authored delay is orphaned. Pyre Movement also stopped greying itself out for specs whose blasts live in `blastGroups` — a configuration that always worked at runtime. |
| 4 — a chunk asset looks like what it does | the "I should understand what they do by looking at them" ask | `Runtime/Chunks/ChunkSpec.cs` | Done. The preview chain consulted only the legacy fields and was blind to every Chunks 2.0 module, so any spec without old-style art fell through to a fixed-seed swatch — which is why five of seven library entries were byte-identical. It now resolves the fragment slicer's subject, then the splash, then the blasts, and the fallback swatch is seeded per asset and shaped by the spec's own count, size and direction. |
| 5 — window fitness | "not fit for user workflow", the structural half | `Editor/Chunks/ChunkWindow.cs`, `.Preview.cs` | Done. A `ZuiSectionToggleBar` over all 17 sections, and the order inverted so the composed-effect modules come first — a user whose goal is "this character shatters" previously scrolled past nine sections of plain-debris physics to reach the first one that helps. Nine sections gained stable state keys. The preview stage's blur turned out to be fractional blow-up, not a missing preview, and now paints through `ZuiPixel` at a whole-number zoom. |
| 6 — blast panel polish | the dead space and the two-owners trap | `Editor/Chunks/ChunkWindow.PyreSpawn.cs`, `.Formation.cs` | Done. Rows packed; the offset control became a real 2D drag pad instead of an 18px strip that read as a slider; and the Spawn Formation module, which silently replaces the first blast at runtime, now says so on screen on both sides. The formation builder was parameterised, so a blast card gets its own live ring preview and about 80 lines of duplicated dials were deleted rather than copied again. |

Two smaller faults were fixed by the project manager directly, in files no pass owned. A predicate had been hand-copied from the runtime into the editor because of a scope boundary, which is the exact shape a silent drift takes — the runtime one is now the single owner. And the shared thumbnail resolver treated a fully transparent preview as a valid picture: measured live, the Pyre blast `Proper Blast` returned 0 opaque pixels out of 4096 at its first frame while `Old School Explo 2 Plus` returned 3607, so two chips side by side disagreed about whether a blast has a picture at all. The resolver now detects a blank preview and, for anything animatable, walks its own timeline for a frame that actually shows something — which is the right answer for an explosion that starts from nothing.

## What was verified, and how

**Verified by probe (the editor was driven and the result read back):** the editor compiles clean after every pass, confirmed by `recompile_status` rather than by reading a cumulative console buffer. The section toggle bar registers **17 buttons against 17 sections in the live tree** — the one cross-agent risk flagged in this batch was that a section could silently drop out of the bar, and it did not happen. `ZuiAudit` returns **zero findings** over the laid-out window; its 18 skipped subtrees were each identified and are all control internals (a hidden mode strip, fourteen micro-slider entry fields, two empty labels, one hidden preview island), not collapsed authored content, so the coverage is real rather than the meaningless clean that an un-laid-out audit returns.

**Verified by eye (a picture was captured and looked at):** the chip renders as a bordered pill with a 16px thumbnail and a picker caret, in the Chunks window and in an unrelated tool checked for blast radius. The blast section reads `Blasts` with cards named `Background` and `Between` — no ordinal anywhere. The remove `×`, the grip, the inline name and `+ Add blast…` are all present and placed as the rulebook's card layout specifies. The offset control draws a real 2D pad. The supersede warning reads *"Not firing here — the Spawn Formation section is firing this blast at its own points."* on a spec configured to trigger it. On a spec with no depth slots the blast card offers `+ Add depth slot…` instead of showing nothing, which is the affordance the cold walk found missing. The library now shows six visually distinct chunks, with `Floating Disc Blowup` showing the actual Floating Disc artwork.

**Not verified — nobody has used a mouse.** No human, and no agent, has clicked a control, dragged a card by its grip, dragged the 2D pad, or typed into a name field. Everything above was driven through code and read back or photographed. Drag-reorder in particular is a gesture with known sharp edges in this codebase (there are two documented, previously-paid-for bugs about pointer capture and popover anchoring) and it has not been exercised by hand.

**Not done, and deliberately so.** The window is still one tall column: with the composed modules switched on it is about 3900pt in a 1500pt window. The reorder and the toggle bar cut down the hunting, they do not cut down the height — a `Z.Split` with the preview stages in a right-hand pane is the next structural step and was not attempted here. Section icons were not added, because an unresolved icon name silently yields nothing and that cannot be checked without opening the editor on each one. The standalone Spawn Formation module was not retired despite now being redundant with the per-blast shape dials; authored assets depend on it and removing it is a data-affecting decision that belongs to Lautaro, not to this batch.

## Asset cleanup (the other open user request)

Requested: *"all chunk assets that are not tied to this demo should be deleted. If they are a part of the demo i should understand what they do by looking at them."*

Deleted, after a reference sweep over the whole `Assets` tree confirmed zero incoming references: `Assets/Demos/ChunksDemo/Composed.asset` (an unfinished draft superseded by Floating Disc Blowup, with no source visual set on its fragment slicer), `Assets/Chunks/Floating Disc Debris.asset` (an unreferenced scratch spec), and three screenshot PNGs that a previous agent had left inside the project's asset tree where Unity was importing them as textures — `FloatingDiscBlowup.png`, `FloatingDiscBlowup_Authoring.png`, `FloatingDiscBlowup_Sequence.png`.

Kept, because the demo scene actually fires them: `Sparks.asset` (left click), `WallDebris.asset` (Space), `SampledDebris.asset` (T), `Floating Disc Blowup.asset` (C).

Nothing was hard-deleted without a way back. `Composed.asset` and the three PNGs were untracked by git, so they were copied to `.agenthq/workspace/T-0081/removed/` before deletion; `Floating Disc Debris.asset` was tracked, so git already holds it.

The second half of that request — understanding an asset by looking at it — was not satisfied by deletion alone, and is Pass 4. Opening the library browser showed five of the seven specs rendering an identical scatter of white squares, including Floating Disc Blowup, whose whole identity is a character shattering with three layered explosions.
