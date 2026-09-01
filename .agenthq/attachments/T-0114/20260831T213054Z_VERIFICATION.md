# T-0114 — VERIFICATION

Universal effects for Shaper — sheets, padded buffers, an explicit pre/post-composite stage — built in the
separate worktree `D:\UNITY\Laubrary Dev - Shaper` (branch `feat/shaper`, editor on port 7801, **not** the
primary `D:\UNITY\Laubrary Dev` editor). This is the honest account of what was built, what was measured, and
what is explicitly NOT yet true. Design document: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0114\SPEC.md`.

Contact sheet: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0114\t0114-contact-sheet.png` (6 cells, eyeballed
personally before this report was written).

## Compile state

Reflection/compile checks run against the live Shaper editor (port 7801) via `unity command --project-path
"D:\UNITY\Laubrary Dev - Shaper" recompile` / `recompile_status`, and via Coplay `check_compile_errors` after
every edit:

- Final state: `{"status":"completed","failed":false,"errors":[]}` — clean.
- One real compile error was hit and fixed during the build (not a logic bug): `ShaperEffectStageRunner.cs`
  referenced `SpriteFxBurst.MakePixel`, but `MakePixel` is declared on `SfxKernels`, a different class in the
  same file (`SpriteFxBurst.cs`) — fixed by correcting the reference; doc-comment mentions of the wrong class
  name were fixed alongside it for accuracy (they would only have produced XML-doc cref warnings, not build
  errors, but were wrong and are now fixed).

## New files (uncommitted, in `Assets/Packages/Laubrary/Runtime/PyreShaper/`)

- `ShaperEffectContract.cs` — `ShaperEffectStage`, `ShaperEffectPortability`, `ShaperEffectCatalogEntry`,
  `ShaperEffectCatalog` (the 41-effect table + `IsAvailable` gate).
- `ShaperEffectPicture.cs` — `RequiredPadding`, `RenderPadded` (padded-buffer render + the picture-rect call,
  wired via reflection to `PostModifier.SetPicture`).
- `ShaperEffectStageRunner.cs` — `RunPreComposite`/`RunPostComposite` over N swarm instances of an
  `IShaperCompositeSource`.

## Modified files

- `Assets/Packages/Laubrary/Runtime/SpriteFx/SpriteFxRecolor.cs` — fixed the hardcoded `crossFrac = 0f` bug in
  `ApplyManaged` (Part 5 of SPEC.md).
- `Assets/Packages/Laubrary/Runtime/SpriteFx/SpriteFxModifiers.cs` — rewrote `PixelInfo.crossFrac`'s doc comment
  to state the real two-concepts-plus-one-bug taxonomy (Part 5 of SPEC.md). No behavioural change in this file.

## What each check actually proves, and what it does not

**CATALOG check** (`ShaperEffectCatalog.All.Length` + per-bucket counts, read back live from the compiled
assembly, not asserted from the source file) — proves the classification in SPEC.md Part 2 is what actually
shipped, not what was drafted:

```
CATALOG total=41 (expected 41) bufferFree=20 bufferPadded=7 needsSheets=13 stuck=1 buffer-only-sum=27
```

Matches SPEC.md Part 2's table exactly (11+9=20 free, 7 padded, 13 sheet-needing, 1 stuck, 27 buffer-only total).
**What it does not prove:** that every one of the 41 entries' `typeName` string actually matches a real class
that still exists with that exact base type — the catalog is data (strings), not compile-time type references
(deliberately, per the file's own doc comment on why), so a future rename of an effect class would not fail this
check; it would need a separate reflection-based cross-check against `typeof(...)` for all 41, which this task
did not build (a real, named gap, not hidden).

**GATE check** (`ShaperEffectCatalog.IsAvailable` against two different published sets) — proves the
declared/greyed-out-with-a-reason contract works, not just exists:

```
GATE TintModifier coverage-only=False ("Needs edge distance, which this generator does not publish.")
     shippedShapeEngine=True ("")
```

**What it does not prove:** that a real generator's live `ShaperQuantitySet` is actually threaded through to
this gate anywhere in an authored/serialized Shaper node yet — no such wiring was built (see SPEC.md Part 6);
this proves the gate function itself is correct given a set, not that anything calls it end-to-end today.

**PADDING check** — proves the padded buffer is sized correctly and the picture-rect call is genuinely wired,
via a real regression it caught mid-build:

```
Attempt 1 (before the Prepare fix): pad=8 paddedSize=112x112 (unpadded 96x96) bloomPixelsInMargin=0
Attempt 2 (after adding the missing post.Prepare(evalRaw) call): marginHit=772
```

The first number (`0`) was **wrong and caught, not accepted** — `BloomModifier.Apply` silently no-oped because
its tunable fields are computed in `Prepare`, which `RenderPadded`'s first draft never called. Confirmed the
form itself has bright pixels near its own edge (`edgeBrightPixels=11`, checked independently) before concluding
the 0 was a real bug rather than "nothing to bloom." Fixed in `ShaperEffectPicture.cs`, re-measured: 772 margin
pixels lit by the glow, and the contact sheet's last cell shows it visually (a bright halo well past the small
orb's own silhouette). **What it does not prove:** that `SetPicture`'s picture-rect VALUES are read correctly by
every one of the 10 Post modifiers that could use them (`OutwardReachPx`-bearing ones especially) — only Bloom
was exercised; the reflection call itself was confirmed to resolve (`SetPicture=FOUND Void SetPicture(Int32,
Int32, Int32, Int32)`) and Bloom's own behaviour change (0 → 772) is the evidence it's actually being invoked
with real values, not just resolving.

**SWARM pre/post-composite check** — proves the two-stage distinction is real, numerically and visually, on an
overlapping swarm (two `OrbForm` instances, one offset to overlap the other, `WipeModifier` at a 50% threshold
run at each stage):

```
SWARM canvas=96x64 differingPixels=122 / 6144 preOpaque=705 postOpaque=690
```

122 of 6144 canvas pixels (~2%) differ between the pre- and post-composite runs of the identical effect on the
identical instances — a real, nonzero, measured difference, not asserted from the architecture alone. The
`t0114_swarm_diff.png` cell in the contact sheet renders those 122 pixels in magenta against the rest in dark
grey — visible, not just numeric. **What it does not prove:** that this specific magnitude (122 pixels, ~2%) is
representative of every effect/overlap combination — a starker effect (e.g. Posterize, which quantises colour
non-linearly) or a larger overlap would likely show a bigger difference; this is one measured data point
establishing the distinction is real, not a claim about its typical size. It also does not prove anything about
`ShaperCompiler`'s actual production swarm fold (SDF-domain `ShaperBlend`) — see SPEC.md Part 6's own note that
this runner uses plain alpha Over as an honest proxy, not the production fold rule.

## Honest scope-limits list (full version in SPEC.md Part 6, repeated here for the verification record)

- Only Composite-sourced (baked-raster) Shaper nodes get this pipeline; Primitive/Bag stay untouched (no
  `Color32[]` buffer exists there at all).
- The pre/post-composite runner is standalone, not hooked into `ShaperCompiler`'s production compile pass.
- Only 3 of 41 effects (Wipe, Bloom, Tint) were exercised with a live render/gate check; the rest are classified
  (Part 2) but not individually run.
- `RenderPadded` resolves authored dials to their static value only (same posture `PyreFormCompositeSource`
  already takes for a hosted `PyreForm`).
- Inferno/ForkBlast's `crossFrac` reuse is documented, not split into its own field — a real behavioural change
  to shipped content was judged out of this task's scope, not an oversight.
- The 41-entry catalog is name-string data, not compile-time-checked against the real classes' current base
  types — a future rename would silently desync it.

## Adversarial self-review — what I tried to break, and what happened

- **Assumed padding would obviously work, tested it, and it didn't (Part above).** The clean compile and a
  first "it printed some numbers" pass were not treated as sufficient — the specific number (0 margin pixels)
  was checked against an independent expectation (edge-bright-pixel count) before either accepting or rejecting
  it, which is what surfaced the missing `Prepare` call.
- **Checked whether the "5 crossFrac meanings" framing survived contact with the actual read sites.** It did
  not, cleanly — narrowing "5 sites" to "2 legitimate + 2 deliberate-debt + 1 bug" required actually grepping
  every READ of the field (not just every construction), which showed only `TintModifier` and
  `VoronoiCrackModifier` ever read it as a struct field. This also independently confirmed the bucket-(iii)
  "Tint" ambiguity resolution in SPEC.md Part 2, rather than needing to trust it.
- **Did not force the crossFrac unification the task's phrasing invites ("into one consistent, correctly-named
  concept").** Chose the task's own explicitly-sanctioned alternative ("or, if truly irreconcilable, document
  exactly why not") for the two deliberate-reuse sites, because forcing it would have meant either changing
  Inferno/ForkBlast's shipped visual output or a real audit-every-consumer refactor, neither scoped here.
- **Did not claim the pre/post-composite proof is "the real swarm pipeline."** It is a standalone, honestly-
  labelled proxy using the same fold law Pyre's particle compositor already uses — checked this claim against
  `ShaperSwarmDef.cs`'s own doc comments (Generic implementation unions via `ShaperBlend`, not Over-compositing)
  before writing SPEC.md Part 4/6, rather than assuming the proxy matched production.
