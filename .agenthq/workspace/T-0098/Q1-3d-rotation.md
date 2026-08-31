# Q1 — Can extruded shapes rotate in 3D?

Investigator Q1, task T-0098 ("Future of Pyre"), project Laubrary_Dev. Read-only: nothing in `D:\CODEZ\AgentHQ\3D Shaper`, nothing in the Unity project, and no task file was modified. No Unity editor or Coplay bridge was used. **No subagents were spawned**, so no agent-registration POST was required.

**Sources.** Read against source, not against the prior reports: `public/index.html` (regions `:982`, `:1005-1200`, `:1330-1590`), `project_document.py` (`:11-29`, `:57`, `:62`, `:79`, `:152`, `:235`, `:294-304`, `:421`), the seventeen task files `T-0099.md`–`T-0115.md`, and — the piece nobody has brought into this question yet — `Assets\Packages\Laubrary\Runtime\Pyre\Pyre.cs` (`:44-78`, `:111-117`, `:732-743`) and `Assets\Packages\Laubrary\Runtime\Pyre\PyreRenderer.cs` (`:4135-4230`, `:4786-4850`). Two numerical experiments were run in a sandbox to settle claims that could not be settled by reading; both are reproduced in the verification index.

**Answer to the owner's question, in one line: yes, it is possible, it is cheaper than the last two reports said, and the reason it is cheaper is that the code being compared against does not exist yet.**

---

## 0. Verdicts

| Question | Verdict |
| --- | --- |
| 1. Is Route A real? | **Confirmed, in full.** Every primitive, every fusion mode, every extrusion profile and every bevel survives unchanged into a 3-D implicit field — verified case by case. **But P3's list of seven casualties is wrong in four places**: two of the seven are not casualties at all, one gets strictly better, and one that P3 did not list at all is the real blocker (see §1.2(h)). |
| 2. Was the cost framed wrongly? | **Yes — the hypothesis is correct, and I can be more precise about it.** The rebuild is confirmed from-scratch C#: no Shaper C# exists, and T-0102 says "before a line is written". So B7's "rewriting 200 lines" is not a cost of rotation; those 200 lines are JavaScript that is being discarded under *every* option. The real marginal cost is not lines, it is **tuning fidelity and a metric-field prerequisite**, and I have measured both. |
| 3. Is there a cheaper third way? | **Two of the three offered are half-measures that would be reported as bugs** (depth peeling, relief mapping). **One is real and better than both routes** (reduce the 3-D march to a 2-D march inside the Z slab). And there is a **fourth** nobody has considered: Laubrary already contains a working, tuned, pixel-art software 3-D rasteriser with real rotation (`PyreRenderer.DrawFacetSolid`). P3 dismissed "tessellate" without knowing that. |
| 4. Build-plan changes | **Five task files need text; one would need the feature if Option 3 is taken.** T-0102, T-0105, T-0108, T-0109, T-0115 take sentences. T-0109 is the least-bad host for the feature itself, and it is a genuinely poor fit — I say so plainly in §4. |
| 5. Recommendation | **Option 2, hardened** — the height-field resolve ships in v1, but the resolve stage is written with a ray-shaped signature whose v1 body is the trivial closed form, and **one tilted frame is rendered as a conformance artefact in Wave 1**. Reasoning and the strongest objection to it in §5. |

### The plain-language version of the whole report

The owner wants to take a shape he has cut out flat, given a thickness, and be able to turn it in space and see it from an angle. Today the tool cannot do that, because of how it stores what it has drawn: for every dot on the screen it remembers exactly one number — how tall the shape is at that dot. That is enough to fake light and shadow beautifully from directly above, and it is why the tool looks as good as it does. It is not enough to turn the shape, because the moment you tilt something you start seeing its *side*, and a single dot on screen then has to remember both the top and the side. There is only one slot.

The way out is to stop remembering a picture of heights and start remembering the solid itself — a rule that can answer "is this point in space inside the shape or outside it?" — and then, for each dot on screen, follow a line into the scene until it hits something. That is the standard technique, and the important thing is that the tool *already contains* the rule; it just never asks it about anything except one fixed direction. Turning the shape is then a matter of pointing the line differently.

The previous two reports said this was expensive because it means throwing away two hundred lines of the best code in the app. That reasoning does not apply here, because the app is being rewritten in a different language from scratch. Those two hundred lines are going to be rewritten whatever is decided. What genuinely costs something is different and smaller: the shape rules answer "how far am I from the edge?" with a number that is sometimes an *over*-estimate, and following a line into a scene relies on that number being an under-estimate — otherwise the line strides straight through a thin shape and misses it, leaving holes. Fixing that is a small change to a dozen lines of arithmetic, but it must be made where the shapes themselves are written, not later.

The measured cost, once that fix is in: turning the shape costs roughly **seven to eleven times** as much work per frame as not turning it. Not the hundreds of times people assume. And at the normal straight-down view it costs *nothing at all*, because the calculation collapses back into exactly the one the tool does today. So the honest position is: this is affordable, it is worth keeping possible, and the sensible thing is to build the fixed view first while making sure the door genuinely stays open — with an actual test that walks through the door once, because a door nobody opens gets bricked up by accident.

---

## 1. Route A, re-verified against the code

### 1.1 The authored model really is an extruded implicit

P3's claim is that `F(x,y,z) = max(sdf2D(x,y), z − heightProfile(x,y))` is a well-defined 3-D solid that every authored feature survives into. **Confirmed, and it is stronger than P3 stated.** The reason is a property of the code P3 did not name:

> **The height profile is a function of the 2-D distance, not an independent function of position.**

`renderModelGrid` computes `inside = clamp01(-distance/span)` (`public/index.html:1423`) and then `z = base + extrusionHeight(type, params, body, inside, nx, ny) * bevelFactor(bevel.type, bevel.params, inside)` (`:1424`). `extrusionHeight` (`:1140-1152`) reads only `inside` — except `linear`, which additionally reads the normalised local coordinates `nx, ny` (`:1144`) and is a tilted plane cap. `bevelFactor` (`:1155-1167`) reads only `inside`. So the whole family collapses to `H = H(d, nx, ny)` where `d` is the one number the shape engine already produces.

That matters because it means **the 3-D solid is not a new representation that has to be built — it is one line of algebra over the representation that already exists**, and every knob keeps its meaning verbatim.

Feature-by-feature survival, verified:

| Authored feature | Where it lives | Survives into `F(x,y,z)`? |
| --- | --- | --- |
| 8 primitives (rect, ellipse, diamond, triangle, hexagon, octagon, capsule, star) | `primitiveSdf`, `:1031-1042`; `SHAPE_TYPES`, `project_document.py:16` | **Yes, untouched.** Pure 2-D scalar maths; `F` calls it unchanged. |
| Corner rounding | `:1041-1042` | **Yes.** A parameter of the rect case. |
| Pulge (3-band bulge/pinch) | `evalBasicShape`, `:1054-1059` | **Yes.** A pre-warp on the sample point plus a Lipschitz correction `*Math.min(1,warp)` (`:1058`). |
| Skew | `:1055` | **Yes.** A shear of the sample point. |
| 4 fusion modes (add / subtract / softAdd / subtractSoft) | `evalShape`, `:1113-1140` | **Yes** for add/subtract/softAdd — 2-D field algebra, dimension-agnostic. **Qualified yes** for `subtractSoft`: see §1.2(h), it is not a distance field. |
| Nested shape tree (depth 4, 512 parts) | `compileShape`, `:1073-1111`; caps at `:990` | **Yes.** Recursion over the tree is unchanged. |
| Per-component 2-D affine transform | `nodeMatrix`, `:1017-1028`; `TRANSFORM_TYPES`, `project_document.py:27` | **Yes**, and it becomes a 3-D matrix with the 2-D one as its top-left block — which is exactly how rotation gets added later, for free. |
| 7 extrusion techniques | `extrusionHeight`, `:1140-1152`; `project_document.py:28` | **Yes.** All closed-form in `inside` (`linear` also in `nx,ny`). |
| 6 bevel techniques | `bevelFactor`, `:1155-1167`; `project_document.py:29` | **Yes.** All closed-form in `inside`. |
| Layer Z (today `order * 0.75`) | `compileLayer`, `:1362` | **Yes**, and it stops being derived — it becomes the solid's actual position. This is the Z-offset T-0109 already asks for. |
| Edge/strip parameterisation | `edgeRingIndex(edge, lx, ly, …)`, `:1429` | **Yes** on the top face — it reads the layer's *local* coordinates (`lx, ly` from `:1419`), not screen coordinates. See §1.2(f); P3 got this one wrong. |

**There is no camera, no projection and no 3-D anything today** — a grep for `rotateX|rotateY|camera|projection` across the whole 4,102-line `public/index.html` returns **zero** hits. And no `zOffset` exists in either the renderer or the schema. Both of P3's negative claims confirmed.

### 1.2 The pieces that genuinely cannot survive — and one P3 missed

P3 named seven screen-space consumers. I take each in turn, say what its raymarched equivalent is, and rate it. **Four of the seven verdicts differ from P3's.**

**(a) Normals by finite difference** — `normalX=(sampleHeight(gx-1,gy,h)-sampleHeight(gx+1,gy,h))*0.65`, `normalY` likewise, `normalZ=1.4+reflection*1.5` (`:1491-1493`). Raymarched equivalent: the gradient of `F`, by central difference in 3-D or analytically. Technically **better** — it is a real surface normal, it is correct on side walls, and it does not garbage out at silhouettes.

**But it is not a drop-in, and this is the single biggest look risk in the whole change.** The current normal is *deliberately non-physical*: `normalZ` is a hard-coded 1.4 (a flattening constant, so surfaces never turn fully side-on and never go black), and the X/Y differences are scaled by 0.65 (a slope-softening constant). A true gradient normal will make every wall face sideways and read much harder and darker than today's relief. **Verdict: better in correctness, a look change that must be re-tuned.** The mitigation is cheap and belongs in the plan now: expose `0.65` and `1.4` as named dials ("slope strength", "flattening") in v1's height-field normal, so a later true-gradient normal has a target to be dialled against rather than a memory to be chased. This is why T-0108 needs a sentence (§4).

**(b) Shadow march** — `shadowAt`, `:1469-1480`. Steps in screen space, comparing stored heights against a rising ray (`other > h + step*rise + 0.35`), capped at `MAX_SHADOW_STEPS = 24` (`:982`). Raymarched equivalent: a secondary ray toward the light. **Better in correctness** — it handles occlusion between tilted bodies, self-shadowing across a fold, and light positions genuinely behind the scene, none of which the screen-space march can do. **And, counter-intuitively, it is not much worse on cost:** today's version costs up to 24 array reads per pixel per light; a sphere-traced shadow ray measures at ~7-11 field evaluations (§2.3). It trades cheap reads for fewer expensive evaluations. **Verdict: better, roughly cost-neutral.** P3 listed this as a straight casualty; that overstates it.

**(c) Fusion smooth-max** — `heights[key] = (previous>-900 && z-previous<FUSION_CELLS) ? smoothMax(z,previous,FUSION_CELLS*0.85) : z`, plus the losing-side branch (`:1436-1447`); `FUSION_CELLS = 1.7` (`:982`). Raymarched equivalent — there are two, and the choice matters:

- *Naive:* put every layer into one 3-D field and smooth-union them there. **More correct** (bodies weld in space, not merely in depth value) — and it reuses `smoothMinShaped` (`:1007`), which the shape tree already has. **But it destroys the per-layer bounding-box cull** (`gx0..gy1`, `:1365-1366`), so cost becomes canvas-area × layer-count instead of shape-area × 1. That is a hidden ~10× the owner would not anticipate.
- *Better, and my recommendation:* **march each layer's own solid separately, then composite the resulting depths with exactly today's smooth-max.** This keeps the bounding-box cull (a tilted layer's screen box is just its 3-D box's 8 corners projected), keeps the cost linear in layers, and keeps the fusion behaviour byte-comparable to today's.

**Verdict: equal, provided the per-layer march is chosen over the single-field march.** That decision must be recorded, because the naive version is the one a fresh implementer reaches for.

**(d) The below / second-depth slot** — `belowHeights`/`belowMaterial` (`:1391-1392`, written at `:1438` and `:1445`), consumed only by the glass surface for transmission (`:1516-1520`). Raymarched equivalent: continue the ray past the first hit. **Strictly better, and by a wide margin.** Today's slot is (i) limited to two layers and (ii) only populated inside the fusion band, which is why the code needed a *separate* coverage bitmap for occluded outlines — its own comment at `:1403-1407` says so in as many words. A continued ray gives real N-deep transmission for free and would make the coverage bitmap redundant. **Verdict: better.**

**(e) Occlusion-coverage bitmaps** — `coverage` (`:1411`), written at `:1422`, consumed by the hidden-border pass (`:1548-1563`). Raymarched equivalent: have the primary ray record *every* layer it passes through, not just the winner, and derive the same information from that. **Verdict: equal, and achievable — but only if the resolve stage is specified now to record all layers along the ray rather than only the front-most hit.** If it is specified as "return the nearest hit", this feature has to be retrofitted by re-marching once per outlined layer, which is ×N cost. **This is a concrete, cheap, day-one constraint that belongs in T-0102** (§4).

**(f) Edge band** — `if (edge && (entry.edgeStripes || edgeFull || -distance<=edgeBand))` (`:1428`), with `edgeBand = Math.max(cellSize*1.7, span*edgeDepth)` (`:1350`) and the ring index from `edgeRingIndex(edge, lx, ly, halfW, halfH, edgeArc)` (`:1429`).

**P3 is wrong about this one.** P3 wrote that "'how far in from the outline' in 2-D is no longer 'how far in from the silhouette' on screen; the pattern ring would slide off the rim it is supposed to trace." It would not. The test is evaluated on `distance`, `lx` and `ly`, all of which are the layer's **local** quantities obtained by inverse-transforming the sample point at `:1419`. Under a ray hit you get the hit point's local coordinates the same way, and the test runs verbatim. **The ring stays welded to the rim under any camera.**

What actually breaks is different, and it is an *authoring* question rather than a rendering one: **an extruded solid has side walls, and a side wall has no "how far in from the outline" value at all.** Today the walls are invisible, so the question has never arisen. Tilt the shape and every strip, every bevel band and every fill that reads inside-ness needs a defined answer on the wall. There are three sensible answers (the wall inherits the rim value it descends from; the wall gets its own vertical parameterisation; the wall is a separately paintable surface) and picking one is a real design decision. **Verdict: survives unchanged on the face; opens one new unanswered authoring question on the walls.**

**(g) Glow spill** — `:1450-1466`. **P3 is wrong about this one too.** It is a 2-D max-blend disc computed over the *finished* `cellMaterial` buffer and composited into `pixels` at `:1540`. It is a screen-space post-effect: it does not know or care what produced the buffer beneath it. It is camera-independent by construction. **Verdict: unchanged.** The same is true of the two border passes (`:1548-1579`), which work on the `cellLayer` ID buffer — also unaffected.

**(h) The one P3 did not list, and it is the real prerequisite: the field is not a metric field.**

Sphere tracing — the technique that makes raymarching cheap — requires the field to *under*-estimate the true distance to the surface. If it over-estimates, the ray strides past the surface and the shape develops holes. Shaper's field over-estimates in at least four independent ways. I measured them rather than asserting them (experiment 1, verification index):

| Source | Measured over-estimate | Citation |
| --- | --- | --- |
| `diamond` at a 45° corner | **1.414×** (exactly √2) | `:1033` |
| `triangle` on a slanted side of an 80×20 triangle | **2.236×** (exactly √5) | `:1034` |
| `star` at a lobe tip | **1.308×** | `:1038` |
| A component with a non-uniform `scale.x = 0.2` | **5.00×** | `evalShape`, `:1114` — the child's distance is used with **no rescale** by the transform |
| `subtractSoft` at partial strength | not a distance field at all — `distance + strength*bite` (`:1132`) is an authored blend, not a metric | `:1118-1133` |

**Why this has never mattered:** the current renderer only ever reads the field's **sign** (`if (distance>0) continue;`, `:1421`) and uses its magnitude as a *shaping parameter* (`inside`, `:1423`), never as a distance to travel. Any monotone distortion is harmless. **It is a sign-plus-shaping field, not a metric field** — and the moment a ray uses it to decide how far to step, that stops being true.

**This is fixable, cheaply, and only at the right moment.** The fix is one extra number per compiled node — a Lipschitz constant — and one divide in `evalShape`: each primitive declares its own bound (√2 for diamond, computed from the aspect for triangle, and so on), each transform multiplies in the largest singular-value ratio of its matrix, and each combine mode takes the max of its children's. That is perhaps twenty lines. **But it has to be written into the primitive registry when the primitive registry is written**, i.e. in T-0105, not bolted on later — which is exactly the day-one-versus-day-five-hundred cost B7 was reaching for and located in the wrong place.

**If it is not fixed**, the fallback is fixed-step marching, and the cost difference is not marginal: sphere tracing measures at **~7-11 evaluations per pixel**; a safe fixed-step march over the same span at half-cell resolution is **~400 per pixel**. That is the difference between a 10× feature and a 400× feature. **This single decision is worth more than everything else in this report.**

---

## 2. The cost question

### 2.1 Is the rebuild really a from-scratch C# port? — Confirmed

- **No Shaper C# exists.** A search of `Assets\` for anything named "shaper" returns only `Assets/Plugins/Shapes/…`, an unrelated third-party vector-drawing plug-in.
- **T-0102** is titled "lock the buffer contract **on paper before any code**" and its body opens "Three things to write down and agree **before a line is written**".
- **T-0100** creates "a second working copy of this repository (a git worktree…) on a new branch for Shaper" — the code has no home yet.
- **T-0105** says "**Port** 3D Shaper's silhouette mechanism (its whole primitive registry is about a dozen lines of scalar maths)" — a port of the *mechanism*, into new code.
- **T-0109** says "Extrusion and bevel are the **genuinely new construction** in this design — there is exactly one worked instance in Pyre today, welded into the Text generator as eight slabs".

**So the hypothesis in the brief is correct, and B7's cost framing is structurally wrong.** The 214 lines of `renderModelGrid` (`:1371-1583`) are JavaScript in a browser app that is not being kept. They are being rewritten in C# under Option 1, Option 2 and Option 3 alike. "Retrofit penalty" is the wrong category; there is no artefact to retrofit.

**Where B7's instinct was nonetheless pointing at something real.** Those 214 lines carry *tuning*, and tuning survives a port only when the target is the same algorithm. Porting a height-field resolve preserves `FUSION_CELLS = 1.7`, the `*0.85` smooth-max width, `normalZ = 1.4`, the `0.65` slope scale, the `+0.35` shadow bias, the `+0.45` proud-edge bump (`:1433`) — dozens of small numbers that took real iteration. Writing a raymarcher discards all of them and you re-derive the look from scratch, **with no reference to compare against**, because 3D Shaper cannot produce a tilted frame for you to match. **So the honest restatement of B7's point is: the cost of Option 3 is not lines, it is that you lose your only calibration target.** Smaller than B7 implied, real nonetheless, and on the critical path of a tool that has never rendered a frame.

### 2.2 What resolutions does Shaper actually work at?

The plan's stated numbers are wrong, in both directions, and T-0115 repeats them. The measured truth:

- **Grid width** is `clampTo(Math.round(source.resolution||96), 16, 256)` in the renderer (`:1372`), but the *document schema* bounds it to **32–256, default 96** (`project_document.py:79`, `:152`). So 16 is unreachable through a saved document.
- **Grid height** is derived from the canvas aspect: `gridW * (height/width)` (`:1373`). At the default 960×640 canvas (`project_document.py:11`) that gives **96 × 64 = 6,144 cells** — so "64" is the default *height*, not the default square size.
- **Ceiling:** `MAX_GRID_CELLS = 68000` (`:982`), enforced at `:1374`. The true worst case is ~68,000 cells, and 256×256 (65,536) is reachable.

**Working figure for every budget in this report: 6,144 cells typical, 68,000 cells worst case.** T-0115's "16 to 256 square with 64 the default" should be corrected to that (§4).

### 2.3 The measured marginal cost

Experiment 2 (verification index) sphere-traces an extruded disc (radius 32 cells, half-thickness 6) across a 96×64 grid at several tilts. The height-field baseline is **1 field evaluation per cell**, i.e. 6,144 per layer for a full-canvas layer.

| Configuration | Evals per pixel (avg) | Worst pixel | Total per layer per frame | vs baseline |
| --- | --- | --- | --- | --- |
| Height field (today) | 1.0 | 1 | 6,144 | 1× |
| Sphere trace, exact field, tilt 0° | 6.2 | 206 | 38,205 | 6.2× |
| Sphere trace, exact field, tilt 15° | 7.0 | 89 | 43,066 | 7.0× |
| Sphere trace, exact field, tilt 45° | 8.6 | 46 | 52,941 | 8.6× |
| Sphere trace, exact field, tilt 75° | 10.9 | 38 | 67,023 | 10.9× |
| Sphere trace, Shaper's diamond, **Lipschitz-corrected**, tilt 45° | 10.9 | 30 | 67,240 | 10.9× |
| **Fixed-step march** (the fallback if the field is left non-metric) | 400 | 400 | 2,457,600 | **400×** |

Three things fall out of that table, and all three are new to this workspace:

1. **The cost of turning is ~7-11×, not the 100× everyone assumes.** At pixel-art resolutions a march is short: there is very little empty space to cross and the shapes are fat relative to the canvas.
2. **The Lipschitz correction roughly doubles the step count** (measured: 5.6 → 10.5 average on the diamond at tilt 0). That is the *price* of the fix in §1.2(h); it is cheap. The price of *not* fixing it is the 400× row.
3. **The zero-tilt row is misleading and the real number is 1×.** The 6.2 figure is what a *general* marcher costs when pointed straight down. But a marcher pointed straight down at an extruded solid is algebraically the same query as the point sample: the ray hits iff `sdf2D(x,y) <= 0`, and the hit depth is `base + H(d,nx,ny)` in closed form. **A one-line special case recovers the exact current cost and the exact current output.** This is the fact that decides the whole question, and it is what makes Option 2 and Option 3 nearly the same code in v1.

**Does Burst change the picture? Yes, decisively — and it is already in the plan.** At 6,144 pixels × ~10 evals × (1 primary + 1 shadow ray) × 12 layers ≈ 1.5M composite field evaluations per frame. In Burst-compiled C# over flat arrays that is roughly 15-30 ms — inside a responsive-preview budget. In ordinary managed C# with per-pixel virtual calls it is roughly 75-150 ms — outside it. **So a rotating Silhouette is a Burst-required feature, while a v1 height-field Silhouette is not.** T-0102's requirement (1) — "Generators fill flat numeric arrays for a block of the canvas; they are never asked per pixel through a managed callback" — is therefore not merely enabling Burst *later*, it is the precondition for rotation ever being affordable. Worth saying so in T-0102, because it upgrades that clause from an optimisation hedge to a load-bearing requirement.

**What budget does T-0115 imply?** T-0115 gives no millisecond figure; it gives a structural one — "editing a dial dirties that node and everything downstream and only that", and "a dozen layers each holding a bag of four sub-shapes is around sixty passes a frame". Design B9 supplies the only quantitative anchor in the whole plan: Kiln's Tapestry surface at "roughly thirty to a hundred and twenty milliseconds per image" with Shape spiking "past a second and a half", both called out as *too slow* and the reason caching is "load-bearing rather than nice to have". **Read together, the implied ceiling for an interactive dial-drag is tens of milliseconds, and a 15-30 ms Burst-compiled tilted resolve sits just inside it — with no headroom.** The correct consequence is not "too slow" but "the tilted resolve is its own cache node with its own budget", which is a sentence T-0115 should carry.

### 2.4 The three options

**Option 1 — height-field renderer only, rotation permanently out of scope.**

*What you get:* the smallest, most certain v1. Every tuned constant ports one-for-one from working JavaScript. The primitive registry stays "about a dozen lines of scalar maths" as T-0105 promises. Nothing in the plan changes.

*Cost relative to the others:* zero now. Unbounded later — this is the option where "add rotation" means designing the buffer contract a second time, after there are authored assets. B7's analysis of what that costs is right in kind even though its arithmetic was aimed at the wrong artefact.

*Risk:* the owner has asked for this feature by name, twice, and this option answers "no, permanently". P3's closing advice applies verbatim: **if this is chosen, say so out loud, in the tool and in the design, rather than leaving a "later" that is not a later.** The second risk is subtler: Silhouette without rotation is a *relief* generator, and the design already concedes (B8) that it will never match Solids under the same lights. Two generators in one document that disagree about what "3-D" means is a permanent explanation the tool has to keep making.

**Option 2 — height field in v1, three door-open constraints honoured, raymarch added later.**

*What you get:* Option 1's v1 verbatim, plus a layering discipline. The three constraints (shape publishes an implicit field sampleable at a depth; nothing but the resolve reads screen-space neighbours; the depth buffer's meaning stays "nearest surface depth") are already written into **T-0102** and cost nothing to keep.

*Cost relative to Option 1:* close to zero in code. Non-zero in discipline: it forbids the shortest implementation of at least three things — the normal must be published *by the shape* as "surface direction" rather than differenced out of the buffer by the shader; the coverage bitmap must be derived from a layer list rather than a neighbour scan; the fusion must read published depths rather than poke at `heights[]`. Each is slightly more code than the shortcut, and each is better layering anyway.

*Risk, and it is the real one:* **an interface nothing exercises rots.** This codebase supplies its own evidence. T-0107: the border stage "exists in today's Pyre already… but it is gated to six of 29 generators and not even constructed in the UI for the other 23". T-0114: "the picture-rect call that tells an effect where the artwork sits inside a padded buffer is actually made — it is never called anywhere in Pyre today". Both are doors that were left open and quietly closed. A rule with no test is a promise, and this project has a measured track record of those decaying.

**Option 3 — raymarched resolve from day one, the fixed top-down view being one camera.**

*What you get:* the feature, immediately, and several things that come with it for free — genuinely correct shadows between tilted bodies, N-deep transmission instead of a two-slot approximation (§1.2(d)), true surface normals on walls, and Z translation as a natural consequence rather than an added slider.

*Cost relative to Option 2:* the general march path, the metric-field work in the primitive registry (§1.2(h)), and the re-tuning (§2.1). Call it a fortnight of the Wave 2 spine rather than a day.

*Risks the owner would not think of, stated plainly:*

- **The field is not metric and the fix lands in the wrong task.** §1.2(h). Getting this wrong is not a slow renderer, it is a renderer that draws holes in diamonds and triangles. And it lands on T-0105, whose whole selling point is that the primitive registry is "about a dozen lines". It stops being a dozen lines.
- **There is no reference to check against.** 3D Shaper cannot render a tilted frame, so the visual baseline T-0101 exists to establish (of *Pyre's* generators) does not cover this at all. Option 3's output is unfalsifiable by comparison; it can only be judged by eye, on a tool that does not exist yet.
- **The pixel-exact silhouette is conditional.** This is the whole point of a pixel-art tool and the answer has two halves. At the fixed top-down view: **fully preserved and provably so**, because the ray test collapses to `sdf2D(x,y) <= 0` — the same point sample and the same hard edge as today (`:1421`). At any tilt: **not preserved** — the silhouette becomes a function of march quality, and a thin feature or a shallow bevel lip can drop out of one frame and reappear in the next, which on a 96×64 canvas is a visibly flickering pixel, not a subtle artefact. Fixed-step marching removes the flicker at the 400× cost; sphere tracing with a correct Lipschitz bound removes most of it; neither makes it a theorem the way the flat case is one.
- **Cost is no longer bounded by shape area.** Today a small layer costs its own bounding box (`:1365-1366`). A tilted layer's box grows with the tilt, and if the naive single-field fusion is chosen (§1.2(c)) the bound disappears entirely. A user who tilts a document from 0° to 45° will see the preview cost jump by more than the 10× table suggests unless per-layer marching is chosen deliberately.
- **The side-wall authoring question has no answer yet.** §1.2(f). Every fill, every border, every bevel and the entire indexed-strip feature of T-0110 are defined in terms of "how far in from the outline", and a wall has no such number. Shipping rotation before answering that means shipping a feature where tilting the shape makes its decoration behave in an undefined way — which is precisely the shape of a bug report.
- **It front-loads risk onto a tool with no first frame.** Wave 2 is the spine; T-0105 through T-0110 are what make Shaper exist at all. Putting the most novel piece of engineering in the plan onto that path is the standard way a rebuild stalls before it renders anything.

---

## 3. Is there a cheaper third way for a pure extrusion?

### 3.1 (a) Analytic ray-prism intersection — **rules IN, and it is the best of the three**

An extruded silhouette is a generalised prism, and a ray meets it in a way that decomposes cleanly:

1. **Clip the ray to the Z slab analytically.** Two plane intersections, exact, no search. This is not an approximation — the top cap and the bottom cap are hit exactly, which is where a naive 3-D march wastes most of its steps.
2. **Reduce the remainder to a 2-D problem.** The clipped segment projects to a 2-D ray in the layer's local plane. Finding where it enters the silhouette is a 2-D query against `evalShape`, not a 3-D one.
3. **Test the profile analytically at each step.** Because `H = H(d, nx, ny)` (§1.1), once you know `d` at a point you know the solid's top there in closed form — so "am I under the surface?" is a comparison, not another field evaluation.

**Why this is genuinely cheaper, not just differently arranged:** it removes the dimensions that cost the most. A shallow tilt makes the 3-D path through the solid long and thin — exactly the geometry sphere tracing handles worst — while the *2-D* path through the silhouette is short regardless of tilt. And every one of Shaper's profile and bevel curves survives exactly, including the sharp ones (`stepped`, `pyramid`) that a 3-D march tends to round off.

**Honest limit, so this is not oversold:** it does not eliminate the search, it lowers its dimension. The 2-D entry point still has to be found by tracing, and it inherits the same non-metric-field problem from §1.2(h). What it buys is exact caps, exact walls, a shorter search, and a formulation in which the flat top-down case is *visibly* the degenerate one — which is the property Option 2 wants anyway. **This should be the specified shape of the resolve stage under any option, because its degenerate case is the height-field resolve.**

### 3.2 (b) Depth peeling / two-sided height fields — **rules OUT: a half-measure that would be reported as a bug**

Storing front and back depth does cover the simplest case (a convex prism tilted so a screen column sees the top face and one wall). It fails on everything the tool is actually for:

- **Non-convex silhouettes give more than two surfaces.** Shaper's `subtract` mode makes real holes (`:1116`), and the shape tree is four levels deep with 512 parts (`:990`). A ring tilted 45° needs four depths; a fused blob needs however many its outline has lobes. Two slots is one slot's problem again, moved.
- **It does not compose with the layer stack.** Each layer would need its own pair, which is an unsorted A-buffer — at which point you have written a depth-peeling rasteriser, which is strictly more work than the marcher, for strictly less capability.
- **The fatal one: it does not tell you how to *produce* the two depths.** The height field is cheap because it needs no search — evaluate at the pixel and you are done. A tilt destroys that property, so a search is needed to fill either slot. The saving this option promises is the exact saving the tilt has already taken away.

The failure mode is easy to predict and it is the worst kind: it works on the demo (a tilted rounded rectangle) and produces holes on the user's actual artwork the first time they subtract something. **Rule out as a path; it survives only as an internal optimisation *inside* a marcher, which is a different thing.**

### 3.3 (c) Relief / parallax mapping — **rules OUT: a half-measure that would be reported as a bug**

Shading a flat quad while offsetting the lookup along the view ray is the standard cheap trick, and it is genuinely cheap here because the march is through an *array*, not a field. It fails on the one thing the owner asked for:

- **The silhouette does not change.** Relief mapping fakes interior depth; the outline stays exactly the flat shape's outline. A tilted disc still reads as a flat disc with a lumpy middle. "Rotate my extruded shape" means seeing its *outline* change and its *side* appear, and this delivers neither.
- **No side walls, at all.** The wall is not in the height map, so it cannot be looked up.
- **The artefacts are per-pixel visible at 96×64.** Relief mapping's stepping errors are normally hidden by high resolution and texture filtering. At pixel-art scale, with hard-edged palette colours, each error is a wrong pixel.

**Rule out for this purpose.** It is, however, a genuinely good technique for a *different* item already in the plan: giving a fill's height channel a sense of depth under the shared light rig (T-0108). Filed there it is useful; offered as rotation it is a bug report.

### 3.4 (d) The fourth way, which nobody has considered: Laubrary already owns a 3-D rasteriser

P3 dismissed "Route B — voxelise or tessellate" in two sentences. That dismissal was made without a fact that changes it: **`PyreRenderer.DrawFacetSolid` (`Runtime/Pyre/PyreRenderer.cs:4152`) is a complete, working, tuned software 3-D rasteriser in C#, at pixel-art resolutions, with real 3-D rotation, already in this repository.** Verified:

- Full 3-D rotation — roll about Z, then yaw about Y, then tilt about X (`PyreRenderer.cs:4193-4207`); the layer fields `swarmTurn`/`swarmTilt`/`shapePitch`/`shapeYaw` exist (`Pyre.cs:732-743`).
- Real geometry: Box is 8 verts / 6 quads, Pyramid apex+base, Can a 16-sided cylinder (`Pyre.cs:48-50`; `BuildBoxGeometry`, `PyreRenderer.cs:4830-4849`).
- Per-pixel point-light Blinn-Phong, hard edge lines, halo and inner glow, all shared across forms (`PyreRenderer.cs:4136-4141`).
- **And the load-bearing limitation, stated by the code itself:** *"Convex ⇒ backface culling ONLY (no depth sort)"* (`PyreRenderer.cs:4140`).

So a fourth route exists: **contour-trace the 2-D silhouette, extrude the contour into a wall strip plus two caps, and hand it to the rasteriser that already works.** Shaper even has the contour tracer — `traceEdgeRadii` (`:1191+`) already marches rays outward and bisects the boundary crossing, with documented failure modes and fallbacks (`:1174-1183`).

**Why I do not recommend it, having taken it seriously:**

- **Convexity.** The rasteriser's correctness rests on it (`:4140`). An arbitrary fused silhouette — the entire point of the feature — is routinely non-convex, so this needs a depth sort or a Z-buffer added to code the design elsewhere insists on not disturbing.
- **The tracer's limits become the shape's limits.** `traceEdgeRadii` returns null and falls back whenever a ray crosses the boundary more than once, or the origin is outside the shape (`:1174-1183`) — i.e. exactly on multi-lobed and subtracted shapes. A fallback that makes a *pattern* slightly uneven is fine; one that makes the *geometry* wrong is not.
- **It discards the analytic bevels.** The 5 bevel curves and 7 profiles (`:1140-1167`) become tessellated approximations. Those curves are Shaper's distinguishing quality; a marched or analytically-intersected solid keeps them exactly.

**Rule out as the recommendation — but record it**, because it is a real fallback, it is much closer to done than P3's Route B implied, and it is the only option that would let the owner *see* a rotating extruded shape in days rather than weeks if he wanted a throwaway proof.

---

## 4. What must change in the build plan

For **Option 2, hardened** (my recommendation, §5). Exact sentences, and where each goes. All additions go in the `## Description` body of the named file; none of the seventeen tasks is created, split or renamed.

### T-0102 — "lock the buffer contract on paper before any code" — *3 additions, this is the main host*

This file already carries the constraint as item (2). It needs to become testable, and it needs the two facts that make it work.

**Add to item (2)**, immediately after "…this is what keeps 3D rotation possible without building it (design B7)":

> Make this testable rather than aspirational: specify the resolve stage as "given a ray, return every layer the ray passes through and the depth and local coordinates of each surface it crosses", and ship its v1 implementation as the trivial closed form for a straight-down orthographic ray, which is algebraically identical to today's point sample and costs exactly the same. Before Wave 2 closes, render one deliberately tilted frame through the general path and keep it as a conformance artefact - not as a feature, as proof the door is still open. A rule with no test has already decayed twice in this codebase: T-0107's border stage, gated to 6 of 29 generators, and T-0114's picture-rect call, which is never called anywhere.

**Add as a new sentence to item (1)**, after "…this is what makes Burst possible later rather than a rewrite (design B9)":

> Note this clause is stronger than an optimisation hedge: a tilted resolve costs roughly seven to eleven times a flat one, which is inside a responsive budget in Burst-compiled code over flat arrays and outside it in ordinary managed code with per-pixel calls. If item (2)'s door is ever to be walked through, item (1) is its precondition.

**Add to item (3)**, after the list of named per-pixel quantities:

> "Surface direction" is the quantity that makes item (2) real, so it is not optional: the shading stage must receive a normal published by the shape, never derive one by differencing the depth buffer itself. Today's tool derives it by finite difference with two hand-tuned constants (a 0.65 slope scale and a 1.4 flattening term); publish both as named dials of the height-field implementation so a later true-gradient normal has a calibration target rather than a memory of how it used to look.

### T-0105 — "Silhouette generator: primitives, sweep, shell, N-gon, parameterised star" — *1 addition, and it is the important one*

**Add at the end of the description:**

> Each primitive must also declare a Lipschitz bound on the distance it returns, and every transform and combine node must compose those bounds. This is not theory: measured against true Euclidean distance, Shaper's diamond over-reports by 1.414x at a corner, its triangle by 2.236x on a slanted side, its star by 1.308x at a lobe tip, and any component carrying a non-uniform scale over-reports by the scale ratio (5.00x measured at scale.x = 0.2, because evalShape uses a child's distance with no rescale by the transform). None of that matters today, because the current renderer reads only the sign and uses the magnitude as a shaping parameter - it is a sign-plus-shaping field, not a metric field. It matters enormously the moment anything steps along a ray by the returned distance, which is the difference between a rotation feature costing 10x and one costing 400x. The bound is one extra number per compiled node and one divide; it is nearly free here and expensive anywhere else.

### T-0108 — "light rig: one light list, per-layer response, Solids ported onto it" — *1 addition*

**Add after "The height-channel-to-relief-lighting machinery already exists in the current tool…":**

> The shared shading law takes a surface normal as an input, never a height buffer it differences itself - that is what lets Silhouette's normal be swapped later without touching the law or Solids. Note while porting Solids that its family is a facet rasteriser with genuine 3D rotation and per-pixel Blinn-Phong (Box 8 verts / 6 quads, Pyramid apex-and-base, Can a 16-sided cylinder), whose correctness rests on the solids being convex - backface culling only, no depth sort - and that Orb is the exception that does not rotate geometrically at all, its silhouette staying a fixed circle while the lighting frame rotates instead. Both facts are real constraints on any future attempt to reuse this family for arbitrary fused silhouettes, and should be written down here rather than rediscovered.

### T-0109 — "extrusion, bevel and Z offset" — *2 additions; also the least-bad host if Option 3 is ever taken*

**Add after "…a handful of lines and needs no new buffer, since the height field already is the depth buffer (design B7)":**

> State the layer's solid explicitly while defining these, because it costs nothing here and cannot be recovered cheaply later: the extrusion and bevel stages together publish a height as a closed-form function of the shape's own published inside-distance (plus, for the linear technique only, the normalised local coordinates), so the layer is exactly the implicit solid "inside the 2D silhouette, and below that height". Every one of the seven profiles and five bevels is a function of that one number, which is why the whole authoring model survives into a 3D field unchanged and why no primitive has to be re-expressed.

**Add as a second paragraph:**

> Answer one authoring question here even though nothing in v1 renders it: an extruded solid has side walls, and a wall has no "how far in from the outline" value, yet inside-distance is what the bevel band, the border region and the indexed-strip fill are all defined against. Pick one rule now - the wall inherits the rim value it descends from, or the wall carries its own vertical parameterisation, or the wall is a separately paintable surface - and record it. This is the only part of 3D rotation that is a design decision rather than an implementation, so it is the only part that genuinely has to be settled before the fills and borders are built on top of it.

**On hosting the feature itself.** If the owner chooses Option 3, the rotation work has no good home in the seventeen. **T-0109 is the least-bad host**, because it is the task that defines the solid and already owns the Z axis. It is a genuinely poor fit and I will not pretend otherwise: T-0109 is currently scoped as extrusion, bevel and "a handful of lines" of Z offset, and adding a raymarched resolve to it would make it three to five times the size of any other Wave 2 task, put the plan's most novel engineering on the spine's critical path, and hide a fortnight of work behind a title that reads as a small feature. The second-least-bad host is **T-0102**, which at least owns the resolve contract, but T-0102 is explicitly a paper task and putting a renderer in it destroys that property. **My honest position: if Option 3 is chosen, the seventeen-task shape is the wrong shape, and the owner should be told that rather than the work being smuggled into T-0109's title.**

### T-0115 — "caching and preview responsiveness" — *2 additions, one of them a factual correction*

**Replace "Canvas sizes are 16 to 256 square with 64 the default, so the budget is generous provided work is not repeated" with:**

> Canvas sizes measured from the source rather than assumed: the document schema bounds resolution to 32-256 with 96 the default, the renderer clamps 16-256, grid height is derived from the canvas aspect rather than being square, and the whole grid is capped at 68,000 cells. The default 960x640 canvas at resolution 96 therefore gives a 96x64 grid - 6,144 cells - and the true worst case is about 68,000. Budget against 6,144 typical and 68,000 worst, not against "64 square".

**Add at the end:**

> If a tilted or otherwise non-fixed camera is ever added, its resolve is a per-pixel search rather than a per-pixel evaluation, so its cost is not bounded by the shape's area the way today's is. Make it a separately keyed, separately budgeted cache node from the start, so a document that tilts one layer does not silently lose the responsiveness guarantee for the eleven layers that did not.

### Tasks that need nothing, and why it is worth saying so

- **T-0110** (indexed-strip fill): **no change needed, and that is a positive finding.** Its parameterisation reads the layer's local coordinates, not screen coordinates, so the strip stays welded to the rim under any camera. Worth not re-litigating later.
- **T-0114** (universal effects): **no change needed.** The glow spill and the border passes are screen-space post-effects over a finished buffer and are camera-independent by construction. P3 listed glow spill as a casualty of rotation; it is not one.
- **T-0099, T-0100, T-0101, T-0103, T-0104, T-0106, T-0107, T-0111, T-0112, T-0113**: unaffected.

---

## 5. One recommendation

**Take Option 2, hardened — and the hardening is what makes it a recommendation rather than a deferral.** The decisive fact is the one in §2.3: at the fixed top-down view a raymarched resolve and a height-field resolve are *the same query* — the ray hits exactly where `sdf2D(x,y) <= 0` and the depth is the profile in closed form — so the two options do not differ at all in v1's output or v1's cost. They differ only in whether the general path is written now. Given that, writing the general path now buys nothing the owner can see, while costing the two things Wave 2 can least afford: the primitive registry stops being "about a dozen lines" the moment it must carry metric guarantees, and the re-tuning has no reference to be judged against because 3D Shaper cannot produce a tilted frame to compare with. Meanwhile the *actual* day-one costs of keeping the door open are small, specific and independently worth paying — publish the normal instead of differencing the buffer (better layering), record every layer a ray passes rather than only the winner (which makes the occlusion-outline bitmap redundant rather than merely portable), and give every primitive a Lipschitz bound (twenty lines, and the only one of the three that is expensive later). And the measured 7-11× cost means the deferred feature is genuinely affordable when it comes: this is a door worth holding open, not a polite way of saying no.

**The strongest argument against my own choice, stated fairly:** doors held open in this codebase do not stay open. The plan itself documents two that closed — T-0107's border stage, which exists but is "gated to six of 29 generators and not even constructed in the UI for the other 23", and T-0114's picture-rect call, which "is never called anywhere in Pyre today". Both were once someone's day-one constraint. An interface that nothing exercises is indistinguishable from an interface that was never written, and Option 3's real merit is not the feature — it is that it forces the general path to be *executed*, every frame, which is the only thing that reliably keeps it correct. My answer is the conformance frame in T-0102 (§4): render one tilted image once, keep it, and re-render it whenever the resolve changes. That is a genuine mitigation and it is far cheaper than Option 3. **But it is a weaker guarantee than running the code for real, and I would rather say that than pretend it closes the gap entirely.** If the owner's instinct is that he will want to tilt shapes within the first year, Option 3 is defensible and I would not argue hard against it — provided he accepts that T-0105 grows, the seventeen-task shape stops fitting, and Wave 2 gets longer before Shaper renders its first frame.

---

## 6. Where I correct the earlier reports

1. **P3 §2.3, "the edge band test… the pattern ring would slide off the rim it is supposed to trace."** Wrong. The test at `public/index.html:1428-1429` reads `distance`, `lx` and `ly`, all of which are the layer's *local* quantities produced by inverse-transforming the sample point at `:1419`. Under any camera, a ray hit yields those same local coordinates and the test runs verbatim. The ring stays on the rim. What actually breaks is the side wall, which has no inside-distance at all — a different and more interesting problem (§1.2(f)).

2. **P3 §2.3 and §2.4, glow spill listed among the seven consumers that "all index by (screenX, screenY)" and would "need a different formulation".** Wrong. The emission pass (`:1450-1466`) runs over the finished `cellMaterial` buffer and composites into `pixels` at `:1540`; it is a screen-space post-effect and is camera-independent by construction. The same holds for the two border passes (`:1548-1579`), which work on the `cellLayer` ID buffer. Three of the seven need no reformulation at all.

3. **P3 §2.3, the below-slot listed as a compounding problem ("assume one surface per layer per pixel").** Understated in the wrong direction. Under a march this gets *strictly better*: the ray simply continues, giving N-deep transmission instead of the current two-slot approximation — which the code's own comment at `:1403-1407` describes as insufficient (it is precisely why a separate coverage bitmap had to be allocated).

4. **P3 §2.3, Route A's cost given as "you rewrite `renderModelGrid` (214 lines, `:1371-1583`)", and B7's "the renderer is around two hundred lines of very well-tuned code… Retrofitting means rewriting all of it."** The framing is wrong, and the brief's hypothesis is correct. Those 214 lines are JavaScript in an app that is not being kept; the rebuild is confirmed from-scratch C# (T-0102 "before a line is written"; T-0105 "Port… the mechanism"; no Shaper C# exists in `Assets\`). They are rewritten under every option, so they are not a cost *of rotation*. **The real marginal cost is (i) making the field metric — twenty lines in the right place, four-hundred-fold in the wrong one — and (ii) losing the calibration target, since 3D Shaper cannot render a tilted frame to compare against.** B7's conclusion partly survives, for a better reason than it gave.

5. **P3 §2.3 and §2.4 give no cost figure; B7 implies rotation is expensive per frame.** Measured: **~7-11 field evaluations per pixel at tilts from 15° to 75°, against a height-field baseline of 1** — and **exactly 1** at the fixed top-down view, because the query degenerates. Not the order-of-magnitude-worse cost the framing implies. The 400× figure applies only if the field is left non-metric.

6. **P3 §2.3, "Route B — voxelise or tessellate… gains nothing Route A doesn't, at pixel-art resolutions. I do not recommend it."** The recommendation stands but the reasoning was incomplete: it was made without noting that **Laubrary already contains a working, tuned, pixel-art software facet rasteriser with real 3-D rotation and per-pixel lighting** (`PyreRenderer.DrawFacetSolid`, `Runtime/Pyre/PyreRenderer.cs:4152`; geometry builders at `:4786-4849`), which makes Route B materially closer to done than "build actual geometry" suggests. It is still not what I recommend — the rasteriser's correctness rests on convexity (`:4140`) and arbitrary fused silhouettes are not convex, and tessellation discards the analytic bevel curves — but the dismissal deserved the fact.

7. **B7 and T-0115: "the current tool allows sixteen to two hundred and fifty-six pixels square, and defaults to sixty-four."** Wrong on all three counts. The document schema bounds resolution to **32-256, default 96** (`project_document.py:79`, `:152`); the renderer clamps 16-256 (`:1372`) but 16 is unreachable through a saved document; the grid is **not square** — height is derived from the canvas aspect (`:1373`) — and the whole grid is capped at **68,000 cells** (`:982`). The default 960×640 canvas at resolution 96 gives **96×64**, so "64" is the default *height*. Every performance budget in the plan should be restated against 6,144 typical / 68,000 worst.

8. **B7, "Given that Solids… does real 3D rotation and is staying, you already have a way to get a rotating 3D primitive."** True but worth qualifying, because one member of that family does not rotate in the sense implied: Orb's "silhouette is a plain circle that **NEVER** changes under spin/tilt — the LIGHTING FRAME rotates instead" (`Pyre.cs:52-53`). Box, Pyramid, Can and Gem do rotate genuinely (`PyreRenderer.cs:4193-4207`). Ring is a tilted annulus, analytic (`Pyre.cs:55-57`). So "Solids rotates" is true of four of six forms and is a shading trick in one — a useful precedent to know about, since the same trick will inevitably be proposed for Silhouette.

---

## 7. Verification index

| # | Claim | How verified |
| --- | --- | --- |
| 1 | The height profile is a function of the 2-D distance (plus `nx,ny` for one technique), not of position independently | Read `public/index.html:1423-1424`, `extrusionHeight` `:1140-1152`, `bevelFactor` `:1155-1167` |
| 2 | Every primitive, fusion mode, profile and bevel survives into a 3-D implicit unchanged | Read `primitiveSdf` `:1031-1042`, `evalBasicShape` `:1054-1059`, `evalShape` `:1113-1140`, `compileShape` `:1073-1111`; schema `project_document.py:16`, `:28`, `:29` |
| 3 | No camera, projection or 3-D rotation exists anywhere in Shaper | `grep -c "rotateX\|rotateY\|camera\|projection" public/index.html` → **0** over 4,102 lines; `TRANSFORM_TYPES` `project_document.py:27` lists only translate/rotate/scale/skew/origin |
| 4 | No `zOffset` exists; Z position is `order * 0.75` | `grep -n zOffset` over both files → empty; `compileLayer` `:1362` |
| 5 | Normals are a finite difference with two hand-tuned constants | `:1491-1493` |
| 6 | Shadows march the height map in screen space, capped at 24 steps | `shadowAt` `:1469-1480`; `MAX_SHADOW_STEPS = 24` at `:982` |
| 7 | Fusion is a depth-proximity smooth-max at 1.7 cells | `:1436-1447`; `FUSION_CELLS = 1.7` at `:982` |
| 8 | The below-slot is two-deep and only written inside the fusion band; a separate coverage bitmap was needed because of that | `:1391-1392`, `:1438`, `:1445`; the code's own explanation at `:1403-1407`; allocation `:1411` |
| 9 | The edge band and ring index are evaluated in **local**, not screen, coordinates | `:1419` (local coords), `:1428` (band test on `distance`), `:1429` (`edgeRingIndex(edge, lx, ly, …)`) |
| 10 | Glow spill and both border passes are screen-space post-effects over a finished buffer | `:1450-1466`, `:1540`; border passes `:1548-1563` and `:1568-1579` |
| 11 | **Experiment 1** — Shaper's distances over-report true Euclidean distance: diamond 1.414×, triangle 2.236×, star 1.308×; non-uniform `scale.x=0.2` 5.00× | Sandbox: re-implemented `primitiveSdf` verbatim from `:1031-1042`, measured the true distance by 4,096-direction bisection to the zero contour, and separately compared a child rect under a 0.2× x-scale against its true world SDF (`evalShape` `:1114` uses the child's distance unrescaled) |
| 12 | The current renderer only reads the field's sign and uses magnitude as a shaping parameter | `:1421` (`if (distance>0) continue;`), `:1423` (`inside = clamp01(-distance/span)`) |
| 13 | **Experiment 2** — sphere tracing an extruded prism costs ~6.2 / 7.0 / 8.6 / 10.9 evals per pixel at tilt 0/15/45/75°, vs 1 for the height field; the Lipschitz correction roughly doubles the step count (5.6 → 10.5); fixed-step is ~400 | Sandbox: 96×64 orthographic grid, tilted camera basis from an X-rotation, exact extruded implicit, disc radius 32 half-thickness 6; run for an exact field, Shaper's diamond uncorrected, and Shaper's diamond divided by √2 |
| 14 | Real grid sizes: schema 32-256 default 96, renderer clamps 16-256, height derived from aspect, cap 68,000 cells, default 96×64 | `project_document.py:11`, `:79`, `:152`; `public/index.html:1372-1375`; `MAX_GRID_CELLS = 68000` at `:982` |
| 15 | The rebuild is from-scratch C#: no Shaper C# exists | Search of `Assets\` for `*shaper*` → only `Assets/Plugins/Shapes/…` (unrelated third-party vector plug-in); `T-0102` "before a line is written"; `T-0100` creates the worktree; `T-0105` "Port… the mechanism"; `T-0109` "genuinely new construction" |
| 16 | T-0115 makes preview responsiveness a hard requirement with no ms figure; B9 supplies the only quantitative anchor | `T-0115.md` description; `SHAPER_THE_DESIGN.md` B9 (Tapestry surface 30-120 ms, Shape past 1.5 s, "the cache is load-bearing rather than nice to have") |
| 17 | Laubrary already contains a software 3-D facet rasteriser with real rotation, whose correctness rests on convexity | `Runtime/Pyre/PyreRenderer.cs:4135-4141` (the convexity comment at `:4140`), `:4152` (signature), `:4193-4207` (roll/yaw/tilt), `:4786-4849` (geometry builders); `Runtime/Pyre/Pyre.cs:48-50`, `:73` |
| 18 | Orb does not rotate geometrically — its lighting frame rotates instead | `Runtime/Pyre/Pyre.cs:51-53` |
| 19 | Doors left open in this codebase have decayed twice | `T-0107.md` ("gated to six of 29 generators and not even constructed in the UI for the other 23"); `T-0114.md` ("it is never called anywhere in Pyre today") |
| 20 | `subtractSoft` is not a distance field at partial strength | `:1118-1133`, specifically `distance = distance + part.strength * bite` at `:1132` |
