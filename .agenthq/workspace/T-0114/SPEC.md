# T-0114 — Universal effects: sheets, padded buffers, explicit pre/post-composite stage

Built in the separate worktree `D:\UNITY\Laubrary Dev - Shaper` (branch `feat/shaper`, editor on port 7801, **not** the primary `D:\UNITY\Laubrary Dev` editor). Changes are left **uncommitted** in that worktree for review, same as every prior Wave 3 task.

## Part 1 — the terrain, confirmed before building anything

Three things were true going in, checked directly against the shipped code rather than assumed from the digest:

- **The picture-rect call exists and is genuinely dormant in Pyre.** `PostModifier.SetPicture(int sw, int sh, int px, int py)` (`Assets/Packages/Laubrary/Runtime/SpriteFx/SpriteFxModifiers.cs:1802-1803`) sets `pictureW/pictureH/padX/padY`, and its one pre-existing caller anywhere in the project is `SpriteFxBurst.cs:656` (the standalone SpriteFx Stack's own post-pass driver). `Runtime/Pyre/PyreRenderer.cs` never calls it — confirmed by grep, not inferred — which matches the digest's claim exactly.
- **`SetPicture` is `internal` to `Laubrary.SpriteFx`, and the bridge assembly has no grant.** `SpriteFx/AssemblyInfo.cs` grants `InternalsVisibleTo` to `com.Lautaro-Arino.Laubrary.Pyre` only. `Runtime/PyreShaper/` (asmdef `com.Lautaro-Arino.Laubrary.Pyre.Shaper`) is a different assembly with no such grant — and, tellingly, `PyreRenderer.cs` itself drives the sibling hooks (`SetLife`/`SetSeed`/`SetFrameIndex`) by reflection rather than relying on its own grant (`PyreRenderer.cs:1372-1391`). This task's picture-rect wiring follows that exact established pattern (cached `MethodInfo`, reflection call) rather than widening SpriteFx's public API or adding a second assembly grant for one call.
- **Shaper's only raster/pixel buffer today comes from Composite-sourced nodes.** Primitive/Bag nodes stay in the SDF/coverage domain end to end (`ShaperEvaluator`, `ShaperResolve`) and never produce a `Color32[]`. The one place a `Color32[]` picture exists in the new Shaper stack is `IShaperCompositeSource.Render(...)` (T-0112, `Runtime/Shaper/ShaperCompositeDef.cs`), baked once at compile time into `ShaperCompiledComposite`. This is therefore where this task's padding/picture-rect/pre-post-composite work is built — a real, scoped, honestly-stated boundary (Part 6).

## Part 2 — the 41-effect catalog, classified with real counts

Method: every `class X : Y` under `Runtime/SpriteFx/SpriteFxModifiers.cs` and `SpriteFxSimulationModifiers.cs` whose base resolves to `GeometryModifier`/`PixelModifier`/`PostModifier`/`EdgeModifier`/`SimulationModifier`, grep-enumerated by name (not sampled). Total: **17 Geometry + 12 Pixel + 10 Post + 1 Edge + 1 Simulation = 41**, matching T-0098 Investigator E's independent count exactly.

Cross-checked against T-0098 Investigator I's four buckets (`I-effect-universality.md` §7 table) by matching each bucket's named members to the real shipped class names. They reconcile **exactly**, with one real ambiguity resolved: bucket (iii)'s "Tint" is `TintModifier` (a directional/shape-aware tint reading `PixelInfo.crossFrac`, confirmed by grep — see Part 5) — **not** `ColorTintModifier`, which is bucket (i)'s flat "Colour tint" and stays buffer-only. Confirmed independently by checking who actually reads `PixelInfo.crossFrac` as a struct field repo-wide: only `TintModifier` (`SpriteFxModifiers.cs:707`) and `VoronoiCrackModifier` (`:1098`), which is exactly the Pixel half of bucket (iii).

| Bucket | Count | Members |
|---|---|---|
| **Buffer-only, free, inherently post-composite** (whole-picture passes) | **11** | 10 Post (Dissolve, Bloom, Outline, ChromaticAberration, BallisticShockwave, Fuse, EdgeSmooth, DropShadow, Kaleidoscope, Relight) + 1 Simulation (PixelFluid) |
| **Buffer-only, free, pre-composite-native but portable to post** | **9** | Contrast, Brightness, Saturation, Posterize, ColorTint, ColorReplace, ColorRemap, OrderedDither, Wipe (all Pixel) |
| **Buffer-only via padding, at a resample cost** | **7** | Skew, Scale, Rotate, Wobble, CurlProgress, Smudge, PinWarp (all Geometry) |
| **Needs sheets** (`ShaperQuantitySet.ShippedShapeEngine` = Coverage \| EdgeDistance) | **13** | 10 Geometry (SunburstWobble, RingWave, PointBlast, Sunburst, Turbulence, Curl, Profile, PulseRings, Sphere, Ground) + 3 Pixel (Tint, VoronoiCrack, LayerDissolve) |
| **Genuinely stuck** | **1** | EdgeWarp — needs a per-angle boundary test; no reconstruction from a merged buffer, and currently hostless anywhere in the project |

**41 total, 27 buffer-only + 13 sheet-needing + 1 stuck.** This is inside the task body's approximate "13 to 20 need sheets" range, at the floor — measured, not assumed. The classification lives as executable data, not just this table, in `Runtime/PyreShaper/ShaperEffectContract.cs` (`ShaperEffectCatalog.All`), with a compile-time-checkable `ExpectedTotal = 41` and a runtime probe (Part 7) that confirms `ShaperEffectCatalog.All.Length == 41` and the exact per-bucket counts above against the live assembly.

**Required sheets, mapped onto the existing contract rather than invented.** The "shape-local coordinate + shape seed" the 13 need (I-effect-universality.md §7) maps directly onto `ShaperQuantitySet.ShippedShapeEngine` (Coverage | EdgeDistance) — the exact set the Wave 2 fill contract (T-0106) already defines and a Primitive/height-carrying node already publishes today (`ShaperFillContract.cs`). A Composite-sourced node publishes Coverage (its render's alpha channel, `ShaperCompiledComposite.coverage`) but not EdgeDistance — so today these 13 effects are honestly available on Primitive/Bag/height nodes and honestly gated on a bare Composite node, not silently broken. Verified live (Part 7): `ShaperEffectCatalog.IsAvailable` returns `false` with reason `"Needs edge distance, which this generator does not publish."` against Coverage-only, and `true` against `ShippedShapeEngine`.

## Part 3 — the padded buffer + the picture-rect call, wired for real

`Runtime/PyreShaper/ShaperEffectPicture.cs`:

- `RequiredPadding(PyreModifier[] chain)` sums each modifier's own `OutwardReachPx()` — the same dial the standalone SpriteFx Stack already uses per-modifier, applied here to a chain the same way.
- `RenderPadded(source, width, height, padX, padY, phase01, seed, postChain, out paddedW, out paddedH)` renders the composite source at its native size, blits it into a padded `Color32[]` (transparent margin), then runs every `PostModifier` in `postChain`: `Prepare` → **`SetPicture` (the dormant call, invoked for real, via reflection)** → `Apply`, mirroring `SpriteFxBurst.cs:656`'s existing call site.
- With `padX == padY == 0` this is byte-identical to running the chain directly — the same "no padding, no behaviour change" contract `SfxKernels.MakePixel`'s own padded overload promises.

**A real bug was caught and fixed during verification, not left in.** The first working version of `RenderPadded` called `post.Apply(...)` without first calling `post.Prepare(...)`. Every `PostModifier`'s tunable fields (e.g. `BloomModifier.inten`/`thr`) are computed inside `Prepare` and default to `0`/`false`-equivalent otherwise, so `Apply` silently no-opped — `BloomModifier` compiled and ran without error but produced **zero** pixels in the padding margin even for a form with bright pixels at its own edge. Caught by the adversarial check in Part 7 (an edge-hugging Orb, expected bloom in the margin, measured `marginHit = 0`), fixed by adding the missing `Prepare` call, re-verified (`marginHit = 772`). This is exactly why Part 7's checks exist rather than trusting a clean compile.

## Part 4 — the pre-composite / post-composite pipeline, built as two real stages

`Runtime/PyreShaper/ShaperEffectContract.cs` declares `ShaperEffectStage { PreComposite, PostComposite }` (append-only int, same posture as `ShaperSwarmImplementation`/`ShaperCompositeReason`). This is **not** collapsed into one stage — an earlier design report's claim that doing so was "zero behavioural cost" is explicitly withdrawn (T-0098 digest, error E2), and this task does not re-derive or re-adopt it.

`Runtime/PyreShaper/ShaperEffectStageRunner.cs` runs one `PixelModifier` at an explicit stage across N swarm instances of an `IShaperCompositeSource`:

- `RunPreComposite`: renders each instance's own buffer, applies the effect to **each instance separately** (its native per-particle semantic, matching `PyreRenderer.ApplyPix`'s pre-composite convention), then Over-composites all instances into one canvas.
- `RunPostComposite`: Over-composites all instances **first** (the identical fold rule), then applies the effect **once** to the finished canvas.

**Honest scope limit, stated once here and repeated in Part 6:** this runner does not hook into `ShaperCompiler`'s production swarm-compile pass, which folds Composite-swarm instances through `ShaperBlend` in the SDF/pseudo-distance domain, not literal Over-compositing. The fold rule used here is plain alpha Over — the same rule `PyreRenderer.cs:3089-3092` already uses for particle compositing — so this is a real, named proxy for "instances fold into one picture," not a claim about the production swarm compiler's own fold rule. Wiring the stage split into that production pass is separate, real surgery this task does not take on.

## Part 5 — the "how far across this shape am I" (crossFrac) audit

Every `new PixelInfo(...)` construction site was enumerated (`grep -rn "new PixelInfo("`), and every read of `PixelInfo.crossFrac` as a struct field (`grep -rn "\.crossFrac\b"`, narrowed from the wider "5 sites" framing since most matches were the field's own declaration/assignment, not a read).

**The finding: two of the five sites carry the SAME legitimate concept, two carry a genuinely different one by deliberate design, and one is a plain bug.**

| Site | What it is | Verdict |
|---|---|---|
| `PyreRenderer.cs:1346` (fed by ~15 raster sites) | Geometric shape-local progress (centre→edge), computed from the shape's own analytic centre/radius | **Canonical meaning.** Unchanged. |
| `SpriteFxBurst.cs` `SfxKernels.MakePixel`'s padded overload | The SAME geometric concept, computed from the **picture rect** (`srcW/srcH/padX/padY`) when there is no analytic shape to ask — its own doc comment already explains why: "the buffer supplies the INDEXING and the source rect supplies the MEANING." | **A correct, documented fallback for the same concept — not a second meaning.** Unchanged. |
| `PyreInferno.cs:820` | The form's own HEAT value, deliberately placed in this slot (own comment: "crossFrac = the heat value") so existing Tint/cross-gradient-authored content responds to heat without a new dial | **Named technical debt, not an accident.** Left unchanged — splitting it into its own field is real, separate surgery (auditing every Inferno-hosted `PixelModifier` stack for reliance on the reuse) and is named as future work rather than silently changed here, which would have altered shipped visual behaviour. |
| `PyreForkBlast.cs:394` | The form's own fork/puff parameter, same deliberate-reuse pattern as Inferno | **Same verdict as Inferno.** Unchanged, for the same reason. |
| `SpriteFxRecolor.cs:167` (`ApplyManaged`) | Hardcoded `0f`, no comment, no justification | **A genuine bug.** **Fixed** — now computes the same picture-local formula `SfxKernels.MakePixel` uses (this host has no padding concept, so the "picture" is simply the whole `W×H` buffer), restoring `TintModifier`'s cross-gradient and `VoronoiCrackModifier`'s spread mask in this host. |

`PixelInfo.crossFrac`'s doc comment (`SpriteFxModifiers.cs:645-668`) was rewritten to state this taxonomy explicitly, so a future reader sees the real two-concepts-plus-one-bug picture instead of five unexplained call sites. **Not force-unified into one field** — that would require either changing Inferno/ForkBlast's shipped visual behaviour or a real field-split refactor auditing every consumer, neither of which this task's honest scope covers; the task's own escape hatch ("or, if truly irreconcilable, document exactly why not") is the outcome taken here for the two deliberate-reuse sites, while the one real bug is fixed outright.

## Part 6 — honest scope limits (read before treating anything above as more than it is)

- **Only Composite-sourced (baked-raster) nodes get this task's effects pipeline.** Primitive/Bag nodes have no `Color32[]` buffer at all today; extending sheets/padding/staging to them is a different, larger task (it would mean deciding what "padding" and "compositing" even mean in the SDF/coverage domain).
- **The pre/post-composite runner is a standalone, verified proof — not wired into `ShaperCompiler`'s production swarm-compile pass.** See Part 4's own note. A future task that wants this live in an authored Shaper node's actual compile output has real additional work: deciding how `ShaperBlend`'s SDF-domain fold interacts with a stage that can drop RGBA pixels, which is a genuinely different question than this task's "prove the distinction is real" scope.
- **Only 3 of the 41 effects were wired end-to-end and verified with a live render**, not all 41: `WipeModifier` (buffer-only, pre/post-composite proof), `BloomModifier` (buffer-padded, picture-rect proof), `TintModifier` (needs-sheets, availability-gate proof only — not rendered through a live sheet-publishing generator, because no current `IShaperCompositeSource` publishes `EdgeDistance` yet; the gate itself is verified live, the "effect actually runs once a generator publishes the sheet" path is not). This is the same judgment call named as acceptable in every prior Wave 3 task's own verification — landing a smaller, fully-verified slice rather than rushing all 41.
- **`ShaperEffectPicture.RenderPadded` resolves dials to their static value only** (same "named, honest simplification" `PyreFormCompositeSource` already takes for a hosted `PyreForm`'s own dials) — an authored Curve/MinMax dial on a Post modifier renders as if it were Static at that field's default.
- **Inferno/ForkBlast's crossFrac overload is documented, not split.** See Part 5.

## Part 7 — verification method

All checks were run live in the Shaper editor (port 7801) via Coplay `execute_script`, confirmed targeted via `Application.dataPath == D:/UNITY/Laubrary Dev - Shaper/Assets` before anything else. A temporary probe script (`Assets/Temp/T0114Verify*.cs`, deleted after use — never committed, matching the demos-are-scenes-not-builders convention applied to a verification harness) produced the measured numbers quoted throughout this document and the contact sheet below. See `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0114\VERIFICATION.md` for the full run log and adversarial self-review.

Contact sheet: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0114\t0114-contact-sheet.png` — left to right: swarm instance A, instance B, the Wipe effect run pre-composite, the same effect run post-composite, a magenta diff of the two (122 pixels differ), and the padded Bloom render (glow visibly reaching past the small orb's own silhouette into the transparent margin).
