# T-0108 — implementation notes

Wave 2, the light rig. Built against `LIGHT-RIG-CONTRACT.md` in the Shaper worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, **uncommitted and untracked**. Verified in the Shaper editor (PID 261208, port 7801, `Application.dataPath = D:/UNITY/Laubrary Dev - Shaper/Assets` — confirmed by probe before any compile result was trusted).

---

## 1. What was built

All new code is under `Assets\Packages\Laubrary\Runtime\Shaper\` and `Assets\Packages\Laubrary\Editor\Shaper\`. Nothing outside those two folders was touched. `Runtime/Pyre/` and `Editor/Pyre/` are byte-identical to the worktree's HEAD (`git status --porcelain` over both = 0 entries).

| File | Lines | What it is |
|---|---|---|
| `Runtime/Shaper/ShaperLightRig.cs` | 322 | LR-1, LR-4, LR-7. `ShaperLightKind`, `ShaperLight`, `ShaperLightRig` (ambient + up to 8 lights + the four `const string` limitation sentences), `ShaperLightResponse`, `ShaperLayer`, **`ShaperDocument`** — the first document-level authored object in Shaper (LR-0.1). |
| `Runtime/Shaper/ShaperLightLaw.cs` | 272 | LR-2. `ShaperLightCompiled`, `ShaperLightRigCompiled` (8 named slots, no array), `ShaperResponseCompiled`, and `ShaperLightLaw.Shade` with **exactly** LR-2.2's signature. |
| `Runtime/Shaper/ShaperNormals.cs` | 162 | LR-3. `ShaperNormalKind` (`Constant = 0`, `Profile` reserved and commented for T-0109), `ShaperNormalOp` carrying BC-3.6's three named dials with provenance, `ShaperNormals.FillTile`. |
| `Runtime/Shaper/ShaperLightCompiler.cs` | 336 | The compile stage + diagnostics (`ShaperLightProgram`) + the host-owned `ShaperLightScene`. |
| `Runtime/Shaper/ShaperSolids.cs` | 700 | LR-6. The Solids port as a **generator publishing coverage + normal**: Box, Pyramid, Can, Orb, Gem, Ring. |
| `Runtime/Shaper/ShaperFillResolver.cs` | 1222 → 1439 | LR-5. Lighting placed inside `PaintTile`, per owner, before `coverageEff` and before premultiplication. **This is the only pre-existing file modified.** |
| `Editor/Shaper/ShaperLightAudit.cs` | 1268 | LT-1 .. LT-17. |

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

**The one consequence, named rather than discovered:** Pyre also rotates the light for the *attenuation* term (measuring distance from the rotated surface point); this generator measures it from the **true canvas position** of the sample, which is what LR-1.5 requires of every surface point. Diffuse direction, specular direction and rim are identical; only a *point* light's falloff differs, and for a directional light the two are bit-identical because there is no falloff.

### D-7 — the outside halo is not rendered

LR-6.4 says two things that conflict for one case: "coverage stays HARD (0 or 1) for all five members" **and** "halo and inner glow stay in the generator". Pyre's halo *outside* the silhouette is emitted as a soft alpha fragment (`:4396`), which is a soft-coverage feature. Under hard coverage it is multiplied by `ce = 0` and vanishes. **Implemented: the halo and inner glow inside the silhouette, as an additive unlit triple; the outside spill is not rendered.** It becomes available for free when T-0114 softens coverage. Flagged here rather than silently dropped.

### D-8 — LT-10 measured as two renders, not two owners on one node

Part 8 says "one node, two owners". Two owners on one node necessarily composite into each other, which confounds the ratio with the composite. Measured instead as two separate renders of the same node, one `Over` and one `Add`. The assertion is identical (ratio exactly 2.0, and the `Add` result exactly equals the raw linear albedo) and the measurement is cleaner.

### D-9 — LT-8 compares against the live unlit path, not a stored golden

Part 8 says "compare against the T-0107 golden". No stored golden exists in this worktree. The leg compares against `PaintTile` with a **null scene** — the same method with the lighting branch structurally absent, i.e. the actual pre-T-0108 code path. That is strictly stronger than a stored file: a golden can go stale, the live unlit path cannot. A control leg asserts the same rig with `receive` ON differs at >1000 pixels, so the leg is not vacuous (measured: 12 799).

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
