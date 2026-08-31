# T-0109 — SECOND fix pass, against `VERIFICATION-2.md`

**Fixer:** second pass (general-purpose), `claude-opus-5`. **Subject:** `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\{Runtime,Editor}\Shaper\`, branch `feat/shaper`, uncommitted. **Nothing was committed.** No `git checkout` / `restore` / `stash` / `reset` was run. No scene or asset was saved from a probe. `D:\UNITY\Laubrary Dev\Assets\` (the MAIN copy) was not written to — `git status --porcelain -- Assets` there still returns only the pre-existing `?? Assets/Temp.meta` plus the unrelated `?? Assets/ChunksMock/` and `?? Assets/ChunksMock.meta`, which belong to somebody else's Chunks task and were **not deleted**.

## Headline

| # | Defect | Before | After |
|---|---|---|---|
| **N1** | `ClipSlab` parallel branch inclusive at both ends | 530/552 rays wrong, 40/42 combinations, worst **`Depth` 0.000 vs a true 399.609** | **1/552 rays differ, worst `Depth` error 0.001 px** — and that one is a 0.001 px air gap at a `Stepped` riser, below the march's own 0.019 px feature resolution |
| **N2** | crossings emitted in ζ order, not ray order | 242 truncated cases: **200 non-prefix, 144 mis-stamped, 11 over-reporting solid, worst +83.138 px** | **0 non-prefix, 0 mis-stamped, 0 over-reporting, worst 0.000** |
| **N3** | `Member`'s closed set manufactures zero-width pairs | H6 fixture: **42 extra pairs, all width 0.000, all at `z == baseZ`, all counted "verified real"** | **0 degenerate, 0 verified, 0 spurious** — and H6 can no longer absorb the class |
| **N4** | per-step contained prism on raw `InverseAtE` | **13 024 violations / 14 430 139 checks, worst 3.3379e-6** | **0 violations, worst 0.0000e+0** on the same sweep |
| **N5** | `linearEGrad` not an upper bound under the clamp | **385 / 1650 configurations, worst ratio 3.6235×** | **0 / 1650, worst ratio 1.0000** |
| **N6** | `−1e-6` absolute epsilons in the two `Stepped` inverses | `ProfileInverse` **\|Δt\| 3.333e-1**, `BevelInverseU` **5.000e-1** | **0.000e+0 and 0.000e+0**, plus four latent round-trip failures fixed that no ζ epsilon could reach |
| **N7** | H11 statement 2 proves the seed, not the sum | `heightDelta ≡ 0` in its document | 2a + **2b**: 656/1920 samples with BOTH terms non-zero, max height rises by exactly **3.0000** |

**Audit H1–H11: all eleven `VERDICT ok`, 0 lines containing `FAIL`.** **Four earlier waves: `ShaperFieldAudit` 13 PASS / 0 FAIL, `ShaperFillAudit` 85 / 0, `ShaperBorderAudit` 79 / 0, `ShaperLightAudit` 150 / 0** — the same counts the second verifier measured before this pass. Both PNGs re-rendered; the tilted frame still asserts **general 126 000 rays / straight-down 0** and still shows **10 049 wall pixels**. Hot-path allocation **0 bytes / 0 gen-0** (H8 over 1 197 824 samples, covering `ShaperResolve.Query` on both branches), corroborated by the border wave's own IL decoder (BT-14: 50 methods reachable from `PaintTile` including `ShaperHeight`, **0 heap allocations**).

## Instruments

The second verifier's own harness, reused unchanged and extended: `…\T-0109\verify\` compiles the real `Runtime/Shaper` source against a `UnityEngine` stub into a .NET 8 console exe. **Every "before" number below is that harness run against the unmodified source, and every "after" number is the same probe, same fixture, after the edit** — the probes were not rewritten to be kinder. I added one file, `V2p.cs` (selector `FIX`), for the four measurements the verifier's probes could not make because they point at the pre-fix call sites. Unity-side work went through the `unity` CLI's `eval_file` at `--project-path "D:/UNITY/Laubrary Dev - Shaper"`, with `Application.dataPath` asserted under `Laubrary Dev - Shaper` before every batch; port 7800 was never targeted.

**Compile verification, per the brief's warning.** `EditorUtility.scriptCompilationFailed = False` **and** a reflection probe for a newly-added symbol — `ShaperHeight.InverseAtEUpperBound` came back `PRESENT Single InverseAtEUpperBound(ShaperHeightOp ByRef, Single, Single)`, which is what proves the assembly actually reloaded rather than the console merely looking clean. Every type in every probe is fully qualified, because `eval_file` wraps the script in a method body and `using` aliases silently fail.

---

## N1 — `ClipSlab`'s parallel branch was inclusive at both ends

**The defect, confirmed by my own measurement.** Adjacent Z slabs share their boundary as the *same expression* (`op.baseZ + op.body * breakpoints[k+1]`), so a ray with `|dz| < 1e-9` at exactly that z satisfied `o >= lo && o <= hi` for both. Both marched the whole clipped range, both emitted the same flips, `SortByRayT` interleaved them, and step 5's parity stamp toggled on each — so `Depth` summed two zero-length spans.

**The fix.** `ClipSlab` takes a `bool hiInclusive = true`; the parallel branch is `o >= lo && (hiInclusive ? o <= hi : o < hi)`. The Z-slab call site passes `k == lastSlab`, where `lastSlab` is the largest `k` with `breakpoints[k+1] > breakpoints[k]`, computed once before the loop. So every interior boundary is half-open and a parallel ray belongs to exactly one slab, while the **outer** ends stay closed — the brief's warning, and it is a real one: the verifier's own fixture has a correct row at `z = 40.0` (exactly `base + body·supG`), which a naïvely half-open rule would have dropped out of every slab. The two XY support-box clips keep the default `true`; they are outer extents with no neighbour to share with.

**Before / after** (`probe3.dll B`, the verifier's exact fixture: 400×400 plate, `Flat` + `Stepped` bevel amount 0.5 steps 4, depth 40, ray `(−400,0,z)` direction `(1,0,0)`):

| ray z | before: got / `Depth` | after: got / `Depth` | true |
|---|---|---|---|
| 0.0000 | 2 / 400.000 | 2 / 400.000 | 2 / 400.000 |
| 10.0000 | 2 / 400.000 | 2 / 400.000 | 2 / 400.000 |
| **20.0000** | **4 / 0.000** | **2 / 350.000** | 2 / 350.000 |
| **30.0000** | **4 / 0.000** | **2 / 300.000** | 2 / 300.000 |
| 40.0000 | 2 / 250.000 | 2 / 250.000 | 2 / 250.000 |
| 20.3700 (control) | 2 / 300.000 | 2 / 300.000 | 2 / 300.000 |

`rows wrong: 2 → 0`.

**Breadth** (`probe3.dll LAST`, 42 combinations, one horizontal ray at every interior breakpoint):

```
before:  combinations wrong = 40,  wrong rays = 530 / 552,  worst |Depth error| = 399.609 px
after :  combinations wrong =  1,  wrong rays =   1 / 552,  worst |Depth error| =   0.001 px
```

**The one residual ray, examined rather than waved past** (`probe3.dll FIX`, `N1-RESIDUAL`). `Stepped+Rounded`, breakpoint 8, ζ = 0.5 exactly, z = 20. The march emits one span `[299.9830, 500.0145]`; the reference scan finds three: `[299.9829, 299.9870]` (width 4.089e-3 px), `[299.9880, 500.0120]`, `[500.0130, 500.0171]` (width 4.089e-3 px). **The `Depth`s agree to the printed digit — 200.032 vs 200.032.** The difference is two air gaps of **0.001 px** at a `Stepped` riser, which the march steps over. That is inside its declared accuracy: `SurfaceResolution` is 0.02 canvas px, and the verifier's own thin-feature sweep found the march resolves 0.019 px slots and misses 0.01 px ones. This is the resolution floor, not the N1 defect, and I did not tune anything to make the row go green.

**I also re-ran the whole 552-ray sweep against a reference carrying the amended HS-1.1** (`N1-AMENDED-REF`), because N3 changes the convention and it would be dishonest to grade the new code only against the old truth: identical result, 1 wrong ray, worst 0.001 px. The N1 outcome does not depend on which convention the reference uses.

---

## N2 — the crossing list was emitted in ζ order, not ray order

**The defect.** `QueryGeneral` iterated slabs by ζ. For a descending ray the lowest-ζ slab is met at the *largest* ray parameter, so emission was roughly decreasing in `rayT`; `Emit` drops on overflow, so the retained list was a **suffix** of the ray, while step 5 stamped parity onto it as if it were a prefix starting from `insideAtStart`.

**The fix.** The slab loop runs in ray order: `for (int kk = 0; kk + 1 < bp; kk++) { int k = descending ? (bp - 2 - kk) : kk; … }` with `descending = dz < 0f`. That is enough on its own, because everything else was already monotone: step 3 emits at `s0` (the smallest parameter), each slab marches from `a0` upward with `BisectSurface` returning inside `(s, sNext]`, and step 4b emits at `s1` (the largest). So the whole layer now emits monotonically increasing in `rayT`, truncation keeps a genuine prefix, and the parity stamped forward from `insideAtStart` is correct for every element kept. `SortByRayT(crossings, count, layerStart)` is left in place as a cheap no-op safety net. I did not add a sort-then-truncate path: reordering the traversal fixes the *set* that is retained, which a post-hoc sort cannot.

**Before / after** (`probe3.dll TRUNC`, the verifier's fixture verbatim: 400×80 plate minus two 12 px slots, `Stepped` 4/8/16 steps, depth 300, rays 20°–80°, buffer capped 1 … count−1):

```
                                                              before      after
truncated cases exercised                                        242         237
retained set is NOT a ray-order prefix                           200           0
cases with at least one MIS-STAMPED parity flag                  144           0
   worst flags wrong in one case                                   6           0
cases whose Depth OVER-reports the true solid length              11           0
   worst over-report                                        83.138 px    0.000 px
```

*(242 → 237 truncated cases: the same rays now overflow at slightly different caps because the emission order changed. It is the same fixture and the same sweep.)*

**The single-ray illustration** (`probe3.dll C`) that proved the retention was not a prefix — `Depth` **rising** as the buffer *shrank*, 119.596 at cap 6 and 202.840 at cap 5 — is now monotone non-increasing throughout: 280.447 / 245.605 / 245.605 / 242.492 / 242.492 / 42.765 / 42.765 / 0.000 for caps 8 down to 1, every parity string a legal alternating sequence, and **0 caps over-reporting**.

---

## N3 — the membership convention manufactured zero-measure spans, and H6 laundered them

Two fixes, as the brief requires: the convention, and the check that absorbed it.

**The convention.** `Member` read HS-1.1's closed set literally: `above >= 0 && above <= body·G(t)`. Where `G(t) = 0` that degenerates to the single plane `z = base` — harmless for a point, but `G` is zero over a **band**: a `Stepped` extrusion has `E = 0` for the whole ring `t < 1/n` around the silhouette, and a `Stepped` bevel does the same at the rim. So the layer carried a two-dimensional membrane of zero thickness, and every tilted ray whose base-plane exit landed on it emitted an entering flip from the slab march and an exiting flip from step 4b at the same `rayT`.

The rule is now `G(t) > 0`, in `Member` **and** in `QueryStraightDown` (which without it emits `sTop == sBottom`, the same zero-width pair through the closed form — the verifier did not report that half, and the two branches must agree or BC-2.3 breaks). This is not a tolerance and not a new idea: `Query` already skips a layer with `op.body <= 0` citing HS-2.2 — *"a zero-thickness layer publishes height 0 and no wall … there is no solid to cross"* — and this is that sentence applied pointwise. HS-1.1 is amended in `HEIGHT-SPEC.md` Part 12 and in `ShaperHeight.cs`'s own statement of it.

**Before / after** (`probe3.dll DRILL`, the verifier's fixture: 400×400 plate minus a 20 px slot at x = +40, `Stepped` 5 steps + `Stepped` bevel, depth 300, ray from `(−400,0,100)` at 12° down):

```
before:  got=4 true=2   emitted 245.3618I/Wall 398.7128O/Wall 480.9734I/Cap 480.9734O/Cap
                        (the last two at the SAME rayT, width exactly 0, z = 0.00000, G = 0.00000)
after :  got=2 true=2   emitted 245.3618I/Wall 398.7128O/Wall
                        truth   245.3617 398.7128
```

**Breadth.** The verifier's 42-combination tilted sweep reported six rows `true=0 got=2 spurious=2`, all `Stepped`. Re-run (`probe3.dll MARCH`, attack 3, and `FIX` `N3-BREADTH`): **126 rays, 0 wrong, 0 zero-width emitted pairs, worst gap 3.052e-5 px**.

**The audit half.** H6 counted an unexplained extra pair as `extraVerified` if `TruthInside` said the *midpoint* was solid. For a zero-width pair the midpoint IS the crossing, and `TruthInside` was a second implementation of the same closed convention, so the answer was unconditionally `true`. All 42 phantom pairs were certified under a printed line calling them *"slivers finer than the scan's 0.003 px spacing"*. They were exactly zero-width; that line was false.

Three changes, and the third is the one that matters most:

1. `TruthInside` carries the amended HS-1.1 (`G > 0`). It is still a second, independent implementation — it does not call `ShaperResolve.Member`.
2. A degenerate pair is counted on a new `extraDegenerate` and **fails** the check: `omitOk` now requires `extraDegenerate == 0`, and the report prints the count with `T-0109 FIX N3: MUST be 0`.
3. A pair only reaches the predicate at all when its midpoint is **strictly interior in float** (`hi > lo && mid > lo && mid < hi`). This kills the tautology *independently of the convention* — even if HS-1.1 were later re-opened, the arm can no longer certify a span it never sampled the inside of. The "verified real" line now prints the narrowest width it actually certified, so a future reader can see whether it is a sliver or nothing.

**Before / after** (`probe3.dll H6`, H6's own fixture replicated in the harness; and the audit itself in the editor):

```
harness, before:  rays=504 omissions=0  extra VERIFIED=42 SPURIOUS=0   of the 'verified': ZERO-WIDTH 42, on the base plane 42
harness, after :  rays=504 omissions=0  extra VERIFIED=0  SPURIOUS=0   ZERO-WIDTH 0, on the base plane 0

editor H6, after: rays 504, omissions 0, worst gap 1.5259E-005 px,
                  extra VERIFIED 0, extra SPURIOUS 0, extra DEGENERATE 0, VERDICT ok
```

**The safety check that matters.** `omissions = 0` and `worst gap = 1.5259e-5 px` were measured **against a truth scan still carrying the OLD closed convention**. So the convention change removed 42 zero-width pairs and lost **nothing** the old reading contained.

---

## N4 — the per-step contained prism now goes through the guarded path

**The defect.** F1 hardened `InverseUpperBound` with verify-and-nudge. The march calls that once per slab (`tauMaxSlab`), but the **per-step** adaptive bracket — the machinery F1 exists for — called `ShaperHeight.InverseAtE` raw, which is short by a few float roundings and therefore is not an upper bound at all.

**The fix.** A new public `ShaperHeight.InverseAtEUpperBound(op, zeta, e)` carries `InverseUpperBound`'s verify-and-nudge generalised to any pinned `E`; `InverseUpperBound` is now a two-line wrapper that picks `e` and delegates, so there is exactly one implementation of the guarantee. `ShaperResolve.cs`'s per-step `tauMax` calls it.

**Before / after** (`probe3.dll FIX` `N4-AFTER`: 42 combinations × 9 angles × 4 amounts × 400 ζ × a t grid that **includes τ itself**, because the verifier's worst violation sits at `t == τ` exactly and a uniform grid misses it — my first attempt did, and reported a false 0):

| function used for the contained prism | checks | violations | worst shortfall in ζ |
|---|---|---|---|
| `InverseAtE` — what it used to call | 14 430 139 | **13 024** | **3.3379e-6** (`Flat+Cove`, a = 0.05, ζ = 0.995) |
| `InverseAtEUpperBound` — what it calls now | 14 417 115 | **0** | **0.0000e+0** |

**Cost.** The guarded form early-outs without bisecting whenever the closed form already verifies, which is **99.91%** of calls (13 024 of 14 430 139 needed the nudge). No allocation: H8 still measures 0 bytes / 0 gen-0 over 1 197 824 samples with `ShaperResolve.Query` in the covered paths.

I agree with the verifier that no behavioural failure was ever produced from this, and I fixed it anyway for the reason the brief gives: it is the same unsound-direction class that produced F1's 278-million-violation defect, and the FIX-REPORT's headline row and Part 11's "every step is either a proof" did not describe the function the march called.

---

## N5 — `linearEGrad` is now a real upper bound on `|∇E|`

**The defect, and the compiler comment that was false.** `E = 1 + 0.6(cosθ·nx + sinθ·ny)` with `nx` and `ny` clamped to `[−1,1]` **independently**. The compiler baked the exact Jacobian of the *unclamped* map and commented that "the clamp inside `LocalNormalised` can only reduce the variation". It cannot: `∇E` has four regimes, and where the two axis terms partially cancel, a surviving single term is **larger** than their sum.

**The fix.** The bake is the max over the regimes, which is the true supremum over the whole plane:

```
a = 0.6·cosθ·∇nx        (vanishes where nx is clamped)
b = 0.6·sinθ·∇ny        (vanishes where ny is clamped)
linearEGrad = max( |a + b|,  |a|,  |b| )        ( |0| for both-clamped is never the max )
```

**Before / after** (`probe3.dll CLAMP`, the verifier's 1650-configuration fixture: 11 angles × 5 rotations × 5 skews × 3 scales × 2 aspects):

```
before:  configurations where the TRUE sup|grad E| EXCEEDS the baked value: 385 / 1650, worst ratio 3.6235
after :                                                                      0 / 1650, worst ratio 1.0000
```

`probe3.dll GRAD` (18 rotated/skewed/scaled configurations) still shows the bound is exact-or-conservative for the unclamped map, ratios ≤ 1.0000. `probe3.dll LIN` (5 184 targeted `Linear` rays on skewed/scaled slotted plates, 4 angles × 3 rotations × 3 skews × 6 bevels × 24 tilts) still finds **0 rays with an unmatched true crossing, worst gap 0.0000e+0** — as before the fix. So, exactly as the verifier judged: the soundness hole was real and its consequence was never observable.

**What the correct bound costs, since the brief asks** (`probe3.dll FIX` `N5-COST`, same 1650 configurations):

```
configurations where the new bound EQUALS the old (no cost at all): 1216 / 1650
mean new/old = 1.0858        worst new/old = 3.6235
worst at angle=-135 rot=17 skew=60 sx=2.3 halfH=40:  old 3.48485E-003 -> new 1.26274E-002
```

Three quarters of configurations pay nothing, because the max IS the old value whenever the sum dominates. A wider `E` window makes the per-step bracket more conservative, i.e. shorter steps that are still provable — a speed cost on `Linear` only, never a correctness one. It is worth paying for the reason the brief states: a bound that is not a bound is what T-0105 was built to eliminate.

---

## N6 — the absolute epsilons are gone, and took a second defect with them

**The defect.** `ProfileInverse(Stepped)` computed `k = CeilToInt(zeta*(n−1) − 1e-6)` and `BevelInverseU(Stepped)` computed `k = CeilToInt(zeta*m − 1 − 1e-6)`. An absolute epsilon on a scale-free quantity: `ζ(n−1)` ranges over `[0, n−1]`, so the effective ζ tolerance was `1e-6/(n−1)` — about 17 ulps at n = 2 and about 1 ulp at n = 32, a different rule at every step count.

**Why a smaller epsilon is not the fix.** The epsilon existed for a genuine reason — ζ read back off a tread often computes one float rounding *above* the integer (ζ = 1/3 at n = 4 gives `ζ·3 = 1.0000001`) and must not ceil a whole step up. Any epsilon large enough to catch that is also large enough to swallow a ζ that is genuinely one ulp above the tread. The two cases are one ulp apart; no magnitude separates them.

**The fix is to ask the forward function instead.** `SteppedProfileInverseExact` / `SteppedBevelInverseExact`: ceil the step index with no epsilon, then

- walk the index **DOWN** while the step below already reaches ζ, judged by `SteppedE`/`SteppedB`, which are the forward `Profile`/`Bevel` Stepped branches written out bit-for-bit so the verify asks the same expression;
- nudge the returned `t` **UP by ulps** (a relative step of one ulp, at most 8 of them) if the candidate falls short — never by a tread, which is `1/n ≥ 1/32` and seven orders of magnitude wider, so a riser can never be skipped.

Both loops are bounded, allocate nothing, and in the common case run zero times.

**Before / after** (`probe3.dll D`, probed at ζ one ulp above a tread, which neither of H4's grids lands on):

| function | before `\|Δt\|` | after `\|Δt\|` | before reach shortfall | after |
|---|---|---|---|---|
| `ProfileInverse(Stepped)` | **3.333e-1** (n = 3), 2.5e-1 (n = 4), 3.125e-2 (n = 32) | **0.000e+0** | 1.788e-7 | **0.000e+0** |
| `BevelInverseU(Stepped)` | **5.000e-1** (m = 2) | **0.000e+0** | 1.788e-7 | **0.000e+0** |

`InverseLowerBound` containment at tread-adjacent ζ: **46 200 checks, 0 violations**, before and after.

**A second defect the rewrite exposed, which no ζ epsilon could ever have reached.** The candidate `t = k/(float)n` is not always a float that `⌊t·n⌋` maps back to `k`. Exhaustively over the authored range (`n ∈ [2,32]`, every `k`) there are **exactly four** such indices — `n=22,k=13`; `n=23,k=7`; `n=23,k=14`; `n=29,k=15` — where `fl(fl(k/n)·n) < k`, so the returned `t` sat one ulp below its own riser and `E(t)` came back a **whole tread short**. That is the UNSAFE direction for the contained prism, and it is in `t`, not in ζ, so it was structurally out of reach of the mechanism that was there. The ulp nudge fixes it. The bevel's reciprocal form `k·(1/m)` is exhaustively exact over `m ∈ [2,16]` (0 failures), so its nudge never fires today; it is kept because the clamp is a compiler constant and nobody should have to re-derive the property if it moves.

**The sweep the brief asked for, over `ShaperHeight.cs`, `ShaperHeightCompiler.cs` and `ShaperResolve.cs`.** Ten other absolute constants exist. **None is in N6's class**, and I am recording the judgement rather than changing them for tidiness:

| site | constant | quantity | verdict |
|---|---|---|---|
| `ShaperHeight.Insert` | `1e-7` dedup | ζ, range pinned to `[0, supG ≤ 1.8485]` | **pinned scale**, ≤ ~1.7 ulps. Correct as absolute |
| `ShaperResolve.StraightDownTolerance` | `1e-5` | component of a NORMALISED direction | pinned to 1. Correct, and publicly declared |
| `ShaperResolve` ×5 | `1e-9`, `1e-12` | `\|dz\|`, `dxy`, `len` — normalised direction | pinned to 1. Degenerate-direction guards |
| `ShaperHeightCompiler` | `1e-4` on `span`/`pixelSize` | canvas pixels | dimensional; divide-by-zero guard |
| `ShaperHeightCompiler` | `1e-5` on `localSupportHalfW/H` | local units | dimensional; degenerate-box guard |
| `ShaperResolve.EmitAt` | `zTol = max(1e-3, 1e-3·body)` | canvas pixels, already relative to `body` | dimensionally sound but **still underived** — see below |

**And the claim that overstated is corrected.** `FIX-REPORT.md` §F3's table and `HEIGHT-SPEC.md` Part 11 both said `Stepped + *` went from `|Δt| 5.000e-1` to `0.000e+0`. That was true of H4's grid and false of the code. Part 11's clause is now marked `⚠ ITS CLOSING "|Δt| 0.500 → 0.000" WAS FALSE OF THE CODE — see Part 12`, and Part 12 states what it is now, with the measurement above. The first FIX-REPORT is left as written — it is a historical record of that pass — and this document is the correction, same convention the spec uses.

---

## N7 — H11 statement 2 now proves the sum

**The defect.** H11's document authors no fill, so `heightDelta ≡ 0` and `buf.height[i] == buf.ownHeight[i]` held for a reason that has nothing to do with FC-2.5 being a sum. It would hold identically if the `+=` were a plain assignment, or if the delta term had been deleted.

**The fix.** The old statement survives as **2a**, honestly labelled *"(delta 0 document)"*. A new **2b** re-runs the same document with a `Solid` fill carrying `heightDelta = 3` and requires three things at once: both terms non-zero on a real population, every residual a legal `coverageEff` in `[0,1]` against the authored delta, and the maximum height rising by exactly the delta — the last being the one a broken sum cannot fake.

**Measured** (editor, H11):

```
2a FC-2.5 height_final carries height_shape (delta 0 document): 1920/1920 samples  ok
2b samples with height_shape != 0                 656/1920
   samples with heightDelta   != 0                656/1920
   samples with BOTH terms non-zero               656/1920   <- the sum is genuinely a sum
   (height - height_shape)/delta a legal coverageEff in [0,1]: 1920/1920
   max height_final with delta 3.0: 26.9752   with delta 0: 23.9752   rise 3.0000 (expected 3.0000)
   ok
```

The verifier reached the same numbers independently before this pass, which is the point of quoting them: 2b measures what the verifier had to measure for it.

---

## The rim question — measured and pictured, NOT decided

The brief says do not decide it. I have not. What I can add is that the verifier's framing — *"a visible artefact now, not a latent one"* — is **not what the re-rendered artefacts show**, and the owner should see that before ruling.

**The cut is genuinely live and genuinely exercised.** The contact sheet's rig is a directional key **plus a point lamp** at `(26,−20,30)` range 70, so `pointZ = base + height` is read by the shading, and `ShaperHeight.FillTile`'s hard cut at `d = 0` is in the picture. The numbers are exactly the verifier's: at `edgeSoftness 1.5`, `d = +0.5` has coverage 0.25926 and height 0.00000, `d = −0.5` has coverage 0.74074 and height 24.00000 — the whole `body` across one pixel.

**What it looks like.** `rim-step.png` (regenerate with `_p2_rim3.cs`) is a 10× crop of the same rim of three cells of the re-rendered sheet, at the same magnification:

| panel | cell | what it is |
|---|---|---|
| LEFT | `27 depth 0` | **the control** — no height stage at all. Whatever rim you see here is the ordinary coverage antialias |
| MIDDLE | `01 Flat / None` | the live full-`body` cut |
| RIGHT | `17 Flat + Rounded` | height ramps to 0 through the bevel band — what "ramping it" looks like |

Luminance across the rim (the side the point lamp is on):

```
27 depth 0 (control)    0.291 0.291 0.588 0.855 0.858 0.859 0.862 ...
01 Flat/None (live cut) 0.291 0.291 0.588 0.783 0.783 0.787 0.787 ...
17 Flat+Rounded (ramp)  0.291 0.291 0.588 0.416 0.494 0.539 0.583 0.633 0.673 ...
```

**The last covered pixel reads 0.588 in all three, bit-identical**, and the largest luminance difference between the control and the live cut across sixteen rim pixels is **0.0760** — which is a plateau-brightness difference (the point lamp is 24 px further from a `depth 0` plateau than from a `body 24` one), not a rim artefact. A sweep of all 28 cells' right and bottom rims finds the transition band is **1 pixel** on every unbevelled cell *including cell 27, which has no height stage at all*, and the largest single-pixel jump on a `Flat` rim (0.2970) is identical to cell 27's.

**So: the full-`body` cut is not visible in either committed artefact.** That does not make it right — the sheet's height column really does step by 16–24 px at a sub-pixel boundary, and a shallower grazing lamp or a finer `pixelSize` would expose it. It means the decision is not urgent, and the right panel shows what changing it would cost visually. Still the owner's.

---

## Regression

**Full height audit, in the Shaper editor, `2026-08-31 16:01`, `dataPath D:/UNITY/Laubrary Dev - Shaper/Assets`:**

| | verdict | | | verdict |
|---|---|---|---|---|
| H1 monotonicity | ok | | H7 wall statements | ok |
| H2 finite slope bounds (39 measured) | ok | | H8 hot-path allocation | ok |
| H3 infinite declarations by divergence | ok | | H9 analytic G′ vs central difference | ok |
| H4 closed-form inverse round-trip (327 360) | ok | | H10 the three normal dials | ok |
| H5 identity early-outs bit-identical | ok | | H11 stage is wired (incl. new 2b) | ok |
| H6 branch agreement + omission + N3 arm | ok | | tilted conformance | ok |

**0 lines containing `FAIL` in `HEIGHT-AUDIT.txt`.**

**The four earlier waves, run unchanged** (`_p2_regress.cs`; outputs kept as `_p2_{field,fill,border,light}.txt`):

```
ShaperFieldAudit    13 PASS   0 FAIL   RESULT: PASS
ShaperFillAudit     85 PASS   0 FAIL   RESULT: PASS
ShaperBorderAudit   79 PASS   0 FAIL   RESULT: PASS
ShaperLightAudit   150 PASS   0 FAIL   RESULT: PASS
```

Same counts the second verifier measured before this pass. **Gotcha for the next agent, confirming the verifier's:** the `unity` CLI's `--timeout` did not raise the pipeline's own 30 s cap, so the four-audit call returned `COMMAND_FAILED … timed out after 30000ms` — and the code kept running in the editor and wrote all four files ~3½ minutes later. Poll the output files; do not re-issue.

**The first fixer's whole battery, re-run** (`probe.dll`, rebuilt against the fixed source): ATTACK 1 all `ok` across every wall thickness × tilt; `InverseLowerBound` containment **829 316 800 checks, 0 violations**; multi-layer merge sorted and paired; Wall/Cap classification and `edgeDistance ≡ 0` on walls unchanged; H9 injection, H4 injection and the forward-evaluation sweep unchanged. The 2 520 one-ulp monotonicity drops under CoreCLR are the known runtime split Part 11 already documents, unchanged by this pass.

**Adversarial march attacks** (`probe3.dll MARCH`): thin slots resolved down to 0.019 px (unchanged); breakpoint-adjacent tilted rays 45 cases / 0 wrong; **42 combinations × 3 tilts: 126 rays, 0 wrong (was 6 wrong, all N3)**; wall-parallel 30/30; ellipse-tangent 24/24; nested subtractions 42/42; 800×800 support box with a 2 px sliver 14/14.

**Allocation.** H8: 1 197 824 samples, **0 bytes managed heap delta, 0 gen-0 collections**, covering `ShaperHeight.FillTile`, `Composed`, `ComposedDerivative`, `Inverse`, `Breakpoints`, `ShaperResolve.Query` (both branches) and `ShaperNormals.FillTile(Profile)`. BT-14's IL decoder over the transitive call graph from `PaintTile`: **50 methods, 0 heap allocations**, types reached including `ShaperHeight`.

**Artefacts.** Both re-rendered in the same `RunAll` pass, `2026-08-31 16:01`:

- `height-contact-sheet.png` — 28 cells, 6×5, 659×550.
- `tilted-conformance.png` — 420×300, tilt 34° / yaw 26°, two layers. **Branch ASSERTED: general 126 000 rays, straight-down 0 rays.** Surface hits 43 025, of which **WALL 10 049** and CAP 32 976 — a visible side wall, which is the whole point of the frame.
- `rim-step.png` — new, the rim A/B above.

**Both PNGs are byte-identical to the pre-Part-12-doc-edit re-render, and their reported counts are identical to the first fix pass's.** That is the expected outcome and I want it on the record rather than looking like a skipped render: N1 needs a Z-parallel ray, N2 needs a truncated buffer, and N3 removes pairs of zero screen area — the tilted frame contains none of the three. The render was re-run twice from the fixed code (15:47 and 16:01) and both times produced the same bytes, which is also the determinism check.

---

## What I could NOT fix, and why

1. **`zTol = max(1e-3, 1e-3·body)` in `EmitAt` is still underived.** Third pass in a row it has been recorded as a gap. It is *dimensionally* sound — both operands are canvas pixels and it is already relative to `body`, so the N6 sweep does not condemn it — but nobody has derived the constant from the thing it is presumably absorbing (the bisection's residual in z, which is `|dz|·step/2^SurfaceIterations` and therefore depends on the step, a quantity `EmitAt` does not have). Deriving it properly means either passing the bracket into `EmitAt` or deciding on a different rule for Cap-vs-Wall, and that is a design change, not a fix. I built no fixture that puts a cap within `zTol` of its own base either.

2. **The march's speed was not measured against a pre-fix baseline.** Getting one honestly needs a second harness built against a copy of the pre-fix source, which is exactly the kind of parallel copy that causes confusion in this tree. I measured the two cost drivers structurally instead — `InverseAtEUpperBound` early-outs on 99.91% of calls, and `linearEGrad` widens by a mean 1.0858× on `Linear` only — and left an absolute figure for whoever wants a baseline: the tilted render, 126 000 general-branch rays, is **2 608 ms** on this machine.

3. **The `Linear` y-frame *look*** remains an owner question, as both verifiers said. F7 is internally consistent across all four sites; whether the resulting lean is the wanted one is not a thing I can measure.

4. **The 0.001 px slot anomaly** the second verifier noticed (the march misses 0.01 / 0.005 / 0.002 px slots and then finds a 0.001 px one) is still there and still unexplained. It reproduces exactly after the fix, so it is not related to anything I touched. I agree with the verifier that it looks like sampling phase rather than a property, and I did not prove it either.

5. **The rim cut** is deliberately not decided. See above.

## What a THIRD verifier should attack

1. **The half-open rule at a DEGENERATE breakpoint set.** `lastSlab` is the largest `k` with `breakpoints[k+1] > breakpoints[k]`, and `Breakpoints` can emit a duplicate-suppressed list whose *last* entry is the top. Build a fixture where the top breakpoint is itself degenerate (e.g. `supG` colliding with a `Stepped` tread at 1e-7, `Insert`'s dedup tolerance) and check a Z-parallel ray at exactly `base + body·supG` is still marched by exactly one slab. I asserted this by construction and by the `z = 40.0` row; I did not build the adversarial breakpoint set.

2. **Truncation ACROSS layers.** N2 makes each layer's retained crossings a prefix *in that layer's ray order*, which is what `Depth(layer)` needs. It does **not** make the whole multi-layer list a global prefix: layer 0 can fill the buffer and starve layer 1 even where layer 1's crossings come earlier along the ray. I judged that out of scope because parity is stamped per layer and `Depth` filters by layer, but nobody has tested a multi-layer scene with a buffer too small for layer 0.

3. **The `G > 0` convention where `G` is *nearly* zero.** I ruled on exactly zero. A `Round` profile with `curve = 4` reaches `G = 1e-45` at small `t`; the march will now call that solid and emit a pair of width ~1e-43 px. That is not a zero-measure span so my rule does not catch it, and `SurfaceResolution` should — but I did not build the fixture.

4. **`SteppedProfileInverseExact`'s DOWN loop is unbounded in principle.** It terminates (`k` decreases, `k > 1` guards), and in practice runs 0–1 iterations, but a fixture that makes it walk many steps would be worth having — and would also tell you whether the O(n) worst case is reachable on the hot path.

5. **`InverseAtEUpperBound`'s bisection inside the march.** It now runs, rarely, *inside the per-step bracket*. Check it cannot make the march non-terminating or push `bracketCapped` up on a fixture the old code handled: my sweeps report `bracketCapped 0` and `stepsExhausted 0` throughout, but they are not adversarial about this specific interaction.

6. **The rim, if the owner rules on it.** My "not visible" claim is measured on one contact sheet at `pixelSize 1` with one rig. A grazing point lamp low over the plane, or a much finer `pixelSize`, is where I would expect it to become visible, and I did not render either.

7. **Whether N5's wider bound changed any `Linear` march result.** It should not have (a wider bound only shortens steps), and `probe3.dll LIN` finds 0 omissions before and after — but that probe was built to hunt *omissions*, not to detect a spurious extra crossing or a moved one. A crossing-position diff on `Linear` across the two bounds would close it.
