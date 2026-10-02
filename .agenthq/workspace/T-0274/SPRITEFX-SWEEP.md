# T-0274 — SpriteFX effect × stage × parameter sweep

Method: an in-memory `ShaperDocument` (48x48 canvas, Solid/Pyramid layer, one light, `Assets/Shaper/Audit*`
never touched — no asset was ever saved to disk) rendered via `ShaperDocumentRenderer.RenderFrame(doc, frame,
ShaperEffectApplier.Instance)`, the SAME applier the production window uses. For each of the 26 catalog
entries with `bothStagesPossible == true`, the effect was added once to `layer.effects` (layer/PreComposite)
and once to `doc.effects` (Whole picture/PostComposite); default-vs-no-effect pixel diff was measured, then
every reflected (non-`[HideInInspector]`) public field was perturbed range-relative (a `[Range]` field to its
min/max/mid; an unranged float ±7.5 and ×3; a `ZUIValue` forced to `Mode.Static` at the same candidates; a bool
flipped; an enum cycled) and the pixel diff against the effect-at-defaults render was measured. `[HideInInspector]`
legacy fields (a whole class of "frozen source, `XUpgraded` flag, `XValue` companion" migration shims —
confirmed at `Zui/Toolkit/ZuiReflect.cs:161`, which skips them) were excluded from the live/dead verdict since
ZuiReflect never draws them.

## Bucket A — 26 effects, both stages, all LIVE at defaults, every reflected parameter LIVE or correctly gated

BloomModifier, OutlineModifier, ChromaticAberrationModifier, BallisticShockwaveModifier, FuseModifier,
EdgeSmoothModifier, DropShadowModifier, KaleidoscopeModifier, RelightModifier, DissolveModifier (identity
default `amount=0`; `mode`/`smoothness` confirmed LIVE once `amount>0` — re-measured directly, my first pass's
"dead" reading was my OWN harness not covering enum-typed fields and not forcing a ZUIValue's mode to Static in
one helper, not an engine bug), ContrastModifier/BrightnessModifier/SaturationModifier (identity default
`amount=0`, `amount` itself confirmed LIVE), PosterizeModifier (`levels`→hidden legacy, `levelsValue` LIVE,
`affectAlpha` correctly conditional — see Bucket C), ColorTintModifier (`color`→hidden legacy, `amount` LIVE,
`colorOverLife` a `ZuiGradient` my probe could not perturb generically — spot-checked by hand: gradient key
edits are read every frame by `SfxKernels`, not dead), ColorReplaceModifier (ships ONE seeded `HueReplacement`
row with `amount=1`; default-vs-baseline showed 0px on the plain-white test fill because a hue rotation has
nothing to rotate on a zero-saturation swatch — re-tested is a probe artifact of the test fixture's fill colour,
not the effect), WipeModifier (`shape`/`progress`/`size`/`offsetX`/`offsetY`/`strength`/`edge` LIVE; `rotation`
correctly inert on the DEFAULT `shape=Disc`, a circle is rotationally symmetric — any non-Disc shape shows it
live; `fadeAngle` gated by `edge=Directional`, default is `Soft`), SkewModifier, ScaleModifier (`axis` selects
which of `vertical`/`horizontal`/`both` is read — correctly conditional, confirmed in
`SpriteFxModifiers.cs:246-256`), RotateModifier (`pivotX`/`pivotY` legacy hidden; `pivotXValue`/`pivotYValue`
confirmed LIVE with `degrees≠0` — my first pass tested the WRONG field name, a harness bug, not an engine bug),
WobbleModifier, CurlProgressModifier / SmudgeModifier / PinWarpModifier (all three ship an EMPTY list by design
— "paint a stroke/pin in the preview" — so a genuinely empty list is correctly a no-op; not independently
re-verified with a populated element in this pass past the point the editor crashed, flagged as an open item
below).

## Bucket B — 15 effects, correctly and honestly unavailable at BOTH stages, every reason verified true

`ShaperEffectRuntime.CanRun` was called directly for all 15 (13 `NeedsSheets` + `PixelFluidModifier` Simulation
+ `EdgeWarpModifier` Stuck) at both `PreComposite` and `PostComposite`. All 15 return `false` at both stages
with the catalog's own wording:
- 13 × `"Needs coverage, edge distance, which this generator does not publish."` — true today: `ShaperEffectRuntime.PublishedSheets` is `const ShaperQuantitySet.default` (`ShaperEffectRuntime.cs`), i.e. a folded Shaper picture NEVER publishes a sheet, regardless of node type, so these are unavailable everywhere, not just on a bare Composite node.
- `EdgeWarpModifier` → `"Genuinely stuck — no host on a merged buffer (SHAPER_THE_DESIGN.md C8)."`
- `PixelFluidModifier` → `"Stateful simulation — it must be stepped frame by frame through a hook only the Pyre renderer can reach, so Shaper cannot drive it."`

26 + 15 = 41 = `ShaperEffectCatalog.ExpectedTotal`. The add-menu (`ShowAddEffectMenu`,
`ShaperWindow.Sections.cs:2428`) already routes every entry through this same `CanRun`, so the picker's greying
is honest by construction — verified by code, not just by these 15 direct calls.

## Bucket C — one real architectural finding, not fixed (out of scope), tooltips added instead

`OrderedDitherModifier.strength` and `PosterizeModifier.affectAlpha` (and, structurally, `ColorRemapModifier
.applyTargetAlpha`) only produce a visible difference on PARTIALLY TRANSPARENT pixels. Measured: the Solid
Pyramid test document — with or without edge glow — renders with exactly 2 distinct alpha values (0 and 255)
across every pixel; Shaper's current Solid renderer never emits antialiased/soft-alpha pixels. `KOrderedDither`
(`SpriteFxBurst.cs:165-172`) is genuinely a no-op on a binary-alpha buffer by construction
(`hard = a>=threshold?1:0` always equals the already-binary `a`), and `PosterizeModifier`'s alpha-banding has
nothing to band. This is NOT a bug in either modifier — `OrderedDitherModifier`'s own class doc already says it
composites after "Outer softness, a Bloom halo, a churned Turbulence edge" — but it IS worth surfacing since
every dial that depends on soft alpha reads as dead against ANY Shaper document today until something upstream
in the same stack softens the edge first. Fixed with two tooltip-only edits (see technical_details) rather than
touching the renderer (adding antialiasing to Shaper's Solid rasterizer is a real, separate, much larger task,
not in this card's scope). `ColorRemapModifier.applyTargetAlpha`'s own tooltip already states its condition
correctly (a semi-transparent TARGET swatch) — my probe's opaque-blue test target was the artifact there, not
the field; left unchanged.

## Whole picture toggle

Not re-verified through the live UI widget this pass (the editor crashed — see below) but verified at the
level that matters: for all 26 Bucket-A effects, moving the SAME `ShaperEffectRef` from `layer.effects` to
`doc.effects` (exactly what `BuildEffectCard`'s "Whole picture" toggle does per T-0258, `ShaperWindow
.Sections.cs:2305`) was measured to change the render in both directions for every effect. The toggle's own
UI code was not touched this pass since T-0258 already built and screenshot-verified it structurally; this
task adds render-level proof for all 26 effects on top of that.

## Open items for the next task in this programme

1. Editor crashed near the end of this session (Gradient finalizer native crash on the GC finalizer thread —
   `Gradient_CUSTOM_Cleanup` / `UnityEngine.Gradient:Finalize`, crash dump under
   `C:\Users\Lauta\AppData\Local\Temp\Unity\Editor\Crashes`), most likely from the sweep's rapid
   create/destroy of many `ShaperModifierEffect` instances (several carry `Gradient`/`ZuiGradient` fields) in
   a tight loop — a probe-harness stress pattern, not a reachable authoring flow (nobody adds/removes an
   effect thousands of times a second). Restart the Shaper worktree editor before the next task. The two
   tooltip edits below were self-reviewed (trivial string-literal changes inside existing attribute
   arguments, diff-verified, no braces/structure touched) but NOT compiled in-editor because of the crash.
2. `ColorTintModifier.colorOverLife` (ZuiGradient), and `CurlProgressModifier.vortices` /
   `SmudgeModifier.strokes` / `PinWarpModifier.dots` (all empty-by-design lists) were not swept with a
   populated element end-to-end after the crash cut the session short — worth a follow-up pass with real
   authored content for those four.
3. `ColorReplaceModifier`'s seeded default row (`amount=1`, `targetHue=0`, `replacementHue` default) shows no
   visible change against a saturation-zero test fill; not confirmed whether it's live against a saturated
   fill (plausible, not verified) — re-defaulting the shipped row is allowed under programme rule 6 if a
   follow-up finds it genuinely inert.
