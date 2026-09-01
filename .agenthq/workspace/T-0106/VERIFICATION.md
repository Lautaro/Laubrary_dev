# T-0106 Wave 2 — independent verification of the Shaper fill stage

Written 2026-08-31 by the independent verifier, against `D:\UNITY\Laubrary Dev - Shaper` on pipeline port 7801. Every result below was produced by my own probes, not by re-running the implementer's audit, except where I say explicitly that I re-ran theirs. `Application.dataPath` was asserted as `D:/UNITY/Laubrary Dev - Shaper/Assets` before any measurement was believed, and `EditorUtility.scriptCompilationFailed` was `False` at every measurement point, gated additionally on a reflection probe for a symbol I had just added. Nothing is committed; the worktree is left dirty as instructed.

**Headline.** The implementer's audit reported 20/20 PASS. Six defects survived it. Three were real defects in shipped runtime behaviour, all three are now fixed and re-measured. One is a genuine spec contradiction that I am escalating rather than resolving unilaterally. Two are defects in the *tests* — instruments that structurally cannot fail — and both instruments have been replaced. Separately, the single ruling the brief asked for (question B) goes against the specification's worked example and in favour of its rule, and I show why with numbers. The contact sheet was unreadable in two cells and is re-rendered; every one of its twenty cells now demonstrates its case against a visible backdrop.

---

## 0. What I measured with, and why each instrument can detect the failure it is aimed at

The lesson of T-0105 is that an audit's *verdict* is only worth its *instrument*, so each instrument is stated with the reason it is sensitive.

- **Bit-exact float comparison, not epsilon.** Every "identical" claim below compares `System.BitConverter.SingleToInt32Bits`, or `a[i] != b[i]` on raw floats. An epsilon comparison would have hidden the sign-of-zero result in §6 and would have made the tile-independence results meaningless, since BC-1.6 asks for bit-identity and not for closeness.
- **IL opcode scan for the allocation claim.** Managed allocation is three opcodes — `newobj` (0x73), `newarr` (0x8D), `box` (0x8C) — and none of them can hide from a scan of `MethodBody.GetILAsByteArray()`. A byte-frequency scan is an *upper* bound, because an operand byte can coincide with an opcode value, so a zero is conclusive and a non-zero would need disassembling. Zero is what the fill loop reports. This is strictly stronger than any heap-delta measurement, and it is why I could prove the implementer's *claim* true while proving their *test* worthless.
- **Deliberately short arrays for the read-set claim.** To detect a read whose value is discarded, you cannot perturb the value — you have to make the read itself fault. Handing a fill a one-element array for every sheet it did not declare turns any index into an `IndexOutOfRangeException`. This is an instrumented measurement, where the implementer's is behavioural.
- **Calibration ladders before trusting any counter.** I calibrated `GC.GetAllocatedBytesForCurrentThread`, `GC.GetTotalMemory(false)` and `GC.CollectionCount(0)` against known allocations before reading anything into them. Two of the three turned out to be blind on this Mono runtime, which is the finding in §5.1.
- **Forced domain reload for the determinism claim.** `EditorUtility.RequestScriptReload()` between two runs, with FNV-1a hashes of the whole 128×128 RGBA float destination persisted to disk across the reload. The implementer's FT-10 measures within one session by their own admission; this closes that gap.
- **A full-turn rotation sweep for the anchor claim.** 24 angles at 15° steps, sampling at a point that rotates *with* the shape, so an anchor that breathed would show as a spread in `(u,v)` rather than having to be inferred from a picture.

---

## 1. Question B — the ruling: FC-3.3 is right and FC-3.8's table is wrong, but the 119 samples were a third thing entirely

This is the highest-value judgement in the brief and the answer has three parts, because the question conflates them.

**Part one — who is right, the rule or the example.** `FC-3.3` rules that an Intersect member owning a fill paints the entire intersected result. `FC-3.8`'s table gives `G_body` three whole regions. They cannot both hold, and **FC-3.8's table is the one that is wrong** — not on one row, but on all three rows where it names `G_body` or gives `G_body` a share. The argument is internal to the table and needs no new principle. Row 2 of that table ("Torso ∩ Head ∩ Belt") correctly reasons that ownership is exclusive and that the bag therefore does *not* paint where a descendant claims. Row 1 ("Torso ∩ Belt, not Head, not Iris") then awards that region to `G_body` — but `T_belt`'s claim is non-empty there too, for exactly the reason row 2 gives, so row 1 contradicts row 2 by forgetting that Belt covers Torso∩Belt as well as Head∩Belt. Row 3 repeats the slip. The table's author reasoned about Head and Iris as descendants and forgot that Belt is one.

**And the geometry makes it worse than a slip.** In FC-3.8's own tree, Belt is the *last restrictor* in the fold. `coverage_Body = ((Torso ∪ Head) ∩ Belt) ∪ Iris`, so everywhere the bag has any coverage at all, that coverage came through Belt — which means `claim_Belt = min(coverage_Belt, coverage_Body) = coverage_Body` identically. Belt's claim is not merely large, it is *exactly the bag's own claim*. So the correct answer under FC-3.3 + FC-3.1 + FC-3.5 is that **`G_body` paints zero samples**, and no table entry can rescue it. That is not a pathology of the rule; it is what "the cookie cutter is the last thing standing" means. Measured, on my own reconstruction of the tree: `Belt` 1122 samples / area 1104.46, `Head` 32 / 28.95, `Iris` 32 / 28.95, `Body` **0 / 0.00**.

**Part two — the 119 samples are not FC-3.3's consequence, they are a bug.** This is the part the implementer's report gets wrong and it is the reason the brief's instinct ("a rule whose worked example paints only an antialiasing fringe is a strong smell") was right while its diagnosis was aimed at the wrong clause. The fringe was not produced by FC-3.3. It was produced by FC-3.5's *implementation*: the implementer extended exclusivity continuously as `claim · (1 − descendantClaim)`. Where `claim_Belt == claim_Body == c` — which, per part one, is every antialiased sample of the whole silhouette — that expression leaves the bag `c · (1 − c)`, which is non-zero for every `0 < c < 1`. Measured, on the leaking samples: `claimBody = 0.8157`, `descClaim = 0.8157`, painted `0.1503`; `0.6362 / 0.6362 → 0.2315`; `0.3403 / 0.3403 → 0.2245`. That is a fringe of the *bag's* colour tracing the entire outline at up to 25% strength — a visible artefact, not a rounding residue.

**Part three — the correct behaviour, and what I did.** The correct continuous extension of "exactly ONE owner per pixel" is **set difference, not independent probability**: `paint = max(0, claim − descendantClaim)`. `claim` is an area fraction, and the min-chain (`claim[o] = min(ownCoverage[o], claim[ancestor])`) guarantees a descendant's covered sub-area is a *subset* of its ancestor's, so subtraction is well-defined, never negative, and is the geometrically right split. The two forms agree exactly at both limits (`e = 0` gives `c`; `e = 1` forces `c = 1` by the chain and gives `0`), and they differ **only where the ancestor's own claim is below 1** — that is, only along the ancestor's own antialiased edge, which is precisely where subtraction is the correct model and multiplication is not. I checked the awkward direction too: where two descendants of one ancestor are spatially disjoint within a pixel, both forms are wrong, but they can only differ if `claim_ancestor < 1`, and there subtraction is still the closer of the two. **So subtraction is never worse and is materially better in the demonstrated case.** I applied it (§4.1), and `G_body` now paints 0/16384 in the implementer's own FT-7 fixture, down from their reported 119.

**Recommended documentation change** (I have not edited the contract, only the audit's report text): FC-3.8's table needs rows 1, 2 and 3 rewritten to name `T_belt` as the owner, and the worked example should ideally be re-geometried so the Intersect member is *not* the last restrictor — otherwise the example demonstrates a degenerate case and teaches the wrong lesson about the default in FC-3.7. FC-3.3 itself needs no change; its own admission that the result is "indistinguishable in output from putting the same fill on the bag" is correct and is the honest cost of the rule.

---

## 2. Question D — the anchor. Verified by measurement, and the "undocumented deviation #4" is a correction to FC-1.5, not a deviation from it

**(i) The local box is genuinely local, not the canvas box mapped back down.** On a rect authored at half-extents 30/12 rotated to 30°, the compiler publishes `localSupportHalfW/H = 30.00000 / 12.00000` — the authored numbers, unchanged. The forbidden derivation, mapping the canvas box down through `rootInverse` with the same absolute-value corner sum FC-1.5a names, would give `40.39230 / 37.98076`: an inflation of **1.3464× on X and 3.1651× on Y**. The two are not close and cannot be confused. Reading the code confirms the mechanism: a leaf's `localBox` is `Box.FromCentre(0, 0, halfExtentX, halfExtentY)` before any transform, and a bag folds its children's local boxes through each child's **relative** `localToParent` only, never through the accumulated forward.

**(ii) A gradient on a rotating shape does not breathe.** Over 24 angles through a full turn, sampling at the shape-local point (15, 6) carried round with the shape, the normalised anchor coordinate was constant to float epsilon: `u ∈ [0.5000000, 0.5000001]`, span `5.96e-8`; `v` span `8.94e-8`. `localSupportHalfW` was constant to `0.000e+0` across all 24. The rejected candidate breathes exactly as FC-1.5 predicted: `supportHalfW` ranged `[12.00000, 32.08360]`, a **2.6736× swing per turn** with nothing authored changing. No FT test covers this; it is now measured.

**(iii) Deviation #4 (subtracting the local box centre) is required, and FC-1.5's stated formula is the thing that is wrong.** On a bag whose two members sit at x = 50 and x = 70 (box centre (60, 30), half-extents 20/10), the shipped anchor gives `(0, 0)` at the box centre, `(−1, 0)` at its left edge and `(+1, 0)` at its right edge — a ramp that runs end to end across the shape. FC-1.5's literal formula, `(lx, ly) / max(hx, hy)` with no recentring, would give `(3.0000, 1.5000)` at the box centre: the entire shape would sit past the end of the ramp and every gradient on any offset bag would render as one flat colour. **Recommendation: amend FC-1.5's formula to subtract the local box centre.** The implementer was right to deviate and wrong only to leave it undocumented.

**One latent limitation I found while measuring (i)–(iii)**, reported because nothing in the contract or the audit catches it: `ShaperFillAnchor` carries `canvasHalfW/canvasHalfH` but **no canvas centre**, and `BakeAnchor` hardcodes `cx = cy = 0` for `Fixed` space. Every shipped path builds its grid with `ShaperSampleGrid.Centred`, whose origin is `−0.5·(w−1)·pixelSize`, so the canvas centre is the origin and the assumption holds today (measured: `Fixed` `(u,v)` at canvas (0,0) is exactly `(0,0)`). A host that supplies a grid with any other origin would get an off-centre `Fixed` pattern with no diagnostic. FC-1.6 says "normalised by the layer canvas half-extents" and never says where the centre is, so the contract does not catch it either. **Severity: low, latent. Not fixed** — the fix is a field on `ShaperFillAnchor`, which is an API change I judged out of scope for a verification pass.

---

## 3. Question C — exclusivity at the limits, in the middle, and three deep

Fixture: three concentric fill-owning nodes, `A` (r 42, red) ⊃ `B` (r 28, green) ⊃ `C` (r 14, blue), so every pair is ancestor–descendant and every paint region is disjoint by construction. 244 of the 16384 samples have fractional coverage, so the fixture is not vacuous.

**(i) The limits are correct.** `e = 0` → `paint = claim`; `e = 1` → `paint = 0`. Both forms agree here and both are right.

**(ii) There is no double-counting or energy loss along the chain, after the §4.1 fix.** Summing every owner's paint and comparing against the root's own coverage, sample by sample: **0/16384 samples differ, worst deviation 0.00000.** Total painted area 5543.69 against a root coverage area of 5543.69. Before the fix, the concentric fixture happened to agree too (because `claim_A = 1` wherever `claim_B > 0` there, and the two forms coincide when the ancestor's claim is 1) — which is exactly why the implementer's tests could not see the problem and why the FC-3.8 tree, where the ancestor's claim *is* fractional, was the fixture that exposed it.

**(iii) There IS a visible artefact at a descendant's soft edge, and it is a different defect from (ii).** Comparing the composited destination alpha against the shape's own published coverage: **244/16384 samples come out below coverage, total deficit 29.288, worst 0.2484.** That is a one-pixel ring, up to 25% transparent, around every internal fill boundary. It is not the exclusivity expression — it survives the §4.1 fix unchanged. It is `Over`. Exclusive owners hold *disjoint* sub-areas of a pixel and `Over` is the compositing rule for *independent* ones: an ancestor at 0.5 and a descendant at 0.5 give `0.5 + 0.5·0.5 = 0.75` where the shape's coverage is 1.0. This is a contradiction between **FC-3.5** (ownership exclusive, therefore the regions are disjoint) and **FC-3.4 / FC-2.6a** (composite with `Over`, which assumes they are not). Both clauses are individually defensible; together they are inconsistent at fractional coverage. See §4.4 for why I did not fix it.

---

## 4. Defects, most severe first

### 4.1 Multiplicative exclusivity leaks the ancestor's colour into every antialias fringe — `ShaperFillResolver.cs`, `PaintTile` step 4 — **FIXED and re-verified**

**Severity: high.** Visible artefact on the contract's own worked example. Full argument in §1 part two.

`buf.paint = c * (1f - e)` replaced with `max(0, c - e)`. Re-measured on the FC-3.8 tree: `G_body` painted samples **58 → 0** on my reconstruction, **119 → 0** on the implementer's own FT-7 fixture. Total painted area over the tree 1169.574 → 1162.354 against a root coverage of 1133.406; the residual 28.9 is the Head/Iris **sibling** overlap, which FC-3.4 explicitly wants composited rather than excluded, so it is correct and unchanged. Chain conservation on the three-deep fixture: 0/16384 samples off, worst 0.00000. All 20 pre-existing FT tests still pass afterwards; FT-7's probe table is unchanged row for row.

### 4.2 A NaN veil writes NaN into every destination float, with no diagnostic — `ShaperFillCompiler.cs` + `ShaperFillResolver.cs` — **FIXED and re-verified**

**Severity: medium.** `Mathf.Clamp01` is `v < 0 ? 0 : v > 1 ? 1 : v`, and every comparison against NaN is false, so it passes NaN through untouched. `if (ce <= 0f) continue` is likewise false for NaN, so the composite proceeded. Measured before the fix, with a `ZUIValue(float.NaN)` veil on a plain Solid disc: **16384/16384 non-finite floats in the destination**, baked veil `NaN`, and `EncodeToByte(NaN)` produces a garbage byte with no diagnostic at all — the "renders silently" failure FC-6.5 exists to forbid. The veil is an animatable `ZUIValue`, so its value is arithmetic the author never sees; this is not a theoretical input.

FT-14 swept 288 parameter combinations and never a NaN, so it could not see this. FT-15's degenerate-input list (zero extent, degenerate window, size 0, null gradient, null texture, unreadable texture, empty bag) does not include a non-finite dial either.

Fixed with `Clamp01OrZero` (`v > 0f ? (v < 1f ? v : 1f) : 0f`, so NaN falls to 0 by ordering rather than by an `IsNaN` test) and `FiniteOrZero` for the height delta, plus `if (!(ce > 0f)) continue` in the compositor as a second line of defence for a NaN arriving from anywhere else. Re-measured across the full adversarial set: `−1 → 0`, `+2 → 1`, `1e30 → 1`, `+Inf → 1`, `−Inf → 0`, `NaN → 0`; **0 non-finite destination floats in every case, 0 samples of paint outside coverage in every case.** An animated `Curve` veil authored `min = −3, max = 4` bakes to 1.000 at every phase tested (0, 0.25, 0.5, 0.75, 1.0), so the animated out-of-range case was already safe and remains so.

### 4.3 Every fill kind indexed the `edgeDistance` sheet, including those declaring `[None]` — `ShaperFillOps.cs`, `FillTile` — **FIXED and re-verified**

**Severity: low–medium.** `float[] edgeSheet = sheets.edgeDistance;` was taken unconditionally, so a Solid fill declaring `[None]` and a Ramp-by-coverage declaring `[Coverage]` both read the edge array at every sample. This is a literal violation of FT-12 as the contract words it ("A fill that reads a sheet it did not declare fails the build"), and it is live rather than cosmetic: BC-3.7a and FC-2.5 both prescribe *declared-then-allocated*, so a host is entitled to size or share arrays strictly by the declared set. Measured: handing a Solid fill a one-element `edgeDistance` array threw `IndexOutOfRangeException`; same for Ramp-by-coverage.

FT-12 could not see it because it measures reads *behaviourally* — perturb a sheet, look for a change in the albedo — and a read whose value is discarded moves nothing. The instrument is not circular (it does not derive "measured" from the declaration, which was the brief's suspicion) but it has this specific blind spot.

Fixed by gating `edgeSheet` on `op.kind == Gradient && op.gradientMode == ByEdgeDistance`, mirroring the `rampSheet` line one above it. Re-measured: both traps now return "no throw — the sheet was NOT touched", and FT-12's new instrumented leg reports **0 undeclared sheets indexed across all 8 cases**.

### 4.4 `Over` between exclusive owners under-reports alpha at a descendant's soft edge — **CONFIRMED, NOT FIXED, needs a spec ruling**

**Severity: high (visible), but it is a contract contradiction and not an implementation slip.** Numbers in §3(iii): 244/16384 samples, total deficit 29.288, worst 0.2484, i.e. a 1px ring up to 25% transparent at every internal fill boundary.

**Why I did not fix it.** The correct compositing for exclusive owners is a *sum in premultiplied space* (`alpha = c_d·v_d + (c_a − c_d)·v_a`), which is exactly 1.0 in the case that currently gives 0.75 and which also handles FC-3.5's "descendant over transparent" requirement correctly. But summation is *wrong* for siblings, whose claims genuinely overlap and which FC-3.4 explicitly requires to composite `Over` — the contract even names the failure mode ("Reading 'wins' as 'replaces' would make a translucent veil on the upper sibling produce a hole rather than a blend"). So a correct compositor has to distinguish ancestor↔descendant pairs (disjoint → sum) from sibling pairs (overlapping → `Over`) per pixel, which means either a per-owner destination accumulator (k × n × 4 floats) or a recursive per-subtree composite. Both are a redesign of `PaintTile`, both change behaviour that FC-2.6a specifies in formula form, and neither is something a verification pass should land unilaterally. **This is the item I am escalating.** It is recorded as FT-21 leg (b) in the audit, reported as a measured number and explicitly marked KNOWN OPEN rather than counted as a pass.

### 4.5 The default `Color32` writer emits a representation no consumer can read back — `ShaperFillResolver.EncodePremultiplied` — **NOT FIXED, worked around in the contact sheet**

**Severity: medium, and it is the implementer's deviation #5.** The premultiplication happens in *linear*, and then sRGB encode is applied to the already-premultiplied value. That is neither straight-alpha nor the conventional premultiplied-sRGB (which premultiplies *after* encoding), so neither kind of consumer reads it correctly. Measured, for a linear-white fill at each alpha, premultiplied byte versus straight byte:

| alpha | premult RGB | straight RGB | conventional premultiplied-sRGB would be |
|---|---|---|---|
| 1.000 | 255 | 255 | 255 |
| 0.750 | 225 | 255 | 191 |
| 0.500 | 188 | 255 | 128 |
| 0.250 | 137 | 255 | 64 |
| 0.100 | 89 | 255 | 26 |
| 0.000 | 0 | 0 | 0 |

At α = 0.5 a premultiplied-alpha consumer reconstructs 188/128 = **1.47× too bright**; a straight-alpha consumer sees 188 where it should see 255, **26% too dark**. The two encoders agree only at α = 0 and α = 1, which is inevitable and not itself the defect.

**The implementer's justification is sound and the implementation is not.** They are right that only premultiplied can represent `Add`-over-transparent (alpha 0, RGB > 0), which straight alpha cannot express at all. The fix is to premultiply in the *encoded* space — `round(255 · encode(rgb/α) · α)` — or to document the buffer as linear-premultiplied and never hand it to a PNG. **A PNG is straight-alpha by definition**, so writing this buffer to one is wrong for every non-opaque pixel, and that is a large part of why the old contact sheet was unreadable.

I did not change the encoder, because which convention the document should ship is a decision above a verifier's pay grade and both encoders have callers. I did fix the *deliverable*: the contact sheet now composites over an opaque backdrop before the single encode, so the PNG is fully opaque and the question does not arise (§7).

**What is NOT corrupted, measured:** the ordinary `Over` path is exactly right in linear. Half-veiled white over an opaque mid-grey gives linear `0.607930` against a hand-computed expected `0.607930` and encodes to byte 205, which is `round(255·encode(0.607930))` exactly. And a **mid-grey survives the entire round trip to zero codes**: over all 256 grey levels, authored `Color` → sRGB decode → linear albedo → composite → `EncodePremultiplied`, the opaque interior sample came back with **worst |out − in| = 0 codes**. FC-2.3 is correct end to end.

### 4.6 Two tests that cannot fail — `ShaperFillAudit.cs`, FT-5 and FT-9 — **both instruments replaced and re-verified**

**FT-5 could not detect the removal of the clause it is named for.** Every assertion in it reads `dst` alpha out of `PaintTile`, and `PaintTile` applies a *second* `Mathf.Clamp01` to the veil at use. So deleting FC-2.4's bake-time clamp — "the load-bearing half", per the contract — changes nothing FT-5 looks at. Measured directly, by overwriting the compiled op's veil with 2.0 (exactly what deleting the bake clamp produces) and re-running the same measurement: coverageEff at the half-covered sample stayed at **0.453203**, identical to the clamped run. FT-5 would have reported PASS. Its own explanatory sentence was wrong twice over: "without the clamp it would be 1.0" — with the bake clamp gone it is 0.453203, and with *both* clamps gone it is 0.906406.

Fixed by adding a leg that asserts the bake directly on the compiled op: authored 2 → 1.0000, −1 → 0.0000, +Inf → 1.0000, NaN → 0.0000. That leg goes red if the clamp is weakened, which the old one could not.

**FT-9's instrument is blind in both directions.** Calibrated on this editor: `GC.GetTotalMemory(false)` reports a delta of **0 for real allocations of 1 KB, 8 KB and 64 KB**, and first moves at 512 KB. `GC.CollectionCount(0)` reports **0 gen-0 collections for 1 MB of garbage in 1000 allocations** — so the leg the audit calls "the stronger of the two signals" is the weaker one. Over FT-9's 40 reps, a loop allocating up to ~1.6 KB per call would have passed undetected. And it produces false positives too: on one run after my edits, the same leg reported deltas of **4096 and 12288 bytes** for Gradient and Ramp on a loop the IL scan proves contains no allocation opcode at all, because `GC.GetTotalMemory` is process-wide and picks up the editor's own background churn.

I also self-tested the obvious replacement first and it failed: `GC.GetAllocatedBytesForCurrentThread()` returned a delta of **0 bytes for a `new float[10]` and 0 bytes for a boxed float** on this Mono runtime. It is not implemented here. Had I trusted it, every zero I measured would have been meaningless — which is the whole reason to calibrate before believing.

Fixed by making the IL scan the assertion and the heap numbers informational, with the calibration printed in the report so the floor is a number rather than an assumption. **The implementer's claim was true**: 0 `newobj`/`newarr`/`box` occurrences across `FillTile` (364 B), `Sample` (743 B), `Anchor` (86 B), `ByEdgeDistanceT` (10 B) and `Lut` (103 B). Their test just could not establish it.

---

## 5. Everything else the brief asked me to attack, with the numbers

### 5.1 The other FT instruments

**FT-11 (tile independence).** The 7×5 decomposition does cross interesting boundaries — 128 = 7·18 + 2 and 5·25 + 3, so tile edges land at non-aligned offsets and the last tile of each row and column is ragged. That part of the brief's suspicion is unfounded. The real gap was scope: FT-11 drove `ShaperFillOps.FillTile` only, never the integrated `ShaperFillResolver.PaintTile`, which additionally evaluates each owner's shape program per tile, rebuilds the claim/descendant-claim/paint arrays per tile and composites — three more chances to seam. I measured it: whole grid versus the same 7×5 decomposition, two owners, `Texture.Tiled` in `Fixed` space, **0/36864 floats differ**. It is clean, but it was untested; a leg is now in the audit. FT-11 also compared albedo and veil but not the height delta; the new integrated leg compares the composited destination, which carries height's effect.

**FT-12 (declared == read).** Not circular — "measured" comes from perturbing each sheet and watching the albedo, which is independent of the declaration. Its blind spot is discarded reads (§4.3), now closed.

**FT-3 (cross-kind identity).** Re-ran: all six cases 0/49152 albedo mismatches, worst |delta| 0.00e+0. This is a strong test and I found nothing wrong with it.

**FT-10 (determinism).** Its within-session legs are sound. I closed the admitted gap: five fixtures (Solid, Gradient.Radial, Ramp, Texture, Solid + `MinMax` veil) hashed with FNV-1a over the full 128×128×4 float destination, persisted to disk, then re-run **after a forced `EditorUtility.RequestScriptReload()`**. All five hashes **identical across the reload**. The `MinMax` leg is the one that would catch a stray `System.Random`, and it survives.

### 5.2 The integrated path versus the direct call (question H)

This is the class of defect the implementer found only by rendering a picture, so I swept it exhaustively rather than spot-checking. **48 configurations** — Solid; Gradient × 4 modes × 2 spaces × 2 fits; Ramp × 2 available quantities; Texture × 2 mappings × 2 spaces; each × both composites — comparing `ShaperFillResolver.PaintTile`'s destination against a hand-composite of `ShaperFillOps.FillTile`'s output using the same per-owner sheets.

**Result: max delta 0.00e+0 in all 48. Zero cases where the integrated path differs from the direct path.** No further instance of that defect class exists.

I also checked that each fill actually *varies* through the integrated path rather than rendering flat, counting distinct RGB values inside the silhouette: Gradient.Linear 71, Radial 206, Angular 253, ByEdgeDistance 148, Ramp-by-edgeDistance 216, Texture 2 (a two-colour checker, correct), Solid 1 (correct). Ramp-by-coverage reports 1 and that is also correct, not a defect: coverage is 1 everywhere inside a silhouette, so a ramp *by coverage* is one colour there and its ramp is the one-pixel rim.

And I checked that the dials do something, on a rotated, offset, non-square node — a test no FT covers, and the one place a "the dial is wired to nothing" bug would hide: `Stamped` vs `Fixed` 3001 floats differ (max 0.7200); Radial `Uniform` vs `Stretch` 2774 (max 0.7994); Texture `Fitted` vs `Tiled` 2958; Texture `Stamped` vs `Fixed` 3003; Texture angle 0° vs 37° 3240. Two null results, both correct rather than broken, and both worth recording because they look like failures: `Linear` at θ = 0 gives **0 floats differ** between `Uniform` and `Stretch`, because θ = 0 reads only `u` and `u` is divided by `max(hx,hy) = hx = 30` either way — at θ = 90° the same pair differs by 2880 floats (max 0.5163) and at θ = 45° by 2880 (max 0.4028). And in `Fixed` space `Uniform` and `Stretch` are identical on a **square** canvas (measured: 0 floats differ), because one divisor and two equal divisors are the same number.

### 5.3 Question E — regression on the shape stage

**`ShaperFieldAudit` re-run by me, in four chunks: 13/13 PASS, 0 FAIL.** V1 and V2 report 0 mismatched bits over 248897 samples each.

The bit-identity of the old `Compile(root, phase01, seed)` signature cannot be diffed literally, because the entire `Runtime/Shaper` tree is untracked in git and no pre-T-0106 copy exists anywhere on disk (I checked the main worktree, the T-0105 workspace and the Shaper worktree). I constructed the strongest available substitute instead, in three parts.

1. **The only route by which `parentForward` can perturb the old path is `Mul(parentForward, localToParent)`, and the old code hardcoded `Identity` there.** So: is `Mul(Identity, X)` bitwise `X`? Over 24 adversarial matrices built from ±0, ±1, ±1e−30, ±1e30, ±`float.Epsilon` and irrationals, **6 component-level mismatches, every single one of them the sign bit of a zero** (`−0.0 → +0.0`), and **0 mismatches of any other kind**. `−0.0` and `+0.0` compare equal and are indistinguishable everywhere downstream here — nothing divides by a matrix component, and `TryInvert` divides by the determinant, which is 0 either way — and a `ShaperTransformBlock` cannot produce `−0.0` through translate/rotate/scale/skew authoring in the first place.
2. **The two overloads produce identical programs.** On a deliberately nasty tree (bag with rotation and translation; an ellipse child with rotation, translation and non-uniform scale; a Subtract rect with a shell; an Intersect N-gon with a sweep): 9 ops, **field-by-field bitwise differences = 0**, and `bound`, `stackDepth` and the whole canvas support box equal.
3. **The `localBox` fold is purely additive and does not feed back.** Reading the code, `localBox` is computed in parallel with `box` and is only ever assigned to `program.localSupport*`. Measured confirmation on a rotated rect: the leaf op's own canvas box `(11.00000, −4.00000) half 31.98076/25.39231`-style values are bitwise equal to `program.support*`, so the canvas-box path is untouched.

I cannot rule out that the pre-change file differed from the current one in some way *other* than the `parentForward` threading. That is a limit of the evidence and it is recorded in §8.

### 5.4 Question G — the veil invariants

Covered in §4.2. Summary of the adversarial set after the fix, all measured through the full integrated path on a disc: `−1 → 0`, `+2 → 1`, `1e30 → 1`, `NaN → 0`, `+Inf → 1`, `−Inf → 0`; in every case **0 non-finite destination floats, 0 samples of paint outside coverage, max alpha never above 1.0**. An animated `Curve` veil with `min = −3, max = 4` bakes to 1.000 at all five phases sampled.

**The one case I could not exercise through the integrated path** is a veil on a node whose published coverage exceeds 1 — BC-3.3's unbounded fog. `PaintTile` recomputes coverage from each owner's own compiled program at step 1, and the shipped shape engine's `ShaperField.Coverage` clamps to [0,1] by construction, so no injection point exists short of writing a fake generator. I verified the *expression* instead: `clamp01(1.7) · clamp01(1.0) = 1.0000`, and the clamp sits at use in `PaintTile` exactly as FC-1.6 and FC-2.4a require. That is a partial verification and is listed as such in §8.

---

## 6. What I re-ran of the implementer's own audit

All 20 original FT tests, in three chunks, after all three of my fixes. **20/20 PASS, plus the new FT-21.** No pre-existing test regressed. `FILL-AUDIT.txt` has been regenerated from that run so the attached output matches the shipped code, and it now carries: FT-5's direct bake-clamp leg, FT-9's IL scan and instrument calibration, FT-11's integrated-path leg, FT-12's instrumented short-array leg, FT-21 (exclusivity conservation, with the §4.4 alpha deficit reported as a number), a corrected FT-7 note explaining the 119 → 0 change, and the re-rendered FT-20 with its legend.

---

## 7. The contact sheet

The old sheet's cells `[0,0] Solid` and `[0,1] Ramp.coverage` were unreadable for a specific and fixable reason: each cell was written with `EncodePremultiplied`, which leaves alpha in the PNG, and `SetPixels32` replaces the destination block *including* alpha — so the sheet's opaque dark background was punched out and a white Solid over a transparent background rendered white-on-white in any viewer that draws alpha as white. The last grid position was empty because 17 cells do not fill 3 × 6.

**Re-rendered to the same path, `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\fill-contact-sheet.png`.** 20 cells, 5 × 4, 516 × 414 px, fully opaque. Every cell is composited over an opaque 8-pixel mid-grey checkerboard in linear before the single sRGB encode, so nothing can render invisibly against any viewer background, and where the checker shows *through* a shape that is the veil or a texel's alpha doing its job rather than a hole. Each cell carries its own two-digit index bottom-left in a 3×5 bitmap font, so the legend is unambiguous without counting. Every cell has a 1px frame.

Cells, in order: 01 Solid tinted · 02 Solid **white** (the cell that used to be invisible) · 03 Solid at veil 0.45 · 04–07 Gradient Linear/Radial/Angular/ByEdgeDistance · 08 Ramp by coverage · 09 Ramp by edgeDistance · 10–11 Texture Fitted/Tiled · 12–15 space × fit on a wide rotated node · 16–17 Composite Over/Add · 18 the FC-3.8 worked tree · 19 nested ownership (bag fill + child fill, FC-3.5) · 20 the availability gate falling back (a child asks for `heat`, refuses to bind, and its region is painted by the ancestor with no hole).

Five cases are demonstrated that the old sheet did not show at all: a white Solid, an explicit veil, Ramp by a quantity that actually varies across the interior, FC-3.5 exclusivity in isolation, and FC-4.4's fallback as a picture rather than as a diagnostic string.

**I looked at it.** All twenty cells render their case. Three cells look wrong at a glance and are not, so the reading is now printed in the report rather than left to be re-derived: cell 08 is flat inside *correctly* (coverage is 1 throughout a silhouette; the ramp is the rim); cells 14 and 15 are identical *correctly* (measured: 0 floats differ — `Fixed` normalises by the canvas half-extents, this canvas is square, and one divisor equals two equal divisors), with 12 vs 13 being the pair where `fit` actually bites; and cell 18 is nearly all one colour because that is FC-3.3's ruling made visible, per §1.

---

## 8. What I could NOT verify, and why

- **The pre-T-0106 `ShaperCompiler.cs` / `ShaperProgram.cs` / `ShaperNode.cs` do not exist anywhere on disk.** The whole `Runtime/Shaper` and `Editor/Shaper` tree is untracked in git and there is no copy in the main worktree or in the T-0105 workspace. So "bit-identical to what it produced before" is verified *by argument plus measurement of the argument's premises* (§5.3), not by a literal diff. If the implementer changed anything in those three files beyond the `parentForward` threading and the additive `localBox`/`rootInverse` fields, my method would not see it. The 13/13 field audit and the ops-array bitwise equality are what stands behind the claim.
- **A veil on an unbounded (> 1) coverage cannot be exercised end to end**, because no producer of unbounded coverage exists and `PaintTile` recomputes coverage internally (§5.4). I verified the clamp expression and its placement, not the integrated behaviour.
- **§4.4 (the `Over`-between-exclusive-owners alpha deficit) is measured but unfixed**, so the fix is unverified by definition. I did not implement a summing compositor, so I cannot report what it would cost or whether it introduces problems of its own.
- **§4.5 (the premultiplied `Color32` writer) is measured but unfixed** in the runtime. The contact sheet works around it; any other consumer of `EncodePremultiplied` still gets the non-standard pair. I did not survey for such consumers beyond the audit.
- **The `Fixed`-space canvas-centre limitation (§2) is unfixed**, and I did not test a non-centred grid end to end — I verified that `ShaperFillAnchor` carries no centre and that `BakeAnchor` hardcodes 0, and confirmed the shipped grid happens to satisfy the assumption.
- **Burst.** No Shaper code is `[BurstCompile]`, so FC-5.2's Burst-addressability rationale remains architectural. The IL scan proves the loop is allocation-free, which is a necessary but not sufficient condition; I did not attempt to Burst-compile anything.
- **`ZuiGradient.Evaluate`'s transform semantics.** The contract's own §F10 flags that if any `ZuiGradient` transform were position-dependent rather than parameter-dependent, FC-5.4's LUT bake would be invalid. I did not read `ZuiGradient`'s body. The indirect evidence is good — FT-3's cross-kind identity is bit-exact and the LUT round-trips a flat ramp to a Solid with worst |delta| 0.00e+0 — but that only exercises a constant gradient, so a position-dependent transform would not show there.
- **Performance.** I measured allocation, not time. No throughput number in this document.
- **Anything requiring a UI.** T-0106 builds none, correctly, so the availability gate's diagnostics were verified as strings and flags on the compiled document, never as a greyed control.

---

## 9. Bottom line

The fill stage is substantially correct and considerably better built than its audit was able to demonstrate. The architecture holds up under attack: the anchor is genuinely rotation-invariant and beats the rejected alternative by 2.67×; colour is exactly right end to end at zero codes over all 256 grey levels; the tile loop is provably allocation-free; determinism survives a domain reload; the integrated path is bit-identical to the direct path across all 48 configurations; and the availability gate produces real reasons on a real path rather than a mock.

Against that: **three shipped defects that a green 20/20 audit did not see** — a colour fringe from multiplicative exclusivity, a NaN veil that poisons every output float, and an undeclared sheet read — all three now fixed and re-measured; **two tests that structurally could not fail**, both instruments replaced after calibrating and rejecting two candidate replacements of my own; **one contract contradiction** (`Over` versus exclusivity) measured at 244 samples and up to 25% transparency, escalated rather than patched; **one encoder that emits a representation no consumer can read back**, worked around in the deliverable and left for a ruling; and **one specification table** whose three `G_body` rows are wrong for a reason internal to the table itself.

The single most useful thing I can say to whoever reads this next: the 119 samples were not a smell about FC-3.3. They were a bug in FC-3.5's implementation, and FC-3.3's answer — that in FC-3.8's tree the bag paints nothing at all — is both correct and, now, exactly what the code does.

---

# T-0106 fix pass — closing the two escalated defects (§4.4 and §4.5)

Written 2026-08-31 by the fix-and-re-verify pass, after the verification above and appended to it rather than edited into it. Same worktree, `D:\UNITY\Laubrary Dev - Shaper`; `Application.dataPath` asserted as `D:/UNITY/Laubrary Dev - Shaper/Assets` before any number below was believed, and `EditorUtility.scriptCompilationFailed` confirmed `False` with a reflection probe for symbols added in this pass (`ShaperFillResolver.Encode`, `ShaperFillBuffers.subtree`, `ShaperFillAudit.FT22_EncodeIsReadableBack`, `ShaperFillAudit.StampSeam`) before any measurement was taken. Nothing is committed. Only `Runtime/Shaper/ShaperFillResolver.cs`, `Editor/Shaper/ShaperFillAudit.cs` and the three workspace documents were touched.

**Headline.** Both escalated items are closed. §4.4's transparent seam went **244/16384 samples → 0/16384** on the escalation's own fixture and instrument, with the sibling half of the rule provably unmoved. §4.5's unreadable colour write is gone: the premultiplied encoder was **deleted** rather than repaired, straight alpha is now the single `Color32` write, and its round trip is exact at **0 codes over all 256 levels at every alpha tested** where the old one was wrong by up to **67 codes**. `FILL-CONTRACT.md` carries three new amendment clauses recording both rulings and the superseded readings. All 21 asserting FT legs pass, `ShaperFieldAudit` is still 13/13, and the contact sheet is 21 cells with a new cell that would show the seam if it came back.

---

## A. Defect §4.4 — `Over` between exclusive owners. FIXED, re-measured

### A.1 What was changed

`ShaperFillResolver.PaintTile` step 5/6 was restructured. Previously every owner composited straight into one destination with `Over` (or `Add`), in forward paint order. Now:

- Each owner gets its own **subtree accumulator** — `ShaperFillBuffers.subtree`, 4 floats per sample per owner, host-allocated once on the same BC-3.7f terms as every other array here.
- An owner's own contribution is **summed** into its own accumulator: `rgb += albedo·ce` always, `alpha += ce` only when the composite is `Over`. This is the partition arithmetic — step 4 already made the owner's region disjoint from every descendant's, so the two are two parts of ONE pixel's area and their alphas add.
- A **finished** subtree is then folded into its parent's accumulator with `target = target Over source`, or into `dst` for the root. That is where FC-2.6a's `Over` belongs and it is the only place it now appears.
- The loop runs in **reverse paint order**. That is an evaluation order, not a reordering of the fold: a child's subtree must be finished before its parent needs it (the same reason step 3 already iterates backwards), and reaching the children in decreasing index places the highest-index member first, so it stays on top — which is fold order exactly, by the associativity of `Over`. FC-3.4 is preserved, and FT-7b still measures it.
- FC-3.5b, the mixed Over/Add-within-one-pixel case the escalation asked to have defined rather than left to fall out, is now stated in the contract and in the code: **alpha is the sum of the `Over` owners' claims alone; RGB is the claim-weighted sum of every owner's albedo.** An `Add` owner contributes light and never opacity, against the destination, a sibling, or an ancestor sharing its pixel.
- Accumulated alpha is clamped at 1 at use. It can legitimately overshoot, because `descendantClaim` is a MAX over descendants rather than a sum (siblings may overlap, so a sum would double-count), so two spatially disjoint descendants at fractional coverage can leave their ancestor more claim than the pixel has left. RGB is deliberately not rescaled with it — an additive result carries more light than its alpha on purpose.

### A.2 Before and after, one instrument

The "before" number is not quoted from §3(iii); it was **re-measured in this pass**, by reconstructing the previous build's forward-`Over` accumulation from the very same `paint[]` array the shipped build produced on the same run, so the two columns come from one execution and are directly comparable.

| | samples with composited alpha below the shape's own coverage | total deficit | worst |
|---|---|---|---|
| BEFORE (`Over` between exclusive owners) | **244 / 16384** | 29.288 | 0.2484 |
| AFTER (FC-3.5a claim-weighted sum) | **0 / 16384** | 0.000 | 0.0000 |

That reproduces §3(iii)'s three numbers exactly, which is the check that the reconstruction is faithful rather than merely favourable.

### A.3 The exclusivity fix from §4.1 was not weakened — both its results re-measured

- `G_body` on the FC-3.8 worked tree: **0 painted samples**, unchanged (FT-7's probe table is row-for-row identical, and FT-7 passes).
- Chain conservation on the three-deep concentric fixture: `sum(paint over the chain) != rootCoverage` at **0/16384 samples, worst 0.00000** — FT-21 leg (a), unchanged.
- The fixture is not vacuous: **244** samples carry fractional coverage, which is the same 244 the deficit was measured over.

### A.4 The sibling half is provably still `Over` — a new leg, because a sum here would have been an invisible regression

The obvious wrong fix is to sum everything, which would silently reverse FC-3.4. FT-21 now carries leg **(c)**: two overlapping sibling owners at veil 0.5 each, alpha where both cover = **0.75000** (expected 0.75000 — `Over`; a sum would read 1.00000). PASS.

### A.5 What else moved, measured rather than asserted

Six fixtures, comparing the shipped destination against a faithful reconstruction of the previous algorithm, over all 65536 floats of a 128×128 RGBA destination:

| fixture | owners | floats differing | worst \|delta\| | channel |
|---|---|---|---|---|
| single Solid owner | 1 | **0 / 65536** | 0 | — |
| single `Add` owner, veil 0.7 | 1 | **0 / 65536** | 0 | — |
| two overlapping `Over` siblings | 3 | **0 / 65536** | 0 | — |
| siblings, upper is `Add` (sheet cell 17) | 3 | 1012 / 65536 | 2.980e−8 | colour only |
| nested bag + child (sheet cell 19) | 2 | 432 / 65536 | 2.451e−1 | **alpha** |
| three-deep chain (FT-21 fixture) | 3 | 1104 / 65536 | 2.484e−1 | **alpha** |

Read it as three groups. Single-owner and sibling-only documents are **bit-identical** — the change cannot reach them. The `Add`-sibling row differs by 3e−8 on colour and nothing on alpha: that is float summation order, because an additive sibling's contribution is now added in the other order, and one ULP at magnitude 1 is exactly what that costs. Only the two nested rows change materially, only on alpha, and only at the internal boundary — which is the defect and nothing else.

### A.6 The fix is not free, and the cost is stated

One extra host-allocated buffer, `ownerCapacity · sampleCapacity · 4` floats. Nothing is allocated inside a paint call: an IL opcode scan of `PaintTile` reports **0 `newobj`, 0 `newarr`, 0 `box` over 1304 bytes of IL** (a byte-frequency scan is an upper bound, so a zero is conclusive). `ClearDestination` now also clears the accumulators, once per tile, never per sample.

---

## B. Defect §4.5 — the unreadable `Color32` write. FIXED by deletion, re-measured

### B.1 What was changed, and which of the two options was taken

`EncodePremultiplied` was **deleted**. `EncodeStraight` was renamed `Encode` and is now the single `Color32` write — FC-2.3's one encode boundary, straight alpha, un-premultiplied in linear and encoded once.

The escalation offered two outs: repair the premultiplied encoder's order of operations, or delete it if the additive case does not actually need it. **Delete was taken, and the reason is that repairing it does not rescue the case it existed for.** The justification for premultiplication is sound in general — only premultiplied can express an additive glow over nothing (α = 0, RGB > 0). It is not sound about any correct 8-bit *encoding* of that sample: the conventional premultiplied-sRGB byte is `encode(rgb) · α`, and at α = 0 that is 0, which deletes the glow exactly as thoroughly as straight alpha does. So repairing the order of operations would have produced a second encoder that is standards-conformant, unreadable-by-nobody, and still unable to do the one job it was kept for. It had no caller anywhere in the project. Deleting it trades an undocumented misencoding for a documented limitation, which is the better trade.

### B.2 Before and after, one instrument, linear-white fill

| α | OLD `EncodePremultiplied` = `encode(rgb·α)` | NEW `Encode` (straight) | conventional premultiplied-sRGB `encode(rgb)·α` |
|---|---|---|---|
| 1.00 | 255 | 255 | 255 |
| 0.75 | 225 | **255** | 191 |
| 0.50 | 188 | **255** | 128 |
| 0.25 | 137 | **255** | 64 |
| 0.10 | 89 | **255** | 26 |
| 0.00 | 0 | 0 | 0 |

The old column matches neither of the other two at any alpha strictly between the endpoints, which is the whole defect. Over **all 256 codes at α = 0.5**, read back by a straight-alpha consumer: the old encoder was wrong by up to **67 codes**; the new one by **0**.

### B.3 The new leg that would have caught it — FT-22

Three legs, all PASS:

- **(a)** straight round trip, all 256 codes, at α ∈ {1.00, 0.75, 0.50, 0.25, 0.10}: **0/256 codes wrong at every alpha, worst |delta| 0 codes**. The old encoder fails this at every alpha but 1.00.
- **(b)** the alpha byte itself round-trips: **0/256 wrong**.
- **(c)** additive-over-transparent is still representable. Through the full integrated path: the float destination holds R 1.0000 with alpha 0.0000, finite — the representation that actually carries an additive result. Into bytes through an opaque backdrop, the backdrop goes **111 → 255**, i.e. the glow brightens it, which is how an additive fill reads in an 8-bit image at all. And `Encode`'s `α ≤ 1e-6` branch does not divide, so it keeps the colour byte and honestly reports α = 0; a straight-alpha blender will discard that, which is the stated limitation rather than a third answer.

### B.4 What was NOT corrupted stayed uncorrupted

§4.5's own finding — that the ordinary `Over` path is exactly right in linear and a mid-grey survives the whole round trip at 0 codes over all 256 levels — still holds: FT-2 (sRGB round trip) and FT-1/FT-3 (bit-exact cross-kind identity) pass unchanged, and FT-22 (a) is the stronger version of the same claim taken at five alphas rather than at opacity only.

---

## C. What was amended in `FILL-CONTRACT.md`

Three clauses added in place, each marked `[T-0106 fix pass, 2026-08-31]`, each recording the superseded reading explicitly rather than quietly replacing it, and each carrying the measurement that forced it.

- **FC-2.6a-AMENDED** — the SCOPE correction. The formula is unchanged and stays correct for what it governs; what was wrong was reading it as the rule for compositing every fill against the destination, including two fills that own disjoint parts of one silhouette. `Over` describes how a finished layer meets what is beneath it, never how one silhouette's own exclusive owners meet each other.
- **FC-3.5a** — the accumulation rule FC-3.5 always implied and never said: exclusive owners accumulate by claim-weighted sum; sibling claims overlap and still composite `Over` in fold order. Carries the cost (the per-owner accumulator) and the `descendantClaim`-is-a-MAX limitation.
- **FC-3.5b** — the mixed `Over`/`Add` case, defined.
- **FC-2.3a** — the `Color32` write is straight alpha and there is exactly one of it; records the deleted encoder, its measured misencoding table, why deletion beat repair, and where the additive case lives instead.

FC-3.8's table is still wrong in the way §1 describes. **I did not fix it**, because §1's recommendation is to re-geometry the worked example so the Intersect member is not the last restrictor, which is a rewrite of the example rather than an amendment, and it is not one of the two items this pass was scoped to close.

---

## D. Re-verification of everything, not just the two changes

- **`ShaperFillAudit`, full run.** **21 legs asserting, 21 RESULT: PASS, 0 FAIL** — FT-1 through FT-19, FT-21 (now three legs) and the new FT-22, plus FT-20 which renders the contact sheet and correctly refuses to report itself as a pass. No pre-existing test regressed; FT-7b, FT-17 (Add alpha 0/16384) and FT-18 (Over vs Add height, 0/16384 mismatched, worst 0.00e+0) all still pass, which are the three the fix could most plausibly have broken. `FILL-AUDIT.txt` regenerated from that run.
- **`ShaperFieldAudit`.** **13/13 PASS, 0 FAIL**, re-run leg by leg in this pass (the pipeline's reply timeout is a fixed 30 s and several legs run longer, so each leg was invoked separately and the gate was the report count reaching 13, not the CLI returning).
- **Contact sheet.** Re-rendered to `fill-contact-sheet.png`, **21 cells, 7 × 3, 720 × 312 px**, fully opaque, same mid-grey checkerboard, every cell still carrying its two-digit index. 7 columns because 21 = 7 × 3 exactly — the grid has no empty tail position, which was one of the things wrong with the first sheet. All twenty original cells are kept and unchanged in content.

  **Cell 21 is new: the internal fill boundary, with the seam drawn rather than tabulated.** A bag fill and a nested child fill partition one silhouette, and the child is a long, thin, rotated ellipse precisely to maximise the length of internal boundary in one cell. Every sample whose composited alpha still falls below the shape's own coverage is stamped **magenta**, at the same 1e-3 threshold FT-21 leg (b) uses, so the picture and the table cannot disagree. **Marks in this render: 0.** Before the fix a magenta line would have traced the whole bar.

  **Looked at, by eye.** All 21 cells render their case and are legible. **What changed, and why:** cells 01–15 and 16 are unaffected — they are single-owner or sibling-only and A.5 measures them bit-identical. Cell 17 changed by at most 3e−8, invisible, and it is float summation order rather than a colour shift. Cells 18, 19 and 20 changed only along their internal fill boundaries, only in alpha, and only in the direction of the fix — the checker no longer shows through a one-pixel ring where a child fill meets its parent's. **Nothing shifted for the wrong reason:** defect 4's change cannot reach the sheet at all, because a cell is composited over an opaque backdrop before a single `ShaperSrgb.EncodeToByte` call and never goes through `Encode`, and that helper is byte-for-byte the same code it was.

---

## E. What this pass could NOT verify

- **The "before" numbers are a faithful reconstruction, not a re-run of the old binary.** The previous `PaintTile` and `EncodePremultiplied` no longer exist and the tree is untracked in git, so "before" was rebuilt in-probe from the shipped run's own `paint[]`, `ownCoverage[]` and per-owner fill evaluations. It reproduces §3(iii)'s 244 / 29.288 / 0.2484 and §4.5's 225 / 188 / 137 / 89 exactly, which is strong evidence the reconstruction is faithful — but it is a reconstruction.
- **No consumer of `Encode` exists to test against.** The claim that straight alpha is what every consumer wants rests on FC-2.3's wording, on PNG having no premultiplied mode, and on Unity's sprite blend being `SrcAlpha OneMinusSrcAlpha`. T-0106 ships no consumer, so this is verified by argument and by round trip, not by rendering through a real material.
- **The alpha-overshoot clamp is verified as an expression, not exercised end to end.** No shipped fixture produces two spatially disjoint fill-owning descendants at fractional coverage under one ancestor, so the `> 1` branch was reasoned about and written, and I did not construct a fixture that trips it.
- **Performance.** As with the verification above, allocation was measured and time was not. No throughput number here either, and `PaintTile` now clears and writes a per-owner accumulator, which is real work that nobody has timed.
- **FC-3.8's table is still wrong** (§1), and the `Fixed`-space canvas-centre limitation (§2) is still unfixed. Both were out of this pass's scope and both remain open.
- **Everything in §8 above that was not one of the two escalated items remains as the verifier left it** — the pre-T-0106 shape files still do not exist on disk, unbounded coverage still cannot be exercised, `ZuiGradient.Evaluate`'s transform semantics are still unread, and no Shaper code is Burst-compiled.
