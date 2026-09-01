# Check B — does `pro` (Protrusion) actually produce relief?

Empirical check for T-0101. **Route 1: the real app, run in a real browser.** No reimplementation.

## 1. What I ran

**Route 1, headless Chromium via `playwright-core`.** Nothing was downloaded — `playwright-core@1.62.1` was already present at `C:\Users\Lauta\AppData\Local\Temp\pwtest\node_modules\playwright-core` and its Chromium build was already cached at `C:\Users\Lauta\AppData\Local\ms-playwright\chromium-1234\chrome-win64\chrome.exe`.

The app was loaded as-is from `file:///D:/CODEZ/AgentHQ/3D Shaper/.agenthq/attachments/T-0030/20260829T063646Z_retro_scifi_flyer_lab_draft6-1.html`. **Its own `raster` / `lightShade` / `shadowAt` / `draw` executed unmodified.** Nothing was stubbed or reimplemented.

Harness scripts (kept as a record of method): `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\checkB_harness.js` (pixel-view sweeps + depth test + strip images), `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\checkB_harness2.js` (height-view / depth-buffer sweeps + per-frame pixel deltas), `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\cmp.js` (pairwise image diffs).

Four things I did to the running app, all disclosed:

1. **Drove the real slider.** The value change was made by setting `#matPro`'s `.value` and dispatching a real `input` event — the app's own handler then ran. That handler (**line 17**) is `state.mats[state.activeMat][key]=+e.target.value` plus a label update, and nothing else, so this is byte-for-byte the state change a mouse drag causes.
2. **Set up the scene by assigning `state.layers` directly** (via the app's own `makeLayer()`), then called the app's `renderLayers()` / `syncMaterialUI()`. There is no slider path to author a specific `seq`, so this was necessary. The rasteriser was not touched.
3. **Froze the animation.** `window.requestAnimationFrame` was replaced with a no-op to halt the app's own `loop` (**line 54**), then `draw(0)` was called with a fixed `t`. Needed for determinism — `Plasma` and `Light` surfaces animate off `t` (**line 35**). Every frame in a sweep therefore differs *only* by `pro`.
4. **Set `bgMode='color'`** (`#101722`) instead of the default `transparent`, so the PNGs are legible. Background pixels are written before rasterising (**line 49**) and do not enter the height field.

Images are the app's own `#preview` canvas read back with `toDataURL`, 256x256. **No smoothing was introduced** — the app itself sets `ctx.imageSmoothingEnabled=false` at **line 12**, so the 64x64 grid is already nearest-neighbour upscaled 4x by the app's own `drawImage` at **line 53**.

**Scene.** One ellipse layer, `w=36 h=36 z=1`, `heightProfile:'round'`, `bodyH:4`, `edgeCoverage:0.30`, `mode:'stretch'`. All four materials pinned to `pro=0` except material 0, which is the swept one. Default lighting kept: `lightAngle=315`, `lightHeight=.7`, `ambient=.38`, `shadowLength=9`, `shadowStrength=.58` — a diagonal key light, so relief is legible.

- **Patterned set** — `seq` = the authored default `[0,0,1,1,2,2,3,1,0,0,1,2,3,2,1,1,0,0]`, `fill=1`.
- **Flat control** — `seq` = all-zeros (eighteen `0`s) **and** `fill=0`, so every cell in the shape is material 0 and therefore one identical colour. Any variation you see in the control is *purely* height-driven shading, not palette.
- **Depth-test set** — the patterned layer at `z=1` sitting on a larger flat base ellipse at `z=0` (material 1, `pro=0`).

### Output files (all under `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101\checkB\`)

| File | What it is |
|---|---|
| `checkB_protrusion_sweep.png` | patterned strip, all 7 values in order |
| `checkB_flat_control_sweep.png` | flat single-material control strip |
| `checkB_flat_HEIGHTVIEW_sweep.png` | the app's own Height view of the control — literal depth buffer |
| `checkB_patterned_HEIGHTVIEW_sweep.png` | Height view, patterned |
| `patterned_pro-8 / -4 / +0 / +4 / +10 / +18 / +28 .png` | 7 individual frames |
| `flat_pro*.png`, `flat_height_pro*.png` | control frames |
| `depthtest_pro-8 / +0 / +28 .png` | strip vs. neighbouring layer |

## 2. Does `pro` visibly change the picture?

**Yes, unambiguously — but only over roughly the first third of the slider's range.**

Mean absolute RGB delta against the `pro=0` frame, and the fraction of pixels that moved by more than 3/255:

```
patterned  meanAbsDelta   -8:1.10  -4:1.10  0:0.00  +4:1.07  +10:1.31  +18:1.29  +28:1.29
patterned  %px changed    -8:3.0%  -4:3.0%  0:0.0%  +4:3.7%  +10:5.1%  +18:5.1%  +28:5.1%
flat       meanAbsDelta   -8:2.89  -4:2.74  0:0.00  +4:2.48  +10:4.13  +18:4.01  +28:3.95
flat       %px changed    -8:6.5%  -4:6.0%  0:0.0%  +4:4.3%  +10:7.5%  +18:7.5%  +28:7.5%
```

It becomes obvious immediately — **at ±4 the shape already changes from a featureless disc to a ringed, shadowed one**. Look at `checkB_flat_control_sweep.png`: `pro=0` is a flat blank disc, `pro=+4` already has a rim, an inner-wall highlight and a cast shadow.

**But it saturates hard.** Pairwise diffs in the pixel view:

```
flat  +4 vs +10 : mean 2.035, max 70   <- large
flat +10 vs +18 : mean 0.206, max 24   <- near-identical
flat +18 vs +28 : mean 0.096, max 10   <- near-identical
pat  +10 vs +18 : mean 0.047, max 18
pat  +18 vs +28 : mean 0.022, max  8
```

Above about `+10` the rendered picture stops responding. See §6.

## 3. Does it read as *relief* — shading and shadow, not just flat colour?

**Yes, and the flat control proves it beyond argument.** In `checkB_flat_control_sweep.png` every cell in the shape is material 0, so `rgb(m.color)` is the same constant for all of them. The disc is therefore *guaranteed* to be one flat colour unless something height-driven modulates it. At `pro=0` it renders as exactly that: a blank, featureless disc. At every nonzero `pro` it renders as a cup/crater — raised rim, sunk core, a bright specular streak on the inner wall, and a hard-edged dark crescent cast across the core. That crescent is a cast shadow: it has a direction, it grows with `pro`, and it flips to the opposite side when `pro` goes negative.

The code path, with line references into `20260829T063646Z_retro_scifi_flyer_lab_draft6-1.html`:

**`pro` enters the height field / depth buffer — line 50** (in `raster`):

```js
const z=L.z+layerHeight(L,d)+state.mats[mi].pro*(usePattern?1:.25);
if(z>=hm[k]){hm[k]=z;mm[k]=mi}
```

`hm` is the single `Float32Array` that is both height map and depth buffer (allocated **line 49**, `hm.fill(-999)`).

**The surface normal is derived from `hm` by finite differences — line 52**:

```js
const m=state.mats[mi],h=hm[k],safe=v=>v<-900?h:v;
const nx=(safe(hm[yy*N+Math.max(0,xx-1)])-safe(hm[yy*N+Math.min(N-1,xx+1)]))*.65;
const ny=(safe(hm[Math.max(0,yy-1)*N+xx])-safe(hm[Math.min(N-1,yy+1)*N+xx]))*.65;
const nz=1.4+m.round*1.8;
let [R,G,B]=rgb(m.color),v=state.view==='pixel'?lightShade(nx,ny,nz,m,t,xx,yy):clamp(.2+(h+8)/44,.2,1.4);
v*=1-shadowAt(xx,yy,h,hm,N);
```

So `pro` → `hm` → `nx,ny` → `lightShade` → the pixel's brightness multiplier `v`. **That is the whole relief chain**, and it is real.

**Lambert term — line 35** (`lightShade`): normalises `(nx,ny,nz)`, dots it with the light vector built from `state.lightAngle` / `state.lightHeight`, `v=state.ambient+d*(1-state.ambient)`, plus a `Math.pow(d,8)*m.shine*.6` specular for `Metal` (material 0 is Metal — that is the bright inner-wall streak).

**Shadow ray-march — line 48** (`shadowAt`): steps along the light direction and compares stored heights against a rising ray, `const rayH=h+s*state.lightHeight; if(oh>rayH+.35) return state.shadowStrength*(1-.35*s/(state.shadowLength+1))`. A strip raised by `pro` is exactly what becomes `oh` and occludes its neighbours.

**Direct confirmation from the app's own Height view.** `checkB_flat_HEIGHTVIEW_sweep.png` renders `clamp(.2+(h+8)/44,.2,1.4)` — brightness *is* height. It shows a uniform disc at `pro=0`, a ring brighter than its core (rim above floor) for `pro>0` growing monotonically all the way to `+28`, and a ring darker than its core (moat below plateau) for `pro<0`. `pro` is moving the depth buffer, monotonically, across the whole slider range.

## 4. Is the "full weight when patterned, quarter weight in plain fill" rule real?

**Yes — it is literally in the source, and it is the reason the control image has a ring at all.**

The rule is the ternary at the end of **line 50**: `state.mats[mi].pro*(usePattern?1:.25)`, where `usePattern` is decided a few statements earlier on the same line:

```js
let mi=L.fill,usePattern=false;
if((L.patternLayout||'edge')==='stripes'){usePattern=true;mi=stripeMat(L,X,Y,rx,ry)}
else{usePattern=L.edgeCoverage>=1||depthInto<=L.edgeCoverage;
     if(usePattern){let a=Math.atan2(Y/ry,X/rx);if(a<0)a+=Math.PI*2;mi=patternMat(L,a/(Math.PI*2))}}
```

**And the control makes the consequence visible.** In `checkB_flat_control_sweep.png` the band and the core hold the *same material*, so both get the same `pro` value. If the weighting were uniform, that `pro` would be a constant offset over the whole shape and the disc would stay perfectly flat at every value. It does not — a ring appears the instant `pro` leaves zero. The only thing that can produce a step inside a single-material shape is the `1` vs `.25` split. The step height is `0.75 * pro`, which is why the ring gets visibly taller/deeper with magnitude in the height view.

Contrast with `checkB_protrusion_sweep.png`, where the patterned layer has a different fill material: there the moving parts are only the two arcs whose `seq` entries name material 0, so you get differential relief *within* the border strip rather than one uniform lifted band.

Worth stressing: **`.25` is not zero.** A fill material with a large `pro` still lifts the whole layer body by a quarter of it.

## 5. Does negative `pro` read as sunk/engraved?

**Yes when the layer stands alone; no when another layer is underneath — there it reads as deleted.**

*Standing alone* (`flat_pro-4.png`, `flat_pro-8.png`, and the height view): genuinely engraved. The band drops below the core, so the core becomes a raised plateau, the band a moat, and the cast shadow flips to the opposite side of the shape versus positive `pro`. Correct, readable intaglio.

*With a neighbour* (`depthtest_pro-8.png`, patterned layer at `z=1` over a base at `z=0`): the material-0 arcs **vanish entirely**. They lose the `if(z>=hm[k])` test on **line 50** to the base layer and are overwritten by the base layer's material, so you see the base's colour rather than a sunk groove of the strip's own material. There is no depth-clipping or intersection shading — the loser is simply not written.

**The positive direction of the depth-test claim also confirms.** `depthtest_pro+0.png` shows the arcs dim and flush; `depthtest_pro+28.png` shows them bright, proud and shaded, clearly winning against the base layer.

## 6. What the prior source-only reading got wrong

**Nothing it asserted was false.** Every specific claim checks out against the running app: `seq` default and its location (line 13), the material struct (line 14), the `-8`..`+28` slider range (line 7, `<input id="matPro" type="range" min="-8" max="28" step="1">`), the `raster` height formula (line 50), `lightShade` (line 35), `shadowAt` (line 48), the shared height/depth buffer, and both directions of the depth-test consequence.

What it **missed**, all of which only shows up by running it:

1. **The visual effect saturates around `+10`. Roughly half the positive slider range is dead.** `+10`, `+18` and `+28` are near-indistinguishable in the normal Pixel view (mean pairwise deltas `0.21` and `0.10` out of 255). Two mechanisms cause it, both on line 52 / line 48: shading uses the height *gradient*, and after the `Math.hypot` normalisation in `lightShade` (line 35) a steeper step converges to a horizontal normal while `clamp(...,0,1)` pins the diffuse term; meanwhile `shadowAt` returns `state.shadowStrength*(1-.35*s/(...))` — a function of *distance*, never of how much taller the occluder is — so once the strip occludes, raising it further adds nothing. The **height view keeps changing** across the whole range (mean delta vs `pro=0` climbing `2.52 → 6.04 → 9.58 → 13.99` at `+4/+10/+18/+28`), which is precisely how you can tell the height field is still moving while the lit image has stopped.
2. **`pro` is per-material, not per-strip.** It only lifts the arcs whose `seq` entries name that material, so raising one material carves differential relief *inside* the border strip. It does not lift the strip as a unit. Getting the "whole band raised" reading described in the prior summary requires every material in the `seq` to share a `pro`.
3. **Negative `pro` against a neighbouring layer is deletion, not engraving** (see §5). The prior reading treated "raised wins the depth test" and "negative reads as engraved" as independent claims; they are the same mechanism, and the second is destroyed by the first whenever a layer sits underneath.
4. **`.25` is not "mostly ignored".** A quarter weight over a `-8..+28` range is a `-2..+7` unit shift of the entire layer body — comparable to the `bodyH` values in the shipped defaults (`5`, `12`, `5` on line 14). Fill-material `pro` meaningfully reorders layers by itself.
