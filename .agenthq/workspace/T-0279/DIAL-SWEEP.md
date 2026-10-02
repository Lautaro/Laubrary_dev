# T-0279 — the eight slow generators, swept at 32x32/mid-frame

Methodology: for each generator, a `ShaperDocument` (32x32 canvas, frameCount=3, seed=12345) hosting the
generator via `PyreFormCompositeSource` (Fire/Fireball via their own `IShaperCompositeSource`) is rendered at
the mid frame as a baseline. Every leaf field `ZuiReflect.FieldsOf` would draw — recursed through nested
`[Serializable]` classes/arrays/lists, gated by `[ZUIShowIf]` exactly as the card evaluates it — is perturbed
in BOTH directions (increase then, only if that showed nothing, decrease) and the render re-counted;
`ZUIValue` fields perturb `staticValue`. Raw per-field logs are attached (`raw-sweep-<Generator>.log`).

**A bidirectional retest was necessary.** The first pass only increased each value and found 137 "dead"
dials; several were fields already at a `[Range]` ceiling (e.g. Plasma Bloom's `driftAmt`/`biasK`/etc.), so
"increase" was a no-op while "decrease" moved the picture fine. Re-running with the fallback above dropped
Plasma Bloom's dead count from 24 to **0** — every one of its 168 dials acts. The numbers below are the
corrected (bidirectional) pass.

## Known limitation of this sweep — read before trusting a "dead" `ZUIValue` finding

`ZUIValue` perturbation here only ever touches `staticValue`. A field whose factory default is
**`Mode.Curve`** (an authored envelope, not a flat number) never reads `staticValue` at all, so this sweep
necessarily reports it dead regardless of its true behaviour. Confirmed by hand (bypassing the sweep,
perturbing an actual curve point instead) on three fields that the sweep called dead and are NOT:

- `Fire.intensity` (`PyreShaperSimSupport.IgniteHoldFade()`, Curve mode) — perturbing a curve point: diff=62.
- `Fireball.source` (same factory) — diff=157.
- `Fire.alpha` / `Fireball.alpha` (`PyreLayer.DefaultAlpha()`, also Curve mode, `Pyre.cs:980-990`) — not
  independently re-tested, but it is the identical factory, so it is almost certainly the same false
  positive rather than a real dead dial.

**Anything below marked `ZUIValue` and reported dead has NOT been confirmed dead** — only fields verified
by a targeted, mode-aware manual test (or those that are plain float/int/bool/enum, which this limitation
does not touch) are trustworthy. A follow-up sweep with a curve-aware perturbation (move a point, not
`staticValue`) is needed before any of the remaining `ZUIValue` dead findings below are acted on.

## Per-generator counts

| generator | dials swept | dead (raw) | resolved this task | still open |
|---|---:|---:|---:|---:|
| Plasma Bloom | 168 | 0 | — | 0 |
| Kiln Orb | 59 | 6 | 1 absented (swarmSize) | 5 |
| Torch | 105 | 15 (+5 skipped, null anim overrides) | 1 absented (swarmSize) | 14 |
| Jet | 85 | 18 | 1 absented (swarmSize) | 17 |
| Radial Jet | 95 | 25 | 1 absented (swarmSize) | 24 |
| Explosive Jet | 171 | 32 | 1 absented (swarmSize) | 31 |
| Fire | 27 | 3 | 1 greyed (armMode), 2 false positives (intensity, alpha) | 0 |
| Fireball | 13 | 4 | 1 greyed (mirror), 1 false positive (source, and almost certainly alpha too) | 1 (sharpness) |
| **Total** | **723** | **103** | **9** | **92** (mostly unverified `ZUIValue` per the caveat above) |

## Fixed — absented (`Editor/PyreShaper/PyreFormShaperUI.cs`)

`swarmSize` (Orb/Torch/Jet/RadialJet/ExplosiveJet) and InfernoForm's four `[PyreSwarmOnly]` blast-variation
dials, plus ArcBurstForm's, were ALL measured dead. Root cause, confirmed in source, not guessed:
`PyreFormCompositeSource.Render` builds every hosted form's `PyreFormCtx` with `null` for the three swarm
slots (`Runtime/PyreShaper/PyreFormCompositeSource.cs:~118`) — a composite node hosts exactly one form with
Shaper's OWN swarm never wired into it, so `ctx.swarm` is always null and every `[PyreSwarmOnly]` dial
(`Runtime/Pyre/PyreForm.cs:55`) is structurally unreachable when hosted, unlike in Pyre's own window, which
only hides these dials while the layer's swarm toggle is off (`PyreWindow.Forms.cs:127`) because there it CAN
be turned on.

Fix: `PyreFormShaperUI.cs`'s `ZuiReflect.Options` for the dial dump now sets
`Skip = f => Attribute.IsDefined(f, typeof(PyreSwarmOnlyAttribute))` — the same test Pyre's own window uses,
simplified to "always skip" since Shaper never offers the swarm these dials need. No Pyre form file touched;
this is editor-drawer-only. Compiles clean (`recompile_status`: `up_to_date`, no errors).

## Fixed — greyed with a reason (tooltip, `Runtime/PyreShaper/*.cs`)

- `FireCompositeSource.armMode`: measured dead at the default `Arms = 1`; re-measured with `Arms = 3`, diff=153 —
  genuinely acts once there is more than one arm to mirror or vary. Tooltip now says so.
- `FireballCompositeSource.mirror`: same pattern — dead at `Arms = 1` (diff=0), acts at `Arms = 3` (diff=50).
  Tooltip now says so.

Both are `Runtime/PyreShaper/**` (bridge code), not Pyre's own files.

## Confirmed false positives (sweep methodology, not a dial bug)

`Fire.intensity`, `Fireball.source`, and almost certainly `Fire.alpha`/`Fireball.alpha` (same `DefaultAlpha()`
Curve-mode factory) — see the limitation section above. No code changed for these; they are not dead.

## T-0266 seed-offset spot-check (Jet, Torch, Plasma Bloom, Inferno)

None of the four declares an authored **field** called `seed` — checked directly in
`PlasmaBloomForm.cs`, `TorchForm.cs`, `JetFormBase.cs`, `InfernoForm.cs`: every "seed NNN" string in these
files is a code comment naming the reference dataset a variant was tuned from, not a control. The only seed
input any of them reads is `ctx.seed` (`PyreFormCtx.seed`), and every one of them derives its internal working
seed the same way: `unchecked(ctx.seed + ctx.layerSalt * 1000003)` (Plasma Bloom, Jet family) or
`PyreRenderer.Hash(ctx.seed, ctx.layerSalt, 0, 0)` (Inferno) — an ADDITIVE offset off the ctx seed, never an
independent draw. `ctx.seed` itself is the document's own seed carried down through
`PyreFormCompositeSource.Render`, and `layerSalt` is a fixed compile-time constant `0` for every
Shaper-hosted form (`PyreFormCompositeSource.cs`'s own comment: "a composite node hosts exactly one form — no
sibling layer... so the salt is a fixed constant"). So for all four: **confirmed, the internal seed IS the
document seed, offset by exactly zero** when hosted in Shaper. There is no field to add a tooltip to — the
finding is stated here instead, per the card's "say so" being satisfied by the nearest true home (this report)
when no control exists.

## Still open — measured dead, not yet root-caused (attach: raw sweep logs)

Two recurring shapes, neither acted on because neither is confirmed beyond "measured 0 at 32x32, this
document, this seed":

1. **A "ring/pulse/shock/lobe/flash/bulge/curl" cluster** on the Jet family (`Jet.gout.*`, `RadialJet.corona.*`,
   `ExplosiveJet.detonate.*`) and Torch (`Torch.barbs.*`) — 17/24/31/14 dials respectively. Every field checked
   (`ringAmp`, `ringGrow`, `ringR0`, `ringReach`) IS read in `PyreJetEngine.cs:622-628` and does feed the
   picture's maths. Working hypothesis, NOT verified: these variants are authored at native canvases of
   150-300px (their own tooltips: Gout "160×92", Corona "176×176", Detonate "184×184"); at a 32x32 test canvas
   a ring/pulse effect sized for that native scale may fall sub-pixel or entirely off-frame. A follow-up sweep
   at native canvas size (or at least ≥128px) is needed before concluding these are bugs rather than a
   too-small test canvas.
2. **Orb's `despeckle`/`despeckleBelow`/`emberdrift.ramp.*`** (5 dials) — `despeckle` IS read
   (`OrbForm.cs:659`, feeds `PyreOrb.Rasterise`) but only removes isolated faint pixels; this document/seed at
   32x32 may simply have none to remove. Content-dependent, not re-verified against a different seed/canvas.
3. **`Fireball.sharpness`** — re-tested by hand at the extremes (0 and 3.5, not just the sweep's ±7.5 offset):
   diff = 0 both ways. This one is NOT explained by the canvas-scale or curve-mode hypotheses above (arms
   already at default 1, unrelated) and deserves a real look at `FireballSim`'s use of `sharpness` — the one
   finding in this report that reads as a genuine candidate bug rather than a test artifact, left unfixed
   because I ran out of budget to trace `FireballSim.cs` itself.

Full per-dial lists for all eight generators are in `raw-sweep-<Generator>.log` in this folder.
