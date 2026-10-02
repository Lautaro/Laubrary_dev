# Chunks sub-pixel debris — incident report and handoff

**Status as of 2026-09-21, late session: NOT RESOLVED.** Five real code fixes landed and were each individually verified, and the owner is STILL seeing sub-pixel/"textured, blurry" debris after all five. This document exists because that pattern — narrow fix, verified, still broken — repeated enough times that the owner asked for a full written account for a fresh agent, rather than another reactive patch in the same conversation.

**Read this whole document before touching any code.** The short version: every fix so far was real, correctly diagnosed for what it targeted, and correctly verified for what it targeted — but each one covered a different narrow code path, and nobody did a systematic sweep of every place in the Chunks system where a sprite gets scaled to a target size. The bug class is "a sprite/crop is rendered at a size independently rolled from its own native pixel resolution, with no coupling between the two" — it has now been found and fixed in TWO of at least THREE places it occurs, and this document identifies the third, still-unfixed one with a concrete mechanism and file/line references.

## The owner's own words, verbatim, across the session (read these first — they are the ground truth)

1. "The debris from the hits have really small pixels though. I dont think they are following the global setting are they?"
2. "Its still wrong. I see larger debris now, but most are still subpixel size."
3. "There are no visible changes. I dont know what you are doing changing in the code but i am worried you are causing chaos without solving anything."
4. "LOOK! AT THIS POINT JUST SHOW ME YOU CAN MAKE DEBRIS WHERE NO PARTICLE IS SMALLER THAN THE SET PIXEL SIZE!!!! That is your priority now. GO!"
5. "No the pixels are still unresolved. I see no change." — followed by a request to add a size label to the on-screen reference rect, slow the particles down, and give them a long lifetime so they could be screenshotted and measured precisely.
6. Final (this document's trigger): "Look at the latest screenshot. But you changed more than i told you. The particles look different now. I think the size might very well be 1/200 but they rotate in a way that changes their size to subpixel and they have some texture on them that is definitely subpixel. Honestly this is so wrong and i dont understand how you keep getting it wrong."

## The five commits, what each actually did, and — critically — what it did NOT cover

All on branch `dev`, Laubrary Dev project (`D:\UNITY\Laubrary Dev`), Chunks module (`Assets/Packages/Laubrary/Runtime/Chunks/`).

### 1. `6951f3e2` (T-0390) — FragmentFracture PPU
`FragmentFracture` (the "few large pieces" capability, Death-only in this demo) read a live sprite's own baked `pixelsPerUnit` instead of the project's `PixelScaleProjectSettings`. Added `useProjectPixelScale`/`EffectivePixelsPerUnit`, mirrored from `DebrisScatter`'s existing (T-0383, an earlier session) pattern.
**Verified:** measured before/after — but on the REAL asset this recipe uses, the source sprite's own PPU already happened to equal the project's, so the fix was a correctly-implemented, correctly-verified **no-op** for anything the owner could see. Real bug, wrong target for THIS complaint.
**Does not touch:** any DebrisScatter or PaletteSplash code path at all.

### 2. `653cf995` (T-0391) — PaletteSplash PPU + a shared shard-cache bug
`PaletteSplash` (the "spray" capability, fires on Hit) had a flat, ungoverned `pixelsPerUnit` field defaulting to 32 against the project's 16 — every splash particle was rendering at half its intended size. Fixed the same way as #1. Its own verification pass ALSO found and fixed a second, deeper bug: `ChunkSprites` (the shared procedural-shard texture cache used by PaletteSplash's "Shards" mode and DebrisScatter's "Squares" mode) built its shapes once at whatever PPU was asked for FIRST in the whole play session (primed at a hardcoded 32 by a `Count` getter), and silently ignored the PPU any later caller actually passed. So fix #2's own capability-level change would ALSO have been a runtime no-op without this second half.
**Verified:** measured before/after sizes, 2× difference, matched the owner's report at the time.
**Does not touch:** `Chunk.cs`, `DebrisScatter`'s "Sampled" visual mode, or anything about a chunk's size changing over its lifetime.

### 3. `f497d3a7` (T-0392) — Mirage preview PPU
Unrelated to the debris complaint directly — the owner asked, after seeing the pattern in #1/#2, why Mirage's own preview PPU wasn't given the same "always follow the project setting" treatment. Converted it from a one-time snapshot to the same live pattern. Verified as a true no-op for existing assets. **Not part of the debris chain**, included here only for completeness of "what changed today."

### 4. `2f993028` (T-0393) — shard shape squashed below its own native pixel grid
Even after #2's PPU fix, the owner reported debris still mostly sub-pixel. Root cause: `PaletteSplash`'s "Shards" mode picked a random procedural shape (native footprints 1×1 up to 3×3 texels from `ChunkSprites`) **independently** of the target particle size it then scaled that shape to. A 3×3 shard paired with a 1px target got scaled to 0.33× — each of ITS OWN texels rendering at a third of a game pixel. Fixed with `ChunkSprites.GetFitting(targetPx, ppu, pick)`, which only picks a shape that already fits the target, so a shape is only ever scaled UP or left 1:1.
**Verified with real numbers:** over the real asset's authored 1-3px range, 66.6% of particles were previously being scaled below native resolution, worst case 0.334×; after the fix, worst case is exactly 1.000×. Also proved, three independent ways, that this didn't break the seeded-preview-must-match-real-burst contract.
**Does not touch:** `DebrisScatter` at all (explicitly, deliberately out of scope — see its own commit message: "DebrisScatter's identical Get call left alone"). **This is the single most important scope note in this whole document** — T-0393 fixed this exact bug class for ONE of the (at least) three places it occurs, and explicitly, correctly, on-the-record did not touch the other two.

### 5. `6a0757e6` (T-0395) — hard floor on `Chunk.ApplyLook`
The owner's screenshot at this point showed debris from a DIFFERENT recipe than any of the above targeted: `Assets/Zoetrope/Floating Disc.asset`'s own private, hand-authored Hit-reaction `ChunkSpec` (a `DebrisScatter` capability, `visual: Sampled`). Its authored `sizeOverLife` envelope drops to ~0.168 of base size by 25% into a chunk's life and STAYS there for the remaining 75% — a genuinely different bug from #1-#4: a *per-frame runtime shrink*, not a static PPU or shape-fit issue. Fixed with a hard, unconditional floor in `Chunk.ApplyLook`: both axes are clamped so a chunk's rendered size can never drop below `1f / PixelScaleProjectSettings.Instance.pixelsPerUnit` world units, computed via the chunk's own native `spriteUnit`.
**Verified as thoroughly as anything today:** 1,570 editor frames, 440 real chunk measurements in a live Play-mode Hit, minimum observed size exactly 1.000px on both axes, never below, across every chunk sampled. This is real, live, behavioral proof, not just arithmetic.
**Its own stated caveat, on the record:** "the floor guarantees one pixel on a sprite's LARGER native axis... a strongly non-square sprite could still go sub-pixel on its THIN axis... confirmed this doesn't apply to anything in the project TODAY (sampled debris are always cut square)."

**That caveat's premise was wrong, and this is the actual open bug.** See below.

## The bug that is STILL OPEN — found while writing this report, root-caused, not yet fixed

The owner's screenshot after T-0395 (private Floating Disc Hit recipe, tuned to slow/long-lived for easy capture) shows debris that are: (a) roughly the right overall size, matching the owner's own read ("the size might very well be 1/200"), but (b) internally "textured" and "blurry" in a way that reads as sub-pixel, and (c) changing shape/apparent-size as they rotate.

**Root cause, traced to exact code:**

`DebrisScatter`'s `Sampled` visual mode (`Runtime/Chunks/Capabilities/DebrisScatter.cs`, ~line 259) crops a REAL, MULTI-TEXEL piece of the source art via `SampledChunkSprites.Sample(resolvedSampleSource, samplePxMin, samplePxMax, effectivePpu, ...)`. The crop's own size, in SOURCE TEXELS, is randomly rolled between `samplePxMin` and `samplePxMax` (this recipe: 5–20 texels — i.e. the crop itself can be a native 20×20-texel, highly detailed slice of the art, `20/16 = 1.25` world units across at this project's PPU).

That cropped sprite is then handed to `Chunk.Init` (`Runtime/Chunks/Chunk.cs`, ~line 59-89), which computes:
```
spriteUnit = Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y);   // the CROP's own native size
baseScale  = worldSize / spriteUnit;                                        // worldSize = an INDEPENDENT roll, 0.16–0.35 for this recipe
```
`worldSize` (the on-screen target size) and the crop's own native pixel dimensions are **two completely independent random rolls, with no relationship to each other.** When a large crop (near 20 native texels, 1.25 world units) is paired with a small `worldSize` roll (near 0.16), `baseScale ≈ 0.16/1.25 ≈ 0.128` — the entire 20×20-texel crop, full of real image detail, gets squashed into a ~2.56-game-pixel screen footprint. That means roughly 8 of the crop's own source texels are being crammed into the space of ONE rendered game pixel. **This is exactly what "textured and blurry, definitely sub-pixel" looks like** — it's not a uniformly-undersized speck (which is what #1-#4 all fixed), it's a *richly detailed image compressed far below the resolution it was captured at*, which reads as noise/blur rather than a clean pixel-art block.

**This is THE SAME BUG CLASS T-0393 fixed** ("a shape scaled independently of its target size, ending up below native resolution") — **in a different, adjacent code path that T-0393 explicitly and correctly left out of scope.** T-0393 fixed it for `ChunkSprites`' procedural shard picking (used by `PaletteSplash`'s Shards mode and, in principle, `DebrisScatter`'s Squares mode). It never touched `SampledChunkSprites` + `Chunk.Init`'s coupling for `DebrisScatter`'s Sampled/Sprites/Animated visual modes — and this project's owner-authored real recipe (`Floating Disc.asset`'s own Hit reaction) uses exactly that untouched path.

**T-0395's floor does not fix this**, and its own commit message's caveat — "does not affect this recipe... sampled debris are always cut square" — was correct about squareness (the crop IS square) but incomplete: squareness prevents an axis-specific starvation, it does NOT prevent the WHOLE crop, all axes, from being downscaled below native resolution together. The floor guarantees the outer bounding box reaches one pixel; it says nothing about whether the 400 texels inside a 20×20 crop are still individually resolvable at that size — they are not, by roughly 8:1.

**The rotation/tumble complaint is a second, related symptom of the same root cause, not a separate bug.** `tumble: true` on this recipe drives `ChunkTumble.Evaluate` (`Runtime/Chunks/ChunkTumble.cs`), which squashes the X axis by `|cos(phase)|`, sweeping through zero several times a second at this recipe's `tumbleSpeedMin/Max` (180–720°/s). T-0395's floor DOES stop the bounding box from vanishing on that axis — but if the crop is, say, 20 texels wide natively, squashing that whole 20-texel width down to a floor-clamped single game pixel means ALL 20 of the crop's horizontal texels get compressed into that one pixel at the extreme of the squash, every single tumble cycle. The floor prevents the box from disappearing; it does nothing to prevent the image detail inside it from being crushed as it rotates through the squash.

## What actually needs to happen (recommendation for whoever picks this up)

This needs ONE systematic pass, not another single-symptom patch. Concretely:

1. **Audit, explicitly, every place in `Runtime/Chunks/` where a sprite/crop is scaled to a size rolled independently of that sprite's own native pixel dimensions.** Do this as a first step, in writing, before touching any code — the pattern in this whole incident is that narrow, reactive fixes kept missing siblings of the same bug. Known instances as of this report:
   - `ChunkSprites` procedural shards via `PaletteSplash` — **FIXED** (T-0393, `GetFitting`).
   - `PaletteSplash`'s new `SampledCrops` mode (T-0394, committed `174a4804`) — reported as structurally immune ("a crop's texel count and its on-screen size are the same number, scale 1") — **should be independently re-verified as part of this audit, not just trusted from its own commit message**, given how many "should be fine" claims in this exact area turned out to have an uncovered sibling.
   - `DebrisScatter`'s `Sampled`/`Sprites`/`Animated` visual modes via `Chunk.Init`'s `baseScale = worldSize/spriteUnit` — **NOT FIXED.** This is the open bug documented above.
   - `DebrisScatter`'s `Squares` mode via `ChunkSprites` — inherits T-0393's `GetFitting` fix IF it's wired to use it (verify — T-0393's own commit message says DebrisScatter's `Get` call was left alone, meaning Squares mode may ALSO still have this exact gap, just via the procedural-shard path instead of the real-crop path).
2. **The real fix, once the audit is complete, is almost certainly: make the crop/shape SIZE follow the target, not the other way around** — the same principle T-0393 already established (pick/cut at a size that fits the target, rather than cutting at an arbitrary size and rescaling down). For `DebrisScatter`'s `Sampled` mode specifically, that likely means coupling `samplePxMin`/`samplePxMax` to the ALREADY-ROLLED `worldSize` (or the reverse — roll `worldSize` first, then cut a crop sized to match it), so a crop is never cut bigger than what it will actually be displayed at.
3. **Do not add another "floor" as the fix.** T-0395's floor was the right tool for its specific job (a size-over-life curve legitimately driving a value toward zero) but a floor on the OUTER bounding box cannot fix internal-detail compression — it's the wrong shape of fix for this bug. The fix has to happen at the point a crop's SOURCE SIZE is chosen, coupled to its eventual on-screen size, mirroring T-0393's `GetFitting` approach exactly.
4. **Verify with the same rigor this session's later tasks used** (T-0393, T-0395): real numbers, a live Play-mode observation with actual measured `SpriteRenderer.bounds`, not just arithmetic on the authored fields. Screenshot and eyeball it too — the owner has been right every time they said "this still looks wrong," even when the code-level fix was individually correct.

## Self-assessment (written by the agent that made all five fixes above, for the owner)

Every one of the five fixes was real, correctly diagnosed for what it targeted, and correctly, rigorously verified for what it targeted. None of them was fabricated or careless — each had genuine before/after measurements, and several caught real secondary bugs beyond their original brief (the shared shard-cache bug in #2, the compile error in #2's own predecessor edit, the discovery that #5's fix generalizes beyond its original recipe). That part of the process worked.

**What did not work: treating each user report as a new, isolated bug to hunt down, rather than stepping back after the SECOND "still broken" report and asking "is this one instance of a general pattern I should audit exhaustively, instead of patching the specific thing in front of me."** T-0393 came close — it correctly identified "shape scaled independently of target size" as a bug CLASS, said so explicitly in its own doc comments, and even named `DebrisScatter`'s parallel `Get` call as an out-of-scope sibling in its own commit message. That was the moment to broaden the fix into a project-wide audit. Instead, work continued task-by-task, and the exact sibling T-0393 had already named in writing is the bug still open right now.

Am I capable of solving this? Yes — the root cause above was found by reading the same handful of files (`Chunk.cs`, `DebrisScatter.cs`, `SampledChunkSprites.cs`) that were already read multiple times earlier today, and the mechanism is not exotic: two independent random numbers, no coupling between them, same shape as three bugs already found and fixed today. The failure was not a capability gap in reading or reasoning about this code. It was a process gap — narrow, reactive, single-report-at-a-time fixing, in a single long conversation with accumulating context and, in hindsight, accumulating pressure to produce a fix quickly rather than stop and do the systematic audit T-0393 itself had already flagged as the right next step.

## For the next agent, concretely

Start at "What actually needs to happen" above. Read `Chunk.cs`, `DebrisScatter.cs`, `SampledChunkSprites.cs`, `ChunkSprites.cs`, and T-0393's/T-0395's full commit messages (`git show 2f993028`, `git show 6a0757e6`) before writing any code. Do the audit as its own deliverable — a written list of every scale-independent-of-native-size site in `Runtime/Chunks/` — before fixing anything, and get that list checked (by the owner, or by re-reading it yourself with fresh eyes) before implementing. The owner has been asking, in increasingly direct language, for exactly this kind of thoroughness instead of another quick patch.
