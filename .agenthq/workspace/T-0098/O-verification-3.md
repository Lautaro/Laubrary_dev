# O — Verification pass 3: fact-check of `PYRE_WHERE_I_STAND.md`

**17 ERRORs / 11 OVERSTATEMENTs / 7 OMISSIONs / 7 QUIBBLEs.**

Method: every load-bearing number re-derived from source or from `git`, not from the evidence file that asserted it. Three parallel source agents re-counted points 6, 7, 8 and 9 independently; git/filesystem/AgentHQ claims measured directly. No source file, report or evidence file was modified.

**The document's four biggest claims survive: the branch really is on no remote; the parallel rebuild really happened and ran 33 days; the edge stage really is dead; the top-right pivot bug is real, including the corner and the 10-of-17 blast radius. The entanglement retraction — "not one generator is mathematically fused" — also survives, verified independently in four pixel-emitting loops.** What fails is a layer of supporting counts, and two claims that are on the action list.

---

## The four findings that would most change the document

### 1. ERROR — "The tangles are properties of the rebuild, not of the old Pyre" is wrong for half the tangles it names

The document (§4) calls this **"the single most important sentence in this section"** and builds the whole design-document argument on it.

> "the four competing mechanisms deciding what a layer draws, the border control that is never built for most generators, **grouping expressed as a number matched across two layers inside a panel called 'Matte'**, **the blob control offered where it's never read** — are not legacies of the old Pyre. They are properties of the rebuild."

**What is actually true.** The parallel rebuild's first commit is `23c13700`, **2026-07-23 22:21:31**. The Matte system was built in the **old** Pyre earlier that same evening:

- `daa6278d` 19:07:10 — *"Pyre: matte layers — any layer can mask the layers above it"*
- `e6c9b841` 19:16:46 — *"Pyre: move the matte switch onto the layer row, settings as an accordion"*
- `f17d34b7` 20:59:23 — *"Pyre matte: combine channels — mask alpha + hue + brightness at once"*
- `ca44aad1` 21:06:06 — *"Matte box: drop the dividers/sub-headers"*

All **before** 22:21:31. The rebuild then copied it deliberately — `aa707636` (2026-07-24) is titled *"PyrePlus: Matte is per-layer in the layer list (**Pyre1-style**)"*. The blob-melt/MetaBlob control predates the rebuild outright (`c56670cd`, 2026-07-12; `86f6a4c6`, 2026-07-23 13:24).

Only two of the four are genuinely rebuild properties: the border built for six forms only (`5c61c81d`, 2026-07-26, PyrePlus) and the multi-mechanism dispatch.

**Why it matters, and why the fix improves the document.** As written, the owner is told the tangles are five weeks old when at least two were inherited. The *conclusion* — that a copy-as-mold rebuild re-mints the old model unless something forbids it — gets **stronger**, not weaker: `aa707636` is direct documentary proof that the rebuild reproduced the old model on purpose and named it "Pyre1-style". That is a better argument for the design document than the one currently made.

**Minimal correction:** "Two of the four are properties of the rebuild. The other two — the Matte panel and its grouping model, and the blob control — were inherited from the old Pyre and copied deliberately: one commit is literally titled 'Matte is per-layer in the layer list (Pyre1-style)'. That is the proof, not the counter-example."

---

### 2. ERROR — "Move the recolour stage out of the draw loop … no behavioural cost anywhere" is false, and it is item 5 on the action list

> §9 table: *"Universal-capable today, mis-advertised | 9 | Move the recolour step out of the draw loop, or give it a buffer fallback. **Zero behavioural cost.**"*
> What I'd do next §5: *"**Move the recolour stage out of the draw loop** — … no behavioural cost anywhere."*

**What is actually true.** `ApplyPix` (`Assets\Packages\Laubrary\Runtime\Pyre\PyreRenderer.cs:1343-1350`) runs on each particle's **pre-composite** colour, and its result is then alpha-composited: `PyreRenderer.cs:3089-3092` —

```
if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
```

A pass over the finished buffer is not equivalent, three ways:

1. **Non-linear kernels differ whenever particles overlap.** Posterise, colour replace, contrast and ordered dither applied once to the composite ≠ applied per particle before compositing.
2. **Even linear kernels differ once anything lies underneath.** `Over(dst, c·k, a) = dst·(1−a) + c·k·a`, whereas post-scaling gives `dst·k·(1−a) + c·k·a`. Equal only when `dst` is empty.
3. **`ApplyPixel` can return `false` to drop the pixel** (`continue`, `:3091`). A post pass can drop a *composited* pixel but cannot drop one particle's contribution. Wipe drops pixels — as a post pass it would wipe the whole layer rather than per-particle contributions.

The document's own alternative is the safe one and it is already in the same sentence: *"or give it a buffer fallback."*

**Minimal correction:** drop "zero behavioural cost" / "no behavioural cost anywhere"; make action item 5 read *"give the four generators that have no per-pixel stage a buffer fallback — leaving the existing in-loop stage exactly where it is, because moving it changes output on every layer where particles overlap."*

---

### 3. ERROR — the sheet-publishing interface is materially less ready than "promotion, not construction" implies

> §7: *"Six of the nine big effects already hand out **exactly these sheets under exactly these names**"* — where the named sheets are *how much stuff is here, how hot it is, how sooty it is, **how far this pixel is from the edge**, **how old this pixel's stuff is**, **which way the surface faces***. And §7: *"all eleven already compute coverage, and **six already publish it**."*

**What is actually true.** The interface is real — `IPlusFieldPublisher`, `Assets\Packages\Laubrary\Runtime\Pyre\PyreForm.cs:156-159`, sole call site `PyreRenderer.cs:352-358`, sole installer `Editor\Pyre\Parity\PyreParityDump.cs:79`. Nothing in Runtime reads it. That half is confirmed. But:

- **Count is understated, not overstated:** six *declaring types*, but `JetFormBase` is abstract and inherited by Jet / RadialJet / ExplosiveJet, so **eight of the nine** generators publish. Only Inferno does not.
- **The channel list is wrong.** What is actually published is `H`, `T`, `C`, `ramp_t`, `alpha`, `alpha_f`, `rim_mix` (`OrbForm.cs:467-469`, `TorchForm.cs:119-122`, `ArcBurstForm.cs:424-426`, `PlasmaBloomForm.cs:399-402`, `ForkBlastForm.cs:139-140`, `JetFormBase.cs:75-77`). **"How old this pixel's stuff is" is published by nobody. "Which way the surface faces" is published by nobody.** "How far this pixel is from the edge" exists only as PlasmaBloom's `rim_mix`, which is a crossfade weight (`Smooth(rimLo,rimHi,rn[i]) * rimMix`, `PyrePlasmaBloom.cs:586`), not a distance. **Two of the six named sheets do not exist anywhere and a third is misdescribed.**
- **"Six already publish coverage" → four.** Only Orb (`alpha_f`), Torch (`alpha_f`), ArcBurst (`alpha`) and PlasmaBloom (`alpha`) publish an alpha plane. ForkBlast publishes `H`,`T` only; the Jet family `H`,`T`,`ramp_t` only.

**Why it matters.** The monolithic contract is sold as *"it asks for nothing new: all eleven already compute coverage, and six already publish it."* For at least four generators it does ask for something new, and two of the six sheets the design is built around have to be constructed from scratch. "Promotion, not construction" is true of the *mechanism* and false of the *channel set*.

**Minimal correction:** "Eight of the nine publish a **subset** of these sheets — coverage/heat, soot and ramp position — through machinery that already works. Distance-from-edge exists on one generator as a crossfade weight; **age and surface-direction are published by nobody and would have to be added.** Exposing the mechanism is promotion; two of the six sheets are still construction."

---

### 4. ERROR — the two headline arithmetic splits do not add up

**(a) The swarm buckets.** §8: *"of **28** generators, **21** (75%) already comply … **5** (18%) have a better native interacting-seeds path; **2** need real work … **1** is out of scope."* 21 + 5 + 2 + 1 = **29**. Fire is double-counted: the evidence table lists it once as INERT and once as MULTI-SEED (rows 15 and 15b — 29 entries under 28 numbers), while the base of 28 counts it once. The percentages were computed against 28 and the counts enumerate 29, which is exactly why the error is invisible — they still sum to 100%. Against the true 29 it is 72.4% / 17.2% / 6.9%.

**(b) The entanglement denominators.** §0 and §7: *"In **four of the nine** big effects … In the other **seven** …"* — 4 + 7 = 11, not 9. The denominator silently switches mid-sentence. True figures: of the **nine** scene generators, four are palette-independent and **five** are the product form; adding the two fire simulations gives 4 + 7 of **eleven**. Same slip at §7 *"false for four of the nine generators it claimed to cover"*. (*"Mathematically fused: zero of eleven"* is correctly denominated.)

**Minimal correction:** (a) "of 28 generators, 21 already comply, 4 have a better native interacting-seeds path, 2 need real work, 1 is out of scope" — or restate against 29. (b) "four of the nine … the other five, or seven counting the two fire simulations."

---

## Confirmed correct — the load-bearing claims that hold

Stated explicitly, because these are the claims most worth trusting.

- **The branch exists on no remote. TRUE, and it is the most urgent line in the document.** `git branch -r --contains 60309923` returns empty; `feat/lathe` has no upstream; `origin` carries only `master`, `release`, `SmartStats`, `GamaSave-system`, `fix-demo-scenes-for-packages`. The entire current Pyre exists on one disk.
- **The parallel rebuild is real and ran 33 days.** `23c13700` (2026-07-23 22:21:31) — *"PyrePlus slice 1: **parallel Pyre rework** — Shape section + live renderer"* → `60309923` (2026-08-25) — *"Rename PyrePlus -> Pyre package-wide (**bulk WIP-safeguard commit 3/15**)"*. **366 files, 203 renames, +76,456 / −106,076.** Every figure in §4 amendment 3 checks out, including the switch-over being buried in a fifteen-commit batch.
- **CLAUDE.md is stale as described.** `git diff --cached` is empty; the rename is in history, dated 2026-08-25. Tree carries 129 modified / 72 untracked.
- **Four live attachment stages, not five.** `EdgeOffset` / `EdgeSoftness` (`SpriteFxModifiers.cs:2570`, `:2576`) have **zero invocations repo-wide** — all four hits are declarations or overrides.
- **The top-right pivot bug is real, including the corner.** `SpriteFxBurst.cs:683` passes `new Vector2(hHalf, vHalf)` as `GeoCtx.center` where every other site passes a centre-relative offset (`PyreFormWarp.cs:34` `Vector2.zero`; `PyreRenderer.cs:3016-3017`; type doc `SpriteFxModifiers.cs:178-179`). Consumers compute `off − ctx.center`, zero at `(W−0.5, H−0.5)`. Row 0 is the bottom row (`PyreFormWarp.cs:13`), so it is the **top**-right. **"Ten of the seventeen" is exact** (Sunburst wobble, RingWave, PointBlast, Profile, Ground, Sunburst, PulseRings, Turbulence, Curl, Sphere).
- **"Not one generator is mathematically fused."** Verified independently in four pixel-emitting loops. In all four the alpha comes from field quantities and form dials only, and the palette is read afterwards and never feeds back: Orb `PyreOrb.cs:617-635` (the LUT at `:631` is a 256×3 RGB array with no alpha column at all, `:112`); Torch `TorchForm.cs:207-215` (alpha from the raw heat plane `_Fp`, colour from the separately-cooled `_C`); Arc Burst `ArcBurstForm.cs:496-504`; Plasma Bloom `PyrePlasmaBloom.cs:587-594`. **All four palette-independent generators confirmed, not just the two requested.** The product form is confirmed with exact lines: `PyreJetEngine.cs:760`, `PyreForkBlast.cs:388`, `FireSim.cs:304`, `PyreInferno.cs:783→799→800`.
- **41 effects, and the 11 / 9 / 7 / 13 / 1 split sums to 41.** Re-derived independently: 46 classes derive from `PyreModifier` (`SpriteFxModifiers.cs:132`), minus 5 abstract stage bases = 41 concrete. Reconciles exactly with the stage counts (Geometry 17 · Pixel 12 · Post 10 · Edge 1 · Simulation 1).
- **The recolour stage really is a per-pixel callback inside every draw loop** (`ApplyPix` called from 16 sites, all inside `for(y) for(x)` bodies), and the four dead destinations are exactly Fire, Fireball, Playback3D and the height consumer.
- **Whole-frame effects really do run outside the generator dispatch**, for every layer including the one that draws nothing (`PyreRenderer.Layers.cs:174,176`; Playback3D returns at `PyreRenderer.cs:269`).
- **The Inferno flag really is a double-application guard, not an aesthetic verdict** (`InfernoForm.cs:17-19`, `PyreForm.cs:197-201`, `PyreFormWarp.cs:8-9`). Report 2's reading is wrong; the document's is right.
- **The two numbers really are computed and thrown away.** `crossFrac` (14 sites) and `pHash` (11 sites) are pure locals consumed only by `ApplyPix`; the publishing mechanism exists (`PyreFormWarp.Apply(float[] plane, int[] map)`, `:57-62`).
- **The border control really is not constructed.** `PyreWindow.cs:1371` — `if (IsFlat2DBorderForm(s.shapeForm)) BuildBorderBox(s);`. No greyed row, no tooltip. 29 techniques − 6 flat 2D forms = the 23 the document cites.
- **Only two of the seven give an instance its own clock — exact.** Own clock: ArcBurst (`ArcBurstForm.cs:481-482`), PlasmaBloom (`PlasmaBloomForm.cs:462`). Shared clock, hoisted above the loop: Orb (`OrbForm.cs:506`), Torch (`TorchForm.cs:155`), Jet/RadialJet/ExplosiveJet (`JetFormBase.cs:113`). All five use `own` purely as a liveness filter (`if (sp.own < 0f || sp.own > 1f) continue;`). **"Pop in mid-animation" genuinely follows** — `spawnLife` is staggered per instance (`PyreRenderer.cs:1977`), so a late instance passes the filter at `own ≈ 0` but is drawn at the layer's current phase.
- **Shaper's silhouette engine is 98 lines** — exactly, at `D:\CODEZ\AgentHQ\3D Shaper\public\index.html:981-1141` (161 total / 98 non-comment). It genuinely contains all eight primitives, the bulge warp, the transform maths, all four combine modes including both soft ones, and the tree compiler with both guards. **Extrusion + bevel = 26 exactly** (`:1144-1171`). **124 total.** (See OVERSTATEMENT 8 for what the number does not measure.)
- **Pyre's disc and Shaper's ellipse are the same function.** `index.html:1033` reduces at `rx==ry==r` to `hypot(x,y) − r`; `PyreRenderer.cs:2995-2996` is `Sqrt(dx*dx+dy*dy) > radius`.
- **Pyre's built-in shapes have no anti-aliasing at all** — confirmed for every one, including Text, whose bilinear SDF is thrown away by a hard `if (sd < 0.5f) continue;` (`PyreRenderer.cs:4021`). `PyreSupersample` is referenced 0 times in `PyreRenderer.cs`.
- **Shaper's star is hardcoded five-lobed with zero parameters** (`index.html:1039`); there is no N-gon in `SHAPE_TYPES`; hexagon `0.55` and octagon `1.42` are hand-fits, not `√3/2` / `1+√2`. Both guards exist and the code says why (`:987-990`).
- **The UI-guide claims are all correct.** No progressive-disclosure rule exists (`grep -i "progressive|disclos|advanced"` over `ui-layout-rules.md` and `UNITY_DEV_GUIDE.md` returns zero hits), and all four constraining rules exist verbatim: Card-layout rule 5 (`ui-layout-rules.md:91`), Stable workspace (`:66`, `:73`), Label = action (`:99`, `:103-104`), Labeling (`:95`). **All three shipped disclosure patterns exist**, and the third is enforced, not just documented (`PyreWindow.Forms.cs:127`).
- **Both cost figures are arithmetically self-consistent.** 8 × 200 × 400 px = 640,000 vs 200 × 400 = 80,000 (both over the particle's own 20×20 raster box, so apples-to-apples). 200 · 24²/2 · 4,096 = 236 M vs 288 · 4,096 = 1.2 M (grid 64×64, `Pyre.cs:1185`); the ratio is 200× = N, which is what "asymptotic, not a constant factor" requires.
- **The measured residue precedent.** 366 files and 203 renames exact; **exactly seven** misnamed test files (`PlusCapabilityTests`, `PlusFieldOps`, `PlusFormWarp`, `PlusFrameFill`, `PlusLayerCache`, `PlusParityDump`, `PlusShade`); **exactly three** empty folders (see ERROR 8).
- **165 tests / ~104 guarding ports — exact.** `Assets\Tests\Pyre` holds 134 `[Test]` + 31 `[TestCase]` = **165**; the seven Kiln-effect test files sum to **104** (ExplosiveJet 19, Jet 17, RadialJet 16, Torch 15, PlasmaBloom 13, ArcBurst 13, Orb 11).
- **~10,700 lines of ported effects — 10,666**, and they are **already their own assembly** (`Forms\Kiln\Pyre.Forms.Kiln.asmdef`). Amendment 2 ("share, don't copy") is as cheap as claimed.
- **8.8 GB Library — exact.**
- **AgentHQ.** 31 to-dos (run to `T31`), **11 agents** (`agent_subagent_count: 11`) — both exact. The planning-document feature exists and holds 3 tabs with both reports filed (`planning/PyrePlus.json`). "Seventeen tasks under one node" for the port work is **defensible**: `PyrePlus.KilnPorts` now holds 23, but T-0042…T-0058 — three phases + eleven ports + two phases + step 0 — is exactly 17.
- **No inadmissible usage-count argument is used anywhere.** Checked: every appearance of a usage count in the document is a count being *withdrawn*, never one doing work.
- **Fairness to the owner: passes cleanly.** Checked against his verbatim text (`tasks/T-0098.md:189-212`). All nine points **plus 5b** are answered, under their own numbers, in his order. Every title summarises **his** point, not the answer. Nothing merged, skipped, or answered with a change instead of an answer. **Point 3 gets a real judgment answer** — named withdrawals plus a five-question gate — not just a list. Point 5's three structural sub-questions, the "develop anything in AgentHQ as a harness" sub-question and the overwatcher sub-question are each answered separately.
- **The K-vs-L adjudication is right, and the two do not actually conflict.** K's "UNAFFECTED" answers *"does withdrawing usage evidence change this verdict?"* (it does not — the four consolidations rest on shared code paths, no usage figure appears in the argument). L answers a different question — *"is this pattern-chasing?"* — and says yes. Siding with L on the recommendation while K's technical observation stands is correct. **But see OVERSTATEMENT 2: K's technical observation is the fact the document contradicts.**
- **The document does not smuggle a "coverage above 1.0 is meaningless" claim.** It claims unbounded *extent*, not unbounded value, and it does not overstate the emission-channel case beyond what OVERSTATEMENT 6 notes. It also never says "zero of the twenty-nine techniques" — only "zero of eleven", which is correctly denominated.

---

## ERRORs (full list)

**E1. "The tangles are properties of the rebuild."** — see finding 1.

**E2. "No behavioural cost anywhere" for moving the recolour stage.** — see finding 2.

**E3. Sheet-publishing readiness / channel list.** — see finding 3.

**E4. "All eleven already compute coverage, and six already publish it."** — see finding 3. Four publish it.

**E5. Swarm buckets sum to 29, not 28.** — see finding 4(a).

**E6. "Four of the nine … the other seven."** — see finding 4(b).

**E7. "The old Pyre received zero feature commits across the entire thirty-three-day window."**
Filed under *Measured and solid* in the Confidence section. **`4523a595`, 2026-07-23 23:01:00 — forty minutes after the parallel rework's first commit — is *"Pyre: pair value controls as independent masonry columns, not height-coupled rows"*, touching only `Assets\Packages\Laubrary\Editor\Pyre\PyreWindow.cs` (+14 / −3).** Not a PyrePlus commit, not a cross-cutting sync, not the rename. Two more are borderline: `661cba5d` (2026-07-28) added thumbnails and a picker to old Pyre (+59 lines across `PyreEditorLink.cs`, `PyreWindow.Preview.cs`, `Pyre.cs`), and `df35566f` (2026-08-04, *"SpriteFx/Pyre local work"*) changed old `BlastRenderer.cs` and `PyreSimulationModifiers.cs`. The conclusion survives; the absolute survives only as "essentially none".
*Correction:* "received essentially no feature work — one small UI commit on the first evening and a handful of cross-cutting syncs."

**E8. "Deleting the two empty leftover folders" (§3) vs "3 empty folders" (§3 gate question 4).**
Internal contradiction, and **three** is right: `Runtime\PyrePlus\`, `Editor\PyrePlus\` **and `Assets\Tests\PyrePlus\`** — all verified empty. Inherited from `CLAUDE.md`, which also says two.

**E9. "Nine effects read *nothing* from the generator at all."**
Six read literally nothing. **Colour remap reads `info.life`** (`SpriteFxColorRemap.cs:185`). **Wipe reads `p.hash`** (`SpriteFxBurst.cs:234`) — a per-*shape* seed minted inside Pyre (`PyreRenderer.cs:3027`) — in its `MaskNoise` shape, one of nine `WipeShape` values. So Wipe is buffer-runnable for 8 of its 9 shapes and changes character on the ninth. Combined with E2, bucket (i) is not free. The evidence file's own table gets this right; its summary line does not, and the document inherited the summary.

**E10. "Two of the blast effects place blasts that know they are siblings and bias each other's fire-versus-smoke character across the sequence."**
**Only one does.** Inferno is confirmed in full (`PyreInferno.cs:293`, `:304-307` `charDrift`/`charJitter` → `fireMul`/`smokeMul` at `:313-314`). **ForkBlast has no `fireMul`, `smokeMul`, `charDrift` or `charJitter` anywhere** — its `Ev` struct is `cx, cy, start, duration, scale, seed` (`PyreForkBlast.cs:119`). It is sibling-aware for **scale only** (`:133-135`).
*Correction:* "one blast effect biases fire-versus-smoke across the sequence; a second is sibling-aware for scale."

**E11. "The fireball … That's the entire blocker: four missing numbers."**
It is **two**. `FireballParams` (`FireballSim.cs:20-30`) has exactly 8 fields and no position; the fix the evidence itself states is *"adding `sourceX`/`sourceY`"*. Nothing derives four. (The hardcoding is confirmed: `FireballSim.cs:66`, `cx = (W-1)*0.5f, cy = (H-1)*0.5f`, recomputed inside `Step`, unreachable by any caller.)

**E12. "They do receive a size multiplier and an orientation and five of them use it."**
Two different counts collapsed into one wrong one. **`sizeMul`: seven forms use it.** **`orientDeg`: four** — Torch (`TorchForm.cs:174,190,198`) and the Jet family (`JetFormBase.cs:126`); zero hits in ArcBurst, Orb, PlasmaBloom, Inferno, ForkBlast. The "five" is the *ignored* count read as the *used* count. The "half wrong" verdict on report 1 stands; the number does not.

**E13. "One honest definition of 'how far across this shape am I', which is currently computed six different ways."**
**14 assignment sites, 7 distinct expressions** in `PyreRenderer.cs`. The six sites the evidence cites (`:3090, 3217, 3356, 3454, 3539, 3617`) are **all the same formula** `Clamp01(d / Max(0.001f, extent))`, differing only in the extent variable's name. The genuinely different formulas are all **omitted**: Streak's `Clamp01(as_ / halfW)` (`:3773`), Ring's annulus remap `Clamp01((rho − innerR) * invHole)` (`:4762, 4777`), and the facet solid's box extent (`:4045`). "Six different ways" describes six copies of one way; the 6 and the 6-distinct are equal by coincidence.

**E14. "Shaper's '3D' is a height map lit from one fixed direction that cannot be rotated."**
The lighting half is **false**. `renderModelGrid` builds a **multi-light rig from user-placed lights** — `lightsOf(project)` (`index.html:1383`), each with `x`, `y`, `height`, `color`, `intensity` (`:1388-1390`), weighted-summed with per-light Lambert and a per-light shadow ray-march (`shadowAt:1469-1472`, loop `:1498-1504`). The single fixed upper-left direction (`:1386`) is only the fallback when a project has no lights, and the comment says so. **What is actually fixed is the *view*** — there is no camera or view matrix anywhere in `renderModelGrid` (`:1419-1423` samples only `lx/ly` and `inside`). The carve-out's *conclusion* survives intact (Pyre's solids do real 3D rotation with back-face culling — `Rot()` at `PyreRenderer.cs:4203-4208`, cull `n.z <= 0` at `:4213`), but the stated reason is wrong.
*Correction:* "Shaper's 3D is a height map rendered from one fixed, unrotatable viewpoint (its lighting rig is fully movable; its camera is not)."

**E15. "The part that is genuinely big — Shaper's materials and lighting, about 214 lines."**
That figure is `renderModelGrid`, which is the **rasteriser**, not materials and lighting — and it is **211 total / 182 code**. 214 is reachable only by including a 3-line doc comment. **The 98 / 26 / 124 figures are non-comment counts and the 214 is a with-comments count**, so the comparison mixes conventions. Apples-to-apples it is 124 : 182. The actual material/lighting code is scattered *outside* it (`patternMix:1308-1329`, `SURFACE_CATEGORIES:1332`, `autoOutlineColour:1331`, `compileLayer:1336-1367`, `FALLBACK_MATERIAL:991`) and is not in the 214. Consequently *"which overstates it by roughly double"* is itself inflated.

**E16. The "second clock" — "today every single call site feeds the ramp's animation position a zero."**
Misidentifies what is zero. `ZuiFill.Evaluate(float life, float u, float v)` (`Assets\Packages\Laubrary\Zui\Scripts\Runtime\ZuiFill.cs:167`) takes a life clock and a **spatial sample point**. What every Pyre call site passes as zero is `u, v` — the spatial point — not an animation position: `fill.Evaluate(life, 0f, 0f)` (`PyreRenderer.cs:2902`), `Evaluate(frac, 0f, 0f)` (`:1659`), `Evaluate(own, 0f, 0f)` (`:3718`), `Evaluate(h, 0f, 0f)` (`:1871`), `Evaluate(1f - t, 0.5f, 0.5f)` (`PyreForkBlast.cs:373`). That is why every spatial and noise fill mode collapses to a single sample — a real defect, but a different one.
Also, the claimed separation does not hold in the plain over-life mode: `case Mode.OverLife: return HasGrad ? EvalGrad(Mathf.Clamp01(life), life) : color;` (`ZuiFill.cs:178`) passes the *same* `life` as both the ramp position and the ramp's own clock. Decoupling them there needs a signature change to a shared ZUI runtime type consumed by other tools — which is an engine change.
*Correction:* "Every Pyre call site passes the fill a spatial sample point of (0,0), so spatial and noise fills collapse to one sample and cannot churn. Feeding the shape-local coordinate — **the same number point 9 needs**, so this is not an independent cheap win — plus a phase field lights it up. In the plain over-life mode the ramp position and the ramp's clock are one value and cannot be separated without changing the shared fill type."

**E17. "Five of the eleven generators already do exactly that today"** (the fill thinning the shape).
**It is seven:** the three Jets (`PyreJetEngine.cs:760`, `ac = hot.a[idx]` at `:744`), Fork Blast (`PyreForkBlast.cs:388`, `aceil = baseColor.a` at `:386`), Inferno (`PyreInferno.cs:783 → :799 → :800`), Fire and Fireball (`FireSim.cs:304`). The evidence file's own enumeration lists seven items under the word "five"; the document inherited the word. **Correcting it strengthens the veil argument** (7 of 11, not 5), so it costs nothing. Arguably eight — see OVERSTATEMENT 5.

---

## OVERSTATEMENTs

**O1. "Not one generator is genuinely fused. Not in one generator. Not partly."** Stated without a denominator anywhere in §0 or §7, while the Confidence section says only **eight** were read line by line and the counted set is **eleven** — out of **29** selectable techniques. The broad claim *is* supportable: `F-decomposability.md` classified all 29 (CLEAN 14 · MECHANICAL 13 · ENTANGLED 1 · N/A 1) and the single ENTANGLED is the sprite generator the document itself retracts. But the document never says so, so a reader cannot tell whether "not one" means "not one of eight", "of eleven" or "of twenty-nine". *Correction:* state the scope once in the headline.

**O2. "The three jets are genuinely one engine wearing three hats … Fusing those removes a lie rather than a picture."** The four lit solids are **also one engine**. `PyreRenderer.cs:2844` says so in its own comment — *"All four share DrawFacetSolid — same rotation, lighting, edge lines and glows — differing only [in their vertex sets]"* — repeated at `Pyre.cs:73`, with `DrawFacetSolid` at `:4152`. So "one engine" does **not** distinguish the surviving fusions from the withdrawn ones. And the document never applies its own gate question 3 (*"Does it remove or hide a picture? … or could only produce it after one extra decision"*, which it explicitly says *"Consolidation is subject to this test"*) to the jets, which plainly do produce three different pictures. As written, the split between the two surviving and two withdrawn consolidations rests on a criterion that is factually false for one side. *Correction:* justify the jets fusion on "same picture" grounds, or run all four through test 3.

**O3. "On a single centred shape the pivot is already identical and only the reach differs (about double)."** The ratio is `min(W,H)/2 ÷ r` (`PyreFormWarp.cs:34` / `SpriteFxBurst.cs:683` vs `PyreRenderer.cs:3017`). "About double" holds only where the particle radius is exactly a quarter of the canvas's short side. For the swarm case the sentence is actually about, particles are far smaller — a 4-px particle on a 64-px canvas is **8×**. Presented as a general figure; it is one worked example.

**O4. "All three already exit through the same shared downsampling function."** Arc Burst reaches `PyreSupersample.Downsample` only when its supersample factor `k > 1`. At a canvas of 128 px or more, `ArcBurstForm.cs:455` gives `k = 1` and it takes a hand-written direct-write branch (`:506-519`) that bypasses the shared function and does its own byte truncation. True at Pyre's typical 64 px canvas; not unconditionally. (The count of three is correct and complete — `Downsample` has exactly three call sites: `ArcBurstForm.cs:520`, `PlasmaBloomForm.cs:469`, `TorchForm.cs:216`.)

**O5. The sprite generator: "a one-line separation, arguably the most trivially separable thing in the library."** Understated by one term. When sprite tinting is on, the **fill colour's alpha multiplies coverage**: `PyreRenderer.cs:3592` (`ta2 = tint ? col.a : 1f`) × `:3611` (`outA = (sc.a/255f) * alpha * ta2`). That is the same product form the document files under its category (B), making the sprite generator an eighth generator whose fill can thin it. The separability conclusion survives; "one line" does not.

**O6. "A channel that means 'paint where there is no coverage' *is* coverage under another name … and buys nothing."** The document then concedes two paragraphs later that *"Additive light isn't expressible by coverage alone"* and proposes *"a fill declares whether it blends over or adds"*. The blend-mode flag is genuinely cheaper than a second channel, but "buys nothing" overstates: emission bought the additive case, which the document has to re-buy elsewhere. *Correction:* "buys nothing that a blend-mode flag on the fill doesn't buy more cheaply."

**O7. "Time to seeing something working: about a week — and that's measured, not guessed, because the *first* commit of the last parallel rebuild was 'shape section plus live renderer'."** The cited evidence supports **one day**, not a week: `23c13700` (23 Jul 22:21) already carried a live renderer, and `879730f9` (24 Jul) is *"CHANGELOG entry for the **completed prototype** (slices 1-3)"*. The estimate is conservative and therefore harmless, but the stated derivation does not support the stated number.

**O8. "What moves is about 124 lines of arithmetic, transcribed once, by a person."** The raw count is exact, but it is not a measure of the work. Shaper is a **single-file JavaScript web app** (`D:\CODEZ\AgentHQ\3D Shaper\public\index.html`, 4,102 lines), and that block is extraordinarily dense: **7,307 code characters over 98 lines (~75 chars/line), 92 semicolons in 98 lines, 16 lines carrying more than one statement, longest line 311 characters.** `matInvert` (`:1014`) is one line holding five declarations and a return; `primitiveSdf` (`:1031-1042`) is 12 lines for 8 primitives. A conventionally formatted C# port is materially larger. The count also **excludes a required dependency** — `transformParam` (`:1920`), which `nodeMatrix` calls six times and cannot run without — and **includes ~5 lines that are not silhouette at all** (`:982` renderer/glow/shadow constants, `:991` `FALLBACK_MATERIAL`, `:997` `smoothMax` for z-buffer layer fusion, `:1008-1009` pattern noise). It is a contiguous line range, not a dependency closure.

**O9. "Every one of Pyre's flat shapes is already an inverse-transformed point test."** True for Crescent (`:3196`), Star (`:3310-3313`), Polygon (`:3418-3421`) and Streak (`:3745-3751`), all via `ResolveSample` (`:3105-3132`). **Three exceptions:** Disc's fast path (`:2988-2996`) does not call `ResolveSample` — same operation, different code, so there are two mechanisms not one; **Sparkle** (`:3470`, test at `:3518`) decides lit/unlit by a stochastic hash over a virtual cell grid, not a point-in-shape test — which the document's own carve-out concedes ("Sparkle is not a primitive"), so the sentence contradicts its own §6 four paragraphs later; and **Sprite** (`:3553`) is a texture fetch.

**O10. "'Run the generator N times with per-instance offsets' … is already the implementation in seven of the nine big effects. Their code is literally a loop over the swarm."** True at the *form* level, false at the *implementation* level: there are **five distinct loops** (`ArcBurstForm.cs:474-482`, `OrbForm.cs:512-518`, `PlasmaBloomForm.cs:454-462`, `TorchForm.cs:178-198`, `JetFormBase.cs:120-128`). `RadialJetForm` and `ExplosiveJetForm` contain no `ctx.swarm` reference at all — they inherit `JetFormBase`'s single loop. And "the nine" is not the right denominator either: Inferno (`InfernoForm.cs:196-204`) and ForkBlast (`ForkBlastForm.cs:169-177`) also loop the swarm, they just build event descriptors instead of drawing. **All nine loop it.**

**O11. "Each of the seven invents its own seed from that index using a different magic number, and one of them invents none at all."** The premise is right — `PyreSwarmInstance` (`PyreForm.cs:61-71`) has `index` but no seed. But there are **two** magic numbers across the seven, not seven: `7919` in ArcBurst alone (`:482`), `104729` shared by **five** forms (`OrbForm.cs:518`, `TorchForm.cs:184`/`:197`, `JetFormBase.cs:125`), and none in PlasmaBloom (correct). The real defect — that it is convention rather than contract — stands; the picture of seven divergent inventions does not.

---

## OMISSIONs

**M1. A second live bug, found by the same investigation, is dropped.** The section is titled *"Three corrections, **one of them a live bug**"* and names only the pivot. The point-9 evidence also found: **Pyre never calls `SetPicture`** — the only invocation repo-wide is `SpriteFxBurst.cs:656`, and `PyreRenderer.cs:1379-1393` sets only life/seed/frame — so any post pass that normalises a position measures against the raw buffer rather than a picture rect (`SpriteFxRelight.cs:472-474` falls back to `W,H`). The owner asked for live bugs to surface; this one did not.

**M2. "The 13 need exactly two numbers" omits a third precondition.** The evidence states that for "every effect works on every generator", **three** things must be true; the third is that the buffer be padded by `OutwardReach`, which requires the `SetPicture` call Pyre does not make. The document discusses padding only under 5b (as a consequence of the frame-edge ruling) and never connects it to point 9's preconditions. "Exactly two numbers" understates the work.

**M3. A caveat on the "universal today, correctly advertised" bucket is dropped.** The document's table says those 11 take *"Nothing"*. **Pixel fluid — one of the 11 — has no host in the standalone stack**: `SpriteFxSpec.cs:32` has no slot and `RunStack` has no case.

**M4. A second stale line in the project instructions is not flagged**, in the section dedicated to exactly that. `CLAUDE.md`'s *"Open packaging gap: `Assets/ZUI/` currently lives OUTSIDE the package … package tools referencing ZUI only work in this dev host"* is stale: `Assets\ZUI\` holds exactly two files (`ZUIEnvelopePresets.asset` + `.meta`), while ZUI's **132 `.cs` files and three asmdefs** live at `Assets\Packages\Laubrary\Zui\` (`ZUI.Editor.asmdef`, `ZuiRuntime.asmdef`, `com.Lautaro-Arino.Laubrary.Zui.Editor.asmdef`). The document's own §4 clone argument leans on ZUI being a shared dependency, so it had reason to check.

**M5. A fourth exclusion site for the dead edge stage is missed** — `Editor\SpriteFx\SpriteFxStackView.cs:343` (`if (typeof(EdgeModifier).IsAssignableFrom(t)) continue;`) excludes it from the **standalone** editor's add-menu too. Minor, but it means the stage is unreachable in *both* hosts, which strengthens the "give it a real host or delete it" conclusion.

**M6. A loss recorded in the decomposability evidence never reaches the document.** `F-decomposability.md`'s Disc row: `brightMul` — swarm depth shading, `PyreRenderer.cs:2906-2911` — multiplies RGB per particle, so *"it must become a **depth channel** on the substance or it vanishes."* That is a shape-derived quantity driving colour. The document's sweeping *"the edge rule never asks what colour came out; the colour lookup never asks how solid the pixel is"* reads as universal, but was established only for the eleven shade-pass generators; the stamped forms carry their own shape→colour coupling.

**M7. "What I'd do next" puts the visual baseline last but needs it first — an ordering defect.** Item 6 is *"Render one frame per generator and look at them"*, which the document elsewhere calls *"a rendered frame per generator from today's Pyre as the **visual baseline**"* and *"now the only empirical evidence left"*. It sits **after** three items that change output: #3 ungate the border ("exercises the real border implementation against 23 generators that have never fed it"), #4 fix the five-meaning value "including the hardcoded zero that silently kills an effect", and #5 move the recolour stage. A baseline captured after three output-changing edits is not a baseline and cannot serve as the regression reference the design document asks for. It is also the cheapest item and has no prerequisites. *Correction:* move it to #1 (or #2, after the push).

---

## QUIBBLEs

**Q1. The evidence-file bookkeeping is inconsistent with itself three ways.** §5 says *"two reports and **eleven** evidence files — roughly 544 KB"*; the closing footnote says *"**thirteen** files now"* and *"seven fresh investigations on top of the **six** behind the first two reports"*. There are **fourteen** (A–N), of which **seven** (A–G) predate this round. Measured total: 590,993 bytes of evidence + 92,139 of the two reports = **~667 KB**, not 544 KB. The understatement runs *against* the section's own argument, so correcting it helps.

**Q2. "Roughly 850 MB of project"** — measured **957 MB** excluding `Library`.

**Q3. "Around 129 modified and 71 untracked files"** — currently 129 modified, **72** untracked.

**Q4. "It would produce exactly what an existing wobble effect already produces."** The true near-duplicate of a universalised edge warp is **`SunburstWobbleModifier`** (`SpriteFxModifiers.cs:339`, doc at `:330-336`, reads `ctx.center` at `:369`) or `TurbulenceModifier` (`:2653`) — not the plain `WobbleModifier` (`:322-327`), which is a horizontal ripple keyed to vertical position with no centre and no angle. The document's vaguer phrasing happens to stay true; the working notes name the wrong one, so an implementer will chase it.

**Q5. Line-number drift throughout the working notes.** The document carries no citations and is unaffected, but whoever implements from `H`–`N` will hit: Jet family cited 4–5 lines off throughout (`:736-757` for what is `:731-763`); Inferno ~5–9 off; edge hooks `:2568/:2575` vs actual `:2570/:2576`; `DrawParticle` `:280` vs `:279`; `ColorRemap` `:186` vs `:185`; SunburstWobble `:366` vs `:369`; sprite alpha cited `:3603` (an early-out) when the real line is `:3611`; `crossFrac` `:3618` vs `:3617`.

**Q6. "640,000 primitive evaluations … where a disc today is about 80,000 square roots."** Arithmetically correct and self-consistent, but the contrast is **exactly 8×** — the component count — and both figures are over the *same* 20×20 particle box. The presentation reads as a larger gap than the arithmetic delivers.

**Q7. "Fixing it is one line each"** (the per-instance clock). True for Torch and the Jet family — move `t`/`phase` inside the loop and read `sp.own`. **Orb is not a swap:** `DrawOne` takes an int frame index plus `N`, so the fix must convert (`int t = Mathf.RoundToInt(sp.own * (N-1))`, the idiom borrowed from `ArcBurstForm.cs:481`). One line, but a changed line plus a move.

---

## Consequences of the owner's rulings that nobody traced

Checked as requested; two are worth a sentence in the document.

- **The "assets are disposable" ruling does *not* endanger the ported effects' fidelity tests.** Those 104 tests assert against an outside original, not against saved Pyre assets, and `Forms\Kiln` is already its own assembly. The ruling is orthogonal to them. Worth saying, because "delete all the assets" and "the ported effects are the only irreplaceable thing" sit two pages apart and a reader could connect them wrongly.
- **The ruling *does* interact with the demo scenes and with cross-tool consumers, and the document is silent.** `Assets\Demos\` holds committed `.unity` scenes and `.asset` files that a Pyre deletion would break, and Pyre output is consumed cross-tool through `IChunkAnimation` / `PyreChunkAnimation` (commit `a358d7cf`) and by Mirage and Zoe effect palettes. The owner's ruling plainly covers *authored Pyre specs*; whether it covers *committed demo scenes that other tools' tests and demos load* is a question he has not been asked. One sentence would close it.

---

*Verification pass 3. Read-only: no source file, report, evidence file or git ref was modified. Three parallel source agents (points 6+8, 7, 9) re-derived counts from source independently of the evidence files that asserted them; git, filesystem and AgentHQ claims were measured directly by this pass.*
