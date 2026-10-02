# T-0272 — hosted Pyre parity re-check (64x64, seed 1234567, frames 0/mid/last of an 8-frame document)

Reference: a one-layer Pyre spec at Pyre's factory defaults through `PyreRenderer.RenderFrame`.
Candidate: the composite source's own `Render` at the same canvas, seed and phase.
"Visible diff" = a texel where either side has alpha>0 and the bytes differ.
"Exact diff" = any byte differs, including fully-transparent texels (RGB noise behind alpha=0 is invisible and harmless — confirmed here too).

| Family | Source | Worst maxCh (visible, 3 frames) | Worst visible diffs | Worst exact diffs (incl. transparent) | Coverage Pyre->Shaper (worst frame) | Verdict |
|---|---|---|---|---|---|---|
| Form | Inferno | 1 | 23 | 55 | 1848 -> 1848 | Same (+/-1 lsb) |
| Form | Fork Blast | 1 | 40 | 117 | 1410 -> 1410 | Same (+/-1 lsb) |
| Form | Orb | 1 | 1 | 1 | 432 -> 432 | Same (+/-1 lsb) |
| Form | Torch | 1 | 2 | 9 | 597 -> 597 | Same (+/-1 lsb) |
| Form | Arc Burst | 1 | 29 | 29 | 898 -> 898 | Same (+/-1 lsb) |
| Form | Plasma Bloom | 1 | 18 | 140 | 1495 -> 1495 | Same (+/-1 lsb) |
| Form | Jet | 1 | 21 | 21 | 397 -> 397 | Same (+/-1 lsb) |
| Form | Radial Jet | 1 | 49 | 49 | 1137 -> 1137 | Same (+/-1 lsb) |
| Form | Explosive Jet | 1 | 23 | 23 | 1027 -> 1027 | Same (+/-1 lsb) |
| Simulation | Fire | 255 | 52 | 52 | 52 -> 51 | Differs (colour only — see note) |
| Simulation | Fireball | 197 | 163 | 163 | 163 -> 163 | Differs (colour only — see note) |
| Legacy shape | Disc | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Gem | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Box | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Pyramid | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Can | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Orb | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Ring | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Crescent | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Star | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Polygon | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Streak | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Sparkle | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Sprite | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Text | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Fire | 0 | 0 | 0 | 0 -> 0 | Exact |
| Legacy shape | Fireball | 0 | 0 | 0 | 0 -> 0 | Exact |

Same-or-Exact: 25/27.

## Note on Simulation Fire / Simulation Fireball

These are the two `IShaperCompositeSource` implementations (`FireCompositeSource`, `FireballCompositeSource`),
distinct from "Legacy shape Fire/Fireball" above (which hosts a `PyreLayer{shapeForm=Fire}` through
`PyreLayerCompositeSource`, i.e. Pyre's own renderer verbatim — hence Exact).

A real bug was found and fixed here in this task: Pyre's own `RenderFireLayer`/`RenderFireballLayer`
(`PyreRenderer.cs:408`, `PyreRenderer.cs:765`) hash the sim's own RNG seed as
`Hash(spec.seed, layerSalt, 0, 0)` before stepping; both composite sources were handing the sim the raw,
un-hashed dial-eval seed instead. Fixed in `FireCompositeSource.cs:236` and `FireballCompositeSource.cs:144`.
Verified by reflecting into `FireSim`'s own private `heat[]` grid after replaying frames 0..4 side by side
with matching `FireParams` and the corrected seed: 0/4096 cells differ — the sim state is now bit-identical
once params match.

What remains after that fix is a colour-only difference (coverage now matches almost exactly: Fireball
163->163, Fire 52->51). Root cause: Pyre's own Fire/Fireball dispatch paints through
`layer.shapeFill.gradient` — the layer's generic Shape Fill (`PyreRenderer.cs:437`, `PyreRenderer.cs:797`).
`FireCompositeSource.ramp` / `FireballCompositeSource.ramp` (`FireCompositeSource.cs:146`,
`FireballCompositeSource.cs:72`) instead default to `PyreShaperSimSupport.DefaultRamp()`, an explicit
conversion of Pyre's "Ember" ramp preset — a deliberate authored choice documented in both files' own doc
comments ("converted from Pyre's own Ember preset... rather than by hand-typing a second set of stops...
a null ramp would make the sim render pure white and read as a broken generator"). This is not a bug
relative to any spec Pyre defines (Pyre hands Fire no special default ramp at all), so it was left as
authored rather than silently reverted. If exact colour parity is wanted here too, the one-line change is
`ramp = new PyreLayer().shapeFill.gradient` — traded against a worse out-of-the-box flame. Worth an explicit
owner call, not a silent revert; no question was filed since neither choice looks obviously wrong.
