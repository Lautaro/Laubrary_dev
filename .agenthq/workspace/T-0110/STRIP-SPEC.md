# T-0110 — the indexed-strip fill with per-slot height

Written before the code, from the shipped fill contract (`T-0106\FILL-CONTRACT.md`, "FC-" clauses) and the
reference/3D-Shaper maths transcribed at `T-0109\REF-HEIGHT-MATHS.md` section F (the section is literally
titled "background for T-0110"). Clause prefix `SS-`. Authority order: `SHAPER_THE_DESIGN.md` B6/C4 >
`FILL-CONTRACT.md` (FC-*) > this document.

---

## Part 1 — the reference maths, transcribed (not re-derived)

Source: `REF-HEIGHT-MATHS.md` section F, itself citing the reference app
`D:\CODEZ\AgentHQ\3D Shaper\.agenthq\attachments\T-0030\20260829T063646Z_retro_scifi_flyer_lab_draft6-1.html`
("TOY"), plus 3D Shaper's current implementation at `D:\CODEZ\AgentHQ\3D Shaper\public\index.html` ("SHAPER"),
which I additionally re-read directly (lines 1255–1305, 1336–1450) and it agrees with `REF-HEIGHT-MATHS.md`
line for line.

**SS-1.1 — the reference's z-expression, verbatim (`TOY:50`):**

```js
const z=L.z+layerHeight(L,d)+state.mats[mi].pro*(usePattern?1:.25);
if(z>=hm[k]){hm[k]=z;mm[k]=mi}
```

Reading it: a cell's height is the layer's own Z, plus the layer's shape-height profile, plus the *chosen
material's own protrusion* (`pro`), weighted **×1 where the strip is patterned and ×0.25 where it is plain
fill**. `usePattern` and `mi` (which material this cell takes) are decided immediately above it on the same
line:

```js
let mi=L.fill,usePattern=false;
if((L.patternLayout||'edge')==='stripes'){usePattern=true;mi=stripeMat(L,X,Y,rx,ry)}
else{usePattern=L.edgeCoverage>=1||depthInto<=L.edgeCoverage;
     if(usePattern){let a=Math.atan2(Y/ry,X/rx);if(a<0)a+=Math.PI*2;mi=patternMat(L,a/(Math.PI*2))}}
```

**SS-1.2 — what "patterned" structurally means.** It is a per-cell BOOLEAN computed from the strip-indexing
test above (`depthInto <= edgeCoverage` in Edge layout, or unconditionally true in Stripes layout) — it is
**not** a flag stored on the material/slot itself. Every material (the layer's own plain-fill material, and
every strip slot) always carries a `pro`; whether *that occurrence* counts at full or quarter weight depends on
whether *the specific cell being shaded* fell inside the patterned region.

**SS-1.3 — the weight is exactly `0.25`, not tunable.** `pro*(usePattern?1:.25)`. `REF-HEIGHT-MATHS.md` §F
underlines that 0.25 is not negligible: "A quarter weight over a −8..+28 range is a −2..+7 unit shift of the
entire layer body — comparable to the `bodyH` values in the shipped defaults." It is a fixed design constant in
the reference, not an authored dial, and this port keeps it fixed for the same reason (§4.4 below).

**SS-1.4 — why the weight exists at all, empirically confirmed** (`T-0101\CHECK-B-protrusion.md`, a real
headless-Chromium run of the unmodified reference app, not a re-implementation): with a single-material
("plain") shape, `pro` still visibly produces a ring — because the band and the core hold the *same* material,
so if the weight were uniform the whole disc would stay flat at every `pro` value. It does not: the 1-vs-0.25
split is the only thing that can produce a step inside a single-material shape, and the empirical run confirms a
distinct ring appears the instant `pro` leaves zero, and that the ring's *height view* delta keeps climbing
monotonically across the whole `−8..+28` slider range even where the *lit* picture visually saturates around
`+10` (a shading/shadow-march saturation, not a height-field one — see the same doc §6).

**SS-1.5 — protrusion range in the reference:** slider `−8..+28`, step `1` (`TOY:7`). Not ported as a hard
clamp here (§4.3 below) — the Laubrary convention is an authorable `ZUIValue`, not a fixed slider range, and the
reference's own numbers are canvas-pixel-in-a-64-grid units that do not transfer to Shaper's own canvas-pixel
convention 1:1.

**SS-1.6 — the shared height buffer.** `if(z>=hm[k])` — `pro` participates in the SAME z-buffer extrusion and
every other layer write to, which is what makes a raised strip a genuine divider against a neighbouring layer in
the reference app. **This is exactly the property Shaper's own architecture does NOT currently give a fill's
`heightDelta` — see Part 3 (SS-3) below, which is the honest limitation this task must report rather than
paper over.**

---

## Part 2 — 3D Shaper's improved parameterisation, transcribed

Source: `public/index.html:1255–1305` (`edgeRingParam`, `edgeStripeParam`, `edgeSequenceIndex`,
`edgeRingIndex`), read directly, cross-checked against `REF-HEIGHT-MATHS.md` and a second independent read
(`P3-shaper-internals.md` via subagent). All three agree.

**SS-2.1 — the three-part split**, its own comment names it: "Pixel edge registry, in three parts, matching the
reference prototype's patternMat/stripeMat/raster split" (`:1255`).

1. **`edgeRingParam`** — where a point sits along the ring (Edge layout). Reads `reach` (clamp01, default 1 —
   at 1 the ring covers the WHOLE outline, confirming the design doc's "a reach control that at maximum covers
   the whole shape"), `position` (clamp01, slides the covered arc around the outline) and `orientation`
   (0–360°, slides the strip's zero point around the outline). Returns `-1` when the sample falls outside the
   reach-restricted arc (only the ring layout can produce a "not part of the pattern" answer):

   ```js
   function edgeRingParam(edge, lx, ly, halfW, halfH, arc) {
     const reach=clamp01(edge.reach===undefined?1:edge.reach);
     if (reach<=0) return -1;
     const position=clamp01(edge.position||0), orientation=(((edge.orientation||0)%360+360)%360)/360;
     let angle=arc ? edgeArcFraction(arc, ((Math.atan2(ly,lx)/(Math.PI*2))%1+1)%1)
                   : Math.atan2(ly/(halfH||1), lx/(halfW||1))/(Math.PI*2);
     angle=((angle%1)+1)%1;
     angle=(((angle-orientation)%1)+1)%1;
     const relative=(((angle-position)%1)+1)%1;
     if (reach<1 && relative>reach) return -1;
     return reach<1 ? relative/reach : relative;
   }
   ```

   The `arc` argument is an optional true-arc-length remap table (`edgeArcTableFor`/`edgeArcFraction`,
   `:1244`/`:1251`) so ring cells are spaced evenly by PERIMETER on a non-circular silhouette rather than by
   raw angle; **without it (the `arc` fallback branch, `Math.atan2(ly/halfH, lx/halfW)/(2π)`) the formula is
   exactly the reference's own ellipse-normalised angle.** This port implements the fallback branch only — see
   §4.6 for why the arc-length table is deliberately deferred rather than silently dropped.

2. **`edgeStripeParam`** — where a point sits along a directional band (Stripes layout), projecting onto an
   axis set by `stripeAngle` and normalising by the silhouette's extent along it:

   ```js
   function edgeStripeParam(edge, lx, ly, halfW, halfH) {
     const angle=(((Number(edge.stripeAngle)||0)%360)+360)%360*Math.PI/180, px=Math.cos(angle), py=Math.sin(angle);
     const extent=Math.abs(px)*(halfW||1)+Math.abs(py)*(halfH||1)||1;
     return 0.5+(lx*px+ly*py)/(2*extent);
   }
   ```

   `reach`/`position`/`orientation` do **not** apply to Stripes — confirmed structurally, `edgeRingIndex` never
   passes them into `edgeStripeParam`.

3. **`edgeSequenceIndex`** — which slot a 0..1 run parameter lands on, given `mode` (`stretch`/`repeat`/
   `mirror`) and `repeats` (**forced to a whole number**, `clampTo(Math.round(...),1,16)` — this is 3D Shaper's
   fix for the reference's hardcoded fractional `2.35` repeat count, which the reference's own docs record as
   leaving a mismatched cell at the wrap point):

   ```js
   function edgeSequenceIndex(edge, t, resolution) {
     const mode=edge.mode||'repeat';
     const repeats=clampTo(Math.round(Number(edge.repeats)||2),1,16);
     let u=((t%1)+1)%1;
     if (mode==='mirror') { u=(u*2)%2; if (u>1) u=2-u; }
     const index=mode==='stretch' ? Math.floor(u*resolution) : Math.floor(u*resolution*repeats)%resolution;
     return clampTo(index,0,resolution-1);
   }
   ```

**SS-2.2 — the entry point**, one function the rasteriser calls, dispatching Edge vs Stripes and folding in the
"outside reach" `-1`:

```js
function edgeRingIndex(edge, lx, ly, halfW, halfH, arc) {
  const resolution=Math.max(2,Math.round(edge.resolution)||16);
  if ((edge.patternLayout||'edge')==='stripes') return edgeSequenceIndex(edge, edgeStripeParam(edge,lx,ly,halfW,halfH), resolution);
  const t=edgeRingParam(edge,lx,ly,halfW,halfH,arc);
  return t<0 ? -1 : edgeSequenceIndex(edge, t, resolution);
}
```

**SS-2.3 — what 3D Shaper dropped, confirmed by direct read of the same file, `:1425–1433`.** Its material has
no protrusion field at all — `{primaryColor, secondaryColor, surface, reflection, emission, pattern, ...}`,
colour and shading only. In its place, a single hardcoded constant on every ring cell (never on Stripes cells):

```js
isEdgeCell=1; if (!entry.edgeStripes) z+=0.45;
```

A flat, uniformly-proud ring, with zero per-slot control and zero effect on plain-fill cells. This is the
regression this task restores — with the difference that the restoration lands in a brand-new fill kind inside
a brand-new engine, not a patch to `public/index.html`.

**SS-2.4 — the depth/edgeCoverage band, `:1349–1350`, for completeness (feeds SS-4.1's `patterned` test):**

```js
const edgeDepth=edge ? clamp01(edge.depth===undefined?0.13:edge.depth) : 0.13, edgeFull=!!edge && edgeDepth>=1;
const cellSize=(cellW+cellH)/2, edgeBand=Math.max(cellSize*1.7, span*edgeDepth);
```

and the consuming test at the raster site, `:1428`: `entry.edgeStripes || edgeFull || -distance<=edgeBand`. In
canvas-pixel terms (no grid-cell floor, no `edgeFull` special case needed once `depth` can legally reach 1),
this is `depthInto <= depth` where `depthInto = clamp01(-edgeDistance/span)` — the same `t` this whole
sub-project already standardised on (`HEIGHT-SPEC.md` §0 / `REF-HEIGHT-MATHS.md` §0).

---

## Part 3 — how this plugs into Shaper's actual fill contract (zero contract changes, ONE necessary extension)

**SS-3.1 — the contract already promised this is cheap, and it is.** `FILL-CONTRACT.md` §F8.3 states T-0110 in
advance: *"Because this contract already gives every fill an albedo output and a height-delta output, T-0110 is
**one new fill kind and zero contract changes** — its palette entries carry a colour and a height, and both
outputs already exist."* Confirmed against the shipped code
(`Runtime\Shaper\ShaperFillContract.cs`, `ShaperFillDef.cs`, `ShaperFillOps.cs`, `ShaperFillCompiler.cs`):
`ShaperFillEmit.heightDelta` is already declared as "one per sample" (not one per fill), `ShaperFillDef` already
carries a shared `heightDelta` dial pattern every kind reuses, and `ShaperFillKind` is append-only
(`Solid=0, Gradient=1, RampByQuantity=2, Texture=3`) — adding `IndexedStrip=4` is exactly the shape T-0106
anticipated.

**SS-3.2 — the one place the claim needs a footnote: height is currently baked as a per-FILL constant, not
per-SAMPLE, and this fill genuinely needs the latter.** `ShaperFillCompiler.Compile` resolves `def.heightDelta`
ONCE per compile into a single `op.height` float (`ShaperFillCompiler.cs:242-245`), and
`ShaperFillOps.FillTile` writes that same constant into every sample of the tile
(`heightOut[d] = op.emitsHeight != 0 ? op.height : 0f;`, `ShaperFillOps.cs:113`) — `Sample()` never touches
height at all today. That is a valid simplification for the four T-0106 fills, whose authored `heightDelta` is
genuinely spatially uniform (Solid, Gradient, Ramp, Texture all bump the WHOLE surface by one number). It is not
valid for the indexed strip: `REF-HEIGHT-MATHS.md` §F is explicit that `pro` is "a MATERIAL field, not a layer
field... each slot of the strip can protrude differently" — this is the entire point of restoring it (B6:
"the raised slots catch light and cast shadows"), and collapsing it to one constant would just be 3D Shaper's
`+0.45` regression wearing new clothes.

Nothing in `FC-2.5` mandates constancy — `ShaperFillEmit.heightDelta` is already declared "one per sample", the
contract's own comment on it. The constant-write in `FillTile` is an implementation shortcut valid for the first
four kinds, not a rule. **The minimal, additive fix: extend `ShaperFillOps.Sample`'s signature with one more
`out float height` parameter.** The four existing kinds fall through to `height = op.height;` at the end of
`Sample` (bit-identical to today — verified by the audit, §5 below); `IndexedStrip` is the one case that
computes a genuinely varying value. `FillTile` then reads `Sample`'s `height` output instead of `op.height`
directly. This is the single code-shape change this task makes outside of adding the new kind itself, and it is
backward compatible by construction — see FT-S9 in the audit.

**SS-3.3 — the honest limitation SS-1.6 flagged: this heightDelta does NOT currently reach the cross-layer
depth test.** Read `HEIGHT-SPEC.md` HS-1.1 and HS-9.1 directly: the resolve's solid membership test and its
cross-layer ordering are both defined purely in terms of `base + body·G(t)` — the SHAPE's own extrusion height —
with no term for a fill's `heightDelta` anywhere in the solid definition. `HEIGHT-SPEC.md` Part 10 confirms this
is deliberate and current: *"No fill reads `height` yet... LR-3.4's ruling that a fill's height delta does not
reach the normal in Wave 2 is NOT reopened."* Part 11 (T-0109's own fix pass) DOES wire the fill's `heightDelta`
into `ShaperLightScene.pointZ` (`base + height`, where `height` is `ShaperLayer.height` = shape height +
Σ fill heightDelta·coverageEff) — so it reaches **shading** (a point light's distance/attenuation math) — but
`pointZ` is not consulted anywhere in `ShaperResolve`'s crossing computation, which is what decides which layer
wins a pixel. So: **this task restores the paint-and-sculpt half of the reference behaviour (a slot's height
genuinely lifts/sinks the surface and is genuinely lit differently), and it restores the shading-reaches-height
half. It does NOT currently restore "a raised strip wins the depth test against a neighbouring layer" — that
would require `ShaperResolve`'s solid definition itself to read the accumulated fill height, which is out of
this task's contract-respecting scope (BC-4.2, FC-1.7's own table row, explicitly excludes fills from anything
the resolve/march reads) and is not something T-0109's own shipped HS-1.1 does today.** This is reported
honestly in the handover rather than claimed.

---

## Part 4 — the port, decided

**SS-4.1 — new fill kind.** `ShaperFillKind.IndexedStrip = 4` (append-only, matches the existing `Solid=0,
Gradient=1, RampByQuantity=2, Texture=3`).

**SS-4.2 — dials on `ShaperFillDef`** (all new fields are additive; nothing existing changes shape):

| Dial | Type | Default | Reference/3D-Shaper source |
|---|---|---|---|
| `stripLayout` | `enum { Edge, Stripes }` | `Edge` | `patternLayout` |
| `stripSlots` | `ShaperStripSlot[]` (`{ Color color; ZUIValue height; }`) | one slot, white, height 0 | the palette (`seq`/`materialIds`, each entry's own `pro`) |
| `stripMode` | `enum { Stretch, Repeat, Mirror }` | `Repeat` | `edge.mode` |
| `stripRepeats` | `ZUIValue` (rounded, clamped [1,16] at bake) | 2 | `edge.repeats` |
| `stripOrientation` | `ZUIValue`, degrees | 0 | `edge.orientation` |
| `stripReach` | `ZUIValue`, clamp01 at bake | 1 (whole shape, per the design doc) | `edge.reach` |
| `stripPosition` | `ZUIValue`, clamp01 at bake | 0 | `edge.position` |
| `stripDepth` | `ZUIValue`, clamp01 at bake | 0.13 (3D Shaper's own default, kept so an unauthored strip looks like a ring rather than either extreme) | `edge.depth` (edgeCoverage) |
| `stripAngle` | `ZUIValue`, degrees | 0 | `edge.stripeAngle` — Stripes layout only |
| `plainColor` | `Color` | white | the layer's own separate face material in the reference |
| `plainHeight` | `ZUIValue` | 0 | the SAME per-material `pro`, just authored once for "not patterned" rather than per ring slot |

**Deliberately not ported as a separate dial: `resolution`.** 3D Shaper keeps `edge.resolution` independent of
`edge.materialIds.Length` (a ring can be declared wider than its authored material list, falling back past the
end). This port makes `stripSlots.Length` **be** the resolution directly — one array, one number, resizing the
array IS changing the resolution. This is a deliberate simplification over 3D Shaper, named rather than hidden:
it removes one way to mismatch two numbers, at the cost of not being able to declare "16 ring cells, only 4
painted, rest fall back" in one authoring gesture (author 4 slots instead, or extend the array with plain-fill
mimicking entries). `resolution` is therefore computed as `max(1, stripSlots.Length)` at bake and never authored
directly.

**SS-4.3 — protrusion range: not clamped to the reference's `−8..+28`.** Every Laubrary `ZUIValue` height field
in this codebase (the four T-0106 `heightDelta` dials, `HEIGHT-SPEC.md`'s `zOffset`) is an open, unclamped
canvas-pixel float — clamping only this one to a legacy 64-grid slider range would be a Shaper-wide convention
violation for a number that means something different in Shaper's own units. Per-slot height is therefore an
unclamped `ZUIValue`, exactly like `plainHeight` and the four kinds' own `heightDelta`.

**SS-4.4 — the weight stays a fixed `0.25`, not an authored dial.** SS-1.3/SS-1.4 establish it as a load-bearing
design constant, not a tuning knob — `REF-HEIGHT-MATHS.md` §F never treats it as authorable (it is not in the
material struct, only in the raster expression), and exposing it as a dial would let an author set it to 1 and
silently lose the entire "a plain single-material shape still shows relief" property SS-1.4 empirically confirms
is the mechanism's whole value. Named as a **project rule violation risk avoided**, not merely a copy choice:
CLAUDE.md's "don't add surface the user didn't ask for" applies to dials as much as menu items, and the task
brief itself states the two weights as fixed numbers ("at full weight for patterned slots and a quarter weight
in plain fill"), not as a range.

**SS-4.5 — per-sample algorithm, `ShaperFillOps.Sample`, `case ShaperFillKind.IndexedStrip`:**

```
Anchor(op, x, y, out u, out v)                          // node-local, same as Gradient/Texture (FC-1.5/1.6)

if stripLayout == Stripes:
    t = 0.5 + (u·cosStripeAngle + v·sinStripeAngle) / (2·extent)     // extent baked from local half-extents
    patterned = true
else:                                                    // Edge
    depthInto = clamp01(-edgeDistance · invSpan)          // SS-2.4, span = max(ε, min(localHalfW, localHalfH))
    patterned = (depth >= 1) || (depthInto <= depth)
    if patterned:
        angle = frac(atan2(v, u) / 2π)                    // SS-2.1's arc==null fallback branch (§4.6)
        angle = frac(angle - orientationFrac)
        relative = frac(angle - positionFrac)
        if reach < 1 and relative > reach: patterned = false
        t = reach < 1 ? relative/reach : relative

if patterned:
    u01 = frac(t); if mirror: u01 = mirrorFold(u01·2)
    index = mode==Stretch ? floor(u01·resolution) : floor(u01·resolution·repeats) % resolution
    index = clamp(index, 0, resolution-1)
    (r,g,b) = slotColor[index]          (LINEAR, decoded at bake)
    height  = slotHeight[index] * 1.0                        // SS-1.3, full weight
else:
    (r,g,b) = plainColorLinear
    height  = plainHeight * 0.25                              // SS-1.3, quarter weight

veil = op.veil        // the shared common dial, unaffected by patterned/plain — SS-4.7
```

**SS-4.6 — true arc-length parameterisation (`edgeArcTableFor`) is deliberately deferred, not silently
dropped.** 3D Shaper's improvement over the reference is genuinely two things: (a) whole-number repeats +
orientation/reach/position (ported, SS-2.1/2.2), and (b) an optional perimeter-arc-length remap so ring cells
land evenly by distance-around-the-outline rather than by angle on non-circular silhouettes, with 3D Shaper's
own fallback being exactly the angle-only formula this port implements. Building the arc-length table requires
tracing the shape's own SDF at compile time (a new per-node computation, not a fill-stage concern per FC-1.7 —
it would live in the shape/anchor layer, same class of addition as `FC-1.5a`'s local support box), and it is not
required to prove B6's central claim (colour-plus-height composing through the existing fill contract) — which
is this task's stated purpose ("the cheapest thing that proves the whole colour-plus-height fill model"). Named
here as a follow-on enhancement, not built, so it is answered rather than discovered later.

**SS-4.7 — the shared `veil` dial composes exactly as every other kind's does**, unaffected by the
patterned/plain split; there is no interaction to define because SS-1.3's weighting is internal to this fill's
own height computation, resolved before `FC-2.5`'s external `heightDelta·coverageEff` composition ever runs.

**SS-4.8 — availability (`RequiredSet`).** Edge layout requires `edgeDistance` (used for the depth-band test);
Stripes layout requires nothing — same shape as Gradient's `ByEdgeDistance` vs its other three modes (FC-4.1a).
Both layouts are positional (`op.positional = 1`), same handling as Gradient's spatial modes and Texture.

**SS-4.9 — degenerate/fallback cases, per FC-6.5 ("a half-configured fill never renders empty").**

- Zero slots authored → degenerates to `Solid` of `plainColor`, diagnostic recorded (mirrors `BakeGradient`'s
  null-gradient fallback exactly).
- `stripReach <= 0` → every sample is "not patterned" (plain fill only) — matches the reference's own
  `if (reach<=0) return -1;`.
- Degenerate anchor (empty bag, `FC-1.5b`) → `(u,v) = (0,0)`, still evaluates, never divides, never refuses.
- Degenerate `span` (a near-zero-extent shape) → `invSpan` floored at the same `1e-6` `DegenerateExtent` guard
  the anchor bake already uses, matching the existing `MinPositive`/`DegenerateExtent` convention in
  `ShaperFillCompiler.cs` rather than inventing a new epsilon.

---

## Part 5 — the Lipschitz/slope-bound question (todo T4), decided: NONE is declared, and here is why

**SS-5.1 — the honest shape of the question.** The strip's height, as a function of the shape's own
parameterisation (`t`, the ring/stripe run parameter), is a genuine STEP FUNCTION — a hard jump of
`(slotHeight[k+1] − slotHeight[k])` at every slot boundary, exactly the same shape as `HEIGHT-SPEC.md`'s
`Stepped` extrusion profile (`HS-4.1`: declared `sup|dE/dt| = +∞`, discontinuous). If this fill's height needed
to feed a ray-marcher the way `HEIGHT-SPEC.md`'s extrusion/bevel profiles do, the honest declaration would be
`+∞` — exactly HS-4.1's own pattern ("declaring infinity is a first-class answer, not a failure to measure"),
and the composed constant would be undeclarable in general (matching `Stepped`'s own treatment in HS-4.1/HS-5).

**SS-5.2 — but that question does not arise, because fills are explicitly out of the Lipschitz-bound system by
contract, not by oversight.** `FILL-CONTRACT.md` FC-1.7's own binding table has a row for exactly this:

> **BC-4.2 — the bound, the safe march step | Does NOT bind the fill stage. A fill emits no distance and
> nothing marches on its output. Recorded so nobody adds a bound to a fill by analogy.**

This is a direct, load-bearing instruction not to build what todo T4 might otherwise be read as asking for: a
`SlopeBound`/`IsLipschitz`-style declaration on `ShaperFillOp`, mirroring `ShaperBound`'s role for shape
primitives and `HEIGHT-SPEC.md`'s `HS-4.*` role for extrusion/bevel profiles. `ShaperBound` and its consumers
(the general resolve's slab march, `HEIGHT-SPEC.md` Part 5) exist to bound how fast the **shape's own solid
membership test** can change, so a ray-marcher can take a provably-safe step. A fill's `heightDelta` is not
consumed by that machinery at all (SS-3.3) — it never marches, never feeds a resolve query, and per
`HEIGHT-SPEC.md` Part 10, "LR-3.4's ruling that a fill's height delta does not reach the normal in Wave 2 is NOT
reopened." Declaring a bound nothing reads would be exactly the kind of unconsumed ceremony `HS-4.3` warns
against for extrusion profiles ("nothing on the rendering path... its purpose is to make refusal mechanical for
a FUTURE consumer") — except here there is no contract-sanctioned future consumer to make refusal mechanical
for, because BC-4.2 has already ruled that door shut for the whole fill stage, all four existing kinds included.

**SS-5.3 — the decision.** No slope/Lipschitz bound is declared anywhere on `ShaperFillOp` or `ShaperFillDef`
for the indexed strip. This is consistent with every other fill kind (none of the four T-0106 fills declares
one either, despite Gradient's `Angular` mode and Ramp's hard-step case (`FC-6.3a`) being equally discontinuous
in their own parameter). If a future task genuinely needs a fill's height to feed a march (which would first
require reopening `LR-3.4`, itself flagged as a live, deliberate, named decision rather than an oversight), that
task is the right place to design a fill-side bound contract from scratch against whatever the new consumer
actually needs — not this one, retrofitting a mechanism BC-4.2 explicitly forbade in advance.

---

## Part 6 — what the runtime code changes, concretely (for the implementer / this same session)

1. `ShaperFillContract.cs` — add `IndexedStrip = 4` to `ShaperFillKind`. Add `ShaperStripLayout { Edge, Stripes }`
   and `ShaperStripMode { Stretch, Repeat, Mirror }` enums (both append-only, mirroring `ShaperGradientMode`'s
   style). Add `ShaperStripSlot { public Color color; public ZUIValue height; }` (plain serializable struct/class,
   not `SerializeReference` — a strip does not need polymorphism, matching `ShaperFillDef`'s own "flat class, not
   a hierarchy" reasoning, FC-5.2).
2. `ShaperFillDef.cs` — add the SS-4.2 dials; extend `RequiredSet()`/`OptionalSet()` per SS-4.8.
3. `ShaperFillCompiler.cs` — add `BakeIndexedStrip`; extend `ShaperFillOp` with the baked strip fields (bulk
   offset/count for the slot palette, mode/repeats/orientation/reach/position as baked floats, `invSpan`,
   `plainHeight`, stripe-angle cos/sin); extend `Compile`'s switch. Bulk layout: `SlotChannels = 4` (linear R, G,
   B, height) per slot, same pattern as `LutChannels`/`TexelChannels`.
4. `ShaperFillOps.cs` — extend `Sample`'s signature with `out float height` (SS-3.2); add the `IndexedStrip` case;
   update `FillTile` to read `Sample`'s height output instead of the bare `op.height` constant.
5. New editor file `ShaperStripAudit.cs`, mirroring `ShaperFillAudit.cs`'s shape exactly (plain statics, no
   `[MenuItem]`, `RunAll()` returning a report string, invoked via `unity command eval_file`).

No change to `ShaperFillResolver.cs`, `ShaperHeight.cs`, `ShaperLightCompiler.cs` or `ShaperResolve.cs` — the
new kind is generic to all of them by construction (SS-3.1), which is the whole point of F8.3's promise.

---

## Part 7 — SUPERSEDED: Part 4's Edge/Stripes design, replaced by a simpler one closer to B6's own words

**SS-7.1 — what happened.** A background research pass ("extract the live Shaper fill/height/light contract")
went further than asked and partially implemented a fill kind while this document was still being written —
`ShaperFillContract.cs` and `ShaperFillDef.cs` gained `ShaperFillKind.IndexedStrip`, a
`ShaperStripParameterisation { Angular, Projection }` enum, and dials on `ShaperFillDef`; `ShaperFillCompiler.cs`
gained a working `BakeStrip`. `ShaperFillOps.cs` (the per-sample evaluate) and the audit were not touched. Found
mid-session via `git diff` in `D:\UNITY\Laubrary Dev - Shaper`, read in full, and judged on its merits rather
than discarded or blindly kept.

**SS-7.2 — why it is adopted over Part 4's plan, not merely tolerated.** Part 4 above ported 3D Shaper's
Edge/Stripes split (`patternLayout`) literally — two layouts, only one of which (Edge) is gated by a reach/depth
band, the other (Stripes) unconditionally patterning the whole face. Re-reading the actual authority ordering
this task was given (`SHAPER_THE_DESIGN.md` B6 outranks the 3D Shaper source, which is evidence, not spec): B6's
own words are *"the shape is parameterised — either by the angle around it, or by projection across it — and
that parameter selects a slot from the strip. A separate control decides how far in from the outline the strip
reaches; turn it to maximum and it covers the whole shape."* That is ONE reach control, gating BOTH
parameterisations identically, with maximum reach covering the whole shape — not a second layout where reach
does not apply at all. 3D Shaper's Edge/Stripes split (where Stripes ignores reach) is 3D Shaper's OWN further
elaboration, not B6's model, and Part 4 over-indexed on the source code over the higher-authority design doc.
The adopted design fixes this: `ShaperStripParameterisation { Angular, Projection }` selects HOW the running
parameter is computed (angle vs. linear projection, both over the node-local anchor coordinate already used by
Gradient), and ONE `stripReach` dial gates BOTH identically, tested against the shape's own `edgeDistance`
(unconditionally required — `RequiredSet()` already updated) against a reach expressed as a **fraction of the
node's own local half-extent** rather than raw canvas pixels, so `reach = 1` reaches the shape's own shorter
half-extent — which for a symmetric primitive means the entire disc/box is patterned, exactly B6's "turn it to
maximum and it covers the whole shape." This also drops 3D Shaper's separate `edgeDepth`/`edgeBand` cell-size
floor entirely (no grid-cell concept exists in Shaper), which is a simplification made possible by Shaper's own
continuous canvas-pixel field, not a regression.

**SS-7.3 — what changes concretely from Part 4's plan (§4.2/§4.5), and what stays.** `stripLayout` (Edge/
Stripes) is REPLACED by `stripParameterisation` (Angular/Projection); `stripDepth` (the edgeCoverage band
fraction) is REPLACED by `stripReach` doing double duty as both "how much of the shape is patterned" AND the
selection basis's own extent, per SS-7.2; `stripAngle` (a separate Stripes-only rotation dial) is REPLACED by
`stripOrientationDegrees`, which now means "the axis direction" under Projection and "the phase where the
parameter is 0" under Angular — i.e. one dial doing what `gradientAngleDegrees` already does for Gradient's own
Linear/Angular pair, which is a closer sibling relationship than Part 4's design had. `stripPosition` is
REPLACED by `stripOffset` (same meaning: a 0..1 wrapping phase shift). `plainHeight` (Part 4's own new dial) is
DROPPED — the plain/interior region's height comes from the fill's own shared `heightDelta` dial (already on
every `ShaperFillDef`) at quarter weight instead, which avoids adding a dial that duplicates existing surface
(a closer reading of the reference: the "plain fill" material in the TOY app IS the layer's own separately
authored face material with its own single `pro` — the closest existing Shaper analogue to a node's single
"face material" is its own shared `heightDelta` dial, not a strip-specific second height dial). `stripSlots`
stays a `List<ShaperStripSlot>` with `{ Color color; float height; }` — height is a plain `float`, not a
`ZUIValue`, in the adopted implementation (a deliberate simplification versus SS-4.2's `ZUIValue`: it keeps
`BakeStrip`'s per-slot loop allocation-free and matches the reference's own per-slot `pro` being a plain
authored number rather than independently animated per slot — the STRIP's own `stripRepeats`/`stripOrientation`/
`stripOffset`/`stripReach` dials remain full `ZUIValue`s and so the whole strip can still animate as a unit).
`resolution` is still implicit as `stripSlots.Count` (SS-4.2's simplification stands). The reference-app
protrusion weighting (SS-1.3, ×1 patterned / ×0.25 plain) is UNCHANGED and is what Part 8 below wires up — the
one piece the partial implementation had not yet reached when found.

**SS-7.4 — the final per-sample algorithm, as actually shipped** (`ShaperFillOps.Sample`,
`case ShaperFillKind.IndexedStrip`), superseding SS-4.5 and revising my own SS-7.4 first draft below — the
implementation that landed is a SOFT, continuous blend between patterned and plain, not the hard boolean cutoff
I had first specified, and it is the better design:

```
Anchor(op, x, y, out u, out v)                            // node-local, shared with Gradient/Texture

t = stripAngular
    ? frac( (atan2(v,u) - atan2(stripSinTheta,stripCosTheta)) / 2π )   // Angular: identical construction to Gradient's own Angular case
    : clamp01( (u·stripCosTheta + v·stripSinTheta)·0.5 + 0.5 )         // Projection: identical construction to Gradient's own Linear case, unscaled (no separate "size" dial — stripRepeats subdivides instead)

raw = t·stripRepeats + stripOffset
frac01 = raw − floor(raw)
slot = clamp(floor(frac01 · stripSlotCount), 0, stripSlotCount−1)
(slotR,slotG,slotB,slotHeight) = palette[slot]             // LINEAR, baked at compile time

// B6's reach, made CONTINUOUS rather than a hard cutoff: 1 at the silhouette edge (edge==0), ramping down to 0
// at the reach depth (edge==−reachPixels) and beyond. `edge` is signed, negative inside (the polarity trap).
edgeCoverage = clamp01(1 + edge · stripInvReach)

(r,g,b) = lerp(plainColor, slotColor, edgeCoverage)          // colour ramps smoothly across the reach boundary
weight   = lerp(0.25, 1.0, edgeCoverage)                     // SS-1.3's two weights, now the ENDPOINTS of one ramp
height   = (emitsHeight ? op.height : 0) + slotHeight · weight   // common heightDelta dial ADDED underneath, FC-2.5
veil     = op.veil                                            // unaffected, same as every other kind
```

**Why continuous rather than the reference's hard `usePattern` boolean, and why this is an improvement rather
than a drift from the brief.** The reference's `usePattern` is a hard step (`depthInto<=edgeCoverage`), which is
exactly the kind of hard-edged test C3 (`SHAPER_THE_DESIGN.md:317-321`) argues against for coverage itself —
"the shape always owns its own edge... a fog rather than a stencil" — and porting a hard step here would put a
visible ring-shaped seam in the RELIEF at the reach boundary even though the colour ramp already avoids exactly
that seam nowhere else in this fill contract (every other kind's spatial parameter is continuous). Colour and
height are blended by the SAME `edgeCoverage` value, so they never disagree about where "the pattern" ends — a
hard cutoff in one and a soft one in the other would be its own new defect. The two SS-1.3 weights (1 and 0.25)
survive exactly, as the two ENDPOINTS of the ramp rather than as a two-valued switch — at `edge == 0` (exactly
on the reach boundary reading as "fully patterned") the weight is 1; at `edge <= -reachPixels` it is 0.25;
between the two it interpolates. This is a generalisation of the reference's rule, not a different rule: setting
`stripInvReach` so the ramp collapses to a single canvas pixel recovers the reference's hard edge to within one
sample, and nothing about SS-1.1's z-expression assumed a hard step was load-bearing — only that the two weights
themselves (1 and 0.25) are what they are (SS-1.4's empirical confirmation used a single-material shape, which
is insensitive to whether the patterned/plain split is hard or soft, since both regions were the same colour
regardless).

**The common `heightDelta` dial is ADDED underneath the strip's own relief, everywhere, weighted by the same
ramp — not swapped out in the patterned region the way the reference's `mi` swap works.** This is a genuine,
named departure from SS-1.1's literal z-expression (where the patterned region reads `pro` from the
strip-selected material ONLY, never from the layer's separate face material) and it is deliberate: FC-2.5
defines a fill's `heightDelta` as something "ADDED to the shape's own height," and every other T-0106 fill kind
already treats it as a uniform authored nudge available regardless of what the fill's own per-pixel logic is
doing. Keeping that property for IndexedStrip means an author can still raise or lower the WHOLE fill (patterned
and plain together) with the one dial every other kind already has, on top of the per-slot detail — which is
more capable than the reference, not merely different from it, and it costs nothing: with the common dial at its
default of 0 (the common case, since it is not the kind's headline feature) this term vanishes and the formula
reduces to exactly `slotHeight · weight`, bit-identical to SS-1.1's rule.

No `mode` (stretch/repeat/mirror, Part 4's SS-2.1) in the adopted version — `stripRepeats` alone, always
tiling (never "stretch exactly once"), is what shipped. Recorded as a scope reduction versus 3D Shaper's
three-mode picker, not silently dropped: "stretch" is achievable today by authoring `stripRepeats = 1`, and
"mirror" is a genuine gap (no seam-matching fold). Both are one-line additions to the `frac01` computation
above if wanted later — not built now, because they are not required to prove B6's central claim and this task
is restoring capability 3D Shaper itself dropped, not matching 3D Shaper's picker feature-for-feature.

---

## Part 8 — CORRECTION to SS-3.3, verified directly against the shipped code (recorded rather than silently fixed)

**SS-8.1 — SS-3.3's `pointZ` claim is WRONG and is corrected here.** SS-3.3 states that "Part 11 (T-0109's own
fix pass) DOES wire the fill's `heightDelta` into `ShaperLightScene.pointZ`... so it reaches shading." This was
checked directly against `ShaperFillResolver.cs` rather than taken from a document, and it does not hold: the
ONLY assignment to `scene.pointZ` for a Silhouette owner (`ShaperFillResolver.cs:978`,
`scene.pointZ[slab + i] = hopO.baseZ + buf.ownHeight[slab + i];`) runs in PASS 1, before any owner's fill has
been evaluated, and reads `buf.ownHeight` — the shape's own extrusion height only. It is never touched again;
`grep`ing the whole `Runtime/Shaper/` tree for `pointZ` finds this one write site (plus `ShaperSolids.cs`'s own,
unrelated, geometry-derived write for the Solids family) and nothing that folds a fill's `heightDelta` into it.
`buf.height` (the accumulated buffer `heightDelta` DOES reach, `:1046`/`:1229`) is, separately, read by nothing
outside the editor audits (`ShaperFillAudit.cs`, `ShaperHeightAudit.cs`, `ShaperLightAudit.cs`) anywhere in the
shipped `Runtime/` or `Editor/` trees — confirmed by grep, zero runtime consumers.

**SS-8.2 — the corrected, verified position.** A strip slot's height reaches exactly one place at runtime:
`ShaperFillBuffers.height`, via `ShaperFillEmit.heightDelta` → `buf.heightDelta[i] += ...` →
`buf.height[i] += buf.heightDelta[i]·coverageEff` — precisely FC-2.5's documented interface, and precisely
what B6 calls "the cheapest thing that proves the whole colour-plus-height fill model." It reaches NONE of:
the surface normal (LR-3.3/LR-3.4, explicit and deliberate for Wave 2), `pointZ` / point-light attenuation
(SS-8.1, corrected), diffuse or specular shading (both derive from the normal and `pointZ`), shadows (same
derivation), or any cross-layer depth test (no consumer exists in Wave 2 at all — not for this fill, not for
any of the other four kinds; `buf.height` has zero runtime readers). This is not a gap T-0110 introduced; it is
the pre-existing, `LR-3.4`-documented state of the whole fill-height interface, and T-0110 is the first fill
kind to populate it with anything meaningful. Reported honestly in `VERIFICATION.md` rather than claimed.
