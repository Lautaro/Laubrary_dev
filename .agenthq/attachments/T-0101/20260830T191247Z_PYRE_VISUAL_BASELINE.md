# Pyre â€” the visual baseline

**T-0101, Wave 0. 2026-08-30.** The first thing in this whole line of work that was produced by *running* Pyre rather than reading it.

Four rounds of investigation produced four documents and not one rendered pixel. This is the correction: every one of Pyre's 29 selectable picture-making techniques was actually rendered, and the result is kept as the reference the rebuild is judged against. Everything below is a measurement or an image, not a reading.

## The deliverables

| File | What it is |
|---|---|
| `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\PYRE_BASELINE_SHEET.png` | **The baseline.** One frame from each of the 29 techniques (plus the 2 retired enum slots as a control), labelled, on a checker so transparency reads. |
| `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\PYRE_BASELINE_FILMSTRIPS.png` | The same 31 specs across seven frames each, so what each technique *does over its life* is visible rather than guessed. |
| `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\check-A-colour-ramp.md` | Check A â€” whether editing a colour ramp on Fire, Fireball and Text actually changes anything. |
| `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\CHECK-B-protrusion.md` | Check B â€” the 3D Shaper reference browser app, protrusion swept from -8 to +28. |
| `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\check-text-glyphs.md` | The Text-glyph probe, prompted by the baseline showing Text spelling the wrong letters. |
| `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\frames\` | The individual 96x96 PNGs, one per technique, plus every before/after pair from Check A and the Text probe. |
| `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\strips\` | Every sampled frame of every technique (217 PNGs), the raw material of the filmstrip sheet. |
| `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\checkB\` | 35 PNGs from the reference browser app. |
| `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\render-log.txt`, `manifest.txt` | The full run log and the machine-readable per-technique record (id, kind, label, file, coverage, mean colour, pixel hash, chosen frame, coverage at every sampled frame). |

## How it was made, and how to redo it

Unity **6000.3.10f1**, launched headless against `D:\UNITY\Laubrary Dev`:

```
"C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe" -batchmode ^
  -projectPath "D:\UNITY\Laubrary Dev" -executeMethod T0101PyreBaseline.Run ^
  -logFile "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\unity-batch.log"
```

The probe script is kept at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\T0101PyreBaseline.cs.txt`. To re-run, copy it to `Assets\Editor\T0101PyreBaseline.cs`, run the command above, then delete it again â€” it is deliberately **not** left in the project, because it is a probe and not a feature, and it adds no menu item.

Everything is stock: `canvasSize 96`, `frameCount 16`, `seed 1234`, transparent background, one layer built in memory with `new PyreLayer()` defaults and nothing authored. Plug-in forms are constructed exactly the way the picker constructs them â€” `Activator.CreateInstance(type)` with no post-configuration â€” so what the sheet shows is what a user gets the instant they click that card. Nothing was saved to disk inside `Assets/`; no asset was dirtied.

Seven frames (2, 4, 6, 7, 9, 11, 13 of 16) were rendered per technique and the one with the most visible pixels is the one on the sheet. That matters: at a single fixed frame several techniques look empty for reasons that have nothing to do with the technique â€” Fireball is still building at frame 7 and triples by frame 13, while Fork Blast and Explosive Jet are already **completely gone** by frame 11.

## The inventory, as rendered

| # | Technique | Kind | Best frame | Coverage | Verdict from the picture |
|---|---|---|---|---|---|
| E01 | Disc | Enum form | 6 | 16.02% | renders |
| E02 | Gem | Enum form | 6 | 17.36% | renders |
| E03 | Crescent | Enum form | 6 | 10.68% | renders |
| E04 | Sparkle | Enum form | 6 | 5.70% | renders |
| E05 | Sprite | Enum form | 6 | 21.01% | renders |
| E06 | Box | Enum form | 6 | 34.90% | renders |
| E07 | Pyramid | Enum form | 6 | 17.64% | renders |
| E08 | Can | Enum form | 6 | 29.93% | renders |
| E09 | Orb | Enum form | 6 | 22.83% | renders |
| E10 | Ring | Enum form | 6 | 19.53% | renders |
| E11 | Text | Enum form | 6 | 7.30% | renders |
| E12 | Streak | Enum form | 6 | 0.56% | renders, but is a sliver at stock defaults |
| E13 | Star | Enum form | 6 | 5.77% | renders |
| E14 | Fire | Enum form | 11 | 0.77% | renders, but is a sliver at stock defaults |
| E15 | Fireball | Enum form | 13 | 7.88% | renders |
| E16 | Polygon | Enum form | 6 | 9.77% | renders |
| E17 | Playback3D | Enum form | 2 | 0.00% | **renders nothing** |
| F01 | Arc Burst | Plug-in form | 7 | 18.39% | renders |
| F02 | Explosive Jet | Plug-in form | 6 | 41.07% | renders |
| F03 | Fork Blast | Plug-in form | 6 | 76.78% | renders |
| F04 | Inferno | Plug-in form | 4 | 46.80% | renders |
| F05 | Jet | Plug-in form | 13 | 10.26% | renders |
| F06 | Orb | Plug-in form | 2 | 9.54% | renders |
| F07 | Plasma Bloom | Plug-in form | 7 | 35.11% | renders |
| F08 | Radial Jet | Plug-in form | 7 | 28.09% | renders |
| F09 | Torch | Plug-in form | 2 | 15.82% | renders |
| P01 | Fuse (metaball) | Field pass | 11 | 34.41% | renders |
| P02 | Ramp (height balls) | Field pass | 13 | 48.16% | renders |
| P03 | Height consumer | Field pass | 13 | 49.22% | renders |
| X16 | Inferno (retired slot) | Retired slot | 6 | 16.02% | **a plain Disc, pixel for pixel** |
| X17 | ForkBlast (retired slot) | Retired slot | 6 | 16.02% | **a plain Disc, pixel for pixel** |

## What the pictures actually show

**1. 28 of the 29 techniques draw something. One draws nothing.** `Playback3D` returns zero pixels at every frame sampled â€” 0.00% coverage, seven times over. This is honest rather than broken (the source says so at `PyreRenderer.cs:269`: it is an editor-preview-only proof of concept with no bake path), but it means the picker offers a card that cannot produce a picture. It is a card, not a generator.

**2. The two retired enum slots are pixel-identical to a plain Disc.** Not similar â€” the same 64-bit hash of the same 9216 pixels. Confirmed empirically, not inferred.

**3. Text draws the wrong letters out of the box, and the cause is font selection, not the text renderer.** With `textString = "PYRE"` the baseline cell spells something closer to `Î¨ Ä± ' & E`. The probe settles it: against `Inconsolata-SemiBold SDF` and `LiberationSans SDF - Fallback` the same layer spells **PYRE correctly**. Against `Splash Demo (LiberationSans SDF) Border Font` â€” a demo-specific font with a partial character table â€” it spells gibberish. And that broken font is exactly the one Pyre picks by itself, because its auto-picker takes the *first* readable TMP font it finds in the project. So a user who adds a Text layer and does nothing else gets garbage, in this project, today.

**4. A Text layer with a non-readable font atlas silently draws a Disc.** `LiberationSans SDF` (atlas not readable) rendered 16.02% coverage â€” the exact coverage, and the exact look, of a plain Disc. No warning, no error, no visual hint that the Text form was ignored. The same silent fallback exists for `Sprite` with a non-readable texture.

**5. Several techniques are near-invisible at stock defaults.** `Streak` peaks at 0.56% of the canvas â€” a one-pixel vertical line. `Fire` peaks at 0.77% â€” a candle flame a few pixels tall. Neither is broken; both are simply authored to need dials before they show anything, while `Fork Blast` at the same defaults floods 76.8% of the canvas. There is no shared sense of scale across the picker.

**6. Lifetimes are wildly inconsistent.** From the filmstrips: `Fork Blast`, `Explosive Jet` and `Plasma Bloom` are finished before frame 11 of 16; `Radial Jet`, `Torch` and `Orb` are essentially static across the entire range and barely animate at all; the field-pass modes and Fireball are still growing at frame 13 and are cut off by the end of the clip. Three different notions of "a life" are in the same picker.

**7. The three field-pass modes are the visually weakest of the 29.** Fuse, Ramp and the height consumer all render large, soft, low-detail brown-orange blobs. They are the modes with no picker entry, and on the evidence of the sheet that is not a great loss in their current form â€” but they are also the only three that produce a genuine height field, which is precisely what the Shaper design's lighting stage wants.

**8. The Kiln plug-in forms are far and away the best-looking things in Pyre.** Arc Burst, Inferno, Radial Jet, Torch and Plasma Bloom are the only cells on the sheet that look like finished VFX rather than primitives. They are also the seven-of-nine that ignore the layer's fill entirely and carry their own baked palettes â€” which is the tension the Shaper design has to resolve, now visible rather than argued.

## Check A â€” the colour ramp

Full table and images: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\check-A-colour-ramp.md`.

- **Fire responds. Fireball responds.** The prior claim that editing a colour ramp on them does nothing is **withdrawn by measurement** â€” swapping the gradient repaints essentially every visible pixel of both. The claim only ever looked true because a canvas-wide percentage of a sub-1%-coverage generator rounds to nothing.
- **Text is the genuine case.** `layer.shapeFill` â€” the ordinary Colour row â€” changes **exactly zero pixels** on a Text layer. What a Text layer's colour actually comes from is `layer.textBorder` (93.5% of its pixels) with `layer.textFill` a distant second (6.5%).
- **Controls behaved as expected**, which is what makes the above trustworthy: Disc repaints fully, Gem partially (it has four other fills), Inferno fully, Torch not at all.

## Check B â€” protrusion in the reference browser app

Full report and 35 images: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\CHECK-B-protrusion.md`.

The reference app was run **for real**, in headless Chromium, with the Protrusion slider driven through its own handler. The design document's B6 claim holds: a per-material protrusion produces genuine lit relief with a directional cast shadow, and the single-material control makes it undeniable â€” at `pro = 0` the disc is a featureless flat circle, and at any non-zero value it becomes a crater with a rim highlight, from a layer that is guaranteed to be one flat colour. The "full weight when patterned, quarter weight in plain fill" rule is real and is the reason the control has a ring at all.

Two things B6 did not say, both found by running it:

- **The slider saturates around +10.** The height buffer keeps climbing all the way to +28, but the lit image stops changing â€” roughly half the positive range is visually dead.
- **Negative protrusion only reads as engraved on a lone layer.** With another layer underneath, sunk cells lose the depth test and are replaced by the layer below, so they read as *deleted*, not as a groove. Engraving and "a raised strip wins the depth test" are the same mechanism, and the first destroys the second.

## What this baseline does not cover

- **One frame per technique, at stock defaults, on one canvas size, at one seed.** It shows what each technique *is*, not what it can be made to do. A tuned Streak or a tuned Fire will look nothing like the cell here.
- **Default variants only.** Six of the nine plug-in forms carry a variant enum with 5â€“10 entries each (ArcBurst 10, ExplosiveJet 10, RadialJet 8, Orb/Torch/Jet 5 each). Only the default variant of each was rendered. There are roughly 50 more distinct looks behind those cards that this sheet does not show.
- **No modifiers, no swarm except where a mode requires it, no borders, no matte stack beyond the two layers the height consumer needs.** Those stages are untouched here.
- **Playback3D was not coaxed into rendering.** It would need `PreviewRenderUtility`, a graphics device, and a supplied ParticleSystem prefab; the honest result is the blank cell.
- **Nothing here was seen inside the Pyre editor window.** These are renderer outputs. The window's own drawing, layout and controls remain unverified by eye.
