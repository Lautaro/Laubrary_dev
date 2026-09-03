# The Ring Blast's first point — Escalated Debug (T-0216, 2026-09-03)

T-0215 measured a Ring Blast's immediate, delay-0 spawn landing about half a ring-spacing from where the recipe's own placement formula said it should, while its four staggered points matched bit-for-bit. It was reproduced twice and left un-root-caused.

**It is real, it is not a placement bug, and it is fixed.** The point is spawned in exactly the right place and is then moved one frame before anything can see it — a head start its four neighbours do not get.

## Phase 1 — research

- `SpawnFormationRunner.Fire` resolves the formation once, then spawns every point whose delay is ≤ 0 **inline, in the calling frame**, and hands the rest to a `WaitForSeconds` coroutine.
- Every point, immediate or staggered, gets its Trajectory applied at spawn, which registers its transform with `ChunkModuleRunner` for per-frame integration.
- Unity resumes a `WaitForSeconds` coroutine AFTER every `Update` in the frame. A component added mid-frame may have its `Update` called in that same frame.
- So the two paths are not symmetric: the inline point can be integrated in the frame it was born, the coroutine points cannot.

## Phase 2 — proof of control

Five predictions, deliberately overlapping so they triangulate rather than merely agree. All were run in Play mode against a runtime CLONE of the Ring Blast recipe, with each spawn recorded in `LateUpdate` (after both the burst's Update and the runner's) together with how many frames old the burst was.

| # | prediction | result |
|---|---|---|
| R1 | Observing 0.16 s late reproduces a large offset on the delay-0 point and none on the later ones | **reproduced** — point 0 off by 0.334 units, points 1–4 exact |
| R3 | The offset scales with how late the observation is | **reproduced** — 0.067 at one frame, 0.334 at ~0.16 s, 0.574 in T-0215's own slower poll |
| N1 | Recording every spawn in the frame it happens shows all five exact | **half failed, and that is the finding** — points 1–4 exact, point 0 off by 0.067 at frame 0 |
| N2 | With the Trajectory switched off, point 0 is exact even at frame 0 | **held** — `(-6.802965, -3.716902)`, bit-for-bit the resolver's own answer |
| N3 | The staggered points, observed with the same lag, are off too | **held** — with a 0.16 s lag point 1 is off as well; index 0 is not special, being early is |

N2 is the one that settles it: the placement is exact. N1's 0.067 is one frame of flight at the launch speed the Trajectory drew.

## Phase 3 — theory and Phase 4 — the fix

**Cause.** `ChunkModuleRunner.Update` integrated a motion entry in the frame it was registered. A formation's immediate point is registered from inside somebody's `Update`, so it was moved that same frame; a staggered point is registered from a coroutine, which runs after every `Update`, so it was not. One point of a pattern therefore flew for one frame longer than its neighbours at the same age — and whether it did at all rested on Unity's execution order for a component added mid-frame, which is not a thing a burst's geometry should depend on.

**Fix.** A motion entry now records the frame it was handed over, and the runner skips it for that frame. Every driven transform starts moving on its first full frame — which is what the coroutine path already had, and what the editor preview assumes when it integrates from the moment of firing.

Six lines in `ChunkModuleRunner`. It changes nothing else: the entry is still dropped the moment its target dies or is deactivated, still ages by real `deltaTime`, still integrates gravity → drag → move in that order.

## Phase 5 — re-test

The same run, on the fixed build:

```
predict i=0 t=0.0000 pos=(-6.802965,-3.716902)
saw     i=0 t=0.0200 pos=(-6.802965,-3.716902) frames=0
saw     i=1        pos=(-6.617278,-2.599232) frames=1
saw     i=2        pos=(-7.291456,-1.829654) frames=7
saw     i=3        pos=(-8.384436,-1.605056) frames=12
saw     i=4        pos=(-9.224371,-2.341525) frames=19
```

All five match `SpawnFormation.Resolve()` bit-for-bit, the delay-0 point included. No prediction reproduces the anomaly any more.

## Phase 6 — cleanup and smell assessment

The probe (a temporary observer component and its driver) is deleted. Nothing it touched was saved: it fired a runtime clone of the recipe, and the demo asset reads back not dirty.

**Is this fix painting over something larger? Partly — and the larger thing is worth naming.**

The local defect is genuine and the fix belongs where it is: "flight starts on the next frame" is a property of the runner, not of any caller, and every caller now gets it without knowing about it.

But the reason the two paths could differ at all is that **`SpawnFormationRunner.Fire` has two dispatch paths for what is one idea.** A delay of zero is not a different kind of event from a delay of 0.08 s; it is the same event at t = 0. The inline branch exists as an optimisation ("staggerSeconds == 0 costs nothing at all — no runner is created"), and it bought a real asymmetry: different frame semantics, and a different order of draws off the blast's own random stream, since immediates are spawned in index order and the rest in delay order. That second consequence is still live and is filed separately — a pattern's pool pick, rotation and scale depend on the firing order rather than on the point index, so the preview (which hashes from the seed and the index, per design decision 9) and the burst can legitimately disagree about which alternate lands where.

Two paths for one idea is the structural weakness. Collapsing them — always going through the scheduler, with a zero wait resolving immediately — would remove the class rather than this instance, but it is a behavioural change to the spawn path and belongs in its own task, not smuggled into an audit. **Fix kept.**
