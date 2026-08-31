# Q3 — adversarial verification of the "Option 2 hardened" round

Verifier Q3, task T-0098 ("Future of Pyre"), project Laubrary_Dev. Read-only against the repo (no tree-mutating git; `git status`/`git diff` only). Nothing in `D:\CODEZ\AgentHQ\3D Shaper` was modified. No Unity editor or Coplay bridge used. **No subagents were spawned**, so no agent-registration POST was required.

**Scoreboard: 3 ERROR · 3 OVERSTATEMENT · 5 OMISSION · 5 QUIBBLE.**

**The three that matter most**

1. **ERROR-1** — the star's measured over-report figure (1.308×) is wrong, and it is written into a build task as a number an implementer will use. At the lobe tip the factor is exactly **1.000×**; the star's real worst case is **≈2.39× outside / ≈3.13× inside**. A build agent that takes 1.308 as the star's bound builds the exact holes the clause exists to prevent.
2. **OMISSION-1** — the dominant non-metric term is **not** in the primitive registry. It is the extrusion/bevel height profile, and `stepped` has **no finite bound at all** (it is a step discontinuity). Every bound requirement in this round was filed against T-0105 (primitives, transforms, combines); T-0109 (extrusion and bevel) carries none. The "twenty lines in the primitive registry" cost is understated and mislocated.
3. **ERROR-2** — B7's closing escape hatch bills the owner for a cost the chosen plan already pays. `SHAPER_THE_DESIGN.md:184` lists "the primitive engine stops being a dozen lines of arithmetic and takes on the guarantees above" as the price of asking for usable tilt — but `:174` puts exactly that guarantee into the recommended plan, and `:170` cites the same cost as a reason *not* to take the other option. Two of the three costs quoted against saying yes are sunk either way. This is the finding most likely to change what the owner decides.

---

## A. The load-bearing factual claims

### A1 — "the height a layer takes is a closed-form function of the shape's inside-distance" — TRUE, with one false generalisation

Verified at `public/index.html:1423-1424` (`inside = clamp01(-distance/span)`, then `z = base + extrusionHeight(...)*bevelFactor(...)`), `extrusionHeight` `:1144-1156`, `bevelFactor` `:1159-1171`.

All six bevels and six of the seven extrusion profiles read only `inside`. **`linear` (`:1148`) reads `nx, ny` and does not read `inside` at all.** So the correct statement is `H = H(inside, nx, ny)`, a closed form in *local position* — which still implies the solid, so the conclusion survives intact.

- Q1 states this correctly (§1.1) and **T-0109 states it correctly** ("plus, for the linear technique only, the normalised local coordinates").
- **`SHAPER_THE_DESIGN.md:162` states it incorrectly**: *"every extrusion profile and every bevel is a statement about inside-ness, and the height a layer takes is a closed-form function of a single number the shape already produces — how far in from the outline you are."* False for `linear`. → **ERROR-3**, low impact (the design does not depend on it), but this is the owner-facing document and it is the one place the exception was dropped.

### A2 — "the edge/rim band is local, not screen space" — Q1 is RIGHT, P3 was WRONG

Verified: `lx, ly` are produced by inverse-transforming the sample point at `:1419`; the band test at `:1428` is on `distance`; `edgeRingIndex(edge, lx, ly, halfW, halfH, edgeArc)` at `:1429` takes only local quantities. Nothing in the test indexes by screen position. **Q1's correction of P3 stands.**

One caveat Q1 does not state: `edgeBand = Math.max(cellSize*1.7, span*edgeDepth)` (`:1350`). The dominant term is local, but the **floor is a grid-resolution quantity** (`cellSize = source.width/gridW`), so the band's minimum width is derived from the render grid and mixed into a local-space comparison. Under a tilt it would no longer correspond to ~1.7 rendered cells. → **OVERSTATEMENT-3** on "not in screen space" being absolute; the substance is correct.

### A3 — "glow and outline passes are camera-independent post-effects" — TRUE

Verified: the emission pass (`:1450-1466`) reads the finished `cellMaterial` buffer, writes `glowR/G/B`, composited at `:1487` and `:1540`. The hidden-border pass (`:1548-1563`) reads `coverage`; the visible border pass (`:1568-1579`) reads `cellLayer`. None of the three consults heights, depths or geometry. **Confirmed camera-independent by construction.**

Two caveats:
- The glow radius is in **grid cells** (`:1457`, clamped 1..`MAX_GLOW_RADIUS=9`), so a tilted surface's glow would not foreshorten. A look consequence, not a reformulation — Q1's verdict holds.
- The hidden-border pass's *input* (`coverage`) is one of the seven consumers Q1 itself grades "equal, **but only if** the resolve is specified now to record all layers" (§1.2(e)). Q1 §6.2 then counts it among "three of the seven need no reformulation at all", and `SHAPER_THE_DESIGN.md:164` repeats that. The pass needs none; its input needs exactly the reformulation. → **OVERSTATEMENT-2**.

### A4 — the distance-field over-report factors — TWO CONFIRMED, ONE WRONG, ONE CONFIRMED ANALYTICALLY

Re-derived independently: `primitiveSdf` reimplemented verbatim from `:1031-1042`, boundary recovered by 60,000-direction radial bisection (valid: every one of these primitives is star-shaped about its own origin), ratio = `|reported| / |true Euclidean|`.

| Claim (Q1 §1.2(h); T-0105) | My measurement | Verdict |
| --- | --- | --- |
| diamond 1.414× | **1.4142×**, uniformly over the whole field for `rx=ry` | **Confirmed** |
| triangle 2.236× on a slanted side of an 80×20 | **2.2361×** (= √5; gradient of `\|x\| − 2(y+10)` from `:1035`) | **Confirmed** |
| star 1.308× at a lobe tip | **1.0000× at the lobe tip.** Worst over the field: **3.13× inside**, **2.39× outside** | **WRONG — ERROR-1** |
| non-uniform `scale.x = 0.2` → 5.00× | Confirmed analytically from `evalShape:1118` — the child's distance is returned with no rescale by the inverse transform, so a 5× inverse x-scale returns 5× the world distance | **Confirmed** |

**ERROR-1 detail.** At a lobe tip the star's radial field is `r − R·g(θ)` with `g′(θ) = 0`, so the reported distance *equals* the true one; the figure is not merely imprecise, it is measured at the one place on the star where there is nothing to measure. The real bound is ~2.4× and the safe bound (covering interior points, which a ray also traverses) is ~3.1×. This figure survives into **`T-0105.md`** as a directive number. Consequences: (a) a per-primitive bound of 1.308 for the star is unsafe by ~2.4×, i.e. holes; (b) Q1's "the Lipschitz correction roughly doubles the step count" (measured on the diamond ÷√2) understates the star's case by roughly another factor of two.

`subtractSoft` "is not a distance field at partial strength" (`:1134-1136`) — **confirmed by reading**; I probed its gradient numerically and did **not** reproduce a bound above 1.0, so I am *not* claiming Q1's "max of the children's bounds" rule is unsound for it. It is untested either way, and T-0105 correctly says it "needs an explicit bound rather than a derived one".

### A5 — the corrected canvas/grid figures — ALL CONFIRMED

Every figure now asserted in `SHAPER_THE_DESIGN.md:204` (B9) and `T-0115.md` checks out:

| Assertion | Source | Verdict |
| --- | --- | --- |
| schema bounds resolution 32–256, default 96 | `project_document.py:79` `("resolution": (32.0, 256.0))`, `:152` `bounded_integer(..., 96, 32, 256)` | ✔ |
| renderer clamps 16–256 | `public/index.html:1372` `clampTo(..., 16, 256)` | ✔ |
| grid height derived from canvas aspect, not square | `:1373` `gridW*(source.height/source.width)` | ✔ |
| cap 68,000 cells | `MAX_GRID_CELLS = 68000` at `:982`, enforced `:1374` | ✔ |
| default canvas 960×640 | `DEFAULT_CANVAS`, `project_document.py:11` | ✔ |
| default grid 96×64 = 6,144 | derived; arithmetic correct | ✔ |
| today's cost bounded by the shape's box (T-0115's contrast) | `gx0..gy1`, `:1365-1366` | ✔ |

The replacements are right. No finding here.

### A6 — a claim nobody measured: the height profile's own slope (→ OMISSION-1)

The extruded solid is `max(sdf2D, z − H)`. Its Lipschitz constant is not bounded by the primitive's — near the profiled region it is dominated by `|∇H|`, which is `(body/span) · dProfile/d(inside)`. Measured `max dProfile/d(inside)` straight from `extrusionHeight`:

| profile | max dH/d(inside) |
| --- | --- |
| `pyramid` (taper 1) | 1.00 |
| `taper` (taper 1) | 1.67 |
| `round` (curve 1, **the common case**) | 16.4 — formally unbounded as `inside → 0` |
| `taper` (taper 0.1) | 16.7 |
| `dome` (curve 1) | 23.1 — formally unbounded as `inside → 0` |
| `stepped` | **discontinuous — no finite bound exists** |

`bevelFactor`'s `stepped` (`:1168`, a `floor`) is discontinuous too, and `rounded`/`cove`/`ogee` all have infinite slope at one end. With a typical depth-to-span ratio of ~1:3 that is a 5–8× over-report for the *default* profiles and an unbounded one for `stepped`.

None of this is mentioned anywhere in Q1, B7, C3 or T-0109. Every bound requirement was filed against the primitive registry:

- `SHAPER_THE_DESIGN.md:174` — "Every **primitive** declares a bound… and every **transform and combination** composes those bounds."
- `SHAPER_THE_DESIGN.md:323` (C3) — same wording.
- `T-0105.md` — the whole bound clause.
- `T-0109.md` — extrusion and bevel; **carries no bound requirement at all.**

A profile is not a primitive, a transform, or a combine node, so it falls through all three. → **OMISSION-1**. This also means Q1's "perhaps twenty lines in the right place" is an underestimate: there is a second bound to design, in a second task, and `stepped` needs a different treatment entirely (slope-clamped profile, or slab-bounded marching) rather than a constant.

### A7 — the 7–11× cost figure is narrower than it is presented (→ OVERSTATEMENT-1)

Q1 §7 item 13 states the experiment's subject: "exact extruded implicit, **disc** radius 32 **half-thickness 6**" — i.e. an exact field, a circular silhouette, and a **flat** profile with no bevel. The band is then quoted as the general cost of turning in five places: `SHAPER_THE_DESIGN.md:28` (Part A), `:168` (B7), `:214` (B9), `:413` (D3), and `T-0102.md`. It does not cover Shaper's own field, a star silhouette (whose true bound is ~2.4× larger than the one Q1 used to size the correction), or any profiled extrusion (A6). The direction of the error is known — every one of those makes it worse — the magnitude is not. The honest statement is "7–11× measured on an exact flat-profile disc; unmeasured for profiled extrusions and for the shipped field".

### A8 — claims I checked that are clean

Verified accurate and worth recording so they are not re-litigated: no camera/projection/rotation anywhere in `index.html`; `base = order*0.75` with no `zOffset` (`:1362`); normals are a finite difference with the `0.65` and `1.4` constants (`:1491-1493`); `shadowAt` marches screen space capped at 24 (`:1469-1480`, `:982`); `FUSION_CELLS = 1.7` (`:982`) and the fusion branch (`:1436-1447`); the two-slot below-buffer and the code's own explanation of why a coverage bitmap was needed (`:1391-1392`, `:1403-1407`, `:1411`); `MAX_COMPONENT_DEPTH = 4` / `MAX_COMPONENT_PARTS = 512` (`:990`). **T-0108's Pyre claims all check out**: Box 8 verts / 6 quads, Pyramid apex + base, Can 16-sided cylinder (`Pyre.cs:48-50`); Orb's silhouette never changes under spin/tilt, the lighting frame rotates instead (`Pyre.cs:51-53`); "Convex ⇒ backface culling ONLY (no depth sort)" (`PyreRenderer.cs:4139-4140`).

---

## B. The zero-code-identifiers style rule

The document declares at `:8` "There is no code anywhere in this document." No literal code survives. But the rule as briefed also bans engineering jargon, and the changed sections carry it. Judged by "would a non-programmer know this phrase":

| Line | Quote | Term |
| --- | --- | --- |
| `:162` | "the height a layer takes is a **closed-form function** of a single number" | banned outright |
| `:172` | "implements that with the straight-down **closed form**" | banned outright |
| `:323` (C3) | "a node's height is a **closed-form function** of its own published edge distance, so every layer *is* an **implicit solid**" | banned outright, twice |
| `:190` (B8) | "real 3D geometry with genuine per-pixel **normals**" | banned |
| `:337` (C6) | "Any layer publishing height or **normals** receives them" | banned |
| `:214` (B9, **new text this round**) | "inside a responsive budget in **Burst-compiled code over flat arrays**" | product name + data-structure jargon |
| `:391` (D1, **changed this round**) | "The **buffer** contract (B9's **chunked-not-callback** ruling…" | banned |
| `:180` (B7) | "the wall carries its own top-to-bottom **parameterisation**" | jargon |

→ **QUIBBLE-1**. Note the split: `:162`, `:172`, `:180`, `:190`, `:214`, `:323`, `:337`, `:391` are all in sections written or rewritten this round. B9's other jargon (`:204` "a **hash** of its own settings", `:210` "flat numeric **arrays**", `:212` "Chunked **buffer** writes, not per-pixel **callbacks**… a per-pixel callback **interface**") is pre-existing text this round did not touch, and I flag it only for completeness. The single cheapest fix is "closed-form function" → "a fixed recipe worked out from"; it accounts for four of the eight.

Part A's new line `:28` is **clean** — no jargon at all. Worth saying, because it is the paragraph most likely to be the only one read.

---

## C. Internal consistency after the edits

**Clean.** I searched the whole 423-line document for surviving statements that rotation is impossible, permanently excluded, or priced as rewriting the best-tuned code. There are none. Every downstream section was caught: Part A `:28`, B8 `:198` ("by decision rather than by impossibility, and B7 now says which decision and why"), B9 `:214`, C3 `:323`, C6 `:337`, D1 `:391`/`:393`, D2 `:399`, D3 `:413`. The "what I did not decide" list and the "never checked" list were both updated. That part of the job was done properly.

**Three consistency defects found:**

**ERROR-2 — B7 contradicts itself about who pays for the primitive bound.**
- `:170` argues against writing the general path now because "the primitive engine stops being a dozen lines of arithmetic **the moment it has to carry guarantees**".
- `:174` then makes carrying that guarantee **requirement three of the chosen plan**: "Every primitive declares a bound on how far it may claim its edge is, and every transform and combination composes those bounds."
- `:184` charges it to the owner *again* as the price of asking for usable tilt: "the primitive engine stops being a dozen lines of arithmetic and takes on **the guarantees above**".

So the cost cited as a reason to defer is paid in v1 regardless, and is then re-quoted as an incremental cost of not deferring. Of the three costs `:184` lists, only the third ("the spine of the build gets meaningfully longer") is genuinely incremental; the second (no reference picture to judge against) applies equally to the kept tilted test picture the plan already commits to at `:176`. **T-0105 mirrors the same contradiction**: paragraph 1 still promises "its whole primitive registry is about a dozen lines of scalar maths" and paragraph 2 requires per-primitive bounds, composition rules, and an explicit hand-written bound for `subtractSoft`. The task never retracts its own promise. A build agent reading it finds two incompatible statements about scope and no instruction on which governs.

**OMISSION-3 — the supersession is generic, and the record it points at still holds the reversed claim.**
`PyrePlus.json` gained a fifth tab, "-> Shaper (report 4 lives in the Shaper group)", which says: *"It supersedes parts of the three tabs beside this one… A claim-by-claim digest of what the earlier three got wrong or withdrew is at …P1-digest.md - read that before acting on anything in the older tabs."*
- The pointer exists and is correctly placed. But it never names this reversal.
- The tab it supersedes still says, verbatim: *"They do real 3D rotation, **which Shaper structurally cannot**"* ("Where I Stand — the nine answers", question 4) and *"Shaper's '3D' is a height map rendered from **one fixed, unrotatable viewpoint**"*.
- **`P1-digest.md:402` — the file the pointer nominates as the claim-by-claim record — repeats "which Shaper structurally cannot" unchanged**, as a live open question. `P1-digest.md` is timestamped 17:44; Q1 is 19:21. It could not contain the reversal and does not.

So the one place the owner is told to look before trusting the old tabs is the one place that still asserts the opposite of B7. Two of the three earlier reports are otherwise silent on rotation (I checked; GUG mentions it only about Pyre's own solids). Fix is one sentence in the pointer tab, or one line in the digest.

**QUIBBLE-5** — `:399` says B7 carries "a correction of **three** things this document originally got wrong". `:164` enumerates two categories (the 200-lines pricing, and the casualty list) and names three of the four corrected verdicts; Q1 says four of seven verdicts differ. Nothing counts to three. Also `:164` says "four of those are wrong" and then names only three (the shadow-march verdict is the one dropped).

---

## D. The planning JSON round-trip

**Clean, byte for byte.** Verified with Python, explicit UTF-8:

- `Shaper.json` — **71,848 bytes**, parses, single top-level key `tabs`, exactly one tab: `id="Shaper_-_the_design"`, `name="Shaper - the design"`, **70,889 characters** of content.
- `SHAPER_THE_DESIGN.md` — **71,213 bytes**, decodes as UTF-8, **70,889 characters**.
- `tab.content == file text` → **True**. Not "equivalent" — identical.
- **Mojibake: none.** Zero occurrences of `â€”`, `â€œ`, `â€™`, `â€“`, `Ã©`, `Â` in either. The canary holds: **162 em-dashes in the tab, 162 in the file**. (Curly quotes are not a usable canary here — the document contains zero of them, in both copies; the em-dashes carry the test alone.)
- `PyrePlus.json` parses; tabs at 61 / 41,013 / 50,574 / 90,946 / 627 characters.

**QUIBBLE-2, and it affects whether the owner can read the deliverable.** Every tab in both files is written with **`"md": false`**. In AgentHQ that flag is the planning tab's "Render as Markdown (read-only)" toggle (`web/app.js:1846-1870`; normalised in `store.py:1203` with default `False`). So opening "Shaper - the design" shows **70,889 characters of raw Markdown source** — `# Shaper — the design`, `**bold**`, `| --- | --- |` table pipes — until the owner notices the Markdown button. The same applies to the three 41k–91k-character report tabs in `PyrePlus.json`, which this round explicitly *added* `"md": false` to. One click each fixes it; the default written this round is wrong for four Markdown documents. Separately, the `PyrePlus.json` diff silently renames the Notes tab's id from `notes` to `Notes` (`store.py:1197`/`:1205` use `"notes"` as the canonical default id) — no observed breakage, but it is an unrequested mutation in the same commit.

---

## E. The task edits

**Safe on every mechanical check.** For all five of T-0102, T-0105, T-0108, T-0109, T-0115: `id`, `title`, `node: Shaper`, `status: open`, `priority: normal`, `assignee`, `tags`, `confirmed` are unchanged; the section skeleton (`## Todos` / `## Questions` / `## Handover Log` / `## Agents`) is intact and empty; every addition is appended prose in `## Description`. Exactly five files carry an mtime of 19:29:04; **T-0110 and T-0114 are untouched** (18:07:55, matching their eleven siblings). **No new task file exists anywhere**: 111 task files total, highest is T-0115, `counters.json` still reads `next_task_seq: 116`, and T-0099…T-0115 is exactly the seventeen D1 claims.

**OMISSION-5** — `updated:` was **not** bumped on any of the five; it still equals `created` (`2026-08-30T16:07:55Z`) on substantially rewritten bodies. Anything on the board that sorts or filters by recency will not see these edits.

**Per task:**

- **T-0102** — consistent with what it already said; the three amendments attach cleanly to the three numbered items they name. Jargon is fine here (build-agent audience). **OMISSION-2, and it would confuse a build agent:** the amendment instructs "**Before Wave 2 closes**, render one deliberately tilted frame through the general path and keep it as a conformance artefact". T-0102 is a Wave-1 paper task whose own first sentence is "Three things to write down and agree **before a line is written**". It will be closed long before Wave 2 opens, and no Wave-2 task (T-0105–T-0110) carries the obligation. The conformance frame — the single mechanism B7 offers as proof the door stayed open, and the thing B7 `:178` already concedes is the weak point of the whole recommendation — currently has **no owner and no scheduled moment**. Q1 itself identified T-0109 as "the least-bad host for the feature"; nothing acted on that.

- **T-0105** — carries **ERROR-1** (the 1.308× star figure, presented alongside two correct figures in a sentence explicitly framed as "This is not theory: measured against true Euclidean distance"). Carries **ERROR-2**'s second half (the unretracted "about a dozen lines" promise sitting one paragraph above a requirement that ends it). To answer the brief's question directly: **the task does not acknowledge that its own promise has changed — it now contains two incompatible statements about its own size, and softens rather than withdraws the first** ("The bound is one extra number per compiled node and one divide; it is nearly free here"). Given OMISSION-1 that softening is itself optimistic.

- **T-0108** — the cleanest of the five. Every Pyre fact added is verified accurate (A8). Consistent with what the task already said; the added Solids/Orb constraints genuinely belong here.

- **T-0109** — the extrusion/bevel statement is **more accurate than the design document's** (it correctly excepts `linear`). Two small defects: it then writes "**Every one** of the seven profiles and five bevels is a function of that one number" two sentences after excepting one of them (**QUIBBLE-4**); and "**five** bevels" undercounts — `BEVEL_TECHNIQUES` has six entries (`project_document.py:29`) and both Q1 and B7 say six, though five are non-`none`. The substantive problem is **OMISSION-1**: this is the task that owns the profiles, the profiles are where the real metric hazard lives, and the task carries no bound requirement. It does correctly host the side-wall authoring question.

- **T-0115** — every corrected figure verified (A5). The added paragraph ("a per-pixel search rather than a per-pixel evaluation… separately keyed, separately budgeted cache node") is consistent with the task and with B9. No finding.

---

## F. The judgement call — possibility versus capability

**Does the written B7 make the distinction unmissable to a skimmer? Mostly yes.** A reader who reads only the bold lead-ins gets: *"3D rotation: yes, it is possible"* → *"The decision. The first version still resolves flat, and the general path is designed in from day one."* → *"And one word from you changes this."* The one-page summary at `:28` opens with the distinction in its first clause — *"Yes, extruded shapes can rotate in 3D — and the first version still ships flat, on purpose"* — and D2 `:399` restates it. Three independent places. That is honest work and the framing is not buried.

**Is the escape hatch prominent enough to count as offering the real choice? No — and not because of where it sits, but because of what it says.**

- It is the **eleventh and last paragraph** of the longest section in the document, after the strongest-counter-argument paragraph and the side-wall paragraph. Structurally that is survivable; the bold lead-in carries it.
- **The disqualifying problem is that its price list is wrong** (ERROR-2). It names three costs. The first — the primitive engine taking on the guarantees — is already in the plan at `:174`, in D1 at `:393`, and in T-0105. The second — no reference picture to judge a tilted result against — applies identically to the tilted conformance frame the plan already commits to at `:176`, and D3 `:413` says as much in its own words. Only the third is genuinely incremental. **An owner deciding on that paragraph is being quoted roughly three times the true marginal cost of the thing he asked for.** That is the single most consequential defect in this round, because it is the paragraph that exists specifically to let him overrule the recommendation.
- **The word has nowhere to go** (**OMISSION-4**). "Say so and that becomes the plan" appears in B7 and in D2, and in **no task's `## Questions` section** — T-0102's and T-0109's are both empty. On a board-driven workflow, a question that lives only in prose in a planning tab is a question with no inbox.

**On what is actually being guaranteed, stated plainly for the record:** "possibility" here rests on three design constraints plus one kept test image, and B7 `:178` already concedes — correctly, and to its credit — that a test nobody runs is a weaker guarantee than a path that executes every frame. Given OMISSION-2 (the test has no owner) and OMISSION-1 (the bound requirement is filed in the wrong task and is incomplete), the possibility as currently written is **weaker than B7 represents it**. That is not an argument for Option 3; it is an argument that the three door-open constraints need one more pass before they can be called requirements rather than intentions — which is exactly the standard B7 `:170` sets for itself.

---

## Verification index

| # | Claim checked | How |
| --- | --- | --- |
| 1 | Height is a closed form in `inside` (+ `nx,ny` for `linear` only) | Read `public/index.html:1423-1424`, `:1144-1156`, `:1159-1171` |
| 2 | Edge band and ring index are local, not screen | Read `:1419`, `:1428`, `:1429`; band floor at `:1350` |
| 3 | Glow and both border passes are camera-independent | Read `:1450-1466`, `:1487`, `:1540`, `:1548-1563`, `:1568-1579` |
| 4 | Diamond 1.4142×, triangle 2.2361× | Sandbox: `primitiveSdf` reimplemented verbatim from `:1031-1042`; boundary by 60,000-direction radial bisection; ratio over a swept query grid. Both also confirmed analytically (√2; gradient of `\|x\| − 2(y+10)` = √5) |
| 5 | Star is **not** 1.308× | Same method. 1.0000× at the lobe tip (`g′(θ)=0` there); worst 3.13× inside / 2.39× outside over a ±80-unit grid at step 2 |
| 6 | Non-uniform scale 5.00× | Analytic from `evalShape:1118` — child distance returned unrescaled by the inverse transform |
| 7 | Extrusion/bevel profile slopes | Sandbox: `extrusionHeight`/`bevelFactor` reimplemented from `:1144-1171`, `dProfile/d(inside)` swept at 0.001; `stepped` is a `floor`, hence discontinuous |
| 8 | Canvas/grid figures | `project_document.py:11`, `:79`, `:152`; `public/index.html:982`, `:1372-1375` |
| 9 | Pyre Solids/Orb facts in T-0108 | `Runtime/Pyre/Pyre.cs:48-53`; `Runtime/Pyre/PyreRenderer.cs:4135-4141`, `:4182-4185`, `:4786-4791` |
| 10 | JSON round-trip, byte counts, mojibake | Python, explicit UTF-8; `tab.content == file text` → True; em-dash counts 162/162 |
| 11 | `md` flag semantics | `AgentHQ/web/app.js:1846-1870` (Markdown toggle), `AgentHQ/store.py:1197`, `:1203`, `:1205` |
| 12 | Task frontmatter, no new task, T-0110/T-0114 untouched | `ls` mtimes (five files at 19:29:04, rest at 18:07:55), `.agenthq/counters.json`, 111 task files, max T-0115 |
| 13 | No surviving "rotation impossible" in the design doc | Case-insensitive sweep of all 423 lines for rotate/tilt/impossib/permanent/structurally/200 lines/64 square |
| 14 | The reversal is still asserted in the superseded material | `P1-digest.md:402`; `PyrePlus.json` tab "Where I Stand — the nine answers", question 4 and the "one fixed, unrotatable viewpoint" line; pointer tab text read in full |
