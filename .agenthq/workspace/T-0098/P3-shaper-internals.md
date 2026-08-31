# P3 — Shaper internals: pixel border, 3D rotation/Z, arcs, polygon & star

Investigator P3, task T-0098 ("Future of Pyre"), project Laubrary_Dev. Read-only investigation; nothing in `D:\CODEZ\AgentHQ\3D Shaper` or the Unity project was modified, no Unity/Coplay used.

**Sources read.** `C-shaper3d.md` and `J-shaper-primitives.md` (this workspace) as briefed, then verified against source: the reference browser app at `D:\CODEZ\AgentHQ\3D Shaper\.agenthq\attachments\T-0030\20260829T063646Z_retro_scifi_flyer_lab_draft6-1.html` (read in full — 55 physical lines, ~30.7 KB, near-minified so a "line" is a whole function group), 3D Shaper's `public\index.html` (regions `:1010-1470`, `:1595-1596`, `:2428`, `:2522`, `:2652`, `:3805-3824`, `:1487-1500`) and `project_document.py` (`:16`, `:27`, `:229`), plus `Assets\Packages\Laubrary\Runtime\Pyre\Pyre.cs` (`:54-56`, `:113`, `:388-392`, `:428-441`, `:485-505`).

**Where I confirm / correct the prior reports.** `J-shaper-primitives.md:23,35,36` is correct on the 8 primitives, on Shaper's star being a hardcoded 5-lobe cosine with zero parameters, and on hexagon/octagon being hand-fitted approximations — all verified at `public/index.html:1032-1041` and `project_document.py:16`. `C-shaper3d.md:11`'s description of the renderer (per-cell SDF rasteriser, height map, z-buffer, normals from neighbouring heights) is also correct — I verified the normals at `public/index.html:1491-1492`. **One correction to `C-shaper3d.md:11`:** it lists "Pixel edges" and "Layer border" as two separate Shaper-native features without noting that the pixel-edge feature is an explicit, acknowledged port of the reference app's pattern system — Shaper's own code comment at `public/index.html:1255` says so in as many words ("matching the reference prototype's patternMat/stripeMat/raster split"). That link is the whole subject of §1 below and was not previously recorded anywhere in this workspace.

---

## 0. TL;DR for the redesign

| Owner's item | Verdict |
| --- | --- |
| 1. Pixel border | **Found.** It is not a border generator. It is a *1-D indexed material strip sampled through a parameterisation of the shape*, where the material entry carries **height as well as colour**. Shaper reimplemented the colour half well and **dropped the height half** — which is exactly the half the owner is describing as "mix fills and extrusion". |
| 2. 3D rotation + Z translate | **Z translate: does not exist today** (the field named "Depth" is extrusion thickness, not position; Z is `layerOrder × 0.75`). Cheap to add. **3D rotation: impossible in the current representation** (single-valued screen-aligned height field, no camera, no geometry). Must be decided before the rebuild — retrofitting is not a feature addition, it is replacing the renderer. |
| 3. Arcs on every primitive | Natural and cheap. Two orthogonal operators, both **primitive-agnostic** (they act on the SDF *result*, not on any primitive's formula): an angular wedge intersection, and an SDF shell/onion. Shaper already computes both quantities — just for the pattern, not the shape. |
| 4. Polygon + star engines | Both are single new cases in one 11-line function that the code itself advertises as the extension point (`public/index.html:1030`). Pyre already has better versions of both; the work is porting Pyre's parameters *in*, not inventing them. |

---

## 1. The reference app's "pixel border"

### 1.1 Where it is

`D:\CODEZ\AgentHQ\3D Shaper\.agenthq\attachments\T-0030\20260829T063646Z_retro_scifi_flyer_lab_draft6-1.html`

Three byte-identical copies exist (md5 `301abaa3e5f293f58afcdbd99070fee0`), attached to three different 3D Shaper tasks — T-0018, T-0027 and T-0030 — each under `D:\CODEZ\AgentHQ\3D Shaper\.agenthq\attachments\<task>\`. AgentHQ stores attachments as `<UTC-timestamp>_<original filename>` in a per-task folder, which is why the same file appears three times. It is a single self-contained HTML file: "retro sci-fi flyer lab, draft 6-1". It is the *only* non-3D-Shaper HTML/JS in the project. Nothing in the 3D Shaper folder proper is a copy of it.

It is a small standalone toy: a 64×64 pixel canvas, up to N layers, 4 materials, one light, one shadow ray-march, and a set of retro backgrounds. It is unmistakably the ancestor — 3D Shaper's layer schema is a superset of its layer schema, field for field.

### 1.2 What the function actually is

Everything hangs off two things: **a per-layer array of material indices** and **the fact that a material carries a height offset, not just a colour**.

**The strip.** Each layer owns `seq`, an array of small integers, each naming one of the four materials. Default `[0,0,1,1,2,2,3,1,0,0,1,2,3,2,1,1,0,0]` — 18 entries (reference app, `:13`). In the UI it renders as a literal row of 18 coloured square buttons (`:27`, class `pixel`). You **long-press one to pick the paint material, then tap as many as you like to paint them** (`:10` note text; `bindLongPress`/`showPixelPopover`, `:22-23`). So the "pixels" the owner refers to are UI pixels you hand-author — a tiny 1-D pixel-art strip that becomes the shape's decoration.

**Where the strip gets applied.** Per rasterised cell inside a layer's silhouette, in `raster()` at `:50`:

- The 2-D signed distance `d` to the silhouette is evaluated (`sdf`, `:30`), and `depthInto = clamp(-d, 0, 1)` — 0 exactly at the outline, 1 deep inside.
- **Layout "Edge / radial":** the cell is part of the pattern when `depthInto <= edgeCoverage`. If it is, the strip is indexed by the cell's **polar angle** around the shape (`atan2(Y/ry, X/rx)` normalised to 0..1, then `patternMat`, `:31`). If it is not, the cell just uses the layer's plain `fill` material.
- **Layout "Stripes":** every cell inside the silhouette is patterned (`usePattern = true` unconditionally), and the strip is indexed by the cell's **projection onto a direction** set by `stripeAngle`, normalised by the shape's extent along that direction (`stripeMat`, `:32`).

**The parameters.**

| Parameter | Range | What it does |
| --- | --- | --- |
| `seq` | 18 entries, each a material index | the authored strip itself |
| `edgeCoverage` ("Edge cover") | 0 – 1, step 0.05 (`:26`) | how far the strip reaches inward from the outline |
| `patternLayout` | `edge` / `stripes` (`:26`) | angular ring vs parallel bands |
| `mode` ("Pattern") | `repeat` / `stretch` / `mirror` (`:26`) | how the strip maps onto the run |
| `stripeAngle` | 0 – 345°, step 15° (`:26`) | band direction, stripes layout only |

**Turning `edgeCoverage` up.** At 0 nothing is patterned — the shape is its plain fill. At 0.18 (the default first layer, `:14`) you get a thin decorated rim; the strip's colours read as pixels marching *around the outline*. As you raise it the ring thickens inward. **At 1 the ring reaches the centre and the whole shape is nothing but the pattern** — because the pattern is indexed by angle, a fully-covered disc becomes a pie/sunburst of angular wedges rather than a fill with a border. That is precisely the owner's "can cover the whole shape if configured to". The `stripes` layout is the same idea with a linear parameter instead of an angular one, which is why the UI note says "Stripe mode lays the same pattern across the whole shape" (`:10`).

⚠️ **One real quirk worth knowing before copying the maths.** The reference's `sdf` (`:30`) is inconsistent about units. `ellipse`, `diamond`, `hexagon` and `octagon` return a **normalised** distance (−1 at centre, 0 at boundary), so `edgeCoverage` behaves as a clean 0..1 fraction of the radius on those. `rect`, `roundrect`, `capsule` and `triangle` return **canvas-pixel** distances, so `clamp(-d,0,1)` saturates one pixel inside the outline and `edgeCoverage` below 1 gives you a literal one-pixel-wide border. The same bug hits `layerHeight` (`:33`), so the "Rounded"/"Half dome" profiles only dome properly on the normalised shapes. **3D Shaper fixed this**: `inside = clamp01(-distance/span)` with `span = min(halfW,halfH)` (`public/index.html:1339, :1425`), and every primitive multiplies back to canvas units (`primitiveSdf`, `:1032-1041`). Do not port the reference's version.

### 1.3 "The pixels act as a divider … mix fills and extrusion" — this is the important part

This is the sentence the redesign has to understand, and it is one term in one expression. The reference app, `:50`:

> the cell's height = layer base + the layer's height profile at this depth + **the chosen material's own protrusion**, where the protrusion counts **in full if this cell is patterned and only a quarter if it is plain fill**

A material in the reference app is `{color, surface, pro, round, shine, glowPower, glowRadius}` (`:14`). **`pro` is "Protrusion", a slider from −8 to +28** (`:7`). So a material is not a colour — it is a colour *and a height*, and it can be either raised or sunk.

Three consequences, all real, all visible:

1. **The strip is relief, not decoration.** Because each strip entry names a material and each material has its own `pro`, an authored strip like `[0,0,1,1,2,2,3,…]` becomes a sequence of ridges and grooves marching around (or across) the shape. The colours and the bumps are the same authored data — you cannot paint one without the other. That is the "mix fills and extrusion".
2. **The relief is genuinely lit and genuinely casts shadows.** The height map feeds the surface normal by finite differencing neighbouring heights (`:52`), and it feeds a shadow ray-march that steps toward the light and asks whether anything stands above the ray (`shadowAt`, `:48`). So a raised pattern pixel self-shades its own groove and throws a shadow onto its neighbour. This is why the output looks carved rather than printed.
3. **This is the "divider".** The height map is a z-buffer shared by *all* layers (`if (z >= hm[k]) { hm[k] = z; mm[k] = mi }`, `:50`). A pattern pixel with a high `pro` therefore competes for the pixel against other layers, and can win — a raised ring around layer A can stand proud of layer B that overlaps it. A pattern pixel with a *negative* `pro` sinks below and lets the layer underneath show through. So the strip physically separates neighbouring bodies: it reads as a seam, a gasket, a rivet line, a panel gap. It divides in the depth axis, not merely in the colour axis.

The `× 0.25` on the plain-fill branch is the small design trick that makes it work: the un-patterned interior only gets a quarter of its material's protrusion, so a patterned band always stands proud of (or sunk below) the face it sits on, without you having to tune two numbers against each other.

### 1.4 Did 3D Shaper reimplement it?

**Yes — the parameterisation half, thoroughly and better. No — the extrusion half, not at all.** The code says so itself: the comment above the port reads *"Pixel edge registry, in three parts, matching the reference prototype's patternMat/stripeMat/raster split"* (`public/index.html:1255`).

**What was ported and improved** (`public/index.html:1263-1305`, schema at `:2428`/`:2522`, UI at `:3816-3824`, animation bounds at `:1595`):

| Reference | 3D Shaper | Change |
| --- | --- | --- |
| `seq`, fixed 18 cells | `edge.materialIds` with `edge.resolution` **2–128** cells | authorable strip length; animatable |
| `edgeCoverage` 0–1 | `edge.depth` 0–1, default 0.13, tooltip *"1 covers the whole face"* (`:3824`) | same parameter, correct units (see §1.2 quirk) |
| repeat count hardcoded to the magic constant `2.35` (`:31`) | `edge.repeats` **1–16, forced whole** (`:1291`) | fixes a real seam: a fractional repeat count closed the ring part-way through the strip, leaving one mismatched cell |
| angle = ellipse-normalised `atan2` | **true perimeter arc-length** table, traced radially and integrated, with a documented fallback to the old angle when the trace is unsafe (`edgeArcTableFor`, `:1244`; `edgeArcFraction`, `:1251`) | cells are evenly spaced along the *border*, not along *angle* — so they stop bunching at the ends of an elongated or concave silhouette |
| — | `edge.orientation` 0–360 | slides the strip's start point right around the outline |
| — | `edge.reach` 0–1 + `edge.position` 0–1 (`:1264-1273`) | **the strip covers only part of the perimeter, positioned anywhere** — cells outside fall back to the face material |
| `mode` repeat/stretch/mirror, `stripeAngle` | identical, same three modes (`:1290-1296`) | carried over verbatim |
| materials are 4 fixed slots | edge cells draw from the shared material library by ID | proper asset model |
| — | separate `edge.outline` — a true silhouette border, occlusion-aware (`:1397-1411`) | a genuinely different feature, added on top |

**What was dropped.** There is no per-material protrusion in 3D Shaper. Its material is `{primaryColor, secondaryColor, surface, reflection, emission, pattern, plasma…}` — colour and shading only. In its place, the rasteriser applies a **single hardcoded constant**:

> `isEdgeCell = 1; if (!entry.edgeStripes) z += 0.45;`  — `public/index.html:1433`

Every edge cell gets the same +0.45 lift, regardless of which material it names; stripes-layout cells get nothing at all (the comment there explains why: lifting *every* cell of a layer would only shift it bodily against its neighbours, which is true — but it is an argument for making the lift per-material, not for making it constant). **So the strip in 3D Shaper drives colour only. It is a flat, uniformly-proud ring.** The owner's "the pixels act as a divider and can be used to mix fills and extrusion" describes a capability that exists in the reference app and does *not* exist in 3D Shaper.

That is, I think, exactly why he remembers this as the reference app's feature and wants it looked into: the thing he liked was lost in the port, quietly, as a one-line simplification.

### 1.5 My assessment: what is it, and where does it file?

**It is not a border generator.** Nothing about it is anchored to the boundary except one scalar (`edge.depth` / `edgeCoverage`) that happens to default low. Set that scalar to 1 and there is no border left — the shape is entirely pattern. Set the layout to `stripes` and the boundary is not consulted at all.

**It is not a dithering or quantisation stage either.** It does not approximate a continuous value with a pattern of discrete ones. The strip is authored by hand, is spatially organised (marching around a perimeter or across an axis), and is indexed by a smooth geometric parameter. Nothing is being quantised.

**It is a FILL — specifically, an indexed palette fill driven by a shape parameterisation, where the palette entry carries geometry as well as colour.** That is the honest classification, and it decomposes into three independent pieces that the redesign should file separately, because each is reusable on its own:

1. **A parameterisation stage: shape → per-pixel scalar `u ∈ [0,1]`.** Currently there are two implementations — perimeter arc-length (`edgeRingParam`, `:1263`) and axis projection (`edgeStripeParam`, `:1277`). Both are shape-agnostic; both consume only the SDF field and the shape's extent. A third obvious member of the same family is *depth-into-the-shape*, which the code already computes (`inside`, `:1425`) but never uses as a pattern parameter — that would give concentric rings for free, which today are impossible.
2. **A masking stage: which pixels participate.** Currently `edge.depth` (a radial band from the outline inward) plus `edge.reach`/`edge.position` (an arc of the perimeter). This is the only part with any "border" character, and it is one small optional gate — not the identity of the feature.
3. **A palette-lookup stage: `u` → an index into an authored strip → a material.** With `repeat`/`stretch`/`mirror` and a repeat count as the mapping policy.

And then the coupling that makes it powerful: **the material a strip entry names must be allowed to carry a height offset, not only a colour.** Without that, you have a nice ring-decoration feature. With it, you have the thing the owner is describing — one authored 1-D strip that simultaneously paints and sculpts, and whose sculpting participates in the depth test against other layers.

**Concrete recommendation for the redesign.** File it under FILL, as a "strip fill" / "indexed palette fill" that takes a pluggable parameterisation. Do *not* file it under BORDER — doing so will make `edge.depth = 1` look like an odd degenerate case instead of the headline capability. Do *not* bury the per-material height as an implementation detail of the fill; make "materials have depth" a first-class property of the material model, because the moment it exists, the strip fill, the plain face fill and any future fill all gain relief at once. In Pyre terms this lands in the shape/fill/border split as: **fill gains a per-pixel parameterisation input and a palette-indexed variant, and the fill's output gains a height channel alongside its colour channel.** That last clause is the one that has to be designed in early — see §2, because it is the same buffer.

---

## 2. Rotating extruded shapes in 3D, and translating in Z

### 2.1 What the extrusion actually is today

**It is a 2.5-D height field: one single-valued height per screen pixel, shaded with a fake-3D lighting model from a fixed, non-configurable top-down orthographic view. There is no mesh, no voxel grid, no 3-D SDF, no camera, no projection matrix, and no per-vertex geometry anywhere in the program.** All of the following is verified:

- **The shape is a 2-D SDF only.** `evalShape(compiled, x, y)` takes exactly two coordinates (`public/index.html:1113`). Every primitive is a 2-D analytic SDF (`primitiveSdf`, `:1030-1042`). Composition (`add`/`subtract`/`softAdd`/`subtractSoft`) is 2-D field algebra (`:1113-1140`).
- **Transforms are 2-D affine, full stop.** `TRANSFORM_TYPES = ("translate", "rotate", "scale", "skew", "origin")` (`project_document.py:27`). `nodeMatrix` builds a canvas-order `[a,b,c,d,e,f]` 3×2 matrix (`public/index.html:1016-1028`). `rotate` has a single parameter, `degrees` — it is rotation in the image plane. There is no rotateX/rotateY anywhere in the file.
- **Height is a *function of the 2-D field*, evaluated per pixel.** `z = base + extrusionHeight(type, params, body, inside, nx, ny) * bevelFactor(bevel.type, bevel.params, inside)` (`:1424`), where `inside` is normalised depth into the 2-D silhouette and `nx, ny` are normalised local coordinates. The six extrusion techniques (`flat`, `linear`, `stepped`, `dome`, `round`, `taper`, `pyramid`, `:1144-1157`) and five bevels (`:1159-1173`) are all closed-form functions of `inside`. Nothing is ever tessellated.
- **The composite is one Z-buffer, plus one "below" slot.** `heights[key]`, `cellMaterial[key]`, `cellLayer[key]`, and a second-deepest `belowHeights`/`belowMaterial` used for the fusion band and the 2-layer transparency approximation (`:1436-1449`). One winner per pixel.
- **The "3-D look" is entirely a shading trick over that height map.** The normal is a finite difference of neighbouring heights with a constant-ish Z component: `normalX = (h[x−1] − h[x+1]) * 0.65`, `normalY = (h[y−1] − h[y+1]) * 0.65`, `normalZ = 1.4 + reflection * 1.5` (`public/index.html:1491-1493`). Shadows are a march across the height map toward the light (`shadowAt`, `:1468+`). Glow is a 2-D max-blend disc (`:1452-1466`).

The reference app is the same architecture, smaller (`raster`, `:50`; `lightShade`, `:35`; `shadowAt`, `:48`).

The consequence to state plainly: **the height map only exists in screen space.** Every downstream stage indexes it by `(gx, gy)` — the neighbour lookups for the normal, the shadow march, the fusion smooth-max, the below-slot, the edge band, the coverage bitmaps for occlusion-aware outlines. Nothing anywhere holds a point in 3-D space.

### 2.2 Z translation

**It does not exist today, and the field that looks like it isn't it.** The layer has a slider labelled "Depth" (`:3805`), stored as `layer.depth`, clamped `≥ 0` (`:2652`), tooltip *"How far this layer extrudes from its face, in canvas units"*. At compile it becomes `body: Math.max(0, layer.depth) / cellSize` (`:1362`) — the **thickness** argument to `extrusionHeight`. It is the height of the solid, not its position.

The layer's actual Z position is one expression on the same line: **`base: order * 0.75`** (`:1362`). It is derived from the layer's index in the list. So today the *only* way to move a layer in Z is to reorder the layer list, in fixed 0.75-unit steps.

**Adding real Z translation is cheap — genuinely a handful of lines.** `base` becomes `order * 0.75 + layer.zOffset` (or a pure `layer.zOffset` if you prefer explicit control, with the order term as a tie-break). Everything downstream already handles arbitrary heights: the Z-test, the fusion smooth-max, the below-slot, the normals, the shadow march. Nothing needs a new buffer, because **the height map already *is* the depth buffer.**

Two caveats, both minor and both worth flagging in the design:
- It interacts with fusion. `FUSION_CELLS` decides when two layers smooth-max together instead of hard z-testing (`:1441-1447`). Sliding a layer in Z will make it drift in and out of the fusion band, which is arguably the correct and desirable behaviour, but it needs to be a stated behaviour rather than a surprise.
- Layer *order* currently carries two meanings at once (paint order and Z position). Once Z is explicit they separate, and the tie-break rule has to be written down. `PRODUCT_CONTRACT.md:50` already asserts a documented resolve order with stable-ID tie-breaks, so there is a place to put it.

**Verdict on Z: add it. It is small, it is low-risk, and it is a real capability gap the owner has correctly identified.**

### 2.3 Arbitrary 3-D rotation

**Not possible in this representation — not "hard", not "expensive", but structurally excluded.**

The reason is one sentence: a height field stores exactly one Z per screen pixel, and rotating a solid about a horizontal axis makes it multivalued in screen space. Tilt an extruded disc 45° about X and a single screen column now covers the top face *and* the front wall *and* possibly the underside. There is nowhere to put the second and third values. This is not a shading limitation you can patch — the data structure has one slot.

Everything downstream compounds it. Even if you could store the folds:
- **The normal is a screen-space finite difference of neighbouring heights** (`:1491-1492`). At a fold it differences two surfaces that are not adjacent in 3-D, producing a garbage normal along every silhouette of the tilted body.
- **The shadow march walks the height map in screen space** (`shadowAt`, `:1468+`). With a tilted body the march is no longer along a light ray in world space.
- **The edge band test is `-distance <= edgeBand` in the layer's local 2-D space** (`:1432`). Under a tilt, "how far in from the outline" in 2-D is no longer "how far in from the silhouette" on screen; the pattern ring would slide off the rim it is supposed to trace.
- **Fusion, the below-slot and the occlusion-coverage bitmaps** (`:1436-1449`, `:1403-1411`) are all per-screen-pixel and all assume one surface per layer per pixel.

**What it would actually take.** Two honest routes, and both are renderer replacements, not features:

- **Route A — raymarch a real 3-D implicit.** This is the elegant one and it is genuinely available, because the current model *already implies* a 3-D solid: `F(x,y,z) = max( sdf2D(x,y), z − heightProfile(x,y) )` is a well-defined extruded implicit surface, and everything Shaper authors (8 primitives, four fusion modes, corner rounding, pulge, skew, 6 extrusion profiles, 5 bevels) survives untouched into it — you keep the *entire* authoring model and the *entire* shape engine. You then march a ray per pixel from an arbitrary camera. Rotation about any axis becomes a matrix on the ray, which is free. Normals become the analytic gradient of `F`, which is *better* than the current finite difference. Shadows become a second march, which is more correct than the current one. Z translation becomes trivially part of the transform. The cost is real: you rewrite `renderModelGrid` (214 lines, `:1371-1583`), you pay a per-pixel march instead of a per-pixel evaluation, and the fusion/below-slot/occlusion-outline machinery all has to be re-expressed against the 3-D field. But the shape engine — the 98-line piece `J-shaper-primitives.md:226` measured, which is the piece Pyre actually wants — is **completely unaffected**. It is 2-D SDF maths and it stays 2-D SDF maths.
- **Route B — voxelise or tessellate and rasterise conventionally.** Build actual geometry from the height field and draw it through a normal pipeline. This throws away the analytic cleanliness (the pixel-perfect silhouette, the exact bevel curves, the sub-cell fusion) and gains nothing Route A doesn't, at pixel-art resolutions. I do not recommend it.

### 2.4 Verdict: before or after the rebuild?

**Z translation: after is fine, but there is no reason to wait.** It is `base + zOffset` and a slider. It cannot corner you.

**3-D rotation: this must be decided BEFORE, and it is the single highest-stakes decision in the whole absorption.** Not because rotation is expensive to build, but because the decision determines what the buffers *are*, and every other feature is written against the buffers.

What specifically becomes expensive to retrofit if you build the height-field version first and add rotation later:

1. **Every consumer of the height map has to be rewritten, not extended.** Normals, shadows, fusion, the below-slot, the occlusion-coverage bitmaps, the glow spill, and the edge-band gate are seven independent pieces of logic that all index by `(screenX, screenY)`. Under an arbitrary camera every one of them needs a different formulation. In 3D Shaper these total roughly 200 lines and they are the *good* 200 lines — the fusion smooth-max, the occlusion-aware outline and the arc-length ring each carry hard-won correctness (see the code comments at `:1403-1411` and `:1173-1187`, which document real bugs they fix). Rewriting them is exactly the kind of work that loses those fixes.
2. **The authored data starts meaning the wrong thing.** `edge.depth` ("how far in from the silhouette") and `bevel.amount` (an `inside` band) are defined in 2-D local space. Under a tilt they need to be defined in surface space instead, or every authored asset shifts. If assets exist by then, that is a migration.
3. **Per-material height (§1.3) is the same buffer.** If you decide materials carry depth *and* you later decide to raymarch, the material's height contribution has to move from "an addend on the height map" to "a displacement on the implicit surface". That is a different thing, and it is the piece that makes the pixel-border feature work. Deciding both at once is much cheaper than deciding them six months apart.

**My recommendation.** Design the rebuild so the shape stage produces a **3-D implicit field** (`sdf2D` + `heightProfile` + a transform, which is *literally what it already has*, just not named that way) rather than a screen-space height map, and let the current fixed top-down orthographic view be the *first camera* rather than the *only* possibility. That costs almost nothing now — it is a naming and layering decision, not extra code — and it means adding a tilt later is "add a camera matrix and swap the resolve stage", not "replace the renderer". Concretely: keep the height-map resolve as the shipping path for v1, but **do not let any stage other than the resolve stage assume screen-space adjacency.** The moment the fusion or the edge band or the material system reaches into `heights[gy*gridW+gx]` for a neighbour, rotation has been foreclosed.

If that discipline is judged too expensive, then the honest position is: **say out loud that 3-D rotation is out of scope permanently**, and don't leave it as an open "we'll add it later", because at that point it is not a later.

---

## 3. Arcs on every primitive

### 3.1 What the primitives actually are (verifying `J-shaper-primitives.md`)

**Confirmed.** The entire primitive registry is `primitiveSdf` at `public/index.html:1030-1042` — **11 lines of scalar maths, one line per shape**, each returning a canvas-unit signed distance, negative inside. Above it, `compileBasicShape` (`:1047-1053`) and `evalBasicShape` (`:1054-1059`) add skew, corner rounding and the three-band pulge warp in another ~12 lines. Composition (`compileShape` + `evalShape`, `:1073-1140`) adds the four fusion modes. `J-shaper-primitives.md:226`'s "the silhouette engine is 98 lines" is a fair measure of that whole block and I confirm it as the right order of magnitude.

The file's own comment states the extension contract explicitly: *"Primitive registry: one analytic signed distance function per shapeType… Adding a primitive is one more case here plus one more entry in SHAPE_TYPES"* (`:1029-1030`, with `SHAPE_TYPES` at `project_document.py:16`).

The eight, as they actually read:

| Primitive | The maths (`:1032-1041`) | Radial? |
| --- | --- | --- |
| `ellipse` | `(hypot(x/rx, y/ry) − 1) · unit` | yes, purely |
| `diamond` | `(|x|/rx + |y|/ry − 1) · unit` | yes (L1 norm) |
| `hexagon` | `max(b−1, a+b·0.55−1) · unit`, `a=|x|/rx, b=|y|/ry` | yes-ish (a max of half-plane tests) |
| `octagon` | `max(max(a,b)−1, (a+b)/1.42−1) · unit` | yes-ish, same shape of expression |
| `star` | `(hypot(x/rx,y/ry) − (0.42 + 0.58·lobe^0.8)) · unit`, `lobe = 0.5+0.5·cos(5·atan2(y/ry,x/rx) − π/2)` | **explicitly polar** |
| `rect` (default branch) | rounded-box: `hypot(max(qx,0),max(qy,0)) + min(max(qx,qy),0) − r` | no |
| `capsule` | same rounded-box form with `r = min(rx,ry)` | no |
| `triangle` | `max(|x| − rx·down, −(y+ry), y−ry)`, `down = clamp01((y+ry)/2ry)` | no |

### 3.2 "Arc at 1 = all 360°" — the angular sweep

**This is natural on every primitive without touching any primitive's formula, because it is not a change to the primitive at all — it is an intersection with a pie wedge.**

In plain language: you already have, per pixel, a signed distance saying "am I inside the shape, and by how far". A wedge — the region between two rays from the centre — has its own signed distance, computable from the pixel's angle in three or four lines. Intersecting two SDF regions is a `max`. So "arc" is: *compute the wedge field, take the max against the primitive's field*. One helper, applied once in `evalBasicShape`, and **all eight primitives gain it simultaneously**, plus any future primitive, for free.

Two details that matter:

- **Which angle?** Shaper already computes two candidates, both for the edge ring. The cheap one is the ellipse-normalised polar angle `atan2(y/ry, x/rx)` (`edgeRingParam`, `:1268` fallback branch; also exactly what `star` already uses at `:1039`). The good one is the **perimeter arc-length fraction** built by `edgeArcTableFor` (`:1244`) and looked up by `edgeArcFraction` (`:1251`), which traces the boundary radially and integrates chord lengths so that equal steps cover equal *border length* rather than equal *angle*. For a shape parameter I recommend the **normalised polar angle**, not the arc-length table — because "an arc of a disc" means an angular wedge (a pie slice), and because the arc-length trace has documented failure modes (it returns `null` and falls back whenever a ray leaves the silhouette more than once, `:1173-1187`), which is fine for a decoration but not acceptable for the shape's own definition. Keep the arc-length table where it is, for the pattern.
- **Sharp or soft ends?** A hard `max` gives radial cut faces, which is right for a pie slice. Keep the wedge's own SDF proper (distance to the two half-planes, plus the outside-the-wedge case) rather than a binary in/out test, so the cut edges bevel and antialias like every other edge. This is the difference between an arc that looks authored and one that looks clipped — worth the extra two lines.

**At sweep = 1, the wedge covers 360° and the max is a no-op**, so the primitive is byte-identical to today. That is the right default and it means the parameter is free to add to every existing asset.

**Owner's phrasing check:** the owner said *"At 1 then all 360 degrees of it is used"*, which is exactly this. Note that Shaper already ships this exact concept — `edge.reach` (0–1, *"Fraction of the perimeter covered by edge cells"*) plus `edge.position` (where the covered arc starts), `:1264-1273`, UI at `:3823`. **The machinery exists; it is just wired to the pattern instead of to the shape.** A sweep parameter on the primitive needs the same pair — a sweep *amount* and a sweep *start angle* — and the redesign should name them consistently with the edge ring's pair so the two read as the same idea in two places.

### 3.3 "Remove from the middle outwards" — the hole

Also natural, also primitive-agnostic, also not a change to any primitive's formula. There are **two genuinely different definitions**, they look different, and the redesign should pick deliberately rather than discover the difference later:

- **(a) Uniform-thickness shell (the SDF "onion" operator): `|d| − thickness`.** This carves a band of *constant width* that follows the outline exactly. On a rect you get a rectangular picture-frame with even-width sides. On a star you get a star-shaped outline of even stroke weight. It is one line, works on literally any field including composed shapes, and it is the definition that generalises cleanly to the non-radial primitives. It is what a *stroke* is.
- **(b) Subtract a scaled-down copy: `max(d, −sdf(x/s, y/s))` for some inner scale `s`.** This is what "inner radius" means on a disc, and it is what Pyre's Ring already does — `ringInner`, 0.1–0.92, *"inner radius as a fraction of the outer radius"* (`Pyre.cs:392`, doc at `:54-56`). It gives a band whose width *varies* with how far the outline is from the centre — on a rect the long sides come out thinner than the short ones. On a disc (a) and (b) are identical; everywhere else they diverge visibly.

**My recommendation: implement (a), the shell, as the general operator, and name it in radius-fraction terms so it reads as "inner radius" on the shapes where users expect that.** Reasons: it is a one-liner that costs nothing per primitive; it composes (you can shell a fused complex shape, which (b) cannot do meaningfully because a composed shape has no single "scale about the centre"); it degrades gracefully on non-radial primitives; and it is the same operator that would let a *border* be expressed as geometry rather than as a post-pass — which is a bonus worth having given §1's finding that Shaper's border and fill machinery are already entangled. If a true concentric-scale variant is wanted later for discs specifically, it can be a second mode.

⚠️ **One thing to watch:** the shell operator changes what `inside` means. Every downstream consumer — the extrusion profiles, the bevel band, the edge-band gate — is a function of *depth into the shape*. After shelling, the deepest point of a thin ring is `thickness/2` from a surface, not `radius` from a surface, so a "dome" extrusion on a shelled disc domes across the ring's cross-section rather than across the disc. **That is almost certainly the desired behaviour** (a shelled dome should look like a torus, and it will) — but it is behaviour that falls out of the maths rather than behaviour anyone chose, so it should be checked by eye, not assumed. It is also a good argument for making the shell part of the *shape* stage rather than a fill/border trick: put it in the shape and everything downstream is automatically consistent.

### 3.4 Non-radial primitives — what "arc" and "remove from the middle" even mean

This is the interesting case the brief flags, and I think it is less awkward than it sounds — because **neither operator asks the primitive anything**. Both act on the field the primitive returns. So the question is not "can a rect support this" (it can, mechanically) but "does the result mean anything to a user". Taking them one at a time:

**Rectangle.** Wedge-intersect a rect and you get a triangular or trapezoidal offcut — a rect with a corner sliced off along two rays from the centre. At sweep 0.5 you get exactly half the rect, split along a diameter whose direction the start-angle controls; at 0.25, a quarter. **This is genuinely useful and immediately legible** — it is how you make a wedge, a ramp, a corner gusset, a half-panel. I'd argue it's *more* useful on a rect than on a disc, because the disc case is just a pie slice and a rect case gives you shapes you'd otherwise need a subtract for. Shell a rect and you get a picture frame. Both meanings are obvious.

**Regular polygon / N-gon.** Identical story to the rect and better behaved, since the polygon is already radially organised about its centre. Wedge = a slice of the polygon; shell = a polygonal outline of even stroke. Both read exactly as a user would guess.

**Line / streak / capsule — the genuinely awkward one.** A capsule (and Pyre's `Streak`, `Pyre.cs:113`) is a shape whose interesting parameter is *along its length*, and whose centre is a point on that length, not a hub the outline goes around. A polar wedge about its midpoint cuts it into a bowtie, which is meaningless. **My recommendation: for length-dominant primitives, do not silently reinterpret — redefine the parameter's axis and say so.** "Sweep" on a line should mean *what fraction of the line's length is drawn*, and "start" should mean *from where along the length* — i.e. the parameter runs along the shape's dominant axis rather than around its centre. That is the same two-parameter interface (amount + position), the same UI, and the same mental model ("how much of it is created, and from where"), just measured along the natural coordinate of that shape instead of angularly. The shell operator, by contrast, needs no special-casing at all on a capsule — `|d| − t` gives a stadium-shaped outline, which is exactly right.

There is precedent for this being acceptable: Shaper's own edge ring already switches its parameter axis between angular (`edgeRingParam`) and linear (`edgeStripeParam`) depending on layout, presenting both through the same strip and the same repeat modes (`:1263-1305`). Users evidently cope. The rule I'd write down is: **every primitive declares which coordinate its sweep runs along — angular about the centre for hub-shaped primitives (disc, polygon, star, rect, diamond, hexagon, octagon, triangle), linear along the dominant axis for length-shaped ones (capsule, line, streak). The parameter pair and the UI are identical either way.** Do not try to find one definition that covers both; you'll get a bowtie.

### 3.5 Cost

Two shared helpers (a wedge SDF and a shell operator), one call site each in `evalBasicShape`, one axis-declaration per primitive, and two new parameters (plus one for the shell) in the shape schema. **No primitive's formula changes.** At sweep = 1 and shell = 0 every existing shape is bit-identical, so it is additive with no migration. This is small work with a very high capability-per-line ratio, and I'd class it as the cheapest of the owner's four items by a wide margin.

---

## 4. A proper polygon engine and a star engine

### 4.1 The primitive set today

Eight, listed in §3.1: `rect`, `ellipse`, `diamond`, `triangle`, `hexagon`, `octagon`, `capsule`, `star` (`project_document.py:16`; SDFs `public/index.html:1032-1041`). Every one carries `width`, `height`, `skew` (±89°), `cornerRounding` (0–1, though only the `rect`/default branch reads it) and a three-band `pulge` pinch/bulge warp (`:229` in the schema; `compileBasicShape`/`evalBasicShape`, `:1047-1059`).

### 4.2 A general N-gon

**Fits the same scalar formulation exactly.** A regular N-gon's SDF is one of the standard closed forms — fold the point's angle into a single sector (a modulo on the polar angle), then take the distance to that sector's single edge. It is one `case` in `primitiveSdf`, comparable in length to the existing `capsule` line.

What it needs beyond that:

- **`sides`** (Pyre uses 3–12, `Pyre.cs:503`). Straightforward.
- **Rotation.** Two options, and only one is right. The layer transform already has a `rotate` (`nodeMatrix`, `:1018`), so a *layer-level* rotation is free. But Pyre's polygon does something more specific that should be preserved: *"An even side count is rotated half a sector so it rests on a flat edge (a square sits flat, not a diamond); an odd count points a vertex up"* (`Pyre.cs:498-500`). That is a per-primitive phase convention, not a user rotation, and it should be baked into the SDF so an N-gon looks right at rotation 0 whatever N is. A separate user-facing phase dial is optional on top.
- **Corner rounding.** Nearly free and worth having, because it is *already in the schema* (`cornerRounding`, `:229`) and currently read by only one branch. A polygon SDF rounds the same way a rounded box does — shrink the shape by `r` and subtract `r` from the distance. One extra term.

**What it replaces.** `hexagon` and `octagon` today are `max(b−1, a+b·0.55−1)` and `max(max(a,b)−1, (a+b)/1.42−1)` — axis-aligned approximations whose constants `0.55` and `1.42` are visual fits, **not regular polygons** (confirming `J-shaper-primitives.md:36`). A real N-gon supersedes both. ⚠️ Keep the old two around, or map them onto the N-gon with a compatibility phase/aspect, because existing Shaper assets reference `hexagon`/`octagon` by name and will change shape slightly if silently redirected. `triangle` is a *different* thing (an isoceles triangle stretched to `rx`/`ry`, not a regular one, `:1035`) and should stay.

**Machinery needed: essentially none beyond the case itself.** One integer parameter, one optional rounding term, one entry in `SHAPE_TYPES`, one UI row. It slots into the existing `compileBasicShape` → `evalBasicShape` path with no structural change, and it inherits skew, pulge, sweep and shell automatically.

### 4.3 A star engine

**Also fits — but the existing one has to be *replaced*, not extended, because it is a different kind of object.**

Shaper's star is `lobe = 0.5 + 0.5·cos(5·angle − π/2)`, radius `0.42 + 0.58·lobe^0.8` (`:1039`). Read that carefully: it is a **smooth cosine lobe**, a flower, not a star polygon with straight edges. It is hardcoded to **5 points**, with a hardcoded inner ratio and a hardcoded lobe exponent. **It has no parameters whatsoever** — confirming `J-shaper-primitives.md:13, 35`. It is not a star engine; it is one specific decorative blob.

Pyre's star is the real thing and is the spec to port (`Pyre.cs:485-492`, renderer `PyreRenderer.DrawStarBody`):

| Pyre parameter | Range | Meaning |
| --- | --- | --- |
| `starArms` | 2–20 | point count |
| `starLength` | 0–1, animatable per particle | arm reach; inner radius = `R·(1 − starLength)` |
| `starBaseWidth` | 0.1–1, animatable | valley angular position as a fraction of the half-sector — smaller gives thinner arm bases and wider valleys |
| `starSkew` | −60…+60°, animatable | valley swirl — rotates the valleys, pinwheel-twisting the arms; clamped so valleys never cross tips |

That maps to the owner's "points, inner/outer radius, twist" one-for-one: `starArms` = points, `starLength` = inner/outer ratio, `starSkew` = twist. `starBaseWidth` is a fourth dial Pyre has and the owner didn't ask for, but it is what separates a spiky star from a chunky one and it should come along.

**As a scalar SDF:** a star polygon is *star-shaped about its centre*, so — exactly like the N-gon — the point's angle folds into one sector and you take the distance to the one tip→valley edge spanning it. `Pyre.cs:485-488` describes precisely this approach for its own renderer, so the two implementations agree on the method. Adding the skew is a phase offset inside the fold; adding `starBaseWidth` moves the valley's angular position within the half-sector. **Same one-case-in-`primitiveSdf` shape as the N-gon**, a few lines longer because of the two extra terms.

**Machinery needed:** one integer + three floats in the schema, one `SHAPE_TYPES` entry, one SDF case, four UI rows. ⚠️ **Two migration notes.** (i) The clamp Pyre documents — *"clamped so valleys never cross tips"* (`Pyre.cs:492`) — is load-bearing; without it the skew inverts the shape at the extremes. Port the clamp, not just the parameter. (ii) Shaper's existing `star` should be kept under its current name (or given a compatibility mapping) rather than redefined, since a straight-edged 5-point star is visibly *not* the cosine flower currently on screen.

### 4.4 Combined assessment

Both fit the existing scalar formulation with no structural change, and both are named in the code's own extension contract at `public/index.html:1029-1030`. `J-shaper-primitives.md:13` estimated "~4 new lines each"; having read `primitiveSdf` I'd call that right for the N-gon and mildly optimistic for the star (the four-parameter version with the tip/valley clamp is more like 8–12), but the order of magnitude holds and both are unambiguously small.

**The thing worth saying to the owner is that the design work here has already been done — in Pyre.** Pyre's star and polygon are strictly better specified than Shaper's, with animatable dials and documented edge cases. The correct framing for the rebuild is not "add a polygon engine and a star engine to Shaper" but **"Shaper's primitive registry is the right *mechanism*, and Pyre's star/polygon are the right *parameters*; the absorption should take the mechanism from one and the specification from the other."** Doing it the other way round — taking Shaper's primitives as shipped — is the regression `J-shaper-primitives.md:13, 199` already warned about, and I confirm that warning independently.

There is also a nice interaction with §3 worth noting: once sweep and shell exist as general operators, a parameterised star and a parameterised N-gon multiply with them. A shelled N-gon is a polygonal frame; a swept N-gon is a fan; a shelled swept star is a spiky arc. Four small parameters produce a combinatorially large shape vocabulary, which is the strongest argument for doing §3 and §4 in the same pass rather than separately.

---

## 5. Verification index

Every load-bearing claim above, with where to check it.

**Reference app** — `D:\CODEZ\AgentHQ\3D Shaper\.agenthq\attachments\T-0030\20260829T063646Z_retro_scifi_flyer_lab_draft6-1.html` (also byte-identical at `..\T-0018\20260827T151208Z_…` and `..\T-0027\20260828T193521Z_…`; md5 `301abaa3e5f293f58afcdbd99070fee0`):
- `:7` — material controls; **Protrusion slider, min −8 max 28**
- `:10` — the UI note describing edge vs stripe mode
- `:13` — `makeLayer` defaults incl. `edgeCoverage`, `patternLayout`, `stripeAngle`, `mode`, `seq` (18 entries)
- `:14` — `state.mats`, each `{color, surface, pro, round, shine, glowPower, glowRadius}`
- `:22-23` — `showPixelPopover` / `bindLongPress`: long-press to pick, tap to paint
- `:26-27` — layer UI: Pattern/Layout/Edge cover/Stripe angle rows, and the strip rendered as `.pixel` buttons
- `:30` — `sdf`, 8 primitives, mixed normalised/pixel units (the quirk in §1.2)
- `:31` — `patternMat`, incl. the `2.35` fractional repeat constant
- `:32` — `stripeMat`
- `:33` — `layerHeight`, 5 profiles
- `:35` / `:48` — `lightShade` / `shadowAt`
- **`:50` — `raster`: the whole pixel-border function. The load-bearing line is the `z` expression, which adds `state.mats[mi].pro` at full weight for patterned cells and `×0.25` for plain fill, then z-tests into the shared height map.**
- `:52` — normals as finite differences of the height map

**3D Shaper** — `D:\CODEZ\AgentHQ\3D Shaper\public\index.html`:
- `:1010-1028` — 2-D affine matrices; `nodeMatrix` (translate/rotate/scale/skew/origin only)
- **`:1029-1042` — `primitiveSdf`, the entire 11-line primitive registry, with the "adding a primitive is one more case here" comment**
- `:1047-1059` — `compileBasicShape` / `evalBasicShape` (skew, pulge, rounding)
- `:1073-1140` — `compileShape` / `evalShape` (the 4 fusion modes)
- `:1144-1157` — `extrusionHeight`, 6 techniques, all functions of `inside`/`nx`/`ny`
- `:1159-1173` — `bevelFactor`, 5 techniques
- `:1188-1253` — the arc-length ring machinery (`edgeArcTableFor` `:1244`, `edgeArcFraction` `:1251`)
- **`:1255` — the comment stating the port: "matching the reference prototype's patternMat/stripeMat/raster split"**
- `:1263-1305` — `edgeRingParam` / `edgeStripeParam` / `edgeSequenceIndex` / `edgeRingIndex`
- **`:1362` — `body: max(0, layer.depth)/cellSize, base: order*0.75` — the proof that "Depth" is thickness and Z is layer order**
- `:1371` — `renderModelGrid`, the 214-line renderer
- `:1424-1425` — the `z` expression and `inside = clamp01(-distance/span)`
- **`:1433` — `isEdgeCell=1; if (!entry.edgeStripes) z+=0.45;` — the hardcoded constant that replaced per-material protrusion**
- `:1436-1449` — z-test, below-slot, fusion smooth-max
- `:1487-1500` — normals from neighbouring heights; lighting
- `:1595-1596` — animatable bounds: `edge.resolution [2,128]`, `edge.repeats [1,16]`, `edge.orientation [0,360]`, `edge.reach [0,1]`, `edge.depth [0,1]`, `edge.position [0,1]`, `edge.stripeAngle [0,360]`
- `:2428` / `:2522` — layer and edge schema defaults
- `:2652` — `layer.depth` clamped `≥0`; the whole layer-field write path
- `:3805-3824` — the layer UI rows and their tooltips, incl. *"1 covers the whole face"*

**3D Shaper** — `project_document.py`: `:16` `SHAPE_TYPES` (8); `:27` `TRANSFORM_TYPES` (5, all 2-D); `:229` the shape schema.

**Pyre** — `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Runtime\Pyre\Pyre.cs`: `:54-56` and `:388-392` Ring + `ringInner` 0.1–0.92; `:113` the `ShapeForm` enum; `:428-441` Crescent; `:485-492` Star (4 dials, animatable); `:496-503` Polygon (3–12 sides, even/odd phase convention); `:505` the flat-2D border list.

**Not verified / out of scope.** I did not run 3D Shaper or the reference app — every behavioural claim above is read from source, not observed on screen. In particular the *visual* claims in §1.3 (that the strip reads as carved relief, that a negative protrusion reads as a seam) are inferred from the maths and from the presence of the normal/shadow passes; they are confident but they are inferences. If anything in §1 is going to drive a design decision, opening the reference app and dragging Protrusion on one material while a patterned layer is on screen would settle it in about thirty seconds, and I'd recommend that as the single highest-value manual check arising from this report.
