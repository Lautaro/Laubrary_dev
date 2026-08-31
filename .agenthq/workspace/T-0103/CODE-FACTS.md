# T-0103 — CODE FACTS (Pyre + 3D Shaper), primary-source read

Investigator pass. **Facts only.** Every claim carries `file:line`. Claims I could not verify are marked **UNVERIFIED**. Where a thing does not exist I say what I searched.

## Source trees actually read

| Thing | Absolute path |
| --- | --- |
| Pyre runtime | `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Runtime\Pyre\` |
| Pyre editor | `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Editor\Pyre\` |
| 3D Shaper app root (= its AgentHQ project folder; they are the same directory) | `D:\CODEZ\AgentHQ\3D Shaper\` |
| 3D Shaper renderer + all client logic | `D:\CODEZ\AgentHQ\3D Shaper\public\index.html` (427,789 bytes, single file) |
| 3D Shaper document schema / normalisation | `D:\CODEZ\AgentHQ\3D Shaper\project_document.py` (44,613 bytes) |
| 3D Shaper HTTP server | `D:\CODEZ\AgentHQ\3D Shaper\server.py` (28,476 bytes) |

3D Shaper's source tree **is** its AgentHQ folder — there is no separate app directory. Confirmed by `README.md`: "From `D:\CODEZ\AgentHQ\3D Shaper`, run: `py -3.12 .\server.py`", and by `GET /api/health` comparing "`server.py`, `project_document.py`, `public/index.html`" as the app's own source files. The largest Pyre files: `PyreRenderer.cs` 361,449 B, `Pyre.cs` 108,617 B, `PyreRenderer.Layers.cs` 23,486 B, `PyreLayerKey.cs` 13,280 B.

---

# A. COMBINE / FUSE MODES

## A1. Pyre — is there any notion of combining two shapes into one silhouette?

**Yes, but only in two restricted forms, and neither is a general per-member boolean on a bag of shapes.**

### A1a. Across layers: `MatteCombine` on a mask channel (Max / Add / Subtract — no intersect, no soft)

`Runtime/Pyre/Pyre.cs:161-164`:

```csharp
    // How a WriteMatte layer's coverage combines with whatever is already in its channel (earlier WriteMatte
    // layers can target the same channel). Max (the default) = the classic union of masks; Add = accumulate and
    // clamp; Subtract = carve one mask out of another. The cheap mirror of vanilla Pyre's combinable mattes.
    public enum MatteCombine { Max, Add, Subtract }
```

The mode is stored **on the member** (the writing layer), not on the container: `Pyre.cs:213-215`

```csharp
        public MatteRole matteRole = MatteRole.Draw;
        public int matteChannel = 0;               // 0..3 — which channel a WriteMatte layer writes into
        public MatteCombine matteCombine = MatteCombine.Max;   // how it combines with what's already in that channel
```

There is **no container**. Layers are a **flat** `List<PyreLayer>` on the spec — `Pyre.cs:1207`:

```csharp
        public List<PyreLayer> layers = new List<PyreLayer> { new PyreLayer { matteEnabled = false } };
```

The combine is executed at `PyreRenderer.cs:908-920`:

```csharp
        static void WriteMatteCoverage(float[] channel, Color32[] scratch, MatteCombine combine, bool useLuma)
        {
            for (int i = 0; i < channel.Length; i++)
            {
                float cov = useLuma ? Luma(scratch[i]) * (scratch[i].a * (1f / 255f)) : scratch[i].a * (1f / 255f);
                switch (combine)
                {
                    case MatteCombine.Add:      channel[i] = Mathf.Clamp01(channel[i] + cov); break;
                    case MatteCombine.Subtract: channel[i] = Mathf.Clamp01(channel[i] - cov); break;
                    default:                    channel[i] = Mathf.Max(channel[i], cov); break;
                }
            }
        }
```

**Important scope limit:** this does **not** produce a combined *silhouette that then draws*. It produces a scalar 0..1 **mask channel** which a *different* layer consumes either as an alpha clip (`clipByChannel`, `Pyre.cs:216`) or as a heightmap (`heightFromChannel`, `Pyre.cs:233`). The union/carve happens in the mask plane, not in the drawn geometry.

**Evaluation order is the stored order and it is stable.** `PyreRenderer.Layers.cs:75` iterates `for (int li = 0; li < layers.Count; li++)` in list order; `FrameComposer.Apply` is likewise called in ascending `li` (`PyreRenderer.cs:225-234`, `PyreRenderer.Layers.cs:219-220`). Index 0 is documented as the BACK (`Pyre.cs:1204`).

### A1b. Within one layer: `LayerCoalesce.Fuse` / `.Ramp` (a metaball union of that layer's own swarm)

`Pyre.cs:173`:

```csharp
    public enum LayerCoalesce { Off, Fuse, Ramp }
```

`Fuse` sums a compact metaball kernel over the layer's placed particles and resolves one iso-surface; `Ramp` fuses domes by a SmoothMax. Details under **B2**. This is a **union only** — there is no subtract or intersect at this level.

### A1c. What does NOT exist in Pyre

**No Intersect anywhere.** Searched `Runtime/Pyre` + `Editor/Pyre` for `enum .*(Combine|Blend|Boolean|Merge)`, `Union`, `Subtract`, `Intersect` (case-insensitive). Every `Intersect` hit is either a comment gloss on `MatteCombine.Max` (`Pyre.cs:224`) or ray-segment geometry in the Star raster (`Pyre.cs:486`, `PyreRenderer.cs:3247`, `:3332`). There is **no** `Intersect` combine mode.

**No shape tree / no bag.** There is no nesting construct: `spec.layers` is a flat list, and `PyreLayer` contains no child-layer field (I enumerated every serialized field of `PyreLayer` — see C1). A layer's *contents* nest only as: layer → swarm of N particles → one form. Not layer → layer.

**No soft combine between two layers.** Searched `Runtime/Pyre` for `smoothmin`, `softness`, `viscosity`, `sharpness`, `blend`. The only smooth-min-family function is `PyreField.SmoothMax` (B2), which is used *inside* a layer's own field pass, never between two layers' buffers.

## A2. 3D Shaper — same questions

**Confirmed: exactly four modes, Add / Add-soft / Subtract / Subtract-soft, and no Intersect.**

Canonical list, `project_document.py:19`:

```python
COMPONENT_MODES = ("add", "subtract", "softAdd", "subtractSoft")
```

UI mirror, `public/index.html:457-464`:

```js
    // The four ways a layer can fuse with everything before it -- shared by the Layer fusion radio group and each
    // layer row's own quick-switch button (T-0058) so the value/label/icon/order can never drift between the two.
    const FUSE_MODES = [
      ['add', 'Add', 'fuseAdd', 'Add: this layer adds its shape to everything before it.'],
      ['softAdd', 'Add soft', 'fuseAddSoft', 'Add soft: this layer blends into everything before it with a soft, rounded seam.'],
      ['subtract', 'Subtract', 'fuseSubtract', 'Subtract: this layer cuts a hole out of everything before it.'],
      ['subtractSoft', 'Subtract soft', 'fuseSubtractSoft', 'Subtract soft: this layer softly cuts into everything before it, with a rounded seam.'],
    ];
```

An unrecognised mode does **not** fall back — it disables the component: `project_document.py:322` `supported = mode in COMPONENT_MODES`, then `:329` `"enabled": bool(source.get("enabled", True)) if supported else False`.

**Mode is stored on the MEMBER.** `project_document.py:329` (the component record) carries `"mode": mode` alongside `viscosity`, `sharpness`, `transforms`. The *container* (the shape) carries no combine mode at all — `normalise_shape` at `project_document.py:229` returns `{... "kind", "shapeType", "width", "height", "skew", "cornerRounding", "pulge", "components"}` with no mode field.

**Order is significant and stored-order == evaluation-order.** `evalShape`, `public/index.html:1113-1141`, walks `compiled.parts` in a plain forward `for` loop, and `compileShape` (`:1086`) built `parts` by iterating `source.components` in stored order. Comment at `:1061`: *"Complex shapes compose their ordered components in stored order"*.

**First-member special case (load-bearing):** `public/index.html:1119`

```js
        if (!started) { distance = (part.mode==='subtract'||part.mode==='subtractSoft') ? 1e6 : d; started=true; continue; }
```

A leading subtract member has nothing to cut, so the accumulator seeds at `1e6` (empty) rather than at that member's own field.

## A3. Is subtraction order-dependent (non-commutative) in the implementation?

**3D Shaper: YES, definitively — a single sequential accumulator.** `public/index.html:1113-1141`:

```js
    function evalShape(compiled, x, y) {
      if (compiled.basic) return evalBasicShape(compiled.basic,x,y);
      const parts=compiled.parts; if (!parts || !parts.length) return 1e6;
      let distance=1e6, started=false;
      for (const part of parts) {
        const px=matX(part.inverse,x,y), py=matY(part.inverse,x,y), d=evalShape(part.child,px,py);
        if (!started) { distance = (part.mode==='subtract'||part.mode==='subtractSoft') ? 1e6 : d; started=true; continue; }
        if (part.mode==='subtract') distance=Math.max(distance,-d);
        else if (part.mode==='softAdd') distance=smoothMinShaped(distance,d,part.k,part.n);
        else if (part.mode==='subtractSoft') { ... distance=distance+part.strength*bite; }
        else distance=Math.min(distance,d);
      }
      return distance;
    }
```

One `distance` scalar, folded left-to-right. `max(distance, -d)` against the *accumulated* field, so moving a subtract earlier or later changes the result. Note this is an **SDF accumulator, not a pixel buffer** — the fold happens per sample point.

**Pyre: YES for `MatteCombine`, same reason.** `WriteMatteCoverage` (`PyreRenderer.cs:910-919`) accumulates sequentially into **one shared `float[] channel` buffer**, in layer order, with `Clamp01` after every step. `Max` and `Add` are commutative-ish but the `Clamp01` on `Add` makes it order-sensitive at saturation; `Subtract` is plainly non-commutative. The channel buffers are allocated once per frame — `PyreRenderer.Layers.cs:257-258`:

```csharp
                        channels = new float[4][];
                        for (int c = 0; c < 4; c++) channels[c] = new float[W * H];
```

---

# B. SOFT COMBINE CONTROLS

## B1. 3D Shaper's soft modes — knob count, names, and the actual formula

**Two knobs, named `viscosity` and `sharpness`, both 0..1.** Defaults `viscosity: 0.4`, `sharpness: 0.5` — `project_document.py:329`:

```python
"viscosity": bounded_number(source.get("viscosity"), 0.4, 0, 1), "sharpness": bounded_number(source.get("sharpness"), 0.5, 0, 1)
```

Same defaults on the client when a component is created, `public/index.html:1964`: `mode:'add', enabled:true, viscosity:0.4, sharpness:0.5`.

**Both are disabled in the UI unless the mode is soft** — `public/index.html:2058`: `input.disabled = fusion && component.mode!=='softAdd' && component.mode!=='subtractSoft';`

### The actual blend function — a *compactly supported power smooth-min*, not the polynomial one

There are **two distinct smooth-min functions in the file** and they are used for different jobs. Do not conflate them.

1. **`smoothMin` / `smoothMax` — polynomial (Quilez), used ONLY for the model-layer z-buffer fusion band**, `public/index.html:994-997`:

```js
    // Polynomial smooth-min: k=0 is an exact union (a hard crease), larger k rounds the join over a wider band.
    // smoothMax is its mirror and does the same job for the z-buffer's fusion band between two layers.
    function smoothMin(a, b, k) { if (k<=0) return Math.min(a,b); const h=clamp01(0.5+0.5*(b-a)/k); return b+(a-b)*h-k*h*(1-h); }
    function smoothMax(a, b, k) { return -smoothMin(-a,-b,k); }
```

Its only consumer is the height/z-buffer at `public/index.html:1444`: `heights[key]=smoothMax(previous,z,FUSION_CELLS*0.85);` with `FUSION_CELLS = 1.7` (`:982`) — a **fixed constant, not a user knob**.

2. **`smoothMinShaped` — the one the soft fuse modes actually use**, `public/index.html:1006-1007`:

```js
    function blendExponent(sharpness) { return Math.pow(8, clamp01(sharpness===undefined||sharpness===null?0.5:Number(sharpness)||0)); }
    function smoothMinShaped(a, b, k, n) { if (k<=0) return Math.min(a,b); const h=Math.max(k-Math.abs(a-b),0)/k; return Math.min(a,b)-Math.pow(h,n)*k/(2*n); }
```

So the formula is:

```
h = max(k - |a-b|, 0) / k
smoothMinShaped(a,b,k,n) = min(a,b) - h^n * k / (2n)
```

with the two knobs mapping as (`public/index.html:998-1005`, the code's own comment):

```js
    // Soft fusion has TWO independent axes, exactly as the brief asks ("depending on chosen sharpness and strength"):
    //   viscosity -> k, the STRENGTH: the half-width of the band, in canvas units, over which the two fields blend
    //                   at all. k=0 is a hard union; a large k lets shapes bend toward each other from far away.
    //   sharpness -> n, the FALLOFF CURVE inside that band. The blend is a compactly supported power smooth-min,
    //                   min(a,b) - h^n*k/(2n) with h = max(k-|a-b|,0)/k, so the deepest the joint ever pulls in is
    //                   k/(2n). n runs 1..8 across sharpness 0..1, i.e. the fillet at the contact point goes from
    //                   k/2 (wide, gooey, rounded merge) to k/16 (a tight crease hugging a hard union) at the SAME
    //                   k. That is why the two sliders are visibly independent rather than two names for one knob.
```

**k is not the raw slider — it is scaled by the child's size**, `public/index.html:1102-1105`:

```js
          k: Math.max(0,Number(component.viscosity)||0)*Math.min(shapeWidth,shapeHeight)*0.55,
          reach: Math.min(shapeWidth,shapeHeight)*0.55,
          strength: clamp01(Number(component.viscosity)||0),
          n: blendExponent(component.sharpness) });
```

So `k = viscosity · min(w,h) · 0.55` (canvas units) and `n = 8^sharpness ∈ [1,8]`.

**"strength + sharpness, two genuinely independent knobs" — CONFIRMED as written, with one caveat worth flagging.** They are mathematically independent for `softAdd`: `k` sets the band width, `n` sets the curve inside it, and the maximum pull-in `k/(2n)` depends on both. But for **`subtractSoft` the `viscosity` slider is overloaded** — it is read *twice*, as `strength` (how much of the cut lands) and, through `Math.sin(strength·π)`, as the blend band. `public/index.html:1122-1137`:

```js
        else if (part.mode==='subtractSoft') {
          const carve=-d-distance, band=Math.sin(part.strength*Math.PI)*part.reach*0.6;
          const bite=-smoothMinShaped(0,-carve,band,part.n);
          distance=distance+part.strength*bite;
        }
```

Note the consequences that follow directly from that line: `strength=0` is an exact no-op; `strength=1` is byte-identical to a hard `subtract` (because `band = sin(π)·… = 0`); the softness peaks at `strength=0.5`. So for `subtractSoft`, **viscosity is NOT a pure band-width knob** — it is a cut-amount knob whose softness is a bell curve over its own range. Only `sharpness` (`n`) is an independent axis there.

## B2. Pyre — does it have any soft/blended combine at all?

**Yes — one, `PyreField.SmoothMax`, with a single knob. But it operates within one layer's own particle set, never between two shapes or two layers.**

`Runtime/Pyre/PyreField.cs:137-144`:

```csharp
        // Blends toward max(a,b) with a soft knee of width k — the "fusion" that melts neighbouring domes into one
        // mass. k ≤ 0 ⇒ a hard Max. Verbatim from BlastRenderer.SmoothMax. General (no HeightBalls knowledge).
        public static float SmoothMax(float a, float b, float k)
        {
            if (k <= 0.0001f) return Mathf.Max(a, b);
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(a, b, h) + k * h * (1f - h);
        }
```

**This is the exact algebraic mirror of 3D Shaper's polynomial `smoothMin` at `index.html:996`** — same `h = clamp01(0.5 + 0.5(b−a)/k)`, same `± k·h·(1−h)` term. It is **not** the `smoothMinShaped` power form the soft fuse modes use.

**The knob name is `rampFusion`** — `Pyre.cs:296-297`:

```csharp
        [Tooltip("Ramp: fusion knee — how eagerly neighbouring domes MELT into one mass. 0 = a hard max (distinct orbs); higher = a smoother, heavier merged cloud.")]
        public float rampFusion = 0.35f;
```

Consumed by `PyreField.AccumulateDomes` (`PyreField.cs:155-179`), which fuses `s·weight` domes (`s = √(1−d²/r²)`) into one `float[]` field, `acc = SmoothMax(acc, s * c.weight, knee);` at `PyreField.cs:179`. Only reachable when `layer.coalesce == LayerCoalesce.Ramp`.

**The other soft-merge path, `LayerCoalesce.Fuse`, is a metaball sum + iso-threshold, NOT a smooth-min.** `PyreField.cs:67-84`:

```csharp
        public static float Sample(List<FieldParticle> parts, float sx, float sy)
        {
            float field = 0f;
            int n = parts.Count;
            for (int i = 0; i < n; i++)
            {
                var c = parts[i];
                ...
                float k = 1f - d2 / r2; k *= k;   // compact polynomial kernel: 1 at centre → 0 at radius
                field += c.weight * k;
            }
            return field;
        }
```

resolved by `ThresholdShade` (`PyreField.cs:113-125`), driven by **three** layer knobs — `fuseThreshold` (0.6), `fuseShadeRange` (1.5), `fuseSoftness` (0.18) — at `Pyre.cs:276-280`. `fuseSoftness` is an **anti-aliasing band across the iso-surface**, not a blend-band between two shapes; the comment at `Pyre.cs:279` says so: *"edge softness — the alpha AA band across the iso-surface"*.

**Bottom line for B2:** Pyre has smooth-merging machinery (`SmoothMax`, one knob `rampFusion`; and a metaball sum with a 3-knob iso resolve), but it is **not a per-member combine mode** — you cannot say "this shape soft-adds to that shape." It fuses a homogeneous swarm of identically-configured particles inside one layer.

---

# C. LOCAL TRANSFORMS

## C1. Pyre — per-layer transform fields, and are they nestable?

**A layer carries a full 2D-plus-pseudo-3D placement, all animatable. It is NOT nestable — there is exactly one level.**

Per-layer shape placement, `Pyre.cs:727-733`:

```csharp
        public ZUIValue shapeOffsetX = new ZUIValue(0f);   // shape-centre offset X, canvas pixels
        public ZUIValue shapeOffsetY = new ZUIValue(0f);   // shape-centre offset Y, canvas pixels
        public ZUIValue shapeScale = new ZUIValue(20f);    // shape radius in canvas pixels
        ...
        public ZUIValue shapeRotation = new ZUIValue(0f);  // 2D rotation, degrees
        public ZUIValue shapePitch = new ZUIValue(0f);     // pseudo-3D tilt, degrees (T3)
        public ZUIValue shapeYaw = new ZUIValue(0f);       // pseudo-3D tilt, degrees (T3)
```

Whole-swarm ("cloud") transform, a second tier **within the same layer**, `Pyre.cs:742-753`:

```csharp
        public ZUIValue swarmTurn = new ZUIValue(0f);      // whole-cloud yaw, degrees, at the current frame's life
        public ZUIValue swarmTilt = new ZUIValue(0f);      // whole-cloud pitch, degrees, at the current frame's life
        public ZUIValue swarmRoll = new ZUIValue(0f);      // whole-cloud roll (screen plane), degrees, at the current frame's life
        ...
        public ZUIValue swarmScale = new ZUIValue(1f);     // whole-cloud uniform radial scale about centre, at the current frame's life
```

Per-particle travel, a third tier, `Pyre.cs:642-647`: `particlePathX`, `particlePathY`, `particleSpin`.

**Nesting: NO.** There is no parent/child relationship between layers. Evidence:
- `spec.layers` is a flat `List<PyreLayer>` (`Pyre.cs:1207`) with no tree structure.
- I enumerated every serialized field of `PyreLayer` (`Pyre.cs:180`–`:775`, via the `PyreLayerKey` reflection rule); there is **no** field of type `PyreLayer`, `List<PyreLayer>`, or any child/parent index.
- The only "composition onto a child" that exists is the fixed **layer → swarm → particle** chain: `RenderSwarm` applies `swarmTurn/Tilt/Roll` then `swarmScale` to each already-placed spawn (`PyreRenderer.cs:1459-1465`, applied at `:1520-1526`), and the particle then adds its own `particlePathX/Y` (`PyreRenderer.cs:2890-2894`). Three hardcoded tiers, not recursion.

**Nothing in Pyre is a `pivot`/`origin` field.** Rotation is always about the shape centre (`ApplyShapeTransform` works in "shape-local coords relative to the centre", `PyreRenderer.cs:2081`) or the canvas centre (`cx = spec.Width * 0.5f`, `PyreRenderer.cs:1458`). Searched `Pyre.cs` for `pivot`, `origin`, `anchor` — the only hit is `streakAnchor` (`Pyre.cs:171`), which is a length-bias along a streak, not a transform pivot.

## C2. 3D Shaper — shape-layer (component) vs model-layer transform

**CONFIRMED: a shape layer carries silhouette combination and geometry only — no colour, no depth.**

Component record, `project_document.py:329`:

```python
    return {**source, "id": ..., "sourceShapeId": ..., "shape": shape_snapshot, "mode": mode, "enabled": ..., "viscosity": ..., "sharpness": ..., "transforms": transforms, "warning": warning}
```

Fields: `id, sourceShapeId, shape, mode, enabled, viscosity, sharpness, transforms, warning`. **No `materialId`, no `depth`, no `extrusion`, no `bevel`, no `edge`.**

Model layer record, `project_document.py:421`:

```python
    return {**source, "id": ..., "name": ..., "sourceShapeId": ..., "shape": shape_snapshot, "visibility": bool(source.get("visibility", True)), "transforms": transforms, "depth": bounded_number(source.get("depth"), 0, 0, 2048), "extrusion": normalise_extrusion(source.get("extrusion")), "bevel": normalise_bevel(source.get("bevel")), "materialId": string(source.get("materialId"), ""), "edge": normalise_edge(source.get("edge"))}
```

Model layer adds exactly: `name`, `visibility`, `depth` (0..2048), `extrusion`, `bevel`, `materialId`, `edge`. Component adds exactly: `mode`, `viscosity`, `sharpness`.

**The transform block is IDENTICAL for both.** Both call the same builder — `project_document.py:420` (layer) and `:325` (component) are the same line of code, and both default to `default_component_transforms()` (`:303-304`):

```python
def default_component_transforms() -> list[dict[str, Any]]:
    return [normalise_transform({"type": kind}) for kind in TRANSFORM_TYPES]
```

with `project_document.py:27`:

```python
TRANSFORM_TYPES = ("translate", "rotate", "scale", "skew", "origin")
```

So a shape layer's transform fields are: **translate (x,y), rotate (degrees), scale (x,y), skew (xDegrees,yDegrees), origin (x,y — a normalised pivot)** — a full affine set, and the *same* set a model layer gets. `PRODUCT_CONTRACT.md` states "Transforms are evaluated in stored order."

## C3. Existing matrix / transform-composition helper for nested 2D shapes?

**3D Shaper: YES — it already exists and is already used recursively.** `public/index.html:1010-1028`:

```js
    // Canvas-order affine matrices [a,b,c,d,e,f]: x' = a*x + c*y + e, y' = b*x + d*y + f. The renderer builds the
    // forward transform once per layer/component and inverts it, then inverse-transforms every sample point --
    // sampling an SDF only ever works backwards, never by transforming the shape itself.
    function matMul(m, n) { ... }
    function matInvert(m) { const det=(m[0]*m[3]-m[1]*m[2])||1e-6, ... }
    function matX(m, x, y) { return m[0]*x+m[2]*y+m[4]; }
    function matY(m, x, y) { return m[1]*x+m[3]*y+m[5]; }
    function nodeMatrix(node, width, height) {
      const tx=transformParam(node,'translate','x')||0, ty=transformParam(node,'translate','y')||0;
      const rot=(transformParam(node,'rotate','degrees')||0)*Math.PI/180;
      const sx=transformParam(node,'scale','x')||1, sy=transformParam(node,'scale','y')||1;
      const kx=Math.tan(clampTo(transformParam(node,'skew','xDegrees')||0,-89,89)*Math.PI/180), ky=...;
      const px=((transformParam(node,'origin','x')??0.5)-0.5)*width, py=((transformParam(node,'origin','y')??0.5)-0.5)*height;
      let m=matMul([1,0,0,1,tx,ty],[1,0,0,1,px,py]);
      m=matMul(m,[Math.cos(rot),Math.sin(rot),-Math.sin(rot),Math.cos(rot),0,0]);
      m=matMul(m,[sx,0,0,sy,0,0]);
      m=matMul(m,[1,ky,kx,1,0,0]);
      return matMul(m,[1,0,0,1,-px,-py]);
    }
```

Composition order: `T · P · R · S · K · P⁻¹` (translate, move-to-pivot, rotate, scale, skew, move-back).

**The composition is done by point-chaining, not matrix-chaining.** Each part stores only its *own* `inverse` (`public/index.html:1101`), and `evalShape` recurses, inverse-transforming the sample point at every level (`:1118` `const px=matX(part.inverse,x,y), py=matY(part.inverse,x,y), d=evalShape(part.child,px,py);`). Functionally equivalent to composing matrices down the tree; cheaper to build, more per-sample work.

Depth is bounded: `MAX_COMPONENT_DEPTH = 4, MAX_COMPONENT_PARTS = 512` (`public/index.html:990`, mirrored at `project_document.py:26`), with the reason given at `:986-989`: *"nesting MULTIPLIES leaf counts rather than adding to them: 8 components nested 4 deep is 4096 primitives evaluated for every single grid cell"*.

**Pyre: NO — this would be new construction.** Searched `PyreRenderer.cs` for `Matrix4x4`: **zero hits**. The only transform helper is a hand-written point transform that is applied **once, at one level**, and returns an absolute canvas position — `PyreRenderer.cs:2077-2117`:

```csharp
        static Vector2 ApplyShapeTransform(Vector2 baseAbs, float cx, float cy, float r,
                                           float rot, float yaw, float pitch, float offX, float offY, out float zNorm)
        {
            float x = baseAbs.x - cx, y = baseAbs.y - cy, z = 0f;
            if (rot != 0f) { ... }        // 2D rotation
            if (yaw != 0f) { ... }        // pseudo-3D about Y
            if (pitch != 0f) { ... }      // pseudo-3D about X
            zNorm = Mathf.Clamp(z / Mathf.Max(1e-3f, r), -1f, 1f);
            float persp = 1f + 0.25f * zNorm;
            return new Vector2(cx + offX + x * persp, cy + offY + y * persp);
        }
```

It is **not composable**: it takes a *canvas-absolute* point plus a canvas centre, bakes a perspective scalar (`persp`) into the result, and emits an absolute point. There is no inverse, no identity, no multiply. Chaining two of these is not the same as composing two transforms.

---

# D. CLOCKS / TIME

## D1. How does a Pyre layer or fill get "the current time"?

**There are three distinct clocks, all normalised 0..1, all derived from `frameIndex` — no seconds, no wall time, no delta-time anywhere in the render path.**

**Clock 1 — the document/spec clock, `life`.** `PyreRenderer.Layers.cs:155-156`:

```csharp
            int frames = Mathf.Max(1, spec.frameCount);
            float life = frames > 1 ? frameIndex / (float)(frames - 1) : 0f;
```

Identical computation in `FrameComposer`'s constructor (`PyreRenderer.Layers.cs:244-245`) and in `RenderLayerFrame` (`:191`).

**Clock 2 — the per-layer clock, `plan.layerLife`.** `PyreRenderer.Layers.cs:81-88`:

```csharp
                int winStart = Mathf.Clamp(layer.startFrame, 0, frames - 1);
                int winEnd = layer.endFrame < 0 ? (frames - 1) : Mathf.Clamp(layer.endFrame, 0, frames - 1);
                if (winEnd < winStart) winEnd = winStart;
                if (frameIndex < winStart || frameIndex > winEnd) continue;
                ref var p = ref plans[li];
                p.active = true;
                p.layerLife = Mathf.Clamp01((frameIndex - winStart) / (float)Mathf.Max(1, winEnd - winStart));
```

**This is the clock the layer body actually runs on** — `PyreRenderer.Layers.cs:169`: `RenderLayer(target, W, H, plan.layerLife, spec, layer, mods, phase, frameIndex);`

**Clock 3 — the per-particle clock, `own`.** `PyreRenderer.cs:1510-1513`:

```csharp
                float own = dieTogether
                    ? (life - sp.spawnLife) / Mathf.Max(0.0001f, deathPoint - sp.spawnLife)
                    : (life - sp.spawnLife) / Mathf.Max(0.0001f, layer.swarmParticleLife);
                if (own < 0f || own > 1f) continue;
```

with the comment at `:1505-1506`: *"Each particle's own life clock: 0 at its spawn frame, 1 at its death… All Shape fields (size/alpha/colour) then evaluate at `own`"*.

**How a fill gets time:** every animatable dial is a `ZUIValue` funnelled through one `Eval` (`PyreRenderer.cs:5026`): `static float Eval(ZUIValue v, float life, int seed, int particleIndex, int fieldId)`. Fills go through `EvalFill` (`PyreRenderer.cs:4987`): `static Color EvalFill(ZuiFill f, float life, float lu, float lv, int px, int py, int W, int H)` — time plus a local (u,v) plus canvas coords. The background fill also takes `life` (`PyreRenderer.Layers.cs:135`).

**`frameIndex` (the raw integer) is also passed down separately** — see the signatures at `PyreRenderer.cs:241`, `:1440`, `:2810`. It is used for pixel modifiers and stateful sims, not for envelope evaluation.

**A fourth, derived quantity: `phase`.** `PyreRenderer.Layers.cs:159`: `float phase = frames > 1 ? (frameIndex / (float)frames) * Mathf.PI * 2f : 0f;` — a 0..2π loop angle, handed to geometry modifiers.

## D2. Can two Pyre layers run on different phases/speeds/offsets of the clock?

**Partially — via a lifetime WINDOW only. There is NO per-layer time offset or time-scale field.**

The only mechanism is `startFrame` / `endFrame`, `Pyre.cs:196-199`:

```csharp
        [Tooltip("First frame this layer's shapes are alive. Its life is lerped 0..1 across [start, end]; before Start the layer contributes nothing.")]
        public int startFrame = 0;
        [Tooltip("Last frame this layer's shapes are alive (life reaches 1 here; after End the layer contributes nothing). -1 = the last frame — full range, the whole timeline, by default.")]
        public int endFrame = -1;
```

**What this gives you:** a layer whose 0..1 clock is *remapped* onto a sub-range of the timeline, i.e. both an **offset** (where 0 lands) and a **speed** (how fast 0→1 runs, since a narrower window compresses the same 0..1 into fewer frames). Two layers with `[0,7]` and `[8,15]` on a 16-frame spec genuinely run different phases.

**What it does NOT give you:**
- **No phase without gating.** Outside `[start,end]` the layer contributes *nothing* (`PyreRenderer.Layers.cs:84` `continue`). You cannot offset a layer by 3 frames and keep it visible the whole time — it is a window, not a shift.
- **No wrap / loop / ping-pong.** `layerLife` is `Clamp01`'d (`:88`), never `Repeat`'d.
- **No multiplier.** Searched `Pyre.cs` for `timeScale`, `timeOffset`, `speed`, `phaseOffset` on `PyreLayer`: no such field exists. The `phase` variable (`PyreRenderer.Layers.cs:159`) is derived purely from `frameIndex`/`frameCount` and is **identical for every layer in a frame**.
- **No fractional windows.** `startFrame`/`endFrame` are `int` frame indices.

## D3. Multi-instance effects — per-instance clock, or shared document clock?

**REFUTED for Pyre's swarm. The prior claim ("most instances start on the shared clock so they pop in mid-animation") is wrong as stated — there IS a genuine per-instance clock.**

Each spawn stores its own birth time (`PyreRenderer.cs:156`): `public float spawnLife;   // this particle's spawn point on the blast timeline, [0..1]`, and every Shape dial evaluates at `own`, not at `life` — see the calls at `PyreRenderer.cs:1523` (`Eval(layer.size, own, ...)`), `:1526` (`Eval(layer.alpha, own, ...)`), `:1533-1538` (density/heat), and the `DrawParticle` body at `:2878` / `:2881`.

**How `spawnLife` is assigned** — `PyreRenderer.cs:1968-1979`:

```csharp
                float spawnLife;
                ...
                    spawnLife = Mathf.Clamp01((layer.swarmFirstFrame + i * layer.swarmFrameStep) / (float)Mathf.Max(1, frames - 1));
                ...
                    spawnLife = 0f;   // single-char Text: one particle at timeline 0 (avoids the (n-1)==0 divisions)
                ...
                    spawnLife = Mathf.Clamp01(k * i / (n - 1));
                ...
                    spawnLife = Mathf.Clamp01(Eval(layer.swarmSpawnTiming, i / (float)(n - 1), spec.seed, i, FldSpawnTiming));
```

**The default deliberately staggers spawns across the first half of the timeline** — `Pyre.cs:1076-1086`:

```csharp
        static ZUIValue DefaultSpawnTiming()
        {
            // (0,0)→(1,0.5): particle number maps to its spawn moment on the TIMELINE, finishing the swarm's
            // spawns halfway through — the old default's window 0.5 × linear timing folded into one envelope now
            // that swarmSpawnWindow is gone. ...
```

So by default, particle `i` of `n` is born at `spawnLife = 0.5·i/(n−1)`, and lives for `swarmParticleLife = 0.5f` of the timeline (`Pyre.cs:706`, `[Range(0.05f, 1f)]`).

**The grain of truth behind the prior claim:** particles genuinely *do* appear mid-animation — that is the intended staggered spawn, not a bug. And three things DO run on the shared `life`, not on `own`:

1. The **whole-cloud transforms** — `PyreRenderer.cs:1459-1464`: `Eval(layer.swarmTurn, life, ...)`, `swarmTilt`, `swarmRoll`, `swarmScale` all take `life` (which at this call site is `plan.layerLife`).
2. The **placement snapshot**: `shapeScale`, `shapeRotation`, `shapeYaw` etc. are evaluated at **`spawnLife`**, not at `own` and not at `life` — `PyreRenderer.cs:1987`, `:2008-2009`. Comment at `Pyre.cs:658-659`: *"samples it at ITS OWN spawn frame and keeps that value for life, so an animated transform leaves a trail of placements."*
3. The **Fuse/Ramp field passes** are handed `life`, not `own` — `PyreRenderer.cs:1598` / `:1600`.

**Non-swarm multi-instance forms (Fire, Fireball, the plug-in `PyreForm`s) — NOT verified per-instance.** `RenderFireLayer` / `RenderFireballLayer` take a single `life` (`PyreRenderer.cs:392`, `:757`); they are stateful sims replayed from frame 0, with no per-instance clock in their signature. Whether the Kiln forms (`PyreInferno`, `PyreArcBurst`, `PyreTorch`, `PyreOrb`, `PyrePlasmaBloom`, `PyreForkBlast`, the Jet family) maintain per-instance clocks internally is **UNVERIFIED** — I did not read their render loops. What IS verified: `RenderFormLayer` hands each form the whole swarm instance list including per-instance `own` — `PyreRenderer.cs:329-331` builds `PyreSwarmInstance` with `own = (life - sp.spawnLife)/…` and `spawnLife = sp.spawnLife`, so the per-instance clock is *available* to every plug-in form; whether each uses it is not established.

---

# E. SWARM / MULTI-INSTANCE

## E1. What is the mechanism — generic wrapper or per-generator native code?

**A generic per-layer wrapper.** Two boolean-gated fields on the layer, `Pyre.cs:660-661`:

```csharp
        public bool swarmEnabled = false;
        [Min(2)] public int swarmCount = 8;
```

The dispatch is one `if` in `RenderLayer` (`PyreRenderer.cs:271-282`):

```csharp
            if (!layer.swarmEnabled)
            {
                if (layer.shapeForm == ShapeForm.Text && _textReady)
                    RenderTextLine(target, W, H, life, spec, layer, mods, frameIndex);
                else
                    DrawParticle(target, W, H, W * 0.5f, H * 0.5f, life, spec, layer, 0, mods, phase, frameIndex);
            }
            else
                RenderSwarm(target, W, H, life, spec, layer, mods, phase, frameIndex);
```

Named types:
- `PyreRenderer.RenderSwarm` — `PyreRenderer.cs:1440`
- `PyreRenderer.ComputeSpawns` — called at `:1444`, body around `:1940-2010`
- `SpawnPoint` struct (holds `pos`, `spawnLife`, `zNorm`, `orientDeg`) — `PyreRenderer.cs:~150-160`
- `PyreSwarmInstance` — the hand-off record for plug-in forms, built at `PyreRenderer.cs:329-331`
- `PyreRenderer.PlaceParticle` — "THE single source of placement truth" (`PyreRenderer.cs:2252`)
- Placement config enums on the layer: `SwarmSpawnMode {Area, Path}`, `SwarmShapeKind {Circle, Triangle, Square, Pentagon, Hexagon, Custom, Line}`, `SwarmOrient {None, Outward, PathTangent}`, `SwarmTiming {Window, FrameStep}` — `Pyre.cs:18, 26, 33, 39`

**Important qualifier:** the wrapper is generic for *placement*, but three forms bypass it entirely and it is not universal for *drawing*. `RenderLayer` returns before the swarm dispatch for `ShapeForm.Fire` (`:258`), `ShapeForm.Fireball` (`:261`) and `ShapeForm.Playback3D` (`:269`). Fire has a **separate parallel swarm path** gated on `layer.fireSwarmEmitters && layer.swarmEnabled` (`PyreRenderer.cs:399`, described at `:558`) — i.e. one form re-implements the swarm hand-off natively. Plug-in `PyreForm`s receive the instance array and decide for themselves (`PyreRenderer.cs:297-312`).

## E2. Is a swarm composited into ONE buffer before the layer composites?

**It depends on `coalesce`, and this is the load-bearing answer for "can a swarming member hand a bag one contribution".**

**`coalesce == Off` (the default, and every form today per `Pyre.cs:166-171`): each particle composites directly into the layer's buffer, one at a time.** `PyreRenderer.cs:1581-1582`:

```csharp
                DrawParticle(buf, W, H, dp.x, dp.y, own, spec, layer, i, mods, phase, frameIndex,
                             sizeMul, brightMul, sp.orientDeg, lenByIndex);
```

and inside the disc raster, `PyreRenderer.cs:3006`: `Over(buf, y * W + x, cr, cg, cb, a);` — an alpha-Over straight into `buf`. Particles therefore **occlude each other with premultiplied alpha**; there is no separate swarm buffer.

**`coalesce == Fuse` or `Ramp`: the whole set is collected first, then resolved into ONE silhouette.** `PyreRenderer.cs:1558-1580` pushes `FieldParticle`/`RampParticle` records and `continue`s ("no per-particle draw — the whole set is fused after the loop", `:1566` / `:1578`), then `PyreRenderer.cs:1584-1587`:

```csharp
            if (fuse)
                RenderPlusFusedField(buf, W, H, life, spec, layer, mods, phase, frameIndex, fieldParts, cx, cy);
            else if (ramp)
                RenderPlusRampField(buf, W, H, life, spec, layer, mods, phase, frameIndex, rampParts, cx, cy);
```

**Crucially, `buf` in both cases is the LAYER's buffer, not a swarm-private one.** In `RenderFrame` (`PyreRenderer.cs:230-233`) a layer either paints straight into the frame (`fastPath`) or gets `var scratch = new Color32[W * H];`. So:

- **At layer granularity, a swarm already yields exactly one contribution** — one `Color32[W*H]` that `FrameComposer.Apply` composites once (`PyreRenderer.Layers.cs:314` `CompositeLayer(buf, pixels, clip, layer.clipInvert);`).
- **At sub-layer granularity, with `coalesce == Off`, it does not** — the union is baked in by successive `Over` calls and cannot be recovered as a coverage field. Only `Fuse`/`Ramp` produce a real *field* that a combine operator could act on before shading.

## E3. Can a swarm nest today? What breaks?

**No, and there is nothing to break — the construct does not exist.**

`swarmCount` is a plain `int` on the layer (`Pyre.cs:661`); a layer has exactly one swarm, and a swarm's members are particles (a `SpawnPoint` = position + spawnLife + zNorm + orientDeg), not layers. A `SpawnPoint` has no `swarmEnabled` of its own. Searched `Pyre.cs` and `PyreRenderer.cs` for every `swarmEnabled` reference — 9 hits, all reading `layer.swarmEnabled` (`Pyre.cs:660`; `PyreRenderer.cs:271, 298, 399, 558, 710, 1931, 2139, 3691`). None is nested or recursive.

`RenderSwarm` is **not recursive** — it calls `DrawParticle`, `RenderPlusFusedField` or `RenderPlusRampField`, never itself.

**The nearest thing to two levels** is the Fire form's emitter swarm (`PyreRenderer.cs:399`, `:558`): a swarm of *emitters*, each running the fire sim. That is one hardcoded special case in one form, not a general nesting mechanism.

**By contrast, 3D Shaper's shape tree DOES nest** and its cost guards are already written (`public/index.html:983-990`), which is directly relevant to what a Shaper-style bag would cost in Pyre.

---

# F. MASKS

## F1. Is a Pyre layer a mask WRITER or a mask CONSUMER? Is "never both" enforced?

**A single enum field decides the role, and "never both" is enforced structurally by the enum plus two `continue` statements in the plan pass. It is not merely conventional.**

`Pyre.cs:135`:

```csharp
    public enum MatteRole { Draw, WriteMatte, LumaMatte }
```

A layer has exactly one `matteRole` (`Pyre.cs:213`), so it cannot simultaneously be `Draw` and `WriteMatte`. The consumer-side fields (`clipByChannel`, `heightFromChannel`) are **structurally unreachable for a writer** — `PyreRenderer.Layers.cs:94-105`:

```csharp
                bool matteOn = layer.matteEnabled;
                p.isMatte = matteOn && layer.matteRole == MatteRole.WriteMatte;
                p.isLuma = matteOn && layer.matteRole == MatteRole.LumaMatte;
                p.hasClip = matteOn && !p.isMatte && !p.isLuma && written != null
                            && layer.clipByChannel >= 0 && layer.clipByChannel < 4 && written[layer.clipByChannel];
                if (p.isMatte) { if (written != null) written[Mathf.Clamp(layer.matteChannel, 0, 3)] = true; continue; }
                if (p.isLuma) { matteArmed = true; matteOneShot = layer.matteScope == MatteScope.NextLayer; continue; }
                p.isHeightConsumer = matteOn && written != null && layer.heightFromChannel >= 0 && layer.heightFromChannel < 4;
```

Three separate guards: `hasClip` explicitly requires `!p.isMatte && !p.isLuma`; the `continue` at `:102` means a WriteMatte layer never reaches the `isHeightConsumer` line; the `continue` at `:103` does the same for LumaMatte. The serialized data can hold a stale `clipByChannel` on a WriteMatte layer — it is simply never read.

**Two nuances worth recording:**
- There are actually **two independent mask systems**, not one: the numbered-channel system (`WriteMatte` → 4× `float[]` planes → `clipByChannel` / `heightFromChannel`) and the **luma matte** (`LumaMatte` → a mask imposed on layers *above*, scoped by `MatteScope.NextLayer` / `AllAbove`, `Pyre.cs:155-159`, applied at `PyreRenderer.Layers.cs:306-312`). A `Draw` layer can consume both at once.
- A single `matteEnabled` master gate (`Pyre.cs:210`) turns the whole block off while preserving the sub-fields.

**Read-before-write is prevented by construction**, not by convention: `written[]` (`PyreRenderer.Layers.cs:69`) is filled in the *same ascending pass* that sets `hasClip`, so `hasClip` is true only when a writer at a **lower index** already claimed that channel.

## F2. What is written into the mask channel, and when is it resolved?

**Written: a scalar 0..1 per pixel — either coverage (alpha) or luminance×alpha. Never colour.**

`PyreRenderer.cs:912`:

```csharp
                float cov = useLuma ? Luma(scratch[i]) * (scratch[i].a * (1f / 255f)) : scratch[i].a * (1f / 255f);
```

`useLuma` is the layer's `matteWriteLuma` (`Pyre.cs:231`, default `false`). Storage is four `float[W*H]` planes, `PyreRenderer.Layers.cs:257-258`.

**Resolved AFTER fills, borders, pixel modifiers, post modifiers and the layer sim.** The order in `RenderLayerBody` (`PyreRenderer.Layers.cs:168-177`) is: render the layer → composite its border (unless `borderOverMatte`) → `ApplyLayerPost` → `ApplyLayerSim` → return. Only then does `RenderFrame` call `fc.Apply(li, p, scratch, border)` (`PyreRenderer.cs:233`), which is where `WriteMatteCoverage` runs (`PyreRenderer.Layers.cs:275-283`). So the mask reads the layer's **finished pixels**.

**One deliberate exception:** a border with `borderOverMatte == true` (`Pyre.cs:528`) is *deferred* — returned separately (`PyreRenderer.Layers.cs:177`), held in `deferredBorders`, and composited only in `Finish()` (`:342-344`). Its purpose is stated at `PyreRenderer.cs:947-948`: *"the fill can feed a matte while the border composites separately"* — i.e. that border is excluded from the mask.

The luma matte is built differently: `BuildMatteMask(pixels, layer.matteInvert, strength)` at `PyreRenderer.Layers.cs:290`, also from finished pixels.

## F3. If a layer both wrote and read the shared channel today, what would concretely go wrong?

Answering strictly from the code, three distinct failures:

**(1) Read-before-write on the same layer — the value read would be from the previous layer's state, never its own.** In `RenderFrame` the consumer's channel pointer is fetched *before* the layer renders (`PyreRenderer.cs:229`):

```csharp
                float[] heightField = p.isHeightConsumer ? fc.Channel(spec.layers[li].heightFromChannel) : null;
```

and `fc.Apply` (which would do the writing) happens at `:233`, after. So a self-referential layer would consume the channel as it stood *before* its own contribution — the accumulator is strictly one-directional in stack order.

**(2) The `float[]` is genuinely shared and mutated in place — the read would then be corrupted for everyone above.** `WriteMatteCoverage` writes `channel[i] = …` directly into the same array (`PyreRenderer.cs:915-917`). There is exactly one array per channel per frame (`PyreRenderer.Layers.cs:257-258`), no copy-on-write, no versioning. A `Subtract` self-write would carve the layer out of the very field it just read from, and every later consumer of that channel would see the carved version.

**(3) The plan pass would mis-order it, silently.** `hasClip` is gated on `written[layer.clipByChannel]` (`PyreRenderer.Layers.cs:99-100`), and `written[]` is set at `:102` — but `:102` is a `continue`, so under today's code a layer can never appear in both sets. Relax the enum and the ordering becomes dependent on which of lines 99 and 102 runs first for that same index, i.e. on statement order inside the loop rather than on any declared rule. The `p.hasClip` decision is also taken at *plan* time while the write happens at *composite* time, so a plan-time answer would go stale.

**Additional consequence for the per-layer cache:** `PyreLayerKey.cs:16-19` states clip channels and the luma matte are deliberately **not** part of any key, precisely because a layer's own render must not depend on them:

```
// Clip channels and the luma matte are NOT part of any key —
// they are applied from the matte layers' own cached buffers at composite time, so editing a matte layer re-renders
// that matte layer alone.
```

A layer that both wrote and read would break that invariant and produce stale cached buffers. (A heightmap consumer *is* handled — it carries a `FrameVariant` naming its writers, `PyreLayerKey.cs:89-106` — which shows the cost of the exception: one extra dependency-tracking mechanism per direction of coupling.)

---

# G. COST

## G1. What IS "a pass over the canvas" concretely?

**First, the correction that matters most: a Pyre layer does NOT generally cost a full-canvas pass. Particle rasters are bounding-box-limited. But there are five specific things that ARE full-canvas, and one of them is a cliff.**

### Canvas sizes and frame counts actually used (measured from the assets on disk)

Defaults, `Pyre.cs:1185-1186`:

```csharp
        [Min(1)] public int canvasSize = 64;
        [Min(1)] public int frameCount = 16;
```

The canvas is always square: `Pyre.cs:1259-1260` — `public int Width => Mathf.Max(1, canvasSize);` and `Height` returns the identical expression.

I read `canvasSize`/`frameCount` out of all 33 Pyre `.asset` files under `D:\UNITY\Laubrary Dev\Assets\`:

- **`canvasSize`: 64 in 29 of 33 files.** The only exceptions are four Imported grenade assets: 18, 31, 38 (and `Lathe/New Lathe.asset` at 64 with no `frameCount`). **No asset exceeds 64.**
- **`frameCount`: 6 to 49.** Distribution: 16 is by far the most common (13 files), then 8 (4), 20 (2), 26 (2), 30 (2), and singletons at 6, 21, 22, 31, 32, 35, 49.

So the working envelope is **64×64 × ~16–32 frames**, i.e. **4,096 pixels per frame, ~65k–131k pixels per full animation**.

### Buffer types

**Colour buffers are byte (`Color32[]`), scalar fields are float (`float[]`).**

- Layer/frame pixels: `Color32[]` — `PyreRenderer.cs:231` `var scratch = new Color32[W * H];`, `PyreRenderer.Layers.cs:246` `buf = new Color32[W * H];`. 4 bytes/px.
- Matte channels: `float[]` — `PyreRenderer.Layers.cs:258` `channels[c] = new float[W * H];`. 4 bytes/px, ×4 channels.
- Fuse/Ramp/heightmap fields: `float[]` — `PyreField.AccumulateDomes(float[] field, …)`, `PyreField.cs:155`.
- Border distance transform: `float[]` — `BorderInsideDistance`, `PyreRenderer.cs:958`.

Note the internal maths is float throughout; the **quantisation to bytes happens at each `Over`** (`PyreRenderer.cs:3006`), so a many-particle stack accumulates 8-bit rounding per composite.

### The per-pixel loops — what is bounded and what is not

**Bounded (bounding box of the primitive):** `PyreRenderer.cs:2981-2986`:

```csharp
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
                int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
```

The same `Max(0, FloorToInt(c − r))` bounding-box pattern appears at `:3022`, `:3183`, `:3300`, `:3408`, `:3505`, `:3585`, `:3733`, `:3988`, `:4305`, `:4503`, `:4672` — i.e. for every flat form, facet solid, orb, ring, streak, glow and text glyph.

**THE CLIFF — any enabled geometry modifier turns every particle into a full-canvas scan.** `PyreRenderer.cs:3019`:

```csharp
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }   // a warp can pull any pixel into the disc, so scan the whole canvas (as RasterShape does)
```

With `swarmCount = 8` particles that is 8 × 4,096 = 32,768 pixel iterations per layer per frame instead of (typically) 8 × a few hundred. This is a **per-particle** multiplier, so it scales with swarm size.

**Unconditionally full-canvas (`for y in 0..H, for x in 0..W`):**

| Pass | Location | Cost |
| --- | --- | --- |
| Background paint | `PyreRenderer.Layers.cs:128-137` (fill) / `:142` (flat) | 1× W·H per frame |
| `CompositeLayer` (per isolated layer) | `PyreRenderer.cs:926` | 1× W·H per non-fastpath layer |
| `OverOrCopy` (fastpath layer) | `PyreRenderer.Layers.cs:325` | 1× W·H |
| `WriteMatteCoverage` (per WriteMatte layer) | `PyreRenderer.cs:910` | 1× W·H |
| `RenderPlusFusedField` | `PyreRenderer.cs:1640-1641` | W·H **× n particles** (see G5) |
| `PyreField.AccumulateDomes` (Ramp, ×3 fields) | `PyreField.cs:159-163` | W·H × n × 3 |
| Border distance transform (2 sweeps) | `PyreRenderer.cs:958-960` | 2× W·H per bordered layer |
| Luma `ApplyMatte` | `PyreRenderer.Layers.cs:309` | ≥1× W·H per affected layer |
| Global post modifiers | `PyreRenderer.Layers.cs:358` `post.Apply(buf, W, H)` | 1× W·H each, on the finished frame |

**So the honest cost model is:** *a layer costs ~2 full-canvas passes (its own buffer allocation+clear, and its composite) plus the bounded rasters of its particles — UNLESS it has a geometry modifier, a Fuse/Ramp coalesce, or a border, any of which turns it into 1–3n full-canvas passes.*

## G2. Caching — what exactly, keyed on what, invalidated when, does the key include time?

Three separate caches. **None of them is used by the runtime bake path — `RenderFrame` (`PyreRenderer.cs:214`) consults no cache at all.** The layer cache is an editor-preview mechanism (`PyreLayerKey.cs:1-2`: *"for the per-layer preview cache (PyreWindow.FrameCache.cs …)"*).

### `PyreLayerCache.cs` — the rendered-buffer store

**What is cached:** `Runtime/Pyre/PyreLayerCache.cs:18-23`:

```csharp
        public sealed class Slot
        {
            public ulong variant;
            public Color32[] pixels;
            public Color32[] border;     // the over-matte border rim, or null
        }
```

i.e. one **finished layer buffer** (and its deferred border) per **(layer key, frame index)**.

**Keyed on:** `Entry.key` (a `ulong`) → `Slot[] frames` indexed by frame, each slot re-checked against a `variant`. `PyreLayerCache.cs:51-61`:

```csharp
        public bool TryGet(ulong key, int frame, ulong variant, out Slot slot)
        {
            ...
            var s = e.frames[frame];
            if (s == null || s.variant != variant) return false;
```

**Invalidated when:** LRU eviction under a byte cap (default **64 MiB**, `PyreLayerCache.cs:44` `capacityBytes = 64L << 20`), with a generation guard so entries touched this generation are never evicted (`:96-112`). Plus `Clear()` (`:84`). Content changes invalidate implicitly by producing a different key.

**Does the key include time/frame?** **The 64-bit key does NOT; the frame is a separate array index, and there is a per-frame `variant`.** `PyreLayerKey.LayerKey` (`PyreLayerKey.cs:72-85`) mixes: the spec part + the layer's index + every serialized field of `PyreLayer` except `enabled`, `name`, `shapeAdvanced`. `SpecPart` (`:43-57`) mixes `canvasSize`, `frameCount`, `seed`, and the global geometry/pixel modifiers with their positions. **No `frameIndex` anywhere.**

The per-frame `variant` (`PyreLayerKey.cs:89-106`) is `0` in the common case, and non-zero only for two situations:

```csharp
        public static ulong FrameVariant(Pyre spec, PyreRenderer.LayerPlan[] plans, ulong[] layerKeys, int li)
        {
            ref var p = ref plans[li];
            if (!p.fuseBackground && !p.isHeightConsumer) return 0;
            ...
```

— a background-fused buffer (mixes `BackgroundPart`) or a heightmap consumer (mixes the keys of every active writer below it on its channel).

**Explicitly excluded from every key** — `PyreLayerKey.cs:17-19`: clip channels and the luma matte, "so editing a matte layer re-renders that matte layer alone." Also excluded: global **POST** modifiers (`:13-14`), because they run after the cache.

The hashing itself is FNV-1a over reflected serialized fields (`PyreLayerKey.cs:34-35, 121-139, 171-222`), with special cases for `Gradient`, `AnimationCurve`, `PyreForm.ContentHash()`, Unity object instance IDs, and `IList`. Depth-capped at 12 (`:36`). Known false-negative documented at `:141-153`: a brand-new layer re-keys once at its first undo/reload.

### `PyrePrepassCache.cs` — the plug-in form pre-pass store

**Generic `PyrePrepassCache<T>` keyed by a weak table on `form.PrepassIdentity`** (`PyrePrepassCache.cs:30`), holding one `Snapshot { key, value }` per form (`:28-29`). Key, `PyrePrepassCache.cs:58-68`:

```csharp
        public static int KeyOf(in PyreFormCtx ctx, PyreForm form, int extraHash = 0)
        {
            unchecked
            {
                int h = form != null ? form.ContentHash() : 0;
                h = (h ^ ctx.W) * 16777619; h = (h ^ ctx.H) * 16777619;
                h = (h ^ ctx.frameCount) * 16777619; h = (h ^ ctx.seed) * 16777619;
                h = (h ^ ctx.layerSalt) * 16777619; h = (h ^ extraHash) * 16777619;
                return h;
            }
        }
```

**No frame index** — this is deliberately the frame-*independent* part of a form's work (e.g. a spawn trace computed once for the whole timeline). Thread-safe via a `lock` + double-check (`:43-51`) and `Interlocked` hit/miss counters. Invalidated by content change (via `ContentHash`) or explicit `Invalidate(form)` (`:55`).

### `PyreWindow.FrameCache.cs` — the editor orchestrator

`Editor/Pyre/PyreWindow.FrameCache.cs` (25,172 B) drives the above and accumulates `fillRenderMs += r.ms;` (`:280`) — a live counter, not a stored benchmark.

## G3. Parallelism — is there any?

**Yes, but only at whole-job granularity, only in the editor preview, and gated behind a safety predicate. The runtime bake path is single-threaded managed C#.**

**Evidence for the runtime path being single-threaded:** `RenderFrame` (`PyreRenderer.cs:214-236`) is a plain sequential `for` over layers calling sequential rasters. Searched all of `Runtime/Pyre` + `Editor/Pyre` for `Parallel.For`, `Unity.Jobs`, `IJob`, `Task.Run`, `new Thread`, `BurstCompile`, `System.Threading`. Complete hit list (11 hits, 4 files):

**(a) Two worker-thread pools — `PyreFrameFill.cs` and `PyreLayerFill.cs`.** `PyreLayerFill.cs:1-11`:

```
// PyreLayerFill — runs render jobs for one spec on every core, each job against that worker's own spec clone.
//
// The per-layer preview cache hands out two kinds of job: "render layer L of frame F on its own" and "compose
// frame F from these layer buffers". Both are pure functions of (spec, inputs) and so run anywhere; what a worker
// must not share is scratch (forms, modifiers and sims keep Prepare'd state on their instances), so — exactly as
// PyreFrameFill does for whole frames — every worker owns a deep clone of the spec ...
```

Thread count `PyreLayerFill.cs:36`: `public static int DefaultWorkers => Math.Max(1, Environment.ProcessorCount - 1);`. Threads created at `:66-74`, each with `PyreFrameFill.CloneForWorker(spec)` (`:67`).

**Gated by a safety predicate:** `PyreLayerFill.cs:13-14`: *"Specs that are not parallel-safe (PyreRenderer.IsParallelSafe) must not be given to this class; the caller runs the same delegates on the main thread against the real spec instead."* The exclusion list is at `PyreRenderer.cs:195-207`: `Text`, `Playback3D`, `Fire`, `Fireball`, `Sprite`, any enabled simulation modifier, and any fill that samples a sprite.

**The unit of parallelism is one (layer, frame) job or one whole frame — never a scanline or a pixel range.** There is no `Parallel.For` inside any raster.

**(b) Burst — exactly one kernel.** `Runtime/Pyre/Forms/Kiln/ArcRaster.cs:37`:

```csharp
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
```

Reached "by Burst direct call" (`ArcRaster.cs:13`) because the surrounding code is main-thread-only; a second `[BurstCompile]` at `:70`, and a warn-once guard at `:64` (`if (_warned || BurstCompiler.IsEnabled) return;`). This is **one form's rasteriser (ArcBurst)** — nothing else in Pyre is Bursted.

**(c) `PyrePrepassCache.cs:22` `using System.Threading;`** — only for `Interlocked` counters and a `lock`; it does not create threads.

## G4. Are there any MEASURED timings in the repo?

**For Pyre: NO. I found none, and one prior report explicitly states none were taken.**

Searched `D:\UNITY\Laubrary Dev\.agenthq\workspace\` recursively for `[0-9]+ ?(ms|milliseconds)`, `measured`, `benchmark`, `Stopwatch`.

**Every "measured" in the T-0098 evidence files means "counted by reading source", not "timed"**, e.g. `T-0098/B-compatibility.md:66`: *"Measured by counting `ApplyGeo` / `ResolveSample` / `anyGeo` occurrences inside each draw routine's body"*; `T-0098/D-verification.md:27` and `:66` likewise count layers.

**The explicit disclaimer** — `.agenthq/workspace/T-0098/F-decomposability.md:241`:

```
4. **Performance.** I did not measure the cost of a group of *N* members, of a `float[]`-per-plane substance handover, or of Torch's 3× supersample surviving a substance split. All three are cheap to measure and none is measured here.
```

### The only real millisecond numbers in the workspace — and they are NOT Pyre

`.agenthq/workspace/T-0098/P2-tapestry.md:146-148` reports a table:

| Size | plasma | steel | steel_clean |
| --- | --- | --- | --- |
| 128×128 | 32 ms | 116 ms | 61 ms |
| 256×256 | 114 ms | 505 ms | 253 ms |
| 512×512 | 769 ms | 3750 ms | 1752 ms |

**⚠️ These are Kiln / Tapestry Surface timings in numpy, a different project entirely.** `P5-verification-4.md:67` says so directly: *"P2 §4.4 measures two different projects. **Tapestry Surface** has no retry paths at all; at 128×128 (numpy) it is `plasma` 32 ms, `steel` 116 ms, `steel_clean` 61 ms."* Also from P2: *"Tapestry Shape, at its native 256×256: `plates` 33–124 ms typical, `lines` 64–102 ms typical — but with occasional spikes to 588 ms and 1773 ms"* (`P2-tapestry.md:150`), and `:154`: *"At, say, 24 frames, even an optimistic 10 ms/frame is 240 ms per full rebuild."* **Do not attribute any of these to Pyre.**

`Q1-3d-rotation.md:156` contains an **estimate, not a measurement**: *"At 6,144 pixels × ~10 evals × (1 primary + 1 shadow ray) × 12 layers ≈ 1.5M composite field evaluations"* — an arithmetic projection, explicitly about a future Shaper design. `:158` notes T-0115 "gives no millisecond figure".

`.agenthq/workspace/T-0101/unity-batch.log` has Unity's own domain-reload numbers (548 ms etc.) — editor startup, irrelevant to render cost.

**Live instrumentation exists but stores nothing.** `PyreFrameFill.cs:56` `public double RenderMsTotal => Interlocked.Read(ref _renderTicks) * 1000.0 / Stopwatch.Frequency;` and `:109-115` time each job; `PyreLayerFill.cs:30, 54` the same (`WorkMsTotal`, "the serial-equivalent cost"); `PyreWindow.FrameCache.cs:280` accumulates `fillRenderMs`. These are **runtime counters shown in the editor** — no logged figures, no benchmark script, no committed results file.

**For 3D Shaper:** it defines cost *limits* rather than measurements — `MAX_GRID_CELLS = 68000` (`public/index.html:982`), and the README's stated safeguard thresholds ("more than roughly 4 million on-screen pixels, more than 128 visible layers"). Those are budgets, not timings. There are QA scripts under `.local-data/` (`arc_perf.py`, `canvas_anim_qa.py`) whose outputs I did not read; whether they contain real numbers is **UNVERIFIED**.

## G5. Order-of-magnitude float ops / writes per pixel for one primitive's coverage pass

**This is a READING of the hottest loop, not a measurement.**

The hottest and most common path is the fast Disc raster, `PyreRenderer.cs:2985-3007`:

```csharp
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        if (fDoSpin) { float rx = dx * fCos - dy * fSin; float ry = dx * fSin + dy * fCos; dx = rx; dy = ry; }
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d > radius) continue;
                        float edge = d <= inner ? 1f : 1f - Mathf.InverseLerp(inner, radius, d);
                        if (fillSpatial) { col = EvalFill(fill, life, dx / radius, dy / radius, x, y, W, H); ... }
                        float a = alpha * col.a * edge;
                        if (a <= 0.002f) continue;
                        Over(buf, y * W + x, cr, cg, cb, a);
                    }
```

Reading it, for a **solid-fill, no-spin, no-modifier disc** (the default and most common configuration):

- **Rejected pixel** (outside the disc, ~21% of the bbox): 2 subs, 2 muls, 1 add, 1 `sqrt`, 1 compare ≈ **~7 float ops, 0 writes**.
- **Accepted pixel:** the above, plus `InverseLerp` (1 sub, 1 sub, 1 div, 1 clamp) + 1 sub for `edge`, + 2 muls for `a`, + 1 compare, + `Over` (which reads the destination `Color32`, converts 4 bytes to float, does the standard source-over blend and writes 4 bytes back — call it ~12–15 float ops and 1 read + 1 write of 4 bytes) ≈ **~25–30 float ops, 1 texture read + 1 texture write**.

**Order of magnitude: ~10¹ float ops per pixel and exactly 1 RGBA byte write per covered pixel.**

The multipliers that change this by an order of magnitude or more, all read from the same file:

- **Spatial fill** (`fillSpatial`, `:2998-3003`): `EvalFill` per pixel — a gradient/noise/texture evaluation. Noise alone (`PyreField.ValueNoise`, `PyreField.cs:224`) is 4 hashes + 2 smoothsteps + 3 lerps ≈ **+30–50 ops/px**.
- **Per-particle spin with a spatial fill** (`:2989-2994`): +4 muls, +2 adds.
- **Any geometry modifier** (`:3019`): the loop becomes W·H **and** each pixel runs `ApplyGeo` through the whole modifier chain before the distance test — the single largest multiplier available (≈5× more pixels for a typical disc, plus per-modifier cost).
- **Fuse coalesce** (`:1640-1646`): W·H pixels × `PyreField.Sample`, which loops **all n particles** (`PyreField.cs:71-82`, ~6 ops per particle plus 2 early-outs). At 64×64 with 8 particles that is 4,096 × 8 ≈ **33k kernel evaluations per layer per frame**; the per-pixel figure becomes ~**50 ops** rather than ~25.
- **Ramp coalesce**: the same shape, three times over (density/heat/height fields via `AccumulateDomes`, `PyreField.cs:155-179`), each `SmoothMax` adding a divide + lerp + 2 muls per covered dome.

**Absolute scale for calibration:** at the near-universal 64×64, a full-canvas pass is **4,096 pixels**. A 16-frame spec with 4 layers, no modifiers, is on the order of 16 × (4 composites + 1 background) × 4,096 ≈ **330k pixel-ops of compositing** plus the bounded particle rasters. That is small. The cliffs are `anyGeo` (×W·H per particle) and `Fuse`/`Ramp` (×n per pixel), not the baseline.

---

# Contradictions with the assumptions in the brief

Recorded plainly, per the brief's instruction to say so loudly.

1. **"A shape node is either a primitive or a bag of children" has NO analogue in Pyre.** Pyre's layer list is flat; the only recursion-shaped structure in either tool is 3D Shaper's `compileShape`/`evalShape` (`public/index.html:1073-1141`). Any bag in Pyre is new construction, including the transform composition it would need (C3).

2. **"Each child carries a combine mode" is true of 3D Shaper and FALSE of Pyre.** Pyre's nearest equivalent, `MatteCombine`, is stored on the member but combines into a *mask channel*, not into a drawn silhouette, and it has no soft mode and no intersect.

3. **D3's premise is refuted.** The brief cites a prior claim that "most instances start on the shared clock so they pop in mid-animation." Pyre swarm particles have a real per-instance clock `own` (`PyreRenderer.cs:1510-1512`) and the default spawn envelope deliberately staggers births across the first half of the timeline (`Pyre.cs:1076-1086`). They appear mid-animation *by design*, not because they share a clock. What genuinely does run on the shared clock: the whole-cloud transforms, and the Fuse/Ramp field passes.

4. **"Its four modes are Add / Add-soft / Subtract / Subtract-soft, order significant, no Intersect" — fully CONFIRMED** for 3D Shaper (`project_document.py:19`, `public/index.html:459-464`, `:1113-1141`).

5. **"strength + sharpness, two genuinely independent knobs" — CONFIRMED for `softAdd`, QUALIFIED for `subtractSoft`.** In `subtractSoft`, `viscosity` is read twice (as cut amount *and*, via `sin(strength·π)`, as blend band), so it is not a pure band-width axis there — `public/index.html:1134-1136`.

6. **The soft-fuse formula is NOT the polynomial smooth-min.** The file contains both; the fuse modes use `smoothMinShaped` (`min(a,b) − hⁿ·k/(2n)`, `:1007`). The polynomial `smoothMin`/`smoothMax` (`:996-997`) is used only for the model-layer z-buffer band with a **fixed constant** `FUSION_CELLS = 1.7`, not a user knob. Pyre's `SmoothMax` (`PyreField.cs:139-144`) is the mirror of the *polynomial* one, i.e. of the z-buffer function, not of the fuse function.

7. **"What IS a pass over the canvas… find the per-pixel loop(s) that a layer costs" presumes layers cost full-canvas passes. They mostly do not.** Particle rasters are bounding-box-bounded (`PyreRenderer.cs:2981-2984` and eleven sibling sites). The full-canvas costs are the composite, the background, the matte write, and — the cliff — anything with a geometry modifier (`:3019`) or a Fuse/Ramp coalesce.

8. **"Is there any parallelism… or is it single-threaded managed C#" is a false dichotomy — both are true of different paths.** Two real worker-thread pools exist (`PyreLayerFill.cs`, `PyreFrameFill.cs`) plus one Burst kernel (`ArcRaster.cs:37`), but they parallelise *whole (layer, frame) jobs in the editor preview only*, are gated by `PyreRenderer.IsParallelSafe` (`:195-207`), and the runtime `RenderFrame` bake path uses none of them.

9. **The G4 timings that exist are from a different project.** The 32/116/61 ms and 769/3750/1752 ms figures in `T-0098/P2-tapestry.md` are Kiln/Tapestry numpy measurements, per `P5-verification-4.md:67`. **There is no measured Pyre timing anywhere in this repo.**

10. **Canvas size is not a free parameter in practice.** 29 of 33 Pyre assets are 64×64 and none exceeds it, so any cost reasoning anchored on a larger canvas is extrapolation, not observation.

---

## Explicitly searched and NOT FOUND

| Claimed / expected thing | What I searched | Result |
| --- | --- | --- |
| Intersect combine mode (either tool) | `Intersect` case-insensitive across `Runtime/Pyre`, `Editor/Pyre`; `COMPONENT_MODES` in `project_document.py`; `FUSE_MODES` in `index.html` | **Does not exist.** All Pyre hits are comments or ray-segment geometry. |
| Pyre shape/layer nesting (parent→child) | Every serialized field of `PyreLayer` (`Pyre.cs:180-775`); every `swarmEnabled` reference (9 hits); recursion in `RenderSwarm` | **Does not exist.** Flat `List<PyreLayer>`, no self-recursion. |
| Pyre per-layer time offset / time-scale | `timeScale`, `timeOffset`, `speed`, `phaseOffset` in `Pyre.cs`; the `PyreLayer` field enumeration | **Does not exist.** Only `startFrame`/`endFrame` (int window). |
| Pyre soft combine BETWEEN two layers or shapes | `smoothmin`, `softness`, `viscosity`, `sharpness`, `blend` across `Runtime/Pyre` | **Does not exist.** `SmoothMax` (1 knob) operates inside one layer's own particle set only. |
| `Matrix4x4` / transform composition helper in Pyre | `Matrix4x4` in `PyreRenderer.cs` | **Zero hits.** Only the non-composable `ApplyShapeTransform` (`:2077`). |
| Pyre pivot / origin field | `pivot`, `origin`, `anchor` in `Pyre.cs` | **Does not exist.** Only `streakAnchor`, an unrelated length bias. |
| Measured Pyre render timings | `[0-9]+ ?ms`, `measured`, `benchmark`, `Stopwatch` across `.agenthq/workspace/` | **None.** Live counters only; the one ms table is a different project. |
| A stored benchmark script for Pyre | `find` for cache/benchmark files under `Assets/Packages/Laubrary` | **None.** |
| Colour or depth on a 3D Shaper shape component | `normalise_component` return dict (`project_document.py:329`) | **Confirmed absent** — no `materialId`, `depth`, `extrusion`, `bevel`, `edge`. |

## Open items I did not verify

- **The seven Kiln plug-in forms' internal render loops** (`PyreInferno.cs`, `PyreArcBurst.cs`, `PyreTorch.cs`, `PyreOrb.cs`, `PyrePlasmaBloom.cs`, `PyreForkBlast.cs`, `Jet/*`) — I read their headers and the shared dispatch, not their per-pixel bodies. Whether any maintains its own per-instance clock or its own combine is **UNVERIFIED**. (`PyreForkBlast.cs:10` and `PyreInferno.cs:548` both mention "union-accumulated into one heat field" / "union density / heat / smoke", which suggests at least two of them do internal unions — worth a follow-up read.)
- **3D Shaper's `renderModelGrid` per-cell loop** — I read the SDF/combine layer (`compileShape`/`evalShape`) and the z-buffer fusion line, not the full model-layer rasteriser.
- **Whether `.local-data/arc_perf.py` contains real Pyre or Shaper timings** — not opened.
