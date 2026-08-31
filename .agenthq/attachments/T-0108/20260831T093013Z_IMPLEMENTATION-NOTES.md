# T-0108 — implementation notes

Wave 2, the light rig. Built against `LIGHT-RIG-CONTRACT.md` in the Shaper worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, **uncommitted and untracked**. Verified in the Shaper editor (PID 261208, port 7801, `Application.dataPath = D:/UNITY/Laubrary Dev - Shaper/Assets` — confirmed by probe before any compile result was trusted).

---

## 1. What was built

All new code is under `Assets\Packages\Laubrary\Runtime\Shaper\` and `Assets\Packages\Laubrary\Editor\Shaper\`. Nothing outside those two folders was touched. `Runtime/Pyre/` and `Editor/Pyre/` are byte-identical to the worktree's HEAD (`git status --porcelain` over both = 0 entries).

**Line counts corrected by the fix pass** — the originals were stale and understated the work (`ShaperSolids.cs` was reported as 700 when it was 897, `ShaperLightAudit.cs` as 1268 when it was 2221), and the table is presented as exhaustive. The numbers below are measured after the fix pass, which added ~1 090 lines of audit and ~330 of runtime.

| File | Lines | What it is |
|---|---|---|
| `Runtime/Shaper/ShaperLightRig.cs` | 369 | LR-1, LR-4, LR-7. `ShaperLightKind`, `ShaperLight`, `ShaperLightRig` (ambient + up to 8 lights + the four `const string` limitation sentences), `ShaperLightResponse`, `ShaperLayer`, **`ShaperDocument`** — the first document-level authored object in Shaper (LR-0.1). |
| `Runtime/Shaper/ShaperLightLaw.cs` | 306 | LR-2. `ShaperLightCompiled`, `ShaperLightRigCompiled` (8 named slots, no array), `ShaperResponseCompiled`, and `ShaperLightLaw.Shade` with **exactly** LR-2.2's signature. |
| `Runtime/Shaper/ShaperNormals.cs` | 164 | LR-3. `ShaperNormalKind` (`Constant = 0`, `Profile` reserved and commented for T-0109), `ShaperNormalOp` carrying BC-3.6's three named dials with provenance, `ShaperNormals.FillTile`. |
| `Runtime/Shaper/ShaperLightCompiler.cs` | 622 | The compile stage + diagnostics (`ShaperLightProgram`) + the host-owned `ShaperLightScene`. |
| `Runtime/Shaper/ShaperSolids.cs` | 1058 | LR-6. The Solids port as a **generator publishing coverage + normal**: Box, Pyramid, Can, Orb, Gem, Ring. |
| `Runtime/Shaper/ShaperFillResolver.cs` | 1222 → 1457 | LR-5. Lighting placed inside `PaintTile`, per owner, before `coverageEff` and before premultiplication. **This is the only pre-existing file modified.** |
| `Editor/Shaper/ShaperLightAudit.cs` | 3311 | LT-1 .. LT-17. |

`ShaperFillResolver.cs` changes, exhaustively: **one optional parameter added to `PaintTile`** (`ShaperLightScene scene = null`); a branch in step 1 that lets a Solids generator replace the shape stage for one owner and fills the normal sheet otherwise; the lit/unlit branch in the owner accumulate block; the same branch in the border block; and one new private helper, `LightSample`.

> **A second overload was the first attempt and it broke a shipped audit.** `ShaperBorderAudit.BT14_NoAllocation` reflects on the method by name alone (`ShaperBorderAudit.cs:1707`, `typeof(ShaperFillResolver).GetMethod("PaintTile")`), and a second overload makes that throw `AmbiguousMatchException` — which took the whole border audit down, 14 legs, measured on the first regression run. Replaced with one method and a defaulted argument, which keeps every existing caller *and* every existing reflection probe working untouched. This is the concrete reason the regression pass exists, and it would not have been caught by compiling.

### The shape of the integration

`ShaperLightScene` is host-allocated and host-owned on BC-3.7f's terms. It carries the compiled rig, a per-owner response block, a per-owner normal provider, an optional per-owner compiled Solids generator with prebuilt geometry, and the four per-owner sheets (`normal`, `lineMask`, `glow`, `pointZ`). **Passing `null` is the pre-T-0108 build, bit for bit**, and so is a scene whose owner has `receive == 0` and no Solids overlay — both take the *original* accumulate expression by an explicit branch rather than relying on `x*1 + 0 == x`. LR-4.3 says "bit for bit" and that is the only way to mean it (same structural-identity discipline as `ShaperEvaluator`'s Sweep early-out).

A Solids owner's geometry is built once at compile (`ShaperSolids.Build`) and the tile pass allocates nothing. The generator **replaces the shape stage for that owner only** — everything downstream (claim, exclusivity partition, fill, border, composite) is identical to any other owner's, which is what LR-6.1's "goes through the ordinary fill and light pipeline like every other generator" has to mean to mean anything.

---

## 2. Deviations from the contract

Nine. Each is flagged in a code comment or in the audit's own report text naming the LR/LT clause, per the brief.

### D-1 — LT-1's "bit-identical, 512/512" is unachievable against LR-3.5 *(contract conflict)*

LR-3.5 makes **unit output a provider contract**, so `ShaperNormals` re-normalises its authored direction. An Orb's `N = P/R` is unit only to float precision. So driving the Constant provider to the Orb's own normal produces a sheet that differs in the last bit at 110 of 512 cases (worst componentwise 1.19e-7), and a `pow(nh, 24)` specular amplifies that to 4.9e-6 in the law's output. **The two clauses are in genuine conflict and LR-3.5 is the one worth keeping.**

Implemented instead, and reported as four measured legs: (i) the two providers agree to ≤1e-6; (ii) **where the two providers wrote bit-identical normals, the law's six outputs are bit-identical, 402/402**; (iii) where they did not, the worst total error is 4.917e-6 (bound 1e-4); (iv) the same float triple fed twice is bit-identical 512/512 — the law is a pure function of its arguments. **Recommend fixing LT-1's wording, not the code.**

### D-2 — LT-14c's stated angles are arithmetically impossible

Part 8 expects Ring's covered count "> 0 through 89.4° and exactly 0 at 89.6°". The ported guard is `|cos tilt| < 0.02` (`PyreRenderer.cs:4633`) and **`acos(0.02) = 88.854°`**, so 89.0° and 89.4° are *already* zero. The sweep uses the correct angles ({80, 87, 88, 88.8, 88.9, 89.4, 89.6}) and asserts the same defect: non-zero through 88.8°, exactly 0 from 88.9°, 100% drop in one step. Same defect locked; only the angles corrected.

### D-3 — LT-2's stated instrument does not exist in this runtime

`GC.GetTotalAllocatedBytes(precise: true)` is .NET Core 3.0+ and is absent from this editor's profile (measured: `error CS0117`). Substituted `GC.GetAllocatedBytesForCurrentThread()`, bound through a delegate so the probe allocates nothing at the call site. **Calibration showed it is BLIND below 4 MB on this Mono runtime** (it updates per allocation *context*, not per object), and `GC.GetTotalMemory(false)`'s floor is 512 KB — reproducing FT-9's finding. So the leg was restructured: 65 repetitions per case raise a violation of the stated mutation's magnitude (120 000 B/call) to ~7.8 MB, above both floors; the calibration runs **before** the measurements and its threshold is stated before the numbers; and the verdict is carried by a **token-confirmed** IL scan.

### D-4 — the IL scan had to resolve its tokens, not count bytes

A byte-frequency scan for `newobj`/`newarr`/`box` reported 1 hit in `PaintTile` (a 2823-byte method dense with 4-byte tokens). Every hit is now confirmed by resolving its operand as a metadata token — a real `newobj` resolves to a constructor, a real `newarr`/`box` to a type. Raw 1, **confirmed 0**. Both numbers are reported; hiding the raw one would hide the instrument's imprecision.

### D-5 — LT-14a had to distinguish a shared edge from an overlap

`InTri` is inclusive (`w >= 0`, ported verbatim from `:4936`), so a sample lying exactly on the diagonal that splits a box quad is genuinely inside both triangles. Measured at rotation (0,0,0): Box 2, Can 2, Gem 3 — **every one on a seam**. That is a tie the generator's `break` resolves deterministically, not a convexity violation. The audit now counts **strictly-interior** containment for the verdict and reports the seam count separately. Removing the backface cull still trips it, because a back face covers the *interior*.

### D-6 — Orb rotates its published NORMAL, not the light *(the one place the port diverges from Pyre)*

Pyre rotates the **light** by the inverse of the sphere's rotation (`:4442-4455`). Under a shared rig that is a **per-particle private light, which LR-1.1 forbids outright** ("no light is owned by a generator"). This port takes the other side of Pyre's own identity `N·(Rot⁻¹·L) == (Rot·N)·L` and rotates the published normal forward, leaving the one shared light where the author put it.

**CORRECTED BY THE FIX PASS (T-0108 verification finding 7). The ruling above is right and the code is right; both consequences this note originally claimed were wrong, and it stated them as measured facts.** What it said was: "Pyre also rotates the light for the *attenuation* term (measuring distance from the rotated surface point); this generator measures it from the true canvas position of the sample... only a *point* light's falloff differs, and for a directional light the two are bit-identical because there is no falloff." Both halves are false.

*Claim A — "for a directional light the two are bit-identical because there is no falloff" — is false.* Measured over 3 600 normals spread across the sphere, with rotation (25, 40, 15) and a unit light direction: `(Rot*N).L` vs `N.(Rot^-1*L)` is **bit-identical in only 1 895 of 3 600 cases (53 %), worst |difference| 5.960e-08**. The identity is exact in real arithmetic and *not* in floats — which is precisely the argument D-1 makes correctly about LT-1, applied inconsistently here.

*Claim B — "only a point light's falloff differs" — is false, and it cannot be true.* **A rotation is an isometry, so it cannot change a distance.** Measured directly: with `P = (7,-3,4)`, `lightPos = (30,-22,22)` and `Rot = Euler(25,40,15)`, this port's `|lightPos - Rot*P| = 38.238780` and Pyre's `|Rot^-1*lightPos - P| = 38.238780` — difference **exactly 0.000e+00**. The normal-forward vs light-backward choice has **no effect whatsoever on point-light attenuation**.

*The real reason the port's Solids falloff diverges from Pyre's*, which is what this note should have said: **LR-1.6's light-position convention change.** Pyre's solid light is `ldist = layer.gemLightDistance * R` with `R` the particle's own evaluated radius (`PyreRenderer.cs:4265`) and a falloff range of `(gemLightDistance + 1.2) * R` (`:4267`) — a per-particle, radius-relative position that is not a position in canvas space at all. This port promotes it to an **absolute canvas position on the shared rig**, and that, together with the solid's centre translation, is where every falloff difference comes from. LR-1.6 rules and prices that break explicitly, so the divergence was already accounted for; D-6 attributed it to the wrong cause and a later reader could have acted on the wrong explanation.

**Nothing in the code changed as a result of this correction.** The Orb ruling stands on LR-1.1 alone ("no light is owned by a generator"), which is sufficient on its own and was always the real argument.

### D-7 — the outside halo is not rendered

LR-6.4 says two things that conflict for one case: "coverage stays HARD (0 or 1) for all five members" **and** "halo and inner glow stay in the generator". Pyre's halo *outside* the silhouette is emitted as a soft alpha fragment (`:4396`), which is a soft-coverage feature. Under hard coverage it is multiplied by `ce = 0` and vanishes. **Implemented: the halo and inner glow inside the silhouette, as an additive unlit triple; the outside spill is not rendered.** It becomes available for free when T-0114 softens coverage. Flagged here rather than silently dropped.

### D-8 — LT-10 measured as two renders, not two owners on one node

Part 8 says "one node, two owners". Two owners on one node necessarily composite into each other, which confounds the ratio with the composite. Measured instead as two separate renders of the same node, one `Over` and one `Add`. The assertion is identical (ratio exactly 2.0, and the `Add` result exactly equals the raw linear albedo) and the measurement is cleaner.

### D-9 — LT-8 compares against the live unlit path, not a stored golden

~~Part 8 says "compare against the T-0107 golden". No stored golden exists in this worktree. The leg compares against `PaintTile` with a **null scene** — the same method with the lighting branch structurally absent, i.e. the actual pre-T-0108 code path. That is strictly stronger than a stored file: a golden can go stale, the live unlit path cannot.~~

**WITHDRAWN BY THE FIX PASS (T-0108 verification finding 3). The defence above is false and the leg has been rebuilt against a stored golden.** "Strictly stronger" was not merely optimistic, it was **circular**: with `receive == 0` and no Solids overlay, `doLight` and `solidOwner` are both false (`ShaperFillResolver.cs:1089-1090`) and the receive-off render takes the *identical* `if (!doLight && !solidOwner)` block that the null-scene render takes. Both sides of the comparison executed the same code, so any regression in that shared block moved both sides equally and the difference stayed 0.

Proved by mutation, twice. Multiplying the shared unlit accumulate branch (`ShaperFillResolver.cs:1121-1123`) by `0.9`: the old `LT8_ReceiveOffIsBitIdentical` **still passed**, while `LT10_AddIsNotLit` and — pointedly — *T-0107's* `ShaperBorderAudit.BT11_Ordering` both failed. A leg from the previous wave detected the unlit-path regression that the leg written specifically to guard that path did not.

**What the leg does now.** `ShaperLightAudit.WriteGoldens()` wrote `.agenthq/workspace/T-0108/golden/lt8-unlit-128x128.rgba` (65 536 B, 128x128 RGBA8) from a build whose `ShaperFillResolver.cs` md5 is `8d4f0e5d441ff8ba115a2ff69717f28d` — byte-identical to the copy the independent verifier confirmed clean. LT-8 now compares **both** the null-scene render and the receive-off render against those bytes, and keeps the live null-scene comparison as an explicitly-labelled WEAK third leg (it still proves the narrower "receive-off ≡ null-scene within this build", which is all it ever proved). Re-running the same 0.9 mutation now fails LT-8 at **5 242 / 16 384 pixels on both golden legs**.

The staleness risk D-9 raised is real and is handled rather than argued away: the golden is regenerated only by `WriteGoldens()`, which `RunAll()` never calls and which is deliberately named outside the `LT*` prefix so no reflection sweep can invoke it by accident. A regeneration is a visible act with a diff. The control leg is unchanged and still live (measured 5 054 pixels this run).

---

## 3. Mutations run to prove each leg falsifiable

Every one was applied to the source, compiled clean, run, observed, and **reverted** (verified: 0 `.bak` files, 0 `MUTATION` markers outside the audit's own prose, `git status` for Pyre = 0).

| # | Leg | Mutation applied | Observed |
|---|---|---|---|
| M1 | **LT-1b** | Added `ShaperSolids.MutantShade(in ShaperLightRigCompiled, …)` calling the law — a second shading site in the generator | **FAIL**: (b) callers 2, expected 1 `[LightSample, ShaperSolids.MutantShade]`; (c) 1 Solids method touching a rig type, expected 0 |
| M2 | **LT-3** | `LightSample`: surface point from a tile-local index (`i % width`) instead of the absolute one | **FAIL**: Constant 652/1470 pixels differ, Solids 169/1470 |
| M3 | **LT-7** | `Shade`: rim computed from the first light's direction instead of `V` | **FAIL**: lights 2–8 give a non-zero rim at 16384/16384 samples each. Light 1 (yaw 0, pitch 0) is on-axis and passes — **exactly the "catches it on light 2" the contract predicted** |
| M4 | **LT-9** | `PaintTile`: a light-dependent factor allowed into the alpha accumulation (LR-0.8's trap) | **FAIL**: (b) 5656/16384 samples below coverage, deficit 2217.472; (c) alpha differs lit vs unlit at 5664/16384; (d) 3964/16384 |
| M5a | **LT-12 (a)** | `CompileResponse`: `receiveShadows` halves `intensityScale` — shadows do something | **FAIL**: 4644/16384 pixels differ between shadows-off and shadows-on |
| M5b | **LT-12 (b)** | *First attempt:* paraphrased the `const string` itself | **PASSED — the test was vacuous.** Both sides of the identity check move together. Leg (b) was then strengthened with a content assertion (the sentence must still say *saved* / *not computed* / *straight down* / *ray*) |
| M5b′ | **LT-12 (b)** | Paraphrased the diagnostic at the **assignment site** | **FAIL**: both the verbatim check and the new content check |
| M5c | **LT-12 (d)** | `unimplementedShadowCount = 1` instead of `++` — record only that *some* asked | *First attempt* **PASSED** on the single-layer fixture (1 == 1 — FT-8b's exact failure mode, reproduced). Fixture rebuilt with **four** layers, three asking; mutation then **FAIL**: count 1, expected 3 |
| M6 | **LT-16** | `LightSample`: a Solids owner lit in its **own** frame (surface point offset by the solid's centre) — the analogue of `ldist = gemLightDistance · R` | **FAIL**: leg (c) difference 8.897e-2 against a 1e-4 bound |
| M7 | **LT-15** | `BuildFacet`: `nrm = nrm.normalized` omitted (`PyreRenderer.cs:4236`) | **FAIL**: Box 169766/524288 non-unit, worst ‖N‖−1 = 3.599e3; all four facet forms fail |
| M8 | **LT-10** | `PaintTile`: the `!add` guard removed from the lighting call | **FAIL**: ratio 1.000 instead of 2.000 |
| M9 | **LT-5a** | `ShaperLightCompiler`: light colour not sRGB-decoded | **FAIL**: destination 0.50196 instead of 0.21586, **encoded byte 188 — exactly the 188 the contract predicted** |

Two of these (M5b, M5c) found **real weaknesses in my own tests** before the verifier could. Both are recorded above rather than quietly fixed.

---

## 4. Everything I did NOT do

- **No UI, no `[MenuItem]`, no `EditorWindow`, no menu entry of any kind.** Wave 2 adds none, and this project has a standing rule against unrequested ones.
- **No `.asset` was created, read or authored.** LR-4.4's "UNVERIFIED whether any shipped asset uses a spatial `gemSpecularFill`" is **still unverified** — I did not open one either.
- **Pyre was not modified.** Not one line. The Solids work is a re-expression into new files, reading `PyreRenderer.cs` as the reference.
- **No commit, no stage, no push.** Everything is uncommitted and untracked, as T-0105/0106/0107 left theirs.
- **`ZUI`, `ZuiFill`, `ZUIValue`, `ZuiGradient` used by value only** — no file outside `Runtime/Shaper/` and `Editor/Shaper/` was edited.
- **The `Profile` normal provider is NOT built.** It is a reserved, commented enum slot for T-0109, exactly as LR-3.2 requires.
- **Shadows compute nothing.** Recorded, persisted, diagnosed; absent from `ShaperResponseCompiled` so the law cannot see them.
- **No automatic converter for the LR-1.6 light-position break.** The contract says none is proposed, and none was written.
- **The `lineAmbientBoost` floor** that would restore Pyre's `k = clamp(0.25 + lit, 0, 1.15)` is **named as deferred and not built**, per LR-6.3.
- **The halo's re-expression as an outward border** (LR-6.4's follow-up) is not attempted.
- **Soft coverage for the solids** is deliberately not implemented, including the Orb's free softening — LR-6.4 assigns all five together to T-0114.
- **LT-4's across-a-domain-reload leg was NOT RUN** and is reported as not-verified: an eval executes inside one domain. The within-session and `MinMax` legs — the ones that would catch a stray `Random` — did run.
- **No `[BurstCompile]`.** LR-2.2's blittable signature keeps the door open; nobody walked through it, and the contract's Part 9 already says the Burst claim is inherited rather than demonstrated.
- **`ShaperDocument` is not wired to any renderer.** It is the authored type LR-1.1 requires; the audit drives the compiler and the scene directly, which is how a document would.

---

## 5. Verified vs not verified

**Verified by measurement** — 20 asserting legs PASS, 0 FAIL; every leg reports measured-vs-expected; 12 mutation runs confirm falsifiability; `EditorUtility.scriptCompilationFailed == false` with **zero fresh `error CS` lines** (read from a byte offset marked *before* the refresh, because `Editor.log` is cumulative and grepping the whole file misses fresh errors); every new type resolves by reflection.

**Verified by eye** — the LT-17 contact sheet was rendered and looked at. Its non-obvious cells were then also checked **numerically** rather than by eye (label plate excluded): cell 01 ≡ cell 13 at 0 differing pixels (rim on a flat normal is exactly nothing); the four shadow-flag cells 19–22 are identical to each other at 0 differing pixels; cell 15 (`Add`) is brighter than cell 14 (`Over`) at the centre; the rim sweep 09→12 differs at 3120 pixels; Silhouette vs Solids differ at 3604.

**Regression** — `ShaperFieldAudit` 13, `ShaperFillAudit` 21, `ShaperBorderAudit` 14 = **48 `RESULT: PASS`, 0 `RESULT: FAIL`, 0 case-sensitive `FAIL` tokens** (9 lowercase "fail" occurrences are prose). Full output in `REGRESSION.txt`. Re-run *after* the `PaintTile` fix, not before.

**A note on running these** — the Unity CLI's reply timeout is a fixed 30 s that `--timeout` does not raise, and each of the three prior audits exceeds it. The pattern that works is the one T-0106/T-0107 established: have the script APPEND to a file and poll the file. Two further traps found here and worth recording: (1) firing several `eval_file` calls concurrently at one editor **wedges it** (`unity status` reported port 7801 `unreachable`; it recovered on its own in ~5 s once the queue drained) — the project's standing "never run two agents against one editor" rule applies to two commands as well; (2) a runner that reads *which* audit to run from a scratch file races with its own queued predecessors, so each audit needs its own script file with the name baked in.

**NOT verified** — the domain-reload determinism leg; any authored `.asset`; Burst; whether the visual result is *aesthetically* right (Wave-2 Silhouette lighting is structurally complete and visually thin by design, LR-3.2, and that is the trade the wave is making); and the contact sheet has not been looked at by the owner.


---

## 6. The fix pass (T-0108, after independent verification)

An independent verifier holed this build with twelve findings. All twelve are addressed; the full account — what each was, what was done, before/after numbers, and the mutation proving each new leg — is in **`FIX-REPORT.md`**, which is the authoritative record. In summary:

- **Four real defects fixed in the runtime**: every dial on the rig, the response block and the Solids generator is now clamped at compile (a negative `rimPower` wrote 19 200 non-finite floats and 6 400 black garbage pixels; now 0 across 1 428 degenerate cases); the eight-light cap counts ENABLED lights, not authored ones (it was wrong in three of five configurations); every Solids dial is now either live or DECLARED inert with a reason (five were silently dead); and the point-light `range` dial no longer inverts near zero (it snapped from 0 to full, unattenuated reach at 1e-6 and below).
- **Three dead tests rebuilt so they can fail**, each proved by mutation: LT-8 against a stored golden, LT-2 against a calibrated instrument with a stated numeric floor, LT-14b against an independently computed disc plus a live control.
- **Three gaps closed** with new legs: LR-5.4's border-lit-by-host rule, the `ShaperDocument`/`ShaperLayer` path, and LR-6.4's glow — plus a Pyramid cell and two glow cells on the contact sheet.
- **Two records corrected**: D-6's justification (above) and D-9's defence (above).
- **One thing deliberately NOT changed**: rim's ambient tint. It makes `rimStrength` wholly dead on a black ambient, which is real, but it is what LR-2.3 specifies. It is now DECLARED (a compile diagnostic plus an extended LR-7.2 sentence) and flagged for the owner in `FIX-REPORT.md` with the alternative, rather than changed unilaterally.

New audit legs: **LT-18** (degenerate dials never go non-finite, incl. the range sweep), **LT-19** (every Solids dial live or declared inert), **LT-20** (LR-5.4), **LT-21** (the document path), **LT-22** (the glow path), **LT-23** (rim inert on a black ambient, declared). New runtime API: `ShaperLightCompiler.CompileDocument`, `ShaperLightCompiler.BindLayer`, `ShaperSolids.InertReason`, `ShaperSolidDial`, `ShaperLightRig.RimNeedsBlackAmbientRelief`, and the `hasInertDial` / `hasInertRim` / `enabledLightCount` / `authoredLightCount` diagnostics on `ShaperLightProgram`.

**Final counts: 26 asserting light legs, 26 `RESULT: PASS`, 0 `RESULT: FAIL`, 0 case-sensitive `FAIL` tokens; regression 48/48 across `ShaperFieldAudit` (13), `ShaperFillAudit` (21) and `ShaperBorderAudit` (14), 0 `FAIL` tokens.** `Runtime/Pyre/` and `Editor/Pyre/` remain at 0 `git status` entries.
