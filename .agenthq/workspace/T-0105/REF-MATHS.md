# T-0105 — Reference maths extraction for the Laubrary "Shaper" rebuild

Every formula below is transcribed literally from the source, with a `file:line` citation. Where I paraphrase, I say so. Two sources: the reference web app **3D Shaper** (Part 1) and **Pyre** in this repo (Part 2). Part 3 lists the traps an implementer will otherwise walk into.

Sources read in full for the relevant regions:
- `D:\CODEZ\AgentHQ\3D Shaper\public\index.html` (4102 lines — the entire renderer is inline in this one file)
- `D:\CODEZ\AgentHQ\3D Shaper\project_document.py` (the server-side schema: defaults, ranges, normalisation)
- `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Runtime\Pyre\Pyre.cs`, `...\Runtime\Pyre\PyreRenderer.cs`, `...\Editor\Pyre\PyreWindow.cs`, `...\Zui\Scripts\Runtime\ZUIValue.cs`

---

## PART 1 — 3D Shaper

### 1.1 The primitive registry (a)

There is **exactly one** primitive registry, `primitiveSdf(type, x, y, rx, ry, rounding)` at `public/index.html:1031-1043`. The type list is `SHAPE_TYPES` at `public/index.html:981` and is mirrored verbatim server-side at `project_document.py:16`:

```js
const SHAPE_TYPES = ['rect','ellipse','diamond','triangle','hexagon','octagon','capsule','star'];
```
(`public/index.html:981`)

```py
SHAPE_TYPES = ("rect", "ellipse", "diamond", "triangle", "hexagon", "octagon", "capsule", "star")
```
(`project_document.py:16`)

The full function, verbatim (`public/index.html:1031-1043`):

```js
function primitiveSdf(type, x, y, rx, ry, rounding) {
  const unit=Math.min(rx,ry);
  if (type==='ellipse') return (Math.hypot(x/rx,y/ry)-1)*unit;
  if (type==='diamond') return (Math.abs(x)/rx+Math.abs(y)/ry-1)*unit;
  if (type==='triangle') { const down=clamp01((y+ry)/(2*ry)); return Math.max(Math.abs(x)-rx*down, -(y+ry), y-ry); }
  if (type==='hexagon') { const a=Math.abs(x)/rx, b=Math.abs(y)/ry; return Math.max(b-1, a+b*0.55-1)*unit; }
  if (type==='octagon') { const a=Math.abs(x)/rx, b=Math.abs(y)/ry; return Math.max(Math.max(a,b)-1,(a+b)/1.42-1)*unit; }
  if (type==='capsule') { const r=Math.min(rx,ry), qx=Math.abs(x)-(rx-r), qy=Math.abs(y)-(ry-r); return Math.hypot(Math.max(qx,0),Math.max(qy,0))+Math.min(Math.max(qx,qy),0)-r; }
  if (type==='star') { const angle=Math.atan2(y/ry,x/rx), lobe=0.5+0.5*Math.cos(5*angle-Math.PI/2); return (Math.hypot(x/rx,y/ry)-(0.42+0.58*Math.pow(lobe,0.8)))*unit; }
  const r=clamp01(rounding||0)*unit*0.95, qx=Math.abs(x)-(rx-r), qy=Math.abs(y)-(ry-r);
  return Math.hypot(Math.max(qx,0),Math.max(qy,0))+Math.min(Math.max(qx,qy),0)-r;
}
```

Per-primitive notes (all derived from the lines above):

| type | expression | metric? | notes |
|---|---|---|---|
| `rect` (the default / fallback branch, no `if`) | `hypot(max(qx,0),max(qy,0)) + min(max(qx,qy),0) - r` with `r = clamp01(rounding)*min(rx,ry)*0.95`, `qx=|x|-(rx-r)`, `qy=|y|-(ry-r)` | **yes** — a true Euclidean rounded-box SDF in canvas units | the ONLY primitive that reads `rounding`; the `0.95` cap means full rounding never quite reaches a stadium |
| `ellipse` | `(hypot(x/rx, y/ry) - 1) * min(rx,ry)` | no — normalised radial, scaled | exact only when `rx==ry` (then it is a true circle SDF) |
| `diamond` | `(|x|/rx + |y|/ry - 1) * min(rx,ry)` | no — normalised L1 | vertices at (±rx,0),(0,±ry) |
| `triangle` | `max(|x| - rx*down, -(y+ry), y-ry)` with `down = clamp01((y+ry)/(2*ry))` | no — **and it is the only primitive with no `*unit` at all**, so it is returned in raw canvas units while its siblings are scaled by `min(rx,ry)` | apex where `down==0`, i.e. `y == -ry` (the TOP in Shaper's y-down canvas); base half-width `rx` at `y == +ry` |
| `hexagon` | `max(b-1, a+b*0.55-1) * min(rx,ry)` with `a=|x|/rx`, `b=|y|/ry` | no | **hand-fitted, axis-aligned** — see 1.5 |
| `octagon` | `max(max(a,b)-1, (a+b)/1.42-1) * min(rx,ry)` | no | **hand-fitted constant 1.42 ≈ √2** — see 1.5 |
| `capsule` | identical rounded-box body to `rect` but with `r = min(rx,ry)` forced (ignores `rounding`) | **yes** — true Euclidean | i.e. a capsule is just "rect at maximum rounding, uncapped" |
| `star` | `(hypot(x/rx, y/ry) - (0.42 + 0.58*pow(lobe, 0.8))) * min(rx,ry)` with `angle = atan2(y/ry, x/rx)`, `lobe = 0.5 + 0.5*cos(5*angle - π/2)` | no | **fixed five-lobe cosine flower, zero parameters** — see 1.5 |

#### Parameters, defaults and ranges

There are only **six** authored scalar dials on a primitive (plus the type). Every one is normalised server-side at `project_document.py:229`:

```py
return {**passthrough, "id": asset_id(source.get("id")), "name": string(source.get("name"), "Shape"), "kind": kind,
        "shapeType": shape_type if shape_type in SHAPE_TYPES else "rect",
        "width": bounded_number(source.get("width"), 100, 1, 8192),
        "height": bounded_number(source.get("height"), 100, 1, 8192),
        "skew": bounded_number(source.get("skew"), 0, -89, 89),
        "cornerRounding": bounded_number(source.get("cornerRounding"), 0, 0, 1),
        "pulge": normalise_pulge(source), "components": components}
```
(`project_document.py:229`, `bounded_number(value, default, min, max)`)

| dial | default | range | reaches the maths as |
|---|---|---|---|
| `shapeType` | `"rect"` | the 8 names above | which `if` branch runs |
| `width` | 100 | 1 … 8192 | `rx = max(0.5, width/2)` — **a HALF-extent, not a radius** |
| `height` | 100 | 1 … 8192 | `ry = max(0.5, height/2)` |
| `skew` | 0 | −89 … +89 **degrees** | `skew = tan(clamp(deg,-89,89) * π/180)`, applied as a horizontal shear |
| `cornerRounding` | 0 | 0 … 1 | only `rect` reads it; `r = clamp01(rounding)*min(rx,ry)*0.95` |
| `pulge.top` / `.middle` / `.bottom` | 0 each | −1 … +1 each | negative = pinch, positive = bulge; a horizontal-extent warp, see below |

(Ranges independently confirmed in the client's mirror of `ANIMATABLE_PARAMETERS` at `public/index.html:1591-1592` and the canonical table at `project_document.py:51-64`.)

`compileBasicShape` (`public/index.html:1047-1053`) is the literal parameter→maths bridge:

```js
function compileBasicShape(shape) {
  const source=shape||{}, pulge=source.pulge||{};
  return { type: SHAPE_TYPES.includes(source.shapeType) ? source.shapeType : 'rect',
    rx: Math.max(0.5,(Number(source.width)||100)/2), ry: Math.max(0.5,(Number(source.height)||100)/2),
    skew: Math.tan(clampTo(Number(source.skew)||0,-89,89)*Math.PI/180), rounding: clamp01(Number(source.cornerRounding)||0),
    warpTop: Number(pulge.top)||0, warpMid: Number(pulge.middle)||0, warpBot: Number(pulge.bottom)||0 };
}
```

and `evalBasicShape` (`public/index.html:1054-1059`) applies skew + pulge and then calls the registry:

```js
function evalBasicShape(compiled, x, y) {
  const ly=y, lx=x-compiled.skew*y, t=clamp01(ly/compiled.ry*0.5+0.5);
  const wTop=Math.max(0,1-2*t), wMid=1-Math.abs(2*t-1), wBot=Math.max(0,2*t-1);
  const warp=Math.max(0.08, 1+(compiled.warpTop*wTop+compiled.warpMid*wMid+compiled.warpBot*wBot)*0.8);
  return primitiveSdf(compiled.type, lx/warp, ly, compiled.rx, compiled.ry, compiled.rounding)*Math.min(1,warp);
}
```

Read that carefully: the three pulge bands are **overlapping triangular weights** over `t = clamp01(y/ry*0.5+0.5)` (so `t=0` at `y=-ry`, `t=1` at `y=+ry`), the sum is scaled by `0.8`, floored at `0.08`, applied as a DIVISION of the local x, and the result is multiplied by `min(1, warp)` — a partial (one-sided) Lipschitz repair that only fires when the shape is pinched, never when it is bulged.

### 1.2 Sign convention and where the value becomes coverage (b)

**Negative is INSIDE.** Stated in the registry's own header comment (`public/index.html:1029-1030`): *"one analytic signed distance function per shapeType, all returning canvas-unit distances (negative inside)"*. Every consumer agrees: `evalShape(compiled,0,0)>0` is the "the origin is outside" test at `public/index.html:1217`, `1789`, `1813`.

**The value is a distance, not a coverage** — but only `rect` and `capsule` return a true Euclidean distance; the other six return a *shaped* value with the right sign and the right zero-set but a wrong (usually non-unit, sometimes anisotropic) gradient. See 1.6.

**There is NO anti-aliasing and NO smooth coverage function anywhere.** The single conversion site is the rasteriser's inner loop (`public/index.html:1417-1424`):

```js
const lx=matX(inverse,cx,cy), ly=matY(inverse,cx,cy);
const distance=evalShape(shape,lx,ly);
if (distance>0) continue;
if (coverBase>=0) coverage[coverBase+gy*gridW+gx]=1;
const inside=clamp01(-distance/span), nx=clampTo(lx/halfW,-1,1), ny=clampTo(ly/halfH,-1,1);
let z=base+extrusionHeight(extrusion.type,extrusion.params,body,inside,nx,ny)*bevelFactor(bevel.type,bevel.params,inside);
```

So:
- coverage is a **hard binary test** `distance > 0 → skip`, written into a `Uint8Array` as literal `1` (`public/index.html:1411` allocates `new Uint8Array(total*outlineOrders.length)`). This is deliberate: the grid is blitted at grid size and upscaled with smoothing off, which is what produces the pixel-art look (`public/index.html:975-977`).
- the *only* thing the magnitude of the distance is used for is `inside = clamp01(-distance / span)`, a normalised "how deep am I" ramp that feeds the **extrusion height** and the **bevel factor**, not alpha. `span = Math.max(1, Math.min(halfW, halfH))` (`public/index.html:1339`).

The two places smoothing does exist are both *after* the SDF:
- **Z-buffer fusion** between layers, `smoothMax`, with `FUSION_CELLS = 1.7` (`public/index.html:982`) and a band of `FUSION_CELLS*0.85` (`public/index.html:1441`, `1445`).
- **The soft-combine operators inside one shape**, `smoothMinShaped` — 1.3 below.

The two smoothing kernels, verbatim (`public/index.html:996-997` and `1006-1007`):

```js
function smoothMin(a, b, k) { if (k<=0) return Math.min(a,b); const h=clamp01(0.5+0.5*(b-a)/k); return b+(a-b)*h-k*h*(1-h); }
function smoothMax(a, b, k) { return -smoothMin(-a,-b,k); }
function blendExponent(sharpness) { return Math.pow(8, clamp01(sharpness===undefined||sharpness===null?0.5:Number(sharpness)||0)); }
function smoothMinShaped(a, b, k, n) { if (k<=0) return Math.min(a,b); const h=Math.max(k-Math.abs(a-b),0)/k; return Math.min(a,b)-Math.pow(h,n)*k/(2*n); }
```

Note `blendExponent` is `pow(8, sharpness)`, i.e. **n runs 1 … 8** as sharpness runs 0 … 1 (the in-code comment at `public/index.html:1003` says "n runs 1..8 across sharpness 0..1" — correct, though it phrases it as if linear).

### 1.3 How a shape node is evaluated: transform + combine (c)

#### The transform block

Canvas-order affine matrices `[a,b,c,d,e,f]` meaning `x' = a*x + c*y + e`, `y' = b*x + d*y + f` (`public/index.html:1010-1012`). The renderer builds the FORWARD transform per node, inverts it, and **inverse-transforms every sample point** — it never transforms the shape (`public/index.html:1011-1012`).

Five transform kinds, fixed set: `TRANSFORM_TYPES = ("translate", "rotate", "scale", "skew", "origin")` (`project_document.py:27`).

`nodeMatrix`, verbatim (`public/index.html:1017-1029`):

```js
function nodeMatrix(node, width, height) {
  const tx=transformParam(node,'translate','x')||0, ty=transformParam(node,'translate','y')||0;
  const rot=(transformParam(node,'rotate','degrees')||0)*Math.PI/180;
  const sx=transformParam(node,'scale','x')||1, sy=transformParam(node,'scale','y')||1;
  const kx=Math.tan(clampTo(transformParam(node,'skew','xDegrees')||0,-89,89)*Math.PI/180), ky=Math.tan(clampTo(transformParam(node,'skew','yDegrees')||0,-89,89)*Math.PI/180);
  const px=((transformParam(node,'origin','x')??0.5)-0.5)*width, py=((transformParam(node,'origin','y')??0.5)-0.5)*height;
  let m=matMul([1,0,0,1,tx,ty],[1,0,0,1,px,py]);
  m=matMul(m,[Math.cos(rot),Math.sin(rot),-Math.sin(rot),Math.cos(rot),0,0]);
  m=matMul(m,[sx,0,0,sy,0,0]);
  m=matMul(m,[1,ky,kx,1,0,0]);
  return matMul(m,[1,0,0,1,-px,-py]);
}
```

Composition order (left-to-right = outermost-first, applied to a point right-to-left): **translate ∘ (+pivot) ∘ rotate ∘ scale ∘ skew ∘ (−pivot)**. The pivot is `origin` expressed as a fraction 0..1 of the node's `width`/`height`, re-centred to −0.5..+0.5 and scaled by the extent — so `origin = (0.5, 0.5)` is the node centre and is the default.

Supporting matrix helpers (`public/index.html:1013-1016`):

```js
function matMul(m, n) { return [m[0]*n[0]+m[2]*n[1], m[1]*n[0]+m[3]*n[1], m[0]*n[2]+m[2]*n[3], m[1]*n[2]+m[3]*n[3], m[0]*n[4]+m[2]*n[5]+m[4], m[1]*n[4]+m[3]*n[5]+m[5]]; }
function matInvert(m) { const det=(m[0]*m[3]-m[1]*m[2])||1e-6, a=m[3]/det, b=-m[1]/det, c=-m[2]/det, d=m[0]/det; return [a,b,c,d,-(a*m[4]+c*m[5]),-(b*m[4]+d*m[5])]; }
function matX(m, x, y) { return m[0]*x+m[2]*y+m[4]; }
function matY(m, x, y) { return m[1]*x+m[3]*y+m[5]; }
```

Transform defaults and ranges (`project_document.py:240-244`):

| transform | params | default | range |
|---|---|---|---|
| `translate` | `x`, `y` | 0, 0 | −8192 … 8192 |
| `rotate` | `degrees` | 0 | −360 … 360 (**degrees**, converted with `*Math.PI/180`) |
| `scale` | `x`, `y` | 1, 1 | 0.01 … 20 (**independent per axis → non-uniform scale is legal**) |
| `skew` | `xDegrees`, `yDegrees` | 0, 0 | −89 … 89 (**degrees**, converted through `tan()`) |
| `origin` | `x`, `y` | 0.5, 0.5 | 0 … 1 (fraction of the node extent) |

#### The combine modes

Exactly four, fixed set (`project_document.py:19`):

```py
COMPONENT_MODES = ("add", "subtract", "softAdd", "subtractSoft")
```

Per-component dials (`project_document.py:329`):

| dial | default | range |
|---|---|---|
| `mode` | `"add"` | the four above; an **unsupported mode disables the component** rather than erroring (`project_document.py:320-326`) |
| `enabled` | `true` | bool |
| `viscosity` | **0.4** | 0 … 1 |
| `sharpness` | **0.5** | 0 … 1 |

`compileShape` turns those into the per-part compiled fields (`public/index.html:1101-1105`):

```js
parts.push({ child, mode:component.mode||'add', inverse:matInvert(forward),
  k: Math.max(0,Number(component.viscosity)||0)*Math.min(shapeWidth,shapeHeight)*0.55,
  reach: Math.min(shapeWidth,shapeHeight)*0.55,
  strength: clamp01(Number(component.viscosity)||0),
  n: blendExponent(component.sharpness) });
```

So `viscosity` plays **two different roles depending on mode**: it is the smooth-min band half-width `k` (in canvas units, scaled by `0.55 * min(w,h)`) for `softAdd`, and the 0..1 `strength` fraction for `subtractSoft`. `sharpness` becomes the exponent `n = pow(8, sharpness)` for both.

`evalShape`, the whole combiner, verbatim (`public/index.html:1113-1142`):

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
    else if (part.mode==='subtractSoft') {
      const carve=-d-distance, band=Math.sin(part.strength*Math.PI)*part.reach*0.6;
      const bite=-smoothMinShaped(0,-carve,band,part.n);
      distance=distance+part.strength*bite;
    }
    else distance=Math.min(distance,d);
  }
  return distance;
}
```

The four literal expressions:

- **`add`** (also the fallthrough `else`) → `distance = min(distance, d)` — hard union.
- **`subtract`** → `distance = max(distance, -d)` — a real hole in the field, not a repaint.
- **`softAdd`** → `distance = smoothMinShaped(distance, d, k, n)` = `min(a,b) - pow(max(k-|a-b|,0)/k, n) * k/(2n)`. Deepest the joint ever pulls in is `k/(2n)`.
- **`subtractSoft`** → the three-line block above. **The exact "bite" term is `bite = -smoothMinShaped(0, -carve, band, n)` where `carve = -d - distance` and `band = sin(strength*π) * reach * 0.6`, `reach = 0.55*min(shapeWidth, shapeHeight)`, and the final assignment is `distance = distance + strength * bite`.**

The design reasoning is documented in-line at `public/index.html:1123-1134` and is worth carrying over verbatim, because it is a genuine correctness argument, not a preference:
- `max(a,b) == a + max(0, b-a)`, so `distance + max(0, -d - distance)` is *algebraically identical* to the hard `max(distance, -d)`. `carve` is exactly that `b-a` term.
- Scaling the **carved amount** by `strength` (rather than shrinking the cutter's own SDF by an offset) is what makes `strength = 0` an **exact no-op everywhere in the field**. Offsetting the cutter instead still clips a deep point's magnitude wherever the shrunken cutter surface sits closer than the point's own depth, which silently corrupts extrusion/bevel shading with no visible hole — the failure mode this shape was chosen to avoid.
- The blend band is `sin(strength*π)*reach*0.6`: **zero at strength 0 AND at strength 1**, peaking at strength 0.5. That is what rounds the growing cut's edge instead of creasing it, and it multiplies through the same zero at `strength = 0` so it can never reintroduce the corruption.
- At `strength = 1`, `band = 0`, so `smoothMinShaped` degenerates to `min(0, -carve)`, `bite = max(0, carve)`, and `distance = distance + max(0, -d - distance) = max(distance, -d)` — byte-for-byte a plain `subtract`.

**First-component rule (easy to miss):** the first enabled part seeds `distance` with `1e6` if its mode is `subtract` or `subtractSoft`, otherwise with its own `d`. So a leading subtract cuts nothing, it just leaves the field empty.

**Empty / capped tree:** `if (!parts || !parts.length) return 1e6` and `if (depth>=MAX_COMPONENT_DEPTH) return { parts:[], halfW:1, halfH:1, truncated:true }` (`public/index.html:1079`) — a truncated subtree degrades to "infinitely far away", i.e. an add vanishes and a subtract cuts nothing. Caps: `MAX_COMPONENT_DEPTH = 4`, `MAX_COMPONENT_PARTS = 512` (`public/index.html:990`), the parts budget counting only leaves at `depth >= 1` (`public/index.html:1085`).

### 1.4 Sweep and shell operators (d) — **NEITHER EXISTS**

**There is no angular sweep, wedge, pie-slice or angular-range limiter anywhere in 3D Shaper's geometry, and no shell / hollow / annulus / onion operator either.** I searched the whole file for `sweep|wedge|pie|hollow|annulus|shell|thickness|onion` — every hit is unrelated:

- `sweep` appears **only as a UI concept** — the section-bar "quick view" that temporarily flips which panel sections are shown (`public/index.html:187`, `527`, `534`, `547`, `570-577`, `614`, `623-641`, `2278`). Zero geometric meaning.
- `shell` appears only as the CSS custom property `--shell-top`, the app-bar height (`public/index.html:216`, `267`, `271-272`).
- `thickness`, `annulus`, `hollow`, `onion`, `wedge`, `pie` — **zero hits**.

The four combine modes in 1.3 are the complete operator set. A ring in Shaper can only be built the manual way: an `add` disc plus a `subtract` (or `subtractSoft`) disc, which is exactly what the code comment at `public/index.html:1809-1810` assumes when it talks about "a ring, or parts pushed off-centre" whose origin falls outside the field.

The nearest thing to a shell that exists is **not geometry at all**: the per-layer *edge ring* is a **material band** measured inward from the silhouette, applied at shading time (`public/index.html:1348-1350`):

```js
const edgeDepth=edge ? clamp01(edge.depth===undefined?0.13:edge.depth) : 0.13, edgeFull=!!edge && edgeDepth>=1;
const cellSize=(cellW+cellH)/2, edgeBand=Math.max(cellSize*1.7, span*edgeDepth);
```

with the band test `-distance <= edgeBand` at `public/index.html:1428`. `edge.depth` defaults to **0.13**, range 0 … 1 (`project_document.py:300`). It recolours cells; it removes no geometry, changes no silhouette, and is not composable.

Likewise the layer **outline** is a post-pass over a binary coverage bitmap (`public/index.html:1404-1411`, `1566-1567`), width default 2, range 0.5 … 32 (`project_document.py:278`) — again a paint, not a field operator.

### 1.5 Hexagon, octagon and star verbatim (e) — **the prior audit is CONFIRMED**

Verbatim, from `public/index.html:1036`, `:1037`, `:1039`:

```js
if (type==='hexagon') { const a=Math.abs(x)/rx, b=Math.abs(y)/ry; return Math.max(b-1, a+b*0.55-1)*unit; }
if (type==='octagon') { const a=Math.abs(x)/rx, b=Math.abs(y)/ry; return Math.max(Math.max(a,b)-1,(a+b)/1.42-1)*unit; }
if (type==='star')    { const angle=Math.atan2(y/ry,x/rx), lobe=0.5+0.5*Math.cos(5*angle-Math.PI/2); return (Math.hypot(x/rx,y/ry)-(0.42+0.58*Math.pow(lobe,0.8)))*unit; }
```

**Hexagon — confirmed hand-fitted, axis-aligned, and not a regular hexagon.** It is a max of two half-plane families in the *normalised* coordinates `a=|x|/rx`, `b=|y|/ry`: a pair of flats at `b = 1` and a slanted pair at `a + 0.55b = 1`. Solving the boundary: half-width is `1` (in `a` units) at `b=0`, and `0.45` at `b=1`. A genuine flat-top regular hexagon normalised the same way needs the coefficient **0.5**, not 0.55 — the top edge would be half-width 0.5, not 0.45. So the constant is a hand-picked approximation, off by 10% on the top/bottom edge width. There is **no rotation parameter and no side-count parameter**: it is permanently flat-top-and-bottom with points at ±x, and `rx`/`ry` stretch it non-uniformly.

**Octagon — confirmed hand-fitted and axis-aligned, but geometrically closer than the hexagon.** `max(a,b) ≤ 1` is the axis-aligned square, `(a+b)/1.42 ≤ 1` are the four diagonal cuts. For a regular octagon whose axis-aligned and diagonal faces share the same apothem, the exact constant is `√2 = 1.41421…`; the code uses **1.42**, so the diagonal faces sit ~0.4% further out than regular. It is truly *regular-shaped* in normalised `(a,b)` space only — the `rx`/`ry` denominators mean an octagon on a non-square box is a stretched octagon, not a rotated or scaled regular one. Again: **no rotation, no side count**.

**Star — confirmed: a fixed five-lobe cosine flower with no parameters at all.** Not a star polygon. `lobe = 0.5 + 0.5*cos(5*angle - π/2)` is a smooth 5-periodic cosine in `[0,1]`; the boundary radius is `0.42 + 0.58 * lobe^0.8`, so the valleys sit at normalised radius **0.42** and the tips at exactly **1.0**. The `^0.8` exponent sharpens the tips slightly and widens the valleys. Every number in it is a literal:

- arm/point count: **hard-coded `5`** in `cos(5*angle …)` — not exposed, not animatable, not in `SHAPE_TYPES`, not in `ANIMATABLE_PARAMETERS`.
- arm length: **hard-coded** as the `0.42` / `0.58` split.
- tip sharpness: **hard-coded `0.8`**.
- phase: **hard-coded `−π/2`**, putting a tip at `angle = π/10 = 18°` and hence also at `90°`. Because Shaper's canvas is y-DOWN, `90°` is straight **down**: the default star points a tip at the bottom of the image, not the top. (See Part 3.)
- base width, skew, valley position: **do not exist**.

Because the boundary is a smooth cosine rather than straight chords, the silhouette is a **flower/blob** — rounded valleys and rounded tips — not the straight-edged star polygon Pyre draws. Any rebuild that wants a Pyre-style star must not port this expression.

### 1.6 What breaks a Lipschitz bound (f)

Ranked by how badly they break it.

1. **`evalShape`'s child-distance path returns the CHILD's distance unrescaled** (`public/index.html:1118`). A part transforms the sample point by `part.inverse` and returns `evalShape(part.child, px, py)` with **no multiplication by the transform's scale factor**. So a component scaled to `scale.x = 0.1` returns distances ten times too large in the parent's units. This is the single biggest violation, because it then feeds directly into `smoothMinShaped`'s band `k` (which *is* in parent canvas units) and into `subtractSoft`'s `carve`/`band`, so the soft-blend band width is wrong by the child's scale factor.

2. **Non-uniform scale is a first-class legal transform** — `scale.x` and `scale.y` are independent, each 0.01 … 20 (`project_document.py:242`). Under an anisotropic map no SDF stays a distance function; the correct conservative repair is division by the largest singular value, and **the code performs no such repair anywhere**.

3. **Skew is legal on both the node and the primitive** — node `skew.xDegrees`/`yDegrees` (`project_document.py:243`) and the primitive's own `skew` (`public/index.html:1055`, `lx = x - skew*y`). A shear is not a similarity; `|∇f|` is not preserved, and nothing compensates.

4. **Six of the eight primitives are not metric at all.** `ellipse`, `diamond`, `hexagon`, `octagon`, `star` all evaluate in normalised `(x/rx, y/ry)` coordinates and then multiply by `unit = min(rx, ry)`. That factor makes the value *dimensionally* a canvas distance and makes it *exact* when `rx == ry`, but on a 4:1 box the gradient is off by up to 4× along the long axis. `triangle` is worse: it has **no `*unit` at all** (`public/index.html:1035`) and returns a plain `max` of three unnormalised half-plane expressions — its gradient is 1 along the vertical faces but is scaled by the taper along the sloped ones.

5. **`pulge` divides x by `warp`.** `primitiveSdf(type, lx/warp, ly, …) * Math.min(1, warp)` (`public/index.html:1058`). The `*min(1,warp)` term is a **one-sided** repair: it fixes the pinch case (`warp < 1`, where the field would otherwise be too steep) and does **nothing** for the bulge case (`warp > 1`, where the field is too shallow). `warp` is floored at 0.08 but has no ceiling beyond `1 + 0.8*max(pulge)` = 1.8.

6. **`max` and `min` blends are not gradient-preserving in general.** `subtract`'s `max(distance, -d)` is exact for an exterior SDF but overestimates near a concave seam; `add`'s `min` underestimates near a convex corner. `smoothMinShaped(a,b,k,n) = min(a,b) - pow(h,n)*k/(2n)` subtracts up to `k/(2n)`, so it deepens the field by a bounded but non-zero amount and its own gradient exceeds 1 inside the band.

7. **`subtractSoft` piles a scaled quantity onto a distance.** `distance = distance + strength*bite` where `bite` itself came out of a `smoothMinShaped`. For `0 < strength < 1` the result is a fractional blend of two fields with different gradients and is not a distance to anything.

8. **Sentinel `1e6`.** Both the empty-parts case and the leading-subtract seed inject a literal `1e6` (`public/index.html:1116`, `1119`), which is not a distance and will hard-break any sphere-tracer or any consumer that reads magnitude.

9. **Compiled-extent inflation on nested children** (`public/index.html:1090-1098`, documented in the code as a known consequence): a complex child's `halfW`/`halfH` come from a transform-**corner box**, so an off-axis-rotated nested child measures larger than its silhouette and gets a **~2.5× wider soft-add band at 45°** than an identically sized basic child would. The author documented this deliberately rather than faking a number.

Two things that are *not* Lipschitz problems but are worth flagging alongside them:

- The whole app never sphere-traces the field for shading. It brute-force rasterises a bounding box per layer (`public/index.html:1417-1418`, box from `compileLayer`'s `rect` at `public/index.html:1351-1357`). The two places that *do* march are `buildShapePath` (`public/index.html:1785-1805`) and `buildCompiledShapePath` (`public/index.html:1811+`), and **both use bisection, not sphere tracing** — 24 bisection steps on a ray of length `max(rx,ry)*4` — precisely because the field is not trustworthy as a distance. `edgeArcTable`'s tracer at `public/index.html:1193-1209` says so explicitly ("a march would start stepping straight over thin features").
- `buildShapePath` is **not a second shape registry**. It ray-marches the same `evalBasicShape` and emits a `Path2D` polyline, sample count `clampTo(ceil(max(drawW,drawH)*2.4), 220, 3000)` (`public/index.html:1795`). Both path builders fall back to a plain bounding **rect** when the origin is outside the field (`public/index.html:1789`, `1813`) — i.e. a ring previews as a box.

---

## PART 2 — Pyre

### 2.1 The parameterised star (a) — the load-bearing spec

Pyre's star is `ShapeForm.Star`, one case of the enum at `Runtime/Pyre/Pyre.cs:111-117`:

```csharp
public enum ShapeForm
{
    Disc, Gem, Crescent, Sparkle, Sprite, Box, Pyramid, Can, Orb, Ring, Text, Streak, Star, Fire, Fireball, Polygon,
    [System.Obsolete(...)] Inferno,
    [System.Obsolete(...)] ForkBlast,
    Playback3D,
}
```

Its dials are declared at `Runtime/Pyre/Pyre.cs:489-492`, verbatim including the trailing comments:

```csharp
[Range(2, 20)] public int starArms = 5;                 // point count 2..20 (5 = the classical five-pointed star)
public ZUIValue starLength = new ZUIValue(0.62f);       // arm reach 0..1 over own life; inner radius = R·(1−length). 0.62 ≈ the golden-ratio pentagram inner radius
public ZUIValue starBaseWidth = new ZUIValue(1f);       // valley angular position as a fraction of the half-sector, 0.1..1 over own life (1 = classical midpoint; smaller = thinner arm bases, wider valleys)
public ZUIValue starSkew = new ZUIValue(0f);            // valley swirl in degrees −60..60 over own life — rotates the valleys, pinwheel-twisting the arms (clamped so valleys never cross tips)
```

| dial | C# type | default | range | animatable? | where the range is enforced |
|---|---|---|---|---|---|
| `starArms` | `int` | **5** | **2 … 20** | **NO** — a plain `int`, not a `ZUIValue` | `[Range(2,20)]` attribute (`Pyre.cs:489`), re-clamped in the renderer `Mathf.Clamp(layer.starArms, 2, 20)` (`PyreRenderer.cs:3260`), and in the editor `Mathf.Clamp(Mathf.RoundToInt(v), 2, 20)` (`PyreWindow.cs:1713`) |
| `starLength` | `ZUIValue` | **0.62f** | **0 … 1** | **YES** — over the particle's own life | the range lives on the **editor row**, `Val("Length", …, s.starLength, 0f, 1f)` (`PyreWindow.cs:1725`); the renderer additionally hard-clamps with `Mathf.Clamp01` (`PyreRenderer.cs:3261`) |
| `starBaseWidth` | `ZUIValue` | **1f** | **0.1 … 1** | **YES** | editor row `Val("Base width", …, s.starBaseWidth, 0.1f, 1f)` (`PyreWindow.cs:1730`); renderer `Mathf.Clamp(…, 0.1f, 1f)` (`PyreRenderer.cs:3263`) |
| `starSkew` | `ZUIValue` | **0f** | **−60 … +60 DEGREES** | **YES** | editor row `Val("Skew °", …, s.starSkew, -60f, 60f)` (`PyreWindow.cs:1719`); the renderer converts with `* Mathf.Deg2Rad` (`PyreRenderer.cs:3264`) and applies **no** magnitude clamp of its own — the clamp that matters is the valley clamp below |

Two shared dials also feed the star and must not be forgotten:
- **`size` × `sizeMul`** is the **tip radius R** — passed into `DrawStarBody` as the `radius` argument. Stated at `Pyre.cs:68` and `Pyre.cs:484-487`, and the editor keeps the shared Size row visible for exactly this reason (`PyreWindow.cs:1702-1703`).
- **`edgeSoftness`** feathers the rim radially; **`shapeFill`** colours it; **`particleSpin`** turns it. (`Pyre.cs:487`, `PyreWindow.cs:1732-1733`.)

Defensive null-fill in the editor (`PyreWindow.cs:1706-1708`) repeats the same three defaults — worth mirroring so a hand-edited asset can't NRE:

```csharp
s.starLength ??= new ZUIValue(0.62f);       // defensive; the real defaults come from the spec factories
s.starBaseWidth ??= new ZUIValue(1f);
s.starSkew ??= new ZUIValue(0f);
```

#### The literal maths

`DrawStarBody` is at `Runtime/Pyre/PyreRenderer.cs:3256-3364`. The geometry setup, verbatim (`PyreRenderer.cs:3259-3281`):

```csharp
float R = radius;
int N = Mathf.Clamp(layer.starArms, 2, 20);
float len = Mathf.Clamp01(Eval(layer.starLength, life, spec.seed, particleIndex, FldStarLen));
float rIn = Mathf.Max(0.5f, R * (1f - len));                              // valley radius, floored so it never collapses to a point
float baseW = Mathf.Clamp(Eval(layer.starBaseWidth, life, spec.seed, particleIndex, FldStarBase), 0.1f, 1f);
float skew = Eval(layer.starSkew, life, spec.seed, particleIndex, FldStarSkew) * Mathf.Deg2Rad;

float sector = 2f * Mathf.PI / N;        // angular span between adjacent tips
float halfSector = Mathf.PI / N;         // half of it — the classical (single-valley) midpoint
float lo = 0.02f * sector, hi = 0.98f * sector;
float offA = Mathf.Clamp(halfSector * baseW + skew, lo, hi);   // valleyA angle past tip k
float offB = Mathf.Clamp(halfSector * baseW - skew, lo, hi);   // valleyB angle before tip k+1
float vA = offA;                         // valleyA relative angle from the tip
float vB = sector - offB;                // valleyB relative angle from the tip
bool coincide = vB <= vA;
const float TIP = Mathf.PI * 0.5f;       // tip 0 points UP (+90°), matching the polygon convention
```

**THE CLAMP THAT STOPS THE VALLEYS CROSSING THE TIPS, transcribed exactly:**

```csharp
float lo = 0.02f * sector, hi = 0.98f * sector;
float offA = Mathf.Clamp(halfSector * baseW + skew, lo, hi);
float offB = Mathf.Clamp(halfSector * baseW - skew, lo, hi);
```

(`PyreRenderer.cs:3272-3274`.) It is a **symmetric clamp of each valley's offset into `[0.02·sector, 0.98·sector]`**, applied to `offA` and `offB` separately, where `sector = 2π/N`. The design comment at `PyreRenderer.cs:3231-3237` gives the proof that this is sufficient and I reproduce its argument because an implementer needs it: the unclamped sum is `offA + offB = sector·baseWidth ≤ sector` (the `+skew` and `−skew` cancel); and any clamp that raises one offset to `0.98·sector` necessarily drives the other to `0.02·sector`, capping the sum at `sector`. Therefore **`offA + offB ≤ sector` always ⇒ `vA ≤ vB` always ⇒ the two valleys never cross**. At worst they **coincide** (`offA + offB = sector`), which is exactly the `baseWidth = 1` default and the extreme-skew case; the `coincide` branch then collapses to the classical two-segment star.

The byte-identity argument for the default (`PyreRenderer.cs:3237-3244`) is also worth preserving: at `bw=1, skew=0`, `offA = offB = halfSector` unclamped, so `vA = halfSector` and `vB = sector − halfSector`; since `sector == halfSector + halfSector` exactly in IEEE round-to-nearest (`fl(2a) == 2·fl(a)`), the exact difference gives `vB == halfSector == vA` → `coincide` → the pre-fix two-segment star renders bit-for-bit unchanged, and the flat base chord has zero length and is unreachable.

The inside test, verbatim (`PyreRenderer.cs:3313-3341`):

```csharp
float d = Mathf.Sqrt(dx * dx + dy * dy);
if (d > R) continue;                                 // beyond the tips — nothing is inside

float th = Mathf.Atan2(dy, dx);
float phi = th - TIP;
float ph = phi - Mathf.Floor(phi / sector) * sector;  // [0, sector): angle past this arm's tip
float aTip = th - ph;                                 // absolute angle of the tip starting this slot
float a1, r1, a2, r2;
if (coincide)
{
    if (ph < vA) { a1 = aTip;      r1 = R;   a2 = aTip + vA;     r2 = rIn; }   // tip → valley
    else         { a1 = aTip + vA; r1 = rIn; a2 = aTip + sector; r2 = R;   }   // valley → next tip
}
else if (ph < vA) { a1 = aTip;      r1 = R;   a2 = aTip + vA;     r2 = rIn; }  // tip → valleyA
else if (ph < vB) { a1 = aTip + vA; r1 = rIn; a2 = aTip + vB;     r2 = rIn; }  // valleyA → valleyB (flat base chord)
else              { a1 = aTip + vB; r1 = rIn; a2 = aTip + sector; r2 = R;   }  // valleyB → next tip

float Dx = Mathf.Cos(th), Dy = Mathf.Sin(th);
float p1x = r1 * Mathf.Cos(a1), p1y = r1 * Mathf.Sin(a1);
float p2x = r2 * Mathf.Cos(a2), p2y = r2 * Mathf.Sin(a2);
float ex = p2x - p1x, ey = p2y - p1y;
float denom = ex * Dy - ey * Dx;                      // cross(E, D)
float bound = Mathf.Abs(denom) < 1e-6f ? R : (ex * p1y - ey * p1x) / denom;   // cross(E, P1) / cross(E, D)
if (bound <= 0f) continue;                           // safety: degenerate ray
if (d > bound) continue;                             // outside the star along this ray (a valley notch)

float rInner = bound * (1f - soft);                  // Disc rim idiom, radial
float edge = d <= rInner ? 1f : 1f - Mathf.InverseLerp(rInner, bound, d);
```

So Pyre's star is **not an SDF at all**. It is a **per-ray boundary solve producing coverage directly**: take θ, find the one of three edges (tip→valleyA, the flat base chord valleyA→valleyB, valleyB→next tip) whose angular slot contains θ, intersect the ray with that segment via `t = cross(E,P1)/cross(E,D)` with `E = P2−P1`, `D = (cosθ, sinθ)`, `cross(A,B) = Ax·By − Ay·Bx`. Inside iff `d ≤ bound`. This is legitimate because a star polygon is star-shaped about its centre. The shape fill maps `(u,v) = (dx/R, dy/R)` (`PyreRenderer.cs:3345`).

Geometry summary in one line: **a 3N-gon** — N tips at radius `R`, and **two** valley vertices per sector at radius `rIn = max(0.5, R·(1−starLength))`, joined by a flat base chord. Two valleys rather than one is the whole point: a single valley placed `off` from the tip is inherently asymmetric and reads as an unintended pinwheel — the defect this revision fixed (`PyreRenderer.cs:3226-3230`). With two, `starBaseWidth` narrows both symmetrically and `starSkew` shifts **both the same signed way**, giving a true pinwheel where the arm rotates but keeps its width.

Bounding box when no geometry modifier is active: an `R` box exactly, because tips reach exactly `R` (`PyreRenderer.cs:3303-3307`). With any geo warp active it degrades to a whole-canvas scan (`PyreRenderer.cs:3301`).

### 2.2 Pyre's regular-polygon / N-gon generator (b)

**Two separate ones exist.** They are different code with different purposes — do not conflate them.

**(i) `ShapeForm.Polygon` — the drawn 2D N-gon.** Dial at `Runtime/Pyre/Pyre.cs:502`:

```csharp
[Range(3, 12)] public int polygonSides = 4;             // side count 3..12 (3 = triangle, 4 = square, 6 = hexagon)
```

`polygonSides`: `int`, default **4**, range **3 … 12**, **NOT animatable** (a plain int; `Pyre.cs:501` notes the value-type consequence for `Clone()`). `DrawPolygonBody` is at `PyreRenderer.cs:3373-…`; its setup, verbatim (`PyreRenderer.cs:3379-3386`):

```csharp
float R = radius;
int N = Mathf.Clamp(layer.polygonSides, 3, 12);
float sector = 2f * Mathf.PI / N;        // angular span between adjacent vertices
float baseRot = (N % 2 == 0) ? sector * 0.5f : 0f;
const float UP = Mathf.PI * 0.5f;        // vertex 0 points UP (+90°), matching the Star's tip convention
float tip0 = UP + baseRot;               // absolute angle of vertex 0
```

It **reuses the Star's per-ray boundary idiom unchanged**, with one edge per angular sector and both endpoints at `R` (no valleys) — same `t = cross(E,P1)/cross(E,D)` solve, same `d ≤ bound` test, same radial `edgeSoftness` rim, same `shapeFill`, same pix modifiers (`PyreRenderer.cs:3364-3372`). Every vertex sits at the **circumradius** `R = size × sizeMul`, so an `R` box bounds it exactly.

The base-rotation rule is a fixed geometric offset independent of `particleSpin`: **an even `N` is turned half a sector so a flat EDGE faces up/down** (a square sits flat, not as a diamond; a hexagon is flat-top), while **an odd `N` keeps a vertex up** (an upright triangle/pentagon, which already gives a flat bottom edge).

**(ii) The swarm-layout polygon — a placement helper, not a silhouette.** `SwarmShapeKind` (a regular polygon by side count, `Circle = ∞ sides`, `Pyre.cs:20`) drives particle *placement* through `PolyVertex` / `GridLayoutPolygon` / `GridLayoutPolygonRing` (`PyreRenderer.cs:2355-2361`, `2486-2493`, `2631-2652`). The per-side parameterisation is the same idea:

```csharp
int sides = SideCount(kind);
float t = p * sides;
…
if (k >= sides) { k = sides - 1; frac = 1f; }   // defensive: wrapped p is < 1 so t < sides and this can't fire
Vector2 a = PolyVertex(cx, cy, r, k, sides);
Vector2 b = PolyVertex(cx, cy, r, k + 1, sides); // k+1 may equal sides; trig is periodic → vertex 0
```
(`PyreRenderer.cs:2355-2361`)

`GridLayoutPolygonRing` uses per-side-uniform (not perimeter-uniform) spacing deliberately: *"on any straight-sided ring, perimeter-uniform does not [work]. A ring whose count equals `sides` …"* (`PyreRenderer.cs:2631-2633`). Note the `half ? 0.5f : 0f` half-step offset at `PyreRenderer.cs:2648`.

### 2.3 The animatable-dial convention (c) — **`ZUIValue`**

**The type is `ZUIValue`** — a `[Serializable] class` (reference type, hence the `??=` null-guards and the explicit `CloneVal` deep copies), defined at `Assets/Packages/Laubrary/Zui/Scripts/Runtime/ZUIValue.cs:17`. It is ZUI's, not Pyre's, and it is runtime-safe with no editor dependency.

Its modes (`ZUIValue.cs:19-20`, append-only, serialized as an int):

```csharp
public enum Mode { Static, MinMax, Curve, Steps, Oscillation }
```

Backing fields: `m_static = 1f`; `m_min = 0f` / `m_max = 1f`; a `List<ZUIEnvelopePoint> m_points` in normalised time `[0..1]` with `m_yMin = 0f` / `m_yMax = 1f`, `m_duration = 4f` seconds, `m_warmup = 0f`, `m_cooldown = -1f` (−1 = never loop), `m_smoothness` `[0..1]` (0 = authored per-segment bend, 1 = Catmull-Rom through the points, no effect below 3 points); `List<float> m_steps`; three oscillation envelopes `m_oscMin` / `m_oscMax` / `m_oscRate` with `m_oscRateMax = 8f`; and an optional external `m_multiplierId` resolved by `public static Func<string,float> MultiplierResolver` (`ZUIValue.cs:22-72`).

**The call used to sample it at a time `t` — and this is the one that matters — is NOT `ZUIValue.Evaluate`.** Pyre has its own deterministic funnel, `PyreRenderer.Eval`, at `Runtime/Pyre/PyreRenderer.cs:5026-5054`, verbatim:

```csharp
static float Eval(ZUIValue v, float life, int seed, int particleIndex, int fieldId)
{
    if (v == null) return 0f;
    switch (v.mode)
    {
        case ZUIValue.Mode.Static: return v.staticValue;
        case ZUIValue.Mode.MinMax:
        {
            var rng = new System.Random(Hash(seed, particleIndex, fieldId, _layerSalt));
            return Mathf.Lerp(v.min, v.max, (float)rng.NextDouble());
        }
        case ZUIValue.Mode.Curve:
            return ZUIEnvelopeEvaluator.Evaluate(v.points, Mathf.Clamp01(life), v.yMax);
        case ZUIValue.Mode.Steps:
        {
            var steps = v.steps;
            int n = steps != null ? steps.Count : 0;
            if (n == 0) return v.staticValue;
            return steps[Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(life) * n), 0, n - 1)];
        }
        default: return v.staticValue;
    }
}
```

Call shape at every site: **`Eval(layer.<dial>, life, spec.seed, particleIndex, Fld<Name>)`** — e.g. `Eval(layer.starLength, life, spec.seed, particleIndex, FldStarLen)` (`PyreRenderer.cs:3261`). Key points an implementer must not get wrong:

- **`life` is the particle's own normalised life in `[0..1]`, not seconds.** The `Curve` branch samples `v.points` **directly at `Clamp01(life)` and deliberately ignores `duration`/`warmup`/`cooldown`** — the code comment (`PyreRenderer.cs:5037-5039`) says why: `EvaluateRaw` would divide `life` by `duration` (default 4 s), sweeping only the curve's **first quarter**. So **never call `ZUIValue.Evaluate(t)` from render code.**
- `MinMax` draws **once per particle** from a seeded `System.Random`, keyed by `Hash(seed, particleIndex, fieldId, _layerSalt)` — this is what makes each particle differ *reproducibly*. `Hash` is `public` precisely so a `PyreForm` derives every random draw from the same function (`PyreRenderer.cs:5056-5058`).
- Each animatable dial needs its **own `fieldId`** constant (`FldStarLen`, `FldStarBase`, `FldStarSkew`, `FldSpin`, `FldModifier`, …). `PyreRenderer.cs:109` is explicit that the id list is closed and that *"ringInner is a plain non-animatable float"* — i.e. **a non-animatable dial gets no field id.**
- `Oscillation` mode currently falls through to `default: return v.staticValue` in Pyre's `Eval`. Steps used to do that too and was fixed (`PyreRenderer.cs:5041-5044`); Oscillation has not been.
- There is a fast path, `IsLinearTo(ZUIValue v, out float k)` (`PyreRenderer.cs:5014-5022`), that recognises the exact two-point `(0,0)→(1,k)` linear curve and short-circuits it; anything else returns false and goes through `Eval`.
- Determinism + threading contract (`Runtime/Pyre/PyreForm.cs:12-19`): `Render` must be a pure function of `(ctx, fields)`; use `ctx.seed` / `ctx.layerSalt` / `PyreRenderer.Hash` for every draw — **never `UnityEngine.Random`, never `Time`**. Frames render concurrently on per-thread deep clones, so per-instance scratch is fine but **mutable statics and any `UnityEngine.Object` touch are forbidden**.

Editor side: a `ZUIValue` row is authored with the window's `Val(label, tooltip, value, min, max)` helper (`PyreWindow.cs:1716-1730`) and a non-animatable int with `Z.MicroSlider(label, value, min, max, tooltip, setter, width, showValue:, decimals:)` (`PyreWindow.cs:1711-1714`). A `PyreForm`'s own dials need **zero editor code** — `[Range]`/`[Tooltip]` attributes drive the UI through `ZuiReflect` (`PyreForm.cs:5-6`).

Cloning: every `ZUIValue` field must be listed in `Clone()` with `CloneVal(...)` (`Pyre.cs:799-801`), because `MemberwiseClone` would share the reference. Value-type dials like `polygonSides` are copied for free (`Pyre.cs:501`).

### 2.4 Existing angular-sweep and hollow/ring dials on Pyre shapes (d)

**Angular sweep: none.** I grepped `Runtime/Pyre/Pyre.cs` for `arcDeg|spanDeg|sweepDeg|angleSpan|startAngle|endAngle` — **zero hits**. No Pyre shape can be limited to an angular range. The nearest relatives are all something else:

- **`fireballArms`** (`Pyre.cs:604`): `[Min(1)] public int fireballArms = 1;` — *radial wedges the flame is mirrored into (1 = a plain outward burst; more = kaleidoscope)*, with `public bool fireballMirror = true` (`Pyre.cs:605`) alternating reflected wedges. This is a **kaleidoscope fold on a stateful heat-grid sim**, not an angular clip on a silhouette.
- **`ArcBurstForm`** (`Runtime/Pyre/Forms/Kiln/ArcBurstForm.cs`) has a tooltip mentioning *"the angular sweep does the dissipation"* (`ArcBurstForm.cs:252`) — that is prose about an effect's look, not a shape dial.

**Hollow / ring: exactly one, and it is not animatable.** `Runtime/Pyre/Pyre.cs:388-392`:

```csharp
// ── Ring form (shapeForm == Ring) — a flat two-sided tilted annulus (a Saturn ring) ───────────────────
// Outer radius R = evaluated `size` × sizeMul; inner hole radius = R·ringInner. gemTilt tips the ring
// … Not animatable (a plain float) — the ring's animation lives in size/tilt/spin.
[Range(0.1f, 0.92f)] public float ringInner = 0.55f;   // inner radius as a fraction of the outer radius
```

`ringInner`: `float`, default **0.55**, range **0.1 … 0.92**, **NOT animatable** — explicitly, and `PyreRenderer.cs:109` confirms it gets no field id. The renderer's annulus test (`PyreRenderer.cs:4617`, `4696`):

```csharp
float innerR = R * Mathf.Clamp(layer.ringInner, 0.1f, 0.92f);   // the hole radius
…
// Band SDF in ring-plane (local) units: < 0 inside the annulus, > 0 past either rim.
float dBand = Mathf.Max(innerR - rho, rho - R);
```

with `rho = sqrt(u*u + v*v)` after inverting the tilt/spin forward map (`PyreRenderer.cs:4691-4694`) and `bool hasFace = dBand <= 0f` (`PyreRenderer.cs:4711`). This is a genuine `max(inner − ρ, ρ − outer)` annulus band — **negative inside**, the same sign convention as Shaper.

It also carries a **gradient-magnitude correction** that a rebuild should copy wholesale, because it is the one place Pyre repairs a non-uniform map (`PyreRenderer.cs:4697-4709`, the "Bakery lesson: `trueDist ≈ Δf/|∇f|`"):

```csharp
float drdx = rho > 1e-4f ? (u / cyw - v * sywst / (cyw * ct)) / rho : 0f;
float drdy = rho > 1e-4f ? (v / ct) / rho : 0f;
float gradMag = Mathf.Max(0.2f, Mathf.Sqrt(drdx * drdx + drdy * drdy));
float rimDist = Mathf.Abs(dBand) / gradMag;   // screen-space distance to the NEAREST rim (both)
```

Without it, tilt/spin compress the ellipse so a fixed ring-plane distance spans fewer screen pixels near the flat sides, and the rim line visibly thins there. `gradMag` is floored at **0.2** so a near-zero gradient cannot blow the distance up.

**The other hollowing operator: the Crescent's bite.** `Pyre.cs:430-441`:

```csharp
public ZUIValue crescentBite = new ZUIValue(0.55f);   // mask disc size vs the main disc, 0..1, over own life
public ZUIValue crescentAngle = new ZUIValue(0f);     // degrees — which way the bite faces, over own life (LEGACY — see the centre pad)
[Range(0f, 1f)] public float crescentOffset = 0.5f;   // how far the bite disc is pushed out, fraction of radius (LEGACY — see the centre pad)
[SerializeReference] public ZUIValue crescentCenterXAnim;
[SerializeReference] public ZUIValue crescentCenterYAnim;
```

The bite is a **subtracted disc** with a soft rim, evaluated per pixel (`PyreRenderer.cs:3197-3202`):

```csharp
float mdx = dx - bx, mdy = dy - by;
float mdist = Mathf.Sqrt(mdx * mdx + mdy * mdy);
float biteEdge;
if (biteRadius < 0.5f) biteEdge = 1f;                              // no meaningful bite → plain disc
else if (soft <= 0.001f) biteEdge = mdist >= biteRadius ? 1f : 0f; // hard bite edge
else biteEdge = Mathf.Clamp01((mdist - biteRadius) / (soft * radius));
```

then multiplied into alpha: `float baseA = alpha * col.a * outerEdge * biteEdge;` (`PyreRenderer.cs:3211`). Note this is a **coverage-domain subtract** (multiply the alpha by an inverted mask), not a field-domain `max(d, -d')`. Nearer to Shaper's `subtractSoft` in intent, entirely different in mechanism. The centre pad supersedes the legacy polar `(crescentOffset, crescentAngle)` pair; the legacy path is kept for byte-identity (`Pyre.cs:435-440`).

### 2.5 The coverage idiom Pyre uses everywhere (for context)

Pyre's flat 2D forms all share one "rim" idiom: full alpha inside `rInner`, linearly feathered out to the boundary.

```csharp
float rInner = bound * (1f - soft);                  // Disc rim idiom, radial
float edge = d <= rInner ? 1f : 1f - Mathf.InverseLerp(rInner, bound, d);
```
(Star, `PyreRenderer.cs:3343-3344`; the Disc/Crescent form is `float outerEdge = d <= inner ? 1f : 1f - Mathf.InverseLerp(inner, radius, d);` at `PyreRenderer.cs:3210`.)

`soft` is the layer's `edgeSoftness`, **0 = a hard pixel edge, 1 = maximally soft**, and the feather is measured **along the ray**, radially — not perpendicular to the true silhouette. Final alpha is `alpha * col.a * edge` with an early-out at `baseA <= 0.002f` (`PyreRenderer.cs:3346-3347`).

Also present on Star/Polygon and the other flat forms: a **`borderWidth`/`borderFill` silhouette-outline pass over the layer's FINISHED alpha** — one general implementation for all six flat forms, no per-form boundary maths, the outermost `borderWidth` px of the drawn silhouette recoloured with `borderFill`, its alpha × the shape's coverage so the rim inherits the anti-aliased edge (`Pyre.cs:505-512`). NOT applied to the 3D solids.

---

## PART 3 — Discrepancies and traps

Things an implementer WILL get wrong if they port either source naively.

**1. Y-axis direction is OPPOSITE between the two sources.** 3D Shaper is a `<canvas>`: **y is DOWN**. Its grid walks `cy = (gy+0.5)*cellH` with `gy` increasing downward (`public/index.html:1418`) and its rotation matrix is `[cos, sin, −sin, cos]`, i.e. **clockwise-positive** in screen terms (`public/index.html`, `nodeMatrix`'s rotate step). Pyre writes a `Color32[]` through `Texture2D.SetPixels32` (`PyreRenderer.cs:5100`), which is **row-major, bottom-left origin** — the code says so explicitly at `PyreRenderer.cs:3602` — so **y is UP**, and `PyreRenderer.cs:5149` has to flip explicitly (`int py = (rows - 1 - row) * cellH;   // flip so row 0 is the top row in the image`) when packing a sheet. Consequence: Shaper's `TIP`-equivalent phase of `−π/2` puts a star tip at atan2 `+90°` = **DOWN on screen**; Pyre's `const float TIP = Mathf.PI * 0.5f` puts tip 0 at `+90°` = **UP on screen**. Same number, opposite result. Shaper's `triangle` likewise has its apex at `y = −ry` = the **top** of the canvas, which is what you want on screen but is the *bottom* under Pyre's convention.

**2. Degrees vs radians, and which one is stored.** Both sources **store degrees and convert at use**, but at different points. Shaper: `rotate.degrees` × `π/180` inside `nodeMatrix`; `skew.xDegrees`/`yDegrees` and the primitive's own `skew` go through `Math.tan(clampTo(deg,−89,89) * Math.PI/180)` — the stored value is degrees, the compiled value is **a tangent, not an angle** (`public/index.html:1051` for the primitive, `nodeMatrix` for the node). Pyre: `starSkew` is stored in degrees and converted with `* Mathf.Deg2Rad` at `PyreRenderer.cs:3264`; `particleSpin` is degrees and converted with `float sa = -spin * Mathf.Deg2Rad` — **note the negation** (`PyreRenderer.cs:3296`, `3392`). Every internal angle in the star/polygon maths (`sector`, `halfSector`, `offA`, `offB`, `vA`, `vB`, `TIP`, `aTip`) is **radians**.

**3. Radius vs half-extent vs diameter.** Shaper stores `width`/`height` (a full box) and halves them: `rx = max(0.5, width/2)`, `ry = max(0.5, height/2)` (`public/index.html:1050`). So Shaper's `rx` is a **half-extent**, and for `ellipse` it doubles as a semi-axis. Pyre's `R` is a **circumradius** — the *tip* radius for the Star, the *vertex* radius for the Polygon, the *outer* radius for the Ring — and it comes from `size × sizeMul`, an evaluated envelope, not a stored box (`Pyre.cs:68`, `484-487`, `496-498`). A shape authored as "100 wide" in Shaper is 50 in Pyre's units. Related: Shaper's `hexagon`/`octagon`/`star`/`ellipse`/`diamond` are all **stretched by an independent `rx`/`ry`**, so a Shaper hexagon on a non-square box is *not* regular; Pyre's Polygon/Star are always **radially regular** with a single `R` and cannot be stretched at all without a geo modifier.

**4. Shaper's `star` and Pyre's `Star` are not the same shape and must not be cross-ported.** Shaper's is a smooth **five-lobe cosine flower** with rounded valleys, rounded tips, and zero dials. Pyre's is a straight-edged **3N-gon star polygon** with four dials. If the rebuild wants "a star", it wants Pyre's. If it wants Shaper's look, it wants a "flower/blob" primitive and should call it that.

**5. The sign convention agrees; the *meaning* does not.** Both use negative-inside where a field exists. But Shaper's value is a (mostly non-metric) **distance** consumed as `inside = clamp01(-distance/span)` for extrusion height, with a **hard binary `distance > 0 → skip`** for coverage (`public/index.html:1420-1422`) — no anti-aliasing at all, by design, because the grid is nearest-neighbour upscaled. Pyre never computes a distance for Star/Polygon at all: it solves a **boundary radius per ray** and produces **anti-aliased coverage directly** via the `InverseLerp` rim. Porting Shaper's compositing into a context that expects soft alpha will give you jagged edges; porting Pyre's per-ray solve into a context that expects an SDF will give you something you cannot boolean.

**6. `viscosity` means two different things in Shaper depending on `mode`.** Same stored field, same 0..1 range, same default 0.4 — but for `softAdd` it becomes `k = viscosity * min(w,h) * 0.55`, a band half-width **in canvas units**, and for `subtractSoft` it becomes `strength = clamp01(viscosity)`, a dimensionless **fraction of the cut** (`public/index.html:1102-1104`). A UI that shows one slider labelled "viscosity" for both is showing two different quantities.

**7. `sharpness → n` is exponential, not linear.** `n = pow(8, clamp01(sharpness))` (`public/index.html:1006`), so `n` runs 1 → 8 with most of the visible change bunched at the high end. And the deepest a `softAdd` joint ever pulls in is `k/(2n)`, so raising sharpness at fixed `k` shrinks the fillet from `k/2` to `k/16` — the two sliders are genuinely independent, which is the whole reason both exist.

**8. `subtractSoft`'s band is zero at BOTH ends of `strength`.** `band = sin(strength*π)*reach*0.6` peaks at `strength = 0.5` and is 0 at both 0 and 1. A naive "wider strength = softer cut" implementation is wrong in both directions, and getting it wrong at `strength = 1` breaks the exact-equality-with-`subtract` guarantee.

**9. A leading `subtract`/`subtractSoft` component seeds `1e6`, it does not cut.** `public/index.html:1119`. Any port that starts from "empty = everywhere" or "empty = nowhere" without matching this will differ on the first component.

**10. Shaper never rescales a child's distance across a transform.** `evalShape` at `public/index.html:1118` inverse-transforms the point and returns the child's raw value. If the rebuild introduces sphere tracing, or lets the soft-blend band be specified in world units, this must be fixed — divide by the transform's largest singular value. Note also that Shaper's own path builders (`buildShapePath` at `public/index.html:1785-1805`, `buildCompiledShapePath` at `:1811`) use **24-step bisection, not sphere tracing**, precisely because the field is not a reliable distance; the arc tracer at `public/index.html:1193` says so in as many words.

**11. Neither source has a sweep or a shell — this is a genuine gap, not something to copy.** Shaper: nothing (1.4). Pyre: one non-animatable `ringInner` annulus fraction on one form, and a coverage-domain crescent bite. If Shaper-the-Laubrary-tool is to have an angular wedge or a hollow-out operator, **there is no reference implementation in either source** and it has to be designed. The one piece of prior art worth reusing is Pyre's ring **gradient-magnitude correction** (`PyreRenderer.cs:4697-4709`), which is the correct way to keep a rim a uniform screen thickness under a non-uniform map.

**12. Animatability is not uniform, and the non-animatable ones are non-animatable on purpose.** In Pyre, counts are plain ints (`starArms`, `polygonSides`, `fireballArms`) and so is `ringInner` — the comment at `Pyre.cs:391` says the ring's animation "lives in size/tilt/spin" and `PyreRenderer.cs:109` closes the field-id list against it. Only continuous shape dials get a `ZUIValue`. In Shaper, `ANIMATABLE_PARAMETERS` (`project_document.py:51-64`) exposes `shape.width`, `shape.height`, `shape.skew`, `shape.cornerRounding` and the three `shape.pulge.*` — and **notably NOT `shapeType`, and not a component's `mode`, `viscosity` or `sharpness`**. So in Shaper you cannot animate a boolean-op's softness at all.

**13. Shaper's per-layer `shape.*` envelope targets a shape one level deeper than you'd expect.** `compileShape` never reads a shape's own fields when it has any components, so a layer's `shape.*` envelope has to be written through `withLayerPrimitiveShape` into `components[0].shape` when the layer's shape is the common one-component wrap (`public/index.html:1630-1634`, `1745-1757`). Any port of Shaper's data model inherits this wrap-or-not ambiguity.

**14. Shaper's `triangle` is the odd one out in units.** Every other non-metric primitive multiplies by `unit = min(rx,ry)`; `triangle` does not (`public/index.html:1035`). If you unify the primitives under one scaling rule, the triangle's silhouette will not move but every downstream consumer of the magnitude (extrusion `inside`, `softAdd` band interaction, `edgeBand`) will change.

**15. Shaper's `capsule` ignores `cornerRounding` entirely** and `rect` caps rounding at `0.95 * min(rx,ry)` (`public/index.html:1038`, `1041`). So "rect at rounding 1" and "capsule" are *nearly* but not exactly the same shape, and no value of `cornerRounding` reaches the capsule.

**16. Pyre's `MinMax` mode draws once per particle, not per frame.** `Eval`'s `MinMax` branch seeds `System.Random` with `Hash(seed, particleIndex, fieldId, _layerSalt)` and draws once (`PyreRenderer.cs:5032-5036`) — it is **constant over the particle's life**, unlike `ZUIValue.EvaluateRaw`'s `MinMax`, which calls `UnityEngine.Random.Range` and re-rolls on every call (`ZUIValue.cs:113`). Calling the ZUI API instead of Pyre's `Eval` would make a dial flicker every frame *and* break determinism.

**17. Pyre's `Eval` silently ignores `ZUIValue.Mode.Oscillation`.** It falls to `default: return v.staticValue` (`PyreRenderer.cs:5050`). An author who sets a star dial to Oscillation gets a flat value with no warning. `Steps` had exactly this bug and was fixed (`PyreRenderer.cs:5041-5044`); Oscillation is the remaining one. Worth fixing rather than reproducing.

**18. Pyre's `Eval` deliberately ignores `duration`/`warmup`/`cooldown` on a Curve.** It samples `v.points` at `Clamp01(life)` directly (`PyreRenderer.cs:5036`). Calling `ZUIValue.Evaluate(life)` instead would divide by `duration` (default **4 s**) and sweep only the curve's **first quarter** — a silent, subtle wrongness. This is documented in the code and is easy to "helpfully" undo.

**19. Enum ordering in Pyre is append-only and load-bearing.** `ShapeForm`, `MatteRole`, `MatteChannel` and `ZUIValue.Mode` are all serialized as ints; every one carries an explicit APPEND-ONLY warning (`Pyre.cs:84-85`, `Pyre.cs:133-134`, `ZUIValue.cs:19`). Two `ShapeForm` slots are `[System.Obsolete]` retired-but-retained (`Inferno`, `ForkBlast`, `Pyre.cs:114-115`) precisely so the indices don't shift. Never reorder, never insert.

**20. Shaper's clamps are the *only* validation, and they normalise rather than reject.** `bounded_number(value, default, min, max)` silently coerces; an unknown `shapeType` becomes `"rect"` (`project_document.py:229`); an unknown component `mode` **disables the component** rather than erroring (`project_document.py:320-326`); a truncated subtree renders as absent rather than wrong (`public/index.html:1076-1080`). The stated principle — *"never draw geometry that is wrong"* — is a good one to carry over, but it means bad input fails **silently and visibly-differently**, not loudly.
