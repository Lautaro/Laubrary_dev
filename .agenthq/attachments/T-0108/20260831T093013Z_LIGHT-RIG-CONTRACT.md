# Shaper — the light-rig contract

**T-0108, 2026-08-31. Wave 2. Written before any lighting code exists, which is the only moment at which it is cheap.**

This document is normative. Where it says MUST, a task that violates it is wrong even if it works. Where it says MAY, the choice is genuinely free. Every factual claim about existing code carries a `file:line`, verified against the Shaper worktree at `D:\UNITY\Laubrary Dev - Shaper` on 2026-08-31. Claims I could not verify are marked **UNVERIFIED** in place.

**Authority.** `SHAPER_THE_DESIGN.md` (signed off) B4, B7, B8, C1, C2, C6; `BUFFER_CONTRACT.md` (T-0102) BC-1.*, BC-2.*, BC-3.3, BC-3.6, BC-4.*; `FILL-CONTRACT.md` (T-0106) FC-1.*–FC-8.*; `BORDER-CONTRACT.md` (T-0107) BD-1.*–BD-4.*. This document does not re-decide anything those decided. Where it goes further, it says so and marks the addition as a decision taken here (`[D-T0108]`).

**Rule IDs.** `LR-1.3`, `LR-3.2`, … Cite the ID in task text and in code comments. Tests are `LT-n`. A rule nobody can name is a rule nobody can defend.

**Scope.** This contract binds the document light rig, the shared shading law, the per-layer response block, the surface-normal provider interface, the placement of lighting in the paint pass, and the shape of the Solids port. It builds no UI, adds no menu item, and ships no window (design F8.6's rule, applied here unchanged).

---

# Part 0 — one page, and the eight things that turned out not to be true

## The rulings, in one page

**A — the light list.** One new authored type, `ShaperLightRig`, owned by the document, holding a document ambient (colour + intensity) and up to **8** lights. A light is `Directional` or `Point`; it carries a colour (plain `Color`, sRGB, decoded once at compile), an intensity, a direction (yaw/pitch) or a position (x, y, z), a range, a specular strength, and an enabled flag. Every scalar is a `ZUIValue` sampled at compile through `ShaperValue.Sample` (`ShaperValue.cs:26`), exactly like `ShaperBorderDef.width` (`ShaperBorderDef.cs:70`); the colour is not animated and that asymmetry is argued rather than assumed. Ambient is **document-level, not per-light**. **The frame is pinned: canvas pixels, canvas-centred, +Y up, +Z out of the screen toward the viewer — the same frame `ShaperSampleGrid.Centred` already establishes (`ShaperEvaluator.cs:20-29`), and the same unit `ShaperField`'s σ_min rescale already guarantees for every distance at every nesting depth (`ShaperField.cs:181-183`).** Height is in those same pixels. This is the single decision the whole "one scene" claim rests on, and honouring it **breaks every existing Solids look**, because Pyre's solid light is not a position in any shared space at all (see LR-0.5).

**B — the law.** One static entry point, `ShaperLightLaw.Shade`, taking blittable parameter blocks by `in`, a surface point, a **surface normal as an input**, and a view direction; returning **two** per-channel linear triples — a multiplicative term `L` and an additive term `S`. `final = albedo · L + S`. Two outputs, not one, so the law never needs to know the albedo and both families can call it with their own material. Diffuse is `N·L` with `1/(1+(d/range)²)` falloff; specular is Blinn-Phong on the half vector; rim is `pow(1 − clamp01(N·V), rimPower)` tinted by ambient and never attenuated. Everything is linear; the law never encodes; the one encode stays at `ShaperFillResolver.Encode` (`:1207`).

**C — the normal.** **The analytic route is the only one, and in Wave 2 the only Silhouette provider is `Constant` (default `(0,0,1)`).** Screen-space differencing of a height buffer is **FORBIDDEN in Wave 2** — not on purity grounds, but on two measured, concrete ones: the only differenceable buffer that exists is `ShaperFillBuffers.height` (`ShaperFillResolver.cs:227`, written at `:1046`), which is entirely *fill*-authored, so differencing it lights the paint instead of the surface; and `PaintTile` (`:836`) is handed a bare tile with no apron and no way to reach a sample outside it, so a differencing provider cannot be tile-independent without a signature change and a re-render of every owner over a grown rectangle. A fill's height delta therefore **does not contribute to the normal in Wave 2** — it accumulates and is consumed by nothing, exactly as it does today. The consequence is stated rather than hidden: **Silhouette's Wave-2 lighting is correct but nearly featureless**, and it becomes interesting the instant T-0109 lands a height profile, with zero change to the law, to Solids, or to the response block. That is the architectural claim this task exists to make true, and delivering it structurally complete and visually thin is the honest reading of the wave plan.

**D — shadows.** **Out of scope, and unimplementable on v1's resolve**, which is the decisive reason rather than a scheduling one: BC-2.3 says v1 implements "exactly one ray direction: straight down, orthographic… no march, no acceleration structure and no camera", and a shadow ray is by definition a ray *toward a light*, which is not straight down for any light worth authoring. The block carries `castShadows` and `receiveShadows` as authored, persisted booleans; neither changes a pixel; and the compiled rig raises a `hasUnimplementedShadow` diagnostic with a verbatim reason sentence, mirroring FC-4.4's shape exactly. **Rim IS implemented now**, with the equation and the reference app's own exponent.

**E — placement.** Lighting runs **inside `PaintTile`, per owner, immediately after that owner's fill emits and before the `coverageEff` weighting and the subtree accumulation** — the three lines at `ShaperFillResolver.cs:1012-1014`. The exclusivity partition is untouched by construction. **An `Add` fill is NOT lit** (a lamp does not dim in a dark room). **A border IS lit**, by its *host node's* response block and its host node's normal — never its own. A layer with `receiveLighting = false` passes albedo through **bit-identically to the pre-T-0108 build**, which is testable and is why it is not a silent no-op.

**F — Solids.** **A generator producing coverage + normal, going through the ordinary fill + light pipeline like everything else — not a composite.** Its inline lighting block is deleted and replaced by one call to the shared law. Its `shapeFill` becomes an ordinary Shaper fill; its specular tint moves to the response block; its facet edge lines are **kept and renamed** per BD-4.1 but **re-expressed** to be lit by the shared law rather than by the `k = clamp(0.25 + lit, 0, 1.15)` hack at `:4349`; its halo and inner glow stay in the generator for Wave 2. Coverage is **hard (0 or 1) for all five members**, declared as a limitation with a named owner. Three constraints are written down with citations, plus **a fourth the brief did not name: Ring breaks the convexity rule outright**.

**G — the limitation.** Three `public const string` fields on `ShaperLightRig`, following the shipped precedent of `ShaperQuantities.SurfaceDirectionRefusal` (`ShaperFillContract.cs:140`) and `SubtractRefusal` (`:152`) — a runtime-fetchable sentence, because Wave 2 ships no UI and an XML comment cannot be read by the task that eventually builds one.

**H — tests.** Sixteen, each with the source mutation that makes it fail. Including the one that makes "one shared law" falsifiable rather than asserted, and the one that makes "recorded but inert" honest by asserting *both* that nothing happens *and* that the tool says so.

## Eight corrections to the brief and to the documents this stands on

Stated first, because three of them change what the task can do and two of them contradict something a reader of the earlier documents would otherwise assume.

**LR-0.1 — There is no document in Shaper, and no layer either.** The design's C1 says "A Shaper document has a canvas size, a frame count and a rate, a palette, **one light rig**, and an ordered list of layers" (`SHAPER_THE_DESIGN.md:299`), and C6 opens "The document owns the lights". A grep over all eighteen files in `Runtime/Shaper/` finds **no `ShaperDocument` type, no `ShaperLayer` type, and no light or lighting symbol of any kind**. `ShaperFillDocument` (`ShaperFillResolver.cs:115`) is not a document — it is the resolved owner list `ShaperFillResolver.Resolve` returns (`:332`). A "layer" today is one `ShaperNode` root handed to `Resolve`, and FC-3.2's "layer root" means exactly that. **So "put the lights on the document" reads like an edit to an existing type and is in fact the introduction of the first document-level authored object in Shaper.** LR-1.1 takes that on deliberately rather than smuggling the rig onto a node.

**LR-0.2 — Three of the brief's seven `PyreRenderer.cs` line numbers point at the right region but the wrong line, and one names a function that does not exist.** Verified: `DrawFacetSolid` at `:4152` ✓, `DrawOrb` at `:4418` ✓, `BuildBoxGeometry` at `:4825` ✓, `BuildPyramidGeometry` at `:4869` ✓, `BuildCanGeometry` at `:4903` ✓. Corrected: the brief's "`:4608` (the flat-plane variant)" is `DrawRing`, whose header comment starts at `:4601` and whose signature is at `:4609`. The brief's "`:4788` (the face/edge resolver)" is not a function — `:4786` is the geometry-builder section header, `EdgeKey` is at `:4793` and `BuildGemGeometry` at `:4796`. **There is no separate face/edge resolver: face visibility and the line-edge set are resolved inline inside `DrawFacetSolid` at `:4224-4256`.** Nothing in this contract depends on the wrong numbers; recorded so the next reader does not go looking for a function.

**LR-0.3 — BC-3.3 #9's three "analytic 3D solids" citations are similarly off.** It names "`PyreRenderer.cs:4414` sphere, `:4212` facet, `:4635` ring plane" (`BUFFER_CONTRACT.md:214`). Current file: the sphere's normal is `Vector3 N = P / R;` at `:4533`; the facet's is `nrm = nrm.normalized;` at `:4236` (the region comment BC-3.3 probably meant is at `:4212-4213`); the ring plane's is `Vector3 N = new Vector3(syw, -cyw * st, cyw * ct);` at `:4639`. Two are within four lines, one is 119 lines out. The *substance* — that four of the five places already have the arithmetic a publisher needs — is confirmed and is what LR-6 builds on.

**LR-0.4 — `PyreField.ReliefLight`'s light is not a 2D angle; it is a 3D directional light whose azimuth alone is authored.** The brief says "`lightAngleDeg` is a 2D angle". It is the azimuth; the elevation is a separate parameter `lz`, defaulted to `0.72` and hard-wired at every call site (`PyreField.cs:191`, `:194-197`; called at `PyreRenderer.cs:1804` and `:1858`, neither passing it). `atan(0.72) = 35.75°`. **Pyre's solids default their key-light pitch to `38°` (`Pyre.cs:352`).** The two lineages' default key elevations agree to within 2.3°, which is real evidence that one shared rig is achievable rather than aspirational, and it makes the reconciliation the brief asks for materially easier than the brief implies.

**LR-0.5 — Solids' light is not in any shared space, and this is the hardest single fact in the task.** `ldist = layer.gemLightDistance * R` (`PyreRenderer.cs:4265`, mirrored at `:4437` for Orb and `:4643` for Ring), where `R` is **that particle's own evaluated radius** (`:4157`). The falloff range is `(gemLightDistance + 1.2f) * R` (`:4267`). So two solids of different size, with identical dials, are lit by lights at different absolute positions with different falloff ranges — and a solid whose `size` animates has a light that moves with it. The brief's "read both and reconcile them" understates the problem: `ReliefLight`'s light is a direction in canvas space and Solids' light is not a position in canvas space at all. LR-1.6 rules the break and gives the migration formula; LT-16 is the test that makes the ruling falsifiable.

**LR-0.6 — BD-4.1's reason #2 for keeping the facet edge lines out of the border stage is superseded by this document, and its ruling survives on its other two reasons.** BD-4.1 argues the facet edge lines cannot be a border because "it is lit, and a fill may not be… A fill emits unlit albedo by construction… Porting it to a border's fill would either lose the light response or smuggle lighting into the fill stage" (`BORDER-CONTRACT.md:215`). Under LR-5.1 a border's fill *is* lit — downstream of the fill stage, by the rig, with nothing smuggled anywhere — so that reason no longer holds. **BD-4.1's ruling is nonetheless unchanged**, because its reason #1 (the lines trace interior facet seams, which are nowhere in the zero set of any 2D field) and reason #3 (the `edist` measurement is shared with the halo at `:4366` and gates the inner glow at `:4373-4378`, so the painting is separable but the measurement is not) are each independently sufficient. **Flagged, not edited** — BD-4.1 is signed off.

**LR-0.7 — the relief-light contrast remap is at two call sites, not one, and the buffer contract's line number for it is stale.** BC-3.6 cites "a downstream remap of `0.35 + light × 1.15` (`PyreRenderer.cs:1867-1868`)" (`BUFFER_CONTRACT.md:248`). In the current file it appears twice: `:1821` in the Ramp shading pass (`value = Clamp01(value * (0.35f + light[i] * 1.15f))`) and `:1874` in `RenderHeightConsumer` (`float shade = 0.35f + light[i] * 1.15f`). LR-1.7 rules it is not carried across and gives the exact dial values that reproduce it, so the mapping is a conversion rather than a loss.

**LR-0.8 — the compositor's destination is PREMULTIPLIED, which FC-2.3's "non-premultiplied" describes the *albedo*, not the buffer.** `ShaperFillBuffers.dst` is documented as "PREMULTIPLIED linear RGB and alpha, 4 floats per sample" (`ShaperFillResolver.cs:224-225`), and `Encode` un-premultiplies before encoding (`:1207-1220`). This matters exactly once, and it is a trap worth naming: **a light term multiplied into a premultiplied destination scales alpha as well as colour, which is wrong.** LR-5.2 places lighting before premultiplication for that reason, and LT-9 is the test that catches the mistake.

---

# Part 1 — LR-1, the light list

## LR-1.1 The rule

> **`[D-T0108]` The document owns exactly one light rig. A light rig is a document ambient plus an ordered list of at most eight lights. No light is owned by a node, a layer, a fill, a border or a generator, and no stage may add one at render time.**

Design C6, restated as a clause. The reason it is a clause and not a convention: the failure this rule prevents is the one Pyre already has, where `gemAmbient` is a per-layer float (`Pyre.cs:358`) and `ReliefLight`'s ambient is a hard-wired default argument (`PyreField.cs:192`), so two layers in one picture are lit by two ambients that no author chose and no control shows. That is the "not one scene" defect at its smallest, and one rig is the whole of the fix.

Because there is no document type (LR-0.1), **T-0108 introduces one**: `ShaperDocument`, whose Wave-2 content is a canvas size, an ordered list of layer roots, and a `ShaperLightRig`. It is a `[Serializable]` class, flat, no `SerializeReference` hierarchy — the same shape `ShaperFillDef` and `ShaperBorderDef` take, for the same reason `ShaperBorderDef.cs:6-11` gives: neither nests, and a managed-reference hierarchy imports the nulls-against-a-broken-assembly hazard this project has already been bitten by.

## LR-1.2 What a light is

```csharp
public enum ShaperLightKind { Directional = 0, Point = 1 }

[Serializable]
public class ShaperLight
{
    public string name = "Light";
    public bool enabled = true;
    public ShaperLightKind kind = ShaperLightKind.Directional;

    /// Authored in sRGB (that is what Unity's picker gives). Decoded ONCE at compile through
    /// ShaperSrgb.Decode (ShaperFillContract.cs:436). Alpha is NOT read — FC-2.2's rule, applied to a light.
    public Color colour = Color.white;

    /// Multiplies the light's contribution. Animatable.
    public ZUIValue intensity = new ZUIValue(0.943f);

    // Directional: where the light comes FROM, in the canvas frame (LR-1.5).
    public ZUIValue yaw   = new ZUIValue(-55f);
    public ZUIValue pitch = new ZUIValue(36f);

    // Point: absolute canvas position, in canvas pixels, +Z toward the viewer.
    public ZUIValue posX = new ZUIValue(0f);
    public ZUIValue posY = new ZUIValue(0f);
    public ZUIValue posZ = new ZUIValue(40f);

    /// Point only. Canvas pixels. The distance at which attenuation reaches 1/2 (LR-2.4).
    public ZUIValue range = new ZUIValue(40f);

    /// Blinn-Phong highlight strength for THIS light. The highlight's COLOUR is the light's colour
    /// times the receiving layer's specularTint (LR-4.4) — a light does not carry a second colour.
    public ZUIValue specular = new ZUIValue(0.9f);
}
```

**Why no separate specular colour on the light.** Pyre carries one — `gemSpecularFill`, a whole `ZuiFill` defaulting to `(0.9, 0.95, 1)` (`Pyre.cs:368`) — and it is exactly the double-meaning defect `ShaperBlend` and `ShaperSweep` were both split to remove (`ShaperNode.cs:11-15`, `:30-37`): one lamp showing two different colours, with no rule saying which is the lamp. Physically the highlight colour is the *light's* colour for a dielectric and the *surface's* for a metal, so the surface half belongs on the receiver. Ruling: **the light carries one colour and a specular strength; the tint lives on the response block (LR-4.4).** The loss is named in LR-6.5: a *spatially varying* specular fill does not survive the port.

**Why no falloff-exponent dial.** One falloff, fixed (LR-2.4). A `range` and an `exponent` both control "how fast it dies", and two dials for one quantity is the same defect a fourth time. If a look genuinely needs a harder falloff it is a second light at a shorter range, which is one authored object rather than one authored ambiguity.

**Why no cookie, no area, no IES, no colour temperature.** Not refusals on principle — refusals on scope. Each is a new authored concept with its own conformance surface, and none of them is named anywhere in the design. Recorded here so their absence reads as a decision.

## LR-1.3 The ambient term is document-level, and is not per-light

> **`[D-T0108]` LR-1.3 — exactly one ambient term exists, on the rig: a colour and an intensity. A light carries no ambient.**

The argument, since the alternative is superficially attractive. Ambient is the term that stands in for all the light you did not model. Per-light ambient means N lights sum to N ambients, so **adding a second lamp brightens the shadows for no authored reason**, and **switching a lamp off darkens shadows that lamp was never pointed at** — both of which read as bugs and neither of which the author can undo without touching a dial on a different object. There is also a concrete instance of the failure in the code this ports from: the two lighting lineages carry two unrelated ambients (`PyreField.cs:192`'s `0.18f` default and `Pyre.cs:358`'s `gemAmbient = 0.05f`), and reconciling them is a third of the work of "read as one scene".

```csharp
public Color ambientColour = Color.white;
public ZUIValue ambientIntensity = new ZUIValue(0.18f);   // PyreField.cs:192's default, with provenance
```

**Ambient is NOT scaled by the per-layer `intensityScale` (LR-4.2), and that is deliberate.** A receiver dialled to zero intensity is *in shadow*: it renders at ambient. A layer with `receiveLighting = false` is *not in the lighting model at all*: it renders at exactly its albedo. Those are two different states and both are reachable and both are useful, and collapsing them would make "receive off" and "intensity 0" the same control twice.

## LR-1.4 Eight lights, hard cap, refused with a reason

> **`[D-T0108]` LR-1.4 — the rig holds at most eight lights. A ninth is refused at compile time with a diagnostic; it is never silently dropped and never silently rendered.**

**Why a cap at all.** The compiled rig is a fixed-size blittable struct so that `Shade` takes it by `in` with no indirection and no managed array (FC-5.2, BC-1.2's "a container a Burst kernel can actually address"). A variable-length rig means either a bulk-data indirection — which FC-5.5 reserves for things that are not floats — or a managed array reference inside the parameter block, which is precisely what BC-1.2's fourth bullet forbids.

**Why eight and not four or sixteen.** The per-sample cost is linear in the light count: at the design's measured worst-case canvas of ~67 840 cells (`BUFFER_CONTRACT.md:307`), eight lights is roughly 1.5 M multiply-adds per layer per frame before anything else, which is inside a responsive budget for a 96×64 default (~6 144 cells) and survives the worst case with the caching B9 requires. Four would refuse a key + fill + two accents, which is an ordinary authored setup. Sixteen doubles the floor cost of every document for a case nobody has asked for. Eight is a power of two, which keeps the struct's stride tidy. **This is the number in the contract most cheaply changed by the owner, and changing it touches one constant and one test.**

The refusal follows FC-4.4's shape and FT-8b's lesson exactly — the count is not decoration, because recording only the first offender is the mistake `ShaperProgram.leadingNonAddCount` exists because of (`ShaperProgram.cs:90-95`), and the fill stage already applied that correction from the start (`ShaperFillResolver.cs:108-114`):

```csharp
public bool hasTooManyLights;
public string tooManyLightsReason;   // "the rig holds 11 lights; only the first 8 are lit. The cap is 8."
public int    tooManyLightsCount;    // 3 — how many were dropped, not just that some were
```

## LR-1.5 The frame and the units — the clause everything else rests on

> **LR-1.5 `[D-T0108]` — a light lives in CANVAS space: canvas pixels, origin at the canvas centre, +X right, +Y up, +Z out of the screen toward the viewer. Height, surface normals, light positions, light ranges and edge distances are all in those same units, at every nesting depth. There is no second frame and no per-layer or per-particle rescale.**

This is the clause the whole "one scene" promise is made of, and it is affordable only because the shape engine already did the hard half. `ShaperSampleGrid.Centred` places sample (0,0) at `-0.5·(width−1)·pixelSize` on both axes (`ShaperEvaluator.cs:24-25`), so the sample grid is canvas-centred with +Y up. `ShaperField`'s header states the convention and names the mechanism that makes it hold at depth: "Units are canvas pixels at every level of the tree — the transform block's σ_min rescale is what guarantees that, so a blend width authored in pixels means the same thing at any nesting depth or scale. Frame is the layer's own local space, +Y up, origin at the layer origin" (`ShaperField.cs:181-184`), implemented as `stack[sp++] = d * ops[i].distanceScale` (`ShaperEvaluator.cs:72`) with `distanceScale` documented at `ShaperProgram.cs:60-66`.

The three consequences, each of which would otherwise be discovered:

- **Height is in canvas pixels.** BC-3.3 #2 says height is "layer-local units along the layer's own up axis, 0 = the base plane". For the flat resolve of v1 the layer's own up axis is +Z and the layer's own units are canvas pixels, so **a height of 4 is four pixels tall**. Pinning this now is what makes `dh/dd` dimensionless, which is what makes T-0109's analytic normal a pure ratio with no scale factor to get wrong.
- **A light's Z is real.** A point light at `(0, 0, 40)` is forty pixels out of the screen on a 96×64 canvas — comfortably outside it, which is what makes its attenuation vary across the canvas rather than blow out at the centre. The default `posZ = 40` and `range = 40` are chosen so `atten` at the canvas centre is exactly `1/(1 + 1) = 0.5`, which is the value LT-6 hand-checks.
- **The `edgeSoftness` and `pixelSize` of the grid do not enter lighting at all.** They are the coverage kernel's (`ShaperField.cs:215-232`) and coverage is not the law's business (LR-2.6).

## LR-1.6 The Solids light-position break, ruled and priced

> **LR-1.6 `[D-T0108]` — Solids' light stops being a multiple of the solid's own radius and becomes an absolute canvas position on the rig. This changes every existing Solids look. It is a deliberate break, converted once, not carried as a compatibility mode.**

The break is forced by LR-1.5 and there is no version of "one scene" that survives keeping the old behaviour: today `ldist = layer.gemLightDistance * R` (`PyreRenderer.cs:4265`) with `R` the particle's own evaluated size (`:4157`), so a 10 px solid and a 40 px solid in the same document are lit by lights 35 px and 140 px away, with falloff ranges of 47 px and 188 px. There is no single canvas position that reproduces both, so a compatibility mode would have to keep a per-layer light, which is LR-1.1 abandoned.

**The one-time conversion, so the break is a migration rather than a loss.** For a layer whose authored `size` evaluates to `R₀` at phase 0:

```
posX   = R₀ · gemLightDistance · sin(gemLightYaw) · cos(gemLightPitch)
posY   = R₀ · gemLightDistance · sin(gemLightPitch)
posZ   = R₀ · gemLightDistance · cos(gemLightYaw) · cos(gemLightPitch)
range  = R₀ · (gemLightDistance + 1.2)
```

read straight off `PyreRenderer.cs:4264-4267`. Applying it to the largest solid in a document and leaving the rest to be relit is the intended workflow; there is no automatic converter in Wave 2 and building one is not proposed. **This is the one ruling in this document I would expect the owner to want to overturn, and overturning it costs the whole of LR-1.5.**

## LR-1.7 The relief-light contrast remap is not carried across, and here is the dial pair that reproduces it

Pyre applies a second gain downstream of `ReliefLight` at two sites: `Clamp01(value * (0.35f + light[i] * 1.15f))` (`PyreRenderer.cs:1821`) and `float shade = 0.35f + light[i] * 1.15f` (`:1874`). That is a gain on top of `ReliefLight`'s own `ambient + ndl·gain` (`PyreField.cs:208`) — two dials for one quantity, the same defect LR-1.2 refuses on the light. **Not carried across.**

It is exactly reproducible, which is why dropping it costs nothing. Expanding: `0.35 + (0.18 + ndl·0.82)·1.15 = 0.557 + ndl·0.943`. So a ported Ramp or heightmap look is recovered by setting

```
ambientIntensity = 0.557          (= 0.35 + 0.18·1.15)
light.intensity  = 0.943          (= 0.82·1.15)      ← the default in LR-1.2, for this reason
```

on a directional light, with `atten ≡ 1`. Put that arithmetic in the comment on `ambientIntensity`, with the two line numbers, so the number is a provenance rather than a magic constant — which is BC-3.6's standing instruction ("All of these become named, defaulted dials with their provenance in a comment").

## LR-1.8 Lights animate, on the DOCUMENT clock, and this is the one exception to FC-1.4

> **LR-1.8 — every scalar on a light and on the ambient is a `ZUIValue`, sampled once per compile through `ShaperValue.Sample(v, phase01, seed)` (`ShaperValue.cs:26`), never from inside a tile loop. The `phase01` is the DOCUMENT's, not any node's. A light's `colour` is a plain `Color` and does not animate.**

Consistency with everything else in Shaper is the default and this follows it: `ShaperBorderDef.width` is a `ZUIValue` for exactly this reason and says so (`ShaperBorderDef.cs:65-70`), and `ShaperFillCompiler` resolves every fill dial the same way (FC-5.3). Using `ShaperValue.Sample` rather than `ZUIValue.Evaluate` is not style: `ZUIValue.Evaluate(t)` divides by the value's `duration` and would sweep only the curve's first quarter (`ShaperValue.cs:9-13`).

**The document clock is an exception and needs its argument.** `ShaperFillInputs.phase01`'s doc comment says "There is deliberately no separate document-clock input (FC-1.4): handing a fill a raw document clock as well as its node clock would give it two answers to 'when is it' and let a fill quietly ignore its node's window, which is the exact class of bug R3 exists to close" (`ShaperFillContract.cs:264-267`). That rule protects a thing *attached to a node* from ignoring the node's window. **A light is not attached to a node and has no window to ignore**, so there is no second answer to give it and nothing to bypass. The rig is compiled once per document per frame-time with the document phase; a node's own window remaps the node's clock and has no authority over the scene's lamps, which is also the intuitive reading — a light does not stop moving because one layer's animation finished.

**Colour does not animate, and the asymmetry is deliberate.** A fill's colour dials are plain `Color` decoded at compile (`ShaperFillOp.colR/colG/colB`, `ShaperFillCompiler.cs:118`), and an animated colour would want a `ZuiGradient`, which needs a `t` — a fill's `t` comes from its own spatial parameterisation and a light has none. If an animated light colour is wanted later it arrives as a gradient sampled at `phase01`, which is a one-field addition; naming that here is what stops it becoming a surprise.

---

# Part 2 — LR-2, the shared shading law

## LR-2.1 The rule

> **LR-2.1 — Silhouette and Solids call one shared shading law, implemented exactly once, as a static entry point on blittable parameter blocks. The law takes a surface normal as an INPUT. It never derives one, never reads a sheet, never allocates, and never knows what coverage is.**

Design C6, verbatim in substance. BC-3.6 states the same rule from the other side and makes it mandatory: "The shading stage MUST receive a surface direction published by the shape. It MUST NOT derive one by differencing a depth or height buffer" (`BUFFER_CONTRACT.md:230`).

## LR-2.2 The signature, exactly

```csharp
namespace Laubrary.Shaper
{
    /// The compiled rig: fully blittable, fixed size, taken BY `in`. No managed reference arrives
    /// through the parameter (BC-1.2), and the cap (LR-1.4) is what lets it be a fixed-size struct
    /// rather than a bulk-data indirection.
    public struct ShaperLightCompiled
    {
        public int   kind;                    // 0 = Directional, 1 = Point. int, not enum-in-struct-alignment games.
        public float dirX, dirY, dirZ;        // Directional: UNIT vector TOWARD the light. Precomputed at compile.
        public float posX, posY, posZ;        // Point: canvas pixels.
        public float r, g, b;                 // LINEAR colour × intensity, folded at compile.
        public float invRangeSq;              // 1 / range², precomputed. Zero for a directional light.
        public float specular;                // this light's Blinn-Phong strength.
    }

    public struct ShaperLightRigCompiled
    {
        public const int MaxLights = 8;       // LR-1.4
        public int count;
        public ShaperLightCompiled l0, l1, l2, l3, l4, l5, l6, l7;   // fixed, blittable, no array
        public float ambR, ambG, ambB;        // LINEAR ambient colour × ambient intensity, folded at compile.
    }

    /// The per-layer response block, compiled. LR-4.
    public struct ShaperResponseCompiled
    {
        public int   receive;                 // 0 or 1. int, not bool (bool has no guaranteed blittable width —
                                              // the same reason ShaperFillOp stores booleans as ints,
                                              // ShaperFillCompiler.cs:88).
        public float intensityScale;
        public float rimStrength, rimPower;
        public float specular, specularPower;
        public float specTintR, specTintG, specTintB;   // LINEAR
        // castShadows / receiveShadows are NOT here: they change no pixel (LR-4.5) and a field the law
        // can see is a field the law will eventually be blamed for.
    }

    public static class ShaperLightLaw
    {
        /// THE shading law. One implementation, called by every family (LR-2.1). Enforced by LT-1b.
        ///
        /// Returns TWO per-channel linear triples, and that is the structural decision of this part:
        ///   final = albedo · (lr,lg,lb) + (sr,sg,sb)
        /// A single combined output would force the law to be handed the albedo, at which point it stops
        /// being a law and becomes a shading pipeline that each family has to feed differently.
        ///
        /// It does NOT clamp. L and S may exceed 1; an overbright highlight is legitimate and the only
        /// clamp is at the byte (ShaperSrgb.EncodeToByte, ShaperFillContract.cs:444-449), which is also
        /// what Pyre's Over already does.
        public static void Shade(
            in ShaperLightRigCompiled rig,
            in ShaperResponseCompiled resp,
            float px, float py, float pz,          // surface point, canvas pixels (LR-1.5)
            float nx, float ny, float nz,          // surface normal, UNIT, canvas frame. AN INPUT.
            float vx, float vy, float vz,          // unit direction toward the viewer. v1: (0,0,1).
            out float lr, out float lg, out float lb,
            out float sr, out float sg, out float sb);
    }
}
```

**Every parameter is a `float` or an `in` struct of `float`s and `int`s. No array, no sheet, no interface, no delegate, no `Color`, no `Vector3`.** That is not stylistic austerity: BC-4.2's own correction says the blittable signature "closes one door: nothing managed can arrive *through the parameter*", and LT-13's reflection assertion — *the parameter list of `Shade` contains no array type* — is the mechanical, always-runnable enforcement of BC-3.6 that BC-4.2's table asks for ("grep-level assertion that no shading code path differences a height or depth buffer"). A signature with no array in it cannot difference a buffer, and that is checkable by reflection in four lines.

`Vector3` is deliberately not used even though it is blittable, because a `Vector3` invites `Vector3.Cross`/`.normalized`/`.magnitude`, and `.normalized` is a property call that is not free in a hot loop; the shipped shape engine makes the same choice, passing twelve loose floats through `ShaperSdf.Evaluate` (`ShaperEvaluator.cs:67-71`).

## LR-2.3 The arithmetic, as equations

All in LINEAR, non-premultiplied, per channel. `·` is scalar multiply, bold names are triples.

```
L  =  amb                                                    ← the document ambient, already colour × intensity
S  =  0

for each enabled light i in 0..count-1:

    if kind_i == Directional:
        Ldir  =  (dirX, dirY, dirZ)_i                        ← unit, precomputed at compile, points TOWARD the light
        atten =  1
    else:                                                    ← Point
        dv    =  pos_i − P
        dist  =  |dv|
        Ldir  =  dv / max(dist, 1e-4)
        atten =  1 / (1 + dist² · invRangeSq_i)               ← LR-2.4

    ndl   =  max(0, N · Ldir)
    w     =  atten · resp.intensityScale
    L    +=  colour_i · ndl · w                              ← DIFFUSE, multiplicative

    Hv    =  normalize(Ldir + V)                             ← Blinn-Phong half vector
    nh    =  max(0, N · Hv)
    spec  =  pow(nh, resp.specularPower) · resp.specular · specular_i
    S    +=  colour_i · resp.specTint · spec · w             ← SPECULAR, additive

rim   =  pow(1 − clamp01(N · V), resp.rimPower) · resp.rimStrength
S    +=  amb · rim                                           ← RIM, additive, ambient-tinted, NOT attenuated

final =  albedo · L  +  S
```

**Order of colour mixing, fixed so two implementations cannot disagree:** ambient first (it is the accumulator's initial value, not a term added at the end — adding it last would make it survive `intensityScale`, which LR-1.3 forbids); then each light's diffuse into `L` and specular into `S` in rig order; then rim into `S` last. Rig order is authored order and no stage may reorder it — the same clause R1 gives for a bag's members and FC-3.4 gives for paint order, applied here for the same reason: floating-point summation is not associative, and two lists that look identical must not disagree about which end is which.

**`resp.intensityScale` gates diffuse and specular and NOT ambient and NOT rim.** Ambient because LR-1.3. Rim because rim stands for grazing-angle light from everywhere and a layer dialling down its response to the *lamps* has not dialled down the sky.

## LR-2.4 Falloff, and why this exact form

```
atten = 1 / (1 + dist² / range²)
```

Ported in form from `PyreRenderer.cs:4343` (`float atten = 1f / (1f + dist * dist / lrange2)`), with `range` promoted from `(gemLightDistance + 1.2f) · R` (`:4267`) to an absolute canvas dial (LR-1.6). Three properties, all of which matter and none of which the obvious alternatives have: it is exactly `1/2` at `dist == range`, which makes `range` mean something an author can predict and is what LT-6 hand-checks; it never reaches zero, so a light never produces a hard cutoff circle; and it costs one multiply, one add and one divide with no `pow` and no `sqrt` beyond the `dist` the direction already needed.

A **Directional** light has no falloff at all: `atten ≡ 1`, `invRangeSq` compiles to `0`, and the branch is on `kind` which is uniform across the tile. Stated because "a directional light with a range" is a thing people add by accident.

## LR-2.5 Rim, and where its exponent comes from

```
rim = pow(1 − clamp01(N · V), rimPower) · rimStrength
```

**`rimPower` defaults to `2.2`, and that is not a taste.** The reference app's Fresnel term is `pow(1 - clamp01(normalZ), 2.2)` (`index.html:1510`, quoted at `BUFFER_CONTRACT.md:246`). With `V = (0,0,1)` — v1's only view direction — `N · V` is exactly `nz`, so **the two expressions are identical**, and adopting 2.2 is a reconciliation rather than a guess. Put that equivalence in the comment; it is the kind of fact that gets re-derived expensively.

**One trap the reference app itself walks into, named so it is not inherited.** BC-3.6 records that the reference's `normalZ` is read by the Fresnel term *after* normalisation but *not through a dot product* (`BUFFER_CONTRACT.md:246-248`), which is why `normalZBase` and `slopeGain` are not interchangeable there. Here the law's `N` is contractually **unit** (LR-3.5), so `N · V` and `nz` cannot diverge — the asymmetry disappears by construction rather than by care. LT-7's mutation is exactly this: computing the rim from a non-normalised `nz`.

**Rim is tinted by the ambient colour and is not attenuated.** Argued in LR-2.3. The visible consequence, which must be in the UI string (LR-7.2): **on a flat surface `N · V = 1`, so `rim = 0` identically.** In Wave 2 every Silhouette layer has a flat normal (LR-3.2), so rim is visible on Solids and on a `Constant`-tilted layer and nowhere else. That is arithmetic, not a bug, and stating it is the difference between an honest limitation and a knob that appears broken.

## LR-2.6 What the law does NOT do

Enumerated, because each one is a thing somebody will try to add:

- **It does not know about coverage, alpha, veil or opacity.** It is called per sample with a point and a normal and returns two colour triples. Coverage weighting happens after it, in the compositor, where FC-2.4's authority already lives.
- **It does not know about shadow casting or receiving.** `ShaperResponseCompiled` does not carry those flags (LR-2.2) precisely so the law cannot be blamed for them later.
- **It does not difference anything.** Enforced structurally by the signature (no array parameter) and mechanically by LT-13.
- **It does not encode or decode sRGB.** `ShaperSrgb.EncodeToByte` must have exactly one caller in the runtime assembly, `ShaperFillResolver.Encode` (`:1207`); asserted by LT-5b.
- **It does not clamp.** `L` and `S` may exceed 1. The clamp is at the byte (`ShaperFillContract.cs:430`, `:448`).
- **It does not read the fill, the shape, the grid, or any sheet.** `ShaperSampleGrid` is not a parameter, so `pixelSize` and `edgeSoftness` cannot leak into shading.
- **It does not allocate, box, use LINQ, or call `System.Random`.** FC-5.1's clause, applied unchanged. Asserted by LT-2.

---

# Part 3 — LR-3, the normal provider — the decision that makes or breaks Wave 2

## LR-3.1 The ruling

> **LR-3.1 `[D-T0108]` — the law's normal comes from a NORMAL PROVIDER: a named, declared, swappable stage that writes a `float[3n]` sheet of unit vectors in the canvas frame, one per sample, before the law runs. The provider interface IS that sheet plus a declaration. Anything that can write the sheet is a provider. The law reads the sheet and knows nothing about who wrote it.**

**Wave 2 ships two implementations, and they are genuinely different machinery, which is what makes the interface real rather than a single-implementation fiction:**

1. **`ShaperNormals.FillTile`, kind `Constant`** — Silhouette's. Writes an authored unit direction, defaulting to `(0,0,1)`. Consumes no sheet.
2. **The Solids generator writes the sheet directly** from its own geometry — the facet normal at `PyreRenderer.cs:4236`, the sphere's `N = P/R` at `:4533`, the ring plane's constant `N` at `:4639` (line numbers corrected in LR-0.3). It is not a case in the enum and does not go through `ShaperNormals`; it is a second writer of the same array.

That structure is what makes design C6's promise mechanically true: **T-0109 adds a `Profile` case to `ShaperNormals.FillTile` and touches neither `ShaperLightLaw` nor one line of Solids.** LT-13 is the test.

```csharp
public enum ShaperNormalKind
{
    Constant = 0,
    // Profile = 1 — T-0109. Reserved, named now so its arrival is an addition and not a renumbering
    // (the same APPEND-ONLY discipline ShaperQuantity and ShaperFillKind already carry,
    // ShaperFillContract.cs:9-13, :206).
}

/// Flat, blittable, one struct and a switch — FC-5.2's form, for the same reason: a provider does not
/// nest, so its "program" is a single op struct and the flat form is SIMPLER than a class hierarchy.
public struct ShaperNormalOp
{
    public ShaperNormalKind kind;
    public float cx, cy, cz;               // Constant: the authored UNIT direction (normalised at compile).

    // ── BC-3.6's three dials, named NOW with their provenance, unused until T-0109 ──
    // BC-3.6 is explicit: "All of these become named, defaulted dials with their provenance in a comment.
    // None of them is a magic number in the new code" (BUFFER_CONTRACT.md:248). Naming them here
    // discharges that instruction in Wave 2 and gives T-0109 a calibration target rather than a memory.
    public float slopeGain;                // 0.65  — index.html:1491. Scales dh/dd into the tangent components.
    public float normalZBase;              // 1.4   — index.html:1492. The out-of-screen component tilt is measured against.
    public float reflectionFlatten;        // 1.5   — index.html:1492, the undocumented third dial BC correction 3 found.
    // Only the RATIO slopeGain/normalZBase ≈ 0.464 sets tilt sensitivity (a unit height difference tilts
    // the normal by atan(0.65/1.4) ≈ 24.9°), but they are not interchangeable, because normalZ is also read
    // on its own by the Fresnel term — BUFFER_CONTRACT.md:246. Under LR-2.5 that asymmetry does not
    // reproduce here, because this law's N is contractually unit; kept as two dials anyway so a ported
    // look has both of the reference's handles.
}

public static class ShaperNormals
{
    /// Mirrors ShaperFillOps.FillTile exactly (ShaperFillOps.cs:48-53), including srcOffset/srcStride and
    /// dstOffset/dstStride, so provider, shape and fill all tile identically and one host loop drives all three.
    /// `distance` and `height` MAY be null — a provider reads only what it declared.
    /// Writes 3 floats per sample at 3·(dstOffset + row·dstStride + i). Every written vector is UNIT.
    public static void FillTile(in ShaperNormalOp op, in ShaperSampleGrid grid,
                                int x0, int y0, int width, int height,
                                float[] distance, float[] heightSheet,
                                float[] normal,
                                int srcOffset, int srcStride,
                                int dstOffset, int dstStride);
}
```

## LR-3.2 What Silhouette's normal actually is in Wave 2, and the honest consequence

> **LR-3.2 `[D-T0108]` — Wave 2's only Silhouette provider is `Constant`, defaulting to `(0,0,1)`. There is no height profile, because a height profile is T-0109's (`FILL-CONTRACT.md` F8.2: "no bevel profile… no height *profile*"), and taking it here would be scope theft that collides with a scheduled task.**

**Why the fully analytic route the brief describes cannot be completed here, worked through rather than asserted.** The brief's option is `n = normalize(−(dh/dd)·∇d, 1)`, and it is right that both halves are obtainable per-point with no neighbour differencing. `∇d` in particular is available *legally and cheaply*: `ShaperEvaluator.Distance` is a pure function of a canvas point, not of a sample index (`ShaperEvaluator.cs:50`), so a central difference of the **continuous field** at ±ε canvas units costs four extra `Distance` calls and is not a buffer read at all — BC-2.4 puts that squarely in the *legal* category, and tile independence holds structurally because the value at a sample depends only on that sample's canvas point and a fixed ε. **But `dh/dd` requires `h`, and there is no `h`.** The shipped engine writes exactly two sheets, `distance` and `coverage` (`ShaperEvaluator.cs:160-161`), and `ShaperQuantitySet.ShippedShapeEngine` declares precisely that (`ShaperFillContract.cs:79`). Without a profile, `∇d` alone gives a **constant-magnitude tilt everywhere inside the silhouette** — because `|∇d| = 1` for a distance field, the surface would read as an infinite cone rather than a bevelled card. So the analytic normal is not merely unavailable in Wave 2; it is *meaningless* until a profile exists.

**The consequence, stated rather than hidden.** Under `Constant(0,0,1)` a Silhouette layer is lit and the lighting is real and correct: `N · Ldir` gives a genuine per-light term; a **point** light's `atten` and `Ldir` both vary across the canvas, so two layers at different canvas positions are genuinely differently lit and genuinely read as being in one scene; colour mixing across several lights is exercised; ambient is exercised. What is **not** there: no relief, no specular except where a light is near the view axis, and **rim identically zero**. So Wave 2's Silhouette lighting is *structurally complete and visually thin*, and T-0109 turns it on by adding one enum case. That is the trade this wave is making and it should be stated to the owner in those words rather than discovered from a flat-looking picture.

**`Constant` is not a placeholder and is shipped as a real feature.** An authored constant tilt is a legitimate look — a flat card angled like a signboard, lit consistently with everything else in the document — and it costs three dials. It is also what makes LT-1 (the two families produce the same value from the same law given the same normal) and LT-13 (the provider is swappable) possible at all, because it can be driven to a *known* normal that the Solids path can be made to match.

## LR-3.3 Screen-space differencing is FORBIDDEN in Wave 2 — and here is exactly what it violates and what it would cost

> **LR-3.3 `[D-T0108]` — no normal provider in Wave 2 may read the screen-space neighbours of any intermediate buffer. A differencing provider is permitted in a LATER wave only if it is registered as an image-space stage under BC-4.3, named in the tile-independence exclusion list, and excluded from the tilt conformance render. Wave 2 registers no such stage, and the exclusion list gains nothing from this task.**

The brief asks whether the prohibition extends to a separate, clearly-labelled provider stage running before the law, and asks for a ruling rather than a hedge. It is forbidden, and the reasons are concrete rather than doctrinal.

**What it would violate.** BC-2.1 forbids any pre-resolve stage reading "the screen-space neighbours of an intermediate buffer", and BC-2.4's illegal category is exactly "neighbourhood work on an intermediate screen-space buffer that is neither [the shape's own domain nor the resolved picture]". A per-tile height accumulator is that buffer precisely. BC-2.5 rows #5 and #6 name relief lighting by name and rule that "under BC-3 the normal is published, so the finite difference **disappears rather than being ported**". Against that, BC-3.6 does say "v1's Silhouette normal is still a finite difference of its own height field — that is fine, because it is the height-field implementation's private business rather than the shading stage's" (`BUFFER_CONTRACT.md:234`). **Those two clauses are in genuine tension and the tension is resolvable on a fact BC-3.6 assumed and Wave 2 does not have: BC-3.6's permission is for differencing *its own height field*, and in Wave 2 the shape publishes no height field at all.**

**What it would cost, measured against the shipped code rather than estimated.**

- *It would light the wrong thing.* The only differenceable buffer is `ShaperFillBuffers.height` (`ShaperFillResolver.cs:227`), and every value in it comes from `buf.height[i] += buf.heightDelta[i] * ce` (`:1046`) — that is, **entirely from fills**. Differencing it produces a normal describing the paint's bumps, not the surface's, which inverts B4's central ruling ("shine belongs to the lights", `SHAPER_THE_DESIGN.md:116`) at the first opportunity.
- *It cannot be tile-independent without a contract change.* `PaintTile` takes `(x0, y0, width, height)` and `ShaperFillBuffers.height` is `new float[n]` with `n = width·height` (`:262`), cleared per tile at `:277`. There is **no apron and no mechanism to supply one**. Providing a 1-pixel apron means growing every tile by 2 in each dimension and re-running, for every owner, both `ShaperEvaluator.FillTile` (`:855`) and `ShaperFillOps.FillTile` (`:1002`) over the grown rectangle. For BC-1.6's prescribed 7×5 prime decomposition that is `(9·7)/(7·5) = 1.80×` the work — an 80% increase on the entire fill stage, paid on every tile, to obtain a normal describing the wrong surface.
- *It is the exact bug the enforcement instrument exists to catch.* LT-3's stated mutation is "implement the normal provider by differencing `buf.height`", and it seams at every tile boundary. So LR-3.3 is not an unenforced preference: tile independence is its continuous check, running every build.

**Both objections evaporate in T-0109** — when the *shape* publishes height, the analytic route becomes available *and* correct, so the differencing provider is never needed rather than merely deferred. That is why forbidding it now costs nothing later.

## LR-3.4 A fill's height delta does not reach the normal in Wave 2

> **LR-3.4 `[D-T0108]` — of the three available answers, this contract takes the first: a fill's height delta does NOT contribute to the surface normal in Wave 2. It continues to accumulate into `ShaperFillBuffers.height` (`ShaperFillResolver.cs:1046`) and is consumed by nothing.**

The fork, and the price of each branch:

- **(a) It contributes nothing.** *Taken.* Costs: a fill that authors a bump does not light up, so the indexed strip's per-slot height (T-0110) and Tapestry's height fields (T-0111) both arrive before anything reads them. That is already the state FC-2.5 shipped knowingly — "Nothing consumes it in T-0106… That is not a reason to defer it: it is the hook that makes T-0110's indexed strip one new fill kind and zero contract changes" (`ShaperFillContract.cs:371-373`) — so this branch changes nothing and breaks nothing.
- **(b) Fills additionally emit a height gradient.** *Refused.* It reopens a signed-off contract: FC-2.1 says a fill "emits albedo, always; a veil, always; and a height delta… It emits nothing else", and F8.1 names the height delta as "the entire interface" T-0108 needs. A fourth output means a fourth host array, a fourth declaration, a fourth entry in FT-12's declared-set-equals-read-set check, and a gradient that three of the four shipped fill kinds cannot produce analytically anyway (a point-lookup LUT has no derivative — FC-5.4 rules the lookup deliberately non-interpolating, so a Gradient fill's albedo is a step function and its height would be too).
- **(c) A differencing provider picks it up for free.** *Refused by LR-3.3*, and note that "for free" is false: the apron costs 80% of the fill stage per tile.

**The visible consequence, stated:** the task's own framing — "the height-channel-to-relief-lighting machinery already exists and is what a shared rig needs underneath it" — is true about `PyreField.ReliefLight` (`PyreField.cs:191-210`) as a *design precedent* and false as a *portable component* in Wave 2, because the thing it needs underneath it is a height field the shape publishes, and no shape publishes one until T-0109. What Wave 2 ports from it is not the code: it is the three constants, reconciled and named (LR-0.4, LR-1.7, LR-3.1's dial block).

## LR-3.5 What every provider guarantees

> **LR-3.5 — every vector a provider writes is UNIT to within 1e-4, finite, and in the canvas frame of LR-1.5. A provider never writes NaN. A provider that cannot produce a direction at a sample writes `(0,0,1)`, never leaves the sample unwritten and never writes zero.**

Unit is a contract rather than a courtesy: the rim term reads `N · V` directly and LR-2.5's identity with the reference app's `normalZ` holds only for a unit `N`. Zero is refused because `N · L = 0` everywhere would silently render an unlit black layer with no diagnostic — the silent-inertness failure BC-3.1 names as "the single most-reported confusion in the existing tool". `(0,0,1)` is the honest degenerate answer and it is the same choice the shipped engine already makes for a singular transform, publishing the empty field and flagging rather than dividing (`ShaperCompiler.cs:131-142`, cited at `ShaperFillCompiler.cs:113-115`). LT-14 asserts unit-ness across a dense sweep; the mutation that fails it is omitting the normalise in the Solids facet path (`PyreRenderer.cs:4236`).

---

# Part 4 — LR-4, the per-layer response block

## LR-4.1 The block

> **LR-4.1 — every layer carries exactly one response block. It is authored on the layer, compiled into `ShaperResponseCompiled` at compile time, and is the only per-layer lighting authority. A node does not carry one; a fill does not carry one; a border does not carry one (LR-5.4).**

```csharp
[Serializable]
public class ShaperLightResponse
{
    // ── the four the design names (SHAPER_THE_DESIGN.md:192) ──
    public bool      receiveLighting = true;                    // LR-4.3
    public ZUIValue  intensityScale  = new ZUIValue(1f);        // LR-4.2
    public bool      castShadows     = false;                   // LR-4.5 — recorded, computes nothing
    public bool      receiveShadows  = false;                   // LR-4.5 — recorded, computes nothing
    public ZUIValue  rimStrength     = new ZUIValue(0f);        // LR-4.6

    // ── three added here, argued in LR-4.4 ──
    public ZUIValue  specular        = new ZUIValue(0.9f);      // Pyre.cs:363
    public ZUIValue  specularPower   = new ZUIValue(48f);       // Pyre.cs:367
    public Color     specularTint    = new Color(0.9f, 0.95f, 1f);  // Pyre.cs:368's default

    // ── the surface-direction provider for this layer (LR-3.1) ──
    public ShaperNormalKind normalKind = ShaperNormalKind.Constant;
    public Vector3          normalConstant = new Vector3(0f, 0f, 1f);

    public ZUIValue  rimPower = new ZUIValue(2.2f);             // LR-2.5, from index.html:1510
}
```

## LR-4.2 `intensityScale`

Multiplies each light's diffuse and specular contribution (LR-2.3's `w`). Does **not** scale ambient or rim (LR-1.3, LR-2.3). Animatable, sampled at compile like every other dial. `0` means the layer is lit by ambient and rim only — *in shadow* — which is a different and reachable state from `receiveLighting = false`, and the difference is the point of having both.

## LR-4.3 `receiveLighting` off is bit-identical to the pre-T-0108 build

> **LR-4.3 `[D-T0108]` — `receiveLighting = false` produces `L = (1,1,1)` and `S = (0,0,0)`. The albedo is written through unchanged, bit for bit.**

This is what "pass through unlit" means mechanically, and it is deliberately not a no-op in the pejorative sense, because it is *testable*: LT-8 renders T-0107's conformance fixture with a fully populated eight-light rig and every layer set to `receiveLighting = false`, and asserts the output is bit-identical to the stored T-0107 golden. A build that quietly applied ambient, or quietly added `S`, fails immediately.

**A layer that receives nothing is still visible in the diagnostics.** The compiled rig records `receiverCount`, and a document holding at least one enabled light with **zero** receivers raises `hasLightsButNoReceivers` with a reason sentence. That is the exact silent-inertness case B4 names — an author adds three lights, sees nothing change, and has no way to find out why — and the diagnostic is the runtime analogue of the greyed control, following FC-4.4's reasoning unchanged ("the control is present, its reason is stated, and the thing it would have done does not happen").

## LR-4.4 Three fields added beyond the design's four, and the argument for each

The design names four knobs (`SHAPER_THE_DESIGN.md:192`). This contract adds `specular`, `specularPower` and `specularTint`, and the addition is flagged rather than smuggled.

**Why they are needed.** Without them the Solids port is lossy in a visible way: `gemSpecular` (`Pyre.cs:363`, default 0.9), `gemSpecPower` (`:367`, default 48) and `gemSpecularFill` (`:368`, default `(0.9, 0.95, 1)`) have no home, and the Blinn-Phong hotspot that is the entire visual signature of a Pyre gem disappears. Three fields against a lost feature is the right trade.

**Why they are on the LAYER and not on the LIGHT.** A specular exponent is a statement about how rough the *surface* is, and a highlight tint is a statement about whether the surface is a dielectric (highlight takes the light's colour) or a metal (highlight takes the surface's). Putting them on the light means two lights in one document cannot disagree about a surface's roughness, which is exactly backwards. Putting them on the layer makes the response block the "material" slot, honestly named.

**Why this does not violate FC-8.1's refusal of a "reflective or metallic dial on any fill".** F8.1 refuses such a dial *on a fill*, quoting B4's "Reflective, shiny, bevelled and lit are not fills. They are what happens *after* a fill". These are not on a fill. They are on the layer's lighting response — which is precisely the "after a fill" slot B4 points at, and which the design itself calls "a small per-layer response block". Recorded because the boundary is thin and someone will test it.

**What is lost.** `gemSpecularFill` is a full `ZuiFill` and can be `Linear`, `Radial` or `Noise` (evaluated per pixel at `PyreRenderer.cs:4325` when `specSpatial`). `specularTint` is a flat `Color`, so **a spatially varying specular tint does not survive the port.** Whether any authored asset uses one is **UNVERIFIED** — I did not read any `.asset`. If one does, the recovery is an ordinary Shaper fill on a second node, not a re-added `ZuiFill`.

## LR-4.5 Shadows: recorded, inert, and diagnosed — the ruling

> **LR-4.5 `[D-T0108]` — `castShadows` and `receiveShadows` are authored, serialized and persisted. Neither changes a single pixel in Wave 2. Both are absent from `ShaperResponseCompiled`, so the law cannot see them. Any layer setting either raises a compile-time diagnostic carrying a fixed reason sentence, and a UI reading that diagnostic MUST present the controls as not-yet-computed.**

**Why not implement them.** Not merely unscheduled — **unimplementable on v1's resolve, and this is the decisive reason.** BC-2.3: "v1 implements exactly one ray direction: **straight down, orthographic**… There is no march, no acceleration structure and no camera in v1" (`BUFFER_CONTRACT.md:116-118`). A shadow test is by definition a ray from a surface point *toward a light*, which is straight down only for a light directly overhead — i.e. for the one light configuration that casts no interesting shadow. Building shadows therefore means building the general march, which BC-4.4 explicitly assigns elsewhere and which BC-2.7 says is first exercisable in T-0109. Separately, the design describes **no shadowing algorithm anywhere**: C6 names the knobs and stops, and there is no clause anywhere on penumbra, bias, self-shadowing or cross-layer occlusion.

**Why carry the flags at all rather than omit them.** Omitting them means the document format changes when shadows land, so every document authored between now and then silently loses the author's intent, and the eventual task has a migration instead of an addition. Two booleans are the cheapest possible way to keep the intent.

**How the knob is presented, so it is not the silent-inertness failure.** The diagnostic block mirrors FC-4.4's shape exactly — a boolean, the first offender's name, the full reason sentence, and a count, with the count present from the start because `ShaperProgram.leadingNonAddCount` exists precisely because recording only the first offender was already got wrong once (`ShaperProgram.cs:90-95`):

```csharp
public bool   hasUnimplementedShadow;
public string unimplementedShadowReason;   // ShaperLightRig.ShadowsNotComputed, verbatim (LR-7.3)
public string unimplementedShadowNode;     // the first layer that asked
public int    unimplementedShadowCount;    // how many did
```

**Nothing-but-recorded is an acceptable ruling only because LT-12 makes it honest**: that test asserts *both* that turning every shadow flag on changes no byte *and* that the tool says so with a non-empty reason naming the layer. A build that quietly implemented shadows fails the first half; a build that dropped the diagnostic fails the second.

## LR-4.6 Rim strength is real and is implemented now

Equation in LR-2.5. `rimStrength` defaults to `0` — off — because a rim is an authored look rather than a physical default, and because a non-zero default would put a halo on every layer of every existing document the moment the rig lands. `rimPower` defaults to `2.2` with the provenance in LR-2.5.

Its Wave-2 reach is stated rather than implied: visible on Solids immediately (a sphere has grazing angles everywhere near its silhouette; a facet body has them wherever a face turns away), zero on any `Constant(0,0,1)` layer by arithmetic, non-zero on a `Constant`-tilted layer by a fixed amount everywhere. That sentence is `ShaperLightRig.RimNeedsRelief` (LR-7.2).

---

# Part 5 — LR-5, where lighting sits in the pipeline

## LR-5.1 The placement

> **LR-5.1 `[D-T0108]` — lighting runs INSIDE `ShaperFillResolver.PaintTile`, per owner, immediately after that owner's fill has emitted albedo/veil/heightDelta and BEFORE the `coverageEff` weighting and the subtree accumulation. It is not a stage between the fill stage and the border stage; it is a per-sample transform applied at the moment each owner emits, and it applies to borders and non-borders by the same code.**

Concretely, the three lines at `ShaperFillResolver.cs:1012-1014`

```csharp
buf.subtree[s4 + 0] += buf.albedo[a3 + 0] * ce;
buf.subtree[s4 + 1] += buf.albedo[a3 + 1] * ce;
buf.subtree[s4 + 2] += buf.albedo[a3 + 2] * ce;
```

become

```csharp
buf.subtree[s4 + 0] += (buf.albedo[a3 + 0] * lr + sr) * ce;
buf.subtree[s4 + 1] += (buf.albedo[a3 + 1] * lg + sg) * ce;
buf.subtree[s4 + 2] += (buf.albedo[a3 + 2] * lb + sb) * ce;
```

with `(lr..sb)` produced by one `ShaperLightLaw.Shade` call reading `buf.normal` at this sample. The identical substitution is made in the border block at `:1085`ff.

## LR-5.2 Why exactly there, and not one line later

Three reasons, each of which rules out a placement someone would otherwise pick.

- **Not after premultiplication.** `ShaperFillBuffers.dst` and `.subtree` are *premultiplied* linear RGBA (`:224-225`, `:230-244`, and LR-0.8). Multiplying a premultiplied destination by `L` scales its alpha too, which is silently wrong and produces exactly the transparent-seam class of defect T-0106's fix pass measured at 244/16384 samples. LT-9 is the test.
- **Not after the `coverageEff` weighting.** Lighting scales colour; coverage is a separate authority (FC-2.4, FC-2.4b). Applying `L` to `albedo · ce` gives the same number for the multiplicative half but a **wrong** one for the additive half, because `S` would be weighted once instead of gated by coverage — an additive rim would spill outside the shape.
- **Not once per document after all owners.** The response block is per layer and the normal provider is per owner (a border's strip and its host's face need different sheets when a future provider varies). Once the owners are mixed into `dst` they are not separable — the same argument `ShaperFillBuffers.subtree`'s own doc comment already makes for the exclusivity fix (`:230-243`).

## LR-5.3 An additive fill is NOT lit

> **LR-5.3 `[D-T0108]` — when an owner's compiled composite is `Add`, the law is skipped and `L = (1,1,1)`, `S = (0,0,0)`. An additive fill's albedo is written through unlit.**

`Add` means "this is light, not paint" — `ShaperFillComposite.Add`'s own doc says "an additive glow over nothing stays transparent-but-bright, which is what makes it read as light rather than as paint" (`ShaperFillContract.cs:169-172`), and FC-2.6b's rule that Add does not raise alpha exists for the same reason. **Multiplying emitted light by an incident-light term is backwards: a lamp does not get dimmer because you put it in a dark room.** FC-2.6d records that the heat and soot ramps B4 identifies as the fire and explosion palettes "are usually authored to `Add`" — so this ruling is what keeps a fire emissive under a rig that would otherwise plunge it into shadow.

The branch is free: `bool add` is already computed one line above, at `ShaperFillResolver.cs:1013` (and `:1091` for a border).

**The height delta is unaffected**, per FC-2.6c: `buf.height[i] += buf.heightDelta[i] * ce` (`:1046`) runs in both modes. So an additive fill can still raise a surface that an `Over` fill on the same node gets lit by, which is consistent and worth one comment line because "Add mode" reads like it should change everything.

## LR-5.4 A border IS lit, by its HOST's response block and its HOST's normal

> **LR-5.4 `[D-T0108]` — a border's fill is lit at the point it is evaluated, inside its host's turn (`ShaperFillResolver.cs:1082`), using the HOST NODE's response block and the HOST NODE's normal sheet. A border carries no response block of its own and no provider of its own.**

**Why lit at all.** BD-3.1 makes a border's fill an ordinary fill, and BD-3.5 makes the border "a finished thing meeting what is beneath it". An unlit outline on a lit shape is the "not one scene" failure at the smallest possible scale — a bright rim on a shape lying in shadow.

**Why the host's block and not its own.** The design gives the response block to a *layer*; a border is not a layer. Giving it one would let an outline be lit differently from the shape it traces, which is the same failure again with an authored control to make it worse.

**Why the host's NORMAL and not the strip's — the trap.** BD-3.3 rules that "the `edgeDistance` sheet handed to a border's fill is the STRIP's, not the node's", and `PaintTile` implements it (`:1077-1081`, `bown.edgeDistance = buf.ownDistance` at the border's own slab). **The normal is the opposite and must be written down, because symmetry with BD-3.3 is the obvious wrong answer.** The strip is not a separate surface — it is a band painted *on* the node's surface — so it faces the way the node faces. A normal derived from the strip's own field would make an outline read as a raised welt around every shape, which nobody authored.

## LR-5.5 The exclusivity partition is untouched

Lighting is a per-sample transform of one owner's colour before that colour enters the partition arithmetic. It reads `buf.normal` and writes nothing but the two output triples. `buf.claim`, `buf.descendantClaim`, `buf.paint` and every alpha path are untouched, so **FC-3.5a's measured result — 0 of 16384 samples below published coverage, total deficit 0.000 — is preserved by construction.** LT-9 re-runs that instrument with lighting on and asserts the same numbers, and its mutation (apply `L` to `dst[s4+3]` as well) reproduces the original 244-sample deficit, so the test is a real guard rather than a restatement.

## LR-5.6 No lighting stage is registered as image-space

BC-4.3 requires any stage reading screen-space neighbours to be declared, and says the declaration "has teeth in two places": exclusion from tile independence by name, and exclusion from the tilt conformance render. **T-0108 adds nothing to either list.** Stating that is the point of the clause — the exclusion list is meant to be a visible inventory of every place the rule is bent, and the value of this task's ruling is that lighting, which BC-2.5 lists as two of today's six illegal stages, comes back legal.

---

# Part 6 — LR-6, the Solids port

## LR-6.1 The ruling: generator, not composite

> **LR-6.1 `[D-T0108]` — Solids in Shaper is a GENERATOR that publishes coverage and a surface normal (and writes into the same `normal` sheet any provider writes). It goes through the ordinary fill and light pipeline like every other generator. It is NOT a composite and NOT an escape-hatch effect.**

**The argument.** B8 says Solids publishes "real 3D geometry with a genuine surface direction at every dot and real hiding of one part behind another" (`SHAPER_THE_DESIGN.md:190`) — that sentence is literally coverage plus normal. BC-3.3 #9 already records that the analytic 3D solids "already have the arithmetic a publisher needs" (`BUFFER_CONTRACT.md:214`). And the alternative is worse in four specific ways: a composite is exempt from the fill contract (so Solids keeps its private colour ramps forever), exempt from the availability gate, exempt from borders, and lands in the category design Part A reserves for "the big pre-existing effects that make finished pictures on their own and are **not worth taking apart**". Solids is worth taking apart, because its inline lighting block is the exact thing this task exists to share — leaving it a composite would mean shipping "one shared law" with one of its two callers structurally unable to call it.

## LR-6.2 What comes across unchanged

Verbatim, because it is correct and because rewriting it would be a byte-identity risk for no gain:

- The four geometry builders: `BuildGemGeometry` (`PyreRenderer.cs:4796`), `BuildBoxGeometry` (`:4825`), `BuildPyramidGeometry` (`:4869`), `BuildCanGeometry` (`:4903`), and `EdgeKey` (`:4793`).
- The rotation `Rot` — roll about Z in model space, then yaw about Y, then world tilt about X (`:4193-4198`).
- Face resolution: the centroid outward-normal test (`:4234`), the backface cull (`:4235`), the normalise (`:4236`), the deduped line-edge gather (`:4238-4254`).
- `InTri` (`:4936`) and `DistSeg` (`:4949`).
- Orb's analytic normal `N = P/R` (`:4533`) and its inverse-light-rotation identity (`:4442-4455`).
- Ring's forward/inverse plane map and its gradient-magnitude correction.

## LR-6.3 What is re-expressed

- **The inline lighting block, deleted.** `PyreRenderer.cs:4341-4360` (facet), `:4534-4553` (Orb), `:4720-4740` (Ring) are three copies of `lit = ambient + diffuse·ndl·atten` plus a Blinn-Phong half-vector term. All three become one `ShaperLightLaw.Shade` call. `gemAmbient` → the document ambient. `gemDiffuse` → light intensity. `gemSpecular`/`gemSpecPower` → the response block. The light position and range → the rig (LR-1.6).
- **The light position**, per LR-1.6. The visible break, priced there.
- **The facet edge lines**, kept per BD-4.1 and *renamed to "facet edge lines"*, but re-expressed: the generator computes `edist` and `isLine` as today and, where `isLine`, **substitutes the line colour as the sample's albedo while keeping the face's normal**, so the line is lit by the shared law exactly like the face it sits on. **`float k = Mathf.Clamp(0.25f + lit, 0f, 1.15f)` (`:4349`) is DELETED.** That expression is a crude hand-approximation of "lit, with a floor and a ceiling", and the shared law does the lit part properly. **Expected visual change, stated:** an edge line in deep shadow now falls to ambient instead of stopping at 0.25, and a fully lit one is no longer clamped at 1.15 so it can blow out. If the floor turns out to be wanted it returns as a `lineAmbientBoost` on the Solids generator — **named as deferred, not built**, because a floor that nobody has asked for is a dial nobody can explain.
- **`layer.shapeFill`** (`matFill`, read at `:4356`, `:4520`) becomes an ordinary Shaper fill on the Solids node. Strict gain: Solids acquires Gradient, Ramp-by-quantity and Texture, plus the availability gate, for free.

## LR-6.4 What is deliberately left behind or deferred

- **Coverage stays HARD (0 or 1) for all five members in Wave 2.** Today `hasFace` is a boolean from `InTri` (`:4327`) or from `d <= R` (`:4526`). BC-3.3 #1 wants coverage soft. Antialiasing a facet silhouette needs a per-edge coverage estimate, which is real work and is not in this task. **The Orb could be softened for free** — it has an exact SDF, `d − R`, and `ShaperField.Coverage` (`ShaperField.cs:225`) would take it directly — and it is **deliberately not softened**, because an Orb and a Can in the same document with visibly different edge quality is the "not one scene" complaint restated about edges instead of light, and this task exists to remove that complaint rather than relocate it. Declared limitation, owner **T-0114**.
- **Halo and inner glow stay in the generator.** `:4363-4378`. The halo *is* re-expressible as an outward border with a `ByEdgeDistance` Add gradient (BD-1.1 + FC-6.2 + FC-2.6b) and shedding it would remove ~20 lines — **but the inner glow is not**, because it measures distance to the *facet seam* set, which BD-4.1 reason #1 establishes is nowhere in the zero set of any 2D field, and because `edist` is one measurement feeding both (`:4366`, `:4373`) so separating them means computing it twice. Both stay; the halo's re-expression is named as a follow-up so the generator can shed it later without rediscovering the option.
- **A spatially varying specular tint.** LR-4.4. **UNVERIFIED** whether any asset uses one.
- **Geometry modifiers.** Already excluded today, deliberately ("GEOMETRY modifiers are deliberately NOT applied to the solids in this slice", `:4149-4150`). Unchanged.

## LR-6.5 The constraints, written down here rather than rediscovered

The task requires three; there are four, and the fourth is the one the brief did not name.

### LR-6.5a Convexity, backface-cull-only, no depth sort

**Citations.** `PyreRenderer.cs:4139-4140` — "backface culling ONLY (no depth sort)". `:4213` — "Convex + culled ⇒ front faces tile the silhouette with no overlap." `:4234` — `if (Vector3.Dot(nrm, centroid) < 0f) nrm = -nrm;   // force outward (origin is inside the solid)`. `:4235` — `if (nrm.z <= 0f) continue;   // backface: not seen`. `:4362` — `break;   // convex + culled: first hit wins`. `:4787-4788` — "origin strictly INSIDE the convex solid so DrawFacetSolid's centroid test resolves each face's outward normal".

**What it forbids.** The family cannot render a non-convex solid, cannot render two solids that interpenetrate, and **cannot render a fused silhouette of several solids** — because `break` at `:4362` is only correct when at most one front face covers a pixel. Reusing this family on an arbitrary fused silhouette therefore requires a per-pixel depth compare, which is a different rasteriser rather than a parameter. It is worse than that in one further respect that is easy to miss: the outward-normal resolution depends on the **origin being strictly inside the solid** (`:4234`, `:4787`), and a fused shape has no such point in general — so even the normals would be wrong, not merely the ordering.

### LR-6.5b Orb does not rotate geometrically; the lighting frame rotates instead

**Citations.** `:4412` — "Silhouette = the plain circle d ≤ R, **NEVER squashed by spin/tilt** (a sphere looks identical from every angle)." `:4442-4455` — "Orb spin/tilt = a LIGHTING-FRAME rotation, NOT a geometry rotation… we rotate the LIGHT by the INVERSE of the sphere's rotation and keep shading with the true N = P/R". `:4526` — `bool hasFace = d <= R;`. `:4533` — `Vector3 N = P / R;`.

**What it forbids.** Orb cannot participate in any shared 3D transform: a document-level camera tilt would turn every facet solid and leave the Orb's silhouette exactly unchanged, so a scene containing both would visibly come apart. Orb cannot be a member of a fused silhouette that rotates, because its coverage is not a function of the rotation at all. And the equivalence the trick rests on — `N·(Rot⁻¹·L) == (Rot·N)·L`, valid because a rotation preserves dot products and lengths (`:4448-4451`) — **holds only for rotations**: a non-uniform scale or a perspective divide breaks it silently, producing a plausible-looking but wrong hotspot. So Orb is locked to uniform scaling and to orthographic projection, and both locks are invisible in the code.

### LR-6.5c Ring breaks LR-6.5a outright, and this is the constraint the brief did not name

**Citations.** `:4601` — "Ring: a flat **two-sided** tilted annulus (a Saturn ring), analytic". `:4607` — "It's TWO-SIDED (**no backface cull**): the plane normal is flipped to whichever side faces the viewer". `:4636-4640` — `Vector3 N = new Vector3(syw, -cyw * st, cyw * ct); if (N.z < 0f) N = -N;`. `:4633` — `if (Mathf.Abs(ct) < 0.02f || Mathf.Abs(cyw) < 0.02f) return;`.

**Why it does not blow up.** An annulus is not a convex set and Ring is not backface-culled, so both halves of LR-6.5a are violated. It survives only because it is a **single flat plane**: a ray meets it exactly once regardless of convexity, so "first hit wins" is trivially true with one hit available.

**What it forbids.** Ring cannot be combined with any other solid in one pass — there is no depth sort to order a plane against a facet body, so a ring around a sphere is not renderable by this family. And its edge-on degeneracy is a **hard early return**, not a fade: at `|cos tilt| < 0.02` or `|cos yaw| < 0.02` the function returns having drawn nothing. Under a shared camera passing through edge-on, the Ring **pops out of existence discontinuously**. LT-14c measures the discontinuity and locks it as a known defect rather than pretending it is a design.

### LR-6.5d The whole family is orthographic, and the light is a screen-space object

**Citation.** `:4936-4938`, `InTri`'s own header: "Barycentric point-in-triangle (**orthographic**: screen bary == plane bary)". Combined with LR-0.5, this means the light position, the view direction (`viewDir = (0,0,1)`, `:4269`) and the surface points all live in a screen-space frame with no camera. **What it forbids:** introducing any perspective breaks the barycentric interpolation at `:4340` (`P = visP0·w0 + visP1·w1 + visP2·w2`) as well as Orb's rotation identity, so a future camera is a rewrite of the interpolation and not a matrix change. Recorded because "add a camera later" reads cheap and is not.

---

# Part 7 — LR-7, the stated limitation

## LR-7.1 Where it lives

> **LR-7.1 `[D-T0108]` — the limitation sentences are `public const string` fields on `ShaperLightRig`. Not XML doc comments, not a README, not a tooltip.**

Wave 2 ships no window (F8.6), so the sentence has to be **fetchable verbatim at runtime by whichever task builds one** — an XML comment is not readable at runtime and a tooltip needs a UI to hang on. The shipped code already establishes the pattern for exactly this: `ShaperQuantities.SurfaceDirectionRefusal` (`ShaperFillContract.cs:140`) and `ShaperQuantities.SubtractRefusal` (`:152`) are both `const string` refusal sentences authored for a UI that does not exist yet. Follow the precedent rather than invent a second mechanism.

Each is also the string a diagnostic carries, so there is one sentence per fact and not two that can drift.

## LR-7.2 The sentences

```csharp
public static class ShaperLightRig   // fields on the authored rig type
{
    /// B8's honest limitation, stated up front "because it will otherwise be reported as a bug"
    /// (SHAPER_THE_DESIGN.md:194). The UI shows this wherever a document contains both families.
    public const string ConsistentNotIdentical =
        "Silhouette and Solids are lit by the same law, the same lights and the same ambient, so they read " +
        "as being in one scene. They will not match pixel for pixel. Silhouette shades a height field and " +
        "Solids shades real geometry, and a sphere and an extruded disc genuinely differ under the same " +
        "light. Consistent, not identical, is the promise.";

    /// LR-2.5 / LR-4.6. Shown on the Rim Strength control whenever the layer's surface direction is flat.
    public const string RimNeedsRelief =
        "Rim strength has no effect on a layer whose surface direction is flat, because a flat surface " +
        "never faces the viewer edge-on. It appears on Solids immediately, and on Silhouette once a layer " +
        "has relief.";

    /// LR-4.5. Shown on the Cast Shadows and Receive Shadows controls whenever either is ticked.
    public const string ShadowsNotComputed =
        "Cast and receive shadows are saved with the document, but no shadow is computed yet. The picture " +
        "is resolved by looking straight down, and a shadow needs a ray pointed at the light.";

    /// LR-3.2. Shown on the Surface Direction control while the only provider is Constant.
    public const string SilhouetteIsFlatUntilExtrusion =
        "This layer's surface faces one fixed direction. Lighting, falloff and colour are real and match " +
        "the rest of the document, but there is no relief to catch a highlight until the layer is extruded.";
}
```

## LR-7.3 How the UI is required to use them

A future window MUST show `ConsistentNotIdentical` where both families are present, and MUST attach each of the other three to the specific control it qualifies rather than pooling them into a help panel. That is the difference between a stated limitation and a buried one, and it is the same standard FC-4.4 sets for the availability gate: "the control is present, its reason is stated, and the thing it would have done does not happen." **A control that silently does nothing is the failure B4 says must not survive the rebuild**; a control that does nothing *and says so on itself* is not that failure.

---

# Part 8 — LT, the conformance tests

Every row is a mechanical assertion with an expected value **and the source mutation that makes it fail**. That third column is the point: T-0105, T-0106 and T-0107 each shipped a green audit that an independent verifier then holed, and in every case some tests turned out to be structurally incapable of failing (`BORDER-CONTRACT.md:245` records the finding and made `BT-13` an instrument self-test in response). **A test with no stated mutation is not a test and must not be counted.**

Instrumentation follows the established arrangement: plain static methods returning a report string, **no `[MenuItem]`**, invoked through the Unity CLI against the Shaper editor (`--project-path "D:\UNITY\Laubrary Dev - Shaper"`), the same as FC Part F7.

| ID | What it measures | Expected | The source mutation that makes it fail |
|---|---|---|---|
| **LT-1** | **One law, two families — numeric.** Call `Shade` from the Silhouette path and from the Solids path with the same rig, response, `P`, `N`, `V`. Sweep 64 normals × 8 rig configurations. Compare all six output floats. | Bit-identical, 512/512 cases. | Paste an inline `lit = ambient + diffuse*ndl*atten` back into the Solids rasteriser and use it. **This test alone is insufficient and that is stated: it cannot see a duplicate that currently agrees.** Pair it with LT-1b. |
| **LT-1b** | **One law — structural.** Reflection: `ShaperLightLaw` exposes exactly one public method named `Shade`. IL scan: the Solids generator's method body contains a `call` to it. IL scan: no other type in the runtime assembly contains both a `Mathf.Pow` and a `Dot`-shaped half-vector expression. | One method; call present; no second shading site. | Duplicate `Shade`'s body into Solids — LT-1 still passes, LT-1b fails. This is the half that makes "one law" falsifiable. |
| **LT-2** | **Zero allocation in the hot path.** `GC.GetTotalAllocatedBytes(precise: true)` bracketing a 10 000-sample lit `PaintTile`, after one warm-up, for {1, 8} lights × {Over, Add} × {Constant, Solids}. | **Exactly 0 bytes**, all 8 cases. | Move the normal sheet's allocation from the host into `PaintTile` (`new float[n*3]` per tile) — the exact BC-3.7f violation `PyreRenderer.cs:1803` and `:1857` (`light = new float[W * H]`, per layer per frame) actually commit today. |
| **LT-3** | **Tile independence with lighting on.** Whole-grid render vs a 7×5 prime decomposition (BC-1.6's prescription), with 8 lights, both providers, an animated intensity. | Bit-identical `Color32` output. | (a) Implement the normal provider by differencing `buf.height` — seams at every boundary, which is the enforcement of LR-3.3. (b) Compute the surface point `P` from a tile-local index instead of the absolute sample index — the point light's falloff then restarts at every tile. |
| **LT-4** | **Determinism.** Same document rendered twice in one session and once after a domain reload, with a `Curve` light intensity and a `MinMax` light intensity. | Bit-identical, all three. | Sample a light dial with `ZUIValue.Evaluate` instead of `ShaperValue.Sample` (the duration trap, `ShaperValue.cs:9-13`), or draw `MinMax` from `UnityEngine.Random` instead of the hash at `ShaperValue.cs:33`. The reload run diverges. |
| **LT-5a** | **Linear space, hand-computed.** One directional light, colour sRGB byte 128 on all channels, intensity 1, `N·L = 1`, ambient 0, albedo linear white, `receiveLighting` on, specular 0, rim 0. Assert the destination float and the encoded byte. | Destination float `0.215861…` (±1e-6); **encoded byte exactly 128**. | Skip `ShaperSrgb.Decode` on the light colour and treat `128/255 = 0.50196` as linear. The byte comes out **188** — a 60-code error, which is the classic muddy-shading artefact FC-2.3 names, made numeric. |
| **LT-5b** | **One encode boundary.** IL scan: `ShaperSrgb.EncodeToByte` has exactly one caller in the runtime assembly, `ShaperFillResolver.Encode` (`:1207`). | One caller. | Add an encode inside `Shade`, or re-add a second encoder. Two callers → fail. (FC-2.3a records that a second encoder was already shipped once and deleted.) |
| **LT-6** | **Falloff, ambient, specular and rim against hand-computed values.** Point light at `(0,0,20)`, linear white, intensity 1, range 20; ambient 0.18; `specular = 0.9`, `specularPower = 4`; `rimStrength = 1`, `rimPower = 2.2`; `V = (0,0,1)`. Three probes. | **P1** `P=(0,0,0)`, `N=(0,0,1)`: `atten = 0.5`, `L = 0.68`, `S_spec = 0.45`, `rim = 0`. **P2** same but `N = (0.866025, 0, 0.5)`: `L = 0.43`, `S_spec = 0.028125`, `rim = 0.217638`. **P3** light moved to `dist = 40`, range 20, `N=(0,0,1)`: `atten = 0.2`, `L = 0.38`. All to 1e-6. | Change the falloff to `1/(1 + d/range)` → P3's `L` reads 0.5133 not 0.38. Change `rimPower` 2.2 → 2.0 → P2's rim reads 0.25 not 0.217638. Drop `atten` from the specular term → P1's `S_spec` reads 0.9 not 0.45. Add ambient last instead of first → P2 with `intensityScale = 0` reads 0.18 not 0.18 (unchanged) but with `intensityScale = 0.5` reads 0.09 + 0.125 not 0.18 + 0.125. Each mutation is caught by a different probe. |
| **LT-7** | **Rim is exactly zero on a flat normal, and is not light-dependent.** `Constant(0,0,1)`, `rimStrength = 1`, sweep 8 light directions including three well off-axis. | Rim component **exactly 0.0f** at all 16 384 samples, for all 8 light directions. | (a) Compute the rim from a non-normalised `nz` (the reference app's own asymmetry, `BUFFER_CONTRACT.md:246`) — a length-1.4 normal gives a non-zero rim on a flat card. (b) Substitute the light direction for `V` in the rim term — the plausible-but-wrong implementation — rim becomes non-zero the moment a light is off-axis, and the sweep catches it on light 2. |
| **LT-8** | **`receiveLighting = false` is bit-identical to the pre-lighting build.** Render T-0107's stored conformance fixture with a fully populated 8-light rig and every layer's `receiveLighting` off; compare `Color32` output against the T-0107 golden. | Bit-identical, every pixel. | Make `receive = false` mean "ambient only" instead of "unlit" — every byte shifts. Also catches an unconditional `S` add, or a rim that escapes the receive gate. |
| **LT-9** | **Lighting does not perturb the exclusivity partition.** Re-run T-0106's FT-21 instrument (the 16 384-sample fixture that measured the 244-sample alpha deficit) with lighting ON, 8 lights, mixed Over/Add. | **0 of 16384** samples below the shape's published coverage; total deficit **0.000**; worst **0.0000** — the same numbers FC-2.6a-AMENDED records. | Apply the light multiplier to the premultiplied destination *after* accumulation, including index 3 — alpha is scaled by `L` and a large deficit reappears immediately. This is LR-0.8's trap made mechanical. |
| **LT-10** | **`Add` fills are not lit.** One node, two owners: an `Over` Solid and an `Add` Solid of the same linear colour, under a rig giving `L = 0.5`, ambient 0, `S = 0`. | The `Add` contribution's RGB is exactly `colour · coverageEff`; the `Over` contribution's is exactly `0.5 ×` that. Ratio exactly 2.0 to 1e-6. | Remove the `if (add)` guard at the lighting call — the `Add` contribution halves and the ratio reads 1.0. |
| **LT-11** | **The cap refuses with a reason and a count.** Author 11 lights. | `hasTooManyLights == true`; `tooManyLightsCount == 3`; the reason string contains both "11" and "8"; and lights 9–11 contribute **exactly 0** at all 16 384 samples. | `continue` past the ninth light without setting the flag → the boolean assertion fails. Set the flag but record only that *some* were dropped → the count assertion fails. That second mutation is FT-8b's lesson (`ShaperProgram.cs:90-95`) applied here before the mistake is made rather than after. |
| **LT-12** | **Shadow flags are recorded AND inert.** Two renders of one document: all shadow flags false, then all true. | (a) `Color32` output **bit-identical** between the two. (b) `hasUnimplementedShadow` false then **true**; `unimplementedShadowCount` equal to the layer count; `unimplementedShadowNode` naming the first layer; `unimplementedShadowReason == ShaperLightRig.ShadowsNotComputed` verbatim. | Any accidental shadow implementation breaks (a). Dropping or paraphrasing the diagnostic breaks (b). **Both halves are required** — this is the test that makes "nothing-but-recorded" an honest ruling rather than an excuse. |
| **LT-13** | **The provider is swappable without touching the law.** (a) Render one document with `Constant(0,0,1)` and with `Constant(0.6,0,0.8)`; assert a named probe sample's byte differs by ≥ 8 codes. (b) Reflection: **`Shade`'s parameter list contains no array type and no reference type.** | (a) differs; (b) zero array or reference parameters. | Have `Shade` take the height or normal sheet and compute the normal itself. (b) fails instantly. This is the mechanical form of BC-4.2's "grep-level assertion that no shading code path differences a height or depth buffer", and it runs every build. |
| **LT-14a** | **Convexity (LR-6.5a).** For Box, Pyramid, Can and Gem, densely sample the silhouette and count how many *culled-visible* faces contain each sample. | Maximum count **exactly 1**, every sample, every form, over 32 rotation triples. | Remove the backface cull at `PyreRenderer.cs:4235` → the count reaches 2 at the first sample. |
| **LT-14b** | **Orb's silhouette is rotation-invariant (LR-6.5b).** Render an Orb at `(yaw, tilt, roll) = (0,0,0)` and at `(37°, 61°, 23°)`. | **Alpha channel bit-identical**; RGB differs at ≥ 100 samples. | Apply the geometric rotation to the Orb's coverage instead of the inverse rotation to the light — alpha changes and the first half fails. |
| **LT-14c** | **Ring's edge-on pop (LR-6.5c).** Covered-sample count at tilt ∈ {87°, 88°, 89°, 89.4°, 89.6°}. | Count > 0 through 89.4° and **exactly 0** at 89.6°, with the 89.4 → 89.6 drop ≥ 90% in one step. | **None — and that is stated rather than hidden.** This test locks in a known defect (`:4633`'s hard early return) as a regression guard. If a later task fades the ring out instead, this test must be updated deliberately, which is the whole reason it exists. |
| **LT-15** | **Normals are unit (LR-3.5).** Every vector written by every provider, over a dense sweep and 32 rotation triples for Solids. | `abs(|N| − 1) ≤ 1e-4`, no NaN, no zero vector, no unwritten sample (NaN sentinel pre-fill, per FT-13's method). | Omit the `nrm = nrm.normalized` at `PyreRenderer.cs:4236` — the facet normals come out with the cross product's raw magnitude and the sweep fails at the first face. |
| **LT-16** | **The two families agree because they share a FRAME, not only a law.** One directional light, azimuth 0°, elevation 45°. Render (i) a Solids Box of half-extent 20 px centred on the canvas, and (ii) a Silhouette square of half-extent 20 px with `Constant` set to the Box's +Z face normal. Compare `L` at the centre sample. | Identical to 1e-6, and the encoded bytes identical. | Leave Solids' light at `gemLightDistance · R` (`:4265`) instead of the absolute canvas position of LR-1.6 — the two families are then lit by lights in different places and disagree by a visible margin. **This is the strongest single test in the set**, because it is the only one that can fail while every other test passes, and it is the one that makes LR-1.5 falsifiable rather than decorative. |
| **LT-17** | **The visual check.** A contact sheet PNG: both families side by side under one rig; each light kind; a rim sweep; an `Add` fill under a lamp; `receive` on and off; all four shadow-flag combinations. | Rendered, kept, and **looked at by a human.** | Not falsifiable and not claimed to be. FT-20's precedent and its reason: a passing table is not a picture. |

**Report every result as measured-number versus expected-number.** Do not report a rule as verified on the strength of the code compiling — `SHAPE-ENGINE-SPEC.md:268`, applied here unchanged.

---

# Part 9 — what this contract could not check

Stated because a contract that claims verification is worth exactly as much as the verification.

- **Nothing in this document has been run.** No lighting code exists. Every number in Part 8 is derived from a formula or read off the shipped code, not observed. LT-5a's `0.215861` and LT-6's three probes are hand-computed from the IEC 61966-2-1 constants at `ShaperFillContract.cs:409-413` and from the equations in LR-2.3; they are predictions.
- **I did not open the Unity editor.** No probe was run against port 7801; every claim about existing behaviour is a source reading.
- **I did not read any authored `.asset`.** So LR-4.4's claim that no shipped asset uses a *spatial* `gemSpecularFill` is **UNVERIFIED**, and so is the assumption that no document depends on the per-particle light position LR-1.6 breaks. The break is ruled anyway; if an asset does depend on it, LR-1.6's conversion formula is the remedy.
- **I did not read `PyreShade.cs` end to end.** I read its sRGB helpers (`:129-132`), its LUT bake surface (`:164-223`) and its ramp-space enum (`:21`). Its `AdditiveEmissive` (`:300`) in particular may contain a colour-mixing convention this contract should have reconciled with; it is not reflected here.
- **I did not read `T-0105/VERIFICATION.md`, `T-0106/VERIFICATION.md` or `T-0107/VERIFICATION.md`.** Anything those record about *measured* results, as opposed to the contracts' stated expectations, is not reflected here.
- **The Burst claim is inherited, not demonstrated.** No Shaper code is `[BurstCompile]` (`BUFFER_CONTRACT.md:84`). LR-2.2's blittable signature keeps the door open; it does not prove anyone walks through it.
- **`ShaperFillOps.FillTile`'s body was read only in its first 60 lines.** The claim that a fill never reads a neighbour is taken from its doc comment (`ShaperFillOps.cs:11-12`) and from T-0106's FT-11, not from reading every branch.

---

# Part 10 — open questions handed onward

Each names the task that must answer it. Recorded so they are answered rather than discovered, which is why design C9, `BUFFER_CONTRACT.md` Part 5 and `FILL-CONTRACT.md` F8.7 all exist.

**To T-0109 (extrusion, bevel, Z) — four, and the first two are requirements rather than questions.**

1. **A height profile MUST publish `dh/dd` analytically, not only `h`.** LR-3.2 shows why: without the derivative the provider is back to differencing, which LR-3.3 forbids. State it as a requirement on the profile's interface, not as something a later task discovers when the normal looks wrong.
2. **A profile MUST be expressible as a `ShaperNormalKind` case**, added to `ShaperNormals.FillTile`, touching neither `ShaperLightLaw` nor Solids. LT-13 is already written and will fail if it is not.
3. **The side wall's normal is a NEW consequence of the side-wall question that nobody has recorded.** `BUFFER_CONTRACT.md:304` and `FILL-CONTRACT.md` F8.2 both record that a side wall has no `edgeDistance` and that three candidate rules exist. **The normal has the same problem and is not mentioned in either:** a wall's normal is in-plane (`nz ≈ 0`), so `rim` on a wall saturates to `rimStrength` and the diffuse term inverts sign relative to the top face. Whichever of the three side-wall rules is picked must state what the wall's normal is, not only what its `edgeDistance` is.
4. **BC-3.6's three dials (`slopeGain` 0.65, `normalZBase` 1.4, `reflectionFlatten` 1.5) are named and defaulted on `ShaperNormalOp` (LR-3.1) and are unused until this task.** T-0109 owns wiring them and owns the calibration BC-3.6 asks for.

**To T-0110 (indexed strip) — one.** An indexed strip's per-slot height is **piecewise constant**, so its derivative is a delta function at every slot boundary and an analytic normal from it is undefined. Does a strip publish a smoothed height, a per-slot normal, or no normal at all (leaving the layer's profile to own the normal and the strip to own only the colour)? B6 calls the strip "the existence proof for B4's central claim" (`SHAPER_THE_DESIGN.md:154`), and this is the one place that proof is not yet complete.

**To T-0111 (Tapestry) — one.** B5 establishes that Tapestry Shape's output *is* a height field. LR-3.4's fork therefore recurs at larger scale: does a Tapestry surface publish a gradient alongside its height, or does it accept a flat normal like every other fill? A procedural surface *can* often differentiate itself analytically, which makes the answer different from LR-3.4's and worth deciding rather than inheriting.

**To T-0112 (composites) — three.**

1. **Shadows.** LR-4.5 rules them out of Wave 2 on the grounds that v1's resolve is straight-down-only. This task is where cross-layer occlusion first becomes expressible, and it owns both the algorithm and the retirement of `ShaperLightRig.ShadowsNotComputed`.
2. **Can a composite generator be a receiver at all?** C2 says a composite layer "may not be re-filled". A generator that produces a finished picture has no albedo to multiply and no normal to publish, so it is presumably not a receiver — but the design does not say so and neither does this contract.
3. **The halo's re-expression as an outward border with a `ByEdgeDistance` Add gradient** (LR-6.4), if a Solids node can hold a border by then.

**To T-0114 (effects) — one.** **Soft coverage for the facet solids** (LR-6.4). All five members publish hard 0/1 coverage in Wave 2, and the Orb's free softening is deliberately withheld to keep the family consistent. This task owns making all five soft together.

**To the owner — two decisions, neither of which blocks the build.**

1. **The Solids light-position break (LR-1.6)** changes every existing Solids look. It is ruled and the conversion formula is given; overturning it costs the whole of LR-1.5 and with it the "one scene" promise. Flagged, not deferred — the build proceeds on the ruling.
2. **The eight-light cap (LR-1.4)** is the number in this contract most cheaply changed: one constant, one struct, one test.
