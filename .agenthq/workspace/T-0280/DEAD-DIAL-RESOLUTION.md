# T-0280 — the 92 open dead-dial hypotheses, every one resolved

Follow-up to T-0279, whose sweep left 92 findings "measured dead at 32×32/mid-frame, not root-caused". All 92 are resolved below. Nothing is left as a hypothesis.

## What the new sweep does differently

Three faults in T-0279's method account for every false-dead it produced, and all three are fixed here:

1. **`ZUIValue` was perturbed by writing `staticValue`.** A field whose authored mode is Curve/MinMax never reads `staticValue`, so it could only ever read dead. This sweep snapshots the whole `ZUIValue` (`CopyFrom`), forces `mode = Static`, drives the value to its `[Range]` ends, and restores the snapshot verbatim — so a Curve-mode dial is exercised whatever it was authored as.
2. **The perturbation was a fixed ±7.5 / ±3, ignoring `[Range]`.** On `detonate.h` (`[Range(16,400)]`, default 184) ±3 changes nothing visible; 16 and 400 change thousands of pixels. This sweep reads `[Range]` and drives each dial to its declared ends (and midpoint), and every enum to every other value.
3. **One frame, 32×32, and a node whose half-extent was 4× the canvas.** 32×32 rendered 114–277 lit pixels — a picture too small for a ring, a lobe or a flash to survive. This sweep re-renders at the variant's own native frame (Jet 160×92, Radial Jet 176×176, Explosive Jet 184×184, Torch/Orb/Fireball 128×128) with the node mapped 1:1 onto the canvas, and diffs **three phases** (0.25 / 0.50 / 0.75) rather than one.

A fourth stage was then added: **open the guard the engine's own source shows the dial sitting behind, and re-measure**. That is what separates "broken" from "waiting for its companion", and it is the finding that accounts for most of the 92.

Method for every row: build a `ShaperDocument` hosting the generator through `PyreFormCompositeSource` (or the sim's own `IShaperCompositeSource` for Fireball), seed 12345; render the baseline; perturb one field; re-render; count changed pixels; restore. Pixel counts below are the maximum over the tested phases.

## Result

| bucket | dials | what it means |
|---|---:|---|
| **LIVE — T-0279's verdict was a method artefact** | 12 | acts at defaults once the canvas is the generator's own frame and the perturbation reaches the `[Range]` ends |
| **LIVE — content-dependent** | 2 | Orb's despeckle pair: it acts, but only on output that contains what it removes |
| **CONDITIONAL — acts once a named companion is on** | 54 | measured dead at the variant's defaults, measured live the moment the engine's own guard was opened |
| **INERT — the generator's shade never reads it** | 24 | ramp knobs the Kiln bakes ignore, or that only a runtime driver reads; none of them is a dial on the Shaper card |
| **total** | **92** | |

---

## 1. LIVE — 12 dials T-0279 called dead and are not (no gate needed)

| dial | px @32×32 | px @native | why T-0279 missed it |
|---|---:|---:|---|
| `RadialJet.corona.h` | 219 | 7124 | `[Range(16,400)]`, default 176 — ±3 is below the noise floor |
| `ExplosiveJet.detonate.h` | 147 | 8530 | same |
| `ExplosiveJet.detonate.rootR` | 0 | 28 | 32×32 too small for the seat lump |
| `ExplosiveJet.detonate.rootAmp` | 0 | 28 | same |
| `RadialJet.corona.rootAmp` | 0 | 9 | same |
| `Torch.barbs.pulse` | 0 | 2926 | surge is 0 at the single mid phase tested |
| `Torch.barbs.pulseGain` | 0 | 1700 | same |
| `Torch.barbs.bulge` | 0 | 1631 | same |
| `ExplosiveJet.detonate.flash.radius` | 0 | 1964 | see below |
| `ExplosiveJet.detonate.flash.amp` | 0 | 366 | see below |
| `ExplosiveJet.detonate.flash.grow` | 0 | 2105 | see below |
| `ExplosiveJet.detonate.flash.elong` | 0 | 1139 | see below |

**The four flash dials are the interesting case.** They are live and unconditional, but the flash's own life is `0.095` of the loop (`ExplosiveJetProgram.cs:716-727`), and its blast fires at phase 0. A 3-frame document samples phases 0, 0.5, 1 — none of them inside the flash's window, so the flash is *never drawn* at any frame and every dial on it reads dead. Re-rendered at 21 frames, frame 1 (phase 0.05) lands inside the window and all four move the picture. This is not a bug and needs no fix — the dials' own tooltips already say the flash is brief — but it is worth knowing that **a short Shaper document can miss an Explosive Jet's flash entirely**.

## 2. LIVE — content-dependent (2)

`OrbForm.despeckle`, `OrbForm.despeckleBelow` (`PyreOrb.cs:654-668`). Despeckle drops a pixel only when it is BOTH faint (`0 < alpha < despeckleBelow`) AND isolated (fewer than 2 lit 4-neighbours over a wrapped plane). Measured over every Orb variant at three seeds, three frames each, counting removable pixels directly in the rendered frame:

| variant | removable @40 | removable @255 |
|---|---:|---:|
| Emberdrift (default) | 0 | 0 |
| Wisp | 0 | 0 |
| Coronal | 0 | 0 |
| Membrane | 0 | 0 |
| **Voltcore** | **5–10** | **5–10** |

Four of the five variants are smooth enough that no pixel is ever isolated at any threshold — so the dial is genuinely live, and genuinely does nothing on them. On Voltcore, toggling despeckle changes 7/9/9 px across three frames, and `despeckleBelow` 1 → 40 changes the same 7/9/9. **Fixed:** both tooltips now state this, with the measurement.

## 3. CONDITIONAL — 54 dials that act once their companion is on

Every row was measured dead at the variant's own defaults and live with the named guard opened. The guard is the engine's own `if`, cited.

### Jet family — `JetSettings`, shared by all three (`PyreJetEngine.cs`)

| dial | guard | source | px once open (Jet / RadialJet / ExplosiveJet) |
|---|---|---|---|
| `pulseN` | Surge depth > 0 | `PyreJetEngine.cs:587` | 3125 / 8966 / 9869 |
| `pulseDepth` | Surges > 0 | same | 3125 / 8966 / 9869 |
| `sweepN` | Sweep angle > 0 | `PyreJetEngine.cs:585` | 3889 / 9429 / 9541 |
| `shockN` | Shock amt. > 0 | `PyreJetEngine.cs:601` | 3223 / 8847 / 9275 |
| `shockDepth` | Shock count > 0 | same | 3223 / 8847 / 9275 |
| `ringK` | Ring count > 0 | `PyreJetEngine.cs:615` | 524 / 963 / 183 |
| `ringR0` | Ring count > 0 | same | 1206 / 1053 / 291 |
| `ringGrow` | Ring count > 0 | same | 1118 / 1038 / 306 |
| `ringLife` | Ring count > 0 | same | 1410 / 1232 / 526 |
| `ringReach` | Ring count > 0 | same | 1542 / 1165 / 245 |
| `ringAmp` | Ring count > 0 | same | 938 / 1003 / 317 |

11 dials × 3 forms = 33 of the 54.

`Surges`/`Surge depth` and `Shock count`/`Shock amt.` are **mutually gating pairs** — both default to 0, so perturbing either alone can never show anything. That is the single most misleading shape in the whole set and it accounts for 12 of the 92.

### Radial Jet's own (`RadialJetProgram.cs`)

| dial | guard | source | px once open |
|---|---|---|---|
| `lobes` | Tongue clump or Tongue kick > 0 | `RadialJetProgram.cs:128,134` | 8799 |
| `lobeDepth` | Tongue count > 0 | `RadialJetProgram.cs:128` | 6958 |
| `lobeKick` | Tongue count > 0 | `RadialJetProgram.cs:134` | 8334 |
| `rootK` | Burn radius > 0 | `RadialJetProgram.cs:189` | 760 |
| `ringFlat` | Ring count > 0 | `RadialJetProgram.cs:275` + `PyreJetEngine.cs:615` | 4451 |

### Explosive Jet's own (`ExplosiveJetProgram.cs`)

| dial | guard | source | px once open |
|---|---|---|---|
| `lobes` | Tongue clump or Tongue kick > 0 | `ExplosiveJetProgram.cs:468,473` | 9523 |
| `lobeDepth` | Tongue count > 0 | `ExplosiveJetProgram.cs:468` | 7506 |
| `lobeKick` | Tongue count > 0 | `ExplosiveJetProgram.cs:473` | 9264 |
| `ringFlat` | Ring count > 0 | `ExplosiveJetProgram.cs:843` | 907 |
| `ringArc` | Rings face-on on AND Ring count > 0 | `ExplosiveJetProgram.cs:864-868` | 966 |
| `blasts[0].share` | two or more blasts | `ExplosiveJetProgram.cs:449-451` | 9413 |
| `rootK` | Burn radius > 0 AND the Blast schedule is EMPTY | `ExplosiveJetProgram.cs:683-706` | 468 |

`rootK` is the sharpest of these and was the one hypothesis T-0279 flagged as a likely genuine bug. It is not a bug: `Root` takes the `HasSchedule` branch and `return`s before it ever reaches the burner-ring branch that reads `rootK`. Measured: with the schedule kept, `rootK` 8 vs 0 renders 26748 lit px both ways (identical); with the schedule cleared, 34153 vs 33685 — 468 px of difference. Detonate ships with a schedule, so the dial is unreachable at its own defaults. The field's existing tooltip already said "With Src R > 0 and NO schedule" — but it named the companion `Src R`, which is labelled **Burn radius** on the card, so the reader could not follow it.

### Torch — `TorchSettings` (`PyreTorch.cs`)

| dial | guard | source | px once open |
|---|---|---|---|
| `lashK` | Whip amount > 0 | `PyreTorch.cs:638` | 2491 |
| `lashWave` | Whip amount > 0 | same | 2289 |
| `lashPh` | Whip amount > 0 | same | 4 |
| `pulseN` | Surge jump, Surge glow or Heat lump > 0 | `PyreTorch.cs:589-594,657` | 2731 |
| `pulsePh` | same | same | 2892 |
| `bulgeW` | Heat lump > 0 | `PyreTorch.cs:657` | 1488 |
| `curlX` | Curl warp > 0 | `PyreTorch.cs:581,604` | 1531 |
| `curlY` | Curl warp > 0 | same | 1405 |

The Barbs variant (the Torch's default) ships `lash = 0`, `pulse = 0`, `pulseGain = 0`, `bulge = 0`, `curl = 0` — five whole groups off, 8 dials behind them.

### Fireball (`FireballCompositeSource` → `FireballSim`)

| dial | guard | source | px once open |
|---|---|---|---|
| `sharpness` | Arms > 1 | `FireballSim.cs:124` (`if (arms > 1) cool += p.sharpness * axisDist * axisDist;`) | 133 |

The last of T-0279's three "genuine candidate bug" flags, and the same shape as the `armMode` / `mirror` pair T-0279 already greyed: a single wedge has no off-axis to cool.

## 4. INERT — 24 ramp knobs the generator's shade never reads

None of these is a dial on the Shaper card. `ZuiReflect` routes a `PyreRamp` to one `ZuiRampControl` and a `ZuiGradient` to one `ZuiGradientControl` (`ZuiReflect.cs:590,617`) rather than recursing into their fields, so the hosted-form dump never draws them; they are reachable only inside that control's own popover. T-0279's sweep walked reflection rather than the drawn controls, which is why they appear in its table at all.

| knob | count | verdict, proven at source |
|---|---:|---|
| `ramp.space` / `sootRamp.space` | 7 | **The Kiln bakes ignore it.** `JetShade.Bake` (`PyreJetEngine.cs:726-753`) reads only `stops` + `adjust` and always linearises sRGB→linear itself; `PyreOrb.BakeLut` (`PyreOrb.cs:572`) interpolates the byte colours directly. Measured: flipping `space` changes **0 of 1024** LUT entries in `JetShade.Bake` and **0 of 768** in `PyreOrb.BakeLut`, even though the stops themselves differ by up to 12/255 between the two spaces. |
| `ramp.adjust.cycle` / `.cycleSpeed` (and `ZuiGradient.cycle`/`.cycleSpeed`) | 16 | **Driver-only, by design.** `ZuiRampAdjust.cs:52-57` states it: "a driver advances the phase at runtime; a still frame is unaffected", and `IsIdentity` deliberately excludes both. The only readers in the whole package are `ZuiPaletteCycle` (a runtime MonoBehaviour) and the ramp editor itself. Shaper renders still frames; there is no driver. |
| `Torch.barbs.ramp.bandLocked` | 1 | **Inert while the ramp is already banded.** `ZuiGradient.Evaluate` computes `steps = bandLocked ? Max(1, quantiseSteps) : quantiseSteps`; the Torch preset ships `bandLocked = true, quantiseSteps = 7`, so both branches give 7. Measured: **0 of 256** LUT entries change when it is flipped. It is a UI floor on the band count, not a colour knob. |

Breakdown: Jet 6, Radial Jet 6, Explosive Jet 6, Orb 3, Torch 3.

## What changed in code

Two files, both bridge:

- **`Assets/Packages/Laubrary/Editor/PyreShaper/PyreFormShaperUI.cs`** — a `ConditionOf` table keyed by *declaring type + field name* (the same name carries different conditions on different engines: `JetSettings.pulseN` waits on Surge depth, `TorchSettings.pulseN` on Surge jump), appended to each dial's own tooltip by the drawer's `TooltipFor`. 33 entries covering all 54 conditional dials plus the two Orb content-dependent ones. The existing Orb Nose X / Axis Y tooltip path is folded into the same delegate.
- **`Assets/Packages/Laubrary/Runtime/PyreShaper/FireballCompositeSource.cs`** — `sharpness`'s `[Tooltip]` states its Arms > 1 condition, matching the `mirror` wording T-0279 established.

**No Pyre file was touched.** The conditions live in the bridge, not in Pyre's forms, because 40+ tooltip edits across four Pyre engine files is a far wider blast radius than one table in the drawer that already owns how these dials are presented in Shaper.

**"Grey with a reason" is delivered as the reason, not as greying.** `ZuiReflect.Options` exposes `Skip`, `TooltipFor`, `ConfigureValue` and `ReorderFields` — no per-field disable hook. Adding one would be a new control, which this programme forbids (rule 6). If the owner wants these dials visibly dimmed rather than explained, that is a ZUI change worth its own task.

## Left for someone else

- **The ramp control's own inert knobs.** `cycle` / `cycleSpeed` are dead in every still-frame render, not just Shaper's, and `space` is dead specifically inside the Kiln generators. Both live in `Assets/Packages/Laubrary/Zui/**`, outside this card's allowed file scope. A ZUI-level task could hide or annotate them.
- **Explosive Jet's flash window.** Live, but a document of ~10 frames or fewer may never sample it. Not a defect; possibly worth a note on the Frames dial.
- **Radial Jet's Burn radius blanks the picture at large values.** Setting `corona.srcR` to 8 (its `[Range]` is 0..0.3, so this is out of authored reach) renders 0 lit pixels. Noticed while calibrating the gate presets; not a card dial finding, and inside its range it behaves.
