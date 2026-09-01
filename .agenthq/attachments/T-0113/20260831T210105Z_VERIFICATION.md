# T-0113 — VERIFICATION

Universal swarm modifier for Shaper — a generic wrapper (works on every generator) plus one native path (a
representative simulation-style generator), built in the separate worktree `D:\UNITY\Laubrary Dev - Shaper`
(branch `feat/shaper`, editor on port 7801, **not** the primary `D:\UNITY\Laubrary Dev` editor). This document is
the honest account of what was built, what was measured, and what is explicitly NOT yet true.

Design document: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0113\SWARM-SPEC.md` (9 parts — the contract, the
data model, the generic wrapper mechanics, the native path and why only one was built, the simulation-cost
measurement, per-instance-parameter plumbing, the shared-clock fix, future-UI requirements, and a full honest
scope-limits list).

Contact sheet: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0113\swarm-contact-sheet.png` (6 cells, eyeballed
personally before this report was written — see Part 2).

## Part 1 — Compile status

Recompiled the Shaper editor (port 7801) after every source change in this task via the Unity CLI
(`unity.exe command --project-path "D:\UNITY\Laubrary Dev - Shaper" recompile` / `recompile_status`). Final
state: **`{"status":"completed","failed":false,"errors":[]}`** — zero compile errors. Verified not-stale by
executing new code through `eval_file` after each recompile (per the `unity-cli-compile-check` gotcha:
`recompile_status` alone can read cumulative/stale state) — every probe below actually ran the new types.

## Part 2 — The audit, measured (`ShaperSwarmAudit.RunAll()`, 9 CTs, all PASS)

Full run, verbatim (Unity CLI `eval_file`, `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Editor\Shaper\ShaperSwarmAudit.cs`):

```
CT-0 swarm disabled, or count<=1, is an EXACT no-op
  plain: 1 ops, hasSwarm=False
  swarm.enabled=false, count=9: 1 ops, hasSwarm=False (expected 1 ops, hasSwarm=False)
  swarm.enabled=true, count=1: 1 ops, hasSwarm=False (expected 1 ops, hasSwarm=False -- 1 instance is a legal identity)
  RESULT: PASS

CT-1 generic wrapper works on Primitive, Bag and Composite with ZERO extra generator code
  Primitive swarm(5): Leaf ops=5 (expected 5), impl=Generic
  Bag(2 members) swarm(5): Leaf ops=10 (expected 10), impl=Generic
  Composite(non-native) swarm(5): CompositeSample ops=5 (expected 5), impl=Generic
  instance[0].boxCx=0.00 vs instance[1].boxCx=-17.94 -- differ=True (N distinct instances, not N identical copies)
  RESULT: PASS

CT-2 independent per-instance lifetime -- the shared-clock bug this task's body names, fixed and measured
  lifetimeStagger=1 (the fix, default): 8 instance phases, spread (max-min) = 0.810 (expected > 0.3)
  lifetimeStagger=0 (old bug, kept selectable): spread = 0.00000, all equal node's own phase 0.4 = True
  RESULT: PASS

CT-3 native path selected for a source that offers it; op count is O(1), not O(N)
  count=6:  CompositeSample ops=1 (expected 1), impl=Native
  count=40: CompositeSample ops=1 (expected 1 -- SAME as count=6, O(1) not O(N)), impl=Native
  RESULT: PASS

CT-4 explicit hard-cap-with-a-visible-warning for a stateful sim with NO native path
  no-native sim, authored count=20: capped=True, resolved count=6 (expected True, 6)
    reason: stateful simulation source with no native batched swarm path -- N independent full simulations
    cost roughly N times one (SWARM-SPEC.md Part 5), so count is held at 6 instead of the authored 20.
  native-capable sim (ToyEmberSource), authored count=20: capped=False, resolved count=20
  RESULT: PASS

CT-5 MEASURED: N independent simulations vs one batched native pass (SWARM-SPEC.md Part 5)
  fixture: 96x96 grid, 30 diffusion steps/frame, N=16 instances
  16 independent Render() calls (one grid each): 94.14 ms
  1 batched RenderSwarm() call (one shared grid, 16 injectors): 6.05 ms
  measured ratio = 15.6x  (re-run also measured 16.1x-16.2x -- consistent across repeats)
  RESULT: PASS (ratio >= 2.0x required to demonstrate the non-linear-vs-flat shape)

CT-6 interact is LIVE under Native -- two close instances merge (true) vs stay separate (false)
  midpoint alpha, interact=true:  1.000 (heat diffused in from both neighbours)
  midpoint alpha, interact=false: 0.000 (EXACTLY 0 -- no cross-instance spatial bleed is possible at all)
  RESULT: PASS

CT-7 interact is INERT under Generic -- true vs false compile identically on a Primitive
  Generic swarm(4), interact=true:  9 ops
  Generic swarm(4), interact=false: 9 ops (identical op count, identical coverage across a sample line)
  RESULT: PASS

CT-8 a Native implementation always carries a non-blank declared reason (T-0112 sec6.2 precedent)
  ToyEmberSource.NativeSwarmReason blank=False
  RESULT: PASS
```

**9/9 PASS.** The single load-bearing number in the whole task — the sim-cost multiplier (CT-5) — is a real
`System.Diagnostics.Stopwatch` measurement, JIT-warmed before timing, not an assertion. CT-6's `interact=false`
case (exactly 0.000) is a structural guarantee, not a magnitude judgement call: `DecayOnly` literally never
writes to a neighbouring cell, so a non-injector cell can only ever read 0.

## Part 3 — The contact sheet, eyeballed

`D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0113\swarm-contact-sheet.png`, 3×2 cells:

| Cell | Content | What it proves |
|---|---|---|
| Bottom-left | Generic swarm of a Primitive disc, 7 instances, scale-jittered | Scattered circles of visibly different size and position — Generic works on a bare Primitive. |
| Bottom-middle | Generic swarm of a two-member Bag, 5 instances | Scattered *pairs* of blobs — Generic works on a compound Bag, not just a leaf. |
| Bottom-right | Native ember swarm, `lifetimeStagger=1` (the fix), 6 instances | Six embers of **visibly different size and brightness** — some dim/small (near birth or death), one or two bright (mid-life) — independent lifetimes, not lockstep. |
| Top-left | Native ember swarm, `lifetimeStagger=0` (the old bug, reproduced on demand), 6 instances | Six embers of near-**identical** size and brightness — the bug, rendered: every instance evaluated at the same phase. |
| Top-middle | Two close ember instances, `interact=true` | The two blobs visibly **touch and merge** into one connected glow. |
| Top-right | Same two instances, `interact=false` | Two small, clearly **separate** dots with a visible gap between them. |

Personally reviewed (Read tool, rendered at native size) before writing this report. The staggered-vs-lockstep
contrast (bottom-right vs top-left) and the interact contrast (top-middle vs top-right) are both legible at a
glance, which was the actual bar — "measure that two instances at different swarm-assigned phases are visibly
at different points in their lifecycle" (task body, verbatim) is satisfied by direct visual inspection, not just
by CT-2's numeric phase spread.

## Part 4 — Adversarial self-review

Questions asked of this work before writing it up as done, and the answer found for each:

- **"Does the generic wrapper actually touch the SAME code path an un-swarmed node uses, or a parallel one that could drift?"** It calls the ordinary `EmitNode` per instance (not a stripped-down re-implementation), so sweep/shell/border on the swarmed node itself apply per-instance automatically, and any future change to `EmitNode`'s own behaviour is inherited by swarm with no separate maintenance. Verified by CT-1's exact op-count arithmetic (5 Leaf ops for 5 Primitive instances — if instances went through a different path the op shapes could easily have drifted from a lone node's own).
- **"Is `count<=1` really the identity, or does it just look like one?"** CT-0 compares op ARRAY LENGTH and `hasSwarm`, not just coverage — a coverage-only check could pass even if the compiled program shape changed underneath (extra dead ops, different bound). Op-count equality is the stronger claim and it holds.
- **"Could the mutate-and-restore transform trick leave a node corrupted if an exception fires mid-loop?"** Wrapped in `try/finally`; restoration happens even on a thrown exception. Not independently fault-injection-tested (no CT throws mid-instance), named here as unverified rather than silently assumed safe.
- **"Is the 15.6x-16.2x CT-5 number real, or an artifact of comparing a cold run to a warm one?"** Both paths run after an explicit JIT warm-up call; `RenderSwarm` runs SECOND (a colder-cache disadvantage, if anything) and still measured dramatically faster — the bias, if any, works against the claim being tested, not for it.
- **"Does `interact=false` really forbid ALL cross-instance influence, or just reduce it?"** CT-6 checks for an EXACT 0.000 at the midpoint, not "lower than" — a structural guarantee traced to `DecayOnly` never writing a neighbour cell, not a magnitude threshold that could pass on a lucky parameter choice.
- **"Is the ~200x figure being quietly reinterpreted as 'close enough' to the measured ~16x?"** No — `SWARM-SPEC.md` Part 5 states directly that the figure is NOT reproduced and explains why (different system: toy single-grid fixture vs the real layered Pyre Fire/Fireball sim), rather than blurring the two together.

## Part 5 — Honest scope limits (mirrors `SWARM-SPEC.md` Part 9)

- No native path for Primitive or Bag nodes — both always resolve to Generic. A geometric domain-repeat native
  path is real future work, explicitly not attempted (`SWARM-SPEC.md` Part 4 explains the structural reason:
  Shaper's flat RPN evaluator has no mechanism today for a parent op to re-sample a child at a folded point).
- No real Pyre Fire/Fireball simulation is hosted in Shaper yet — the CT-5 cost measurement uses a toy
  representative sim built for this task, not the system the design doc's 200x figure was measured on.
- The design doc's ~200x figure is not reproduced, only the asymptotic SHAPE it depends on is verified
  (independent cost grows with N, batched cost does not) — stated as a correction, not silently dropped.
- A swarmed node's own "local frame" (read by a parent Bag's box-fold, or a fill-stage subtree anchor) is
  approximated as the node's own base, un-jittered transform — documented, not a precision claim.
- No live editor UI exists for ANY Shaper node yet (Wave 3's authoring window is a later task) — "shown, not
  hidden" is satisfied structurally (`ShaperProgram` fields) and via the audit report; `SWARM-SPEC.md` Part 8
  states the concrete requirements the future window must meet.
- `ShaperSwarmDef.merge.carveStrength` is present (reused whole from `ShaperBlend`) but has no effect, since
  swarm always folds instances with `Add` — a named wart, not a silent one.
- The mutate-and-restore transform technique in the generic wrapper is not fault-injection-tested (Part 4).

## Part 6 — What a reader can trust without re-deriving it

All 9 CTs above are runnable again at any time via `unity.exe command --project-path "D:\UNITY\Laubrary Dev - Shaper" eval_file <a .cs file containing `return Laubrary.Shaper.Editor.ShaperSwarmAudit.RunAll();`>` — nothing here depends on taking this report's word for it.
