# Shaper UI — vision and design plan (T-0126, 2026-09-01)

Six waves of Shaper engine work (T-0099–T-0115, `feat/shaper`, worktree `D:\UNITY\Laubrary Dev - Shaper`) have shipped node trees, seven fill kinds, a border stage, a light rig, extrusion/bevel/Z-offset, a swarm modifier, a universal-effects stage and a caching layer — all verified through the Unity CLI and static audit test methods, never through a live editor window. This is the plan for the window. It is planning only: no `ShaperWindow`, no code, no change to either worktree.

**Method.** This document mirrors `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0123\CHUNKS-EDITOR-BLUEPRINT.md` — the absence rule, packed rows, "label = action", a stable workspace, Undo designed in from day one, and DECIDED/REQUIRED/OPEN FOR OWNER with a traceability table. Where Chunks proved a rule with a real mouse on a disposable mock, this document cannot — there is no Shaper mock yet — so every DECIDED item here is a design decision grounded in Pyre's shipped editor (the nearest real precedent) and in Shaper's actual, measured engine behaviour (T-0110–T-0115's VERIFICATION.md files, plus the runtime source itself), not in a walked prototype. **The next step this document sets up is exactly that: a T-0120-style mock programme, against this blueprint, the same way Chunks went design → mock → blueprint.**

**Sources read directly for this document** (not summarised secondhand): `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\SHAPER_THE_DESIGN.md` (full); `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0123\CHUNKS-EDITOR-BLUEPRINT.md` (full); `Assets/Packages/Laubrary/Editor/Pyre/PyreWindow.cs`, `PyreWindow.Modifiers.cs` (Pyre's shipped editor, targeted reads of the layer list, modifier stack, `Val`/`FillRow` helpers); the ZUI control inventory (`Assets/Packages/Laubrary/Zui/Toolkit/Zui.cs` + every `Zui*.cs` file in that folder, `ZUIValue.cs`, `ZuiValueControl.cs`, `ZuiFillControl.cs`, `ZuiSectionToggleBar.cs`); `.agenthq/workspace/T-0110/VERIFICATION.md` through `T-0115/VERIFICATION.md` (all six, full); `.agenthq/workspace/T-0106/VERIFICATION.md` (fill-ownership semantics, targeted); and — because a UI plan is only as honest as the dial list it plans for — the actual Shaper runtime source in the second worktree: `ShaperNode.cs`, `ShaperFillDef.cs`, `ShaperBorderDef.cs`, `ShaperLightRig.cs`, `ShaperHeight.cs`, `ShaperSwarmDef.cs`, `ShaperPrimitives.cs`, `ShaperCompositeDef.cs`, `ShaperMatrix.cs` — grepped for every authored field, to answer the envelope-first mandate with measurements instead of a paragraph agreeing with the philosophy.

---

# Part A — the whole plan in one page

A Shaper window is a **two-pane tool in Pyre's own idiom**: a left `ScrollView` of stacked, foldable `Z.Section`s (Canvas, Layers, then — for the currently selected node — Shape/Sweep/Shell, Fill, Border, Swarm, Effects), and a right pane holding the live preview plus a **breadcrumb bar above it** that is the one genuinely new piece of chrome this tool needs. Nesting depth costs no screen space because the breadcrumb, not indentation, is how a user drills into a bag — exactly SHAPER_THE_DESIGN.md's own stated principle, and there is no existing ZUI control for it, so it is the first REQUIRED addition (§I).

The node tree does not get its own always-visible outliner panel. A **Layers** section lists the document's layers (Pyre's own layer-list chrome: reorder grip, enable toggle, select, rename-in-place); selecting a layer shows its root node's card. **Drilling into a bag is a breadcrumb push, not a second tree view** — the left pane always shows exactly one node's own dials (whichever node the breadcrumb is currently on), plus, only for a Bag, a compact members list scoped to *that* bag alone. This is the direct UI expression of C2/C3's "a bag with one child renders its editor as though it were that child" and "the UI shows one level at a time with a breadcrumb."

Every dial in the plan is drawn from ZUI's existing vocabulary — `Z.MicroSlider`, `Z.Value` (the `ZUIValue`/`ZuiValueControl` envelope machinery Pyre already uses for every animatable dial), `Z.Toggle`, `Z.MiniRadio`/`Z.Segmented`, `Z.BoxKeyed`, `Z.Section`, `ZuiReorder`, `Z.Fill`-family — with exactly three REQUIRED additions: a breadcrumb control, a Shaper-native fill picker (the shipped `ZuiFillControl` edits the *old* `ZuiFill` type, not `ShaperFillDef` — a different data model, not a reusable control), and a small frame-cache-state strip (§G). No fourth control was found necessary anywhere in the scope below; every other surface — sweep, shell, swarm, border, light rig, effects — is packed rows of controls ZUI already has.

**The envelope-first finding, stated up front because it is the single most consequential thing this document found.** Grepping the actual shipped Shaper runtime source shows fills, the light rig, extrusion/bevel and the star primitive's three shape dials are *already* `ZUIValue` (envelope-ready) — Shaper's engine side already followed the "resolved via `ShaperValue.Sample` like every other dial" rule B9 states. But **every node's own transform (position, rotation, scale, skew), every sweep/shell/blend dial, every non-star primitive's own geometry dial, and every swarm dial is a plain `float`/`int`/`bool`/`Vector2` today — none of them can be an envelope at all**, not because anyone excluded them on purpose but because those structs were never wired to `ZUIValue` in the first place. Part H turns this into a field-by-field table and a concrete recommendation: promote the transform block, sweep, shell, blend and swarm's continuous dials to `ZUIValue` as a Wave-4 engine task that this UI plan depends on, because a UI cannot offer an envelope on a field that is compiled as a bare float.

---

# Part B — the node tree: layout, breadcrumb, absence rule

## B1. Window layout — DECIDED

- `Z.Split`, left fixed, controls left / preview right, divider persisted per key `shaper.split` (mirrors Chunks §2 and Pyre's own view-state convention).
- Left pane, top to bottom, all `Z.Section`s (green-header, foldable, same chrome as Pyre's Canvas/Layers/Shape/Swarm/Modifiers): **Canvas** (size, frames, rate, seed, palette — mirrors Pyre's `BuildCanvas`), **Light Rig** (document-level, always present — C1 says a document owns exactly one), **Layers** (the layer list), then, scoped to the **currently selected node** (a layer root or whatever the breadcrumb is drilled into): **Shape** (primitive dials, or the Bag's member list, or the Composite's declaration), **Sweep / Shell**, **Fill**, **Border**, **Swarm**, **Effects**.
- Right pane: the breadcrumb bar (§B3), then the preview (fills the rest), then — only when the document has more than one frame — the transport (Pyre's own frame scrubber, extended in §G with the cache-state strip).
- Minimum window size and left-pane minimum width are not fixed numbers here the way Chunks fixed 460pt/820×480 — Chunks measured those against its own mock's actual packed rows with a real mouse. **REQUIRED for the mock stage:** measure Shaper's widest packed row (candidate: the Fill section's mode-dependent body, or the light rig's per-light block) the same way Chunks measured its layer row, and set the minimum from that measurement, not from a guess.

## B2. The node tree data model, mapped to UI — DECIDED

Per C2/C3, a node is one of three kinds — Primitive, Bag, Composite — and a bag's children are themselves nodes of any kind. The UI never shows a full tree widget. It shows:

- **Layers section** — one row per `ShaperLayer` (document-level list), each row selecting that layer's **root node**.
- **The selected node's own card stack** (Shape/Sweep-Shell/Fill/Border/Swarm/Effects sections) — always describing whichever node is *current*: the layer root, or — after a breadcrumb push — a node somewhere inside a bag.
- **A Bag's Shape section, specifically**, replaces the usual primitive-geometry dials with a compact **members list** — one row per child (name, enabled toggle, combine-mode `Z.MiniRadio` [Add/Subtract/Intersect/Blend], a `≡` reorder grip via `ZuiReorder`, a "drill in" button, a remove button), plus "+ Add member" — the same row shape as Pyre's own layer list, one level down. This is the "one nesting mechanism, used at every level" (B2 of the design doc) made concrete: a bag's member row and a document's layer row are the same control, parameterised by which list they edit.

## B3. Breadcrumb — REQUIRED (new ZUI control)

SHAPER_THE_DESIGN.md states this as a UI principle already ("Nesting depth is not capped in the data. The UI shows one level at a time with a breadcrumb, so depth costs nothing in screen space" — B2) — this document does not relitigate that call, it specifies what building it actually requires, because no breadcrumb control exists anywhere in ZUI or Laubrary today (checked directly: grepping the whole `Assets/` tree in both the primary and Shaper worktrees for "breadcrumb" finds nothing outside an unrelated third-party asset, `vTabs`).

- **Shape.** A horizontal strip above the preview: `Layer Name › Bag 2 › Bag 2.1`, each segment a clickable label, the last segment non-interactive (you are already there). Segments truncate with an ellipsis and a tooltip carrying the full name when the strip would overflow the pane width — the same truncation discipline Pyre's own layer-name field already needs at narrow widths.
- **Behaviour.** Clicking a non-last segment pops the breadcrumb to that depth and rebuilds the left pane's node-scoped sections for the node now current. Drilling in (from a Bag member's "drill in" button, or from the breadcrumb itself for a Bag's own card) pushes one segment. The breadcrumb's own state (current path) is **view state, not model state** — same rule Chunks §7.6 states for its own carried-across UI state — so it resets to the layer root on layer switch and does not attempt to survive a document reload.
- **Stable workspace consequence.** Drilling in/out rebuilds only the node-scoped sections (Shape/Sweep-Shell/Fill/Border/Swarm/Effects) — Canvas, Light Rig and Layers stay exactly where they are and do not refold or resize. This is Chunks' own stable-workspace lesson (§6: "toggling a capability does not resize the guide") applied to depth navigation instead of capability toggling.
- **Proposed API shape**, for the ZUI addition: `Z.Breadcrumb(List<string> segments, Action<int> onSegmentClicked)`, returning a `ZuiBreadcrumb : VisualElement` — a peer of `ZuiSection`/`ZuiBox` in the toolkit folder, styled from `ZuiToolkit.uss` like every other control, so it is available to any future Laubrary tool with the same "you're inside something, get back out" need (Cartographer's tile groups and Zoetrope's nested Zoe composition are named as plausible future consumers, though neither is scoped here).

## B4. The absence rule, applied per node card — DECIDED

Mirrors Chunks §1's "a recipe shows only the surfaces its capabilities bring", restated for a node:

- A **Primitive** node's Shape section shows exactly the dial set for its own `ShaperPrimitiveKind` (a disc shows radius; an N-gon shows sides, radius, rotation, corner radius; a star shows arms, radius, length, base width, skew) — never every primitive kind's dials with the wrong ones disabled. Switching kind via the shape picker rebuilds the section's body, the same way Pyre's `BuildSolidBox` rebuilds on form change (`PyreWindow.cs:1451`, comment: "the box rebuilds on form change").
- **Sweep and Shell sections are absent, not folded-and-empty, when their `enabled` flag is off**, with a single "+ Enable sweep" / "+ Enable shell" affordance in their place — mirroring Chunks §5's "hide the dials" for a lone-capability's timing pair, and mirroring how Pyre's own Simulation slot shows "+ Add simulation" rather than a disabled sim block (`PyreWindow.Modifiers.cs:172-179`). Once enabled, the section shows only the axis-relevant dials: a radial-sweeping primitive shows `startDegrees`/`extentDegrees`; a longitudinal one shows `startFraction`/`extentFraction` — never both pairs, because C3/B12 declare each primitive owns exactly one axis identity.
- **Fill is absent on a Subtract member** (FC-3.3's own rule: a member combined by Subtract may not own a fill) — the Fill section does not render at all for a node whose `mode == Subtract`, with a one-line note in the Shape section itself ("Subtract members carve; they don't paint — put a fill on the bag or a sibling instead") rather than a greyed-out Fill section nobody can turn on. This is a genuine exception to "greyed out with the reason shown" (FC-3.3 forbids the state outright, it isn't merely unavailable), and it is named here explicitly because a reader who only remembers "grey it out, don't hide it" from the design doc's own fill-quantity rule (below) would wire this one wrong.
- **A fill control that needs a quantity the shape doesn't publish is greyed out WITH the reason shown** (this is the one place the design doc's own principle — C4, "a fill needing a quantity the shape does not publish is greyed out with the reason shown, not hidden" — is followed literally rather than folded into the absence rule): e.g. a `RampByQuantity` fill on a plain Primitive (which publishes no heat/density/soot) shows its mode selector but the ramp body is disabled with a tooltip naming the missing quantity, because the *fill kind itself* stays a legitimate choice to preview/pre-author even when nothing downstream currently feeds it — unlike Subtract's fill, which is structurally forbidden, not merely unfed.
- **Border is absent by default** (null is the default at every level per BD-1.1) with a "+ Add border" affordance, same pattern as Sweep/Shell. **Border is entirely absent (not shown, not greyed) on a Composite node** — BD's own rule, confirmed structurally by T-0112's CT-2 (a Composite node's border is "structurally inert (no Dilate op emitted)" and CT-6 confirms `ShaperCompositeDef` has no fill field either) — so the whole Fill and Border sections do not render for a Composite-kind node at all; its Shape section instead shows the composite's own declaration (§B5).
- **Swarm is present on every node kind but starts collapsed with "+ Enable swarm"**, since it is universal (T-0113: "Swarm is a modifier available on every generator") but off by default, matching Pyre's own Simulation-slot affordance shape.
- **Effects is absent when the node's compiled source is not Composite-backed** — T-0114's own scope limit states plainly: "Only Composite-sourced (baked-raster) Shaper nodes get this pipeline; Primitive/Bag stay untouched (no `Color32[]` buffer exists there at all)." So a Primitive or Bag node shows no Effects section at all — not a disabled one — until it is a Composite, or (per SHAPER_THE_DESIGN.md's own escape-hatch framing) unless/until a future wave extends the pipeline to analytic nodes. This is stated as a hard absence, not a future promise, because promising a control that reads as "coming soon" on every node is worse than showing nothing.

## B5. A Composite node's card — DECIDED

A Composite node's Shape section shows: which of the nine catalogued composite generators it hosts (a picker, not free text — T-0112's `PyreCompositeCatalog.All` is the source list), its declared `reason` (NotYetSplit or AuthoredData — a read-only badge, not an editable dial, since T-0112's CT-5 proves this is a structural compliance fact the audit checks, not authoring data) and its `reasonNote` (the one text field this card has, because it is a declaration — "why is this still a composite" — not a reference). Below that, its own Sweep/Shell/Transform/Swarm rows apply exactly as any other node's (a Composite is a leaf like a Primitive per T-0112's own doc comment), and its Effects section is present (Composite is the ONLY node kind Effects attaches to, per B4 above).

---

# Part C — fills: seven kinds, palette-quantise, and the picker

## C1. What exists, ground-truthed against the shipped fill contract — DECIDED (facts, not design)

Seven `ShaperFillKind` values ship today: **Solid**, **Gradient**, **RampByQuantity**, **Texture** (T-0106); **IndexedStrip** (T-0110); **HeightField**, **TapestrySteel** (T-0111); plus the **palette-quantise** post-stage (`ShaperFillOps.Quantise`, T-0111, applies to any fill kind, not just TapestrySteel — confirmed: `quantiseLevels` is a field on the shared fill def, not scoped to one kind). Grepping `ShaperFillDef.cs` directly for the dial list per kind:

| Kind | Own dials (beyond the shared `veil`/`heightDelta`) |
|---|---|
| Solid | `solidColor` |
| Gradient | `gradientTint`, `gradientAngleDegrees`, `gradientCentreX/Y`, `gradientSize`, `gradientDepthPixels` (+ a mode: Linear/Radial/Angular/ByEdgeDistance) |
| RampByQuantity | `rampTint`, `rampInputLow/High` (+ which quantity: heat/density/height) |
| Texture | `textureTilesX/Y`, `textureOffsetU/V`, `textureAngleDegrees`, `textureTint` (+ the source sprite — a picker, never a typed path) |
| IndexedStrip | `stripSlots` (a list of `{Color color; float height}`), `stripRepeats`, `stripOrientationDegrees`, `stripOffset`, `stripReach`, `stripPlainColor` (+ Angular/Projection mode) |
| HeightField | `heightFieldScale`, `heightFieldTint` (+ the source preset — a picker over the 245-entry library, T-0111) |
| TapestrySteel | `steelCells`, `steelOctaves`, `steelSeed`, `steelBaseLow/High`, `steelRustColor`, `steelRustAmount`, `steelRustReachPixels`, `steelGrain` |
| (shared, every kind) | `veil`, `heightDelta`, `quantiseLevels` |

**No authoring UI exists for any of the seven today** — T-0110 Part 5, T-0111 Part 3.3 and T-0106's own scope both state this identically ("no ZUI window can create/edit a strip yet — out of scope per the design doc's own task breakdown"). This document is that UI's plan.

## C2. The fill picker — REQUIRED (new ZUI control, not a reuse of `ZuiFillControl`)

Pyre already has a fill-picker pattern — `Z.Fill`/`ZuiFillControl`, a labelled header row whose compact body swaps with the active mode, a "⋯" menu to switch kind — and it is the right **shape** to copy. It is not directly reusable, because it edits the old `ZuiFill` runtime type (SHAPER_THE_DESIGN.md B4's own "there is an existing fill abstraction... every call site in Pyre passes zero for its spatial point... The new fill contract is a new thing that lives in the new namespace"), not `ShaperFillDef`. **REQUIRED:** a new `ZuiShaperFillControl` (or, if the maintainer prefers, a generic `Z.Fill<T>` that both `ZuiFill` and `ShaperFillDef` can plug into via a small adapter interface — a call the mock-stage implementer should make once both shapes are in front of them, not settled here) that:

- Shows a compact **swatch + mode label** row when collapsed (Solid: a colour swatch; Gradient: a mini gradient bar; Texture: a thumbnail; IndexedStrip/TapestrySteel/HeightField: a small rendered preview tile — the fill stage already renders a 128×128 buffer for its own audits, so a cheap low-res preview render is the same code path, not new machinery).
- Opens a "⋯" menu identical in shape to `ZuiValueControl`'s own right-click menu (Z1/Z2 §… wait — mirrors the *convention*, not the exact menu) listing the seven kinds as one radio group, then reveals that kind's own packed-row body below — the mode switch rebuilds only this control's body, same as `ZuiValueControl.SetMode`.
- For **IndexedStrip specifically**, the slot list (`stripSlots`) needs its own compact row-per-slot editor — a colour swatch + a height `Z.MicroSlider` + remove, with "+ Add slot", inside the fill's expanded body. This is the "hand-authored list of slots" B6 describes, and it is the fill kind that most directly proves the colour-plus-height model, so its editor deserves to be legible on day one, not an afterthought.
- For **HeightField**, the source picker is a `LauAsset`-style picker over the 245 imported presets (`Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/*.asset`) — never a typed asset path, per the project's own "never type a reference string" rule. A thumbnail grid (`ZuiThumbGrid`/`ZuiShapeBrowser` are the existing precedent for a browsable asset grid — Pyre's own shape browser reuses `ZuiShapeBrowser.BuildBrowser` for exactly this "click a thumbnail to apply it" shape) is the natural fit given 245 entries is too many for a dropdown.

## C3. Fill ownership and the nearest-ancestor rule, made visible — DECIDED

C4/FC-3.2/FC-3.7: the nearest ancestor that owns a fill paints the whole subtree; a layer root always owns one (falls back to `ShaperFillDef.DefaultRootFill` if empty) and a bag's default is to own the fill itself while its members own none. The UI consequence, stated as a rule rather than left implicit: **a node with no fill of its own shows a one-line note under where the Fill section would start** — "Painted by [ancestor node name]'s fill" — rather than either an empty Fill section or no mention at all. This is a direct application of "label = action": a member with no visible fill dials could otherwise read as "unpainted," when it is in fact painted by something else, and the Chunks lesson (P1/E2 in its own traceability table) is exactly that a control's apparent state must never contradict what is actually happening.

---

# Part D — border, light rig, extrusion/bevel/Z-offset

## D1. Border stage — DECIDED

One `ShaperBorderDef` per node (BD-1.1), absent by default (§B4). When present: `width` is already a `ZUIValue` (confirmed in the runtime source, `ShaperBorderDef.cs:69`, matching BORDER-CONTRACT.md's own "Width is a ZUIValue, so it animates over the layer's life") — so its row is a `Z.Value` exactly like Pyre's own `Val()` helper produces, not a plain slider. `joinsCoverage` is a `Z.Toggle` ("include the outward strip in this node's own published coverage" — the design doc's own B3 default-on rule, with the opt-out named in its tooltip). The strip's own fill (inward/outward/straddling mode + which fill paints it) reuses the same fill-picker control from §C2, because BD-1.1's whole point is "the strip is a region; a fill paints it" — no separate border-colour machinery.

## D2. Light rig — DECIDED

One document-level `Z.Section`, not per-layer (C1: "the document owns a single light rig"). A compact list of lights (name, enabled, colour swatch), each expandable to its own dial block. Ground-truthed against `ShaperLightRig.cs`: `intensity`, `yaw`, `pitch`, `posX/Y/Z`, `range`, `specular` are **already `ZUIValue`** — every one of them is a `Z.Value` row out of the box, no engine work needed first. `colour` is a plain `Color` (not animatable via `ZUIValue`, which is scalar-only — named as a real limitation in §H, not silently treated as fine). `ambientIntensity` (document-wide) is likewise already `ZUIValue`; `ambientColour` is a plain `Color`.

Per-layer response block (`receiveLighting`, `intensityScale`, `castShadows`, `receiveShadows`, `rimStrength`, `specular`, `specularPower`, `specularTint`, `rimPower`) lives inside that node's own card as a small `Z.BoxKeyed("Lighting response", …)`, mirroring Pyre's own `Light` box (`PyreWindow.cs:1503`). `receiveLighting`/`castShadows`/`receiveShadows` are toggles (binary, correctly not envelope candidates); `intensityScale`/`rimStrength`/`specular`/`specularPower`/`rimPower` are already `ZUIValue`; `specularTint` is a plain `Color` (same colour-envelope gap as above).

**The design doc's own honest limitation (C6) belongs in the tooltip, not buried:** "Silhouette and Solids will not produce identical results from identical lights... What can and should be made identical is the shading law." The light rig's section tooltip should say this directly, so an author who lights a Silhouette layer and a Solids layer identically and sees a difference reads it as a documented limit, not a bug to report.

## D3. Extrusion, bevel, Z-offset — DECIDED

Ground-truthed against `ShaperHeight.cs`: `depth`, `angle`, `steps`, `curve`, `taper`, `bevelAmount`, `bevelSteps` are **already `ZUIValue`** — every extrusion/bevel dial is envelope-ready today, no engine work needed. These live in the node's own Shape section as a packed sub-block ("Extrusion" — depth/angle on one row, curve/taper on the next; "Bevel" — amount/steps on one row), collapsed by default when `depth == 0` (a flat shape shows no extrusion rows at all — absence rule again) with a "+ Extrude" affordance, matching Sweep/Shell's own pattern.

Z-offset (`ShaperLayer.zOffset`, confirmed `ZUIValue` in `ShaperLightRig.cs:340` — it lives on the layer, not the node, per B7's "actual Z position is derived from layer order alone... giving each layer a real Z offset") is a `Z.Value` row in the **Layers** section's per-layer row, packed beside the existing enable/name/reorder controls — not a separate section, since it is one number per layer, not a stage with its own state.

**The tilted-render door (B7) has no UI in the first version, and this document does not add one.** SHAPER_THE_DESIGN.md is explicit that the first version resolves flat by decision, with the general path designed in but not exposed — so there is no "tilt" control anywhere in this plan. If a future decision turns tilt on (B7's "one word from you changes this"), it is a new task, not a gap in this one.

---

# Part E — swarm: generic vs. native, shown not hidden

## E1. What exists — DECIDED (facts)

T-0113: swarm is universal (every node kind), with two implementations behind one control — a **Generic** wrapper (works everywhere, O(N) op cost) and a **Native** path (available only for sources that declare it, O(1) op cost, and the only path where `interact` does anything — CT-6/CT-7 measure this exactly: `interact=true` under Native produces a real cross-instance heat-diffusion effect, `interact` under Generic compiles identically whether true or false). Ground-truthed against `ShaperSwarmDef.cs`: `enabled` (bool), `count` (int, 1–64), `positionJitter` (Vector2), `rotationJitterDegrees` (float), `scaleJitter` (float, 0–1), `lifetimeStagger` (float, 0–1), `interact` (bool) — **none of these are `ZUIValue` today**, unlike fills/light/extrusion (flagged again in §H).

## E2. The swarm card — DECIDED

- Absent-by-default per §B4 ("+ Enable swarm").
- Once enabled: `count` (`Z.MicroSlider`, integer decimals), `positionJitter` (a 2D field — `Z.Vector2Field` or `Z.Pad`, ZUI already has both), `rotationJitterDegrees`, `scaleJitter` and `lifetimeStagger` packed two-per-row.
- **`interact` is shown for every swarmed node, never hidden — but its row carries a live badge naming which implementation is actually in effect**, e.g. "Interact — active (Native: [source name])" vs. "Interact — has no effect here (Generic wrapper; this source has no native swarm path)". T-0113's own instruction is explicit: "Which one is in use is shown, not hidden, because it changes what the controls mean" — so the control is never greyed out or removed, only labelled honestly, which is the swarm-specific instance of the exact "label = action" defect Chunks caught (a control whose visible affordance and real effect disagree). The toggle stays interactive even when inert, because a user preparing content for a future native-capable source should be able to set the intent now.
- **A hard-capped count shows the cap and why**, per T-0113 CT-4 ("stateful simulation source with no native batched swarm path... count is held at 6 instead of the authored 20"): the count slider's own tooltip/live-readout states the effective resolved count when it differs from the authored one, rather than silently clamping with no explanation — again, the T-0113 test that measures this (`capped=True, resolved count=6`) is exactly the state a UI must surface, not swallow.

## E3. Generic vs. Native as a document-legibility question — OPEN FOR OWNER

Should the badge in §E2 be a passive label only, or should it double as a live filter — e.g. a small icon in the node-tree breadcrumb / layer row marking "this node's swarm silently downgrades under a document-wide budget change" if the source's native-eligibility can change based on something else authored elsewhere? T-0113's own scope limits don't establish whether native-eligibility is ever dynamic (it reads as a static property of the source type), so this is likely moot — but it's worth a one-line confirmation from whoever builds the mock before assuming it's always static.

---

# Part F — the effect stage

## F1. What exists — DECIDED (facts)

T-0114: a 41-entry catalog (`ShaperEffectCatalog`), each entry declaring a **stage** (pre-composite or post-composite — an explicit, shown property, never inferred: "the stage an effect runs at stays an explicit property, shown in the UI, rather than something the system decides," C8) and a **portability** bucket — measured exactly: `bufferFree=20`, `bufferPadded=7`, `needsSheets=13`, `stuck=1` (41 total). Only Composite-sourced nodes get this pipeline (§B4). Padding is real (`RequiredPadding`/`RenderPadded`, with the picture-rect wiring T-0114 fixed as a real caught bug — Bloom's margin hit went from 0 to 772 pixels after the fix). The catalog is name-string data, not compile-time type-checked (a named, real limitation: a future effect rename would silently desync the catalog and nothing would catch it).

## F2. The effect list — DECIDED

One ordered list, per C8's own recommendation ("one ordered effect list with honest greying-out, rather than four typed lists — a single list is what people expect, and greying with a stated reason is more informative than hiding"). Each row: name, enabled toggle, stage badge (Pre / Post — a small fixed-width `Z.MiniRadio`-style tag, not editable per-instance since stage is a property of the *effect type*, per T-0114's catalog being the source of truth, not per-layer authored data), reorder grip, remove. "+ Add effect" opens a menu grouped by portability bucket (mirrors Pyre's own `ShowAddModifierMenu` GenericMenu pattern), and:

- An effect needing sheets (13 of 41) that the node's current source doesn't publish is **greyed out in the add-menu with the reason** ("needs edge distance, which this generator does not publish" — the exact phrasing T-0114's own GATE check produces: `TintModifier coverage-only=False`), not omitted from the list — same absence-vs-grey distinction as fills (§C, §B4): the *choice* is legitimate to see, its *availability* is conditional.
- The one **stuck** effect (currently hosted nowhere in the project, per C8) does not appear in the add-menu at all — there is nowhere for it to attach and no host, so listing it would be a control that can never be turned on, which the absence rule forbids outright rather than greying.
- Padding is not a per-effect dial; it is a document-computed consequence (`RequiredPadding`) shown as a read-only line under the list when any active effect needs it ("This layer renders 8px padded on each side for Bloom's glow" — legible, not editable, since the value derives from the active effect set).

## F3. Effect dial rows — DECIDED

Each effect's own body uses the **same generic reflection drawer Pyre's modifier stack already uses** (`ZuiReflect`, per `PyreWindow.Modifiers.cs`'s own stated reasoning: "every Geometry/Pixel/Post modifier Pyre ships is editable here with ZERO per-type code"). This is the correct precedent to copy exactly, not adapt: Shaper's 41 effects are the *same* SpriteFx modifier types Pyre already hosts (T-0114's own file list confirms `SpriteFxRecolor.cs`/`SpriteFxModifiers.cs` are the effects being wired, not new Shaper-only types), so the identical reflection drawer applies with no new code, the same way T-0112's composite generators are Pyre forms hosted unmodified.

---

# Part G — cache-state indicator

## G1. What exists — DECIDED (facts)

T-0115 built the full data layer (`ShaperFrameCache.IsFrameCached`, `CountCachedFrames`, per-node dirty propagation, a background non-blocking pre-baker) and named explicitly what it did not build: "No visible per-frame cached/not-cached UI indicator exists, because there is no Shaper document/window to add one to yet... This task built the data, not a control, because the control has nowhere to live." This section is that control.

## G2. The indicator — DECIDED

Pyre already has a frame transport (a thumbed `Z.SliderInt` scrubber, per-Pyre-parity comment in `PyreWindow.cs:731` distinguishing it from the "Frames" count MicroSlider). Shaper's transport is the same control with one addition: **a thin strip of per-frame ticks directly under the scrubber track**, one tick per frame, filled (solid) when `ShaperFrameCache.IsFrameCached(frame)` is true and hollow when not — the same visual language a video NLE uses for a render cache, and a close cousin of the tick-mark rendering `ZuiTimeline`/the Chunks multi-lane clock already do for frame-boundary markers (`ZuiValueControl.Options.frameCount`, `ZuiEnvelope`'s own `showFrameLines`). **This does not require a new ZUI control** — it is a small addition to the existing scrubber composition (a `VisualElement` with `generateVisualContent` painting ticks, the same technique `ZuiEnvelope`'s `CurveThumb`/`OscThumb` already use for a lightweight custom-painted strip) — so it is scoped as an ordinary implementation detail of the Shaper window, not a ZUI-toolkit REQUIRED item.

A one-line summary sits beside the transport: "N / total frames cached" (`CountCachedFrames`), and a background pre-bake in progress shows a subtle animated state on that line (not a modal progress bar — the whole point of T-0115's non-blocking pre-baker is that the UI stays usable while it runs, so a blocking-looking indicator would misrepresent it). **Playback while the pre-bake is incomplete** should scrub live (compute-on-demand, same as any uncached frame today) rather than stall — this is a direct behavioural consequence of the cache design, not a new UI decision, but it is worth stating so a future implementer doesn't accidentally gate the scrubber on "fully baked."

---

# Part H — envelope-first: the audit

**The mandate, restated concretely.** The owner's own instruction: every authored value defaults to being an envelope candidate; "not eligible" is the exception requiring a stated reason. Laubrary already has the machinery for this — `ZUIValue` (Static / MinMax / Curve / Steps / Oscillation) plus `ZuiValueControl`, the exact control Pyre wraps in its own `Val()` helper (`PyreWindow.cs:2574-2586`) to get Undo + live-dirty + frame-marker wiring for free on every dial. **The Shaper UI plan's job is not to invent an envelope mechanism — it already exists and half of Shaper's own dials already use it — the job is to point every remaining plain field at it, or name why not.** The table below was built by grepping the actual shipped struct fields, not by re-describing the design doc's philosophy.

## H1. Already envelope-ready today — no UI decision needed, just wire `Z.Value`

| Group | Fields | Source |
|---|---|---|
| Fill (all 7 kinds) | `veil`, `heightDelta`, `quantiseLevels`, `gradientAngleDegrees`, `gradientCentreX/Y`, `gradientSize`, `gradientDepthPixels`, `rampInputLow/High`, `textureTilesX/Y`, `textureOffsetU/V`, `textureAngleDegrees`, `stripRepeats`, `stripOrientationDegrees`, `stripOffset`, `stripReach`, `heightFieldScale`, `steelCells`, `steelOctaves`, `steelSeed`, `steelRustAmount`, `steelRustReachPixels`, `steelGrain` | `ShaperFillDef.cs` |
| Border | `width` | `ShaperBorderDef.cs:69` |
| Light rig | `intensity`, `yaw`, `pitch`, `posX/Y/Z`, `range`, `specular`, `ambientIntensity` | `ShaperLightRig.cs` |
| Per-layer light response | `intensityScale`, `rimStrength`, `specular`, `specularPower`, `rimPower` | `ShaperLightRig.cs` |
| Extrusion / bevel | `depth`, `angle`, `steps`, `curve`, `taper`, `bevelAmount`, `bevelSteps` | `ShaperHeight.cs` |
| Layer | `zOffset` | `ShaperLightRig.cs:340` |
| Star primitive | `starLength`, `starBaseWidth`, `starSkew` | `ShaperPrimitives.cs:96-100` — the one primitive kind B11 explicitly rules "animatable," and it already is |

This is the majority of Shaper's authored surface by dial count, and it means the fill/light/extrusion sections of this UI plan (§C, §D) are the *easy* part: every row is `Val()`-shaped already.

## H2. NOT envelope-eligible today, with no stated reason found — the real finding

| Group | Fields | Why this matters |
|---|---|---|
| **Node transform** | `translate` (Vector2), `rotation` (float), `scale` (Vector2), `skewDegrees` (Vector2), `origin` (Vector2) — `ShaperTransformBlock`, `ShaperMatrix.cs:113-120` | The single biggest gap. A node's own position/rotation/scale is about as canonical an animation need as exists in any 2D tool (Pyre's own `Val`/`Val2D` helpers exist largely *because* Pyre lets shapes move/rotate/scale over life) and Shaper's transform block cannot do it at all today — every field is a bare `float`/`Vector2`. |
| **Sweep** | `startDegrees`, `extentDegrees`, `startFraction`, `extentFraction` — `ShaperNode.cs` (`ShaperSweep`) | A sweep that opens over time is an extremely plausible ask (a "loading wedge," a radial wipe-reveal, a growing pie-chart) and none of it is possible without an engine change first. |
| **Shell** | `thickness` — `ShaperShell` | A pulsing ring/tube wall is a plausible ask; currently static only. |
| **Blend (soft combine)** | `width`, `sharpness`, `carveStrength` — `ShaperBlend` | A blend band that widens/narrows over a layer's life (two blobs merging on-screen, not just statically merged) is exactly the kind of thing a swarm/organic-growth effect would want, and it's currently locked to a single authored number. |
| **Primitive geometry (non-star)** | `rectHalfW/H`, `rectCornerRadius`, `ellipseRx/Ry`, `diamondRx/Ry`, `triangleBase/Height`, `capsuleHalfLength/Radius`, `ngonSides`(int)/`ngonRadius`/`ngonRotation`/`ngonCornerRadius`, `starArms`(int)/`starRadius` — `ShaperPrimitiveDef` | Every shape's own size/radius/corner-rounding is static except the three star fields B11 singled out. A growing/shrinking disc, a rounding corner-radius animation — all currently impossible. |
| **Swarm** | `positionJitter` (Vector2), `rotationJitterDegrees`, `scaleJitter`, `lifetimeStagger` — `ShaperSwarmDef.cs` | A jitter band that widens over a burst's life (embers scattering further as they age) is a natural ask given swarm already models per-instance lifetime; currently static per-document. |

**Recommendation, stated as a dependency this UI plan has on the engine, not as UI work itself:** before or alongside the mock-editor programme, a Wave-4 engine task should promote the fields in this table to `ZUIValue`, mirroring exactly how the fill/light/extrusion structs already did it — same serialization pattern, same `ShaperValue.Sample`-per-frame resolution, no new mechanism to design. Until that lands, this UI plan's Shape/Sweep/Shell/Swarm sections use plain `Z.MicroSlider`/`Z.Toggle` rows for these fields (§B–E as written), and the moment the engine promotes a field, its row becomes a `Z.Value` row with zero layout change (`Val()`'s own signature already returns the same `VisualElement` shape a plain slider does) — so this document's card layouts do not need to be redrawn when that engine work lands, only their control calls swapped.

## H3. Deliberately NOT envelope-eligible, with a real, stated reason — the legitimate exceptions

| Field(s) | Reason |
|---|---|
| `count` (swarm instance count, int 1-64) | Structural, not continuous: T-0115 CT4/T-0113's own scope note show swarm-count changes recompute the *whole* swarm node (per-instance sub-caching is designed but "not wired into the built evaluator" — T-0115 Part 3.2), so animating count every frame would mean a full swarm recompute every frame, defeating the cache. A per-frame envelope on a value whose every change is O(document) expensive is the wrong shape for `ZUIValue`'s "cheap to resolve per sample" contract (B9's own Burst-shaped requirement: "a generator does not get asked... a hundred thousand times"). Named here as a genuine performance-grounded exception, not an oversight. |
| `interact` (swarm, bool), `enabled` (every stage), `joinsCoverage` (border), `receiveLighting`/`castShadows`/`receiveShadows` (light response), `ngonSides`/`starArms` (int, discrete shape identity) | Binary/discrete structural switches or integer counts that select *which code path runs*, not a continuous authored quantity — an envelope on a boolean or a shape's own side-count would mean the compiled program itself changes shape frame to frame, which is a different (and much heavier) feature than what `ZUIValue` is for. `ZUIValue`'s own `decimals=0` integer mode exists for genuinely-animatable discrete counts (Pyre uses it for frame counts), so this is a real category distinction, not a blanket "ints don't animate" rule — it's specifically about values that reshape the compiled program. |
| Canvas `canvasWidth`/`canvasHeight`, `pixelSize`, `layerSpacing`, Composite's `halfExtentX/Y`/`bakeWidth/Height` | Document/buffer-sizing values, exactly the category Pyre's own `Val()` helper deliberately does NOT wrap (`PyreWindow.cs:714-739`'s `sizeSlider`/`framesSlider` are plain `Z.MicroSlider`, with an explicit comment about why: canvas size drives the *ranges* of every other pixel-scaled control, so it cannot itself be a per-frame-resolved value without those ranges recomputing every sample). This is precedent-matched, not a new call. |
| All `Color` fields (`solidColor`, every fill's `*Tint`/`*Color`, light `colour`/`ambientColour`/`specularTint`) | **Not a design exception — a genuine ZUI capability gap.** `ZUIValue` is scalar-only; there is no equivalent "this colour is an envelope over time" mechanism in the value model these fields would plug into. Pyre's own SpriteFx layer already solves "colour over life" a different way (a `ZuiGradient` sampled by life-phase, used today for particle colour-over-lifetime), and that is the right existing mechanism for Shaper to reuse for time-varying colour — **not** a request to add colour support to `ZUIValue` itself. **REQUIRED, scoped as a small follow-up, not blocking this document:** confirm whether `ZuiGradient`'s life-phase-sampling API can attach to a Shaper fill's colour fields with the same near-zero engine cost the ZUIValue promotions in §H2 have, and if so wire it the same way; if not, name it as an open ZUI gap rather than silently leaving every colour in Shaper permanently static. |

---

# Part I — ZUI-first: what's reused, what's REQUIRED

## I1. Reused as-is — DECIDED

Every control named in this document that already exists in `Assets/Packages/Laubrary/Zui/Toolkit/`: `Z.Section`/`ZuiSection` (every top-level and node-scoped section, folds, header-suffix counts — e.g. "Swarm (on)", "Effects (3)" mirroring Pyre's own `EnabledModifierSuffix`), `Z.BoxKeyed`/`ZuiBox` (per-node sub-blocks needing their own persisted view state, e.g. the light-response block), `Z.Value`/`ZuiValueControl` (every envelope-ready dial, §H1), `Z.MicroSlider` (every plain-float dial pending §H2's promotion), `Z.Toggle`/`Z.MiniRadio`/`Z.Segmented` (mode switches: fill kind's radio group mirrors `ZuiValueControl`'s own mode menu shape; combine-mode picker; Angular/Projection for IndexedStrip), `ZuiReorder` (layer list, bag member list, effect list — three direct copies of Pyre's own `ZuiReorder.MakeGrip` call at `PyreWindow.cs:818`), `ZuiShapeBrowser`/`ZuiThumbGrid` (HeightField's 245-preset picker), `Z.Fill`-family conventions (the new Shaper fill control mirrors, not reuses, `ZuiFillControl`'s shape — §C2), `ZuiSectionToggleBar` (optional, same bulk-show/hide-all-sections affordance Pyre offers — not load-bearing, but free to adopt since the window is already `ZuiSection`-based throughout), `Z.Vector2Field`/`Z.Pad` (swarm's `positionJitter`, node transform's `translate` once promoted).

## I2. REQUIRED — new ZUI additions, in priority order

1. **Breadcrumb** (`ZuiBreadcrumb`, §B3) — blocks the node-tree navigation model entirely; nothing in this plan works without it, since "one level at a time with a breadcrumb" is the load-bearing UI principle the whole left pane depends on.
2. **A Shaper-native fill control** (§C2) — blocks every fill-authoring surface (7 kinds × every node that owns one). Whether it is a wholly new `ZuiShaperFillControl` or a generalised `Z.Fill<T>` with an adapter is an implementation call for whoever builds the mock, not settled here — but *some* new control is required, because `ZuiFillControl` is contractually bound to the old `ZuiFill` type.
3. **`ZuiGradient` life-phase colour, extended (or confirmed already sufficient) for Shaper's fill/light colour fields** (§H3's colour gap) — does not block a first mock (colours can ship static-only in v1, same posture the design doc itself takes toward tilt), but should be resolved before the UI is called "envelope-first complete."

No fourth control was found necessary. The frame-cache strip (§G2) and the swarm interact-badge (§E2) are both compositions of existing primitives (`generateVisualContent` painting, conditional tooltip text), not new toolkit types.

---

# Part J — Undo, stable workspace, traceability, open questions

## J1. Undo — DECIDED, designed in from the start (Chunks §7.1's own lesson, applied before the mistake happens here)

Every mutation in this plan routes through the exact pattern Pyre's own `Val()`/`FillRow()` helpers already establish (`PyreWindow.cs:2574-2622`): `onBeforeMutate: () => Undo.RecordObject(document, "Edit Shaper")` fired once per gesture, `onChanged: () => MarkDirty()` after. Every list mutation (add/remove/reorder a layer, a bag member, a swarm-native source, an effect) additionally calls `Undo.RegisterCreatedObjectUndo` where a new sub-asset is created (a fresh `ShaperNode`/`ShaperFillDef` is plain serialized data inside the document, not a separate asset, so ordinary `RecordObject` on the document covers it — confirmed by the same reasoning Pyre's own layer add/remove uses, `PyreWindow.cs`'s `AddLayer`/`DuplicateSelectedLayer`). This is stated as DECIDED rather than REQUIRED-but-undesigned specifically because Chunks' own §7.1 names Undo as "cheaper to design in than to retrofit" and this document is written before any Undo-less mock exists to retrofit.

## J2. Stable workspace — DECIDED

- **Breadcrumb navigation never resizes or refolds Canvas/Light Rig/Layers** (§B3).
- **Toggling Sweep/Shell/Border/Swarm/Effects on or off rebuilds only that section**, not the whole left pane — same granularity lesson Chunks §6 states ("the production tool should rebuild only the card that changed").
- **The preview's own rect does not move when a section folds/unfolds or a node's dial count changes** — the right pane's layout is fixed (breadcrumb, then preview filling the rest, then transport) independent of what the left pane is doing, so there is no analogue of Chunks' own P3 defect (a timing bar's presence resizing the guide) to reproduce here, provided the left/right panes stay genuinely independent — **REQUIRED for the mock stage: verify this by measurement**, the same way Chunks measured its guide rect byte-identical before/after a toggle, because "the panes are independent" is a design intent until it's measured against a real window.
- **Scroll position in the left pane survives a structural edit** (add a layer, add a bag member, add an effect) — same P2/E3 lesson Chunks fixed and measured (§6 of the blueprint): a newly-added row should not throw the pane to the top.

## J3. Traceability

| Finding / decision | Source | Where in this document |
|---|---|---|
| Nesting shown one level at a time via breadcrumb | SHAPER_THE_DESIGN.md B2, C3 | §B2, §B3 |
| No breadcrumb control exists anywhere in ZUI/Laubrary | grep, this task | §B3, §I2 |
| A bag with one child renders as though it were that child; combine modes appear only with ≥2 members | SHAPER_THE_DESIGN.md B2 | §B2, §B4 |
| Border attaches to any shape node; strip is a region a fill paints | SHAPER_THE_DESIGN.md B3, BD-1.1 | §D1 |
| Composite border is structurally inert (no Dilate op); Composite has no fill field | T-0112 VERIFICATION CT-2, CT-6 | §B4, §B5 |
| Fill needing an unpublished quantity is greyed out with the reason shown, not hidden | SHAPER_THE_DESIGN.md C4 | §B4, §C3 |
| Subtract members may not own a fill | SHAPER_THE_DESIGN.md FC-3.3 | §B4 |
| Nearest-ancestor fill ownership; exclusivity semantics | SHAPER_THE_DESIGN.md C4; T-0106 VERIFICATION §1 | §C3 |
| IndexedStrip: slot list, reach control, Angular/Projection, per-slot height | T-0110 VERIFICATION Part 1–2 | §C1, §C2 |
| HeightField/TapestrySteel fills + palette-quantise; 245-preset library | T-0111 VERIFICATION Part 1–2 | §C1, §C2 |
| Composite catalog: 9 entries, 4 palette-indifferent / 5 not; Effects present only on Composite | T-0112 VERIFICATION CT-5; T-0114 scope limit | §B4, §B5, §F1 |
| Swarm generic vs. native shown, not hidden; interact live under Native, inert under Generic | T-0113 VERIFICATION CT-6, CT-7; SWARM-SPEC.md | §E1, §E2 |
| Swarm count hard-cap with a visible reason | T-0113 VERIFICATION CT-4 | §E2 |
| Effects: 41 catalog, 20 buffer-free / 7 padded / 13 needs-sheets / 1 stuck; explicit pre/post-composite | T-0114 VERIFICATION Part "CATALOG check" | §F1, §F2 |
| Padding + picture-rect: a real bug caught (0 → 772 margin pixels) | T-0114 VERIFICATION "PADDING check" | §F1 (context) |
| Cache data layer exists, no UI yet; per-frame cached/not-cached indicator explicitly deferred | T-0115 VERIFICATION Part 3, scope limit 7 | §G1, §G2 |
| Fills/light/extrusion/border-width/star already `ZUIValue`; transform/sweep/shell/blend/primitive-geometry/swarm are not | grep of `ShaperFillDef.cs`, `ShaperLightRig.cs`, `ShaperHeight.cs`, `ShaperMatrix.cs`, `ShaperNode.cs`, `ShaperPrimitives.cs`, `ShaperSwarmDef.cs`, this task | §H1, §H2 |
| Envelope-first is the owner's stated bias; "not eligible" is the exception | Task brief (owner's own instruction, T-0126) | §H (whole part) |
| Val()/FillRow() Undo+dirty wiring pattern | `PyreWindow.cs:2574-2622` | §J1 |
| Absence rule, packed rows, label = action, stable workspace, Undo-from-start, DECIDED/REQUIRED format | CHUNKS-EDITOR-BLUEPRINT.md (whole document) | Method (top), throughout |
| ZuiFillControl edits the OLD ZuiFill type, not ShaperFillDef | `ZuiFillControl.cs` header comment; SHAPER_THE_DESIGN.md B4 | §C2, §I2 |

## J4. OPEN FOR OWNER

1. **Left-pane minimum width and window minimum size** (§B1) — Chunks measured these against a real mock; this document cannot without one. First thing the mock-editor programme should establish.
2. **`ZuiShaperFillControl` vs. a generalised `Z.Fill<T>`** (§C2, §I2) — an implementation call, not a design one, but worth the owner's steer before two implementers guess differently.
3. **Colour-as-envelope for Shaper fills/lights** (§H3) — whether `ZuiGradient`'s existing life-phase mechanism is the right reuse, or whether this waits.
4. **Swarm native-eligibility dynamism** (§E3) — almost certainly moot (native-eligibility reads as static per source type) but not confirmed against the engine.
5. **The engine promotion in §H2 (transform/sweep/shell/blend/primitive-geometry/swarm → `ZUIValue`)** — this document recommends it as a Wave-4 dependency but does not schedule it; the owner should decide whether it happens before, during, or after the mock-editor programme. Doing it after means the mock's Shape/Sweep/Shell/Swarm sections ship as plain sliders and get upgraded later with no layout change (§H2's own note); doing it first means the mock can demonstrate the full envelope-first vision immediately. Both are legitimate sequencing choices — this document does not pick one.
6. **Whether the "stuck" effect (T-0114, 1 of 41, currently hosted nowhere) should get a host before this UI ships**, so its absence from the add-menu (§F2) isn't permanent by construction.
