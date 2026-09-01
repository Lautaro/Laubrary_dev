# -*- coding: utf-8 -*-
import io

p = r'D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\VERIFICATION.md'
s = io.open(p, encoding='utf-8').read()

section = u"""
---

# T-0106 fix pass — closing the two escalated defects (\u00a74.4 and \u00a74.5)

Written 2026-08-31 by the fix-and-re-verify pass, after the verification above and appended to it rather than edited into it. Same worktree, `D:\\UNITY\\Laubrary Dev - Shaper`; `Application.dataPath` asserted as `D:/UNITY/Laubrary Dev - Shaper/Assets` before any number below was believed, and `EditorUtility.scriptCompilationFailed` confirmed `False` with a reflection probe for symbols added in this pass (`ShaperFillResolver.Encode`, `ShaperFillBuffers.subtree`, `ShaperFillAudit.FT22_EncodeIsReadableBack`, `ShaperFillAudit.StampSeam`) before any measurement was taken. Nothing is committed. Only `Runtime/Shaper/ShaperFillResolver.cs`, `Editor/Shaper/ShaperFillAudit.cs` and the three workspace documents were touched.

**Headline.** Both escalated items are closed. \u00a74.4's transparent seam went **244/16384 samples \u2192 0/16384** on the escalation's own fixture and instrument, with the sibling half of the rule provably unmoved. \u00a74.5's unreadable colour write is gone: the premultiplied encoder was **deleted** rather than repaired, straight alpha is now the single `Color32` write, and its round trip is exact at **0 codes over all 256 levels at every alpha tested** where the old one was wrong by up to **67 codes**. `FILL-CONTRACT.md` carries three new amendment clauses recording both rulings and the superseded readings. All 21 asserting FT legs pass, `ShaperFieldAudit` is still 13/13, and the contact sheet is 21 cells with a new cell that would show the seam if it came back.

---

## A. Defect \u00a74.4 — `Over` between exclusive owners. FIXED, re-measured

### A.1 What was changed

`ShaperFillResolver.PaintTile` step 5/6 was restructured. Previously every owner composited straight into one destination with `Over` (or `Add`), in forward paint order. Now:

- Each owner gets its own **subtree accumulator** \u2014 `ShaperFillBuffers.subtree`, 4 floats per sample per owner, host-allocated once on the same BC-3.7f terms as every other array here.
- An owner's own contribution is **summed** into its own accumulator: `rgb += albedo\u00b7ce` always, `alpha += ce` only when the composite is `Over`. This is the partition arithmetic \u2014 step 4 already made the owner's region disjoint from every descendant's, so the two are two parts of ONE pixel's area and their alphas add.
- A **finished** subtree is then folded into its parent's accumulator with `target = target Over source`, or into `dst` for the root. That is where FC-2.6a's `Over` belongs and it is the only place it now appears.
- The loop runs in **reverse paint order**. That is an evaluation order, not a reordering of the fold: a child's subtree must be finished before its parent needs it (the same reason step 3 already iterates backwards), and reaching the children in decreasing index places the highest-index member first, so it stays on top \u2014 which is fold order exactly, by the associativity of `Over`. FC-3.4 is preserved, and FT-7b still measures it.
- FC-3.5b, the mixed Over/Add-within-one-pixel case the escalation asked to have defined rather than left to fall out, is now stated in the contract and in the code: **alpha is the sum of the `Over` owners' claims alone; RGB is the claim-weighted sum of every owner's albedo.** An `Add` owner contributes light and never opacity, against the destination, a sibling, or an ancestor sharing its pixel.
- Accumulated alpha is clamped at 1 at use. It can legitimately overshoot, because `descendantClaim` is a MAX over descendants rather than a sum (siblings may overlap, so a sum would double-count), so two spatially disjoint descendants at fractional coverage can leave their ancestor more claim than the pixel has left. RGB is deliberately not rescaled with it \u2014 an additive result carries more light than its alpha on purpose.

### A.2 Before and after, one instrument

The "before" number is not quoted from \u00a73(iii); it was **re-measured in this pass**, by reconstructing the previous build's forward-`Over` accumulation from the very same `paint[]` array the shipped build produced on the same run, so the two columns come from one execution and are directly comparable.

| | samples with composited alpha below the shape's own coverage | total deficit | worst |
|---|---|---|---|
| BEFORE (`Over` between exclusive owners) | **244 / 16384** | 29.288 | 0.2484 |
| AFTER (FC-3.5a claim-weighted sum) | **0 / 16384** | 0.000 | 0.0000 |

That reproduces \u00a73(iii)'s three numbers exactly, which is the check that the reconstruction is faithful rather than merely favourable.

### A.3 The exclusivity fix from \u00a74.1 was not weakened \u2014 both its results re-measured

- `G_body` on the FC-3.8 worked tree: **0 painted samples**, unchanged (FT-7's probe table is row-for-row identical, and FT-7 passes).
- Chain conservation on the three-deep concentric fixture: `sum(paint over the chain) != rootCoverage` at **0/16384 samples, worst 0.00000** \u2014 FT-21 leg (a), unchanged.
- The fixture is not vacuous: **244** samples carry fractional coverage, which is the same 244 the deficit was measured over.

### A.4 The sibling half is provably still `Over` \u2014 a new leg, because a sum here would have been an invisible regression

The obvious wrong fix is to sum everything, which would silently reverse FC-3.4. FT-21 now carries leg **(c)**: two overlapping sibling owners at veil 0.5 each, alpha where both cover = **0.75000** (expected 0.75000 \u2014 `Over`; a sum would read 1.00000). PASS.

### A.5 What else moved, measured rather than asserted

Six fixtures, comparing the shipped destination against a faithful reconstruction of the previous algorithm, over all 65536 floats of a 128\u00d7128 RGBA destination:

| fixture | owners | floats differing | worst \\|delta\\| | channel |
|---|---|---|---|---|
| single Solid owner | 1 | **0 / 65536** | 0 | \u2014 |
| single `Add` owner, veil 0.7 | 1 | **0 / 65536** | 0 | \u2014 |
| two overlapping `Over` siblings | 3 | **0 / 65536** | 0 | \u2014 |
| siblings, upper is `Add` (sheet cell 17) | 3 | 1012 / 65536 | 2.980e\u22128 | colour only |
| nested bag + child (sheet cell 19) | 2 | 432 / 65536 | 2.451e\u22121 | **alpha** |
| three-deep chain (FT-21 fixture) | 3 | 1104 / 65536 | 2.484e\u22121 | **alpha** |

Read it as three groups. Single-owner and sibling-only documents are **bit-identical** \u2014 the change cannot reach them. The `Add`-sibling row differs by 3e\u22128 on colour and nothing on alpha: that is float summation order, because an additive sibling's contribution is now added in the other order, and one ULP at magnitude 1 is exactly what that costs. Only the two nested rows change materially, only on alpha, and only at the internal boundary \u2014 which is the defect and nothing else.

### A.6 The fix is not free, and the cost is stated

One extra host-allocated buffer, `ownerCapacity \u00b7 sampleCapacity \u00b7 4` floats. Nothing is allocated inside a paint call: an IL opcode scan of `PaintTile` reports **0 `newobj`, 0 `newarr`, 0 `box` over 1304 bytes of IL** (a byte-frequency scan is an upper bound, so a zero is conclusive). `ClearDestination` now also clears the accumulators, once per tile, never per sample.

---

## B. Defect \u00a74.5 — the unreadable `Color32` write. FIXED by deletion, re-measured

### B.1 What was changed, and which of the two options was taken

`EncodePremultiplied` was **deleted**. `EncodeStraight` was renamed `Encode` and is now the single `Color32` write \u2014 FC-2.3's one encode boundary, straight alpha, un-premultiplied in linear and encoded once.

The escalation offered two outs: repair the premultiplied encoder's order of operations, or delete it if the additive case does not actually need it. **Delete was taken, and the reason is that repairing it does not rescue the case it existed for.** The justification for premultiplication is sound in general \u2014 only premultiplied can express an additive glow over nothing (\u03b1 = 0, RGB > 0). It is not sound about any correct 8-bit *encoding* of that sample: the conventional premultiplied-sRGB byte is `encode(rgb) \u00b7 \u03b1`, and at \u03b1 = 0 that is 0, which deletes the glow exactly as thoroughly as straight alpha does. So repairing the order of operations would have produced a second encoder that is standards-conformant, unreadable-by-nobody, and still unable to do the one job it was kept for. It had no caller anywhere in the project. Deleting it trades an undocumented misencoding for a documented limitation, which is the better trade.

### B.2 Before and after, one instrument, linear-white fill

| \u03b1 | OLD `EncodePremultiplied` = `encode(rgb\u00b7\u03b1)` | NEW `Encode` (straight) | conventional premultiplied-sRGB `encode(rgb)\u00b7\u03b1` |
|---|---|---|---|
| 1.00 | 255 | 255 | 255 |
| 0.75 | 225 | **255** | 191 |
| 0.50 | 188 | **255** | 128 |
| 0.25 | 137 | **255** | 64 |
| 0.10 | 89 | **255** | 26 |
| 0.00 | 0 | 0 | 0 |

The old column matches neither of the other two at any alpha strictly between the endpoints, which is the whole defect. Over **all 256 codes at \u03b1 = 0.5**, read back by a straight-alpha consumer: the old encoder was wrong by up to **67 codes**; the new one by **0**.

### B.3 The new leg that would have caught it \u2014 FT-22

Three legs, all PASS:

- **(a)** straight round trip, all 256 codes, at \u03b1 \u2208 {1.00, 0.75, 0.50, 0.25, 0.10}: **0/256 codes wrong at every alpha, worst |delta| 0 codes**. The old encoder fails this at every alpha but 1.00.
- **(b)** the alpha byte itself round-trips: **0/256 wrong**.
- **(c)** additive-over-transparent is still representable. Through the full integrated path: the float destination holds R 1.0000 with alpha 0.0000, finite \u2014 the representation that actually carries an additive result. Into bytes through an opaque backdrop, the backdrop goes **111 \u2192 255**, i.e. the glow brightens it, which is how an additive fill reads in an 8-bit image at all. And `Encode`'s `\u03b1 \u2264 1e-6` branch does not divide, so it keeps the colour byte and honestly reports \u03b1 = 0; a straight-alpha blender will discard that, which is the stated limitation rather than a third answer.

### B.4 What was NOT corrupted stayed uncorrupted

\u00a74.5's own finding \u2014 that the ordinary `Over` path is exactly right in linear and a mid-grey survives the whole round trip at 0 codes over all 256 levels \u2014 still holds: FT-2 (sRGB round trip) and FT-1/FT-3 (bit-exact cross-kind identity) pass unchanged, and FT-22 (a) is the stronger version of the same claim taken at five alphas rather than at opacity only.

---

## C. What was amended in `FILL-CONTRACT.md`

Three clauses added in place, each marked `[T-0106 fix pass, 2026-08-31]`, each recording the superseded reading explicitly rather than quietly replacing it, and each carrying the measurement that forced it.

- **FC-2.6a-AMENDED** \u2014 the SCOPE correction. The formula is unchanged and stays correct for what it governs; what was wrong was reading it as the rule for compositing every fill against the destination, including two fills that own disjoint parts of one silhouette. `Over` describes how a finished layer meets what is beneath it, never how one silhouette's own exclusive owners meet each other.
- **FC-3.5a** \u2014 the accumulation rule FC-3.5 always implied and never said: exclusive owners accumulate by claim-weighted sum; sibling claims overlap and still composite `Over` in fold order. Carries the cost (the per-owner accumulator) and the `descendantClaim`-is-a-MAX limitation.
- **FC-3.5b** \u2014 the mixed `Over`/`Add` case, defined.
- **FC-2.3a** \u2014 the `Color32` write is straight alpha and there is exactly one of it; records the deleted encoder, its measured misencoding table, why deletion beat repair, and where the additive case lives instead.

FC-3.8's table is still wrong in the way \u00a71 describes. **I did not fix it**, because \u00a71's recommendation is to re-geometry the worked example so the Intersect member is not the last restrictor, which is a rewrite of the example rather than an amendment, and it is not one of the two items this pass was scoped to close.

---

## D. Re-verification of everything, not just the two changes

- **`ShaperFillAudit`, full run.** **21 legs asserting, 21 RESULT: PASS, 0 FAIL** \u2014 FT-1 through FT-19, FT-21 (now three legs) and the new FT-22, plus FT-20 which renders the contact sheet and correctly refuses to report itself as a pass. No pre-existing test regressed; FT-7b, FT-17 (Add alpha 0/16384) and FT-18 (Over vs Add height, 0/16384 mismatched, worst 0.00e+0) all still pass, which are the three the fix could most plausibly have broken. `FILL-AUDIT.txt` regenerated from that run.
- **`ShaperFieldAudit`.** **13/13 PASS, 0 FAIL**, re-run leg by leg in this pass (the pipeline's reply timeout is a fixed 30 s and several legs run longer, so each leg was invoked separately and the gate was the report count reaching 13, not the CLI returning).
- **Contact sheet.** Re-rendered to `fill-contact-sheet.png`, **21 cells, 7 \u00d7 3, 720 \u00d7 312 px**, fully opaque, same mid-grey checkerboard, every cell still carrying its two-digit index. 7 columns because 21 = 7 \u00d7 3 exactly \u2014 the grid has no empty tail position, which was one of the things wrong with the first sheet. All twenty original cells are kept and unchanged in content.

  **Cell 21 is new: the internal fill boundary, with the seam drawn rather than tabulated.** A bag fill and a nested child fill partition one silhouette, and the child is a long, thin, rotated ellipse precisely to maximise the length of internal boundary in one cell. Every sample whose composited alpha still falls below the shape's own coverage is stamped **magenta**, at the same 1e-3 threshold FT-21 leg (b) uses, so the picture and the table cannot disagree. **Marks in this render: 0.** Before the fix a magenta line would have traced the whole bar.

  **Looked at, by eye.** All 21 cells render their case and are legible. **What changed, and why:** cells 01\u201315 and 16 are unaffected \u2014 they are single-owner or sibling-only and A.5 measures them bit-identical. Cell 17 changed by at most 3e\u22128, invisible, and it is float summation order rather than a colour shift. Cells 18, 19 and 20 changed only along their internal fill boundaries, only in alpha, and only in the direction of the fix \u2014 the checker no longer shows through a one-pixel ring where a child fill meets its parent's. **Nothing shifted for the wrong reason:** defect 4's change cannot reach the sheet at all, because a cell is composited over an opaque backdrop before a single `ShaperSrgb.EncodeToByte` call and never goes through `Encode`, and that helper is byte-for-byte the same code it was.

---

## E. What this pass could NOT verify

- **The "before" numbers are a faithful reconstruction, not a re-run of the old binary.** The previous `PaintTile` and `EncodePremultiplied` no longer exist and the tree is untracked in git, so "before" was rebuilt in-probe from the shipped run's own `paint[]`, `ownCoverage[]` and per-owner fill evaluations. It reproduces \u00a73(iii)'s 244 / 29.288 / 0.2484 and \u00a74.5's 225 / 188 / 137 / 89 exactly, which is strong evidence the reconstruction is faithful \u2014 but it is a reconstruction.
- **No consumer of `Encode` exists to test against.** The claim that straight alpha is what every consumer wants rests on FC-2.3's wording, on PNG having no premultiplied mode, and on Unity's sprite blend being `SrcAlpha OneMinusSrcAlpha`. T-0106 ships no consumer, so this is verified by argument and by round trip, not by rendering through a real material.
- **The alpha-overshoot clamp is verified as an expression, not exercised end to end.** No shipped fixture produces two spatially disjoint fill-owning descendants at fractional coverage under one ancestor, so the `> 1` branch was reasoned about and written, and I did not construct a fixture that trips it.
- **Performance.** As with the verification above, allocation was measured and time was not. No throughput number here either, and `PaintTile` now clears and writes a per-owner accumulator, which is real work that nobody has timed.
- **FC-3.8's table is still wrong** (\u00a71), and the `Fixed`-space canvas-centre limitation (\u00a72) is still unfixed. Both were out of this pass's scope and both remain open.
- **Everything in \u00a78 above that was not one of the two escalated items remains as the verifier left it** \u2014 the pre-T-0106 shape files still do not exist on disk, unbounded coverage still cannot be exercised, `ZuiGradient.Evaluate`'s transform semantics are still unread, and no Shaper code is Burst-compiled.
"""

s = s.rstrip(u"\n") + u"\n" + section
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print("appended, chars =", len(s))
