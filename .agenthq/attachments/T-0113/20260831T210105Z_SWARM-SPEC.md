# T-0113 — Universal swarm: a generic wrapper plus one native path

Wave 3 of the Shaper rebuild, built in the separate worktree `D:\UNITY\Laubrary Dev - Shaper` (branch `feat/shaper`, editor on port 7801). This is the design document for the swarm modifier — the part of `SHAPER_THE_DESIGN.md` Part C7 this task exists to build. Companion doc: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0113\VERIFICATION.md` (the measured account, contact sheet, honest scope-limits).

## Part 1 — The contract, stated once

Swarm is a modifier available on **every** `ShaperNode` — Primitive, Bag, and the T-0112 Composite escape hatch — exactly the way `ShaperSweep`/`ShaperShell` already are: a node **gains** it, swarm never replaces the node's own kind. Its identity setting is `enabled == false` or `count <= 1`, which returns the node's own content untouched — bit-identical to the un-swarmed tree (measured, `ShaperSwarmAudit` CT-0).

Two implementations sit behind the SAME authored fields on `ShaperSwarmDef`:

- **Generic** — the default/fallback. Compiles the node's own content `count` times, each with its own jittered transform, clock and seed, and unions the results. Works on **every** node kind with **zero extra code** from the generator, because the jitter is applied at the swarm layer (the node's own `transform` block and the compiler's own `phase01`/`seed` state), not inside any generator's own code.
- **Native** — a generator-declared faster or interacting path. Offered today by a Composite node whose source implements `IShaperSwarmNativeSource` and currently reports it supports batching.

**Which implementation ran is never authored, always read back.** `ShaperSwarmImplementation` (`None`/`Generic`/`Native`) is published on `ShaperProgram.swarmImplementation` after every compile — a node cannot request Native, a source can only offer it, mirroring the "declared reason, not declared exemption" stance `ShaperCompositeReason` already takes for T-0112's escape hatch. This is the direct answer to the task's explicit requirement: *which implementation is in use must be SHOWN, never silently hidden, because it changes what the controls mean* — `interact` is a real, working control under Native (CT-6) and a structurally-verified no-op under Generic (CT-7), and a reader of `ShaperProgram` can always tell which case they are in without inspecting source code.

Files: `Runtime/Shaper/ShaperSwarmDef.cs` (new — the modifier data, the two capability interfaces), `Runtime/Shaper/ShaperNode.cs` (new `swarm` field), `Runtime/Shaper/ShaperProgram.cs` (new summary fields — the structural "shown, not hidden" surface), `Runtime/Shaper/ShaperCompiler.cs` (the emission logic — `EmitNodeMaybeSwarm`, `EmitSwarm`, `EmitGenericSwarm`, `EmitNativeSwarm`), `Editor/Shaper/ShaperSwarmAudit.cs` (the compliance pass + contact sheet + the two test-fixture sources).

## Part 2 — Data model

`ShaperSwarmDef` (mirrors `ShaperSweep`/`ShaperShell`'s shape):

| Field | Meaning |
|---|---|
| `enabled` | Off is the identity. |
| `count` (1..64) | Instance count. 1 is a legal identity — one instance, itself. |
| `seed` | Base seed; each instance draws from `Hash(seed, instanceIndex)`, never `System.Random` (BC-1.3). |
| `positionJitter` (Vector2) | Half-range, parent-local canvas units, of a per-instance random position offset. |
| `rotationJitterDegrees` | Half-range degrees, per-instance random rotation offset. |
| `scaleJitter` (0..1) | Half-range fraction, per-instance random uniform scale multiplier around 1. |
| `lifetimeStagger` (0..1) | **The shared-clock fix** — see Part 7. |
| `merge` (`ShaperBlend`, reused) | How instances fold together — the same width/sharpness/carveStrength knobs a Bag member's own blend already exposes. |
| `interact` | Native-path-only. Authored data with no effect under Generic (CT-7). |

`ShaperProgram` gained: `hasSwarm`, `swarmImplementation`, `swarmImplementationNode`, `swarmCount`, `swarmNodeCount`, `swarmCapped`, `swarmCapReason`, `swarmInstancePhases` — all **read-only outputs of a compile**, following the same "first offender's name, total count" pattern `hasSingularTransform`/`singularNode`/`singularTransformCount` already use elsewhere in the same file.

## Part 3 — The generic wrapper, mechanically

`EmitGenericSwarm` (in `ShaperCompiler.cs`) reuses `EmitBag`'s own combine-fold **exactly** (same `ShaperOps.Combine`/`ShaperBound.Combine` calls, same box/localBox union tracking) rather than inventing a second folding mechanism — the two are structurally the same operation ("union several sibling emissions"), so they share the shape even though the members differ (a Bag's members are distinct authored nodes; a swarm's "members" are `count` jittered instances of the *same* node).

For each instance `i`, the wrapper:
1. Draws five hash values from `Hash(swarm.seed, i)` (position x/y, rotation, scale, phase) — the same avalanche-hash technique `ShaperValue.Sample`'s `MinMax` mode already uses, restated locally so swarm jitter never touches `System.Random`/`UnityEngine.Random`.
2. **Temporarily mutates** the node's own `transform.translate/rotation/scale` and the compiler's own `State.phase01`/`seed`, calls the ordinary `EmitNode` (so sweep/shell/border on the swarmed node itself apply per-instance too — a full copy, not a partial one), then restores everything in a `finally` block.
3. Guards against re-entrance by temporarily setting `swarm.enabled = false` for the duration of its own per-instance `EmitNode` call.
4. Emits one `Combine` op per instance (mode `Add`, using `swarm.merge`'s width/sharpness/strength), exactly mirroring `EmitBag`'s own op emission.

This mutate-then-restore technique is safe under Shaper's ownership model (a `ShaperNode` is not shared by reference across two parents in one tree) and the compiler's single-threaded, synchronous, non-reentrant-per-node call shape — documented explicitly in the code rather than left implicit.

**Zero extra generator code, measured.** CT-1 swarms a Primitive, a two-member Bag, and a non-native Composite source and confirms each produces exactly `count`× the expected leaf-level op (5 Leaf ops, 10 Leaf ops for 2 members × 5 instances, 5 CompositeSample ops respectively) with `swarmImplementation == Generic` in every case — none of the three generator kinds needed a single line changed to support this.

## Part 4 — The native path, and why only one was built

`IShaperSwarmNativeSource` is the capability interface a Composite source implements to offer Native: `SupportsNativeSwarm` (checked every compile, never assumed), `NativeSwarmReason` (required, non-blank — the same declared-reason obligation `ShaperCompositeDef.reasonNote` already carries), and `RenderSwarm(width, height, phase01, instanceSeeds[], instanceOffsets[], instancePhases[], interact, target)` — one call renders **all** instances into **one** raster.

`EmitNativeSwarm` computes the same per-instance jitter arrays the generic wrapper would have (so a native source's output is comparable to, not a different effect from, what Generic would have produced), calls `RenderSwarm` once, and emits exactly **one** `CompositeSample` op — **O(1) ops regardless of `count`**, against the generic path's O(count) (CT-3: count=6 and count=40 both produce exactly 1 op).

**Honest scope limit — only one representative native path was built, not a Primitive/Bag native path too.** The task's suggested approach explicitly allows "a smaller fully-verified slice beats a rushed full port," and the judgment call here is real: a geometric "native swarm" for a Primitive/Bag would mean domain-repeat (SDF space-folding, `p = mod(p, spacing) - spacing/2` before the child's own distance evaluates) — a real, well-known technique, but Shaper's flat post-order RPN evaluator has no mechanism for a parent op to re-evaluate a child at a *different* point than the canvas sample it was handed (every op reads the SAME canvas `(x,y)` through its own baked inverse; a child's distance is already fixed by the time a wrapping op could run). Building that correctly — including the well-known "domain-repeat breaks signed-distance correctness near cell boundaries unless neighbouring cells are also checked" problem — is a real, separately-scoped compiler feature, not a small addition, and was deliberately not attempted in this task. Primitive and Bag nodes always resolve to Generic today; this is a true, structural limitation, stated here rather than glossed over.

The representative native path built instead is a **simulation-style Composite source** (`ShaperSwarmAudit`'s `ToyEmberSource`, a toy heat-diffusion sim), chosen because it is the ONE case the task body names explicitly as needing "real engineering, not a thin wrapper" — see Part 5 — and because it is where "interact" has an honest, non-contrived meaning (Part 6).

## Part 5 — The simulation cost problem: measured, not assumed

The task body states the design doc's own claim: an independent-instance swarm of a simulation-based generator is "measured at roughly 200x cell-update cost for a realistic swarm size" (`SHAPER_THE_DESIGN.md` Part C7). This task's job was to get a REAL answer, not wrap-and-hope.

**What was measured.** No simulation-based Composite source is hosted in Shaper yet (T-0112 hosted only Orb, which is stateless/closed-form) — the real Pyre Fire/Fireball sim harness referenced by the design doc lives outside Shaper's scope for this task. So a toy representative sim (`ToyEmberSource`: a 96×96 heat-diffusion grid, plus-shaped injection, `Decay`/`Diffuse` per step) was built and timed directly (`ShaperSwarmAudit` CT-5, `System.Diagnostics.Stopwatch`, JIT-warmed before timing):

- 16 independent `Render()` calls (one grid each, 30 diffusion steps/frame): **94.1 ms**
- 1 batched `RenderSwarm()` call (one shared grid, 16 injection points, same 30 steps): **6.1 ms**
- **Measured ratio: ~15.6×–16.2× across repeated runs** (N=16, the instance count used for the measurement)

**This does not reproduce the design doc's ~200x figure, and the doc says so explicitly rather than implying otherwise.** The 200x number was measured on the real Pyre Fire/Fireball sim — layered heat+fuel+turbulence grids, warmup passes before steady state, real per-instance managed-object overhead — none of which this toy single-grid fixture reproduces. What this measurement DOES verify is the **shape** the 200x figure depends on: independent cost grows linearly with instance count (N sims × the same per-sim cost), batched cost does not grow with instance count at all (one shared grid, O(1) diffusion passes regardless of N — only the cheap O(N) injection step grows). That asymptotic shape is the actionable engineering fact this task needed to establish and build against; the exact multiplier is a property of the specific sim, not of the swarm mechanism.

**The fix, both halves of it, built:**
1. `IShaperSwarmNativeSource.RenderSwarm` — "many seeds inside one simulation, not many simulations," the design doc's own words, implemented and measured (above).
2. `IShaperSimulationSource` + `ShaperSwarmDef.SimulationHardCap` (= 6) — for a source that declares itself a stateful simulation (`IsStatefulSimulation == true`) but does **not** offer `IShaperSwarmNativeSource`, the compiler clamps `count` down to the hard cap and publishes `ShaperProgram.swarmCapped` / `swarmCapReason` — the **"explicit hard cap with a visible warning"** the task body names as one of three acceptable real answers. Measured (CT-4): an authored count of 20 on a no-native sim source clamps to 6 with a populated reason string; the SAME authored count of 20 on `ToyEmberSource` (which DOES offer native) is not capped at all — the cap is specifically for sources with nothing better on offer, not a blanket swarm-size limit.

## Part 6 — Per-instance parameters that didn't exist yet

Reading `ShaperPrimitiveDef` (`Runtime/Shaper/ShaperPrimitives.cs`) shows most shape parameters (`ellipseRx`, `rectHalfW`, `capsuleRadius`, …) are plain `float` fields, not `ZUIValue` dials — only Star's three parameters (`starLength`, `starBaseWidth`, `starSkew`) are `ZUIValue`, which is the ONE place `ShaperValue.Sample`'s seed-hashed `MinMax` mode could already give a primitive per-instance variation. That mechanism is real but narrow — most generators had genuinely nothing for a swarm to vary per instance beyond the whole node's position.

**The plumbing this task adds is at the swarm layer, not per-generator**, and it is universal rather than narrow: `ShaperSwarmDef.positionJitter`/`rotationJitterDegrees`/`scaleJitter` act directly on `ShaperNode.transform` — a field every node kind already has — so every generator gains real per-instance variation (different position, rotation, uniform scale) with no change to its own code. This is the concrete answer to "generators whose per-instance parameters don't exist yet ... need real plumbing added, not just documentation of the gap": the plumbing is the transform jitter itself, added once, inherited by every node kind. Where a generator's OWN dials also happen to be `ZUIValue` (Star today; potentially more as Shaper grows), those get a second, free source of per-instance variation through the seed swap already described in Part 3 step 2 — a bonus on top of the guaranteed transform-level mechanism, not a substitute for it.

## Part 7 — The shared-clock bug, and the fix

Task body: "most of the big effects currently start every instance on the SAME shared clock, so when N instances spawn they all pop in mid-animation instead of each having its own independent lifetime." `ShaperSwarmDef.lifetimeStagger` (0..1, default **1**) is the fix: each instance's phase is `Lerp(node's own phase, hash-drawn phase across the whole 0..1 cycle, lifetimeStagger)`.

**The old broken behaviour is kept selectable, not deleted**, at `lifetimeStagger = 0` — every instance shares the node's own phase exactly, reproducing the bug on demand rather than leaving it as a claim nobody can check. CT-2 measures both ends directly off `ShaperProgram.swarmInstancePhases` (published specifically so this is measurable rather than inferred): at `lifetimeStagger = 1`, 8 instances spread `max−min = 0.81` across the 0..1 cycle (well past the >0.3 bar for "genuinely staggered"); at `lifetimeStagger = 0`, all 8 instances read EXACTLY the node's own phase (0.4), spread `0.00000` — an exact, bit-level reproduction of the old bug.

The contact sheet's ember panels (Part 2 of `VERIFICATION.md`) make this visible directly: the staggered panel shows six embers of visibly different size/brightness (some near-birth/near-death and dim, one or two near mid-life and bright); the lockstep panel shows six embers of near-identical size/brightness, because they are all evaluated at the identical phase — the bug, rendered.

## Part 8 — For the future authoring window

No Shaper node has a live ZUI authoring window yet (Wave 3's "caching and preview-responsiveness harness," `SHAPER_THE_DESIGN.md` Part D1, is a later task) — this task's "shown, not hidden" requirement is therefore satisfied structurally (`ShaperProgram.swarmImplementation` etc.) and via the audit's plain-text report, per the same posture `PyreShaperCompositeAudit`/`ShaperFillAudit` already take. When that window is built, it MUST:

- Show "Implementation: Generic" or "Implementation: Native — *(NativeSwarmReason verbatim)*" for the swarmed node currently selected, read live off the last compile's `ShaperProgram`, never inferred or cached across a source swap.
- Show `interact` as **disabled/greyed with a reason** when the resolved implementation is Generic, not merely present-but-inert — CT-7 proves it has no effect there; the UI should not let an author believe otherwise.
- Show a visible warning banner (not just a console line) when `swarmCapped` is true, quoting `swarmCapReason` verbatim.

## Part 9 — Honest scope limits (full list)

- **No Primitive/Bag native path** (Part 4) — domain-repeat is real future work, not attempted here; both kinds always resolve to Generic.
- **No real Pyre Fire/Fireball sim hosted in Shaper yet** — the cost measurement (Part 5) uses a toy representative sim built for this task, not the actual system the design doc's 200x figure describes; the two are not the same measurement and the doc says so.
- **The 200x figure is not reproduced, only its shape is verified** (Part 5) — stated as a correction/clarification, not silently dropped.
- **A swarmed node's own "local frame"** (used by a parent Bag's box-folding, or a fill-stage subtree compile anchored on a swarmed node) is approximated as the node's own BASE, un-jittered transform — a swarm of N differently-transformed instances has no single coherent frame, and this is a documented approximation, not a precision claim.
- **No live editor UI** — see Part 8; this wave of Shaper has none yet for any node.
- **`swarm.merge.carveStrength` is dead weight** — swarm always folds instances with `ShaperCombineMode.Add` (a union; Subtract/Intersect between instances was judged not to make sense for a swarm and was not exposed), and `Combine`'s Add branch never reads `carveStrength` — only `width`/`sharpness` matter. `ShaperBlend` was reused whole, for consistency with the rest of the codebase's "reuse existing knobs" convention, rather than inventing a narrower type — a small, named wart rather than a silent one.
