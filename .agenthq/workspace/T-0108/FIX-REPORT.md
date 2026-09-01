# T-0108 — fix report

Fix pass over Wave 2's light rig, against the twelve findings in `VERIFICATION.md`. Work done in the Shaper worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, **uncommitted and untracked**, exactly as the three prior wave tasks left theirs. Editor identity confirmed before any result was trusted: `Application.dataPath = D:/UNITY/Laubrary Dev - Shaper/Assets`, PID 261208, port 7801.

**Headline.** All four real defects fixed. All three dead tests rebuilt and each proved falsifiable by mutation. All three gaps closed with new legs. Both records corrected. One contract-specified behaviour (rim's ambient tint) deliberately **not** changed and instead declared, with the alternative flagged for the owner in finding 9 below.

**Final counts.** Light audit: **26 asserting legs, 26 `RESULT: PASS`, 0 `RESULT: FAIL`, 0 case-sensitive `FAIL` tokens** (plus LT-17, the contact sheet, which is rendered and explicitly not counted as a test). Regression: **48 legs discovered, 48 `RESULT: PASS`, 0 `RESULT: FAIL`, 0 `FAIL` tokens** — 13 `ShaperFieldAudit`, 21 `ShaperFillAudit`, 14 `ShaperBorderAudit`.

**Every mutation run in this pass is listed in §Mutations at the end**, with the leg it targeted and the failure it produced.

---

## 1. REAL DEFECT — `rimPower < 0` writes Infinity and NaN into `dst` — **FIXED**

**What it was.** `ShaperLightCompiler.CompileResponse` applied no clamp of any kind to `rimStrength`, `rimPower`, `specular`, `specularPower` or `intensityScale`. `Shade` evaluates `Mathf.Pow(1 - ndv, resp.rimPower)`; on a flat normal `ndv` clamps to exactly 1, so the base is exactly 0 and a negative exponent yields `+Infinity`; the rim is then tinted by ambient, so a **black** ambient turns that Infinity into NaN via `0 * Inf`. The resolver's NaN guard at `:1105` is upstream of the law and cannot see it.

**What I did.** Introduced one gate, `ShaperLightCompiler.Dial(v, lo, hi, fallback)` — refuses non-finite, then clamps — and ran **every** dial through it, on the rig, on the response block **and** on the Solids generator (`ShaperSolids.Dial`, same shape, same reason). The sweep the audit never did found a **second, independent NaN path nobody had looked at**: a light's `posX/posY/posZ`. A NaN position gives a NaN `dist`, which fails `dist > 1e-4f`, so `ldx = NaN * inv`; an *infinite* position gives `dist = Inf`, `inv = 0`, and `Inf * 0 = NaN`. Both reached `lr += li.r * dw` unguarded. Solids' `aspect`/`depth` were a third: a NaN there reaches the surface point through the barycentric interpolation and out through `pz` (the coverage tests happen to reject NaN by accident, since `w0 >= 0f` is false for NaN; the surface point does not).

Two sibling degeneracies the same clamp closes:
- `rimPower = 0` gave `Mathf.Pow(0, 0) = 1`, so rim was `rimStrength` on a flat normal rather than zero (measured `S = 1.1` against a baseline `0.9`) — which contradicts **LR-2.5's own stated consequence** that "on a flat surface N·V = 1, so rim = 0 identically". `rimPower` is now floored at `0.01`, and `pow(0, 0.01)` is exactly 0, which makes the contract's sentence true.
- `specularPower = -4` at a grazing normal returned `S = 8.999998E+11`. Now floored at `0.01`, ceilinged at `512`.

**The leg.** New **LT-18**, which feeds every dial on the rig, the response block and all six Solids forms the values `{-1, 0, +Inf, -Inf, NaN, -1e30, 1e30}` at ambient `{0.2, 0.0}` — **1 428 degenerate cases** — and asserts the count of non-finite floats reaching `dst` and the count of garbage encoded pixels are **exactly 0**.

| | before | after |
|---|---|---|
| non-finite floats in `dst` (`rimPower = -1`, ambient 0.2) | **19 200 Inf** | 0 |
| non-finite floats in `dst` (`rimPower = -1`, ambient 0.0) | **19 200 NaN** | 0 |
| garbage encoded pixels (`rimPower = -1`, ambient 0.0) | **6 400** | 0 |
| **across all 1 428 degenerate cases: non-finite floats reaching `dst`** | not measured | **0** |
| **across all 1 428 degenerate cases: garbage encoded pixels** | not measured | **0** |

**The number asked for: 0.** Zero non-finite floats and zero garbage encoded bytes, across every dial at every degenerate value.

LT-18 carries a standing control, leg (e): it drives the **law** directly with an unclamped `rimPower = -1` and asserts the result **is** non-finite (measured `S.r = Infinity`). If that ever goes finite, either the law started clamping — which LR-2.2 forbids — or the probe stopped measuring, and either way the zeroes above would stop meaning anything.

**Mutation proving the leg:** M2, removing the single `Dial(...)` wrapper from `c.rimPower`. LT-18 → **FAIL**, at **268 800 non-finite floats and 89 600 garbage pixels**.

---

## 2. REAL DEFECT — the eight-light cap counted AUTHORED lights, not ENABLED ones — **FIXED**

**What it was.** The refusal was raised from `authored = list.Count`, which includes disabled entries, while the compile loop skipped `!l.enabled`. The two disagreed the moment anything was switched off, and the sibling diagnostic in the same file (`Finish`) already used `prog.rig.count` correctly.

**What I did.** Made them agree. The loop now walks the whole list — a loop that stops at eight cannot know how many enabled entries came after the eighth — counting `enabled` while compiling only the first eight, and the refusal is measured against `enabled`. `enabledLightCount` and `authoredLightCount` are both recorded on the program. The sentence now reads "the rig holds 11 enabled lights; only the first 8 **enabled** are lit" and appends the authored count when it differs, so the last row's doubly-wrong sentence cannot recur.

| rig | enabled | truly dropped | `hasTooManyLights` before → after | `tooManyLightsCount` before → after |
|---|---|---|---|---|
| 11 authored, none disabled | 11 | 3 | True → True ✓ | 3 → 3 ✓ |
| 9 authored, the 9th disabled | 8 | 0 | **True ✗** → **False ✓** | **1 ✗** → **0 ✓** |
| 11 authored, 5 disabled | 6 | 0 | **True ✗** → **False ✓** | **3 ✗** → **0 ✓** |
| 12 authored, first 8 disabled | 4 | 0 | **True ✗** → **False ✓** | **4 ✗** → **0 ✓** |
| 8 authored, none disabled | 8 | 0 | False → False ✓ | 0 → 0 ✓ |
| 13 authored, 2 disabled *(added)* | 11 | 3 | — → True ✓ | — → 3 ✓ |

**Wrong in 3 of 5 configurations before; correct in 6 of 6 after.**

**The leg.** LT-11 strengthened with all six configurations above, plus an assertion that `Finish`'s `rig.count` and the cap's `enabledLightCount` agree.

**Mutation proving the leg:** M1, reverting the cap to `authored`. LT-11 → **FAIL**, reproducing the verifier's table exactly (`flag True/False, dropped 1/0`, `3/0`, `4/0`, `5/3`).

---

## 3. REAL DEFECT — `aspect` and `depth` silently inert on three forms — **FIXED, as a DECLARED inertness**

**What it was.** `aspect` was documented as "the Y half-extent multiplier" with no exception and `depth` with exactly one ("Unused by Can"). Measured at rotation (35, 28, 12), a 1 → 0.4 sweep moved **0 pixels** for `aspect` on Orb, Gem and Ring, and for `depth` on Orb, Gem and Ring. Five silent zeroes, no diagnostic, no doc note, no leg — against LR-7.3's own words.

**What I did — the brief's preferred fix.** Made the inertness **declared rather than silent**: `ShaperSolids.InertReason(form, dial)` is a table returning `null` for a live dial and a reason sentence for a dead one, and `ShaperSolids.Compile` now raises `hasInertDial` / `inertDialName` / `inertDialForm` / `inertDialReason` / `inertDialCount` in the **exact shape** of `hasTooManyLights` and `hasUnavailableFill`, so a future UI can grey the control with the reason shown. Only a dial moved **off its neutral** raises it — the complaint is about a control the author operated and got nothing from, not about a field's existence.

**I did NOT wire the dials, and that is a judgement not a shortcut.** The brief says "wire the dial for real only where it is genuinely cheap and correct; do not invent geometry." Pyre's own `BuildGemGeometry` (`PyreRenderer.cs:4796-4818`) reads neither `solidAspect` nor `solidDepth` — a gem's proportions are `gemCrown` and `gemPavilion` — and `DrawOrb`'s silhouette is the plain circle `d <= R` by LR-6.5b's explicit ruling. Wiring either would invent geometry the reference does not have, against LR-6.2's "verbatim, because it is correct". The table also caught a **sixth** silent dial nobody had reported: **`roll` on a Ring** (`BuildRing` takes only yaw and tilt; a ring is rotationally symmetric about its own axis, so rolling maps the annulus onto itself).

**The leg.** New **LT-19**, the sweep the audit never did: **every form × every dial** (6 × 14 = 84 combinations), asserting each dial either moves at least 20 pixels **or** is declared inert. The assertion is **two-sided** — a declared-inert dial must move *exactly zero* — so declaring a working dial inert fails just as loudly as leaving a dead one silent. That is what stops the table becoming a place to park an inconvenient result.

| | before | after |
|---|---|---|
| silently inert dials (live by declaration, dead by measurement) | **5** (6 with Ring/roll, unreported) | **0** |
| falsely declared dials (declared inert, actually move pixels) | n/a | **0** |
| live dials moving ≥ 20 px | not measured | **56** |
| declared-inert dials measuring exactly 0 | not measured | **28** |
| coverage | 0 of 84 combinations tested | **84 of 84** |

Also added contact-sheet **cell 28**: a Gem with `aspect` and `depth` authored to 0.4, visibly identical to cell 08's Gem — the declared inertness made visible rather than only asserted.

**Mutation proving the leg:** M5, deleting the Gem row from `InertReason`'s `Aspect` case. LT-19 → **FAIL** (`SILENTLY INERT: 1`, diagnostic count 1 instead of 2). A second, unplanned proof arrived from M8: zeroing the glow made `EdgeGlow`/`InnerGlow` measure 0 on all six forms and LT-19 correctly reported **12 silently inert**.

---

## 4. REAL DEFECT — the point-light `range` dial inverted near zero — **FIXED**

**What it was.** `c.invRangeSq = range > 1e-6f ? 1f / (range * range) : 0f;`. `invRangeSq == 0` is the value that means **no falloff**, where the author means **no reach**, so the dial reversed at the bottom of its travel.

**What I did.** `float reach = range > MinRange ? range : MinRange; c.invRangeSq = 1f / (reach * reach);` with `MinRange = 1e-3f`. Branchless in effect and **exactly continuous** — at `range == MinRange` both formulations give the same number — and `dist² · invRangeSq` stays finite for every coordinate `MaxCoord` allows. The comment cites LR-2.4 and states plainly what `range == 0` means: **the light reaches nothing**; `atten` collapses to 0 at every non-zero distance and the receiver falls back to ambient. That is the continuous limit of the dial's own travel and the honest reading of a zero-range lamp.

`L` at canvas (0,0), light at (0,0,40), ambient black:

| range | 1e4 | 40 | 10 | 1 | 1e-2 | 1e-4 | 1e-5 | **1e-6** | **1e-7** | **0** | **−1** | **NaN** | **+Inf** |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **before** | — | 0.500000 | — | 0.000625 | 0 | 0 | 0 | **1.000000** | **1.000000** | **1.000000** | — | — | — |
| **after** | 0.999984 | 0.500000 | 0.058824 | 0.000625 | 0 | 0 | 0 | **0** | **0** | **0** | **0** | 0.500000 | 0.500000 |

**Inversions as the range shrinks: 1 before → 0 after.** `range == -1` now matches `range == 0` exactly; `NaN` and `+Inf` fall back to the documented default of 40 (hence `L = 0.5`), finite in both cases.

**The leg.** LT-18 leg (d), sweeping range across the discontinuity including exactly 0, negative, NaN and +Inf.

**Mutation proving the leg:** M3, restoring the old guard. LT-18 → **FAIL**, reproducing the defect verbatim: `inversions: 1`, `range == 0 gives L = 1.000000`.

---

## 5. DEAD TEST — LT-8 could not fail on the path it names — **REBUILT AGAINST A STORED GOLDEN, and D-9 corrected**

**What it was.** Both sides of LT-8's comparison executed the same branch of the same method: with `receive == 0` and no Solids overlay, `doLight` and `solidOwner` are both false and the render takes the identical `if (!doLight && !solidOwner)` block the null-scene render takes. D-9's claim that this is "strictly stronger than a stored golden" is **false** — it is circular. The verifier proved it: darkening that shared branch by 10 % left LT-8 **passing**, while T-0107's `BT11_Ordering` caught it.

**What I did.** `ShaperLightAudit.WriteGoldens()` wrote `.agenthq/workspace/T-0108/golden/lt8-unlit-128x128.rgba` — 65 536 B, 128×128 RGBA8, the T-0107-shaped conformance fixture rendered with lighting entirely absent (`scene = null`). **Provenance matters here and was checked:** the golden was written from a build whose `ShaperFillResolver.cs` md5 is `8d4f0e5d441ff8ba115a2ff69717f28d`, **byte-identical** to the `.ORIG` copy the independent verifier confirmed clean, and that file is untouched by this entire fix pass. LT-8 now compares **both** the null-scene render and the receive-off render against those bytes.

The staleness risk D-9 raised is real and is handled rather than argued away: the golden is regenerated only by `WriteGoldens()`, which `RunAll()` never calls and which is deliberately named **outside** the `LT*` prefix so no reflection sweep can invoke it by accident. Regenerating is a visible act with a diff. The live null-scene comparison is **kept but demoted and labelled WEAK** — it still proves the narrower "receive-off ≡ null-scene within this build", which is worth having and is all it ever proved.

| | before | after |
|---|---|---|
| unlit branch × 0.9 (the verifier's mutation) | **LT-8 PASS** — the leg was blind to it | **LT-8 FAIL, 5 242/16 384 differing pixels on both golden legs** |
| null-scene render vs stored golden | no golden existed | 0/16 384 |
| receive-off render vs stored golden | no golden existed | 0/16 384 |
| control (receive ON must differ) | 12 799 px | 5 054 px, still ≫ 1 000 |

**D-9 corrected in `IMPLEMENTATION-NOTES.md`**: the original defence is struck through and replaced with the circularity argument, the mutation evidence, and what the leg does now.

**Mutation proving the leg:** M9, `buf.subtree[...] += buf.albedo[...] * ce * 0.9f`. LT-8 → **FAIL**.

---

## 6. DEAD TEST — LT-2's primary instrument returned a constant zero — **REBUILT AND CALIBRATED**

**What it was.** `GC.GetAllocatedBytesForCurrentThread()` returns a constant 0 at every size on this Mono runtime, including a 64 MB allocation. The old calibration escalated to 4 MB, stopped, and concluded "BLIND… floor > 4 MB" — a phrasing implying it works above 4 MB. It does not work at any size. So `thread-alloc 0 B` was a dead reading, and "exactly 0 bytes" had never been measured. The old calibration also ran on a *different code path* from the measured region.

**What I did.**

1. **Calibrated in-region.** The calibration now runs the *same* `for (rep) { Paint(); }` loop with a known allocation added per rep, escalating `{64, 128, 256, 512, 1024, 4096, 16384, 65536}` bytes until the heap probe sees it. Whatever that smallest per-rep size is **is** the leg's detection floor, and it is printed as a number.
2. **Stated the real floor as a number.** **Detection floor = 1 024 bytes per `PaintTile` call** on the reporting run (observed heap delta 16 384 B over 65 reps). It is run-to-run variable — 512 B and 64 B were also measured across runs, because `GC.GetTotalMemory` is process-wide and the editor churns — which is exactly why the leg calibrates every time rather than hard-coding a threshold. **Honest reading: the heap probe cannot see a per-tile temporary below roughly 1 KB per call**; FT-9's known spurious editor churn produced 12 288 B in one case this run, comfortably under the 66 560 B gate.
3. **Demoted the dead probe.** `GetAllocatedBytesForCurrentThread` is still *read and printed*, so its inertness is demonstrated rather than asserted (it reported **0 B across a calibration in which 66 560+ bytes were provably allocated**), but it **no longer gates the verdict**.
4. **Promoted the IL scan to primary.** The instrument T-0106 used successfully, with **no floor at all**: an allocation is an opcode. `newobj` (0x73), `newarr` (0x8D), `box` (0x8C) across `ShaperLightLaw`, `ShaperNormals`, `ShaperSolids`, `PaintTile` and `LightSample`, each hit confirmed by resolving its operand as a metadata token.

| | before | after |
|---|---|---|
| what the verdict rested on | a probe that reads 0 at 64 MB | **IL scan (no floor)**, corroborated by a calibrated heap probe |
| stated detection floor | "> 4 MB", and wrong | **1 024 B per `PaintTile` call**, measured in-region |
| IL scan: raw byte hits / token-confirmed | 1 / 0 | 3 / **0** |
| heap deltas across the 8 cases | — | 0 B in 7 cases, 12 288 B in one (editor churn, under the 66 560 B gate) |

**Mutation proving the leg:** M11, `float[] mutScratch = new float[3]` inside `LightSample`. LT-2 → **FAIL**: the IL scan reported **1 token-confirmed allocation** (the conclusive instrument), and the heap probe independently caught it in 6 of 8 cases at 6–12 MB.

---

## 7. DEAD TEST — LT-14b was a structural guarantee — **MADE FALSIFIABLE**

**What it was.** `ShaperSolids.SampleOrb` computes coverage from `lx, ly, R` only; the rotation matrix is never consulted for coverage, only for the normal. So "0 differing alpha under rotation" could not fail, and the contract's named mutation — rotating the coverage instead of the normal — would have rotated *both* renders identically, leaving the difference at 0.

**What I did.** Made it falsifiable rather than marking it a guard, by asserting against a path the rotation **could** reach. Two new legs:

- **(c)** the rotated Orb's covered set is compared, sample by sample, against the **independently recomputed** analytic disc `d <= R` — recomputed here from the grid, not read back from the generator. Rotating the coverage makes the covered set an ellipse, which no longer equals the disc.
- **(d)** a **live control** on a form whose coverage genuinely *is* rotation-dependent: a Box under the same rotation must move its alpha. Without it, (a) and (c) both passing would be consistent with the fixture never applying rotation at all.

Leg (a) is retained but **relabelled STRUCTURAL** in its own output line, so it is not counted as a measured invariant.

| | before | after |
|---|---|---|
| rotate-Orb-coverage mutation | **undetectable** — both renders rotate, difference stays 0 | **detected**: (a) 612/16 384 and (c) 612/16 384, both FAIL |
| (c) coverage vs independently computed disc | did not exist | 0/16 384 wrong over 3 640 covered |
| (d) Box alpha under the same rotation (control) | did not exist | 1 812/16 384 |

**Same judgement applied elsewhere.** I swept the other legs for the same category. **LT-14c** is the one other case, and it is *already* honest: its own summary says "**Mutation: NONE**, and that is stated rather than hidden. This test locks in a KNOWN DEFECT as a regression guard." That is the correct disposition and I left it. LT-14b's leg (a) is now labelled the same way in its output. No other leg asserts a value the code cannot produce otherwise.

**Mutation proving the leg:** M10, making Orb coverage tilt-dependent. LT-14b → **FAIL** on both (a) and (c).

---

## 8. CONTRACT ERROR — D-6's justification was a rationalisation — **RECORD CORRECTED**

Not a code change; D-6's *ruling* is right (LR-1.1 forbids a per-generator light, so the Orb must rotate its published normal forward) and the code is right. Its two stated consequences were both false and were presented as measured facts. `IMPLEMENTATION-NOTES.md` now carries the correction with the verifier's numbers:

- *"For a directional light the two are bit-identical because there is no falloff"* — **false**. `(Rot·N)·L` vs `N·(Rot⁻¹·L)` over 3 600 normals at rotation (25, 40, 15): **bit-identical in only 1 895/3 600 (53 %), worst |difference| 5.960e-08**. The identity is exact in real arithmetic and not in floats — the same argument D-1 makes correctly about LT-1, applied inconsistently here.
- *"Only a point light's falloff differs"* — **false, and it cannot be true. A rotation is an isometry, so it cannot change a distance.** With `P = (7,−3,4)`, `lightPos = (30,−22,22)`, `Rot = Euler(25,40,15)`: this port's `|lightPos − Rot·P| = 38.238780`, Pyre's `|Rot⁻¹·lightPos − P| = 38.238780`, difference **exactly 0.000e+00**.
- *The real cause*, now written down: **LR-1.6's light-position convention change** (radius-relative `gemLightDistance · R` → an absolute canvas position) plus the solid's centre translation. LR-1.6 already rules and prices that break, so the divergence was accounted for — D-6 simply attributed it to the wrong thing, and a later reader could have acted on the wrong explanation.

---

## 9. CONTRACT ERROR — `rimStrength` is wholly dead on a black ambient — **DECLARED, arithmetic NOT changed; owner's call flagged below**

**What it is.** LR-2.3 specifies `S += amb · rim` and the code implements it faithfully, so on a black ambient the Rim Strength control is inert: measured at the law level on a tilted normal (0.7, 0, 0.71414), `S = 0` **exactly** at every rim strength, against `S = 0.06361176` at ambient 0.2. "Only my lamps, no ambient" is an ordinary authoring choice.

**What I did — as instructed, made it honest rather than different.**

1. Added `ShaperLightProgram.hasInertRim` / `inertRimReason` / `inertRimNode` / `inertRimCount`, in `hasTooManyLights`' exact shape, raised at compile when a receiving layer has `rimStrength > 0` while the rig's ambient is black.
2. **Extended LR-7.2's mandated limitation text.** `ShaperLightRig.RimNeedsRelief` gains a second sentence covering the black-ambient case, and a new sibling `RimNeedsBlackAmbientRelief` carries that half alone, for a UI that knows which of the two states the layer is in. That sibling is the string the diagnostic carries verbatim (LR-7.1: one sentence per fact). The deviation from LR-7.2's authored wording is flagged in the code comment.

| | before | after |
|---|---|---|
| `S` at `rimStrength ∈ {0, 0.5, 1, 2.5, 5}`, black ambient | 0 exactly (undiagnosed) | 0 exactly, **diagnosed** |
| `S` at the same, ambient 0.2 | 0.00636 / 0.01272 / 0.03181 / 0.06361 | unchanged |
| compile diagnostic | none | `hasInertRim = True`, `inertRimCount = 2`, first node named, reason verbatim |
| false positives (lit ambient / rim 0 / receive off) | n/a | **0** — asserted, all three must be False |

**The leg.** New **LT-23**, asserting the arithmetic, the diagnostic, that it does *not* fire in the three non-cases, and that LR-7.2's sentence now covers both inert states.

> ### ⚠️ For the PM / owner to rule on — I did not change this unilaterally
>
> **I do think the arithmetic is questionable, and here is the alternative, per the brief's instruction to say so and let the PM rule.**
>
> The contract's argument for tinting rim by ambient (LR-2.3) is that "rim stands for grazing-angle light from everywhere" — which is exactly why it is *not* attenuated and *not* scaled by `intensityScale`. That reasoning is sound. But it makes ambient do **two** jobs: it is both the fill-light floor *and* the rim's colour and gate, so an author who wants a rim without a lifted shadow floor cannot have one, and the control reads as broken. That is the same "two meanings, one dial" defect LR-1.2 refuses on the light's specular colour and that `ShaperBlend`/`ShaperSweep` were split to remove.
>
> **Alternative A (smallest, recommended if it is changed at all):** tint rim by `max(amb, rimFloor)` where `rimFloor` is a new document-level rim colour defaulting to the ambient colour — one field, back-compatible when left alone, and it separates "how bright is the fill" from "what colour is the sky at a grazing angle".
> **Alternative B:** tint rim by the light rig's summed colour rather than the ambient. Physically closer, but it makes rim attenuate indirectly, which contradicts LR-2.3's own reasoning.
> **Alternative C (do nothing):** keep the arithmetic and rely on the diagnostic — which is what is shipped now.
>
> Either A or B is a contract amendment to LR-2.3, not a fix, so it is the owner's ruling. Shipping C.

---

## 10. GAP — LR-5.4 had no effective test — **CLOSED**

**What it was.** LR-5.4 ("a border is lit by its HOST's response block and its HOST's normal") is a substantive rule whose trap the contract names in its own text — "symmetry with BD-3.3 is the obvious wrong answer". The code was correct; nothing would have told you if it stopped being. A border is constructed in exactly three of the twenty-one light legs and none of the three could detect a host/border index swap: LT-3 compares a tiled render against a whole render (both sides move identically), LT-8 sets `receive = false` (the lit-border branch never executes), LT-12 compares shadows-off against shadows-on (the error cancels on both sides).

**What I did.** New **LT-20**. Give the border owner a *materially different* response block and normal from its host — different `intensityScale` (1.0 vs 0.1), different rim (0.9 vs 0.0), different `specularPower` (12 vs 220), different tint, and a normal pointing somewhere else entirely — then render twice: once with the border owner's own scene entries set to that different block, once with them set to the host's. The two must be **bit-identical over the border's pixels**, because LR-5.4 says the border's own entries are never read. Plus a control: change the **host's** entries instead, and the border's pixels must move.

| | before | after |
|---|---|---|
| legs asserting LR-5.4 | **0** | 1 (LT-20) |
| border pixels moving when the BORDER's own response/normal are replaced | untested | **0 / 1 184** |
| control: border pixels moving when the HOST's change | untested | **1 184 / 1 184** |

**Mutation proving the leg:** M7 + M7b, swapping `o` for `b` at `ShaperFillResolver.cs:1227-1229` — both halves, the response-block index and the normal slab index. LT-20 → **FAIL** at **1 148 / 1 184** border pixels moved.

---

## 11. GAP — `ShaperDocument` and `ShaperLayer` were never constructed — **CLOSED, with an architectural finding stated plainly**

**What it was.** The task's headline deliverable — "the document owns the lights", "the first document-level authored object in Shaper" — had **zero references outside its own declaration file**. `canvasWidth`, `canvasHeight`, `pixelSize`, `layers`, `phase01`, `seed` and `Grid()` were entirely unexercised and the document→compile path had never been run once.

**What I did.** Added the two runtime methods that path actually needs, and no more:

- `ShaperLightCompiler.CompileDocument(ShaperDocument)` — compiles the document's rig on the **document's** clock (LR-1.8's one exception to FC-1.4).
- `ShaperLightCompiler.BindLayer(document, layerIndex, prog, sampleCapacity, ownerCapacity)` — builds the paint pass's `ShaperLightScene` for one layer, compiling that layer's own response block and normal provider and copying in the shared rig. It has **no path to the authored rig at all**, which is LR-1.1's "no stage may add a light at render time" made structural rather than promised.

New **LT-21** builds a real `ShaperDocument` (96×72 @ pixelSize 1, phase 0.37, seed 909, one rig with two lights, four `ShaperLayer`s with materially different response blocks), renders every layer through that path, and asserts the per-layer responses take effect and that both the rig and the grid came from the document.

| | before | after |
|---|---|---|
| `ShaperDocument` references outside its declaration file | **0** | runtime `CompileDocument`/`BindLayer` + LT-21 |
| `ShaperLayer` references outside its declaration file | **0** | same |
| `Grid()`, `canvasWidth/Height`, `pixelSize`, `phase01`, `seed`, `layers` exercised | **no** | **yes**, all six |
| per-layer responses take effect (px differing from the lit layer) | untested | receive-off 2 188, in-shadow 2 188, rim 2 188 |
| luminance ordering | untested | in-shadow 350 080 < lit 645 940 < rim 660 401 |

> **The architectural finding, stated plainly rather than papered over.** **Wave 2 still has no document-level COMPOSITOR.** The document is now genuinely the source of the rig, the grid, the clock, the seed and each layer's response block, and that path is exercised end to end — but **stacking several layers into one picture is not built**, and it is not in this contract's scope (LR-1.1's Wave-2 content is "a canvas size, an ordered list of layer roots, and a `ShaperLightRig`"). LT-21 proves the document is **wired**, not that a document **renders**. That sentence is in the leg's own output as well as here, so it cannot be read off a green table as more than it is. Building the compositor is a real, unscheduled piece of work and I did not smuggle it in under a fix pass.

**Mutation proving the leg:** M6, making `BindLayer` read `document.layers[0]` for every index. LT-21 → **FAIL** — all four layers render identically (0 differing pixels, all luminances 645 940), `receiverCount` 4 instead of 3.

---

## 12. GAP — LR-6.4's glow path never executed, and no Pyramid cell — **BOTH CLOSED**

**What it was.** `edgeGlow` and `innerGlow` appeared exactly once in the 2 221-line audit, inside the shared `SolidDef` helper, both hardcoded to `new ZUIValue(0f)` — and every fixture and every cell was built through that helper. So `scene.glow` was all-zeros in all 21 legs and all 24 cells, the generator's halo/inner-glow block never ran, and `LightSample`'s `cr += scene.glow[t3 + 0]` added zero every time. Separately, Pyramid appeared in no contact-sheet cell.

**What I did.** Exercised it for real rather than declaring it out of scope. New **LT-22** asserts, on **every one of the six forms**: the glow sheet is written; the glow reaches `dst`; **D-7's ruling is measured** (the alpha channel is bit-identical with glow on and off, because hard coverage multiplies the outside spill by zero); and glow survives `receiveLighting = false`, because a glow is light rather than paint.

| form | glow sheet non-zero floats | encoded RGB moved | alpha moved (D-7: must be 0) |
|---|---|---|---|
| Box | 17 106 | 5 702 | 0 |
| Pyramid | 7 371 | 2 457 | 0 |
| Can | 13 494 | 4 498 | 0 |
| Orb | 8 484 | 2 825 | 0 |
| Gem | 4 164 | 1 388 | 0 |
| Ring | 4 752 | 1 584 | 0 |

Glow on a `receiveLighting = false` layer: **1 388 samples moved, summed delta +704.234**.

**Contact sheet re-rendered, 24 → 28 cells** (6 × 5, 618×516 px): **25 Pyramid** (the missing form), **26 Pyramid + halo** and **27 Orb + halo + inner glow** (the glow path, visible), **28 Gem with aspect+depth 0.4** (the declared inertness from finding 3, visibly identical to cell 08). Cell 28's reading is added to the legend's "cells whose correct reading is *nothing to see*" list, alongside 01/03, 13, 15, 17-vs-18 and 19-22.

**Before: 0 of 24 cells exercised glow, 0 showed a Pyramid. After: 2 of 28 exercise glow, 2 show a Pyramid, all six forms present.**

**Mutation proving the leg:** M8, replacing the `cr += scene.glow[...]` block with `+= 0f`. LT-22 → **FAIL** on all six forms — the glow sheet is still written (17 106 non-zero floats on Box) but **0 encoded pixels move**, which is exactly the shape of "the generator produces it and the resolver drops it".

---

## Mutations run

Every mutation was applied by exact-string replace, compiled, run, then reverted — and **every revert was proved by md5** against pre-mutation copies in `fix/orig/`. `ShaperFillResolver.cs` returned to `8d4f0e5d441ff8ba115a2ff69717f28d` after each batch, which is the same md5 the independent verifier recorded. A **forced recompile was waited on before every run**, in both directions; the verifier lost an hour to skipping that and reported a phantom failure.

| # | mutation | file | targeted leg | result |
|---|---|---|---|---|
| M1 | cap raised from `authored` instead of `enabled` | `ShaperLightCompiler.cs` | LT-11 | **FAIL** — `flag True/False, dropped 1/0`, `3/0`, `4/0`, `5/3` |
| M2 | `Dial(...)` removed from `c.rimPower` | `ShaperLightCompiler.cs` | LT-18 | **FAIL** — 268 800 non-finite floats, 89 600 garbage px |
| M3 | `invRangeSq` guard restored to `range > 1e-6f ? … : 0f` | `ShaperLightCompiler.cs` | LT-18 (d) | **FAIL** — 1 inversion, `range == 0 → L = 1.000000` |
| M4 | `blackAmbient` forced false | `ShaperLightCompiler.cs` | LT-23 | **FAIL** — `hasInertRim = False`, count 0 |
| M5 | Gem row deleted from `InertReason`'s `Aspect` case | `ShaperSolids.cs` | LT-19 | **FAIL** — 1 silently inert, diagnostic count 1 not 2 |
| M6 | `BindLayer` reads `layers[0]` for every index | `ShaperLightCompiler.cs` | LT-21 | **FAIL** — 0 differing px across all four layers |
| M7 | `bnslab` from `b` instead of `o` | `ShaperFillResolver.cs` | LT-20 | **FAIL** — 1 148/1 184 border px moved |
| M7b | `LightSample(..., b, ...)` instead of `o` | `ShaperFillResolver.cs` | LT-20 | (same batch as M7) |
| M8 | `cr += scene.glow[...]` → `+= 0f` | `ShaperFillResolver.cs` | LT-22 | **FAIL** — all six forms, 0 encoded px moved; **also** LT-19 caught it (12 silently inert) |
| M9 | shared unlit accumulate branch × 0.9 | `ShaperFillResolver.cs` | LT-8 | **FAIL** — 5 242/16 384 vs the golden, on both golden legs |
| M10 | Orb coverage made tilt-dependent | `ShaperSolids.cs` | LT-14b | **FAIL** — (a) 612 and (c) 612 |
| M11 | `new float[3]` inside `LightSample` | `ShaperFillResolver.cs` | LT-2 | **FAIL** — IL scan 1 token-confirmed alloc; heap 6–12 MB in 6/8 cases |

**Revert proved for every leg.** After batch 1's revert, LT-11 / LT-21 / LT-23 returned PASS. After batch 2's revert, LT-18 / LT-19 / LT-20 / LT-22 returned PASS. After batch 3's revert, the full 26-leg audit and the 48-leg regression both returned clean.

**One cross-batch observation worth keeping:** M8 (dropping the glow add) was caught by LT-19 as well as LT-22, because zeroing the glow makes the `EdgeGlow` and `InnerGlow` dials measure 0 pixels on all six forms and LT-19's two-sided assertion flags them as silently inert. Two independent legs catching one regression is the property the previous build lacked.

---

## Regression

Re-ran every zero-argument public `string` method on `ShaperFieldAudit` (`V*`), `ShaperFillAudit` (`FT*`) and `ShaperBorderAudit` (`BT*`) by reflection, after the final revert and a forced recompile.

**48 legs discovered, 48 unique, 48 `RESULT: PASS`, 0 `RESULT: FAIL`, 0 case-sensitive `FAIL` tokens** — 13 `ShaperFieldAudit`, 21 `ShaperFillAudit`, 14 `ShaperBorderAudit`.

**The `AmbiguousMatchException` hazard was checked specifically**, since it once took all 14 border legs down and compiling did not catch it. Every reflection-by-name-alone site was enumerated: `ShaperBorder.CompileStrip`, `ShaperFillDocument.Summary`, `ShaperFillResolver.PaintTile`, `ShaperLightLaw.Shade`. This pass added **no new overload of any reflected member** — `ShaperSolids.Compile` gained a *defaulted argument*, not an overload (1 declaration), and `PaintTile` is still a single declaration. `CompileDocument`, `BindLayer` and `InertReason` are new names nothing reflects on. All 14 BT legs passing confirms it empirically as well as structurally.

## Containment

- **`Runtime/Pyre/` and `Editor/Pyre/`: `git status --porcelain` returns 0 entries.** Not touched, as required — they are the reference, not the target.
- **The main copy `D:\UNITY\Laubrary Dev` contains no Shaper code**: no `Runtime/Shaper` and no `Editor/Shaper` directory exists there, and the only changes in its working tree under this task are inside `.agenthq/workspace/T-0108/` (the re-rendered contact sheet, `LIGHT-AUDIT.txt`, `IMPLEMENTATION-NOTES.md`, `FIX-REPORT.md`, `golden/`, `fix/`).
- **Worktree state:** the only changes under `Assets/Packages/Laubrary` are the two untracked Shaper trees. Nothing committed, nothing staged — the same disposition the three prior wave tasks left.

## Still unverified

Carried forward honestly rather than closed by assertion.

- **LT-17's contact sheet has still not been judged by the owner.** I looked at all 28 cells and they read as expected — the Pyramid renders as a real triangular solid with facet edge lines, cells 26/27 glow visibly, and cell 28 is indistinguishable from a neutral Gem, which is the declared-inertness point. That is a diagnostic reading, **not** an aesthetic verdict, and LT-17 still reports `RENDERED - NOT VERIFIED BY THE TABLE`.
- **The domain-reload determinism half of LT-4** is still not run. An eval executes inside one domain. Inherited unsolved from both prior passes.
- **No authored `.asset` was opened.** LR-4.4's "UNVERIFIED whether any shipped asset uses a spatial `gemSpecularFill`" stands, as does whether any document depends on the light position LR-1.6 breaks.
- **Burst is not demonstrated.** LT-13b checks the blittable signature structurally; no `[BurstCompile]` path exists to measure.
- **The verifier's largest hole is unchanged and unfixable here:** the whole `Runtime/Shaper/` and `Editor/Shaper/` tree is **untracked**, so there is no baseline to diff against and a silent edit elsewhere in `ShaperFillResolver.cs`'s other 1 200 lines could not be detected by reading. I mitigated it only for this pass, by md5-pinning that file against the verifier's confirmed-clean `.ORIG` before writing the golden and after every mutation revert. **Committing these trees would close it permanently and is the single highest-value process change available**, but committing was explicitly out of scope here.
- **LT-2's heap floor is run-to-run variable** (64 B, 512 B and 1 024 B per call observed across runs). This is inherent to a process-wide instrument in a live editor and is why the leg recalibrates every run rather than hard-coding a gate. The IL scan is what carries the assertion; the heap probe corroborates.
- **The cosmetic Pyre divergence the verifier logged is unaddressed** (the port lights facet edge lines through the full law including specular, where Pyre's line branch has no specular term). Magnitude below 5e-6 in linear space, invisible in the encoded bytes. Left as recorded, not ranked as a defect.
