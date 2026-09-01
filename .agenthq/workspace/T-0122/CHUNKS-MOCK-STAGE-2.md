# Chunks mock editor — stage 2: capability stack, shared timeline, generic particles

**Task:** T-0122 (group `ChunksMock-2026-08-31`) · **Built:** 2026-08-31 · **Artefact:** `D:\UNITY\Laubrary Dev\Assets\ChunksMock\Editor\ChunksMockWindow.cs` (+ `D:\UNITY\Laubrary Dev\Assets\ChunksMock\Editor\ChunksMock.Editor.asmdef`)

Stage 1 (T-0121) proved one capability could be edited. Stage 2 answers the question the whole exploration exists for: **can a Chunks recipe be assembled from a stack of independent capabilities, and does a recipe that needs only one of them stay free of every surface belonging to the others?** Both halves are now on screen and measured.

---

## 1. What the window does now

| Surface | Behaviour |
|---|---|
| **Recipe row** | A name field (this is where the name is *declared*, so a text field is correct) plus `Recipes…` — a flyout with three mock recipes, check-marked on the current one, and `New empty recipe`. Stage 1 had no create/open/name path at all. |
| **Capability stack** | N cards in list order, plus a permanently-present `Add capability…` flyout offering all four kinds. Stage 1 could hold exactly one capability, so the stack was never visible. |
| **Card header** | `fold · icon · NAME · flexible gap · [On] · [×] · ?`. The fold caret hides the card; the **On** toggle enables/disables the capability. |
| **Pyre Formation** | Pyre picker, Count, Stagger, Line/Ring, and Direction°/Radius (whichever the pattern uses). |
| **Layer Plan** | Ordered layers, each one row: name · Opacity · Normal/Add/Multiply · ▲ ▼ · ×. ▲/▼ disable at the ends. Not time-occupying, so its card carries no timing row. |
| **Palette Splash** | Mock palette picker, Swatches, Spread. |
| **Generic Particle Burst** | Cone/Disc/Sphere, Count, Speed, Lifetime, Spread° — deliberately *not* a Pyre and not a sprite sheet. |
| **Shared timeline** | One `Timing` section below the workspace, present **only** when two or more enabled time-occupying capabilities exist. Each contributes a dim delay band and a coloured duration band. |
| **Spatial guide** | Draws every *enabled* capability: formation spawns with order arrows, a burst fan whose spread/speed/shape it obeys, a splash ring of swatches. A Layer Plan draws nothing, on purpose. |

## 2. The three gate findings this task carried

**A1 — no create/open/name path.** Closed. The recipe row is the affordance; the cold empty state now contains two real buttons (`Recipes…`, `Add capability…`) instead of one, verified by a cold-open probe through the real `Open()` menu method.

**A2 — only one capability could exist.** Closed. `MockRecipe` holds a `List<MockCapability>`; the add button is never swapped out; three cards were driven live and all three rendered independently.

**A3 — the toggle only folded.** Closed, and measured rather than asserted. Disabling the Generic Particle Burst in the three-capability recipe left the card in place (3 cards before and after) but dropped the capability out of the shared timeline — which then fell below the two-capability threshold and disappeared entirely — and made the card body inert (`bodyEnabled` went `True,True,True` → `True,True,False` → `True,True,True`).

**D2 — isolation was convention, not structure.** Closed. The mock now compiles into its own assembly and its referenced-assembly list, read out of the live domain, is `mscorlib; System; com.Lautaro-Arino.Laubrary.Zui.Editor; UnityEditor.CoreModule; UnityEngine.UIElementsModule; UnityEngine.CoreModule` — no production Chunks assembly, and the compiler now enforces that rather than a comment asking for it. The namespace moved from `Laubrary.Chunks.Mock.Editor` to `ChunksMock.Editor`, so no production Chunks type resolves unqualified in this file any more.

## 3. The absence demonstration

The whole point of the conditional surfaces. Three states driven live, then captured:

| Recipe | Result |
|---|---|
| `Untitled Chunk` (empty) | 0 timelines. Only the recipe row, the Capabilities heading and `Add capability…`. |
| `Barrel Pop` (one Pyre Formation) | **0 timelines, no Timing section**, and no layer, palette or particle control anywhere in the window. This preset exists to be the counter-example. |
| `Crate Smash` (Layer Plan + Formation + Burst) | 1 timeline, total 1.45s — Formation 0.00→0.50, a 0.15s gap, Burst 0.65→1.45. |

Screenshots: `mock2-empty.png`, `mock2-pyre-only.png`, `mock2-full-recipe.png`, `mock2-add-capability-menu.png`, all in this folder.

## 4. Where the B/C findings from the stage-1 review ended up

T-0122 was told to leave these to T-0123. Four of them stopped existing on their own when the cards were rebuilt, and three were closed deliberately because leaving them would have shipped a fresh inconsistency or a visibly broken pane. Nothing on that list is still open, so T-0123's compliance pass is a re-check against what shipped rather than a worklist.

- **B1** (a `Recipe` box wrapping one field), **B2** (the capability name printed twice), **B3** (the on-screen "One Pyre, repeated" subtitle and its build-process commentary), **B4** (a separate 64px "Remove" row instead of the standard header) — all gone with the rebuild.
- **B5** — the Line/Ring pair is now `Z.Segmented`. Every new card uses `Z.Segmented` for its short sets; leaving one `MiniRadio` beside them would have been a new inconsistency, not a deferred one.
- **C1** — the stage resolved to its 300pt minimum inside a ~550pt pane because `flexGrow` on the stage alone cannot win: the section *and* its body sit between the stage and the pane, and neither grows by default. Both now grow. Measured after: 572pt of a 692pt pane, with the Timing section taking the rest.
- **C2** — superseded rather than fixed. The left pane's minimum is now **460pt**, because the Layer Plan row genuinely needs it: at a 400pt pane it wrapped (row height 44pt, the reorder buttons pushed onto a second line, exactly the "flexible gap in a wrapping row" failure the guide names). The flexible gap was removed and the widths trimmed; at 480pt the row is a single 22pt line ending at x=449 inside a pane ending at x=484.

## 5. Verified, and how

**Verified by probe** (live editor, `unity command eval_file` against `D:\UNITY\Laubrary Dev`, port 7800 — `Application.dataPath` confirmed as `D:/UNITY/Laubrary Dev/Assets` on every call):

- Compiles clean; `EditorUtility.scriptCompilationFailed` is `False` and the type resolves in assembly `ChunksMock.Editor`. The old namespace no longer exists in the domain.
- The assembly reference list carries no production Chunks assembly (§2, D2).
- Cold open through the real `Open()` menu method yields the empty state with exactly the two intended buttons.
- The three recipe states and the enable/disable behaviour, as tabulated above.
- `ZuiAudit` returns **0 findings** in all three states. It reported `foldedSkipped=5/13/17`, which would normally invalidate a clean result — so all 17 were enumerated in the fullest state and every one is MicroSlider value chrome (`zui-microslider__numfield` FloatFields and `zui-microslider__value` Labels, `display:None` by design), not folded content. The clean result is therefore authoritative, by the same reasoning the stage-1 review established.
- Geometry measured, not eyeballed: pane widths, the layer row's per-child extents before and after the wrap fix, stage height vs. pane height, and zero visible horizontal scrollbars.
- The `Add capability…` flyout opens and contains all four kinds — the stage-1 review's "the picker flyout has never been opened" gap, closed for this menu at least.
- A **domain reload** was forced (`RequestScriptReload`): the window survived, rebuilt to the empty state, and `recipe` came back non-null via `EnsureRecipe()` rather than null-referencing. The in-memory recipe resets, which is correct for a disposable mock.
- The Unity console holds no error or exception from this work (only pre-existing Coplay toolbar and Input Manager warnings).

**Verified by eye** (Win32 `PrintWindow` with `PW_RENDERFULLCONTENT`, from a DPI-aware process — the capture script is `capture.ps1` in this folder and carries the `SetProcessDpiAwarenessContext(-4)` fix for the crop trap the stage-1 review recorded):

- All four captures listed in §3. The card headers, the layer rows, the timeline bands and their time labels, and the guide drawing formation + burst together were all confirmed against the rendered window.

**Not verified — still owed to T-0123:**

- **No human and no real mouse has touched this window.** Every interaction here was driven by reflection into the window's own methods. Clicking a button, dragging a MicroSlider, dragging the splitter, and dragging the timeline playhead are all unexercised.
- The `Recipes…`, Pyre and Palette flyouts were never opened — only `Add capability…` was.
- The ▲/▼ layer reorder was never exercised at all, in any form.
- The window was never re-entered by closing and reopening it *after* authoring something (the domain-reload test covers the reload path, not the close/reopen path).

## 6. Notes for whoever picks this up

- The implementation was delegated to a **Codex-family agent** (`codex exec`, per the programme's Codex-only implementation mandate) against the brief in `CODEX-BRIEF.md` in this folder. Its structural design is what shipped. Its output was **not** shippable as written and was repaired in place: non-ASCII glyphs (`×`, `▲`, `▼`, `°`, `…`) were corrupted because it wrote the file through a PowerShell `Set-Content` without `-Encoding utf8`; `Z.MicroSlider`'s `decimals` argument was passed positionally and landed on `defaultValue` at every call site; the timeline field was never nulled when the section was not built, leaving a stale detached control; and the whole file was minified to 106 lines. If Codex is used here again, pin the encoding and require named arguments in the brief.
- The mock state is a plain in-memory object — not Undo-recorded, not persisted. Correct for a disposable mock, and **not** to be inherited by the production blueprint: every Laubrary editor tool is Undo-safe and the real Chunks editor will be editing a real asset.
- The shared timeline models a **sequence**, because `ZuiTimeline` bands are consecutive: capability *n*'s delay and duration follow capability *n−1*'s. If the real Chunks model needs overlapping capabilities on one clock, this is the surface that has to change, and ZUI has no multi-track control today.
