# T-0109 — Reference height maths, transcribed

Read-only transcription of the extrusion / bevel / Z maths from the three sources named in the brief. Every claim carries a `file:line`. Where I could not find something I say NOT FOUND and where I looked. Nothing outside this file was modified.

**Source shorthand used throughout**
- `SHAPER` = `D:\CODEZ\AgentHQ\3D Shaper\public\index.html` (the "3D Shaper" web app), 4102 lines.
- `SCHEMA` = `D:\CODEZ\AgentHQ\3D Shaper\project_document.py`.
- `TOY` = `D:\CODEZ\AgentHQ\3D Shaper\.agenthq\attachments\T-0030\20260829T063646Z_retro_scifi_flyer_lab_draft6-1.html` (the ancestor, 55 physical lines, near-minified).
- `PYRE` = `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Runtime\Pyre\…`.

---

## 0. The one number everything is a function of

`SHAPER:1423` — `const inside=clamp01(-distance/span), nx=clampTo(lx/halfW,-1,1), ny=clampTo(ly/halfH,-1,1);`

The reference calls it **`inside`**. It is the shape's own signed distance, negated (so positive inside the silhouette), divided by `span`, clamped to `[0,1]`. Throughout this report I write it as **`t`**.

- `span` is defined at `SHAPER:1339` — `const halfW=Math.max(1,compiled.halfW), halfH=Math.max(1,compiled.halfH), span=Math.max(1,Math.min(halfW,halfH));` — i.e. `span = max(1, min(halfW, halfH))`, the SHORTER half-extent of the shape's bounding box, in canvas units. `distance` comes from `evalShape` (`SHAPER:1420`) and is in canvas units for every primitive (see §H1).
- `t = 0` at the silhouette boundary; `t = 1` at `span` canvas units in from the boundary; the `clamp01` saturates everything deeper than `span`.
- The brief cites `inside` at "~:1339, :1425". Correction: `span` is at `:1339` (correct), but the `inside` assignment is at **`:1423`**, not `:1425`. The consuming line `let z=base+…` is `:1424`.

The composed height expression, verbatim, `SHAPER:1424`:

```js
let z=base+extrusionHeight(extrusion.type,extrusion.params,body,inside,nx,ny)*bevelFactor(bevel.type,bevel.params,inside);
```

So `z(t) = base + body·E(t, nx, ny) · B(t)` where `E` is normalised to a `body` multiplier (§A) and `B` is a pure multiplier in `[0,1]` (§B).

- `body` and `base`, `SHAPER:1362` — `body: Math.max(0,Number(layer.depth)||0)/cellSize, base: order*0.75,` — `body` is the layer's authored `depth` converted from canvas units into GRID CELLS (`cellSize=(cellW+cellH)/2`, `SHAPER:1375`); `base` is the layer's Z, derived from list order alone.

---

## A. THE EXTRUSION PROFILE SET (7)

Authored enum, `SCHEMA:28`:

```python
EXTRUSION_TECHNIQUES = ("flat", "linear", "stepped", "dome", "round", "taper", "pyramid")
```

UI labels, `SHAPER:3776`: `flat` "Flat (full-depth plateau)", `linear` "Linear (tilted constant depth)", `stepped` "Stepped (terraces)", `dome` "Dome (spherical)", `round` "Round (soft sqrt falloff)", `taper` "Taper (frustum)", `pyramid` "Pyramid (falls to a point)".

Renderer registry: `SHAPER:1143-1155` (`function extrusionHeight(technique, params, body, inside, nx, ny)`). Guard at `SHAPER:1144`: `if (body<=0) return 0;`. Parameter defaults and authored ranges: `SCHEMA:245-257` and `SCHEMA:60`; UI rows `SHAPER:3806-3810`.

Notation below: `t = inside ∈ [0,1]`; `E(t)` is the returned height DIVIDED by `body` (so `h = body·E`). `E(0)`/`E(1)` are stated for the profile's own default parameter.

| # | name | line | formula (h/body) | extra inputs | E(0) | E(1) |
|---|---|---|---|---|---|---|
| 1 | `flat` | `SHAPER:1154` (`default: return body;`) | `E(t) = 1` | none | 1 | 1 |
| 2 | `linear` | `SHAPER:1148` | `E(nx,ny) = max(0, 1 + 0.6·(cos θ · nx − sin θ · ny))`, `θ = angle·π/180` | **normalised local coords** `nx, ny` | n/a (t-independent) | n/a |
| 3 | `stepped` | `SHAPER:1149` | `E(t) = min(1, ⌊t·n⌋ / (n−1))`, `n = max(2, round(steps) ‖ 4)` | `steps` | 0 | 1 |
| 4 | `dome` | `SHAPER:1150` | `E(t) = (√(max(0, 1 − (1−t)²)))^{1/c} = (t(2−t))^{1/(2c)}`, `c = max(0.2, Number(curve) ‖ 1)` | `curve` | 0 | 1 |
| 5 | `round` | `SHAPER:1151` | `E(t) = t^{0.5/c}`, `c = max(0.2, Number(curve) ‖ 1)` | `curve` | 0 | 1 |
| 6 | `taper` | `SHAPER:1152` | `E(t) = min(1, t / max(0.05, 0.6·τ))`, `τ = clamp01(taper)` (default 1) | `taper` | 0 | 1 |
| 7 | `pyramid` | `SHAPER:1153` | `E(t) = 1 − τ + τ·t`, `τ = clamp01(taper)` (default 1) | `taper` | `1 − τ` (0 at τ=1) | 1 |

Verbatim source, `SHAPER:1143-1156`:

```js
function extrusionHeight(technique, params, body, inside, nx, ny) {
  if (body<=0) return 0;
  const settings=params||{};
  switch (technique) {
    case 'linear': { const angle=(settings.angle!==undefined?settings.angle:45)*Math.PI/180; return Math.max(0, body*(1+0.6*(Math.cos(angle)*nx-Math.sin(angle)*ny))); }
    case 'stepped': { const steps=Math.max(2,Math.round(settings.steps)||4); return body*Math.min(1,Math.floor(inside*steps)/(steps-1)); }
    case 'dome': { const curve=Math.max(0.2,Number(settings.curve)||1); return body*Math.pow(Math.sqrt(Math.max(0,1-(1-inside)*(1-inside))),1/curve); }
    case 'round': { const curve=Math.max(0.2,Number(settings.curve)||1); return body*Math.pow(inside,0.5/curve); }
    case 'taper': { const taper=settings.taper!==undefined?clamp01(settings.taper):1; return body*Math.min(1, inside/Math.max(0.05,taper*0.6)); }
    case 'pyramid': { const taper=settings.taper!==undefined?clamp01(settings.taper):1; return body*(1-taper+taper*inside); }
    default: return body;
  }
}
```

**The one profile that uses normalised LOCAL COORDINATES is `linear`** (`SHAPER:1148`), and it is the only profile that does NOT read `inside` at all. Its two extra inputs are `nx = clampTo(lx/halfW, −1, 1)` and `ny = clampTo(ly/halfH, −1, 1)` (`SHAPER:1423`), i.e. the cell's position in the layer's own local frame normalised to `[−1,1]²` by the half-extents `halfW`/`halfH` (NOT by `span`). It tilts a constant-thickness slab: a plane in `(nx, ny)` of gradient magnitude `0.6` rotated by `angle`.

Authored parameter ranges (`SCHEMA:60`, mirrored `SHAPER:1593`):

- `extrusion.params.angle`: `[-180, 180]`, default `45` (`SCHEMA:251`)
- `extrusion.params.steps`: `[2, 32]` integer, default `4` (`SCHEMA:252`)
- `extrusion.params.curve`: `[0.2, 4]`, default `1` (`SCHEMA:253`)
- `extrusion.params.taper`: `[0, 1]`, default `1` (`SCHEMA:254`)

---

## B. THE BEVEL SET — six authored entries, five non-identity

Authored enum, `SCHEMA:29`:

```python
BEVEL_TECHNIQUES = ("none", "linear", "rounded", "cove", "ogee", "stepped")
```

UI labels, `SHAPER:3777`: `none` "None (sharp edge)", `linear` "Linear (chamfer)", `rounded` "Rounded (fillet)", `cove` "Cove (concave fillet)", `ogee` "Ogee (S-curve)", `stepped` "Stepped (micro-terraces)".

**Reconciliation of "five bevels" vs "six".** Both are right about different things and the flat entry is exactly the explanation the brief guessed. The authored list has **six** entries; **`none` is the identity** — `bevelFactor` returns `1` for it before doing anything (`SHAPER:1161`), it takes no parameters (`SCHEMA:268` falls to `params = {}`), and `SHAPER:2751` excludes it from the list of techniques that even show the `amount` slider (`['linear','rounded','cove','ogee','stepped']`). So there are **five bevel FUNCTIONS plus one "off"**. Count the shapes → five; count the enum → six. Report both; do not drop `none`, it is a real serialised value and the layer default (`SHAPER:2428` creates layers with `bevel:{type:'none', params:{}}`).

### The band parameter

`SHAPER:1159-1173`:

```js
function bevelFactor(technique, params, inside) {
  const settings=params||{}, amount=settings.amount!==undefined?clamp01(settings.amount):0;
  if (!technique || technique==='none' || amount<=0 || inside>=amount) return 1;
  const u=clamp01(inside/amount);
  switch (technique) {
    case 'linear': return u;
    case 'rounded': return Math.sqrt(Math.max(0,1-(1-u)*(1-u)));
    case 'cove': return 1-Math.sqrt(Math.max(0,1-u*u));
    case 'ogee': return u<0.5 ? 0.5*(1-Math.sqrt(Math.max(0,1-4*u*u))) : 0.5+0.5*Math.sqrt(Math.max(0,1-4*(1-u)*(1-u)));
    case 'stepped': { const steps=Math.max(2,Math.round(settings.steps)||3); return Math.min(1,(Math.floor(u*steps)+1)/steps); }
    default: return 1;
  }
}
```

**Band width definition:** `a = clamp01(amount)` is a fraction OF `inside`, i.e. of `span`. The band is `inside ∈ [0, a)`, which is `a·span` canvas units wide measured inward from the silhouette. Outside the band (`inside ≥ a`) the factor is exactly `1` — the early-out at `SHAPER:1161`. The band parameter is `u = clamp01(inside/a) = t/a ∈ [0,1)`. UI title, `SHAPER:3813`: "How wide a band next to the silhouette the bevel shapes". Note the band is defined against `span` (the SHORTER half-extent), so on an elongated silhouette the band is not a constant-width rim in the long axis — it is a constant fraction of the short half-extent.

| # | name | line | formula `B(u)` | params | B(0) | B(u→1⁻) |
|---|---|---|---|---|---|---|
| 0 | `none` | `SHAPER:1161` | `B ≡ 1` | — | 1 | 1 |
| 1 | `linear` | `SHAPER:1163` | `B(u) = u` | `amount` | 0 | 1 |
| 2 | `rounded` | `SHAPER:1164` | `B(u) = √(max(0, 1 − (1−u)²)) = √(u(2−u))` | `amount` | 0 | 1 |
| 3 | `cove` | `SHAPER:1165` | `B(u) = 1 − √(max(0, 1 − u²))` | `amount` | 0 | 1 |
| 4 | `ogee` | `SHAPER:1166` | `B(u) = ½(1 − √(1 − 4u²))` for `u < ½`; `½ + ½√(1 − 4(1−u)²)` for `u ≥ ½` | `amount` | 0 | 1 |
| 5 | `stepped` | `SHAPER:1167` | `B(u) = min(1, (⌊u·m⌋ + 1)/m)`, `m = max(2, round(steps) ‖ 3)` | `amount`, `steps` | **1/m** | 1 |

All five non-identity bevels are **continuous at the band edge** (`B → 1` as `u → 1⁻`), so there is no seam at `inside = a` — verified numerically. `stepped` is the only one that does **not** reach 0 at the silhouette: it starts at `1/m` (default `m=3` → `1/3`), a hard step of `1/m` at the outline itself.

Authored parameter ranges (`SCHEMA:61`, `SCHEMA:266-267`, `SHAPER:1594`):

- `bevel.params.amount`: `[0, 1]`, default `0.25`, UI step `0.01` (`SHAPER:3813`)
- `bevel.params.steps`: `[2, 16]` integer, default `3`, `stepped` only (`SHAPER:2754-2755`)

---

## C. THE SLOPE FIGURES — independently re-derived

All figures below are `sup |dE/dt|` for the NORMALISED profile (`h/body`). Derived analytically; each was cross-checked by a fine central-difference sweep over `t ∈ (0,1)` at `h = 1e-7`, 2×10⁶ samples.

### C.1 pyramid — quoted 1.00 — **AGREES**

`E(t) = 1 − τ + τt` ⟹ `dE/dt = τ`, constant. `τ = clamp01(taper) ∈ [0,1]` ⟹ `sup|E'| = τ`, maximised at `τ = 1` giving **1.00**. Numeric sweep: `1.0000`. ✅ Exact. This is the only profile whose bound is tight, exact and independent of any clamp.

### C.2 taper — quoted 1.67 at τ=1 and 16.7 at τ=0.1 — **AGREES at those two points, but the stated worst case is WRONG for the authored range**

`E(t) = min(1, t/T)` with `T = max(0.05, 0.6τ)`. Piecewise linear: slope `1/T` on `t < T`, `0` after.

**Closed form: `L_taper(τ) = 1 / max(0.05, 0.6·τ)`.**

- `τ = 1` → `1/0.6 = 1.6667` — quoted 1.67 ✅ (numeric `1.6667`)
- `τ = 0.1` → `0.6·0.1 = 0.06 > 0.05` → `1/0.06 = 16.667` — quoted 16.7 ✅ (numeric `16.6667`)
- **`τ ≤ 0.08333`** (i.e. `0.6τ ≤ 0.05`) → `T` clamps at `0.05` → **`L = 20` exactly**, and stays 20 all the way to `τ = 0`. Numeric at `τ=0`: `20.0000`.

**Range over the authored parameter range `τ ∈ [0,1]` (`SCHEMA:254`): `L_taper ∈ [1.6667, 20]`.** The task quotes 16.7 as the taper family's low-`τ` figure; the true worst case at the bottom of the authored range is **20**, 20% higher. `0.1` is not the minimum authorable taper — `SCHEMA:60` allows `0`, and the UI slider `SHAPER:3810` has `step:0.01` down to the same `0`. A bound declared as 16.7 would be violated by any `τ < 1/12`.

### C.3 round — quoted 16.4 — **DISAGREES: formally UNBOUNDED at the default parameter**

`E(t) = t^p` with `p = 0.5/c`, `c = max(0.2, curve ‖ 1) ∈ [0.2, 4]` ⟹ `p ∈ [0.125, 2.5]`.

`dE/dt = p·t^{p−1}`.

- If `p < 1` (⟺ `c > 0.5`): `t^{p−1} → ∞` as `t → 0⁺`. **UNBOUNDED at the silhouette edge.** The DEFAULT `c = 1` gives `p = 0.5`, `E = √t`, `E' = 0.5/√t → ∞`. Numeric sweep at `c=1` returns `709.98` at `t = 5.0e-7` and keeps climbing as the sweep is refined — the signature of a divergence, not a maximum.
- If `p = 1` (`c = 0.5`): `E' ≡ 1`, bounded, `L = 1`.
- If `p > 1` (⟺ `c < 0.5`, i.e. `c ∈ [0.2, 0.5)`): `E'` is increasing, `sup = p` attained at `t = 1`. **`L_round(c) = 0.5/c ∈ (1, 2.5]`.** Numeric at `c = 0.2`: `2.5000 @ t = 1.0` ✅.

**So `round` is bounded ONLY on `c ∈ [0.2, 0.5]`, where `L = 0.5/c ≤ 2.5`, and unbounded for every `c > 0.5` — which includes the default and 7/8 of the authored range.** 16.4 is not a bound.

### C.4 dome — quoted 23.1 — **DISAGREES: formally UNBOUNDED at the default parameter**

`E(t) = (t(2−t))^m` with `m = 1/(2c)`, `c ∈ [0.2, 4]` ⟹ `m ∈ [0.125, 2.5]`.

`dE/dt = 2m(1−t)·(t(2−t))^{m−1}`.

- As `t → 0⁺`, `(t(2−t))^{m−1} ≈ (2t)^{m−1}` → diverges iff `m < 1` ⟺ `c > 0.5`. **UNBOUNDED at the silhouette edge for `c > 0.5`, which includes the default `c = 1`** (`E' = (1−t)/√(t(2−t)) → ∞`). Numeric at `c=1`: `1004.07 @ t = 5.0e-7`, again a divergence.
- For `m ≥ 1` (`c ≤ 0.5`) it is bounded. Setting `d/dt ln E' = 0` gives `2(m−1)(1−t)² = t(2−t)`, i.e. `(1−t)² = 1/(2m−1)`, so the interior maximum is at

  **`t* = 1 − 1/√(2m−1)`, and `L_dome(m) = 2m · (1/√(2m−1)) · (1 − 1/(2m−1))^{m−1}`** (for `m > 1`; for `m = 1` the max is at `t = 0` with `L = 2`).

  - `c = 0.5` (`m = 1`): `E' = 2(1−t)`, `L = 2` at `t = 0`. Numeric: `2.0000` ✅
  - `c = 0.2` (`m = 2.5`): `t* = 1 − 1/√4 = 0.5`, `L = 5 · 0.5 · 0.75^{1.5} = 1.6238`. Numeric: `1.6238 @ t = 0.5` ✅ (closed form confirmed)

**So `dome` is bounded ONLY on `c ∈ [0.2, 0.5]`, where `L ≤ 2`, and unbounded for every `c > 0.5`.** 23.1 is not a bound.

### C.5 Where the quoted 16.4 / 23.1 actually come from — a sampling artefact, and they prove it

Neither number is a maximum; both are the analytic derivative evaluated at **one and the same small `t`**:

- `round`, `c=1`: `E'(t) = 0.5/√t`. `E'(9.29e-4) = 16.404` → matches the quoted 16.4.
- `dome`, `c=1`: `E'(t) = (1−t)/√(t(2−t))`. `E'(9.29e-4) = 23.183` → matches the quoted 23.1.

Confirmation independent of guessing `t`: for small `t` the ratio `dome'/round' → (1/√(2t)) / (0.5/√t) = √2 = 1.41421`. The quoted ratio is `23.1/16.4 = 1.4085` — agreement to 0.4%, which is what you get from two readings on one grid, not from two independent maxima of two different functions. The measurement was almost certainly a finite-difference sweep whose innermost sample sat at `t ≈ 9.3e-4 ≈ 1/1075`; halve that step and both numbers rise by √2. **Treat 16.4 and 23.1 as "what a 1000-ish-sample sweep happened to see", not as bounds. They are not reproducible and a build agent that hard-codes them will ship a wrong Lipschitz constant.**

### C.6 The remaining two extrusion profiles

- **`flat`**: `E ≡ 1`, `L = 0`. It is the exact identity in `t`; a resolve can early-out on it entirely.
- **`linear`**: `dE/dt = 0` exactly — it never reads `inside`. Its slope lives in the LOCAL coordinates: `∂E/∂nx = 0.6·cos θ`, `∂E/∂ny = −0.6·sin θ`, so `|∇_{(nx,ny)}E| = 0.6` — **constant 0.6 for every angle**. Converted to canvas units: `∂h/∂lx = body·0.6·cos θ / halfW`, `∂h/∂ly = −body·0.6·sin θ / halfH`. It therefore does NOT compose with the shape's `inside`-bound at all and needs its own rule (§H4).
- **`stepped`**: see §D — no finite bound.

### C.7 Bevels — `sup |dB/du|` and `sup |dB/dt|`

Because `u = t/a`, `dB/dt = (1/a)·dB/du`. Every non-identity bevel therefore carries a `1/a` amplifier on top of its own shape.

| bevel | `dB/du` | `sup|dB/du|` | `sup|dB/dt|` | where |
|---|---|---|---|---|
| `none` | 0 | 0 | 0 | — |
| `linear` | `1` | **1** (exact, tight) | **`1/a`** | everywhere |
| `rounded` | `(1−u)/√(u(2−u))` | **∞** | ∞ | `u → 0⁺` (the silhouette) |
| `cove` | `u/√(1−u²)` | **∞** | ∞ | `u → 1⁻` (the INNER edge of the band) |
| `ogee` | `2u/√(1−4u²)` for `u<½`; `2(1−u)/√(1−4(1−u)²)` for `u>½` | **∞** | ∞ | `u → ½` (the inflection, mid-band) |
| `stepped` | 0 a.e. + `m−1` jumps of `1/m` | **∞** (no bound, §D) | ∞ | `u = k/m`, `k=1…m−1` |

Numeric sweep (`a = 1`) returned `linear 1.0000`, `rounded 1004.07 @ u=5.0e-7`, `cove 1004.07 @ u→1`, `ogee 3162.28 @ u=0.5` — the last three all diverging under refinement, confirming the analysis.

**Four of the five bevels are unbounded, and the fifth is bounded only in `u`.** For `linear`, `sup|dB/dt| = 1/a` and `a` may be as small as the UI's `0.01` step (`SHAPER:3813`), giving **100**; the limit as `a → 0⁺` is `∞`, but `a = 0` exactly is a hard early-out to the identity (`SHAPER:1161`), so the function is discontinuous in `a` at 0. Any declared bound must be a function of `a`, not a constant.

### C.8 The composed bound — what actually has to be declared

`h(t) = body · E(t) · B(t/a)`, so on the band `t < a`:

`|dh/dt| ≤ body · ( L_E · sup|B| + sup|E| · L_B ) = body · ( L_E · 1 + sup|E| · L_B/a )`

and off the band (`t ≥ a`) simply `|dh/dt| ≤ body · L_E`. Note `sup|E| = 1` for every profile EXCEPT `linear`, where `sup|E| = 1 + 0.6(|cos θ| + |sin θ|) ≤ 1 + 0.6√2 = 1.8485` (§H4).

And the conversion any ray-marcher actually needs, from `t` to canvas units: `dt/d(distance) = 1/span`, so

**`|dh/d(canvas unit)| ≤ (body / span) · L_composed`**, with `body = max(0, layer.depth)/cellSize` (`SHAPER:1362`) and `layer.depth ∈ [0, 2048]` (`SCHEMA:57`).

That `(body/span)` factor is a per-layer scalar that can be enormous — a 2048-unit-deep layer on a small silhouette. **The profile figures in §C are per-unit-body, per-unit-span. They are NOT the numbers to compare against a primitive's "worst 3.1"; that comparison is only meaningful after the `body/span` scaling.** The task's framing ("profiles over-report far more badly than any primitive does") is directionally right but the quoted magnitudes are the wrong quantity twice over — wrong because unbounded (§C.3, §C.4) and wrong because unscaled.

---

## D. THE STEPPED PROFILE

It appears twice, with **different formulas, different default step counts and different authored ranges**. They are not the same function and must not be shared.

**Extrusion `stepped`** — `SHAPER:1149`:

```js
case 'stepped': { const steps=Math.max(2,Math.round(settings.steps)||4); return body*Math.min(1,Math.floor(inside*steps)/(steps-1)); }
```

`E(t) = min(1, ⌊t·n⌋/(n−1))`, `n = max(2, round(steps) ‖ 4)`. Authored `steps ∈ [2, 32]` integer, default `4` (`SCHEMA:252`, `SHAPER:1593`, `SHAPER:3808`). `E(0) = 0`; `E(1) = 1`. Jump discontinuities of `1/(n−1)` at `t = k/n` for `k = 1…n−1`; the `min(1, ·)` clamp means the topmost tread `t ∈ [(n−1)/n, 1]` is flat at 1 and the final riser at `t = (n−1)/n` is the last one.

**Bevel `stepped`** — `SHAPER:1167`:

```js
case 'stepped': { const steps=Math.max(2,Math.round(settings.steps)||3); return Math.min(1,(Math.floor(u*steps)+1)/steps); }
```

`B(u) = min(1, (⌊u·m⌋+1)/m)`, `m = max(2, round(steps) ‖ 3)`. Authored `steps ∈ [2, 16]` integer, default `3` (`SCHEMA:266`, `SHAPER:1594`, `SHAPER:3814`). `B(0) = 1/m` (NOT 0); `B(1⁻) = 1`. Jumps of `1/m` at `u = k/m` for `k = 1…m−1`, plus the `1/m` offset at the outline itself.

**No finite Lipschitz bound exists — CONFIRMED, not refuted.** `⌊·⌋` is discontinuous. Formally: for any candidate `L`, take `t₁ = k/n − ε` and `t₂ = k/n`; `|E(t₂) − E(t₁)| = 1/(n−1)` while `|t₂ − t₁| = ε`, so the difference quotient is `1/((n−1)ε) → ∞` as `ε → 0`. The same argument with `1/m` applies to the bevel. This is a genuine discontinuity, not a steep-but-finite slope: no refinement of a measurement grid will ever converge, and there is no constant to hunt for. Any general path that assumes local Lipschitz continuity is not merely inaccurate on `stepped` — it is unsound on it.

### D.1 A slope-clamped variant (concrete formula — an OPTION, not a decision)

Add one riser-width parameter `w ∈ (0, 1]` (fraction of a tread consumed by the rise). Write `s = t·n`, `k = ⌊s⌋`, `f = s − k`, and `sat(x) = clamp01(x)`.

**Extrusion:**

> `E_w(t) = min(1, ( k + sat( (f − (1 − w)) / w ) ) / (n − 1) )`

Each tread is flat for `f ∈ [0, 1−w)` and rises linearly over `f ∈ [1−w, 1]`. It is continuous (at `f → 1⁻` the value is `(k+1)/(n−1)`; at the next tread's `f = 0` it is `(k+1)/(n−1)`), it reproduces the original exactly in the limit `w → 0⁺`, and at `w = 1` it degenerates to the straight ramp `min(1, t·n/(n−1))`.

> **`L = n / ((n−1)·w)`** — exact and tight. At the default `n = 4`, `w = 0.25`: `L = 4/(3·0.25) = 5.333`. At the authored worst case `n = 32`, `w → 0`: `L → ∞`, so `w` needs a floor if a single declared constant is wanted (e.g. `w ≥ 0.1` ⟹ `L ≤ 10.32` for all `n ∈ [2,32]`, since `n/(n−1) ≤ 2`).

**Bevel** (same shape, note the `+1` offset and the `/m`):

> `B_w(u) = min(1, ( 1 + ⌊um⌋ + sat( ((um − ⌊um⌋) − (1 − w)) / w ) ) / m )`, with **`L = 1/w` in `u`, hence `1/(a·w)` in `t`.**

Both keep the terraced READ (flat treads dominate the tread width for small `w`) while being finitely Lipschitz. Cost: one new authored dial per stepped technique, and old assets change appearance unless `w` defaults small.

### D.2 What "march slab by slab between the steps" would require (an OPTION)

The observation that makes this cheap: **inside a slab the height is exactly constant**, so the profile contributes ZERO slope there and the marcher only needs the SHAPE's own bound. All the difficulty is concentrated in `n−1` explicit surfaces. Concretely a marcher would need:

1. **The profile to publish its breakpoint set**, not just a constant: `{ t_k = k/n : k = 1…n−1 }` for the extrusion, `{ t_k = a·k/m : k = 1…m−1 }` for the bevel, and the height value on each tread. This is a new item on the profile interface (a `Breakpoints()` alongside `SlopeBound()`), and it is what makes `stepped` expressible at all.
2. **The ability to intersect a ray with the shape at a given `inside` value.** `inside = t_k` is the level set `distance = −t_k·span`, i.e. the shape's OWN SDF offset by a constant. Sphere-tracing an offset SDF is sphere-tracing the same field with the same (finite) primitive Lipschitz bound from T-0105 — no new machinery, and crucially no profile bound is needed.
3. **Per slab, a plane clip.** Within slab `k`, the solid is `{ inside ∈ [t_k, t_{k+1}) } ∩ { z ≤ base + body·E_k }`. Two ray-plane intersections (constant `z`) plus the two offset-SDF traces bracket it exactly.
4. **Cost:** `O(n)` sphere-traces per ray in the worst case, `n ≤ 32` for the extrusion and `m ≤ 16` for the bevel, and `n·m` if BOTH are stepped on the same layer (the breakpoint sets interleave — up to 46 slabs). That product case is the one to size for, and it is the argument for capping the combination rather than the individual counts.
5. It also **fixes the bevel/extrusion interaction generally**, because the same slab decomposition works for a smooth profile with a stepped bevel and vice versa.

### D.3 The third option the task names

An **explicitly written-down exclusion**: `stepped` is authorable only where the general path is not used (the existing screen-aligned height-field resolve), and is refused — visibly, with a warning, the way `SCHEMA:256`/`:269` already refuse an unknown technique — on any path that steps along a ray. This is the cheapest and the only one that ships without new maths. Recording it counts as satisfying "do not leave a build agent hunting for a constant that does not exist".

**Not my call.** Stated as three options with their costs; the decision is T4's.

---

## E. Z AND DEPTH

### E.i "Depth" is extrusion THICKNESS, not position — CONFIRMED

The proving line is the UI row that authors it, `SHAPER:3805`:

```js
const depth = rangeRow({label:'Depth', field:'data-layer-field', path:'depth', kind:'layer', anim:'layer', animKey:'depth', step:1, default:0, title:'How far this layer extrudes from its face, in canvas units'})
```

— "**How far this layer extrudes from its face, in canvas units**". Three further independent confirmations:

- `SHAPER:1362` — `body: Math.max(0,Number(layer.depth)||0)/cellSize` — `layer.depth` becomes `body`, the multiplier every extrusion profile scales (`SHAPER:1143`, `body` is the second argument and the returned magnitude), and it is `max(0, …)`: a thickness cannot be negative, a position could be.
- `SCHEMA:57` — `"depth": (0.0, 2048.0)` — the serialised range is non-negative, same argument.
- `SHAPER:1144` — `if (body<=0) return 0;` — depth 0 means *no extrusion*, a zero-thickness sheet, not "at Z = 0".

Note the naming collision with the ancestor, which is the opposite way round (§H5): in `TOY:26` the slider **labelled "Depth" IS the Z position** (`rangeRow('Depth','z',-8,32,1,L.z)`), and the thickness is a separate slider labelled "Shape height" (`rangeRow('Shape height','bodyH',0,40,1,L.bodyH,'px')`). Anyone porting from the ancestor will get this backwards unless told.

### E.ii A layer's Z position today — CONFIRMED as `order * 0.75`

`SHAPER:1362`, the same expression, second field:

```js
body: Math.max(0,Number(layer.depth)||0)/cellSize, base: order*0.75,
```

`order` is the layer's index in the model's layer list, passed in at `SHAPER:1389` (`layers.map((layer,order)=>compileLayer(…,order,…))`). There is no other contribution to `base`; grep for `base` in the renderer finds it only here and at its single use site, `SHAPER:1424`. So **the only way to move a layer in Z is to reorder the list, in fixed 0.75-cell steps**, and two layers can never share a Z. This confirms `P3-shaper-internals.md:138`. The units are GRID CELLS, the same units as `body` (which was divided by `cellSize` on the same line) — so `0.75` is three quarters of a cell, and it interacts with `FUSION_CELLS` (see the gotcha in §H3).

### E.iii Where a Z offset goes, and the height field IS the depth buffer

**The buffer.** `SHAPER:1391`:

```js
const heights=new Float32Array(total).fill(-9999), belowHeights=new Float32Array(total).fill(-9999);
```

One `Float32` per grid cell, initialised to a sentinel `-9999`; `-900` is the "nothing here yet" test threshold used everywhere (`SHAPER:1438`, `:1444`, `:1467`). There is no second buffer. The same array is read by the normal finite-difference (`SHAPER:1467`, `:1491`), by the shadow march (`SHAPER:1469`) and by the height view. **`heights` is simultaneously the depth buffer, the surface geometry, and the input to lighting** — which is exactly why "the height field already IS the depth buffer" (design B7) is literally true here and no new buffer is needed for a Z offset.

**The z-buffer compare**, `SHAPER:1436-1447`, verbatim:

```js
const key=gy*gridW+gx, previous=heights[key];
if (z>previous) {
  if (previous>-900) { belowHeights[key]=previous; belowMaterial[key]=cellMaterial[key]; }
  // Fusion: a layer landing within a cell or two of what is already there smooth-maxes into it rather
  // than hard z-testing over it, so touching bodies read as one welded object instead of a stack.
  heights[key]=(previous>-900 && z-previous<FUSION_CELLS) ? smoothMax(z,previous,FUSION_CELLS*0.85) : z;
  cellMaterial[key]=material; cellLayer[key]=entry.order; localX[key]=lx; localY[key]=ly;
} else if (previous>-900 && previous-z<FUSION_CELLS) {
  heights[key]=smoothMax(previous,z,FUSION_CELLS*0.85);
  if (z>belowHeights[key]) { belowHeights[key]=z; belowMaterial[key]=material; }
}
```

Strict `>` (later layer does NOT win a tie), plus a fusion branch on BOTH sides of the test. `FUSION_CELLS = 1.7` (`SHAPER:982`), knee `1.7·0.85 = 1.445`; `smoothMax` at `SHAPER:997` (`-smoothMin(-a,-b,k)`).

**Exactly where a Z offset would be added — two candidate sites, one line each:**

1. `SHAPER:1362`, in `compileLayer`'s returned record: `base: order*0.75` → `base: order*0.75 + (Number(layer.zOffset)||0)`. This is the correct site: `base` is computed once per layer per frame, it is already the sole Z contribution, it is already inside `compileLayer` which receives the envelope-evaluated layer (`SHAPER:1389`) so the offset animates for free through the existing `applyEnvelopesToTarget` path, and nothing downstream changes.
2. `SHAPER:1424`, the per-cell `let z=base+…` — same arithmetic but per-cell, so strictly worse. Mentioned only to rule it out.

Serialisation would need one line in `SCHEMA:421` (`normalise_layer`) alongside `"depth": bounded_number(…, 0, 0, 2048)` — and unlike `depth` its bounds must be SIGNED (a Z offset that cannot go negative can only push layers apart in one direction). One UI row next to `SHAPER:3805`, and one entry in the animatable-range table `SHAPER:1593`/`SCHEMA:57`.

Nothing else needs touching: the compare (`:1437`), the fusion smooth-max (`:1441`, `:1445`), the `belowHeights` slot (`:1438`, `:1446`), the normals (`:1491`), the shadow march (`:1469`) and the height-view remap all consume `heights` as an opaque float and are agnostic to how it was produced.

---

## F. THE PER-SLOT PROTRUSION (background for T-0110)

**Verbatim, `TOY:50`** (the last statement of `raster()`; the whole function is on physical line 50):

```js
const z=L.z+layerHeight(L,d)+state.mats[mi].pro*(usePattern?1:.25);if(z>=hm[k]){hm[k]=z;mm[k]=mi}
```

Reading it left to right: the cell's height is **the layer's own Z (`L.z`) + the layer's height profile at this depth (`layerHeight(L,d)`, `TOY:33`) + the chosen material's own protrusion `pro`, weighted `× 1` where the strip is patterned and `× 0.25` where it is plain fill.**

- **Exact weights: `1` (patterned) and `0.25` (plain fill).** The ternary is on `usePattern`, set earlier on the same line: `usePattern=true` unconditionally for the `stripes` layout, otherwise `usePattern = L.edgeCoverage>=1 || depthInto<=L.edgeCoverage` — i.e. inside the ring band, or the whole face when coverage is 1.
- **`pro` is a MATERIAL field, not a layer field** — `state.mats[mi].pro`, and `mi` is the material the *ring/stripe index* selected (`patternMat`, `TOY:31`; `stripeMat`, `TOY:32`), so each slot of the strip can protrude differently. This is precisely the "same height channel" point: a fill slot writes into the depth buffer, not just the colour buffer.
- **Authored range: `-8 … 28`, step `1`** — `TOY:7`, `<input id="matPro" type="range" min="-8" max="28" step="1">`. Wired at `TOY:17` (`['#matPro','pro'], …`) and written back at `TOY:17` (`state.mats[state.activeMat][key]=+e.target.value`). Seeded defaults in `TOY:13-14` include `pro:4`, `pro:0`, `pro:3`, `pro:2`. **Negative values are authorable and meaningful** — a `pro` of −8 sinks that slot BELOW its own layer's face and can lose the depth test to whatever is underneath, which is what makes a patterned strip read as a seam/panel-gap rather than a stripe.
- Units are the same as `L.z` and `bodyH`: 64-grid cells, i.e. the toy's canvas pixels (`N=64`, `TOY:49`). For scale: `L.z ∈ [−8, 32]` (`TOY:26`) and `bodyH ∈ [0, 40]` (`TOY:26`), so a `pro` of 28 is comparable to a whole layer's thickness — the protrusion is a first-class Z actor, not a nudge.
- **Tie-break differs from 3D Shaper**: the toy uses `z >= hm[k]` (later layer wins ties); 3D Shaper uses `z > previous` (`SHAPER:1437`, earlier layer wins ties). Port carefully.
- **3D Shaper dropped `pro` entirely.** Its material carries no height field (`normalise_material` in `SCHEMA` has no protrusion key; the renderer reads `primaryColor/secondaryColor/surface/reflection/emission/pattern`), and in its place there is a single hardcoded constant, `SHAPER:1433`: `isEdgeCell=1; if (!entry.edgeStripes) z+=0.45;` — every ring cell gets `+0.45` cells, no per-slot control, and stripe-layout cells get nothing at all. That is the regression T-0110 is about; `P3-shaper-internals.md:92, :331` says the same.

---

## G. NORMALS FROM HEIGHT

`SHAPER:1490-1495`, verbatim:

```js
const material=materials[index]||FALLBACK_MATERIAL, surface=surfaceOf(material), h=heights[key];
let normalX=(sampleHeight(gx-1,gy,h)-sampleHeight(gx+1,gy,h))*0.65, normalY=(sampleHeight(gx,gy-1,h)-sampleHeight(gx,gy+1,h))*0.65;
let normalZ=1.4+clamp01(Number(material.reflection)||0)*1.5;
if (surface==='gelatinous') { normalX+=Math.sin(gx*0.62+clock*0.0038)*0.42; normalY+=Math.cos(gy*0.58+clock*0.0031)*0.42; }
const normalLength=Math.hypot(normalX,normalY,normalZ)||1;
normalX/=normalLength; normalY/=normalLength; normalZ/=normalLength;
```

with the sampler at `SHAPER:1467`:

```js
const sampleHeight=(gx,gy,fallback)=>{ if (gx<0||gx>=gridW||gy<0||gy>=gridH) return fallback; const value=heights[gy*gridW+gx]; return value<-900?fallback:value; };
```

In maths: **`n = normalize( ( 0.65·(h[x−1,y] − h[x+1,y]), 0.65·(h[x,y−1] − h[x,y+1]), 1.4 + 1.5·clamp01(reflection) ) )`** — a central difference over one cell in each axis, un-divided by the cell spacing (so the "0.65" absorbs a `1/(2·Δ)` that is never written), with a constant-ish Z. Note the sign: `h[x−1] − h[x+1]`, i.e. **`−∂h/∂x`** up to scale, and likewise in y — the normal points back toward decreasing height index, which is the y-DOWN screen convention (see §H2).

**The two hand-tuned constants named by `SHAPER_THE_DESIGN.md:173`** ("Today's version is inferred from neighbouring dots using two hand-tuned constants, a slope softening and a flattening. Both become named dials"):

| design-doc name | constant | value | line | what it does |
|---|---|---|---|---|
| **slope softening** | the gradient scale on both tangential components | **`0.65`** | `SHAPER:1491` (twice, once per axis) | scales the finite-difference gradient before normalising; smaller = a flatter-looking, less contrasty surface for the same height field |
| **flattening** | the base Z of the un-normalised normal | **`1.4`** | `SHAPER:1492` | how much the normal is pulled toward straight-out-of-screen; larger = flatter shading. A per-material `+1.5·clamp01(reflection)` rides on top of it, so a fully-reflective material sits at `2.9` |

The out-of-range / no-data fallback is the CENTRE cell's own height `h` (`sampleHeight`'s third argument at every call site, `SHAPER:1491`), which makes the border gradient one-sided rather than wrong — a cell at the grid edge sees a zero difference on the missing side. `-900` is the empty-cell sentinel, so a cell adjacent to background also falls back to its own height, i.e. **silhouette edges produce NO normal tilt from the background** — the entire edge shading comes from the bevel, not from the height discontinuity at the outline.

**Ancestor equivalent** (`TOY:52`) is the same expression with the same `0.65` and the same `1.4`, differing only in which material field rides on the Z term: `const nx=(safe(hm[yy*N+Math.max(0,xx-1)])-safe(hm[yy*N+Math.min(N-1,xx+1)]))*.65; const ny=(safe(hm[Math.max(0,yy-1)*N+xx])-safe(hm[Math.min(N-1,yy+1)*N+xx]))*.65; const nz=1.4+m.round*1.8;` — `round × 1.8` there vs `reflection × 1.5` in 3D Shaper. **The two constants 0.65 and 1.4 are inherited verbatim from the ancestor and have never been re-derived.**

---

## H. GOTCHAS FOR A PORTING AGENT

**H1. Unit inconsistency in the ANCESTOR — CONFIRMED — and 3D Shaper's fix — CONFIRMED.** `TOY:30`'s `sdf` returns a **normalised** distance (−1 at centre, 0 at the boundary) for `ellipse`, `diamond`, `hex` and `octagon` (`Math.hypot(X/rx,Y/ry)-1`, `Math.abs(X)/rx+Math.abs(Y)/ry-1`, `max(b-1, a+b*.55-1)`, `max(max(a,b)-1, a+b-1.42)`), but **canvas-pixel** distances for `triangle`, `capsule`, `roundrect` and `rect` (`Math.max(Math.abs(X)-rx, Math.abs(Y)-ry)` etc.). `TOY:33`'s `layerHeight` then does `clamp(-d,0,1)` with no normaliser at all, so on the pixel-distance primitives the profile saturates ONE PIXEL inside the outline and the "Rounded"/"Half dome" profiles only dome properly on the normalised four. The same bug hits `depthInto` and hence `edgeCoverage` at `TOY:50`. **3D Shaper fixed it on both ends**: every normalised primitive is multiplied back to canvas units by `unit=Math.min(rx,ry)` (`SHAPER:1031-1041`, e.g. `if (type==='ellipse') return (Math.hypot(x/rx,y/ry)-1)*unit;`), and the consumer divides by `span` (`SHAPER:1339`, `:1423`). `P3-shaper-internals.md:56` says the same and is correct. **Do not port the ancestor's version of either half.**

**H2. y-down, and the sign of the normal.** Both apps rasterise in screen order with `y` increasing DOWNWARD (`SHAPER:1418`, `cy=(gy+0.5)*cellH` over `gy` from `gy0` upward; lights are placed in the same canvas coordinates, `SHAPER:1387`). The normal's y component is `h[y−1] − h[y+1]` (`SHAPER:1491`), i.e. built in that same y-down frame, and the light direction at `SHAPER:1499` (`dy=light.gy-gy`) is too — so the two agree and neither is flipped. A Unity port into a y-UP frame must flip **both** together or the lighting mirrors vertically. The `linear` extrusion profile has the same exposure: `ny = ly/halfH` at `SHAPER:1423` is y-down local space, and `SHAPER:1148` subtracts `sin(angle)*ny`, so "angle 45°" means 45° in a y-down frame.

**H3. `FUSION_CELLS = 1.7` will silently swallow a small Z offset.** `SHAPER:982`, used at `:1441` and `:1444`. Two surfaces within 1.7 cells of each other do NOT z-test — they `smoothMax` into one welded blob. Since `base` steps by only `0.75` per layer (`SHAPER:1362`), **adjacent layers already fuse by default** (0.75 < 1.7); it takes three list positions (2.25 cells) to separate cleanly. A new per-layer Z offset therefore has a dead zone: any offset under ~1.7 cells changes the fused shape rather than the stacking order. Anyone testing a new Z-offset dial with small values will conclude it "doesn't do anything".

**H4. `linear` breaks two invariants everyone else keeps.** (a) It is the only profile that ignores `inside`, so it composes with the shape's inside-distance bound *not at all* and needs its own local-coordinate rule. (b) Its output is **not** in `[0, body]`: `1 + 0.6·(cos θ·nx − sin θ·ny)` ranges over `[1 − 0.6(|cos θ|+|sin θ|), 1 + 0.6(|cos θ|+|sin θ|)]`, worst case at `θ = ±45°` giving **`[0.1515, 1.8485]`**. So a `linear` layer can stand up to **1.85 × its authored depth** and `body` is not a height budget for it. Corollary: the `Math.max(0, …)` guard at `SHAPER:1148` is **dead code** — the minimum `1 − 0.6√2 = 0.1515` is strictly positive, so the clamp can never fire. Do not "preserve" it as if it were load-bearing, and do not assume it protects you if you change the `0.6`.

**H5. "Depth" means opposite things in the two apps.** 3D Shaper: `layer.depth` = extrusion thickness, `[0, 2048]`, non-negative (`SHAPER:3805`, `SCHEMA:57`). Ancestor: the slider *labelled* "Depth" is `L.z`, the Z POSITION, `[−8, 32]`, signed (`TOY:26`), and thickness is the separately-labelled "Shape height" `bodyH ∈ [0, 40]` (`TOY:26`). A port that reads the ancestor's UI labels will invert the two.

**H6. Degenerate / clamp inventory** (each one is a place a naive port diverges):

- `span = Math.max(1, min(halfW,halfH))` (`SHAPER:1339`) — floored at 1 canvas unit, so a sub-1-unit shape gets a squashed `inside` ramp rather than a divide-by-zero.
- `halfW`, `halfH` likewise floored at 1 (`SHAPER:1339`) — which also floors `nx`/`ny` for `linear`.
- `body <= 0 → return 0` (`SHAPER:1144`) — a zero-depth layer is a zero-height sheet at `base`, and it STILL writes to the height buffer and still wins the z-test against lower layers.
- `Number(settings.curve)||1` (`SHAPER:1150`, `:1151`) — JS falsiness: an authored `curve` of **exactly 0 becomes 1**, not 0.2. A port using a plain `max(0.2, curve)` gets `0.2` there instead, changing `round` from `√t` to `t^2.5`.
- `Math.round(settings.steps)||4` / `||3` (`SHAPER:1149`, `:1167`) — same trap: `steps` of 0 becomes 4 (or 3), then `max(2, …)`.
- `settings.taper!==undefined?clamp01(settings.taper):1` (`SHAPER:1152`, `:1153`) — absent means 1, present-and-out-of-range means clamped; `undefined` and `0` are different, unlike the `||` cases above.
- `max(0.05, taper*0.6)` (`SHAPER:1152`) — the clamp that caps taper's slope at 20 and makes `L_taper` non-monotone-looking below `τ = 1/12` (§C.2).
- `amount<=0 → return 1` (`SHAPER:1161`) — the bevel is a *hard* identity at `amount = 0`, discontinuous in the parameter (`amount = 0.001` gives slope 1000, `amount = 0` gives slope 0).
- `inside>=amount → return 1` (`SHAPER:1161`) — the early-out; combined with `u=clamp01(inside/amount)` the `clamp01` is redundant (`u < 1` always inside the band).
- `Math.max(0, 1-(1-inside)*(1-inside))` and `Math.max(0, 1-u*u)` / `1-4u*u` (`SHAPER:1150`, `:1164`, `:1165`, `:1166`) — guards against `sqrt` of a tiny negative from floating point; keep them.
- Heights sentinel: buffer filled with `-9999` but tested against `-900` (`SHAPER:1391` vs `:1438`, `:1444`, `:1467`). A real height below −900 would be misread as "empty". With `base = order*0.75 ≥ 0` today that cannot happen; **a signed Z offset makes it reachable** (offset < −900 cells). Any new Z-offset range must be bounded well inside that, or the sentinel must become a separate occupancy mask.
- `if (distance>0) continue;` (`SHAPER:1421`) — cells exactly ON the boundary (`distance == 0`, `inside == 0`) ARE rasterised, at `E(0)·B(0)`, which is height 0 for five of the seven profiles and a hard `1/m` step for a stepped bevel.

**H7. The bevel multiplies the extrusion, it does not replace it.** `SHAPER:1424` is a product. So a `flat` extrusion + a `rounded` bevel is a plateau with a filleted rim, whereas a `dome` extrusion + a `rounded` bevel multiplies two rim falloffs and produces a much sharper edge than either alone — and its composed slope is the product-rule sum in §C.8, not the larger of the two. Two identity cases worth early-outing on: `E = flat` (`L_E = 0`, `sup|E| = 1`, so the composed bound collapses to `body·L_B/a`) and `B = none` (`L_B = 0`, collapses to `body·L_E`).

**H8. The edge-cell `+0.45` is added AFTER the bevel and is not part of either profile.** `SHAPER:1433` — `isEdgeCell=1; if (!entry.edgeStripes) z+=0.45;` — a hardcoded constant bump on ring cells (not on stripe-layout cells). It is a Z-channel write that no profile knows about, it is the 3D Shaper replacement for the ancestor's per-material `pro` (§F), and it means the height field is **not** purely `base + body·E·B` at ring cells. Any bound must add it (it is a step, so it is another finite-Lipschitz violation, of magnitude 0.45 cells at the ring boundary).

---

## I. PYRE TODAY — the one worked extrusion and the two unrelated height mechanisms

The task's characterisation is accurate. All three found; described precisely below. Searched: all of `Runtime/Pyre/` and `Editor/Pyre/` for `extrud|slab|height|Height|zbuf|depthBuf|z-buffer|painter`.

### I.1 The one worked extrusion — the Text generator's eight slabs

`Runtime/Pyre/PyreRenderer.cs:3917-4054`, `static void DrawTextChar(...)`.

- Slab count is a compile-time constant: `PyreRenderer.cs:3945` — `const int DepthSteps = 8;`
- Thickness: `PyreRenderer.cs:3946` — `float depth = solid ? sz * Mathf.Clamp(layer.textDepth, 0.05f, 1f) : 0f;` — a fraction of the character height `sz` in screen px. `layer.textDepth` is `[Range(0.05f, 1f)] public float textDepth = 0.35f;` (`Runtime/Pyre/Pyre.cs:426`), gated by `public bool textSolid = true;` (`Pyre.cs:425`). `!textSolid` ⇒ `steps = 0` ⇒ a single flat plane (`PyreRenderer.cs:3947`).
- The march, `PyreRenderer.cs:4009-4011`: `for (int k = 0; k <= steps; k++) { float zoff = solid ? depth * 0.5f - (depth * k) / DepthSteps : 0f;` — nine sample planes (`k = 0…8`) from `+depth/2` (front face) to `−depth/2` (back), evenly spaced by `depth/8`.
- Occlusion is **first-hit-wins per pixel, front to back** — `PyreRenderer.cs:4019-4020` (`float sd = SampleTextSdf(...); if (sd < 0.5f) continue;`) then `break;` at `PyreRenderer.cs:4052` with the comment "first (front-most) depth hit wins: face vs side". **There is no depth buffer and no height value is ever stored** — the loop is a fixed-count ray march through 9 parallel planes of one rotated glyph box, and the result is written straight into the RGBA buffer with `Over()`.
- Side shading, `PyreRenderer.cs:4041`: `col *= 0.45f * Mathf.Lerp(1f, 0.55f, k / (float)DepthSteps);` — sides are `0.45×` the face colour, further darkened to `0.55×` at the back. A brightness ramp standing in for lighting; no normal is ever computed.
- Inter-character occlusion is a painter's sort, not a depth test — `PyreRenderer.cs:3902-3907` sorts characters by `x0 * dir` where `dir` comes from the sign of the rotated Z axis, then draws far→near (`RenderTextLine`, `PyreRenderer.cs:3859`).

**What it shares with the reference model: nothing.** No `inside`-distance, no profile, no bevel, no height field, no z-buffer, a hardcoded slab count, a thickness in glyph-relative units, and it is welded to a TMP SDF atlas sample (`SampleTextSdf`) rather than to a shape's own SDF. It is a demonstration that Pyre *can* draw an extruded solid; it is not a reusable extrusion stage.

### I.2 Height mechanism #1 — the Ramp (HeightBalls) field pass

`Runtime/Pyre/PyreField.cs:127-208` (substrate) and `Runtime/Pyre/PyreRenderer.cs:1676-1810` (the pass). Dials at `Runtime/Pyre/Pyre.cs:282-300`.

- Height is **accumulated from a swarm of circular domes**, not derived from a profile: `PyreField.cs:155-183`, `AccumulateDomes` — per dome `q = d²/r²`, `s = √(1−q)` (1 at centre → 0 at rim), fused by `acc = SmoothMax(acc, s·weight, knee)`. `SmoothMax` at `PyreField.cs:139-145`: `h = clamp01(0.5 + 0.5(b−a)/k); lerp(a,b,h) + k·h·(1−h)`.
- **Three** such scalar fields are built — density / heat / height (`PyreField.cs:131-132`).
- Lighting: `PyreField.cs:191-208`, `ReliefLight` — a normal from one-cell finite differences of the height field, `nx = −(hr−hl)·relief`, `ny = −(hu−hd)·relief`, `nz = 1`, Lambert against a screen-plane light, `light = ambient + max(0, n·L)·gain` with defaults `ambient 0.18`, `gain 0.82`, `lz 0.72` (`PyreField.cs:191`, described `PyreField.cs:189-190` as the exact Pyre1 `BlastRenderer.RenderHeightBalls` Pass-2 constants).
- Its per-particle weights are ZUIValue envelopes (`density`, `heat` — `Pyre.cs:292`, `:294`), i.e. animated over a particle's own life; `rampFusion = 0.35f` (`Pyre.cs:296`) is the SmoothMax knee.

**Concept overlap with the reference model: only the finite-difference-normal-from-heights idea** (`ReliefLight` ≈ `SHAPER:1491-1495`, with the same shape but `relief` as a dial instead of a hardcoded `0.65`, and `nz = 1` instead of `1.4`). There is no z-test, no layer base, no extrusion, no silhouette-relative distance — height is a fused blob field, not a solid under a profile.

### I.3 Height mechanism #2 — the matte heightmap channel

`Runtime/Pyre/Pyre.cs:219-238` (fields) and `Runtime/Pyre/PyreRenderer.cs:1839-1880`, `RenderHeightConsumer`.

- `public bool matteWriteLuma` (`Pyre.cs:230`) — a layer deposits `LUMINANCE × alpha` into one of four numbered matte channels instead of flat coverage-alpha.
- `public int heightFromChannel = -1;` (`Pyre.cs:233`) — a Draw layer with `≥ 0` does **not** draw its own shape; it renders that already-fused channel as a relief-lit surface through its own `shapeFill` gradient.
- `public float heightRelief = 3f;` (`Pyre.cs:235`) and `heightLightAngle` (`Pyre.cs:236`) — passed straight to the shared `PyreField.ReliefLight` (`PyreRenderer.cs:1858`).
- Shading: `PyreRenderer.cs:1870-1876` — `fill.Evaluate(h, 0, 0)` at the RAW fused height, then multiplied by `shade = 0.35f + light[i] * 1.15f`. Draws nothing where `h <= 0.003f` (`PyreRenderer.cs:1864`).
- Gated by `matteEnabled` (`Pyre.cs:202-203`); wired at `PyreRenderer.Layers.cs:105` and `PyreRenderer.cs:229`.

**Relationship to #1:** they DO share one thing — `PyreField.ReliefLight` (`PyreRenderer.cs:1843-1847` says so explicitly: "the SHARED PyreField.ReliefLight substrate, the same helper Ramp's Pass 2 uses"). **Minor correction to the task description**, which says the two mechanisms share "no concept": they share the slope→normal→Lambert relief-lighting helper. What they genuinely share nothing of is the *height-authoring* concept — one PUSHES a field from a particle swarm, the other PULLS an already-fused matte channel — and neither shares anything with the Text extrusion. The task's substantive point (there is no reusable extrusion/height stage in Pyre) is correct.

**Also checked and not applicable:** the Gem/facet true-3D solids (`Pyre.cs:48-50`: Box / Pyramid / Can) are real geometry with per-facet lighting, not a height field, and Pyre's only depth ordering anywhere is the painter's sort at `PyreRenderer.cs:3855-3856`. **NOT FOUND anywhere in `Runtime/Pyre/` or `Editor/Pyre/`: any z-buffer, any per-pixel depth array, any `inside`-distance→height profile, and any bevel of any kind.** Grepped for `zbuf|zBuf|depthBuf|DepthBuffer|z-buffer|bevel|Bevel` across both trees.

---

## J. Summary table — declared slope bounds as they would actually have to be written

`sup |d(h/body)/dt|`. "∞" means no finite bound exists.

| technique | kind | bound | valid over |
|---|---|---|---|
| `flat` | extrusion | **0** | always (exact identity) |
| `linear` | extrusion | **0** in `t`; `0.6` in `(nx,ny)`; `sup|E| = 1.8485` | always; needs a separate local-coordinate rule |
| `pyramid` | extrusion | **`τ`**, max **1.00** | `τ ∈ [0,1]`, tight |
| `taper` | extrusion | **`1/max(0.05, 0.6τ)`**, range **[1.6667, 20]** | `τ ∈ [0,1]`, tight |
| `round` | extrusion | **`0.5/c`** if `c ≤ 0.5` (range `[1, 2.5]`); **∞** if `c > 0.5` | unbounded at `t→0`; default `c=1` is unbounded |
| `dome` | extrusion | **`2m/√(2m−1) · (1−1/(2m−1))^{m−1}`**, `m=1/(2c)`, if `c ≤ 0.5` (range `[1.6238, 2]`); **∞** if `c > 0.5` | unbounded at `t→0`; default `c=1` is unbounded |
| `stepped` | extrusion | **∞** (floor function) | never bounded; §D |
| `none` | bevel | **0** | always (exact identity) |
| `linear` | bevel | **`1/a`** (→ 100 at the UI's minimum `a=0.01`; `∞` as `a→0⁺`) | tight in `u` |
| `rounded` | bevel | **∞** | unbounded at `u→0` (the silhouette) |
| `cove` | bevel | **∞** | unbounded at `u→1` (inner band edge) |
| `ogee` | bevel | **∞** | unbounded at `u→½` (mid-band inflection) |
| `stepped` | bevel | **∞** (floor function) | never bounded; §D |
| composed | — | `body·(L_E + sup|E|·L_B/a)` on the band; `body·L_E` off it; **×`1/span`** to reach canvas units | §C.8 |
| edge ring | — | **+0.45 cells step** at the ring boundary (`SHAPER:1433`) | a further finite-Lipschitz violation, outside both registries |

**Bottom line for T4/T6: six of the thirteen non-identity techniques (`round` and `dome` at their defaults, `rounded`, `cove`, `ogee`, and `stepped` twice) have no finite Lipschitz bound as authored today. `stepped` is a discontinuity; the other five are unbounded derivatives of continuous functions, which is a different problem with different fixes (a floor on the parameter, or a slope-limited variant near the singular end, both work for those — neither works for `stepped`).** The task's framing that only `stepped` is unboundable understates it: `stepped` is uniquely *discontinuous*, but it is not uniquely *unbounded*.

---

## K. What in the task description I found to be wrong

1. **"round 16.4 (the common case), dome 23.1"** — not maxima. Both are unbounded at their default `curve = 1`, and the two quoted numbers are the analytic derivatives of the two profiles at one and the same sample point `t ≈ 9.3e-4` (their ratio is `√2` to 0.4%, which is the giveaway). Refining the measurement grid raises both without limit. §C.3, §C.4, §C.5.
2. **"taper 1.67 at taper=1 but 16.7 at taper=0.1"** — both correct at those two points, but 16.7 is not the worst case. `taper` is authorable down to 0 (`SCHEMA:60`, UI step 0.01), and the `max(0.05, …)` clamp pins the bound at **20** for every `τ ≤ 1/12`. §C.2.
3. **"pyramid 1.00"** — correct and exactly tight. §C.1.
4. **"five bevels"** — the authored enum has **six** entries (`SCHEMA:29`); five are functions and `none` is the identity. Both counts are defensible but the serialised list is six. §B.
5. **"Round and dome are formally unbounded as inside-distance approaches zero"** — correct, but incomplete: it is conditional on `curve > 0.5` (true for the default and most of the range, false for `curve ∈ [0.2, 0.5]`), and **`rounded`, `cove` and `ogee` are unbounded too** — at `u→0`, `u→1` and `u→½` respectively. §C.7.
6. **"the STEPPED profile … is a floor function, so … NO finite bound exists for it at all"** — confirmed, but it is not the only technique with no finite bound (see 5), and it is the only *discontinuous* one. §D.
7. **"two unrelated height mechanisms sharing no concept"** (Pyre) — the two DO share `PyreField.ReliefLight`, explicitly and by design (`PyreRenderer.cs:1843-1847`). What they share nothing of is the height-*authoring* concept. §I.3.
8. **`inside` cited at "~:1339, :1425"** — `span` is at `:1339` (correct); the `inside` assignment is at **`:1423`**. §0.
9. **Normals cited at "~:1491-1495"** — correct as a range; the two constants specifically are at `:1491` (`0.65`) and `:1492` (`1.4`). A prior digest (`P3-shaper-internals.md:128`) cites `:1491-1493`, which includes the gelatinous wobble line and not the normalise. §G.
10. **Not stated anywhere but load-bearing:** the profile slopes in the task are per-unit-`body`, per-unit-`span`. The real height-field slope is `(body/span)×` those, with `body = depth/cellSize` and `depth ∈ [0, 2048]`. Comparing "pyramid 1.00" against "a worst primitive of about 3.1" is comparing two different quantities. §C.8.
