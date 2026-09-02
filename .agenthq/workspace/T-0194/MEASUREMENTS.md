# T-0194 — preview performance measurements

All figures are **ms per frame**, measured by a throwaway probe run through the Shaper worktree editor
(`unity command eval_file`, pipeline port 7801) against in-memory `Instantiate` copies of
`Assets/Demos/ShaperDemo/ShaperDemoDoc.asset` — nothing was saved and the asset was never edited.
Canvas 96×152 (the demo document's current, uncommitted authoring state). The probe has been deleted.

## 4-layer, 16-frame document — final committed code (commit 5572f210)

| Scenario | Before (no layer cache) | After (per-layer cache) | Layer-cache hits/misses |
|---|---|---|---|
| Cold render of every frame | 89.8 | 90.8 | 0 / 64 |
| **Toggle a layer off** | **65.2** | **5.2** | 48 / 0 |
| **Change the document background** | **89.8** | **7.6** | 64 / 0 |
| **Edit one dial on one layer** | **89.8** | **26.4** | **48 / 16** |
| Invalidation keying (rebuild + re-key all 16 frames) | n/a | 4.19 ms total | — |
| Pixel parity, cached vs uncached, every frame | — | **SAME** (`3c7c6fe6517c8c85`) | — |

The `48 / 16` on the one-dial row is the load-bearing number: sixteen misses is exactly the one layer that
changed, across sixteen frames, and the other three layers composite from cache. An earlier build of the
keying memoized the layer content hash across renders and scored `64 / 0` here — a stale key making a changed
layer look unchanged — which is why the shipped code re-keys on every render.

## Where one layer's render time goes (96×152, 8 reps) — the Burst question

| Stage | ms per layer per frame |
|---|---|
| `ShaperFillResolver.Resolve` (compile + bake rasters) | 0.08 |
| `ShaperLightCompiler.BindLayer` (light + height compile) | 0.04 |
| **`ShaperFillResolver.PaintTile` (SDF + fill + light)** | **20.89** |
| — of which `ShaperEvaluator.FillTile`, root owner alone | **9.44** |
| `ShaperDocumentRenderer.CompositeDepth` | 0.39 |
| `ShaperFillResolver.Encode` | 0.38 |

So the cold path is `PaintTile`, and the SDF block fill is at least 45% of it on a two-owner layer. Burst is
aimed at the right place — but it is **not shipped here**, and the reason is a hard one rather than a time one:
`ShaperSdf.Evaluate` and `ShaperOps.Combine` call `Mathf.Pow / Sin / Cos / Atan2 / Sqrt` in the inner loop, and
Burst does not promise bit-identical transcendentals against Mono. The task's own contract is "determinism must
be bit-identical to the managed path", and a dual path cannot meet it for any shape that uses those (NGon, Star,
every smooth blend). That leaves two honest options, both of which are the owner's call, not an implementer's:
run **everything** through Burst and accept a one-time re-bake of existing sheets, or restrict the Burst path to
programs whose ops use no transcendental primitive and accept that it helps only some documents. This matters
more now than before T-0194, because the layer cache's correctness rests on a cache hit being byte-identical to
a miss.

## Demo document (its ArcBurst layer removed — see the pre-existing crash below)

| Scenario | Before | After | hits/misses |
|---|---|---|---|
| Cold render of every frame | 22.3 | 22.2 | 0 / 16 |
| Change the document background | 22.3 | 2.6 | 16 / 0 |
| Pixel parity, cached vs uncached | — | **SAME** (`927d3f62417c8c85`) | — |

## Two things found that are NOT T-0194's, and are not fixed here

1. **The demo document cannot render at all.** `ShaperDocumentRenderer.RenderFrame(ShaperDemoDoc, any frame)`
   throws `IndexOutOfRangeException` inside `Laubrary.Pyre.PyreSupersample.Downsample`
   (`Runtime/Pyre/PyreSupersample.cs:84`), reached from `ArcBurstForm.Render` →
   `PyreFormCompositeSource.Render` → `ShaperCompiler.EmitComposite`. Reproduced on the asset itself and on a
   clone, at HEAD, and it is in the console from before this task started. The document's working copy is
   currently 96×**152** with `pixelSize 2.49` (uncommitted authoring), so a non-square canvas fed to T-0191's
   derived bake box is the first thing to test. Every measurement above therefore removes that one layer.
2. **`Assets/Demos/ShaperDemo/ShaperDemoDoc.asset` is modified and uncommitted in the worktree**, and one of
   its layers now has `height: rid: -2` — a nulled `SerializeReference`. Not touched by this task.

## The GUI editor for this worktree will not start any more

**The Shaper worktree's GUI editor is down and I could not bring it back.** Batchmode works; the GUI does not.

- Four consecutive GUI crashes with an identical native stack —
  `MonoManager::ReloadAssembly → UnloadDomain → APIUpdating::Caching::Reset → malloc_internal` — one of them
  before any project script had run. The compile is clean every time (`*** Tundra build success`, 2361
  evaluated, zero errors, only pre-existing obsolete-API warnings).
- After those, GUI launches stopped even producing a log: `Unity.exe -projectPath … -logFile …` exits with
  code 0 in under 25 s and writes nothing. `-useHub -hubIPC` made no difference.
- Tried and did not help: deleting `Library/APIUpdater`, `Library/Bee`, `Library/ScriptAssemblies` and `Temp`;
  `-disable-assembly-updater`; clearing `Temp/UnityLockfile`. 22 GB of RAM was free throughout, so it is not
  exhaustion.
- One suspect worth checking: every crash log carries
  `Duplicate assembly 'System.Runtime.CompilerServices.Unsafe.dll' with different versions detected` between
  `com.unity.collections`' test assembly (6.0.0.0) and `com.unity.pipeline`'s CodeAnalysis copy (4.0.4.0) —
  `com.unity.pipeline` being the package the Unity CLI itself needs. I did not touch either.
- One GUI startup earlier in the task was blocked by a modal **"Recovering Scene Backups"** dialog left by the
  first crash; I answered **Yes** (recover — it loads into memory and writes nothing) rather than discard.

**Every number above was produced in BATCHMODE** (`-batchmode -quit -executeMethod`) with a temporary probe at
`Assets/Temp/T0194_Probe.cs`, since deleted; its raw output is `probe-output-final.txt` beside this file.
Consequence: there are **no screenshots and no by-eye verification** of this task. Nobody has seen the strip
not flash, or dragged a dial with the cache in place — that is bucket three in the handover.
