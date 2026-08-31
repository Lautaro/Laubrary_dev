# T-0107 — independent verification of the border stage

**2026-08-31. Verifier's report.** The subject is the Shaper border stage as delivered against `BORDER-CONTRACT.md`, together with the `BORDER-AUDIT.txt` that reported it green. The audit was treated as the thing under test, not as evidence.

Everything below was measured in the live Shaper editor (`D:/UNITY/Laubrary Dev - Shaper/Assets`, port 7801), through probe snippets in `verify/`, against the real compiled code. Numbers are quoted as measured; nothing is asserted from reading alone unless it says so.

**Verdict: four defects found behind the green audit, three of them in code and one in the contract. Three legs of fourteen could not fail in the direction they existed to test. All four defects are fixed and re-measured, all four unfalsifiable legs are now falsifying and mutation-proven, and one further defect was found in a stage underneath this one and deliberately left.**

---

## 1. Summary table

| # | What | Before | After | Fixed |
|---|---|---|---|---|
| D1 | BD-2.4 — the culling box under-bounds a joined node and its strip on an anisotropically transformed member | **808 samples outside the declared box, 23.500 px overshoot** | **0 outside, 0.000 px** | yes |
| D2 | BD-3.6 — a border on a node with no fill of its own draws OVER a later fill-owning sibling | rim reads magenta `(1.000, 0.000, 1.000)`; control reads red | reads red `(1.000, 0.000, 0.000)`, matching the control | yes |
| D3 | BD-3.2 — the anchor wiring was accidentally satisfied, not implemented; BT-6 could not fail | strip anchor ≡ node anchor bitwise on every fixture (34 vs 34) | strip anchor 54 vs node 34 at reach 20; wiring now load-bearing | yes |
| D4 | BD-1.6 — the outward complement is claimed EXACT and is not | BT-5 asserted "max difference 0" on one disc, 208 samples | asserted ≤ 1 ULP over 5 primitives, 1004 samples; contract over-claim recorded | code correct, contract wrong |
| D5 | BT-14's allocation instrument swept a hand-written type list | list happened to cover the closure; no owner for keeping it so | transitive closure from `PaintTile`, 31 methods, 0 allocations | yes |
| D6 | BT-14's XML doc named an instrument the body does not use | doc said `GC.GetAllocatedBytesForCurrentThread` | corrected, with the reason it was wrong | yes |
| D7 | Contact sheet exercised neither D1 nor D2 | byte-identical after both fixes | two cells added; D2 now visible | yes |
| D8 | **Pre-existing (T-0105):** a wide soft-Add blend pushes the silhouette outside the box **with no border at all** | k=60: 4892 samples outside, 26.500 px, border absent | unchanged — reported, not fixed | **no, deliberately** |

Files changed, all inside the two folders I was allowed to touch:

- `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Runtime\Shaper\ShaperCompiler.cs`
- `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Runtime\Shaper\ShaperProgram.cs`
- `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Runtime\Shaper\ShaperBorder.cs`
- `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Runtime\Shaper\ShaperFillResolver.cs`
- `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Editor\Shaper\ShaperBorderAudit.cs`

Nothing else in the worktree and nothing in the main copy outside `.agenthq\workspace\T-0107\`. No commit, no stage, no stash, no MenuItem, no EditorWindow.

---

## 2. D1 — the culling box excludes real samples (BD-2.4)

**BD-2.4 makes this a correctness clause in its own words:** "it is a bound; a bound that excludes real samples is a correctness failure, not a performance one."

### What was measured

An Outward border of width 8 (reach 8), `joinsCoverage = true`, on an ellipse under four transforms. For every sample where the joined field or the strip field is ≤ 0, I asked whether the sample lies inside the program's declared `supportC*/supportHalf*`.

| Fixture | Samples inside | Outside the box, BEFORE | Worst overshoot, BEFORE | Outside, AFTER |
|---|---|---|---|---|
| identity | 2628 | 0 | 0.000 px | 0 |
| rotation 37° | 2616 | 0 | 0.000 px | 0 |
| **scale (2.0, 0.5)** | 4288 | **808** | **23.500 px** | **0** |
| rot 37° + scale (1.7, 0.6) + skew (20, −13) + translate | 4014 | **304** | **8.520 px** | **0** |
| bag (identity) wrapping an anisotropic child | 4288 | **808** | **23.500 px** | **0** |
| the pre-existing `Shell` operator, same member | 2924 | **808** | **23.500 px** | **0** |

A plain anisotropic member with no border and no shell was already tight (0 outside), so the base corner-sum box is correct — the defect is entirely in the growth.

### Root cause

Every node publishes `σ_min · d_local`, which is a conservative **under**-estimate of true canvas distance. So the set `{d ≤ r}` reaches as far as `r · σ_max/σ_min` canvas pixels from the boundary along a stretched axis. `EmitBorderJoin` grew the canvas box by the bare `reach`. On scale (2.0, 0.5), `σ_max/σ_min = 4`, so reach 8 needed a growth of 32 and got 8 — deficit 24, measured overshoot 23.5. The identical bug is in `EmitShell`, which the border stage reuses, and which predates it.

The bag case proves the correction cannot come from the node's own transform: the bag's `σ_max/σ_min` is 1 and its box was still 23.5 px short, because it inherits its child's anisotropy.

### The fix

`ShaperCompiler.Emitted` gained `spreadRaw` / `Spread` — the worst `σ_max/σ_min` anywhere in the subtree, set at a leaf from the accumulated forward map, taken as a `max` over a bag's members, and carried unchanged through Sweep, Shell and the border join. `EmitShell` and `EmitBorderJoin` now grow the canvas box by `distance × Spread`; `ShaperProgram.supportSpread` publishes it so `ShaperBorder.CompileStrip`'s unjoined path uses the same product. A `max` is conservative for every combine mode, because Subtract's and Intersect's results are subsets of an operand and Add's union needs the worst of the two.

`Spread` is a property, not the field, because `Emitted` is a struct: an unset `spreadRaw` is 0, and a 0 would have multiplied every box growth to nothing.

**On an isotropic tree `Spread` is exactly 1 and every number is unchanged bit for bit** — confirmed by the identity and rotation rows above, by both regression audits being byte-identical, and by the contact sheet being byte-identical.

### The leg that let it through, and its replacement

BT-10 asserted `supportHalfW grew == reach` on a **disc**, where `Spread` is 1 and the defect's own value is the expected value. The leg asserted the bug as correct. It now also runs `BoxContainment`, which asserts containment on the scale-(2.0, 0.5) member: 5028 joined samples and 3216 strip samples, 0 outside.

**Mutation-proven.** Reverting `EmitBorderJoin` to `Grow(border.reach)`, recompiling, re-running: BT-10 reports `OUTSIDE the declared box 940 (expected 0), worst overshoot 23.500px  FAIL` on both sub-legs. Reverted.

---

## 3. D2 — a hostless border's z-order depends on an unrelated dial (BD-3.6)

The implementer flagged the accumulator choice for a border whose node bound no fill as "an over-reach". It is worse than an over-reach; it is a visible ordering bug.

### What was measured

A bag filled white, containing member **A** (magenta 6 px inward border, **no fill of its own**) and, above it in fold order, member **B** (opaque red fill) overlapping A. The probe reads the linear colour at `(17, 0)` — a point on A's rim that lies inside B. BD-3.6: "a member's border folds into the member's subtree, which folds into the bag", so B must be on top.

| Configuration | Colour at (17, 0), BEFORE | AFTER |
|---|---|---|
| A owns **no** fill | **`(1.000, 0.000, 1.000)` magenta — A's rim over B** | `(1.000, 0.000, 0.000)` red |
| A owns a fill (control) | `(1.000, 0.000, 0.000)` red | `(1.000, 0.000, 0.000)` red |

So giving A a fill — a dial with nothing to do with z-order — flipped which shape was on top. That is the "looks deliberate and is wrong" class.

### Why it happened, and why the obvious fixes do not work

A hostless strip has no accumulator of its own, so it composites into its nearest binding ancestor's, at that ancestor's turn. That turn is necessarily **after** the ancestor's own paint — it has to be, because the ancestor's own contribution is *summed* (FC-3.5a) and would otherwise wash the strip out to `magenta + white` — and therefore also after every one of the ancestor's descendants has folded in.

I worked through the alternatives before patching:

- **Give the strip its own slab and fold it at its own index.** Correct against later siblings, but the ancestor's own paint then sums on top of it and the rim disappears into the fill. Measurably worse.
- **Give the bordered node a pass-through accumulator.** Same failure: the ancestor's own paint is still summed last.
- **Split "ancestor's own paint" from "descendants" into two layers and composite Over between them.** Exact, and it reopens the alpha deficit T-0106 closed (244 samples, worst 0.2484) because those two regions are disjoint and must be *summed*, not Over-ed.

The architecture is built on "sum, because disjoint", and a border is deliberately *not* disjoint. Inserting a non-disjoint layer between two disjoint parts is not expressible without restructuring FC-3.5a.

### The fix actually applied

`ShaperFillOwner.borderSubtreeEnd` — one past the last owner index of the bordered node's own subtree, recorded in `Walk` after the children are walked, and only for a hostless border. In `PaintTile`, at the turn of owner `borderSubtreeEnd − 1`, the ancestor's accumulator holds **exactly the later siblings** (everything at or above `borderSubtreeEnd` has folded; nothing inside the node's own subtree has). Its alpha is snapshotted into the border's own otherwise-dead subtree slab, and the strip's coverage is later scaled by `1 − that`.

Costs nothing: no allocation, no new buffer, and `ClearDestination` already zeroes the slab per tile. **Zero is the correct "no later sibling" value, so a document without one is bit-for-bit unchanged** — which is why both regression audits and the contact sheet came back identical.

**Honest limit, stated because it is an approximation and not a proof.** Exact when the later sibling is opaque (the strip contributes nothing, which *is* "the sibling is on top") and when there is none. Under a *partially transparent* later sibling the result is a blend rather than the exact `sibling Over strip`, because the accumulator has already mixed that sibling with the ancestor's other content and the two can no longer be separated. The approximation is monotone and agrees at both limits. I measured only that; I did not measure how far it is from exact in the middle.

**Mutation-proven twice.** BT-13's M11 clears `borderSubtreeEnd` on the resolved owner and requires FAIL. Separately, reverting the `hostless` guard in `PaintTile` to `false` and recompiling: BT-11 reports `reads 'A border' ... expected 'B fill'  FAIL` while the control still reads `B fill` — the exact asymmetry. Reverted.

---

## 4. D3 — BD-3.2 was accidentally satisfied; BT-6 could not fail

The implementer flagged BT-6 as unable to fail in the positive direction. Confirmed, and worse than reported.

`ShaperFillAnchor.From` reads only `rootInverse` and `localSupport*`. `CompileStrip` copied **both** from the node program unchanged. So `From(strip)` was bitwise equal to `From(nodeProgram)` **always, on every fixture** — not by luck. BD-3.2 was therefore satisfied under *any* wiring, and could not have been implemented wrongly in a way any test could see. BT-6's leg (c) ("the bound fill's anchor == the node's anchor, bitwise") was equally vacuous.

BT-6's leg (a) was independently vacuous for a second reason: it built **both** anchors itself with `ShaperFillAnchor.From(nodeProgram)`, so it compared the node's anchor against the node's anchor and never touched the resolver at all.

### Fix

`CompileStrip` now grows the strip's own node-local box by `reach / rootSigmaMin` — the same `distance / σ_min` conversion `EmitShell` already makes — via a new `ShaperProgram.rootSigmaMin`. The strip program becomes honest about its own extent, and BD-3.2's wiring becomes load-bearing.

Measured after: node anchor half-extent **34**, strip anchor **35** at reach 1, **54** at reach 20, **44** for a straddling border of width 20 (reach 10), and **34 == 34** for an inward border (reach 0, correctly identical — an inward strip does not extend past its node).

BT-6 now: (a0) a **precondition** leg asserting the two anchors differ, printed with both numbers; (a) the 1px-vs-20px comparison taken from the **resolver-bound border owner**; (a1) a control anchoring the same fill on the strip, which must differ. Nothing about the picture changed — `anchorOverride` was always passed correctly — so this is a testability fix, not a behaviour fix.

**Mutation-proven.** Reverting `localGrow` to 0: `precondition: the STRIP's anchor half-extent 34 differs from the NODE's 34 ... FAIL`, `control, the STRIP's own anchor instead: colour diffs 0/4 ... FAIL`, and BT-13 M10 FAIL. That is precisely the state the shipped audit was in, now reported as a failure of the audit. Reverted.

---

## 5. D4 — BD-1.6's outward identity is not exact (a contract error, not a code defect)

BD-1.6 and BT-5 both claim the outward strip's coverage and the shape's coverage "sum to **exactly** 1 at every sample". The shipped leg reported `max abs difference 0, bitwise-identical 208/208, PASS`.

**That is true of a disc and false of the stage.**

`Coverage(−d, h) = 1 − Coverage(d, h)` holds algebraically — `(1−t)²(1+2t)` expands to `1 − t²(3−2t)` identically — but `fl(0.5 − d)` is not `1 − fl(d + 0.5)`, and the cubic is then evaluated at two slightly different points.

| Measurement | Result |
|---|---|
| Kernel swept over 400,001 values of `d` across the band | **10,018 (2.5%) where the float sum ≠ 1**, max error `1.1920929e-07` = **one ULP at 1.0** |
| Through the real strip programs, ring `\|d\| < 0.5`, disc R=34 | 0 of 208 non-bitwise-1 |
| same, rect | 0 of 276 |
| same, star | 16 of 248 |
| same, diamond | 16 of 136 |
| same, **triangle** | **136 of 136 — every single sample** |

Max deviation is always exactly `5.96046448e-08`, a half-ULP, i.e. the sum rounds to `0.99999994`.

**This is a contract over-claim, not a code defect,** and I have not "fixed" the code to match: no reasonable change makes a shared `Coverage` self-complementary bitwise, and doing so would mean special-casing the kernel for one caller. Per my remit I fixed the *test* to assert what is true and recorded the disagreement here.

BT-5 now measures five primitives (1004 ring samples), asserts `|sum − 1| ≤ 1 ULP`, and **reports** the bitwise count without asserting it — so the triangle's `0/136` is visible in the output rather than hidden by a fixture choice. BT-4 (the inward identity) still asserts **bitwise exactness**, and it holds: 0 diffs on all seven primitives I tried, for a structural reason — near the boundary `max(d, −d − w)` returns the operand `d`, so the two are literally the same float.

`ShaperBorder`'s class documentation repeated the over-claim verbatim; corrected in place with the measurement.

**Recommendation for the contract:** BD-1.6's second bullet should read "sums to 1 to within one ULP" and note that the exactness is algebraic, not IEEE754.

---

## 6. Legs that could not fail, and what was done

| Leg | Why it could not fail | Now |
|---|---|---|
| **BT-6** (positive) | compared the node's anchor with the node's anchor; and the strip's anchor was identical to the node's by construction anyway | takes the anchor from the resolver; precondition asserts the two are distinguishable; strip-anchor control; M10 |
| **BT-5** | asserted "difference 0" on a disc — the single best case | 5 primitives, 1-ULP bound, bitwise count reported |
| **BT-10** (culling half) | asserted `grew == reach` on a disc, which **is** the defect's value | asserts containment on an anisotropic member; M9 |
| **BT-14** (instrument) | swept a hand-written type list; no live failure, but structurally unable to notice code added elsewhere | transitive closure from `PaintTile`; prints the types reached |
| *(introduced and caught)* Rect ring fixture | an axis-aligned rect of half-extent 34 on a half-integer grid has its edges exactly on `\|d\| = 0.5`, so the band contained **0 samples** and "max difference 0" passed over an empty set | half-extent 34.25; the leg now requires >40 ring samples |

BT-13 gained **M9, M10, M11**, each of which runs a strengthened measurement against a deliberately broken subject and requires FAIL. All three detect.

I also ran **three source-level mutations** on the runtime and confirmed the legs fail on a genuine regression, not only at the point of use — reverting the spread (BT-10 → FAIL, 940 outside), the hostless mask (BT-11 → FAIL, reads 'A border'), and the strip anchor growth (BT-6 → FAIL + M10 → FAIL). All reverted; final state verified compile-clean with all three fixes present.

---

## 7. What I checked and found CORRECT

These matter as much as the defects. Each was measured, not read.

- **BD-2.2, the dilation, is right and BT-8 is a real test.** Over the interior of the dilated silhouette (samples with `d₀ ≤ reach − 1`, measured against the *borderless* program so the test is not circular): `d − reach` gives **0 samples below 0.999, min coverage exactly 1.000000** on Ellipse (4260 samples), Star (3934) and Triangle (4216). The naive `min(d, s(d))` control fails the same measurement with **172 / 286 / 140** samples and min coverage **0.508 / 0.505 / 0.813** — the predicted ≈0.5 seam, on the original silhouette ring exactly as BD-2.2 says. Both halves are genuine; BT-8 is not decorative.
- **BD-1.6 inward identity: bitwise exact.** 0 diffs over 7 primitives.
- **BD-3.3 is implemented and is independently falsifiable.** `ownDistance` at the border owner's slab matches the strip program's field 4096/4096 and the node's field 0/4096; the ramp is a ridge `[0.0000, 1.0000, 0.0000]` at [inner face, centre line, outer face]; M5 proves the ridge test fails on the node's field. BD-3.2 and BD-3.3 are now each proven separately and each proven able to fail — they were the pair most likely to be silently wired to each other, and they are not.
- **BD-3.4, the ancestor clip, is correct.** A member's inward rim inside a region a *later* Subtract member carved away reads alpha **0.0000**; on the intact side **1.0000** and the rim colour. This clause had **no leg of its own**; it now has one in BT-12.
- **BD-3.5's Over fold is clean — no seam, no fringe, no NaN.** Six fixtures scanned sample-by-sample strictly inside the finished silhouette: three borders in three alignments (4773 samples), an additive border, a translucent border, a hole + bag border, a **NaN veil** on the border, and a hostless border. **0 samples with alpha < 0.99, 0 NaN/Inf, 0 negative components** in all of them. The NaN veil is correctly rejected by the `!(ce > 0)` guard rather than propagating. (The sixth fixture reports 2734 samples at alpha 0.5000 — that is an authored `veil = 0.5` sibling, not a seam, and 0.5000 is exactly the authored value.)
- **BD-1.5 is an exact no-op, in all three senses.** Width 0 from the start, width swept 6 → 0 on the same object, and `enabled = false` at width 6 — all three with `joinsCoverage = true` — give 0/16384 byte diffs, 0 field bit diffs, **0 extra ops** (so no `Dilate` is emitted at width 0), 0 border owners, and the same owner count. The live-border control changes 1628 bytes, so the identity is not between two pictures that were never different.
- **BD-3.7 is refused in both halves and the diagnostic block genuinely mirrors the existing four.** No owner is created on the Subtract member, the compiler refuses the dilation on the identical test (0 field bit diffs), and `ShaperFillDocument` carries `hasSubtractBorder` / `subtractBorderNode` / `subtractBorderReason` / `subtractBorderCount` — boolean, first offender's name, full sentence, count — and `Summary()` prints it in the same form as the other four. The sentence is the full one, not a fragment.
- **BD-1.3 / BD-1.4.** BT-2's bitwise agreement with `ShaperOps.Shell` over 24,576 samples × 6 configurations, including the joined path; BT-3's band placement and the straddling double-width trap.
- **BT-14's claim is true.** Verified independently by a transitive closure over the call graph from `PaintTile`: **31 methods across 9 Laubrary types, 0 allocation opcodes**. The hand-written list the shipped leg used covered that closure except for one allocation-free struct accessor, so the claim held; only the instrument was fragile.
- **Regression is clean.** `ShaperFillAudit.RunAll()` (21 legs) and `ShaperFieldAudit.RunAll()` (13 legs) were re-run leg by leg after every change. Both are **byte-identical** to the pre-change baselines captured before I touched anything, with 0 FAIL tokens — so T-0106's `Refuse`/`UnavailableReason` sentences are byte-identical, and my change to the pre-existing `EmitShell` box perturbed nothing. Saved to `verify/FILL-AUDIT.txt` and `verify/FIELD-AUDIT.txt`, with the baselines kept alongside as `*.before.txt`.
- **The contact sheet reads correctly by eye.** I looked at it. Cell 04 is an unadorned disc (BD-1.5 as a picture); 11 is bright along the strip's centre line and dark at both faces (the BD-3.3 ridge, not a monotone band); 13 has **no magenta** (the BD-3.7 refusal); 14 has 0 seam marks; 07 vs 08 differ as the legend predicts.

---

## 8. D8 — a pre-existing defect I found and deliberately did **not** fix

While bounding the border's box I checked whether a wide soft-Add blend also escapes the declared box. It does, **with no border present at all**:

| Blend width `k` (sharpness 0) | Samples inside | Outside the box | Worst overshoot |
|---|---|---|---|
| 0, no border | 4344 | 0 | 0.000 px |
| 20, no border | 4792 | **304** | **4.500 px** |
| 60, no border | 10044 | **4892** | **26.500 px** |
| 60, joined border reach 8 | 13152 | 5376 | **26.500 px** |

The last row is the important one: with the border added the overshoot is **identical** to the borderless case, so the border's own contribution is correctly bounded by the D1 fix and the entire remaining deficit is the blend's. `smoothMin(a,b,k,n) ≤ min(a,b)` by up to `k/(2n)`, an **additive** deficit that `supportSpread` (a multiplicative factor) does not and should not cover.

**I left it, on purpose.** It is not the border stage, it is not any BD clause, it is present with no border at all, and the correct bound compounds across nested soft combines — it deserves its own derivation and its own conformance test in the shape engine, not a one-line patch smuggled into a border verification by someone auditing a different stage. The one-line shape of the fix, for whoever schedules it: grow a soft-Add's combined box by `blend.width / (2 · ShaperOps.BlendExponent(sharpness))` in `ShaperCompiler.EmitBag`. Numbers above are the before-measurement.

---

## 9. What remains UNVERIFIED

Stated plainly, because a green audit that hides its gaps is what this pass exists to catch.

1. **Nothing has been seen animated.** `width` is a `ZUIValue` resolved once per compile by construction, and I confirmed `ShaperBorder.Resolve` samples it exactly once, but no moving picture was rendered and no per-frame sweep was measured. The contract already says this; it is still true.
2. **Determinism across a domain reload** is untested, the same gap the fill and field stages carry.
3. **Speed is not measured.** Nothing in T-0105, T-0106 or T-0107 is timed. The IL closure proves there is no allocation *opcode* on the per-tile path; it says nothing about cost. Note the border apply loop is O(k²) in owner count.
4. **The heap instrument in BT-14 remains unreliable in both directions and is reported, never asserted.** M8's calibration moved between runs during this very session: 1 MB of 24-byte arrays showed as **0 bytes** on one run and **12,288** on the next, with no code change. Treat any heap number from that leg as context only.
5. **The D2 fix under a partially transparent later sibling is an approximation**, as set out in §3. Exact at both limits, unmeasured in between.
6. **`supportSpread` does not cover additive under-reporting** from soft combines (§8), nor anything a future non-affine operator might introduce. It bounds the transform-induced multiplicative factor and that is all it claims.
7. **Texture and Gradient border fills are verified by eye on the contact sheet, not numerically.** Cells 09 and 10 look right; no per-sample assertion covers a textured outline's texel mapping.
8. **The Part B4 rulings** (lit-solids facet lines kept, Text ported, Inferno reclassified) rest on reading Pyre, not on running it — unchanged from the contract's own Part B9, and outside what this task builds.
9. **I did not re-derive the contract.** Where I concluded a clause was wrong (BD-1.6, §5) I fixed the test to measure the truth and reported the disagreement rather than editing the clause.

---

## 10. Artefacts

- `BORDER-AUDIT.txt` — 14 legs, **14 `RESULT: PASS`, 0 FAIL tokens**, with every strengthened leg in place.
- `verify/FILL-AUDIT.txt`, `verify/FIELD-AUDIT.txt` — regression runs, 0 FAIL, byte-identical to `*.before.txt`.
- `border-contact-sheet.png` — re-rendered, now **16 cells**. Cells 15 and 16 are new and are the D2 fix as a picture: the magenta rim belongs to the left member, which is below the red one in fold order, so the red disc is on top **in both** — the two configurations now agree, where before they did not. Legend in `verify/contact-sheet-report.txt`.
- `verify/v*.cs` — the independent probes, runnable as-is through the Unity CLI.
- `verify/scripts/` — the patch scripts used to edit the audit, kept so every edit is reproducible.
