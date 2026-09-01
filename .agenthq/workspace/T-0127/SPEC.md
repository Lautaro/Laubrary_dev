# T-0127 — Solids: wire fill height into surface normal (real relief shading), SPEC

Built in the separate worktree `D:\UNITY\Laubrary Dev - Shaper` (port 7801), **not** the primary
`D:\UNITY\Laubrary Dev` editor. Uncommitted, per the standing convention for every Shaper Wave task.

Continues directly from T-0110 (indexed-strip fill), T-0111 (Tapestry `HeightField` presets), T-0114
(universal effects) and T-0115 (caching) — specifically from the gap those tasks left on record: a fill's
sampled height accumulates into `ShaperFillBuffers.height` and, per `ShaperNormals.cs`'s own LR-3.3/LR-3.4 doc
comments, is "consumed by nothing." T-0124 (Lathe mesh relief) is explicitly a different, unrelated approach —
real vertex displacement on a Lathe mesh — and nothing here reuses or references its code.

## Part 0 — the boundary this task does not cross

Agreed with the owner before any code was written, restated here because it is the one rule every other
decision below serves: this task can only ever make a Solid look carved under raking light. It can never change
what a Solid's own silhouette IS. Solids' coverage and edge distance stay entirely the product of its own
closed-form facet/sphere/plane geometry (`ShaperSolids.SampleFacet`/`SampleOrb`/`SampleRing`) — a fill is
architecturally outside that system for all five fill kinds, exactly as T-0110 found, and this task does not
open that door. Every change below touches only the **direction** written into the normal sheet, never
`cov`/`dist`/`line`. Verified, not just asserted — see VERIFICATION.md leg 2.

## Part 1 — why LR-3.3's refusal does not reopen, and why it doesn't apply here anyway

`ShaperNormals.cs`'s LR-3.3 forbids screen-space DIFFERENCING OF AN ACCUMULATED BUFFER
(`ShaperFillBuffers.height`, written by `buf.height[i] += buf.heightDelta[i] * ce` in `PaintTile`), for two
measured reasons: every value in that buffer is fill-authored and already blended across every owner in the
tree, so differencing it would describe the *composited paint's* bumps rather than any one surface's; and
`PaintTile`'s tile decomposition has no apron, so growing one to support differencing would cost 1.80x the
whole fill stage on every tile, paid for a normal that would still be describing the wrong thing.

That objection is about the BUFFER. It does not apply to `ShaperFillOps.Sample` itself, which for
`ShaperFillKind.HeightField` is a **pure function of a canvas point**: `Anchor(op, x, y, ...)` (a fixed 2×2
affine map compiled from the fill's own authored dials) plus one nearest-texel lookup into `bulk` — no read of
`ShaperFillBuffers.height`, no read of any other sample's own output, no dependency on tile decomposition at
all. Central-differencing THAT function at the sample point is exactly the licence LR-3.2 already grants
`ShaperNormals.FillProfile` for `∇d` (a central difference of `ShaperEvaluator.Distance`, itself a pure function
of a canvas point) — not the buffer read LR-3.3 forbids. This is the whole of why a HeightField fill can be
wired in cheaply where a general fix for every fill kind could not be, and it is why the fix is scoped to
Solids specifically: only `ShaperSolids.FillTile` has the analytic base normal this perturbation composes onto,
and only a HeightField fill's height is cheap to re-sample four extra times per covered pixel.

**IndexedStrip is the other height-emitting fill kind, and it is deliberately excluded.** Its height depends on
`edge` — the edge distance to the strip's own reach — and getting an accurate edge distance at four neighbour
taps means re-running `SampleFacet`/`SampleOrb`/`SampleRing`'s silhouette walk (iterating every triangle or
silhouette segment), which is not a cheap pure-function evaluation the way a texel lookup is. That is the same
cost argument LR-3.3 already made, applied to a different sheet. A general fix for every fill kind was
explicitly out of scope (task body, "not a general-purpose fix for all five fill kinds") and this is the
concrete reason a second kind was not folded in for free.

## Part 2 — the fix, and exactly where it plugs in

`Runtime/Shaper/ShaperSolids.cs`, `FillTile` (`:275`'s `Published` sheet, the per-sample loop the task pointed
at): gained two optional trailing parameters, `ShaperFillProgram fillProgram = null` and
`ShaperNormalOp normalOp = default`. Every existing call site — there is exactly one, `ShaperFillResolver.cs`'s
`PaintTile` step 1 — compiles and behaves bit-for-bit unchanged if it doesn't pass them (a `default`
`ShaperNormalOp` has `slopeGain == 0`, which the perturbation block treats as an explicit no-op gate). The one
real call site now passes `ow.fill` (the Solids node's OWN compiled fill — the same object T-0106 already
routes into `ShaperFillOps.FillTile` for albedo) and `scene.normalOp[o]` (the layer's compiled normal-provider
dials, already populated for every owner including a Solids one, just previously unused by it).

Inside `FillTile`'s per-sample loop, immediately after the `switch` that calls `SampleFacet`/`SampleOrb`/
`SampleRing` and writes the analytic `(nx, ny, nz)`, a new block runs only when `cov > 0f` and the node's own
fill is `ShaperFillKind.HeightField` with a real field bound (`heightFieldOffset >= 0`):

1. Central-difference `ShaperFillOps.Sample`'s `height` output at `(cx ± hs, cy)` / `(cx, cy ± hs)`, the same
   `hs = max(1e-3, 0.25 * pixelSize)` step `FillProfile` already uses, giving `dhdx`/`dhdy`.
2. Perturb the analytic base: `vx = nx − slopeGain·dhdx`, `vy = ny − slopeGain·dhdy`, `vz = nz` — the SAME
   construction `FillProfile` uses (`vx = −slopeGain·dhdx`, etc.), generalised from an implicit flat `(0, 0,
   nzBase)` base to THIS facet's own already-correct analytic normal. `nzBase`/`reflectionFlatten` are
   deliberately NOT reused: they describe a flat base plane's Z, which has no meaning once the facet already
   has a real, non-trivial Z of its own from the geometry.
3. Renormalise; on the LR-3.5 degenerate case (`len` too small or non-finite) leave `(nx, ny, nz)` exactly as
   the switch above wrote it — it was already a valid unit vector, so there is nothing to fall back to.

`slopeGain` is read from the SAME per-layer `ShaperNormalOp` dial `FillProfile`'s `Profile` case already uses
(BC-3.6), reused rather than reinvented so a HeightField fill tilts a Solids facet by the same sensitivity it
would tilt an extrusion's base plane by — this is the concrete answer to "does it interact correctly with the
light rig's existing per-layer response."

## Part 3 — what did NOT change

- `ShaperSolids.SampleFacet`/`SampleOrb`/`SampleRing` — untouched. Coverage, edge distance and the base normal
  are exactly the closed-form values they always were.
- `ShaperFillOps`/`ShaperFillResolver`'s existing height-accumulation path (`buf.heightDelta`/`buf.height`) —
  untouched. This fix does not read that buffer at all; it calls `ShaperFillOps.Sample` directly, bypassing it.
- Every other generator, and every fill kind but `HeightField` on a Solids node — unperturbed, by construction
  (the gate on `fop.kind == HeightField && heightFieldOffset >= 0 && slopeGain != 0`). Measured in
  VERIFICATION.md leg 4.
- `ShaperNormals.cs` itself, structurally — LT-1b (unchanged, still passing) confirms `ShaperSolids`' IL still
  touches no compiled-light-rig type: Solids still cannot see a light, an ambient or a response block directly.
  This fix only gives it access to ITS OWN node's fill and the layer's normal dials, both of which it already
  received as ordinary Wave-2 plumbing.

## Part 4 — files

**Modified:** `Runtime/Shaper/ShaperSolids.cs` (the perturbation block + doc comments),
`Runtime/Shaper/ShaperFillResolver.cs` (the one call site, passing `ow.fill`/`scene.normalOp[o]`),
`Runtime/Shaper/ShaperNormals.cs` (LR-3.4's doc comment, updated to record the exception rather than restating
a now-partially-superseded blanket claim), `Editor/Shaper/ShaperLightAudit.cs` (new `LT24_*` tests — see
VERIFICATION.md).

**New:** none — no new files. This is a narrow, scoped addition to two existing runtime files and one
existing test file, not a new subsystem.

Full measured results: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0127\VERIFICATION.md`.
