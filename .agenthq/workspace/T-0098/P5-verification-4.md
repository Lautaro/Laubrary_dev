# P5 — Verification pass 4: fact-check of `SHAPER_THE_DESIGN.md`

Verifier P5, task T-0098. Read-only. Nothing was edited except this file.

**Method.** Every load-bearing claim was checked against (a) the owner's verbatim text in `questions[0..2]` fetched live from `/api/task`, (b) `P1-digest.md` sections C (withdrawn) / D (open) / E (bugs) / F (citable index), (c) `P2-tapestry.md`, `P3-shaper-internals.md`, and (d) the actual sources: `D:\CODEZ\AgentHQ\server.py`, `Assets\Packages\Laubrary\Runtime\Pyre\Pyre.cs`, `Editor\Pyre\PyreWindow.cs`, `Assets\Packages\Laubrary\Zui\Scripts\Runtime\ZuiFill.cs`, `Runtime\Tapestry\TapestryLayer.cs`, `.agenthq\tree.json`, `.agenthq\planning\PyrePlus.json`. Evidence files H–N were not used as primary sources (line-number drift warning).

**Headline.** The draft is broadly sound and does not resurrect the big withdrawn claims (it correctly states zero generators are fused, correctly puts the warp pivot at the top-right corner, correctly folds vortex-field into swirl rather than culling it, and does not repeat the "Shaper is lit from one fixed direction" error). But it contains **one false statement of completed work** (D1/B14: the Shaper group does not exist), **one misattributed quotation of the owner** (B11), **one factual reversal of P2's single most important measurement** (B5, plasma), and **eleven other errors**. Two claims the draft states as verified were only read from source, and it says so in one place while contradicting itself in another.

---

# ERRORS

## E-1 — §D1 and §B14: "what I have set up" does not exist. **Most serious finding.**

**The false statement.** D1: *"A new group on the Laubrary board called **Shaper**, holding this document as its planning document and a first wave of tasks. Nothing in it is set to run on its own — the group is not supervised and every task is sitting in the open state."* B14: *"A **group of tasks under one node on the existing board**, which is what I have set up."* D1 also: *"If you disagree with the shape of it, deleting the group costs nothing."*

**The truth.** There is no `Shaper` node. `.agenthq\tree.json` contains 22 nodes; none is named or IDed Shaper (`BackSplash, Cartographer, Chunks, ChunksOverhaul-2026-08-18, Combat2D, Launimator, Mirage, PyrePlus, Rulesets, SpriteFx, Story, Zoetrope, Zounds, ZUI, IncidentRecovery-2026-08-16` + 5 children, `PyrePlus.KilnPorts`, `ZoePalette-2026-08-26`). `.agenthq\planning\` holds exactly three files — `Cartographer.json`, `Chunks.json`, `PyrePlus.json` — so no Shaper planning document exists either. A scan of every `.agenthq\tasks\*.md` for `node: Shaper` returns zero tasks.

**Corrected sentence.** Either create the group before publishing, or rewrite D1 as a proposal: *"What I propose to set up: a new group on the Laubrary board called Shaper, to hold this document as its planning document and a first wave of tasks, unsupervised and with every task open so nothing dispatches until you ask."* The dependent clause in B14 (*"and — now, because of B13 — its own automatic delivery of this design document"*) and D1's *"deleting the group costs nothing"* must change with it.

---

## E-2 — §B11: the draft quotes the owner saying something he never said

**The false statement.** *"The existing Pyre star … maps one-for-one onto your **"points, inner and outer radius, twist"**."*

**The truth.** The owner never wrote any of those words. Searching the concatenation of `questions[0].text`, `questions[1].text` and `questions[2].text`: `twist` → 0 hits, `inner` → 0, `outer` → 0, `radius` → 0, `points` → 0. His entire statement on the subject (`questions[2]`) is: *"Star/polygon: we should give shaper a proper polygon engine of sorts and also a star engine. But if we dont and everything else is built well im sure we can easily add it since the architecture will be modular."* The phrase was invented in `P3-shaper-internals.md §4.3` (which also attributes it to "the owner") and the draft imported it inside quotation marks.

**Corrected sentence.** *"The existing Pyre star, by contrast, is a real specification — arm count, arm length, base width, skew, all animatable, with a documented clamp that stops the valleys crossing the tips (`Pyre.cs:485-492`). That is the 'proper star engine' you asked for, already specified."* Drop the quotation marks and the invented parameter list.

---

## E-3 — §B5 and Part A: `plasma` is not a fill, and the draft says all three are

**The false statement.** Part A: *"**Tapestry Surface** makes seamless colour material — it is a fill."* B5: *"Three evolved generators … each producing a still, seamless, full-colour image from a height field it is handed"* and *"Tapestry Surface's generators become **fill generators** (they emit albedo)."*

**The truth.** P2 §2.4 is that report's own "single most important measurement", and it splits the three: rendering each against a completely flat (all-zero) field, `steel` and `steel_clean` came back **opaque everywhere with a mean pixel difference of 4–8/255** versus the bevelled panel — i.e. 97–98% shape-independent, genuine fills. `plasma` came back with **alpha 0 everywhere — it renders literally nothing** without a shape, and differs 140–165/255 between the two cases. P2's verdict: *"`plasma` is not a fill — it is a shape-coupled glow whose alpha *is* the shape's silhouette … closer to a Pyre effect than to a fill."*

**Corrected sentence.** *"**Two of Tapestry Surface's three generators are already fills.** `steel` and `steel_clean` were rendered against a blank field and came back complete and opaque — measured 97–98% shape-independent — so they drop in as fill generators unchanged. `plasma` is not a fill: hand it no shape and it produces nothing, because its alpha **is** the silhouette. It belongs with the effects, as an emissive edge/body treatment, not in the fill list."* Part A's one-liner needs the same qualification.

---

## E-4 — §B4: the existing Laubrary fill abstraction does take some of those numbers

**The false statement.** *"there is an existing fill abstraction used across the whole Laubrary library, and **it takes none of the numbers in the table above**."*

**The truth.** Verified in source: `Assets\Packages\Laubrary\Zui\Scripts\Runtime\ZuiFill.cs:167` — `public Color Evaluate(float life, float u, float v)`. It takes a life clock **and** a spatial sample point, which are two of the seven rows in B4's own table ("Where am I inside this shape" and "How old is this pixel"). Digest §C4 E16 records exactly this as a correction already made once: *"Misidentifies what is zero … what every Pyre site passes as zero is `u, v`."* The real problem is not that the signature lacks the inputs; it is that **Pyre's call sites feed them zero**, that the plain over-life mode passes the same `life` for both arguments (`ZuiFill.cs:178`), and that widening the signature is *"an engine change, not a call-site change"* because other Laubrary tools consume the type.

**Corrected sentence.** *"There is an existing fill abstraction used across the whole Laubrary library. It takes a life clock and a spatial sample point — but Pyre's own call sites pass zero for the spatial point, so spatial and noise fills collapse to one sample, and it takes none of the other five numbers in the table above. Widening it is a signature change to a shared ZUI type other Laubrary tools consume, so the blast radius is outside this tool. The new fill contract is therefore a new thing in the new namespace; the old one keeps working for everyone else and is not touched."* (The conclusion survives intact — only the reason was wrong.)

---

## E-5 — §B7: "200 lines **plus** seven other places" double-counts, and the parenthetical lists five

**The false statement.** *"the renderer is around two hundred lines of very well-tuned code **plus seven other places** that read the screen-space buffers directly (the fusion, the layer-below slot, shadows, occlusion outlines, the edge band)."*

**The truth, two ways.** (a) P3 §2.4 says the seven pieces *are* the ~200 lines, not an addition to them: *"Normals, shadows, fusion, the below-slot, the occlusion-coverage bitmaps, the glow spill, and the edge-band gate are seven independent pieces of logic that all index by `(screenX, screenY)`. In 3D Shaper these total roughly 200 lines and they are the good 200 lines."* The draft turns "seven pieces totalling 200 lines" into "200 lines plus seven pieces". (b) The parenthetical enumerates only **five** of the seven — it drops **normals** and **glow spill**, and normals are the most important one, being the finite difference at `public/index.html:1491-1492` that the whole shading trick rests on.

**Corrected sentence.** *"But that renderer is around two hundred lines of very well-tuned code, and those two hundred lines are seven independent pieces that all index the buffer by screen pixel: the normals, the shadow march, the fusion smooth-max, the layer-below slot, the occlusion-coverage bitmaps, the glow spill and the edge band. Retrofitting means rewriting all seven, and they are the best-tuned code in the app."*

---

## E-6 — §B9: the "retry paths spike into whole seconds" belong to Tapestry **Shape**, not Surface

**The false statement.** *"The measured Kiln timings support this being necessary rather than precautionary — **one Tapestry surface** takes tens of milliseconds at preview size and **its retry paths** spike into whole seconds."*

**The truth.** P2 §4.4 measures two different projects. **Tapestry Surface** has no retry paths at all; at 128×128 (numpy) it is `plasma` 32 ms, `steel` 116 ms, `steel_clean` 61 ms, scaling smoothly with resolution. The spikes belong to **Tapestry Shape**: *"`plates` 33–124 ms typical, `lines` 64–102 ms typical — but with occasional spikes to 588 ms and 1773 ms … the discard-and-retry loops … Worst case is roughly 20–50× the median."* Secondly, "tens of milliseconds" is not a measured Surface figure — the measured numbers are the numpy ones above; "low tens of milliseconds single-threaded" is P2's **estimate for a future C# port**, explicitly labelled as an estimate.

**Corrected sentence.** *"The measured Kiln timings support this being necessary rather than precautionary. A Tapestry **Surface** costs 32–116 ms per frame in numpy at preview size, which a C# port should bring to the low tens of milliseconds — fine for one preview repaint, not fine for twenty-four frames. A Tapestry **Shape** is worse in a way caching specifically fixes: its discard-and-retry loops give it an unpredictable worst case, measured at 588 ms and 1773 ms against a 33–124 ms median, 20–50× the typical cost. Nothing with a worst case like that can sit on a UI repaint path."*

---

## E-7 — §B13/§B14: the inlining is the task's **own** node only, never an ancestor

**The false statement.** B13: *"the task-tracker now looks up the planning document belonging to the dispatched task's group … so an agent starting work on **anything under a group** automatically arrives knowing that group's design context."*

**The truth.** Verified in `D:\CODEZ\AgentHQ\server.py`. `node_id` comes from the task's own frontmatter (`:676`, `detail["frontmatter"]["node"]`) and `planning_section` looks up exactly that one node, with an explicit docstring saying so: *"Deliberately the task's OWN node only, never its ancestors … walking the chain would multiply the exact size problem PLANNING_BUDGET exists to contain"* (`:373-378`). A task filed under a **child** node of the Shaper group therefore gets that child's document, or nothing — never the group's.

**Corrected sentence.** *"…so an agent starting work on a task filed **directly under** a node automatically arrives knowing that node's design context. The lookup is deliberately the task's own node only and never walks up to a parent, so if the Shaper group grows sub-nodes, either the tasks stay directly on the group node or each sub-node needs its own document."*

## E-8 — §B13 is otherwise correct, including the restart claim — **verified independently**

Not an error; recorded because the brief asked for it to be checked rather than taken on P4's word. `planning_section` exists at `server.py:369-419`, is wired into the brief template at `:214` (`{planning_doc}`) and populated at `:723`. `PLANNING_BUDGET = 12000`, `PLANNING_MIN_TAB = 800`. Under budget the whole doc is inlined; over budget each filled tab gets `max(800, budget // len(filled))` characters with an explicit `[... TRUNCATED: N of M characters shown. Read tab "X" in <path> for the rest ...]` marker plus a document-level `TRUNCATED FOR THIS BRIEF` line. The `Tabs:` listing iterates `tabs`, not `filled`, so every tab name and size is named even when its content is cut — the draft's "truncation is never silent" is accurate. All four bail-outs the draft claims are present and return `""`: no `node_id`, node deleted from the tree, `path.exists()` false, and a bare `except Exception` around the load; plus a fifth the draft does not mention (an all-empty doc). **The restart claim is true:** `server.py` was last modified 2026-08-30 17:33:09, and the process listening on port 8778 is PID 68052, started 2026-08-29 13:19:22 — over a day earlier. The change is on disk and is not live.

---

## E-9 — §D3: "nothing has been verified by looking at the tool running" is now false

**The false statement.** *"Three rounds of investigation, four documents, and **nothing has been verified by looking at the tool running**. No frame rendered, no output eyeballed. **Every visual judgement in all four documents is inferred from reading code.**"*

**The truth.** True of Pyre and 3D Shaper. False of round four. P2 states in its own preamble that it took its measurements *"by importing the generators … and calling their render entry points in memory"*, and it reports rendered output: alpha and mean-pixel-difference comparisons against a blank field (§2.4), a numerically verified wrap-seam test (ratios 0.80 / 1.14), byte-identical determinism on a re-render, unique-colour counts (936 for `steel`, 85 for `steel_clean`), wall-clock timings at three resolutions, a 248-file scan of the published height data (min −1.648, max +2.206, 245 at 256×256), and an inspection of the tiled preview images.

**Corrected sentence.** *"Four documents, and **nothing in Pyre or 3D Shaper** has been verified by looking at the tool running … Every visual judgement about Pyre and Shaper in all four documents is inferred from reading code. The one exception is round four's Kiln work, where the Tapestry generators were actually executed and their output measured — which is why the Tapestry findings are the most solid in the set."*

---

## E-10 — §B1: a composite generator loses more than "exactly one thing"

**The false statement.** *"Its contract is one line — it must publish coverage, and it may publish nothing else. With that it still groups, still masks, still swarms, still takes post-processing, and **loses exactly one thing: its fill picker offers one entry instead of a choice.**"*

**The truth, from the draft's own Part C.** C2 defines a composite layer as *"a composite generator, then optionally effects; **it may not be re-filled**"* — no border stage, where a shape layer gets one. B3 says a border attaches to *"a shape node — any shape node"*, and a composite generator is not a shape node, so a composite also loses borders. And C8 says an effect that needs to know where it is inside a shape *"needs the shape to publish that"* — a generator publishing coverage and nothing else fails that test, so "still takes post-processing" is true only for the buffer-runnable subset. Per the digest's index, that is 20 of 41 effects buffer-only, 20 needing per-sample context, 1 irreconstructible.

**Corrected sentence.** *"…With that it still groups, still masks, still swarms, and takes any effect that runs off the finished buffer. What it gives up is three things: its fill picker offers one entry instead of a choice, it has no border stage (a border traces a shape node's coverage and a composite is not a shape node), and the effects that need shape-local position or edge distance are greyed out on it for the same reason a fill would be."*

---

## E-11 — §D2: the open-question count is wrong

**The false statement.** *"**Six older open questions** from the first two documents are still open and are listed in the digest."*

**The truth.** `P1-digest.md` §D lists **six from R1, five from R2 and seven from R3** — eighteen in total, and **eleven** from "the first two documents". The draft then characterises the still-unanswered residue as *"how much resolution memory you want to spend and whether the standalone effect stack should continue to exist"* — those are R2 Q4 / R3 Q5 and **R3 Q6**, so at least one of the two named is from the third document, not the first two.

**Corrected sentence.** *"**Eighteen older open questions** from the first three documents are listed in the digest — six from the first, five from the second, seven from the third. Most are now answered implicitly by this design; the ones that are not are about how much resolution memory you want to spend and whether the standalone effect stack should continue to exist as a separate tool at all."*

---

## E-12 — §B8: "unused or under-used" resurrects evidence ruled inadmissible

**The false statement.** *"the machinery for turning a height channel into relief lighting with a light angle already exists in the current tool, **unused or under-used**."*

**The truth.** This is a usage count, and the owner's ruling 2 (`questions[1]`, point 2: *"The amount of assets using a perticular shape, generator or spriteFX says nothing about the tech it uses"*) makes it inadmissible. The digest lists the exact instance under §C5 ruling 2's void list — *"8 vs 0 (the two height techniques)"* — and §C1 records it as **STRUCK**: *"They share one lighting routine; it is a merge of equals."* Independently, P2 §5 finds the machinery is not merely present but working: `Pyre.cs` exposes `matteWriteLuma`, `heightFromChannel` (0..3), `heightRelief` and `heightLightAngle`, and `PyreRenderer.cs` threads a `float[] heightField` into layer rendering, so *"the consumer half … is already built in Pyre, for a different producer."*

**Corrected sentence.** *"Some existing capability transfers straight across: Pyre already has a working height-channel → relief-lighting path — layers deposit luminance × alpha into one of four numbered channels, those fuse into one scalar heightmap, and a consumer layer renders it relief-lit through its own fill with a controllable light angle. That is exactly what a shared rig needs underneath it, and it is also the consumer Tapestry Shape's height fields want."*

---

## E-13 — §B5: "zero porting work" for the 248 height fields

**The false statement.** *"248 finished height fields already exist in that project, published and unused, single-channel, signed, tileable, **256 by 256**. Shipping them as presets is a genuine feature with **zero porting work**."*

**The truth.** Two corrections. (a) P2 §3.3: *"**245 are 256×256**; 3 early ones are 128×128."* (b) P2 §5 identifies real glue: *"Because the range is not 0..1 and varies per field, a consumer needs either a per-field normalise or an explicit height-scale dial. **This is the one real piece of glue.**"* The measured global range is −1.648 to +2.206. On top of that the data ships as float32 `.npy`, which is not a Unity-importable asset type.

**Corrected sentence.** *"…248 finished height fields already exist in that project, published and unused, single-channel, signed, tileable — 245 at 256×256 and three early ones at 128×128. Shipping them as presets is a genuine feature and the algorithms need no porting at all; the only work is glue, because the data is float32 `.npy` in unnormalised units (measured range −1.648 to +2.206), so it needs a converter and either a per-field normalise or an explicit height-scale dial."*

---

## E-14 — §B9: "sixty-four to two hundred and fifty-six pixels square" is the wrong lower bound

**The false statement.** *"Canvases here are small — sixty-four to two hundred and fifty-six pixels square."*

**The truth.** `Pyre.cs:1185` declares `[Min(1)] public int canvasSize = 64` — 64 is the **default**, not the floor. The UI slider is `Z.MicroSlider("Size", s.canvasSize, 16f, 256f, …)` clamped `Mathf.Clamp(…, 16, 256)` (`Editor\Pyre\PyreWindow.cs:714-717`), so the authored range is **16 to 256**.

**Corrected sentence.** *"Canvases here are small — sixteen to two hundred and fifty-six pixels square, with sixty-four the default."*

---

# OMISSIONS

## O-1 — The additive-blend requirement is missing from the fill contract

`questions[1]` ruling 5b is the ruling the draft leans on hardest in B3. The digest records **two** things that survive it (§C5): the padded working buffer, and — *"additive light is not expressible by coverage alone (two overlapping half-covered haloes composite to 75% and add to 100%), so **a fill must declare whether it blends over or adds**."* The digest further notes this is a debt R3 already owes: *"'the emission channel buys nothing' overstates (it bought the additive case, which R3 has to re-buy with a blend-mode flag)."* §C4's fill contract is *"A fill emits **albedo** and optionally a **height delta** and a **veil**"* — no blend mode anywhere in the document. Ironically the draft notes in B5 that the existing Laubrary Tapestry *"has blend modes that Pyre lacks, which are worth taking"* (confirmed: `TapestryLayer.cs:13`, `enum TapestryBlendMode { Normal, Add, Multiply, Screen }`; no equivalent in `Pyre.cs`) and then does not put one in its own contract.

**Add to C4:** a fill declares whether it composites **over** or **adds**, because glow and emissive material are not expressible by coverage alone.

## O-2 — "Shaper is the only way to make primitives" now has an unstated exception

`questions[1]` point 6: *"I think the shaper generator that handles primitives should be the only way to generat primitives. I dont want to keep the current way of creating a disc when the shaper generator will do the same."* B8 keeps Solids as a separate shape generator with its own primitives, which is the named exception to that ruling — and the digest flags it as an open question in exactly those words (§D, R3 Q4: *"which means 'Shaper is the only way to make primitives' has a named exception from day one"*). The draft never says this out loud, and it never confirms that Pyre's existing flat built-ins (Disc, Ring, Crescent, Streak, Star, Polygon) are subsumed into Silhouette rather than carried alongside it. Given the owner made that ruling explicitly, the design should state that it is being honoured for the 2-D family and consciously excepted for Solids.

## O-3 — The `PyreForm` contract check that the digest calls a pre-condition was never done

The digest names two locks the design document must state *"before a line is copied"*. The draft has the second (C9's not-carried list). The first is missing: *"whether the existing `PyreForm` plug-in contract is unchanged — **verify by checking that two of the big effects satisfy the new contract unmodified**, because it decides whether the ported effects can be *shared* rather than copied."* C9 then asserts the answer without the check — *"they should be **referenced** rather than copied, so that ten thousand lines of carefully verified ports do not get duplicated and then drift"* — and Wave 1 in D1 does not schedule the verification. This also sits in unacknowledged tension with the owner's own instruction in `questions[1]` point 4 (*"use the current Pyre could as a mold but **duplicate (and adjust) rather than modify**"*), which the draft never reconciles.

## O-4 — Two of the six sheets the fill contract rests on are new construction, and the draft never says so

B4's table and C3's node contract both list **age** ("how old is this pixel") and **surface direction** among the named quantities, and B7's insurance plan depends on surface direction being askable (*"Stages that want a surface direction ask the shape for it as a named quantity — B4's table already has it"*). Digest §C4 E3 is explicit that neither exists: *"**'How old this pixel's stuff is' is published by nobody. 'Which way the surface faces' is published by nobody.** Distance-from-edge exists on one generator only (PlasmaBloom's `rim_mix`) and is a **crossfade weight, not a distance**. Two of the six sheets the design is built on are **construction, not promotion**."* Also relevant and unstated: only **4 of 9** plug-ins publish coverage at all (§C4 E4), so even coverage is not free for five of them. The design is still right; it is just not as cheap as the document implies, and D1's Wave 1 item "the list of named quantities from B4" should say which of them have to be built from nothing.

## O-5 — §C7 drops the swarm cases that "need real work", including a 200× cost blow-up

C7 presents swarm as *"two implementations behind one control"* and moves on. The digest's measured bucket split over 29 behaviours is **21 comply · 5 better native path · 2 need real work · 1 out of scope** — the draft accounts for the first two buckets and silently drops the last two. The two that need real work are specific and one is expensive: Fireball is blocked on two missing numbers (`sourceX`/`sourceY`, `FireballSim.cs:20-30`), and the simulation swarm-wrapper cost is *"**236 M cell updates vs 1.2 M** at 24 frames × 200 particles on a 64×64 grid — ratio **200× = N**, i.e. asymptotic, not a constant factor."* A design that promises swarm on *every* generator, in a document whose §B9 treats performance as an architectural constraint, should name the case where the generic wrapper is asymptotically unaffordable.

## O-6 — §B12 picks the shell definition without naming the one it rejected

P3 §3.3 identifies **two genuinely different** hollow operators and says explicitly that *"the redesign should pick deliberately rather than discover the difference later"*: (a) the uniform-thickness SDF onion `|d| − t`, which gives even stroke weight and composes onto fused shapes; and (b) subtracting a scaled-down copy, which is what "inner radius" means on a disc and is what Pyre's `Ring` already does (`ringInner`, 0.1–0.92, `Pyre.cs:392`). They are identical on a disc and visibly different everywhere else — on a rect, (b) makes the long sides thinner than the short ones. The draft adopts (a) without telling the owner that (b) exists, that Pyre already ships it, or that his existing Rings mean (b).

---

# OVERSTATEMENTS

## V-1 — §B6: "already produced better output than anything that came after it" contradicts its own caveat one paragraph later

B6 asserts the reference app's strip *"was already built, already worked, and **already produced better output than anything that came after it**"*, then says four lines down: *"it was **read from source, not watched running**. Opening the reference app and dragging the protrusion control … would confirm the visual claim."* A comparative visual judgement cannot be made from source by an investigator who says he never saw either output. The structural finding is solid and independently verified (P3 §1.4: the reference's per-material `pro` slider spans −8 to +28 and is added at full weight on patterned cells and `× 0.25` on plain fill; 3D Shaper replaced it with `if (!entry.edgeStripes) z += 0.45;` at `public/index.html:1433`, a single constant, with stripes-layout cells getting no lift at all). Say **that**, not the comparative: *"it was already built, it worked, and the capability was lost in the port as a one-line simplification."*

## V-2 — §B9: "Taken as hard requirements" overstates requirement 3

The owner's verbatim third requirement is *"**Consider** using burst compiler to boost editor **if calculations get out of hand**"* — conditional and advisory, unlike requirements 1 and 2 which are stated flatly. The draft opens B9 with *"Taken as hard requirements"* covering all three, then escalates the third into *"an architectural constraint rather than an optimisation"*. The escalation is well-argued and is probably right, but it should be presented as the draft's own recommendation departing from the owner's wording, not as obedience to it: *"You stated the first two as requirements and the third as something to consider if calculations get out of hand. I am promoting the third to an architectural constraint, and here is why that is not gold-plating…"*

## V-3 — §B14: "automatic delivery of this design document" is delivery of about a quarter of it

`PLANNING_BUDGET` is 12,000 characters of tab content. `SHAPER_THE_DESIGN.md` is ~52 KB. As a single-tab planning document it would be truncated to the first 12,000 characters — roughly Parts A and B1–B4 — plus the path. B13 states the truncation mechanism honestly; B14 then says a group gives each task *"its own automatic delivery of this design document to whoever picks it up"* and *"an agent that arrives already holding the design does not have to reconstruct it."* It arrives holding about a quarter of it and a file path. Worth one clause in B14.

## V-4 — §C8: "the handful that do not" understates by an order of magnitude

C8: *"Once the sheets exist, the great majority of the effect library works on everything, and **the handful that do not** are individually identifiable and can say so."* The digest's published universality table over 41 effects is **11 universal & correctly advertised · 9 universal-capable but mis-advertised · 7 universal via buffer fallback at a cost · 13 need two extra sheets · 1 genuinely stuck**, and the independent bucket count is **buffer-only 20 · needs per-sample context 20 · irreconstructible 1**. Twenty of forty-one is not a handful. C8 also omits the third precondition the digest attaches to that group: *"a shape-local coordinate and a stable per-shape seed. **Plus a third precondition: a padded buffer.**"*

## V-5 — §B14: the one-editor objection to an overwatcher contradicts §B10

B14 rejects the overwatcher partly because *"building one would collide with the one-editor rule, since a permanently-resident overseer would be a second agent wanting the same editor."* B10 spends a section establishing that the one-editor rule is not a constraint on agent count but on editor count — *"the rule forbids two agents driving **one** editor, and cloning gives you **two** editors, one agent each"* — and the draft is simultaneously recommending the clone. An overseer that coordinates a board also need not drive an editor at all. The first reason (dispatched agents are one-shot processes with no cross-dispatch context) is sound on its own and is enough; the second should be dropped or reconciled with B10. For completeness: AgentHQ *does* have a per-group oversight mechanism (`scan_group_oversight`, `server.py:601-625`, driven by `POST /api/node/supervise`), which auto-promotes a supervised group's next open task once nothing is in flight — it is not a context-holding overseer, but the draft's flat "no mechanism" is worth softening to "no mechanism that holds context".

## V-6 — §B1: "Eleven were read line by line"

B1: *"**Eleven were read line by line** and re-read independently."* The digest's record of O's own objection to R3 is that this claim *"had **no stated denominator** (8 read line by line, 11 counted, 29 in the library)"*. The 4-and-7-of-eleven split that follows is correct (it is the corrected form of §C4 E6), but the reading claim should be **"Eight were read line by line and eleven classified"**.

## V-7 — §B11 and §C9: "supersedes the hexagon and octagon outright" drops P3's own caveat

P3 §4.2 confirms the hand-fits (`0.55` and `1.42`, not `√3/2` and `1+√2`) and then flags: *"⚠️ **Keep the old two around, or map them onto the N-gon with a compatibility phase/aspect**, because existing Shaper assets reference `hexagon`/`octagon` by name and will change shape slightly if silently redirected."* The asset half of that is void under the owner's disposable-assets ruling, but the **picture** half is not: a true hexagon is a visibly different shape from the `0.55` fit, so "supersedes outright" removes two pictures and re-adds them only behind one extra decision (`sides = 6`). That is precisely what the owner's anti-pattern-chasing gate question 3 tests for, and it is structurally the argument the digest records as **WITHDRAWN** for Pyre's own Star+Polygon (*"Hides four visible pictures behind a mode"*). Soften to: *"A true N-gon is one more case in that registry, and at `sides = 6` and `8` it does what the hand-fitted hexagon and octagon were approximating. Whether to retire the two hand-fits or keep them as their own named shapes is your call under gate 3 — the two are not the same picture."* The same applies to C9's table row. (P3 §4.3 makes the identical point about the star and the draft drops it too: Shaper's `star` is a smooth cosine flower, not a straight-edged star polygon, so a parameterised star at 5 points is a **different** picture, not the same one better specified.)

---

# QUIBBLES

- **§B3, "keeps border and interior in two separate buffers."** That is `borderOverMatte`, an opt-in flag that defaults **off** and exists for one narrow case — *"When this layer feeds a matte (Write/Luma) or is clipped: send only the FILL into the mask and draw the BORDER on top of the finished frame instead"* (`Pyre.cs:527-528`). Calling it the standing architecture overstates a per-layer escape hatch. The rest of the sentence checks out exactly: inward distance from the edge (`Pyre.cs:508-509`), its own separate fill (`borderFill`, a `ZuiFill`, `:521`), animatable width (`borderWidth` is a `ZUIValue`, `:519`), gated to six flat 2-D forms (`:505`, `PyreRenderer.IsFlat2DBorderForm`).
- **§B9 arithmetic.** *"a dozen layers each holding a bag of four sub-shapes is around fifty passes a frame"* — at one pass per node that is 12 bags + 48 children = **60**, not "around fifty". The conclusion is unaffected; the number understates the case the design has to survive.
- **§B5 dial and palette counts.** *"Between them they expose around thirty dials each and six named palettes."* P2 §2.2: `plasma` 23 dials, `steel` 30, `steel_clean` 30 — so "around thirty each" is wrong for one of three. The six named palettes (`aqua, arc, ion, magenta, solar, toxic`) belong to `plasma` alone; `steel`/`steel_clean` compute continuous RGB with no LUT at all (P2 §4.2), which matters because it is exactly why they need the palette-quantise step B5 goes on to ask for.
- **§B12, "at zero it does nothing."** With the shell defined as P3 defines it (`|d| − thickness`), thickness = 0 erases the shape rather than leaving it alone; the no-op only exists if the dial is expressed as an inner-radius fraction. Since B12 also promises "at their identity settings the output is bit-for-bit what it is today", say *"at its identity setting it does nothing"* and specify which parameterisation gives that.
- **§C9 vs §B12 on Streak.** C9 retires Streak; B12 then uses *"a capsule, a line, a streak"* as the family whose sweep axis must be redefined. Harmless, but "streak" should be dropped from B12's example list or the reader will wonder why a retired form is driving a design rule.
- **§C9's auxiliary-map row.** It sits in a table headed *"What is deliberately not carried across"* while its own cell hedges (*"either wire it up deliberately or let it go"*), and the digest still lists it as an **open owner question** (§D, R1 Q4). Either decide it or move it to the open list; it cannot be both.
- **Part A vs §B10 on the rebuild's length.** Part A says *"this entire **five-week** rebuild"*; B10 says *"the entire **thirty-three-day** rebuild"*. Same fact, two roundings, three pages apart — the exact figure is 33 days (`23c13700` 2026-07-23 22:21:31 → `60309923` 2026-08-25).
- **Preamble vs §B13 on tab count.** The preamble cites *"all **three** tabs of the planning document"*; B13 says *"four tabs"*. Both are defensible — `PyrePlus.json` has four tabs, one of which is a 61-character `Notes` stub, and 182,594 characters of content in total — but pick one and say why.
- **§D4's retired-effect bug.** *"Two retired effect types are still referenced by saved files"* omits that the three saved assets are in a **sibling project** (`SplashText`), not in this repository (digest E9). It does not change the point, but it changes who is affected.
- **§D3, "Three rounds of investigation, four documents."** This is round four; the sentence reads as though the fourth document arose from three rounds of work.

---

# WHAT I CHECKED AND FOUND CORRECT

Recorded so the corrections above are not read as a general verdict.

- **No withdrawn claim from digest §C is resurrected**, with the single exception of E-12 ("unused or under-used"). Specifically checked and clean: B1 correctly states **zero** generators are fused (§C1 reverses R2 §7.1); B1's 4-palette-independent + 7-product-form split over **eleven** is the corrected form of §C4 E6, not the 4-of-9 error; D4 places the warp pivot at the **top-right corner** (§C1); C9 **folds** vortex-field into swirl rather than culling it (§C1's reversal); B7 makes no claim about Shaper's lighting direction, so §C4 E14 is not re-imported; B7 correctly credits Solids with real 3-D rotation.
- **Every ruling the draft attributes to the owner is real, except E-2.** Verified verbatim: drawing outside the shape (`questions[1]` 5b, *"If drawing outside of the shape then the shape either increases or the output just gets more processing of more pixels"* — B3 quotes it near-exactly); disposable assets (`questions[1]` 1, *"They can all be deleted if it would simplify this refactoring project"*); the names Shaper / Silhouette / Solids (`questions[2]`, verbatim); arcs *"At 1 then all 360° of it is used"* plus *"a way to remove from tye middle outwards"*; *"Sparkle: we can remove it"* and *"Streak: remove"*; *"adding lights should only be done in one place for all light receivers"*; and the three performance requirements as quoted (with the requirement-3 caveat at V-2).
- **Every point in `questions[2]` is addressed somewhere.** Walked item by item: generators kept (B1), shapes/fills/borders (B2/B3/B4, C3/C4/C5), sub-shapes (B2), where borders go (B3), fill varieties incl. reflective (B4), procedural texture / external-tool question / Tapestry Surface (B5), Tapestry Shape's bevel (B5), the pixel-border function (B6), the AgentHQ change he told you to go ahead with (B13), the cloning objection (B10), 3-D rotation and Z translation, both "is it possible" and "is it important" (B7), Sparkle (B11/C9), Streak (B11/C9), star and polygon engines (B11), Solids as a separate generator never on the same layer (B8), shared lighting added in one place (B8/C6), the naming (B8), all three performance requirements incl. "many layers animated with many spriteFxs" (B9), arcs and middle-removal (B12), and the closing choice between an MD, a group, a board, a clone and a namespace (B10/B14/D1). Nothing in his message is unanswered. The omissions above are gaps against the *evidence*, not against his questions.
- **§B13 verified independently against `server.py`**, including the restart claim — see E-8.
- **Pyre's border stage** verified against `Pyre.cs:505-528`: inward band, own `ZuiFill`, animatable `ZUIValue` width, gated to six flat 2-D forms, all defaults off and byte-identical when off.
- **Tapestry's blend modes**, which B5 says Pyre lacks: confirmed. `TapestryLayer.cs:13` — `enum TapestryBlendMode { Normal, Add, Multiply, Screen }`, applied in `TapestryCompositor.Blend` (`:115-132`); no blend-mode enum or equivalent exists anywhere in `Runtime/Pyre/Pyre.cs`.
- **B6's structural account of the pixel-border feature** matches P3 §1.2–1.4 in every particular: the hand-authored strip, the two parameterisations (polar angle / axis projection), the reach control that covers the whole shape at maximum, the per-material protrusion in the reference and its absence in the port, the full-weight/quarter-weight rule, normals from finite differencing, the shadow march, and the shared z-buffer that makes the strip a divider.
- **B12's arc/shell analysis** matches P3 §3 throughout, including that neither operator touches a primitive's formula, that Shaper already computes the angle and already ships a reach-and-position pair wired to the pattern, that a swept rect is a gusset and a shelled rect a picture frame, that length-dominant shapes need a declared axis or you get a bowtie, and that shelling changes what "inside" means downstream.
- **The eighteen bugs** in D4 and the count itself match digest §E (E1–E18) exactly.

---

*Verifier P5, T-0098. Read-only; this file is the only thing written.*
