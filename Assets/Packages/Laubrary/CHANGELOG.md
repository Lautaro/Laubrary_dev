# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
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

