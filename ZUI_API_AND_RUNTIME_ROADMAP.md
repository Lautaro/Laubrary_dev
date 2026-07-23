# ZUI: API-for-Claude, Runtime Styling, and Audit Tooling — Roadmap

**Status update (2026-07-21):** Task 1's core items (1a coverage passes, 1b decision index, 1d smell-test wording) landed in `zui.md` since this doc was written (see `zui-docs-vs-code-audit.md` project memory for the incremental history) — re-verify against current `zui.md` before treating any Task 1 item below as still open. 1c's bad/good gallery, plus the width/spacing/labeling depth this doc didn't scope at all (what was a separate per-project `EDITOR_TOOL_CONVENTIONS.md`), is now consolidated into a NEW canonical file, `references/ui-layout-rules.md` (global skill, not per-project) — read that alongside `zui.md` going forward. Task 3a (`Overlap`/`Crowded` detection) and 3b (ground-truth fixture + test) are DONE for the runtime IMGUI section — see the CHANGELOG's `[Unreleased]` → Added entries. Today's session extended 3a/3b to **editor tool windows** (`ZUI.Editor`, previously uncovered — Part 0 §? never flagged this gap explicitly, but it's the same "coverage is silently incomplete" problem Task 1 was about, applied to the audit tool instead of the doc): `EditorWindowAuditSection` + `BadEditorWindowFixture` + a new `UIIssueKind.OverWidth`. Task 2 (runtime 9-slice styling) is untouched by today's session — still whatever state 2a-2d's status note above says.

Scoping doc from a 2026-07-14 assessment session. Covers three independent extension efforts against
ZUI/ZuiRuntime/UIAudit, plus the standing question of how a Claude-facing API reference should actually
be built so it works. Source of truth for the assessment itself: this session's research pass over
`Assets/Packages/Laubrary/Zui/`, `Runtime/UIAudit/`, `references/zui.md`, and `authoring.md`. Nothing here
is committed work yet — it's scope, not a patch.

---

## Part 0 — How does a Claude-facing API doc actually work?

This is the mechanism question underneath Task 1: `references/zui.md` exists so Claude can use ZUI
*without* reading `Zui/Scripts/...`. What makes that actually hold up, versus just being a doc that looks
thorough?

**What `zui.md` already gets right (keep doing this):**
- **Leads with a runnable template**, not a table of signatures. Claude pattern-matches against a worked
  example far more reliably than it assembles one from a list of method signatures — the first 40 lines
  of `zui.md` are a complete `ZUIWindow` subclass, not prose.
- **States anti-patterns with a measured cause, not a style opinion.** The Forms section doesn't say
  "prefer grouping fields" — it says *"223 raw `EditorGUILayout.*` calls in 25 files, `HRow()` used in
  only 7"* and names the exact failure. A quantified, sourced claim gets treated as a hard rule; an
  unsourced style preference gets treated as optional. This is the single biggest lever the doc has and
  it's under-used elsewhere in the file.
- **Explains the WHY behind footguns**, not just the workaround. E.g. `ObjectPicker`'s `+ 40px` chrome
  width fix is explained via *why* `CalcSize` under-measures (it only sees the text, not Unity's fixed-
  width icon/picker-button chrome). Knowing the mechanism lets Claude generalize to adjacent, undocumented
  situations instead of only pattern-matching the one fixed string.
- **Calls out naming collisions by name** (`ZUIRow` vs `ZUIRowScope`) before Claude hits them.
- **Nails down default behavior explicitly** ("leaving `style` unspecified must always mean the ZUI-skinned
  look, never a silent native fallback"). Ambiguity about defaults is one of the top sources of
  wrong-but-plausible code — stating it removes a guess Claude would otherwise have to make silently.
- **Defines its own boundary** — the "When to read the source" section tells Claude exactly when to drop
  confidence and escalate, rather than confidently guessing past the doc's edge.

**Where it currently falls short — and why each one matters:**

1. **Coverage is silently incomplete, which is worse than looking incomplete.** This session's research
   found several real, working, non-trivial scopes with zero mention in `zui.md`: `ZUI.VGroup`/`VGroupBox`,
   `ZUI.AnimatedFoldout2`, `ZUI.FoldControls`, `ZUI.Blocks`/`.Cell(...)`, `ZUI.PaddedArea`,
   `ZUI.SelectableRow`/`ZUI.Chip`. An API doc's entire value proposition is "trust this instead of reading
   source" — every gap like this means Claude can conclude "ZUI doesn't have a stacking/grouping helper for
   X" when it actually does, either duplicating something that already exists or (per the user's own
   "smell test" policy — see #3) proposing to extend ZUI for a control that's already there. **A doc that's
   supposed to replace source-reading is only as trustworthy as its completeness**; partial coverage
   actively produces wrong conclusions rather than just missing ones.
2. **No task→control decision index.** The doc is organized "here is each control," which works once
   Claude already knows what primitive it wants. It doesn't help at the moment that actually matters most:
   "I need to make this collapsible" or "I need a right-click menu here" with no idea yet which of ~15
   scopes covers that. A short lookup table (task phrased in plain language → control name) at the very
   top would resolve this cheaply — see Task 1.
3. **The "smell test" instruction lives in the wrong file.** The rule *"if no ZUI control fits, that's a
   smell — surface it and consider extending ZUI rather than hand-rolling"* exists today, but only in
   `D:\Unity\Laubrary Dev\CLAUDE.md` (this dev host's project instructions). A Claude session working
   inside a **consumer** project (OutBurner, or any other Laubrary-consuming project) never loads that
   file — it only loads the portable `laubrary` skill. So the exact policy this session was asked to
   evaluate does not currently reach most of the sessions that need it. It belongs in `zui.md` itself (or
   `SKILL.md`), not only in one dev host's CLAUDE.md.
4. **No worked bad-vs-good gallery.** Prose rules ("don't clump controls," "don't truncate text") are
   weaker signal than a paired before/after snippet tied to the same rule — closer to how few-shot examples
   steer a model than abstract description does. The one place `zui.md` already does this well (the Forms
   section's row-packing example) is the template to repeat, not a one-off.
5. **No freshness signal.** Nothing marks what commit/date the doc was last checked against source, so
   drift (exactly what #1 found) accumulates silently. `authoring.md` already mandates a `CHANGELOG.md`
   entry for every Laubrary change — the same discipline doesn't yet extend to "update `zui.md` in the same
   change that adds/changes public ZUI API."

None of this changes the underlying policy (API-first, don't read source to use a tool) — it's about
making the doc actually earn that trust. Task 1 below is the concrete fix for all five.

---

## Task 1 — Close the API-doc gaps; add popover/context-menu primitives

### Current state
- `references/zui.md` is the only Claude-facing API reference in all of Laubrary (confirmed: no other
  tool has one; no `docs/` folder exists anywhere in the package).
- It under-documents real, shipped scopes (see Part 0 §1).
- Popovers and context menus are a genuine capability gap, not just a doc gap: every popup in the
  codebase (`EasePickerPopup`, `ZUIGradientStopPopup`, `ZUIPatternPickerPopup`, `ZUIStopColorPopup`,
  `ZUIColorPickerPopup`, `ZUIIconPickerPopup`, plus more outside ZUI proper in Launimator/Zounds) is a
  hand-written `PopupWindowContent` subclass invoked via raw `PopupWindow.Show`. Every context menu (11+
  call sites) is a hand-built `GenericMenu`. `ZUI.SelectableRow`/`ZUI.Chip` only *detect* a right-click via
  an out-param — the caller still assembles the menu from scratch every time.

### Proposed scope
**1a. Documentation completeness pass.** Add sections to `zui.md` for `VGroup`/`VGroupBox`,
`AnimatedFoldout2`, `FoldControls`, `Blocks`/`.Cell(...)`, `PaddedArea`, `SelectableRow`/`Chip` — same
depth/style as existing entries (signature, when to reach for it, one short example).

**1b. Task→control decision index.** A short table at the top of `zui.md`, above the main pattern example:
plain-language need → control. E.g. "stack fields vertically → `VGroup`/`Box`", "collapsible section →
`FoldoutBox` / `AnimatedFoldout2` (animated)", "right-click menu → [new `ZUI.ContextMenu`, once built]",
"floating panel anchored to a button → [new `ZUI.Popover`, once built]". This is the single cheapest,
highest-leverage item in this whole roadmap.

**1c. The "good layout" guidelines — as a gallery, not a prose checklist.** Per Part 0 §4, structure this
as short paired before/after snippets, each tied to one concrete rule, added as a new `zui.md` section
(not a separate file — see the placement note below):
   - Truncated text (an object field / foldout title clipped) → the fix already used elsewhere in the doc
     (uncapped dynamic width, or hand-truncate with ellipsis when IMGUI won't clip for you).
   - Overly-wide controls (a slider or field stretched to fill an entire wide window with no cap) → pack
     into a `ZUI.Row`/`ZUI.Form` or apply an explicit max width.
   - Clumped controls with no breathing room → `form.Gap()` / `ZUI.HorizontalSpace()` between unrelated
     groups.
   - Related short fields each claiming a full-width row → `ZUI.Form`/`ZUI.Row` (already documented, but
     cross-link it from the new gallery section so it isn't a separate mental bucket from "layout
     quality").
   Each entry: one or two lines of "bad" code, one or two lines of "good" code, one line of why. Resist
   turning this into an essay — the whole point is that it reads like the Forms section's 223-calls
   callout, not like a style guide.

**1d. Move the smell-test instruction into the portable doc.** Add the *"if nothing in this reference
fits, treat that as a signal to propose extending ZUI — don't silently hand-roll raw IMGUI, and don't
silently assume the gap is real without checking §1a's index and, if still unsure, the source"* instruction
directly into `zui.md` (top or bottom, near "When to read the source"). Leave the existing wording in
`D:\Unity\Laubrary Dev\CLAUDE.md` alone (still correct there) — this is additive, not a move.

**1e. New generic helpers.**
   - `ZUI.Popover(Rect activatorRect, Vector2 size, Action drawContent)` — a thin generic wrapper around
     `PopupWindow.Show` for the common case (arbitrary IMGUI content, no persistent internal state beyond
     what the caller closes over). Existing bespoke popups with real internal state/animation (color
     picker, gradient stop editor) are not migration targets — they stay hand-rolled `PopupWindowContent`
     subclasses; this helper is for the 80% case where a tool author would otherwise hand-roll one from
     scratch for a one-off menu/panel.
   - `ZUI.ContextMenu(params (string label, Action onClick, bool enabled)[] items)` (or a small builder
     object if the fixed-arity signature turns out too rigid once you look at the 11 existing call sites) —
     a declarative wrapper over `GenericMenu`, meant to pair directly with `SelectableRow`/`Chip`'s existing
     `rightClicked` out-param as the canonical "selectable row with a context menu" pattern.
   Both are editor-only (`ZUI.Editor`) — see open question below on whether runtime needs an equivalent.

**1f. Freshness discipline.** Add one line to `authoring.md` (or wherever rule #14's CHANGELOG mandate
lives): "if the change adds/renames/removes a public ZUI-editor API, update `references/zui.md` in the
same change." No tooling needed — just closing the loop that let §1a's gap happen.

### Deliverables
- Updated `references/zui.md` (decision index, gallery section, missing-scope entries, smell-test wording,
  When-to-read-source cross-reference).
- Updated `authoring.md` (one added freshness rule).
- New `ZUIPopover.cs` / `ZUIContextMenu.cs` under `Zui/Scripts/Editor/`.
- CHANGELOG entry under `[Unreleased]`.

### Open questions for you
- **Placement of the layout-guidelines gallery**: fold into `zui.md` as a new section (single file the
  skill already points at, guaranteed to be read) vs. a separate `references/zui-layout-guidelines.md`
  (cleaner separation, but needs `SKILL.md`'s router updated too, and risks being missed the way the
  now-documented gaps were). **Recommendation: fold into `zui.md`.**
- **Does the popover/context-menu helper need a runtime (`ZuiRuntime`) equivalent**, or is this editor-only
  scope correct? A build has no OS popup window — an in-game "context menu" would have to be a
  `ZuiMenu`-based overlay instead, a materially different mechanism. Flagging so it's a deliberate choice,
  not an oversight, if runtime HUD work later wants a right-click/long-press menu.
- **Migration of existing hand-rolled popups/menus**: leave all 17+ existing call sites as-is (forward-
  looking helper only), or is there appetite to migrate any of them as a cleanup pass? Recommendation:
  leave as-is for now — low value, real risk of regressing working editor tools for no functional gain.

---

## Task 2 — Wire 9-slice/Ztyle-driven rendering into the documented runtime API

**Status (2026-07-14): 2a–2d done and verified in Play mode; 2e (demo promotion) not done.** 2a resolved
per direct instruction: keep a single always-available, user-editable default sheet (`Zui.DefaultSheet`,
`ZuiRuntimeSheet.cs`, `Resources.Load`-backed), with runtime's default kept separate from the editor's
`ZUI.DefaultSheet` rather than shared. 2b/2c shipped as `Zui.Panel(rect, styleName, sheet?, padPts?)` and
a fixed `ZUISheet.Button` procedural path. 2d confirmed by direct test: mutating a sheet's button color
in memory during a running Play session repainted correctly on the next frame with no restart — the same
mechanism the Style Editor itself uses, so live re-styling works. Found and fixed a real bug along the
way: `DemoRuntimeZheet.asset`'s `SpriteButton` had a stale `nineSliceNormal` reference (pointed at
`"BtnNormal"`, not any of the sheet's actual nineSlice names), so its own 9-slice button test had never
actually rendered a frame. See the CHANGELOG entry for full detail. 2e remains open — low priority, purely
tidiness, not gating anything.

### Current state
- The documented, in-use runtime surface (`ZuiRuntime.Zui` — `Panel`, `FillRect`, `ZuiStack`, `ZuiMenu`) is
  flat-color only. `ZuiStyles.cs` builds plain `GUIStyle`s off `GUI.skin`; nothing in the `ZuiRuntime`
  namespace references `ZUIStyleDef`/`ZUIStyleSheetAsset` at all.
- 9-slice rendering at runtime **is already solved**, just disconnected: `ZUISheet.DrawBox`/`ZUISheet.Button`
  (global namespace, `ZUISheet.cs`) can paint a `ZUIStyleSheetAsset`'s box style — including full 9-slice —
  into runtime `OnGUI`, proven working in `Assets\ZUIDemo\ZuiZheetTestHud.cs` /
  `ZUIRuntimeTest.unity`. That scene lives loose in the dev project, outside the package, unreferenced by
  `zui.md`, not wired into `Zui.Panel`/`ZuiStack`/`ZuiMenu`. `ZUISheet.Button`'s own doc comment: *"Only
  works for button styles that use 9-slice — procedural buttons don't render at runtime yet."*
  `ScaleTestHud.cs` is explicitly marked "TEMP... safe to delete."
- Aside, likely stale: `D:\Unity\Laubrary Dev\CLAUDE.md` has an "Open packaging gap" note claiming
  `Assets/ZUI/` lives outside `Packages/Laubrary/`. This session found the actual ZUI source already under
  `Assets/Packages/Laubrary/Zui/Scripts/...` — i.e. already inside the package. Worth a quick confirm-and-
  delete-the-stale-note pass, independent of this task's real scope.

### Proposed scope
**2a. Define how a runtime script picks its sheet.** Editor ZUI resolves style via `ActiveSheet`/scoped
`using (ZUI.UseSheet(...))` — neither concept exists at runtime or in a build. A `ZuiRuntime` HUD
component needs an explicit, serialized reference (`[SerializeField] ZUIStyleSheetAsset sheet;` on the
MonoBehaviour, or a name-based `GetConsumerSheet(name)` lookup if the asset is meant to be swappable
without a scene edit). This is the actual design decision the rest of the task hangs off — pick one before
writing the wiring code.

**2b. Additive (not breaking) sheet-aware overloads.** `Zui.Panel(rect, styleName)` / a `ZuiStack` variant
that resolves through the chosen sheet, sitting alongside the existing flat-`Color` overloads — don't force
every HUD (e.g. a cheap debug overlay) to pay for asset resolution it doesn't want.

**2c. Finish `ZUISheet.Button` for procedural (non-9-slice) styles**, closing the gap its own doc comment
already flags.

**2d. Confirm the live-Zeditor-edit story actually works end to end**: open `ZUIStyleEditorWindow`, tweak a
9-slice inset on a sheet a Play-mode `ZuiRuntime` panel is bound to, confirm it updates without extra glue
(IMGUI redraws from the asset's current values every frame, so this is likely free — but nothing today
actually exercises it, so it needs a real check, not an assumption).

**2e. Promote the demo out of throwaway status**, per this repo's own "demos ship scenes" convention
(`authoring.md` rule #8; Laubrary Dev `CLAUDE.md`'s "Demos — ship SCENES, not scripts that build scenes"):
delete `ScaleTestHud.cs` per its own comment, and either author a proper `Assets/Demos/ZUIRuntimeDemo/`
scene or fold the surviving proof into whatever demo Task 2's finished feature ships with.

### Deliverables
- Design note (short, inline in code comments or the CHANGELOG entry) recording the sheet-resolution
  decision from 2a.
- `ZuiRuntime.Zui` sheet-aware overloads (additive).
- Finished procedural-button runtime rendering in `ZUISheet.Button`.
- A real (non-throwaway) demo scene under `Assets/Demos/`.
- CHANGELOG entry.

### Open questions for you
- **2a's actual answer** — direct serialized-asset reference per-component, or name-based lookup
  (`GetConsumerSheet`)? Affects whether a shipped build needs the sheet asset in `Resources`/Addressables
  or just a normal inspector reference. Depends on whether OutBurner (or whichever consumer) wants to swap
  HUD skins at runtime without a scene edit — if not needed, the simpler direct-reference path is less
  work and less to get wrong.
- **Default panel behavior going forward**: should new runtime HUD code default to flat-color (current
  behavior, cheaper, no asset dependency) or sheet-styled (matches editor ZUI's "leaving it unspecified
  means the skinned look" rule from `authoring.md` #13)? Recommendation: flat stays the default for
  runtime specifically (a build shouldn't silently require a sheet asset to render anything), sheet-styling
  is opt-in via the new overload — this is a deliberate exception to rule #13, worth you confirming rather
  than me deciding unilaterally.

---

## Task 3 — Audit tooling: widen detection + build a ground-truth test project

### Current state
- `UIAudit` (`Runtime/UIAudit/`) covers both uGUI and IMGUI through a pluggable `IUIAuditSection`
  architecture, but its entire detection surface is 4 checks:
  `OffScreen | TextOverflow | TinyText | NeedsScrollView`. **No overlap detection, no
  crowding/spacing detection exist at all.**
- IMGUI coverage is further limited to draws made *through* `ZuiRuntime.Zui` helpers specifically (via the
  `ZuiAudit` frame recorder) — raw `GUI.Label`/`GUI.Button` calls aren't seen.
- **No ground-truth test project exists.** No Unity Test Framework assemblies reference ZUI or UIAudit
  anywhere in either the dev host or any consumer project checked; no `Tests/` folder. The only nearby
  scene (`ZUIRuntimeTest.unity`) is a manual visual-verification demo for 9-slice rendering, not a
  deliberately-broken-layout gallery with known expected findings. This would be the package's first use
  of Unity Test Framework at all.

### Proposed scope
**3a. Widen the detection surface.** Add to `UIIssueKind`:
   - `Overlap` — bounding-box intersection between sibling draws not marked as intentionally layered.
     Needs an opt-out (mirroring the existing `Clipped` flag that suppresses `OffScreen` inside scroll
     views) so decorative overlaps (a badge on an icon) don't spam false positives on day one.
   - `Crowded` / insufficient spacing — gap between adjacent same-row/column elements below a threshold.
     Pairwise, not per-element like the current 4 checks — O(n²) over one frame's recorded draws, which is
     fine at HUD-sized element counts.
   Treat `InconsistentAlignment` (rows that should share a baseline but don't) as a stretch item, not MVP —
   fuzzier to define correctly than the two above.

**3b. Ground-truth test project — the actual "test suite for IMGUI auditing" ask.** Build a deliberately-
bad layout fixture: a scene/HUD exercising `ZuiRuntime` controls with known, labeled issues (intentionally
truncated text, controls jammed together, an off-screen button) alongside a "fixed" counterpart of the
same layout, plus a small expected-issues manifest (which issue, roughly which control/rect, should fire).
Wrap it in an actual Unity Test (EditMode/PlayMode) that runs `UIAudit` against the bad fixture and asserts
the expected findings show up — and, just as importantly, asserts the *fixed* counterpart reports clean,
so the suite also catches false positives in new detectors, not only false negatives. This is the concrete
mechanism for "evaluate tools for improving IMGUI auditing" the user asked for — a checkable baseline
before and after any detector change, rather than eyeballing it.

**3c. Decide where this lives.** Recommendation: dev-host-only (`D:\Unity\Laubrary Dev\Assets\Tests\
UIAudit\`), **not shipped inside the package** — consumer projects don't need Laubrary's own internal test
suite, and `package.json` shouldn't pick up a `com.unity.test-framework` dependency for something only the
package's own maintainer runs. This is a first-of-its-kind addition to the package (no test infrastructure
exists anywhere in Laubrary today), so flagging it rather than just doing it.

### Deliverables
- New `UIIssueKind` members (`Overlap`, `Crowded`) + detection logic in `ImguiAuditSection`/
  `UGuiAuditSection`, with an opt-out mechanism for intentional overlaps.
- A bad-layout + fixed-layout fixture scene/prefab pair exercising `ZuiRuntime` controls.
- An EditMode/PlayMode test asserting `UIAudit` catches the bad fixture's known issues and passes clean on
  the fixed one.
- CHANGELOG entry.

### Open questions for you
- **Confirm dev-host-only test placement** (not shipped in the package) — my recommendation above, since
  it's a first for the package and worth a deliberate sign-off rather than a silent precedent.
- **Priority order for the new detectors**: `Overlap` and `Crowded` as MVP, `InconsistentAlignment` deferred
  — agree, or is alignment actually the one you care about most given the original ask mentioned "proper
  distance between groups of controls" specifically?

---

## Suggested sequencing

1. **Task 1** first — cheapest, no design dependencies on the others, and it's the thing that most directly
   improves how every future Claude session (including on the other two tasks) reasons about ZUI.
2. **Task 3's fixture/test project** next — establishes a checkable ground truth that Task 2's visual work
   can also lean on later (e.g. a "does the 9-slice panel render without truncation" check), and validates
   Task 3's own new detectors as they're built.
3. **Task 2** last — the largest scope and the one open design decision (2a, sheet resolution at runtime)
   most benefits from Task 1's documentation discipline and Task 3's test scaffolding already existing.

They're independent enough to reorder or parallelize if you'd rather start somewhere else — this ordering
is a recommendation, not a dependency chain, except that Task 2 genuinely benefits from 1 and 3 existing
first.
