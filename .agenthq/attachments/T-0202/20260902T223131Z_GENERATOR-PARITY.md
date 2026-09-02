# T-0202 — generator parity: Shaper hosting vs Pyre's own window path

Canvas 128x128, 16 frames, seed 1234567, frames probed 0/8/15.
Reference is a one-layer Pyre spec at Pyre's factory defaults through `PyreRenderer.RenderFrame`.
Candidate is the composite source's own `Render` at the same canvas, seed and phase.

Verdict `Same` means no VISIBLE difference: every disagreeing texel is fully transparent
on both sides, where no consumer can read it (a composite's coverage IS its alpha, and
`SampleCompositeColour` filters premultiplied, weighting a zero-alpha texel to nothing).
`Exact` is the stricter byte-for-byte test, transparent texels included.

| Family | Generator | Verdict | Exact | Visible diffs (rgb/alpha) | Worst channel | Colours Pyre -> Shaper | Coverage Pyre -> Shaper | Note |
|---|---|---|---|---|---|---|---|---|
| Form | Arc Burst | Same (±1 lsb) | no | 0/0 / 127/0 / 0/0 | 1 | 0->0 / 441->441 / 0->0 | 0->0 / 3132->3132 / 0->0 |  |
| Form | Explosive Jet | Same (±1 lsb) | no | 0/0 / 71/0 / 0/0 | 1 | 0->0 / 1994->1995 / 0->0 | 0->0 / 4105->4105 / 0->0 |  |
| Form | Fork Blast | Same (±1 lsb) | no | 0/0 / 284/0 / 0/0 | 1 | 0->0 / 2391->2393 / 0->0 | 0->0 / 8093->8093 / 0->0 |  |
| Form | Inferno | Same (±1 lsb) | no | 0/0 / 97/0 / 0/0 | 1 | 0->0 / 6328->6328 / 0->0 | 0->0 / 7644->7644 / 0->0 |  |
| Form | Jet | Same (±1 lsb) | no | 0/0 / 76/0 / 0/0 | 1 | 0->0 / 1116->1118 / 0->0 | 0->0 / 1621->1621 / 0->0 |  |
| Form | Orb | Same (±1 lsb) | no | 0/0 / 6/0 / 0/0 | 1 | 0->0 / 384->384 / 0->0 | 0->0 / 1605->1605 / 0->0 |  |
| Form | Plasma Bloom | Same (±1 lsb) | no | 0/0 / 145/0 / 0/0 | 1 | 0->0 / 3752->3754 / 0->0 | 0->0 / 5585->5585 / 0->0 |  |
| Form | Radial Jet | Same (±1 lsb) | no | 0/0 / 179/0 / 0/0 | 1 | 0->0 / 2911->2912 / 0->0 | 0->0 / 4531->4531 / 0->0 |  |
| Form | Torch | Same (±1 lsb) | no | 0/0 / 4/0 / 0/0 | 1 | 0->0 / 287->287 / 0->0 | 0->0 / 2266->2266 / 0->0 |  |
| Legacy shape | Disc | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 81->81 / 0->0 | 0->0 / 1340->1340 / 0->0 |  |
| Legacy shape | Gem | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 742->742 / 0->0 | 0->0 / 1550->1550 / 0->0 |  |
| Legacy shape | Crescent | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 188->188 / 0->0 | 0->0 / 940->940 / 0->0 |  |
| Legacy shape | Sparkle | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 1->1 / 0->0 | 0->0 / 470->470 / 0->0 |  |
| Legacy shape | Sprite | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 81->81 / 0->0 | 0->0 / 1340->1340 / 0->0 |  |
| Legacy shape | Box | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 471->471 / 0->0 | 0->0 / 3076->3076 / 0->0 |  |
| Legacy shape | Pyramid | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 749->749 / 0->0 | 0->0 / 1584->1584 / 0->0 |  |
| Legacy shape | Can | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 961->961 / 0->0 | 0->0 / 2588->2588 / 0->0 |  |
| Legacy shape | Orb | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 579->579 / 0->0 | 0->0 / 1992->1992 / 0->0 |  |
| Legacy shape | Ring | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 779->779 / 0->0 | 0->0 / 1768->1768 / 0->0 |  |
| Legacy shape | Text | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 27->27 / 0->0 | 0->0 / 585->585 / 0->0 |  |
| Legacy shape | Streak | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 7->7 / 0->0 | 0->0 / 48->48 / 0->0 |  |
| Legacy shape | Star | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 105->105 / 0->0 | 0->0 / 462->462 / 0->0 |  |
| Legacy shape | Fire | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 67->67 / 0->0 | 0->0 / 74->74 / 0->0 |  |
| Legacy shape | Fireball | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 136->136 / 0->0 | 0->0 / 395->395 / 0->0 |  |
| Legacy shape | Polygon | Same | yes | 0/0 / 0/0 / 0/0 | 0 | 0->0 / 7->7 / 0->0 | 0->0 / 900->900 / 0->0 |  |
| Simulation | Fire | Differs | no | 0/0 / 11/64 / 0/0 | 255 | 0->0 / 67->50 / 0->0 | 0->0 / 74->59 / 0->0 |  |
| Simulation | Fireball | Differs | no | 0/0 / 4/394 / 0/0 | 255 | 0->0 / 136->117 / 0->0 | 0->0 / 395->325 / 0->0 |  |

Same (no visible difference): 16 / 27.
Same to within ±1/255 on partially transparent texels: 9 / 27.
**Matching Pyre: 25 / 27.**  Remaining: Simulation Fire, Simulation Fireball
Exact (byte-for-byte incl. transparent texels): 16 / 27.

## Why the ±1

Measured over the nine forms at the mid frame: of every texel that disagrees, **not one is
fully opaque** — the highest alpha among them is 251, never 255. That is the signature of a
premultiply → un-premultiply round trip, which is exact at `a == 255` and loses up to 1/255
below it. Pyre composites its layer through that round trip; the bridge hands the form's own
output straight out and does not. The bridge is therefore the more faithful of the two, and
reproducing Pyre's loss would mean degrading the picture to make a number read 100%.

## Two findings that are not in the table

**1. T-0201's "colour is lost between the source raster and the composite paint" is a false
positive.** The tell it rests on is `TestDiscForm`'s colour dials doing nothing. `TestDiscForm`
lives in `Assets/Tests/Pyre/PlusCapabilityTests.cs:13` and is a test fixture whose `Render`
writes a literal `new Color32(255, 128, 0, 255)` — it ignores `ctx.fill` and its own `ramp`
field by construction, so its colour dials genuinely cannot do anything, in Shaper or in Pyre.
It is not offered in Shaper's picker either (43 entries, none of them a test fixture), so no
owner can reach it.

The albedo path itself was measured end to end and is intact. Building a real `ShaperDocument`
whose layer root is a composite, and comparing the source raster with the finished document
picture:

| generator | raster colours | baked colours | albedo bound | document colours |
|---|---|---|---|---|
| Inferno | 3354 | 3354 | yes | 3354 |
| Orb | 188 | 188 | yes | 188 |
| Plasma Bloom | 2181 | 2181 | yes | 2181 |
| Fork Blast | 1268 | 1268 | yes | 1268 |

Colour survives 1:1 from `IShaperCompositeSource.Render` through `ShaperCompiledComposite.pixels`,
`ShaperCompositeAlbedo.From` and `FillCompositeAlbedoTile` into the finished picture. Nothing in
`ShaperCompiler.cs:971 → ShaperFillResolver.cs:433 → FillCompositeAlbedoTile` needed changing, and
nothing there was changed. What the owner was seeing was the hosting stub this task fixed: the
generators were being handed no fill, no alpha and no frame index, so they had little colour to
send in the first place.

**2. At Pyre's defaults the first and last frame of a hosted generator are EMPTY — on both
sides.** Every contact sheet shows one populated column for that reason, not because two of the
three renders failed. Pyre's factory Alpha envelope for a layer fades in from zero and out to
zero (`Pyre.cs:325`, `DefaultAlpha`), so frame 0 and frame 15 of a 16-frame clip carry no
coverage in Pyre's own window either — the probe measures 0 covered texels on both sides at both
ends. This is faithful, but it is worth the owner knowing it is the DEFAULT and not a fault: the
Alpha row on the generator card is what flattens it, and setting that envelope to a static 1
makes the generator paint through all sixteen frames.
