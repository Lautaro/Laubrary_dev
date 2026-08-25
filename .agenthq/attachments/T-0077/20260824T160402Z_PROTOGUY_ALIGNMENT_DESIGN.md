# ProtoGuy part alignment — diagnosis and recommended system

Task T-0077. Rewritten 2026-08-24 after the first version's headline conclusion was refuted by the user's reference screenshot.

**This document replaces an earlier version that claimed both halves sit at the same registered origin and that a zero offset assembles the character. That claim was wrong** — it produced the squashed composite the user rejected. Everything below is re-measured from the source art, the baked atlas and the live asset data, cross-checked against the user-supplied ground-truth image, and independently re-derived by a second agent working blind.

---

## 1. Answer in one paragraph

The two halves of ProtoGuy are **not** drawn on a shared body canvas — each part set is drawn centred in its own canvas, so registering them at a common origin stacks them on top of each other. What joins them correctly is **one constant offset per sprite sheet**: the legs sit 22 px below and 1 px left of the torso, the same numbers in all 16 directions and in every walk frame. Nothing in the project stores that number. The legs clips carry no waist points at all, so the legs side of the joint silently falls back to the bounds of the legs sprite's baked box — a value that is constant within a sheet but **different for every sheet** — while the torso side uses hand-painted points that wander 9 px horizontally and 3 px vertically between directions. The fix is to stop deriving the joint from artwork bounds and declare it once per sheet.

## 2. Ground truth

The user supplied a screenshot of the correct east-facing character (`.agenthq/attachments/T-0077/20260824T153445Z_Screenshot_20260816_074714_Claude.jpg`). Template-matching it against the source art recovers an exact reconstruction:

| | value |
|---|---|
| Upscale factor | **9×** — established structurally, by fitting the block-boundary grid spacing (peaks at 9.00 on both axes), not by match fraction |
| Torso | `Assets/Demos/ProtoGuyDemo/Sprites/Upper/Upper-E.png`, **354 / 354 opaque pixels matched** |
| Legs | `Assets/Demos/ProtoGuyDemo/Sprites/LegsIdle/Legs-E.png` at **dx −1, dy +22**, 231/245 = 0.943 |
| Next-best direction | `Legs-ENE` at 0.661 — every other direction ≤ 0.67, so the identification is unambiguous |
| Next-best offset | dy +23 scores 0.767 and dy +21 scores 0.739, against 0.943 at dy +22 — a single sharp peak |

Side-by-side proof: `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_reference_match.png`.

> **Method note.** A first pass fitted scale 8.75 and read dy +23 off it. The torso is a 30×24 px blob, small enough to score 354/354 at several scales, so match fraction alone cannot pin the scale. The block-grid fit and an independent blind re-derivation both give 9× and dy +22. Do not trust a template match on a small sprite without an independent scale check.

## 3. How the art is actually registered

| Sheet | Canvas | Content rows | Content columns |
|---|---|---|---|
| `Sprites/Upper/*.png` | 56×56 | **top row = 16 on all 16 sprites**, bottom row 37–47 | union x 8..50 |
| `Sprites/LegsIdle/*.png` | 56×56 | top row 14–17, bottom row 38–41 | union x 15..41 |
| `Sprites/LegsWalk/*.png` | 368×35 = 8 frames of 46×35 | top row 7–8, bottom row 29 on every frame | varies |

Both part sets occupy the *same* vertical band of their own canvas. A full character is ~48 rows tall, so those are the same band, not two halves of one body. The export centred each part in its own box and discarded the shared origin.

**The walk sheets are the idle canvas cropped by (5, 9).** Restoring that crop makes idle and walk agree exactly:

| Direction | Idle bbox (56×56 canvas) | Walk frame bbox + (5,9) | Horizontal centre |
|---|---|---|---|
| E | x 21..34, y **16**..40 | x 17..38, y **16**..38 | 27.5 vs **27.5** |
| S | x 15..41, y **16**..39 | x 18..38, y **16**..38 | 28.0 vs **28.0** |
| N | x 15..41, y **17**..38 | x 18..38, y **17**..38 | 28.0 vs **28.0** |

Top row identical in all three, horizontal centre identical to the half-pixel in all three. So the artist *did* keep one canvas for the whole legs set — the registration was lost at export, not at drawing.

## 4. The complete offset table

With the torso as the reference part, these canvas offsets assemble a correct character everywhere:

| Sheet | Offset (x, y down) |
|---|---|
| `Upper` (reference part) | (0, 0) |
| `LegsIdle` | **(−1, +22)** |
| `LegsWalk-E/S/N` | **(4, +31)** — i.e. the same (−1, +22) plus the (5, 9) crop |

Five number-pairs for the whole character, replacing 56 hand-placed per-frame points.

Proof renders — every direction, every frame, one constant offset each:

- `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_idle_16dir.png` — all 16 idle directions
- `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_walk_3dir.png` — all 8 frames × 3 walk directions

## 5. Why it misaligns today — the mechanism

`ProtoGuy.asset` anchors the `Upper` part to the `Legs` part with `mode: 1` (MetaLayer, id `Waist`) on **both** sides. Only one side has data.

**5.1 — The legs side has no waist data and silently degrades.** In `ProtoGuy_draft.asset`, `LegsIdleRotation` and `LegsWalk_N/E/S` all carry `metaLayersEnabled: 0` and `metaLayers: []`. `ResolveAnchor` (`Assets/Packages/Laubrary/Runtime/ZoetropeLaunimator/CompositeZonedPlayer.cs:109`) therefore falls straight through the MetaLayer branch into `switch (anchor.edge)` and uses `edge: 1` = `AttachEdge.Top` — the top of the legs sprite's world bounds. **No warning is ever logged**, and `edge` is not even editable in the UI while the mode is MetaLayer, so the value being used was never authored by anyone.

**5.2 — That fallback is a different constant for every sheet.** `AtlasBaker` composes each sheet into a uniform frame box with one shared pivot, so the bounds are stable within a sheet but set by that sheet's own content union:

| Legs clip | Baked rect | Pivot (px) | `Edge.Top` = px above pivot |
|---|---|---|---|
| `LegsIdleRotation` | 27×28 | (13, 14) | **14** |
| `LegsWalk_E` | 39×24 | (21, 13) | **11** |
| `LegsWalk_S` | 21×23 | (10, 13) | **10** |
| `LegsWalk_N` | 21×22 | (10, 13) | **9** |

**5.3 — The torso side uses hand-painted points that wander.** The `Waist` layer on `UpperAimRotation` is painted on all 16 frames, one cell each, on a 43×32 grid. Centroids span **x 17.5..26.5 (9 px) and y 7.5..10.5 (3 px)** for what is meant to be a single fixed joint.

**5.4 — Resulting seam error, in source pixels.** Positive = torso too high / too far right. Correct is 0.

| Legs clip | dy error across the 16 torso directions | dx error |
|---|---|---|
| `LegsIdle` | +1.5 .. +4.5 | −7 .. +2 |
| `LegsWalk_E` | +0.5 .. +3.5 | −9 .. 0 |
| `LegsWalk_S` | −0.5 .. +2.5 | −7 .. +2 |
| `LegsWalk_N` | −1.5 .. +1.5 | −7 .. +2 |

Two independent error terms are visible here. Vertically, each clip carries **its own constant bias** — the four clips sit 1 px apart in a ladder, straight out of the 14/11/10/9 table above — so changing gait shifts the torso. Horizontally, the error is larger and swings **up to 9 px with facing**, entirely from the hand-painted Waist points; that is the dominant defect by magnitude.

**5.5 — What I could not reconcile.** The report is that standing looks right and the walk cycles do not. By these numbers the vertical bias is actually *smallest* on `LegsWalk_N` and largest on `LegsIdle`, which is the opposite ranking. The magnitudes are all in the same 1–5 px band, so this may simply be below the threshold where a subjective "looks good" call is reliable, or the running configuration may differ from the serialised asset. **I have not run this in the editor to check.** Flagging it rather than asserting a contradiction — the previous round asserted exactly this kind of contradiction confidently and was wrong. It does not affect sections 4, 6 or 7, which stand on the art and the ground-truth image alone.

## 6. Recommended system

**6.1 — One registration point per sheet, authored once.** Each sheet declares where its joint sits in its own pixel space; joining two parts means making the parent's point and the child's point coincide. Per sheet, not per frame: in this art style the hip does not move relative to the sheet, so per-frame painting is 56 chances to be a pixel out — and measurably was, by up to 9 px.

**6.2 — Never derive a joint from artwork bounds.** This is the actual defect. A baked box is sized by whatever the artist happened to draw, so it differs per sheet and silently changes on any redraw or re-bake. If an anchor's data is missing, fall back to the part's registered origin (which always exists) and **warn once**. The silent degrade is what turned "no data" into a facing-dependent visual error with no error message, which is far more expensive to debug than a hard failure.

**6.3 — Keep per-frame meta points as an override, not the mechanism.** They are right for a joint that genuinely travels inside the picture — a hand gripping a weapon through a swing. They should sit on top of the sheet registration, not replace it.

**6.4 — Fix registration at import, not by hand.** The walk sheets only need a different number because they were exported pre-cropped. Either ask for exports on a common untrimmed canvas, or record the crop offset at import. With either, the whole legs set shares one offset and the walk sheets need nothing of their own.

**6.5 — Author it by eye, once, against a ghost of the other part.** The number is a judgement about anatomy, not something recoverable from the file. The authoring UI should be "drag the child part over a ghost of the parent until it looks right, for one representative direction" — that one number then covers the entire sheet.

## 7. Concrete change set

**7.1 — Add a `Pivot` anchor mode (~8 lines).** There is currently no anchor mode meaning "this part's own registered origin": `Edge.Center` is `bounds.center`, which moves with content. Add a third member to `AttachAnchorMode` (`Assets/Packages/Laubrary/Runtime/ZoetropeLaunimator/CompositeLauminaryView.cs:16`) and one branch at the top of `ResolveAnchor` (`Assets/Packages/Laubrary/Runtime/ZoetropeLaunimator/CompositeZonedPlayer.cs:109`) returning `partGo.transform.position + (Vector3)anchor.offset`. Mirror it in the editor's offline copy (`Assets/Packages/Laubrary/Editor/Zoetrope/ZoetropeWindows.cs:1119`) and make the anchor row a three-way (`Assets/Packages/Laubrary/Editor/Zoetrope/ZoetropeWindows.cs:931`).

> **Serialization hazard — append, never insert.** The enum serialises as an int and `ProtoGuy.asset` already stores `mode: 1`. Inserting `Pivot` before `MetaLayer` would silently reinterpret every existing MetaLayer anchor.

**7.2 — Fix the LegsWalk sheets' registration.** Their recipe cells use `pivot: {x: 0.5, y: 0.5}` of a 46×35 cell, but that cell is the 56×56 canvas cropped at (5, 9), so 0.5 is a different physical point than on the idle sheets — a 2 px discrepancy. The canvas centre in walk-cell space is **(0.5, 16/35 = 0.4571429)**. Set that and re-bake, and idle and walk then share one origin.

**7.3 — Set the ProtoGuy anchors.** With 7.1 and 7.2 done, both sides go to `Pivot` and the whole join is one offset on the `Upper` part's parent anchor. At PPU 16 (confirmed in both the Lauminary and the baked atlas), the measured (−1, +22) px is **(+0.0625, +1.375) world units** — torso up and to the right of the legs origin.

**7.4 — Two related defects worth their own tickets.** `ProtoGuy.asset` sets `idleClip: LegsWalk_N` on the *Upper* part, so the torso shows a legs sprite for one frame after spawn. And `Assets/Demos/ProtoGuyDemo/Lauminaries/ProtoGuyLegs.asset` / `ProtoGuyUpper.asset` are orphans referenced by nothing — but `ProtoGuyUpper.asset` holds a `MuzzleVec` layer the live draft does not, so check before deleting.

## 8. Status

Diagnosis, offset table and seam-error figures are measured against the source art, the baked atlas, the live asset data and the user's reference image, and the offsets were independently re-derived by a second agent working blind from the same inputs. **No code, asset, artwork or setting in the project has been changed.** Section 7 is a specification, not something that has been built or run.

## 9. Reproducing the measurements

Scratch scripts (read-only, ephemeral, safe to delete) are under `D:\UNITY\Laubrary Dev\Temp\t0077\`: `scale.py` (block-grid scale fit — run this first, it pins everything else), `fit9.py` (reference template match at the correct scale), `crop.py` (walk crop offset), `grid16.py` / `walkcomp.py` / `proof1.py` (proof renders).

## 10. Notes for whoever picks this up

`MetaFrame.cells` is a `byte[]` (`Assets/Packages/Laubrary/Runtime/Launimator/MetaLayer.cs`). Unity YAML serialises `byte[]` as **two hex characters per byte**, and the rows are stored **bottom-up** (`TryComputeCentroid` feeds the row index straight into an up-positive coordinate in `PixelToWorld`). Parsing it one character per byte, or flipping the rows, yields plausible-looking but wrong coordinates — it cost a full agent round on this task.

And per §2: a template match against a small sprite will happily report a perfect score at the wrong scale. Pin the scale independently before believing any offset derived from it.
