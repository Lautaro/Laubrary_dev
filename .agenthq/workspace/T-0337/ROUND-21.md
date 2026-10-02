# T-0337 — round 21, the Shaper-only THIRD closing pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `7519618d` at session start (rounds 11–20 committed), editor on port 7801, **PID 184580** — the editor round 20 relaunched, still up; `cap.sh` and `capprint.ps1` were already pointed at it and were re-checked rather than assumed. `Application.dataPath` confirmed `D:/UNITY/Laubrary Dev - Shaper/Assets`; `isPlaying=False`, `isCompiling=False`, `scriptCompilationFailed=False`, active scene `Assets/Demos/ShaperDemo/ShaperDemo.unity` `dirty=False`. **No Laubrary tool window was open at session start**, and `Assets/Shaper` held only the two untracked `New Shaper*.asset` files rounds 11/12/18/19/20 left.

**Scope is Shaper only** (owner, 2026-09-09). No other tool window was opened, audited or edited.

**This file is written incrementally — each section is appended as it lands.**

---

## 0. Session log (append-only)

- Read `ShaperHarmony/RULES.md`, `ui-layout-rules.md` (the hard gate), `UNITY_DEV_GUIDE.md` and `T-0336/ROUND-20.md` in full before touching anything.
- Probe library copied from `T-0336/probes` into `T-0337/probes` and re-pointed (`T-0336` → `T-0337`, `T336.` → `T337.`, `AuditT336` → `AuditT337`, `fill-sweep-t336` → `fill-sweep-t337`). **Every hard-coded path in a copied probe was checked before it ran.** Two were already correct for this task (`capprint.ps1`'s `-ProcId 184580` and `cap.sh`'s, both pointing at the live editor); the six absolute `.tsv` destinations and the two runner `$D` roots were wrong and were corrected. `T-0334/probes/zpress.cs` was diffed against the copy already in `T-0336/probes` — byte-identical, so the inherited one is the same file.
- `zrunl.sh` carried `--json --timeout 540 command eval_file`. The CLI rejects that: `--timeout` is an option **of the `command` verb**, not a global one, so the flag had silently never been applied in the rounds that used it. Corrected to `--json command --timeout 540 eval_file`. It still does not help — the **server-side** Pipeline cap is 30 s regardless — which is exactly why the card says to poll the process, not the CLI.
- Session-start pref values recorded for restore: `T0312.out = …/T-0324/out`, `T320.capOut = …/T-0324/shots/cap.png`, `T320.capWin = ShaperWindow`, `ZuiSectionToggleBar.ShaperWindow.userSel = Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`, `Shaper.lastView` unset.
- **A counting error in round 20, carried into this card.** ROUND-20 §2.3's prose says "**Thirteen** more declared dials", and then lists **twenty** names (6 on Arc Burst, 12 on Plasma Bloom, 2 on Fork Blast). This round worked the list, not the number. Of the twenty, **fourteen** turn out to be genuinely guarded and are now greyed; **six** are live and were only ever measured dead because of *when* round 20 sampled them. The two numbers reconcile: 14 + 6 = 20.

---

## 1. The twenty dials round 20 named but did not trace

Every one was traced to the engine's own `if` **and every citation was re-read in the source before it was used** — the trace was produced by a read-only research pass, then each `file:line` was opened and the line quoted back. Two of round 20's own line numbers were off by one and are corrected below.

Then each was **re-measured over all eight frames** (`probes/d8-correct.cs`, `out/corrections.tsv`; follow-ups in `probes/d9-correct2.cs`, `out/corrections2.tsv`), not at the three mid frames round 20 used. That single change to the instrument moved six of the twenty out of the "dead" column.

### 1.1 Fourteen are genuinely guarded — greyed, each with its own sentence

| dial | guard | engine | measured: shut → open |
|---|---|---|---|
| Arc Burst `Deep ghost lo`, `Deep ghost hi` | the **active arc pattern's** own `Deep ghost` share | `PyreArcBurst.cs:403` — `if (rng.Random() < deepP) return rng.Uniform(deepLo, deepHi);` | 0 → **549** / **681** |
| Plasma Bloom `Drift Ease`, `Drift Linear`, `Drift Lag` | `Drift Amount` **and** (`Drift X` or `Drift Y`) | `PyrePlasmaBloom.cs:206` — `if (f.live.driftX == 0f && f.live.driftY == 0f) { sx = sy = 0f; return; }`; `:207` — `float d = rMax * f.live.driftAmt * Prog(…)` | 0 → **1429** / **1853** / **1745** |
| Plasma Bloom `Bloom Mode`, `Gate Gain` | `Lobe Count` ≥ 2 **and** (`Lobe Amount` or `Plume Amount`) | `PyrePlasmaBloom.cs:327-328`, `:383`, `:403`, `:405` | 0 → **1529** / **1688** |
| Plasma Bloom `Plume Amount` | `Lobe Count` ≥ 2 | `PyrePlasmaBloom.cs:328` — `bool plume = f.mode == …Plume && lobes;` | 0 → **1941** |
| Plasma Bloom `Plume Reach`, `Plume Width` | `Lobe Count` ≥ 2 **and** `Plume Amount` | `PyrePlasmaBloom.cs:395` `if (plume)`, `:403` `shell += f.live.plumeAmp * gate * …` | 0 → **2024** / **1991** |
| Plasma Bloom `Plume Vary` | `Lobe Count` ≥ 2, `Plume Amount`, **and** `Front Warp` | `PyrePlasmaBloom.cs:399` — `if (warp && f.live.plumeVary > 0f)` | 0 → **1952**, and back to 0 at `warp = 0` |
| Plasma Bloom `Swirl follow` on **Chunks**, **Embers**, **Motes** | the generator's own `Swirl` | `PyrePlasmaBloom.cs:245` — `float ang = pop.th[k] + f.swirl * t * p.swirl + …` | 0 → **2120** / **2079** / **1110** |

Three of these needed reads the drawer did not have, and each is one small helper beside the ones T-0281 already wrote:

- **`AtLeast(owner, name, limit)`** — the mirror of round 20's `Below`. Plasma Bloom's whole lobe block is skipped below **two** lobes (`PyrePlasmaBloom.cs:327`, `bool lobes = f.lobes >= 2;`), so a plain non-zero read would wrongly free six dials at a single lobe. Verified at the boundary: `Plume Amount` is greyed at `lobes = 1` and live at `lobes = 2`.
- **`LayoutDeepGhost(owner)`** — Arc Burst declares `Deep ghost lo`/`hi` on the **form**, but the share that gates them lives on whichever pattern's settings box is in use. Crown and Lattice never call `Ghost` at all (`PyreArcBurst.cs:639`, `:688`) and carry no such field, so an absent one reads as shut. Verified: greyed on Bolt (ships 0), **live on Core** (ships 0.18), greyed on Crown and on Lattice.
- **`RootNonZero(name)` + `_reflectingForm`** — the only guard on this card that does not sit on the dial's own owner. A population's `Swirl follow` is its share of the **form's** `Swirl`, and a `PlasmaPopulation` has no way back to the form. The drawer names the form it is reflecting for exactly the span of the (synchronous) `FlowFields` call, in a `try`/`finally`. Verified three ways: greyed at `Swirl = 0`, live at `Swirl = 3`, and **live when no card is being built at all** — it fails open, like every other read in this file.

All fourteen guards are **one-directional**: each guard is itself live at the form's own defaults and is never gated by the dial it guards, so a greyed dial is always one drag away from live. The two Plasma Bloom drift halves (`Drift X`/`Drift Y` ↔ `Drift Amount`) *are* a mutual pair — and they are deliberately left live, exactly as round 20 left them; it is only the three dials **downstream** of the pair that are greyed. Measured both ways to be sure the pair is really mutual: `Drift X` alone moves 0 pixels, `Drift Amount` alone moves 0 pixels, the two together move 1429.

### 1.2 Six are LIVE — and round 20 measured them dead because of *when* it looked

This is the round's second cautionary tale, and it is the same family as round 20's own: **round 20 fixed its frame window to frames 2–4 of 8 after discovering that frames 0 and 7 are blank, and three of these dials only ever act at frames 5, 6 or later.** A window chosen to avoid one blind spot created another.

| dial | what it really is | measured |
|---|---|---|
| Arc Burst `Ghost floor` (`keepHueFloor`) | **Not gated at all** — `KeepHue` is unconditional (`PyreArcBurst.cs:273`). It only ever sees a stroke that came out a ghost (`:402`), and at Bolt's shipped five trunks and this seed **not one of them does**. Content-dependent, the `OrbForm.despeckle` shape. | 0 at Bolt defaults; **798** with six more trunks |
| Arc Burst `Escape lo` / `Escape hi` (`lattice.flyLo`/`flyHi`) | **Live.** Multiplied by a ramp that is 0 until `Net breaks at` (0.60) — `PyreArcBurst.cs:725-727`. Frames 2–4 are all below 0.60. | **1736** / **1757**, both entirely at frames **5 and 6**; and 0 at `burstStart = 1.0`, which is the guard actually shut |
| Arc Burst `Cooling shape` (`cage.coolK`) | **Live, but a short document can hold no frame that reads it.** `Cool = Pow(1 − Ramp(t, coolStart, 1), k)` is 1 at or before `Cool start` (0.88) and exactly **0 at the last frame**, so only a frame *strictly between* them reads the exponent. An 8-frame document has none. | **0** over 8 frames; **455** over 16 (frame 14); **438** over 32 (frames 28–30) |
| Fork Blast `Ignition` (`flash`) | **Live, same shape.** Its whole life is the first 12 % of a blast's own (`PyreForkBlast.cs:418`, `const float flashLifeFrac = 0.12f;`), and it fades in from zero, so it needs a frame strictly inside that window. | **0** over 8 frames; **936** over 16; **952** over 32 and 64 |
| Fork Blast `Solidity` (`opacity`) | **Live, but its condition is the Fill, not a dial.** It is an exponent on the Fill's alpha ceiling (`PyreForkBlast.cs:386-387`), and `Pow(1, k) == 1`. The default `shapeFill` is `OverLife` with `gradientAnim` **null**, and `Evaluate` returns alpha **1.000 at every point of life** — measured. | 0 at every value tried, on the default fill |

**None of these six is greyed**, and each says so on its own tooltip instead:

- `keepHueFloor` gets the `OrbForm.despeckle` treatment — a sentence naming what it reaches and the measurement that shows when it reaches nothing.
- `flyLo`/`flyHi`/`coolK`/`flash` get a sentence naming the window they act in. **They are deliberately not greyed** even though four of them measure 0 on an 8-frame document, because whether *any* frame lands in the window depends on the **document's frame count** — which is not a field on the owner, and must not decide a greyed control. Greying `coolK` on an 8-frame document would be right; on a 16-frame one it would be a lie.
- `opacity`'s condition is the **Fill drawn above it in the same card**. It is not greyed for a second, concrete reason: the Fill row goes through `ctx.Touch`, not `ctx.Rebuild`, so a card greyed on the Fill would stay grey after the author had already fixed it — the exact stale-grey trap `InertGuardFields` exists to prevent. **This is the one item on the list that is a live candidate for greying and is not greyed; it needs an owner decision (see §7).**

### 1.3 Where the fix lives

`Assets/Packages/Laubrary/Editor/PyreShaper/PyreFormShaperUI.cs` — the same three tables round 20 extended, extended again, plus three helpers:

| table / helper | what |
|---|---|
| `ConditionOf` | **+20 sentences**: fourteen naming a guard in the words the control beside it uses, six naming a window or a content dependency for a dial that stays live |
| `LiveIf` | **+14 guards**, live-checked against the guard on the same owner (or, for the three `Swirl follow` dials, on the form being reflected) |
| `InertGuardFields` | **+15 guard fields** — the eight per-pattern `ghostDeepP`s, and Plasma Bloom's `driftX`/`driftY`/`driftAmt`/`lobes`/`plumeAmp`/`warp`/`swirl` — so editing a guard rebuilds the card and the dial it just freed stops being greyed. `ArcBurstForm.layout` is deliberately **not** listed: it already carries `[ZUIShowIf]` from ten settings boxes, so `ZuiReflect.IsGate` (`ZuiReflect.cs:478`) already rebuilds on it, and a second entry would only fire `OnStructureChanged` twice |
| `AtLeast(owner, name, limit)` | new sibling read for a guard whose open state is a value **at or above** a limit (two lobes), the mirror of round 20's `Below` |
| `LayoutDeepGhost(owner)` | reads the form's `layout`, resolves the matching settings box, and reads its `ghostDeepP`; an unmapped pattern (Crown, Lattice) reads as shut because it draws no ghosts |
| `RootNonZero(name)` + `[ThreadStatic] _reflectingForm` | the one cross-owner guard, named for the span of the synchronous `FlowFields` call and cleared in a `finally` |

**No Pyre file was edited this round** — not even for an attribute. The whole change is in the one editor drawer.

**Verified after the change:** compiled twice, `scriptCompilationFailed=False` both times. Then `probes/db-verify.cs` read all twenty dials back off the drawer's own `DialInertReason` / `DialTooltip` / `IsDialInertGuard`: **56 rows, 14 greyed at the form's own factory defaults, 14 freed the moment the named guard was raised**, the six live ones all still live with their sentence on the tooltip, the `lobes = 1` / `lobes = 2` boundary correct, `Drift X` alone still correctly greyed, the Core/Crown/Lattice layout cases correct, the cross-owner read failing open when no card is being built, and every one of the fifteen new guard fields reporting as a guard.

---

## 2. The six hosted forms round 20 did not sweep AS DECLARED

`probes/d6-declared.cs` — round 20's declared walk (`ZuiReflect.FieldsOf` over the form, recursing into nested `[Serializable]` settings objects exactly as `ZuiReflect` does, every `[ZUIShowIf]` gate driven), **one form per eval**, with round 20's measurement fault fixed at the root: instead of a hard-coded phase triple, each form is first scanned for coverage over its own eight frames and every dial is then measured at the **three littest frames it actually paints**. Output `out/declared-dials-6.tsv` (2 861 rows) and `out/declared-totals-6.txt`.

| form | declared | reachable in some gate state | moving pixels | 0 pixels | greyed with a cited reason | never visible at any gate value | absented, with a reason |
|---|---|---|---|---|---|---|---|
| Inferno | 36 | **36** | 34 | 2 | (see below) | **0** | 4 |
| Orb | 180 | **180** | 173 | 7 | | **0** | 1 |
| Torch | 414 | **414** | 373 | 41 | | **0** | 1 |
| Jet | 274 | **274** | 212 | 62 | | **0** | 1 |
| Radial Jet | 516 | **516** | 377 | 139 | | **0** | 1 |
| Explosive Jet | 1 434 | **1 434** | 1 130 | 304 | | **0** | 1 |
| **total** | **2 854** | **2 854** | **2 299** | **555** | | **0** | **9** |

**Every declared dial on all six is reachable.** No field on any of them is hidden by a gate that no value of the gate ever opens — which, with round 20's three, closes the question for all nine hosted generators. The nine absented dials are the `[PyreSwarmOnly]` ones, deliberately removed by `PyreFormShaperUI`'s `Skip` with the T-0279 reason (a composite node hosts no swarm, so they are dead by construction here).

Two things about the counts, so the numbers are not read for more than they say. **A count is per PATH, not per field**: Radial Jet declares one `JetSettings` per variant, so `ringK` appears seven times in its 516 because seven variant boxes each own one. And Explosive Jet's 1 434 includes two entries of its blast schedule expanded in full. The honest per-form statement is "every path the drawer would draw", which is what a reader of the card sees.

### 2.1 The 555 zero-pixel dials, cross-referenced against the drawer's own reason tables

A dial that moves 0 pixels is not by itself a fault — the card's bar is that it must be **greyed with a cited reason, absented with a reason, or genuinely reachable**. `probes/d7-reasons.cs` asks the drawer itself (`DialInertReason` / `DialTooltip` / `IsDialInertGuard`, through reflection, at each form's own defaults) what it would do with every declared leaf, and the two tables are joined per path.

| form | moving pixels | greyed with a cited reason at defaults | a condition on the tooltip only | **no reason at all** | absented |
|---|---|---|---|---|---|
| Inferno | 34 | 0 | 0 | **2** | 4 |
| Orb | 173 | 0 | 2 | **5** | 1 |
| Torch | 373 | 32 | 0 | **9** | 1 |
| Jet | 212 | 28 | 16 | **18** | 1 |
| Radial Jet | 377 | 64 | 47 | **28** | 1 |
| Explosive Jet | 1 130 | 92 | 68 | **144** | 1 |
| **total** | **2 299** | **216** | **133** | **206** | **9** |

Of those 206, **51 are the probe's own limitation, not a finding**: they are `ramp` / `sootRamp` fields, and the sweep can only perturb a `float`, `int`, `bool`, `ZUIValue`, `Color` or a gradient stop — it has no way to change an `IZuiRamp`, so it records 0 for one whether or not the ramp reaches the picture. Said plainly rather than folded into a total. That leaves **155 paths over 54 distinct declaring-type.field names** that really do move nothing at their form's defaults and say nothing about it.

A **bounded** guard search then asked, for one representative path of each, whether a single sibling on its own owner opens it (`probes/dc-guards.cs`; hard 150 s wall-clock budget, at most 26 siblings per dial, first hit wins, and — the thing round 20's version got wrong — every sibling AND the dial restored after each try, including `ZUIValue` scalars). Every run finished well inside the budget; the longest was 36 s and **the editor was never wedged**.

| verdict | count | detail |
|---|---|---|
| moves already, at a frame the six-form sweep did not sample | 1 | `InfernoForm.linger` |
| opened by one named sibling | 5 | `InfernoForm.hollowRim` ← `hollow` (1 795 px); `ExplosiveFlash.radius` / `amp` / `grow` / `elong` ← `life` (97 / 90 / 105 / 184 px) |
| **no single sibling opens it** | 48 | 3 on Torch (`rh`, `pulseSkew`, `pulseSharp`), 5 on Jet (`soot`, `sootLo`, `sootHi`, `shedKick`, `shedLife`), 3 more on Radial Jet, 43 inside Explosive Jet's blast schedule (`ExplosiveFracture`, `ExplosiveFracture2`, `ExplosiveGobs`, `ExplosiveChunks`, `ExplosiveDust`, `ExplosiveBlast.offX/offY`) |

**These 48 are named, not guessed at.** Writing "Nothing until X" for a dial whose guard has not been read out of the engine puts a wrong sentence on screen, which is worse than none — the same rule round 20 set and this round kept. They are the clean next piece of work, and the six that DID resolve are listed with their opening sibling so that pass starts with six already done.

---

## 3. Round 20's unexplained s42/s43 stage-hash disagreement

**Reproduced exactly, and it is not the probe.** On a scratch bag document built through the window's own New then the picker's own `Bag › Combine children`, `s42`/`s43` reported the same shape round 20 saw: four distinct PRE hashes, and a POST in which **all four frames carry one identical hash**.

The obvious explanation was ruled out first, by reading the code rather than assuming. `ShaperPreviewStage.Refresh` reads through the frame cache and, when the cache hands back nothing, **holds the picture already on screen** (`ShaperPreviewStage.cs:562-570`, T-0188, comment and all), with a prebaker filling the cache in the background — so a probe that edits, refreshes and reads in one round trip could legitimately read a stale picture. Measured (`probes/s50-stage.cs`, `s51-settled.cs`): `ComputeFrame` returns **pixels, not null**, on the first call after an `InvalidateFrameCache`; the same-round-trip hashes are stable; and a later round trip, after `EditorApplication.update` has run many times, returns the identical values. **The T-0188 hold is not what is happening.**

What is happening was found by measuring instead:

| step | what it says |
|---|---|
| `s21b` (stage asked FIRST, then the renderer, all 16 frames) on the freshly-made bag | **16 / 16 identical, mismatches = 0**, and the document paints **16 distinct pictures** |
| the same after `s43`'s reimport and `SetAsset` | 16 / 16 identical again — and now every frame is **the same picture** |
| the two documents' **asset files**, diffed | identical apart from `m_Name` and the `[SerializeReference]` rid renumbering — 340 diff lines, every one of them a rid |
| a full scalar walk of both documents (`probes/s56-fulldiff.cs`, 601 fields equal) | exactly two meaningful differences: **`layers[0].root.children[0].fill.authored`** and **`.border.authored`**, `True` on the static one and `False` on the animating one |

**The mechanism, proven in both directions** (`probes/s59-cause.cs`): flipping the animating document's `child0.fill.authored` from `False` to `True`, in memory, collapses all eight sampled frames to the single static picture; flipping it back restores the animation byte-for-byte. So **a bag member's `authored` flag decides whether the document animates** — with it clear the member inherits the bag's fill, with it set the member paints its own, which does not vary over the clip.

**The everyday path is clean.** `probes/s60-cold.cs` walks what an author actually does — author the bag, press the toolbar **Save**, force it back off disk, reopen it through the window's own `SetAsset` — checking the flags and what `ShaperBaker.RenderFrame` paints at each of the four steps:

| step | member flags | render, 8 sampled frames |
|---|---|---|
| 1 as authored, never saved | `fill = NULL`, `border = NULL` | 8 distinct pictures |
| 2 right after the toolbar Save | `fill.authored = False`, `border.authored = False` | **identical to step 1** |
| 3 after a forced reimport | unchanged | **identical** |
| 4 after reopening it in Shaper | unchanged | **identical** |

That is T-0271 behaving exactly as designed: Save materialises the objects Unity needs, `authored` stays clear, and the picture round-trips. Three individual candidates were then tested and all three are clean — the **toolbar Save** on an already-saved document changes nothing (`s58`), **`SetAsset`** on a saved document changes nothing (`s57`), and toggling the **Fill section chip** changes nothing.

**What is NOT established: which step set `authored` on a member nobody authored.** It happened somewhere in the longer walk the older scratch document went through, and the three most likely candidates are individually ruled out. So round 20's "instrument disagreement" is now a **named, reproducible behaviour with a proven mechanism and a clean ordinary path**, and one bounded question left: *what sets a bag member's `authored` flag without the author asking?* It matters because the answer is "the document silently stops animating", with nothing on screen to say so. **This is the round's most important open item.**

---

## 4. By eye — the greyed dials on a live Plasma Bloom and a live Fork Blast card

Both documents were built cold from the window's own toolbar (New, a name, Create, then the shape picker's own menu), never saved, deleted at the end.

**Plasma Bloom.** The live disabled-control census over the whole window (`probes/dz-live.cs`): `elements = 2007`, `controls = 566`, **28 disabled leaves, 27 of them carrying a reason on their own tooltip** (the 28th is §6's GIF finding). Round 20's five Plasma Bloom greyings are all present and correct — `Bias Dir`, `Bias Lobes`, `Gate Dir`, `Gate Soft`, `Gate Lobes` — and beside them this round's: `Drift Ease`, `Drift Linear`, `Drift Lag`, three `Swirl follow` dials and `Bloom Mode`, each with the sentence written for it. The guards `Lobe Count`, `Drift Amount` and `Swirl` stay **live**.

Raising the guards on the real window (`probes/dz-raise.cs`, then `dz-watch.cs` in a **separate** round trip — a rebuilt card's layout does not resolve until the next frame, so a same-eval check reports every control as "not drawn"; a fifth probe fault of the same family, caught before it was reported):

| dial | at the form's own defaults | after raising Drift X, Drift Amount, Swirl, Lobe Count, Lobe Amount |
|---|---|---|
| `Drift Ease` / `Drift Linear` / `Drift Lag` | **GREYED**, with their reason | **LIVE** |
| `Swirl follow` | **GREYED** | **LIVE** |
| `Bloom Mode` | **GREYED** | **LIVE** |
| `Lobe Count` / `Drift Amount` / `Swirl` | LIVE | LIVE |

**By eye** (`shots/pb-greyed-crop.png`, a real `PrintWindow` capture of the running editor): the rows read `Width Grow −0.05` · **`Bias Amount 0`** · *`Bias Dir 0`* — then *`Bias Lobes 1`* · **`Gate Amount 0`** · *`Gate Dir 0`* — then *`Gate Soft 0.35`* · *`Gate Lobes 1`* · **`Drift X 0`** — then **`Drift Y 0`** · **`Drift Amount 0`** · *`Drift Ease 0.9`*. Every greyed dial is dimmed **in place**, beside a live guard, nothing moved. `shots/pb-swirl-crop.png` shows *`Swirl follow 1`* dimmed beside a live `Spin rate 0`; `shots/pb-mode-crop.png` shows the *`Bloom Mode  Bloom | Plume`* segmented control dimmed beside a live `Lobe Count 0`.

**Fork Blast.** All seven of round 20's greyings hold on the live window, each with its sentence: `Gob size`, `Gob travel`, `Gob swell`, `Gob lifetime`, `Gob glow`, `Gob window` ("Nothing until Gob count is above 0.") and `Aim direction` ("Nothing while Spread angle is at 180: a full circle has no facing…"). `Gob count` and `Spread angle` stay live, and so — correctly — do this round's two, `Ignition` and `Solidity`. **By eye**: `shots/fb-gob-crop.png` reads `Shed mass (gobs)` / **`Gob count 0`** · *`Gob size 4.5`* · *`Gob travel 0.85`*; `shots/fb-aim-crop.png` reads `Emission shape` / **`Spread angle 180`** · *`Aim direction 0`* · `Angle bias 1.05`.

---

## 5. Temporal on the demo document, and the dial sweep re-run against HEAD

### 5.1 The demo document over time

`ShaperDemoDoc` bound (never edited, never saved — `IsDirty=False` throughout, and the demo scene stayed `dirty=False`). Play pressed from the window's own transport, **paused before every hash**:

| sample | elapsed since the play press | frame reached | hash | lit pixels |
|---|---|---|---|---|
| 1 | 5.18 s | 6 | `DD2E3556` | 4 433 |
| 2 | 4.74 s | 15 | `C4CF44D5` | 124 |
| 3 | 4.38 s | 11 | `0AE1E809` | 3 199 |
| 4 | 4.46 s | 7 | `11A31059` | 4 220 |

Four samples, every gap far above the 1.5 s the card asks for, four distinct frames and **four distinct pictures** spanning 124 → 4 433 lit pixels. The demo document genuinely moves.

### 5.2 The dial sweep, re-run against HEAD

`probes/t337-fillsweep.cs`, verbatim from round 20 with only its output path re-pointed; `probes/p1-rfield.cs` ran first so the 16 × 16 `RFloat` height field the sweep loads exists (round 14's trap). Output `out/fill-sweep-t337.tsv`, **414 rows** over a Primitive (Star), a Solid (Pyramid) and a Bag with two members.

| baseline | rows | rows differing | rows missing | **regressions (was > 0, now 0)** |
|---|---|---|---|---|
| `T-0336/out/fill-sweep-t336.tsv` | 414 | **0** | 0 | **0** |
| `T-0334/out/fill-sweep-t334.tsv` | 414 | **0** | 0 | **0** |
| `T-0328/out/fill-sweep-t328.tsv` | 414 | **0** | 0 | **0** |
| `T-0326/out/fill-sweep-t326.tsv` | 414 | **0** | 0 | **0** |

**Byte-identical to the last four rounds. No regression, and none introduced by this round's own change** — which matters, because the change is in the drawer every hosted generator goes through.

---

## 6. A greyed control that never said why

Found while taking §4's live census: of the 28 disabled leaves on a hosted-generator window, exactly one carried **no reason at all** — `GIF scale`, and its neighbour `GIF dither` with it.

Both sit in the `Bake` box and are deliberately greyed rather than hidden while `GIF` is unticked, so their values stay readable (`ShaperWindow.Bake.cs` says so in its own comment). But their tooltips were the ones written at construction — a full account of what a GIF upscale does and nothing whatever about why the control will not take a drag. A greyed control that explains its purpose and not its state is exactly the gap T-0280/T-0281 closed for every dial on a generator card, still open on Shaper's own Bake box.

**Fixed**, in `Editor/Shaper/ShaperWindow.Bake.cs`: a small `SetGifOptionsEnabled(bool)` that both enables the row and appends "Greyed out because GIF is not one of the outputs above — tick GIF to use it." to each control's own tooltip, removing it again the moment GIF is ticked — so neither tooltip can read for the wrong state (`ui-layout-rules.md`, "a conditional tooltip must read for the CURRENT state"). It replaces the two bare `SetEnabled` calls, so the initial build and the toggle callback go through one place.

---

## 7. Files touched

| file | what |
|---|---|
| `Assets/Packages/Laubrary/Editor/PyreShaper/PyreFormShaperUI.cs` | `ConditionOf` **+20 sentences**, `LiveIf` **+14 guards**, `InertGuardFields` **+15 guard fields**, and three helpers — `AtLeast(owner, name, limit)`, `LayoutDeepGhost(owner)`, and `RootNonZero(name)` with the `[ThreadStatic] _reflectingForm` slot that `Build` names for the span of its own `FlowFields` call (§1.3) |
| `Assets/Packages/Laubrary/Editor/Shaper/ShaperWindow.Bake.cs` | `SetGifOptionsEnabled(bool)` — the greyed GIF scale / GIF dither pair now says why it is greyed, and stops saying it the moment GIF is ticked (§6) |

**No Pyre file was touched this round, not even for an attribute** (round 20 needed one `[Tooltip]` correction; this round needed none). No `Editor/Pyre/**` file was opened. No `CHANGELOG.md`, nothing committed (ShaperHarmony rule 3). Four compiles this session, `scriptCompilationFailed = False` after every one.

## 8. State left behind

`git status` over `Assets/Packages/Laubrary` shows **exactly the two files above**, plus the `Samples~` block that was already modified before this session started. One further file is modified that this task did not author: `Assets/Demos/TextSplashDemo/Border Fonts/Splash Demo (LiberationSans SDF) Border Font.asset` — a TextMeshPro dynamic-font atlas that the editor regenerates across domain reloads. It was not modified at session start and this session forced four recompiles, so it is most likely a side-effect of those; it is named here rather than quietly left for the PM to commit.

**Seven assets and one folder deleted** through `AssetDatabase.DeleteAsset` (which removes the `.meta`): the five scratch documents `AuditT337B`, `C`, `D`, `P`, `F`, the sweep's `rfield0277.asset`, and the folder `Assets/Shaper/Audit0277`. A project-wide scan finds **0 dirty ScriptableObjects** and `AuditT337` matches **0**. `Assets/Shaper` holds only the two untracked `New Shaper*.asset` files that were there at session start — **not this task's, and not deleted**, exactly as rounds 11, 12, 18, 19 and 20 left them. Nothing was baked, so there is no bake output to remove.

Prefs restored: `ZuiSectionToggleBar.ShaperWindow.userSel` back to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`; `T320.capOut` and `T320.capWin` back to their T-0324 values; `T0312.out` restored **last**, after the final dumping probe (round 14's lesson); every `T337.*` key and `Shaper.lastView` deleted. `Undo.ClearAll()`.

The demo document was bound for §5.1 and **never edited and never saved** (`IsDirty=False` throughout); the demo scene is the open scene, `dirty=False`, and was never saved. **Play mode was never entered, the Test Runner was never run, no tag was created, and no confirm dialog or native file panel was opened.** The open window set is back to the editor's own seven; no Laubrary tool window is open, and none was at session start.

**The editor was NOT relaunched.** PID 184580 — the one round 20 left behind — ran the whole session. Four probe runs overran the CLI's server-side 30 s cap (`d6-declared` on Torch and on Explosive Jet, `dc-guards` on Torch and on Jet); in every case the editor kept executing and finished on its own, and the result was collected by **polling the output file**, never by re-sending. The one thing this cost was a genuine loss: while polling Torch's overrun, the loop's next pref write for Jet was queued behind it and Jet's run read the wrong pref, producing a duplicate Torch row and no Jet row at all — caught by the per-form row census and re-run on its own. `d6-declared` was then changed to write its totals **into the output file** as well as returning them, so a CLI timeout can never lose them again.

## 9. Verified how

**By probe, in the live editor:** the twenty dials of §1 traced to the engine's own `if` with every `file:line` re-read in the source before use, then re-measured over **all eight frames** rather than three; the coverage control that picks each form's three littest frames; the six-form declared enumeration with every `[ZUIShowIf]` gate driven (2 861 rows); the drawer's own `DialInertReason`/`DialTooltip`/`IsDialInertGuard` read back for every declared leaf of all six forms and joined to the sweep; the 56-row read-back of the fourteen new greyings, both states, including the `lobes = 1` / `lobes = 2` boundary, the Core/Crown/Lattice layout cases, one side of the mutual drift pair alone, and the cross-owner read failing open with no card being built; the bounded guard search over all 54 uncovered fields; the stage-vs-renderer comparison over 16 frames on three documents; `ComputeFrame` asked directly after an invalidate; the same-round-trip against later-round-trip hash comparison; the 601-field scalar walk of two documents and the `diff` of their asset files; the causal `authored` flip, both directions, on two documents; the four-step cold save and reload round trip; the live disabled-control census on two hosted generators; the guard-raise on the real window; four temporal samples with the transport paused before each hash; the 414-row fill sweep against four baselines; four compiles; and the asset, pref and window cleanup with a project-wide dirty scan.

**By eye, in real `PrintWindow` captures of the running editor** (`shots/`): a hosted Plasma Bloom card showing round 20's five greyed dials and this round's `Drift Ease` dimmed in place beside their live guards; `Swirl follow` dimmed beside a live `Spin rate`; the `Bloom Mode` segmented control dimmed beside a live `Lobe Count`; and a hosted Fork Blast card showing `Gob size` and `Gob travel` dimmed beside a live `Gob count`, and `Aim direction` dimmed beside a live `Spread angle 180`.

**Not verified:**

1. **No *human* has operated any of this.** Every press was a synthesized pointer or key event — the same code path a real one takes, not the same hand.
2. **What sets a bag member's `fill.authored` flag without the author asking (§3) is not identified.** The mechanism is proven and the ordinary path is clean; the trigger is not found, and it silently stops a bag document animating.
3. **48 declared dials across Torch, Jet, Radial Jet and Explosive Jet move 0 pixels at their own defaults with no reason on the card** (§2.1), and no single sibling opens them. Measured, not traced.
4. **51 `ramp` / `sootRamp` paths were not really measured** — the sweep cannot perturb an `IZuiRamp`, so their "0 pixels" says nothing either way.
5. **`ForkBlastForm.opacity` is a live candidate for greying and is not greyed** — its condition is the Fill drawn above it in the same card, and the Fill row does not rebuild the card. Owner decision (§10).
6. **`CageSettings.coolK` and `ForkBlastForm.flash` measure 0 on an 8-frame document and are not greyed**, because whether any frame lands in their window depends on the document's **frame count** — greying on that would be right at 8 frames and a lie at 16. Owner decision (§10).
7. **`Editor/Pyre/**` was never opened**, per the programme's read-only rule, so nothing here says whether Pyre's own window shows the same fourteen dials live and silent. It draws them through its own drawer, so it very likely does.
8. **Nothing was measured below 820 × 520 or above 1500 × 900**, and no window was measured while another Laubrary tool was open.

### Trivia (named, not fixed)

- Round 20's "thirteen" in its §2.3 is a miscount of its own twenty-name list (§0). Corrected here rather than propagated.
- The `Views` bar's native dropdown and the shape picker's duplicate names are unchanged — both still open owner questions (T-0260 Q11 and Q10). The picker's 43 entries still include `Orb`, `Gem`, `Box`, `Pyramid`, `Can`, `Star`, `Polygon`, `Text`, `Fire` and `Fireball` twice each.
- `zrunl.sh` had been passing `--timeout` in a position the CLI rejects outright, so no round that used it ever got a longer timeout. Corrected; it makes no difference, because the 30 s cap is server-side.

## 10. For the owner (T-0260)

Two Shaper-only questions, both small, both about **greying**, and neither guessed at:

1. **Should `Solidity` on a hosted Fork Blast card grey out while the Fill above it has alpha 1?** It is an exponent on the Fill's alpha ceiling and `Pow(1, k) == 1`, so on the fill the source ships with (`Solid`, alpha 1.000 at every point of life — measured) the dial takes a drag and moves nothing. The reason it is not greyed today is mechanical, not editorial: the Fill row goes through `ctx.Touch`, not `ctx.Rebuild`, so a dial greyed on the Fill would stay grey after the author had already fixed it. Greying it would mean making the Fill row rebuild the card.
2. **Should a dial whose window no frame lands in grey out?** `Cooling shape` on an Arc Burst Cage acts only strictly between `Cool start` (0.88) and the last frame, and `Ignition` on a Fork Blast only in the first eighth of a blast's life. On an **8-frame** document neither has a frame to act on and both measure exactly 0; at 16 frames both come alive (455 and 936 pixels). Greying on the document's frame count would be correct at 8 and wrong at 16, so both are left live with a sentence saying what window they act in.

---

## 11. Verdict

Round 19 found three items after eighteen passes had called Shaper clean; round 20 found one. This round found four, and the largest of them is not a dial.

**The twenty dials round 20 named but did not trace are now traced** — every citation re-read in the source, two of round 20's own line numbers corrected on the way. **Fourteen are genuinely guarded and now grey with a sentence naming their guard in the words of the control beside them**, verified in both states on the real window and by eye on two hosted cards. The other **six are live, and round 20 measured them dead because of *when* it looked**: it fixed its sampling to frames 2–4 of 8 after finding frames 0 and 7 blank, and three of these act only at frames 5, 6 or later while a fourth acts only in the first eighth of a blast's life. That is the programme's own lesson landing for the third round running — the probe is as likely to be wrong as the tool — and it is why every measurement here runs over all eight frames.

**The six hosted forms round 20 did not sweep are swept.** All 2 854 declared paths are reachable in some gate state, on every one of the six; with round 20's three, that question is now closed for all nine hosted generators. But the sweep also surfaced this round's honest residue: **48 declared dials across Torch, Jet, Radial Jet and Explosive Jet move nothing at their own defaults and say nothing about it**, and no single sibling opens them. They are named with their measurements rather than given invented sentences.

**Round 20's unexplained instrument disagreement is explained — and it is real.** It is not the probe's timing: the stage and the renderer agree 16/16 at every step. It is a bag member's `fill.authored` flag, proven causal in both directions on two documents whose asset files are identical bar the name. With it clear the document animates; with it set every frame is the same picture. The ordinary authoring path — author, Save, reimport, reopen — is clean at all four steps, and the toolbar Save, `SetAsset` and the Fill chip are each individually ruled out. What is left is one bounded question the next pass should answer, because its consequence is a document that silently stops moving.

Everything the earlier rounds fixed holds: the 414-row dial sweep is byte-identical to the last four rounds, round 20's fifteen greyings are all correct on the live window and visible in captures, and the demo document plays four distinct pictures over four samples. And one more small thing was found and closed on the way — a greyed `GIF scale` in Shaper's own Bake box that explained what it did and never why it would not take a drag.

**FOUND: 4 non-trivial items, next pass needed**
