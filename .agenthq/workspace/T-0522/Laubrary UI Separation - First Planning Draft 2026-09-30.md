# Laubrary UI: separating function, content, layout and style

**First planning draft · 30 September 2026 · AHQ T-0522 · Research only**

This is a starting point for a planning conversation, not an approved implementation specification. No Laubrary implementation was changed. The intended outcome is that a designer can change presentation in a predictable place while the controls, editing operations and underlying tools continue to behave exactly as before.

## Recommendation

Adopt a Laubrary-wide presentation contract: **C# owns behaviour and live data; semantically named elements expose that behaviour; USS owns static presentation; narrowly defined adapters translate live data and resolved styling into dynamic geometry.** Keep each tool's current appearance during migration. Preserve Zounds' Colorful appearance as a separate skin. Applying Colorful to everything is a later conversation.

This is feasible, but it is a substantial shared-toolkit migration. Moving colour and spacing literals into a stylesheet is only one part. Some controls paint themselves, some window layouts calculate positions using hardcoded sizes, and some Laubrary interfaces still use rendering systems that do not consume USS at all. Those must be addressed explicitly before claiming that *all* Laubrary follows the new architecture.

Start with one representative pilot in both existing visual styles. Prove three things together: the same appearance, the same interactions, and a designer's ability to make a meaningful presentation change without editing C#. Expand only after that pilot establishes the contract and a credible cost per migration family.

The important distinction is between **presentation-independent function** and literally pure mathematical functions. A slider still has focus, pointer capture and transient drag state. The goal is to remove visual policy from those operations, not to pretend that an interactive control is stateless.

## What this research establishes

The starting evidence is the earlier **Zounds UI Styling Inventory for Designer Handoff**, read in full. Its 980 direct style settings versus 305 class assignments show why the request arose. Those are source counts, not a reliable percentage of pixels or UI that a designer controls: one shared rule can affect hundreds of elements, while one direct assignment may legitimately position a moving marker.

I extended the investigation across the package and inspected representative controls, layout helpers, custom painting, runtime UI and the previous Zounds migration. The main checkout was on `x/zounds-sap`, at commit `2ba9770d6601c17f329553fee9706420d24b3a9e`, with substantial existing uncommitted work. These findings describe the **working files inspected**, not just that commit. Unity is 6000.3.10f1. No editor session was driven, no assets were edited and no live visual parity is claimed.

### First-pass inventory

The scan enumerated the package's files with ripgrep and counted C# occurrences of direct style-property assignment and adding/enabling a semantic class. It excluded equality comparisons from the assignment pattern. This is a lexical screening measurement, not a C# syntax analysis: it does not classify intent, trace helper calls, or measure resolved visual results. The full follow-up audit below is deliberately stricter.

| Area | C# files | Direct style assignments | Class assignment/state calls |
|---|---:|---:|---:|
| Shared UI Toolkit layer | 64 | 650 | 238 |
| Zounds editor family, including old and new screens | 110 | 981 | 306 |
| Cartographer editors | 6 | 198 | 3 |
| Launimator editors | 31 | 115 | 5 |
| Pyre editors | 12 | 104 | 13 |
| Chunks editors | 24 | 103 | 4 |
| DotGen editors | 6 | 91 | 8 |
| SpriteFx editors | 8 | 70 | 2 |
| Remaining package editor families | 121 | 326 | 26 |
| **Toolkit plus package editors** | **382** | **2,638** | **605** |

There are 1,105 C# files in the scanned package overall, but most are not UI migration targets. Only three USS files and no UXML files were found in this package scan. A separate screening of the real Shaper editor in its own worktree found 26 C# files and 157 direct style assignments. Shaper is **not included** in the table; adding the entire second checkout would double-count shared code. The Tapestry worktree and consumer projects were not fully audited.

The slight difference from the earlier Zounds inventory is not an improvement or regression measurement: the scan scope/method differs and the working tree is active. Keep the earlier report as its own dated observation.

Several important findings go beyond the counts:

- **Semantic tagging already exists.** Shared controls already identify sliders, labels, fields and other parts with classes. Extend and regularise that vocabulary rather than replacing everything at once.
- **Factories also impose presentation.** Shared creation helpers assign widths, growth, alignment, gutters and margins. Moving styles out of individual controls while leaving those helpers unchanged would leave designers blocked.
- **Custom painting bypasses ordinary USS properties.** The compact slider's gradient colours are C# fields; its numeric editor has fixed geometry. A comment suggests stylesheet integration, but the implementation still describes that integration as future work. No custom-style resolution hook was found in the inspected Toolkit layer. A comment is not evidence that a designer can actually control a colour.
- **The skinned envelope mixes interaction with presentation definitions.** Its curve mapping and dragging belong to behaviour. Grid colour, handle appearance, hover strokes, tooltip-like label styling and padding do not. Some still come from the older style-definition data rather than USS.
- **The Toolkit assembly is editor-only.** The in-game immediate-mode toolkit and the uGUI-based SimpleMenu are separate paths. They cannot acquire USS support by changing a shared editor stylesheet.
- **Some project guidance is historical.** The guide describes older immediate-mode entry points and paths alongside newer corrections. Use the current source and the current UI rules together; refresh migration guidance when implementation lands.

### What the complete audit should record

Make a surface register covering windows, inspectors, popovers, context menus, shared asset browsers, previews, overlays, runtime menus and HUDs. Record each surface's owning tool, active renderer, shared controls, current skin, supported sizes, interaction states and consumer projects. Include held/deprecated tools explicitly as retained, excluded by owner decision, or scheduled; zero USS assignments must never be read as zero work.

For each presentation decision, classify it as **static styling**, **static layout**, **content**, **interaction state**, **dynamic geometry**, or **legacy renderer dependency**. Trace factory helpers and painter inputs as well as direct assignments. Separately count unique class names, classes without rules, obsolete selectors, duplicated tokens and inline overrides. Missing rules can be intentional hooks, so report rather than automatically delete them.

Pair source inspection with laid-out measurements: dimensions, padding, fonts, colours, clipping, focus order and which style source won. Sample populated, empty, disabled, selected, focused, hovered, dragging and error states. Resolve the remaining unknowns before estimating each family. A useful progress measure is **static decisions migrated and verified / static decisions identified**, with dynamic exceptions separately listed. The ratio of assignments to class calls is not that measure.

## The separation contract

The requested three-way separation is easier to enforce if layout is explicitly distinguished from both content and appearance.

| Responsibility | Owns | Must not own |
|---|---|---|
| Function and interaction | Valid values, drag behaviour, keyboard actions, commands, Undo, persistence and preview requests | Colours, fonts, decorative spacing, default row heights |
| Content and meaning | Labels, current values, available choices, tooltips, asset thumbnails, semantic roles and stable identities | A hardcoded colour for every meaning, or persistence keyed by visible text |
| Composition | Which parts exist, their semantic grouping, repeated rows, required control slots and data binding | Unrelated business operations hidden inside a visual template |
| Layout and skin | Static sizes, alignment, margins, fonts, borders, gradients, colours and state appearance | Saved values, which command runs, whether an operation is allowed |
| Dynamic adapter | Mapping values to coordinates, measured responsive grouping, user-resized panes and custom drawing | Independent copies of the designer's spacing, colour or size choices |

For example, the sound editor decides that a value is driven by game code. It tags that meaning; the current Zounds skin makes it amber. A designer can change how that meaning is presented in a future skin, but cannot change whether the value is code-driven. During this faithful migration, amber retains its existing meaning and appearance.

Labels and tooltips should be supplied through one content boundary, outside low-level control drawing. Changing a label must not change an action identifier, binding or saved fold state. An authored asset colour, image or animation is **content**, not a toolkit theme colour: turning all displayed colours into theme tokens would corrupt the distinction between UI chrome and the work being edited.

### What moves, and what remains

| Existing example | Move to USS or a skin asset | Keep in code |
|---|---|---|
| Compact slider | Default width, caption/value spacing, numeric editor width, track/fill colours | Pointer-to-value mapping, clamp, fine adjustment, value formatting and editing |
| Skinned horizontal slider | Track appearance, fill appearance, preferred height | Filled fraction calculated from the live value |
| Band sliders | Band gap, baseline colour/thickness, hover wash, overflow marker appearance | Number of bands, values, band selection, baseline location and bar geometry |
| Envelope editor | Grid/curve/selection colours, line thickness, handle appearance, surrounding padding | Time/value coordinates, zoom, point selection, tangent editing, drag constraints |
| Effect/modifier rows | Preferred control widths, gaps, row height, header padding | Which parameter groups exist and whether whole groups fit the available width |
| Multi-column browser | Column appearance, preferred column width and gutter | Splitting the ordered content into width-dependent columns |
| Resizable two-pane window | Initial dimensions, minimum presentation sizes and divider appearance | User's chosen split, resize interaction and clamping against measured available space |
| Drag insertion indicator | Colour and thickness | Which insertion target is active and the indicator's current location |
| Asset thumbnail or waveform | Frame, background and selection outline | Actual asset image, sampled waveform and play position |

**Dynamic geometry is not permission to keep visual constants in C#.** The band chart needs to calculate bar positions, but the gap and cap thickness should be supplied by its resolved style. The row-wrap calculation may stay, but it must measure the styled controls or consume the same resolved size tokens. Changing row height in USS while hit testing still assumes twenty pixels is a broken migration.

Some present calculations can eventually be replaced with ordinary flex layout. Do that only where it reproduces the current grouping and wrapping exactly. Preserve deliberate group boundaries and the stable-workspace rules. A contextual toolbar must not grow and move the canvas when a selection changes.

### Custom painting requires a presentation adapter

A painted curve does not magically become themeable when its parent gains a class. Keep a small drawing adapter that receives the curve's data and reads its colours, thicknesses and handle appearance from USS custom properties. The drawing algorithm stays in C#; **the visual decisions become stylesheet-owned**. Unity documents this custom-property bridge for custom controls. [Unity 6.3: custom styles for custom controls](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-create-custom-style-custom-control.html).

Read styling when it resolves or changes, cache it, and request a repaint. If a metric changes, invalidate the relevant geometry as well. Avoid repeatedly rebuilding a whole element tree. Keep visible handle size and interaction hit tolerance distinct so a cosmetic change cannot accidentally make a control impossible to grab.

Ordinary style setters in C# override USS; increasing selector complexity will not fix that. Each migration must remove the old static assignment, including the factory assignment, not merely add a parallel USS rule. USS also does not offer the web CSS `!important` escape hatch. [Unity 6.3: style precedence](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-uss-selector-precedence.html).

## A semantic vocabulary a designer can use

Use a consistent **block / part / modifier** convention. A block names the control's purpose, a double underscore names a part, and a double hyphen names a variant or state. The examples below are **proposed stylesheet names**, not existing C# types or a demand to rename every existing hook immediately.

| Control | Root and parts | Variants or state meaning |
|---|---|---|
| Button | `zui-button`, `zui-button__icon`, `zui-button__label` | `zui-button--confirm`, `zui-button--danger` |
| Toggle button | `zui-toggle`, `zui-toggle__icon`, `zui-toggle__label` | `zui-toggle--on`, `zui-toggle--mixed` |
| Compact slider | `zui-slider`, `zui-slider__track`, `zui-slider__fill`, `zui-slider__caption`, `zui-slider__value`, `zui-slider__input` | `zui-slider--editing`, `zui-slider--dragging` |
| Slider with a thumb | Same slider contract, plus `zui-slider__thumb` | `zui-slider--thumb` |
| Range slider | `zui-range`, `zui-range__track`, `zui-range__selection`, `zui-range__low-handle`, `zui-range__high-handle` | `zui-range--dragging-low`, `zui-range--dragging-high` |
| Band sliders | `zui-band-sliders`, `zui-band-sliders__band`, `zui-band-sliders__fill`, `zui-band-sliders__baseline`, `zui-band-sliders__overflow` | A band's `--hovered`, `--dragging`, `--overflow-high` or `--overflow-low` state |
| Envelope | `zui-envelope`, `zui-envelope__plot`, `zui-envelope__readout` | `zui-envelope--dragging`, `zui-envelope--has-selection` |
| Shared field/card | `zui-field`, `zui-field__label`, `zui-field__control`; `zui-card`, `zui-card__header`, `zui-card__body` | `zui-card--collapsed`; field meaning such as `zui-field--code-driven` |
| Effect chain composition | `lau-chain`, `lau-chain__header`, `lau-chain__parameters`, `lau-chain__remove` | `lau-chain--compact`, `lau-chain--expanded` |

Use built-in hover, focus and disabled pseudo-states where they describe the actual control. Add explicit semantic states where needed, such as a latched toggle or selected envelope point. Do not invent unsupported pseudo-classes. Code owns those states; the stylesheet owns their appearance.

For painted envelope parts, expose documented custom properties such as `--zui-envelope-curve-color`, `--zui-envelope-grid-color` and `--zui-envelope-handle-radius`. These parts need not become hundreds of separate retained elements merely to get class names. The plot is the styled element; its published custom properties describe the painted subparts. The same principle applies to a painted slider fill.

Do not call a role `orange`, `width-20` or `third-child`. Name its purpose. Keep binding IDs, asset IDs and saved-view keys separate from style classes and display labels. Style reusable elements with classes; reserve unique element names for binding/querying where needed. Avoid selectors that depend on an accidental chain of nested containers.

Publish a small selector catalogue: meaning, parts, supported states, available tokens and one visual example per control. Keep existing names as temporary aliases while consumers migrate. Removing an alias is a compatibility change requiring a package-wide usage check, including other worktrees and consumer projects.

## Layout ownership and the designer workflow

**USS makes an existing structure restylable; it does not by itself turn a C#-built hierarchy into a visually editable layout document.** There are no UXML files in the inspected package, so promising drag-and-drop layout editing immediately would be misleading.

The initial contract can retain existing C# composition while moving all static presentation out. A designer would be able to change spacing, sizing, alignment, typography and skins without recompiling the controls. Moving a field into another card, changing hierarchy or adding a new action could still need a programmer.

For broader layout independence, my recommendation is to **pilot one UXML shell or reusable card template alongside the USS work**, rather than convert every screen to templates upfront. Stable named slots receive the live data-driven controls. The designer can rearrange supported slots; binding validation catches missing or duplicate slots before the screen silently loses functionality. Unity's UI Builder edits the hierarchy and attributes of UXML documents. [Unity 6.3: structuring UI](https://docs.unity3d.com/6000.3/Documentation/Manual/UIB-structuring-ui-elements.html).

This is a proposed extension to the task's minimum target, not a requirement already decided by the owner. It should be assessed in the pilot: if the desired designer workflow is USS editing in a text editor, do not require a wholesale UXML conversion. If the goal is visual rearrangement in UI Builder, it needs explicit template and binding work. Arbitrary dynamic content and custom canvases will still have programmed behaviour in either approach.

A concrete acceptance exercise is for someone unfamiliar with the implementation to change one row's spacing, a shared text role and a painted handle colour using only the documented presentation assets, observe the live result, and restore the original. If templates are included, also move one supported field group without changing its binding. This exercise tests the actual benefit, not just whether the code has fewer literals.

## Skins that preserve today's different appearances

Use four clear presentation layers: shared structural rules; layout metrics/recipes; a selected named skin; and narrowly scoped tool-specific rules. This is a responsibility model, not a claim that USS provides CSS cascade layers. Use explicit attachment, scoping and controlled selector specificity to make the intended ownership reliable.

The first named skins should capture **the existing general Toolkit appearance** and **Zounds Colorful**. If a tool already differs, preserve that through a documented variant rather than silently normalising it. The current general appearance is a compatibility baseline, not a declaration that it represents the owner's final design preference.

Use shared semantic tokens for text roles, surfaces, separators, selection, warnings, confirmation and destructive actions, with component-specific tokens where their meaning differs. Capture today's row heights, widths and spacing as role-specific metrics before trying to consolidate them. Two equal numbers are not automatically one design decision: coupling unrelated controls just because both are twenty pixels tall makes later design work harder.

A window chooses its presentation context at the root. Popovers, floating windows, inspectors and menus must receive the same context explicitly where they are separate roots. Two skins must be able to coexist in separate windows without global mutable skin state leaking between them. Swapping a skin should not recreate data bindings, reset scroll, lose focus or alter edited values.

Keep the extracted gradient textures initially. They are valid skin assets when USS selects them; discarding them merely to make everything procedural would endanger fidelity. The existing extractor captured exact fills, borders, corners and text treatments, including effects that cannot simply be assumed to have direct USS equivalents. Unity's supported-property list should be the boundary, not the feature list of browser CSS. [Unity 6.3: supported USS properties](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-USS-SupportedProperties.html).

Make the authority unambiguous after extraction: freeze the imported legacy skin as a reference, then maintain the new USS and its image assets as the new presentation source. Either retire the old generator for that migrated skin or change its output ownership so it cannot overwrite designer edits. Do not leave two competing editable sources.

Image-backed gradients can remain exact while ordinary borders, spacing and text become independently editable. A later procedural equivalent can be evaluated separately. This plan does **not** promise that every artistic texture can be redesigned by changing a colour token.

## Recreating the current look without a redesign

Reuse the Zounds port's **extract, compare, migrate, compare again** discipline. T-0457 records the skin extraction and gallery; T-0460 records the side-by-side capture and pixel-difference harness. The source for these tools is present. Their board entries also contain stale lost/unfinished status, so prior measurements are historical evidence, not proof that every current screen is already complete.

The owner's later confirmation accepted the general Zounds port approach. Its documented fidelity distinction matters: equal layout, shapes, colours and behaviour, with acknowledged rasteriser edge noise between IMGUI and UI Toolkit. For this refactor's same-renderer changes, start with a stricter expectation: **no intentional visible difference**. Do not inherit a blanket tolerance that could hide a shifted control.

### Baseline capture

Before any implementation, freeze the exact working baseline, including relevant uncommitted files and skin assets, into an isolated snapshot. A commit hash alone does not capture today's dirty working tree. Do not clean, reset or commit other people's work to obtain that snapshot. Record package/Unity versions, display scale, font assets, window size, selected asset, skin, scroll positions and expanded sections.

Extract resolved presentation values and preserve the textures that actually produce today's appearance. Capture each control in meaningful states and each surface in representative empty and populated states. For previews, use a fixed time/seed and fixed input data. Put old and candidate captures at identical physical-pixel size; docking, fractional display scaling and client-area offsets must match.

### Comparison harness

Adapt the existing harness rather than build a separate comparison philosophy. An old and new instance must use isolated control/style versions or separate baseline/candidate editors. Opening two windows that both resolve to the newly edited shared toolkit is **not** a before/after comparison.

Produce original, candidate, side-by-side and difference images, plus per-region metrics and geometry measurements. The existing harness uses difference thresholds above 8/255 and a stronger 48/255 view. Preserve those diagnostics, but do not treat either number as an automatic acceptance allowance. Small differences in a large blank window can conceal a completely wrong small button.

The current harness compares the common image extent when sizes differ. For the migration, add an explicit failure for mismatched image dimensions instead of accepting a cropped comparison. Also reject blank/stale captures and check that the expected content is present: prior project experience shows that a successful Windows capture call can still return a blank client area for some Toolkit windows.

For identical-renderer stable regions, target exact equality. Calibrate unavoidable capture noise by taking repeated unchanged-baseline captures first. Any tolerance or dynamic mask must be narrow, documented and reviewed. Freeze animation rather than broadly masking its whole panel. Cross-renderer changes need separate review of known text/edge rasterisation differences; they must still preserve geometry and recognisable appearance.

### Functional and visual acceptance together

For each migrated family, exercise hover, focus, click, keyboard editing, pointer capture, dragging beyond bounds, cancellation, typed values, clamping and disabled state. Verify Undo and persistence on duplicate/synthetic assets, not the owner's real data. Reopen the window and repeat the task to catch stale state. Check long labels, realistic item counts, minimum/typical/wide sizes, and the owner's display scaling.

Run the existing laid-out UI audit with all relevant sections expanded, then inspect the images. Recheck the full UI Guide: proper control choice, compact layout, tooltips, reference pickers, stable workspace and scroll preservation. Existing guide violations discovered during baseline capture should be listed separately; changing them belongs to an explicit improvement, not a hidden redesign inside the migration.

Finish each implementation wave with the Handover Walk twice from an empty state and through to an observable result. For Zounds that includes hearing the edited sound, not merely watching a slider move. Report **verified by probe**, **verified by eye**, and **not verified** separately. Follow the project's direct-method verification convention rather than launching Unity's Test Runner from an agent session.

## Proposed delivery phases

These are proposed future implementation phases. This research task authorises none of the build work below.

| Phase | Scope | Exit evidence |
|---|---|---|
| 0. Baseline and contract | Surface register, exact baseline snapshot, selector/token contract, exception categories, skin ownership | Every active family has an owner and migration classification; reference captures are repeatable |
| 1. Representative pilot | Button, toggle, compact slider, one range control, band sliders, envelope, one width-sensitive row; both current skins | Appearance and interaction parity; designer-only styling exercise; no unexplained static overrides |
| 2. Shared foundations | Factories, fields/cards, reflected/serialized controls, spacing, split/column layout, asset browser shell, popovers and style attachment | Representative consumers still match their baselines; metrics and hit testing share one source |
| 3. Simpler Toolkit tools | Cabinets and BackSplash first, then Choreographer, Lathe and Tapestry, adjusted by the complete inventory | Whole-tool acceptance, not just a control gallery |
| 4. Dense Toolkit surfaces | Pyre, Chunks, SpriteFx, DotGen, Cartographer, Launimator, Zoetrope, Mirage, graphs and remaining Zounds panels; coordinate Shaper separately | Each tool's layouts, previews, editing and both relevant skin paths verified |
| 5. Legacy editor surfaces | Remaining immediate-mode windows/inspectors, including retained old Zounds surfaces and held-tool editors | Real Toolkit replacements or explicit owner-approved exclusions; compatibility reference retired only after acceptance |
| 6. In-game UI | Runtime-safe shared controls and presentation loading; immediate-mode HUD and uGUI/menu migration by consumer | Player build, keyboard/gamepad/mouse navigation, focus, scaling and persistence verified |
| 7. Consolidation | Remove obsolete aliases/static overrides, document designer workflow, package assets, consumer smoke checks and final compliance pass | Every registered surface accounted for; all remaining dynamic adapters justified |

The pilot intentionally includes difficult controls. A button-only success would say little about the envelope, computed layouts or data-rich windows where this architecture can fail. It need not introduce a permanent new menu or designer tool; use the existing comparison facilities and temporary research fixtures.

Zounds participates immediately through the pilot and compatibility checks. Its remaining complex panels are migrated after the shared contract is credible. Do not overwrite its existing presentation with a generic skin while doing that. The rollout order is based on dependency and complexity, not on a claim that the early tools embody the desired final look.

Keep old and new styling paths isolated behind a temporary migration boundary. Change a shared default only after its known consumers pass. Preserve asset formats, bindings, saved view identities and existing control APIs during the first waves; where an old API supplies a cosmetic width, migrate the caller to a semantic layout role and track the remaining compatibility path. Otherwise the legacy width silently keeps winning over USS.

Coordinate worktrees before each shared-control change. Shaper's real editor is in a separate branch, and Tapestry has another active worktree. Verify downstream consumers from a clean package installation, including USS, textures and other imported assets, so a developer checkout cannot hide missing packaging dependencies.

### The runtime track is real scope

For a literal all-Laubrary USS strategy, runtime work cannot be permanently omitted. First separate player-safe interaction/presentation code from editor-only asset browsing and authoring services. Preserve existing menu behaviour and control semantics through adapters, then migrate one real consumer with its existing look. Retain scene/prefab compatibility until replacement behaviour has been proven.

A temporary bridge that keeps legacy runtime style assets while sharing semantic meanings may reduce rollout risk, but it is **transitional separation**, not completion of the USS target. Do not force a backend port of held/deprecated tools without confirming that they remain supported; equally, do not count an unexamined legacy surface as complete.

## Risks, cost and decisions for the next conversation

The main risks are shared regressions, invisible overriding styles, layout measurements disagreeing with hit testing, custom painters ignoring the selected skin, and state loss when a visual refresh rebuilds a view. Separate-root popups and runtime packaging are easy omissions. Visual fidelity is also vulnerable to font/display-scale differences and to accidental normalisation of today's per-tool variants.

Mitigate these with isolated migration boundaries, small reversible changes, per-property ownership, a documented dynamic-exception register, old/new captures from truly independent implementations, and acceptance at both control and whole-task level. Add a guard against **new static presentation writes** in migrated areas, with reviewed exceptions for data geometry and content images. A blunt ban on every style assignment would penalise valid controls while missing hardcoded painter colours.

**Planning estimate, not a delivery commitment:** allow roughly 5–10 focused engineer-days for the baseline and representative pilot, then 20–40 more for shared foundations, Toolkit tool rollout and its verification. Remaining legacy editors, runtime migration and consumer stabilisation could add another 15–35. This puts a literal all-surface programme in the approximate **40–85 engineer-day** range before any optional project-wide reskin. Confidence is low until the surface register and pilot exist; these figures are judgement-based effort ranges, not a conversion rate derived from the assignment count. Parallel work can shorten elapsed time only after the shared contract stabilises; a shared Unity editor and final visual acceptance remain serial constraints.

The review should settle these points:

1. **Which existing tools best represent the desired general layout and style?** I searched the local task descriptions, relevant project memory and the supplied conversation; I found the promise to provide that list, but no later selection. Preserve each existing look for now. Do not assume Zounds is the baseline for everything.
2. **How independently should designers rearrange structure?** USS-only changes within existing composition, or visual template editing too? The recommended pilot tests one template without committing to a whole-package rewrite.
3. **Which legacy/held tools remain supported?** Keep them visible in the register and decide whether to migrate or explicitly retire/exclude them. This determines the meaning and cost of “all”.
4. **How should implementation releases be divided?** Recommend editor architecture first, legacy and runtime as tracked subsequent releases, while keeping the full programme visible. Completing one release must not be reported as completing all Laubrary.
5. **What evidence accepts a visual difference?** Default to none for same-renderer architectural changes. Review unavoidable cross-renderer edge differences explicitly rather than granting an arbitrary whole-screen percentage.

My proposed next implementation brief, after this plan is reviewed, is the baseline plus representative pilot. Its deliverable should be a demonstrated separation contract and measured rollout estimate, not an assertion that a mass replacement will be straightforward.

## Evidence and limits of this draft

**Verified by source inspection and static measurement:** package-wide screening counts; representative shared controls, factories, custom painters and dynamic rows; editor-only Toolkit assembly boundary; immediate-mode runtime and uGUI menu paths; the Shaper supplementary count; the existing extraction and comparison implementation; and the AHQ task's required scope.

**Verified by eye:** no live Laubrary screens or new comparison images were inspected in this research pass. Visual measurements quoted from the earlier Zounds migration are historical records, not tests rerun here.

**Not verified:** current compile/player health, full surface coverage across every worktree/consumer, complete legacy inventory, final designer workflow, and actual feasibility/cost of each migration wave. No human has dragged in a newly refactored pane because no such pane was built during this task.

Local evidence: AHQ T-0522; the 30 September Zounds designer-handoff inventory; T-0457's extraction record; T-0460's comparison record; the 28 September owner-confirmed port approach in shared project memory; the current package sources; the Unity development guide and all three UI-guidance layers. Source screening covered the main package and a narrow Shaper supplement, with deeper reading of representative controls rather than a line-by-line audit of every tool.

The Unity documentation linked beside the relevant claims is for the project's 6.3 version family and was checked on 30 September 2026. It supports the platform mechanics; the proposed boundaries, phases, acceptance criteria and effort ranges are this draft's recommendations.
