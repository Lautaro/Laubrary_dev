# Chunks mock editor — programme review and T-0122 gate decision

**Task:** T-0120 (programme container for group `ChunksMock-2026-08-31`) · **Reviewed:** 2026-08-31 · **Reviewer:** general-purpose / claude-opus-5

This is the PM review that T-0122 explicitly waits on (*"Do not start before T-0121 has been PM-reviewed"*). It reviews the mock built under T-0121 against the programme's own constraints and against the canonical UI Guide (`C:\Users\Lauta\.claude\skills\laubrary\references\ui-layout-rules.md` and `D:\Unity\UNITY_DEV_GUIDE.md`), both read in full before any judgment below.

**No implementation code was written by this review.** The programme requires implementation agents to be Codex-family; this reviewer is not one, so every finding below is left for T-0122 / T-0123 to act on rather than being silently fixed here.

Reviewed artefact: `D:\UNITY\Laubrary Dev\Assets\ChunksMock\Editor\ChunksMockWindow.cs` (314 lines, the whole mock).

---

## Gate decision

**T-0122 may proceed**, on the condition that it carries findings A1–A3 as part of its own scope rather than building on top of them. Those three are not polish — they are the interaction language T-0122 is chartered to extend (a stack of capability cards plus one conditional shared timeline), and the current slice does not yet demonstrate it. Fixing them while adding Layer Plan / Palette Splash / Generic Particle Burst is materially cheaper than a separate round-trip afterwards.

Finding D2 should be done **before** T-0122 adds more surface: it is a two-file change and it is what makes the programme's "this exploration cannot touch production" promise structurally true instead of merely intended.

Findings B and C belong to T-0123's UI-Guide compliance pass.

---

## 1. Programme constraints — all held

The programme forbids altering `ChunkSpec`, the Chunks runtime, production asset formats, or the package `CHANGELOG.md`. Checked against the working tree, not against the previous agent's claim:

| Constraint | Result |
|---|---|
| No production Chunks file modified | **Held.** The only non-`.agenthq` additions are `Assets/ChunksMock/` and its `.meta`. Nothing under `Assets/Packages/Laubrary/**/Chunks*` is touched. |
| `ChunkSpec` untouched | **Held.** `Assets/Packages/Laubrary/Runtime/Chunks/ChunkSpec.cs` is unmodified. |
| Package `CHANGELOG.md` untouched | **Held.** |
| No menu item beyond one explicitly-labelled mock entry | **Held.** Exactly one: `Laubrary/Chunks Mock (Prototype)`. Flat at the `Laubrary/` root, per the no-nesting rule. Production `Laubrary/Chunks` is unchanged. |
| Mock is disposable / in-memory only | **Held.** `MockRecipe` is a plain C# class, never serialised, never written to disk. |

`Assets/Temp/` and `qwen-delegate.ps1` also show as untracked, but both predate this programme (2026-08-25) and are unrelated to it.

---

## 2. Findings

### A. Workflow gaps — carry into T-0122

**A1 — "Create/open a mock recipe" has no code path at all.**
T-0121's own description names this as part of the one cold workflow the slice must demonstrate. There is no create, open, name, duplicate or reset affordance anywhere in the window. `ChunksMockWindow.cs:42-46` builds a `Z.Section("Recipe", …)` containing a single **non-interactive** `Z.Text("Untitled Chunk", …)`. A cold-open probe confirms the empty state contains exactly **one** button in the entire window, and it is `+ Add Pyre Formation`.

This is precisely the failure the handover walk's step 0 exists to catch: a stated requirement with no method, branch or control serving it. T-0121's handover reported the slice as implemented; on this one requirement it is absent rather than unpolished.

**A2 — Only one capability can ever exist, so the capability *stack* is never demonstrated.**
`ChunksMockWindow.cs:50-62` swaps the add-button out for the card the moment a formation exists. There is no way to add a second capability, and no list, order, or selection concept. Since the whole programme is about validating a modular capability stack — and T-0122 must add four more card types to it — the interaction being validated is not yet on screen. T-0122 needs a persistent add affordance and an N-card stack before its own additions mean anything.

**A3 — The capability toggle only folds; nothing enables or disables a capability.**
`ChunksMockWindow.cs:58-60` renders `Z.Toggle("Pyre Formation", …)` whose handler sets `recipe.showFormation` — a pure view flag. Its label is the capability's name, so it reads as "is this capability on", but switching it off leaves the capability fully in the recipe and merely hides its card. This breaks the guide's **Label = action** rule. It is also redundant: the `Z.BoxKeyed` card below it already folds on its own header, so the window ships two fold controls for one card and zero enable controls.

### B. UI-Guide compliance — for T-0123's pass

**B1 — Redundant section title.** `Z.Section("Recipe", …)` wraps exactly one element. Guide: *"Never title a box that holds exactly one field."*

**B2 — The capability name is printed twice, adjacent.** The toggle labelled "Pyre Formation" sits directly above a box titled "Pyre Formation". Visible in `mock-populated.png`.

**B3 — On-screen explanatory text.** The card carries a subtitle line "One Pyre, repeated" (`ChunksMockWindow.cs:73-74`) whose tooltip reads *"This vertical slice intentionally exposes only the parameters needed to repeat one selected Pyre."* The guide is explicit that explanations belong in a tooltip and never as body text the user re-reads on every visit. Separately, this is build-process commentary ("this vertical slice") leaking onto a product surface.

**B4 — Card header does not match the standard shape.** The guide's card header is `fold · icon · NAME · the one universal control · flexible gap · ×`. Here the fold/name row and the remove row are separate rows, and remove is a 64px text button labelled "Remove" rather than the standard `×` in the header.

**B5 — Two-option enum uses `Z.MiniRadio`.** The guide assigns short 2–3 single-line sets to `Z.Segmented` and reserves `MiniRadio` for sets that need to wrap. Consistency only; it renders acceptably today.

### C. Layout — measured, not asserted

**C1 — The spatial guide fills only about a third of its pane.** `stage.style.flexGrow = 1f` is set (`ChunksMockWindow.cs:120-121`) but does not win against the section body's sizing: measured live, the stage resolves to a **300pt height inside a roughly 550pt-tall pane**, leaving bare grey below it. Visible in all three captures. The declared intent and the result disagree.

**C2 — The left pane's declared minimum is narrower than its own content wants.** `left.style.minWidth = 300f` (`ChunksMockWindow.cs:28`), but the widest row — two 150pt MicroSliders plus roughly 22pt of section/box chrome — needs about 328pt. Measured by driving the splitter to 300pt: the Count/Stagger row wraps onto two lines. It degrades gracefully (no horizontal scrollbar appears), so this is minor, but the pane permits a width its content cannot use.

**Checked and found NOT to be a problem** (recorded so it is not re-raised): the two-slider row does *not* overflow at the default 340pt split — measured at x 11.1→161.3 and 167.6→317.3 inside a 340pt pane. The raw `new ScrollView(...)` at line 27 is **not** a guide violation: there is no `Z.Scroll` factory in the UITK toolkit and every ZUI window, including ZUI's own `ZuiGallery` and `ZuiShapeBrowser`, constructs one directly. The `MockFormationStage` custom painter is the guide's explicitly sanctioned raw island.

### D. Isolation — no leak today, but no barrier either

**D1 — Nothing has leaked.** Measured in the live editor: the mock compiles into `Assembly-CSharp-Editor`, and that assembly emits **no reference** to `com.Lautaro-Arino.Laubrary.Chunks`. The mock genuinely touches no production Chunks type right now.

**D2 — The isolation is convention, not structure.** Two things make it fragile, and both are cheap to fix:

- `Assets/ChunksMock/Editor/` has **no asmdef**, so it lands in the predefined `Assembly-CSharp-Editor`, which auto-references every auto-referenced Laubrary assembly — production Chunks included. Nothing prevents the next edit from reaching straight into `ChunkSpec`.
- The namespace is `Laubrary.Chunks.Mock.Editor` — **nested under the production namespace**. Every production Chunks type therefore resolves unqualified inside the mock file. The file's own header comment warns it "must not become a hidden route into the production runtime"; the namespace choice is the one thing making that route easy.

Suggested fix, for whoever picks this up: add an asmdef under `Assets/ChunksMock/` referencing only `com.Lautaro-Arino.Laubrary.Zui.Editor`, `com.Lautaro-Arino.Laubrary.ZuiRuntime` and `ZUI.Editor`, and move the namespace outside the `Laubrary.Chunks` tree. That converts the promise into something the compiler enforces.

### E. Carry-forward note for the blueprint

Mock state is a plain in-memory object, so it is neither Undo-recorded nor persisted across a domain reload. That is the right call for a disposable mock and is **not** a defect here. It must not be inherited by the production blueprint: the standing Laubrary rule is that every editor tool is Undo-safe, and the real Chunks editor will be editing a real asset.

---

## 3. What was verified, and how

**Verified by probe** (live editor, `unity command eval_file` against `D:\UNITY\Laubrary Dev`, port 7800):

- Project targeting is correct (`Application.dataPath` = `D:/UNITY/Laubrary Dev/Assets`) and `EditorUtility.scriptCompilationFailed` is `False`.
- `Laubrary.Chunks.Mock.Editor.ChunksMockWindow` exists in `Assembly-CSharp-Editor` and derives from `Laubrary.Zui.ZuiWindow`; production `ChunkSpec` lives in a separate assembly the mock's assembly does not reference.
- `ZuiAudit` returns **0 findings** with `ExpandAll` reporting nothing left to expand. The audit also reported `foldedSkipped=3`, which would normally make a clean result meaningless — so the three hidden subtrees were enumerated individually and are all **MicroSlider value chrome** (two hidden `FloatField`s and one hidden value `Label`), not folded content. The clean result is therefore genuine, in both the empty and populated states.
- Geometry measured directly rather than eyeballed: pane and control world-bounds at 340pt and at 300pt split widths; stage height vs. pane height.
- Closing every instance and cold-opening through the real `Open()` menu method yields the empty state with `recipe.formation == null` and exactly one button.

**Verified by eye** (Win32 `PrintWindow` with `PW_RENDERFULLCONTENT`, DPI-aware, three captures attached):

- The empty state, the Line formation (count 5), and the Ring formation (count 8, radius 4). The rough spatial guide genuinely draws: origin cross, blue→orange spawn markers, and order arrows, in both patterns.
- The layout, redundancy and short-stage findings above were all confirmed against the actual rendered window.

**Not verified — and this is what T-0123 is for:**

- **No human, and no real mouse, has touched this window.** Every interaction in this review was driven by reflection into the window's own methods and fields. Clicking, slider dragging and splitter dragging are unexercised.
- The **Pyre picker flyout was never opened.** `Z.Menu(...).Section(...).Item(...)` compiles and the anchor button renders, but the menu's contents, its check-mark state and its dismissal behaviour have not been seen.
- No **domain reload** was forced, so re-entry after a recompile is untested. Since `recipe` is a non-serialised reference field, it is worth confirming the window rebuilds cleanly rather than null-referencing.
- The Ring/Line control swap was exercised, but only by setting the field and rebuilding — not by clicking the radio.

---

## 4. Handover to the remaining tasks

**T-0122 (Coordinator mock: layers, timeline, generic particles)** — cleared to start. Fold A1, A2 and A3 into its scope; do D2 first. Its own charter still stands unchanged: mock data and UI only, Mirage stays a stub rather than a second preview, and a simple Pyre-only recipe must still show none of the new surfaces.

**T-0123 (handover walk and refinement)** — the walk is genuinely still owed. This review does not discharge it: it establishes that the parts work and that the layout is measurably sound, which is exactly the thing the guide warns is not the same as a human completing a task. B1–B5 and C1–C2 are its compliance-pass worklist.

**T-0119 (the research task)** is unaffected and remains with the user.
