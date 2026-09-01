# T-0104 — the new generator contract, as clauses (Phase 1 output)

Extracted from `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\SHAPER_THE_DESIGN.md` (423 lines) by the Phase-1 Explore agent, against the existing baseline `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Runtime\Pyre\PyreForm.cs` (361 lines). Line numbers are design-doc lines unless prefixed `PyreForm.cs:`.

This is the checklist a candidate composite generator is scored against. Statuses: **HARD** = the design states it as a requirement; **CONSEQUENCE** = stated as an entailment or benefit; **ASPIRATION** = stated as an intent or budget.

## Group I — how the picture is handed over (B9)

- **C1 HARD** — MUST be handed a block of the canvas and fill it, never asked one pixel at a time. Design :210, :212; promoted from optimisation to contract at :202. Test: entry point is `Render(in PyreFormCtx, Color32[] target)` and the class writes `target` itself.
- **C2 HARD** — MUST declare itself whole-layer, not per-particle and not stateful. Design :373 (the two per-particle dispatch modes are "not carried across"). Test: `Kind` is `PyreFormKind.WholeLayer` (default).
- **C3 HARD as to shape** — the inner loop MUST touch no managed objects (Burst-shaped). Design :208, :214. Test: no per-pixel `new`, `List<>`/`Dictionary<>`, `Func<>`/virtual dispatch, boxing, string work or LINQ inside the pixel loop. Flat `float[]`/`Color32[]` scratch prepared once is the intended shape.
- **C4 CONSEQUENCE** — intermediate sheets MUST be flat arrays over the canvas, produced once, read downstream. Design :216.

## Group II — caching and dirty rules (B9 requirements 1 and 2)

- **C5 HARD** — MUST be a pure function of its own settings and its inputs. Design :204. Test: no `UnityEngine.Random`, `Time.*`, `DateTime`, static mutable counters, no reads of another layer.
- **C6 HARD** — every authoring dial MUST be covered by the cache key. Design :204, :206. Test: `ContentHash()` not overridden lossily; no authoring state in `[NonSerialized]` fields; note `MixValue` hashes a `UnityEngine.Object` by instance id (`PyreForm.cs:335`), so a form whose look depends on a texture's pixels keys wrongly.
- **C7 HARD** — re-rendering the same frame with the same settings MUST reproduce the same picture (replay, not recompute). Design :206. Test: no frame-to-frame carried state that is not reconstructible from `ctx.frameIndex`/`ctx.life`.
- **C8 CONSEQUENCE** — shared pre-passes go through the shared cache, not recomputed per render clone. Design :204; baseline `PyreForm.cs:212-219` (`PrepassIdentity`/`SharePrepassWith`).
- **C9 ASPIRATION/budget** — cheap enough that ~60 node-passes a frame at 96x64 (worst case ~68 000 cells) stays responsive. Design :204, :218.

## Group III — the resolve (B7)

- **C10 HARD** — if it publishes a surface direction at all it MUST publish it, never leave it inferred from neighbouring dots. Design :173, :337. For a composite this is vacuous by exclusion (it publishes nothing but coverage). The thing to look for instead: does the class bake its own directional shading into its RGBA? Legal, but it then will not respond to the document light rig.
- **C11 HARD but N/A to a composite** — every primitive declares a bound on how far it may claim its edge is. Design :174, :323. A composite is not a shape node (:306), so record N/A, not pass.
- **C12 CONSEQUENCE** — MUST tolerate being one of several layers a line passes through, not assume it is the winner. Design :172, :323. Test: writes only its own isolated buffer, never reads the composited frame.

## Group IV — named quantities (B4)

- **C13 HARD (for shape generators)** — MUST declare which named quantities it publishes, visibly to the UI, so a fill needing a missing one is greyed out with the reason shown. Design :106. NOTE: no non-debug declaration mechanism exists in the baseline — `IPlusFieldPublisher.PublishFields` is only ever called while a debug sink is installed (`PyreForm.cs:156-159,171-174`; `PyreRenderer.cs:352`).
- **C14 HARD** — published names MUST come from the fixed vocabulary: heat, density, soot, depth, age, surface direction (plus coverage, height, edge distance). Design :104, :319, :108. Baseline names actually in use are `H`, `T`, `C`, `ramp_t`, `alpha`, `alpha_f`, `rim_mix` — none of which is a contract vocabulary name. Expect a rename, not a rewrite.

## Group V — the composite clause proper (design :49, :306)

The anchor sentence, design :49:
> A **composite generator** is the escape hatch: it produces a finished picture directly, the way the nine big imported effects do today. Its contract is one line — *it must publish coverage, and it may publish nothing else*. With that it still groups, still masks, still swarms and still takes buffer-level post-processing. What it gives up is a choice of fill (its picker offers one entry), the border stage, and any effect that needs to know where it is inside the shape — which is roughly a third to a half of the effect library. That is three things, not one, and a layer using a composite generator should say so.

- **C15 HARD** — MUST produce a finished picture directly. Design :49, :14. Free for every `PyreForm`.
- **C16 HARD** — MUST publish coverage. Design :49. **THE CENTRAL AMBIGUITY — see the section at the bottom. Score it BOTH ways.**
- **C17 HARD (permission-limit)** — MUST NOT publish anything else. Design :49. Read strictly this constrains downstream consumers, not the class; surplus planes are dead weight, not a violation. Record what is published.
- **C18 HARD, stated twice** — MUST NOT offer a choice of fill; its layer may not be re-filled. Design :49, :306. Test: `public override bool UsesFill => false;` (`PyreForm.cs:192-195`). Leaving the `true` default fails as written. This is the sharpest mechanical test in the contract.
- **C19 HARD by exclusion** — MUST NOT participate in the border stage. Design :49; :88 names three generators that already draw a **private rim of their own** — the lit solids, the text generator and **the fire simulation** — and requires each be reconsidered as an ordinary border or *explicitly kept and documented*. "Silently having both is how the current confusion started."
- **C20 HARD by exclusion** — MUST NOT require any effect that needs to know where it is inside the shape. Design :49, :351.
- **C21 HARD (:51) / aspiration (:49)** — a layer using a composite MUST carry a **declared reason** of exactly one of two kinds: its look is authored data rather than a rule (permanent), or nobody has split it yet (temporary, and it must say so on the layer). Design :49, :51. NOTE: no mechanism exists — `PyreFormInfoAttribute` carries only DisplayName/Group/Icon (`PyreForm.cs:38-50`).
- **C22 CONSEQUENCE** — MUST still group. Design :49. Test: canvas-fraction placement, not hard-coded pixel counts; `PyreFormCtx.WithSize` (`PyreForm.cs:140-150`) already re-hosts at another canvas size.
- **C23 CONSEQUENCE** — MUST still mask. Design :49, :309 ("This machinery already exists, is generator-agnostic, and works"). Free from the isolated scratch.
- **C24 HARD** — MUST still swarm; swarm is available on **every** generator. Design :49, :341. Test: reads `ctx.swarm` and places one instance per `PyreSwarmInstance`, or is a declared native-path generator.
  - **C24a HARD** — for a simulation-based generator, swarming MUST mean many seeds inside one simulation, not many simulations (a realistic swarm measures ~200x the cell updates). Design :343.
  - **C24b HARD (precondition)** — MUST have per-instance parameters for the wrapper to vary; several generators have none, so a generic wrapper would produce N identical copies. Design :343. Available per-instance fields: `x, y, own, spawnLife, index, orientDeg, zNorm, sizeMul, brightMul` (`PyreForm.cs:61-71`).
  - **C24c HARD** — every swarm instance MUST run on its own lifetime clock, not the shared clock. Design :345, which pre-announces that **most of the big effects fail this today** ("instances pop into existence mid-animation instead of having their own lifetimes"). Test: does the per-instance loop drive animation from `ctx.swarm[i].own`/`.spawnLife`, or from `ctx.life`?
- **C25 CONSEQUENCE** — MUST still take buffer-level post-processing. Design :49, :164. Free.
- **C26 HARD for the rebuild, but new construction** — MUST tolerate a padded buffer and honour the picture-rect that says where its artwork sits inside it. Design :351, :420 — the call "exists and is never invoked anywhere", and becomes live the instant effects may draw outside the shape. Test: does the class assume `target` is exactly the artwork extent (centre = W/2, H/2)? Record as contract-change impact, not an effect defect.
- **C27 HARD** — the stage an effect runs at (per-instance pre-composite vs finished buffer) stays an explicit property. Design :353; the contrary claim is explicitly withdrawn. For the generator, the question is: does it composite its own swarm instances internally, putting the per-instance stage inside it and out of reach of the effect stack?

## Group VI — the meta-clause

- **C28 HARD — the decision criterion.** The two effects must satisfy the above **WITH NO EDITS**. Design :375, :377. The exact bar is "the existing plug-in contract is unchanged by the new one" / "confirming they satisfy the new contract with no edits". A clause that requires a rename (C14) or a new declared field (C21) is *already* an edit. If they do not satisfy it, "this decision has to be revisited".

## Group VII — carried over from the existing contract, still binding

- **C29 HARD (existing)** — MUST NOT keep mutable static state, and MUST NOT touch a `UnityEngine.Object` (Texture2D, Sprite, TMP, AssetDatabase) from `Render`/`Prepare`. `PyreForm.cs:15-19`. C5 and C7 depend on it.

---

## Free from the base class (any subclass gets these)

C1, C2 (default), C6 (mechanism), C7 (per-thread deep clone), C5 (deterministic Eval funnel), C8 (mechanism), C12, C15, C23, C25 (all four from the isolated transparent scratch, `PyreForm.cs:222-223`), C22 (mechanism, `WithSize`), C18 (mechanism only — the *value* is per-effect), C24 (mechanism only — `ctx.swarm` + all of `PyreSwarmInstance`).

**The single most important Phase-1 finding: B9's hardest new requirement (C1, block-of-canvas) is already the existing contract's exact shape.**

## Per-effect — must be read in the class body

C3, C5, C7, C29 (stated in the base header but not enforced), C6 edge cases, C9, C10, C13, C14, C17, C18 (the value), C19, C20, C22, C24/a/b/c, C26, C27, plus `HandlesGeometry` (`PyreForm.cs:201`).

## Satisfiable by NOBODY today — mechanism does not exist

Report these separately so they do not contaminate the C28 verdict: **C13** (no non-debug quantity declaration), **C21** (no declared-reason field), **C26** (picture-rect never invoked, nothing pads), **C14** (vocabulary rename of the whole family at once).

---

## THE CENTRAL AMBIGUITY — what "publish coverage" (C16) means

Two readings, and the design does not settle it:

- **(i) a separate float coverage plane** emitted through a publishing mechanism like `IPlusFieldPublisher`.
- **(ii) the finished RGBA picture has a meaningful alpha channel.**

**Line 108's referent is proven to be (i).** Its counts match the code exactly: nine Kiln forms exist; **eight** implement `IPlusFieldPublisher` (`InfernoForm.cs:15` is the one that does not); and exactly **four** emit an alpha-named plane — `ArcBurstForm.cs:426` `sink("alpha", _dumpA)`, `PlasmaBloomForm.cs:401` `sink("alpha", _dumpA)`, `OrbForm.cs:469` `sink("alpha_f", _dumpA)`, `TorchForm.cs:122` `sink("alpha_f", _dumpA)`. The other four publishers emit no alpha plane: `ForkBlastForm.cs:139-140` (`H`,`T`) and `Jet\JetFormBase.cs:75-77` (`H`,`T`,`ramp_t`, covering all three jet forms).

**Line 49's referent is not stated**, and the evidence pulls both ways:

Toward (i): coverage is listed alongside height and the named quantities as a flat array (:216); coverage is defined as *"soft and unbounded, a fog rather than a stencil"* (:317) and **an 8-bit `Color32.a` cannot be unbounded**; a shape generator's coverage is the same word for the same species and shape generators never produce colour (:46).

Toward (ii): line 49 asserts a composite with coverage *"still masks"*, and :309 says the masking machinery *"already exists, is generator-agnostic, and works"* — which is true today for all nine, including the five with no float alpha plane; line 377's whole "unmodified / no edits" premise and line 395's *"hosting the nine imported effects unmodified"* both collapse under (i), where only four of nine qualify; and :51 frames the composite as *the migration escape hatch* — an escape hatch five of nine effects cannot enter is not one.

**A third possibility the design neither states nor excludes:** the composite publishes its RGBA alpha *as* the coverage plane, promoted to float **by the host** rather than by the generator. That satisfies (ii) mechanically while giving downstream consumers the flat float array of (i), and makes all nine pass with no edits. It is the obvious reconciliation but the document does not say it anywhere.

**Therefore: score C16 both ways and report both.**
