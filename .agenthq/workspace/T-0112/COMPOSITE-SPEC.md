# T-0112 — the composite generator: hosting the nine imported effects unmodified

Wave 3, Shaper rebuild. Built and verified in the `D:\UNITY\Laubrary Dev - Shaper` worktree, branch `feat/shaper`, uncommitted (matching T-0110/T-0111's convention — the user/PM reviews and commits).

## 1. The contract, restated precisely against the live code

A composite generator is a third `ShaperNodeKind` (`Primitive = 0`, `Bag = 1`, `Composite = 2`) — a leaf, structurally parallel to `Primitive`, but it hosts a rendered picture instead of an analytic signed-distance function. The one-line contract from the task body is unchanged: **it must publish coverage, and it may publish nothing else.**

What that buys, verified against the actual pipeline stages that exist in this codebase today (not the aspirational ones — see §5 for what does not exist yet for *any* node kind):

| Stage | Exists in Shaper today? | Composite generator's status |
|---|---|---|
| Analytic shape combine (`ShaperCompiler`/`ShaperEvaluator`) | Yes | **Works** — a composite node contributes a value to the same op-stack fold every primitive uses (§2). |
| Grouping (Bag = union/carve/soft-union via `ShaperCombineMode`) | Yes | **Works for hard combine** (blend width 0, the default). Soft combine with a wide blend width is degraded — see §2's honesty note. |
| Border stage | Yes (`ShaperBorder`/`ShaperBorderDef`) | **Refused, structurally**, exactly as the task body states. `ShaperCompiler.EmitBorderJoin` now returns early for a `Composite` node before it even looks at `node.border` — an authored border on one is inert, not silently wrong. |
| Swappable fill (`ShaperFillDef`/`ShaperFillKind`) | Yes | **Refused, structurally.** `ShaperCompositeDef` carries no field of type `ShaperFillDef`/`ShaperFillKind` — verified by reflection in CT-6, not just by convention. |
| Masking, swarm, buffer-level post-processing | **No — not built for ANY node kind yet.** Shaper has no layer/mask/swarm/post-process stage at all; only the shape tree, the fill contract, and the border stage exist so far. | Not testable against a real pipeline because there is none yet. What IS true and verified: the composite's declared `published` set is `Coverage` only, which is the exact same declared-quantity contract every future mask/swarm/post-process stage will read (§4's `ShaperFillSheets.published`) — so nothing about this design blocks them; there is simply nothing running yet to demonstrate it against. |

## 2. How a composite node is evaluated (the actual mechanism)

`ShaperCompiler`/`ShaperEvaluator`'s existing engine is a **closed-form SDF walk**: every primitive computes an exact signed distance at a point; `Combine` folds two distances with `min`/`max`-family operators; the finished distance becomes coverage once, at the end, via `ShaperField.Coverage` (a smoothstep over `±halfBand`).

A composite generator has no analytic distance — only a rendered raster. So:

1. **At compile time** (`ShaperCompiler.EmitComposite`), the generator's `IShaperCompositeSource.Render(...)` is called ONCE, producing a `Color32[]` raster at a fixed bake resolution (`ShaperCompositeDef.bakeWidth/Height`, default 128×128). Its alpha channel is decoded to `float[] coverage` and stored on the compiled `ShaperProgram` (`ShaperProgram.composites`), referenced by index from a new op (`ShaperOpKind.CompositeSample`) — the same "bulk data referenced by index, never a managed object in the op" rule `ShaperFillDef.texture` already follows.
2. **At sample time** (`ShaperEvaluator.Distance`), the canvas point is mapped into the node's local frame exactly like a `Leaf` op, the raster is bilinear-sampled there, and the sampled coverage is inverted back into a **pseudo-distance** via the exact closed-form inverse of `ShaperField.Coverage`'s smoothstep. That pseudo-distance is pushed onto the same value stack every other op reads from — so `Combine`, `Sweep`, `Shell` and `Dilate` all "just work" on a composite member with **zero code of their own written for this case**. This is section 6.1's own argument ("coverage is the only thing anything downstream actually requires... Everything works"), and it is the mechanical reason it is true.

**The honest limit, stated once and cross-referenced everywhere it matters.** The inverted pseudo-distance is only a real distance within roughly one bake texel of the generator's own edge — outside that band the raster carries no further information (it has saturated to solid alpha 0 or 1), so the inversion formula saturates too, at `±halfBand`. This is:
- **why the border stage is refused** — a border can reach many pixels out, and past the antialiasing band the field is not a distance to anything;
- **why a hard combine (width 0) is fully correct** — it only needs the *sign* of the value, which is right everywhere, saturated or not (verified numerically, CT-3/CT-4);
- **why a wide soft combine is a documented, un-verified degradation** — not tested in this pass; a soft blend wider than a texel or two will read as closer to a hard combine than the authored width intends.

## 3. The nine, hosted unmodified, and their declared reason

**Finding, stated once:** all nine are §6.2 reason 2 (`ShaperCompositeReason.NotYetSplit`) — technical debt, not architecture. Zero are reason 1 (`AuthoredData`) — every one of the nine is a *procedural* generator (an additive energy field, a turbulence sum, a LUT read), never a baked sprite or hand-painted asset, so there is nothing "authored" about any of their pictures for reason 1 to apply to.

The task's other explicit correction — "no generator's colour and shape are mathematically fused... in four, the outline is completely indifferent to the palette; in the rest, the palette's transparency is multiplied into an edge rule" — resolves, for these nine specifically, as:

| Palette-**indifferent** (4) | Palette-**dependent**, veil-multiplied (5) |
|---|---|
| Orb (`PyreOrb.cs:617-635` — the 256-entry LUT has no alpha column) | Inferno |
| Torch (`TorchForm.cs:207-215` — alpha from a separate `_Fp` plane, colour from `_C`) | Fork Blast |
| Arc Burst (`ArcBurstForm.cs:496-504`) | Jet |
| Plasma Bloom (`PyrePlasmaBloom.cs:587-594`) | Radial Jet |
| | Explosive Jet |

Full per-generator reason text lives in code, not duplicated here — `Runtime/PyreShaper/PyreCompositeCatalog.cs`, one named entry per generator (`PyreCompositeCatalog.Orb`, `.Torch`, …), each with its own `reasonNote`. CT-5 (§6) is an automated compliance pass over this table, the same shape as `ShaperFillAudit`'s own conformance tests.

**Scope cut, stated plainly.** Only **Orb** is wired into a real, working `ShaperCompositeDef` and exercised end-to-end (compiled, sampled, rendered, verified — §6). The other eight are fully *classified* (their reason, their palette-dependence, their citation) but not individually instantiated as demo assets — hosting all nine to the same rigor as Orb is a straightforward repeat of the same adapter (`PyreFormCompositeSource { form = new XxxForm() }`, `PyreCompositeCatalog.Build(form, PyreCompositeCatalog.Xxx)`), not a design gap; it was cut for time, honestly, the same call T-0111 made for its own scope.

## 4. What deliberately is NOT wired

`Runtime/Shaper/ShaperFillResolver.cs` (1,525 lines — the fill-owning-node resolver that builds `ShaperFillSheets` for a Bag's own fill) is **untouched**. That means: a Bag containing a composite member still reports its normal `published` quantity set (typically `Coverage | EdgeDistance`) to its own fill, even though the finished distance is now partly a saturated pseudo-distance wherever the composite member dominates the fold. A `Gradient(ByEdgeDistance)` or `Ramp(EdgeDistance)` fill on such a bag would read a plausible-looking but not-fully-trustworthy edge-distance value near a composite member. This is a real, named follow-up (downgrade a program's declared `published` set to `Coverage`-only whenever it contains any `CompositeSample` op), not an oversight — it was left undone because touching `ShaperFillResolver.cs` safely was a bigger, separate piece of surgery than this pass's verified core, and every fixture built for this task's own verification uses a `Solid` fill specifically to sidestep it (Solid never reads `edgeDistance`).

No editor authoring window exists for `ShaperNode` at all yet (Primitive and Bag have none either — this is not a composite-specific gap). "Visible, not just a code comment" for the declared reason is satisfied today the same way every other Shaper compliance fact is: an automated audit (CT-5) that counts it, not an Inspector — because there is no Inspector for any of this yet.

## 5. Files touched

- `Runtime/Shaper/ShaperNode.cs` — `ShaperNodeKind.Composite`, `ShaperNode.composite` field, `ShaperNode.Composite(...)` factory.
- `Runtime/Shaper/ShaperCompositeDef.cs` — new. `ShaperCompositeReason`, `IShaperCompositeSource`, `ShaperCompiledComposite`, `ShaperCompositeDef`.
- `Runtime/Shaper/ShaperProgram.cs` — `ShaperOpKind.CompositeSample`, `ShaperProgram.composites`.
- `Runtime/Shaper/ShaperCompiler.cs` — `EmitComposite`, the `Composite` dispatch branch in `EmitNode`, the border-refusal guard in `EmitBorderJoin`.
- `Runtime/Shaper/ShaperEvaluator.cs` — the `CompositeSample` case in `Distance`, `SampleCompositeCoverage`, `InverseCoverage`.
- `Runtime/PyreShaper/` — **new bridge asmdef** `com.Lautaro-Arino.Laubrary.Pyre.Shaper`, named for the two systems it connects (the ZoetropePyre/ZoetropeLaunimator convention). `PyreFormCompositeSource.cs` (hosts a `PyreForm` behind `IShaperCompositeSource`), `PyreCompositeCatalog.cs` (the nine-generator classification table, §3).
- `Editor/PyreShaper/` — new bridge editor asmdef `com.Lautaro-Arino.Laubrary.Pyre.Shaper.Editor`. `PyreShaperCompositeAudit.cs` — CT-0 through CT-7 (see `VERIFICATION.md`) and `WriteContactSheet`.

Deliberately **not** touched: `com.Lautaro-Arino.Laubrary.Shaper`'s asmdef references (still only `ZuiRuntime` — Shaper has no dependency on Pyre; only the bridge does). `Runtime/Shaper/ShaperFillResolver.cs` (§4).
