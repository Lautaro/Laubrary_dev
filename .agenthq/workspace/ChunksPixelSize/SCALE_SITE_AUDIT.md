# Chunks — exhaustive audit of every "size decided independently of native texel resolution" site

**T-0397, 2026-09-21.** Deliverable for the incident report's step 1. Scope: `Assets/Packages/Laubrary/Runtime/Chunks/**` plus the two editor files that draw or mirror those sizes. Produced by reading every file myself and cross-checked against an independent mechanical grep sweep of the same tree (categories: every `localScale` write, every `Sprite.Create`, every `target/native` ratio, every read of `bounds.size`/`rect.width`/`pixelsPerUnit`, every `ChunkSprites`/`SampledChunkSprites` call, every RNG roll that produces a size or a pixel count).

## The bug class, stated once

> A sprite is rendered at a size that was decided without reference to that sprite's own texel count, so its texels can end up covering less than one game pixel each.

Two corollaries the earlier fixes kept half-learning:

1. It is **not** enough for the outer bounding box to reach one game pixel. An S×S-texel crop whose *box* is one pixel wide has crushed S texels into that pixel. The guarantee has to be stated on **scale**, not on the box.
2. The fix always belongs at the point the **source size is chosen**, never as a downstream clamp. (T-0393 established this; T-0395 broke it and its own caveat was the result.)

## Structural fact that bounds the whole audit

The entire Chunks module writes `Transform.localScale` in exactly **three** places, and there are exactly **three** `Sprite.Create` calls. Every render-size decision in the module therefore passes through one of these; nothing else can express this bug.

- `Chunk.cs:91` + `Chunk.cs:234` (the `DebrisScatter` chunk)
- `PaletteSplash.cs:642` (the splash particle)
- `Sprite.Create`: `ChunkSprites.cs:147`, `SampledChunkSprites.cs:289`, `FragmentCutter.cs:362`

`Chunk.Init` has exactly **one** caller in the whole solution (`DebrisScatter.cs:280`), so changes to `Chunk` cannot leak into another capability.

## The sites

| # | Site | Where | Verdict |
|---|------|-------|---------|
| **A** | `PaletteSplash` **Shards** — `ChunkSprites.GetFitting(targetPx, ppu, pick)` then `scale = targetPx / shape.rect.width` | `PaletteSplash.cs:634-636, 642` | **FIXED, re-verified.** `GetFitting` returns only shapes with `w <= targetPx`, so `scale >= 1` by construction. T-0394's `Mathf.Max(1f, …)` additionally floors the target itself at one game pixel. Not taken on trust — read the code. |
| **B** | `PaletteSplash` **SampledCrops** — `BuildCrop → SampledChunkSprites.SampleAt(cut of cropPx texels, ppu)`, then `scale = 1f` | `PaletteSplash.cs:584-606` | **IMMUNE, independently re-verified** (the incident report asked for this not to be trusted from its own commit message). The crop is cut at exactly `cropPx` texels and built at the same `ppu` its size is measured in, and `scale` is the literal constant `1f`. Texel count and on-screen pixel count are the same number; there is no second, independent roll to disagree with. |
| **C** | `DebrisScatter` **Sampled** — `SampledChunkSprites.Sample(…, samplePxMin, samplePxMax, ppu)` vs `size = rng.Range(sizeMin, sizeMax)` → `Chunk.Init` → `baseScale = worldSize / spriteUnit` | `DebrisScatter.cs:260, 278, 280` + `Chunk.cs:88-89` | **BROKEN.** The two numbers are independent rolls. This is the open bug the incident report root-caused. Measured for the owner's own recipe below. |
| **D** | `DebrisScatter` **Squares** — `ChunkSprites.Get(rng.Next(ChunkSprites.Count), ppu)` | `DebrisScatter.cs:265` | **BROKEN.** Uses `Get`, not `GetFitting` — the exact sibling T-0393's commit message named and deliberately left out of scope. Same `baseScale` path as C. Confirmed by reading the line, not inferred. |
| **E** | `Chunk.ApplyLook`'s hard floor | `Chunk.cs:229-232` | **RIGHT IDEA, WRONG UNIT.** It floors the *bounding box* at `1 / projectPPU` world units. For a 1-texel shard that is correct and equals a scale floor of 1. For an S-texel crop it under-protects by exactly a factor of S: the box reaches one pixel while S texels sit inside it. This is what lets `sizeOverLife` **and** the tumble squash crush a crop's internal detail, which is precisely the owner's "textured / blurry / changes as they rotate" report. T-0395's own recorded caveat (thin-axis starvation on non-square sprites) is a special case of this and disappears with the correct unit. |
| **F** | `FragmentFracture` → `FragmentCutter.BuildSprite(piece, effectivePpu)` | `FragmentFracture.cs:172, 202`; `FragmentCutter.cs:362` | **IMMUNE.** A fragment is placed at its own cut size. There is no `worldSize` roll and no `localScale` write anywhere in either file (confirmed by the exhaustive `localScale` sweep — only the three sites listed above exist). Nothing can disagree with the piece's own texel count. |
| **G** | `ChunkSprites.Random(ppu)` | `ChunkSprites.cs:45` | **DEAD CODE.** Zero call sites in Runtime or Editor. Would carry D's gap if anyone used it. Left alone deliberately — deleting it is not this task's business. |
| **H** | Editor preview `ChunkPreviewSim.Debris` — `radius = size * 0.5f * sizeMul` | `ChunkPreviewSim.cs:387, 406` | **NOT ITSELF A BUG** (it draws a dot, not a sprite, so it has no texel grid to violate) — but it **mirrors the runtime's RNG stream draw-for-draw** (`:376` mirrors the runtime's sprite pick). Any reorder of `DebrisScatter.Fire`'s rolls must be mirrored here or a seeded preview silently stops matching the real burst. |
| **I** | Editor preview `ChunkPreviewSim.Splash` — `radius = px / ppu * 0.5f` | `ChunkPreviewSim.cs:625` | **OK.** Also a dot. It uses the *target* `px`, which is what site A renders at post-`GetFitting`, so preview and runtime already agree. |
| **J** | Zoe window, "Example pieces this Debris Scatter would cut" — `SampledChunkSprites.Sample(liveSprite, debris.samplePxMin, debris.samplePxMax, ppu, …)` | `ZoetropeWindows.cs:2589` | **WOULD BECOME A LIE** the moment C is fixed: it cuts at the raw authored 5–20 range while real bursts would cut to fit. Same bug class, expressed as preview/runtime divergence. In scope precisely because shipping C without it re-creates the "the window tells you something the game doesn't do" failure. |
| **K** | `ChunkSpec.ProceduralSwatch` — `side = round(lerp(1.5, 7, …))` from `world` | `ChunkSpec.cs:600-601` | **NOT A BUG.** A synthetic icon swatch drawn into a recipe card — no sprite is rendered — and it derives its pixel side *from* the world size, i.e. the correct direction of dependency. |

### Sites deliberately confirmed clean
`Hits.cs`, `Trail.cs`, `ChunkPool.cs`, `Trajectory.cs`, `PyreBlast.cs`, `SpawnFormation.cs`, `SpawnPlacement.cs`, `ChunkFollowEmitter.cs`, `ChunkTravelDirection.cs`, `ChunkModule*`, `Timeline/*`, `Legacy/*` — none contains a `localScale` write, a `Sprite.Create`, or a target/native ratio. Their RNG rolls produce seeds, indices, angles and jitter distances, never a render size.

## Measured "before", on the owner's own recipe

`Assets/Zoetrope/Floating Disc.asset`, the private Hit reaction (capability block at line 880): `visual: 2` (Sampled), `useProjectPixelScale: 1`, `samplePxMin 5 / samplePxMax 20`, `sizeMin 0.16 / sizeMax 0.35`, `tumble: 1` at 180–720°/s. Project PPU read live from the editor: **16**.

- On-screen target: `size × 16` = **2.56 – 5.60 game pixels**.
- Crop cut: **5 – 20 source texels**, i.e. `S/16` = 0.3125 – 1.25 world units.
- `baseScale = size × 16 / S` ranges **0.128 – 1.12**.
- `scale >= 1` requires `S <= size × 16 <= 5.6`, which needs `S = 5` **and** `size >= 0.3125`. `P(S=5) = 1/16`; `P(size >= 0.3125) = 0.0375/0.19 = 19.7%`.
- ⇒ **≈ 1.2% of chunks are currently at or above native resolution. ≈ 98.8% are below it**, worst case 0.128× — 7.8 source texels crammed into one game pixel.

That is the number that explains "textured and blurry, definitely sub-pixel" while the overall speck size looked roughly right: the speck *was* roughly right, and the image inside it was compressed ~8:1.

## Project-wide blast radius of a fix

Census of every `DebrisScatter` capability block in every `.asset` in `Assets/`: **`visual` is only ever `0` (Squares) or `2` (Sampled)**. There is not one Sprites-mode or Animated-mode DebrisScatter anywhere in the project. So sites C and D cover 100% of the project's real debris, and the authored-art path (which a fix must *not* resize, because an author's own sprite resolution is their choice) has no assets riding on it today — but it is still gated explicitly rather than left to luck.

## What this audit changes versus the incident report's hypothesis

The report predicted the fix would be "couple `samplePxMin/Max` to the already-rolled `worldSize`". That is correct and is change 1 below. Two things the report did **not** have:

- **Site D is confirmed broken, not "verify".** The report listed `DebrisScatter`'s Squares mode as needing a check; it does use `Get` rather than `GetFitting`, so it has the identical gap.
- **Site E is a fix, not just a bystander.** The report treated T-0395's floor as simply insufficient for this bug. It is more than that: it is stated in the wrong unit, and restating it on *scale* fixes the tumble symptom, the `sizeOverLife` symptom and T-0395's own recorded non-square caveat in one line. Without E, fixing C alone still leaves the crop crushed every tumble cycle — because C only guarantees the size at spawn.

## The four changes

1. **C** — `DebrisScatter.Fire` rolls `size` before building the chunk's sprite, and caps the sample crop at `floor(size × ppu)` texels. A crop is never cut with more source texels than the chunk has pixels to show them in.
2. **D** — `DebrisScatter.Fire`'s Squares path calls `ChunkSprites.GetFitting(size × ppu, ppu, pick)` instead of `Get(...)`, matching `PaletteSplash`.
3. **E** — `Chunk.ApplyLook`'s floor is restated on scale: a chunk whose sprite we built at the effective pixel scale never renders below scale 1.0 on either axis. Authored art (Sprites/Animated) keeps exactly today's one-game-pixel box floor, so no author's own sprite is ever silently enlarged.
4. **J** — the Zoe window's example cuts go through the same shared range helper as the runtime, so the examples stay honest.

## Accepted, stated consequences

- **The tumble's width squash becomes bounded by how many whole pixels a chunk has to squash into.** A 3-pixel fragment will barely squash at all; its pseudo-3D read comes from the shade swing, which is untouched. This is deliberate and unavoidable: a continuous `|cos|` squash of an S-texel crop *is* sub-pixel crushing, and no clamp on the outer box can change that. The lever for a more visible tumble is a larger authored size range (more headroom above scale 1), not a smaller floor.
- **`sizeOverLife` stops shrinking a sampled/procedural chunk once it reaches native resolution.** It already did, at 1 px; now it does at S px. The chunk fades out via alpha instead of dissolving into noise — which is what the owner asked for in their own words.
- **`samplePxMin/Max` become an upper bound rather than an absolute range** when the size range cannot show that many texels. The authored 5–20 on the Floating Disc recipe will cut 2–5 texels at its authored 0.16–0.35 size. To get large, detailed pieces the author raises the size range — the crop follows it automatically. This is the same knock-on `GetFitting` already documents for shards, and it keeps both dials meaningful (Size = how big on screen, Sample px = how much detail, capped by the first).
- **Seeded bursts re-roll.** Moving the `size` draw earlier in the stream changes which rolled value lands on which chunk. Stream *length and composition* are unchanged, and `ChunkPreviewSim.Debris` is moved in lockstep, so seeded preview↔runtime parity is preserved. Existing seeded recipes will look different (they were wrong before).
