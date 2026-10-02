# T-0336 — round 20, the Shaper-only SECOND closing pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `f5324cc2` at session start (rounds 11–19 committed), editor on port 7801, **PID 42060** (the editor round 19 relaunched; `cap.sh`/`capprint.ps1` re-pointed from 8124 to it). `Application.dataPath` confirmed `D:/UNITY/Laubrary Dev - Shaper/Assets`; `isPlaying=False`, `isCompiling=False`, `scriptCompilationFailed=False`, active scene `Assets/Demos/ShaperDemo/ShaperDemo.unity` `dirty=False`, `pixelsPerPoint=2.25`. **No Laubrary tool window was open at session start**, and `Assets/Shaper` held only the two untracked `New Shaper*.asset` files rounds 11/12/18/19 left.

**Scope is Shaper only** (owner, 2026-09-09). No other tool window was opened, audited or edited.

**This file is written incrementally — each section is appended as it lands.**

---

## 0. Session log (append-only)

- Read `ShaperHarmony/RULES.md`, `ui-layout-rules.md` (the hard gate), `UNITY_DEV_GUIDE.md`, and `T-0334/ROUND-19.md` in full.
- Probe library copied from `T-0334/probes` into `T-0336/probes` and re-pointed (`T-0334` → `T-0336`, `T334.` → `T336.`, `AuditT334` → `AuditT336`, `fill-sweep-t334` → `fill-sweep-t336`, `t334-fillsweep.cs` → `t336-fillsweep.cs`). **Every hard-coded output path in a copied probe was checked before it ran** — `zrun.sh`'s probe dir, `cap.sh`'s `$D` **and its `-ProcId 8124`**, `capprint.ps1`'s default `ProcId`, and the fill sweep's absolute `.tsv` destination were all wrong for this task and all four were corrected.
- Session-start pref values recorded for restore: `T0312.out = …/T-0324/out`, `T320.capOut = …/T-0324/shots/cap.png`, `T320.capWin = ShaperWindow`, `ZuiSectionToggleBar.ShaperWindow.userSel = Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`, `Shaper.lastView` unset.
- **A probe fault of my own, caught in the first ten minutes and worth recording** because it is the fourth of the kind round 19 warned about. My "press every OFF chip in the section toggle bar" probe treated the bar's own **mode segment** (`Sections` | `Toggle Bar`) as two more section chips, and spent five round trips flipping the bar between its two modes while reporting a plausible `off=8 of 11 → pressed`. The bar has **11** buttons: 2 mode segments and 9 section chips. Excluding the mode segment by name, the 9 chips counted down 7 → 0 in seven presses. Nothing about the returned string said the walk was going nowhere; the section census did.

---

## 1. Round 19's three fixes, verified by eye

### 1.1 The split divider handle (round 19 §3.2) — holds, in both directions

Measured on a walk-1 document (Star, edge, height stage, 2 lights, swarm) with all nine sections open, each resize its own round trip:

| | left pane resolved width | dragline **anchor** `style.left` | anchor world x | grab strip world x |
|---|---|---|---|---|
| 1500 × 900 | **551.11** | **551.11** | 555.11 | 550.22 … 561.33 |
| 820 × 520 (Shaper's own minimum) | **492.00** | **492.00** | 496.00 | 491.11 … 502.22 |
| widened back to 1500 × 900 | **551.11** | **551.11** | 555.11 | 550.22 … 561.33 |

The invariant round 19 introduced (`anchor offset == fixed pane width`) holds at the minimum, and — the direction a conditional fix would have missed — **after widening back**.

**By eye** (`shots/w1-820.png`, `shots/w1-1500.png`, both real `PrintWindow` captures of the running editor). A pixel scan for near-full-height 2 px dark (`35,35,35`) columns over the pane region finds **exactly one rule in each capture, and in both it is the pane boundary**:

| capture | full-height dark columns found | physical x | logical x | pane boundary measured live |
|---|---|---|---|---|
| 820 × 520 | 2 (adjacent) | 1121–1122 | ≈ 498.2 | right pane starts 496.89 |
| 1500 × 900 | 2 (adjacent) | 1254–1255 | ≈ 557.3 | right pane starts 556.00 |

Nothing is drawn across the filmstrip, the `Preview backdrop` box or the `Bake` box at either size — confirmed on the crops as well as by the scan. (Round 19's *broken* capture also had its rule at physical 1254 — but that was at **820 × 520**, where the boundary is at 496. At 1500 the same number is correct. Worth stating, because the raw figure looks identical to the defect.)

### 1.2 A long asset name in the Preview backdrop `Image` field (round 19 §4) — holds

A scratch sprite with a 78-character name (`AuditT336 A Deliberately Very Long Backdrop Sprite Name For Measuring Clipping`, 468 px of text) assigned to the one drawn `Sprite` field, window at **820 × 520**:

| | measured |
|---|---|
| field width | **237.8** inside a **284.9** row inside the 292 px box |
| text | `need=468.0`, `have=199.6` → **clipped honestly**, `textOverflow=Ellipsis`, `whiteSpace=NoWrap` |
| overflow past the parent | **0** (round 19's pre-fix figure was 263.6 px, with the field's right edge 240 px outside the window) |
| the full name | on the field's own tooltip: *"AuditT336 A Deliberately Very Long Backdrop Sprite Name For Measuring Clipping — The backdrop image sprite."* |

**By eye** (`shots/w1-820-longname3-crop.png`): the `Image` row sits wholly inside the right pane, reading `Image [▣ AuditT336 A Deliberately Very Lo… ⊙]`, with `Recall…` / `Save…` / `Colour` above it and `Zoom` / `Tint` below, none of them pushed sideways. The same capture's other crop (`shots/w1-820-longname2-crop.png`) shows the right pane scrolled all the way to the **Bake** box at 820 × 520 — `Destination Assets/Shaper`, `Pixels per unit`, the four output toggles and the `Bake` button, all inside the pane.

### 1.3 The section toggle bar on a hosted generator (round 19 §9.1) — holds, including the shared-preference sting

Document `AuditT336W2`, created from the empty toolbar and given `Kiln › Energy Explosion › Arc Burst` through the picker's own menu (`node.kind` `Primitive → Composite`, verified on the document). Saved selection all nine visible going in.

| check | measured |
|---|---|
| the `Fill` chip | `enabled=False`, class `unity-disabled`, tooltip *"There is no Fill section for what is open here, so there is nothing to show or hide."* |
| pressing the greyed `Fill` chip | `1234` elements before, `1234` after; the preference byte-identical before and after |
| pressing **`SpriteFX`** (a chip with nothing to do with Fill) | preference goes `…Fill=1;Swarm=1;SpriteFX=1…` → `…Fill=1;Swarm=1;SpriteFX=0…` — **`Fill=1` carried forward**, which is the whole point of the fix |
| then binding the **Star** document `AuditT336W1` | its `Fill` section is **`drawn=True`**; only `SpriteFX`, the chip actually pressed, is hidden |

**By eye** (`shots/w2-bar-crop.png`): `Sections ‖ Toggle Bar ‖ Views Canvas Layers Shape` **`Fill`** `Swarm SpriteFX Lights Tags` — `Fill` dimmed **in place**, no pill behind it, every other chip lit and none of them moved. `SpriteFX` correctly reads unlit (it is the one I pressed).

**Not a finding, chased and closed:** the Shape section's title reads `Shape — Rectangle` in one probe and plain `Shape` in the next. `ZuiSection`'s header suffix is **collapsed-only** by design (`Zui/Toolkit/ZuiSection.cs:166` — `_title.text = _titleText + (open ? "" : suffix)`), and `ShaperWindow.cs:1009` feeds it the current shape's label. An open Shape section is titled `Shape` with the picker chip reading `Star` right under it; a folded one says `Shape — Star`. Correct, and recorded so the next pass does not re-chase it.

---

## 2. Round 19's declared gap: the hosted dials AS DECLARED, with every gate driven

Round 19 audited a hosted generator's dials **as drawn** and said so. This section enumerates them **as declared** — `ZuiReflect.FieldsOf` over the form, recursing into its nested `[Serializable]` settings objects exactly as `ZuiReflect` does, with every `[ZUIShowIf]` gate driven — for the three largest forms. Probe: `probes/d1-declared.cs`; output `out/declared-dials.tsv`, **398 rows**.

| form | declared dials | nested settings containers | reachable in some gate state | absented, with a reason | NEVER visible at any gate value |
|---|---|---|---|---|---|
| Arc Burst | 187 | 10 (`core`…`lichten`, one per `layout`) | **187** | 1 (`swarmSize`) | **0** |
| Plasma Bloom | 156 | 3 (`chunks`, `embers`, `motes`) | **156** | 1 (`swarmSize`) | **0** |
| Fork Blast | 40 | 0 | **40** | 0 | **0** |

**Every declared dial is reachable.** No field on any of the three is hidden by a gate that no value of the gate ever opens, and the only two that never draw at all are `swarmSize` on Arc Burst and Plasma Bloom — deliberately absented by `PyreFormShaperUI`'s `Skip` with a documented reason (`Editor/PyreShaper/PyreFormShaperUI.cs:119` and the T-0279 comment above it: a composite node hosts no swarm, so the dial is dead **by construction** here and is removed rather than drawn inert).

### 2.1 A control probe that invalidated my own first two passes — and the fourth probe fault of this programme

Passes 1 and 2 sampled phases **0, 0.5 and 1**. A control (`probes/d3-cover.cs`) then measured what the three forms actually paint at each of the eight frames:

| form | lit pixels, frames 0…7 |
|---|---|
| Arc Burst | **0** · 488 · 776 · 897 · 898 · 768 · 576 · **0** |
| Plasma Bloom | **0** · 1469 · 1494 · 1689 · 1495 · 842 · 218 · **0** |
| Fork Blast | **0** · 1799 · 3309 · 3376 · 1410 · **0** · **0** · **0** |

**Frame 0 and the last frame are blank on all three by design** (ignite / burnt out), so two of the three frames every earlier comparison used carried no picture at all, and Fork Blast was blank on four of eight. Every zero below was therefore re-taken at frames **2, 3 and 4** (`probes/d4-zero2.cs`, `out/declared-zero-live.tsv`), where all three are lit. This is the fourth probe fault of the same family round 19 recorded — and, like those, it made a working thing look broken rather than the reverse.

### 2.2 THE FINDING — fifteen declared dials were drawn live and silent while they were dead

Re-measured at live frames (`out/declared-zero-live.tsv`, `out/declared-guards.tsv`), the dials that move **exactly 0 pixels at their form's own factory defaults** split cleanly in two.

**Fifteen come alive the moment ONE named sibling is raised**, and each condition is the engine's own `if`, cited rather than inferred:

| dial(s) | guard | engine | pixels once the guard is opened |
|---|---|---|---|
| Plasma Bloom `Bias Dir`, `Bias Lobes` | `Bias Amount` | `PyrePlasmaBloom.cs:324` — `bool bias = f.live.biasAmt > 0f` | 1796 / 1700 |
| Plasma Bloom `Gate Dir`, `Gate Soft`, `Gate Lobes` | `Gate Amount` | `:329` — `bool half = f.live.halfAmt > 0f` | 2039 / 1902 / 1823 |
| Plasma Bloom `Lobe Mode`, `Lobe Power`, `Lobe Phase` | `Lobe Amount` | `:405` — `shell *= 1 − lobeAmp + lobeAmp·gate` | 1331 / 1758 / 970 |
| Fork Blast `Gob size`, `Gob travel`, `Gob swell`, `Gob lifetime`, `Gob glow`, `Gob window` | `Gob count` | `PyreForkBlast.cs:305` — `if (l.gobCount > 0)` | 2297 / 1517 / 1435 / 1899 / 1209 / 1453 |
| Fork Blast `Aim direction` | `Spread angle` | `PyreForkBlast.cs:215` — `aimRad = spread >= 179.9 ? 0 : aim·Deg2Rad` | 464 |

All fifteen ship dead: `biasAmt`, `halfAmt` and `lobeAmp` default to **0**, `gobs` defaults to **0**, and `spread` defaults to **180** — a full circle, which has no facing. **T-0280/T-0281 built exactly the machinery for this** (a sentence naming the condition, and greying the dial while its guard is shut) and wired it for the Jet family, Torch and Orb. It was never extended to these three forms, so on a hosted Plasma Bloom or Fork Blast card fifteen dials drew bright, took a drag, and moved nothing — with nothing on screen or in the tooltip saying why.

**Fixed**, in `Editor/PyreShaper/PyreFormShaperUI.cs` — the same three tables, extended, plus one helper:

| file | what |
|---|---|
| `PyreFormShaperUI.cs` — `ConditionOf` | 15 sentences, one per dial, each naming the guard **in the words the control beside it uses** ("Nothing until Gate Amount is above 0.") |
| `PyreFormShaperUI.cs` — `LiveIf` | the same 15, live-checked against the guard on the same owner, so a dial is greyed only while its guard is actually shut |
| `PyreFormShaperUI.cs` — `InertGuardFields` | the 5 guard fields (`biasAmt`, `halfAmt`, `lobeAmp`, `gobs`, `spread`), so editing a guard rebuilds the card and the dial it just freed stops being greyed |
| `PyreFormShaperUI.cs` — `Below(owner, name, limit)` | new sibling read, for the one guard whose "open" state is a value BELOW a limit rather than above zero (Fork Blast's full-circle spread). Fails OPEN on an unknown name, exactly as `NonZero` does |
| `Runtime/Pyre/Forms/Kiln/ForkBlastForm.cs` — `aim`'s `[Tooltip]` | **attribute only** (ShaperHarmony rule 2). It said *"Only visible when Spread is below 180"* — the dial is always visible; it is the VALUE the engine drops. Now says "Only has an effect when…" |

**The mutual pairs are deliberately left alone**, exactly as `JetSettings.pulseN`/`pulseDepth` are: `Drift X`/`Drift Y` ↔ `Drift Amount` and `Lobe Count` ↔ `Lobe Amount` are each dead while the other sits at its shipped zero, so greying both sides would lock the pair shut with no way to open it from the card. They get no entry, and the direction was measured to confirm it really is mutual (`driftAmt` is opened by `driftX`, and `driftX` by `driftAmt`; `lobes` is opened by `lobeAmp`, and `lobeAmp` is `[ZUIShowIf]`-gated on `lobes`).

**Verified after the change** — compiled (`scriptCompilationFailed=False`), then on the live window with a hosted Plasma Bloom document bound: `Bias Dir`, `Bias Lobes`, `Gate Dir`, `Gate Soft` and `Gate Lobes` all report `enabledInHierarchy=False` with the reason on the control's own tooltip, while `Bias Amount` and `Gate Amount` beside them stay live; opening a guard on a fresh instance returns every one of the 15 to live. `ZuiAudit` on that card: `elements=2007 controls=338`, **`inertNoReason=0`**, `inertWithReason=38`, every other counter **0**. **By eye** (`shots/pb-greyed-crop.png`): the row reads `Width Grow −0.05 · Bias Amount 0 ·` **`Bias Dir 0`** — the last dimmed, in place, beside its live guard.

### 2.3 What is still dead and NOT fixed — named, with the measurement

Thirteen more declared dials move 0 pixels at live frames and **no single sibling opened any of them** in a bounded search over every numeric sibling on their own owner:

- **Arc Burst:** `keepHueFloor`, `ghostDeepLo`, `ghostDeepHi`, `lattice.flyLo`, `lattice.flyHi`, `cage.coolK`. (`bolt.ghostLo`/`ghostHi`/`ghostDeepP` DID open when `bolt.trunks` was changed — they act on some trunk counts and not on the default — so they are conditional on content rather than on a guard being above zero, the `OrbForm.despeckle` shape.)
- **Plasma Bloom:** `driftEase`, `driftLin`, `driftLag` (behind the mutual drift pair), `mode`, `gateGain`, `plumeAmp`, `plumeReach`, `plumeW`, `plumeVary` (`mode = Plume` also requires `lobes ≥ 2` — `PyrePlasmaBloom.cs:327`, `bool plume = mode == Plume && lobes`), `chunks.swirl`, `embers.swirl`, `motes.swirl`.
- **Fork Blast:** `flash`, `opacity`.

These are **not fixed**, and should not be guessed at: writing a "Nothing until X" sentence for a dial whose guard has not been traced to the engine's own `if` puts a wrong sentence on screen, which is worse than none. They are a clean bounded follow-up — the same tracing T-0280 did for the Jets — and the two Plasma Bloom clusters look like the circular-triple shape that file already documents. Named here rather than built.

---

## 3. Fire and Fireball, with the sim parameters MATCHED on both sides

Round 19 could not reproduce T-0272's numbers because T-0272 never archived its probe body and round 19's rebuild compared **factory defaults** on both sides, which for the two stateful simulations are genuinely different parameter sets. **The probe body is archived this time**: `probes/e1-simparity.cs`, output `out/parity-sims-matched.tsv`.

The harness copies **every** dial of the composite source onto Pyre's own `PyreLayer` by name (`intensity→fireIntensity` … `subSteps→fireSteps`, `threshold→fireThreshold`, `contrast→fireContrast`, `alpha→alpha`), and the source's `ramp` onto `layer.shapeFill.gradient`, which is where `PyreRenderer.cs:442` reads it; the source's `simFrames` is set to the spec's `frameCount`. 64 × 64, seed 1234567, **all eight frames** of an 8-frame document. Not one source field was left unmapped.

| | worst visible diff | worst maxCh | coverage Pyre → Shaper |
|---|---|---|---|
| **Fire, factory defaults** (round 19's method) | 66 | **255** | 66 → 64 |
| **Fire, MATCHED** | **11** | **1** | **identical on every frame** |
| **Fireball, factory defaults** | 273 | **197** | 273 → 273 |
| **Fireball, MATCHED** | **28** | **1** | **identical on every frame** |

Per frame, matched (visible diff / worst channel):

| frame | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| Fire | 0/0 | 2/1 | 5/1 | 1/1 | 3/1 | 11/1 | 0/0 | 0/0 |
| Fireball | 0/0 | 0/0 | 0/0 | 2/1 | 7/1 | 16/1 | 28/1 | 0/0 |

**With the sims matched, both stateful simulations agree with Pyre's own renderer to one least-significant bit on every frame, with byte-identical coverage** — the same verdict the nine `PyreForm` generators get, and precisely the thing round 19 listed as "not verified". Frames 0 and 7 are blank on both sides (ignite / burnt out), which is why they read 0/0.

**What this says about T-0260 Q8.** The warmth the owner sees on a hosted Fire is **entirely** the dials and the ramp Shaper seeds, not a divergence in the simulation: at the same parameters the two pictures are the same picture. Q8 is therefore a pure authoring decision (keep Shaper's Ember ramp, or adopt Pyre's generic layer fill), with no engine risk attached to either answer.

---

## 4. The window between 820 × 520 and 1500 × 900

The walk-1 document (Star, edge box, height stage, three lights, swarm on) with **all nine sections open**, at the two sizes the programme had never measured. Each resize was its own round trip from the audit that followed it.

| | 1000 × 700 | 1200 × 800 |
|---|---|---|
| elements / drawn / controls / leaf controls | 1405 / 977 / 263 / 151 | 1405 / 977 / 263 / 151 |
| captions too short | **0** | **0** |
| overflow-X / overflow-Y | **0 / 0** | **0 / 0** |
| off-window | **0** | **0** |
| no tooltip | **0** | **0** |
| inert without a reason | **0** | **0** |
| left pane `ScrollView` | viewport 571.6, content 2288.9, vertical scroller **drawn**, `Auto` | viewport 671.6, content 2288.9, scroller **drawn**, `Auto` |
| right pane `ScrollView` | viewport 571.6, content 860.0, scroller **drawn**, `Auto` | viewport 671.6, content 860.0, scroller **drawn**, `Auto` |
| **leaf controls outside the window with no `ScrollView` ancestor** | **0** | **0** |
| the `Bake` button | off-viewport at rest; `ScrollTo` brings it to `(566.22, 704.00, 406.67, 18.22)` and `panel.Pick` at its centre then **hits the button itself** | **already reachable at rest** — `panel.Pick` hits it at `(566.22, 789.78, 606.67, 18.22)` |

**By eye** (`shots/w1-1000x700.png`, `shots/w1-1200x800.png`, both real captures). At both sizes: the toolbar row (`AuditT336W1 ⊙ ● Save New Browse Duplicate Rename Delete`) fits on one line; the section bar fits on one line with all eleven segments; the `Tags` section spans the full width above the split; the left pane shows `Height`/`Mask`, the `Lighting` box, the `Shape — Star` card with its `Position` / `Sweep` / `Shell` boxes; the right pane shows the preview stage, the transport, the 16-tile filmstrip with its own horizontal scroller, `Preview backdrop`, `Cherry Framing` and the whole `Bake` box down to the `Bake` button. **Nothing is clipped, nothing overflows, nothing sits below the window, and the divider rule is on the pane boundary at both sizes.**

---

## 5. Two cold walks through a Bag, and the demo document over time

### 5.1 Walk 1 (`AuditT336W3`) and walk 2 (`AuditT336W4`) — identical, step for step

Each walk started from the toolbar's own `New` → type → `Create`, with the shape picked through the picker's own menu. Every press went through `ZPress`, and every data assertion was read back off the document.

| step | walk 1 | walk 2 |
|---|---|---|
| `New` → `Create` | `Assets/Shaper/AuditT336W3.asset` | `Assets/Shaper/AuditT336W4.asset` |
| pick `Bag › Combine children` | `node.kind` `Primitive → Bag` | same |
| open all nine sections through the bar's own chips | 7 presses, `off` 7 → 0 | same |
| `+ Add member` | `children` → **2** (`Member 1`, `Member 2`, both `mode=Add`) | same |
| a fill on the bag | already present and **authored** — the card reads `Remove fill`, not `Add fill` (T-0271's flag behaving as designed) | same |
| `Open` on `Member 1` | breadcrumb becomes `Layer 1 › Member 1`; `Layer 1` carries *"Back to this layer's root node."* and `Member 1` *"Back to this member."* — **the walker can get back out** | same |
| `Combine` → `Cut out` | `child[0].mode` `Add → Subtract` | same |
| `Add edge` on that member | `border.authored` `False → True` | same |
| **the border is refused, on screen** | *"Border on 'Member 1' cannot be attached: a subtracted member removes coverage; it deposits nothing. Outline the hole with a border on the bag."* — the engine's own sentence (`ShaperFillResolver.cs:522-524`), naming the member, the reason **and the remedy** | same, verbatim |
| `Save` (the toolbar's own button) | `saveWasDisabled=False`, `dirtyAfter=False` | same |
| **round trip, renderer level** (`ShaperBaker.RenderFrame`, no preview cache): hash → `ForceUpdate` reimport off disk → hash | `f0=f4=f8=f12=4E5E65C5/1536` **before and after — byte-identical** | **identical** |
| **preview cache vs renderer, all 16 frames** | **16/16 identical, 0 mismatches** (checked twice, once with the renderer asked first and once with the stage asked first) | **16/16 identical** |
| **Bake** (called directly with an explicit path) | `768 × 128`, 16 frames in `8 × 2`, `.png` + `.anim` + `ShaperClip`, `16 keys over 16 beats @ 12fps` | same |
| **baked sheet vs live renderer, frame for frame** | **0 differing pixels of 98 304** | **0 differing pixels of 98 304** |

**By eye** (`shots/bag-refusal-crop.png`): the member's card reads `Fill [Add fill]`, then the `Edge` box with `Width 2 · Sits Centred|Inward|Outward · Joins coverage`, and under the dials, in the Subtle style, the refusal sentence across two lines. Exactly what T-0257 specified: said before the dials are tuned, not after.

### 5.2 An instrument disagreement I could not reproduce — recorded, NOT claimed as a defect

`s42-save.cs` (inherited from round 19) hashes four preview-stage frames, presses `Save`, and `s43-reload.cs` re-hashes them after a forced reimport. On **both** bag walks it reported `IDENTICAL=False` with the same two hashes: `f0=00A2A5C5` and `f12=65213DC5` before, `4E5E65C5` after, at an unchanged 1536 lit pixels.

Chased rather than reported:

- the **renderer-level** round trip on the same document is byte-identical (`s44-rt.cs`: in memory, same object, off disk, off disk again — one hash);
- the **preview cache vs the renderer over all 16 frames** matches 16/16, both with the renderer asked first (`s21-framehash.cs`) and with the stage asked first so the renderer call cannot prime the cache (`s21b.cs`, written for exactly this);
- a **data edit followed immediately** by the 16-frame check (mode `Add`↔`Cut out`, `Add edge`) matches 16/16 every time — four attempts, two documents;
- the **bake** equals the renderer to the byte on all 16 frames of both documents.

So every instrument that can see the picture says the document, the cache and the bake agree; only that one PRE/POST pair disagrees, and only inside its own probe. It is **not** established as a Shaper defect, and it is **not** dismissed either — it is named here with its numbers. The practical carry-forward: `s42/s43`'s PRE/POST pair is a weak instrument, and `s21b.cs` + `s44-rt.cs` are the ones to use.

### 5.3 The demo document over time

`ShaperDemoDoc` bound (never edited, never saved — `IsDirty=False` before and after, and the demo scene stayed `dirty=False`). Play pressed from the window's own transport, **paused before every hash** (round 19's third probe fault):

| sample | elapsed since the play press | frame reached | hash | lit pixels |
|---|---|---|---|---|
| 1 | 28.05 s | 4 | `84797319` | 3630 |
| 2 | 1.83 s | 0 | `C4CF44D5` | 124 |
| 3 | 2.41 s | 9 | `253676B6` | 4246 |
| 4 | 2.52 s | 13 | `AB61EAED` | 1359 |

Four samples, every gap **above 1.5 s**, four distinct frames and **four distinct pictures** with lit-pixel counts spanning 124 → 4246. The demo document genuinely moves.

### 5.4 The dial sweep, re-run against HEAD

`probes/t336-fillsweep.cs`, verbatim from round 19 with only its output path re-pointed; `probes/p1-rfield.cs` first, so the 16 × 16 `RFloat` height field the sweep loads actually exists (round 14's trap). Output `out/fill-sweep-t336.tsv`, **414 rows** over a Primitive (Star), a Solid (Pyramid) and a Bag with two members.

| baseline | rows | rows differing | rows missing | **regressions (was > 0, now 0)** |
|---|---|---|---|---|
| `T-0334/out/fill-sweep-t334.tsv` | 414 | **0** | 0 | **0** |
| `T-0328/out/fill-sweep-t328.tsv` | 414 | **0** | 0 | **0** |
| `T-0326/out/fill-sweep-t326.tsv` | 414 | **0** | 0 | **0** |

**Byte-identical to the last three rounds. No regression, and none introduced by this round's own change** — which is what matters, since the change is in a drawer that every hosted generator goes through.

---

## 6. Files touched

| file | what |
|---|---|
| `Assets/Packages/Laubrary/Editor/PyreShaper/PyreFormShaperUI.cs` | `ConditionOf` +15 sentences, `LiveIf` +15 guards, `InertGuardFields` +5 guard fields, and a new `Below(owner, name, limit)` sibling read — so the fifteen dead-at-default dials on Plasma Bloom and Fork Blast are **greyed with their own reason** instead of drawn live and silent (§2.2) |
| `Assets/Packages/Laubrary/Runtime/Pyre/Forms/Kiln/ForkBlastForm.cs` | **attribute only** (ShaperHarmony rule 2): `aim`'s `[Tooltip]` said *"Only visible when Spread is below 180"*, which is not what happens — the dial is always drawn and it is the VALUE the engine drops. No serialized field renamed, no default changed, no render code touched. |

**No other Pyre file, no `Editor/Pyre/**` file, no `CHANGELOG.md`, nothing committed** (ShaperHarmony rule 3). One compile this session, `scriptCompilationFailed=False`, and that is the state the tree is in.

## 7. State left behind

`git status` over `Assets/Packages/Laubrary` shows **exactly the two files above**, plus the `Samples~` block that was already modified before this session started. `Assets/Shaper` holds only the two untracked `New Shaper*.asset` files that were there at session start — **not this task's, and not deleted**, exactly as rounds 11, 12, 18 and 19 left them.

**Fifteen assets and three folders deleted** through `AssetDatabase.DeleteAsset` (which removes the `.meta`): the four scratch documents `AuditT336W1`–`W4`, the full bake output of both bag walks (`.png` + `.anim` + ` Clip.asset` each), the 78-character scratch sprite, the sweep's `rfield0277.asset`, and the folders `Assets/Shaper/AuditT336Long`, `Assets/Shaper/Audit0277` and `Assets/Shaper/AuditT336Bake`. A project-wide scan finds **0 dirty ScriptableObjects** and `AuditT336` matches **0**.

Prefs restored: `ZuiSectionToggleBar.ShaperWindow.userSel` back to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`; `T320.capOut` and `T320.capWin` back to their T-0324 values; `T0312.out` restored **last**, after the final dumping probe (round 14's lesson); every `T336.*` key and `Shaper.lastView` deleted. `Undo.ClearAll()`.

The demo document was bound for the temporal check and **never edited and never saved** (`IsDirty=False` throughout); the demo scene is the open scene, `dirty=False`, and was never saved. **Play mode was never entered, the Test Runner was never run, no tag was created, and no confirm dialog or native file panel was opened** — both bakes went through `ShaperBaker.Bake` directly. The open window set is back to the editor's own seven; no Laubrary tool window is open, and none was at session start.

### The editor was relaunched, and it was my fault

**~16:20, my own pass-2 declared-dial probe pinned the editor at 100% of one core for 75 minutes** with no bound in sight: it brute-forced "open every sibling and re-measure" over Plasma Bloom's 88 top-level fields for each of 27 dials, and (a bug of its own) never restored a `ZUIValue` sibling it had opened, so the form drifted into ever more expensive states as the scan went on. The editor was healthy, just executing my script; `unity status` reported the port `unreachable` and the process `Responding=False` while CPU climbed steadily.

The card says never to relaunch with `-automated`, and round 19's precedent is a relaunch after the editor quit on its own. This was not that: **I killed PID 42060 deliberately** (`Stop-Process -Force`) because waiting it out had no known end and the whole task is editor-driven, then reopened with plain `unity open "D:\UNITY\Laubrary Dev - Shaper"` and a capped wait loop. It came back on port 7801 as **PID 184580**; `cap.sh`'s hard-coded `-ProcId` and `capprint.ps1`'s default were re-pointed to it. `Application.dataPath` re-confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets`, `isPlaying=False`, `scriptCompilationFailed=False`, scene `ShaperDemo.unity` `dirty=False`, the editor's own seven windows restored with **no Laubrary tool window open**, and the scratch assets intact on disk (the in-memory unsaved edits to two scratch documents were lost and simply redone). Everything from §2.1 onward was measured on that editor.

**The carry-forward, because this is the second round in a row the editor went away:** the `unity … command eval_file` CLI gives up after 30 s but **the editor keeps executing the script**, so a "timed out" line says nothing about whether the work is still running — poll the process, not the CLI. And an exhaustive sibling scan over a form with 88 dials is not a probe, it is an outage; bound it (`d5-findguard.cs` does the same job in minutes) and always restore what you mutate.

---

## 8. Verified how

**By probe, in the live editor:** the split geometry at 1500 → 820 → 1500 with the anchor/pane invariant re-measured in both directions; the `Z.Object` fit at a 78-character name at 820 × 520 (field/row/box widths, overflow 0, tooltip carrying the whole name); the section-bar chip census on a hosted generator, the press of the greyed chip (element count and preference unchanged), the `SpriteFX` sting and the primitive document's surviving `Fill` section; the 398-row declared-dial enumeration over three forms with every `[ZUIShowIf]` gate driven; the coverage control that invalidated two of my own passes; the zero rows re-measured at live frames and again with each declared guard opened; the bounded guard search; `DialInertReason`/`DialTooltip`/`IsDialInertGuard` read back for all 20 affected fields before and after the fix; the live-window disabled-control census on a hosted Plasma Bloom card plus a full `ZuiAudit` (`inertNoReason=0`); the matched Fire/Fireball parity harness over all eight frames of both simulations, with the probe body archived; `ZuiAudit` + pane reachability + `Bake` hit-testing at 1000 × 700 and 1200 × 800; two cold bag walks with every press through `ZPress` and every assertion read back off the document; two save → force-reimport round trips at renderer level; the 16-frame cache-vs-renderer check four times across two documents and in both orders; two bakes compared frame for frame (0 differing of 98 304 each); four temporal samples ≥1.5 s apart with the transport paused before each hash; the 414-row fill sweep against three baselines; one compile; and the asset/pref/window cleanup with a project-wide dirty scan.

**By eye, in real `PrintWindow` captures of the running editor** (`shots/`): the walk-1 document at 820 × 520 and at 1500 × 900, with a pixel scan locating the only full-height rule on the pane boundary in each; the `Preview backdrop` `Image` row with a 78-character name ellipsised inside the pane, and the right pane scrolled to the `Bake` box at 820 × 520; the section toggle bar with `Fill` dimmed in place on a hosted-generator document; the newly greyed `Bias Dir` sitting beside its live `Bias Amount`; the whole window at 1000 × 700 and at 1200 × 800; and the bag member's refused border with the engine's sentence under the `Edge` dials.

**Not verified:**

1. **No *human* has operated any of this.** Every press was a synthesized pointer or key event — the same code path a real one takes, not the same hand.
2. **Thirteen declared dials on the three forms are still dead at their own defaults with no sentence** (§2.3). They were measured, not traced; naming a condition I have not read out of the engine would put a wrong sentence on screen.
3. **The `s42`/`s43` PRE/POST disagreement (§5.2) has no explanation.** Four independent instruments contradict it, so it is recorded rather than claimed — but it is not closed.
4. **The declared-dial sweep covers three forms of nine.** Inferno, Orb, Torch, Jet, Radial Jet and Explosive Jet were not enumerated as declared this round; the Jet family and Torch already carry T-0280/T-0281's tables, Inferno and Orb only partly.
5. **Nothing was measured below 820 × 520 or above 1500 × 900**, and no window was measured while another Laubrary tool was open.
6. **`Editor/Pyre/**` was never opened**, per the programme's read-only rule, so nothing here says whether Pyre's own window shows the same fifteen dials live and silent. It draws them through its own drawer, not this one, so it very likely does.
7. **The bag walks used a bag of two Primitive members.** A bag of Solids, or a nested bag, was not walked.

### Trivia (named, not fixed)

- Round 19's six long MicroSlider captions are unchanged and still fit; nothing new joined them (`captionShort=0` at every size measured this round).
- The `Views` bar's native dropdown and the shape picker's duplicate names are unchanged — both are still open owner questions (T-0260 Q11 and Q10).
- The left pane keeps its 551 px fixed width at 1000, 1200 and 1500 px, so widening the window only widens the preview. Deliberate (`Z.Split`'s fixed pane) and not a defect, but it is the reason the Bake box needs a scroll at 1000 and not at 1200.

---

## 9. Verdict

Round 19 found three items after eighteen passes had called Shaper clean. Round 20 found **one** non-trivial item, and it is a real one: on a hosted **Plasma Bloom** or **Fork Blast** card, fifteen dials drew bright, took a drag, and moved exactly zero pixels — every one of them dead at the form's own factory defaults behind a guard sitting a few controls away, and nothing on screen or in the tooltip said so. The machinery to say it had existed since T-0280/T-0281 and had simply never been pointed at these three forms. It is fixed, in the same three tables, with each condition cited to the engine's own `if` rather than inferred, the mutual pairs deliberately left alone, and the result verified live and by eye.

Everything round 19 fixed holds: the split handle sits on the pane boundary at 820 × 520 **and after widening back to 1500**, a 78-character asset name is clipped honestly inside its pane with the full name on the tooltip, and the section bar's `Fill` chip is greyed with a reason on a hosted generator while the preference of every other document survives untouched. The two sizes the programme had never measured — 1000 × 700 and 1200 × 800 — are clean on every counter with zero unreachable controls and the Bake box reachable at both. Two cold bag walks ran New → pick → members → fill → a refused border with its reason → Save → reload → bake without a single affordance failing, both byte-identical on the round trip and both equal to the live renderer on all sixteen baked frames. The 414-row dial sweep is identical to the last three rounds. And the one thing round 19 had to leave open is now closed: **with the sim parameters matched on both sides, Fire and Fireball agree with Pyre's own renderer to one least-significant bit on every frame with identical coverage** — the probe body archived this time, so the next round does not have to rebuild it.

Two things temper that. Thirteen more declared dials on these same three forms are still dead at their defaults and are named but not fixed, because tracing each one's guard to the engine is a real piece of work and a guessed sentence is worse than none. And this round produced its own cautionary tale twice over: a control probe found that all three forms paint **nothing** at phase 0 and phase 1, which had silently invalidated two of my own passes, and an unbounded sibling scan wedged the editor for 75 minutes and cost a deliberate kill and relaunch. The programme's lesson from round 19 — that the probe is as likely to be wrong as the tool — held again, twice.

**FOUND: 1 non-trivial item, next pass needed**
