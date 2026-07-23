# ZUI → UI Toolkit migration — scope & design

**Status as of 2026-07-23: decision made, this document is the handoff brief for whichever Claude session actually implements it. Nothing in this migration has been built yet — this is planning only.** If you're picking this up fresh, read this whole document before touching code; it front-loads the decisions and open questions so you don't have to re-derive them.

## 1. Why this exists

Laubrary's editor UI toolkit, ZUI, is built entirely on IMGUI (`EditorGUILayout`/`GUILayout`, immediate-mode). Across a long working session (2026-07-21 through 2026-07-23) a real, recurring pattern showed up: IMGUI-specific footguns — `EditorGUIUtility.labelWidth` leaking globally across unrelated sections, `GUILayout.MaxWidth` silently failing to propagate through a `ScrollView`, a `FlexibleSpace()`-before-trailing-buttons layout mistake that made controls vanish with *zero* error or warning — kept costing real iterations, for both the user and for Claude sessions building ZUI-based tools. The question raised was whether Unity's newer UI Toolkit (`VisualElement`/UXML/USS, retained-mode) would produce fewer of these mistakes, specifically for Claude-driven development.

Two controlled trials were run to test this (not just discussed — actually built, screenshotted, and independently re-verified by a second pass rather than trusted at face value). Full blow-by-blow is in project memory (see `zui-uitoolkit-migration-decision` if it exists by the time you read this, or the raw conversation this doc was born from) — the short version, because you need the "why" to make good calls during implementation, not just the "what":

**Trial 1** — a form-heavy editor window (buttons, dropdowns, toggles, a 2D drag pad, asset-picker rows), built twice by independent fresh Claude sessions: once in raw IMGUI, once in raw UI Toolkit, both given the *same* framework-neutral layout rules (not ZUI's own docs, to keep the comparison fair — neither framework had accumulated tooling/docs to lean on). Reference files, still in the repo, read them before building anything:
- `Assets/Framework Trial/MirageMockup_IMGUI.cs`
- `Assets/Framework Trial/MirageMockup_UIToolkit.cs` (+ `.uss`)

Findings, independently re-verified (screenshots taken personally, source code read directly, not just the building agent's self-report trusted):
- The IMGUI build hit a genuinely dangerous failure class: three buttons **silently vanished** (a `FlexibleSpace()` placement mistake) with no exception, no console warning — only caught because the agent happened to pixel-hunt a screenshot. Also had a `ColorField` silently ignoring its requested `GUILayout.Width`.
- Tooltip coverage: the IMGUI build was missing tooltips on 7 of ~30 interactive controls — concentrated *exactly* on the control types whose simplest/default overload doesn't carry a `GUIContent` (`EditorGUILayout.Popup`, `ColorField` with `GUIContent.none`). The UI Toolkit build missed tooltips on only 2 controls (both `ColorField` — the same control type, a shared blind spot, not a framework difference). Every `DropdownField`/`ObjectField` got a tooltip in the UI Toolkit build, because `.tooltip` is a uniform property on every `VisualElement` with no special-case overload to forget.
- Section framing (grouping related controls in a visible box) was applied consistently throughout the UI Toolkit build; the IMGUI build forgot it in one section.
- The UI Toolkit agent built a working automated tooltip-completeness checker via plain reflection over the live `VisualElement` tree in minutes. Building the IMGUI-side equivalent (`EditorZuiAudit`/`EditorWindowAuditSection`, see `Zui/Scripts/Editor/ZUIEditorAudit.cs`) took a whole prior session and needs a "toggle recording, force a Repaint pass" dance because IMGUI has no persistent tree to query.

**Trial 2** — deliberately targeted the *predicted* weak spot: a multi-point draggable curve/envelope editor (the shape of `ZUI.Envelope`/`ZUIEnvelope` — connected points, drag to move, click empty space to insert, right-click to remove), built in **pure UI Toolkit only**, `IMGUIContainer` explicitly forbidden by the brief. Reference file:
- `Assets/Framework Trial/CurveEditor_UIToolkit.cs`

This was expected to be the hard case (continuous custom drawing + interaction is IMGUI's home turf — "just redraw every frame with `Handles`"). It wasn't. Compiled clean and worked correctly on the *first* real attempt. Verified independently: grepped the file for `IMGUIContainer`/`EditorGUI*`/`GUILayout` (zero hits outside a comment), read the full coordinate-mapping/drag-clamp/insert/remove logic end to end (correct), confirmed real `Painter2D` drawing (`BeginPath`/`MoveTo`/`LineTo`/`Stroke`/`Arc`/`Fill`) and a textbook-correct pointer-capture pattern (`CapturePointer`/`HasPointerCapture`/`ReleasePointer`). The only friction all session was in the *test harness* (a synthetic `SendEvent` doesn't populate the OS-driven pointer-target state UI Toolkit's dispatcher expects) — a scripted-testing quirk a real user dragging a real mouse would never hit.

## 2. The decision

**Migrate ZUI's editor half (`ZUI.Editor` — the `Zui/Scripts/Editor/` folder, everything a `ZUIWindow` uses) to UI Toolkit.** This includes envelope/curve editors and 2D drag controls — Trial 2 specifically disproves the earlier assumption that those needed to stay IMGUI. See §5 for the one real exception.

**UI Toolkit is expected to be sufficient to reproduce ZUI's ENTIRE current surface area — not just the parts already trial-tested.** Say this plainly because it was genuinely conflated mid-discussion before landing here: "custom drawing + interaction" (envelopes, 2D plots, the kind of thing a waveform/tracker-grid display would need) is validated by Trial 2 and is native UI Toolkit territory (`Painter2D`). The ONE thing that has no UI Toolkit equivalent — true Scene-view 3D gizmo manipulation (`Handles.PositionHandle`/`OnSceneGUI`, camera-relative world-space interaction) — is a *different* thing entirely, and grepping confirms **ZUI has zero existing usage of it today**. So the Scene-view constraint in §3 is real and permanent, but it is not a gap relative to what ZUI currently does — only a constraint on a hypothetical future capability nothing in Laubrary has ever needed. Don't let that constraint's presence in this document read as "so some meaningful slice of ZUI stays IMGUI" — as far as anyone has found, none of it does.

This is a real, ground-up rewrite of the control surface, not a refactor — UI Toolkit's retained-mode `VisualElement` tree is a different paradigm from IMGUI's per-frame immediate-mode calls, and there is no mechanical way to auto-convert one to the other.

**Before writing any code**: read the `laubrary` skill's `references/authoring.md`, `references/zui.md`, and `references/ui-layout-rules.md` (`~/.claude/skills/laubrary/`). Laubrary's standing rules — Undo on every mutating action, a tooltip on every control, `CHANGELOG.md` entries in the same change, no version bump without explicit user confirmation, asmdef/namespace conventions — apply exactly as much to the new UI Toolkit code as they did to the old IMGUI code. This migration changes the *plumbing*, not Laubrary's authoring standards.

## 3. Hard constraints — not up for re-litigation, these are Unity platform facts, not opinions

- **Scene-view gizmo/manipulator interaction (`Handles.PositionHandle`, `OnSceneGUI`, `SceneView.duringSceneGui`) stays IMGUI, permanently, regardless of migration progress.** Confirmed via research (2026-07-22): UI Toolkit's "World Space" render mode (Unity 6.2+) places a UI *panel* as an object in 3D space (e.g. a VR menu) — it has no mechanism for arbitrary gizmo-style world-space data editing the way `Handles` does, and there is no announced replacement. **This is a different thing from the envelope/curve editors above** — those are 2D drawing *inside an editor window* (screen-space, `Painter2D`), not 3D Scene-view interaction. Don't conflate them; Trial 2 validated the former, nothing validates a Scene-view equivalent because none exists.
  - Grepped 2026-07-22: **no Laubrary tool currently uses `OnSceneGUI`/`Handles.PositionHandle`/`SceneView.duringSceneGui`** — this constraint isn't blocking anything that exists today. It matters if/when a future tool wants in-scene placement (a level tool, a spline editor). Don't let that hypothetical future need block or complicate this migration — just don't design anything that would make adding IMGUI-based Scene-view tooling *harder* later.
- **Runtime HUDs (`ZuiRuntime`, the `Zui/Scripts/Runtime/` half that ships in player builds and draws from `OnGUI`) are explicitly OUT OF SCOPE for this migration.** Unity's own current guidance recommends uGUI over UI Toolkit as the *default* for runtime UI (UI Toolkit is positioned as better specifically for multi-resolution/world-space/custom-shader cases). Neither trial tested runtime `OnGUI`-style HUD rendering — both were editor windows. Do not fold runtime migration into this work without a separate, dedicated trial first; it's a different risk profile (ships in builds, performance-sensitive, different Unity recommendation).

## 4. Scope of this phase

**In scope**: everything under `Zui/Scripts/Editor/` and every Laubrary tool's editor window that consumes it. That's a real number — 15 `.asmdef` files across the package currently reference `ZUI.Editor` or `ZuiRuntime` (grepped 2026-07-23): `Cartographer`, `ZoetropeLaunimator`, `Launimator`, `Zounds`, `ZoetropePyre`, `Zoetrope`, `Pyre`, `Lazor`, `Rulesets`, `Choreographer`, `Larder`, `AssetKit`, plus ZUI's own two, plus `UIAudit`. Every one of those is a real, working tool with real users (Lautaro, across multiple projects) — this is not a greenfield rewrite with no blast radius.

**Out of scope for this phase** (revisit later, each on its own merits):
- `ZuiRuntime` (runtime OnGUI HUDs) — see §3.
- Anything living outside this repo — several Laubrary consumer projects (PreviewLab, RulesAgentLab, and others per project memory) have their own **physically copied** `Packages/com.lautaro.arino.laubrary/` — this migration happens here first, ports out later, same as every other ZUI change this project has made. Don't try to keep remote copies in sync mid-migration.
- Bespoke canvas painting that isn't a curve/plot shape — Pyre's live preview canvas is called out in `zui.md` as "genuinely bespoke canvas painting... stays raw" *today*, but that assessment predates Trial 2. **Re-evaluate it, don't assume it's still true** — check whether Pyre's canvas is vector/procedural drawing (candidate for the same `Painter2D` treatment Trial 2 validated) or literal `Texture2D` pixel blitting from a baked simulation (a different case, more like an `Image`/raw-texture display, which UI Toolkit also handles natively — investigate before assuming either way).

## 5. What goes where

| Goes to native UI Toolkit (validated) | Stays IMGUI (hard constraint) |
|---|---|
| Forms, buttons, toggles, sliders, dropdowns, boxes/foldouts, `ZUIForm`/`ZUIRow`-style packing, asset pickers (`LabeledObjectPicker`, `ObjectPicker<T>`), context menus, popovers | Scene-view gizmo/manipulator authoring (`Handles.PositionHandle`-style), if/when any future tool needs it |
| 2D drag pads (`ZUI.PositionPad`, `ZUIValue2DControl`'s Static-mode dot) — Trial 1 | |
| Envelope/curve editors (`ZUI.Envelope`, `ZUICurveField`, `ZUIValueControl`'s Curve mode, `ZUIValue2DControl`'s Curve mode) — Trial 2 | |
| The `ZUIEnvelopePresetPopup` picker (built 2026-07-21, IMGUI today) — no reason it can't move, it's a plain list+thumbnail popup, exactly Trial 1's territory | |

**Needs investigation before deciding, don't guess**: Pyre's raw preview canvas (see §4), the Style Editor's live swatch previews, anything else currently flagged `// ZUI-GAP:` or "stays raw" in the existing codebase — grep for those markers as a starting checklist, don't assume the old assessment still holds now that Trial 2 has moved the goalposts on what's feasible.

## 6. Architecture: how old and new ZUI coexist during migration

This cannot be a big-bang rewrite — 15 consuming asmdefs, real tools in daily use. Recommended approach (the implementing session should sanity-check this against what it finds, not follow it blindly if reality disagrees):

1. **Build the new UI Toolkit-based control library in a new namespace/assembly, not in place.** The existing `ZUI` type is `public static partial class ZUI` in the **global namespace** (`Zui/Scripts/Editor/ZUI.cs` etc.) — a new parallel API can't reuse that name without collision. Suggest a real namespace this time (the global-namespace choice for the original ZUI was a historical accident, not a deliberate design — don't repeat it): something like `Laubrary.Zui.Toolkit` or similar. Pick a name, but pick ONE and use it consistently — check `MirageMockup_UIToolkit.cs`/`CurveEditor_UIToolkit.cs` for the raw patterns already proven, but don't feel bound to their exact class names, those were trial scaffolding, not an API contract.
2. **Port ONE real, currently-shipping tool first as a pilot** — not the whole package. Good candidates: something small and self-contained. Skim `Editor/Choreographer/` or `Editor/Larder/` for size before committing; don't assume, check actual line counts and control variety first.
3. Keep the OLD `ZUI`/`ZUIWindow` (IMGUI) fully working and unremoved throughout — every not-yet-ported tool keeps using it. Both can coexist in the same package; they're independent code paths until a given tool is actually switched over.
4. Port tool-by-tool after the pilot proves the pattern, in an order that makes sense once you can see real control-variety-per-tool (start simple, save the most control-variety-dense tool — probably Pyre, given its Envelope/2D-plot/canvas usage — for after the pattern is well-proven elsewhere).
5. Only delete old IMGUI ZUI code once nothing references it anymore. Don't delete speculatively.

## 7. Open design question: the styling system (ZUIStyleSheetAsset → USS)

**Not resolved by this document — the implementing session needs to make this call, ideally with the user, before writing more than a couple of controls.** ZUI's current look is entirely sheet-driven: `ZUIStyleSheetAsset`/`ZUIBoxDef`/`ZUIButtonDef`/`ZUITextDef`, 9-slice "Zheets," `ZUI.PaletteColor`, a live Style Editor (`Zui/Scripts/Editor/ZUIStyleEditorWindow.cs`, "Zeditor"). UI Toolkit's native styling story is USS (a real CSS-like cascade, its own asset type, its own editor tooling — the UI Builder). Options, not evaluated in depth here:
- (a) Generate/export USS from the existing `ZUIStyleSheetAsset` data, keeping the current Style Editor as the authoring UI and USS as a compiled-to format underneath.
- (b) Replace `ZUIStyleSheetAsset` outright with hand-authored `.uss` files + USS custom properties (`--my-color`) as the new "palette," and either retire the Zeditor window or rebuild a thin one over real USS.
- (c) Some hybrid — runtime/data-driven parts (palette colors that might change per-project) stay a ScriptableObject-driven system that *writes* USS custom properties, static layout stays hand-authored `.uss`.

9-slice specifically has a native USS answer already (`-unity-slice-left/right/top/bottom`, confirmed via Unity docs 2026-07-22) — likely simpler than the current hand-rolled Zheet 9-slice renderer, worth using directly rather than porting the old system's internals.

## 8. Reference implementations — study these before writing new code

- `Assets/Framework Trial/MirageMockup_UIToolkit.cs` (+`.uss`) — forms, dropdowns, toggles, asset-row pattern, a `FieldWrap`-style helper for consistent label+control pairing (sidesteps `BaseField<T>`'s inconsistent built-in label widths — a real, documented gotcha from the trial, worth carrying into the real API as a first-class control).
- `Assets/Framework Trial/CurveEditor_UIToolkit.cs` — the `Painter2D`/`generateVisualContent` pattern, pointer-capture drag handling, coordinate mapping. This is close to a working first draft of what the real envelope editor's drawing core should look like.
- Both trial windows are self-contained toy reproductions with dummy data — do not treat them as needing to be preserved once real ports exist; they were verification scaffolding for a decision, not deliverables. Fine to delete once the real migration has working equivalents, your call when you get there.

## 9. Carry the accumulated UX rules forward — the wisdom isn't framework-specific, only the plumbing is

`~/.claude/skills/laubrary/references/ui-layout-rules.md` encodes a lot of hard-won, real-bug-driven rules (no infinite-width controls, pack related fields into rows, tooltip-not-title-text, the label-width-global-leak gotcha, etc.). These rules are about UX quality, not IMGUI mechanics — they still apply, just re-expressed in USS/UI Toolkit terms (e.g. "no infinite-width controls" becomes "don't leave `flex-grow` unset/default on a control that should be a fixed size," not "remember to pass `GUILayout.Width`"). **Update `ui-layout-rules.md` and `zui.md` as the new API takes shape** — same rule as always, docs updated in the same change that adds the API, not after. Once there's a real UI-Toolkit-based ZUI, the doc set will need a clear split: which rules are framework-agnostic (most of them) vs which examples are now stale IMGUI-specific code samples that need a UI Toolkit equivalent alongside or instead.

## 10. Auditing strategy for the new controls

Trial 1 demonstrated this directly: a `VisualElement` tree is trivial to introspect live (walk `rootVisualElement`, query `.tooltip`, `.resolvedStyle.width`, `Overflow`, etc.) — no "toggle recording, force a Repaint pass" dance the way `EditorZuiAudit` needs for IMGUI. Build the UI-Toolkit-native audit as a straightforward tree-walker, not a port of `EditorWindowAuditSection`'s draw-recording approach — it's solving the same problem in a structurally easier way, use that.

## 11. Suggested phased rollout (adjust freely — this is a starting point, not a contract)

1. Resolve §7 (styling) enough to build ONE real control end-to-end.
2. Build the new namespace's core: box/section, form/row packing, button/toggle/slider/dropdown, tooltip-everywhere by default.
3. Port the pilot tool (§6.2). Get it working, screenshot-verify it, have the user actually look at it before going further.
4. Build the envelope/curve editor and 2D-drag controls for real, using Trial 2 as the starting pattern.
5. Build the UI-Toolkit-native audit tool (§10).
6. Continue tool-by-tool per §6.4.
7. Re-evaluate runtime (`ZuiRuntime`) migration as its own separate decision, only after the editor half is proven out — don't assume the same verdict carries over, it wasn't tested.

## 12. Open questions the implementing session should resolve (not this document)

- Exact namespace/naming for the new API (§6.1).
- The styling-system decision (§7) — probably needs the user's input given the amount of authored content (existing Zheets, palettes) riding on it.
- Which tool is genuinely the best pilot (§6.2) — pick based on real inspection, not this doc's guess.
- Pyre's canvas and any other `// ZUI-GAP:`-flagged raw IMGUI spots (§4) — investigate each, don't blanket-assume either outcome.
- Whether `ZUIEnvelopePresetPopup`/`ZUIEnvelopePresetLibrary` (built 2026-07-21, see `CHANGELOG.md`'s `[Unreleased]` section) migrate as part of the envelope editor's port or stay a thin IMGUI popup indefinitely — no technical reason they couldn't move, but check whether it's worth the effort relative to how often that popup is actually used.
