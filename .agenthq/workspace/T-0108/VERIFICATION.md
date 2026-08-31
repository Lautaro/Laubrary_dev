# T-0108 — independent verification of the light rig

Verifier pass over Wave 2 (`LIGHT-RIG-CONTRACT.md`, `IMPLEMENTATION-NOTES.md`, `LIGHT-AUDIT.txt`) and the code in the Shaper worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`. Editor identity confirmed before any result was trusted: `Application.dataPath = D:/UNITY/Laubrary Dev - Shaper/Assets`, `scriptCompilationFailed = False`, `ShaperLightLaw` resolving in exactly one assembly (`com.Lautaro-Arino.Laubrary.Shaper`).

The audit ships 20 asserting legs, all PASS. **Ten findings survived verification**, of which four are real defects that change output or lie to the author, three are tests or instruments that cannot do the job their name claims, and three are untested paths. Every finding below carries the measurement that establishes it.

All probe scripts and raw outputs are under `.agenthq/workspace/T-0108/verify2/`. Every source mutation used was reverted and the revert proved by md5 against a pre-mutation copy.

---

## Ranked findings

### 1. REAL DEFECT — a negative `rimPower` writes NaN and Infinity into the destination buffer

`ShaperLightCompiler.CompileResponse` (`ShaperLightCompiler.cs:179-182`) applies **no clamp of any kind** to `rimStrength`, `rimPower`, `specular`, `specularPower` or `intensityScale`. Only `range` is guarded (`:143`). The rim block in `ShaperLightLaw.Shade` then evaluates `Mathf.Pow(1f - ndv, resp.rimPower)`; on a flat normal `ndv` clamps to exactly 1, so the base is exactly 0 and a negative exponent yields `+Infinity`. The rim is then tinted by ambient — `sr += rig.ambR * rim` — so a **black ambient turns that Infinity into NaN** via `0 * Inf`.

Measured at the law level, and then through a full 128×128 render of a rect fill:

| response | `dst` NaN floats | `dst` Inf floats | garbage encoded px |
|---|---|---|---|
| `rimPower = 2.2` (baseline) | 0 | 0 | 0 |
| `rimPower = -1`, ambient 0.2 | 0 | **19 200** | 0 |
| `rimPower = -1`, ambient 0.0 | **19 200** | 0 | **6 400** |

19 200 = 6 400 covered samples × 3 colour channels. The 6 400 encoded pixels come out `r == 0` with non-zero alpha — black garbage with no diagnostic.

This is the same shape as T-0106's NaN veil, and the build has *already decided* it matters: `ShaperFillResolver.cs:1105` (and `:1249` for borders) guards coverage with `if (!(ce > 0f)) continue;`, chosen over `ce <= 0` precisely because "the two differ on NaN, and only the first rejects it", with a comment saying the resolver "must also never render NOT-A-NUMBER, which encodes to a garbage byte with no diagnostic at all". That guard covers NaN arriving from *coverage*, upstream of the law. **Nothing guards NaN produced by the lighting law itself**, which enters at `LightSample`'s `cr = ar * lr + sr` (`:1389-1391`), downstream of the guard.

No leg looks for one either: every `float.IsNaN` check in `ShaperLightAudit.cs` is inside LT-15 (`:1827`, `:1880`) and inspects the **normal sheet** only — the provider's output — never `dst` and never the law's. And `rimPower` takes no value but `2.2` anywhere in the 2 221-line audit.

Reachability is not "an author types −1". `rimPower` is a `ZUIValue` sampled through `ShaperValue.Sample` (`ShaperValue.cs:26-40`), which returns `raw * v.Multiplier()` with no floor, and `Curve` / `Oscillation` modes evaluate an `AnimationCurve` that overshoots below zero between keys as a matter of routine.

Two lesser degeneracies from the same missing clamp, both measured at the law level:

- `specularPower = -4` at a grazing normal returns `S = 8.999998E+11`. Finite, so it encodes to a clamped 255, but it is an unbounded blow-out from a dial with no stated legal range.
- `rimPower = 0` gives `Mathf.Pow(0, 0) = 1`, so **rim is `rimStrength` on a flat normal, not zero** (measured `S = 1.1` against a baseline `0.9`). LT-7's headline — "rim is exactly zero on a flat normal" — holds only for `rimPower > 0`, and LT-7 sweeps eight *light directions* while holding `rimPower` fixed at 2.2, so it cannot see this.
- A related inversion in the one dial that *is* guarded. `ShaperLightCompiler.cs:143` reads `c.invRangeSq = range > 1e-6f ? 1f / (range * range) : 0f;`, and `invRangeSq == 0` means `atten == 1` at every distance. So a point light's range dial reverses at the bottom of its travel — measured at canvas (0,0) with the light at (0,0,40):

  | range | 40 | 1 | 1e-2 | 1e-4 | 1e-5 | **1e-6** | 1e-7 | **0** |
  |---|---|---|---|---|---|---|---|---|
  | resulting `L` | 0.500000 | 0.000625 | 0 | 0 | 0 | **1.000000** | **1.000000** | **1.000000** |

  The darkest reachable setting is `1e-5`; at `1e-6` and below the light snaps to full, unattenuated, infinite reach. The guard exists to avoid a divide-by-zero and picks the value that means "no falloff" where it should mean "no reach". Extreme-value only, and no leg covers it.

**Degeneracies I probed that are safe**, stated so the finding is not read as broader than it is: a point light exactly at the sample point (`dist = 0`) gives `L = 0` with no NaN, because the `1e-4` direction clamp drives `ldir` to `(0,0,0)` and `ndl` to 0; and eight coincident point lights at `dist = 0` with `range = 0`, `intensity = float.MaxValue`, a zero normal `(0,0,0)` and `specularPower = 0` together produce a finite `L = 0.1, S = 0.1`. The rim exponent is the only non-finite vector I found.

### 2. REAL DEFECT — the eight-light cap diagnostic counts authored lights, not enabled ones, and fires when nothing was dropped

`ShaperLightCompiler.cs:153-160` raises the refusal from `authored = list.Count`, which includes disabled entries, while the compile loop at `:100` skips `!l.enabled`. The two never agree once anything is switched off.

| rig | enabled | actually lit | truly dropped | `hasTooManyLights` | `tooManyLightsCount` |
|---|---|---|---|---|---|
| 11 authored, none disabled (LT-11's fixture) | 11 | 8 | 3 | True | 3 ✓ |
| 9 authored, the 9th disabled | 8 | 8 | **0** | **True** | **1** ✗ |
| 11 authored, 5 disabled | 6 | 6 | **0** | **True** | **3** ✗ |
| 12 authored, first 8 disabled | 4 | 4 | **0** | **True** | **4** ✗ |
| 8 authored, none disabled (control) | 8 | 8 | 0 | False | 0 ✓ |

In three of five configurations the tool tells the author lights were dropped when every enabled light was lit. The sentence is wrong twice over in the last row: it reads *"the rig holds 12 lights; only the first 8 are lit"* when four are lit and they are lights 9–12, not the first 8.

LT-11 cannot catch this — its single fixture is 11 lights with none disabled, i.e. the one configuration where counting authored entries happens to give the right answer. This is the T-0107 BT-5 pattern (assert a value on the one case where it is true) reproduced exactly.

That this is an oversight rather than a deliberate convention is settled by the sibling diagnostic in the same file: `ShaperLightCompiler.Finish` (`:226-238`) builds its sentence from `prog.rig.count` — the *enabled and compiled* count — and is correct. Two diagnostics, one file, only one of them counting the right thing.

### 3. DEAD TEST — LT-8 cannot fail on the path it names, proved by mutation

LT-8 is titled "receiveLighting = false is bit-identical to **the pre-lighting build**". D-9 defends comparing against a live null-scene render instead of a stored golden, arguing it is "strictly stronger than a stored golden: a golden can go stale, the live unlit path cannot." **That argument is false**, because both sides of the comparison execute the same branch of the same method: with `receive == 0` and no Solids overlay, `doLight` and `solidOwner` are both false (`ShaperFillResolver.cs:1089-1090`) and the render takes the identical `if (!doLight && !solidOwner)` block that the null-scene render takes.

Proof by mutation. I multiplied that unlit branch by `0.9` (`ShaperFillResolver.cs:1121-1123`), recompiled clean, and re-ran:

- **`LT8_ReceiveOffIsBitIdentical` → still PASS** (PASS=1, FAIL tokens 0).
- `LT10_AddIsNotLit` → **FAIL** (PASS=0, 3 FAIL tokens) — a live control proving the mutation was reaching the renderer.
- The other eight legs I ran alongside were unaffected.
- Separately and unintentionally, **`ShaperBorderAudit.BT11_Ordering` also caught this mutation** (`RESULT: FAIL`, 3 FAIL tokens) while it was still loaded, and passes cleanly once reverted. So a *T-0107* leg detects the unlit-path regression that T-0108's own LT-8 — the leg written specifically to guard that path — does not.

So a 10 % albedo regression in the shared unlit path — precisely the class of change a golden exists to catch — leaves LT-8 green. What LT-8 actually proves is the narrower "receive-off ≡ null-scene *within this build*"; it says nothing about the pre-T-0108 build. Reverted and the revert proved by md5 (`8d4f0e5d441ff8ba115a2ff69717f28d`, identical to the pre-mutation copy).

### 4. DEAD TEST — LT-2's primary allocation instrument returns a constant zero at every size

The audit reports, per case, `thread-alloc 0 B` from `GC.GetAllocatedBytesForCurrentThread()`, and its calibration block concludes the probe is "BLIND on this Mono runtime" with a floor of "> 4 MB" — a phrasing that implies it works above 4 MB. It does not work at any size. Driving it directly with known allocations:

```
raw probe value sampled 3x: 0, 0, 0
single alloc     1 KB -> delta 0        single alloc  8192 KB -> delta 0
single alloc    64 KB -> delta 0        single alloc 16384 KB -> delta 0
single alloc   512 KB -> delta 0        single alloc 65536 KB -> delta 0
single alloc  4096 KB -> delta 0
65 x 120000 B (7.8 MB, the STATED MUTATION's shape) -> thread delta 0, heap delta 7 987 200
```

A 64 MB allocation moves it by zero. The calibration only escalated to 4 MB and stopped, so it never discovered that the instrument is inert rather than coarse. Every `thread-alloc 0 B` in LT-2's output is a dead reading, not a measurement.

The leg is rescued by its second instrument, `GC.GetTotalMemory(false)`, which does work. Its real detection floor, measured against LT-2's own pass condition (`thread == 0 && heap < 524 288`):

| allocation inside the measured region | heap delta | LT-2's verdict |
|---|---|---|
| 65 × 128 B (8 320 B total) | 12 288 | **PASS — missed** |
| 65 × 1 024 B (66 560 B total) | 0 | **PASS — missed** |
| 65 × 8 192 B (532 480 B total) | 798 720 | FAIL — caught |
| 65 × 120 000 B (7 800 000 B total) | 7 987 200 | FAIL — caught |

So LT-2 does not prove "exactly 0 bytes". It proves **no allocation larger than roughly 8 KB per `PaintTile` call**. A per-tile temporary array below that is invisible to it. (The IL scan is a genuine and independent second leg and is not affected by this; D-4's token-resolution work is sound.)

### 5. REAL DEFECT — `aspect` and `depth` are silently inert on three of the six solid forms

`ShaperSolidDef` documents `aspect` as "Pyre's `solidAspect` — the Y half-extent multiplier" with no exception, and `depth` as "the Z half-extent multiplier. **Unused by Can** (circular section)" — flagging exactly one exception. Measured by rendering each form twice and counting differing encoded pixels, at a non-zero rotation (yaw 35, tilt 28, roll 12) so that a Z-squash is genuinely observable:

| form | covered px | `aspect` 1 → 0.4 | `depth` 1 → 0.4 |
|---|---|---|---|
| Box | 6 068 | 3 470 | 3 014 |
| Pyramid | 2 409 | 2 589 | 2 006 |
| Can | 4 512 | 2 623 | 0 *(documented)* |
| **Orb** | 2 828 | **0** | **0** |
| **Gem** | 1 410 | **0** | **0** |
| **Ring** | 1 436 | **0** | **0** |

Orb's immunity to `aspect` is ruled by LR-6.5b ("a sphere looks identical from every angle") and is correct; its immunity to `depth` follows and is merely undocumented. **Gem and Ring ignore both dials completely**, with no diagnostic, no doc note and no leg.

LR-7.3 states the standard this violates in its own words: *"A control that silently does nothing is the failure B4 says must not survive the rebuild."* No leg tests dial responsiveness for any solid, and LR-7.3 itself has no leg.

### 6. CONTRACT ERROR — `rimStrength` does nothing at all when the ambient is black, and the mandated UI sentence does not cover it

The contract specifies `S += amb · rim` (`LIGHT-RIG-CONTRACT.md:298`) and the code implements it faithfully, so this is the contract's ruling rather than a coding error — but the consequence is a dial that is wholly inert in a state authors reach deliberately. Measured at the law level on a tilted normal (0.7, 0, 0.71414) with `rimStrength = 5`:

- ambient 0.0 → `S = 0` **exactly**, at every `rimStrength` value
- ambient 0.2 → `S = 0.06361176`

"Only my lamps, no ambient" is an ordinary authoring choice, and in it the Rim Strength control is a dead knob. LR-7.2 mandates a sentence for rim's other silent-inertness case (`RimNeedsRelief`, the flat-normal case, which LT-7 asserts) but there is no sentence and no diagnostic for the black-ambient case. Under LR-7.3's standard both cases need one. Recommend either a second sentence on the control or reconsidering the ambient tint — flagged for the owner, not fixed here.

### 7. CONTRACT ERROR — deviation D-6's stated consequence is wrong in both halves

D-6 rules that Orb rotates its published normal forward rather than inverse-rotating the light. **The ruling itself is right** and LR-1.1 does forbid a per-generator light. But both consequences the note claims are incorrect, and the note presents them as measured facts.

*Claim A — "for a directional light the two are bit-identical because there is no falloff."* Measured over 3 600 normals spread across the sphere, with rotation (25, 40, 15) and a unit light direction:

```
(Rot*N).L  vs  N.(Rot^-1*L):  bit-identical 1895/3600,  worst |difference| = 5.960E-008
```

Only 53 % are bit-identical. The identity is exact in real arithmetic and *not* in floats — which is the same argument D-1 makes correctly about LT-1, applied inconsistently here.

*Claim B — "Pyre also rotates the light for the attenuation term (measuring distance from the rotated surface point); this generator measures it from the true canvas position … only a point light's falloff differs."* **A rotation is an isometry, so it cannot change a distance.** Measured directly:

```
P = (7,-3,4), lightPos = (30,-22,22), Rot = Euler(25,40,15)
port    |lightPos - Rot*P|   = 38.238780
Pyre    |Rot^-1*lightPos - P| = 38.238780      |diff| = 0.000E+000
```

The normal-forward vs light-backward choice therefore has **no effect whatsoever on point-light attenuation**. Any real divergence from Pyre's falloff comes from LR-1.6's light-position convention change (radius-relative `(gemLightDistance + 1.2)·R` → absolute canvas position) and the solid's centre translation — a break that is separately ruled and priced in LR-1.6. D-6 attributes it to the wrong cause. The code is fine; the justification is a rationalisation and should be corrected so a later reader does not act on it.

### 8. GAP — LR-5.4, a rule with an explicitly named trap, has no leg

LR-5.4 rules that a border's fill is lit by its **host's** response block and its **host's** normal, and the contract calls out that "symmetry with BD-3.3 is the obvious wrong answer". I verified by reading that the code is correct: `ShaperFillResolver.cs:1227-1229` computes `bLight` from `scene.response[o]` and `bnslab` from `o * buf.sampleCapacity`, where `o` is the host owner index and `b` the border's own — the right way round, and the comment says so.

But **no leg asserts it.** Cross-referencing every `LR-x.y` defined in the contract against every `LR-` cited in `ShaperLightAudit.cs`: 47 rules defined, **23 referenced by the audit, 24 not**. LR-5.4 is among the unreferenced.

Stronger than a reference count: a border is constructed in exactly **three** of the 21 legs, and none of the three can detect an LR-5.4 error. Taking each in turn:

| leg | border at | why it cannot catch a host/border index swap |
|---|---|---|
| `LT3_TileIndependence` | `:793` | asserts only that the tiled render equals the whole render. Both sides run the same code, so any normal-source error changes both identically and the difference stays 0. |
| `LT8_ReceiveOffIsBitIdentical` | `:1164` | sets `receive = false`, so `bLight` is false at `:1227` and **the lit-border branch never executes at all**. |
| `LT12_ShadowsRecordedAndInert` | `:1456` | asserts shadows-off equals shadows-on. Both renders light the border identically, so the error cancels on both sides. |

So swapping `o` for `b` at `:1228-1229` — lighting a border by its own unset response block (rendering every outline unlit) or by the strip's own normal (the "raised welt around every shape" the contract explicitly warns against) — would leave all 21 light legs green. This is the T-0107 BD-3.4 pattern: a substantive rule with a named trap and zero effective coverage. The code is right today; nothing would tell you if it stopped being.

Also unreferenced and substantive: **LR-6.3** (the facet edge line, including the deliberate deletion of Pyre's `k = clamp(0.25 + lit, 0, 1.15)` floor and ceiling), **LR-6.4** (halo and inner glow), **LR-7.3** (the UI obligation that catches findings 5 and 6), and **LR-1.7**, **LR-3.4**, **LR-5.6**.

**LR-6.4's glow path is not merely unasserted, it is never executed.** `edgeGlow` and `innerGlow` appear exactly once in the 2 221-line audit — at `ShaperLightAudit.cs:250`, inside the shared `SolidDef` helper, both hardcoded to `new ZUIValue(0f)` — and every Solids fixture and every contact-sheet cell is built through that helper. So `scene.glow` is all-zeros in all 21 legs and all 24 cells, the generator's halo/inner-glow block (`ShaperSolids.cs:577-593`) never runs, and `LightSample`'s `cr += scene.glow[t3 + 0]` (`ShaperFillResolver.cs:1401-1403`) adds zero every time. An entire authored feature — and the exact feature D-7 makes a ruling about — has no coverage of any kind.

### 9. GAP — `ShaperDocument` and `ShaperLayer`, the types LR-0.1 requires this task to introduce, are never constructed by anything

LR-0.1 and LR-1.1 require a document-level owner for the rig, and the notes present `ShaperDocument` as "the first document-level authored object in Shaper". Counting references across the entire `Assets/Packages/Laubrary` tree:

```
ShaperDocument : 2 total references, 0 outside its own declaration file
ShaperLayer    : 3 total references, 0 outside its own declaration file
```

Neither type is constructed, read or exercised by the runtime or by any of the 21 audit legs. The notes state "`ShaperDocument` is not wired to any renderer" and argue "the audit drives the compiler and the scene directly, which is how a document would" — but the audit builds `ShaperResponseCompiled` by calling `CompileResponse` per fixture, never through a document or a layer list. So `canvasWidth`, `canvasHeight`, `pixelSize`, `layers`, `phase01`, `seed` and `Grid()` are entirely unexercised, and the document→compile path a renderer would actually take has never been run once.

### 10. DEAD TEST (weak) — LT-14b asserts a value the code cannot produce otherwise

LT-14b reports "alpha samples differing across the rotation: 0/16384". Orb coverage is computed at `ShaperSolids.cs:674-681` as `R = op.r; d = sqrt(lx*lx + ly*ly); dist = d - R; if (d > R) return;` — **the rotation matrix is never consulted for coverage at all**, only for the normal. The asserted 0 is structurally guaranteed rather than measured. It is not fully vacuous (it would catch someone newly introducing rotation into the silhouette) but it does not verify anything the current code could get wrong, and the audit's framing of it as a measured invariant overstates it. Same class as T-0107's BT-5.

---

## Things I checked that are genuinely sound

Stated because a verification report that lists only failures is not a measurement either.

- **LR-0.8's premultiplication trap is genuinely avoided.** I ran a stronger test than LT-9 as written: a Gem fixture with several owners and partial coverage, sweeping light intensity across {0, 0.5, 1, 2, 4, 8} with rim on, asserting the alpha channel bit-identical across the sweep. **0/9216 differing alpha samples at every step.** Lighting touches colour only.
- **Tile independence is robust well beyond LT-3.** LT-3 uses 7×5 tiles on a 42×35 canvas, which divides exactly. I re-ran on a **41×29** canvas (so no tile size divides it) with 8 lights of which 4 are point lights positioned near tile seams, plus a border and a Solids Gem, reusing one buffer and one scene across all tiles: **0/1189 differing pixels** at 7×5, **3×2**, **13×11**, **1×1** and 41×29. No per-tile state leaks.
- **LT-6's hand-computed values are correct and independently re-derivable.** I re-derived P1–P4 from the contract's own equations rather than trusting the leg: `atten = 1/(1+400/400) = 0.5`; ambient 0.18 and `resp.specular` 0.9 recovered from `L = 0.68` and `S = 0.45`; `pow(0.5, 2.2) = 0.217638` matching the reported rim; and P4's stated ambient-added-last alternative `0.5·(0.18+0.25) = 0.215` is a real discriminator for LR-2.3's ordering. This is the strongest leg in the set.
- **LR-5.4 is implemented correctly** (see finding 8 — the defect is the absence of a test, not the code).
- **Cell 08's blue fringe is legitimate authored colour, not a bug.** The contact sheet's `rigBoth` (`ShaperLightAudit.cs:2040-2042`) contains a point light of colour `(0.35, 0.7, 1.0)` at intensity 1.2, positioned at `(30, -22, 22)` — lower-right and close to the surface. Scanning all 24 cells for blue-dominant pixels (`B − R > 12`), **only cells 07 and 08 have any** (59 and 126 px; worst in 08 is `rgba(120,187,254)`), and 07/08 are exactly the two "dir+point" cells. The Gem's tilted facets face that lamp directly where cell 07's flat normal barely catches it, which is why 08 shows it strongly.
- **Cell 15 reads correctly.** The checkerboard showing through is an Add fill's alpha of 0 by design (FC-2.6b), composited additively over the opaque backdrop — what the legend claims.
- **LT-9 is not circular.** Its (c)/(d) legs compare lit against unlit alpha, and alpha is written outside the lighting branch entirely, so the mutation it names (M4) is the thing it tests. My independent sweep above reaches the same conclusion by a different route.
- **The containment claim holds.** `git status --porcelain` over `Runtime/Pyre` and `Editor/Pyre` returns **0 entries**, and the only changes anywhere under `Assets/Packages/Laubrary` are the two untracked Shaper trees. Pyre really was not modified, and nothing outside `Runtime/Shaper/` and `Editor/Shaper/` was edited.
- **`ShaperLightCompiler.CompileNormal` handles the degenerate case correctly** (`:210-220`): a zero or sub-1e-6 `normalConstant` falls back to `(0,0,1)` rather than dividing, which is what LR-3.5 requires, and LT-15's "Constant (degenerate zero)" case confirms it at 0 non-unit of 16 384.

---

## The nine declared deviations, checked

The brief flags each declared deviation as a place where a wrong result may have an argument wrapped around it. Seven hold up; one is wrong (D-6, finding 7) and one is wrong in its defence (D-9, finding 3).

| # | verdict |
|---|---|
| D-1 — LT-1's "bit-identical 512/512" conflicts with LR-3.5 | **Sound, and the right side was kept.** The conflict is real: LR-3.5 makes unit output a provider contract, so `CompileNormal` divides by `‖v‖` (`ShaperLightCompiler.cs:215-219`); an Orb's `N = P/R` is unit only to float precision, so re-normalising it perturbs the last bits. `pow(nh, 24)` then amplifies that. LT-1's wording should change, not the code. Their leg (ii) — same bits in, same bits out at 400/400 — is the exact assertion that *is* available. |
| D-2 — LT-14c's stated angles are arithmetically impossible | **Sound, verified independently from first principles.** Pyre's guard is `Mathf.Abs(ct) < 0.02f` (`PyreRenderer.cs:4632`), ported verbatim (`ShaperSolids.cs:371`). `acos(0.02) = 88.854°`, so at 89.0° `|cos| = 0.01745` and at 89.4° `0.01047` — both already inside the guard. The contract's "> 0 through 89.4°" cannot hold. Their corrected sweep brackets the true transition (88.8° → 88.9°) exactly. Fix LT-14c's angles. |
| D-3 — LT-2's stated instrument is absent | **Substitution justified, calibration wrong.** See finding 4: the substitute does not merely have a 4 MB floor, it is inert at every size. |
| D-4 — the IL scan resolves tokens rather than counting bytes | **Sound.** Resolving each candidate operand as a metadata token is the correct way to collapse a byte-frequency false positive, and reporting raw 1 / confirmed 0 rather than hiding the raw number is the right call. |
| D-5 — LT-14a distinguishes a shared edge from an overlap | **Sound.** `InTri` is inclusive (`w >= 0`), so a sample exactly on a shared diagonal is genuinely in both triangles; counting strictly-interior containment for the verdict and reporting the seam count separately is correct, and the leg still fails when the backface cull is removed. |
| D-6 — Orb rotates its normal rather than the light | **Ruling right, stated consequences both wrong.** Finding 7. |
| D-7 — the outside halo is not rendered | **Sound as a ruling, but untested.** The conflict it names is real (hard coverage multiplies a soft outside fragment by `ce = 0`). However LR-6.4 has no leg and no contact-sheet cell sets `edgeGlow`/`innerGlow` above 0, so neither the glow that *is* rendered nor the spill that is not is exercised anywhere. Recorded under finding 8. |
| D-8 — LT-10 measured as two renders, not two owners | **Sound, and the leg is live.** Two owners on one node would confound the ratio with the composite. My mutation of the unlit path made LT-10 fail correctly, so it is a real instrument. |
| D-9 — LT-8 compares the live unlit path, not a golden | **The defence is false.** Finding 3, proved by mutation. |

---

## Regression

I re-ran the 48 prior legs myself rather than trusting `REGRESSION.txt`, invoking every zero-argument public `string` method on `ShaperFieldAudit` (`V*`), `ShaperFillAudit` (`FT*`) and `ShaperBorderAudit` (`BT*`) by reflection, counting `RESULT: PASS` and case-sensitive `RESULT: FAIL` per leg.

**Result: 48 legs discovered, 48 `RESULT: PASS`, 0 `RESULT: FAIL`, 0 case-sensitive `FAIL` tokens, no duplicates** — 13 `ShaperFieldAudit`, 21 `ShaperFillAudit`, 14 `ShaperBorderAudit`, matching the suite sizes in `REGRESSION.txt` exactly. The implementer's regression claim is confirmed independently.

One process note, because it produced a false alarm worth not repeating. My first pass reported `BT11_Ordering` as `RESULT: FAIL`. That was **my** artefact, not a real regression: I had reverted the finding-3 mutation on disk but had not forced a recompile, so the editor still had the mutated assembly loaded. After `unity command recompile` → `completed`, `BT11_Ordering` returns `RESULT: PASS` on all five of its legs. Two things follow. First, the 48/48 above is from a confirmed-clean assembly (`ShaperFillResolver.cs` md5 `8d4f0e5d441ff8ba115a2ff69717f28d`, identical to the pre-mutation copy, with the recompile forced before the run). Second, and more usefully, **BT-11 detected the unlit-path mutation that LT-8 could not** — see finding 3.

A second process note for whoever runs these next: this editor goes `unreachable` on port 7801 *while executing a long leg*, and firing a second `eval_file` at it during that window wedges it for ~2 minutes. Two of the `ShaperFieldAudit` legs exceed the CLI's fixed 30 s reply timeout on their own. The pattern that works is the one the notes describe — a single invocation with a large internal budget that appends to a file, then poll the file — **not** a re-fire loop, which is what wedged it for me twice.

I specifically looked for more of the `AmbiguousMatchException` shape the implementer already hit once — reflection-based legs that a newly added public member can break. `ShaperBorderAudit.cs:1707` resolves `typeof(ShaperFillResolver).GetMethod("PaintTile")` by name alone; the single-method-with-default-argument fix keeps it working, and no other new public member on a reflected type was introduced.

---

## Blind spots — what I could not check

- **The domain-reload determinism leg (LT-4's second half) is still not run.** An eval executes inside one domain. Unverified by the implementer and unverified by me; I inherited the limitation rather than solving it.
- **No authored `.asset` was opened, by them or by me.** LR-4.4's "UNVERIFIED whether any shipped asset uses a spatial `gemSpecularFill`" remains unverified, and so does whether any document depends on the light position LR-1.6 breaks.
- **Burst is not demonstrated.** LR-2.2's blittable signature is checked structurally by LT-13b (which is sound), but no `[BurstCompile]` path exists to measure.
- **I did not diff `ShaperFillResolver.cs` against a baseline, because there is none.** The whole `Runtime/Shaper/` and `Editor/Shaper/` tree is **untracked** in git (`git status` shows `?? Assets/Packages/Laubrary/Runtime/Shaper/`), so the "1222 → 1457 lines" change has no recoverable before-state. I verified the changed regions by reading them against the contract instead, which cannot detect a silent edit elsewhere in the file's other 1200 lines. **This is the single largest hole in this verification** and it applies equally to T-0105/0106/0107's files.
- **Aesthetic correctness is not assessed**, and the contact sheet has still not been looked at by the owner. My review of it was diagnostic (per-cell pixel statistics), not a judgement that the lighting looks right.
- **I did not exhaustively mutate all 21 legs.** I independently mutated the unlit path (finding 3), independently calibrated the LT-2 instrument (finding 4), and independently drove LT-11's, LT-14b's and the Orb/dial paths to their failure conditions. For the legs I did not mutate — LT-1, LT-1b, LT-4, LT-5a, LT-5b, LT-12, LT-13, LT-14a, LT-15, LT-17 — I reviewed the assertion for structural falsifiability by reading but did not prove it by mutation. LT-5a's and LT-6's arithmetic I re-derived by hand; LT-13b's reflection assertion I confirmed by reading the signature.
- **A minor divergence I measured but did not rank as a defect:** the port lights facet edge lines through the full law including specular, whereas Pyre's line branch is `fr = lineColor.r * k` with no specular term at all (`PyreRenderer.cs:4347-4350`). Measured on a Gem with `lineWidth 1.1`: 80 of 370 line samples become blue-dominant with specular on and 0 with it off — a real behavioural difference from Pyre, but the magnitude is below 5e-6 in linear space and invisible in the encoded bytes. The LR-6.3 note names the loss of Pyre's 0.25 floor and 1.15 ceiling but does not name the newly added specular. Cosmetic, recorded for completeness.

---

## Housekeeping

- **The implementer's line counts in `IMPLEMENTATION-NOTES.md` §1 are stale** and understate the work: `ShaperSolids.cs` is 897 lines not 700, `ShaperLightAudit.cs` is 2221 not 1268, `ShaperLightRig.cs` 344 not 322, `ShaperLightLaw.cs` 306 not 272, `ShaperLightCompiler.cs` 346 not 336, and `ShaperFillResolver.cs` is 1457 not the stated 1439. Cosmetic, but the table is presented as exhaustive.
- **The contact sheet exercises 5 of the 6 solid forms.** Box, Orb, Gem, Can and Ring each appear; **Pyramid appears in no cell.** It is covered numerically by LT-14a and LT-15, so this is a visual-coverage gap only.
- **Tree state on exit:** every mutation reverted and proved by md5 against pre-mutation copies of `ShaperFillResolver.cs`, `ShaperLightLaw.cs` and `ShaperSolids.cs` (kept in `verify2/*.ORIG`). Nothing committed, nothing staged. `Runtime/Pyre/` and `Editor/Pyre/` were not touched.
