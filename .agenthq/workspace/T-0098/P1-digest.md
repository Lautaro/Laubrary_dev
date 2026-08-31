# P1 — State of the argument: a condensed digest of the three Pyre reports

*Written 2026-08-30 for T-0098 by "Digest P1". Source: the three planning tabs in `.agenthq/planning/PyrePlus.json` — **R1** "GUG — Future of Pyre" (41 KB), **R2** "Shapes / Fills / Borders" (51 KB), **R3** "Where I Stand — the nine answers" (91 KB) — plus the fifteen evidence files A–O in `.agenthq/workspace/T-0098/`. This exists so a fourth document can be written without re-reading ~180 KB of report and ~590 KB of evidence.*

**How to use this.** Section A is what all three reports agree is true. Section B is the current verdict per generator and per effect. **Section C is the most important section: every claim that is now known to be wrong or withdrawn.** Do not resurrect anything in C. Section D is what still needs the owner. Section E is the live bug list. Section F is the citable-number index.

**One global provenance rule, established by all three verification passes:** the *design arguments* in all three reports survived adversarial attack; the *numbers around them* did not. D found 5 errors in R1's draft, G found 12 errors (+9 overstatements, +9 omissions, +5 quibbles) in R2's draft, O found 17 errors (+11 overstatements, +7 omissions, +7 quibbles) in R3's draft. **Every one of O's 17 errors was corrected in the published R3 text** — so the published tabs are mostly clean, but the *pre-correction* figures still circulate in the evidence files and in earlier tabs. Treat any single figure as worth re-checking before it justifies a deletion.

---

# A. Settled facts

## A1. What Pyre is, structurally

**29 selectable picture-making techniques** = 17 live `ShapeForm` enum entries + 9 `PyreForm` plug-in scenes + 3 field-pass modes reached from elsewhere. (Two further enum slots — `Inferno`, `ForkBlast` — are `[Obsolete]` and draw as Disc; they are not in the 29 but their int values must be preserved because the enum is append-only serialized. `Pyre.cs:108-117`.) The arithmetic 17 + 9 + 3 = 29 is confirmed independently by D §5 and F.

**Four unrelated mechanisms decide what a layer draws**, checked in priority order in `RenderLayer` (`PyreRenderer.cs:241`). **Only one of the four has an entry in the picker.** This is the single structural finding all three reports converge on, and R3 §2 re-derives it with no reference to usage data at all:

| Order | Mechanism | Field | Dispatch | In the picker? |
|---|---|---|---|---|
| 1 | `PyreLayer.form` — a `[SerializeReference] PyreForm` plug-in. Non-null wins outright; the entire enum path is skipped | `Pyre.cs:318` | `PyreRenderer.cs:248-252` | **Yes** (form cards) |
| 2 | `PyreLayer.shapeForm` — the `ShapeForm` enum | `Pyre.cs:312` | draw switch; early returns `:258` Fire, `:261` Fireball, `:269` Playback3D; `DrawParticle` `:281` | **Yes** (`PyreWindow.cs:1148-1163`) |
| 3 | `PyreLayer.coalesce` — the field-pass render mode. Reachable only on the enum path with the swarm on, and it **discards the picked shape entirely** (a coalescing particle is always a circle/dome, `PyreRenderer.cs:1552`) | `Pyre.cs:268` | read at exactly two lines, `PyreRenderer.cs:1497-1498`, inside `RenderSwarm` | **No** — a `Z.MiniRadio` inside `swarmBody` (`PyreWindow.cs:2492-2495`) |
| 4 | `PyreLayer.heightFromChannel >= 0` — the matte-heightmap consumer. **Also discards the layer's shape** and all envelopes | `Pyre.cs:233` | `PyreRenderer.Layers.cs:105`, `:168`; body `RenderHeightConsumer` `:1849` | **No** — a `Z.MiniRadio` inside `BuildMatteBox` (`PyreWindow.cs:1094-1096`) |

A defensible *fifth* mechanism exists but neither A nor K counts it: the **runtime decline/fallback path**, where Sprite with a non-readable texture and Text with no font both silently degrade to a plain Disc (`PyreRenderer.cs:2948-2956`, `:166`; the Text font auto-pick is `#if UNITY_EDITOR` only, `:3841-3848`).

**R3's usage-blind restatement (§2), which is the version to quote:** *"Pyre has one picker and four different mechanisms that decide what a layer draws. Three of the four have no entry in that picker at all. Two of its most capable stages are reachable only after making an unrelated choice first — the border only appears if you happened to pick a flat 2D shape, and grouping is an integer you have to make match across two separate layers, inside a panel labelled 'Matte'. The melt-into-a-blob control is offered on generators where the setting is never read at all. All of that is verifiable by reading the window's own layout code, with no reference to what anyone has ever saved."*

## A2. The generator inventory

Verdict column = **current** (R3/K usage-blind re-derivation; where it differs from R1 the change is marked ⚠). Decomposability = F's classification of all 29.

### Built-in `ShapeForm` enum (17 live)

| # | Generator | Class | Frame-addressable? | Decomposability | Current verdict |
|---|---|---|---|---|---|
| 1 | **Disc** `PyreRenderer.cs:2810` | flat stamp 2D | closed-form | mechanical | **Keep.** The structural fallback three paths degrade to. (R1's "78% of layers" justification is void; the structural one replaces it) |
| 2 | **Crescent** `:3140` | flat stamp 2D | closed-form | mechanical | Keep |
| 3 | **Star** `:3254` | flat stamp 2D | closed-form | mechanical | ⚠ **Fusion with Polygon WITHDRAWN** by R3 §3 (hides a picture). Keep separate unless the owner overrules |
| 4 | **Polygon** `:3375` | flat stamp 2D | closed-form | mechanical | ⚠ Same — fusion withdrawn |
| 5 | **Streak** `:3672` | flat stamp 2D | closed-form | mechanical | Keep. Only per-shape special case inside the swarm loop (`:1531` `streakScaleLengthOnly`) |
| 6 | **Sparkle** `:3470` | flat stamp 2D, binary coverage `:3488` | closed-form (twinkle re-seeded per frame) | mechanical | Keep. **Not a point-in-shape test** — a stochastic hash over a virtual cell grid, so it is explicitly carved out of "Shaper handles all primitives"; R3 §6 files it as **fill-side** |
| 7 | **Ring** `:4609` | 3D-lit analytic annulus | closed-form | mechanical | **Keep** — distinctive, nothing replaces it. ⚠ R1's "their problem is discoverability" clause **struck as unsupported** (it is an ordinary picker entry with an icon) |
| 8–11 | **Gem / Box / Pyramid / Can** — one routine `DrawFacetSolid` `:4152`, geometry `:4182-4184` | 3D geometry + Blinn-Phong, real rotation, back-face cull (`Rot()` `:4203-4208`, cull `n.z<=0` `:4213`) | closed-form | mechanical | ⚠ **Fusion into one "Solid" WITHDRAWN** by R3 §3 — four visible pictures behind a mode. Keep all four |
| 12 | **Orb (built-in)** `:4418` | 3D analytic sphere; lighting frame rotates, silhouette fixed | closed-form | mechanical ("the cleanest built-in": normal `:4534`, depth `:4532`, rim distance `:4523`) | **Keep + rename** (collides with the plug-in `OrbForm`). Discoverability clause struck as with Ring |
| 13 | **Sprite** `:3553` | flat stamp, **external asset** | closed-form; forces the spec serial (`GetPixels32`, `:203`) | F called it the **one ENTANGLED** generator | **Keep. ⚠ The "inseparable" label is RETRACTED** (R3 §7). It is *non-parametric*, not inseparable: coverage = artwork alpha, colour = artwork colour. Under the new model it is promoted to the best available **shape provider** — the Shaper landing site |
| 14 | **Text** `:3859` / `:3925` | 2D + the tool's only extrusion (8 slabs, `:4011`) | closed-form; forces serial | mechanical, but "the channel-hungriest built-in" | Keep |
| 15 | **Fire** `:392`, sim in `FireSim.cs` | 2D grid **simulation** | **NO** — retained heat/fuel grid in a `ConditionalWeakTable` (`:386-387`); any scrub or dial-twiddle forces a cold replay from frame 0; checkpoint TODO at `:383` | **clean** (seam is a literal function boundary, `FireSim.Step:112` → `FireSim.Render:295`) | Keep. Statefulness is unfakeable. Has a native multi-emitter swarm path (`fireSwarmEmitters`, `:399-402`, `RenderPlusFireLayer:579`) |
| 16 | **Fireball** `:757`, `FireballSim.cs` | 2D grid **simulation** | **NO** — same replay harness | clean | Keep. **Hardcodes its source at the canvas centre** (`FireballSim.cs:66`); `FireballParams` (`:20-30`) has 8 fields and **no position field at all** — that is the entire blocker to swarming it, and it is **two missing numbers** (`sourceX`/`sourceY`) |
| 17 | **3D Playback** `:269` (`return;`) | viewer for external 3D content | n/a — **renders nothing at bake time**; self-declared PoC at `:262-268` | **not a shape under any model** | ⚠ **CUT, or move it out of Pyre.** R1's three-way choice (finish / demote / cut) **collapses** — R3 §2. It also forces the spec serial (`:196`) and its editor preview **replaces the whole composited canvas** (`PyreWindow.Preview.cs:94-96`) |

### `PyreForm` plug-in scenes (9) — `Runtime/Pyre/Forms/Kiln/`

All nine are ports of external Kiln originals, guarded by ~104 fidelity tests, and live in their **own assembly** (`Forms/Kiln/Pyre.Forms.Kiln.asmdef`).

| # | Generator | Engine / size | Frame-addressable? | Decomposability | Current verdict |
|---|---|---|---|---|---|
| 18 | **Inferno** `InfernoForm.cs:15` | `PyreInferno.cs` 914 lines | closed-form (its own header says "NOT a sim") | clean; field pass `:548` → shading pass `:749`; pseudo-normal from density gradient `:758-763` | Keep. The **only** form with `HandlesGeometry => true` (`:19`); one of only 2 that use `ctx.fill`. Its **smoke grey is hardcoded** — splitting fixes something already broken |
| 19 | **Fork Blast** `ForkBlastForm.cs:15` | `PyreForkBlast.cs` 450 lines | closed-form | clean (nothing lost) | ⚠ **STRAIGHT DELETE, no migration note.** The best-evidenced deletion in the tool: its successor's own source says it replaces it and closes a **47-row** divergence table (`ExplosiveJetForm.cs:16-21`, `ExplosiveJetProgram.cs:16-17`). R1's "migration note rather than a straight delete" is **cancelled** by the owner's disposable-assets ruling |
| 20 | **Orb (plug-in)** `OrbForm.cs:36` | `PyreOrb.cs` 655 + 637 lines; 141 dials | closed-form (its "smear/persistence trail" is a *spatial* blur within one frame, `:157-162`) | clean | **Keep + rename** ("Bolt"/"Projectile"). Gains least from the split — any fill is largely the re-skin its ramp already gives |
| 21 | **Torch** `TorchForm.cs:39` | `PyreTorch.cs` 668 lines; 73 dials | closed-form | clean, with an **order-of-operations sting**: 3× supersampled, hard 7-band palette, filtered down *after* colouring | Keep. Publishes both raw and height-cooled heat |
| 22 | **Arc Burst** `ArcBurstForm.cs:38` | `PyreArcBurst.cs` 1115 lines; 187 dials (densest in the package) | closed-form (bolt trees re-rolled per frame from `seed·m + frame`) | **clean — "the model's poster child"** (separate E and A planes) | Keep. Coverage and energy are already two separately-blurred sheets |
| 23 | **Plasma Bloom** `PlasmaBloomForm.cs:109` | `PyrePlasmaBloom.cs` 613 lines; 114 dials | **The only whole-clip pre-pass.** You can ask for frame 9 first; it triggers a full-clip solve, memoised in `PyrePrepassCache<PlasmaFit>` | clean | Keep. **"Whole-clip" is a real badge with exactly one member.** The only one already crossfading two palettes by a *geometric* sheet |
| 24 | **Jet** `Jet/JetForm.cs:28` | `PyreJetEngine.cs` 795 lines, 60 shared dials | closed-form **and inherently LOOPING** — one period = the clip. The only generators with this property | clean | **Fuse the three Jets into one with a mode** — R3 §3 calls this the *honest borderline* (three recognisably different pictures from one engine + three override sets, 23 presets between them). R3 leans keep-the-fusion but flags it as the owner's call |
| 25 | **Radial Jet** `Jet/RadialJetForm.cs:26` | +10 dials | same, looping | clean | Fuse (as above) |
| 26 | **Explosive Jet** `Jet/ExplosiveJetForm.cs:44` | `ExplosiveJetProgram.cs` 1005 lines, +89 dials | same, looping | clean; fracture / chunks / gobs / dust / sparks **all deposit into the same H and T planes** | Fuse (as above). **The single best argument for the whole exercise** — a fill crossfading two materials by soot gets shrapnel molten at the core and cooling at the rim, free |

### Field-pass generators (3) — the "hidden" ones

| # | Generator | Entry | Decomposability | Current verdict |
|---|---|---|---|---|
| 27 | **Coalesce = Fuse** (MetaBlob / melt-into-a-blob) | `RenderPlusFusedField:1617`; enum `Pyre.cs:173` | clean — "the owner's grouping model, already built, already having paid exactly the price grouping costs" | **Keep + promote into the picker.** Reads the whole cloud as one field and produces a true merged silhouette with real necks, re-shaded through the Fill. Confined to one layer's swarm; only available on plain stamp generators with the swarm on |
| 28 | **Coalesce = Ramp** (HeightBalls / height relief) | `RenderPlusRampField:1697` | clean — "the substance model with three energy channels and a derived normal, already written" | **Fuse with #29** into one "Height field" with a source selector. ⚠ R2's "merge of a used thing with an unused one (8 vs 0)" caution is **STRUCK** — same lighting routine, clean merge of equals |
| 29 | **Height consumer** (mask-fed height field) | `RenderHeightConsumer:1849`; `heightFromChannel` `Pyre.cs:233` | clean — "this is the whole proposal, running, on a shape assembled from several other layers" | **Fuse (as above) + promote into the picker** |

## A3. The pipeline-stage model — why some effects do nothing

**The rule, in one sentence:** there are attachment points in the pipeline where an effect can hook in, and a generator supports an effect exactly when that generator's drawing routine calls that hook. Nothing else matters — not the shape, not the colour, not the sprite.

**There are FOUR live stages, not five.** R1 said five; R3 §9 corrects it, verified by O: the **edge** stage has **zero consumers anywhere**.

| Stage | Base class / hook | Where it attaches |
|---|---|---|
| **Geometry (bend)** — per-sample inverse warp of the coordinate grid | `GeometryModifier` `SpriteFxModifiers.cs:191`; `Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)` `:194` | Pyre: `ApplyGeo`/`ResolveSample` inside each raster body. Standalone stack: `RunWarp`, a whole-buffer resample (`SpriteFxBurst.cs:679`) |
| **Pixel (recolour)** — per-lit-pixel recolour or drop | `PixelModifier` `:661`; `bool ApplyPixel(ref Color col, ref float alpha, in PixelInfo info)` `:664` | `ApplyPix` inside each raster body (16 call sites, all inside `for(y) for(x)`); `RunManagedPixel` / Burst job in a stack |
| **Post** — whole-buffer pass | `PostModifier` `:1745`; `void Apply(Color32[] buf, int W, int H)` `:1747` | `ApplyLayerPost` per layer + the global loop (`PyreRenderer.Layers.cs:174`); spec-wide again at `:346-361` |
| **Simulation** — stateful replay grid | `SimulationModifier` `SpriteFxSimulationModifiers.cs:44` | Pyre only, via the dedicated `layer.simulationModifier` slot (`Pyre.cs:766` → `ApplyLayerSim`, `Layers.cs:176`). **Not a list entry** |
| ~~**Edge**~~ — perturb the silhouette hit-test only | `EdgeModifier` `:2565`; `EdgeOffset` `:2570`, `EdgeSoftness` `:2576` | **NOWHERE.** Zero invocations repo-wide. Excluded from Pyre's collector (`PyreRenderer.cs:1272`), Pyre's add-menu (`PyreWindow.Modifiers.cs:384`), the standalone stack's dispatch switch (`SpriteFxBurst.cs:640-668`, no default) **and** the standalone editor's add-menu (`SpriteFxStackView.cs:343`). Unreachable in both hosts |

**Which generator calls which hook (measured, not inferred):**

- **Bend works on:** Disc, Crescent, Star, Polygon, Sparkle, Sprite, Streak, Coalesce=Fuse, and (indirectly, as a whole-buffer resample after `Render`, `PyreRenderer.cs:349-350`) all nine plug-in scenes except Inferno, which sets `HandlesGeometry` and warps per-sample itself.
- **Bend does NOTHING on 8 built-ins:** Text, Gem, Box, Pyramid, Can, Orb, Ring, Coalesce=Ramp (height relief) — **zero occurrences of `ApplyGeo`/`ResolveSample` in their bodies.** Rising to 11 with Fire, Fireball and 3D Playback.
- **Recolour does NOTHING on exactly 4:** Fire, Fireball, 3D Playback, the height consumer. `mods` is not even a parameter of `RenderFireLayer`/`RenderFireballLayer`/`RenderHeightConsumer`.
- **Post and Simulation are already universal** — invoked from `RenderLayerBody` *outside* the generator dispatch. A 3D Playback layer that renders nothing still runs its post passes. **This is the strongest single piece of evidence for the owner's point 9: universality already works, it just stopped one stage short.**

**Two facts that are NOT true and were specifically hunted for:**

1. **No effect anywhere reads anything a generator wrote.** There is no auxiliary channel any generator writes and any modifier reads. `SpriteFxAuxMap` derives its map from the *finished picture's own pixels* (`SpriteFxAuxMap.cs:16-27`); `RelightModifier` *invents* its height field from the picture (`SpriteFxRelight.cs:463`). So the compatibility problem is purely about *where an effect attaches*.
2. **"Same effect, different meaning" is real and there are THREE hosts, not two.** In Pyre a geometry warp is a per-sample warp *inside the shape raster* with `GeoCtx` set to the shape's own frame (`PyreRenderer.cs:3016-3017`). In a standalone stack it is a whole-buffer resample with `GeoCtx` pinned to the canvas (`SpriteFxBurst.cs:682-683`) — **and that pinning is a bug**, see E1.

**The capability contract, derived and ready to publish (B's answer to "how many facts per generator?"): three booleans and one flag.** `SupportsGeometryModifiers` (discriminates 8), `SupportsPixelModifiers` (discriminates 4), `ProducesPixels` (false only for 3D Playback), `SwarmRole` ∈ {PlacesParticles, IgnoresSwarm, SwarmAsEmitters}. **Three of the four UI pieces already exist**: `PyreForm.HandlesGeometry`/`UsesFill` as the per-form virtual precedent (`PyreForm.cs:195-201`); `IsFlat2DBorderForm(ShapeForm f)` (`PyreRenderer.cs:950-952`) as the per-enum static-predicate precedent; and `DisabledInPicker` (`PyreWindow.Modifiers.cs:354-360`), which **already greys an add-menu row with a reason string** — it is a hardcoded 4-entry dictionary today; making it a function of `(modifierType, selectedLayer)` is the whole UI change.

## A4. The matte/mask system

**Three paths share one box, one master switch (`matteEnabled`, `Pyre.cs:203`) and two near-identically-named settings.** All three operate on the layer's *finished RGBA buffer*, after form, border, post modifiers and sim have run. **They never ask what drew the pixels.**

- **Path A — the cut-out mask (solidity).** `WriteMatte` → numbered channel → `clipByChannel`. An invisible layer deposits its **alpha** into one of **four** scalar planes (`Z.MicroSlider("Channel", 0..3)`); layers above multiply their own alpha by that plane. It can only ever *remove*. Combinable via `MatteCombine { Max, Add, Subtract }` (`Pyre.cs:164`), invertible. Harvest: `WriteMatteCoverage` reads `scratch[i].a` (`PyreRenderer.cs:908-920`). Apply: `CompositeLayer` (`:922-937`).
- **Path B — the light mask (brightness).** `LumaMatte` → `matteFlags`. Deposits **luminance × alpha** (`BuildMatteMask`, `:1070-1083`) as a 0..1 mask that imposes any combination of **six** effects on the layers above — Alpha, Brightness, Saturation, Hue, Blur, Displace — in a fixed order Displace → Blur → Saturation → Hue → Brightness → Alpha (`ApplyMatte`, `:1099-1122`). **One** mask at a time, with a scope (`NextLayer` / `AllAbove`, `Pyre.cs:155-159`), a strength envelope and an α-source modulation.
- **Path C — not a mask at all.** `matteWriteLuma` makes a WriteMatte deposit luminance; `heightFromChannel` makes some *other* Draw layer stop drawing its shape entirely and render the accumulated plane as a relief-lit heightmap through `layer.shapeFill`. **This is a generator reached from the mask controls** — generator #29 above.

**They overlap on exactly one bit** (`MatteChannel.Alpha` and `clipByChannel` both scale opacity) and are otherwise exclusive. **Neither is redundant.**

**The trap, and it is the whole of "the mask system is unpredictable":** the two read *different things*. A dark-but-opaque smoke layer is a **perfect** cut-out mask and a **useless** light mask (mask ≈ 0 everywhere). Nothing in `BuildMatteBox` (`PyreWindow.cs:970-1010`) warns. `Pyre.cs:140-141` already carries a comment calling the `matteChannel` (int) vs `matteFlags` (enum) naming a known hazard.

**Participation is NOT generator-dependent.** Every generator can be a mask source and be masked, with two exceptions: **3D Playback** produces an all-zero mask and has nothing to mask (`:269`); **the height consumer** can be masked but can **never** be a mask source, because `p.isHeightConsumer` is only assigned after the `isMatte`/`isLuma` branches have `continue`d (`Layers.cs:102-105`) — a layer is a writer or a reader, never both.

**The surviving recommendation:** rename to **"Cut-out mask (solidity)"** and **"Light mask (brightness)"**. R3 explicitly keeps this as *"still the best clarity-per-effort item in either document"* even while withdrawing the section it sat in.

## A5. What "simulation" means

**Exactly two generators are simulations: Fire and Fireball.** A simulation keeps a grid of heat and fuel between frames in a `ConditionalWeakTable`, giving fire that genuinely propagates, feeds on what it burnt and dies down unevenly. Everything strange about them follows from that one property:

- **Frame 9 requires frames 0–8.** Scrubbing back, jumping, or touching any dial forces a cold replay from frame 0. Deterministic, and it gets slower the further you scrub. There is a checkpoint TODO in the source (`FireSim.cs:383`) and no checkpointing today.
- **No bend effects and no recolour effects reach them** — `mods` is not a parameter of their render functions. Only whole-image post effects work.
- They ignore size, spin, travel and the whole position section, and they **force the whole effect to render single-threaded** (as do Sprite, Text and 3D Playback — `PyreRenderer.cs:196`, `:201-203`; single-threading is *not* attributable to simulation alone).
- Fire has a native multi-emitter swarm path; Fireball does not, because it has no position field.
- **The cost of a naive "swarm = N full renders" wrapper on these two is asymptotic, not a constant factor.** At 24 frames × 200 particles that is roughly **236 million cell updates against 1.2 million**, and every dial twiddle pays it again. For these two the wrapper must be **refused** and "many emitters into one grid" offered instead — which costs the same regardless of N and is already built for Fire.

**Verdict, unchanged across all three reports:** keep both, label them **"Sequential"**, and never let "simulation" be the obvious answer to "I want fire" — nine other hot-thing generators can jump to any frame instantly and take every effect. **Plasma Bloom is a third, distinct time-behaviour — "Whole-clip"** — with exactly one member.

## A6. What "fuse" turned out to be

**The old per-shape fuse toggle the owner remembered is already gone** — deleted in the 2026-08-23 rename with a documented migration onto the layer's melt-into-a-blob mode. **There is nothing left to retire.** Three surviving things say "fuse", and they are not redundant:

1. **Melt-into-a-blob (the layer mode, `LayerCoalesce.Fuse`).** Works on the particles *before* they are drawn: reads the whole cloud as one field, produces a true merged silhouette with real necks between blobs, re-shaded through the Fill. Exact, but confined to **one layer's swarm** and only on plain stamp generators with the swarm on. Fuse math at `PyreRenderer.cs:1606-1616`, `:1651-1659`; dials `Pyre.cs:271-282`.
2. **Blob melt (the SpriteFx effect, `FuseModifier` `SpriteFxModifiers.cs:2459`).** Works on the *finished picture*: box-blur the premultiplied frame, threshold, re-solidify. Cruder, but works on **anything** and can fuse **across layers**, welding text to a sprite to a fire. The layer mode structurally cannot do that. **This is the owner's grouping feature implemented backwards** — its entire purpose is silhouette union across layers, done after colouring because there was no way to do it before.
3. **The channel-based grouping** in the Matte panel (path A + the height consumer), which is the same idea again at a third place.

**Recommendation, surviving:** rename the effect to "Blob melt", stop calling it fuse, and call grouping a **Group** — never a fourth "fuse".

## A7. The swarm situation

**"Run the generator N times with per-instance offsets" is not a proposal — it is already the implementation.** All nine big effects loop over the swarm; seven run the generator once per instance with its own origin, scale and seed, accumulating into one shared plane; the other two (Inferno, Fork Blast) loop it to build **event descriptors** instead of drawing directly. At the code level there are **five** distinct loops, not seven, because three of the effects share `JetFormBase`'s single loop.

**No generator makes swarming impossible.** Three come close, each for a fixable reason: Fireball (two missing numbers), Fire without its emitter option (uses its own fixed emitters), 3D Playback (renders nothing).

**The buckets, counted against 29 generator behaviours:** **21 (72%) already comply** with the run-it-N-times model; **5 (17%) have a better native interacting-seeds path**; **2 need real work** (the two sims); **1 is out of scope** (3D Playback).

**"Many seeds that interact" is also already built, several times over:** Fire takes N emitters into one fluid grid; **Inferno** places blasts that know they are siblings and bias each other's fire-vs-smoke character across the sequence (`PyreInferno.cs:293`, `:304-307`, `:313-314`); **Fork Blast** is sibling-aware for **scale only** (`:133-135`); and the melt-into-a-blob modes read the whole cloud as one field.

**The real defect is an under-specified contract, and it hides a probable bug:**
- **Every instance gets an index but no seed.** `PyreSwarmInstance` (`PyreForm.cs:61-71`) has `index`, no seed. Each effect invents one: `104729` shared by five forms, `7919` in ArcBurst alone, none at all in PlasmaBloom.
- **Only two of the seven give an instance its own clock.** Own clock: ArcBurst (`:481-482`), PlasmaBloom (`:462`). Shared clock hoisted above the loop: Orb (`:506`), Torch (`:155`), Jet/RadialJet/ExplosiveJet (`JetFormBase.cs:113`) — all five use `own` purely as a liveness filter (`if (sp.own < 0f || sp.own > 1f) continue;`). Because `spawnLife` is staggered per instance (`PyreRenderer.cs:1977`), **instances pop in mid-animation instead of starting their own lifetime.** That is arguably a **latent bug**, not a missing feature. Fixing it is about one line each — a genuine one-liner for Torch and the Jets; Orb needs a small int-frame conversion.
- **The right framing, which nobody wrote down:** the question is not "can every generator be swarmed?" (essentially all already are) but **"what does an instance receive, and does it merge with its siblings or stack on top of them?"** Two axes, both already in the code, neither named. **One contract with two combining rules** — stack-on-top for discrete stamps (per-particle transparent compositing, which no field-accumulation rule reproduces), merge for fields.

**Melt-into-a-blob is offered where it cannot act.** The Coalesce radio is drawn whenever `swarmEnabled` (`PyreWindow.cs:2206`, `:2492-2495`) but `layer.coalesce` is read only at `PyreRenderer.cs:1497-1498`, inside `RenderSwarm`, which `RenderLayer` returns before for every plug-in form, Fire, Fireball and 3D Playback. Precise inert set: **plug-in forms, 3D Playback, swarm-off layers, and Fire with `fireSwarmEmitters` ON.** The two sims *without* emitters are handled correctly — the control is not shown at all.

## A8. What already exists, half-built (the finding that changes the size of the job)

R2's central discovery, unrevised by R3: **two thirds of the proposed re-architecture is already in the codebase, working, and either invisible or artificially restricted.**

- **The border stage — built, then gated to six generators.** It measures distance inward from the edge, paints that band with **its own separate `ZuiFill`**, animates its width, and keeps border and interior as **two separate buffers**. Gate: `IsFlat2DBorderForm` is a hardcoded six-case list {Disc, Crescent, Ring, Streak, Star, Polygon} (`PyreRenderer.cs:950-952`), **plus a second condition** `layer.form == null` (`PyreRenderer.Layers.cs:92`) that excludes all nine plug-ins regardless. And the control is **not constructed at all** for the other 23 (`PyreWindow.cs:1371`) — no greyed row, no tooltip, no trace it exists. **Ungating is three edits** (`PyreRenderer.cs:950-952`, `PyreRenderer.Layers.cs:92`, `PyreWindow.cs:1371`). One real caveat: **three generators already draw a private rim of their own** by other means (the lit solids, Text, Fire), which would now have a public one beside it.
- **Grouping — built, and reachable only by matching an integer across two layers inside a panel called "Matte".** Layers contribute their silhouette to shared channel *k*; contributions combine with union / add / carve; a later layer discards its own shape and colours the combined channel through a single fill with relief lighting. **Pyre covers two of 3D Shaper's four combine modes** (union ≈ Max, carve ≈ Subtract), plus one Pyre extra (arithmetic Add) Shaper has no counterpart for; **both** of Shaper's soft modes are missing. A one-knob smooth-combine primitive exists elsewhere; Shaper's soft blend is a **two-knob** strength-and-sharpness pair, so it is a starting point, not the thing.
- **Fill — a real abstraction, but a closed one.** `ZuiFill` has four modes, four texture kinds, a stamped-or-fixed coordinate space (`FillSpace{Stamped,Fixed}`, `ZuiFill.cs:29-37`, `:48`) and animatable ramp transforms. **`Color Evaluate(float life, float u, float v)` (`ZuiFill.cs:167`) takes no coverage, energy, height, normal or edge-distance** — F calls this "the single most important fact for the proposal". `ZuiFill` is used across the whole Laubrary library, so changing it has a blast radius outside Pyre.
- **Substance — already there, unnamed and meaning five different things.** Every effect already receives, per pixel, its warped position (`wx/wy`), its life, a stable seed (`hash`), and `crossFrac` — "how far across this shape am I". Two effects read `crossFrac`. But it is bound at 14 assignment sites in **7 distinct expressions**, and across hosts it means **five different things**: shape-local distance (`PyreRenderer.cs:1346`, fed by ~15 raster sites), **canvas**-local (`SpriteFxBurst.cs:117`, `unit = min(halfW,halfH)`), **the heat value** (`PyreInferno.cs:820`), **a fork/puff parameter** (`PyreForkBlast.cs:394`), and a **hardcoded `0f`** (`SpriteFxRecolor.cs:167`) that silently kills Tint's cross-gradient in that host.
- **The sheet-publishing mechanism already exists.** `IPlusFieldPublisher` (`PyreForm.cs:156-159`), sole call site `PyreRenderer.cs:352-358`, sole installer `Editor/Pyre/Parity/PyreParityDump.cs:79` — built so the fidelity harness could compare numbers against the Kiln originals. **Nothing in Runtime reads it.** **Eight of the nine** plug-ins publish (Inferno does not; `JetFormBase` is abstract and covers three). But see C4/E3: what they publish is a **subset** — `H`, `T`, `C`, `ramp_t`, `alpha`, `alpha_f`, `rim_mix` — and **only four publish coverage at all**.
- **Two clocks — the machinery is written and has never been switched on**, but the diagnosis was wrong the first two times: see C4/E16. `ZuiFill.Evaluate` takes a life clock **and a spatial sample point**, and **every Pyre call site passes the spatial point as `(0,0)`** (`PyreRenderer.cs:2902`, `:1659`, `:3718`, `:1871`, `PyreForkBlast.cs:373`). That is why every spatial and noise fill mode collapses to one sample and cannot churn. **The number it needs is the same shape-local coordinate the universal-effects work needs** — one change serving two purposes.
- **Extrusion — genuinely new.** Exactly one worked instance, welded into the Text generator (8 slabs, `PyreRenderer.cs:4011`), plus two unrelated height mechanisms that share no concept. **The only one of the four proposals that is honest new construction.**

## A9. The entanglement question — settled, and it is R3's most important correction

R2 named its hardest objection: *"for all nine scene generators, and for fire too, the fill decides the silhouette."* **R3 went looking for that entanglement, in eleven generators, line by line in their pixel-emitting loops, and could not find it anywhere.**

What is actually happening: a big Pyre effect builds an invisible cloud of numbers over the canvas *before any colour exists* — it sits in memory as its own array. Then, in one final loop, it does two independent things to that cloud: (1) an **edge rule** decides how solid each pixel is, from the numbers, not from a colour; (2) a **colour lookup** turns a heat number into a ramp position. **They never read each other.**

- **In four of the nine big effects it is only that** — you could replace the whole palette with a random one and the outline would come out bit-identical. Verified with exact lines: Orb (`PyreOrb.cs:617-635`; the LUT at `:631` is a 256×3 RGB array with **no alpha column at all**), Torch (`TorchForm.cs:207-215`; alpha from the raw heat plane `_Fp`, colour from a separately-cooled `_C` — two sheets, two rules, zero contact), Arc Burst (`ArcBurstForm.cs:496-504`), Plasma Bloom (`PyrePlasmaBloom.cs:587-594`).
- **In the remaining five — seven, counting the two fire sims** — the ramp carries a per-stop transparency slider which the generator **multiplies** into the edge rule's answer. So the ramp genuinely can change the silhouette, but it is a **product of two independent factors**, not a blend. Exact lines: `PyreJetEngine.cs:760`, `PyreForkBlast.cs:388`, `FireSim.cs:304`, `PyreInferno.cs:783→799→800`.
- **Mathematically fused: zero of eleven.**

**One caveat that survives:** this covers the eleven big field-based generators only. **The simple stamped shapes have their own shape→colour coupling** — the swarm shades a particle by its depth, multiplying its colour directly (`brightMul`, `PyreRenderer.cs:2906-2911`). Under a split that has to become a proper **depth** sheet or the effect quietly vanishes.

**The design R3 lands on — "the shape keeps the edge, the fill gets a veil":**
1. **The shape publishes its weather** as named per-pixel sheets — how much stuff, how hot, how sooty, how far from the edge, how old, which way the surface faces. Each generator declares which it has; a fill wanting a missing sheet is **greyed out with the reason shown**.
2. **The shape keeps the edge rule.** It is tuned against number ranges only that generator knows, and it is what makes a jet read as a *tongue* rather than a smear. So a shape can always answer "here is how solid I am at every pixel" **with no paint in sight** — which borders, groups, previews and swarm compositing all require. Per the owner's 5b ruling, that coverage is **soft and unbounded — a fog, not a stencil.**
3. **The fill is a paint recipe plus an optional veil** — a per-pixel transparency that **multiplies** the shape's coverage and never overrides it. Default: no veil, no opinion. Seven of the eleven already do exactly this today (eight counting Sprite with tinting on), so the veil is the existing power named and made deliberate.

**The monolithic escape hatch, with a one-line contract:** a monolithic generator **MUST publish coverage, and MAY publish nothing else**. All eleven already compute coverage internally; four already hand it out. With that it keeps everything (buffer effects, groups, swarming, borders) and loses exactly one thing — its fill picker shows one named entry rather than a choice. **Monolithic must be a declared *reason*, never a declared *exemption***, and there are exactly two legitimate reasons: its default fill is authored data rather than a rule (permanent), or it hasn't been split yet (temporary, and it must say so).

---

# B. Verdicts as they now stand

## B1. Per generator — the current position

| Verdict | Members |
|---|---|
| **Keep, unchanged** | Disc, Crescent, Streak, Sparkle, Sprite, Text, Fire, Fireball, Inferno, Orb (plug-in, renamed), Torch, Arc Burst, Plasma Bloom |
| **Keep — R1 verdict stands, R1's *reasoning* struck** | **Ring** and **built-in Orb**: "distinctive, nothing else replaces them" survives; "their problem is discoverability, not value" is **withdrawn as unsupported** |
| **Keep + rename** | Built-in **Orb** and plug-in **Orb** collide in one picker. Rename the plug-in one ("Bolt" / "Projectile") |
| **Keep + promote into the picker** | Melt-into-a-blob (Coalesce=Fuse), height relief (Coalesce=Ramp), the mask-fed height consumer |
| **Fuse — SURVIVES** | Jet + Radial Jet + Explosive Jet → one Jet with a mode (**flagged by R3 as the honest borderline**, a feature decision for the owner, not tidying). Height relief + mask-fed height field → one "Height field" with a source selector (**clean merge of equals**) |
| **Fuse — WITHDRAWN by R3 §3** | ~~Gem + Box + Pyramid + Can → one "Solid"~~ and ~~Star + Polygon → one N-gon~~. Both hide visible pictures behind a mode, which is the structural pathology the reports are trying to cure |
| **Straight delete** | **Fork Blast** — the best-evidenced deletion in the tool; its successor's own source supersedes it item by item across 47 rows. No migration note needed |
| **Cut, or move out of Pyre** | **3D Playback** — R1's three-way choice collapses. Renders nothing at bake time, self-declares as a PoC, and its editor preview hijacks the whole composited canvas |
| **Delete (dead surface)** | `PyreFormKind.PerParticle` and `.Stateful` — declared, documented, never dispatched. The three empty `PyrePlus` folders (`Runtime/`, `Editor/` **and `Assets/Tests/PyrePlus/`**) — free, buys nothing, is not evidence of anything |

## B2. Per SpriteFx effect — 41 effects, four axes

**Counts (all reconciled and stable):** 41 concrete modifiers, from 46 classes deriving from `PyreModifier` minus 5 abstract stage bases. By stage: **Geometry 17 · Pixel 12 · Post 10 · Edge 1 · Simulation 1 = 41.** By conceptual bucket (E): **shape ops 22 · fill ops 9 · borders 4 · post survivors 2 · fill generators 2 · cull 2 = 41.** By universality bucket (I): **buffer-only 20 · needs per-sample context 20 · irreconstructible 1 = 41.**

| # | Effect | Stage | Conceptual bucket | Universality | Current verdict |
|---|---|---|---|---|---|
| 1 | Skew `:203` | Geo | shape | buffer-runnable, pivot/quality cost | Keep |
| 2 | Scale `:228` | Geo | shape | buffer-runnable | Keep |
| 3 | Rotate `:266` | Geo | shape | buffer-runnable exactly | Keep |
| 4 | Wobble `:302` | Geo | shape | buffer-runnable | Keep |
| 5 | Sunburst wobble `:339` | Geo | shape | needs `ctx.center`; N pivots → 1 on a swarm | Keep. **Affected by the top-right pivot bug** |
| 6 | Ring wave `:386` | Geo | shape | needs `ctx.center` | Keep. **Pivot bug** |
| 7 | Point blast `:437` | Geo | shape | needs `ctx.center` | Keep. **Pivot bug** |
| 8 | Profile `:546` | Geo | shape ("silhouette molder", its own word) | needs `center` + `radius` | Keep. **Pivot bug** |
| 9 | Ground `:583` | Geo | shape | needs all four `GeoCtx` fields; raises `WarpPass` | Keep. **Pivot bug** |
| 10 | Sunburst `:779` | Geo | shape | needs `ctx.center` | Keep. **Pivot bug** |
| 11 | Pulse rings `:830` | Geo | shape | needs `center` + `radius` | Keep. **Pivot bug** |
| 12 | Turbulence `:2653` | Geo | shape | needs `center`, seed | Keep. **Pivot bug** |
| 13 | Curl / swirl `:2882` | Geo | shape | needs `center`, seed | **Keep and rebuild the drawing surface** (click-to-place vortex points). **Pivot bug** |
| 14 | **Vortex field (progress)** `:3007` | Geo | ~~cull~~ | buffer-runnable | ⚠ **REVERSED. Do not cull — fuse into Swirl as a second (progress-driven) driver mode.** Same `List<VortexPoint>`, different driver. Redundancy argues for fusion, never deletion |
| 15 | **Sphere (fake depth)** `:3084` | Geo | **fill op — reclassified** | needs `center` + `radius` | Keep, re-file. **Pivot bug** |
| 16 | Smudge `:3167` | Geo | shape | buffer-runnable | **Keep and rebuild the drawing surface** (drag records points, release ends the stroke). Shares one surface with Swirl + Vortex field |
| 17 | **Pin warp** `:3340` | Geo | **cull** | buffer-runnable | **CULL — stands.** Its frame hook `SetFrame` (`:3350`) has exactly one repo-wide hit, its own declaration; keyframed pins hold frame 0 forever, and keyframing is the entire point |
| 18 | Tint `:686` | Pixel | fill op **+ fill generator** (its `crossGradient` half, read at `:707`, is a radial fill) | needs `crossFrac` | Keep. **Must be split** under the new model |
| 19 | Contrast `:722` | Pixel | fill op | reads **nothing** | Keep. Dead on 4 generators for no reason at all |
| 20 | Brightness `:738` | Pixel | fill op | reads **nothing** | as above |
| 21 | Saturation `:754` | Pixel | fill op | reads **nothing** | as above |
| 22 | Posterize `:869` | Pixel | fill op | reads **nothing** | as above |
| 23 | Ordered dither `:902` | Pixel | **shape (coverage quantiser)** | reads buffer coords only | Keep. **Genuinely wants two homes** — coverage op *and* final-frame dither |
| 24 | **Voronoi crack** `:942` | Pixel | **FILL GENERATOR** (one of only two) | needs `hash` + `wx/wy` | Keep |
| 25 | Layer dissolve `:1247` | Pixel | shape (coverage erosion) | needs `wx/wy`, `hash`, `frame` | Keep. On a buffer it can no longer eat *whole particles* |
| 26 | Wipe / alpha mask `:1299` | Pixel | shape | reads `W,H,x,y`; `hash` **only** in the Noise shape, 1 of its 9 | Keep. Buffer-runnable for 8 of 9 shapes |
| 27 | Colour tint (wash) `:1450` | Pixel | fill op (a flat-colour **fill generator** at amount 1) | reads **nothing** | Keep |
| 28 | Colour replace `:1602` | Pixel | fill op | reads **nothing** | Keep |
| 29 | Colour remap `SpriteFxColorRemap.cs:142` | Pixel | fill op | reads `info.life` only (`:185`) | Keep |
| 30 | Dissolve `:1157` | Post | shape | buffer-only | Keep |
| 31 | Bloom / glow `:1787` | Post | **border generator** (with a caveat: threshold-driven by interior brightness, so it reads the fill) | buffer-only | Keep. Declares `OutwardReachPx` — **clipped in Pyre** |
| 32 | Outline `:1887` | Post | **border generator** | buffer-only | Keep — but **it duplicates Pyre's own layer-level border**, one painting outside the rim, one inside. **Merge into one border with an inside/outside/straddle choice before shipping any border picker.** Declares `OutwardReachPx` — clipped in Pyre |
| 33 | Chromatic aberration `:2102` | Post | **post survivor** | buffer-only | Keep. Declares `OutwardReachPx` — clipped in Pyre |
| 34 | Ballistic shockwave `:2200` | Post | shape (really a mini-generator: advects a density field, erodes alpha) | buffer-only | Keep. Under the new model it honestly **splits into a shape op + a fill generator** |
| 35 | **Fuse / blob melt** `:2459` | Post | **shape, group-level** | buffer-only | **Keep.** This *is* the owner's grouping feature, implemented backwards. It survives afterwards as a post treatment over *ungrouped* layers |
| 36 | **Edge warp** `:2585` | **Edge (DEAD)** | border op | **the single genuinely stuck effect** | **Give it a real host or delete it. Do NOT universalise it** — a buffer version cannot separate outline from paint, so it would ship a duplicate of an existing wobble effect under a name promising something else |
| 37 | Edge smooth `:2734` | Post | **border op** | buffer-only | Keep, re-file |
| 38 | Drop shadow `:3386` | Post | **post survivor** | buffer-only | Keep. Declares `OutwardReachPx` — clipped in Pyre |
| 39 | Kaleidoscope `:3481` | Post | shape, group-level | buffer-only | Keep. **Its own doc is the written record of the hole** — made a Post effect because that is the only universal place; radial repeat already exists but lives inside the scatter path, so three named generators (MetaBlob, Height balls, Fire) can never reach it |
| 40 | **Fake light** `SpriteFxRelight.cs:243` | Post | **FILL GENERATOR** (the second of two) | buffer-only, but Pyre never calls `SetPicture` for it | Keep. Explicitly declares `OutwardReachPx() => 0` — **it does NOT draw outside the silhouette** |
| 41 | Pixel fluid `SpriteFxSimulationModifiers.cs:115` | Simulation | shape (velocity displacement + alpha erosion, no colour) | seeds itself from the finished buffer | Keep. **Silently does nothing in the standalone stack** — see E5 |

**The nine genuinely contested classifications** (E's own flag, not a clean partition): Tint, Colour tint, Ordered dither, Sphere, Kaleidoscope, Bloom, Fuse, Ballistic shockwave, Pixel fluid.

## B3. Where the three reports disagree — which is current, and why

| Question | R1 | R2 | R3 (**current**) |
|---|---|---|---|
| What do the hidden Mass generators become? | *materials* that read height | a **shape** (a Group) plus an ordinary fill | R2's reading, kept. It is what they actually are — ways of combining silhouettes, not ways of colouring one |
| Do any generators fuse shape and colour? | not asked | **yes, 9 (later 11), "the hardest one"** | **No. Zero of eleven.** R2 §7.1 is superseded, not refined |
| Does opacity travel with the shape? | n/a | yes, full stop — accepting "a plasma fill cannot make a solid shape wispy" | **Superseded by the veil.** R2's stated cost is a *regression*, because seven generators can already do that |
| Does substance need an emission channel? | n/a | yes | **Drop it.** Coverage is a soft field of unbounded extent, not a stencil. One sentence replaces the channel |
| Is anything inseparable? | n/a | yes — the imported-sprite generator | **Retract.** It is *non-parametric*, not inseparable. R2's conclusion (promote it to a shape provider) is right; its reasoning is wrong |
| One effect list or four? | n/a | **four typed lists** (slice 2) | **In tension.** Universality *fixes* ordering; the four-way split *creates* an ordering problem, because the standalone stack keeps strict authored order and deliberately flushes. R3 leans **one ordered list + honest greying-out**. Owner's call |
| Is Shaper export a prerequisite? | **yes — "until export exists, no integration is possible"** | n/a | **False under the owner's point 6.** If Shaper's engine *becomes* Pyre's primitive engine, nothing crosses a process boundary. A ~98-line block of arithmetic is transcribed once, by a person. Shaper never needs an exporter |
| Is a rewrite the route that fails? | n/a | **yes, categorically** | **Scoped.** It survives only for the nine ported effects. For Pyre's own core and the 16 built-in enum forms, a parallel rebuild is now a live option — and the owner has already run exactly that strategy successfully, for 33 days, five weeks ago |
| Where does the "Generator" rename sit? | **headline recommendation** | assumed | **Withdrawn as pattern-chasing.** Nobody is blocked by the word "Shape" |

---

# C. Withdrawn, superseded and outright wrong claims

**This is the section to read before writing anything.** Nothing here may be presented as live.

## C1. R3's explicit revision table over R1 and R2

| Where | What it said | Status |
|---|---|---|
| R1 §10 | "Shape → Generator throughout" | **WITHDRAWN.** Pattern-chasing. It touches everything, fixes nothing anyone is blocked by, and the real defect (four hidden dispatch mechanisms) is untouched by renaming the word |
| R1 §10 | Fuse the four lit solids into one "Solid" | **WITHDRAWN.** Hides four visible pictures behind a mode |
| R1 §10 | Fuse Star + Polygon into one N-gon | **WITHDRAWN.** Same objection, weaker |
| R1 §10 | Cull the vortex-field warp | **REVERSED — fuse into Swirl as a second mode.** Redundancy is never an argument for deletion, only for fusion. R1 wrote the right answer as an escape clause and then didn't take it |
| R1 §10 | Cull Fork Blast "with a migration note" | **Straight delete, no caveat.** Best-evidenced deletion in the tool |
| R1 §10 | 3D Playback: finish, demote, or cut | **Cut, or move it out of Pyre.** The three-way choice collapses |
| R1 §5 | "Simplify the mask system rather than extend it" — labelled R1's strongest argument | **WITHDRAWN — unsupported.** The only thing carrying it was a usage count. **The *rename* recommendation in that section survives on its own merits** |
| R1 §5/§10 | The two height techniques are "a merge of unequals" (8 uses vs 0) | **STRUCK.** They share one lighting routine; it is a merge of equals |
| R1 §6 | **Five** attachment stages | **FOUR.** The edge stage has no consumer anywhere |
| R1 §6 | A standalone-stack warp is "centred on the canvas" | **Understated — it is centred on the canvas's top-right corner, and that is a bug** |
| R1 §8 | "Until Shaper can export, no integration of any shape is possible" | **FALSE** under the owner's ruling. Nothing crosses a process boundary |
| R1 §8 | A Shaper model becomes an imported reusable Silhouette asset | **REVERSED.** Its underlying reason — Shaper's parts must never become Pyre layers — survives and still binds |
| R1 §11 | "Retiring something is not finished until the saved files referencing it are migrated" | **CANCELLED as a general principle**, loudly, by the owner's disposable-assets ruling |
| R1 §1, §5, §13; R2 §1, §10, §14 | The **entire "uncomfortable finding" sections** in both reports | **VOID AS WRITTEN.** Both inferred discoverability from usage. **The conclusion survives completely and is stronger stated structurally** (see A1) |
| R2 §1, §12 | The route is safe because six of seven slices cannot break a saved effect | **VOID as an organising principle.** Re-order by *biggest unknown first, then dependencies, then cost* |
| R2 §7.1 | "For all nine scene generators the fill decides the shape" | **FALSE.** Zero are fused; four don't even touch the palette |
| R2 §7.1 | Opacity travels with the shape, full stop | **SUPERSEDED by the veil** |
| R2 §7.3 | Substance needs an "emission" channel | **DROP IT** |
| R2 §7.8 | **Two** generators colour before downsampling | **THREE — and it missed one of the two most affected** (Arc Burst, five hard colour bands). Meanwhile one of the two it named blends continuous ramps and is the *least* sensitive |
| R2 §6 | One generator (the imported sprite) is genuinely inseparable | **RETRACT** |
| R2 §12 slice 5 | Inferno's opt-out flag proves resampling ≠ per-sample warping | **MISREAD — it is a double-application guard** (`InfernoForm.cs:17-19`, `PyreForm.cs:197-201`). **That equivalence question is still open, not settled.** Test it; do not assume it in either direction |
| R2 §1 | "A rewrite is the one route that actually fails here" | **Survives only for the nine ported effects.** For Pyre's own core it is now open |

## C2. Errors D found in R1's draft (all corrected in the published R1, except where noted)

1. **"Two saved layers point at a retired generator slot and are silently drawing plain Discs." — FALSE. Zero layers do this.** Four layers carry `shapeForm: 17`, but all four also carry a non-null `form`, which wins at `PyreRenderer.cs:248-252`. Measured count of retired-slot layers with `form == null`: **0**. This was the only claim in R1 alleging live data corruption. It was replaced in the published R1 by the *true* version — see E9.
2. **"Fifteen warping effects" and "11 recolour effects" — WRONG. Seventeen and twelve.** The correct stage split is Geometry 17 / Pixel 12 / Post 10 / Edge 1 / Sim 1 = 41. ⚠ **The published R1 §2 badge table STILL says "Explains 15 effects silently doing nothing on 8 generators."** That instance was never corrected. Do not carry it forward.
3. **"Five post effects draw outside the silhouette — bloom, outline, drop shadow, chromatic aberration, fake light." — FOUR. Fake light is NOT one of them.** `RelightModifier` explicitly overrides `OutwardReachPx() => 0` (`SpriteFxRelight.cs:401`), with the comment *"the light never paints into empty space"*.
4. **"Fire can use the swarm as multiple emitters; Fireball cannot. That asymmetry is invisible and looks like a bug." — Not invisible.** The UI states it explicitly (`PyreWindow.cs:2179-2186`). The design recommendation is untouched; only the justification was wrong.
5. **"The melt-into-a-blob control is offered on every plug-in scene, both fire sims and 3D Playback; only the fire case warns." — INVERTED for fire.** For both sims (without emitters) the control is **not offered at all**. The genuinely unwarned case R1 missed is **Fire with `fireSwarmEmitters` ON**.

**D's overstatements in R1:** the cut-out mask was said to be "used by 3 layers" — effective usage was **zero on both paths** (moot now, void by ruling); "ten of seventeen never used" was true only under the looser of two readings (eleven never *rendered*); "over half the built-in list" for the 8 bend-blind generators overstates; a second matte exception (the height consumer) was dropped; "twelve patterns" for Shaper is 11 real ones plus `none` (⚠ **still uncorrected in the published R1 §8**).

## C3. Errors G found in R2's draft (all corrected in the published R2)

1. **"Seven of the nine scene generators — the fill decides the shape."** Two different sevens were merged, and one is not seven. The `UsesFill => false` set is genuinely 7 of 9 (Inferno and Fork Blast *do* use the layer fill). The "coverage decided in the shade pass" set is **all nine**. The causal link R2 drew between them does not exist. *(And note C1: R3 later demolishes the whole claim anyway — zero are fused.)*
2. **"The substance value means four things." — FIVE.** The dropped one is Fork Blast's puff parameter (`PyreForkBlast.cs:394`).
3. **"All six lit solids." — FIVE** (Gem, Box, Pyramid, Can + built-in Orb). Ring is a separate routine and was already counted separately, so the MECHANICAL row summed to 14 against its own stated 13.
4. **"The kaleidoscope's own documentation says it was put in the wrong stage."** The doc says the **opposite framing** — Post is presented as the *right* stage given the constraint, *"the only place that is universal"*. R2 promoted E's interpretation into a quotation of the source. **And "half the generators" was invented** — the comment names **three** (MetaBlob, Height balls, Fire).
5. **"Per-particle lifetime fades are on the overwhelming majority of authored layers." — 41.6%, a plurality.** The 78% belongs to **Disc**, not to the ramp. The draft folded an adjacent statistic onto the wrong noun. *(Void by ruling now, but the folding-error pattern is the lesson.)*
6. **"Three of 3D Shaper's four combine modes are already there." — TWO of four** (union, carve). **Both** soft modes are missing. Pyre's arithmetic Add has no Shaper counterpart, so it is a Pyre extra, not a third match. R2 then contradicted itself four sections later by correctly listing five member modes.
7. **"The fake-depth effect provably never moves the silhouette at all."** The invariant holds only at **default settings**. `r` is normalised to the **lens** radius (`radiusOverride`, `:3119-3123`), and the lens centre is offset by two dials (`:3121`). Its own tooltip advertises the opposite case. The *reclassification* stands; "provably" and "at all" do not.
8. **"None of the seven slices breaks a saved effect." — Slice 3 does.** Giving `SpriteFxRecolor.cs:167`'s hardcoded zero a real value **switches a currently-inert effect on**; re-basing `SpriteFxBurst.cs:115` from canvas-local to shape-local **re-tunes** Tint's cross gradient and Voronoi crack's spread mask in every saved standalone stack. *(Void as a selling point now, but the behaviour change is real and still matters for regression planning.)*
9. **"Twenty-two of the 41 operate on geometry or coverage and never look at the colour underneath." — false for at least three of them.** Fuse box-blurs the premultiplied RGBA frame and lerps colour by `bleed`; Kaleidoscope repeats the layer's finished pixels; Dissolve is a Post *specifically* so smoothness can see neighbour pixels. The honest phrasing is "never *need* the colour underneath to decide what they do".
10. **"The 41 was independently verified twice / reconciled three ways."** The **41 was never miscounted** — the *sub-counts* were. And the draft made two different claims about the same fact in one document.
11. **"The melt-together effect's colour-bleed dial becomes meaningless."** It does not. `colorBleed` is a per-pixel lerp toward the blurred colour across the whole affected region, not an inter-shape control; with one fill it still blurs that fill's own internal variation.
12. **Two different denominators for the same zero** (79 vs 305), three sections apart, never explained — and "on this machine" was wrong, since all four project copies are on this machine.

**G's overstatements in R2 worth not repeating:** "exactly two fill generators" is **decision-dependent, with nine effects genuinely contested** (see D1 in section D); "every energy-field generator blurs its field so the visible extent is always larger" overstates; "the maths for soft blending is already in the codebase" glosses a direct disagreement between two evidence files; "for no reason anyone wrote down" about the border gate is wrong (three generators already draw their own rim); "the lit solids are not the expensive case" overstates; "each slice ships value on its own" is contradicted twice inside R2 §12 itself.

## C4. The 17 errors O found in R3's draft — **all corrected in the published R3**

These matter because the *pre-correction* forms are the ones a careless fourth document would re-import from the evidence files.

| # | The wrong claim | The truth |
|---|---|---|
| **E1** | "The four tangles are properties of the rebuild, not of the old Pyre" — called *"the single most important sentence in this section"* | **Only two are.** The Matte panel and its grouping model, and the blob-melt control, were **inherited** from the old Pyre and copied deliberately: `daa6278d`/`e6c9b841`/`f17d34b7`/`ca44aad1` all land 19:07–21:06 on 2026-07-23, *before* the rebuild's first commit `23c13700` at 22:21:31; and `aa707636` is literally titled *"PyrePlus: Matte is per-layer in the layer list (Pyre1-style)"*. **The conclusion gets stronger, not weaker** — that commit is documentary proof a copy-as-mold rebuild re-mints the old model on purpose |
| **E2** | "Move the recolour stage out of the draw loop — **zero behavioural cost**", and it was **action item 5** | **False, three ways.** `ApplyPix` (`PyreRenderer.cs:1343-1350`) runs on each particle's **pre-composite** colour and its result is then alpha-composited (`:3089-3092`). (a) Non-linear kernels (posterise, contrast, colour replace, ordered dither) differ whenever particles overlap. (b) Even linear kernels differ once anything lies underneath: `Over(dst, c·k, a) = dst(1−a) + c·k·a` vs post-scaling's `dst·k·(1−a) + c·k·a`. (c) `ApplyPixel` can return `false` to drop a *particle's contribution*; a post pass would drop the *whole layer's* pixel — Wipe would wipe the layer. **Correct action: add a buffer fallback for the four generators that lack a per-pixel stage; leave the in-loop stage exactly where it is** |
| **E3** | "Six of the nine big effects already hand out **exactly these sheets under exactly these names**" | The mechanism is real (`IPlusFieldPublisher`, `PyreForm.cs:156-159`) and **eight** of nine publish — but what they publish is `H`, `T`, `C`, `ramp_t`, `alpha`, `alpha_f`, `rim_mix`. **"How old this pixel's stuff is" is published by nobody. "Which way the surface faces" is published by nobody.** Distance-from-edge exists on one generator only (PlasmaBloom's `rim_mix`) and is a **crossfade weight, not a distance**. Two of the six sheets the design is built on are **construction, not promotion** |
| **E4** | "All eleven already compute coverage, and **six** already publish it" | **Four** publish an alpha plane: Orb (`alpha_f`), Torch (`alpha_f`), ArcBurst (`alpha`), PlasmaBloom (`alpha`). Fork Blast publishes `H,T` only; the Jet family `H,T,ramp_t` only. So the "monolithic contract asks for nothing new" pitch is not free for at least four generators |
| **E5** | Swarm buckets: *"of **28** generators, 21 (75%) / 5 (18%) / 2 / 1"* — which sums to 29 | Fire is double-counted in the evidence table (INERT *and* MULTI-SEED). Against the true **29** the figures are **72.4% / 17.2% / 6.9%**. The error was invisible because the percentages were computed against 28 while the counts enumerate 29, so they still summed to 100% |
| **E6** | *"In four of the nine … in the other seven"* — 4 + 7 = 11, not 9 | The denominator silently switches mid-sentence. **Of the nine scene generators, four are palette-independent and five are the product form; adding the two fire sims gives 4 + 7 of eleven** |
| **E7** | "The old Pyre received **zero** feature commits across the entire 33-day window" — filed under *Measured and solid* | **`4523a595` (2026-07-23 23:01, forty minutes after the rebuild's first commit)** is a pure old-Pyre UI commit. Two more are borderline (`661cba5d`, `df35566f`). **The conclusion survives; the absolute survives only as "essentially none"** |
| **E8** | "The **two** empty leftover folders" | **THREE:** `Runtime/PyrePlus/`, `Editor/PyrePlus/` **and `Assets/Tests/PyrePlus/`**. Inherited from `CLAUDE.md`, which also says two — **the project instructions are wrong here** |
| **E9** | "**Nine** effects read nothing from the generator at all" | **Six** read literally nothing. Colour remap reads `info.life` (`SpriteFxColorRemap.cs:185`); Wipe reads `p.hash` (`SpriteFxBurst.cs:234`) in its `MaskNoise` shape, 1 of 9. So Wipe is buffer-runnable for 8 of 9 shapes and changes character on the ninth |
| **E10** | "**Two** of the blast effects place blasts that know they are siblings and bias each other's fire-vs-smoke character" | **Only one.** Inferno does it in full (`PyreInferno.cs:293`, `:304-307`, `:313-314`). **Fork Blast has no `fireMul`, `smokeMul`, `charDrift` or `charJitter` anywhere** — its `Ev` struct is `cx, cy, start, duration, scale, seed` (`:119`). It is sibling-aware for **scale only** |
| **E11** | The fireball's blocker is "**four** missing numbers" | **Two.** `FireballParams` (`FireballSim.cs:20-30`) has 8 fields and no position; the fix is `sourceX`/`sourceY` |
| **E12** | "They receive a size multiplier and an orientation and **five of them use it**" | Two counts collapsed into one wrong one. **`sizeMul`: seven forms use it. `orientDeg`: four** (Torch + the Jet family; zero hits in ArcBurst, Orb, PlasmaBloom, Inferno, ForkBlast). "Five" was the *ignored* count read as the *used* count |
| **E13** | "'How far across this shape am I' is currently computed **six different ways**" | **14 assignment sites, 7 distinct expressions.** The six cited sites are all the *same* formula with differently-named extent variables. The genuinely different ones were **all omitted**: Streak (`:3773`), Ring's annulus remap (`:4762`, `:4777`), the facet solid's box extent (`:4045`) |
| **E14** | "Shaper's 3D is a height map **lit from one fixed direction that cannot be rotated**" | **The lighting half is false.** `renderModelGrid` builds a **multi-light rig from user-placed lights**, each with position, height, colour and intensity, weighted-summed with per-light Lambert and a per-light shadow ray-march. The fixed upper-left direction is only the no-lights fallback. **What is actually fixed is the VIEW** — there is no camera or view matrix anywhere. The carve-out's conclusion survives (Pyre does real 3D rotation with back-face culling); the stated reason was wrong |
| **E15** | "Shaper's materials and lighting — about **214 lines**", "which overstates it by roughly double" | 214 is `renderModelGrid`, which is the **rasteriser**, not materials and lighting — and it is **211 total / 182 code**. The 98/26/124 figures are non-comment counts; 214 was a with-comments count, so the comparison mixed conventions (apples-to-apples: **124 : 182**). The actual material/lighting code is scattered *outside* it. **The "overstates by double" claim is itself inflated** |
| **E16** | "Every call site feeds the ramp's **animation position** a zero" | **Misidentifies what is zero.** `ZuiFill.Evaluate(float life, float u, float v)` takes a life clock and a **spatial sample point**; what every Pyre site passes as zero is `u, v`. That is why spatial and noise fills collapse to one sample. **And the claimed clock separation does not hold in the plain over-life mode** — `case Mode.OverLife: return HasGrad ? EvalGrad(Clamp01(life), life) : color;` (`ZuiFill.cs:178`) passes the *same* `life` as both. Decoupling them needs a **signature change to a shared ZUI type other Laubrary tools consume** — an engine change, not a call-site change |
| **E17** | "**Five** of the eleven generators already thin the shape via the fill" | **Seven** — the three Jets (`PyreJetEngine.cs:760`), Fork Blast (`PyreForkBlast.cs:388`), Inferno (`PyreInferno.cs:783→799→800`), Fire and Fireball (`FireSim.cs:304`). **Arguably eight**, counting Sprite with tinting on. Correcting it *strengthens* the veil argument |

**O's overstatements in R3's draft, all softened in the published text:** the "not one generator is fused" claim had **no stated denominator** (8 read line by line, 11 counted, 29 in the library); **"the three jets are one engine" does not distinguish the surviving fusions from the withdrawn ones, because the four lit solids are ALSO one engine** by the code's own comment (`PyreRenderer.cs:2844`, `Pyre.cs:73`) — and R3 never ran the jets through its own gate question 3; "about double" for the pivot-reach ratio is one worked example, not a general figure (a 4-px particle on a 64-px canvas is **8×**); "all three exit through the same downsampling function" holds only below ~128 px canvas (ArcBurst takes a hand-written direct-write branch above that); the sprite separation is not "one line" (tinting adds a coverage term); "the emission channel buys nothing" overstates (it bought the additive case, which R3 has to re-buy with a blend-mode flag); "about a week, and that's measured" — the cited evidence supports **one day**; "124 lines of arithmetic" is a raw contiguous line range at ~75 chars/line with a 311-character line, **excluding a required dependency** (`transformParam`) and **including ~5 lines that are not silhouette work**; "every one of Pyre's flat shapes is an inverse-transformed point test" has **three exceptions** (Disc's separate fast path, Sparkle, Sprite) — and the Sparkle exception contradicts R3's own carve-out four paragraphs later; "seven of the nine loop over the swarm" is true at the form level but there are **five distinct loops**, and **all nine** loop it; "each of the seven invents its own seed with a different magic number" — there are **two** magic numbers, not seven.

**O's omissions from R3's draft, all since added — except two:**
- ⚠ **M5, never added:** the dead edge stage has a **fourth** exclusion site, `Editor/SpriteFx/SpriteFxStackView.cs:343`, which excludes it from the standalone editor's add-menu too. It is unreachable in **both** hosts, which strengthens "give it a real host or delete it".
- ⚠ **M7, half-added and now internally inconsistent:** O said the visual baseline was ordered after three output-changing edits and had to move first. The published R3's action list **does** put "render one frame per generator" at **item 2** — but the published Confidence section still says *"that's why 'render one frame per generator' is **item 6** on the list above."* **The published R3 contradicts itself on this point.** The action list is right.

**O's quibbles worth knowing:** R3's evidence-file bookkeeping was inconsistent three ways (11 / 13 / actually 14 at the time; total **~667 KB**, not the 544 KB claimed — the understatement ran *against* the section's own argument); "roughly 850 MB of project" is **957 MB** excluding Library; "129 modified and 71 untracked" is **72** untracked; ⚠ **the working notes name the wrong near-duplicate for a universalised edge warp** — it is `SunburstWobbleModifier` (`:339`) or `TurbulenceModifier` (`:2653`), **not** the plain `WobbleModifier` (`:322-327`), so an implementer following the notes will chase the wrong class; and ⚠ **line-number drift runs throughout evidence files H–N** — Jet family cited 4–5 lines off, Inferno ~5–9 off, edge hooks `:2568/:2575` vs actual `:2570/:2576`, `DrawParticle` `:280` vs `:279`, sprite alpha `:3603` (an early-out) vs the real `:3611`. **The findings hold; the exact line numbers often sit a few lines off.**

## C5. Everything voided by the owner's rulings

**Ruling 1 — "nothing Pyre or 3D Shaper has produced needs to survive."** Cancels: the word "migration" from the vocabulary; R1 §11's general principle that retiring isn't finished until saved files are migrated; R2's entire route-ordering rationale. **Does NOT endanger the ported effects' fidelity tests** — those 104 tests assert against an outside original, not against saved Pyre assets, and `Forms/Kiln` is already its own assembly. ⚠ **Open: whether it covers the committed demo scenes in `Assets/Demos/` and the Pyre output other tools consume** (`IChunkAnimation`/`PyreChunkAnimation`, Mirage, Zoe effect palettes). All three reports assume the narrower reading.

**Ruling 2 — "usage counts say nothing about whether tech is good."** Every one of these is now **inadmissible evidence** and must not appear in the fourth document as an argument, only as a withdrawn artefact if at all: 123 saved files · ~305 layers · 32 distinct effects · 79 distinct layers · 78% Disc · 10 (or 11) of 17 built-ins never used · 4 of 9 plug-ins never used · matte master switch off on all 305 · "the library grew 10× faster than it was used" · 20 of 305 (coalesce) · **zero of 305 (grouping)** · exactly 1 of 305 (border) · 127 of 305 = 42% (lifetime fades) · 8 vs 0 (the two height techniques) · 13 further layers in per-project layer libraries · 2+2 assets using Smudge/Swirl · three mask sources that are three clones of one layer.

**What replaces them, in descending strength (R3 §2):** reachability → correctness → documented supersession → technical distinctiveness → redundancy → maturity (does the author's own comment call it a stub?) → cost of keeping → explainability. **Two rules for using it: redundancy is never an argument for deletion, only for fusion; and never infer discoverability from usage, in either direction.**

**Ruling 3 — the anti-pattern-chasing gate.** Any future change must pass five questions before it removes or renames anything: (1) **Name the person and the moment** — who was blocked, doing what? (2) **Would the old name still be true?** (3) **Does it remove or hide a picture** — including "could only produce it after one extra decision"? *Consolidation is subject to this test.* (4) **What's the residue budget, and who declares it paid?** (5) **Is the evidence a fact about the code, or about taste?** **Admissible:** is the control constructed, is the hook called, is the value hardcoded, did the author write that this supersedes that. **Inadmissible:** "nothing uses it", "it looks messy", "it doesn't fit the pattern", "that would be fewer entries."

**Ruling 5b — drawing outside the shape and outside the frame is allowed.** Kills R2's emission channel and kills every conclusion that gated an effect on "it must stay inside the shape". **Two things survive:** the frame edge is real code (every generator writes into a fixed-size buffer, so genuine out-of-frame extent needs a **padded working buffer** — and padding is a **precondition** for the owner's own red-pixels warning preview, since the renderer cannot paint out-of-frame pixels red if it never computed them); and **additive light is not expressible by coverage alone** (two overlapping half-covered haloes composite to 75% and add to 100%), so **a fill must declare whether it blends over or adds**.

**Two stale lines in the project's own `CLAUDE.md`, both confirmed:** (a) the Pyre rename is described as *"staged but uncommitted, ~209 staged renames"* — **nothing is staged; it was committed 2026-08-25** (366 files, 203 renames, +76,456 / −106,076). The caution is still correct but for a different reason (129 modified / 72 untracked), and an agent obeying the stated reason may try to reconstruct a staged state that does not exist. (b) the *"open packaging gap: ZUI lives outside the package"* is **no longer true** — `Assets/ZUI/` holds exactly two files; ZUI's **132 `.cs` files and three asmdefs** live at `Assets/Packages/Laubrary/Zui/`. (c) `CLAUDE.md` also says **two** empty PyrePlus folders; there are **three**.

---

# D. Open questions — awaiting the owner

**From R1 (six, all still live):**
1. **Shape and material: two fields on one layer, or two linked layers?** The first is closer to Pyre today; the second is closer to Shaper and composes better. Shapes everything downstream.
2. **Should melt-into-a-blob keep discarding the chosen shape?** Today a Star with melt on gives you circles.
3. **3D Playback: finish, demote, or cut?** *(R3 recommends cut; the owner has not ruled.)*
4. **The unreachable auxiliary-map gate: wire it up or delete it?** ~154 lines, fully built, fully documented, attached to nothing.
5. **Does the enormous plug-in scene library match how you want to work?** They carry the overwhelming majority of Pyre's dials — the argument is that they need **presets and a browser** far more than more dials.
6. **Do you want Shaper's boolean silhouette composition inside Pyre eventually, or is Shaper a one-way pipeline forever?**

**From R2 (five):**
1. **Is a fill evaluated per pixel, or over a whole buffer?** *R2 calls this the most consequential open question in the document.* Per-pixel → only the cracked-cell pattern qualifies as a fill generator. Whole-buffer → the fake light, the glow and one of the shockwaves become fills too.
2. **Does light belong to the shape or to the fill?** It should be the fill — otherwise plasma on a box renders flat-lit — but it moves real work. The lit solids already carry **five separate fills each**, and the code already calls one of them the material fill.
3. **In a group, is the fill applied per member or over the assembly?** Per member is today and keeps lifetime colour fades; over the assembly is the proposal and loses them unless a **per-pixel age** is added.
4. **How much fidelity are you willing to spend?** Three generators anti-alias after colouring; handing over at their internal resolution preserves them at **4×–9× buffer memory**, paid only by them.
5. **Does the rich-fill promise apply to the scene generators at all?** "Brushed metal on an explosion" is mostly a meaningless cell. Should generators **recommend** fills and mark the rest as unusual, or is orthogonality the promise?

**From R3 (seven):**
1. **Does a fill's veil default to "no opinion", and can a generator forbid one?** R3 recommends yes and no. This decides what "any fill on any shape" actually promises.
2. **Do you want the per-instance clock fixed?** Five of seven big effects start instances on the shared clock, so they pop in mid-animation. One line each — but it changes what they render.
3. **One ordered effect list, or four typed ones?** In direct tension with R2 slice 2. R3 leans **one list + honest greying-out**.
4. **Is the lit-solids family in or out of the Shaper ruling?** They do real 3D rotation, which Shaper structurally cannot. R3 recommends they survive as their own technique — which means "Shaper is the only way to make primitives" has a named exception from day one.
   - ⚠️ **SUPERSEDED 2026-08-30 — "which Shaper structurally cannot" is wrong.** This digest predates the investigation that reversed it, and the clause is left standing only because this file is a record of what the earlier reports said, not a live position. Shaper **can** do real 3D rotation: stop keeping a picture of stored heights, keep the rule that says what is inside the shape, and follow a line into the scene for each dot — the tool already contains that rule in full, and no primitive has to be re-expressed. Measured cost is roughly seven to eleven times a flat render (best case; curved profiles cost more), and exactly the same as today at the straight-down view. The current answer is **section B7 of `SHAPER_THE_DESIGN.md`**, which rules that the first version still resolves flat — by decision and for scope reasons, **not** by impossibility — with the general path designed in from day one. The recommendation *in* this item is unaffected: the lit-solids family still survives as its own technique, and the named exception still exists from day one.
5. **How much resolution memory are you willing to spend?** (R2's Q4 restated with the corrected count of three.)
6. **Do you want to keep the standalone effect stack as a separate tool at all?** Several corrections here are really about Pyre and the stack being two hosts for one effect library that **disagree about pivots, ordering and canvas growth**. It keeps surfacing.
7. **Does "all the assets are disposable" include the committed demo scenes and the Pyre output other tools consume?** R3 assumed not.

**Also flagged as needing a decision, not a question but a design lock:** the group model needs seven things answered on paper before anything is built — visible ordering (carve is not commutative; put the combine mode **on the member**), a real per-member local transform (Pyre has **no per-layer transform at all** today), whose clock the fill runs on (**the group's**), whether a member may swarm (**yes, contributing its whole cloud as one contribution**), whether a group can also be a mask (today a layer is a writer or a consumer, never both — and a group is a consumer by construction), which soft-combine control is the target (Shaper's two-knob or Pyre's one-knob), and cost (eight members = eight full-canvas passes per frame per group).

**And two locks the design document must state before a line is copied:** whether the existing `PyreForm` plug-in contract is unchanged — verify by checking that two of the big effects satisfy the new contract **unmodified**, because it decides whether the ported effects can be *shared* rather than copied — and a written list of what is deliberately **not** carried across, with a reason each, because silence there is how things get carried by accident.

---

# E. Live bugs found along the way — **none are fixed**

| # | Bug | Location | Status / note |
|---|---|---|---|
| **E1** | **The standalone effect stack pivots every shape-centred warp at the canvas's TOP-RIGHT CORNER.** `RunWarp` passes `new Vector2(hHalf, vHalf)` as `GeoCtx.center`, into a field documented and used everywhere else as an **offset from the canvas centre** (`PyreFormWarp.cs:34` passes `Vector2.zero`; `PyreRenderer.cs:3016-3017`; type doc `SpriteFxModifiers.cs:178-179`). Consumers compute `off − ctx.center`, which zeroes at `(W−0.5, H−0.5)`. Row 0 is the bottom row (`PyreFormWarp.cs:13`), so it is the **top**-right | `SpriteFxBurst.cs:683` | **LIVE. Confirmed by O including the corner and the blast radius: exactly 10 of the 17 warps** — Sunburst wobble, Ring wave, Point blast, Profile, Ground, Sunburst, Pulse rings, Turbulence, Curl, Sphere. **Found by reading, not reproduced — confirm with one rendered frame before touching it, because the fix changes saved stacks** |
| **E2** | **Editing the colour ramp of a Fire, Fireball or Text layer may do nothing at all.** Those call sites read the **frozen legacy `ZuiFill.gradient`** while the editor writes `ZuiFill.gradientAnim.gradient`; the two stop being the same object the first time the ramp control fires. Worse: the change-detection that decides whether to re-render reads the **same stale copy**, so it would not even notice | `ZuiFill.cs:105-107` and the Fire/Fireball/Text fill reads (G §34: **three generators, four call sites**) | **LIVE, "almost certainly". Confirmed by code path only; NOT reproduced in the editor, and it should be before it is fixed.** It is the proposal's own case study: four call sites that reach *past* the fill instead of *through* it |
| **E3** | **Pyre never calls `SetPicture`.** The only invocation repo-wide is `SpriteFxBurst.cs:656`; `SetPostContext` sets only life/seed/frame. So any post pass that normalises a position measures against the raw buffer rather than a picture rect (`SpriteFxRelight.cs:472-474` falls back to `W,H`) | `PyreRenderer.cs:1379-1394` | **LIVE but currently harmless**, because Pyre never pads its buffer either. **It becomes immediately live the moment padding exists — which is exactly what universal effects and the out-of-frame ruling both require. Fix it in the same change, not afterwards** |
| **E4** | **The edge stage is dead.** `EdgeOffset` / `EdgeSoftness` have **zero invocations repo-wide**; all hits are declarations or overrides. Excluded from Pyre's collector (`PyreRenderer.cs:1272`), Pyre's add-menu (`PyreWindow.Modifiers.cs:384`), the standalone stack's dispatch (`SpriteFxBurst.cs:640-668`, no default — so a saved `EdgeModifier` is silently ignored) **and** the standalone editor's add-menu (`SpriteFxStackView.cs:343`) | `SpriteFxModifiers.cs:2570`, `:2576` | **LIVE (dead code, one orphaned effect).** Unreachable in *both* hosts. Give Edge warp a real host or delete it |
| **E5** | **Pixel fluid is offered in the standalone stack's add-menu and does absolutely nothing there.** `SpriteFxStackView.Catalog()` filters only `EdgeModifier`, so `PixelFluidModifier` appears (under "Colour & mask", since it is neither Geometry nor Post) — and `RunStack`'s switch has no `SimulationModifier` case. `SpriteFxSpec.cs:32` has a single flat modifier list with no simulation slot | `SpriteFxBurst.cs:640-667`, `SpriteFxStackView.cs:329-380` | **LIVE, reachable, silent no-op** |
| **E6** | **Pin warp can never animate.** `PinWarpModifier.SetFrame` has exactly one repo-wide hit — its own declaration. Its doc names `BlastRenderer`, deleted in the 2026-08-23 rename. Every keyframed pin holds frame 0 forever | `SpriteFxModifiers.cs:3350` | **LIVE. Carries the cull verdict on its own**, with or without a usage count |
| **E7** | **The melt-into-a-blob (Coalesce) control is offered where it can never act, with no warning.** Drawn whenever `swarmEnabled`; read only inside `RenderSwarm`, which `RenderLayer` returns before for plug-in forms, Fire, Fireball and 3D Playback. **Inert set: every plug-in scene, 3D Playback, any swarm-off layer, and Fire with `fireSwarmEmitters` ON** — that last case is the genuinely unwarned one | `PyreWindow.cs:2206`, `:2492-2495` vs `PyreRenderer.cs:1497-1498`, `:248-269` | **LIVE.** Independently corroborated by H |
| **E8** | **`crossFrac` means five different things**, one of which is a **hardcoded `0f`** that silently kills Tint's cross gradient in that host | `PyreRenderer.cs:1346` · `SpriteFxBurst.cs:117` · `PyreInferno.cs:820` · `PyreForkBlast.cs:394` · **`SpriteFxRecolor.cs:167`** | **LIVE correctness bug.** Fixing it **changes output**: it switches a currently-inert effect back on, and re-bases a value from canvas-relative to shape-relative, re-tuning every standalone stack using Tint or Voronoi crack |
| **E9** | **Two retired modifier classes are still referenced by saved assets and now deserialize to null.** `PerlinTurbulenceModifier` and `AlphaMaskModifier` (folded into Turbulence at `SpriteFxModifiers.cs:2644` and into Wipe at `:10`/`:1279` on 2026-08-15) still appear as `SerializeReference` entries in three saved assets in a sibling project | `SplashText/Assets/Pyre/Fluid Blast.asset`, `.../PyrePlus/Imported/Fluid Blast Plus.asset`, `.../Pyre/PyreLayerLibrary.asset` | **LIVE.** *(Under the owner's disposable-assets ruling this stops being a blocker, but it is real and it is the live precedent that retirement without migration produces null modifiers in live assets)* |
| **E10** | **Pyre clips the four effects that draw outside the silhouette.** Pyre never asks an effect how much room it needs, so **Bloom, Outline, Drop shadow and Chromatic aberration** are cut off at the canvas edge in Pyre but not in a standalone stack. **Fake light is NOT one of them** (`OutwardReachPx() => 0`) | `SpriteFxModifiers.cs:1790`, `:1891`, `:2105`, `:3389` vs `SpriteFxStack.OutwardReach`, `SpriteFxBurst.cs:492` | **LIVE.** Same effect, same settings, genuinely different picture depending on where it is used |
| **E11** | **Text falls back to Disc at runtime.** The font auto-pick is `#if UNITY_EDITOR` only, so a null `layer.textFont` silently degrades in a build | `PyreRenderer.cs:3841-3848`, `:3843-3844` | **LIVE behavioural trap.** Works fine with an explicit readable font |
| **E12** | **The 3D Playback editor preview replaces the entire composited canvas.** While authoring one you cannot see your other layers, your mattes, or anything else in the stack | `PyreWindow.Preview.cs:94-96`, `PyrePlayback3DPreview.cs:5` | **LIVE (editor-only)** |
| **E13** | **The four blocked-effect messages point at a deleted window.** Smudge, Swirl, Vortex field and Pin warp are all blocked in both add-menus with a message telling the user to author them in a window deleted in the rename | (no line cited in the reports) | **LIVE.** Worse than saying nothing |
| **E14** | **`PyreFormKind.PerParticle` and `.Stateful` are declared, documented and never dispatched.** Every real form is `WholeLayer` | `PyreForm.cs` | **LIVE dead API surface** — it makes the plug-in contract look more capable than it is |
| **E15** | **The auxiliary-map gate is fully built, fully documented and completely unreachable.** No field of its type exists anywhere in `Assets/`; its doc cites a `SpriteFxStack.RunStack` mechanism that does not exist. ~154 lines with 12 authored dials and a `previewHeatmap` mode | `SpriteFxAuxMapGate.cs:35` | **LIVE dead code.** Wire it up or delete it — owner's call (D-question 4) |
| **E16** | **Dead citation:** `Pyre.cs:95-96` cites `Runtime/Pyre/BlastRenderer.cs:1493-1496` as proof HeightBalls is closed-form; `PyreRenderer.cs:1691` and `:1602` cite the same non-existent file. **`BlastRenderer.cs` does not exist** | as cited | **Doc bug.** The *claim* is still true — independently re-verified from `RenderPlusRampField` |
| **E17** | **Two more stale docs:** `Pyre.cs:79-80` says Fire "ignores `size` and the swarm" (it gained `fireSwarmEmitters`); `Pyre.cs:611-612` says Playback3D "renders a flat placeholder colour" (it renders nothing — `:262-268` is authoritative). Plus a stale `pyreplus_N` versioning prefix in the baker's collision guard (`Editor/Pyre/PyreBaker.cs:19`) and **seven misnamed `Plus*` test files** | as cited | **Doc/cosmetic bugs**, worth a cleanup pass whatever else happens |
| **E18** | **The branch `feat/lathe` exists on no remote.** `git branch -r --contains 60309923` returns empty; no upstream; `origin` carries only `master`, `release`, `SmartStats`, `GamaSave-system`, `fix-demo-scenes-for-packages` | git | **LIVE, and it is the largest single risk in the repository.** The entire current Pyre — the whole 33-day parallel rebuild — exists on one disk. **Push it before starting a second large rework** |

---

# F. Named quantities — the citable index

Every figure below is the **current** value after all three verification passes. Where an earlier report published a different number, the superseded value is shown struck.

## Structure

| Quantity | Value | Established by |
|---|---|---|
| Selectable generator techniques | **29** = 17 live enum + 9 plug-ins + 3 field-pass | F; arithmetic confirmed D §5 |
| Dispatch mechanisms deciding what a layer draws | **4**, only 1 in the picker | A §0, restated R3 §2 |
| Live effect-attachment stages | **4** (~~five~~) — the edge stage has zero consumers | R3 §9, verified O |
| Decomposability split of the 29 | **clean 14 · mechanical 13 · entangled 1 · not-a-shape 1** | F §35 |
| Generators genuinely fused (shape = colour) | **0 of 11** examined line by line; 0 of 29 by classification | R3 §7, verified O in four loops |
| Big effects where the palette is provably irrelevant to the outline | **4 of 9** (Orb, Torch, Arc Burst, Plasma Bloom) | R3 §7, O |
| Generators where the ramp's alpha column multiplies into coverage | **7 of 11** (~~five~~), arguably **8** with Sprite tinting | O E17 |
| Generators whose fill can already thin the shape today | same 7 (8) | O E17 |
| Generators the border stage is gated to | **6 of 29**; the control is **not constructed** for the other 23 | F §2.2; `PyreWindow.cs:1371` |
| Edits to ungate the border | **3** (`PyreRenderer.cs:950-952`, `PyreRenderer.Layers.cs:92`, `PyreWindow.cs:1371`) | K |
| Generators that already draw a private rim | **3** (lit solids, Text, Fire) — the one surviving border caveat | K, G §22 |
| Plug-ins declaring `UsesFill => false` | **7 of 9** (5 declarations; `JetFormBase` covers 3). Inferno and Fork Blast do use it | G §1 |
| Plug-ins deciding coverage in their shade pass | **all 9** (~~seven~~) | G §1 |
| Plug-ins that use `ctx.fill` | **2** (Inferno, Fork Blast) | A §3 |
| Plug-ins publishing sheets via `IPlusFieldPublisher` | **8 of 9** (Inferno does not) | O E3 |
| Plug-ins publishing **coverage** | **4** (~~six~~) — Orb, Torch, ArcBurst, PlasmaBloom | O E4 |
| Sheets the design needs that **nobody publishes** | **2** — per-pixel age, surface direction. Distance-from-edge exists on one generator as a crossfade weight, not a distance | O E3 |
| Plug-ins receiving `sizeMul` / `orientDeg` | **7** use `sizeMul`; **4** use `orientDeg` (~~five~~) | O E12 |
| Plug-ins giving an instance its **own clock** | **2 of 7** (ArcBurst, PlasmaBloom); the other five use `own` as a liveness filter only | O, exact |
| Distinct per-instance seed constants | **2** (`104729` shared by five, `7919` in ArcBurst; PlasmaBloom has none) — ~~seven~~ | O O11 |
| Distinct swarm loops at code level | **5** (all nine forms loop it; three share `JetFormBase`) | O O10 |
| Swarm compliance buckets over 29 behaviours | **21 (72%) comply · 5 (17%) better native path · 2 need real work · 1 out of scope** (~~of 28: 75/18~~) | O E5 |
| Missing numbers blocking Fireball swarming | **2** — `sourceX`/`sourceY` (~~four~~) | O E11 |
| Empty leftover `PyrePlus` folders | **3** (~~two~~; `CLAUDE.md` also says two) | O E8 |

## Effects

| Quantity | Value | Established by |
|---|---|---|
| Concrete SpriteFx modifiers | **41** (46 `PyreModifier` subclasses − 5 abstract stage bases) | E, D, re-derived by O |
| Per-stage split | **Geometry 17 · Pixel 12 · Post 10 · Edge 1 · Simulation 1** (~~15 / 11 / 12~~) | D §1.2, confirmed E and O |
| Conceptual buckets | **shape ops 22 · fill ops 9 · borders 4 · post survivors 2 · fill generators 2 · cull 2** | E |
| Genuinely contested classifications | **9** — Tint, Colour tint, Ordered dither, Sphere, Kaleidoscope, Bloom, Fuse, Ballistic shockwave, Pixel fluid | E §3 |
| Stage does not predict conceptual bucket | **10 of 41 (24%)** | E §1 |
| Universality buckets | **buffer-only 20 · needs per-sample context 20 · irreconstructible 1** | I |
| R3's published universality table | **11 universal & correctly advertised · 9 universal-capable but mis-advertised · 7 universal via buffer fallback at a cost · 13 need two extra sheets · 1 genuinely stuck** (sums to 41) | R3 §9, re-derived by O |
| Effects reading **nothing** from the generator | **6** (~~nine~~); +3 read trivia (Colour remap → life; Wipe → per-shape seed in 1 of its 9 shapes; Ordered dither → buffer coords) | O E9 |
| Effects declaring non-zero `OutwardReachPx` | **4** — Bloom, Outline, Chromatic aberration, Drop shadow (~~five; Fake light is NOT one~~) | D §1.3 |
| Warps affected by the top-right pivot bug | **10 of 17** — exact | O, verbatim list |
| Distinct meanings of `crossFrac` | **5** (~~four~~), across 14 assignment sites and **7 distinct expressions** (~~six different ways~~) | G §2, O E13 |
| Numbers needed to make the 13 buffer-runnable | **2** — a shape-local coordinate and a stable per-shape seed. Both computed today, both discarded (`crossFrac` 14 sites, `pHash` 11 sites). **Plus a third precondition: a padded buffer** | I, O M2 |

## Code sizes and history

| Quantity | Value | Established by |
|---|---|---|
| Kiln ported effect lines | **10,666** (≈10,700), already in their own assembly `Forms/Kiln/Pyre.Forms.Kiln.asmdef` | O, exact *(K separately measured 7,542 lines across 15 files **excluding** the `Jet/` subtree)* |
| `PyreRenderer.cs` | **5,191 lines** — A explicitly did not read all of it | A §7.2 |
| Largest form engines | ArcBurst 1115 · ExplosiveJetProgram 1005 · Inferno 914 · JetEngine 795 · Torch 668 · Orb 655 · PlasmaBloom 613 · ForkBlast 450 · Playback3DPreview 443 | A §3.x |
| Dial counts | ArcBurst **187** (densest) · Orb **141** · PlasmaBloom **114** · ExplosiveJet **+89** · Torch **73** · JetEngine **60** shared · Inferno **40** · ForkBlast **40** · RadialJet **+10**. *(Dial counts were taken on trust by D; A labels its own method indicative)* | A §3 |
| Jet family presets | **23** total (5 + 8 + 10) | A §6 |
| Fills carried by each lit solid | **5** — surface, specular, inner glow, line, edge glow; the code already calls the first the material fill | F |
| Fork Blast supersession | **47-row** divergence table, every row closed by Explosive Jet | A §3.2 |
| **Shaper's silhouette engine** | **98 lines exactly** (161 total / 98 non-comment), `index.html:981-1141` — all 8 primitives, the bulge warp, transform maths, **all four combine modes including both soft ones**, and the tree compiler with both guards. **Extrusion + bevel = 26 exactly** (`:1144-1171`). **124 total** | O, exact |
| Caveats on that 124 | ~75 chars/line, 92 semicolons in 98 lines, longest line **311 chars**; **excludes a required dependency** (`transformParam`, `:1920`) and **includes ~5 non-silhouette lines**. It is a contiguous range, not a dependency closure | O O8 |
| Shaper's rasteriser | **211 total / 182 code** (~~"materials and lighting, about 214 lines"~~) | O E15 |
| Shaper's whole app | single file, **4,102 lines** | O |
| Pyre test suite | **165 tests** (134 `[Test]` + 31 `[TestCase]`); **104** of them guard the Kiln ports (ExplosiveJet 19 · Jet 17 · RadialJet 16 · Torch 15 · PlasmaBloom 13 · ArcBurst 13 · Orb 11) | O, exact |
| The parallel rebuild | first commit `23c13700` **2026-07-23 22:21:31** ("PyrePlus slice 1: parallel Pyre rework"); ended `60309923` **2026-08-25**. **33 days.** A completed 3-slice prototype landed the **next day** (`879730f9`) | O, exact |
| The rename | **366 files, 203 renames, +76,456 / −106,076**, executed *inside* a 15-commit housekeeping batch | O, exact |
| Rename residue | **7** misnamed test files (`PlusCapabilityTests`, `PlusFieldOps`, `PlusFormWarp`, `PlusFrameFill`, `PlusLayerCache`, `PlusParityDump`, `PlusShade`) and **3** empty folders | O, exact |
| Current tree state | **129 modified, 72 untracked**; `git diff --cached` empty | O |
| Project size | **957 MB** excluding Library (~~850~~); Library **8.8 GB** | O |
| Composed-shape cost risk | 8 members × 200 particles = **640,000** primitive evaluations + 200 tree builds per layer per frame, vs **80,000** square roots for a disc today — **exactly 8×**, both over the same 20×20 particle box | O, verified self-consistent |
| Simulation swarm-wrapper cost | **236 M cell updates vs 1.2 M** at 24 frames × 200 particles on a 64×64 grid — ratio **200× = N**, i.e. asymptotic, not a constant factor | O, verified |
| Resolution cost of bit-identical handover | **4×–9× buffer memory**, paid only by the **3** generators that ask for it | R3 §7 |
| AgentHQ | **31 to-dos, 11 agents, 3 planning tabs.** The prior port work was **17 tasks** under one node (T-0042…T-0058) | O, exact |
| Evidence corpus | **15 files (A–O)**; ~591 KB of evidence + ~92 KB of reports = **~667 KB** (~~544 KB~~) | O Q1 |

## Anti-facts — things specifically checked and found NOT to be true

- **No effect anywhere reads anything a generator wrote.** There is no aux-channel contract; the ones that look like they need depth *invent* it from the finished picture.
- **Pyre's built-in shapes have no anti-aliasing at all** — confirmed for every one, including Text, whose bilinear SDF is thrown away by a hard `if (sd < 0.5f) continue;` (`PyreRenderer.cs:4021`). `PyreSupersample` is referenced **0 times** in `PyreRenderer.cs`. Routing shapes through a general evaluator would be a free win here.
- **Pyre's disc and Shaper's ellipse are literally the same function.** `index.html:1033` reduces at `rx == ry == r` to `hypot(x,y) − r`; `PyreRenderer.cs:2995-2996` is `Sqrt(dx*dx+dy*dy) > radius`.
- **Shaper's star is hardcoded five-lobed with zero parameters** (`index.html:1039`) against Pyre's four animatable dials; there is **no arbitrary N-gon** — its hexagon (`0.55`) and octagon (`1.42`) are hand-fits, not `√3/2` and `1+√2`. Star and polygon **get worse** under a naive Shaper ruling unless the fix is scheduled.
- **Pyre has no per-layer transform at all.** Position comes from particle scattering, rotation from particle spin, scale from the size dial. A group member needs an honest offset/rotation/scale that is not a swarm — small, new, unavoidable.
- **The project's UI guide has no progressive-disclosure rule** (grep for `progressive|disclos|advanced` over `ui-layout-rules.md` and `UNITY_DEV_GUIDE.md` returns zero hits) — but **four** constraining rules exist verbatim (Card-layout rule 5 `:91`; Stable workspace `:66`/`:73`; Label = action `:99`/`:103-104`; Labeling `:95`), and **three shipped disclosure patterns already exist** in the codebase, the third of them enforced rather than just documented (`PyreWindow.Forms.cs:127`).
- **Nothing in any of the three reports was checked by looking at Pyre running.** No frame rendered, no window opened, in any of the three rounds. With usage evidence withdrawn, **a rendered frame per generator, looked at by the owner, is now the only empirical evidence left** — and it is the cheapest item on any action list with no prerequisites. Every "keep, it's distinctive" verdict in all three reports is currently an inference from how much care went into building something, not from seeing its output.
