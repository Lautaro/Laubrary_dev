# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **ZUI — a reusable "3D direction / orientation" control + a titled non-folding frame + an on-hover 3D preview (task #67).** Grounds a shape that recurred across the tools — a direction on a SPHERE authored as separate yaw/pitch/distance sliders (or a flat 2D pad with no sense of the sphere it lives on): PyrePlus's Gem/solid KEY LIGHT (`gemLightYaw`/`gemLightPitch`/`gemLightDistance`) is the prime example. **`ZuiDirection3D`** (`Zui/Toolkit/ZuiDirection3D.cs`, `Z.Direction3D(yaw, pitch, distance, tooltip, onChanged, options)`) is a compact control: a small DRAGGABLE LIT SPHERE whose per-pixel Blinn-Phong point light makes the lit hotspot itself the readout (so a direction reads as a real point on a ball), a baked light-position marker (filled when the light faces us, a hollow ring when it is BEHIND — disambiguating the back hemisphere where the shading goes flat), numeric FALLBACK fields (Yaw/Pitch/optional Distance, scrub-draggable), and a larger 3D preview that opens on HOVER over the control (a non-modal `Z.Popover`) or PINS open via a toggle. It edits plain float fields and fires `OnChanged(yaw, pitch, distance)` once per edit for the caller to wrap in its Undo/Dirty helper; the sphere is a cheap reference GIZMO (its light-position convention `Ldir = (cos p·sin y, sin p, cos p·cos y)`, `lightPos = Ldir·distance`, `range = distance+1.2` matches `PyrePlusRenderer`'s Gem/solid light exactly so it reads faithfully), never the tool's actual render, and its `Texture2D`s are `HideAndDontSave` and destroyed on `DetachFromPanel`. **`ZuiFrame`** (`Zui/Toolkit/ZuiFrame.cs`, `Z.Frame(title, tooltip, children)`) is the "titled bordered container that does NOT fold" primitive that sat between the three folding containers ZUI already had (`ZuiBox` folds from its title, `ZuiSection` collapses, `ZuiFoldCard` folds a card body) and a bare `Z.Text` Section heading (which owns nothing and draws no border) — for wrapping ONE self-contained control as a single always-visible labelled unit; children go into its body (overridden `contentContainer`), the tooltip renders as a "?" on the title row, and it auto-aligns its field labels (`ZuiLabelAlign`). **`ZuiPopover`** gains a `modal` option (default true = unchanged): when false the full-window scrim's `pickingMode` becomes `Ignore` and the popover no longer dismisses on an outside click or Esc — a NON-MODAL overlay that floats above the tool without stealing its input (what a hover preview needs), with the CALLER owning dismissal; UITK's per-element `PickingMode.Ignore` doesn't stop the child panel from being pickable, so a pinned interactive preview still works. New USS (`ZuiToolkit.uss`): `.zui-frame`/`.zui-frame__header`/`.zui-frame__title` (the titled frame) and `.zui-dir3d`/`.zui-dir3d__gizmo`/`.zui-dir3d__nums` (the dark rounded inset the shaded sphere sits in, matching the 2D pad's framing). **Proven end-to-end by wiring a real site:** PyrePlus's Gem/solid light **Direction** — the flat `Z.Pad` (yaw × pitch) plus the separate Distance MicroSlider are replaced by ONE `Z.Direction3D` (showDistance) wrapped in a `Z.Frame`, editing the SAME `gemLightYaw`/`Pitch`/`Distance` fields under the same `"solid.light.angles"` gear-view key; the ZUI Control Gallery's "2D & spatial" box gains a live **Light direction** demo of the framed control. **Strictly a UI/control change — verified byte-identical:** the renderer was not touched, so the default-64/16-frame/seed-1234 spec reproduces its exact baselines (Disc `0x2F7929699530855D`, Gem `0xB113DC81E4DE442B`) before and after. **Verified in-editor:** compiles clean; `ZuiAudit` at 800px returns **0 findings** on the visible chrome (foldedSkipped counts PyrePlus's many state-hidden conditional/gear blocks, not this control); the control drives the fields (editing the numeric yaw field moved `gemLightYaw −55 → 77` through the Undo-safe path), the `ZuiFrame` wraps it (1 of each in the live tree), and the hover preview's `Z.Popover` opens (`IsOpen`); and rendered sphere textures place the lit hotspot in the correct quadrant for up-left / up-right / below / behind directions (the behind case going dark with a hollow ring marker), confirmed by eye from baked PNGs.
- **ZUI — a popover / context-menu framework: `ZuiPopover` (a floating in-panel card) + `Z.Menu` (a richer, ZUI-styled GenericMenu stand-in) for the UI Toolkit half (task #68).** Gives every migrated tool a reusable floating-panel primitive and a fluent menu built on it, replacing raw `GenericMenu` where a ZUI-styled, richer menu is wanted. **`ZuiPopover`** (`Zui/Toolkit/ZuiPopover.cs`, `Z.Popover(anchor, build, opts)`) shows a bordered ZUI card anchored to a trigger element's `worldBound`, added into the host window's OVERLAY LAYER — the outermost `zui-root` ancestor (a `ZuiWindow`'s `rootVisualElement`), so it inherits the shared stylesheet/palette for free and is torn down when the window rebuilds, falling back to the raw panel root outside a zui-root. It positions BELOW the anchor and, once the panel has a resolved size (via a `GeometryChangedEvent`), FLIPS above / CLAMPS within the window when the preferred side would overflow (a panel taller than the window pins to the top); it DISMISSES on an outside click (a full-window transparent scrim whose own pointer-down closes it — a click on the panel targets a descendant, never the scrim) and on Esc (a TrickleDown `KeyDownEvent` on the focusable scrim). The scrim forces its own padding to 0 so the absolutely-positioned panel maps through `WorldToLocal` in a clean coordinate space regardless of an inherited zui-root padding. `Show()` returns a handle whose `Close()` dismisses it (idempotent, fires `Options.onClosed`); `Options` cover minWidth/maxWidth, gap, edge margin, preferred side, and the two dismiss toggles. Chosen as an IN-PANEL overlay rather than a `PopupWindowContent` (the IMGUI `ZUI.Popover`) precisely because a UITK popup window gets no ZUI stylesheet, can't share the tool's element tree, and clips to a fixed `GetWindowSize` — an in-panel card gets the palette, can be measured/re-placed against the real window bounds, and overlaps the tool's own chrome. **`Z.Menu(anchor)`** (`Zui/Toolkit/ZuiMenu.cs`) is a fluent builder over `Z.Popover` composing menu rows from real ZUI controls: `.Section(title)` (a bold heading — the flat, always-visible, one-fewer-click replacement for GenericMenu's slash-nested submenu groups), `.Separator()`, `.Item(label, tooltip, onClick, checked, enabled, icon)` (a flat full-width hover-highlighting row driven by a `Clickable` — matching ZuiBox/ZuiSection's own clickable headers rather than a boxed Button — that runs the action then dismisses, GenericMenu-style; an optional embedded icon resolves BY NAME via `ZUIAssetLibrary.FindIcon`, and `@checked` draws a tick in a fixed gutter that keeps every label aligned), `.IconItem(...)` (icon-leading shorthand), plus the flyout extras GenericMenu has no equivalent for — `.Toggle(...)` (a persistent `Z.Toggle` row that STAYS OPEN so several settings can be flipped in one visit) and `.Radio(label, options, ...)` (a `Z.MiniRadio` group, opt-in `closeOnSelect`). Every row carries a tooltip; disabled rows grey out and ignore clicks. **Proven end-to-end by converting a real site:** PyrePlus's "+ Add modifier" `GenericMenu` (`PyrePlusWindow.Modifiers.cs`, driving BOTH the per-layer and spec-wide Global Modifiers stacks) now opens a `Z.Menu` anchored to the button — the reflection-discovered catalog is sorted group-then-label, so the Geometry / Pixel / Post groups that were slash-nested submenus become flat SECTION headings; behaviour is otherwise identical (each row adds one modifier, undoable, and rebuilds the stack). The ZUI Control Gallery (`Laubrary/ZUI Control Gallery`) gains a live **Popovers & menus** demo box exercising every row type plus a bespoke `Z.Popover` flyout. New USS (`ZuiToolkit.uss`): `.zui-popover`/`.zui-popover__scrim`/`.zui-menu*` (opaque bordered card — UITK has no box-shadow, so the border does the lifting — flat hover rows, section headings, separators, a check gutter, icon column). **Verified in-editor:** compiles clean; a driven menu opened at a realistic 780px width positions in-bounds (left/top within the window, honouring the 190 min-width) with the correct structure (5 items / 2 sections / 1 separator / 1 toggle / 1 radio / 1 tick), and BOTH dismiss paths fire (a synthesized Esc `KeyDownEvent` and an outside-click `PointerDownEvent` each close it); all menu icons resolve to real textures (an alpha-composited montage confirms the glyphs render); and `ZuiAudit` at 780px returns **0 findings** on both the converted PyrePlus window and the gallery's new chrome (foldedSkipped counts state-hidden subtrees — the asset-window browser, `.Shown(false)` sections — not un-expanded folds).
- **PyrePlus — a FIRST-CLASS BORDER on the flat 2D forms, with a "draw over the matte" option that also solves #65 (tasks #60 + #65).** The six flat 2D forms (Disc / Crescent / Ring / Streak / Star / Polygon) get an optional coloured RIM — the 2D counterpart to the 3D solids' lit edge lines, and what a Disc used as a ball wants for a rim. `PyrePlusLayer` gains four fields: `borderEnabled` (bool, default false), `borderWidth` (a `ZUIValue` in px over the layer's life, default 2), `borderFill` (a `ZuiFill` — Solid / gradient / spatial, default solid near-white), and `borderOverMatte` (bool, default false); `Clone()` deep-copies the width envelope + the fill (the two bools ride `MemberwiseClone`), and an older asset predating the fields deserialises with the initializers ⇒ off ⇒ unchanged. **Render approach — a per-layer SILHOUETTE-OUTLINE post-pass, not per-form band math:** the renderer reads the layer's FINISHED alpha, computes an inside-distance transform (a two-pass chamfer), and recolours the outermost `borderWidth` px of the drawn silhouette to `borderFill` — the rim alpha = the fill's own alpha × a band weight × the shape's own coverage, so the rim inherits the shape's anti-aliased edge and never grows the silhouette. Chosen over threading a border band through each of the six forms' distinct boundary tests because ONE implementation covers all six (and any swarm/union silhouette) AND it keeps the FILL and the BORDER as separate buffers — exactly what #65 needs. **Draw-over-matte (#65):** with `borderOverMatte` off the rim folds into the layer normally (part of its Write-matte coverage / Luma mask / composited-and-clipped Draw output); with it ON, the shape's FILL feeds the layer's role alone and the BORDER is deferred and composited ON TOP of the fully-composited stack (in paint order, before global post) — so a Star can BE a matte (its fill stencils/masks the layers above) while its border still draws on top, with NO separate outline-only layer. The border pass is gated on `borderEnabled && IsFlat2DBorderForm(shapeForm)`, so every 3D solid / Text / Sprite / Fire / Fireball / Sparkle layer, and every border-off layer, takes the exact pre-border path (border-enabled also forces a Draw layer into its isolated scratch so the pass can read its own alpha — a no-op when off). The editor adds a **Border** sub-box to the Shape section for the six forms only: off = a single compact "Border" toggle; on = a `Z.BoxKeyed` with Enable / Width (`Val`) / Fill (`FillRow`) / "Draw over matte" — all ZUI controls, tooltipped, Undo-safe via the window's `Dirty`/Fill/Val contract. **Strictly additive — verified byte-identical:** with `borderEnabled` false the render is unchanged (the default-64/16-frame/seed-1234 spec reproduces its exact baselines — Disc `0x2F7929699530855D`, Gem `0xB113DC81E4DE442B` — before and after). Functional checks: a Disc / Star / Polygon with the border on shows a clean coloured rim of the set width; and the #65 case — a Write-matte Star with `borderOverMatte` ON, below a Draw layer clipped by its channel — shows the Draw layer through the star-shaped fill hole while the star's border draws on top (the border is NOT in the mask).
- **PyrePlus — a per-layer LIFETIME WINDOW, ported 1:1 from Pyre1 (task #55).** Every `PyrePlusLayer` now carries a `startFrame`/`endFrame` frame window, exactly like vanilla Pyre's `Layer.startFrame`/`endFrame`: the layer is ALIVE only across `[startFrame, endFrame]`, its life is lerped 0→1 across that range (clamped), and OUTSIDE the range the layer is INACTIVE — it contributes nothing that frame. The renderer gate is `Mathf.Clamp01((frame - start) / Mathf.Max(1, end - start))` — the byte-for-byte twin of `BlastRenderer`'s own layer-life formula (`BlastRenderer.cs:565`) — computed once per layer at the top of `RenderFrame`'s layer loop and fed to every per-layer draw (modifier stack, shape/swarm dispatch, per-layer post, and every matte Eval) via a new `layerLife` in place of the shared spec-level `life` (which stays the clock for the spec-wide background fill). Because PyrePlus's per-particle swarm timing (spawn moment + particle life) is all a FRACTION of the layer life, remapping the layer's life across `[start, end]` carries the whole swarm's timing along with it for free — the correct Pyre1-like behaviour. **Default = the FULL range so every existing spec is byte-identical:** `startFrame` defaults 0 and `endFrame` defaults **-1**, a SENTINEL meaning "the last frame" resolved to `frameCount-1` at render time — the sentinel (rather than a hardcoded 15) is what keeps a spec byte-identical at ANY frame count (a full-range window on a 24-frame spec still ends at frame 23, giving `life = frame/(frameCount-1)` verbatim), and an older asset written before these fields existed deserialises with them absent ⇒ the initializers (0 / -1) apply ⇒ full range. Both are value-type ints, carried by `Clone()`'s `MemberwiseClone`. The editor adds a per-layer **"Life (frames)"** control at the top of the Shape section — a single `Z.MinMax(isInt: true)` two-handle range over `[0, frameCount-1]` (one bounded min/max pair, per the layout rules; never two separate fields), tooltipped and Undo-safe through the window's `Dirty` wrapper; dragging the high handle to the far right restores the -1 sentinel so a full-range window keeps auto-tracking the frame count, while any inset stores a concrete end. **Scope note:** the stateful sim forms (Fire/Fireball) and the per-layer `simulationModifier` slot keep their own internal replay clock over the global timeline — the window gates their CONTRIBUTION at the layer level (drawn only in-window) but does not remap their internal burn/sim clock; a windowed Fire remapping its internal simulation is a deeper follow-up. FrameStep swarm timing (non-default) maps to the global frame count, so a windowed FrameStep swarm is a documented edge; the default Window timing is fully window-relative. **Strictly additive — verified byte-identical:** with every layer at the default full range the render is unchanged (the default-canvas-64/16-frame/seed-1234 spec reproduces its exact baselines — Disc `0x2F7929699530855D`, Gem `0xB113DC81E4DE442B` — and the new per-layer `layerLife` is bit-for-bit identical to the old spec-level `life` over all 16 frames, so EVERY form/config, Streak-swarm included, renders unchanged). Functional check: a layer windowed to `[4,8]` is blank (0 opaque pixels) on every frame before 4 and after 8, and its life sweeps exactly 0→1 across 4→8 — proven by the render of windowed-`[4,8]`-at-frame-`4+k` being BYTE-IDENTICAL to a full-range 5-frame spec at frame `k` (both give `life = k/4`) for every k.
- **Laubrary.SpriteFx — a reusable "SpriteFx Stack" authoring tool: create / browse / tag / preview a stack of the shaped colour-mask effects, with full Static / Min-Max / Curve authoring of every param (task #54).** Builds the author-and-preview surface for the runtime SpriteFx filter. **`SpriteFxSpec`** (`Runtime/SpriteFx/SpriteFxSpec.cs`, CreateAssetMenu `Laubrary/SpriteFx/Stack`) is a persisted, reusable effect recipe — a `[SerializeReference] List<PixelModifier>` stack plus `duration`, a life-remap `envelope` and a hashing `seed`, mirroring `SpriteFxFilter`'s inline fields so a filter can source them from an asset with no behaviour change (`SpriteFxFilter` gains an optional `stack` field that overrides its inline fields when assigned — additive, byte-identical when null). Named `*Spec` (the `ChunkSpec`/`PyrePlusSpec` convention) to avoid the existing static stack-runner `SpriteFxStack`; the user-facing name everywhere is "SpriteFx Stack". **Keystone — the shared reflection drawer now authors animation:** `ZuiReflect` renders a `ZUIValue` field with the FULL `Z.Value`/`ZuiValueControl` (the ⋯ menu's Static MicroSlider / Min-Max range / Curve envelope) instead of the previous static-float-only fallback, and gained a `Gradient` branch (`Z.Gradient`) it was missing — so a reflected effect's animatable params get real over-life curve authoring and its gradient fields (e.g. Tint's cross-gradient) render, closing the "static value only" prototype limitation for EVERY reflected-field tool at once (PyrePlus and Chunks modifier stacks gain both for free). Editor-authoring only — no render / byte-identity change. **`SpriteFxStackView`** (new `com.Lautaro-Arino.Laubrary.SpriteFx.Editor` asmdef) is a reusable, host-agnostic control — `Build(List<PixelModifier>, Host)` — drawing the effect stack with drag-reorder, folding per-effect cards, reflected bodies, per-effect Copy/Paste, and an Add menu limited to the shaped gather-free pixel family (reflection + `SpriteFxStack.IsShaped`: Tint/Contrast/Brightness/Saturation/Posterize/OrderedDither/LayerDissolve/AlphaMask). It edits a plain `List<PixelModifier>`, so the same control drives a `SpriteFxSpec` asset, a `SpriteFxFilter` component, or any future host (Zoe/Chunks); "SpriteFx"/"effect" is surface naming only — the serialized `*Modifier` class names are untouched (renaming them would null the 42 `[SerializeReference]` entries in the committed demo assets). **`SpriteFxStackWindow`** (`: ZuiAssetWindow<SpriteFxSpec>`, one menu item `Laubrary/SpriteFx Stacks`) is the browse / create / duplicate / rename / delete / tag window, hosting the stack view plus a Timeline section (Duration MicroSlider / Life-remap curve / Seed) and a live **Preview**: pick an input sprite, scrub a Life slider, and Play — reusing the runtime `SpriteFxFilter.Apply` (inline) so the preview is WYSIWYG with what plays, reading the sprite's pixels exactly as the filter does and handling a non-readable texture gracefully; the input sprite is preview-only state (window-scoped, remembered per-spec in EditorPrefs, NEVER written into the asset). A `SpriteFxStackEditorLink` (`[InitializeOnLoad]`) registers Open+Create with `LauAssetEditors`, so a `SpriteFxSpec` is pickable and creatable from any inspector's `LauAssetField`. Additive throughout; the package ships zero assets (a host authors stacks into its own `Assets/SpriteFx`). **Follow-ups (landed):** (1) tags moved to the `ZuiAssetWindow` BASE — a single shared `IMGUIContainer(LauTagField.Draw)` island in `BuildUI` (gated on a selected, saved asset), so all 12 UITK asset windows (Chunks/Pyre/PyrePlus/Larder/Mirage/Choreographer/BackSplash/SpriteCatalog/Zoe/Weapon/Ammo/SpriteFx) surface tags again — restoring parity with the IMGUI base — and the SpriteFx window drops its interim per-window island (a real UITK `Z.Tags` control to replace the shared IMGUI island is a future ZUI enhancement); (2) `SpriteFxFilterEditor` gives the runtime `SpriteFxFilter` component a curated UITK inspector — a shared-Stack picker (`LauAssetElement`) with an inline `SpriteFxStackView` + Timeline fallback when no Stack asset is assigned, plus always-visible Dispatch radios; (3) `ReactionFx` (Zoetrope) gained an optional `SpriteFxSpec bodyFx` that `ReactionFxPlayer` plays on the Zoe's OWN body sprite on hit/death — a flash/dissolve riding the live animation via a `SpriteFxFilter` on the body renderer (not a point-spawned `ICombatFx`, whose `Play(worldPos)` contract can't reach the live body); additive field on the `[Serializable]` reaction (existing Zoe assets get null), Zoetrope→SpriteFx being a legal downward asmdef dependency.
- **ZUI — a cheap "smoothness" slider for curves and 2D paths (Catmull-Rom corner-rounding).** `ZUIValue` gains a `smoothness` (0→1): **0** = the authored per-segment bend (sharp corners at the points — the existing behavior), up to **1** = a uniform Catmull-Rom spline *through* the points (rounded corners). `ZUIEnvelopeEvaluator` blends the two by the slider, GATED to **≥3 points** (2 points have no interior corner — always a straight line), and `ZUIValue.EvaluateCurve` clamps the smoothed result to `[yMin,yMax]` so a smoothed path can't overshoot off the plot (or a bounded value out of its range). The `ZuiValue2DControl` 2D pad exposes it as one **"Smooth"** slider (curve mode) driving both axes together — Catmull-Rom is separable, so per-axis smoothing at one shared tension IS the 2D spline (the pad's points are evenly-timed 2D knots) — and the plot draws the **sampled** spline (the exact runtime evaluation, with the same clamp) so it's WYSIWYG in both the full plot and the collapsed thumbnail. A cheap way to get smooth pathing, deliberately **NOT** a precision curve tool (no per-point tangent handles). Strictly additive: `smoothness` defaults 0 and the evaluator early-returns the exact existing path at 0 — **verified byte-identical** (edit-mode probe: smoothness 0 == the old evaluator to maxDelta 0 over 200 samples; smoothness 1 rounds by up to 0.089; the clamp holds range; 2 points stay linear to maxDelta 0). Every ZUIValue curve and 2D travel path across all tools (Pyre / PyrePlus swarm paths, Custom swarm shapes, etc.) gains it. (A 1D curve-*editor* slider — the data + runtime already support it — is a possible follow-on; only a `ZuiValueControl` slider + a smoothed `ZuiEnvelope` display would be needed.)
- **ZUI — the 1208 system icons are now embedded off-disk in a Unity-invisible `Icons~/` folder instead of being imported assets (task #66, per `ZUI_ICONS_DESIGN.md`).** The Phosphor icon set stays usable by name but stops being 1208 texture imports per project — no import cost, no `Library` artifacts, and (the point) they never again pollute a `Texture2D` object picker. `ZUIAssetLibrary` gains an off-disk loader: a `zui://icons/<file>.png` sentinel scheme, `k_SystemIconsPathAbs` (absolute disk path, prefers the hidden `Icons~` once it exists else the plain folder), `LoadEmbeddedIcon` (`File.ReadAllBytes` + `ImageConversion.LoadImage` into a hidden cached `Texture2D`, case-insensitive basename index, reducing any reference form — bare name, `foo.png`, sentinel, or a stale `Assets/.../Icons/foo.png` path — to its basename so existing sheets keep resolving), and a single `LoadIconTexture`/`IsSystemIconPath` choke-point every picker/editor uses. `FindIconDirect`/`GetAvailableIcons` route system icons through the loader; the Style Editor picker, pattern picker, icon-picker popup and `ImportIcon` (Duplicate-and-Edit) all resolve sentinels; the two system-vs-custom path-prefix discriminators become the sentinel test. Viable ONLY because ZUI already resolves icons by name, not object reference (`iconLibrary` is `[HideInInspector]` legacy). Safe to move because a full-project scan proved **0 authored assets reference any icon by GUID**. Verified live in the dev host: all icons load and decode, `GetAvailableIcons`=1208 all-sentinel, `FindIcon` resolves sentinel + stale-path + bare-name, the refresh dropped the 1208 entries with no missing-GUID/broken-ref errors. Editor-only by construction; the runtime `Resources` fallback is untouched. **Re-syncing the 5 consumer projects (step 9) is a separate out-of-repo pass.**
- **PyrePlus — spec-wide GLOBAL MODIFIERS, ported 1:1 from Pyre1 (task #56).** A `globalModifiers` list on `PyrePlusSpec` applies its modifier stack to EVERY layer (after each layer's own stack), mirroring `BlastRenderer.BuildStack`'s global pass (GlobalPassOffset 1000 / GlobalModBase 500 / GlobalPostBase 700 / GlobalLayerSalt -1 so a global modifier's per-layer RNG differs per layer). A **Global Modifiers** section sits below Layers in the editor. Strictly additive — empty by default ⇒ byte-identical (Disc `0x2F7929699530855D`, Gem `0xB113DC81E4DE442B` unchanged).
- **PyrePlus — a 2D POLYGON form + a subgrouped form picker (task #59).** New `ShapeForm.Polygon` (regular N-gon, `polygonSides` 3–12; 3=triangle, 4=square, 6=hexagon) appended to the enum. The Shape form picker is regrouped into Flat-2D / 3D-solid / Text-Sprite / Sim subgroups. Append-only enum ⇒ existing specs unchanged.
- **PyrePlus — swarm ergonomics (task #62):** scale-by-index gains an Index/Scale-labelled envelope (index numbers on the index axis, no frame numbers — it's a per-index result, not a timeline); Orient greys out where it's a no-op; live SwarmScale shows a preview trace. **PyrePlus layout (task #63):** Canvas above Layers, both as green-header containers, per-row Dup beside the Matte toggle, collapsed sections show a count. **ZUI controls (task #64):** slider double-click resets to default; Shift = gentle (0.1×) / Ctrl = coarse (10×) drag; the fill control shows a preview swatch; `ZuiFill`'s zoom+centre became animatable (`ZUIValue`) via a lossless byte-identical migration; the 2D pad's numeric inputs work in curve mode.

### Fixed
- **PyrePlus matte — disabling a Matte role no longer wipes its settings (task #57).** The renderer honours a `matteEnabled` gate (default true), so toggling a layer's Matte off keeps its channel / clip / relief / luma settings for when it's turned back on — no data loss.
- **PyrePlus matte — the heightmap relief and luma matte now actually shade (task #58).** Relief applies a real multiply from the height gradient (light-angle responsive), and an empty target channel no longer clips the whole layer to nothing; luma matte works across clip modes with the scope respected.
- **PyrePlus render bugs (task #61):** textured Dots now spin; Circle/Area swarm no longer draws a star artifact (uniform-disc RNG); Fire supports an off-centre emitter (`fireEmitterOffset`, byte-identical at 0,0); the Canvas-size slider drags; the preview "Scale" (a GIF-export upscale, no-op on the live preview) is relabelled "GIF scale"; the Outline modifier's faint-fill default lowered to 0.08.

## [0.9.0] - 2026-07-25

### Added
- **Laubrary.SpriteFx — a Burst BATCHED alpha-mask overlap utility for pixel hit-detection, verified equivalent to a managed reference (task #49, the last slice of the batch).** An ADDITIVE, OPT-IN utility (`Runtime/SpriteFx/SpriteFxHit.cs`) that tests MANY candidate mask-vs-mask overlaps at once on Burst. It does NOT rewire the live combat flow — the immediate per-hit path (`Combat.TryDamage` → `Hitbox` → `ReelHitFilter.ConfirmHit` → `ZonedAnimationPlayer.PixelOverlaps`, in `Runtime/Combat2D` + `Runtime/ReelCombat` + `Runtime/Launimator`) is already cheap (coarse MetaLayer cells gated by the Physics2D collider broad-phase) and is left byte-for-byte intact; NO combat file was touched. Burst pays off only at HIGH candidate-pair counts or FULL-RESOLUTION alpha masks, where the immediate per-pair managed test would stall the main thread — so adopting it is a decision left to the user. **Packing model (many pairs pack into flat NativeArrays):** `masks` is ONE flat `NativeArray<byte>` holding every instance's coverage bytes end-to-end (alpha 0..255 per cell, row-major, bottom-left origin — GetPixels32 layout; a cell is "covered" when its byte ≥ the pair's threshold, min 1); an `SfxMask` is one instance's placement (its byte offset + w/h length into `masks`, plus its bottom-left cell position x/y and size w/h in a SHARED integer grid, so mixed-PPU sprites resolve in one world-pixel space); an `SfxMaskPair` carries the two masks inline BY VALUE + the PRECOMPUTED overlap AABB (loX..hiX, loY..hiY, exclusive upper) so the job walks ONLY the intersection region + the byte threshold; `SfxMaskHit` is the per-pair result — a hit flag + the first coincident cell in shared-grid coords (row-major scan, so it matches the reference exactly), Miss ⇒ (−1,−1). A managed `SfxMaskBuilder` concatenates per-instance coverage into the one flat buffer and hands back each `SfxMask` handle. **Job structure:** `MaskOverlapJob : IJobParallelFor` — ONE work item per PAIR, `[BurstCompile(CompileSynchronously = true)]` — broad-phase-skips a disjoint AABB immediately, else walks the intersection region reporting the first pair of coincident covered cells; both the job and the inline path call the SAME Burst-legal `SpriteFxHit.TestPair(in pair, in masks)` so they can't drift (pure integer/byte math ⇒ the job is byte-EXACT vs the managed path, not merely "within 1/255"). **Clean static API:** `SpriteFxHit.BatchOverlap(pairs, masks, out results, alloc, useBurst, batch)` (inline OR job, blocking on Complete), plus `BatchOverlapInline`/`Schedule` for finer control, an overload that follows the project toggle (`SpriteFxSettings.UseBurstJobs`, mirroring #47 — default inline since job scheduling costs more than the loop for small pixel-art masks), `MakePair(a, b, threshold)` (precomputes the AABB), the independent managed reference oracle `MaskOverlap(a, b, masks, threshold, out hitX, out hitY)`, and mask-build helpers (`CoverageFromColors` for a sprite's alpha, `CoverageFromCells` for MetaLayer-style int cells — SpriteFx must not reference Launimator so it takes the raw `int[]`, value>0 ⇒ covered — and `CoverageFromSprite` reading a readable sprite's rect alpha). **Verified rigorously (edit-mode probe):** compiles clean; the Burst job actually compiles + runs (`Schedule().Complete()`); EQUIVALENCE over **1200 random pairs** (varied sizes 1..40px, varied placements from near-coincident to far apart, varied coverage density 0→solid, thresholds {1,64,128,200}; 209 genuine hits / 991 genuine misses) — the Burst job's per-pair hit/no-hit AND first-hit-point EQUAL the managed reference oracle for EVERY pair (**0 mismatches, 0 first-point mismatches**), the inline path == the job (**0**), and the job re-run (different batch size) == the job (**self-deterministic, 0**); a **1000-pair** batch runs through the job in one shot with **0 mismatches** vs the reference (scale sanity, correctness not a benchmark); and the shared render is provably untouched — the canonical PyrePlus Gem still hashes **`0xB113DC81E4DE442B`** (default 16-frame spec, FNV-1a-64 over RGBA). **Adoption path (the integration step, deliberately NOT wired here — rewiring the working combat flow autonomously risks destabilizing it):** each frame, collect the candidate pairs that already survived the cheap broad-phase (distance + collider overlap + faction) into a `NativeArray<SfxMaskPair>` over a shared packed-mask buffer (built with the `CoverageFrom*` helpers from the same painted-silhouette coverage the meta-layer carries, or a sprite's full-resolution alpha), then in **LateUpdate** call `SpriteFxHit.BatchOverlap(...)` ONCE and read the per-pair results — replacing the per-pair inline `PixelOverlaps` calls only when the candidate-pair count or mask resolution justifies it.
- **Laubrary.Chunks — a SpriteFx modifier stack styles sampled debris, baked once per chunk at spawn (task #48).** Now that the stateless SpriteFx modifiers live in the neutral `Laubrary.SpriteFx` module (#45–#47), a `ChunkSpec` can carry a `[SerializeReference] List<PixelModifier> modifiers` stack (default empty) that is baked ONCE into each SAMPLED chunk's cut texture at spawn — a cheap way to style the debris look (tint / posterise / dither / dissolve and the other shaped SpriteFx pixel modifiers) without per-frame cost. The pass hooks into `SampledChunkSprites.Build` (right after the sub-rect is sampled + the existing ChunkTintMode tint, before the `Texture2D` is built): a new `ApplyModifiers` converts the cut `Color[]` to `Color32[]`, resolves + runs the stack through `SpriteFxFilter.Apply(..., useBurst: false)` (which dispatches to `SpriteFxStack.RunInline` — so a chunk bakes exactly what a `SpriteFxFilter` component would play), and writes the styled pixels back. Resolved at **life 0** (the spawn instant — it is a one-time STILL pass, not animated over the chunk's life), with a fresh **per-chunk seed** so hashing modifiers (LayerDissolve scatter/erase, AlphaMask noise, any Min–Max param) give each cut fragment its own pattern. Scope: SAMPLED debris only (Sample source set) — it does not touch procedural pixel-squares, authored sprites or animated content. Only the eight SHAPED pixel modifiers apply (the gather-free family `RunInline` runs: Tint/Contrast/Brightness/Saturation/Posterize/OrderedDither/LayerDissolve/AlphaMask); a VoronoiCrack or geometry/post modifier is a silent no-op here, matching `SpriteFxFilter`. **Strictly additive and default-empty:** the empty / all-inert stack is a hard SKIP (returns before touching the pixels or the RNG) so a chunk built with no modifiers is byte-identical to before — verified: a no-modifier sampled chunk FNV-1a-64-hashes identically before and after the change (`0xEF9A054A30E959A5`), and the shared modules are untouched (the canonical PyrePlus Gem still hashes `0xB113DC81E4DE442B`). With a Tint (or Posterize+OrderedDither) stack the cut texture is visibly restyled vs the raw sample (confirmed by a before/after PNG). Wiring: `Runtime/Chunks` asmdef gains a reference to `com.Lautaro-Arino.Laubrary.SpriteFx` (Editor too); the Chunks window (`ChunkWindow`) exposes the stack inside the Sampled Pseudo-3D Debris section as a "Modifiers" sub-box, REUSING the Pyre/PyrePlus polymorphic modifier-list pattern (drag-reorder grip + enable + "+ Add modifier", each body drawn by the shared `ZuiReflect` reflection drawer, every edit Undo-recorded) — the add menu lists only the shaped family so it never offers a modifier that would no-op. **Deferred (documented, not built):** a per-frame ANIMATED pass over each chunk's life — per-chunk-per-frame filtering is costly (it would re-run the stack every Update on every live chunk), so this slice bakes the look once at spawn.
- **Laubrary.SpriteFx — a RUNTIME sprite filter + project dispatch toggle: apply a stateless colour/mask stack to a live SpriteRenderer over a triggered timeline (slice #47, the "flash on hurt" use case).** Building on #45 (the module) and #46 (the Burst-shaped kernels + `SpriteFxStack.Resolve`/`RunInline`/`Schedule`), the SpriteFx runtime module gains its first MonoBehaviours. **`SpriteFxFilter`** (`Runtime/SpriteFx/SpriteFxFilter.cs`, `[RequireComponent(typeof(SpriteRenderer))]`) holds a `[SerializeReference] List<PixelModifier>` stack (any of the eight shaped ones — Brightness/Tint/Contrast/Saturation/Posterize/OrderedDither/LayerDissolve/AlphaMask; non-shaped/disabled entries are skipped by `Resolve`), a `duration`, and an easing `envelope` (`AnimationCurve` remapping raw progress 0→1 → the LIFE value fed to the stack's animatable curves, identity by default). `Play()`/`Play(float)` triggers it; while active each Update it RE-READS the renderer's CURRENT sprite pixels — so it rides on top of a live Reel/Animator animation (the underlying frame keeps advancing beneath the flash), resolving the stack at the current life and applying it into a **pooled `Texture2D`** that it swaps onto the renderer, restoring the source sprite when the timeline ends. **Live re-sampling** distinguishes a fresh source frame (whatever sprite is on the renderer that ISN'T our own filtered sprite = the Reel wrote it this frame) from a held frame (renderer still shows our filtered sprite ⇒ reuse the last source, never double-filter our own output); **pooling** keeps one working `Texture2D` (resized only when the source geometry changes) and one filtered `Sprite` (rebuilt only when rect/pivot/PPU change — otherwise the same sprite just reflects the texture's new pixels), both freed in `OnDestroy`; **restore** only stomps the renderer if WE are still the one on it (never overwrites a frame a Reel advanced to). The single core `SpriteFxFilter.Apply(pixels, W, H, mods, frame, life, seed, useBurst)` is what both `Update` and the verification call — so a probe tests exactly what plays — dispatching to `SpriteFxStack.RunInline` (managed) or `.Schedule().Complete()` (the Burst `SfxStackJob`). **Project toggle:** a `SpriteFxSettings` `ScriptableObject` (Assets ▸ Create ▸ Laubrary ▸ SpriteFx ▸ Settings) with a `useBurstJobs` bool; the package ships ZERO assets, so the static `SpriteFxSettings.Instance` loads an OPTIONAL `Resources/SpriteFxSettings` from the host project and falls back to a hidden in-memory default. **Default = inline** (`useBurstJobs = false`): pixel-art sprites are small (a 64×64 frame is 4096 px) so job-scheduling + Burst warm-up typically cost more than the inline loop; Burst is an explicit opt-in for large/many buffers. Each `SpriteFxFilter` also carries a per-component `SfxDispatch` (UseProjectSetting / ForceInline / ForceBurst) that overrides the project toggle for profiling/verification. **R/W requirement:** the source texture must be Read/Write enabled (the filter reads its pixels on the CPU each frame); a non-readable texture logs a one-time warning and the filter is a graceful no-op (a GPU-readback/`RenderTexture`+`ReadPixels` fallback for locked textures is noted but intentionally not built — it carries an orientation/platform-flip risk this thin slice avoids; pixel-art sprites are cheap to mark R/W). **Hurt-flash demo:** `SpriteFxHurtFlash` (`[RequireComponent(typeof(SpriteFxFilter))]`) exposes a public argument-less `Flash()` that builds a brightness pulse (`amount` curve 1→peak→1 over life) plus an optional held tint and triggers the filter. It is DELIBERATELY not auto-wired to any damage event — the SpriteFx module sits BELOW Zoetrope/combat in the dependency graph and must not reference them, so `Flash()` is left public to drop into a UnityEvent or call from glue (`health.Damaged += (_, __) => hurtFlash.Flash();` against Zoetrope's `Health.Damaged`/`ReactionFxPlayer.OnHit`), keeping the dependency pointing combat → helper. **Verified (edit-mode, the filter mechanism is testable without play mode):** compiles clean; the shared bake path is untouched so the canonical PyrePlus Gem still hashes `0xB113DC81E4DE442B` (all-16-frame default spec, standard FNV-1a-64); calling `SpriteFxFilter.Apply` on a blue-blob buffer with the hurt-flash pulse gives **life 0 == source byte-for-byte** (maxByteDelta 0) and **life 0.5 (pulse peak) brightens all 448/448 opaque pixels** (the sampled centre blue (80,110,210)→(255,255,255) at peak ×4); **inline == Burst** byte-identical here (maxByteDelta 0 — Brightness×4 clamps cleanly, well within #46's documented ≤1/255 codegen tolerance); the project toggle routes correctly (project-setting off→inline, on→Burst; ForceInline→inline, ForceBurst→Burst, checked via the resolved dispatch); and a before/after PNG (dim shaded blue blob → visibly brighter blob at mid-flash) confirms the flash reads. No demo scene added (the deliverable is the verified runtime component); inspector authoring of an arbitrary `[SerializeReference]` modifier stack would want a small custom editor (future — the hurt-flash helper and code drive it today).
- **Laubrary.SpriteFx — the pure per-pixel colour/mask modifiers are now Burst-shaped: one kernel, run inline OR as a job (slice #46).** Building on the module extraction (#45), the runtime colour-filter/mask workhorses — **Tint, Contrast, Brightness, Saturation, Posterize, OrderedDither, LayerDissolve, AlphaMask** (8 of the family: every `PixelModifier` whose `ApplyPixel` reads only position/life/hash and gathers NO buffer neighbours) — each had its per-pixel math extracted ONCE into a `[BurstCompile]`-legal static kernel (`SfxKernels.K*`) taking a blittable param struct (`TintP`/`ScalarP`/`PosterizeP`/`DitherP`/`DissolveP`/`MaskP`, resolved once per frame from the modifier's already-`Prepare`d floats) + the pixel's `(x,y,wx,wy,crossFrac,life,hash,rgba)`. The change is a pure refactor: **each modifier's existing managed `ApplyPixel` now calls that same kernel**, so the baker/preview stay byte-identical (verified — see below). The runtime path is a new `SfxStackJob` — an `IJobParallelFor` over `NativeArray<Color32>` that selects the kernel per op via an **enum tag** (`SfxOp.kind`/`SfxKernel`, no managed virtual dispatch) and runs a small resolved stack per pixel through the SAME `SfxKernels.RunPixel` the managed path uses (so the two never drift). The static entry `SpriteFxStack` exposes both code paths over one resolved op stack: `Resolve(mods, eval, alloc, …)` prepares + packs the shaped modifiers into a `NativeArray<SfxOp>` (skipping non-shaped/disabled ones for the managed path), `RunInline(Color32[], …)` runs it managed, `Schedule(NativeArray<Color32>, …)` runs it as the Burst job. The design's second conversion — per-pixel `Gradient.Evaluate` → a main-thread `NativeArray<Color32>` 256-entry **LUT** (`SpriteFxLut.Bake`, indexed in the job) — is wired for the one shaped modifier that needs it (Tint's `crossFrac` cross-gradient); the managed bake keeps the live `Gradient.Evaluate` (that's what preserves bake byte-identity), the LUT is purely the runtime path. Packaging: `com.unity.burst`/`com.unity.collections`/`com.unity.mathematics` promoted from transitive to direct `manifest.json` dependencies AND added to the SpriteFx asmdef's references. The job is `[BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]` — Strict (no FMA/reassociation) + synchronous compile keep the native codegen bit-consistent across runs so a byte-identity-critical filter never renders one frame on the Mono fallback and the next on native. **Verified rigorously:** (1) BAKE byte-identity is PERFECT — all render+FNV-hash baselines are unchanged before/after the `ApplyPixel` refactor across all 16 frames (Disc+swarm `0x707DD2F3936D025B`, Gem `0xB113DC81E4DE442B`, Streak+swarm `0xDCDC93EA4F1186AB`, and specs exercising each shaped modifier: Disc+Brightness `0xA5A7C40190A4F66D`, Disc+Tint `0x5A05023FA0B9ACED`, Disc+Contrast+Saturation+Posterize `0x1D9EB9F7FBDCF5FD`, Disc+OrderedDither+LayerDissolve+AlphaMask `0x2D4B5BC34DDE8BB6`, Disc+VoronoiCrack `0xE6C50973F9A2EECF`); (2) the BURST job path is fully self-deterministic (`Schedule().Complete()` == a re-`Schedule` == main-thread `.Run()`, byte-identical for every config); (3) managed-INLINE == Burst-JOB is byte-identical for the great majority of configs, with a small fraction of pixels (0–3%) differing by exactly 1/255 on the arithmetic-heaviest kernels — the documented Burst-vs-Mono float-codegen limit at the ×255 requantise boundary (e.g. `255/255f` = 1.0 in Mono but 0.99999997 in Burst), sub-perceptual and confirmed to be codegen-only (max delta 1, and the Burst path itself is exactly reproducible). A PNG bake of a Tint cross-gradient disc and a Brightness+Posterize+OrderedDither swarm confirms no visual regression. **Deferred (documented, not built):** `VoronoiCrack` — it qualifies as gather-free but its crack-tint gradient is indexed in Fill mode by a value COMPUTED inside the kernel (the Voronoi field), not a pre-sampleable input field like Tint's `crossFrac`, so its gradient step can't be shared between the live-`Evaluate` bake path and the LUT job path without either breaking bake byte-identity or forking the math; all **GeometryModifiers** (their warp is interwoven with rasterisation, not a Color32-buffer pass); all **PostModifiers** (Bloom/Outline/Dissolve/ChromaticAberration/… read neighbour pixels — a gather); and the two **stateful sims** (they retain frame-to-frame state). The project-level inline-vs-job TOGGLE is a separate follow-up (#47) — this slice only exposes both code paths + the static entry.
- **Laubrary.SpriteFx — the 39 stateless Pyre modifiers elevated into a new neutral module (so Reels/Chunks can reuse them without depending on Pyre).** The whole stateless modifier family moved out of the `com.Lautaro-Arino.Laubrary.Pyre` assembly into a new `com.Lautaro-Arino.Laubrary.SpriteFx` runtime asmdef (`Runtime/SpriteFx/`, rootNamespace `Laubrary.SpriteFx`, referencing only ZuiRuntime): the base `PyreModifier`, the four family bases (`GeometryModifier`/`PixelModifier`/`PostModifier`/`EdgeModifier`), all **39** concrete stateless modifiers (Skew/Scale/Rotate/Wobble/SunburstWobble/RingWave/PointBlast/Profile/Ground/Tint/Contrast/Brightness/Saturation/Sunburst/PulseRings/Posterize/OrderedDither/VoronoiCrack/Dissolve/LayerDissolve/AlphaMask/Bloom/Outline/ChromaticAberration/CloudProjectile/BallisticShockwave/Fuse/Jagg/EdgeWarp/Turbulence/PerlinTurbulence/EdgeSmooth/Curl/CurlProgress/Sphere/Smudge/PinWarp/DropShadow/Kaleidoscope), their support types (`GeoCtx`, `PixelInfo`, the now-`public` `PyreNoise`, `VortexPoint`/`IVortexHost`/`SmudgeStroke`/`PinKeyframe`/`PinDot`, and the `MaskShape`/`DissolveMode`/`ScaleAxis`/`CrackSpreadMode`/`KaleidoMode` enums), plus the `ColorMode` enum and a new `Sfx` helper class holding the canonical `Hash01` + the `CurveVal`/`WhiteGradient`/`CloneVal`/`CloneGradient` factories. Only the two STATEFUL modifiers — `SimulationModifier` + `PixelFluidModifier`, which retain frame-to-frame state — stay in Pyre (split into a new `Runtime/Pyre/PyreSimulationModifiers.cs`, referencing `PyreModifier` across the now-legal Pyre→SpriteFx boundary). Every moved class carries `[UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.Pyre", "com.Lautaro-Arino.Laubrary.Pyre", null)]`, which remaps the `[SerializeReference]` type headers in the 24 committed modifier-bearing assets on load. The one cross-assembly cycle (`PyreModifier.Clone`/`PyreNoise` reached back into `Layer`/`BlastRenderer`) was broken by relocating the pure helpers into `Sfx`: `BlastRenderer.Hash01` is now a one-line forwarder to `Sfx.Hash01` (single canonical impl, byte-frozen), and BlastRenderer drives the moved modifiers' `internal` per-frame hooks (SetLife/SetSeed/SetFrameIndex/SetOrigin) via a targeted `[InternalsVisibleTo("…Pyre")]` grant rather than widening the public API (PyrePlus already drives them by reflection). **Strictly a relocation — zero behaviour change, verified rigorously:** a clean compile across all assemblies; a read-only before/after managed-reference census of all 30 assets shows every one of the 73 resolved `[SerializeReference]` modifier instances still resolves — 72 flipped `Laubrary.Pyre.X`→`Laubrary.SpriteFx.X`, the 1 `PixelFluidModifier` correctly stayed Pyre — with the resolved-count conserved per asset and the 6 pre-existing (unrelated) `SquashModifier` missing-refs unchanged (no NEW losses); nothing was saved (`git status` shows no modifier-bearing `.asset` modified); and the PyrePlus renderer's canonical Gem hash still reproduces `0xB113DC81E4DE442B` exactly (the moved modifier types still render byte-identically), with three real Pyre demos using moved geometry/pixel/post modifiers (and the staying `PixelFluidModifier`) rendering unchanged, plausible explosions.
- **PyrePlus — Pyre1 → Pyre Plus converter (slice 9, the capstone): import an existing vanilla Pyre asset into a new Pyre Plus asset.** The window's dial pane gains an **"Import from Pyre…"** section (NOT a menu item, so the Laubrary menu stays lean): a Pyre object-field + a "Convert to Pyre Plus" button that runs a pure, additive `PyreToPlusConverter.ConvertFromPyre(Pyre src, out List<string> warnings)` (in `Editor/PyrePlus/PyreToPlusConverter.cs`), `AssetDatabase.CreateAsset`s a NEW `.asset` beside the source (a `GenerateUniqueAssetPath` name — it NEVER overwrites the source), registers it for Undo (`Undo.RegisterCreatedObjectUndo`), pings/selects it, and surfaces every dropped or approximated field (a `Debug.Log` always, plus a dialog when there are notes). Because slices 0–8 brought every Pyre1 form + the full six-channel luma matte + the per-layer simulation slot into PyrePlus, the design's per-form field maps ARE the converter's dispatch table — it does NOT port Pyre1 `LayerShape`s as monolithic shapes but DECOMPOSES each into PyrePlus's shape + swarm + shared-capability primitives: **Disc/Crescent/Sparkle/Sprite** → the same form + a scatter Swarm (Pyre `count`→`swarmCount`, `spawnRadius`(0..1)→`shapeScale` px, `positionX/Y`→`shapeOffsetX/Y`); **Area** scatter → Circle/Area swarm, **Ring** → Circle/Path swarm (`ringOrder`→even-path, `ringArcDegrees`→path-spread, `ringStartAngle`→rotation, `ringExpand`→live `swarmScale`, `ringAlignRotation`→Outward orient), **Rosing** → ONE PyrePlus layer per `RoseRing` (bloom preserved — each ring's count/radius/size-scale/birth/life drives its own Path swarm); **Bars** → the Streak form on a **Line** swarm (`barWidth`→streak Width, `barForward`→streak Length, `barBackwardFrac`→streak Anchor, `barSoftness`→Edge/Tip softness, `barCount/barSpacing`→the Line count/scale, `barTaper`→a centre-peaked `swarmScaleByIndex` with `streakScaleLengthOnly` = Pyre's equal-width graduated-length flame), a **star** Bars → a Circle/Path/Outward ring of streaks, `barMirror` → a second mirrored layer; **MetaBlob** → a Disc swarm + `coalesce = Fuse` (`metaThreshold/ShadeRange/Softness`→the fuse dials, `metaExpand`→live `swarmScale`); **HeightBalls** → a Disc swarm + `coalesce = Ramp` (a group's `mass`→the Density env, `height`→the Heat env, `cloudSize`→`shapeScale`, `ballSize`→`size`, `hbFusion/hbCoverage/hbLighting/hbRelief/hbLightAngle`→the ramp knobs); **Fire**/**Fireball** → the matching sim form with every rate envelope mapped 1:1 (identical field names). **Colour**: `ColorMode + colorOverLife` → a `ZuiFill` (OverLife→OverLife-gradient; Fill/FlowingFill→a Radial spatial fill, noting the flow-scroll drop; NoiseFill→a Noise-texture fill, noting warp/rotation/drift drop). **Matte** (now a DIRECT map — slice 4a ported Pyre1's exact model): `LayerRole.Matte`→`LumaMatte`, `MatteChannel` flags→`matteFlags` (same `[Flags]` bit values), `MatteScope`→`matteScope`, plus invert/strength/blur(`matteAmount`)/displace/hue. **Modifiers**: each `.Clone()`d into the layer's stack; a `SimulationModifier`/`PixelFluidModifier` → the per-layer `simulationModifier` slot; an `EdgeWarpModifier` → stripped + warned (no edge stage in PyrePlus's disc raster); `globalModifiers` → duplicated into every layer's stack; a blast-wide `simulationModifier` → warned/dropped (PyrePlus has only a per-layer slot). Structural mismatches are surfaced as per-layer warnings rather than silently guessed: non-square `canvasHeight` and a non-centre `origin` (PyrePlus is square + centre-pivoted); a single (count 1) shape's `[start,end]` frame window (PyrePlus has no per-layer window — a scatter layer reproduces it through swarm spawn-timing + particle-life, a lone shape can't); MetaBlob's hand-placed orb positions / HeightBalls' authored ball groups (no procedural-swarm equal — the LOOK converts, not the exact placements). The converter is PURE: it never mutates or saves the source (the one accessor that would upgrade a source layer in place — HeightBalls groups — is read on a `Clone()`), never forks the runtime renderer, and returns a fresh in-memory spec the caller owns. Verified: five varied committed demos converted in-memory + rendered to PNG all read as plausible explosions matching their intent — *New Pyre* (5 layers: smoke + fire core + sparks), *SparkleBurst* (the Bars→Streak Line row with the centre-longest flame taper, plus Crescent + Sparkle), *Rose Blast* (2 Rosing layers → 6 blooming ring layers), *NoiseField Ball* (HeightBalls → a relief-lit Ramp cloud), *Metafield Pyre* (MetaBlob → a fused-metaball blob) — the matte map exercised 1:1 via a synthetic matte Pyre (no committed demo has a matte). Additive editor-only (the runtime renderer is untouched), so the byte-identity baselines are unchanged (Gem reproduces `0xB113DC81E4DE442B` exactly).
- **PyrePlus — swarm-driven Fire emitters (slice 8): a Fire layer can source its emitters from the SWARM instead of the built-in arms.** A new opt-in, default-OFF **"Swarm emitters"** toggle on the Fire form. When on AND the layer's Swarm is enabled, the flame stops using its N built-in arms around the centre and instead sources ONE heat/fuel injection per ALIVE swarm particle: each emitter sits at the particle's own canvas position, its radius/heat/fuel are the Fire box's Emitter-width / Heat / Fuel envelopes evaluated at that particle's OWN life, and its pulse breathes on its own phase (the particle index folded with the sim seed). All those injections feed ONE shared fire grid whose centre-based fluid physics (buoyancy / curl / confinement, driven by the same Arms / Direction dials) advects and merges them into one connected, live flame — so a row or ring of particles becomes a row/ring of flames that fuse at the base rather than N frozen sprites. Because Pyre's `FireSim` bakes its emitter geometry into `Step`'s arm loop and keeps its heat/fuel grids private (and PyrePlus must not modify Pyre), this is delivered as a PyrePlus-LOCAL **`PlusFireSim`** — a faithful re-port of `FireSim` whose state (heat/fuel/heatB/fuelB grids + LastFrame + active-box AABB), Allocate/Reset, clamped bilinear Sample, the whole advect+cool+confine+swap step, and Render are copied byte-faithful, but whose inject loop is generalized to a caller-supplied `IReadOnlyList<HeatEmitter{x,y,radius,heat,fuel,phaseSeed}>` passed to `Step(in FireParams p, emitters, seed, t, dt)` — so a single centred emitter reads ≈ Pyre's Fire look. Determinism is preserved by its OWN replay harness mirroring the slice-6a Fire path exactly — a `ConditionalWeakTable<PyrePlusLayer, PlusFireSimEntry>` with a content hash that folds every Fire input PLUS the swarm placement config (count/shape/timing/particle-life/transform envelopes/live turn-tilt-roll-scale), so any authoring edit forces a cold replay from frame 0; normal playback warm-steps one frame, a scrub cold-replays 0→f rebuilding the identical emitter list from `ComputeSpawns` at each step (public + seed-keyed ⇒ exact), and a same-frame re-request just re-renders. The one deliberate substitution, documented in the file: `FireSim`'s two `PyreNoise.Sample(…, warp=0)` calls (curl swirl + breakup) become `PyrePlusField.Noise01` — `PyreNoise` is `internal` to Pyre and unreachable, and its warp-0 reduction is exactly `Noise01`'s two-octave 0.65/0.35 value-noise shape, differing only in the underlying integer hash, so the turbulence has the same coherent character but is NOT bit-identical to Pyre (hence "≈ Pyre's look", never a byte match to Pyre). The editor un-gates the Swarm section for a Fire layer when the toggle is on (its normal placement controls now drive the emitters, with a note), and shows the built-in-arms note otherwise. **Strictly additive and default-false:** with the toggle off the slice-6a fixed-emitter `FireSim` path runs UNCHANGED and every other path is untouched — verified byte-identical over all 16 frames (Disc `0x707DD2F3936D025B`, Gem `0xB113DC81E4DE442B`, Streak `0xDCDC93EA4F1186AB`, and a plain fixed-emitter Fire spec `0xB4486B28FB5B54E2` all identical before/after). Swarm-fire verified live: a Line of 6 particles ignites 6 distinct sources at exactly their authored positions that then rise and merge into one connected 71px-wide field (rendered to a PNG), and replay is exact — a cold scrub to frame N is byte-identical to arriving by sequential forward-step (frames 4/7/11/14). Fireball stays single-source by design (not extended).
- **PyrePlus — per-layer SimulationModifier slot: a reflection-driven `PixelFluidModifier` (slice 7).** Each PyrePlus layer gains a single, opt-in STATEFUL modifier slot (`PyrePlusLayer.simulationModifier`, a `[SerializeReference] SimulationModifier`) that is separate from the layer's stateless modifier list because it retains frame-to-frame state and replays internally on a scrub — so, like vanilla Pyre's own separate `Layer.simulationModifier`, it is its OWN slot and always runs LAST within the layer: the **sim slot**, after the layer's stateless post modifiers and before the matte apply, on the layer's isolated per-layer scratch (mirroring `BlastRenderer`'s per-layer order). It is driven by REFLECTION — Pyre's `SimulationModifier.SetSeed` and `EnsureFrame(frameIndex, seedBuf, W, H, paramsForFrame)` are `internal` and PyrePlus is a separate assembly with no `InternalsVisibleTo` (and may not modify Pyre), so both `MethodInfo`s are resolved once and NULL-GUARDED (the exact idiom as the existing `SetPostContext` post-modifier reflection); if either can't be found the layer degrades to "sim not driven" (Render on a never-stepped instance is an exact no-op). PyrePlus provides the `paramsForFrame` closure (`Func<int, Func<ZUIValue, int, float>>`) that re-resolves the modifier's animatable params as each frame `f` itself saw them — so a cold replay-from-0 uses each historical frame's OWN param values, not the target frame's — keyed through a dedicated field-id block (`FldSimModifier = 16 + 800*8`, mirroring `BlastRenderer`'s 800*8 sim offset) and decorrelated per layer by the existing `_layerSalt`. `EnsureFrame` is called EVERY frame (it decides forward-step vs replay itself); then the public `Render(scratch, W, H)` advects/erodes the finished layer pixels via the persisted velocity field. A sim-modifier layer is forced into isolated scratch (`hasLayerSim ⇒ needScratch`, mirroring Fire/Fireball) because the sim's Render OVERWRITES/advects rather than clears. The layer's `Clone()` deep-copies the slot via the modifier's own `Clone()` (which resets the copy's live sim grids/PRNG) so a duplicated layer owns its own instance. The editor adds a single-slot polymorphic picker in the Modifiers section ("Simulation (always last)"): an "+ Add simulation" affordance (a reflection-discovered menu of concrete `SimulationModifier` subclasses — today only Pixel fluid) when empty, and otherwise an enable/clear header plus the modifier's own reflected fields — reusing the SAME `ZuiReflect` drawer + Undo/dirty wiring as a modifier block, re-pointing on layer selection with the rest of the section. **Strictly additive and default-null:** with no sim modifier the slot is inert and every path is untouched — verified byte-identical (the Disc/Gem/Streak canonical hashes are unchanged before/after: Disc `0x707DD2F3936D025B`, Gem `0xB113DC81E4DE442B`, Streak `0xDCDC93EA4F1186AB`). With a `PixelFluidModifier` on a static Noise-textured disc, the layer's pixels visibly evolve over the frames (the projectile carves an eroding tunnel and vortices swirl the cloud into advected filaments — while the raw disc with the sim OFF is byte-identical every frame, so all motion is the sim), and replay determinism holds: a cold scrub straight to frame N is byte-identical to arriving there by sequential forward-step (checked at frames 8 and 15).
- **PyrePlus — Fireball form (slice 6b): the second stateful sim, reusing the Fire replay harness.** Fireball is PyrePlus's second simulation-backed `ShapeForm` (after Fire) — Pyre's cheap "doom-fire" cellular flame: heat blooms OUTWARD from one central point and is folded into `fireballArms` kaleidoscope wedges (Mirror = alternate wedges reflected at a seam, or Repeat = rotated copies), so it reads as a radial / star explosion cooling at the rim. Like Fire it retains a frame-to-frame heat grid, so it is reached by REPLAYING the sim from frame 0 rather than by a per-frame closed form, and it REUSES Pyre's own public `FireballSim`/`FireballParams` directly (zero re-port of the cellular physics — PyrePlus writes only the thin harness around them). Its param block mirrors Pyre's `StepFireball` field-for-field — Source (the burn's progress envelope over life), Core radius, Cooling (arm LENGTH), Sharpness (arm THINNESS, set independently of length), Spread (sideways waver), Reach (confinement), plus Arms, Mirror, and the output Threshold / Contrast — with Pyre's defaults, the rate dials each an envelope over the layer's life. The replay harness is DUPLICATED from Fire rather than generalised, because `FireSim` and `FireballSim` are unrelated types with different `Step` signatures (FireballSim.Step takes the INTEGER frame index as its random key with no substeps, so a cold scrub to a frame reproduces that frame's exact randomness) — so the proven Fire path stays byte-untouched: a `ConditionalWeakTable<PyrePlusLayer, FireballSimEntry>` keyed by layer identity, with a **content hash** of every fireball input (the six rate envelopes + arms/mirror + threshold/contrast + the ramp gradient + the alpha envelope + seed + frameCount + W/H) as the invalidation key — any authoring edit flips it and forces a **cold replay from frame 0**, normal playback **warm-steps one frame** (O(1)), a scrub cold-replays 0→f, and a same-frame re-request with an unchanged hash just re-renders. `FireballSim.Render` overwrites (never clears) so a Fireball layer is forced to isolate into its own scratch (mirroring BlastRenderer's Fire/Fireball isolation) and Over-composites; its colour ramp is the layer's Shape **Fill** gradient (smoke→fire) and its opacity the Shape **Alpha** — `size` and the Swarm don't apply (Fireball is SINGLE-SOURCE by design; both hidden, with a note). Fireball's own field ids REUSE Pyre's `F_Fireball*` block (120–125) verbatim. The replay-determinism gate passes on all four axes: **cold == warm** (sequential forward-step reproduces the cold-replay reference per frame), **scrub == replay** (a forward-skip, a backward step, and a forward-again all equal the cold reference frame), and **invalidation** (perturbing Cooling at an active mid-life frame changes the content hash, drops the cache, and re-renders equal to a fresh cold replay under the new params — no stale forward-step); and the three non-Fireball canonical byte-identity hashes (Disc/Gem/Streak) are unchanged (verified against the committed baseline: Fireball is a new appended enum value no existing spec can hold, and the default/non-sim render paths are untouched). Swarm-driven emitters remain out of scope (Fireball stays single-source; swarm-driven Fire is slice 8).
- **PyrePlus — Fire form + the stateful sim / replay harness (slice 6a).** PyrePlus's renderer was pure-per-frame — every frame rendered standalone. Fire is the first form that breaks that: heat is carried by a velocity field, so frame N depends on frame N-1. It stays fully deterministic by being reached through a REPLAY harness rather than a closed form — the same discipline Pyre1's `RenderFire`/`SimulationModifier` use. Fire is a new appended `ShapeForm` and REUSES Pyre's own public `FireSim`/`FireParams` directly (zero re-port of the grid physics — PyrePlus writes only the thin harness around them); its param block mirrors Pyre's `FireParamsAt` field-for-field (direction / emitter width & inset / heat / fuel / pulse / flow / buoyancy / curl & curl-scale / flicker / dissipation / burn / reach / edge-cooling / stretch / pinch / breakup / intensity, plus arms / arm-mode / steps / threshold / contrast), with Pyre's defaults, each rate an envelope over the layer's life. The harness is a `ConditionalWeakTable<PyrePlusLayer, FireSimEntry>` keyed by layer identity: a **content hash** of every sim input (all fire params + arms/steps/mode + threshold/contrast + the ramp gradient + the alpha envelope + seed + frameCount + W/H) is the invalidation key — any authoring edit flips it and forces a **cold replay from frame 0**, so a forward-step can never carry stale dial values into a later frame; normal playback **warm-steps one frame** (O(1)), a scrub cold-replays 0→f, and a same-frame re-request with an unchanged hash just re-renders. `FireSim.Render` overwrites (never clears) so a Fire layer is forced to isolate into its own scratch (mirroring BlastRenderer's Fire/Fireball isolation) and Over-composites; its colour ramp is the layer's Shape **Fill** gradient (smoke→fire) and its opacity the Shape **Alpha** — `size` and the Swarm don't apply (both hidden, with a note that swarm-driven fire emitters are a later slice). Fire's own field ids REUSE Pyre's `F_Fire*` block (100–118) verbatim. The determinism gate that replaces per-frame-standalone for sim layers passes on all four axes: **cold == warm** (sequential forward-step reproduces the cold-replay reference per frame), **scrub == replay** (a fresh cold jump to frame N equals reference N), **invalidation** (perturbing one fire param changes the content hash and drops the cache — no stale forward-step; the re-render equals a fresh cold replay under the new params), and the three non-Fire canonical byte-identity hashes (Disc/Gem/Streak) are unchanged (Fire is a new enum value no existing spec can hold; the default path is untouched). Checkpoints (replay only checkpoint→f) are a valid further win the content-hash gate makes safe but are deferred (TODO) — correctness first. Fireball (slice 6b) and swarm-driven Fire emitters (slice 8) are NOT built here.
- **PyrePlus — Luma matte α-strength source: drive the effect strength off the covered layer's own edge/coverage (slice 5).** A small, opt-in addition on the Luma-matte layer: a **"Strength from edge (1−α)"** toggle. When on, the matte's mask is modulated per COVERED pixel by that pixel's own alpha as `(1 − α)`, so a fully OPAQUE interior receives NO effect and only the soft / thin / anti-aliased EDGE pixels take the full effect — an edge/soft-region mask derived from coverage (fresnel-like, but from alpha). It does NOT change how the mask is built (still the matte layer's Rec.601 luminance × its own alpha); it only reshapes how `ApplyMatte` applies it: a snapshot of each covered layer's alpha is taken ONCE at ApplyMatte entry — before any channel runs, because Blur/Displace rewrite alpha mid-pipeline and all channels must read the same finished-coverage reference — then the mask is multiplied by `(1 − α)` into an `effMask` fed to the five non-Alpha channels. The **Alpha channel is deliberately excluded** from the modulation (clipping opacity hardest where opacity is already lowest is near-degenerate); it always reads the un-modulated mask, so pairing α-source with Alpha-only is a documented no-op. Fully deterministic (a pure function of the layer's own pixels, no RNG). **Strictly additive and default-off:** `matteAlphaSource` defaults false and when off `effMask` IS `mask` (same reference, no multiply), so the OFF path is byte-identical to the plain luma matte — verified: the Disc/Gem/Streak canonical hashes are unchanged and the slice-4a Brightness|Alpha and Hue LumaMatte renders reproduce their pre-slice-5 PNGs byte-for-byte. With it ON over a soft-rimmed disc, the opaque core stays untouched while the feathered rim progressively hue-shifts/tints across the partial-α band (the visible effect lives entirely in `0 < α < 1`, exactly the anti-aliased rims / soft wisps / dissolve fronts intended).
- **PyrePlus — matte heightmap: several matte layers fuse LUMINANCE into one field a target layer renders as a relief-lit surface (slice 4b).** The user's unification idea, built on the EXISTING numbered-channel matte plumbing plus slice 2's `PyrePlusField` — two opt-in, default-off additions, so a spec that uses neither renders byte-identical. **(1) A Write-matte layer gains a "Write luminance (heightmap)" toggle:** instead of depositing its flat coverage-alpha into its channel, it deposits its Rec.601 LUMINANCE × alpha, combined by the same Max/Add/Subtract — so several such layers on one channel (Combine = Max, "wherever they intersect, fused") fuse into ONE scalar heightmap. **(2) A Draw layer gains a "Height from" channel selector (None / 0–3):** when set, the layer does NOT draw its shape — it renders that fused channel as a heightmap, shading the scalar through its own Fill gradient and relief-lighting it from the field's local slope via the shared `PyrePlusField.ReliefLight` (the same helper the Height-balls Ramp pass uses), then Over-compositing ONE fused surface (bright/lit where the source luminance piles up, shaded by an authorable Relief strength + Light angle). So authored matte layers become a single lit heightmap — the HeightBalls look, but fed by mattes instead of a swarm. Purely additive: `matteWriteLuma` defaults false (coverage path unchanged) and `heightFromChannel` defaults -1 (shape drawn normally); the consumer reads only the channel + two plain-float knobs (no Eval, no per-particle state) so it bakes/scrubs identically. Verified byte-identical: the Disc/Gem/Streak canonical hashes and an existing Write-matte coverage-clip spec are all unchanged; a fused 3-layer luminance heightmap renders as one relief-lit surface. Closes the "matte heightmap consumer" the luma-matte slice deferred.
- **PyrePlus — Luma matte: Pyre1's six-channel luminance matte, added as a NEW parallel layer role.** A layer's Matte box gains a third role beside Draw and Write matte: **Luma matte**. Instead of writing a numbered coverage channel, a luma-matte layer is invisible and turns its finished pixels into a MASK — its Rec.601 LUMINANCE × its own alpha (optionally inverted, scaled by an animatable Strength over the matte layer's own life) — and imposes that mask on the layers ABOVE it (a PUSH matte: the authoritative matte layer drives the passive layers on top). Six effect channels, any combination, applied in a fixed deterministic order — **Displace → Blur → Saturation → Hue → Brightness → Alpha**: Alpha (classic opacity clip), Brightness (darken toward black — a shadow/light pass), Saturation (drain to grey), Hue (rotate by up to Hue° where the mask is bright), Blur (per-pixel variable-radius box, sharp where the mask is low), Displace (push pixels along the mask's own slope — refraction/heat-haze). Scope is **Next layer** (a clipping mask over the one layer above) or **All above** (every layer above until another luma matte replaces it). The six channel functions + mask build are ported VERBATIM from `BlastRenderer`, carried through the layer loop as a by-ref `MatteState`; an affected Draw layer is forced to isolate into its own scratch before the matte + composite. **Strictly additive:** the existing Write-matte numbered-channel coverage-clip path (Role/Channel/Combine/Clip-by/Invert) is untouched — no field renamed — so every existing spec renders byte-identical (verified: the Disc/Gem/Streak canonical hashes and a 2-layer Write-matte coverage-clip spec are all unchanged before/after; LumaMatte is a brand-new appended enum value no old spec can hold, and the matte-active path is skipped whenever no luma-matte layer is present). New negative field ids -19..-22 (Strength/Blur/Displace/Hue). The α-strength source and the matte heightmap consumer are separate later slices — not built here.
- **PyrePlus — Bars decomposed as a Streak form under a Swarm (two additive changes, no new geometry).** Pyre1's Bars layer is now an authoring RECIPE rather than a monolithic shape: a Streak-form particle IS a bar (barWidth→streak Width, barForward→streak Length, barSoftness→Edge/Tip), and the two Bars arrangements come from the swarm. **(1) A new `Line` swarm shape** places the particles evenly along a straight horizontal segment through the shape centre (endpoints at ±radius), independent of Area/Path — a 1-D row; the shared shape transform then rotates the whole row, so shape Rotation IS the row angle. A row of Streaks on a Line = Pyre's **row of bars** (barCount/barSpacing → the Line's count/scale). The **star of bars** needs no new kind — it is a Circle + Path + Outward swarm with even spacing (spreadCount/spreadDegrees → count/path-spread). **(2) A new opt-in `Taper length only` toggle on the Streak form**: when on, the swarm's per-index Scale drives the streak's LENGTH only, leaving width uniform — equal-width bars of graduated length, Pyre's **barTaper** flame/asterisk silhouette (barTaper → Scale-by-index + this flag). Both changes are strictly additive and default-off, so every existing PyrePlus spec renders byte-identical (verified: the Disc/Gem/Streak canonical hashes are unchanged; `Line` is a brand-new enum value no old spec can hold, and the length-only path is skipped unless both the flag is set and the form is Streak). **Deliberately deferred** (documented, not built): `BarDecay.Dissolve` — a per-index alpha front advancing across the row over the layer's life — needs a future `swarmAlphaByIndex`-over-life ("dissolve front"); a particle's own-life alpha cannot express cross-index coordination.
- **PyrePlus — Coalesce "Ramp" mode: HeightBalls decomposed as a swarm + a density-relief field-pass.** A swarm layer's Coalesce selector gains a third mode beside Off and Fuse: **Ramp** stops Over-compositing each particle and instead reads the WHOLE placed cloud as three fused scalar fields — the port of Pyre1's Height balls, but built from PyrePlus primitives rather than a monolithic shape. Each particle contributes a soft DOME melted into the others by a smooth-max (so neighbours fuse into one lumpy mass, not a pile of discs), carrying two NEW per-particle envelopes over its own life — **Density** (mass/body) and **Heat** (height/energy, how far up the smoke→fire ramp it sits and how tall it stands) — beside the existing size/alpha. The fused height field's local slope then relief-lights the cloud (carved highlights and shadow from an authorable light angle), a shared surface-noise **rim** deforms every dome together so the mass reads as one boiling body, and the pixel's combined density+heat picks a position on the Shape's Fill gradient (a smoke→fire ramp) with opacity from whichever field is stronger times the per-pixel blended particle alpha. Knobs: Fusion (melt knee), Coverage (opacity gain), Relief lighting (+ Relief strength / Light angle) and Rim boil. Fully deterministic (reads only the seeded spawn placements — scrubs/bakes/plays identically) and byte-identical when Off. The shared field math (a SmoothMax dome accumulate into a `float[]` and slope-relief lighting over a height field) lands in the reusable `PyrePlusField` substrate that the coming luma-matte heightmap will reuse. Deliberately out of scope for this slice (documented, not forced): per-ball squash ellipses, idle-boil churn, the fold-under confinement, and single-pass cross-group fusion.
- **Zui — ZuiColumnFlow: responsive multi-column panels.** A vertical control stack that splits into up to four contiguous, height-balanced columns as it widens past whole multiples of its preferred column width — the bottom of the stack flows into the next column. With `Z.HGroup` (small controls packed as one atomic unit that travels between columns whole) and a new `ZuiSection` header checkbox (bind any bool to the section title, no redundant title-plus-toggle). PyrePlus adopts all three: its dial pane flows to two columns on a wide window, small rows are grouped, and Swarm's enable is now the section-header checkbox.
- **PyrePlus — spawn-window folded into spawn timing.** The Spawn window slider is gone; the spawn-timing envelope is the whole particle-number→timeline mapping now (end it low to finish spawning early). Enum pickers became wrapped ZUI radio rows instead of dropdowns (Form, Shape kind, Matte clip-by, Text sweep), and every pixel-ranged control (Size, Scale, Streak length, offsets, snap) now scales its maximum with the canvas size.
- **Zui — ZuiBox gear-toggle standard + shared view presets.** Any ZuiBox can now opt individual controls (and whole groups) into a ⚙ gear accordion: off = hidden from the body, group toggles show the built-in dash when partly on, nothing renders unless something was opted in, and state survives rebuilds under consumer-supplied stable keys. On top of it, `ZuiViewStore` (a committed ScriptableObject of named presets — view state ONLY, never authored values, all edits Undo-recorded) and the reusable `ZuiViewBar` (Views dropdown / Update / Delete / Save-as; per-user last-view pointer in EditorPrefs so switching views never dirties the shared asset). First adoption: PyrePlus — its Solid box's Light/Lines/Glow rows are gear-toggleable and the window carries a views bar storing to `Assets/PyrePlus/PyrePlusViews.asset`. Ported from the PreviewLab columns/gear/views PoC.
- **Zui — every bare numeric field is drag-adjustable (`ZuiScrub`).** Two drag zones per field — the row's LABEL (Unity's native idiom) and a slim grip on the field's left edge — capture immediately and slide the value (int 1 per 3px; float steps adapt to magnitude; Shift ×10, Alt ×0.1); the field body stays click-to-edit. (The first in-field press-drag design silently never engaged: the field's inner text input takes pointer capture on press and capture redirects all moves — replaced by the zone design, drag-verified live.) Applied by the `Z.Float`/`Z.Int`/`Z.MinMax` factories, `Z.Field` labels, and the value control's per-point curve field.
- **Zui — the 2D value control can present as a pad OR two sliders.** Right-click `ZuiValue2DControl` → choose "2D pad" or "Two sliders" (two stacked 1D value controls bound to the same pair, with per-axis labels via new `xLabel`/`yLabel` options). The choice persists per control identity across sessions.
- **Zui — ZuiFill v2: fills grow a 2D centre, and TEXTURES join as their own group.** Every gradient mode gains a draggable centre offset (slide a radial gradient's middle toward the shape's edge; slide a linear band around) and zoom; textures replace the fill when active (never nested): **Sprite** (point-sampled, tinted), **Noise** (value / ridged / stepped, mapped through the gradient), **Grid** (rotatable lines, per-axis toggles, spacing, width) and **Dots** (size, spacing, staggered rows). PyrePlus adopts it everywhere — including Text's fill/border paints and a per-pixel Background fill (gradient or textured backdrops baked into frames).
- **PyrePlus — GIF export, frame-step spawning, filmstrip, Star.** A GIF… button bakes the whole animation to an animated GIF (self-contained GIF89a encoder: median-cut palette, LZW verified by an internal encode→decode round-trip self-test, transparency, loop-forever, integer nearest upscale, delay from the preview fps). Swarm timing gains a Frames mode — first spawn at frame F, then every N frames until the count is filled — and the preview overlay's spawn dots are labeled with the exact frame each particle spawns on. The preview also gained a filmstrip mode (every frame as a wrapping contact sheet, click a tile to jump) and the Shape family a **Star** form (2–20 arms, classical five-point default, animatable length / base width / pinwheel skew).
- **PyrePlus — LAYERS and MATTE.** A spec is now a list of layers (paint order, per-layer enable/name/duplicate/reorder), each carrying its own full Shape/Swarm/Modifiers stack; the window gets a layer list and per-layer sections, and every random stream is decorrelated per layer while layer 0 reproduces the single-layer output byte-for-byte. Matte: a layer can Write one of four matte channels instead of drawing (coverage stencils, Max/Add/Subtract combine), and any draw layer can clip by a channel (with invert) — animated reveals, shaped fills, punched holes.
- **PyrePlus — Streak form + swarm placement upgrades (the Bars decomposition).** Streak = a root-anchored bar growing forward (length/width envelopes, back-spill, soft tip). Swarm orientation (None | Outward | Path tangent) rotates any form to its placement — streak sunbursts, letters flowing readably along a path. Even path spacing (+spread) places particles by index while Spawn travel rides the whole string along the outline; a scale-by-index envelope gives Bars-style tapers; Die together ends every particle at one shared moment. Legacy Pyre Bars stays frozen; its function is now compositional.
- **PyrePlus — ZuiFill everywhere + steady glows + full rotation + preview chrome.** Every colour slot (shape fill and the solid family's line/specular/glow tints) is a ZuiFill — solid colour with alpha, gradient over life, or rotatable Linear / Radial / Noise spatial fills. Glow defaults are steady (the old anti-phase pulse curves are now opt-in authoring). Solids rotate in all three dimensions (Turn / Tilt / Roll, plainly named). The preview gains Play/Pause, a Frame toggle, and the shared BackSplash backdrop.
- **PyrePlus — Text form: every character of a free string is one particle.** Glyphs come from any TMP SDF font with a readable atlas (auto-picked when none is assigned; bake-then-snapshot handles Dynamic fonts), rendered as flat 2D planes or extruded 3D letter slabs with per-letter spin/tilt. Fill is a dedicated SPATIAL gradient with three modes — the whole gradient inside each character, one flat colour per character stepped along the gradient by index, or one gradient across the whole text — plus a rotatable gradient angle; borders take a width and their own gradient (SDF-band derived, crisp at any size). Swarm on = the characters ride the full swarm machinery (Count hides — character count rules; letters can march a Path in order); swarm off = a centred text line. Line mode paints letters far-to-near so 3D occlusion is correct at steep angles.
- **PyrePlus — particle FORMS: the Shape section now renders Disc, Gem, Crescent, Sparkle, Sprite, Box, Pyramid, Can, Orb or Ring** (one Form choice; every form swarms, travels, spins and modifies identically). The full pseudo-3D family shares one Solid panel — tilt, point light (yaw/pitch/ambient/specular + specular tint), lit hard edge lines (width + colour), and the two glow channels with **their own authorable colours** (edge halo and inner glow; the tints are fields now, not constants): **Box** a true cuboid (only the 12 real edges lined), **Pyramid**, **Can** a 16-strip cylinder (cap rims lined, barrel banded), **Orb** an analytic sphere whose silhouette never deforms while its shading and hotspot roll with spin, and **Ring** a flat two-sided annulus that opens/closes with tilt, rim lines thickness-corrected by the true gradient magnitude (fixing an error carried in the Bakery reference math). **Gem** is a true pseudo-3D faceted solid — an N-sided (3–8) crown/pavilion gem, backface-culled and lit per pixel by an authorable point light (ambient + Blinn-Phong specular, base colour from the life gradient), with hard 1-pixel facet edge lines that catch the light, and TWO glow channels — an edge halo that spills past the silhouette and an inner glow rising from each facet's interior — both animatable envelopes over the particle's own life whose defaults pulse in exact anti-phase. `particleSpin` doubles as the gem's 3D yaw, `gemTilt` leans it. **Crescent** = disc minus an offsettable bite disc (animatable bite size and facing, shared edge softness on both rims). **Sparkle** = deterministic twinkling pixel cells inside the disc (animatable density, 1–4px cells; every frame standalone, no state). **Sprite** = stamps any readable Sprite scaled to the particle, point-sampled, optionally tinted by the life gradient, rotated by spin. Fire/Fireball forms are deliberately deferred (simulation state needs the replay discipline); Bars/MetaBlob on hold.
- **PyrePlus — the modular, opt-in Pyre rework, now feature-complete as a parallel prototype (its own asset/renderer/window; shipping Pyre untouched).** One implicit layer organized as three foldable sections instead of Pyre's flat ~60-field Layer. *Shape* (slice 1) is the particle's own look — colour/alpha/size envelopes over its OWN life, plus opt-in Advanced fields: a per-particle travel path (canvas-pixel offsets added to the spawn position over the particle's life) and a 2D spin that rotates the particle's pixels in place (visible once modifiers give the disc structure). *Swarm* (slice 2) turns one particle into N: even spawn timing across an authorable window, each particle living a fraction of the timeline; **Area** mode scatters uniformly-by-area inside Circle/Triangle/Square/Pentagon/Hexagon, **Path** mode places a spawn point travelling the shape's outline driven by a `progress` envelope that each particle samples at its OWN spawn frame — so easing/holding/rewinding the envelope visibly clusters, stalls or retraces the trail — and **Custom** shapes are a hand-drawn polyline stored as x(progress)/y(progress) envelopes, authored with the 2D value control or directly on the preview canvas (click to add, drag, right-click to remove). A shared shape transform (offset, scale with optional step-snap so placements land on fixed radii, 2D rotation, pitch/yaw pseudo-3D tilt with nearer-bigger/brighter depth shading) is sampled per particle at ITS spawn frame — the crucial snapshot rule: animating the transform leaves a trail of placements instead of retroactively sliding placed particles. The preview draws an authoring overlay: the live shape outline, a depth-coded dot at every particle's actual computed spawn position, and a draggable shape-position handle. *Modifiers* (slice 3) reuse Pyre's `PyreModifier` stack directly — geometry warps fold into the disc's sample position, pixel modifiers run per lit pixel, post modifiers over the composited frame — with the same grip/enable/remove/"+ Add modifier" loop as Pyre and reflection-drawn bodies. The renderer keeps `BlastRenderer`'s determinism contract exactly (pure, seeded, any frame standalone; cold-vs-sequential verified at 0 differing pixels throughout), and its `Eval` now truly mirrors `BlastRenderer.Eval`: Curve fields sample the normalized envelope directly (previously `EvaluateRaw` divided by the 4-second default duration, silently sweeping only the first quarter of every authored curve — a slice-1 bug this fixes). Fire sits alongside MetaBlob and Height balls as a generator, not a modifier, because it *makes* imagery rather than transforming it. Unlike every other Pyre shape it is genuinely stateful — heat is carried by a velocity field, so a frame depends on the one before — and it is reached by REPLAYING the simulation from a reset rather than by evaluating a formula. That is the discipline `SimulationModifier` already uses, and for the same reason: replaying is the only version that cannot show a frame built under stale dial values, so scrubbing and baking stay exact (verified: a cold jump to a mid frame is pixel-identical to arriving there sequentially). **Every rate is an envelope over the layer's life**, so what you author is the shape of the burn — ignite, swell, roar, die back — rather than a speed you then have to time by hand; emitter heat/fuel/pulse, flow, buoyancy, curl, flicker, dissipation, burn rate and reach are all animatable. The simulation is anisotropic — expressed in each pixel's arm-local frame — which is what makes it read as a flame rather than an expanding blob: **Pinch** cools by angle off the arm axis so each arm tapers to a pointed tongue, **Stretch** elongates it along its direction, curl and flicker act sideways so the tongues wave and lick, and **Breakup** eats the edges into wisps. **Arms** repeat the flame radially, and unlike the Kaleidoscope modifier — which can only copy finished pixels — these are real emitters inside ONE shared grid, so neighbouring arms genuinely bleed into each other, **Vary** gives each arm its own seed rather than a reshuffled copy of the same image, and Pinch opens the cold gaps between them so a three-arm fire is a three-point flame star, not a filled triangle. The whole thing is fast enough to author live: a per-layer sim steps one frame forward during playback instead of replaying its whole history every repaint (the difference between O(frames) and O(frames²) per shown frame), with a dirty-box, hoisted single-arm axis and lighter noise on top. Mirror instead folds the turbulence field into each arm's own sector so the arms are actual reflections; without that fold the arms moved through different noise and the symmetry Mirror promises never appeared. **Confinement is the reason a hot setting stays usable**: past the Reach radius the flame is cooled to nothing, with two pixels of guaranteed clearance, so it can never touch the frame edge however hard Flow and Buoyancy are driven — verified at maximum flow, maximum buoyancy, eight arms and Reach 1.0, all with zero lit pixels on the frame border. Colour comes from the layer's own gradient read as one ramp whose low end is smoke and whose high end is fire, exactly like Height balls.
- **Pyre — Kaleidoscope modifier: repeat any layer into N arms.** Rotate (a pinwheel), Mirror (alternate arms reflected, so neighbours meet at a seam) or Vary (each arm gets its own seeded turn, flip and scale). Arms, arc and rotation are animatable, so a fan can sweep open or the whole arrangement can spin. Deliberately a POST modifier operating on finished pixels: Pyre already had radial repeat through `star`/`spreadCount`, but that lives inside the scatter path, so MetaBlob, Height balls and Fire never reached it — working on pixels is the only place that is universal, and every shape gets it including ones not written yet. The honest limit of that choice is Vary: a truly independent arm would mean re-generating the layer with a different seed per arm, which no post modifier can do, so Vary varies each arm's transform rather than its content (Fire's own arms do the real thing).
- **Pyre — Matte layers: any layer can mask the layers above it.** A layer now has a ROLE as well as a shape: Draw composites it normally, Matte doesn't draw it at all and instead reads its luminance (times its own alpha) as a 0–1 mask over the canvas, driving the layers above. Making it a role rather than a shape or a fill mode is the whole point — it is orthogonal to both, so every shape Pyre already has, with every fill and every modifier stack, becomes a mask generator for free: a sweeping disc, a growing ring, a churning ball cloud, a noise fill, a rotated crescent. Deliberately luminance-driven rather than requiring an authored black-to-white gradient, which is what "luma matte" means everywhere else: an existing layer can be flipped to Matte and simply work, and it stays readable in colour while you author it. Because the mask is captured after the layer's own modifier pass, blurring or warping the mask is just a modifier on the matte layer. **Reach** is per-matte: Next layer clips only the layer directly above (a clipping mask), All above affects everything until another matte replaces it. **Drives** picks what the mask changes: Alpha is the classic luma matte; Brightness darkens toward black; Saturation drains toward grey (ash, heat-death); Hue rotates by a settable angle (shimmer, chemical burn); Blur softens per-pixel by the mask's own value, so one matte can hold a core sharp while its surroundings melt; and Displace pushes pixels along the mask's own SLOPE rather than its value, so flat regions don't move and only the mask's edges bend what is behind them — which is what makes it read as refraction or heat haze instead of a smear. Strength and the per-channel amount are animatable, so a mask can fade in or sweep its influence across the layer's life. A matte is never itself masked by another matte; stacking them has no clear authoring meaning. Everything stays closed-form and deterministic — verified across all six channels plus invert and both reach modes, against a rendered comparison strip and 0 differing pixels between independent renders of the same frame.
- **PreviewLab merged back in: Laubrary Dev is the single canonical host again.** Mirage, BackSplash and Pooling arrive as new modules; Zoetrope (weapons, cues, reactions, target practice), Chunks (pooling, trails, ChunkWindow), Combat2D (projectile pooling and containers), the Launimator runtime, Caching, ZoetropeLaunimator, ZoetropePyre, Lazor and AssetKit's whole LauTag/LauAsset system come across at the versions the fork had advanced them to, while this host keeps the Zui UI Toolkit toolkit, every window already ported to it, and the Pyre runtime. The copy was an overlay rather than a replace, so nothing that existed only here vanished silently — each leftover was judged individually. PreviewStage is deleted (BackSplash superseded it) and `CombatPresenter` with it (ReactionFxPlayer replaced it, as its own doc said). All the authored assets came too, verified byte-identical to their source with zero broken imports.
- **The Pyre asset type is now called `Pyre`.** Unity displayed it as "Blast Spec"; a Pyre animation is a Pyre. No asset migration was needed — a ScriptableObject references its script by GUID, not by type name, so moving the file together with its `.meta` kept all 28 assets resolving. `BlastSpecChunkAnimation` became `PyreChunkAnimation`. A type named `Pyre` inside namespace `Laubrary.Pyre` is shadowed by that namespace from any sibling `Laubrary.*` namespace, so a handful of call sites go through a `using PyreAsset = Laubrary.Pyre.Pyre;` alias — the idiom this package already used for `Laubrary.Chunks.Chunks`. `BlastRenderer`, `BlastPlayer` and `BlastBaker` keep their names: only the asset was renamed.
- **One BackSplash editor instead of two.** The IMGUI `BackSplashGUI.DrawInline` and a hand-built Zui copy inlined in Pyre's window were two editors over the same data, and had already drifted apart — only one of them clamped the image position, only one offered Save. Both are now `BackSplashZui.Build`, used by Pyre and Mirage alike; `DrawInline` had no callers left and is gone, which also removes the last IMGUI island from Mirage. The position pad's domain stays a caller parameter rather than something the control guesses, because `imagePos` is screen PIXELS in Pyre's preview and WORLD UNITS in Mirage's camera — the same value means two different things depending on who is drawing it.
- **Chunks on UI Toolkit.** `ChunkWindow` and the `ChunkSpec` inspector are fully native, no IMGUI island, and every dial records Undo — the IMGUI original took a single blanket `Undo.RecordObject` at the top of its per-frame draw, which coalesced every edit of a whole session into one undo step.
- **Zoetrope's three authoring windows (Zoes / Weapons / Ammo) on the UI Toolkit half of ZUI, with the pluggable-type picker rebuilt as a shared control.** A Zoe, a weapon and its ammo are all "recipes" whose interesting parts are *pluggable* — which view draws the character, which effect plays on a hit, how a projectile moves — and the old immediate-mode window's way of switching one of those was a two-character "▾" button that nobody could find. That header is now a real, shared control (`Z.ManagedRef`): it names the kind currently plugged in, switching it is one click on that name, and — crucially — the body underneath belongs to the *window*, not to Unity, which is what lets a field keep the better control it deserves instead of falling back to a generic one. That is what preserves the two things this editor is actually good at: a clip field offers the animation names authored on that character's own reel rather than free text, and every reference to another Laubrary asset keeps its thumbnail plus Recall / New / Edit row rather than becoming a bare object slot. Two new shared pieces make that reusable rather than one-off: a retained-mode version of the asset-picker row (so a UI Toolkit window no longer has to fall back to immediate mode for it), and a translator that turns any serialized field into the right sized ZUI control — the general answer to the specific bug the old window had to hand-patch field by field, where an unconstrained number field grew to 604px inside a 616px window. A pluggable kind that ships its OWN authored inspector (the reel view's "Open in Animation Builder", the blast/debris effect's "Preview in Pyre") is still drawn by that inspector, hosted as an island, so nothing those provide regresses. Two fixes fell out along the way: a section titled the same as the single field inside it no longer prints the word twice, and every dial now records undo through the serialized layer rather than the weapon/ammo windows' old single blanket record per repaint.
- **ZUI (UI Toolkit): plain curve and gradient fields, and controls stop stretching across a column.** A tool whose data already holds a real animation curve or gradient (debris size/alpha/colour over life) had no ZUI control for it and had to reach past the toolkit; both now exist, sized rather than left to fill the panel. Separately, a checkbox placed on its own line used to claim the entire window width as its click target — visually invisible, but it meant clicking far to the right of a checkbox toggled it, and the layout audit flagged it as an oversized control. Controls now size to their own content in that situation while keeping their alignment when they share a row.
- **Mirage on the UI Toolkit half of ZUI, with each block now a section that owns what it names.** Mirage's window is built from retained `Laubrary.Zui` (`Z.*`) controls, rebuilt in one targeted place whenever the SET of controls changes (an entry selected, a content type swapped, Target Practice toggled) — and since a retained control only fires on a real user change, the defaulting the old immediate-mode window got for free (arming a clip step implicitly picked its first trigger, because the dropdown wrote its selection back every repaint) is now done explicitly. Every mutation routes through one `Undo.RecordObject` + `SetDirty` helper, which is also what keeps the live-preview contract intact: the dirtying is what makes Unity publish the change event that `AssetCacheInvalidation` turns into a scene-side refresh, so a MirageRig still updates as you type with no restart. The reverse flow needed real work — the Mirage HUD's Game-view drag writes an entry's position behind the window's back, which an immediate-mode window picked up for free every repaint — so a window-owned editor tick pushes those external writes back into the position pad, its numeric fields and the list's row labels. The blocks (View / Previewables / Entry / Zoe options / Clips) are `Z.Section`s rather than bare headings: a heading works out its own fold extent from its following siblings, and this window's shape defeated that — folding "View" hid everything down to "Entry", the entire previewable list included, and the "Previewables" heading sat inside a row where its siblings were that row's own controls rather than the list below it. Each previewable's name button now grows into one bounded column so Ping/Remove line up as real columns instead of landing at a different x on every row. The ONE deliberate IMGUI island is the embedded BackSplash editor, hosted verbatim in an `IMGUIContainer`: `BackSplashGUI.DrawInline` is a shared viewport-drawing helper that Pyre's own window also calls, so its public API is deliberately untouched (it is explicitly scoped with `ZUI.UseSheet`, since an `IMGUIContainer` gets no ambient ZUI sheet and would otherwise render unskinned). The two asset pickers (`LauAssetPicker`/`LauTagPicker`) stay `PopupWindowContent`, called from UI Toolkit buttons.
- **ZUI (UI Toolkit): every section heading and every framed box now folds when you click it.** A heading used to be a label and nothing more — it sat beside the controls it named without owning them, so a tool panel with a dozen blocks could only ever be scrolled, never collapsed down to the two blocks actually being worked on. Three things changed, and between them every heading already written across Pyre, Choreographer, Launimator, Rulesets and Larder became collapsible without a single call site being touched. `Z.Text(.., ZuiText.Section, ..)` now returns a `ZuiSectionLabel`: still a `Label` to its caller, but it works out its own extent — clicking it hides every following sibling up to the next section heading, which is exactly the "heading, then a run of controls, then the next heading" shape these windows are actually written in. `Z.Box` now returns a `ZuiBox` that owns its children through a `contentContainer` override, so a titled box folds from its title row. And `ZuiSection` (the explicit form, for new code) had its fold state re-keyed by title *plus* tooltip, because title alone collides — "Gradient" heads three different blocks in Pyre alone. All three keep fold state statically, so a section the user closed stays closed through the window rebuilds that every dial edit triggers, and all three toggle through a `Clickable` manipulator rather than a raw `PointerDownEvent`: a bare `RegisterCallback<PointerDownEvent>` never fired for a header inside a `ScrollView` (verified live — the fold machinery worked when driven directly, but no real click ever reached it), whereas `Clickable` owns the down/up pair and the capture between them exactly as `Button` does. A heading sitting in a ROW is deliberately left inert, since its siblings are the rest of the row rather than the block below it. Section headings also got bigger (13px bold), more air above them, a hover highlight, a ▾/▸ caret, and a bright green tint so a heading reads as structure at a glance and is never mistaken for a field label; sub-sections inside a box now show their nesting by a progressively darker fill instead of a second and third border, which was becoming unreadable at depth.
- **Pyre — Height balls: one ball population, and per-ball authoring.** A ball group used to have two ways to make a ball: a resting cloud with no lifetime at all, and waves. A group made only of resting balls therefore existed at full mass from the first frame to the last and could never move — a genuinely frozen layer that the dials permitted. There is now exactly one population: every ball belongs to a wave, waves can never be fewer than one, and "Balls per wave" is the only ball count (one single ball is Waves 1, Balls per wave 1). Because every ball now has a birth and a death, the dials that describe a single ball — Alpha, Height, Mass, Ball size and Spread — are authored across *that ball's own life* rather than the layer's, which makes an Alpha curve that starts and ends at zero the ball's fade in and out; the separate Fade in / Fade out dials are gone as redundant. Cloud size, Push, Rotation and Churn describe the group as a whole and stay on the layer's clock, and the editor now groups the two sets into separate boxes that name which clock they run on. Spread's three positions are exact rather than approximate: 0 clumps every ball of a wave in the middle, 0.5 spreads them evenly, 1 puts them all on the rim as a ring, so animating it blows a clump out into an expanding ring. Cloud size reaches about 20 % further at its maximum: a ball placed inside the confinement circle is no longer squeezed or withered at all — only travel beyond it is — so the cloud can now use the full room Confine allows instead of stopping well short of it, and Confine's own promise that the cloud never touches the frame edge now holds even with Surface noise inflating its rim. Existing groups upgrade in place and non-destructively: resting balls are folded into Balls per wave so density is preserved, and the old fade values seed the new Alpha curve's shape.
- **Pyre — Height balls: any number of ball groups fused into one cloud.** A Height-balls layer is no longer a single cloud with one set of dials; it now holds any number of *groups*, each with its own count, cloud size, spread, ball size, height, mass, alpha, rotation, churn and waves — all animatable over the layer's life. Every group feeds the SAME fused density/heat/height fields in one pass before shading, so a low, cool smoke bed and a hot burst on top melt into one continuous mass. That is the thing two separate Pyre layers can never do (they composite), and it is why groups exist. Fusion, coverage, relief lighting, confinement, folding and the colour ramp stay on the layer — they describe the one fused result. **Height replaces Rise and the old resting-heat dial**: it is how far up the smoke→fire ramp a group sits, authorable as a curve, and it is now the *only* source of a ball's heat — the per-wave Peak heat dial is gone, and Ignition/Wither become Fade in/Fade out, governing a wave ball's presence (mass and size) rather than its temperature, so a ball born mid-life joins the cloud at whatever height the curve is at. Spread is a distinct dial from Cloud size: cloud size is the group's extent, spread decides whether balls crowd the middle, fill the disc evenly, or form a hollow rim shell — animate it to blow a puff out into an expanding ring. Surface noise is now sampled per group, so separate strata can be rough in different ways while each group's own balls still deform together. A single wave now spans the whole layer life instead of ending early. Layers authored before groups existed upgrade themselves on first access, non-destructively (every old field stays serialized), seeding Height from the hotter of the old resting/wave heats so a wave-driven layer keeps its apparent temperature. Everything stays closed-form per frame — preview, bake and runtime remain identical and any frame can be scrubbed to directly. Two bugs were found by looking at the output rather than the code: `SmoothMax(0,0,k)` raised a faint grey haze across the whole frame once more than one group existed, and reordering groups reseeded every group's arrangement (fixed with a stable per-group seed salt, so reordering is now purely cosmetic — verified 0 differing pixels).
- **Pyre "Height balls": Squash and Surface noise — the cloud no longer reads as a bag of circles.** The first implementation fused perfectly circular domes, so however well they melted, the result was still round lobes stuck to round lobes. Two new mechanisms fix that, both closed-form so scrubbing and baking stay bit-identical. **Squash** gives every ball its own seeded ellipse at its own angle (up to roughly 2:1, in *both* directions so the cloud gets wide and tall balls rather than all leaning one way), slowly turning as the cloud churns; a burst's balls additionally lean along their own travel direction, so an energised plume stretches outward instead of pushing a ring of circles. Confinement now measures a ball's LONGEST axis, so squashing can't poke past the self-limiting radius the shape guarantees. **Surface noise** is sampled once per pixel in the cloud's SHARED space — deliberately not per ball — so every ball overlapping that pixel is stretched or pinched by the same amount and neighbours bulge and dent *together*, their rims interlocking into one lumpy mass; a per-ball noise would only have given each ball an independent wobble, which still reads as a pile of blobs. It warps the silhouette and ripples the height field, so the relief lighting picks the roughness up as surface texture rather than merely a wavy outline, with Noise size and Noise drift dials (drift crawls the field over the layer's life so the surface roils instead of holding one frozen pattern). Verified by rendering the same seed at squash-only / noise-only / both and comparing against the original round version.
- **Launimator ported to the UI Toolkit half of ZUI.** All four Launimator windows — the Animation Builder, the Reel Browser, Anim ↔ Aseprite and the Sprite Catalog editor — now build their control surface once as retained `Laubrary.Zui` (`Z.*`) elements instead of redrawing it every IMGUI frame. Six surfaces stay deliberate IMGUI islands because they are bespoke canvas painting or direct-manipulation gizmos and must not regress: the sheet canvas (marquee/handles/pick/eyedropper/auto-detect), the registration stage (onion-skin + drag-to-place), the play box and meta paint editor, the sprite-palette and sequence thumbnail grids, the zone bar, the Sprite Catalog stage, and the Reel Browser's clip preview (which goes through the one shared frame visualiser, so porting it would fork that renderer). Two former "previews" that were only texture blits became native image elements. Playback no longer repaints the whole window — it repaints only the surfaces that show the playhead, patching the frame-naming labels in place — and structural edits go through a targeted host rebuild, so no control can show a stale value. The hand-computed row-width budgets are gone (flex-wrap does it), every control carries a tooltip, and the Sprite Catalog editor is Undo-safe for the first time (its edits previously recorded nothing). The Launimator editor assembly no longer references the IMGUI ZUI assemblies at all. Also fixed along the way: picking a sheet texture no longer half-assigns it before Load (the old path left `_sheet` set with a stale path/width), and the custom pivot is now a single 2D control instead of two float fields.
- **ZUI → UI Toolkit migration: Rulesets and Zoetrope ported, plus a new shared `ZuiReflect` that closes the last three `// ZUI-GAP:` markers.** `RulesEditorWindow` (all three tabs — Rules, Rulesets, Global Rules) and `ZoetropeDefWindow<T>` (Zoes / Weapons / Ammo) are now native UI Toolkit. Both were blocked in the IMGUI era by gaps that UI Toolkit simply does not have: a **typed object field whose type is only known at runtime** (`ObjectField.objectType` is a settable property, so no compile-time generic is needed — the old `ZUI.ObjectField<T>` could not express this, which is why those call sites stayed raw `EditorGUILayout`), an **enum field for a runtime `Enum`**, `helpBox`-framed **list rows/cards** (now just `Z.Box`), and a **`[SerializeReference]` managed-reference picker** — a bound `PropertyField` renders the type dropdowns with automatic Undo, and unlike the old per-frame `SerializedObject` loop it tracks external changes itself. Rulesets' hand-rolled reflection renderer was extracted into a new reusable **`ZuiReflect`** (`ObjectByType`, `EnumByValue`, `Vector2Row`/`Vector2IntRow`, and `BuildFields`/`BuildField` for whole-object rendering including `List<>` with add/remove, plus a duck-typed float-wrapper hook so a host can surface `ZUIValue`-style wrappers as plain floats without this file knowing their type) — so any future data-driven tool gets it instead of hand-rolling its own. Rulesets also gained real **Undo** on rule-field edits (it previously only did `SetDirty` + `SaveAssets`, so a mistuned rule could not be undone), recorded against the owning `RuleSet` asset and correctly skipped in Play mode where the rules are a live clone with no asset behind them.
- **Pyre: a new layer shape, "Height balls" — a height-map + gradient driven smoke-and-fire cloud.** A layer of this shape is a churning cloud of soft balls, each carrying two quantities: MASS (how much of the cloud it is) and ENERGY (how hot it is). The balls are melted together into three continuous fields — mass, energy, and a HEIGHT field that weights energy well above mass — so overlapping balls read as one flowing body rather than a pile of discs. The height field's local slope then lights the cloud as if from one side, which is what gives it the chunky, carved pixel-art 3D look; and a pixel's combined mass+energy indexes ONE continuous gradient whose low end is smoke and whose high end is fire. That single ramp is the layer's own existing Colour gradient, so energising a ball literally walks it UP the ramp from grey smoke into ember, flame and white heat, and losing energy walks it back down. Count / Cloud radius / Ball radius / Position / Alpha are the layer's normal shared dials, so it composites, times, warps and takes modifiers like any other layer — including Pyre's existing Posterize and Ordered dither, which cover the pixel-art quantization this look wants (deliberately not duplicated inside the shape).
  Three behaviours were designed in deliberately, answering how bursts inside such a cloud usually go wrong:
  **(1) Energy bursts are born cold and climb.** A burst's new balls appear at the cloud's resting heat, indistinguishable from the ordinary balls already there and buried inside the existing mass, then heat up over the "Ignition" fraction of their life. So a burst reads as the cloud igniting from within rather than as hot pixels popping into existence.
  **(2) They wither instead of vanishing.** Over the "Wither" fraction of their life a burst's balls cool back DOWN the gradient toward smoke while simultaneously shrinking and thinning out, so a burst ends by dissolving back into the cloud it came from — nothing ever blinks out.
  **(3) Pressure folds the cloud in on itself instead of letting it escape.** Outward travel eases to a halt well before a burst ends (a push and a churn, not a launch), and every ball's distance from the cloud centre is squeezed through a curve that can only ever approach a "Confine" limit, never cross it — so the cloud has a hard, self-limiting silhouette and the animation can never collide with the canvas edge, which previously made the whole effect unusable as a game asset. Inside that outer band, a "Fold under" pressure makes a ball lose mass and heat, shrink, sink back toward smoke and get tucked inward, so pushing harder thickens and churns the cloud rather than flinging balls at the frame.
  Everything above is CLOSED-FORM: a ball's resting place, its churn, which burst it belongs to, how much energy it currently carries, how far out it has travelled and how far it has withered are all computed directly from (seed, ball index, frame) with no state carried between frames. So this stays a plain scrubbable layer shape — any frame can be rendered on its own, and preview, bake and runtime remain identical — rather than needing Pyre's iterative simulation slot.
- **ZUI → UI Toolkit migration: Larder ported — the first FULLY native tool (no IMGUI island at all).** `LarderWindow` is now a `ZuiAssetWindow<WareSpec>` built entirely from `Z` controls. Unlike Pyre, its preview needed no `IMGUIContainer`: the ware stage and the damage-stage strip were only `GUI.DrawTexture(..., ScaleToFit)` blits, which `Image` does natively — so the strip is now a flex row of equal-basis cells, each an `Image` + caption, and the whole window is retained-mode. Every dial gained a tooltip and proper Undo (the IMGUI original only recorded undo for "Randomize whole Ware" — every other dial edited the asset with no undo entry at all); mode switches that reveal/hide dials (Custom colours, Kind) route through a rebuild. Verified: compiles, live screenshot with a real Ware showing the intact preview + 4 damage stages, `ZuiAudit` zero findings.
- **ZUI → UI Toolkit migration, phase 5: first Pyre audit pass — envelope/2D-control parity + an animatable orb radius.** Acting on a hands-on review of the ported Pyre window: **`ZuiEnvelope` gestures now mirror the IMGUI control 1:1** — double-click empty space (or the curve) inserts a point and starts dragging it, a plain click-drag on empty space runs a box-select marquee, double-click or right-click a point removes it, Shift+right-drag bends a segment's exponent, Shift+left-drag moves a whole segment's values, Delete removes the selection, and multi-point drags move every selected point together with per-axis `editState` and neighbour-time clamping. **Expanded envelopes now render per the requested layout**: the LABEL is the fold toggle (click it to open/close) with the "⋯" menu beside it, the envelope itself spans the full width with no thumbnail, and an optional per-point numeric-input COLUMN stands to its right — with new menu items "Show point values" (values drawn beside each dot), "Show numeric inputs", and "Inputs for selected points only" (which pairs with box-select). Collapsed envelopes keep the compact label · thumbnail · ⋯ row. **`ZuiValue2DControl` was refactored onto a `Zui2DSource` abstraction** so ONE control now serves both an animatable ZUIValue pair and a **plain `Vector2`** (`Z.Vector2Field`), the latter reporting `SupportsAnimation = false` so no mode switch appears — used for Pyre's blast Origin and MetaBlob orb positions, which must never animate. Its display-option menu items were **fixed**: they were no-ops whenever the control sat collapsed (its default state), and now expand it as they apply. New `Options`: `showSidePanel`, `sidePanelExtra` (Pyre's origin marker-α slider lives in the origin control's own side panel), `startExpanded`; `Z.Slider` gained `showInput` for a bare track. Pyre's Origin section is now that full 2D control (label + α + Reset column beside the pad), replacing the ad-hoc pad + "Origin → centre" button (Reset covers it). **`MetaOrb.radius` became an animatable `ZUIValue`** (`MetaOrb.Radius`), so an orb can pulse/grow over its OWN life — `BlastRenderer` evaluates it at that orb's life progress. Migration is non-destructive: the legacy scalar stays serialized and seeds the new value on first access behind a `radiusUpgraded` flag (Unity never leaves a `[Serializable]`-class field null, so a flag — not a null check — is what distinguishes them); verified against a real 16-orb asset. MetaBlob orb rows accordingly gained a square position pad + an animatable Rad field, and their delete button moved to the row's end. The preview now draws each orb's ring at its radius **as of the current frame** (so an animated radius is visible while scrubbing) plus, for static radii only, a small **drag handle on the ring** to resize the orb directly in the viewport. `ZuiEnvelope` also gained a public programmatic API (`InsertPoint`/`RemovePoint`/`SelectInBox`/`BeginDrag`/`PointToLocal`/…) and a `HasLayout` guard — testing found that driving a gesture before the panel's first layout would write NaN into the caller's data. Verified: 6/6 headless gesture assertions (insert-sorted, box-select membership, hit-test, remove, locked-anchor refusal, neighbour clamping), a headless assertion that the 2D display options now change the built tree, live screenshots, and `ZuiAudit` still reporting zero findings.
- **ZUI → UI Toolkit migration, phase 4: the ENTIRE Pyre window rebuilt on Laubrary.Zui (Z.*) controls.** `PyreWindow` is now a `ZuiAssetWindow<BlastSpec>` split across four partials (shell/Layers/Modifiers/Preview): every dial — blast settings, global + per-layer modifier stacks with all ~40 modifier bodies, the layer list, every per-shape section (Bars/MetaBlob/Rosing/Sparkle/Crescent/Sprite/Disc), rose rings, noise fill, backdrop/test-background/Reel-Preview panels, and the full transport — is built from `Z` controls with per-control tooltips and the shared once-per-gesture Undo contract (an upgrade: the old window's change-check only recorded undo per-repaint-pass). Layer AND modifier drag-reordering now goes through the new shared `ZuiReorder` grip helper (the deferred "generic reorderable-list" TODO, now with its 3rd+4th real call sites); packed rows use flex-wrap so the old GroupFieldWidth/horizontal-scrollbar machinery is simply gone — controls wrap instead of overflowing. **The ONE deliberate IMGUI island is the preview viewport** (an `IMGUIContainer`): it is genuinely bespoke canvas painting — frame-texture blits, `PreviewStageGUI`/`LiveScenePreview` helpers, and all six gizmo interaction layers (origin ✛, MetaBlob orbs, Smudge strokes, Pin warp, Curl vortices, stage sprites) — kept byte-for-byte from the pre-port window so preview behavior stays pixel-identical; the migration plan's §4 "re-evaluate Pyre's canvas" question is hereby answered: texture-blit + IMGUI-helper composition, correctly left raw. Toolkit additions driving this: `ZuiValueControl.Options.controlWidth` (packed-row support), `Z.MiniRadioVertical`, `Z.Stacked` (label-over-slider), `ZuiReorder`, and a `zui-audit-allow-stretch` opt-out class on `ZuiAudit`'s stretch check for the rulebook's own name-field exception. Verified: compiles clean, full-window screenshots with a real blast (Bars Tentacle) playing — animation advancing with the scrubber tracking live, origin marker flashing, modifier stacks/single-value inline headers/gradients/MinMax life window all rendering — and `ZuiAudit` reporting zero findings. NOT yet hand-verified (flagged for the user's pass): the preview gizmo interactions by mouse, drag-reorder feel, and `PopupWindow.Show` positioning when opened from UI Toolkit buttons (layer library Recall…, backdrop Recall…).
- **ZUI → UI Toolkit migration, phase 3: `ZuiValue2DControl` + the UI Toolkit-native audit.** `Z.Value2D` / `ZuiValue2DControl` ports the synchronized XY-pair control (Static draggable dot with optional "(x, y)" text and numeric X/Y block; Curve mode tracing a numbered path with click-to-append / drag / right-click-remove and order-derived renormalized times; fold-to-thumbnail; the ⋯ mode/display/copy-paste menu). Clipboard payloads use the SAME `ZUIVALUE2:` prefix + JSON shape as the IMGUI control, so copy/paste interoperates across the two halves. Point numbers and value text render via `MeshGenerationContext.DrawText`. And `ZuiAudit.Audit(EditorWindow)` (migration plan §10/§11.5) — a plain tree-walker over any live window checking tooltip-missing (self + ancestor chain), off-screen (outside a ScrollView), BaseField flex-grow stretch, and over-width; public API only, no menu item. Verified: zero findings on the Choreographer + showcase windows, AND a deliberately-bad negative-test window where all four planted defect kinds were caught (the 700px slider correctly fired both over-width and off-screen).
- **ZUI → UI Toolkit migration, phase 2: rich controls + the rest of the fundamental control set in `Laubrary.Zui`.** New retained-mode counterparts of ZUI's heavy controls, all editing the SAME runtime data types as their IMGUI twins (no asset changes): `ZuiPad` (`Z.Pad` — PositionPad twin for a plain Vector2, flipY convention preserved), `ZuiEnvelope` (`Z.Envelope` — DAW-style editor over `List<ZUIEnvelopePoint>`/`ZUIEnvelopeEvaluator`: exponent-bent curve rendering, per-edit-state handles with hover, per-axis drag permissions with neighbor-clamped time, click-empty-to-insert, right-click-remove, drag-a-segment-to-bend-its-exponent, `anchorsLocked`, grid), and `ZuiValueControl` (`Z.Value` — the ValRow workhorse over `ZUIValue`: Static slider with optional double-click reset / MinMax range / Curve mode with live Painter2D thumbnail folding open to the full envelope plus Duration-Warmup-Loop and Value-Range rows, the "⋯" mode/multiplier/copy-paste menu, and the live dynamic-value readout on a 250ms scheduler). All three share one Undo contract: an `onBeforeMutate` hook that fires once per gesture before the first mutation. Fundamentals rounded out in `Z`: `Dropdown`, `EnumDropdown<T>`, `Int`, `Color` (tooltip required — the control type tooltip audits kept catching bare), `MinMax` (numeric-flanked range slider), `CycleButton`, `Foldout` (collapsible framed section; tooltip also set on the internal disclosure Toggle, which doesn't inherit it), `Help` (native HelpBox). Verified: compiles clean; every control screenshot-verified in a live (ephemeral, uncommitted) showcase window; envelope mutation logic additionally smoke-tested headlessly against the live element (hit-test, neighbor-clamped drag, falling-segment exponent-bend direction, change-event counts — all passed). Deliberately NOT ported yet, tracked in zui.md: envelope loop/trim markers, box-select, value labels, the "★" preset popup (IMGUI-only for now), and `ZUIValue2DControl`'s XY-pair control.
- **ZUI → UI Toolkit migration, phase 1: new `Laubrary.Zui` retained-mode toolkit + Choreographer pilot port.** A new editor-only assembly (`Zui/Toolkit/`, asmdef `com.Lautaro-Arino.Laubrary.Zui.Editor`, namespace `Laubrary.Zui`) is the UI Toolkit half of ZUI: a `Z` static factory class (Row/Box/Text/Field/Button/Toggle/Slider/SliderInt/Float/TextInput/Object/MiniRadio/HelpIcon, plus `.W(px)`/`.H(px)`/`.Shown(bool)` extensions) where **every control factory requires a tooltip parameter** — the no-bare-labels rule enforced by construction; a `ZuiWindow` base (build-once `BuildUI(root)`, automatic full `Rebuild()` on undo/redo so retained controls never show stale values); hand-authored `ZuiToolkit.uss` styling with USS custom properties as the palette (the styling decision from the migration plan §7, option b — no Zheet layer underneath) including a global `flex-grow:0` guard on every BaseField (kills the "control silently stretches to fill the window" IMGUI-era failure class) and auto-width BaseField labels. AssetKit gained `ZuiAssetWindow<T>` — the UI Toolkit counterpart of `LaubraryAssetWindow<T>` with the same chrome contract (assign/New/Duplicate/Rename/Delete/Browse toolbar, thumbnail-grid browser, project-change rescan, opt-in animated thumbnails) sharing `AssetLibrary<T>` for all CRUD; the IMGUI base stays untouched for not-yet-migrated tools. **ChoreographerWindow is the pilot port**: full dial panel rebuilt on `Z.*` controls (with per-dial `Undo.RecordObject` — an upgrade over the old change-check-only editing), and the live stage rebuilt as a `Painter2D` custom element (routes/onion/trails/dots/facing ticks/anchors/handles/marquee all vector-drawn, sample sprites as pooled child `Image`s with rotate transforms) keeping the exact same `ChoreographySampler` calls and drag/insert/remove/marquee interaction model. Verified: compiles clean, full-window + browser screenshots (PrintWindow capture; found and fixed a flexbox `min-height:auto` overflow, USS `:first-child` unsupported-pseudo-class warnings, and a DPI-virtualization capture artifact along the way). Old IMGUI ZUI remains fully working and unremoved; migration continues tool-by-tool per `ZUI_UITOOLKIT_MIGRATION_PLAN.md`.
- **Envelope presets — saved curve shapes, two tiers, on both `ZUI.CurveField` and `ZUIValueControl`'s Curve mode.** A new "★" button next to the fold arrow (always visible, folded or expanded) opens `ZUIEnvelopePresetPopup`: a picker over ZUI's own hard-coded `ZUIEnvelopeBuiltInPresets` (Linear, Ease In/Out, Ease In-Out, Hold Then Rise, Spike, Bell, Full Then Fall — literal code, `Zui/Scripts/Editor/ZUIEnvelopeBuiltInPresets.cs`, not `.asset` files, so this doesn't break the "tools ship zero assets" rule) plus the current project's own saved shapes (`ZUIEnvelopePresetLibrary`, auto-created under `Assets/ZUI/` on first save, same pattern as `PyreLayerLibrary`). Every row shows a live thumbnail via the existing `ZUI.DrawCurveThumbnail` renderer. A preset's points (`ZUIEnvelopePreset`, reusing `ZUIEnvelopePoint`) are stored normalized to `[0,1]` on both axes — a SHAPE independent of any one field's range — and get remapped onto the target field's actual `yMin`/`yMax` on apply (and the inverse on save), so the same "Spike" preset looks right on a 0–1 alpha curve or a –200–200 spawn-rate curve alike. Verified: a headless test (built-in set validity, apply/normalize remap math, project-library add/remove round-trip) all passed; the popup's own IMGUI rendering wasn't independently screenshotted (Unity's transient `PopupWindowContent` closes on focus loss, uncooperative to script-drive) but structurally mirrors `PyreLayerLibraryPopup`, an already-shipped, working control of the same shape.
- **`ZUIValue2DControl`: configurable Static-mode value display + copy/paste, on both it and `ZUIValueControl`.** New `Options.showValueText` (small "(x, y)" text beside the drag dot) and `Options.showNumericInputs` (a compact "label Y / input Y / label X / input X" numeric block, replacing the old always-on side-panel X/Y fields) — both are code-set defaults (`Options.WithValueDisplay(...)`) that a user can independently override per control from the existing "⋯" right-click menu, persisted for the session. New code-only `Options.WithVerticalStack()` stacks the numeric block above the plot instead of beside it (not exposed on the menu — a layout call for the window author, not a per-user preference). Both `ZUIValueControl` and `ZUIValue2DControl`'s context menus gained Copy/Paste of the field's full value (mode + all mode-specific data, including a Curve's whole point list — not just a sampled number), via new `ZUIValue.ToClipboardString()`/`TryFromClipboardString()`/`CopyFrom()` (JSON on the system clipboard, prefix-validated so the two controls' payloads and unrelated clipboard text can't be silently cross-pasted; Paste greys out when the clipboard doesn't match). Verified: a headless round-trip test (Static and Curve modes, plus prefix-rejection) and a live visual check of all four new Options combinations via Coplay.

### Fixed
- **ZuiAudit was blind to two whole classes of problem, both found by eye on windows it had just passed clean.** A `PropertyField` was invisible to every check: it is not in the interactive-type list, and the over-width check exempts `Foldout`s as containers — which is exactly what a list `PropertyField` renders as. The Chunks sprite list therefore spanned 812px against a 600px cap with a clean audit, its size field stranded at the far edge of the window. It is now checked (proven by removing the fix and watching the finding appear), and `ZuiSerialized.Property` bounds an unspecified-width PropertyField rather than letting it fill the window. Separately, the walker skips hidden branches — sensible until every section heading and box title became foldable, at which point a window sitting with half its blocks closed could report zero findings having barely been looked at. `Audit` now reports how many subtrees it could not see, and `ZuiAudit.ExpandAll` opens everything first so a clean result means something.
- **Pyre: `PixelFluidModifier`'s vortex/shockwave/viscosity sliders looked almost inert while the preview was
  paused mid-clip** (reported: "none of the vortices sliders actually do anything... viscosity doesn't do
  anything... shockwave sliders also do very little"). Root cause was in `SimulationModifier.EnsureFrame`
  itself, not the simulation math (confirmed by isolating each subsystem directly — all three produced strong,
  correct displacement on their own): a "same frame re-request" (e.g. a slider dragged while paused) took a
  cheap checkpoint-restore-and-re-step shortcut, re-stepping only the CURRENT frame with the new value while
  every earlier frame's contribution to accumulated state — an already-spawned emitter's own strength/radius,
  baked in at spawn time; the velocity field's whole accumulated history — still reflected the OLD value. One
  re-stepped frame barely moves cumulative state built from many frames under the old value, so parameters that
  mostly matter cumulatively looked unresponsive, while the projectile tunnel's own strong, immediate, current-
  frame injection looked fine by contrast. Fixed by removing that shortcut entirely: any frame request other
  than the guaranteed-next-sequential one (which is all the real bake ever does) now does a full, deterministic
  replay from frame 0 with the CURRENT parameters — the only way an edited slider correctly reflects across the
  whole accumulated history, including every emitter's own spawn. `SaveCheckpoint`/`RestoreCheckpoint` (now
  dead weight) removed from `SimulationModifier`'s abstract contract and from `PixelFluidModifier` entirely, a
  nice simplification alongside the fix. Verified: dragging a slider while paused on frame 10 (after previously
  playing there with a low value) now shows the full-strength effect immediately, not just a small nudge; all
  earlier sequential/redo/jump/clone regression checks still pass.
- **Pyre: `DissolveModifier`'s Erase pattern stayed rigid in screen space even under a Sphere/Ground/Jagg warp
  in the same stack** (reported: "having a Sphere after a Dissolve, I would expect the Erase dots to be affected
  by the sphere distortion"). This was true even before this session's Smoothness work — Dissolve has always
  hashed on the raw, POST-composite screen (x, y), by which point any geometry warp has already been baked into
  final pixel positions with no way back to "what local position was this before the warp." Not fixed in place
  (see `LayerDissolveModifier` under Added, and Changed below for how the two now split).

### Added
- **`ZUI.Box(title, styleName, tooltip)` gains an optional tooltip param** that draws a `HelpIcon` sharing the title's own row (right-aligned, sheet-styled) instead of needing a separate row below it — new canonical rule: a hover icon explaining an area belongs on that area's title row, never on a row of its own or buried in a content row. Fixed the two real violations this rule was written against: Pyre's "Preview backdrop" (icon was stuck mid-way down a Save/Recall content row, not the title) and "Reel Preview" (icon had its own blank row right below the title). `ZUIWindow`'s instance `Box(...)` wrappers gained the same param. See `ui-layout-rules.md`'s "Labeling" section.
- **UIAudit gains editor-window coverage** — until now UIAudit could only lint runtime OnGUI HUDs (Play mode, draws routed through `ZuiRuntime.Zui`); the actual `ZUI.Editor` tool-window UI every Laubrary tool is built with was invisible to it entirely. New `EditorZuiAudit` (`Zui/Scripts/Editor/ZUIEditorAudit.cs`) records `Button`/`Toggle`/`Slider`/`MicroSlider`/`SliderRange`/vertical-`Slider`/`Label` draws from their shared core-draw functions; new `Editor/UIAudit/EditorWindowAuditSection.cs` (own asmdef, `com.Lautaro-Arino.Laubrary.UIAudit.Editor`, bridging `ZUI.Editor` and the UIAudit runtime asmdef — kept out of the shipped Runtime assembly per the runtime-never-depends-on-editor rule) lints it against the focused window's own rect, in Edit mode, no Play mode needed. New `UIIssueKind.OverWidth` (a control over a soft per-kind px cap — the mechanical catch for "this slider is way wider than it needs to be") alongside reused `TinyText`/`TextOverflow`/`Crowded`/`Overlap`/horizontal-only `OffScreen` (which doubles as the "would need a horizontal scrollbar" smell signal — see `ui-layout-rules.md`). Menu: `Laubrary/Toggle Editor UI Recording` + `Laubrary/Audit Focused Editor Window`. Not yet instrumented: `ObjectField`/`ColorField`/`TextField`/`MicroMinMax` and raw (non-`ZUI.*`) draws — a known, documented gap, not a silent one.
- **`Assets/Tests/UIAudit/BadEditorWindowFixture.cs`** — the editor-window sibling of `ImguiFixtureHud`: a deliberately-bad `ZUIWindow` exercising every mechanically-detectable issue kind above, PLUS rules-only smells the audit tool can't catch geometrically (row-packing waste, a redundant box title, explanatory title text) — doubles as a cold-start benchmark for how well a fresh Claude session applies `ui-layout-rules.md` unprompted. Frozen by design; do not fix it in place (see its header comment). New `UIAuditEditorWindowFixtureTests` (EditMode `[Test]`, driven via `EditorWindow.SendEvent` — no Play mode needed, unlike the runtime fixture's tests) asserts the bad fixture reports `OverWidth`/`TinyText`/`Crowded`/`Overlap`. Verified live via Coplay before committing (`ScriptableObject.CreateInstance` + `ShowUtility()` + `SendEvent` — `CreateInstance` alone throws on `SendEvent`, no native view yet).
- **Pyre: `LayerDissolveModifier` ("Layer dissolve")** — Dissolve's shape-local sibling, offered only in a
  layer's own Add-modifier menu (not global): hashes on `p.wx`/`p.wy`, the geometry-WARPED position every
  PixelModifier already receives, instead of Dissolve's fixed screen (x, y) — so a Sphere/Ground/Jagg/Wobble
  earlier in the SAME layer's stack genuinely drags the erase-dot pattern along with it. The trade-off: as a
  PixelModifier (one pixel at a time, no neighbour access), its own Smoothness can only do the self-fade half of
  Dissolve's Smoothness (a pixel fades out over subsequent frames as Amount keeps rising past its own
  threshold), not the neighbour-bleed half — that needs whole-frame buffer access, exactly what Dissolve's own
  PostModifier form has and this one gives up in exchange for geometry-awareness. Verified: hashing is provably
  a pure function of (wx, wy) — two different raw (x, y) with identical warped position give the same erase
  decision, and the same raw (x, y) with different warped positions disagree on ~49% of samples; end-to-end,
  stacking Sphere before it and raising Sphere's Strength measurably changes the composited erase pattern.
- **Pyre: `Layer.simulationModifier`** — a per-layer counterpart to `BlastSpec.simulationModifier`, so a Pixel
  fluid (or future SimulationModifier) can react to just ONE layer's own pixels/shape instead of (or alongside)
  a blast-wide one. Mirrors the blast-wide slot exactly — its own single nullable field, not a list, guaranteeing
  "only one, always last" the same way — except scoped to that layer's own isolated buffer: runs at the tail of
  `FinishLayerPost`, after the layer's own Post modifiers, before it composites onto the frame. A layer with a
  simulation modifier now forces the isolated-buffer path (same as having a Post modifier) even with no other
  Post modifier present, since the simulation needs that layer's own composited pixels to react to. New editor
  section ("Simulation (genuinely iterative, always last IN THIS LAYER)") added to every per-layer panel,
  reusing the same UI helper as the blast-wide section (now parameterized by getter/setter instead of being
  hard-wired to `BlastSpec`). Verified: a layer-level Pixel fluid measurably displaces/erodes that layer's own
  pixels; renders without error alongside a separate blast-wide Pixel fluid at the same time.
- **Pyre: Noise fill's Warp promoted from a plain float to MultiCont (ZUIValue)** — now animatable, matching
  every other Noise fill field (Zoom/Rotation/Drift/Gradient position/Gradient zoom). Threaded through all four
  places that resolve Noise fill's per-frame values (the regular Disc/Crescent/SparkleField path used by both
  the per-shape and single-shape-preview call sites, MetaBlob, and Fused blobs) via a new `F_NoiseWarp` field id,
  same pattern as the sibling fields. Verified: an animated Warp curve genuinely changes the rendered noise
  pattern frame to frame (checked against a black→white gradient, since Layer's own flat `WhiteGradient()` helper
  masks any noise-pattern difference behind a solid colour — a test-design trap, not a code bug); all three
  colour paths (Disc/Crescent, MetaBlob, Fused) render without error with an animated/non-default Warp.
- **Pyre: `SimulationModifier` + `PixelFluidModifier` ("Pixel fluid")** — the genuinely ITERATIVE modifier the
- **Pyre: `SimulationModifier` + `PixelFluidModifier` ("Pixel fluid")** — the genuinely ITERATIVE modifier the
  Ballistic shockwave closed-form trick couldn't deliver ("this is the third try we do and it hasnt turned out
  well... can we build a modifier that is genuinely iterative — each frame's density/velocity grids depend on
  the previous frame's?"). `SimulationModifier` is a new, FOURTH modifier family living in its own dedicated
  single slot, `BlastSpec.simulationModifier` — deliberately NOT a list like Geometry/Pixel/Post modifiers —
  which trivially guarantees "only one" and "always applied last" by construction, with zero changes to any
  other modifier's behaviour. Its `EnsureFrame` checkpoint/replay scheme keeps this safe under Pyre's non-
  sequential access patterns (scrubbing, animated thumbnail sampling): a sequential next-frame request costs one
  cheap `Step()`; a same-frame re-request (e.g. a slider dragged mid-pause) restores a checkpoint and re-steps;
  any other jump — including the very first-ever call — does a full, deterministic replay from frame 0,
  re-resolving each replayed frame's OWN animated parameters (not just whatever the caller most recently asked
  for) so a cold scrub straight to frame 40 reproduces bit-for-bit the same state a sequential 0→40 run would
  have. The real bake (`BlastRenderer.RenderSheet`, confirmed strictly sequential with no threading/reordering)
  always hits the cheap path.
  `PixelFluidModifier` itself keeps REAL persisted state — a velocity displacement field and an alpha-erosion
  field, both sized to the canvas — modelled on a reference Python `PixelFluidSimulation` (projectile tunnel +
  trailing shockwave rings + an alternating vortex street advecting density through velocity). Unlike Ballistic
  shockwave's age-derived closed form, a second wave crossing an already-eroded patch genuinely digs it deeper,
  and a vortex's drift is a real integrated position, not re-derived from how old it is. Vortices can shed a
  smaller child of their own (the reference's stochastic branching), safe here because the randomness comes
  from `FluidRandom`, a tiny explicit-`ulong`-state PRNG that checkpoints/restores like every other field
  (`System.Random` can't be rewound mid-stream, which a checkpoint-restore needs). Verified: state genuinely
  persists and displaces/erodes across sequential frames; a same-frame redo reproduces the original result; a
  cold jump straight to frame 10 on a fresh instance matches a sequential 0→10 run byte-for-byte; jumping
  backward mid-run then forward again still reproduces the original sequential result; a freshly-`Clone()`d
  instance (for asset "Dup") renders untouched until it's actually stepped, rather than sharing the original's
  live simulation arrays.
- **Pyre: Noise fill gained a "Band softness" slider** (reported: "the shapes jump too harshly between the
  stages of the gradient fill"). At 0 (default) it's byte-identical to the existing hard
  `Floor(noise×Bands)/(Bands−1)` step; higher crossfades that hard step toward the fully continuous noise value
  (1 = same look as Bands=1), so a small amount keeps recognizable shading bands without the harsh pop between
  them. Verified: softness=0 matches the original formula exactly across sampled inputs; softness=1 reproduces
  the raw continuous value; intermediate softness falls strictly between the hard and raw values.

### Changed
- **Pyre: `DissolveModifier` — removed Fade and Bleed modes, added a Smoothness slider for Erase/Scatter**
  (reported: "Fade and bleed mode... not useful"). Converted from a per-pixel `PixelModifier` to a `PostModifier`
  specifically so Smoothness can see real NEIGHBOUR pixels, which a per-pixel effect has no way to do. At
  Smoothness 0 it's unchanged — the same hard random holes/churn as before. Above 0 it softens two ways: a pixel
  that just crossed the removal threshold doesn't vanish outright, fading out over several SUBSEQUENT frames as
  Amount keeps rising past its own threshold (closed-form — no persisted state needed, since Amount itself
  already changes frame to frame); and a still-solid pixel next to an already-hollowed one bleeds some of its
  own alpha toward it, so growing holes spread/soften into their surround instead of popping in as hard single-
  pixel speckle. `PostModifier` gained `SetSeed`/`SetFrameIndex` hooks (mirroring its existing `SetLife`) so
  Dissolve can hash per-pixel without `PixelInfo.hash`, which only per-pixel Geometry/PixelModifiers get.
  Verified: Smoothness=0 still produces only fully-kept-or-fully-removed pixels (no partial alpha, matching the
  old behaviour exactly); Smoothness>0 produces genuine partial-alpha edge pixels.
- **Pyre: Dissolve's Add-modifier entry now differs by context** (organizing the split with the new
  `LayerDissolveModifier` above so it's unambiguous which one a project is using): the GLOBAL modifiers list
  still offers plain "Alpha/Dissolve" (screen-space, full Smoothness) since there's no single shape/geometry
  stack to be "local" to across a whole composited frame; a LAYER's own list instead offers "Alpha/Layer dissolve
  (follows this layer's own geometry warps)". `ShowAddModifierMenu`/`DrawModifiers` gained an `isGlobal` flag to
  pick the right one.

### Fixed
- **Pyre: `EdgeSmoothModifier` mostly smoothed the WHOLE shape, not just its edges** (reported: "even on the
  lowest setting... the result is all over the shape"). Root cause: it blurred and blended R/G/B/A everywhere,
  unconditionally — any shape with a gradient/noise fill has real pixel-to-pixel colour variation all the way
  through its own interior, and that got blended too, not just the outer alpha transition. Fixed to be genuinely
  edge-aware: a pixel that already has meaningful alpha now keeps its EXACT original colour always (only alpha
  itself may soften); only pixels near the true boundary (low/near-zero original alpha) borrow a blurred colour
  from the premultiplied blur average (still needed there to avoid a black fringe). Verified: deep-interior
  pixels (with their own gradient) come out byte-for-byte identical; the outer edge still softens as expected.
- **Pyre: `SunburstModifier` and `PulseRingsModifier` only tinted colour, doing nothing to geometry** (reported,
  same complaint for both: "just a dark pattern overlaid" / "just dark semitransparent rings" — what was wanted
  was the rays/rings actually pushing pixels). Both converted from `PixelModifier` to `GeometryModifier`:
  - Sunburst now reuses Jagg's own radial-scale mechanism (a ray line pushes the silhouette further OUT, the
    gap between two rays pulls it further IN) with its existing Rays/Sharpness/Rotation fields unchanged in
    meaning — a sharper-edged sibling to Jagg's own rounded star points.
  - Pulse rings now pushes pixels radially (same push mechanism as Ring wave) but keeps its own distinct
    authoring feel: ring count is relative to the SHAPE'S OWN RADIUS (like the old colour version's crossFrac),
    not a fixed pixel wavelength, so it isn't a pure duplicate of Ring wave. Strength is now a pixel push
    distance rather than a 0..1 colour delta (default changed from 0.5 to 3px accordingly).
  - Both moved from the Colour menu category to Geometry to match.

### Added
- **Pyre: `BallisticShockwaveModifier` ("Ballistic shockwave")** — a richer sibling to Cloud projectile, modelled
  on a reference pixel-fluid simulation (projectile tunnel + trailing pressure rings + an alternating vortex
  street, advecting density through a velocity field). The reference is a genuine ITERATIVE simulation — each
  frame's density/velocity depend on the previous frame's — which doesn't fit Pyre's bake-any-frame-
  independently model directly, so every emitter here is re-expressed as a CLOSED-FORM function of "how far
  past its own spawn point the projectile now is" instead of an iterated accumulation: a shockwave/vortex
  spawns every Spacing (a fraction of the whole travel); if the current Depth hasn't reached its own spawn
  point yet, it simply doesn't exist this frame; its age (Depth − its own spawn Depth) alone gives its current
  radius and strength (strength = Strength × exp(−Decay × age) — the reference's per-frame `persistence **
  (dt×60)` accumulation IS this same exponential decay, just re-derived in closed form against elapsed age
  rather than iterated frame by frame). Per-vortex jitter (position offset, drift, radius variance) is drawn
  from a deterministic hash keyed on the vortex's own index instead of the reference's real RNG, so the same
  frame always renders identically (required for a baked, scrubbable timeline). Spin alternates by index parity
  (+1/−1/+1/−1...), reproducing the reference's alternating vortex street exactly. The reference's stochastic
  child-vortex shedding (a per-frame random chance, an unbounded/unpredictable branching tree) is the one piece
  left out — the least closed-form-friendly part of the simulation; everything else carries over. Like Cloud
  projectile, resamples directly from a snapshot of the already-rendered frame (the pixel data IS the cloud)
  rather than advecting a separately-simulated grid. Verified: identity at Depth 0; real displacement at Depth
  0.5; the tunnel's own erosion measurably reduces alpha at its core while leaving distant pixels untouched;
  active wave/vortex counts match the closed-form spawn math; vortex spin alternates correctly. Kept alongside
  the simpler Cloud projectile rather than replacing it.

### Fixed
- **Pyre: two Add-modifier menu entries were silently buried under spurious nested submenus** ("Edge warp
  (jagged/wavy silhouette only)" and "Blast (disc/arc/line)") — Unity's `GenericMenu` splits on EVERY "/" in a
  label to build submenu nesting, not just the first one separating the category, so the "/" inside their own
  descriptive text created extra unintended submenu levels (Geometry → "Edge warp (jagged" → "wavy silhouette
  only)"; Geometry → "Blast (disc" → "arc" → "line)") instead of a flat item under Geometry like every other
  modifier. Audited every Add-modifier label for the same mistake (none of the other 34 have a second "/") and
  reworded these two with commas instead.

### Removed
- **Pyre: "X" (formerly LineForceModifier) and `PiercingModifier` removed entirely** — neither delivered the
  piercing feel asked for. Also removed the `PierceMode` enum and the `IExtraPost` interface (X was its only
  implementer), and both of its dispatch sites in `BlastRenderer`.

### Added
- **Pyre: `SunburstWobbleModifier` ("Sunburst wobble")** — Wobble's radial sibling: instead of a fixed
  horizontal ripple keyed to vertical position, this pushes pixels outward/inward along the ray from the
  shape's own centre, with the push keyed to ANGLE around that centre — the same sin(x×freq+phase) idiom
  Wobble uses, just walking the circumference instead of up the canvas. Reads as wavy "sunbeams" that reach
  further out or pull further in than their neighbours, and phase (free, same as Wobble) sweeps/pulses the
  whole pattern over life. Verified: identity at Amplitude 0; displacement bounded within the amplitude range
  once sampled at angles that don't alias onto the sine's own zero-crossings.
- **Pyre: `PointBlastModifier` ("Blast")** — a single expanding shockwave from an arbitrary origin point, with
  one continuous Arc setting spanning disc → wedge → line: 360° = a full circular explosion in every direction
  at once; a smaller arc narrows it to a wedge that widens with distance from the origin, like a real blast
  cone; 0° collapses to a straight, CONSTANT-width rod along Angle (a directional punch-through, like a
  bullet's exit force) instead of vanishing to nothing the way a literal zero-width wedge test would — the
  wedge's own half-angle is floored at whatever angle a fixed-width rod would subtend at that distance, so the
  0° limit is exactly a rod, not a special-cased branch. Radius (animate it rising over life) is how far the
  front has currently travelled; the push is strongest right at Radius and fades over Band width either side —
  one sweeping front, not a repeating ripple (that's RingWave). Found and fixed a real bug during verification:
  at Arc 360°, the point exactly OPPOSITE Angle sat precisely at the arc-softness math's edge-feathering
  boundary and got silently zeroed, even though a full circle has no edge to feather at all — skip the
  feathering entirely when Arc is a full 360°. Verified post-fix: all 12 sampled directions around a full
  circle (including that exact opposite point) show equal full-strength push; line mode correctly isolates to
  the rod's own axis.
- **Pyre: `CloudProjectileModifier` ("Cloud projectile")** — a projectile tunnelling through the ALREADY-
  RENDERED frame as if it were a cloud of some density: the pixel data itself (alpha) IS the cloud, not a
  separately-authored field, so a dense/opaque region genuinely resists the shot more than empty space. Marches
  the travel line from the canvas edge to its current Depth each frame, integrating the cloud's own alpha along
  the way (Beer-Lambert-style absorption — remaining force = Strength × exp(-integrated density × Density)),
  then resamples from a pre-modifier snapshot so pixels near the path get their content pulled sideways using
  the cloud's OWN pixels. Necessarily a whole-frame Post effect, not a per-shape geometry warp — only a Post
  pass sees finished pixels to read density from at all. Verified: Strength 0 leaves the frame untouched; a
  dense medium measurably absorbs more force than a thin one, showing less displacement by the far side of the
  travel (required a monotonic test gradient rather than a periodic stripe/checkerboard pattern, which aliases
  unpredictably against periodic displacement amounts and gave misleading readings at first).
- **Pyre: `PerlinTurbulenceModifier` ("Perlin turbulence")** — a sibling to Turbulence using genuine 2D GRADIENT
  noise (`PyreNoise.SampleGradient`: interpolated random direction vectors dotted with the offset to each
  lattice corner, plus a quintic fade) instead of Turbulence's own bilinear VALUE noise (raw random values
  interpolated with a cubic fade). Value noise's hills sit visibly centred ON each lattice point, which is what
  gives it a faintly blobby/grid-aligned look at low octave counts (reported: "I get the feeling it could be
  sharper/smoother") — gradient noise doesn't have that bias. Added as a separate, opt-in modifier rather than
  swapping the shared sampler in place, since Turbulence/Curl/Noise fill/AlphaMask's Noise shape are ALL built
  on the same `PyreNoise.Sample`, and changing it under them would silently reshape every asset already using
  any of those. Same fields as Turbulence (Amplitude/Zoom/Rotation/Offset/Warp), Warp included, so the two can
  be compared side by side. Verified: genuinely different output from Turbulence's own noise (differs on ~80%
  of sampled points), zero displacement at Amplitude 0.
- **Pyre: Turbulence's own "Warp" promoted from a plain float to MultiCont** (now animatable, matching every
  other Turbulence field) — same change applied to Perlin turbulence's own Warp above.
- **Pyre: `EdgeSmoothModifier` ("Edge smooth")** — a standalone Post modifier that softens jagged/torn
  silhouette edges (Radius + Strength, both animatable), for when Turbulence/Perlin turbulence's own Warp is
  pushed high enough to fold the noise field over itself and carve sharp notches into what should be a smooth
  edge. Not specific to Turbulence — works on any jagged-edge source (Jagg, EdgeWarp, a wild Wobble), so it's
  a separate opt-in modifier rather than baked into Turbulence itself. Blurs ONLY alpha, done in PREMULTIPLIED
  space specifically to avoid the classic "black fringe" bug a naive straight-alpha blur produces (a pixel
  rising from fully transparent would otherwise blend in whatever arbitrary colour it happened to hold) —
  verified: a pixel just past a hard red/transparent edge comes out red-tinted at partial alpha after blurring,
  not black.
- **Pyre: Noise fill gained a "Gradient" group (Position + a new Zoom)**, mirroring Fill/Flow fill's own
  Gradient group exactly. Zoom (default 1 = unchanged) scales the noise value onto the gradient before Position
  shifts it — higher repeats the gradient several times through the SAME noise pattern (tighter, more numerous
  colour bands); lower compresses it into a narrower slice. Threaded through all three Noise fill paths (Disc/
  Crescent/SparkleField, MetaBlob, Fused Disc), same as Position. Both wrap via the SAME mirror (0→1→0) trick
  FlowingFill already uses, not a plain wrap — a gradient's own start/end colours are rarely identical, so
  wrapping straight into another 0 would jump between them every cycle at Position/Zoom values that cross a
  period boundary, precisely where a smooth result matters most. Verified: at the exact wrap point, a plain
  wrap jumps the whole way across a white→orange gradient in one step; mirrored, the colour stays continuous
  through it. Also verified zoom 1→4 visibly recolours ~76% of the shape's pixels (the feature itself works).
- **Pyre: relabeled Noise fill's field group** to match the "Gradient" group's own convention — a "Noise"
  group title over Zoom/Rotation/Drift/Warp (each label had its own redundant "Noise " prefix before: "Noise
  zoom", "Noise rotation", etc., now just "Zoom", "Rotation", "Drift", "Warp" under the one group title).
- **Pyre: `VoronoiCrackModifier` gained "Seam sharpness"** (reported: "I get the feeling I can't get a totally
  clean crack seam that doesn't bleed over... Perhaps spread softness is not working?"). Diagnosis: Spread
  softness was working exactly as built, but it's a different thing entirely — it only softens the TEMPORAL
  spreading-reveal front, and only in the three non-Uniform Spread modes (there's no "front" in Uniform to
  soften, which is why the control disappears there — not a bug). What was actually missing: a way to control
  how HARD any one seam's own edge looks. Crack width previously conflated "how wide the tinted band is" with
  "how it fades across that width" — it was always a plain linear ramp, at any width, with no way to make it a
  crisp line instead of a soft gradient. New Seam sharpness (default 1 = that same unchanged linear fade)
  reshapes the SAME band into a harder step (higher = a clean seam with no bleed into the cell interior) or a
  softer one (lower), independent of Crack width and working in every Spread mode including Uniform — verified
  numerically: raising it from 1 to 6 cut the "still bleeding" pixel fraction by roughly half.

### Fixed
- **Pyre: `EdgeWarpModifier` ("Edge warp") silently did almost nothing** (reported: "doesn't seem to do
  anything"). Root cause: Disc/Crescent/SparkleField's own outer-softness alpha fade computed its "distance
  from centre, 0..1" fraction against the shape's TRUE, un-perturbed radius — so any pixel the edge warp bulged
  OUTWARD past that true radius got immediately zeroed back to fully transparent by that very next fade,
  regardless of how far outerRadius itself had bulged. Only faint inward dents (already near-zero alpha that
  close to the true edge anyway) ever had a chance of showing at all. Fixed by measuring that fraction against
  the (possibly edge-warped) outerRadius instead — a no-op when no Edge modifier is active (outerRadius ==
  radius then), so every other shape's fade is unaffected. Verified: a bulged direction now correctly fades
  alpha well past the true radius instead of hard-cutting at it.
- **Pyre: `SphereModifier`'s negative range ("Strength" -1..0) read as almost nothing but a plain size-scale**
  (reported: "not useful"). Root cause: the negative branch reused the SAME convex ratio (rSphere/r) as the
  positive/fisheye side, just with a negative exponent — which only inverts that ratio's own narrow (2/π, 1]
  range, so any modest negative Strength stayed close to identity (at -1, the shape only varied ~22%
  corner-to-corner, vs +1's ~82%) — nowhere near a comparable mirror, just a mild overall size-up. Replaced with
  the ACTUAL inverse function of the positive side's projection (sin(r·π/2) instead of asin(r)/(π/2)) — a
  genuine concave dimple with the same order of dynamic range as the convex side, so -1 is now as
  strong/characterful as +1, just pushed the other way (verified: -1 now varies ~38% corner-to-corner). 0..1
  (the fisheye side the user already found useful) is completely unchanged; widened the slider to -5..5 too so
  the exaggerated end of the mirrored range is reachable.

### Added
- **Pyre: Noise fill gained a Gradient position field** (MultiCont, 0..1, wraps) — shifts which part of the
  colour gradient the noise field maps to, independent of the noise pattern's own drift/zoom/rotation. Animate
  it and the gradient sweeps through the noise like an ember/glow effect, the same pattern staying put while
  its colouring cycles. Threaded through all three Noise fill paths (per-shape Disc/Crescent/SparkleField,
  MetaBlob, and Fused Disc) so it works identically everywhere Noise fill does. Verified: pos 0→0.5 visibly
  recolours ~80% of the shape's pixels, and pos 1.0 wraps back to an exact match with pos 0.
- **Pyre: `SphereModifier` gained Origin (a 2D pad, px offset from the shape's own centre) and Radius (px, 0 =
  auto/matches the shape's own radius, unchanged from before this field existed)** — the fisheye/dimple lens no
  longer has to be dead-centre or exactly the shape's own size; a smaller Radius makes a tight bubble, a larger
  one lets it extend past the shape's own edge. Both animatable (MultiCont). Verified the lens's own edge stays
  correctly anchored (zero displacement) and everything beyond it is untouched, same invariant as before.
- **Pyre: `PiercingModifier` ("Piercing")** — a from-scratch second attempt at a piercing rod, replacing the
  never-quite-right feel of the old LineForceModifier (renamed to "X" below, kept as-is). Params match the
  requested design directly: **Angle** (the rod's direction — it has an implicit Start/End at the canvas edges
  along that angle), **Depth** (0..1 — how far the tip has travelled, 0 = hasn't entered, 1 = all the way
  across; a MultiCont curve, so travel can ease, hold, or reverse), **Length** (0..1 — how much of the tip's
  OWN travelled distance still has solid rod behind it: 0 = a bare point/bullet, 1 = a solid rod filling the
  whole distance from Start to the tip), **Offset** (px, slides the rod's line sideways off-centre), plus
  **Radius**/**Strength** (the push's reach and magnitude) and **Ripple memory** (how long, in the same 0..1
  units as Depth, a past instant's push keeps contributing before fading).
  - The key behavioral difference from "X": this one gives the push MEMORY. Area the tip passed through EARLY
    keeps accumulating push from the shockwaves the advancing rod keeps throwing off, ending up more displaced
    by the end than area the tip only just reached — Ripple memory controls how long that accumulation window
    is (small ≈ a plain instantaneous push, no build-up; large ≈ the whole traversal keeps contributing, even
    continuing to settle after the rod stops or exits).
  - Still fully re-derivable from THIS frame's own progress alone — no cross-frame state, matching Pyre's
    fully-baked model. The "memory" is a numerical integral over Depth's own curve, re-sampled backward from
    the current phase at a fixed small step (auto-scaled to Ripple memory, capped at 128 steps) every frame,
    weighted by a proper exponential-decay density so a point's dose ramps up smoothly from zero the moment the
    tip reaches it (rather than jumping to a large fraction immediately) and asymptotes toward a stable max
    regardless of Ripple memory's own value.
  - Does NOT include "X"'s actual alpha-carve/gap toggle (ShapeAware/Always Pierce) — this one is push-only for
    now; ask if a genuine divide/gap should be added on top.

### Changed
- **Pyre: `LineForceModifier` renamed to "X"** (a temporary placeholder label) — its own Pierce toggle never
  quite delivered a convincing piercing feel, so `PiercingModifier` (above) is a fresh attempt at that concept
  under the freed-up name. Nothing about X's own behavior changed; it's a genuinely useful stretch/burst effect
  in its own right and stays available (Geometry menu, labeled "X (was: line force / piercing rod)" for now so
  it's easy to find while it's between names).
- **Pyre: Save/Recall for the preview background moved from "Test background (sprites)" up to "Preview
  backdrop"**, and collapsed from three actions (Recall, New, a separate bottom Save + name field) into two
  (Recall, and a single ★ — same quick-save affordance the layer list already uses). ★ does what New+Save used
  to do in two steps: seeds a fresh asset from whatever's currently showing if nothing's loaded yet, otherwise
  just re-saves in place — one action that saves the WHOLE preset (this box's mode/colour/gradient/image AND
  Test background's sprites together), not two separate save flows for what was always one recallable unit.

### Fixed
- **`ZUI.SliderStacked` silently rounded plain 0..1 float fields to just 0 or 1** — RoseRing's Radius and Birth
  (both a real 0..1 fraction) were affected (reported: "Its either 0 or 1"). Root cause: the numeric field's
  int-vs-float display was GUESSED from the range's shape (`isInt = (max-min)>=1 && both whole numbers`), which
  can't tell a genuine integer range (Count, 1..60) apart from a plain 0..1 fraction (same shape, both ends
  whole). `isInt` is now an explicit opt-in parameter instead of a guess — RoseRing's Count and the Style
  Editor's corner-radius slider (same shape-guess bug, silently already present there too) now request it
  explicitly; every 0..1-shaped float field (Radius, Birth, and any other caller) is continuous again.
- **Pyre: the left pane's scroll view had no real width ceiling**, so ANY child using `ExpandWidth(true)` (a
  plain full-width `ValRow`/`Slider`, the "+ Add modifier"/Paste buttons, the layer-name field) sized itself
  against the scroll view's own virtual content width — which could silently balloon past the visible pane the
  moment one row demanded more room, forcing a horizontal scrollbar and pushing other rows' trailing controls
  (a numeric value box, a button) out of view. This is the same family of bug as the packed-row overflow fixed
  below, just for UN-packed, single, full-width controls — reported as "sliders hundreds of pixels wide... the
  numeric input box gets cut off." Wrapped the whole scroll view's content in one `GUILayout.MaxWidth` instead
  of chasing every leaf control individually — a real ceiling for the whole subtree, not a hint any one control
  could ignore.
- **Pyre: the previous entry's `PaneRowBudget` (760px flat guess) was itself the bug — it overflowed the real
  left pane, forcing a horizontal scrollbar and clipping controls off past the edge** (reported: the Gradient
  row's Offset field pushed off-view, "+ Add modifier"/"Paste" needing to scroll to reach Paste at all) **even
  after widening the pane well past half the screen.** Root causes, all fixed:
  - `PaneRowBudget` now reads the pane's real, live `leftWidth` (the actual resizable splitter width) instead
    of a static guess, and `GroupFieldWidth` no longer floors a field's width ABOVE what that real budget can
    support — the floor was exactly what forced overflow on crowded rows (RoseRing's 5 fields demanded
    5×200px regardless of how much pane was actually there).
  - The Gradient row's Position/Zoom/Offset undercounted itself (`GroupFieldWidth(2)` for 3 fields sharing the
    row), and Offset (a 2D position control) was never width-capped at all — its internal thumbnail uses
    `ExpandWidth(true)` and, left unconstrained, greedily filled whatever was left. New `CompactValue2DRow`
    helper caps it the same way `CompactValRow` already caps a plain ValRow; applied to every 2D control that
    shares a packed row (Gradient's Offset, Position+Spin, Noise drift+Noise warp).
  - "+ Add modifier"/"Paste" had no width cap and no trailing FlexibleSpace, so the button style's default
    stretch inflated "+ Add modifier" to match whatever width other (now-overflowing) rows had already forced
    the scroll view to — pushing Paste out of view. Both now `ExpandWidth(false)` with a FlexibleSpace after.
- **Pyre: still-missing vertical space** between the Seed/Frame count/Bake background row and the Origin row
  right below it (both packed rows, but nothing separated the two groups) — added the same `VerticalSpace(2f)`
  used at every other group boundary this pass.
- **Pyre: Ring expand + Align rotation** — Align rotation is just a checkbox, so it now shares Ring expand's row
  (same pattern as Outer softness/Hollow) instead of the checkbox getting a whole row of empty space to itself.
- **Pyre: single-value modifiers (Skew, Contrast, Brightness, Saturation, Sphere, Ordered dither) no longer draw
  a separate labelled row for their one slider** — the header row already names the modifier ("Contrast"), so a
  second row repeating that label ("Contrast" again, next to a slider stretched across the whole pane) was pure
  redundancy on top of being needlessly wide for one number. Now drawn inline in the header row itself
  (unlabeled, compactly sized) — one row per modifier instead of two.
- **ZUIValueControl's "live: X" readout was noise in Pyre.** It evaluates the field against real wall-clock time
  (`Time.time`/`EditorApplication.timeSinceStartup`) — meaningful for a value genuinely driven by elapsed
  gameplay time, meaningless for Pyre, which evaluates the same ZUIValue against its own BAKED frame/life-phase
  instead (reported: "Why does it say Live:2?" — an arbitrary number with no relation to the frame being
  previewed). New `Options.WithoutLiveReadout()` on the shared control (default off, so Zhowcase's own demo of
  the feature is unaffected); Pyre's `ValRow` wrapper now sets it.
- **New `ZUI.ZTextStyle.GroupTitle`** — for a label that titles a small in-body group of related controls (e.g.
  "Gradient" over Position/Zoom/Offset), one step bigger/bolder than the ad hoc `EditorStyles.miniBoldLabel`
  these used before, without competing with a full `SectionHeader` ("Blast", "Modifiers"). Applied to both of
  Pyre's "Gradient" group labels.

### Added
- **Pyre: `CurlProgressModifier` ("Vortex field (progress)")** — a standalone sibling to Curl's vortices, added
  non-invasively (CurlModifier itself is untouched) so the two driving models can be compared directly: instead
  of a vortex's rotation accumulating as Strength × life-phase × Speed, here it's Strength × Progress, where
  Progress is authored as its own MultiCont value (0 = no rotation, 1 = Strength fully applied) with no
  automatic tie to how far into its life the blast is — a Curve can ease in, hold, pulse, or reverse
  independent of life fraction, which Speed alone can't do cleanly. Shares CurlModifier's click-to-place/drag/
  gizmo authoring via a new `IVortexHost` interface (`VortexPoint` gained a `progress` field alongside the
  existing `speed`, ignored by the other's mode). No ambient churn — pair with Curl's own ambient Strength/Zoom
  if wanted. Add via Geometry/Vortex field (progress).
- **Pyre: a real, tunable width policy for packed control rows, instead of one narrow flat guess.** The earlier
  `CompactValRowWidth` (200px) badly under-used the pane's actual space — reported via screenshots showing
  RoseRing's 5-field rows and the Count/Position/Size rows each filling well under half their row, the rest
  sitting empty. New `PaneRowBudget` (760px, the sizing TARGET this whole left pane realistically gets — see
  `EDITOR_TOOL_CONVENTIONS.md`, a static assumption, not a live measurement) and `GroupFieldWidth(fieldCount)`
  divide that budget evenly across however many fields share one row (floored at `CompactValRowWidth` so a
  crowded 5-field row never goes unusably narrow) — applied across every packed group from the earlier
  regrouping pass (Gradient, Count+Spawn radius, Position+Spin, Size+Alpha, the whole Bars box, MetaBlob's
  fields, Disc/SparkleField/Crescent's edge controls, Noise fill, Curl's ambient+vortex rows) plus the new
  ones below. If it's still off for your own window size, `PaneRowBudget` is the one constant to tune — same
  idea as ZUI's own `verticalSpacing`.
- **Pyre: RoseRing rows switched from `ZUI.StackedInt`/`StackedFloat` (a hidden drag-the-label gesture, no
  visible track) to `ZUI.SliderStacked`** (same label-on-top look, kept as asked, but with an actual draggable
  slider underneath) — a real slider is more discoverable than a gesture you have to already know about. Each
  of the 5 fields (Count/Radius/Size/Birth/Life) now gets its fair share of the row via `GroupFieldWidth(5)`
  instead of a flat ~40-44px regardless of available space, and rings now have a visible gap between them
  (`VerticalSpace(2f)`, was none).
- **`ZUI.SliderStacked` gains a `widthOverride` parameter** (`ZUISlider.cs`) — previously its width was ENTIRELY
  auto-computed from the label text + a fixed ~40px value field, silently ignoring its own `options` parameter
  (dead code — accepted a width but never used it), so there was no way to widen it for a packed multi-field
  row. `widthOverride = 0` (default) reproduces the exact old auto-sizing for every existing caller; `> 0`
  overrides both the value-field column and the track below it. `ZUIWindow.Draw.cs`'s forwarding wrapper
  updated to match.
- **Pyre: blast-level settings regrouped the same way.** Width/Height/PPU now share one row (compact typed
  fields — these are exact sizes you type, not drag, so no slider needed) instead of Width+Height sharing a
  row while Pixels-per-unit sat alone on a full-width-stretched field. Seed/Frame count/Bake background now
  share a row (was: full-width IntField, full-width Slider, full-width ColorField, each its own row). Origin
  X/Y converted from two separate full-width sliders to a `ZUI.PositionPad` (matching Position/Crescent
  offset/Hole offset elsewhere), sharing a row with Origin marker alpha and the "Origin → centre" button.
  "Bake background" gained an inline tooltip clarifying it's the actual pixel colour baked into every EXPORTED
  frame (usually fully transparent) — distinct from "Preview backdrop" further down, which is cosmetic-only
  and never baked, a real point of confusion between two similarly-named but different things.

### Fixed
- **Pyre: the preview transport row's "frame X/XX" counter had a fixed 70px width that was right at the edge
  of what its own worst-case text ("frame 64/64") needs — wrapping/reflowing depending on exact digit count
  and font metrics, changing the row's own height frame-to-frame during playback as the counter ticked over a
  digit boundary. Reported as the whole preview flickering. It's the last element on its row with nothing
  after it, so removed the fixed width entirely (`GUILayout.ExpandWidth(false)`) — auto-sizes to its own
  content, the same safety `ZUI.FitWidth` already relies on elsewhere for varying-length text.
- **Pyre: `SphereModifier` broke down (reported: "fills the whole frame") once Strength climbed past 1.** The
  old extrapolation (`Mathf.LerpUnclamped(r, rSphere, amt)`) kept applying the SAME fixed per-radius delta
  harder as `amt` grew — a runaway, not a hard crash: at Strength 1 ALONE, half-radius already sampled from a
  third of the radius; by 2 it sampled from a sixth. Most of the disc's own AREA collapsed toward a tiny
  central patch well before any actual sign flip, so it read as "the shape just fills with one solid colour"
  once that patch's colour dominated. Replaced with `scale = Mathf.Pow(rSphere/r, amt)` — exponentiating the
  RATIO instead of linearly extrapolating the RADIUS: identical result at Strength 1 (unchanged), true identity
  at 0, and — since `Mathf.Pow` of a positive base can never go negative or flip sign — permanently well-behaved
  at any Strength, verified from -3 to 50 with no break, still matching the exact old values at Strength 1.
- **Pyre: dangling "armed for editing" modifier references (`editPin`/`editCurl`/`paintSmudge`) could leave
  stale gizmos/strokes stuck in the preview with no way to clear them.** Each was only ever nulled by the
  modifier list's own "X" remove button — deleting the whole LAYER that owned the armed modifier, an Undo that
  removed it, or switching to a different asset entirely all left the reference dangling: the C# object stays
  alive (still referenced by the window), so its vortex markers / painted strokes kept rendering indefinitely
  (reported: a Curl vortex circle + "1 CCW" label still visible after removing that Curl modifier, alongside
  unrelated leftover Smudge doodles). New `ValidateArmedModifiers()`/`ModifierStillExists()`, called once at
  the top of `DrawAsset` every frame, self-heals all three by checking whether the referenced modifier is still
  present anywhere in the CURRENT asset (any layer's list, or global) — covers every removal path at once
  instead of patching each one individually, and naturally also clears on an asset switch (the old asset's
  modifiers can never match the new asset's lists).
- **Pyre: `Mathf.Lerp` silently clamps its blend factor to [0,1]**, unlike `LerpUnclamped` — this was quietly
  breaking every "beyond 1 exaggerates further" / "negative inverts/pulls the other way" behaviour already
  advertised in tooltips: `SphereModifier`'s Strength (beyond 1 fisheye exaggeration, negative concave dimple)
  and `LineForceModifier`'s Strength × Tip-strength blend (the new tip-vs-body force differentiation was
  completely inert whenever the combined multiplier reached 1, which it does by design at the tip) were both
  no-ops past their [0,1] range. Caught by direct verification (a tip-strength=3 test showed IDENTICAL
  displacement at the tip and far behind it — the smoking gun) before either shipped in a commit. Both switched
  to `Mathf.LerpUnclamped`; re-verified: tip displacement now measures notably larger (7.65 vs 2.55) than the
  already-pierced shaft, as intended.

### Added
- **Pyre: `LineForceModifier` — Reach/Length/Tip strength model the rod as a genuinely finite, animatable
  segment** instead of an infinite always-fully-present line. `Reach` (animatable) is where the leading tip
  currently sits along the rod's own axis — ahead of it, nothing has happened yet at all; animate it from well
  before the shape to well past it for a real stabbing-through motion. `Length` bounds how far back from the
  tip the rod's own body still reaches (a short stub vs. a long spear — previously unbounded/always-infinite).
  `Tip strength`/`Tip falloff` make the leading tip hit harder than the already-pierced shaft trailing behind
  it (1 = uniform, the old behaviour; higher = a genuine puncture point). Defaults (Reach/Length generously
  large, Tip strength 1) reproduce the original behaviour with nothing new touched. Verified via direct
  `InverseWarp` checks: ahead-of-tip points stay exactly unchanged, points beyond Length are excluded, and
  points within Length correctly stretch.
- **Pyre: `LineForceModifier` gains `Star` mode — an explosion of N piercing rods radiating from a shared
  centre**, mirroring Bars' own Star/ArmCount/ArmSpread exactly. Every arm shares the same Radius/Strength/
  Pierce/Reach/Length settings, summed per pixel like PinWarp sums its pins' displacements (not one arm
  overwriting another) — so Reach animates the whole burst's shrapnel-expanding-outward motion for free.
  `Offset` (the single-rod perpendicular slide) is ignored in Star mode — arms share `ctx.centre` exactly,
  since sliding it per-arm would pull the arms apart rather than move the whole burst.
- **Pyre: `CurlModifier`'s vortex parameters (Radius/Strength/Speed) are now full MultiConts (`ZUIValue`)**
  instead of plain floats, so each vortex can be independently animated (Static/MinMax/Curve), not just a
  fixed placement. `CurlModifier.Prepare()` resolves every vortex's ZUIValues once per frame into a cached
  `ResolvedVortex` list (Eval's curve-sampling/hashing is too costly to redo per-pixel) using a small
  wrapped field-id range (this modifier's id budget within `BlastRenderer`'s per-modifier spacing is only 8
  wide, and a vortex list is unbounded) — Static/Curve modes (the common case) are fully independent per
  vortex regardless; only MinMax's per-field random roll could rarely coincide between two vortices sharing
  a wrapped id, a minor accepted tradeoff. Verified: two vortices with different static Radius/Strength values
  each produce independently-correct rotation with no cross-contamination. Vortex `Strength`'s default and
  slider range rebalanced (default 25, range 0–50, was 60 default / 0–360 range) per hands-on testing — below
  ~15° barely reads, above ~40° tends to tear rather than read as a tighter whirlpool.
- **Pyre: Curl's controls packed onto fewer rows**, mirroring the preview transport bar's Frame count/Zoom/
  Speed row — new `CompactValRow` helper (`ZUI.NarrowLabel` + a fixed-width column, several packed into one
  `ZUI.HRow()`) applied to the ambient-swirl box (Strength/Zoom/Speed/Warp, was 4 full-width rows, now one)
  and each vortex's own Radius/Strength/Speed (was spread across 2 rows with Radius alone eating the first;
  now the non-ZUIValue controls — select/id/CW-CCW/delete — share one row, and all three animatable fields
  share a second, all compact). Widened to a dedicated `CompactValRowWidth` (200px, was reusing the narrower
  150px `CompactSliderWidth` meant for plain-float `ZUI.MicroSlider` fields) — a `ValRow`/`ZUIValueControl`
  field reserves a fixed ~40px numeric value box (`ZUISliderDef.valueWidth`, not adaptive to the field's own
  min/max range — a real ZUI gap, not something to design around per call site) plus a 24px "⋯" config button
  on top of label + slider, so it needs more room than a plain `MicroSlider` field or the slider portion gets
  squeezed to nothing. New `CompactSlider` sibling helper for the handful of plain-float (non-`ZUIValue`)
  fields (`perShapeLifeJitter`, `spawnStagger`, `noiseWarp`, MetaBlob's Threshold/Shade range/Edge softness),
  same packing trick without the value-width/config-button overhead ValRow pays.
- **Pyre: the whole per-layer shape inspector re-grouped and packed onto far fewer rows**, applying the same
  `CompactValRow`/`CompactSlider` treatment throughout instead of just Curl: Gradient position/zoom/core
  offset (Disc/Crescent and MetaBlob) now share one labelled "Gradient" row; Count+Spawn radius; Ring/Rosing's
  Start angle+Arc degrees, and MetaBlob's Radius pulse+Expand and Threshold/Shade range/Edge softness (both the
  MetaBlob box's own copy and the Fuse box's); Position (2D pad)+Spin; Size+Alpha (Alpha now draws once,
  early, for Bars/MetaBlob specifically — since they never reach the scatter section where Size lives — and
  shares Size's row for every other shape, instead of drawing twice or Bars/MetaBlob losing Alpha); Life
  jitter+Spawn stagger; the whole Bars box (Bars per side+Width, Spacing+Edge softness, Forward reach+Backward
  frac, Taper+Stagger, Layer angle+Mirror, Base angle+Star, Arms+Spread degrees); Disc/SparkleField's Outer
  softness+Hollow and Hole size+Inner softness; SparkleField's Blob radius/life/softness; Crescent's Outer+Bite
  softness; Noise fill's Zoom+Rotation and Drift+Warp. `VerticalSpace()` between every group so the packing
  reads as distinct clusters, not one dense wall of controls. Design guidance recorded in
  `EDITOR_TOOL_CONVENTIONS.md`: assume the host panel realistically gets roughly HALF the screen's width as a
  sizing target, and don't shrink controls below what they need just because there's spare room — the
  no-infinite-width rule is about not stretching one control across an absurdly wide panel, not a mandate to
  minimize every field.
- **Pyre: `LineForceModifier` gains a `Pierce` toggle — the rod can now actually divide the shape, not just
  stretch texture around itself.** New `IExtraPost` interface (`PyreModifiers.cs`) lets one modifier serve a
  second role beyond its declared Geometry/Pixel base class — a whole-frame post pass — since GeometryModifier
  and PostModifier are separate base classes and a pure position warp has no way to touch alpha on its own.
  `NeedsPost` lets `BlastRenderer` skip the isolated-buffer/post-loop overhead entirely when an instance's own
  post behaviour is currently off (checked at the same three sites `is PostModifier` already is: the per-layer
  isolated-buffer gate, `FinishLayerPost`'s loop, and the global-modifier post pass). Two Pierce modes, one set
  of Angle/Offset/Radius/Pierce-width values driving both instead of needing two separately-kept-in-sync
  modifier instances: **Shape-aware** shoves the pierced core hard enough to exit the HOST shape's own radius
  — no new alpha code, just reusing the same silhouette test every GeometryModifier already feeds through, so
  the tear inherits that shape's own edge softness/contour (organic, but only works on a bounded shape —
  Disc/Crescent/MetaBlob). **Always** is a genuine alpha carve via `IExtraPost` on the finished pixels,
  shape-agnostic (Bars/Sprite/fused fields, or as a Global modifier tearing through several composited layers
  at once) — a cleaner, more mechanical cut. Pierce off (default) is unchanged: purely the stretch, an energy
  pushing texture aside, never an actual gap. Verified via direct math checks: a straight linear taper across
  the whole pierced core made the push too weak to actually clear the shape's radius partway through (a real
  bug caught before shipping — distance-from-centre landed under the shape's own radius at 60% through the
  core); fixed to a threshold shape (the inner ~70% gets the full guaranteed-exceed push, only the outer 30%
  eases out) — now the inner core reliably opens a gap (36.05/36.45px from centre against a 24px shape radius)
  while the outer edge blends smoothly back into the plain stretch. Always-mode carve confirmed on a raw
  buffer with no shape at all: alpha rises 7→127→247→255 moving out from the line to the core's own edge.
- **Pyre: `LineForceModifier` ("Line force (piercing rod)")** — a straight rod piercing through the shape,
  shoving texture to its sides. A genuinely new mechanism among Pyre's modifiers: LINE-based rather than
  point-based (`RingWaveModifier`) or a directional growth anchor (`GroundModifier`, not a perpendicular
  distortion of existing content). Content only moves PERPENDICULAR to the rod's own axis (Angle); the
  along-rod component is untouched. Same direct-inverse-remap approach as Sphere: sampling at output
  perpendicular distance `rOut` pulls from `rSample = rOut²/Radius` — 0 right at the line (a thin source band
  stretched wide, reading as "pushed clear of the rod"), rising back to meet `rOut` exactly at Radius (seamless
  blend into undisturbed texture). `Offset` slides the line sideways — animate it corner-to-corner for a rod
  that visibly travels THROUGH the shape over life rather than sitting static; negative Strength pulls inward
  (a suction trail) instead of pushing outward. Verified via direct `InverseWarp` checks: perpendicular
  distances 2/6/10/14/18/20 map to sampled 0.2/1.8/5.0/9.8/16.2/20.0 (exactly `t²·radius`, seamless at the
  radius boundary), the along-axis point is exactly unchanged, and ±y mirror with equal magnitude/opposite sign.
- **Pyre: `SphereModifier` ("Sphere (fake depth)")** — fakes volumetric depth on a flat shape via the standard
  "sphere impostor" projection: remaps the radial sample position as if painted on an orthographically-viewed
  sphere (r' = asin(r)/(π/2), the sphere's own surface arc-length from the pole instead of the flat screen
  radius) — magnified near the centre, increasingly compressed toward the silhouette, exactly like a real
  sphere's grazing-angle foreshortening. Radius always tracks the shape's own CURRENT radius (`ctx.radius`,
  including its animated Size), so the bulge stays sized to a growing/shrinking fireball automatically, no
  separate radius field to keep in sync. Composes with Curl (swirl the flow, then bulge it over the implied
  sphere). Verified: sampled radius is smaller than screen radius everywhere (centre magnification), and the
  per-equal-step compression increases monotonically toward the edge (0.064→0.130→0.139→0.160→0.219 across
  even screenR steps) — the intended fisheye/foreshortening curve, not a linear scale.
- **Pyre modifier menu re-categorised.** Five modifiers sat as bare, un-prefixed entries in the flat "+ Add
  modifier" menu (`EdgeWarpModifier`, `DissolveModifier`, `OrderedDitherModifier`, `VoronoiCrackModifier`,
  `AlphaMaskModifier`) while everything else was grouped under `Geometry/`/`Colour/`/`Post/` — inconsistent,
  and mixed in among the Colour submenu with no visual grouping. New **`Alpha/`** category (Dissolve, Ordered
  dither, Alpha mask) groups the "changes what's VISIBLE and where" family, distinct from Colour's "changes
  shading without touching alpha" family; `Voronoi crack` (fundamentally a pattern-driven recolour) joins
  `Colour/`; `Edge warp` joins `Geometry/` (technically its own base class, `EdgeModifier` — silhouette-only,
  doesn't touch fill — but that distinction lives in its label/tooltip, not a separate single-item submenu).
- **Pyre: `CurlModifier` ("Curl (swirl)")** — fakes coherent, fluid-like swirl (fireball roll, mushroom-cloud
  cap turn) without an actual fluid simulation, which would need per-frame ADVECTED state a poor fit for
  Pyre's fully deterministic "compute any frame directly" bake model. Two stackable layers: an ambient
  domain-warped **curl-noise** field (`PyreNoise.Curl` — the perpendicular of the existing `Sample()`
  field's own spatial gradient via finite differences; divergence-free, so it swirls without ever pulling
  toward/away from a point, unlike feeding the raw noise value in as a displacement) plus discrete,
  placeable **vortices** (`VortexPoint`: position/radius/strength°/speed/CW-CCW) — each a clean localized
  whirlpool, smoothstep-falloff to zero at its own radius, continuously rotating via the shared per-frame
  `framePhase` (same clock Wobble/Ring wave already animate on). Authored by clicking the preview to place/
  drag vortices, mirroring Pin warp's pin authoring exactly (`HandleCurlVortices`/`DrawCurlVortexMarkers`).
  Verified: direct `InverseWarp` unit check confirms a vortex holds a pixel's distance from its own centre
  exactly fixed (pure rotation) while its angle advances continuously with phase, and CW/CCW mirror correctly.
- **Pyre: Ring/Rosing gains "Ring expand"** (`Layer.ringExpand`, animatable) — scales a ring's own placement
  radius EVERY FRAME (unlike Spawn radius / a ring's own Radius, which lock in place the instant a shape
  spawns), so every shape already on the ring rides outward/inward together as this animates, fully
  independent of Size — the missing "Rotate, but radial" analog for a ring that visibly blooms over its own
  life instead of only via stacked Rosing rings blooming in sequence.
- **`PreviewBackground`** (`Laubrary.PreviewStage`, shared across tools) gains an optional flat backdrop
  style (`mode`: None/Solid/Gradient/Image + `solid`/`gradient`/`image`/`imageTint`/`imageZoom`/`imagePos`),
  alongside its existing `fill`/`sprites` — so Pyre's Save/Recall on this one asset now carries the WHOLE
  preview background (style + sprite props) as a single recallable preset, not just the sprites. Pyre's
  `bgMode`/`bgSolid`/`bgGradient`/`bgImage`/... proxies read/write these fields once a Test-background asset
  exists, falling back to the legacy per-`BlastSpec` fields when it doesn't — zero migration, no data loss
  for any already-authored blast that never touches Save/Recall.

### Removed
- **Pyre: `LayerShape.NoiseField`** (a standalone "single domain-warped noise cloud" shape). It duplicated
  capability that already existed compositionally: `ColorMode.NoiseFill` already paints the identical
  texture through any Disc/Crescent/MetaBlob's own silhouette, and `AlphaMaskModifier`'s Noise mask shape
  already carves an irregular cloud edge from any shape — so a dedicated shape added upkeep (a second SDF/
  threshold implementation to keep in sync) without adding real capability, and blurred the shape-vs-texture
  line: Disc/Crescent/Sprite/MetaBlob define WHERE pixels exist, Noise fill/masks only ever affect texture
  and edges within that. `noiseThreshold`/`noiseEdgeSoftness` (NoiseField-only) removed with it; `noiseZoom`/
  `noiseRotation`/`noiseDriftX`/`Y`/`noiseWarp`/`noiseBands` kept (still `ColorMode.NoiseFill`'s own params).
  One existing demo asset, `Assets/Demos/PyreDemo/NoiseField Ball.asset`, has a layer using the removed shape
  and needs manual attention (repurpose or remove) — flagged, not touched.

### Fixed
- **Pyre: Ring/Rosing's "keep shape on screen" containment clamp was silently coupling Size to ring
  placement.** The clamp re-centred a shape using its OWN current (possibly animated) radius as the safe-
  window bound — for Ring/Rosing that's a spawn-locked PLACEMENT decision, meant to be fully independent of
  Size (already documented as such: "Radius (placement) and Size (disc scale) are independent per ring").
  Once a ring sat close enough to the canvas edge, growing a shape's Size pulled its clamped centre inward,
  visibly shrinking/distorting the ring's actual radius and inter-shape spacing purely as a side effect of a
  Size curve. Ring/Rosing now skip this centre re-clamp entirely (Area scatter — genuinely random placement —
  keeps it); an oversized shape on an outer ring can now clip past the canvas edge instead, same tradeoff
  Ring start angle/Arc degrees already accept for their own spawn-locked placement. Verified via a direct
  `RenderFrame` comparison: a ring shape's placement centre now measures identical (34.5px) at Size 4 and
  Size 8, where it previously drifted with Size.
- **The Style Editor's flash-highlight now also reaches runtime-rendered controls.** `ZUI.StartFlash`
  (fired by the flash-icon buttons in `ZUIStyleEditorWindow`) previously only highlighted controls drawn
  by the editor toolkit's own draw calls (`ZUIButton.cs`/`ZUISlider.cs`/`ZUIText.cs`/`ZUIToggle.cs`,
  Editor-only) — a Play-mode HUD rendered via `ZUISheet.DrawBox`/`Button` never lit up when you flashed
  its style in the Zeditor. New `ZUIFlash.cs` (Runtime assembly, wrapped in `#if UNITY_EDITOR` — a
  diagnostic aid, not a shipped-build feature, so it compiles out of a real build) holds the same
  state/draw logic, editor-only-safe since it only ever runs inside the Unity Editor process anyway.
  `ZUI.StartFlash` forwards into it alongside its own existing editor-only state (no existing editor call
  site changed); `ZUISheet.DrawBox`/`Button` call it right after drawing their visual. Verified in Play
  mode: flashing "Framed" from a script (simulating the Style Editor's flash button) lit up both the
  direct `ZUISheet.DrawBox` card and the new `Zui.Panel` wrapper using the same style. Slider/Text flashes
  don't forward (no runtime rendering counterpart exists for those today).
- **Testable demos for this session's ZUI runtime and Chunks work.** `ZuiZheetTestHud.cs`
  (`Assets/ZUIDemo/ZUIRuntimeTest.unity`) gains a procedural (non-9-slice) `ProceduralButton` style on
  `DemoRuntimeZheet.asset` and a `Zui.Panel(rect, styleName, sheet)` demonstration, sitting next to the
  existing 9-slice `SpriteButton` card for direct comparison — press Play and everything (9-slice box,
  9-slice button, procedural button, the new sheet-aware Panel wrapper) is visible on one screen.
  `ChunksDemoSpawner.cs` (`Assets/Demos/ChunksDemo/ChunksDemo.unity`) gains a third burst key (`T`, at the
  mouse) firing a new `SampledDebris.asset` `ChunkSpec` — samples chunks from a `DemoSprites`-built shape
  (no shipped art, matching this demo's own convention) and tumbles them via the new pseudo-3D trick;
  on-screen control legend added since there wasn't one before.
- **Chunks: sampled pseudo-3D debris.** New optional `ChunkSpec` mode — instead of a flat procedural shape
  or a hand-authored sprite, a chunk can now be cut directly out of the exploding object's own sprite
  (`sampleSource`, `samplePxMin`/`samplePxMax`, new `SampledChunkSprites.Sample`, biased toward opaque
  pixels so cuts aren't blank) and tumbled with a squash+shade trick (`tumble`, `tumbleSpeedMin`/`Max`,
  `tumbleShadeStrength`, new pure `ChunkTumble.Evaluate`) that fakes a lit 3D fragment turning in place —
  width squashes by `|cos(phase)|`, colour shades by a phase-offset `sin`, no real 3D geometry involved.
  `Chunk`/`ChunkEmitter` wire it in as a third orientation mode alongside the existing free-spin/
  `faceVelocity`, applied only to chunks the emitter actually sourced via sampling (tumbling a full
  pre-made sprite would look wrong — the trick assumes a small, roughly-flat-shaded patch). Requires the
  source texture's Read/Write Enabled import flag; degrades to the existing sprites/procedural fallback
  if sampling fails (unreadable texture, or every attempt landed on transparent space). Compiles clean
  (fixed one bug found on first real compile: `GetPixels32` has no sub-rect overload, switched to
  `GetPixels`/`Color[]`) — still wants an in-editor visual look to judge whether the squash+shade actually
  reads as convincing tumbling debris; that's a judgment call, not something a compiler catches.
- **`UIAudit` gains `Overlap` and `Crowded` detection** (new `UIIssueKind` members) alongside the existing
  `OffScreen`/`TextOverflow`/`TinyText`/`NeedsScrollView`. Shared pairwise geometry logic
  (`UIAuditGeometry.CheckProximity`, `Runtime/UIAudit/UIAuditGeometry.cs`) is used by both the uGUI and
  IMGUI sections so the heuristic is defined once. Deliberately scoped to INTERACTIVE elements only — a
  panel background legitimately sits behind its own content, so checking every draw pair would be mostly
  false positives; two independently-clickable controls overlapping or crammed edge-to-edge is the actual
  bug. New `UIAuditContext.MinControlGap` (default 2px) is the crowding threshold. `ZuiDrawRecord` gains an
  `AllowOverlap` opt-out field (mirrors the existing `Clipped` flag) for deliberate IMGUI overlaps; no
  per-element opt-out exists yet on the uGUI side.
- **Ground-truth test fixture for UIAudit's IMGUI section** (`Assets/Tests/UIAudit/`, dev-host-only — NOT
  shipped in the package, first use of Unity Test Framework anywhere in Laubrary). `ImguiFixtureHud`
  draws a deliberately-bad frame (one instance each of TinyText/TextOverflow/OffScreen/Overlap/Crowded,
  spatially isolated so each fires independently) and a correctly-built equivalent using the same content
  through proper `ZuiStack` calls. `UIAuditImguiFixtureTests` (`[UnityTest]`, PlayMode — `ZuiAudit.Record`
  only appends during a live OnGUI Repaint pass, so this can't be an EditMode test) asserts the bad frame
  reports all five issue kinds and the fixed frame reports none. **Verified working** by manually replaying
  the test's own steps via `play_game` + `execute_script` (driving `TestRunnerApi.Execute` for a PlayMode
  run through Coplay turned out to conflict with Coplay's own play-mode management — see the Coplay
  gotchas memory/skill note; the `[UnityTest]` file itself is left for the user to run by hand via the
  Test Runner window): bad frame reported exactly the 5 expected issues, fixed frame reported 0. See
  `ZUI_API_AND_RUNTIME_ROADMAP.md` Task 3.
- **Runtime sheet-aware `Zui.Panel(rect, styleName, sheet?, padPts?)` overload** (`ZuiPanels.cs`) — draws a
  9-slice/procedural sheet-styled background via `ZUISheet.DrawBox` instead of a flat `Color`, additive
  alongside the existing flat-color `Panel`. Resolves `sheet ?? Zui.DefaultSheet`; a new
  `ZuiRuntimeSheet.cs` gives `ZuiRuntime.Zui` its own `DefaultSheet`, loaded via `Resources.Load` (works in
  a build, unlike the editor's AssetDatabase-based `ZUI.DefaultSheet`) and kept deliberately separate from
  it. Degrades to drawing nothing (returns the rect un-padded) if no sheet resolves, rather than throwing.
  New backing asset `Zui/SystemAssets/Resources/ZUIRuntimeDefaultSheet.asset` (a copy of `ZUIDemo`'s
  `DemoRuntimeZheet.asset`, which already had real 9-slice content, unlike `ZUIDefaultSheet.asset`/
  `ZUIShowcaseSheet.asset`/`ZeditorZheet.asset`, none of which define any `ZUINineSliceDef`).
- **`ZUISheet.Button` now renders procedural (non-9-slice) button styles at runtime**, closing a gap its
  own doc comment used to flag ("procedural buttons don't render at runtime yet"). Turned out to be a
  wiring gap, not a capability one: `ZUIButtonDef.DrawVisual`/`GetResolvedCornerRadius` (the same renderer
  the editor toolkit's own button drawing uses) have no editor-only dependency, so the runtime path now
  calls them directly when the style has no 9-slice frame set, using the same call convention as
  `ZUIButton.cs`.
  **All of the above verified working in Play mode** (screenshot-confirmed, `play_game`/`execute_script`
  probing): the sheet-aware `Panel` renders a real 9-slice-framed box; the procedural button fix renders a
  correctly-filled background; and a live in-memory edit to a sheet's button color (simulating a Style
  Editor tweak) was confirmed to repaint on the very next frame with no restart needed — so restyling a
  running game's runtime HUD via the Style Editor works. Along the way, found and fixed a genuine stale
  reference in `DemoRuntimeZheet.asset`: `SpriteButton`'s `nineSliceNormal`/`Hover`/`Active`/`ToggleOn` all
  pointed at `"BtnNormal"`/`"BtnHover"`/`"BtnPressed"`, none of which match either of the sheet's actual
  `ZUINineSliceDef` names (`"DemoFrame"`, `"Metal 9slice"`) — `ZUISheet.Button`'s existing (unmodified)
  9-slice path silently draws no background when `FindNineSlice` comes up empty, so this demo's own
  9-slice button test had never actually shown a frame. Re-pointed all four states at `"DemoFrame"`.
  Still pending: promoting `ZUIDemo`'s throwaway scene/scripts into a real `Assets/Demos/` entry (see
  `ZUI_API_AND_RUNTIME_ROADMAP.md` Task 2's remaining item).

### Changed
- **Renamed the Zoetrope/Bestiarium/Zoe naming triangle: `Zoetrope`→`Launimator`, `Bestiarium`→`Zoetrope`,
  `CharacterDef`→`Zoe`.** The old `Zoetrope` (sprite-sheet animation module) is now **Launimator**, freeing
  "Zoetrope" for its better-fitting new meaning: the composition/catalog module previously called
  `Bestiarium` (an antique device that creates the illusion of one continuous living thing from separate
  frames is a better metaphor for "assembles separate parts into one entity" than it ever was for raw frame
  animation). `Zoe`/`ZoeVersion` (the old per-animation asset) become `Reel`/`ReelVersion`; `CharacterDef`
  becomes the new top-level **`Zoe`** — the whole composed agent (body, weapon, FX, behaviors) a project
  spawns as one thing. Bridge folders follow: `BestiariumZoetrope`→`ZoetropeLaunimator`,
  `BestiariumPyre`→`ZoetropePyre`, `BestiariumDaemon`→`ZoetropeDaemon`, `PyreZoetrope`(Editor bridge, name
  unchanged — still bridges Pyre↔Launimator, just via the new name internally), `ZoeCombat`→`ReelCombat`.
  Full design rationale in `ZOE_ARCHITECTURE_DESIGN.md`. `[SerializeReference]` polymorphic fields
  (`Zoe.view`, `WeaponDef.muzzle`, `ProjectileDef.impact`) self-heal via `OutBurner ▸ Setup Scavenge Combat`;
  plain-serialized fields (`Reel.reelId`/`reelName`, formerly `zoeId`/`zoeName`) carry `[FormerlySerializedAs]`
  so existing saved data survives the rename.
- **Pyre: wider rollout of the 2D drag-pad control to X/Y field pairs across shapes and modifiers.** The
  "Position" 2D control was originally added as a prototype ("tried here first... before any wider rollout").
  Full sweep of every shape/modifier param now converts the genuine X/Y spatial pairs still using two separate
  sliders: `TurbulenceModifier.offsetX/Y`, `VoronoiCrackModifier.driftX/Y`, and `Layer.positionX/Y`'s second,
  forgotten call site in the NoiseField shape branch (which returns early before reaching the already-converted
  shared "Position" control, so it had its own un-converted copy of the same field) all now use
  `ZUIValue2DControl.Draw`. Three more pairs — `RotateModifier.pivotX/Y`, `AlphaMaskModifier.offsetX/Y`,
  `DropShadowModifier.offsetX/Y` — are plain `float` fields, not `ZUIValue` (not animatable); `ZUIValue2DControl`
  can't take them directly, and promoting the field TYPE to make them animatable would be a real data-model
  change risking existing saved BlastSpec assets, well beyond a UI swap nobody asked for. These use
  `ZUI.PositionPad` instead (works on plain `Vector2`, already built this session for the same reason).
  Explicitly NOT converted: `ScaleModifier.vertical/horizontal/both` (mutually exclusive via a radio toggle —
  never two axes live at once, so a simultaneous-XY drag-pad doesn't fit the actual UX) and `BlastSpec.origin`
  (a genuine 2D quantity, but a blast-level field outside "shape/modifier params," and a plain `Vector2` rather
  than two separate fields — a candidate for a future pass, not this one).
- **Zoe Preview's Asset/Clip/Attach un-split back to one row, and Asset's cap raised.** Two follow-ups after
  the `labelWidth` fix below: (1) the earlier split of Asset onto its own row was a wrong call — it was
  working around the `labelWidth` bug, not a real space shortage, and the original ask was one row. (2) Asset's
  width cap was tightened to 300 "to protect Clip/Attach" even though the row ends in `FlexibleSpace()` with
  nothing actually contending for that space — raised to 500. `ZUI.FitWidth`'s icon/button slack also bumped
  24→34px; it was still clipping the last character or two of an ObjectField's text even when the [min,max]
  clamp wasn't the binding constraint. Verified against the real `PlayerZoe_draft` asset: displayed text needs
  ~244px, comfortably under the new 500px cap.
- **The real reason Pyre's compact right-pane fields (Asset/Clip/Attach in "Zoe Preview", "Colour" in
  "Preview backdrop") kept truncating despite `ZUI.FitWidth` sizing them correctly**: `EditorGUIUtility.
  labelWidth` is a GLOBAL Unity setting. `PyreWindow` sets it to `112f` once, for the LEFT panel's long dial
  labels ("Taper (centre↔edge)"), and that value silently persists into the right pane too — every labelled
  field there reserved 112px just for its label (a 5-character word needs ~35px), eating most of whatever
  total width `FitWidth` had carefully computed, regardless of how correct that computation was. New
  `ZUI.NarrowLabel(label)` scope temporarily sets `labelWidth` to match the label's own actual text width
  (restoring the ambient value on dispose) — wrap any `FitWidth`-sized or otherwise deliberately compact
  labelled field in it. Applied to Zoe Preview's Asset/Clip/Attach and backdrop's Colour field; the backdrop's
  own Tint field and Test Background's per-sprite Tint switched to the `GUIContent.none` + separate stacked
  `Label` pattern instead (sidesteps the ambient value entirely, no scope needed). Verified numerically:
  available content width for the Asset field went from 126px (broken — label ate 112 of FitWidth's 238)
  to 198px (correct), with the ambient value confirmed restored after the scope closes. See
  `EDITOR_TOOL_CONVENTIONS.md`'s new note on this.

### Added
- **`ZUI.Popover(activatorRect, size, drawContent)`** (`ZUIPopover.cs`) and **`ZUI.ContextMenu(params
  ZUIMenuItem[])`** with **`ZUI.MenuItem`/`ZUI.MenuSeparator`** (`ZUIContextMenu.cs`) — generic wrappers
  over `PopupWindow.Show`/`GenericMenu` for the common flat/simple case, so a one-off popup or right-click
  menu no longer needs a hand-rolled `PopupWindowContent` subclass or `GenericMenu` builder at every call
  site. `ZUI.ContextMenu` pairs with `SelectableRow`/`Chip`'s existing `rightClicked` out-param as the
  canonical "row with a context menu" pattern. Popups/menus with real internal state (color picker,
  gradient-stop editor, mode-dependent branching menus) stay hand-rolled — these are additive helpers, not
  a replacement for `PopupWindowContent`/`GenericMenu`. Prompted by a session audit finding every existing
  popup/menu in the package (6+ `PopupWindowContent` subclasses, 11+ `GenericMenu` builders) was a one-off,
  with no shared primitive despite the repetition.
- **`references/zui.md` completeness pass** — documented several real, shipped scopes that had zero
  mention in the reference despite being in active use: `ZUI.VGroup`/`VGroupBox`, `ZUI.AnimatedFoldout2`,
  `ZUI.FoldControls`, `ZUI.Blocks`/`.Cell(...)`, `ZUI.SelectableRow`/`ZUI.Chip`, `ZUI.Toolbar`. Added a
  "Quick index" task→control lookup table, a paired before/after "Avoiding common layout mistakes" gallery
  (truncated text, overly-wide controls, clumped controls, one-field-per-row), and moved the "if nothing
  fits, that's a smell — propose extending ZUI" policy into the doc itself (previously only stated in this
  dev host's own `CLAUDE.md`, so it never reached Claude sessions working in consumer projects). See
  `authoring.md` rule #14 for the new freshness discipline this gap prompted.
- **`ZUI.PaddedArea()`** (`ZUIPaddedArea.cs`) — insets a whole content block (e.g. a window pane) from its
  container's edge on all four sides, distinct from `ZUI.HorizontalSpace()`/`VerticalSpace()` which space
  controls apart from EACH OTHER rather than from the outer boundary. Backed by a new sheet field,
  `ZUIStyleSheetAsset.contentPadding` (wired into the Style Editor's Layout tab, next to the existing
  Vertical/Horizontal spacing sliders) — a user-adjustable value, not a hardcoded pixel number. Pyre's right
  pane (the preview viewport + all its settings sections) is the first adopter: `using (ZUI.PaddedArea())
  DrawPreview();` around the existing call, so its controls no longer start flush against the window edge.

### Fixed
- **Pyre's "Zoe Preview" Attach id field rendered garbled/overlapping controls, including the unrelated Asset
  field above it.** Root cause: Attach id conditionally drew either a `Popup` or a `TextField` depending on
  whether a dropdown option list was available — and that list was computed from the Clip text field's value
  read fresh THIS SAME FRAME (changes on every keystroke while typing). Unity's IMGUI matches Layout and
  Repaint passes by call order; a genuinely different SEQUENCE of GUI calls between the two passes (not just a
  different value) corrupts rect/control-ID matching for the rest of the draw call, not just that one row.
  Fixed: Attach id is now ALWAYS a plain `TextField` (stable structure every frame); when options are
  available it also shows a small "▾" button opening a `GenericMenu` (a modal overlay outside the
  Layout/Repaint-matched hierarchy) instead of swapping the field itself for a different control type. The
  option list now reads the STORED `spec.previewSubjectClip`/`previewSubjectAsset`, not the live text-field
  return value, so its presence/absence is stable within one frame even while typing. See
  `EDITOR_TOOL_CONVENTIONS.md`'s new "Never let a live-edited value decide which control type gets drawn".
- **`ZUI.Slider`/`MicroSlider` numeric input fields could show a long trail of meaningless decimals** (e.g.
  "Speed" landing on `0.40000000596...` instead of `0.4`) — a `Mathf.Lerp`/`InverseLerp` round-trip through
  normalized 0–1 space accumulates float32 mantissa noise, and `EditorGUI.FloatField` reveals it in full once
  focused. First pass only rounded the TRACK-drag path (`SamplePosition`/`SamplePositionVertical`, via new
  `RoundValue`); the bug persisted because `EditorGUI.FloatField`'s number text supports its OWN drag-to-scrub,
  a completely separate interaction path with its own float accumulation that the track fix did nothing for.
  `DrawMicroSlider` now rounds both paths. Verified via the round-trip (`"R"`) format, which shows the full
  precision needed to exactly reconstruct a float (i.e. would expose any hidden noise) — prints a clean `0.4`.

### Added
- **`ZUI.MicroSlider(value, min, max, label, decimals, ...)`** — an overload taking an explicit `int decimals`
  for values whose PRACTICAL precision is genuinely coarser than float-noise cleanup alone justifies (a
  playback speed multiplier has no use beyond 1 decimal even though 5dp noise-cleanup is technically "clean").
  Quantizes every interaction path (track drag and field scrub/type) to exactly that many decimals. A separate
  overload rather than a new parameter on the existing one, because that one ends in
  `params GUILayoutOption[] options` and at least one existing caller passes `options` positionally —
  inserting a parameter before it would have silently broken that call. Pyre's preview Speed slider is the
  first adopter (`decimals: 1`).
- **`ZUI.FitWidth(label, value, min, max)`** (`ZUIFields.cs`) — measures how wide a labelled field actually
  needs to be to show its CURRENT content without truncating (via `GUIStyle.CalcSize`), clamped to a sane
  range. The fix for "no infinite-width controls" is a fixed pixel cap regardless of content, which truncates
  the moment a value is longer than whoever picked that number expected (an asset name, a typed clip name, a
  dropdown selection). This grows/shrinks with whatever the field currently shows instead. Pyre's "Zoe
  Preview" panel (formerly "Live preview subject") adopts it: `Asset` (long, variable-length names) now gets
  its own row instead of being force-fit alongside the others; `Clip`/`Attach id` (normally short) share a
  second row, each still sized to their own current content rather than one blind shared cap. `Attach id` is
  a dropdown of the selected Zoe/clip's actual MetaLayer names when the (new, optional)
  `PyrePreviewSubjectProvider.GetAttachPointOptions` bridge hook can enumerate them, falling back to free text
  otherwise — verified against real project data, resolves to `["Muzzle"]` for `PlayerZoe_draft`/`"Shot"`.
  Also: "Preview backdrop"'s compact combo grew ~20% (56px → 68px) — the panel had the room to spare, and
  three sliders sharing the retimed/zoom/speed row now get real gaps (`ZUI.HorizontalSpace()`) between each
  pair instead of one `FlexibleSpace()` dumped at the row's end.
- **Pyre preview panel, second compaction pass.** "Preview backdrop"'s Mode selector is now an exact-height
  hand-built vertical radio stack (`PyreWindow.DrawModeRadio`, using the Rect-based `ZUI.Toggle` overload —
  `ZUI.MiniRadioVertical` auto-measures its own height from label text and can't be forced to match another
  control's size) so it lines up pixel-for-pixel beside the image picker/position-pad combo. Zoom dropped its
  separate numeric input field (shows "Zoom: 1.88" inline in the bar instead); Tint's label now sits on its
  own line above the swatch instead of Unity's default label-left/swatch-right, so the swatch keeps a usable
  width in the narrow stacked column. `ZUI.HorizontalSpace()` now separates every column in the row (radio |
  picker | position pad | zoom+tint) — the sheet-configurable gap (Style Editor → "H Control Gap"), not a bare
  `GUILayout.Space`. Test Background sprites got the same combo treatment. "Live preview subject" renamed to
  **"Zoe Preview"** (Zoetrope is currently the only registered bridge; the underlying fields/interface stay
  fully generic) and its three fields (Asset/Clip/Attach) now share one row.
- **Attach id is now a dropdown of real MetaLayer names when the bridge can enumerate them**, instead of a
  free-text field the user had to get exactly right by memory. New
  `PyrePreviewSubjectProvider.GetAttachPointOptions: Func<Object, string, string[]>` — optional, bridge-
  supplied, falls back to free text when null/empty (an unresolved asset, no clip yet, or a bridge that
  doesn't support enumeration). `Pyre.Zoetrope.Editor` is the first implementation: returns the selected
  clip's `AnimationDef.metaLayers[].id` list. Verified against real project data: resolves to `["Muzzle"]` for
  `PlayerZoe_draft`/`"Shot"`, matching the actually-painted MetaLayer.
- **`ZUI.PositionPad`** (`Zui/Scripts/Editor/ZUIPositionPad.cs`) — a small, compact 2D drag-pad for a plain
  `Vector2` setting (screen/pixel-space offsets, UV nudges — anything that isn't an animatable per-frame
  value). `ZUIValue2DControl` already solves "drag to aim a position" for the animatable `ZUIValue` pair case;
  this is the lightweight cousin for a single static setting that just needs "drag a dot in a box." Has a
  `flipY` option (default true = "drag up increases Y", the natural feel for a fresh value with no prior
  convention; pass `false` to match an existing screen-Y-down convention some other control already reads the
  same field with).
- **Pyre's "Preview backdrop" and "Test background (sprites)" sections redesigned for compactness** — the
  Image mode's big ~140px preview swatch plus three separate infinite-width rows (Image/Tint/Zoom) is now one
  row: a 56px picker thumbnail + a `ZUI.PositionPad` (both naturally square, side by side) with Zoom
  (`ZUI.MicroSlider`, Zounds-style label+value-on-the-bar) and Tint stacked beside them. `Mode` is now a
  vertical `MiniRadioVertical` beside its own content instead of a dropdown above it, sized to match the combo's
  height so the two read as one unit. Each Test Background sprite entry got the same combo treatment (picker +
  position pad + Scale/Tint), replacing its own three stacked full-width rows — `position` there is wired with
  `flipY: false` to match `PreviewStageGUI`'s pre-existing screen-Y-down convention for direct viewport
  dragging, so the compact pad and dragging the sprite directly in the preview never disagree about which way
  is "up". New `BlastSpec.previewBgImagePos` field backs the backdrop image's own position. The preview's
  transport row (Frame count/Zoom/Speed) collapsed from three full-width slider rows into one compact row of
  `MicroSlider`s. See `EDITOR_TOOL_CONVENTIONS.md`'s new "No infinite-width controls" section for the general
  rule this follows (rough width targets: ~150px slider, ~130px colour field, ~110px short text field).
- **`Laubrary.Caching.AssetCacheInvalidation`** — a general, opt-in "this authored asset just changed, drop
  any cached data derived from it" bus for runtime systems that cache something DERIVED from a
  ScriptableObject asset (rendered frames, a baked mesh, whatever). A new `Laubrary.Caching.Editor` bridge
  (`AssetCacheInvalidationBridge`) watches Unity's own `ObjectChangeEvents` — the same signal the Editor uses
  internally, so it fires regardless of which tool made the edit (a custom ZUI window, the plain Inspector, an
  Undo/Redo) — and forwards asset property changes into the bus automatically. No individual editor tool needs
  to call `Invalidate()` at its own edit sites; a Runtime cache just subscribes once and checks the asset's
  type/identity itself. First preference, not a hard rule: an asset type that wants a frozen/snapshot cache
  simply never subscribes. `BlastPlayer`'s per-spec frame cache is the first subscriber — editing a `BlastSpec`
  anywhere (Pyre's window, the Inspector, mid-Play-Mode) now drops its cached frames automatically, so the next
  play re-renders with the edited data instead of showing stale frames until a domain reload. Verified
  end-to-end: edited a live `BlastSpec`'s `seed` the same way Pyre's own UI does (`Undo.RecordObject` + field
  write + `EditorUtility.SetDirty`), and the next `BlastPlayer.GetFrames` call returned genuinely regenerated
  Sprite instances (different instance IDs), not the stale cached array.
- **Chunks can now fling animated content, not just static/procedural sprites.** A new `IChunkAnimation`
  interface (frames + fps + loop) lets a `ChunkSpec` play any animated asset per chunk instead of a plain
  tinted sprite — e.g. fireballs shot in a line from a jet engine, or a Pyre blast reused as flung debris.
  Chunks itself stays a standalone, zero-dependency package: Pyre and Zoetrope each ship their own adapter
  (`BlastSpecChunkAnimation`, `ZoeAnimationChunkAdapter`) that implements the interface and references Chunks,
  not the other way around. `Chunk`'s existing size/alpha/color-over-life curves apply on top unchanged, so a
  spec can shrink/fade a flung fireball instance without Pyre needing to bake that in.
  Also new: **`Laubrary.PreviewKit.IVisualPreview`**, a small Runtime interface (render a preview texture +
  optionally animate it) any pickable visual asset type can implement — generalizes the
  `RenderThumbnail`/`AnimateThumbnails` pattern `LaubraryAssetWindow<T>` subclasses (Pyre, etc.) already had
  per-window, so a picker outside that asset's own tool can show a live preview too. Both new Chunk animation
  adapters implement it. Chunks gained its first Editor code (`Editor/Chunks/`): a `ChunkSpec` custom
  inspector shows a live preview of the chosen `animationSource` and, when the concrete adapter type has
  registered one via `ChunkAnimationEditors` (mirrors Pyre's own `IPyrePreviewSubject` provider-seam pattern),
  an "Edit…" button that jumps straight into that asset's real authoring tool (Pyre / the Animation Builder).
  Not yet done: `ZUI.LabeledObjectPicker` (named in the ZUI reference docs as the intended long-term home for
  this kind of preview+picker) doesn't exist yet in this codebase — the new `ChunkSpecEditor` draws its own
  preview block with plain `EditorGUILayout` instead (consistent with the documented CustomEditor/no-sheet
  rule); retrofitting `BlastSpec`/`ZoeVersion`/`LazorShape`'s own existing browsers onto `IVisualPreview` is
  deferred too, since nothing today required it.
- **`ZonedAnimationPlayer.PlayAndCaptureMetaPoint(clip, metaLayerId, out worldPos)`** — starts a clip as a
  one-shot and captures a named MetaLayer's point at that exact instant, in one call. This is now the single
  shared definition of "trigger an effect at a point on this animation, captured once when the action
  starts" (a muzzle flash, a footstep dust cloud) — both gameplay code and the `Pyre.Zoetrope.Editor` preview
  bridge call it instead of each separately calling `Play` then `TryGetMetaPoint`, so the two can never
  quietly drift out of alignment with each other again.
- **Pyre: a live, animated preview subject.** A BlastSpec can now reference any asset in a new "Subject
  asset" field (Pyre's own preview panel — a plain `Object` field, zero dependency on what it holds) plus a
  clip name and an "attach point id"; Pyre plays it behind the blast and auto-offsets the blast's own origin
  marker to track a named point on it every frame, restarting together each time the preview loops. Pyre
  core has zero knowledge of what resolves the asset — `IPyrePreviewSubject`/`PyrePreviewSubjectProvider` is
  the seam (same pattern as `ICharacterView`/`ICombatFx` in Bestiarium). The new **`Pyre.Zoetrope.Editor`**
  bridge is the first (only, so far) implementation: drag in a `ZoeVersion`, name a clip, name a MetaLayer
  id (e.g. "Muzzle") — the character's real clip plays via a hidden, editor-only `ZonedAnimationPlayer` (the
  same runtime component the game uses, not a reimplementation — "preview == runtime" for the subject too,
  matching Pyre's own BlastRenderer guarantee). The attach point is captured ONCE at the moment the clip
  (re)starts, not re-sampled every repaint — matching how the real game reads it once at the instant of
  firing. An earlier draft of this feature re-sampled continuously, which read as empty on every frame
  except the one actually painted (a MetaLayer typically has data on only one frame) and made the blast's
  origin visibly snap between "aligned" and "centred" as the clip played. Generic by construction: nothing
  references a specific asset, clip, or layer name anywhere in the mechanism — it works for any
  BlastSpec/ZoeVersion/clip/MetaLayer combination.

### Added
- **`Laubrary.PreviewKit.Editor.LiveScenePreview`** — a generic, Pyre-agnostic utility any IMGUI editor window
  can use to preview real gameplay objects with a structural guarantee they render exactly as they would
  in-game, instead of hand-reimplementing pivot/scale/flip math (which can only ever approximate the real
  rendering and silently drifts the moment a real component's own behavior changes). Wraps
  `UnityEditor.PreviewRenderUtility` — the same isolated-scene-plus-camera primitive behind Unity's own
  material/prefab preview thumbnails — with a small API: `Spawn`/`Adopt` a real GameObject (add whatever real
  components gameplay uses — a `SpriteRenderer`, a `ZonedAnimationPlayer`, ...) into an isolated scene,
  `Frame(worldCenter, worldHeight)` to point an orthographic camera at it (mirroring a real 2D gameplay
  camera's own `orthographicSize` convention), `Draw(rect)` to render and blit into IMGUI with a transparent
  background that composites over whatever the caller already drew. `Camera` is exposed read-only so callers
  can `WorldToScreenPoint` other world positions into the same projection for perfectly consistent alignment.
- **Pyre's live preview subject now renders through `LiveScenePreview`** instead of hand-drawn IMGUI. Because
  `ZonedAnimationPlayer` already drives a real `SpriteRenderer` every `Tick` (pivot, `pixelsPerUnit`, `flipX`
  — all of it), the `Pyre.Zoetrope.Editor` bridge's preview subject now has no custom drawing code left at
  all: it spawns its existing hidden GameObject into the shared `LiveScenePreview` and lets the isolated
  camera render it. `IPyrePreviewSubject`'s contract changed from `Draw(originPx, zoom)` +
  `TryGetAttachOffsetPx` (an IMGUI pixel-offset convention) to `SpawnInto(LiveScenePreview, worldPosition)` +
  `TryGetAttachWorldPos` (plain world space) to match. The blast's own origin-marker alignment now reads the
  attach point back via the SAME camera's own `WorldToScreenPoint` rather than a second, hand-matched
  screen-space formula, so size and alignment structurally cannot drift apart from each other or from
  gameplay again. The blast's own canvas still renders as canvas-pixels × zoom on purpose (pixel-perfect
  WYSIWYG — the origin handle, MetaBlob, Smudge and PinWarp tools all depend on that 1:1 mapping) but is now
  scale-matched to the subject via `zoom * blast.pixelsPerUnit` as the shared screen-pixels-per-world-unit
  conversion, so tuning either asset's `pixelsPerUnit` correctly resizes the subject relative to the blast in
  the preview, matching how their relative on-screen size actually changes in gameplay — previously the
  preview was invariant to `pixelsPerUnit` entirely. Verified end-to-end with a live `MuzzleFlash`/`PlayerZoe`
  pair: `TryGetAttachWorldPos` returned the real captured muzzle point, `Camera.WorldToScreenPoint` placed it
  correctly offset from centre in the rendered frame, and a pixel readback confirmed non-transparent sprite
  content actually reached the render target (not a blank/failed render).
  **Follow-up fix:** the blast's own origin-marker offset went invisible (rendered far outside the viewport)
  because `Camera.WorldToScreenPoint` returns coordinates in the render TEXTURE's own pixel space —
  `PreviewRenderUtility` renders at retina/HiDPI resolution (`camera.pixelWidth/Height`), not the GUI-point
  space the rest of the window's Rects are expressed in. Fixed by scaling the projected point down by the
  actual ratio between the two before combining it with any other screen Rect.
- **`ZUI.HelpIcon(string tooltip)`** (`ZUIFields.cs`) — a small "?" glyph that shows explanatory text as a
  native hover tooltip. New standing convention (see `EDITOR_TOOL_CONVENTIONS.md`): box/section titles and
  field labels stay short and literal; anything explaining *why* or *how* a control works goes in a
  `HelpIcon` tooltip instead of printed into the UI itself. Pyre's "Preview backdrop"/"Live preview subject"
  boxes are the first adopters (previously titled `"... (not baked)"`, which was exactly this problem).

### Fixed
- **Zoe Browser: "New animation" could silently overwrite an existing animation of the same name.**
  `_newAnimName` never cleared after a successful create, so a stray second click (or a value left over from
  renaming/inspecting a different animation) would call `CreateEmptyAnimation` → `SaveAnimationToDraft` with
  a name that matched something real — which replaces in place, discarding its frames/recipe/meta-layers with
  zero warning. Real data loss hit in practice. Now: the field clears itself after a successful create, and a
  name collision with an existing animation prompts a confirmation dialog first (matching the project's own
  convention of confirming before an edit Undo can't reliably cover).

### Added
- **`ZonedZoeView`** (`Bestiarium.Zoetrope`) — a second `ICharacterView` alongside `ZoeView`, backed by
  `ZonedAnimationPlayer` instead of the simpler `ZoePlayer`. For characters that need a one-shot clip to
  auto-hold on its last frame (`Play(clip, loop: false)` — no zone authoring needed, that's already
  `ZonedAnimationPlayer`'s plain-clip behavior) and/or `TryGetMetaPoint` to read a named MetaLayer's painted
  point each frame (a muzzle exit point, a blade position, ...).

### Fixed
- **Zoetrope: colour-key background removal left a bright, tinted fringe around sprites on anti-aliased
  source sheets** — `RegionSlicer.ColorKey`'s hard per-channel-distance cutoff either left the 1px blend
  ring most ripped sheets have (a genuine RGB mix of sprite and background colour) fully opaque and visibly
  tinted toward the key colour (tolerance too low), or, raising tolerance to hide it, ate into genuine edge
  detail too (tolerance too high) — an unavoidable tradeoff of a binary in/out test. `AtlasBaker.TransformCell`
  now runs a new `ErodeKeyFringe` pass after keying: pixels still opaque but spatially adjacent (Chebyshev,
  radius 1) to a newly-transparent pixel get their alpha faded proportionally instead of staying fully
  opaque — a purely spatial fix, so unlike an RGB-distance-based decontamination attempt (tried first, then
  dropped — it degenerates badly on high-contrast art, since even a slight blend toward the key colour on a
  saturated foreground already reads as "far" from the key by distance) it works the same regardless of the
  sheet's actual palette. Verified with a synthetic red-on-white test sheet: the 40 pixels that were fully
  opaque while touching background all now fade to partial alpha; sprite interior and far background pixels
  are untouched.

### Added
- **Editor bridges for Bestiarium's optional presentation modules** (`Bestiarium.Zoetrope.Editor`,
  `Bestiarium.Pyre.Editor`) — the runtime bridges (`ZoeView`, `PyreChunksFx`) already hard-reference concrete
  Zoetrope/Pyre/Chunks types; these add the matching EDITOR-side coupling so a Bestiarium recipe's look/effects
  are actually reachable from where they're authored, not just inert object references. A `[CustomPropertyDrawer]`
  on `ZoeView` adds an "Open in Animation Builder" button next to its `version` field — resolves the owning `Zoe`
  by folder convention (a `ZoeVersion` carries no back-reference to it) via `ZoeRepo.EnumerateZoes()`, since
  without this a selected `ZoeVersion` only ever lands on its bare, useless default ScriptableObject inspector.
  A matching drawer on `PyreChunksFx` adds a "Preview in Pyre" button. `PyreWindow` gains a small
  `public static OpenFor(BlastSpec)` (mirroring the existing parameterless `Open()`) as the entry point these
  drawers — or any future caller — use to jump straight into editing/previewing a specific blast, since the
  base `LaubraryAssetWindow<T>.SetAsset` is protected and wasn't otherwise reachable from outside the window
  class. `Bestiarium.Editor` core itself is untouched — the drawers are purely additive, matching the existing
  optional-bridge pattern rather than adding a hard dependency to the core recipe editor.
- **`VoronoiCrackModifier` gains a Spread mode** — instead of every seam tinting at once (still the default,
  `Uniform`), the crack reveal can now sweep spatially: `Centre out` (cracks nearest the shape's own centre
  light up first, spreading outward), `Edge in` (nearest the outer edge first, spreading inward), or `Both`
  (lights up from the centre AND the edge simultaneously, meeting in the middle last). Driven by a new
  animatable `Spread` (0→1, a rising curve by default — the fracture actively spreading over the shape's life)
  and a `Spread softness` for how hard/soft the reveal's own leading edge reads. Reuses
  `PixelInfo.crossFrac` (already computed per-pixel as 0=shape centre→1=shape edge for every shape kind, not
  just a canvas-relative radius) rather than inventing new position math, so the reveal correctly follows the
  actual shape's own geometry (Disc, Crescent, Bars' back→tip axis, ...). `Both` mode scales each wavefront to
  only half of `Spread`'s range so they meet at the midpoint exactly when `Spread` reaches 1, instead of both
  covering the full range simultaneously and washing out the "meeting" effect. Verified directly: at Spread=0.5,
  Both reveals crossFrac 0.0–0.2 and 0.8–1.0 while leaving 0.4–0.6 dark — confirming the wavefronts converge
  rather than overlapping immediately.
- **Pyre: noise-driven modifiers for churning/rolling explosions** (dust clouds, roiling fireballs, mushroom-cloud
  blasts) — all opt-in, nothing existing changes behavior unless a new modifier is explicitly added:
  - **Alpha mask gains a `Noise` shape** — carves an irregular cloud silhouette from domain-warped noise instead of
    a clean geometric edge, reusing the existing Size/Rotation/Offset params as the noise's zoom/placement, plus a
    new Noise-only warp strength and drift X/Y (for a cloud that visibly rolls as it reveals).
  - **`TurbulenceModifier`** (Geometry) — displaces pixels via a 2D noise field whose own domain spins (Rotation)
    and drifts (Offset X/Y) over life. This is the churn/roll engine: stack on a Disc/MetaBlob with a spatial fill
    to churn the colour bands (a roiling fireball), or after Ground/Profile to roll an already-molded silhouette (a
    mushroom cloud's characteristic turning cap).
  - **`PosterizeModifier`** (Colour) — quantizes colour (and optionally alpha) into a fixed number of bands, the
    main lever for reading as hand-painted shading instead of a smooth procedural gradient.
  - **`OrderedDitherModifier`** — converts smooth alpha into a hard 4x4-Bayer stipple (the genuine crosshatch dither
    pattern classic pixel art uses for shading), as an alternative to Dissolve's random speckle.
  - **`LayerShape.NoiseField`** — a new layer shape: a single domain-warped noise cloud drawn as a whole (no
    scatter/count, like MetaBlob), shaped by the layer's own Size (radius) and Position (centre), with its own
    Zoom/Rotation/Drift/Warp/Bands/Threshold/Edge-softness controls. The dedicated "dust cloud / gas cloud /
    churning energy field" primitive the other additions here support but don't by themselves provide.
  - All noise is built on the existing deterministic `Hash01` primitive (a shared `PyreNoise` helper) — never
    `UnityEngine.Random` or `Mathf.PerlinNoise` — so preview == bake == runtime stays bit-identical.
- **Pyre: five more modifiers exploring non-noise procedural techniques** (also opt-in, additive only):
  - **`RingWaveModifier`** (Geometry) — a RADIAL ripple (unlike Wobble's fixed linear one); animate Phase over life
    to send a shockwave ring travelling outward through a shape's own texture/shading.
  - **`SunburstModifier`** (Colour) — N alternating bright/dim rays around the canvas centre, for a charging-energy
    or classic-sunburst look.
  - **`PulseRingsModifier`** (Colour) — concentric brightness rings travelling outward across a shape over its own
    life (a sonar-ping / energy-pulse look); a pure function of the already-available crossFrac/life, so it works
    on Disc, MetaBlob, Bars, Sprite alike.
  - **`VoronoiCrackModifier`** (Colour) — a cellular (Worley) crack pattern: darkens/brightens near the seams of a
    jittered feature-point grid, for a shattered-crystal / cracked-earth / lightning-crackle look. A genuinely
    different visual family from the Perlin-style domain-warped noise above (faceted/linear vs. blobby/smooth); can
    also tint each cell's interior for a stained-glass look.
  - **`ChromaticAberrationModifier`** (Post, global only) — RGB channel split (radial from the canvas centre, or a
    flat direction) for the classic energy/impact chromatic-fringe look.
- **Pyre: Ring scatter mode** (`Layer.scatterMode`) — an alternative to the existing Area scatter (random points
  filling the Spawn radius disc). Ring places a layer's shapes along the RIM at Spawn radius instead, via
  `RingOrder` (Sequential = evenly spaced in index order; Random = independent random angle) and an animatable
  `ringStartAngle`/`ringArcDegrees` pair (360° = the full rim; less confines shapes to a wedge/fan). Editor: a
  Ring box under the Area/Ring radio in the layer inspector.
- **`PinWarpModifier`** (Geometry) — hand-animated pin/lattice warp, a different KIND of tool from everything else
  above: instead of a procedural formula, you place "pins" directly in the preview and drag each one on whichever
  frames matter (a small "Pin warp" authoring box + preview click/drag, mirroring the existing MetaBlob-orb/Smudge-
  stroke authoring pattern) — the frames you don't touch interpolate automatically (smoothstep between the
  keyframes you did set; held before the first and after the last), so only the moments where a pin's motion
  actually changes need keyframing. Nearby pixels drag along based on inverse-distance falloff from the pin's
  radius (anchored at its very first keyframe, its "rest" position); multiple pins simply add together. Zero
  randomness — fully deterministic authored data, no seed/hash needed at all. One small, deliberately isolated
  addition to `BlastRenderer.CollectMods` (a type-check handing the modifier the raw frame index) was needed since
  this is the first modifier that cares about the actual frame number rather than a 0..1 progress — every other
  modifier's `Prepare`/`InverseWarp` contract is untouched.
- Noted for later: Pyre's demo scene (`Assets/Demos/PyreDemo/`) predates and doesn't follow the `authoring.md` §8
  "Build Demo Scene" menu-command convention (it's hand-authored). Not addressed now — flagged so a future session
  doesn't assume it already matches the convention `authoring.md` itself cites it as an example of.
- **Pyre: Ring scatter gains "Align rotation"** (`Layer.ringAlignRotation`) — rotates each shape to face its own
  angle around the ring instead of every instance sharing one fixed orientation. Matters for asymmetric shapes
  (Crescent's bite, an offset Hollow hole); a plain Disc looks the same either way. Composes with Star spread's own
  per-instance rotation.
- **`ColorMode.NoiseFill`** (Disc/Crescent/MetaBlob) — fills the shape through a domain-warped noise field instead
  of a clean radial gradient, for a cloudy/marbled interior. Reuses the existing `NoiseField` shape's Zoom/Rotation/
  Drift/Warp/Bands params (revealed in the layer inspector whenever Noise fill is selected) rather than adding a
  parallel set of knobs.
- **`EdgeWarpModifier`** (named `RoughEdgeModifier` earlier in this same Unreleased window — renamed before ever
  shipping) — a new modifier family (`EdgeModifier`, alongside Geometry/Pixel/Post) that roughens a shape's OUTER
  silhouette (Disc/Crescent/SparkleField) without touching the fill: unlike a `GeometryModifier` (Wobble/Jagg/
  Turbulence), which warps the whole coordinate frame so the fill ripples along with the boundary, this only
  perturbs the hit-test radius per angle around the shape centre — a jagged/torn or smooth/wavy rim over an
  undisturbed gradient/noise-fill interior. `Jaggedness` blends between a smooth noise wave and a hard faceted
  step. `Softness` feathers that PERTURBED boundary with its own alpha fade — added because the layer's own Outer
  softness fades from the shape's true, unwarped radius, so it doesn't track a jagged/wavy edge correctly. Every
  param on this modifier — including `Jaggedness`/`Warp`, initially left as plain floats matching the package's
  usual "discrete/waveform-character knobs stay static" convention (Jagg's arms, Posterize's levels, Sunburst's
  sharpness) — is MultiCont (`ZUIValue`, animatable) by explicit request for this modifier specifically.
- **Outline gains an Over life / Fill colour mode** (reuses `ColorMode`, like every shape's own fill already does)
  — Fill is the existing behaviour (the gradient read across the outline's thickness, inner→outer); Over life
  instead samples ONE colour from the whole gradient, at the blast's own life. Needed a small new `PostModifier.
  life` hook (set by `BlastRenderer` right before `Prepare`/`Apply`, mirroring `PinWarpModifier`'s frame-index
  hook) since `Apply` otherwise has no access to progress at all.
- **Voronoi crack gains Zoom/Rotation/Drift X/Drift Y/Seed offset** (mirroring `NoiseField`'s own domain controls,
  `cellSize` renamed to `zoom` to match — `[FormerlySerializedAs]` keeps old authored values) so the crack pattern
  can zoom, spin, pan, and re-seed. Rotation pivots on the blast's own Origin marker (not the canvas corner), then
  Drift pans along the — now possibly rotated — grid axes, matching `NoiseField`'s own rotate-then-drift order.
  `Seed offset` is the crack-pattern twin of `Layer.sparkleSeed`, rounded to whole STEPS before hashing (0.7 and
  1.4 both land on step 1) so an animated Curve jumps between a handful of distinct patterns over life instead of
  reshuffling into unrelated noise on every fractional change: Static freezes the pattern, Min-Max re-rolls a fresh
  step every frame for a boiling/crackling reshuffle. Its flat `crackTint` Color is now a `Gradient` with the same
  Over life / Fill split as Outline above — Fill paints the gradient across each seam's own width (0 = away from a
  seam, 1 = right on it) instead of a single flat tint. `Crack width` and `Cell shade strength` are now MultiCont
  (`ZUIValue`, animatable) rather than flat floats — continuous magnitude/blend knobs like these follow the
  existing package convention of being animatable (unlike discrete/waveform-character knobs such as Jaggedness or
  Warp, which stay plain floats since animating those reads as flicker, not motion). `Crack width`'s range was
  also raised from a 0–1 cap to 0–3, since the seam-distance it's compared against can itself exceed 1 near a
  cell's centre — the old cap meant even the maximum setting always left some untouched cell interior; the wider
  range can now genuinely erase cell interiors entirely, reading as one connected crack field.
- **Chromatic aberration gains an Alpha blend** (0 = untouched image, 1 = full effect, animatable — separate from
  `Amount`, the split distance itself) and `Angle` is now a MultiCont (`ZUIValue`, animatable — sweep the split
  direction) instead of a flat float.
- **Pyre preview settings (zoom, playback fps/speed, backdrop, test-backdrop sprite) are now saved on the BlastSpec
  asset itself** (`BlastSpec.previewX` fields, `[HideInInspector]`), not the Pyre window — each blast now
  remembers its own preview setup across switching assets, closing the window, or restarting the editor, instead
  of the whole window sharing one global preview state that reset unpredictably. `previewStageBg` is typed as a
  plain `Object` since `Laubrary.PreviewStage.PreviewBackground` is editor-only and Runtime code can't reference
  it (`PyreWindow` casts it back).
- **Same per-asset persistence extended to UI state: which layer's inspector is open, the left pane's scroll
  position, and the current preview frame** (`BlastSpec.previewLayerSel`/`previewScroll`/`previewFrame`) — opening
  an asset now resumes wherever you left it instead of always landing on layer 0. Layer selection explicitly
  dirties the asset on change (a discrete, meaningful pick worth reliably saving); scroll position and preview
  frame don't (they change continuously while dragging/playing — dirtying on every tick would leave the asset
  permanently "modified" just from watching a preview play), so those two persist for the session and whenever
  any other edit happens to save the asset anyway, rather than being guaranteed durable across every close.
- **Post modifiers (Bloom/Outline/Drop shadow/Chromatic aberration/Fuse) now work on individual LAYERS, not just
  the blast's global list.** A layer with its own enabled Post modifier renders into an isolated buffer first (so
  its post pass only ever sees/affects that layer's own pixels, never anything already composited below it), runs
  its post modifiers, then composites the result onto the frame (`BlastRenderer.FinishLayerPost`) — layers with no
  Post modifiers (the common case) skip this and draw straight into the shared buffer as before, so there's no
  cost when the feature isn't used. The editor's "+ Add modifier" menu no longer hides the Post group on a
  per-layer list.
- **`FuseModifier`** ("Fuse (blob melt)", Post) — a cheap, general "melt nearby shapes into one blob" effect: box-
  blurs the whole (premultiplied) frame, then re-thresholds alpha with a soft band so overlapping/nearby
  silhouettes' blurred halos cross the threshold together and read as fused, while an isolated shape mostly
  reconstitutes near its own edge. A pixel-space APPROXIMATION of MetaBlob's exact SDF-field fusion — much
  cheaper, and (unlike MetaBlob) works on ANY already-rendered pixels: any layer shape (even Bars/Sprite/
  NoiseField), any modifier stack, or — as a global modifier, now that Post modifiers can target a layer or the
  whole blast — several different layers melted together after they all composite. `Colour bleed` separately
  controls how much colour blends across the fused seam, independent of the silhouette fusion.
- **`ScatterMode.Rosing`** — a third scatter mode (`Layer.roseRings`, alongside Area/Ring) for a blooming-rose
  effect: an authored list of rings, each with its own Count/Radius/Birth/Life (mirrors `MetaOrb`'s birth/life
  exactly, just for a whole ring of shapes instead of one orb) — a few shapes close in and early, more shapes
  further out and later, blooming outward over the layer's life. Shares Ring's Start angle/Arc degrees/Ring order/
  Align rotation placement math rather than inventing a parallel set.
- **Ring/Rosing Disc layers gain a Fuse toggle** (`Layer.fuse`) — melts every shape into ONE gradient-shaded
  metaball field via the same SDF-sum → threshold → shade approach `RenderMetaBlob` uses for hand-placed orbs
  (a new `RenderFusedField`, sharing the pattern rather than the literal code — the two per-shape life models
  differ enough that forcing them through one function would obscure both), instead of compositing the shapes
  independently. Reuses MetaBlob's own Threshold/Shade range/Edge softness fields rather than adding a parallel
  set.
- **Copy/paste for modifiers** — every modifier row gets a Copy button; a Paste button appears next to "+ Add
  modifier" (disabled until something's copied). A single in-memory clipboard (last-copied wins, no asset/browser)
  that survives closing and reopening the Pyre window within the session, so a tuned modifier can be carried to
  another slot, another layer, or the global list without re-authoring it.
- **Crescent gains Outer softness + Bite softness** — it previously had no edge softening at all (`outerSoftness`/
  `innerSoftness` were only ever evaluated for Disc/SparkleField), even though the Hollow-disc "offset hole = a
  crescent" alternative already had both via Outer/Inner softness. Reuses those exact same two fields rather than
  adding new ones: Outer softness fades the same outer boundary Disc's does; Bite softness (the label shown for
  Crescent — same underlying `innerSoftness` field as Hollow's Inner softness) fades the edge where the mask disc
  bites in. No separate "bite size" scaling term needed (unlike Hollow's Hole size) since the mask disc is always
  the same size as the main one. Verified: 134 intermediate-alpha pixels with softness on vs. 0 with it off (a
  crisp cutout) on the same crescent.
- **`Layer.spinDegrees`** — a shape's own rotation over its own life (0..1 of ITS life span, via `t`), independent
  of Ring/Rosing placement entirely and available for Area scatter too. Adds onto whatever `Align rotation` set as
  the initial facing, so a shape can start aligned outward (via Align rotation) and then keep spinning from there
  under its own power — e.g. a Crescent that faces the ring's rim on spawn, then continuously turns in place.
  Verified: a Crescent animating Spin 0°→180° over its life rotated its bite direction by ~174° between its first
  and last frame while its own placement stayed fixed.
- **SparkleField gains a Blobs mode** (`Layer.sparkleBlobs`) — off (default) keeps the original single-pixel-per-
  frame twinkle exactly as it was; on, each sparkle becomes a small blob with its own multi-frame lifetime (grow
  in, hold, fade out — the same `MetaEnv` envelope MetaBlob's orbs already use) instead of one flickering pixel,
  and its radius fades in/out along with that same envelope so its "area of effect" breathes too, not just its
  brightness — real sparkles instead of static. Cell presence/phase are hashed off the shape's own seed rather
  than the existing (frame-reseeding) Sparkle seed, so a sparkle's identity stays stable across its own lifetime
  instead of rerolling into a different one every frame; `Blob radius`/`Blob life`/`Blob softness` are all
  animatable. Verified: pixel mode's frame-to-frame lit-pixel overlap (35px) matches the statistically-expected
  COINCIDENTAL overlap for a fully independent reroll (~41px), confirming no regression; Blobs mode's overlap
  (52px of 57-83px lit) is far higher, confirming genuine multi-frame persistence. Blobs mode also gets soft
  edges (50 intermediate-alpha pixels vs. 0 in pixel mode).
- **Rosing: each ring gets its own `Size` (×scale on the layer's own Size, 0.1–3)**, independent of the ring's
  `Radius` (placement distance). Previously the layer's shared `Size` field set every ring's disc size equally
  — no way to make an inner ring's discs small and an outer ring's big without also changing how far out they
  sit, since Radius and disc size were coupled through the single shared field. `RoseRing.sizeScale` multiplies
  onto `Eval(layer.size, ...)` only when Rosing.
- **Rosing: `Layer.roseReverseDraw` toggle** — flips which ring composites over which. Off (default, unchanged
  behaviour): rings later in the authored list draw on top of earlier ones (with the default 3-ring stack, that
  puts the outer/later-blooming ring in front). On: reversed, so earlier (typically inner) rings draw in front
  instead. Implemented by walking `roseRings` back-to-front when assigning each ring its flat shape-index (`si`)
  range in `RoseRingLookup` — the ring that claims the low end of `si` draws first (behind), the ring claiming
  the high end draws last (in front); a ring's own internal angle placement (`localIndex`/`ringCount`) is
  untouched, so this only ever changes depth ordering between rings, never the individual discs' draw order
  within one ring.
- **New ZUI control: `ZUI.StackedFloat`/`StackedInt`** (`ZUIStackedDragField.cs`) — a compact "label above,
  value below" numeric field for packing several short fields into a narrow column, where `ZUI.FloatField`/
  `Slider`'s wide inline "label : field" row doesn't fit. The label itself is the drag-scrub handle (click-drag
  horizontally to scrub, Unity's classic prefix-label-drag feel) rather than a separate grip icon — reuses the
  same scrub math as `ZUIDragField` (the internal drag-handle already behind every `ZUI.FloatField`/`IntField`)
  so the feel matches. Both a plain-value form (`ZUI.StackedFloat(label, value, width)`) and a typed-control
  factory for `ZUIForm`/`ZUIRow` (`ZUI.StackedFloat(label, get, set, width)`) are provided. (The ZUI reference
  doc's `ZUIStackedField` — a *different*, undragable "label above, control below" wrapper — turned out not to
  actually exist in the codebase despite being documented; this is a new, from-scratch control, not a fix to
  that one.)
- **Rosing's ring-list UI rewritten** (raw `EditorGUILayout` + manual `EditorGUIUtility.labelWidth` juggling
  before) — per the ZUI reference's own guidance that packing related short fields into one row is the default
  for any inspector-style panel, not one-field-per-row. Each ring is now a single untitled-box row — ring
  number, then Count/Radius/Size/Birth/Life/✕ — every field its own `ZUI.StackedInt`/`StackedFloat` (see the
  new-control entry above) instead of an unlabelled slider under a shared row label. Dropped the titled
  `Box("Ring N")` wrapper too (a titled box draws its own header line above the content, which was a second
  "row" in practice) in favour of an untitled `Box()` holding just the one line — each field still reads
  clearly and is individually drag-scrubbable by its own label, at a fraction of the old footprint.
- **`Layer.syncDeath`** — Area/Ring toggle: every shape reaches the END of its life at the layer's own End frame
  together, instead of each shape getting the same fixed duration (which, combined with Spawn stagger, makes
  later spawns end later too). Shapes born earlier now mature more slowly (a longer life) so the whole burst
  finishes on the same frame. Implemented by keeping the existing formula for spawn SPACING unchanged
  (`lifeForSpacing`, so Spawn stagger still controls how far apart consecutive shapes spawn) and only overriding
  the actual `life` each shape gets: `span - spawnAt` (floored at 1 frame) instead of the shared `lifeForSpacing`
  — algebraically this makes `spawnAt + life == span` for every shape, where previously only the LAST-spawned
  shape's end landed exactly on `span`. Composes oddly with a large Spawn stagger: a very-late spawn gets squeezed
  into a very short life to still die on time, so it can read as a pop rather than a fade — noted in the field's
  tooltip.
- **`ZUIValue2DControl` (new ZUI editor control)** — a synchronized 2D-position editor for a PAIR of ZUIValues,
  replacing two separate 1D sliders (hard to aim a position with) with one XY plot. Static mode = one draggable
  point + numeric X/Y fields + Reset; Curve mode = several numbered points connected by lines, tracing a path —
  unlike the 1D envelope editor, time isn't plotted at all here (both axes are spatial); it's implicit in point
  ORDER, evenly divided across the lifetime as points are added/removed. Deliberately reuses the existing
  `ZUIValue`/`ZUIEnvelopePoint` data model and `ZUIEnvelopeEvaluator` completely unchanged — x/y are two ordinary
  `ZUIValue`s kept in lockstep by this control, so nothing that already evaluates a `ZUIValue` (including every
  field in `BlastRenderer`) needs any changes to consume a pair authored this way. Folds to a one-line thumbnail
  by default (a shrunk dot/path preview, click to expand) — same affordance as the 1D envelope's curve thumbnail —
  so it no longer eats a fixed 140px of vertical space when you're not actively aiming it. When expanded, a fixed-
  width side panel (label, X/Y fields + Reset, or point-count/hint text in Curve mode, plus the mode and collapse
  buttons) sits LEFT of the XY plot at the same height, laid out horizontally, instead of stacking those rows
  above/below a tall plot. Reset (resets both X and Y to the field's default) is now available in Curve mode
  too, not just Static — collapses both point lists back to a flat 2-point path at the default value. Trialled
  first on
  Pyre's `Position X/Y`, then, confirmed to feel better, rolled out to every other X/Y field pair in Pyre: `Core
  offset X/Y` (gradient core), `Crescent offset X/Y`, `Hole offset X/Y`, and `Noise drift X/Y` (both the layer's
  own Noise fill and `AlphaMaskModifier`'s Noise mask). `AlphaMaskModifier.offsetX/offsetY` were left as plain
  float sliders (not `ZUIValue`s) since they're a different underlying data type — a plain-Vector2 variant of this
  control for non-animatable X/Y pairs is a candidate follow-up, not done here.

### Removed
- **Pyre: Wind drift** (`Layer.windX`/`windY`) removed — a directional push applied to every shape, growing with
  its age. Redundant with existing, more controllable tools (Position X/Y drift, Ground's grow-angle, per-shape
  Curve envelopes) and never found a use. UI box and backing fields both removed.

### Changed
- **Pyre: MetaBlob's gradient now has the same Over life / Fill / Flow fill options as Disc/Crescent** (`ColorMode`,
  Gradient position/zoom), replacing the old bespoke `metaFlow` bool. Fill and Flow fill now both honour Gradient
  position/zoom (previously only the flow-toggled path did); Over life is new for MetaBlob — one flat colour for
  the whole blob, sampled at the layer's own life, instead of always shading by surface→core field depth.
- **`SquashModifier` replaced by `ScaleModifier`.** Adds an Axis choice (Vertical / Horizontal / Both); Both scales
  both axes together from one shared value instead of needing two synced sliders. All three values (Vertical,
  Horizontal, Both) are MultiCont (`ZUIValue`), matching the old Squash's single animatable Amount.
- **`PixelInfo` gains `wx`/`wy` (the geometry-warped canvas position)**, and `VoronoiCrackModifier` now samples
  from it instead of the raw `x`/`y`. Previously a `PixelModifier` stacked after a `GeometryModifier` (e.g.
  Wobble → Voronoi crack) would only see the WARPED SILHOUETTE — the crack pattern itself stayed glued to the
  screen underneath it, since `x`/`y` are always the pre-warp canvas position regardless of any earlier warp in
  the stack. `wx`/`wy` are the same position AFTER that warp (equal to `x+0.5`/`y+0.5` when no geometry modifier
  is active, so this is a no-op unless one is stacked before Voronoi crack). Other canvas-anchored modifiers
  (Sunburst — deliberately canvas-centred; Dissolve; OrderedDither, whose Bayer matrix needs raw screen alignment
  to work at all) are unchanged; only Voronoi crack's own pattern-sampling switched to `wx`/`wy`.

### Fixed
- **Pyre: an animated Spawn radius moved shapes that had ALREADY spawned, instead of only affecting where NEW
  shapes appear.** `spawnRadius` was evaluated at the CURRENT FRAME's layer progress (`lp`) for every shape on
  every frame, rather than at each shape's own spawn moment — so a rising Spawn radius curve read as "the whole
  scatter field's positions scale outward over time" (every shape sliding together, every frame) instead of "the
  ring/area itself grows, and each new shape spawns further out than the last, while already-placed shapes stay
  put." Fixed by evaluating it at the shape's own spawn-time progress instead (already available via its
  `start` frame), so its scatter position — and therefore its Ring/Rosing angle-placement radius — is now
  effectively locked in permanently once it spawns. **`Start angle`/`Arc degrees` now get the identical fix, and
  unconditionally (no toggle)** — a `Lock angle at spawn` opt-in was tried and reverted earlier in this same
  Unreleased window (it read as pointless with `Align rotation` off, since it only ever moved position, which
  Spawn radius already covered) — but that framed it as a rotation feature. It's actually the same placement-vs-
  live-transform conflation Spawn radius had: Start angle/Arc degrees determine WHERE a new shape lands (a spawn-
  time decision), not an ongoing transform, so — like Spawn radius — they should always lock at spawn, full stop,
  not offer a choice. Animating them now changes where new shapes appear over time (e.g. a slow spiral bloom as
  the ring's reference angle drifts between spawns) without ever moving an already-placed shape. For the
  previously-available "whole ring visibly spins live" look, add a `Rotate` geometry modifier instead — that's a
  genuine live transform, and was always the semantically correct tool for that job.
- **Pyre: Crescent's mask-disc offset (Crescent X/Y) was a fixed PIXEL amount, so it desynced from the shape's own
  proportions the moment Size changed** — a large enough Size made the fixed offset barely bite into the disc
  (reading as nearly a full circle), a small enough Size made it overshoot the disc entirely (no crescent left at
  all). Changed to −1..1 of the shape's own (post-clamp) radius, the exact convention `Hole offset X/Y` (the
  Hollow-disc "offset hole = a crescent" alternative) already used — now Size changes scale the bite proportionally
  and the crescent's silhouette (sliver thickness/curvature) stays put. Verified: coverage fraction (lit px ÷ full-
  disc area) is 0.283 at radius 6 and 0.282 at radius 24 with the same authored offset, was wildly different
  before. Also switched its evaluation from the layer's life (`lp`) to the shape's own life (`t`), matching every
  other per-shape animatable value (Hole offset included) — it was the only one of these still reading `lp`.
  Default `crescentOffsetX` changed from `6` (px) to `0.45` (of radius) to preserve roughly the old look.
- **ZUI: `ZUIValueControl`'s "⋯" context menu always showed a `Multiplier ▸ (none)` submenu, even for hosts
  (like every Pyre `ValRow`) that never call `.WithMultipliers(...)`** — an extra click into a submenu with
  nothing useful in it, on every single value in the entire tool. The Multiplier feature itself is real and
  still used (e.g. `Zhowcase`'s demo), so the submenu now only appears when the caller actually declared
  multiplier ids. Also generalised: a "Mode ▸ ..." submenu only earns its own click when there's a second
  category (Multiplier) to disambiguate it from; with a single category, its items now sit directly at the
  menu's top level instead of behind a submenu holding literally everything in the menu. Pyre's `ValRow` menus
  (no multiplier ids ever) now show three flat top-level items with zero submenus. Swept every other
  `GenericMenu` in the package for the same anti-pattern (Pyre, Lazor, Zoetrope, and the rest of ZUI's editor
  windows) — only one other offender found: `ZUIFoldControls`'s fold-mode right-click menu nested both its
  options under a pointless single `"Expand on/"` category; flattened the same way.
- **Pyre: isolated single-shape preview** (`Layer` panel, "Shape preview" toggle, off by default) — shows
  exactly ONE of the selected layer's shapes, centred and rendered at max size for a fixed preview box,
  ignoring Count/Position/Spawn radius/Ring-Rosing placement entirely (scatter concerns, not the shape's own
  look). Seven toggles (Gradient Fill/Crescent/Hollow/Size/Spin/Alpha/Layer Modifiers) independently opt each
  aspect IN — reflecting its authored animation/value — or freeze it to a neutral default when off, so whichever
  aspect is currently a visual distraction can be isolated away while dialing in the rest. Purpose: a layer
  with a lot of scatter/movement and a high instance count makes it hard to visually verify any ONE shape's own
  intrinsic look; this strips all of that away. Layout: preview box on the left, the 7 toggles stacked 2-per-
  row (4 rows, last solo) on the right. Tracks the main transport's current frame (mapped into the layer's own
  life window) instead of a separate scrub control, so the normal Play/scrub/frame slider animates it too.
  New `BlastRenderer.RenderShapePreview`/`RenderShapePreviewTexture` — a dedicated, simplified render path (not
  a count-1 call into the main scatter loop) that reuses `RasterShape`/`RasterSprite` and the modifier-stack
  machinery, skipping spawn timing/scatter placement/the multi-instance loop entirely. Needed two small,
  backward-compatible `RasterShape` additions to reach: a `useGradientFill` param (defaults `true`, only this
  new caller ever passes `false`) so "Gradient Fill off" can force flat `OverLife`-style colour without
  touching the shared `layer.colorMode` state; Hollow/Crescent toggles needed no such change since they're
  already expressed as ordinary `holeSize`/`crescX`/`crescY` values RasterShape already takes as parameters, not
  fields it reads off `layer` directly. `BlastSpec` gains `previewShapeOn` + the 7 `previewShape*` toggle
  fields (per-asset UI state, same category as `previewZoom`/`previewLayerSel`/etc.). Verified headlessly (no
  visual screenshot yet): renders without exceptions on a Crescent layer, produces non-degenerate pixel
  coverage, and Size-off at an early life-progress (t=0.1, where the authored Size curve would still be small)
  shows MORE lit pixels than Size-on at t=0.5 — confirming the "pin to max regardless of the curve" override
  actually overrides rather than just reading through.
- **AssetKit browser thumbnails can now animate — opt-in per tool, zero effect on tools that don't.**
  `LaubraryAssetWindow<T>` gains `AnimateThumbnails` (virtual, default `false`) and `UpdateAnimatedThumbnail
  (item, tex, time)` (virtual, no-op default): when a subclass opts in, its cached browser thumbnail
  `Texture2D`s get mutated in place (`SetPixels32`+`Apply`) on a throttled ~12fps timer while the browser is
  visible, instead of being rendered once and frozen forever. The timer (`EditorApplication.update`, lazy-
  hooked exactly like the existing `projectChanged` hook) is only ever subscribed for a window whose
  `AnimateThumbnails` is true, so every other AssetKit tool (Larder, Choreographer, SpriteCatalog, Bestiarium,
  Lazor) pays literally zero cost — not even an extra subscribed delegate. Pyre is the first (only) adopter:
  `RenderThumbnail` still bakes the initial (middle-frame) texture as before, and the new
  `UpdateAnimatedThumbnail` override advances it through the asset's own baked frames at its own Preview fps,
  looping — so the browser grid shows every blast actually playing instead of one static pose. Verified: two
  frames of the SAME texture rendered 0.5s apart differ in 748/4096 pixels, confirming genuine frame
  advancement (not a frozen/no-op call).
- **Fixed: Pyre had essentially no Undo support for per-field dial edits.** Only structural list operations
  (Add/Remove/Reorder layer or modifier, Add/Remove rose ring) called `Undo.RecordObject` — every slider,
  toggle, gradient, MiniRadio, and `ZUIValue`/`ZUIValue2DControl` edit throughout the entire layer inspector
  (shape params, Colour/Alpha, RoseRings, per-layer Modifiers, Blast settings, Global modifiers) had NO undo
  at all: Ctrl+Z did nothing after dragging a slider. Root cause: `DrawLeft()` already wrapped its whole tree
  in `EditorGUI.BeginChangeCheck()`/`EndChangeCheck()` (to call `EditorUtility.SetDirty`), but never called
  `Undo.RecordObject` — and since Undo has to snapshot state BEFORE a control can mutate it, that single
  missing call at the top of the block (right after `BeginChangeCheck()`) was the entire gap for that whole
  subtree. Also added `Undo.RecordObject` to four preview-viewport drag interactions that live outside
  `DrawLeft()`'s span and so weren't covered by that fix: the origin `✛` handle, MetaBlob orb placement/drag,
  Pin warp pin placement/drag, and Smudge stroke painting (recorded once at the start of each gesture, so a
  whole drag undoes as one step) — plus the preview transport's own duplicate Frame-count slider. Recording
  `Undo.RecordObject` unconditionally every repaint (rather than only when a change is detected) is the
  standard pattern for a hand-rolled, non-`SerializedProperty` editor like this one — cheap, and Unity
  coalesces repeated no-op records so a slider drag becomes ONE undo step, not one per frame dragged. Note:
  the project's own dev guide already flagged "some Pyre/Larder dials" as a known gap to retrofit — Larder and
  Rulesets likely have the same issue and weren't touched here (out of scope for this pass).
- **Fixed: MetaBlob/Fuse's `Edge softness` washed the WHOLE FRAME with a faint uniform fill once raised past
  `Threshold`.** Both `RenderMetaBlob` and `RenderFusedField` anti-alias their iso-surface as `field >
  threshold - band` (band = Edge softness); once `band > threshold`, that lower bound goes negative, and since
  the metaball field is 0 (not undefined) everywhere far from any orb/fused shape, EVERY pixel in the buffer —
  not just near the blob's own edge — started satisfying the "in range" test and picking up a faint alpha.
  Fixed by clamping `band` to never exceed `threshold` (`Mathf.Clamp(layer.metaSoftness, 0.01f, threshold)`) —
  the formula is only valid below that point regardless of how far a user pushes the slider, so this is a
  correctness clamp, not an arbitrary cap. Affected MetaBlob orbs and Fuse (a Ring/Rosing Disc's metaball
  fusion) identically, since both share the same threshold/range/softness fields and formula.
- **Fixed: `OutlineModifier`'s `Inner softness` faded the WRONG way** — it multiplied the outward ring's alpha
  by `d / innerSoftness` (`d` = distance from the shape boundary, increasing outward), which is 0 right at the
  shape and ramps up moving AWAY from it — the exact opposite of "soften the inner edge," and visibly wrong: a
  transparent gap between the shape and its own outline. Inner softness is now a genuinely separate INWARD
  pass over the shape's own pixels (not the transparent ring at all) — full outline strength right at the
  boundary, alpha-composited OVER the shape's existing colour, fading back to the shape's own colour moving
  DEEPER IN over that many pixels. An inset glow, not a gap. Also added `Outer softness curve` (0.2–5, default
  1 = linear/unchanged) — raises the outward fade to this power, so it can stay near full strength longer and
  then drop off sharply right at the tail instead of a straight linear ramp.
- **`OutlineModifier` follow-up tuning**: `Edge alpha` renamed `Edge sensitivity` (same field, `alphaThreshold`
  — clarifies it's the control for WHERE the outline traces on a soft-edged shape, not a separate alpha
  setting easily confused with Inner/Outer softness sitting right next to it). `Inner softness` widened 0–4 →
  0–16 (to match Outer, and reach as deep as a user actually wants now that it's fixed) and gained its own
  `Inner curve` (mirrors `Outer curve` — same `Mathf.Pow` shaping, applied to the inward fade). `Size` can
  still go to 0 — now meaningfully, as "no outward ring, pure Inner-softness glow" (the `Apply` early-return
  is `(sz < 1 && innerSoftness < 0.001f)`, not `sz < 1` alone) — not a dead value like it was before Inner
  softness actually worked.
- **`OutlineModifier` gains `Inner softness`/`Outer softness`** — previously the outline ring had a hard cutoff
  at both its own edges: full alpha the instant it touched the shape's silhouette, and a hard clip exactly at
  `Size`. Inner softness (0–4px) fades it IN gradually from the shape boundary instead of starting at full
  strength immediately; Outer softness (0–8px, a larger cap — the outward fade typically wants to read as a
  longer glow/dissipation, while the inner edge against the shape usually wants to stay crisp) fades it OUT
  past `Size` instead of clipping there. Both default to 0 (unchanged, crisp behaviour). Required extending the
  neighbour-distance search radius to `Size + Outer softness` — pixels in the new outward fade band are past
  the old search window and would never find a shape pixel to measure from otherwise.
- **Pyre: `ValRow`'s Min-Max mode is now hidden for fields that don't actually vary per shape.** Min-Max is a
  per-shape-stable randomizer (`BlastRenderer.Eval` hashes it by shape index `si`) — genuinely useful when a
  field is sampled once per SHAPE, so several instances in one baked animation each land on their own
  fixed-but-different value (Spawn radius, Size, Spin, Sparkle density, Crescent/Hole offsets, ...). But a good
  number of fields are sampled only ONCE for the whole layer instead (`si` isn't in their hash at all) — Count,
  Start angle/Arc degrees (spawn-locked per-layer, not per-shape), every Bars knob except Forward reach, every
  MetaBlob/NoiseField field (no scatter/count at all), and Noise zoom/rotation on a Fused disc. For those, Min-
  Max was just picking one random-but-still-frozen number — no variety to see, just a de-facto Static value
  hidden behind a menu that implied otherwise. New `ValRow(..., allowMinMax: false)` (and a matching `perShape`
  flag threaded through the shared `DrawNoiseFillParams`) hides the option at exactly those call sites. Left
  the Modifiers panel (`DrawModifiers`) alone — modifier fields turned out to be a THIRD case: `Prepare()` bakes
  the current `frameIndex` into the Min-Max hash, so they re-roll every single frame (flicker/static) rather
  than landing on one frozen value OR varying per shape — a different semantic worth a separate conversation,
  not folded into this pass.
- **Pyre: the "keep shape on-screen" scatter clamp could silently cancel EVERY placement value** (Position, Spawn
  radius, Ring angle...) for a shape whose own radius reaches or exceeds the canvas half-size — a common case for
  a big filling layer (e.g. a "Smoke" layer sized to nearly cover the canvas). On a square canvas the clamp's
  valid range collapsed to a single point exactly at dead-centre once `radius >= half`, forcing every shape back
  to the same spot regardless of any authored offset, with no error or visual hint why. Full containment (`no
  clipping at all`) and any placement freedom are mutually exclusive once a shape is that big, so the clamp now
  only enforces full containment while it still leaves room to move (`radius < half`); past that point it backs
  off to the weaker "keep the centre on-canvas" guarantee instead of erasing the placement value outright.
- **PyreWindow gave no indication a selected layer was disabled** — its param panel looked and behaved fully live
  even when the layer's own `enabled` checkbox (in the layer list) was off, so any change made there (this is what
  originally read as "Position/Spin do nothing") had no visible effect for a completely unrelated reason: the
  whole layer wasn't drawing. Added a warning `HelpBox` at the top of the layer panel whenever the selected
  layer is disabled.
- **Pyre: `BloomModifier` ("Bloom (glow)") rendered a dark halo instead of a glow.** Its final blend combined the
  original straight-alpha colour with the additive glow, then stored the result at the new (higher) alpha WITHOUT
  dividing back down — since this codebase stores colour as straight alpha throughout (`BlastRenderer.Over` un-
  premultiplies explicitly), a pixel that started transparent ended up with straight colour ≈ glowValue at alpha ≈
  glowValue, which displays as glowValue² — a dim, dark smudge instead of a bright halo, worst exactly where the
  glow spreads into previously-transparent space around a shape. Fixed by blending in premultiplied space and
  dividing back down by the new alpha, matching how `Over` already does it elsewhere in Pyre.
- **Pyre: `NoiseField` showed a semi-transparent white/grey haze across the whole radius whenever Edge softness
  exceeded Threshold** (e.g. threshold < ~0.3 or softness > ~0.3). The density gate multiplied the noise value and
  the radial edge falloff together BEFORE thresholding, then compared the product to `threshold - softness` — once
  softness exceeded threshold that difference went negative, and since the product can't itself go negative the
  gate never excluded anything, so the whole disc rendered at a visible alpha (showing the gradient's low end,
  e.g. `SmokeGradient`'s pale grey). Fixed by computing the radial edge fade and the noise threshold as two
  independent, separately-bounded smoothstep bands (the same bounded pattern `AlphaMaskModifier`'s sharpness band
  already uses correctly) and multiplying their alphas together, instead of thresholding a combined value.
- **Pyre: MetaBlob's Spawn interval slider looked inert.** It only ever set a NEWLY placed orb's birth time
  (`orbCount x interval`) at the moment you click to place it — moving the slider afterward, or with orbs already
  placed, visibly changed nothing, since nothing re-reads it at render time. Added a "Renumber births" button next
  to it that explicitly re-applies the current interval to every existing orb, so the slider has an actual, visible
  effect on demand instead of only being observable one new orb at a time. (Tooltip also now says this plainly.)

## [0.8.0] - 2026-07-09

### Added (converged from Asteroid+)
- **Lazor** — vector line-art ("laser") shape authoring + rendering: `LazorShape` SO + layers/paths, a Shapes-free
  geometry/rasterizer core, and an editor window (grid canvas, per-layer mirror/symmetry, CRUD browser, SVG import).
  The Shapes rendering binding stays project-side. (Follow-up: add a `Bestiarium.Lazor` view bridge.)
- **RuleParams + `[GraphDropdown]`** — the "Story graph can only target exposed rule parameters" feature: `RuleParams`
  (Rulesets) discovers rule types + their exposed params (public scalars + `staticValue`/ZUIValue wrappers, duck-typed
  so Rulesets stays ZUI-free); `[GraphDropdown]` (Loom) + GraphEditor render annotated string fields as dependent
  dropdowns; PlotTwistPage rule/field are dropdowns; RulesEditorWindow renders ZUIValue tunables.

### Changed
- **Lazor window modernized to `ZUIWindow` + AssetKit.** `LazorWindow` now derives from `LaubraryAssetWindow<LazorShape>`,
  so its toolbar, the auto-refreshing thumbnail browser, and "show the browser when nothing's selected" come from the
  shared base and it matches the other editors. The left panel (tools / layer stack / selected-layer style + mirror) is
  drawn with the ZUI `Box`/`Label`/`Button`/`Toggle`/`Slider`/`EnumPopup` wrappers; the grid canvas stays raw IMGUI
  (legitimate custom painting). The old hand-rolled `LazorWindow.Browser.cs` was removed. Lazor's editor asmdef now
  references `AssetKit.Editor`, `ZUI.Editor`, `ZuiRuntime`.
- **Lazor canvas navigation:** middle-drag pans the canvas; a middle click (no drag) opens a quick popover at the cursor
  with Pen / Edit / Erase and **Undo / Redo** (Ctrl+Z can miss while the pointer is over the drawing surface).

### Fixed
- **Lazor canvas: strokes reaching off-screen points now draw (zoom-in fixed).** The canvas draws each segment as a
  horizontal `GUI.DrawTexture` quad that it then rotates, and IMGUI decides culling from the quad's *un-rotated* bounds.
  Zoomed in, a segment toward an off-canvas vertex had its un-rotated strip land entirely off-screen, so the whole line
  was culled — the "top triangle" of a shape vanished while the rest drew. Segments are now Liang–Barsky clipped to the
  viewport with **zero margin**, so both endpoints stay on/inside the view and the un-rotated quad always overlaps it and
  is never culled; the stroke thickness still covers the edge and `GUI.BeginClip` trims the overhang. (This, not stray
  strokes, was the real "detached lines" bug — verified segment-by-segment.)
- **Lazor window relaid out in pure GUILayout flow (fixes toolbar cut-off + wrong canvas size).** The split view used
  absolute rects carved from `position`, which could ride up over the AssetKit toolbar (cutting off Browse/New) AND, when
  wrong, fed a bad size into the canvas clip so strokes near the edge were dropped. It's now a plain `HorizontalScope`
  (left panel | splitter | expanding canvas) with `RootBoxStyle = null`, so the canvas rect is always correct for both
  clipping and mouse hit-testing.
- **Lazor canvas: segments exiting the right/bottom edge no longer vanish when zoomed.** Even clipped to the viewport, a
  line is a horizontal quad extended rightward from its start then rotated, so a segment clipped to the right edge had its
  un-rotated quad off-screen and IMGUI culled it. `GuiLine` now draws from the left-most endpoint, keeping the quad in
  view. (With this, every visible segment draws at any zoom — verified segment-by-segment against the live window.)
- **Lazor canvas: round joins/caps — no more messy, uneven-width corners.** Each stroke was drawn as independent straight
  quads with nothing at the vertices, so corners gapped and acute angles overlapped into blobs (very visible zoomed in).
  A soft filled disc is now drawn at every vertex (round joins, and round caps on open ends), so a stroke reads as one
  continuous, uniform-width line.
- **Lazor: stray/degenerate strokes no longer render or inflate bounds.** A single-point or all-coincident (zero-extent)
  stray click used to draw as a dot and, via its mirror copies, blow up the content bounds. `LazorGeometry.ResolveLayer`
  skips degenerate strokes (public `IsDegenerate`), the pen refuses a click on the last point's cell, and `FinishStroke`
  discards them; Fit frames the resolved content.
- **Lazor: Undo now covers every edit + refreshes the canvas.** Layer style, mirror, grid-resolution and name/enable
  edits go through `Undo.RecordObject` (they were dirtied without recording), and `undoRedoPerformed` repaints + reclamps
  the selection so an undo is actually visible instead of looking like a no-op.

## [0.7.0] - 2026-07-08

### Changed (BREAKING)
- **Colosseum → `Combat2D`.** The combat backbone (Health/Factions/Hitbox-Hurtbox/Projectiles) has no authoring UI,
  so it's plainly named now (new rule: only systems with a visual UI get cool names). Namespace `Laubrary.Colosseum`
  → `Laubrary.Combat2D`, asmdef `…Colosseum` → `…Combat2D`. Source-only (scene/asset GUID refs unaffected).
  **Consumers must update `using Laubrary.Colosseum` → `Laubrary.Combat2D`.**
- **Codex → `Bestiarium`, rebuilt fully pluggable.** The enemy/character-recipe layer keeps a cool name (it now has
  a browser UI) and is decoupled: its core depends on **Combat2D only**. A character's look (`ICharacterView`, with a
  dependency-free `SpriteView` in core) and its hit/death/muzzle/impact effects (`ICombatFx`) are `[SerializeReference]`
  seams. The old hard-coded Pyre+Chunks `CombatVfx` is now `PyreChunksFx : ICombatFx` in a new **optional** module
  `Bestiarium.Pyre`. `CodexArsenal` → `Bestiary`. So a project with only Combat2D (no Pyre/Chunks) can use Bestiarium
  with a sprite view and no effects. New editor hub (AssetKit): **Laubrary/Bestiarium/{Characters, Weapons, Projectiles}**.

### Added
- **ZUI rollout complete + expanded.** Every IMGUI editor window is now on `ZUIWindow` (added the Rulesets editor,
  the Zoetrope Zoe Browser / Animation Builder / Animation↔Aseprite, and the AssetKit-based tools). ZUI gained the
  field controls it lacked (`ZUIFields.cs`: TextField, ObjectField<T>, ColorField, EnumPopup/EnumField, Dropdown,
  IntSlider, Vector2Field/Vector2IntField, InfoBox/NoteBox, and a ScrollView scope) — so tools stop reaching past ZUI.
- **AssetKit** browsers now auto-refresh on `EditorApplication.projectChanged`, and New/Duplicate are Undo-able.

## [0.6.0] - 2026-07-08

### Added
- **ZoeCombat — the Zoetrope ⇄ Colosseum pixel-perfect bridge.** A tiny integration module (`Runtime/ZoeCombat/`,
  asmdef `com.Lautaro-Arino.Laubrary.ZoeCombat`) so neither combat backbone nor animation tool has to depend on the
  other. Its one component, **`ZoeHitFilter`**, implements Colosseum's `IHitFilter`: after the cheap checks
  (distance + collider overlap + faction) pass, a hit only lands if the contact point falls on a painted cell of the
  Zoe's `hurtLayer` meta-layer at the current frame (`ZonedAnimationPlayer.IsMetaPainted`). Optionally, when the
  attacker is itself a Zoe, it runs a full mask-vs-mask `PixelOverlaps` against an `attackLayer`. `acceptWhenUnresolved`
  keeps a misconfigured filter fail-open so a bad layer id never makes a target invincible. This closes the long-deferred
  Zoetrope pixel-perfect seam.
- **DaemonDemo: an authored Brain asset.** `Assets/Demos/DaemonDemo/DemoBrain.asset` (+ `DemoBehaviours.asset`) so the
  shared Loom graph window (**Laubrary/Brain Graph**) has a real graph to open, drag and rewire —
  Entry → Status → Wait[hasTarget] → Seek ⇄ Shoot, the authored twin of the in-code brain in `DaemonEnemyDemo`. A
  create-if-missing builder lives under **Laubrary/Demos/Build Daemon Demo Brain**.

## [0.5.0] - 2026-07-07

### Removed
- **Pyre: the per-layer Directional emission system** (origin line / bend / angle, emit angle / spread, travel) is
  gone — it predated and is superseded by the **Ground** modifier (directional growth) and **Bars**. Layers now use
  the simple radial scatter (Count + Spawn radius + Position) only; the "Emission" box is removed. (Old assets that
  used Directional emission now render as radial.)

### Changed
- **Pyre: Bloom + Outline post-effects (energy FX).** A new **PostModifier** family runs as a whole-frame pass
  after compositing (in the blast's **global** list, `Post/…` in the +Add menu) — the way neighbourhood effects a
  per-pixel modifier can't do become possible. **Bloom (glow)** blooms bright pixels into a soft additive halo that
  also lifts alpha (glows into the transparent surround); threshold / radius / animatable intensity. **Outline**
  draws a border in the ring around the silhouette — a flat colour = a sharp one-colour outline, a gradient
  fades/recolours/bands outward; animatable thickness. Together they make glowing energy orbs/projectiles/beams.
- **Pyre: Bars edge softness.** Bars get an animatable **Edge softness** — an alpha gradient on the bar **sides + tip**
  (the base stays hard so bars stay connected to their origin), for soft rays / flames. Applies in **Dissolve** mode
  too (composes with the centre-out fade).
- **Pyre: three more modifiers.** **Jagg** (geometry) pushes a circle out into an N-armed **star** (arms /
  strength / twist). **Smudge** (geometry) drags a patch of the shape one way like a finger through wet paint
  (origin / direction / size / strength). **Drop shadow** (post) composites a darkened, offset copy behind the
  shape for depth (offset / colour). All animatable where it makes sense.
- **Pyre: pan the preview frame.** Middle-drag moves the animation frame off-centre in the viewport (for composing
  against a test backdrop); Fit / **Centre** reset it. (Editor-only; not baked.)
- **Pyre: movable hole (offset hollow).** A hollow Disc's hole can be pushed off-centre (Hole offset X/Y, animatable)
  — an offset hole carves a **crescent**, so the Crescent shape is now reproducible with a hollow Disc (and can be
  retired). Inner-edge softness is measured from the offset hole centre.
- **Pyre: Wedge (pie) alpha mask.** The Alpha-Mask modifier gains a **Wedge** shape — removes an angular slice of
  `progress`·360° (0.25 = a pac-man, 0.5 = a half), rotation aims the mouth, sharpness feathers the cut.
- **Pyre: authorable blast Origin / pivot.** A BlastSpec now carries a normalized **Origin** (0,0 = bottom-left …
  1,1 = top-right, default centre) — the point that lands on the spawn position. Set it (sliders, or **drag the ✛
  handle** in the preview) to a directional blast's muzzle/back edge, and the runtime `BlastPlayer` + the baked
  sprites pivot there, so a game (Colosseum/Codex) aligns the blast to the exact hit pixel instead of its centre.
  The ✛ marker has an **opacity slider** and **oscillates white↔black** so it stays visible over any backdrop.

### Added
- **Pyre: MetaBlob layer — click-placed fusing orbs (SDF metaballs).** A new layer shape where you **click the
  preview to drop orbs** (in order); each contributes to a summed metaball field that is thresholded and **shaded by
  the gradient across the whole merged shape** (surface→core). Overlapping orbs **fuse** into one organic blob with
  smooth necks. Each orb has a **position, radius, birth** (set by placement order via a spawn interval) and **life**
  (grows in → holds → melts out), so the fused shape **grows and reshapes over time**. Controls: threshold (how
  eagerly they fuse), shade range, edge softness. Orbs are draggable in the preview (with radius rings + order
  numbers); geometry/pixel modifiers and the Bloom/Outline post-passes all still apply — great for organic
  fireballs, lava, smoke and energy blobs.
- **PreviewStage** — a reusable, editor-only **test backdrop** for any tool's preview: a `PreviewBackground` asset
  (fill + placed, tinted, scaled **sprites**) plus `PreviewStageGUI` (draw + drag-to-position + save/recall).
  Wired into **Pyre's preview**: a "Test background (sprites)" panel to add sprites, drag them behind the animation
  frame, and **Save / Recall** arrangements (stored under `Assets/PreviewBackgrounds/`). The backdrop is saved
  **separately** from the frame's own pan, so the same backdrop is reusable across tools and subjects. Each sprite
  has a **Front** toggle — draw it as a **foreground decoration over** the animation (occluding it) instead of
  behind. **Position the animation** against the backdrop by **left-dragging** it (the origin ✛ and stage sprites
  still grab first; middle-drag also pans).
- **Codex** — a composition layer that ties battle visuals together: small, portable ScriptableObject "recipes"
  that reference the primitives (Colosseum factions, Pyre blasts, Chunks debris) instead of owning art. Adds
  `Runtime/Codex/` (`com.Lautaro-Arino.Laubrary.Codex`, namespace `Laubrary.Codex`):
  - **CharacterDef** — an enemy/NPC: stats (max health, faction, i-frames), a placeholder look (Sprite now; Zoetrope
    Zoe swap-point marked), and **hit / death VFX**.
  - **WeaponDef** — fire stats (rate, damage, speed, spread, projectiles-per-shot), a projectile, and **muzzle VFX**.
  - **ProjectileDef** — a projectile's look (sprite, spin, face-travel), flight (lifetime, pierce, Chunks trail) and
    **impact VFX**.
  - **CombatVfx** — the shared VFX slot (a Pyre blast + a Chunks burst) played at a world point/direction; the one
    building block behind every Def's hit/death/muzzle/impact.
  - **CombatPresenter** — a runtime bridge that plays a CharacterDef's hit/death VFX from Colosseum's `Health`
    events (at the `DamageInfo` point + direction), keeping Colosseum agnostic. Zound refs are stubbed for later.
  - **CodexArsenal** + **ProjectileFx** — the factory that turns Defs into live objects: `SpawnCharacter`
    (Combatant + Health + Hurtbox + view + presenter), `BuildProjectileTemplate` and `EquipWeapon` (a configured
    ProjectileWeapon + a runtime projectile that spins and plays its impact VFX where it lands).
  - First step of the battle-authoring plan (the ColosseumDemo shooting gallery + a generic content browser follow).
- **Colosseum:** `ProjectileWeapon` now activates the instantiated projectile if the prefab was inactive, so
  runtime-built (Codex) projectile templates work as well as project-asset prefabs.

## [0.4.0] - 2026-07-06

### Changed
- **Gradient fill: position + zoom now work in plain Fill mode too** (not just Flow fill), and a new **movable
  gradient core** (Core offset X/Y, animatable) shifts where the gradient radiates from. Offset the core under a
  bright→dark gradient and a Disc becomes a **3D orb / energy ball** (bowling-ball highlight); animate the core for
  a moving hotspot. Great for energy-weapon projectiles/blasts.
- **ZUI envelope control** (reusable — used by every multi-control's Curve mode and `ZUI.CurveField`): hovering or
  selecting a point now shows its **value** (and time, when the x-domain isn't 0–1) in a tag beside the handle; and
  **edge points (first/last) are reliably grabbable** — MouseDown now accepts the same padding-expanded region that
  hover already did, so clicking the outer half of an edge handle (which sits on the plot boundary) no longer misses.
- **Pyre v2 — a modifier-stack rework** (cleaner to author, open to extend). Anything that distorts or recolours
  pixels is now an opt-in **PyreModifier** added to a layer or globally (`[SerializeReference]` polymorphic list),
  so new effects are just new subclasses:
  - **Geometry** modifiers (warp the grid): **Skew, Rotate, Squash, Wobble, Profile** — now apply to **every** layer
    type, Bars included.
  - **Profile (mold shape)** geometry modifier — sculpts a shape's silhouette by driving its horizontal **width at
    each height** from a spatial curve (0 = bottom → 1 = top), with an animatable **Strength** to blend the profile
    in over life. Turns a plain Disc into directional shapes — candle/teardrop flames, flickering campfire tongues
    (stack profiles + Wobble), or a **mushroom cloud** (thin stem → wide domed cap). Height is now measured in the
    shape's **own frame** (not the canvas), so the silhouette stays locked to the shape wherever it sits or grows,
    and composes with Ground. Each stacked layer can carry its own profile, so the inner plume differs from the outer.
  - **Ground (grow from surface)** geometry modifier — plants a shape's **base on a surface line** and grows it out
    from there (like Bars stream off an edge), instead of the shape being locked to the canvas centre. An animatable
    **Grow angle** aims the plume in **any direction** (0 = up, 90 = right, 180 = down, −90 = left) — the base roots
    on the corresponding edge/corner and the whole molded silhouette follows. `Surface` (−1 edge behind … +1 far
    edge) slides the base along that axis; an animatable `Stretch` scales the plume's height about the base (animate
    0→N and it shoots out); `Bury` sinks the base for a half-buried dome / ground burst. Pairs with Profile for
    surface-rooted candles, campfires (several grounded tongues + Wobble) and mushroom clouds. Geometry warps now run
    in a shape-aware context (centre + radius + canvas half-width/height); Ground applies before Profile via a
    warp-pass order.
  - **Rotate** now takes an optional **pivot** (Pivot X / Y in normalized canvas coords; 0,0 = centre = the old
    behaviour) so it can spin about any point instead of only the canvas centre.
  - **Pixel** modifiers (recolour / mask / remove): **Tint** (flat + cross-gradient + contrast/brightness/
    saturation), **Dissolve** (Erase / Fade / Bleed / Scatter modes), and a moving **Alpha Mask** (DiscOut / DiscIn
    / SwipeH / SwipeV with animatable progress, sharpness, size, rotation, offset).
  - The old per-layer + global **deform** and **colour-grade** boxes and the **disintegrate** slider are gone,
    replaced by the modifier stack (their engine code was deleted).
  - **Layer types merged**: **Disc** absorbs Circle/Ring/Sphere — a full disc, or a **Hollow** ring with an
    animatable **Hole size**, plus independent **Inner / Outer edge softness** (alpha gradients, 0 = sharp);
    **Ring** and **DissolvingDisc** retired.
  - **New Sprite layer type** — stamps a supplied sprite as spinning particles (a mini particle system), with a
    **New/Edit sprite (Aseprite)** button that creates a read/write PNG and opens it in Aseprite.
  - **Bars Star is now per-layer** (base angle / arms / spread arc live on the Layer; the canvas auto-fits every
    star layer's arms), instead of one global star for the whole blast.
  - **No built-in motion**: Size/Alpha default to grow→shrink envelopes and everything is an exposed multi-control;
    more params promoted to multi-controls (sparkle density, origin bend/angle, emit spread, base angle, spread,
    taper, stagger, thickness, dissolve, mask progress, …).
  - **Spawn stagger** (Count types): distributes the Count shapes across the layer's timeline (each spawns later
    with a correspondingly shorter life) — 0 = all live the full window, 1 = evenly spread first-frame→last-frame.
  - **Colour mode** for Disc/Crescent: **Over life** (one colour sampled over life — the original), **Fill** (the
    gradient fills the shape centre→edge, constant over life), or **Flow fill** (that spatial fill scrolls through
    the **mirrored** gradient — red→white→red, seamless — by an animatable **Flow position**, with a **Flow zoom**
    for how much of the gradient spans the shape).
  - Editor: a single **Modifiers** section (global + per-layer) with a grouped "+ Add" menu; label column widened
    so long labels don't clip; the **Image backdrop** now shows the whole image (ScaleToFit) instead of cropping.
  - **Global geometry modifiers warp the whole animation as one** — a modifier in the blast's **global** list
    (Rotate / Skew / Squash / Wobble) is applied as the **outermost** coordinate transform wrapping every shape, so a
    global Rotate spins the entire animation (all layers together) rigidly about one pivot while still composing
    correctly with each layer's Ground/Profile. Because it's a coordinate transform (not an image resample) it uses
    the full frame — a directional blast rotated into a wide frame fills the width instead of being clipped to the
    short side. (Global *pixel* effects still act per drawn pixel; Ground/Profile are per-shape and no-op globally.)
  - **Modifier lists are drag-reorderable** — grab a modifier's **≡** grip to reorder it within its list (per-layer
    or global), same as the layer list. Order matters (pixel modifiers apply in order; Ground still runs before
    Profile via its warp-pass).
  - **Simpler new-asset flow** — the redundant **New example blast** button is gone; **New asset** now shows an inline
    name field (suggesting "New Pyre") and saves the `.asset` with no file dialog, beside the current spec or in
    `Assets/Pyre`.
  - **Bars spacing is now in bar-widths** — Spacing = 1 means neighbouring bars **exactly touch** (no gap), 2 = a
    one-bar gap, etc. (centre-to-centre pitch = spacing × width). Width and Spacing both floor at 1 px / 1×, so the
    lowest setting is a single solid bar. (Was an independent pixel spacing that let bars overlap or drift apart.)
  - **Blast browser** — a **Browse** button swaps the dials/layers pane for a grid of every BlastSpec in the
    project (live thumbnails). Click one to preview it in the viewport; the toolbar renames / duplicates / deletes
    it or opens it for edit (double-click a cell also opens it).
  - **Layer library** — a **★** button on each layer row saves a deep clone of that layer to a shared, project-wide
    library asset (`Assets/Pyre/PyreLayerLibrary.asset`, created on demand); a **Recall…** button opens a popup that
    lists every saved layer with a live thumbnail and inserts a chosen one into the current blast (or deletes it).
    Lets layers be moved/copied between BlastSpecs.

## [0.3.0] - 2026-07-06

### Added
- **Colosseum** — a reusable **2D-combat backbone**: the generalised health/damage/factions/projectiles
  foundation the shmup, arena and store games all share. Adds `Runtime/Colosseum/`
  (`com.Lautaro-Arino.Laubrary.Colosseum`, namespace `Laubrary.Colosseum`): **Faction** (SO) — team
  hostility (friendly to self + allies, hostile to the rest; friendlyFire / hostileToAll; a null faction is an
  unaligned hazard); **DamageInfo** + **IDamageable**; **Health** — hit points with C# events (Damaged /
  Healed / Died) and designer UnityEvents (onHealthChanged, onDied), invulnerability + i-frames, lazy-init;
  **Combatant** — identity (faction + Health + optional hit filter); **Hurtbox** / **Hitbox** — regions that
  receive / deal damage (per-region multiplier for headshots; armed strike window, once-per-target);
  **Projectile** + **ProjectileWeapon** — a faction-stamped straight-flying bullet (pierce, wall blockers) and
  an emitter (fire rate, spread, burst, autofire); **Combat** — the one hit funnel (collider overlap → faction
  → optional pixel filter → apply) shared by everything; and **IHitFilter** — the seam Zoetrope plugs
  pixel/meta-layer detection into later. Death stays a fired event so Pyre / Chunks / animation / score react.
  Demo: **ColosseumDemo** — a playable shmup on the backbone (fly + fire; Choreographer-swept enemies that
  shoot back; deaths spawn Pyre explosions + Chunks debris; in-game tuning panel + HUD) — the intended
  replacement for the ChoreographerShmup. Ships zero art (shared DemoSprites).

### Changed
- **Pyre** — major bar/star pass: a **Star** on/off spread (bar arms share the centre and radiate outward as
  an asterisk; canvas auto-fits) replacing the old inward "orbit" circular spread; a **Taper** slider for the
  bar-arm silhouette (centre-longest → flat → concave) replacing the fiddly length-by-distance curve; arms
  drawn interleaved by bar index so overlaps stay symmetric; a **Bars "Dissolve"** decay (expand-hold then fade
  from the centre out); an experimental **colour grade** (cross gradient + contrast/brightness/saturation,
  per-layer and global); and more **animatable multicontrols** (base angle, spread degrees, taper, stagger,
  size, alpha). Bars no longer masquerade with a length curve; Global deform is hidden for all-bars blasts.

## [0.2.0] - 2026-07-04

### Added
- **Larder** — a tool for procedurally generating endless variations of pixel-art **shelf products**
  ("Wares") seen from the front, for store-loot / horde-shooter scenes. A **WareSpec** (ScriptableObject)
  describes one product by a small set of dials — **WareKind** (Book, Can, Box, Crate, Carton),
  **WareShape** (Rectangular, RoundedRect, Round, Spherical), **FillMode** (Solid, Gradient, InnerGlow,
  InnerShadow), a colour scheme (six palettes), a fake white scribble **Label** (Horizontal, Diagonal or a
  narrower CenterPatch), **Corner** treatments (colour triangle, rounded, cut-off), decorative **Bands** and
  spots, and an optional **Lid**. Adds `Runtime/Larder/` (`com.Lautaro-Arino.Laubrary.Larder`, namespace
  `Laubrary.Larder`): `WareGenerator` — one deterministic, `System.Random(seed)`-seeded painter shared by the
  editor preview, the baker and the runtime, so preview == bake == runtime — plus the **destruction system**
  (`ShelfWare`, `WareShake`, `WareDebris`): a shot product bursts into a cloud of pixels whose colours are
  sampled from its own texture, swaps to its next pre-generated **damage stage** (top torn off along an
  irregular scorched edge — 2–4 stages per ware), rattles, and can fling a broken chunk. Editor (`.Editor`)
  adds the **Larder** window under **Laubrary ▸ Larder**: live pixel preview, damage-stage strip, Randomize,
  and a never-overwrite sprite baker (writes **beside the spec asset**). Ships zero assets. Demo: a clickable
  "shoot the stocked shelves" scene stocked from real, editable `WareSpec` + baked-sprite assets (all flat in
  `Assets/Demos/LarderDemo/`, following the Laubrary demo layout).
- **Pyre** — a tool for baking pixel-art **explosion / hit animations** from a stack of timed **Layers**. A
  **BlastSpec** (ScriptableObject) is a flat back-to-front list of **Layers**; each Layer is a burst of shapes
  (**LayerShape**: Disc, Ring, DissolvingDisc, SparkleField, Crescent) alive over a span of frames, changing
  size, position, colour and alpha across life, optionally **disintegrating** (pixels drop out) at the end.
  Most numerics — count, spawn radius (0..1 of the blast, always kept on-canvas), position X/Y, start/end size,
  crescent offset, and every **deform** value — are **animatable** via the ZUI multi-control (a slider whose
  right-click menu switches it to a Min-Max random or an animation curve over the timeline). **Deform** (squash,
  skew, **rotation**, wobble) is available both globally and **per layer** as a toggled section. Adds
  `Runtime/Pyre/` (`com.Lautaro-Arino.Laubrary.Pyre`, namespace `Laubrary.Pyre`): `BlastRenderer` — one
  deterministic pure renderer shared by preview, baker and runtime (per-shape RNG hashed from
  seed+layer+shape) — plus `BlastPlayer`. Editor (`.Editor`) adds the **Pyre** window under **Laubrary ▸ Pyre**:
  a drag-resizable Layer list (select / toggle / duplicate / delete), per-field multi-controls, a resizable +
  zoomable preview with solid/gradient/image backdrops, and play/scrub/**retime** transport; a never-overwrite
  baker writes a sliced sprite sheet + looping **AnimationClip** beside the spec. Ships zero assets. Demo:
  a click-to-explode scene built from real `BlastSpec` assets (flat in `Assets/Demos/PyreDemo/`).
  Later additions: every animatable value (incl. **Size** and **Alpha**, now single multicontrols) can be a
  Static value, a Min-Max random, or an **animation curve** authored with the new foldable curve field; a
  per-layer **emission mode** — Radial or **Directional** (shapes stream off a bendable origin line/surface,
  Choreographer-style); an animatable **wind drift** that sweeps every shape one way over time; and an optional
  **radial alpha** falloff for soft-edged / hollow / haloed shapes.
- **Chunks** — a **runtime** tool that flings physical debris/shrapnel (the gameplay companion to Pyre: Pyre
  bakes the explosion sprite, Chunks throws the moving bits). A **ChunkSpec** (ScriptableObject) describes a burst
  — count, speed, a direction cone + upward bias, gravity / drag / spin, life, size / alpha / colour over life,
  chunk sprites (or a procedural pixel fallback), and a simple floor bounce / settle. `ChunkEmitter.Burst(...)` or
  the static `Chunks.Burst(pos, spec)` spawn independent `Chunk` behaviours that self-integrate gravity + drag,
  spin or face their velocity, bounce / rest on a floor, fade, and despawn; an optional colour palette tints them
  to a sampled object (the `WareDebris` pattern, generalised). Adds `Runtime/Chunks/`
  (`com.Lautaro-Arino.Laubrary.Chunks`, namespace `Laubrary.Chunks`); depends on neither Pyre nor Larder but can
  play any sprite they bake. Ships zero assets. Demo: click to spark-burst, Space for a directional wall burst.

### New ZUI controls (used by these tools)
- **`ZUIValueControl` / `ZUIValue`** multicontrol (a slider whose `⋯` menu switches to Min-Max random or an
  animation curve; the Curve mode folds to a thumbnail; optional flags hide the timing/range chrome).
- **`ZUI.CurveField`** — a foldable envelope curve field (thumbnail folded, full editor expanded) replacing
  `EditorGUILayout.CurveField`, on the ZUI-native point list.
- **`ZUI.FoldControls`** — a control group with always-visible + expand-on-arrow/hover members.
- **`ZUI.MultiToggle`** — a super-toggle over grouped sub-toggles (built on FoldControls).

## [0.1.0] - 2026-07-04

### Added
- **ZUI** — the editor UI framework (the `ZUI` global type: style sheets, sliders, colour pickers,
  envelopes, the Style Editor / Zeditor and the Zhowcase gallery) now ships **inside Laubrary** at
  `Zui/` instead of being hand-copied into each project's `Assets/ZUI/`. Install-path auto-detection
  resolves to the package location, so its SystemAssets (sheets, icons, fonts) travel with it.

### Changed
- **Runtime assemblies merged.** The editor toolkit's data layer (`ZUI.Runtime` — style defs,
  palettes, colours, envelopes, `ZUIAssetLibrary`) and the immediate-mode drawing toolkit
  (`ZuiRuntime` — `Zui`, `ZuiStack`, `ZuiMenu`, gamepad visualisers, `ZuiAudit`) are now a **single
  runtime assembly** `com.Lautaro-Arino.Laubrary.ZuiRuntime`. The old `ZUI.Runtime` assembly is
  retired; `ZUI.Editor` and Choreographer now reference the merged assembly. Type names and
  namespaces are unchanged, so consumer `ZUI.*` / `ZuiRuntime.*` call sites are unaffected.
  ⚠ Projects with a vendored `Assets/ZUI/` must delete it when adopting this version, or duplicate
  global `ZUI` types collide.

## [0.0.19] - 2026-07-03

### Added
- **Choreographer** (first slice) — a tool for authoring the collective motion of N **Dancers** as one
  reusable, unitless shape. A **Choreography** is a single **Path** (a Catmull-Rom spline, optionally
  arc-length paced for constant speed), a **Spread** (a start line that bends into a full circle), a
  **Facing** mode (Radial = paths fan out along the curve, Fixed = all parallel), plus population and timing
  (default count, duration, per-dancer **stagger**, Scatter/Gather direction, loop). It is N-agnostic:
  everything is a function of the normalised index `ni = i/(N-1)`, so the same asset drives 3 dancers or 300,
  packing tighter as N grows. Adds `Runtime/Choreographer/` (`com.Lautaro-Arino.Laubrary.Choreographer`,
  namespace `Laubrary.Choreographer`): `Choreography` (ScriptableObject), `ChoreographySampler` (the one
  `Evaluate(choreo, i, N, phase)` both the editor and runtime call, so preview == runtime), `PathCache`
  (dense arc-length table), and `ChoreographyPlayer` (drives a list of target Transforms through an anchor +
  world size). Editor (`.Editor`) adds the **Choreographer** window under **Laubrary ▸ Choreographer**: a
  looping 2D stage with path presets, drag-to-reshape control points, and toggleable visualisation — path
  lines, onion-skin, trails, index-coloured dots, facing ticks, and drop-in sample sprites. Ships zero
  assets; choreographies are authored into the host project. A Dancer is only ever a sampled pose.
- **Choreographer anchors (Launcher / Target)** — a choreography can optionally bind two world anchors,
  supplied by `ChoreographyPlayer` (`launcher`, `target` Transforms) or the preview. The path is the **spine** of
  the formation, anchored (translate-only) so its first point sits on the **Launcher** — the choreo keeps its
  authored orientation and scale. Dancers fan around the spine (spread tapers to zero over `launchBlend` so they
  converge at the launcher). **Target homing uses a retarget marker (`retargetAt`)**: a dancer flies the exact
  path until that progress, then the tail is course-corrected so its endpoint lands on the **Target** — sampled
  the moment THAT dancer reaches the marker, not at launch — while the spread converges to a point there. So a
  barrage fires from one launcher, fans out, then each missile commits and homes onto where the player is when it
  crosses the marker (dodge after that and it misses). The math lives in `ChoreographySampler`
  (`ChoreoAnchors` + `SamplePosition` + `FramePoint`); facing comes from the actual travelled direction. Demo
  adds a **Barrage** scene (`Build Demo Scene (Barrage)`) with a wandering Launcher and Target.
- **Choreographer preview honours the retarget freeze** — the editor preview now mirrors the runtime's per-dancer
  anchor freeze: each dancer locks the launcher at launch and the target when it crosses the retarget marker, so
  moving the target (e.g. dragging the marker) only re-aims dancers that haven't passed the marker yet — those
  already past keep the committed path. Preview == runtime for homing.
- **Choreographer preview no longer collapses** — dragging a launcher/target marker divides the mouse delta by
  the view scale, so once the auto-fit scale went small a single drag could fling a marker to huge coordinates,
  which shrank the view further until the path and handles were an invisible sub-pixel dot (path data itself was
  never affected). Preview markers are now sanity-healed each frame and all drags are clamped to a sane range.
- **Choreographer crash fix** — `ChoreographyPlayer` could throw `IndexOutOfRangeException` every frame after a
  domain reload (the per-dancer capture arrays fell out of sync); each array is now length-checked independently.
- **Demo builder guard rail** — `ChoreoDemoBuilder` no longer overwrites an existing choreography asset when a
  demo scene is rebuilt (it seeds fields only when creating the asset). A separate, confirm-first "Reset … to
  defaults" menu is the only path that overwrites. Adds a runtime **control UI** (`ChoreoDemoControlUI`, built with
  ZuiRuntime) to the barrage demo to steer the boss/player wandering in play mode, plus an "Add Controls to Open
  Scene" menu that injects it without rebuilding.
- **Choreographer anchors frozen per dancer** — `ChoreographyPlayer` samples the **launcher when each dancer
  launches** and the **target when that dancer reaches the retarget marker**, then holds each (`freezeAnchors`,
  default on). Later movement of those transforms no longer drags the whole choreography around, and each dancer
  homes onto where the target was at the instant it committed. Turn it off to track both every frame.
- **Choreographer preview toggles** — every stage element (grid, path+handles, spread, routes, onion, trails,
  dots, facing, sprites, anchors, blend marks) is now an independent toggle in a 2-column grid; with all off the
  stage is empty. Fixes the Dots toggle appearing to do nothing (facing ticks/other elements now have their own).
- **Choreographer editor on ZUI** — the window now extends `ZUIWindow` and its chrome (buttons, sliders,
  toggles, section headers, min/max-ready controls, enum radios) is built with ZUI, per the new "all UI is ZUI"
  rule; the 2D preview stage stays raw IMGUI. (Requires the `ZUI.Editor` / `ZUI.Runtime` asmdefs.)
- **Choreographer editor clarity** — the stage now draws the authored **path template** faintly (distinct from
  the vivid dancer routes, which apply spread/facing/anchors), so the shape you edit and the paths dancers
  actually travel are no longer conflated. **Click the path curve to add a control point, right-click a handle
  to remove one** (drag still reshapes); with Constant speed off, point spacing sets local speed. When a
  Launcher/Target is used, **blend-stage markers** show where the fan-out completes (progress = launchBlend)
  and where the converge begins (progress = 1 − targetBlend).

## [0.0.18] - 2026-07-03

### Added
- **UIAudit** — a vision-free UI linter that reports layout problems from the UI's own metrics
  (rects, text sizes) so an agent or test can catch them without a screenshot. Adds
  `Runtime/UIAudit/` (`com.Lautaro-Arino.Laubrary.UIAudit`, namespace `Laubrary.UIAudit`) with a
  section architecture: a **uGUI** section (Canvas/RectTransform/TMP — off-screen, text overflow,
  tiny text, list-needs-scroll) and an **IMGUI** section that lints ZuiRuntime-drawn immediate-mode
  UI. Menu under **Laubrary ▸ UI Audit**; entry point `UIAudit.Report()` / `UIAudit.Run()`.
- **ZuiRuntime draw recording (`ZuiAudit`)** — immediate mode has no retained tree, so ZuiRuntime
  now records one frame of its draws (double-buffered, zero cost when off) and the UIAudit IMGUI
  section lints that record for tiny text, text overflow, and off-screen elements. `ZuiStack` gains
  a `LabelIn` escape hatch (explicit-rect text, recorded so misuse is caught).
- **Gamepad scroll** (no version bump) — scroll stacks gain `Zui.ScrollBy` / `ScrollToReveal` /
  `IsScrolling` (viewport tracked), and `ZuiMenu` scrolls its focused item into view on navigation
  (focus-follows-scroll) and exposes `ScrollKey`/`IsScrolling` so an input driver can route the right
  stick to scrolling when a scrollbar is up and to navigation otherwise.
- **`ZuiGamepad` visualiser family** (no version bump) — the sibling of `FaceButtons` (XYAB): `DrawDpad`,
  `DrawStick` (left/right reusable), `DrawShoulder` (bumper+trigger, both sides reusable), and
  `DrawFullMap` composing them plus Start/Select into a full controller map. Show only the controls a
  situation needs (captioned = vivid, uncaptioned = dim) or the whole map.

## [0.0.17] - 2026-07-03

### Added
- **ZuiRuntime** — the runtime sibling of the editor ZUI toolkit: trap-aware immediate-mode
  (OnGUI) UI helpers for prototypes, adopted from TrueEye's nucleus and grown in ClaudeUI. Adds
  `Runtime/ZuiRuntime/` (`com.Lautaro-Arino.Laubrary.ZuiRuntime`, namespace `ZuiRuntime`, pure
  UnityEngine): `Zui` fill/contrast primitives, `UIScale` (one crisp font-based scaling rule),
  cached scaled styles, `ZuiStack` (text measured before drawn — labels cannot clip; button rows
  share one baseline), screen-clamped anchored panels, keyed scroll-stacks that auto-scroll when
  content outgrows the box, tint scopes, `ZuiOverlay` hotkey-overlay base, and the **XYAB gamepad
  face-button visualiser** (`FaceButtons` + `ControllerColors`) for gamepad games.
  ⚠ Projects carrying a vendored `Assets/ZUI/Scripts/Runtime/ZuiRuntime*` copy (TrueEye, ClaudeUI)
  must delete it when updating Laubrary, or the duplicate types collide.

## [0.0.16] - 2026-06-28

### Added
- **Zoetrope** — a new tool for authoring versioned 2D characters/animations (a *Zoe*) from
  sprite sheets, extracted from the retired AssetScavenge project. Adds `Runtime/Zoetrope/`
  (`com.Lautaro-Arino.Laubrary.Zoetrope`) and `Editor/Zoetrope/`
  (`com.Lautaro-Arino.Laubrary.Zoetrope.Editor`), including the Zoe Browser, Animation Builder,
  atlas baking, pixel-accurate meta-layer collision, and an Aseprite import/round-trip pipeline.
  Zoes are authored into the host project's `Assets/Zoetrope/…`; the package ships zero assets.

### Dependencies
- Added `com.unity.2d.sprite` and `com.unity.nuget.newtonsoft-json` (required by Zoetrope).

## [0.0.14] - 2026-02-16

Fixing still the monolith demo scene

## [0.0.13] - 2026-02-15

Fixed monolith demo messages

## [0.0.11] - 2026-02-15

Changed some demos to use the new input system.

## [0.0.11] - 2026-02-15

Changed some demos to use the new input system.

## [0.0.10] - 2026-02-15

The first release. But very alpha.

## [0.1.3] - 2026-02-15

Fixed Simplemenu and its examples

## [0.1.2] - 2026-02-15

Fixed references for default SimpleMenuUI settings asset.

## [0.1.1] - 2026-02-08

Alpha release. Very much alpha.

