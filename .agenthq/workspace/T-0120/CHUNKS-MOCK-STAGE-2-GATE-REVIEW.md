# Chunks mock — stage 2 gate review (T-0120 programme, reviewing T-0122)

Reviewed artefact: `D:\UNITY\Laubrary Dev\Assets\ChunksMock\Editor\ChunksMockWindow.cs` (691 lines) and `D:\UNITY\Laubrary Dev\Assets\ChunksMock\Editor\ChunksMock.Editor.asmdef`. Reviewer: the T-0120 programme container. Everything below was re-measured against the live editor on port 7800 (`D:\UNITY\Laubrary Dev`) in this session — the implementer's claims were treated as hypotheses, not evidence, per the handover-walk rule *"a subagent's 'done' is not evidence"*.

**Verdict: the stage-2 mock PASSES its gate. T-0123 is cleared to proceed.** All four carried gate findings (D2, A1, A2, A3) are closed, and I closed them by my own measurement rather than by reading the implementer's report. Five new findings (E1–E5) go to T-0123 alongside the walk it already owes; none of them is severe enough to send stage 2 back.

## 1. Gate conditions carried from the stage-1 review

### D2 — isolation must be structural, not a comment · CLOSED

The mock now compiles into its own assembly. Read out of the live domain, `ChunksMock.Editor`'s referenced-assembly list is exactly `mscorlib`, `System`, `com.Lautaro-Arino.Laubrary.Zui.Editor`, `UnityEditor.CoreModule`, `UnityEngine.UIElementsModule`, `UnityEngine.CoreModule`. No production Chunks assembly is reachable, `autoReferenced` is `false`, and the namespace moved from `Laubrary.Chunks.Mock.Editor` (nested *under* production Chunks, so `ChunkSpec` resolved unqualified) to `ChunksMock.Editor`. The barrier is now enforced by the compiler. Grepping the source for `Laubrary.Chunks` / `ChunkSpec` returns only the two comment lines that explain the barrier.

### A1 — no way to create, open or name a recipe · CLOSED

The recipe row is a `Z.Field("Recipe", Z.TextInput(...))` plus a `Recipes…` menu carrying three check-marked presets and `New empty recipe`. Measured cold-open: the empty state now holds 2 real affordances plus a text field, where stage 1 had exactly one button and a static label.

### A2 — only one capability could ever exist · CLOSED

`MockRecipe` holds a `List<MockCapability>`; `Add capability…` is never swapped out; four subclasses (`MockFormation`, `MockLayerPlan`, `MockPaletteSplash`, `MockParticleBurst`) each render their own card. Measured: the `Crate Smash` preset renders three independent cards, each with its own per-instance `Guid` fold key so two cards of the same kind fold independently.

### A3 — the toggle only folded the card, it did not disable anything · CLOSED

Measured, not asserted, and measured by me rather than accepted from the report. Setting the particle burst's `Enabled` to `false` and rebuilding kept the card count at 3, made that card's body inert, and dropped the whole shared timeline (`timeline` field went `set` → `null`); restoring `Enabled` brought it back. The painter also skips disabled capabilities, so the toggle changes what is drawn as well as what is dialled.

## 2. The absence demonstration — the point of the whole exploration

Harvested every on-screen `TextElement` in each preset state, walking `hierarchy` rather than `childCount` (the content-container walk silently skips every box header, which is how an earlier pass appeared to find no card titles at all — worth knowing for anyone probing ZUI trees).

- **`Barrel Pop` (one Pyre Formation):** one card, and no "Layer Plan", "Palette", "Generic Particle Burst" or "Timing" anywhere. `ZuiTimeline` element count 0.
- **`Untitled Chunk` (empty):** no capability surface at all, timeline count 0.
- **`Crate Smash` (three capabilities):** three cards, one "Timing" section, `ZuiTimeline` element count 1, total 1.45s.

Confirmed by eye as well as by probe: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0120\pm-barrel-pop.png` and `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0120\pm-crate-smash.png` (my own DPI-aware `PrintWindow` captures, not the implementer's).

## 3. UI-Guide compliance pass

Both mandatory guides were re-read in full first (`C:\Users\Lauta\.claude\skills\laubrary\references\ui-layout-rules.md` and `D:\Unity\UNITY_DEV_GUIDE.md`), because this was a fresh process with no memory of the earlier reading.

**Machine audit.** `ZuiAudit.ExpandAll` + `Audit` returns **0 findings in all three states**. `foldedSkipped` was 0 / 5 / 13, and per the standing rule that a non-zero skip count invalidates a clean result, I enumerated every skipped subtree in the fullest state: 15 hidden subtrees, all of them `zui-microslider__numfield` FloatFields, `zui-microslider__value` Labels, or the ScrollView's own two scrollers. None is folded content, so the clean result is authoritative. The horizontal scroller being hidden also discharges the guide's *"a horizontal scrollbar is a bug signal"* check.

**Control choice.** No native controls anywhere: 0 raw `Toggle`s, 0 `PopupField`/`EnumField`. Enums are `Z.Segmented` throughout (Line/Ring, Normal/Add/Multiply, Cone/Disc/Sphere). Bounded scalars are all `Z.MicroSlider`. The bool is `Z.ToggleButton`. Every reference — the Pyre, the palette, the recipe preset — is a `Z.Menu` pick, never a typed string; the only text fields are the recipe name and the layer name, which are *declarations*, exactly where the rule says a text field is correct. The `Painter2D` stage is the sanctioned raw island. `new ScrollView(...)` is not a violation: no `Z.Scroll` factory exists and ten Laubrary editors do the same.

**Labels and actions.** Every control carries a tooltip (the audit's `tooltip-missing` check found none). `Recipes…`, `Add capability…`, `Green Lantern…` and `Ember…` all end in an ellipsis because they open further UI, and `Add layer` has no ellipsis because it acts immediately — that is the label-equals-action rule obeyed in both directions. No on-screen instructional prose survives.

**Stable workspace.** The conditional Timing section is added *below* the spatial guide, never above it, so its arrival cannot shove the workspace under the pointer. Value-only edits go through `Change()`, which repaints and re-bands without rebuilding, so a slider drag cannot pull the control out from under the cursor.

**Widths.** Measured at a 480pt left pane: the Layer Plan row — the widest thing the window holds — ends at x=448.9 inside a pane ending at x=484, with 35pt of headroom, confirming the 460pt `minWidth` is a real constraint and not a guess.

## 4. New findings — all to T-0123, none blocking

**E1 · Space economy: the capability cards stack rows the guide says should be packed.** Measured per row, in a 480pt pane, as free space to the right of the last child: Pyre Formation card — Pyre picker row 251.6pt free, Count+Stagger 166.7pt, **Pattern 340.4pt**, **Direction°/Radius 329.8pt**, Delay+Duration 206.7pt. The Pattern row and the single slider under it together use 143.6pt and 154.2pt, so they fit on one line with room to spare; that is one wasted row per formation card, and it is exactly the case the guide names — *"only a WIDE control earns its own row"* and *"short fields fill the space beside an existing row"*. Generic Particle Burst has the same shape: its Shape row leaves 283.6pt free while three slider pairs stack beneath it. This matters more than usual because the mock is meant to become the blueprint for the real tool, so a wasteful row rhythm here propagates into production.

**E2 · The shared timeline is a sequence, not a shared clock, and the Delay dial does not say so.** `ZuiTimeline` bands are consecutive, so each capability contributes a delay band followed by a duration band, in list order. In `Crate Smash` the particle burst's Delay reads `0.15` but the band starts at 0.65s, because the formation's 0.5s is laid down first. The tooltip says "seconds of dead time before this capability begins" without saying *relative to what*, so the number on the dial and the position on the bar disagree. Either the dial should be renamed to something sequential ("After", "Gap") with a matching tooltip, or the surface needs a real multi-track clock — and ZUI has no multi-track control today, which makes this a genuine design decision for the blueprint rather than a typo. This is the single most consequential thing the mock currently gets wrong, because "one conditional timeline" is one of the workflows the exploration exists to settle.

**E3 · Every `Rebuild()` recreates the left `ScrollView`, so scroll position resets.** Confirmed structurally (the ScrollView instance is not the same object after a rebuild) but *not* reproducible at the default 1000x700 window, where the content is 516pt inside a 692pt viewport and there is nothing to scroll. `Rebuild()` fires on add, remove, enable-toggle, pattern switch, layer reorder and every menu pick, so at a small window with a long stack, pressing `Add layer` at the bottom may jump the pane to the top. This is the codebase-wide `ZuiWindow.Rebuild` idiom rather than a defect this mock introduced, so it is logged for the hands-on walk to confirm or dismiss, not charged against stage 2.

**E4 · A single-capability recipe still shows Delay and Duration, which dial nothing visible.** With one timed capability there is no shared timeline and nothing to sequence against, so both dials are inert on screen. Defensible (a delay from the trigger is meaningful in the real model) but worth an explicit decision, since the recipe's whole selling point is that irrelevant surfaces are absent.

**E5 · No Undo anywhere.** Correct for a disposable in-memory mock and not a stage-2 failure. Recorded because the blueprint must carry the Undo requirement forward: Laubrary's rule is that every editor tool is Undo-safe, and a blueprint that never shows Undo is a blueprint that will be implemented without it.

## 5. Stage-1 findings B1–B5 / C1–C2 — verified closed

I re-checked each against what stage 2 actually shipped, rather than trusting the claim. **B1** — there is no "Recipe" box around one field; it is a `Z.Field` in an `HGroup` with the Recipes button. **B2** — each capability name appears exactly once, in its card header. **B3** — no on-screen subtitle or build commentary survives. **B4** — the header is caret · icon · NAME · gap · On · × , the standard shape. **B5** — Line/Ring is `Z.Segmented`. **C1** — the spatial guide now fills its pane (572pt of 692pt) instead of resolving to its 300pt minimum. **C2** — superseded: the left pane minimum is 460pt and the widest row measures 448.9pt, so the pane no longer permits a width its content cannot use.

## 6. Programme isolation — re-verified against the working tree

`git status` limited to the production paths returns empty for `Assets/Packages/Laubrary`, for any `ChunkSpec`, and for any `CHANGELOG`. The only new tree entries are `Assets/ChunksMock/` and its meta. Exactly one menu item exists, flat at root: `Laubrary/Chunks Mock (Prototype)`. The Unity console holds no error or exception from this work.

## 7. Robustness

Forced a domain reload with `EditorUtility.RequestScriptReload` and re-probed: the window survived, rebuilt to its empty state with the recipe re-created rather than null-referencing, both presets still load correctly afterwards, and the console stayed clean. That discharges T-0123's domain-reload item.

## 8. Still genuinely not verified

No human and no real mouse has touched this window. Every interaction in this review was driven by reflection into the window's own methods and by reading laid-out geometry. Clicking, MicroSlider dragging, splitter dragging, timeline-playhead dragging, the layer reorder arrows and every flyout (Recipes, Add capability, Pyre, Palette) remain unexercised. The hands-on handover walk is owed by T-0123 and nothing in this review substitutes for it.

## 9. Programme note — the Codex-family mandate

This group's description requires implementation agents to be Codex-family. AgentHQ has no per-task model family (it is a single instance-wide setting), so the dispatcher routed a Claude agent to T-0122 regardless of the task's frontmatter. T-0122 honoured the mandate at the level that actually matters by delegating the C# authoring to `codex exec`; its structural design is what shipped, after repairs for a UTF-8 encoding failure and positional-argument misuse of `Z.MicroSlider`. The open question posted on T-0120's to-do 13 — accept Claude-family dispatch and drop the mandate, flip the global family setting, or keep the mandate as documentation — is still the user's to answer and is not blocking anything.
