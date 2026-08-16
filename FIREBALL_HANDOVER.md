# PROCGEN Fireballs — handover

*Written 2026-08-09 so this project can be continued from Laubrary Dev. Lautaro's instruction: the OutBurner session handles only OutBurner; fireballs and Interlude move here.*

## ✅ ROUND 7 COMPLETE — 38 files, FOUR agents, 2026-08-09

**Three generators finished.** a02, a03 and a04 were each told, in the same words: *"This is done. Dont iterate more untill i will ask you to turn it into a PyrePlus generator."* Each project carries a `STATUS_DONE.md` saying so. **Do not spawn a fireball round in those three.** Their only remaining work is the **PyrePlus port**, on request. Their round-6 output plus `MANIFEST_R6.md`, `PROGRESS.md` and their ledgers are the handover material.

Round 7 pool: `ROUND7\_output\` — a01 ×8, a05 ×16, a05b ×6, a05c ×8. `ROUND7\_rejected\` holds two withdrawn batches (recoverable, still ledger-traceable). `_MANIFEST.txt` and `_triage\round7_all.png` written.

⚠️ **Two batches were rejected on sight and redone in-round**, and one rejection was itself a mis-routing: the *"2 explosions stacked"* note was given to a05 and actually belonged to a05c. a05's files were restored intact; a05 proved it had changed no code in the window by replaying three published genomes byte-identically, then published a second batch of 8 from the same build. **When re-routing feedback, restore first and make the agent prove build identity before it publishes again.**

### What each of the four found

| agent | defect | named cause |
|---|---|---|
| **a01** | *"fire … now not usefull. It skews in a very weird way"* | its own opening fix. It had correctly diagnosed `body(t) = Pace(t) × one fixed shape` (a **similarity** transform) and answered with an **affine** one — still a global transform of one rigid form, now visible as a skew. Replaced with structure that cannot lean *by construction*: k-fold symmetric bud rings (zero second-moment anisotropy) and raggedness on harmonics {3,5,8,13}, **lean being the second harmonic and therefore absent from the basis**. |
| **a05** | *"patches that are … just one shade of grey"* | its colour is a lookup on three integer axes and **two die in the dense body** — opacity saturates, and the shade term's coefficients sum to 1.30 so it clamps, pinning 64–68% of the dense body at shade 0. Dense body: 4 tones, 48.9% on one. Also **separated opacity from illumination** (two renders over two known backgrounds), retiring its own round-6 "inseparable" caveat. |
| **a05b** | *"small hard rock like objects"* | **convexity, not tone.** Solidity (area ÷ convex hull) is 0.697 across 55 clean sheets vs 0.848 in its own output. *A convex blob with a boundary is a rock however it is shaded.* ⚠️ **NOT CLOSED** — its hollowing fix reached the reference band but made a **cracked net, worse than the rocks**, caught by eye with no metric objecting. Ships at 0. |
| **a05c** | *"2 explosions stacked"* + *"rotates to quickly"* | **a lighting bug, not geometry.** Gas in-scattering lit the smoke from the blast's **static declared origin** while buoyancy carried the fire upward — a bright head and a detached glowing arc at the foot. Rotation: `movement.py`'s `rot` is dominated by **churn**, not the rigid turn, so cutting spin alone could never have fixed it. |

### ⚠️ The coordinator's suggested tests were blind — twice, in one round

I told a05c to find the two-body split by **counting connected components** and looking for a **vertical waist**. Both report the rejected batch **completely clean**: the silhouette is one connected blob because smoke fills the gap, and the split lives only in the **luminous** material. Row/column projections fail too — a diagonal split reads 0.365 on rows/columns and **0.999 on all 36 axes**. It caught both suggestions with a synthetic instrument check before trusting either.

**Looking, not measuring, found the defect in both redos** — a05c first *saw* the split on mid-life sheets, and swapped a seed that had passed every gate; a01 found its `openRough` ceiling by eye at a setting where *"every number was fine"* and the fireball is a starfish.

---

## ✅ ROUND 6 COMPLETE — 56 files, all seven agents, 2026-08-09

`ROUND6\_MANIFEST.txt`, `ROUND6\_triage\round6_all.png` and `ROUND6\_PIXELART_REFERENCE.md` are written. **Nothing graded yet.** Every agent published 8.

**Every named defect was traced to a NAMED cause this round** — the best diagnostic round the project has had:

| agent | the defect | the cause it found |
|---|---|---|
| a01 | cloud "unusable" | shading used a **2D normal over a disc**, whose dot product concentrates at zero — so every puff landed mid-ramp regardless of the ramp. Five sessions of palette work could never have widened it. Also the fire→smoke "bridge" **was** the cloud (33.2% of the sprite, cold end pinned to the brightest smoke stop). Drift regression fixed and gated: 0.232–0.426 → 0.043–0.114. |
| a02 | "fire looks in front of the smoke" | literally **one boolean per pixel** (`isFire`), drawn wholly in one ramp or the other. Dense smoke could not occlude fire; fire through thin smoke was never tinted. |
| a03 | "snaps upwards" | **`burnCap` gave every over-running lobe the identical end time**, so they all went out on the same frame; last-born lobes sit low, so survivors are up top. Worst 1-frame fire loss 0.319 → 0.111. |
| a04 | red rims through dense cloud | **`warmReach` seeded from fire BURIED behind smoke**, tracing a hidden fireball's silhouette onto the cloud in front of it. Only `twin` shows it because only `twin` offsets its bursts in space *and* time (buried-fire share 0.393 vs 0.151–0.298). |
| a05 | fire/smoke blend "too sharp" | fire was a **classification** while smoke was a field — two different resolution rules meeting at a boundary, which no tuning can soften. Fire now contributes `tauF` to one integral. |
| a05b | "grows by snapping too hard" | **a step, not a fast curve**: across a 2× duration change the *mean* growth rate scaled 1/N but the *peak* rate did not move at all. Parcels born at full mass in clumped cohorts (jitter ±4 whole spacings). `sooty_003` was **one character** — `palette = 1` (sulphur) instead of `2` (sooty). |
| a05c | fire doesn't churn | round 5's fire *did* churn, but took **0.281 of a lifetime** to half-decorrelate, so a clip was nearly over before its interior changed once. Now 0.090. |

### ⛔ A COORDINATOR ERROR WORTH NOT REPEATING

One agent's measurement of the pixel-art pack was relayed to five agents **as settled fact, in a table, unverified** — the project's eighth instrument failure and the first caused by the coordinator rather than an agent. Three agents then measured the pack independently; **two agree with each other and contradict the propagated figure, one in the opposite direction** (ember: propagated 0.505 pack / 0.125 generators; measured 0.127 and 0.097 pack, 0.30–0.42 generators).

It reached real decisions before it was caught: a01 recorded a round-7 target against it, and a05 pulled back its softness on the strength of it having *"taken the coordinator's numbers on trust"* while having read exactly one sheet. Retracted in `_brief\PIXELART_REFERENCE.md`, with the standing rule now written there: **no number in that document may be tuned to unless its population and thresholds are written beside it.**

### The one thing three agents converged on independently

**Grain size.** a04 (median piece 2–4 px, 12–45% single-pixel), a05b and a05c (1–6 px, 5–14 lone pixels per file) all measured their tails against the native sheets and found the same thing: **not too fragmented — too finely ground.** The medium's grain is 3–30 px lumps with essentially zero single-pixel pieces. The lever is a **floor on fragment size**, not a cap on count. This is the clearest cross-agent signal since the cauliflower in round 3, and it is the obvious round-7 headline.

Also unbuilt anywhere: the **hollow-outline ending** (interior empties, extent held) and **endings that resolve into discrete outlined objects** rather than an eroded field.

---

## ROUND 6 — launched 2026-08-09

Lautaro rated 4 of round 5's 52 files (all into `2`, nothing deleted), gave written per-agent feedback, and said *"Time for round 6!"* / *"Go round 6."* All seven agents relaunched with their feedback verbatim. Shared brief `_brief\ROUND6.md`, copied to each project as `ROUND6.md`. Output: `…\PROCGEN Fireballs\ROUND6\`. Rounds 1–5 frozen.

**Round 5 was the best round so far — nobody was told they went backwards**, which had not been true of any earlier round: *"firepart is excellent"* (a01) · *"Very good!"* (a02) · *"quite good"* (a03) · *"very good!"* (a04) · *"Much bettrt… Improvement needed is no longer smoke"* (a05).

### ⚠️ PIXEL-ART REFERENCE PACK — added mid-round 6, and it exposes a five-round category error

Lautaro supplied **66 hand-drawn pixel-art VFX sprite sheets** (chroma-keyed sprite rips). Stored at `_brief\pixelart_reference\` + `_brief\PIXELART_REFERENCE.md`, copied into all seven projects as `_pixelref\` + `PIXELART_REFERENCE.md`.

**For five rounds the only reference on this project was six live-action video clips — and the deliverable is pixel art.** Every measurement of "what a real explosion does" came from photographed fire. Nobody flagged the mismatch. The video says what fire *does*; these say how the medium *depicts* it, and where they disagree these are closer to the deliverable.

Three findings, offered as leads:

1. **Fire→smoke is a colour journey through ONE body** (`px_0111` is the clearest; also `px_0110/0112/0119/0146`): white-hot → yellow → orange → deep orange → brown → grey-brown → dark grey → specks, one continuously evolving silhouette, never a moment where a grey object arrives. **This independently confirms Lautaro's own suggestion to a04** — it is the medium's standard convention, not a preference.
2. **Dissipation is DISINTEGRATION INTO DISCRETE SPECKS, not a smooth fade** (tails of `px_0110`–`px_0113`, `px_0119`, `px_0144`, `px_0153`). Several generators here dissipate by lowering opacity smoothly — that is the *video's* behaviour, not this medium's.
3. **Frame counts are short** — many sequences 4–12 frames, longest ~28, against this project's 30–120. Silhouette does the work, not duration.

⚠️ **Honest tension to resolve deliberately:** round 6's theme is *softer edges*, but most of these sprites have a **hard cel edge on the body** with the *dissipation* granular. Both can be true; do not let one silently override the other.

⚠️ **Keying:** these are chroma/index keyed, NOT composited — key the exact background colour out. Do **not** apply the green-key un-premultiply that the *video* references need; that works because photographed smoke is genuinely translucent over the key, and applying it here corrupts edge pixels.

Delivered to the six agents still running as **explicitly optional**, with instructions not to restart, delay or republish. a02 and a03 had already published when it arrived. **Recommendation: let round 6 finish, and make this pack a first-class part of the brief from round 7.**

### The three cross-cutting themes of round 6

1. **SOFT EDGES** — named for a01, a02, a04, a05c, then generalised: *"in general all of them could use softer edges. But this is perhaps better handled by a general edge softness parameter when they are in the editor."* ⚠️ That second sentence is the first time Lautaro has framed a fix as belonging to **the editor rather than the generator** — it is a direct signal about the PyrePlus destination. Agents were told the strongest answer is a demonstrated **knob**, not a fixed softer look.
2. **FIRE AND SMOKE ARE ONE MEDIUM, NOT TWO LAYERS** — a02 (*"the firepart is in front of the smoke"*), a05 (*"blending… is to sharp"* + *"they need to move more together"*), a04 (*"the smoke cloud appears is a bit to hard"* + red rims through dense cloud), a01 (*"perhaps it only works with the fire"*). One compositing seam described four ways. a04's own suggestion — smoke born in fireball-gradient colours, cooling into the smoke gradient — was shared with everyone.
3. **THE FIRE MUST DEVELOP AND CHURN** — a05c, a01, a05b. With the smoke now good, the fire is what risks reading as static.

**a04's cloud is the project benchmark**: *"The development and especially dissipaton of cloud is the best there is!"* Every agent was told the bar exists and pointed at a04's round-5 GIFs to watch. Code isolation is unchanged — ratings and published GIFs are shareable, projects and notes are not.

### Round 5's four graded files — all labelled negatives

`r5_a01_dieout_002`, `r5_a01_ground_004`, `r5_a04_twin_006` (*"red borders of firebals shine trhough the dense cloud"*), `r5_a05b_sooty_003` (*"awful and did not look like fire"*).

---

## ROUND 5 — launched and completed 2026-08-09

**The mark arrived.** Lautaro delivered written per-agent feedback for round 5 (reproduced verbatim below), which released the hold. All seven agents were launched in parallel, one per project, each with its own feedback quoted verbatim in its prompt.

- Shared round-5 brief: **`D:\Unity\FireballMega\_brief\ROUND5.md`**, copied to each project root as `ROUND5.md`. `BRIEF.md` is unchanged and still governs — per-round feedback has never gone into it, it goes in the agent's prompt.
- Output folder: **`D:\Claude@GDrive\PROCGEN Fireballs\ROUND5\`** (`_output\`, `1`–`5`, `_triage\`, `_HOW-TO-RATE.txt`). Rounds 1–4 frozen and untouched.
- Each agent writes `MANIFEST_R5.md` to its own project root; those get consolidated into `ROUND5\_MANIFEST.txt` on publish, as in round 4.
- Batch size is **up to** 8 this round, explicitly allowed to be fewer — Lautaro said the gas work *"can take its time"*, so depth beats count.

### Round 4's grades, as they finally stood

Grading was **sequential, not a sample** — a01 → a04 in order, then it stopped. a05/a05b/a05c were never graded at all and were told to treat the written feedback as their whole result.

| agent | 4 | 3 | 2 | 1 | ungraded |
|---|---|---|---|---|---|
| a01 | – | – | 2 | 1 | 5 |
| a02 | – | – | 1 | – | 7 |
| a03 | – | 3 | – | – | 5 |
| **a04** | **6** | 1 | – | – | 1 |
| a05 / a05b / a05c | – | – | – | – | 8 each |

**No 5s in round 4.** a04 took every grade-4 slot awarded.

### Lautaro's round-5 feedback, verbatim

> **A01** has gotten worse. Mostly cause the end part with the dark soot cloud doesnt die out the whole animation just ends.
>
> **A02** should go into darker colors in the end especially the cloud. Stil way to wobbly. But it has improved.
>
> **A03** improved. Worst part is that it stops before the explosion ends.
>
> **A04**: this is quite good now. What i would like to see are the individual fireball blobs in the explosion: they have a tendency to grow in size and rotate as a motion, but id like them to internally churn more on the inside. Cause its clear enough to see that its like a static image of a fireball that just rotates. Also, the smoke cloud should be more gas or liquid like.
>
> **A05**: has deteriorated. The cloud smoke part is too bad now. It looks more like solid liquid mercury than cloud and gas. I would like A05 and others that have problems with gas to iterate and compare their cloud to the cloud parts of the reference videos. It can take its time if you think it can make a difference.
>
> **A05b**: Its better than A05. The gas is more convincing, but not good enough.
>
> **A05c**: I like where this is going. However it has some tendency to make the explosion bump back near the end. Thats weird. Also the gas doesnt seem to evaporate in the end.

### The two cross-cutting themes given to all seven

1. **THE ENDING** — three agents, three versions of one defect: a01 (*the soot cloud doesnt die out, the animation just ends*), a03 (*it stops before the explosion ends*), a05c (*the gas doesnt seem to evaporate*). The dissipation has never been on screen anywhere in this project. Both failure modes are present: cutting too early, and cutting while the cloud is still solid. Gated against the two known cheats — no dead trailing frames, and no metric that truncating the clip earlier would improve.
2. **GAS** — four agents told the smoke is unconvincing (a04 *more gas or liquid like*, a05 *solid liquid mercury*, a05b *not good enough*, a05c *doesnt evaporate*), with an explicit owner instruction to compare against the **cloud parts** of the reference clips and explicit permission to take a long time over it. All were warned that the reference pack's own smoke data is the untrustworthy part: its `area`/`smoke` columns threshold on brightness and cannot see translucent smoke, and its sampled ramps encode thinness as darkness because they were sampled over black.

a04's note about blobs that *"grow in size and rotate"* rather than churning inside was shared with everyone, since it names a structural defect — a fixed interior pattern moved by a similarity transform — that others may have without having been told.

---

**⚠️ The standing instruction that governed rounds 1–4** — *"no agent produces new GIFs until Lautaro gives his mark… Don't do any changes until all feedback is in. In my mark."* — was satisfied by the feedback above. It applies again to **round 6**: nothing new gets generated until round 5 is rated and Lautaro's next mark arrives.

---

## Where everything is

| | |
|---|---|
| **Agent projects** | `D:\Unity\FireballMega\Agent01 … Agent05`, plus `Agent05b`, `Agent05c` — **seven** |
| **Shared brief** | `D:\Unity\FireballMega\_brief\BRIEF.md` (each project also holds its own copy at root) |
| **Seed project** | `D:\Unity\FireballMega\_seed` |
| **Output + ratings** | `D:\Claude@GDrive\PROCGEN Fireballs\` |
| **Reference data pack** | `…\PROCGEN Fireballs\_reference\` — per-frame metrics, palette ramps, contact sheets, from six MP4s |
| **Brief, owner-facing** | `D:\Claude@GDrive\PROCGEN-Fireballs-BRIEF.md` |
| **Progress log** | `D:\Claude@GDrive\PROCGEN-Fireballs-PROGRESS.md` |

Each agent keeps a **ledger** mapping every published ID back to the exact parameters that made it, so a rating always lands on something recoverable.

**How rating works:** files are named `r4_a05b_slowbloom_003.gif` — round, agent, what it demonstrates, counter. Lautaro moves them out of `_output\` into folders `1`–`5` (5 = best, 1 = disastrous), or deletes the ones still showing a problem. Agents poll those folders; it is their only signal. Ratings folders are **shared across agents deliberately** — they may see each other's scores, but **may not read each other's code**.

---

## Where the rounds got to

| round | files | outcome |
|---|---|---|
| 1 | 671 | 177 rated · **zero 5s**, 12 fours |
| 2 | 118 | 64 rated · **2 fives, 11 fours** out of 32 — the big jump |
| 3 | 95 | method changed: written per-agent feedback instead of grades; all files kept, `_snapshot\` holds MD5s |
| **4** | **56** (7 generators × 8) | **partly rated — see below** |

### ⚠️ Round 4 as it stands, 2026-08-09

**14 of 56 rated, 42 still in `_output\`, and no 5s yet.**

```
5/  0      4/  6      3/  4      2/  3      1/  1
```

**a04 holds every single grade-4 slot** (6 of 6), plus one of the four 3s; the rest of grade 3 is a03. That is the clearest per-agent signal the project has produced since round 2 — but it is 14 files, so treat it as a lead, not a verdict, until the rest is rated.

---

## What every agent was told for round 4

- **Traditional fire and smoke colours are what gets judged.** Exotic palettes (plasma, toxic) are welcome and will be used, but they no longer help a file score — so they are not a way to stand out.
- **Full fire colour range, deep saturated colours, blending nicely.**
- **The smoke cloud should travel the whole grey-to-dark range.**
- **Go back to the six reference clips and compare again.**
- **8 files each**, deliberate range, one file per idea, no sweeps.
- Faster and snappier than round 3.
- **No dithering.** Transparency and rich palettes are allowed instead.
- `a05b` and `a05c` are clones of a05, told to take the feedback **and make radical creative changes of their own**; original a05 got only the feedback; a05's approach was injected into a02.

---

## ⚠️ Metrics that were PROPAGATED AND THEN RETRACTED — do not resurrect

This is the most expensive part of the project's history and the easiest to repeat.

- **`tail90`** — points opposite ways across rounds. Verified across 231 graded files. Meaningless.
- **`endsAtPeak`** — an argmax knife-edge; no separation in robust form.
- **"longer is better"** — a confound. Lautaro then asked for *faster*.
- **"less saturated is better"** — a proxy for smoke share (r = −0.795), not a property worth tuning.
- **"more colours is better"** — not monotonic. The real variable is **redundancy** (colours within 3/255 of each other), not raw count.
- **"coarse not fine frequency" for fray** — wrong lever: *a threshold far out in the tail of a smooth noise field has tiny level sets at any frequency.*

**Two errors in the data pack itself:**

- ⚠️ **`drift` was defined wrongly in the brief** — described as *total travel*, but the published bands are **net displacement**. It cost three agents real time. Corrected definition: *net displacement of the **subject-mask** centroid, first valid frame to last, normalised by frame height, **unweighted***. Total travel is the wrong metric regardless, because it accumulates per-frame noise and so scales with frame count. Fire-only centroids are the worst choice of all (2.171 on one clip).
- ⚠️ **The reference pack under-reports smoke.** Its `area`/`smoke` columns threshold on brightness and cannot see translucent smoke; two clips read as 94% and 83% fire at peak, which is wrong. Relatedly, the sampled **smoke ramps encoded thinness as darkness** because they were sampled over black.

### ✅ SOLVED in round 5 by a05 — and the standing advice was backwards

Every previous round told agents to prefer the **two black-background clips** as "clean throughout" and to treat the four green-screen clips as contaminated. **For smoke that is exactly the wrong way round, and it is why every re-derivation of smoke colour so far has disagreed with every other one.**

> Over black you observe only `a·F` — one channel, two unknowns — so **thinness is inseparable from darkness**. That *is* the error in the packaged ramps, and no amount of care sampling over black can remove it. Over green you can separate them, and **the green spill IS the `(1−a)·B` term, so un-premultiplying removes the spill exactly.** The contamination and the matte are the same equation.

So the green-screen clips are the **good** smoke source and the black ones are the unusable ones. This also explains why the "true" opaque-smoke colours quoted across rounds never agreed — (70,69,65) vs (253,255,255) vs (255,245,179) vs (255,255,255) — they were all derived over black, where the quantity is not identifiable. a05 measured the material as **one colour** (in-frame spread 15/255) once properly matted, with smoke taking **half its own radius** to go transparent→opaque.

Two clip-level facts found at the same time: **`1439663147` is a composite reel** — green screen, then black partway through — and **`1337593978` already contains fire on frame 0**, so it has no ignition.

⚠️ This is now the highest-value unpropagated finding in the project. It has NOT been folded back into `_reference/`, so every future agent still gets the broken ramps unless the pack is rebuilt.

---

## Root causes worth remembering

- **Seed monocultures** — three projects independently converged because their seeding chose *values* but not *timing*: *"the seed chooses the values in the cells; it does not choose when the cells change."*
- **Global operators produce flat shapes.**
- **The "vine"** was children inheriting parents' *resolved* positions.
- **A static-pixel bug** traced to a canvas-space material threshold clamping at the last ramp index.
- **Vertical bob** came from lobe births quantised by generation — 82% of it remained with all motion switched off.
- ⚠️ **"The dissolve was never on screen"** — the ending metrics were satisfiable by never dissolving at all. A metric that can be satisfied by omission is not a metric.
- A **`framedelta.py` bug** found by a02: it divided by the *earlier* frame's lit count, unbounded at ignition, reporting up to 2.7e7. Patched to `max(prev, next)` with the first two pairs dropped.

---

## Analysis scripts

Written for this project, in the job scratch dir (`extract.py`, `dither2.py`, `sheet.py`, `clean.py`, `spacetime.py` slit-scan, `framedelta.py` per-frame change, `traces.py` overlaid centroid traces, `dierate.py`, `tail90.py`, `blank.py`, `altdiff.py`, `smoke2.py`, `whatsnew.py`, `r3sheet.py`, `r4sheet.py`). **They are not durable** — copy any you still want into `D:\Unity\FireballMega\_brief\` before relying on them.

---

## The destination

The end goal is **PyrePlus** — Laubrary's pixel-art explosion baker (`PyrePlusGif`, `PyrePlusSpec`, `FireballSim`, `PyreToPlusConverter`). Agents may read it and `PyrePlusGif.Export` saves writing a GIF encoder, but they are told **not to assume it is the answer** and are free to write a different model.

Lautaro's seven wishes for what PyrePlus should eventually gain, recorded 2026-08-08: parameters over time; separate fire and smoke sections; fire that shoots out energy; directional and staggered blasts; smoke gravity; swarm fusion; colour fill options.

---

## Round 5 landed — 52 files, all seven agents, 2026-08-09

All seven published and reported. `ROUND5\_MANIFEST.txt` (the seven manifests consolidated) and `ROUND5\_triage\round5_all.png` (peak frame of every file, grouped by agent) are written. Counts: a01 7, a02 7, a03 8, a04 8, a05 7, a05b 7, a05c 8. Three agents published fewer than 8 deliberately — a01 rejected 5 dry runs on its own gate, a05 and a05b spent the slots on the gas as instructed.

**Three agents independently derived the same green-key matte** (a04, a05, a05b), each without access to the others' work. That is the strongest convergence the project has produced since the cauliflower in round 3, and it is what makes the reference-smoke correction above trustworthy rather than one agent's opinion.

**Every agent found its round-4 defect was structural, not a tuning error**, and in four cases the round-4 "fix" *guaranteed* the defect: a01's gate was minimised by a cloud that never shrinks; a03 set `dieHold=0.92` so mass stopped shrinking before the clip ran out (*"I optimised an ending metric by not ending"*); a05c froze the opacity clock, turning the last sixth of every clip into an accidental mass **source**; a05b's `TailLoss < 0.15` rewarded never dissipating — measured, the best reference dissipation *fails* that gate.

⚠️ **Two known flaws shipped into round 5, both self-reported, both worth knowing before rating.** a01 broke centring (drift 0.23–0.43 against a ≤0.12 centred band) and only measured it after publishing. a04's colour redundancy went 0.006 → 0.434; it identified a one-line fix and correctly did not apply it, because the round publishes once from one build.

**Tooling bug found and fixed mid-round:** `ueval.sh` in both a05b and a05c was inherited from a05 and still pointed at **a05's** pipeline port, so evals ran in another agent's editor. a05c and a05b each found it independently; a05b's copy plus two `sys.path` inserts into a05's module folders were corrected by the coordinator. Also established: **GIF frame delay is integer centiseconds**, so round 4's nominal 18fps and 16fps files encoded identically at 16.67fps — any pace pair claimed on fps alone is unproven.

## To resume

1. **Round 6 waits for Lautaro to rate round 5 and give his next mark.** Round 6 → another new folder; rounds 1–5 stay frozen. Rating had already begun during the round (two a01 files into `2\`).
4. Agent projects are unchanged and each holds its brief, its `ROUND5.md` and its ledger; a session can be pointed at one directly.

### Two things worth doing that nobody has done

- **Nobody on this project has ever watched a GIF play.** Every judgement, including every containment and motion measurement across five rounds, was made from decoded frame grids and contact sheets. Two of the defects Lautaro reports — the a04 turntable and the a05c bump-back — are motion defects that a still cannot show.
- **The reference smoke data has known errors that were never repaired at source** (brightness thresholding that cannot see translucent smoke; ramps sampled over black so thinness reads as darkness; green-screen spill on four of six clips tinting smoke olive). Every round since has worked around them per-agent. Fixing the pack itself would stop the whole field re-deriving it seven times.
