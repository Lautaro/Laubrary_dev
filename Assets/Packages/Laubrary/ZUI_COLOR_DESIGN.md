# ZUI Colour tech — design & scope

**Status:** designed, not implemented. Discussed 2026-07-27. Build gated on a shader spike (see "Test-first gate"). Nothing built yet.

**One line:** a unified ZUI colour system — shared **swatches** (change once, updates everywhere, live at runtime), **editable gradients** (transform knobs + quantise), **colour cycling** (the old CLUT palette-animation trick), and **recolouring of existing sprites / Reels** — all resting on ONE foundation (a swatch palette + a version stamp) and delivered through ONE sprite-LUT shader, with pure-C# non-shader fallbacks so most of the value lands with zero shader risk.

## What Unity lacks (the motivation)

1. **Swatches** — designate a colour once, use it in many places, and when it changes it changes for everything using it. Wanted mainly at runtime, also in the editor. Plain (non-swatch) colours must stay the default. Unity has no such thing for content colours.
2. **Better gradients** — Unity's `Gradient` gives you the keyframe editor and nothing else: no reverse, no hue/brightness/contrast/saturation shift over the whole ramp, no quantise (fewer steps), no sharing. Plus **gradient swatches**.
3. **Colour cycling** — the classic retro palette-animation trick (Mark Ferrari / "Canvas Cycle"): a sprite's pixels are positions in a palette, and sliding/rotating the palette makes gradients flow (fire, water, rain). Wanted as a built-in gradient option, and applicable to **existing** sprites.

## The foundation — swatch palette + version stamp

`ZUIColorRef` (Runtime) already IS the swatch primitive: an inline `Color` OR a named reference into a palette, `Resolve()`d against a source, with **autocolors** (`ZUIAutoColor`/`ZUIColorHSL`) = named HSV-derived variants that track a base. The mechanism exists and is proven for editor theming; it's welded to the editor `ZUIStyleSheetAsset` and `ZuiFill` doesn't use it. The work is generalising it:

- A standalone **`SwatchPalette` asset** (a `LauAsset` — browsable / taggable / runtime-usable), reusing `ZUIPaletteColor` + autocolors verbatim. KEEP it **parallel** to the editor theme palette (shared machinery, separate sources) so content colour never depends on the editor skin.
- **Resolve-on-read, index-based**: a swatch-ref stores `(palette, index)` so a read is an array lookup, not a string hash. Autocolors are **pre-baked at the palette** (HSV runs once when a base changes, not per read) so readers always get a plain `Color`.
- **Version stamp**: the palette carries a `version` int, bumped on any change. Binders/cachers check the version and re-resolve lazily → a runtime swatch change is *free* (`version++`, no find-all-users / no event storm); steady state is an array index.
- **Inline fallback**: null palette / empty key → the literal inline colour, so "just use a plain colour" stays the default.

**Open decision:** runtime resolve model — per-swatch direct asset-reference only (my lean: clean for builds, no ambient singleton), or also a project-wide "active palette" convenience layer?

## Swatches live at runtime — no shader required

The general "swatches update at runtime" feature needs NO shader: a tiny **`ZuiSwatchBinding`** component watches a swatch and, on version change, pushes the resolved colour to the target's `MaterialPropertyBlock` / `Image.color` / `SpriteRenderer.color`. Works on any renderer or UI element. This is the easy case and rests entirely on the version-stamp foundation. The shader (below) is only load-bearing for the *gradient/ramp* case.

## `ZuiGradient` — editable gradients

Wrap Unity `Gradient` in a **`ZuiGradient`** = a base `Gradient` + a small NON-DESTRUCTIVE transform stack, applied at sample time so every op is a dial you can turn back: **reverse** (sample `1-t`), **hueShift / satMul / brightness (valMul) / contrast** (HSV maths already in `ZUIColorHSL`), **quantiseSteps** (round `t` to N buckets — the gradient twin of the Posterize pixel modifier), and a **phase/scroll** (`Evaluate(frac(t + phase))`, the hook for cycling). A "bake to plain `Gradient`" escape hatch stays. **Gradient swatches** = a palette entry that holds a `ZuiGradient` instead of a colour.

**Migration:** `ZuiFill.gradient` (Unity `Gradient`) → `ZuiGradient` via the lossless `ISerializationCallbackReceiver` recipe already proven on the ZuiFill zoom/centre change — byte-safe, existing assets unaffected. Editor: Unity `GradientField` for the base + a row of the transform knobs.

## Colour cycling — the CLUT trick, built in

A `cycle` option on `ZuiGradient` (enabled / speed / direction-or-pingpong / uses `quantiseSteps`) — understood as the default cadence the ramp *wants*; a driver on the target turns time into colour. A `ZuiGradient` can't cycle itself — data can't paint pixels or advance time — so a **driver applies it to the target**. Three delivery paths:

- **a. Shader / LUT (flagship, live)** — `ZuiGradient.ToLut()` bakes a 256×1 RGBA LUT; a Laubrary sprite shader samples `LUT[ frac(ramp + _Phase) ]` where `ramp` = the pixel's luminance (any art) or an **index channel** (true independent-band cycling — and **Pyre can emit the index natively**), quantising `ramp` for the stepped retro look; a tiny **`ZuiPaletteCycle`** component advances `_Phase` per frame via `MaterialPropertyBlock` (one shared material, per-renderer phase). The LUT rebakes on gradient/swatch **version** bump → live edits AND runtime recolour, no re-bake. Alpha lives in the LUT so the ramp can fade.
- **b. Baked frames (fallback, no shader)** — sample the gradient at N phase offsets, bake N sheet frames, play as a normal animation. Retro, universal, fits Pyre/Launimator's baked model; fixed steps + more texture memory.
- **c. CPU (SpriteFx)** — the SpriteFx runtime filter with an advancing phase, writing a `Texture2D` per frame. Flexible, per-sprite/per-frame CPU cost; fine for a few.

## Recolouring existing sprites / Reels

Existing art has fixed RGB and no index channel, so the unit of mapping is a **region → target**, and the target type depends on the art:

- **Flat region → swatch** — a solid-coloured area maps to one swatch.
- **Shaded region → gradient (luma / ramp-order driven)** — a cluster of related shades (e.g. water in 10 blues) maps to ONE gradient; each pixel's luminance (or its position in the artist's original palette ramp) picks where it lands, so **shading is preserved** and it's ONE assignment, not N swatches, and never flattens the shades to a single colour.

Two forms:

- **Form A — source-colour remap (default for Reels)** — map the sprite's *source colours* → swatches/gradients (a region = a colour or a hue-cluster). Keys on **colour, not position**, so it follows the animation automatically with NO per-frame masks — the decisive advantage on animated Reels. Baked to a remap LUT; live via the version stamp. Static recolour bakes source→final-colour; a **cycling** region stores a per-pixel **position** (source→position) and the shader samples the gradient LUT at `position + phase`. Region selection via auto **hue-clustering** ("grab the blues") / colour-range select. Ramp order = the artist's palette order when detectable, luminance as fallback, nudge-able.
- **Form B — painted override mask (fallback)** — paint regions → slots when source colour can't disambiguate (two materials sharing shades). Needs **per-frame masks aligned to the animation** (real authoring cost) — the exception, not the default.

**Home:** a **SpriteFx `ColorRemap` / `PaletteSwap` modifier** (source-table or mask, + optional cycle). Reels opt in via their SpriteFx stack / the Zoe body-fx slot, exactly like adding any other effect.

## Pyre integration — bake vs live

Two colour modes per fill / Pyre:

- **"Bake colour in"** — today's path, but the baked sheet becomes a tracked artifact of `(spec + palette)` (via `AssetDatabase.RegisterCustomDependency` / a ScriptedImporter hash), so a swatch change **auto-re-bakes only the dependent Pyres in the background** — no author toil. Editor-only, and a real background cost on big churns.
- **"Palette-mapped"** — bake an index/luma sheet (colour-agnostic); colour lives in the swatch/gradient LUT applied at draw. A swatch change recolours instantly, edit-time AND runtime, no re-bake — and it unlocks cycling for free. Pyre emits the index channel natively. Only for palette-reducible effects; adds a material.

## Delivery mechanisms — summary

| Target | Mechanism | Live at runtime? | Cost |
|---|---|---|---|
| Any renderer/UI, solid swatch | `ZuiSwatchBinding` (version stamp) | Yes (free on change) | 1 array index + a push on change |
| Sprite, gradient/ramp/cycle | sprite-LUT shader + `ZuiPaletteCycle` | Yes | 1 extra texture lookup / pixel |
| Any sprite, no shader wanted | baked N-frame cycle | No (fixed) | a normal animated sprite |
| Few sprites, flexible | SpriteFx CPU filter | Yes | per-sprite/per-frame CPU |
| Pyre, arbitrary colour | bake-colour-in + auto-rebake | Editor only | background re-bake on change |
| Pyre, palette effect | palette-mapped (index sheet + LUT) | Yes | 1 material + LUT lookup |

## Build order (phased)

1. **Foundation** — `SwatchPalette` LauAsset + version stamp + generalised `ZUIColorRef` (index-based) + inline fallback + `ZuiSwatchBinding`. Delivers runtime-live SOLID swatches. Low-risk C#.
2. **`ZuiGradient`** — transform knobs + quantise + phase; `ZuiFill.gradient` migration; the editor control; gradient swatches. Low-risk C#.
3. **Baked-frames cycling** — sample-at-N-phases → sheet. Gives cycling with zero shader. Low-risk C#.
4. **Shader spike (GATED — see below)** — prototype the sprite-LUT shader on ONE real sprite / Reel frame on the actual URP, verify visually, BEFORE any integration. The one high-risk piece.
5. **`ColorRemap` SpriteFx node** — Form A source-colour remap for existing sprites/Reels (region select, ramp mode, cycle). Builds on 1–4.
6. **Pyre palette-mapped mode + auto-rebake** — index-channel emit + the `(spec+palette)` dependency auto-rebake for bake-colour-in.
7. **Painted mask (Form B)** + polish — later / as needed.

Phases 1–3 (and 5's static path) are pure, compile-checked C# and deliver most of the value — live solid swatches, editable gradients, no-shader cycling, and static recolour of Reels — *without* the shader. The shader (4) is an additive win layered on a proven base, not a load-bearing gamble.

## Open decisions (need Lautaro's call)

- Runtime resolve: per-swatch asset-reference only, or also a project-wide "active palette" convenience?
- One `SwatchPalette` per project, or many named palettes?
- Confirm: content `SwatchPalette` stays **parallel** to the editor `ZUIStyleSheet` theme palette (shared code, separate sources).
- Shader: URP only, or a built-in-RP variant too? (All consumers currently URP / Unity 6.)
- Ramp-order default for shaded-region remap: detect the artist's palette order, or luminance?

## Risks / honest limits

- **The shader is the risk & maintenance centre** — no compile-time safety; breaks silently across pipeline (URP vs built-in), platform (mobile/WebGL precision), and sprite features (masking, sorting, atlas UVs, premultiplied alpha). Hence the spike gate. Everything else is C#.
- **Source-colour remap needs distinct source colours per zone** — clean limited-palette pixel art (typical Reels) is ideal; anti-aliased / continuous-tone art blurs colours together → use the mask or pure luma-ramp.
- **Painted masks need per-frame alignment** — real cost; keep as the exception.
- **Auto-rebake**: editor CPU churn on large swatch changes; editor-only (a shipped build's textures are fixed).
- **Palette-mapped Pyre**: only palette-reducible effects; adds a material.

## Test-first gate (agreed 2026-07-27)

Before committing to the shader integration, prototype the sprite-LUT shader on a **real Reel / Pyre frame on the actual URP setup** and verify it visually (render-to-PNG / window capture). If it fights the pipeline it's a lost spike, not a lost system — the bake-frames + CPU paths still deliver cycling, and phases 1–3 deliver the rest, all in pure C#.
