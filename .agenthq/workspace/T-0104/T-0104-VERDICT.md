# T-0104 — do two imported effects satisfy the new generator contract unmodified?

**Verdict: no. Neither does. But the reference-rather-than-copy decision survives anyway, for a reason the design document did not give — and it needs one carve-out the design does not make.**

Effects examined: **Torch** (`Assets/Packages/Laubrary/Runtime/Pyre/Forms/Kiln/TorchForm.cs` + `PyreTorch.cs`) and **Inferno** (`Assets/Packages/Laubrary/Runtime/Pyre/Forms/Kiln/InfernoForm.cs` + `PyreInferno.cs`). Contract: the 29 clauses at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0104\CONTRACT-CLAUSES.md`, extracted from `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\SHAPER_THE_DESIGN.md`.

Method: contract extracted by one agent, the two effects audited independently by two more, then 22 load-bearing claims re-checked line by line against source by a fourth adversarial agent. **Every line number cited below was verified by that fourth pass.** Where the audits were wrong, the verifier's correction is what appears here.

---

## 1. Why Torch and Inferno

The design gives a composite generator exactly one positive obligation — *"it must publish coverage, and it may publish nothing else"* (design :49) — and that phrase is ambiguous (§4). Torch and Inferno sit on opposite sides of the ambiguity: Torch computes and emits a float alpha plane, Inferno is the one Kiln form of nine that implements no publisher at all. Auditing two publishers would have dodged the question. This pair forces it.

---

## 2. The result, sorted into the three buckets that matter

Blurring these three is the mistake that would make this report useless. A clause nobody can satisfy is not an effect defect, and neither is a clause that does not apply.

### Bucket A — genuine per-effect gaps (this is the real answer)

| Effect | Clause | Gap | Size | Changes rendered output? |
|---|---|---|---|---|
| **Torch** | **C24c** — per-instance lifetime clock | `TorchForm.cs:155` computes `t` once from the shared clock and hands the *same* `t` to every swarm instance (`:184`, `:197`). `sp.own` is used only as a liveness gate (`:181`, `:193`); `spawnLife` appears **zero** times in the file. Every flame sits at the identical point of its breathing/surge/lash cycle, and an instance born mid-clip pops in mid-animation. | 3–6 lines (`t` is already a parameter of `Accumulate`) | **Yes** — design :345 says so explicitly |
| **Torch** | C8 — shared pre-passes | `PermTable`/`TongueTable`/`EmberTable`/LUT (`PyreTorch.cs:504`, `:584`, `:620`) are frame-invariant given seed+settings and are rebuilt every frame, per instance, per clone. `PyrePrepassCache` is never called. | 15–25 lines | No — pure memoisation |
| **Torch** | C9 — responsiveness budget | Structural: `SS = 3` (`PyreTorch.cs:384`, 9 cells per canvas pixel, vs 2 for PlasmaBloom) × 8–11 Perlin evaluations per cell. ~160 000 noise evaluations for one node pass at 96×64, against a budget of ~60 passes a frame. | algorithm-level | **Yes** |
| **Torch** | C27 — per-instance effect stage | The swarm is summed into one shared heat plane and shaded once (`TorchForm.cs:207-216`), i.e. composited **in heat space before colour exists**. There is nothing pixel-shaped for a per-instance effect to act on, even in principle. | redesign | **Yes** |
| **Inferno** | **C18** — must not offer a choice of fill | No `UsesFill` override, so it inherits `true` (`PyreForm.cs:195`). One line to fix — **and the one-line fix is harmful**: the layer Fill is Inferno's *only* colour source (`PyreInferno.cs:544`, with a flat `Color.white` fallback), so hiding the Fill row makes the effect uncolourable. See §5. | 1 line, or ~4 done honestly | No to the renderer; yes to the UI |
| **Inferno** | C3 — Burst-shaped inner loop | Per-pixel `List<T>` indexing and managed-class dereference (`PyreInferno.cs:590`, `:592`, `:608`) plus per-pixel virtual dispatch (`:579-580`, `:824`). Not badly shaped — no per-pixel `new`/LINQ/boxing — but not Burst-able. | ~150–250 lines touched (SoA refactor) | Intended none, but float reassociation makes byte-identity something to **verify, not assume** |
| **Inferno** | C27 | Same shape as Torch's: all events union into one field (`PyreInferno.cs:731-734`) and shade once. | redesign | Yes |

**Torch also passes the contract's sharpest mechanical test unmodified** — `TorchForm.cs:55` is already `public override bool UsesFill => false;`. And **Inferno passes C24c**, the defect design :345 predicted most big effects would fail: `InfernoForm.cs:204` reads `sp.spawnLife`, `PyreInferno.cs:347` gives each event its own duration, `:406` computes a per-event local clock. The two effects fail in almost disjoint places.

### Bucket B — the mechanism does not exist for anyone (host construction, not effect defects)

**C13** (no non-debug channel declares what a generator publishes — `PublishFields` is called only under a debug sink, `PyreRenderer.cs:352`), **C21** (`PyreFormInfoAttribute` carries only DisplayName/Group/Icon, `PyreForm.cs:38-50` — no declared-reason field for anybody), **C26** (the picture-rect call is never invoked anywhere and nothing pads), **C14** (the published vocabulary is `H`/`T`/`C`/`ramp_t`/`alpha`/`alpha_f`/`rim_mix` — a family-wide rename), and **C16 under reading (i)**.

### Bucket C — not applicable to a composite

**C11** (a composite is not a shape node, design :306). **C24a** (neither effect is the simulation-based generator design :343 describes — `PyreInferno.cs:12-14` states outright *"Unlike Fire/Fireball this is NOT a sim"*; the simulation generator is `ShapeForm.Fire`/`PyreFireSim`, which already implements the demanded fix via `fireSwarmEmitters`).

---

## 3. The premise the task was set to test — and its real answer

The task's own framing: *"That only holds if the existing plug-in contract is unchanged by the new one."*

**The existing plug-in contract is not unchanged.** `PyreForm.cs` itself needs at least five additions before any composite can satisfy the new contract:

1. a declared-reason member on `PyreFormInfoAttribute` (C21);
2. a predicate for "offers a choice of fill", which is **not** the same predicate as the existing `UsesFill` — Inferno proves they differ (§5) (C18);
3. a non-debug publication channel (C13, C16(i));
4. a picture-rect on `PyreFormCtx` (C26);
5. the vocabulary rename (C14).

So on the design's own stated bar, the check **fails**, and by design :377 the decision "has to be revisited". §6 revisits it.

**But the bar is stricter than the decision needs.** Every one of those five is *additive*, and `PyreFormCtx`'s own header already says that is the intended growth path: *"Designed to grow (aux-map publishing, per-particle stamping) by adding members — existing forms keep compiling"* (`PyreForm.cs:96-97`). A new virtual with a default and a new optional ctx member do not fork anything. The question that actually decides reference-vs-copy is not *"is the contract unchanged?"* but **"can the nine be shared without forking them?"** — and the things that threaten *that* are the mandated **behaviour** changes, not the contract shape.

---

## 4. The central ambiguity: what "publish coverage" means

Two readings. **The design does not settle it, and the two give different pass/fail answers for five of the nine effects.**

- **(i) a separate float coverage plane.** Line 108's referent is provably this: its counts match the code exactly — nine Kiln forms, **eight** implement `IPlusFieldPublisher` (`InfernoForm.cs:15` is the sole non-implementer), and **exactly four** emit an alpha-named plane (`ArcBurstForm.cs:426`, `PlasmaBloomForm.cs:401`, `OrbForm.cs:469`, `TorchForm.cs:122`). Independently recounted from source. Under (i), Torch fails (its plane exists but only under a debug sink) and Inferno fails outright, along with four of the other seven.
- **(ii) the finished RGBA has a meaningful alpha channel.** Under (ii) both pass, and so do all nine — which is the only reading under which design :51's *"escape hatch"* and design :395's *"hosting the nine imported effects unmodified"* are true statements.

**A claim that had to be withdrawn.** One audit argued (ii) is already implemented, citing `PyreRenderer.cs:912` (`float cov = ... scratch[i].a * (1f/255f)`). The line is real; the framing is wrong. That path is `WriteMatteCoverage`, reached only for layers the author explicitly set to `matteRole == WriteMatte` (`PyreRenderer.Layers.cs:95`, `:208`, `:280`); it reads the *final composited* alpha — after pixel modifiers, after the layer Alpha envelope, quantised to 8 bits — and feeds one of four numbered mask channels. It is a **precedent for how a coverage channel could be plumbed, not an existing coverage publication.** Any design sentence resting on "the host already does this for all nine" needs rewriting.

**Recommendation (this is a decision for the owner; I recommend rather than assume):** take **reading (ii) with host-side promotion** — the composite publishes its RGBA alpha, the host promotes it to a float plane. Three reasons:

1. It is the only reading under which the escape hatch is an escape hatch. One that five of nine effects cannot enter is not one.
2. The obvious alternative — reuse `IPlusFieldPublisher` as the channel — carries a cost nobody costed: `PyreRenderer.cs:185` forces **the entire renderer serial** whenever a field sink is installed, because the sink is a process-wide mutable static (`PyreForm.cs:173`). "Just publish through the existing mechanism" means "give up multithreaded fill".
3. Torch shows the incremental cost is near zero anyway: its alpha is already computed unconditionally (`TorchForm.cs:209`) and already written to output (`:214`) — only the *plane copy* is debug-gated. Publishing it is one float store per cell, not a new calculation.

**The cost of that recommendation, stated honestly:** under (ii) the coverage signal is **opacity, not coverage**, and Inferno shows how far they diverge. At `PyreInferno.cs:800`, with fire and smoke at zero, a fully-dense cloud reads as ~2% alpha — a 40× divergence from its own `density` field on the same pixel. Worse, `:812` multiplies the layer's Alpha envelope in, so fading a layer to 50% halves its "coverage" — which a coverage plane must never do. Reading (ii) buys universality and pays for it with a coverage signal that is wrong in exactly the way masking cares about. **That trade is the owner's to make; it is the one open decision gating Wave 3.**

---

## 5. Inferno's C18 is the sharpest finding in the report

The contract's cleanest mechanical test (`UsesFill => false`) turns out to be the one place where the *existing* contract and the *new* one genuinely mean different things, and Inferno is the specimen that proves it.

- Composite layers *"may not be re-filled"* (design :306), so Inferno must declare it offers no fill **choice**.
- But `PyreInferno.cs:544` is the only `fill` read in the file, and it is the source of *every* colour in the effect — the LUT, the ignition flash (`:840-842`), the embers (`:908`). A null fill degrades to flat `Color.white`.
- So declaring `UsesFill => false` satisfies the clause and simultaneously hides the only control that colours the effect (`PyreWindow.cs:1287-1288`).

The existing `UsesFill` means *"should the editor show a Fill row"*. The new contract needs *"does this generator offer the shaper's fill stage a choice"*. Those coincide for seven of the nine and come apart on Inferno. **Fixing it correctly requires splitting the flag in the base class** — which is precisely the "existing plug-in contract is unchanged" premise failing, in the single most concrete way available.

---

## 6. Resolving the tension: reference the ports, duplicate the tool

The task requires this be settled explicitly, not left to pass silently. The owner's standing instruction is **duplicate rather than modify**. The design proposes **referencing** the ~10,700 lines of Kiln ports. Both cannot be followed literally.

**The resolution: the owner's rule survives, and applying it to the nine Kiln ports would defeat its own purpose.** The rule exists to stop a rebuild-in-flight from breaking working, verified code. On these nine specifically, duplication does the opposite:

- **Divergence here is invisible.** These are the one part of the codebase verified *numerically* against its originals. A duplicate that drifts produces no compile error and no test failure — just a slightly different fire. That is the hardest possible failure to notice, and duplication doubles the surface for it.
- **A duplicate is not asset-safe.** `[MovedFrom]` rewrites one old type identity to one new one; it cannot disambiguate two live claimants (`.agenthq/workspace/T-0098/L-rebuild-strategy.md` §2.4). With `Laubrary.Pyre.TorchForm` still alive, a `Laubrary.Shaper.TorchForm` cannot claim the same predecessor — so every authored asset's `SerializeReference` is at risk. This argument is stronger than the one the design used, and the design did not use it.
- **The design's own freeze-by-construction proposal achieves the owner's goal better than duplication does**: put the ports behind a shared contract assembly so the code most damaged by divergence *physically cannot* be duplicated, and the old tool keeps working untouched.

So: **duplicate the tool, reference the ported algorithms.** That is the design's own proposed resolution and it holds up.

### The carve-out the design does not make

Reference-rather-than-copy assumes one implementation can serve both tools. For some of the nine that is false, because **the design mandates behaviour changes**:

- Torch's per-instance clock (C24c) changes rendered output, and design :345 says the fix *"belongs in the rebuild rather than in the old tool"* — i.e. Shaper wants a Torch that renders differently from Pyre's Torch. One shared file cannot be both.
- The picture-rect (C26) changes output for every form the moment anything pads.
- Inferno's Burst refactor (C3) intends byte-identity but cannot assume it.

**Recommendation: gate each mandated behaviour change behind a flag on the shared class, defaulting to the current behaviour.** One implementation, one file, Pyre's output untouched, Shaper opts in. This keeps reference-rather-than-copy intact for all nine and costs a boolean per change. Forking even one of the nine should be a last resort, and if it happens it must be an explicit, recorded decision — not something that arrives by accident during the port.

---

## 7. Corrections to the design document

Found while verifying; each is a number or claim in `SHAPER_THE_DESIGN.md` that the code does not support.

1. **"Five of nine declare `UsesFill => false`" is a count of declaration sites, not of forms.** The effective census is **seven of nine**: ArcBurst `:50`, PlasmaBloom `:121`, Orb `:51`, Torch `:55` declare it, and JetForm / RadialJetForm / ExplosiveJetForm all inherit it from `JetFormBase.cs:23`. Only Inferno and ForkBlast are on the `true` default — and **ForkBlast's `true` is correct, not an omission** (it genuinely uses the Fill; see its own tooltips at `ForkBlastForm.cs:74`, `:116`). Anywhere the design reasons "five effects carry their own palette", the real number is seven.
2. **"Four of nine publish coverage" is correct** — independently recounted from source.
3. **The private-rim census at design :88 is incomplete, and the code does not support one of its three entries.** The line names the lit solids, the text generator and the fire simulation. But `grep -ni "rim\|outline"` over `PyreFireSim.cs` returns **zero hits** — the fire simulation draws no rim. Meanwhile two effects it does *not* name do draw private rims by other means: Inferno's `outerRim`/`hollowRim` (`InfernoForm.cs:83-85`) and PlasmaBloom's `rimMix`/`rimLo`/`rimHi` (`PlasmaBloomForm.cs:319-326`, published as `rim_mix` at `:402`). This is currently masked because `PyreRenderer.Layers.cs:92` (`p.hasBorder = layer.form == null && ...`) hard-excludes **every** `PyreForm` layer from the border stage — which is also why C19 passes structurally for all nine rather than by luck. **Ungating the border stage, which the design proposes, gives at least two of the nine effects two rims each.** New item for the port checklist.
4. **"The host already promotes alpha to coverage" is not true as stated** — see §4.

## 8. Latent defects found in passing (not contract clauses)

Recorded because they are cheap to fix now and expensive to discover later.

- **Torch allocates six objects per swarm particle per frame.** `PermTable` (`PyreTorch.cs:504`) → `new PyreNumpyRng` + `float[256]` + `int[512]`; `TongueTable` (`:584`) and `EmberTable` (`:620`) similarly. A 60-particle swarm over a 24-frame clip is ~8 600 allocations including ~1 400 `int[512]`s. Caching by seed does not help — every particle passes a different seed.
- **Torch re-Prepares itself inside Render** (`TorchForm.cs:138`), on top of the renderer's own `Prepare` (`PyreRenderer.cs:291`). Harmless today. But `evalAtLife` is an optional ctx parameter (`PyreForm.cs:114`) and `PrepareCtxAt` falls back to static dial values when it is absent (`PyreForm.cs:132-135`) — so any future caller that hand-builds a `PyreFormCtx` and correctly Prepares beforehand has Torch silently overwrite it, killing every envelope. Inferno has the mirror-image gap: it never self-Prepares, and `PyreInferno.Anim` is a struct, so a caller that forgets Prepare gets an all-zero anim and a silently blank frame instead of an exception. One of the two conventions is wrong.
- **`PyreForm.Clone()` shallow-copies exactly the scratch it must not.** `[NonSerialized]` fields are skipped by the deep-copy loop (`PyreForm.cs:234`) and so keep the `MemberwiseClone` reference, aliasing Torch's twelve scratch arrays (`TorchForm.cs:83-87`) between a duplicated layer and its original — and Render's reuse guard (`:141`) will not reallocate, because the aliased array is the right size. **The per-thread story nonetheless holds**, because `PyreFrameFill.CloneForWorker` (`PyreFrameFill.cs:86-97`) uses `Instantiate(spec)`, a serialization round-trip that nulls `[NonSerialized]` fields. So the contract is safe, but by a route the doc comment at `PyreForm.cs:203-205` does not describe, and the aliasing hazard sits one refactor away.
- **Torch mutates authored state during Render** (`TorchForm.cs:131` `s.ramp ??= ...`; `PyreTorch.cs:493` `s.EnsureLive()`). Benign today; means "pure function of its inputs during Render" is not literally true for Torch.

---

## 9. Bottom line

**Neither effect satisfies the new contract unmodified, so design :377's check fails as written.** The gap is smaller and differently shaped than that sentence suggests: of the clauses either effect fails, five are host mechanisms no generator can satisfy today and two are inapplicable to a composite. **Torch's real gap is one mandatory 3–6-line change that alters rendered output (the shared swarm clock) plus two structural performance problems. Inferno's real gap is one declaration whose honest form requires splitting a base-class flag, plus a Burst-shape refactor.** The two fail in almost disjoint places, which is itself evidence the contract is broadly a good fit rather than systematically wrong for this family.

**Reference-rather-than-copy survives the check**, because the contract changes it needs are additive and keep every existing form compiling, and because duplicating these nine specifically is both the most divergence-prone and the least asset-safe option available. It needs the §6 carve-out — flag-gate each mandated behaviour change on the shared class — and it needs the §4 decision made before Wave 3 starts.
