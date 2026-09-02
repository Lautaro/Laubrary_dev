# Shaper capability → UI inventory (T-0147)

The checklist that drives the real UI port (Phase C). Every claim about the engine cites `file:line` in
`D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Runtime\`. Mock citations are
`D:\UNITY\Laubrary Dev\Assets\ShaperMock\Editor\`. Where a planning doc disagrees with the engine, the engine wins and the disagreement is recorded in §CONTRADICTIONS.

**Method.** Worked outward from the engine's authored (serializable) types rather than from the docs or the mock, so a capability nobody remembered still shows up. 44 engine files scanned (38 `Runtime/Shaper/`, 6 `Runtime/PyreShaper/`); every `public` instance field on every authored type enumerated and diffed field-by-field against the mock's own model. **196 authored fields across 21 authored types**, plus 4 catalog/rule surfaces (effects, composite generators, Solids inertness, cache budget).

**Coverage counts (fields):** FULL 138 · PARTIAL 19 · NONE 32 · N/A 7.

---

## 0. In-flight — do not report as missing

`ShaperClock.cs` exists in the engine worktree as of this pass (`ShaperClock.cs:40-73`): `DefaultFrameRate = 12f`, `MinFrameRate = 1f`, `MaxFrameRate = 30f`, `PhaseOfFrame(frameIndex, frameCount)`, `WrapFrame`, and a wall-time accumulator. A concurrent agent is adding the animation clock and Cherry Framing (Phase A1/A2). **Anything cherry/frame/fps-shaped is in flight, not a gap.** One note for Phase C: `PhaseOfFrame` uses `frameIndex / (frameCount - 1)` (`ShaperClock.cs:53-57`), i.e. the last frame lands exactly on phase 1 — so a looping playback will repeat its endpoint unless the transport wraps before re-showing frame 0. Worth confirming against the finished Phase A2 work.

---

## 1. Document — `ShaperDocument` (`ShaperLightRig.cs:375`)

| Capability | Engine | Authored | Mock | Missing |
|---|---|---|---|---|
| Document name | `:377` `name : string` | free text | **NONE** | no name field anywhere in the mock |
| Canvas size | `:380-382` `canvasWidth=96`, `canvasHeight=64` | int, int | FULL — `ShaperMockData.cs:81-82` (`width`/`height`) | — |
| Pixel size | `:384` `pixelSize=1f` | float | FULL — `ShaperMockData.cs:88` | — |
| Layer list | `:387` `layers` | ordered, order is authored data | FULL — `ShaperMockWindow.cs:260` Layers section | — |
| Layer spacing | `:403` `layerSpacing=0.75f` | float, canvas px between layer base planes | FULL — `ShaperMockData.cs:87` | — |
| Light rig | `:406` | one per document, never per node (LR-1.1) | FULL — `ShaperMockWindow.cs:387` | — |
| Document phase | `:413` `phase01=0f` | float 0..1, the ONLY sampling clock (LR-1.8) | **NONE** | mock has `frameCount`/`previewFps` instead and never exposes phase; see §CONTRADICTIONS C4 |
| Seed | `:416` `seed : uint` | uint; the seed every MinMax dial draws from (LT-4) | PARTIAL — `ShaperMockData.cs:84` is `int`, not `uint` | type mismatch; negative values unrepresentable in engine |

**Constraint:** layer order is authored data and no stage may reorder it (`:386`). The UI's layer reorder must be the only thing that changes it.

---

## 2. Layer — `ShaperLayer` (`ShaperLightRig.cs:315`)

| Capability | Engine | Mock | Missing |
|---|---|---|---|
| Name / enabled / root | `:317-319` | FULL — `ShaperMockData.cs:99-101` | — |
| **Per-layer light response** | `:320` `response : ShaperLightResponse` | **PARTIAL** — mock puts it on the NODE (`ShaperMockData.cs:259` `lightResponse`), display-gated to layer root (`ShaperMockWindow.cs`) | data lives at the wrong level; T-0140 fixed the *display*, not the *model* |
| Z offset | `:340` `zOffset : ZUIValue` | FULL — `ShaperMockData.cs:104` | — |
| **Per-layer height/extrusion** | `:357` `height : ShaperHeightDef` `[SerializeReference]`, nullable | **PARTIAL** — mock puts extrusion on the NODE (`ShaperMockData.cs:234-242`) | same wrong-level problem; also the real field is *nullable* (absent = no height stage), which the mock's always-present fields cannot express |

**Constraint (HS-7.2):** `base(i) = i × layerSpacing + zOffset(i)` is the layer's only Z contributor. Two layers *can* share a Z here (the reference app could not) — a UI that implies strict stacking would be lying.

---

## 3. Node — `ShaperNode` (`ShaperNode.cs:82`)

| Capability | Engine | Mock | Missing |
|---|---|---|---|
| Name / enabled | `:84-85` | FULL — `ShaperMockData.cs:172-173` | — |
| Kind | `:86` `ShaperNodeKind = {Primitive, Bag, Composite}` (`:12`) | PARTIAL — mock adds a 4th `Solid` (`ShaperMockData.cs:112`) | see §OPEN D1 |
| Combine mode | `:89` `mode : ShaperCombineMode = {Add, Subtract, Intersect}` (`ShaperOps.cs:11`) | FULL — `ShaperMockData.cs:174` (named `combineMode`) | rename only |
| Blend | `:90` `blend : ShaperBlend` — `width`, `sharpness [0,1]`, `carveStrength [0,1]` (`:21-28`) | FULL — `ShaperMockData.cs:126-130` | all three plain float in BOTH — correct |
| **Transform** | `:93` `transform : ShaperTransformBlock` — `translate` Vector2, `rotation` float, `scale` Vector2, `skewDegrees` Vector2, `origin` Vector2 (`ShaperMatrix.cs:113-120`) | **NONE — zero occurrences of `transform`/`translate`/`origin` in any mock file** | **the single largest gap in the whole tool.** Every node has a position/rotation/scale/skew/origin and none of it is authorable |
| Sweep | `:95` — `enabled`, `startDegrees`, `extentDegrees`, `startFraction [0,1]`, `extentFraction [0,1]` (`:44-56`) | FULL — `ShaperMockData.cs:143-147`, `ShaperMockWindow.cs:1331` | — |
| Shell | `:96` — `enabled`, `thickness`, `alignment` (`:64-69`) | FULL — `ShaperMockData.cs:155-157`, `ShaperMockWindow.cs:1301` | — |
| Swarm | `:101` | see §7 | — |
| Primitive | `:104` | see §4 | — |
| Children | `:107` `[SerializeReference] List<ShaperNode>` | FULL — `ShaperMockData.cs:209` `bagMembers` | — |
| Composite | `:116` | see §5 | — |
| Fill | `:138` `fill : ShaperFillDef` (nullable — absence is meaningful) | see §8 | — |
| Border | `:161` `border : ShaperBorderDef` (nullable) | see §6 | — |

---

## 4. Primitive — `ShaperPrimitiveDef` (`ShaperPrimitives.cs:58`)

`ShaperPrimitiveKind = {Rect, Ellipse, Diamond, Triangle, Capsule, NGon, Star}` (`:9`) — **7 kinds. FULL** coverage in mock (`ShaperMockData.cs:116`, picker at `ShaperMockWindow.cs`).

All 21 dials FULL. Ranges to honour: `ngonSides [Range(3,64)]` (`:84`) — **the mock clamps 3..16 (`ShaperMockWindow.cs:687`), losing 17..64**; `starArms [Range(2,20)]` (`:91`) — **mock clamps 3..12 (`ShaperMockWindow.cs:700`), losing 2 and 13..20**. Both PARTIAL.

Envelope posture (verified, matches mock exactly): only `starLength` `:96`, `starBaseWidth` `:98`, `starSkew` `:100` are `ZUIValue`; `starRadius` `:93`, `ngonSides`, `ngonRadius` and every rect/ellipse/diamond/triangle/capsule dial are plain. The mock is correct here.

---

## 5. Composite — `ShaperCompositeDef` (`ShaperCompositeDef.cs:89`)

| Capability | Engine | Mock | Missing |
|---|---|---|---|
| **Source** | `:95` `[SerializeReference] IShaperCompositeSource source` — interface (`:42`); the shipped impl is `PyreFormCompositeSource` (`PyreFormCompositeSource.cs:31`) holding `[SerializeReference] PyreForm form` (`:33`) | **PARTIAL** — mock uses `int compositeGeneratorIndex` (`ShaperMockData.cs:212`) into a 9-name display catalog | the real picker must **instantiate a source object and assign a `PyreForm`**, not store an index. The mock's generator menu is structurally not the control the engine needs |
| Reason | `:100` `ShaperCompositeReason = {AuthoredData=0, NotYetSplit=1}` (`:14`) — **2 values** | FULL — `ShaperMockData.cs:120` (2 values) | — |
| Reason note | `:108` `[TextArea(2,5)] string` | FULL — `ShaperMockWindow.cs` free-text row | mock uses a single-line input; real is a 2-5 line TextArea |
| Bake box | `:119-120` `halfExtentX/Y = 64f` | FULL — `ShaperMockData.cs:221-222` | — |
| Bake resolution | `:129-130` `bakeWidth/Height = 128` | FULL — `ShaperMockData.cs:223-224` | — |
| Declaration state | `:134` `HasDeclaration => !IsNullOrWhiteSpace(reasonNote)` | **NONE** | a derived flag the UI should surface (a composite with no declaration is the thing the audit flags) |

**Catalog:** `PyreCompositeCatalog` (`PyreCompositeCatalog.cs:43`) — 9 entries, each `displayName`, `paletteIndifferent`, `reasonNote` (`:29-33`). **All 9 are `NotYetSplit`; 0 are `AuthoredData`** (`:11`). Palette-indifferent: Orb, Torch, Arc Burst, Plasma Bloom. Mock mirrors this correctly (`ShaperMockGenerators.cs:138-158`).
**Per-generator dials:** the real per-form parameters live on the `PyreForm` subclass, NOT on `PyreCompositeCatalogEntry` — the catalog carries only name/classification/note. The mock's 7 param classes (`ShaperMockGenerators.cs`) are **acknowledged stand-ins** (`:14-17`), not real field lists. **PARTIAL — the real UI must reflect over the assigned `PyreForm`, which is what `ZuiReflect` already does.**

---

## 6. Border — `ShaperBorderDef` (`ShaperBorderDef.cs:29`)

| Capability | Engine | Mock | Missing |
|---|---|---|---|
| **Enabled** | `:36` `enabled = true` | **NONE** | mock gates the border by object null-ness only; the real type has an explicit on/off that survives the authored settings |
| Alignment | `:49` `ShaperShellAlignment = Inward` (default differs from Shell's `Centred`) | FULL — `ShaperMockData.cs:449` | — |
| Width | `:69` `width : ZUIValue(2f)` | FULL — `ShaperMockData.cs:445` — but mock default is `0.05f` vs engine `2f` | default mismatch (mock is in a normalised space; engine is canvas px) |
| Joins coverage | `:90` `joinsCoverage = true` | FULL — `ShaperMockData.cs:446` | — |
| Border's own fill | `:107` `fill : ShaperFillDef` | FULL — `ShaperMockData.cs` `stripFill` | rename only |

---

## 7. Swarm — `ShaperSwarmDef` (`ShaperSwarmDef.cs:110`)

All 9 fields FULL (`ShaperMockData.cs:461-472`): `enabled`, `count [Range(1,64)]` `:115`, `seed`, `positionJitter` Vector2 `:124`, `rotationJitterDegrees`, `scaleJitter [0,1]`, `lifetimeStagger [0,1]`, `merge : ShaperBlend` `:146`, `interact` `:156`.

**Constraints the UI must honour:**
- `SimulationHardCap = 6` (`:163`) — mock honours it (7 references).
- `ShaperSwarmImplementation = {None, Generic, Native}` (`:14`) — `None` when `count <= 1`, an exact no-op. The UI should show which of the three a given node resolves to; the mock shows a native/simulated indicator.
- `count` is plain `int`, `positionJitter` plain `Vector2` — not animatable, correct in both.

---

## 8. Fill — `ShaperFillDef` (`ShaperFillDef.cs:49`) — 48 fields, the largest surface

`ShaperFillKind = {Solid, Gradient, RampByQuantity, Texture, IndexedStrip, HeightField, TapestrySteel}` (`ShaperFillContract.cs:228`) — **7 kinds, all present in mock** (`ShaperMockData.cs:267`).

### 8a. Cross-kind fields (apply to every fill)

| Field | Engine | Mock | Status |
|---|---|---|---|
| `kind` | `:51` | `:287` | FULL |
| **`veil`** | `:60` `ZUIValue(1f)` | **absent** (the 4 `veil` hits in the mock are all generator-classification prose, `ShaperMockGenerators.cs`) | **NONE** |
| **`heightDelta`** | `:68` `ZUIValue(0f)` | **absent — 0 occurrences** | **NONE** — the fill's contribution to the height buffer; this is what T-0110/T-0127 relief shading reads |
| `composite` | `:75` `{Over, Add}` (`:179`) | `:290` | FULL |
| `space` | `:83` `{Stamped, Fixed}` (`:203`) | `:291` | FULL |
| `fit` | `:86` `{Uniform, Stretch}` (`:217`) | `:292` | FULL |

### 8b. Per-kind

| Kind | Engine fields | Mock | Missing |
|---|---|---|---|
| Solid | `solidColor` `:96` | FULL `:295` | — |
| Gradient | `gradientMode` `:100` `{Linear,Radial,Angular,ByEdgeDistance}`, `gradient : ZuiGradient` `:107`, **`gradientTint`** `:110`, `gradientAngleDegrees` `:113`, **`gradientCentreX/Y`** `:116-117`, **`gradientSize`** `:126`, **`gradientDepthPixels`** `:129` | PARTIAL `:301-303` | **5 of 8 missing**: tint, centreX, centreY, size, depthPixels. Mock also uses Unity `Gradient`, engine uses `ZuiGradient` |
| RampByQuantity | `rampQuantity` `:139` (9 values, `ShaperFillContract.cs:34`), **`rampGradient : ZuiGradient`** `:141`, `rampTint` `:142`, `rampInputLow/High` `:145,151` | PARTIAL `:309-312` | **`rampGradient` missing** — the ramp has no ramp |
| Texture | **`texture : Texture2D`** `:160`, `textureMapping` `:162`, `textureTilesX/Y` `:165-166`, `textureOffsetU/V` `:169-170`, `textureAngleDegrees` `:173`, `textureTint` `:179` | PARTIAL `:315-322` | **`texture` is a real `Texture2D` asset ref; mock uses `textureSourceIndex : int`** — needs a real object picker |
| IndexedStrip | **`stripParameterisation`** `:184` `{Angular, Projection}`, `stripSlots : List<ShaperStripSlot>` `:192`, `stripRepeats` `:199`, `stripOrientationDegrees` `:202`, `stripOffset` `:205`, `stripReach` `:216`, `stripPlainColor` `:227` | FULL `:330-335` (`stripMode` = `stripParameterisation`, renamed) | rename only |
| ↳ **per-slot** | `ShaperStripSlot` `:17` — `color` `:25`, **`height`** `:32` | FULL — `ShaperMockStripSlot` `:435-438`, 11 `stripSlots` refs | the per-slot height (T-0110's whole point) IS covered |
| HeightField | **`heightField : Texture2D`** `:237`, `heightFieldScale` `:248`, `heightFieldTint` `:255` | PARTIAL `:339-341` | **mock uses `heightFieldPresetIndex : int`**; real is a `Texture2D`, and presets are `ShaperHeightFieldPreset` ScriptableObjects (§10) — needs an asset picker |
| TapestrySteel | `steelCells` `:260`, `steelOctaves` `:263`, `steelSeed` `:266`, `steelBaseLow/High` `:269-272`, `steelRustColor` `:275`, `steelRustAmount` `:278`, `steelRustReachPixels` `:281`, `steelGrain` `:284` | FULL `:344-352` | — |
| all kinds | `quantiseLevels` `:292` | FULL `:355` | — |

### 8c. The systematic posture defect

**19 fill dials are `ZUIValue` (animatable) in the engine but plain `float`/`int` in the mock**, measured by direct type diff:

`gradientAngleDegrees`, `rampInputLow`, `rampInputHigh`, `textureTilesX`, `textureTilesY`, `textureOffsetU`, `textureOffsetV`, `textureAngleDegrees`, `stripRepeats`, `stripOrientationDegrees`, `stripOffset`, `stripReach`, `heightFieldScale`, `steelCells`, `steelOctaves`, `steelSeed`, `steelRustAmount`, `steelRustReachPixels`, `steelGrain`, `quantiseLevels` (+ the 2 absent ones, `veil` and `heightDelta`, which are also `ZUIValue`).

The real UI must draw all of these with `Z.Value`, not `Z.MicroSlider`. See §CONTRADICTIONS C1 — the mock justified this with a false doc citation.

---

## 9. Light rig — `ShaperLightRig.cs`

| Type | Engine | Mock | Status |
|---|---|---|---|
| Rig | `:100` — `ambientColour` `:118`, `ambientIntensity : ZUIValue` `:133`, `lights` `:136` | `:487-489` | FULL |
| Light | `:19` — `name`, `enabled`, `kind {Directional, Point}` `:8`, `colour` `:36`, `intensity` `:43`, `yaw` `:46`, `pitch` `:55`, `posX/Y/Z` `:58-66`, `range` `:74`, `specular` `:81` | `:498-509` | FULL (all 12) |
| Response | `:209` — `receiveLighting` `:217`, `intensityScale` `:225`, `castShadows` `:242`, `receiveShadows` `:245`, `rimStrength` `:252`, `specular` `:272`, `specularPower` `:275`, `specularTint` `:283`, `normalKind` `:288`, **`normalConstant : Vector3`** `:295`, `rimPower` `:304` | `:521-530` | **PARTIAL — `normalConstant` missing (0 occurrences)** |

**`normalConstant` is the priority-2 gap.** `ShaperNormalKind = {Constant, Profile}` (`ShaperNormals.cs:11`) and the default is `Constant` (`:288`) — so on a default document the *only* parameter of the active normal path is unauthorable. `Profile` needs a height stage to produce relief at all, so a user with no extrusion who picks a lighting direction has nothing to pick it with.

---

## 10. Height / extrusion — `ShaperHeightDef` (`ShaperHeight.cs:78`)

`ShaperExtrusionTechnique = {Flat, Linear, Stepped, Dome, Round, Taper, Pyramid}` (`:16`) — 7. `ShaperBevelTechnique = {None, Linear, Rounded, Cove, Ogee, Stepped}` (`:47`) — 6. Both FULL in mock (`ShaperMockData.cs:162-163`).

All 9 fields FULL (`technique` `:81`, `depth` `:95`, `angle` `:98`, `steps` `:101`, `curve` `:104`, `taper` `:107`, `bevel` `:110`, `bevelAmount` `:119`, `bevelSteps` `:126`) — mapped to `extrude*`/`bevel*` on the mock node (`:234-242`). All 7 numeric ones are `ZUIValue` in both. **Correct.**

**But:** the real field is `ShaperLayer.height`, nullable, `[SerializeReference]` (`ShaperLightRig.cs:357`) — see §2. The mock's node-level, always-present fields cannot express "this layer has no height stage".

**Height-field presets** — `ShaperHeightFieldPreset : ScriptableObject` (`ShaperHeightFieldPreset.cs:21`), `[CreateAssetMenu("Laubrary/Shaper/Tapestry Height Field Preset")]` (`:20`): `field : Texture2D` `:24`, `sourceId` `:27`, `resolution` `:30`, `measuredMin/Max` `:33-36` (informational only). **245 preset assets** are committed at `Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/`. Mock coverage: **PARTIAL** — an int index into a fake list; needs a real asset picker with a searchable browser at that corpus size.

---

## 11. Solids — `ShaperSolidDef` (`ShaperSolids.cs:32`)

`ShaperSolidForm = {Box, Pyramid, Can, Orb, Gem, Ring}` (`:8`) — 6, FULL. All 19 fields FULL (`ShaperMockSolids.cs` mirrors field-for-field).

**Constraint — the inertness table.** `ShaperSolids.InertReason(form, dial)` (`:312`) returns null (live) or a sentence (inert), across `ShaperSolidDial` (14 values, `:88`). The engine's own rule is *declare inert with a reason, never hide* — and `Compile` actively `Check`s each dial (`:444-450`) and counts `InertDialCount`. There is also `Neutral(dial)` (`:371`) giving each dial's no-op value. Mock honours this (`ShaperMockSolids.cs` mirrors `InertReason`; `SolidVal` greys with the reason).

**Not authored anywhere.** `ShaperSolidDef` is referenced only inside `ShaperSolids.cs` and via a separate `scene.solid[owner]` slot in `ShaperFillResolver`. `ShaperNodeKind` has no `Solid` member (`ShaperNode.cs:12`). See §OPEN D1.

---

## 12. Effects — `ShaperEffectCatalog` (`PyreShaper/ShaperEffectContract.cs:118`)

**41 entries** (`ExpectedTotal = 41`, `:181`). Per entry (`ShaperEffectCatalogEntry` `:85-92`): `typeName` (string, deliberately not a `Type` `:81-82`), `stageKind` ∈ `{Geometry, Pixel, Post, Edge, Simulation}` `:88`, `portability`, `defaultStage`, **`bothStagesPossible`** `:91`, **`requiredSheets : ShaperQuantitySet`** `:92`.

`ShaperEffectPortability = {BufferOnlyFree, BufferOnlyPadded, NeedsSheets, Stuck}` (`:46`). `ShaperEffectStage = {PreComposite, PostComposite}` (`:24`).

| Capability | Mock | Missing |
|---|---|---|
| 41-entry catalog | PARTIAL — 7 bespoke param classes + `MockGenericEffect` top-ups to ~41 names (`ShaperMockEffects.cs:185-206`) | real per-effect dials come from the actual `PyreModifier` subclass; the generic ones are stand-ins |
| Portability buckets | FULL — `ShaperMockEffectBucket = {BufferFree, BufferPadded, NeedsSheets, Stuck}` (`ShaperMockData.cs:535`) | — |
| Stage | FULL — `{Pre, Post}` (`:534`) | — |
| **`bothStagesPossible`** | **NONE** | the engine says some effects may legally run at *either* stage; the mock has no per-effect "you may move this" affordance |
| **`requiredSheets`** | **NONE** | which quantity sheets a `NeedsSheets` effect needs — the mock shows the bucket but never *which* sheets |
| **Availability gating** | **NONE** | `IsAvailable(entry, published, out reason)` (`:188`) returns a *reason* an effect is unavailable given what the node publishes. The UI must show the reason, not just hide the entry |
| `Stuck` handling | the one `Stuck` entry is `EdgeWarpModifier` (`:174`) | mock has a deliberately-stuck entry | see §OPEN D4 |
| `PixelFluidModifier` | `Simulation`, `PostComposite`, `bothStagesPossible: false` (`:149-150`) | — | a real single-slot special case |

**Quantities:** `ShaperQuantity` (9 values, `ShaperFillContract.cs:34`) and `ShaperQuantitySet` flags (`:56`) incl. presets `ShippedShapeEngine = Coverage|EdgeDistance` and `ShapeEngineWithHeight = Coverage|EdgeDistance|Height`. The mock covers the 9 ramp quantities (`ShaperMockData.cs:270`) but not the *set* concept.

---

## 13. Not authored — N/A for UI

| Thing | Why |
|---|---|
| `ShaperCacheBudget` (`ShaperCacheBudget.cs:34`) | Its own doc says it is "not an authored document asset or an editor window with a resolution field… the DESIGN TARGET" (`:10-11`). Constants: `SchemaResolutionMin/Max/Default = 32/256/96` `:36-38`, `MaxGridCells = 68000` `:43`. **No UI.** The mock's cache strip is a preview aid, correctly not authored data. |
| `ShaperTapestryCanvas` (`:37`) | Pure static noise math (`HashCell`, `WrappedValueNoiseAt`, `Fbm`) — no fields. |
| `ShaperProgram` / `ShaperResolve` / compilers / caches | Compiled/derived state, not authored. |
| `ShaperFillSheets` / `ShaperFillEmit` / `ShaperFillInputs` | Runtime buffers. `ShaperFillInputs.phase01`/`seed` `:331,338` are *fed* from the document, not authored per-fill. |

---

## GAP LIST — ranked by user impact

### NO UI AT ALL
1. **Node transform** — `translate`, `rotation`, `scale`, `skewDegrees`, `origin` (`ShaperMatrix.cs:113-120`), on **every** node. Nothing in the mock touches it. Positioning a shape is the most basic authoring act in a 2D tool and it is currently impossible.
2. **`ShaperLightResponse.normalConstant`** (`ShaperLightRig.cs:295`) — the only parameter of the *default* normal path (`Constant`).
3. **`ShaperFillDef.heightDelta`** (`:68`) — the fill's height contribution; the input to relief shading (T-0110/T-0127).
4. **`ShaperFillDef.veil`** (`:60`) — cross-kind opacity/veil on every fill.
5. **Gradient fill: `gradientCentreX/Y`, `gradientSize`, `gradientDepthPixels`, `gradientTint`** (`:110-129`) — Radial/Angular/ByEdgeDistance modes are unusable without centre and size.
6. **`rampGradient`** (`:141`) — RampByQuantity has no authorable ramp.
7. **Effect `requiredSheets`, `bothStagesPossible`, and `IsAvailable` reasons** (`ShaperEffectContract.cs:92,91,188`).
8. **`ShaperBorderDef.enabled`** (`:36`); **`ShaperDocument.name`** (`:377`); **`ShaperCompositeDef.HasDeclaration`** (`:134`).

### PARTIAL
9. **Composite `source`** — index vs `[SerializeReference] IShaperCompositeSource` + `PyreForm`; the picker must build objects (§5).
10. **Per-generator dials** — must reflect over the real `PyreForm`, not 7 stand-in classes.
11. **19 fill dials drawn as fixed sliders that the engine makes animatable** (§8c).
12. **Texture / height-field asset refs** — `Texture2D` and `ShaperHeightFieldPreset` assets vs int indices; 245 presets need a searchable picker.
13. **Per-layer `response` and `height` modelled per-node** (§2) — nullable `height` in particular.
14. **`ngonSides` 3..16 vs engine 3..64; `starArms` 3..12 vs engine 2..20.**
15. **`seed` as `int` vs engine `uint`.**

---

## CONTRADICTIONS — docs vs engine (engine wins)

**C1. The mock's stated reason for plain-float fill dials is a misreading of the design doc.**
`ShaperMockData.cs:16-18` says: *"Fill's own per-kind dials stay plain floats… §H2 of the design doc lists fill dials as an existing-engine fact."* But §H1 of `SHAPER-UI-VISION-AND-DESIGN.md` — titled *"Already envelope-ready today"* — explicitly lists `veil`, `heightDelta`, `quantiseLevels`, `gradientAngleDegrees`, `gradientCentreX/Y`, `gradientSize`, `gradientDepthPixels`, `rampInputLow/High`, `textureTilesX/Y`, `textureOffsetU/V`, `textureAngleDegrees`, `stripRepeats`, `stripOrientationDegrees`, `stripOffset`, `stripReach`, `heightFieldScale`, `steelCells/Octaves/Seed/RustAmount/RustReachPixels/Grain` under `ShaperFillDef.cs`. §H2 is the *opposite* list (things NOT envelope-ready: transform, sweep, shell, blend, non-star primitives, swarm). The engine confirms §H1: all of those fields are `ZUIValue`. **The mock cited the wrong section and regressed 19 animatable fields to fixed.**

**C2. §H2's own list is correct and the mock is right to keep those plain.** Verified: `ShaperSweep` (`ShaperNode.cs:49-56`), `ShaperShell.thickness` (`:68`), `ShaperBlend` (`:24-28`), non-star primitive dials (`ShaperPrimitives.cs:63-93`), `ShaperSwarmDef` numerics (`:115-142`) are all plain in the engine. No contradiction — recorded so a future pass doesn't "fix" them wrongly.

**C3. The mock made `edgeGlowColour` a `ZuiGradient`; the engine has a plain `Color`.** `ShaperSolids.cs:77` is `Color edgeGlowColour = Color.white`. `ShaperMockSolids.cs:97` is `ZuiGradient`. This was a deliberate T-0139 demonstration of colour-as-envelope, documented as such — but it means the mock shows a control the engine cannot currently accept. Phase C must either promote the engine field or draw a `Color`.

**C4. `ShaperDocument` has no `frameCount`/`fps`; the mock's canvas invented both.** `ShaperMockData.cs:83,93`. The engine's clock is `phase01` alone (`ShaperLightRig.cs:413`). **Being resolved right now** by the Phase A2 `ShaperClock.cs` work — recorded because the mock's comment at `:89-91` correctly identified it, and Phase C must consume the *new* engine API rather than the mock's invented fields.

**C5. `ShaperCompositeReason` has 2 values, not more.** `{AuthoredData=0, NotYetSplit=1}` (`ShaperCompositeDef.cs:14`). Mock matches. Recorded because `PyreCompositeCatalog:11` states all 9 generators are `NotYetSplit` and **0** are `AuthoredData` — so the `AuthoredData` branch of any UI is currently unreachable via the shipped catalog and must not be presented as the common case.

**C6. T-0138's audit claimed full coverage after its 18 todos; three of its own areas remain uncovered.** `transform`, `normalConstant`, and the fill `veil`/`heightDelta`/gradient-centre family are absent from the mock today, and T-0138's task record (`:133`) reports "todos #2-18 … built and verified". The audit's scope evidently did not enumerate `ShaperTransformBlock` or `ShaperLightResponse.normalConstant` at all. **Do not treat T-0138 as proof of coverage.**

**C7. Engine ranges are wider than the mock's pickers.** `ngonSides [Range(3,64)]` vs mock 3..16; `starArms [Range(2,20)]` vs mock 3..12. The mock's narrower clamps would silently prevent authoring values the engine accepts.

---

## OPEN DECISIONS — each with a default so nothing blocks

**D1. How does a document author a Solid?** `ShaperNodeKind` has 3 values and no node field references `ShaperSolidDef`; the resolver reads a separate `scene.solid[owner]` slot. Still unresolved.
→ *Default:* keep the mock's 4th node kind as the authoring shape and add `ShaperNodeKind.Solid = 3` (append-only) plus `ShaperNode.solid` in Phase A/C, because it is the only option that makes Solids reachable from the existing tree UI with no new document concept.

**D2. Should `ShaperTransformBlock` become `ZUIValue` before the UI is built?** Design doc §J4.5 recommends it, unscheduled. The owner has just ruled that fixed-vs-animatable is decided *before* more UI is built.
→ *Default:* build the transform UI now against the **current plain types** (closing gap #1 immediately, which is worth more than animatability), and raise the promotion as its own engine task rather than blocking the port.

**D3. Per-layer `response`/`height` — move the mock's model, or keep node-level?** The engine is unambiguous (they are `ShaperLayer` fields).
→ *Default:* the real UI binds to `ShaperLayer`; the node-level mock fields simply do not port.

**D4. Should the one `Stuck` effect (`EdgeWarpModifier`) get a host before the UI ships?** Design doc §J4.6.
→ *Default:* no — show it in the catalog greyed with its `IsAvailable` reason, so its absence is *declared* rather than silent. That matches the engine's own "declare, don't hide" posture in `ShaperSolids.InertReason`.

**D5. `edgeGlowColour` — promote engine to gradient, or demote UI to `Color`?** (C3)
→ *Default:* draw a `Color` in Phase C and keep the gradient demonstration out of the ported tool until the engine field changes.

**D6. `ShaperDocument.seed` is `uint`; UI int fields cannot express the top half of the range.**
→ *Default:* draw it as a hex/uint text field or clamp to `int.MaxValue` and document the clamp.

---

## UNVERIFIED

- The exact per-form authored parameters of the nine `PyreForm` subclasses (OrbForm, TorchForm, JetForm…) were not enumerated — they live in `Runtime/Pyre/Forms/` in the other worktree and are reached only through `PyreFormCompositeSource.form`. Phase C should reflect over them rather than hand-listing. Would need: a field dump of each `PyreForm` subclass.
- Whether `ShaperLayer.height` being `null` is handled distinctly from `technique = Flat` throughout the compiler. Would need: a read of `ShaperHeightCompiler.cs` null paths.
- `PyreFormCompositeSource.Render` notes a "Static-value-only resolution" simplification (`:44`) — whether that limits animating a composite's own dials. Would need: a read of that method plus `ShaperEvaluator`'s CompositeSample case.
