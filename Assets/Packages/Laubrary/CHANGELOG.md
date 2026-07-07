# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.0] - 2026-07-06

### Changed
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
  - **Ground (grow from surface)** geometry modifier — plants a shape's **base on a flat surface line** and grows it
    **up** from there (like Bars stream off an edge), instead of the shape being locked to the canvas centre.
    `Surface` (−1 bottom edge … +1 top) places the line; an animatable `Stretch` scales the plume's height about that
    base (animate 0→N and it shoots up out of the surface); `Bury` sinks the base for a half-buried dome / ground
    burst. Pairs with Profile for surface-rooted candles, campfires (several grounded tongues + Wobble) and mushroom
    clouds (a grounded thin stem + a wide dome cap grounded higher). Geometry warps now run in a shape-aware context
    (centre + radius + canvas half-height); Ground applies before Profile via a warp-pass order.
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
  - **Simpler new-asset flow** — the redundant **New example blast** button is gone; **New asset** now shows an inline
    name field (suggesting "New Pyre") and saves the `.asset` with no file dialog, beside the current spec or in
    `Assets/Pyre`.
  - **Bars spacing is now in bar-widths** — Spacing = 1 means neighbouring bars **exactly touch** (no gap), 2 = a
    one-bar gap, etc. (centre-to-centre pitch = spacing × width). Width and Spacing both floor at 1 px / 1×, so the
    lowest setting is a single solid bar. (Was an independent pixel spacing that let bars overlap or drift apart.)
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

