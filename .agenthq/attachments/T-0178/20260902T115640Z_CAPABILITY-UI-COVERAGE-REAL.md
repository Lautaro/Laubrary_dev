# CAPABILITY-UI-COVERAGE-REAL — field-to-control coverage audit of the real Shaper window (T-0178)

Programme: Shaper completion (T-0158), Wave 3b, task T-0178. Audited against `feat/shaper` HEAD after commit `eb40b1f6` (worktree `D:\UNITY\Laubrary Dev - Shaper`). Code-only — no Coplay/Unity CLI was used; every claim below is a source read, cited `file:line` on both sides (engine field / window control). This repeats T-0147's mock audit against the REAL window instead, per the owner's standing definition of done: "Shaper is not done until everything has a proper user-friendly UI."

## Verdict, up front

The real window is in far better shape than the 2026-09-02 SHAPER_CRISIS_ANALYSIS.md describes. That report was written against an earlier commit; waves landed since (T-0163/T-0164/T-0166/T-0168/T-0169/T-0172/T-0173/T-0174/T-0175/T-0176/T-0177 and others) closed nearly every gap it named: the light rig now has a full window (`ShaperWindow.Lights.cs`), effects now carry real reflected parameters and the pre/post-composite stage mismatch is gone (dispatch is by list membership, not a dead per-entry flag), node transforms and swarm spawn geometry are `ZUIValue` and animate over the document's phase, and Sprite/Text primitives, Fire/Fireball composite sources, cherry framing and layer lifetime windows all have complete authoring surfaces.

Of **279 individually-verified engine fields** (every scalar/enum/asset/list field on `ShaperDocument`, `ShaperLightRig`/`ShaperLight`/`ShaperLightResponse`, `ShaperLayer`, `ShaperNode` and everything it owns — `ShaperBlend`, `ShaperSweep`, `ShaperShell`, `ShaperTransformBlock`, `ShaperSwarmDef`, `ShaperPrimitiveDef`, `ShaperSolidDef`, `ShaperCompositeDef`, `ShaperFillDef` (+`ShaperStripSlot`), `ShaperBorderDef`, `ShaperHeightDef`, `ShaperEffectRef`, `ShaperCherryFrame`, `ShaperLayerMask`):

| Bucket | Count | Meaning |
|---|---|---|
| **FULL** | 273 | A compliant `Z.*`/`Val`/`Val2D`/`Dial` control exists, correctly typed per `ui-layout-rules.md` (enum→radio/segmented, bounded scalar→slider, `ZUIValue`→`Val`, X/Y pair→`Val2D`, reference→picker, declared name→text field), Undo-wired through `Change`/`Val`/`Val2D`, with a real tooltip. |
| **PARTIAL** | 1 | Control exists for the type but the field itself has no control at all in one specific context — see gap list. |
| **NONE** | 1 | Serialized, user-meaningful field with no control anywhere in the window. |
| **N/A** | 4 | Not user-facing by design — internal identity/scratch state, verified against the engine source that treats it that way. |

Additionally, the **nine hosted `PyreForm` composite generators** (~775 authored fields per the crisis inventory, e.g. ArcBurstForm alone ~187) plus the **Fire** (~25 fields) and **Fireball** (~12 fields) composite sources are drawn entirely by `ZuiReflect.FlowFields` (`ShaperWindow.Sections.cs:1016-1036`). These are NOT hand-listed and were not itemized field-by-field in the table below — instead the reflection drawer itself (`Zui/Toolkit/ZuiReflect.cs`) was read and confirmed to map every type correctly per the rulebook (see "Reflection path" section). That is ~812 further fields classified **FULL (reflection-verified)**, for a combined total of **≈1,085 fields with a proper control**.

## Reflection path — why the hosted generator dials count as FULL

`ZuiReflect.cs` (`Zui/Toolkit/ZuiReflect.cs`) is the drawer both the Composite card (`ShaperWindow.Sections.cs:1022`) and every Effect card (`ShaperWindow.Sections.cs:1561`) hand off to. Read in full for this audit; confirmed mappings:

- Enum → `Z.MiniRadio` (line 73); `[Flags]` enum → `Z.SegmentedMulti` (lines 56-67).
- `float`/`int` with `[Range]` → bounded `Z.MicroSlider` (lines 306-341).
- `bool` → `Z.Toggle` (line 341).
- `Color` → `Z.Color` (line 356); `Gradient` → `Z.Gradient` (line 359); `ZuiGradient` → `ZuiGradientControl` (line 481).
- `ZUIValue` (+ optional `[Range]`) → `Z.Value` with the range applied (lines 401-442) — the same animatable control `Val` gives hand-built fields.
- `Vector2` with `[Range]` → a min/max pair control (lines 373-386); a `[ZUIPair2D]`-tagged X/Y pair → a Value2D-style control (line 262).
- `UnityEngine.Object`-derived → an object picker (line 390).

This is exactly the rulebook's per-type mapping, so a field reached only through this drawer is fairly classified FULL without being individually re-verified — the thing to audit is the drawer, once, not each of ~800 fields it serves. The one thing NOT verified by this audit: whether every one of those ~812 fields actually carries a helpful, per-field tooltip (`ZuiReflect.cs:1594`'s `TooltipFor` synthesizes a generic one — `"{NicifiedName} — a {generator} parameter"` — for fields with no `[Tooltip]` of their own; FireCompositeSource/FireballCompositeSource DO carry hand-written `[Tooltip]`s on several fields, e.g. `FireCompositeSource.cs:76,79,86,93,98,109,112,115,128,131,139`, but the nine `PyreForm` classes were not opened field-by-field to check theirs — that would be its own task).

## Full table

Grouped by owning type, in the order a user encounters them (Document → Lights → Layer → Node/Shape → Modifiers → Swarm → Solid → Composite → Fill → Border → Height → Effects → Cherry → Mask). `src` = engine `file:line`; `ctrl` = window `file:line`.

### Document

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| canvasWidth | ShaperDocument.cs:54 | **FULL** | ShaperWindow.cs:289-290 (Dial → MicroSlider) |  |
| canvasHeight | ShaperDocument.cs:56 | **FULL** | ShaperWindow.cs:291-292 (Dial) |  |
| pixelSize | ShaperDocument.cs:58 | **FULL** | ShaperWindow.cs:293-294 (Dial) |  |
| pixelsPerUnit | ShaperDocument.cs:71 [Range(1,64)] | **FULL** | ShaperWindow.cs:329-332 (Dial) |  |
| background | ShaperDocument.cs:79 | **FULL** | ShaperWindow.cs:321-328 (Z.Color) |  |
| layerSpacing | ShaperDocument.cs:98 | **FULL** | ShaperWindow.cs:295-297 (Dial) |  |
| phase01 | ShaperDocument.cs:108 | **N/A** | — | Transient render scratch var, saved/restored per-call by ShaperDocumentRenderer.cs:217,220,301. Never authored. |
| seed | ShaperDocument.cs:112 (uint) | **FULL** | ShaperWindow.cs:307-318 (Z.Int, uint-safe clamp) |  |
| frameCount | ShaperDocument.cs:121 [Min(1)] | **FULL** | ShaperWindow.cs:300-302 (Dial) |  |
| frameRate | ShaperDocument.cs:134 [Range] | **FULL** | ShaperWindow.cs:303-306 (Dial) |  |
| cherryEnabled | ShaperDocument.cs:145 | **FULL** | ShaperWindow.Cherry.cs:90-98 (Section header toggle) |  |
| cherryFrames | ShaperDocument.cs:147 (List<ShaperCherryFrame>) | **FULL** | ShaperWindow.Cherry.cs:104-476 (grid, drag-reorder, popover) — per-element fields in Cherry group |  |
| cherryLoopDelaySeconds | ShaperDocument.cs:150 [Min(0)] | **FULL** | ShaperWindow.Cherry.cs:148-151 (Z.MicroSlider) |  |
| previewZoundFrame | ShaperDocument.cs:156 | **FULL** | ShaperWindow.Cherry.cs:503-510 (Z.MicroSlider) |  |
| previewZoundName | ShaperDocument.cs:160 | **FULL** | ShaperWindow.Cherry.cs:528-541 (Zound picker button) |  |
| previewBackSplash | ShaperDocument.cs:182 | **FULL** | ShaperWindow.Preview.cs:113-118 (BackSplashZui.Build) |  |
| layers | ShaperDocument.cs:82 | **FULL** | ShaperWindow.cs:339-480 (list mgmt) — see Layer group |  |
| lightRig | ShaperDocument.cs:101 | **FULL** | ShaperWindow.Lights.cs:39-77 — see LightRig/Light groups |  |
| effects | ShaperDocument.cs:200 (PostComposite list) | **FULL** | ShaperWindow.Sections.cs:1455-1489 — see Effects group |  |

### LightRig

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| ambientColour | ShaperLightRig.cs:118 | **FULL** | ShaperWindow.Lights.cs:54-55 (Z.Color) |  |
| ambientIntensity | ShaperLightRig.cs:133 (ZUIValue) | **FULL** | ShaperWindow.Lights.cs:56-57 (Val) |  |
| lights | ShaperLightRig.cs:136 (List, cap 8) | **FULL** | ShaperWindow.Lights.cs:59-87 (card list, cap-aware Add) |  |

### Light

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| name | ShaperLightRig.cs:21 | **FULL** | ShaperWindow.Lights.cs:134-136 (Z.TextInput, declares) |  |
| enabled | ShaperLightRig.cs:22 | **FULL** | ShaperWindow.Lights.cs:120-121 (header Toggle) |  |
| kind | ShaperLightRig.cs:23 (enum) | **FULL** | ShaperWindow.Lights.cs:123-126 (Z.Segmented) |  |
| colour | ShaperLightRig.cs:36 | **FULL** | ShaperWindow.Lights.cs:139-143 (Z.Color) |  |
| intensity | ShaperLightRig.cs:43 (ZUIValue) | **FULL** | ShaperWindow.Lights.cs:144 (Val) |  |
| yaw | ShaperLightRig.cs:46 (ZUIValue) | **FULL** | ShaperWindow.Lights.cs:163-169 (Val2D w/ pitch; disabled+reasoned when Point) |  |
| pitch | ShaperLightRig.cs:55 (ZUIValue) | **FULL** | ShaperWindow.Lights.cs:163-169 (same Val2D) |  |
| posX | ShaperLightRig.cs:58 (ZUIValue) | **FULL** | ShaperWindow.Lights.cs:172-179 (Val2D w/ posY; disabled+reasoned when Directional) |  |
| posY | ShaperLightRig.cs:60 (ZUIValue) | **FULL** | ShaperWindow.Lights.cs:172-179 (same Val2D) |  |
| posZ | ShaperLightRig.cs:66 (ZUIValue) | **FULL** | ShaperWindow.Lights.cs:183-185 (Val, disabled+reasoned) |  |
| range | ShaperLightRig.cs:74 (ZUIValue) | **FULL** | ShaperWindow.Lights.cs:186-190 (Val, disabled+reasoned) |  |
| specular | ShaperLightRig.cs:81 (ZUIValue) | **FULL** | ShaperWindow.Lights.cs:145-146 (Val) |  |

### LightResponse

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| receiveLighting | ShaperLightRig.cs:217 | **FULL** | ShaperWindow.Sections.cs:1220-1229 (header Toggle) |  |
| intensityScale | ShaperLightRig.cs:225 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:1232 (Val) |  |
| castShadows | ShaperLightRig.cs:242 | **FULL** | ShaperWindow.Sections.cs:1225 (Z.Toggle) |  |
| receiveShadows | ShaperLightRig.cs:245 | **FULL** | ShaperWindow.Sections.cs:1227 (Z.Toggle) |  |
| rimStrength | ShaperLightRig.cs:252 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:1233 (Val) |  |
| specular | ShaperLightRig.cs:272 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:1235 (Val) |  |
| specularPower | ShaperLightRig.cs:275 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:1236 (Val) |  |
| specularTint | ShaperLightRig.cs:283 | **FULL** | ShaperWindow.Sections.cs:1238 (Z.Color) |  |
| normalKind | ShaperLightRig.cs:288 (enum) | **FULL** | ShaperWindow.Sections.cs:1242-1246 (Z.Segmented) |  |
| normalConstant | ShaperLightRig.cs:295 (Vector3) | **FULL** | ShaperWindow.Sections.cs:1256-1260 (Z.Pad over x/y; z unused by v1 straight-down resolve) |  |
| rimPower | ShaperLightRig.cs:304 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:1234 (Val) |  |

### Layer

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| name | ShaperLightRig.cs:317 | **FULL** | ShaperWindow.cs:440-441 (Z.TextInput) |  |
| enabled | ShaperLightRig.cs:318 | **FULL** | ShaperWindow.cs:434-435 (row Toggle) |  |
| root | ShaperLightRig.cs:319 [SerializeReference] | **FULL(structural)** | ShaperWindow.cs:186-518 — the entire Shape/Modifiers/Swarm/Composite/Fill/Border tree is this field |  |
| response | ShaperLightRig.cs:320 | **FULL(structural)** | ShaperWindow.Sections.cs:1212-1265 — see LightResponse group |  |
| id | ShaperLightRig.cs:333 | **N/A** | — | Internal stable identity, lazily allocated by ShaperDocument.IdOf inside the mask picker's own Undo scope (ShaperWindow.Sections.cs:1365-1368). Never authored directly, matches project "never type a reference string" rule. |
| mask | ShaperLightRig.cs:340 | **FULL(structural)** | ShaperWindow.Sections.cs:1323-1431 — see LayerMask group |  |
| contributesToPicture | ShaperLightRig.cs:351 | **FULL** | ShaperWindow.Sections.cs:1424-1428 (Z.Toggle, Mask card) |  |
| zOffset | ShaperLightRig.cs:371 (ZUIValue) | **FULL** | ShaperWindow.cs:444-446 (row Val "Z") |  |
| height | ShaperLightRig.cs:388 [SerializeReference] | **FULL(structural)** | ShaperWindow.Sections.cs:1273-1314 — see Height group |  |
| startFrame | ShaperLightRig.cs:396 | **FULL** | ShaperWindow.cs:376-394 (Z.MinMax "Lifetime", paired with endFrame) |  |
| endFrame | ShaperLightRig.cs:400 | **FULL** | ShaperWindow.cs:376-394 (same Z.MinMax) |  |
| effects | ShaperLightRig.cs:413 (PreComposite list) | **FULL** | ShaperWindow.Sections.cs:1450-1453 — see Effects group |  |

### Node

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| name | ShaperNode.cs:205 | **FULL** | ShaperWindow.Sections.cs (drill breadcrumb shows node.name); node body has no direct rename field for non-root nodes — see gap list |  |
| enabled | ShaperNode.cs:206 | **FULL** | ShaperWindow.cs (bag child row toggle, ShaperWindow.Sections.cs:1147-1212 children list) |  |
| kind | ShaperNode.cs:207 (enum Primitive/Bag/Composite/Solid) | **FULL** | ShaperWindow.cs:513-518 (Z.MiniRadio) |  |
| mode | ShaperNode.cs:210 (enum ShaperCombineMode) | **FULL** | ShaperWindow.Sections.cs:709-713 (Z.Segmented, Modifiers card) |  |
| children | ShaperNode.cs:228 [SerializeReference] | **FULL(structural)** | ShaperWindow.Sections.cs:1147-1212 (add/reorder/drill/remove) |  |
| composite | ShaperNode.cs:237 | **FULL(structural)** | ShaperWindow.Sections.cs:972-1143 — see Composite group |  |
| solid | ShaperNode.cs:254 | **FULL(structural)** | ShaperWindow.Sections.cs:154-218 — see Solid group |  |
| fill | ShaperNode.cs:276 (nullable) | **FULL(structural)** | ShaperWindow.Sections.cs:250-327 — see Fill group |  |
| border | ShaperNode.cs:299 (nullable) | **FULL(structural)** | ShaperWindow.Sections.cs:646-692 — see Border group |  |

### Blend

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| widthDial | ShaperNode.cs:33 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:714-716 (Val) |  |
| sharpnessDial | ShaperNode.cs:35 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:717-718 (Val) |  |
| carveStrengthDial | ShaperNode.cs:37 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:719-720 (Val) |  |

### Sweep

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| enabled | ShaperNode.cs:97 | **FULL** | ShaperWindow.Sections.cs:725-726 (Z.Toggle) |  |
| startDegreesDial | ShaperNode.cs:100 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:727-728 (Val, cyclic) |  |
| extentDegreesDial | ShaperNode.cs:102 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:729-731 (Val) |  |
| startFractionDial | ShaperNode.cs:105 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:732-733 (Val) |  |
| extentFractionDial | ShaperNode.cs:107 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:734-735 (Val) |  |

### Shell

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| enabled | ShaperNode.cs:162 | **FULL** | ShaperWindow.Sections.cs:741-742 (Z.Toggle) |  |
| thicknessDial | ShaperNode.cs:164 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:743-744 (Val) |  |
| alignment | ShaperNode.cs:165 (enum ShaperShellAlignment) | **FULL** | ShaperWindow.Sections.cs:745-748 (Z.Segmented) |  |

### Transform

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| translateX/Y | ShaperMatrix.cs:122-123 (ZUIValue pair) | **FULL** | ShaperWindow.cs:660-665 (Val2D "Translate") |  |
| originX/Y | ShaperMatrix.cs:132-133 (ZUIValue pair) | **FULL** | ShaperWindow.cs:666-669 (Val2D "Origin") |  |
| scaleX/Y | ShaperMatrix.cs:126-127 (ZUIValue pair) | **FULL** | ShaperWindow.cs:670-674 (Val2D "Scale") |  |
| skewX/Y | ShaperMatrix.cs:129-130 (ZUIValue pair) | **FULL** | ShaperWindow.cs:675-678 (Val2D "Skew") |  |
| rotationDegrees | ShaperMatrix.cs:125 (ZUIValue) | **FULL** | ShaperWindow.cs:680-682 (Val, cyclic) |  |

### Swarm

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| enabled | ShaperSwarmDef.cs:119 | **FULL** | ShaperWindow.Sections.cs:763-764 (Section header toggle) |  |
| count | ShaperSwarmDef.cs:122 [Range(1,64)] | **FULL** | ShaperWindow.Sections.cs:779-780 (Dial, hard-cap tooltip) |  |
| seed | ShaperSwarmDef.cs:126 (uint) | **FULL** | ShaperWindow.Sections.cs:783-786 (Z.Int) |  |
| positionJitterX/Y | ShaperSwarmDef.cs:131-132 (ZUIValue pair) | **FULL** | ShaperWindow.Sections.cs:794-799 (Val2D) |  |
| rotationJitterDegreesDial | ShaperSwarmDef.cs:135 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:787-788 (Val) |  |
| scaleJitterDial | ShaperSwarmDef.cs:139 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:789-790 (Val) |  |
| lifetimeStagger | ShaperSwarmDef.cs:150 [Range(0,1)] | **FULL** | ShaperWindow.Sections.cs:922-923 (Dial, Stagger-mode only) |  |
| shape | ShaperSwarmDef.cs:158 (enum) | **FULL** | ShaperWindow.Sections.cs:823-826 (Z.MiniRadio) |  |
| spawnMode | ShaperSwarmDef.cs:161 (enum) | **FULL** | ShaperWindow.Sections.cs:836-839 (Z.Segmented, hidden for Line shape) |  |
| spawnerRadius | ShaperSwarmDef.cs:166 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:840-844 (Val "Radius"/"Half length") |  |
| spawnerOffsetX/Y | ShaperSwarmDef.cs:169-170 (ZUIValue pair) | **FULL** | ShaperWindow.Sections.cs:853-857 (Val2D "Centre offset") |  |
| spawnerRotationDegrees | ShaperSwarmDef.cs:173 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:845-846 (Val "Turn") |  |
| spawnerPitchDegrees | ShaperSwarmDef.cs:176 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:847-849 (Val "Tilt") |  |
| spawnerYawDegrees | ShaperSwarmDef.cs:178 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:850-851 (Val "Yaw") |  |
| distribution | ShaperSwarmDef.cs:182 [Range(0,1)] | **FULL** | ShaperWindow.Sections.cs:862-863 (Dial, Area-mode only) |  |
| gridReverse | ShaperSwarmDef.cs:186 | **FULL** | ShaperWindow.Sections.cs:864-866 (Z.Toggle, Area-mode only) |  |
| spawnOrderChaos | ShaperSwarmDef.cs:190 [Range(0,1)] | **FULL** | ShaperWindow.Sections.cs:955-957 (Dial, Window/FrameStep only) |  |
| pathProgress | ShaperSwarmDef.cs:194 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:871-873 (Val, Path-mode only) |  |
| evenSpacing | ShaperSwarmDef.cs:201 | **FULL** | ShaperWindow.Sections.cs:874-876 (Z.Toggle, Path-mode only) |  |
| pathSpread | ShaperSwarmDef.cs:205 [Range(0,1)] | **FULL** | ShaperWindow.Sections.cs:878-881 (Dial, Path+evenSpacing only) |  |
| orient | ShaperSwarmDef.cs:208 (enum) | **FULL** | ShaperWindow.Sections.cs:885-891 (Z.MiniRadio) |  |
| scaleByIndex | ShaperSwarmDef.cs:213 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:892-894 (Val) |  |
| timing | ShaperSwarmDef.cs:219 (enum) | **FULL** | ShaperWindow.Sections.cs:911-915 (Z.MiniRadio) |  |
| spawnTiming | ShaperSwarmDef.cs:224 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:931-933 (Val, Window-mode only) |  |
| firstSpawnPhase | ShaperSwarmDef.cs:230 [Range(0,1)] | **FULL** | ShaperWindow.Sections.cs:941-943 (Dial, converted via ShaperClock, FrameStep-mode only) |  |
| spawnPhaseStep | ShaperSwarmDef.cs:234 [Range(0,1)] | **FULL** | ShaperWindow.Sections.cs:944-947 (Dial, FrameStep-mode only) |  |
| instanceLife | ShaperSwarmDef.cs:239 [Range(0.01,1)] | **FULL** | ShaperWindow.Sections.cs:950-951 (Dial, Window/FrameStep only) |  |
| dieTogether | ShaperSwarmDef.cs:245 | **FULL** | ShaperWindow.Sections.cs:952-954 (Z.Toggle, Window/FrameStep only) |  |
| merge.widthDial | ShaperSwarmDef.cs:249 (ShaperBlend) | **FULL** | ShaperWindow.Sections.cs:807-808 (Val "Merge width") |  |
| merge.sharpnessDial | ShaperSwarmDef.cs:249 | **FULL** | ShaperWindow.Sections.cs:809-810 (Val "Merge sharpness") |  |
| merge.carveStrengthDial | ShaperSwarmDef.cs:249 | **PARTIAL** | No control anywhere in ShaperWindow.Sections.cs's BuildSwarmSection (807-810 shows only width+sharpness) | The engine field exists and is sampled by ShaperBlend.Sample alongside the other two — unlike node.blend (all 3 shown, Sections.cs:714-720), the swarm merge blend exposes only 2 of 3. No comment declares carveStrength inert for merge; likely just missed when the row was packed. RULE VIOLATED: every serialised field needs SOME control per the task's own goal ("everything has a proper UI"). |
| interact | ShaperSwarmDef.cs:259 | **FULL** | ShaperWindow.Sections.cs:804-806 (Z.Toggle) |  |

### Primitive

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| kind | ShaperPrimitives.cs:88 (enum) | **FULL** | ShaperWindow.cs:528-531 (Z.MiniRadio) |  |
| rectHalfWDial/rectHalfHDial/rectCornerRadiusDial | ShaperPrimitives.cs:91-93 | **FULL** | ShaperWindow.cs:543-546 (Val×3, Rect) |  |
| ellipseRxDial/ellipseRyDial | ShaperPrimitives.cs:96-97 | **FULL** | ShaperWindow.cs:550-552 (Val×2, Ellipse) |  |
| diamondRxDial/diamondRyDial | ShaperPrimitives.cs:100-101 | **FULL** | ShaperWindow.cs:556-558 (Val×2, Diamond) |  |
| triangleBaseDial/triangleHeightDial | ShaperPrimitives.cs:104-105 | **FULL** | ShaperWindow.cs:562-564 (Val×2, Triangle) |  |
| capsuleHalfLengthDial/capsuleRadiusDial | ShaperPrimitives.cs:108-109 | **FULL** | ShaperWindow.cs:568-570 (Val×2, Capsule) |  |
| ngonSides | ShaperPrimitives.cs:114 [Range(3,64)] (int) | **FULL** | ShaperWindow.cs:577 (Dial, whole-number family pick) |  |
| ngonRadiusDial/ngonRotationDial/ngonCornerRadiusDial | ShaperPrimitives.cs:115-118 | **FULL** | ShaperWindow.cs:578-580 (Val×3, NGon) |  |
| starArms | ShaperPrimitives.cs:122 [Range(2,20)] (int) | **FULL** | ShaperWindow.cs:585 (Dial) |  |
| starRadiusDial/starLength/starBaseWidth/starSkew | ShaperPrimitives.cs:124-131 | **FULL** | ShaperWindow.cs:586-589 (Val×4, Star) |  |
| spriteAsset | ShaperPrimitives.cs:135 (Sprite) | **FULL** | ShaperWindow.cs:596-598 (Z.Object<Sprite>, never typed) |  |
| spriteHalfWDial/spriteHalfHDial/spriteThresholdDial/spriteSoftnessDial | ShaperPrimitives.cs:136-142 | **FULL** | ShaperWindow.cs:605-609 (Val×4, Sprite) |  |
| spriteFitMode | ShaperPrimitives.cs:143 (enum) | **FULL** | ShaperWindow.cs:601-604 (Z.MiniRadio) |  |
| textFont | ShaperPrimitives.cs:149 (TMP_FontAsset) | **FULL** | ShaperWindow.cs:619-621 (Z.Object<TMP_FontAsset>) |  |
| textString | ShaperPrimitives.cs:150 (string) | **FULL** | ShaperWindow.cs:622-624 (Z.TextInput — declares content, compliant) |  |
| textSizeDial/textLetterSpacingDial/textLineSpacingDial/textWeightDial | ShaperPrimitives.cs:153-160 | **FULL** | ShaperWindow.cs:630-638 (Val×4, Text) |  |
| textAlign | ShaperPrimitives.cs:161 (enum) | **FULL** | ShaperWindow.cs:625-629 (Z.MiniRadio) |  |

### Solid

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| form | ShaperSolids.cs:34 (enum ShaperSolidForm) | **FULL** | ShaperWindow.Sections.cs:162-165 (Z.MiniRadio) |  |
| size/centreX/centreY/aspect/depth | ShaperSolids.cs:37-46 | **FULL** | ShaperWindow.Sections.cs:168-177 (SolidVal×5, per-form inertness declared) |  |
| gemSides/gemCrown/gemPavilion/ringInner | ShaperSolids.cs:49-56 | **FULL** | ShaperWindow.Sections.cs:180-187 (SolidVal×4) |  |
| yaw/tilt/roll | ShaperSolids.cs:59-63 | **FULL** | ShaperWindow.Sections.cs:190-195 (SolidVal×3) |  |
| lineWidth/lineColour | ShaperSolids.cs:70-73 | **FULL** | ShaperWindow.Sections.cs:201-205 (SolidVal + Z.Color) |  |
| edgeGlow/edgeGlowColour | ShaperSolids.cs:76-77 | **FULL** | ShaperWindow.Sections.cs:206-210 (SolidVal + Z.Color) |  |
| innerGlow/innerGlowColour | ShaperSolids.cs:80-81 | **FULL** | ShaperWindow.Sections.cs:211-215 (SolidVal + Z.Color) |  |

### Composite

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| source | ShaperCompositeDef.cs:95 [SerializeReference] | **FULL** | ShaperWindow.Sections.cs:1047-1143 (searchable generator picker; instantiates form/source, never an index) |  |
| reason | ShaperCompositeDef.cs:100 (enum) | **N/A** | ShaperWindow.Sections.cs:1000-1002 (shown read-only, Z.Text) | "A structural fact the audit checks, not an authored dial" (code comment, line 1000-1001) — set by PyreCompositeCatalog when a generator is assigned. |
| reasonNote | ShaperCompositeDef.cs:108 [TextArea(2,5)] | **NONE** | — | No control anywhere in ShaperWindow*.cs — only appears in Editor/Shaper/Audits/*.cs fixtures. Populated automatically from the catalog when a generator is picked via AssignGenerator/AssignSourceGenerator (Sections.cs:1094-1143), so most nodes never need one typed by hand, but a custom/NotYetSplit composite has no way to author or review its own declaration text in the window at all. |
| halfExtentX/halfExtentY | ShaperCompositeDef.cs:119-120 | **FULL** | ShaperWindow.Sections.cs:1003-1006 (Dial×2) |  |
| bakeWidth/bakeHeight | ShaperCompositeDef.cs:129-130 | **FULL** | ShaperWindow.Sections.cs:1007-1010 (Dial×2) |  |
| (hosted PyreForm fields) | 9 PyreForm classes, Runtime/Pyre/*.cs (~775 fields total per crisis inventory, e.g. ArcBurstForm ~187) | **FULL (reflection)** | ShaperWindow.Sections.cs:1016-1036 (ZuiReflect.FlowFields over dialOwner) | Verified the reflection drawer itself (Zui/Toolkit/ZuiReflect.cs) maps types per the rulebook: enum→MiniRadio/SegmentedMulti (lines 56-73), [Flags]→SegmentedMulti, [Range] float/int→MicroSlider (306-341), bool→Toggle (341), Color→Z.Color (356), Gradient→Z.Gradient (359), ZUIValue (+[Range])→Z.Value w/ range (401-442), [Range] Vector2→MinMax pair (373-386), UnityEngine.Object→picker (390). Not itemized field-by-field — the drawer, not this window, is the thing to audit per-field, and it is compliant. |
| (FireCompositeSource fields) | Runtime/PyreShaper/FireCompositeSource.cs (~25 fields: intensity, arms, armMode, direction, emitterWidth, emitterInset, heat, fuel, pulse, flow, buoyancy, curl, curlScale, flicker, stretch, pinch, breakup, dissipation, burn, reach, edgeCooling, ramp, threshold, contrast, subSteps, simFrames) | **FULL (reflection)** | ShaperWindow.Sections.cs:1016-1036 (same ZuiReflect.FlowFields path; Fire is offered via ShaperCompositeSourceInfoAttribute in ShowGeneratorMenu, Sections.cs:1057-1064) |  |
| (FireballCompositeSource fields) | Runtime/PyreShaper/FireballCompositeSource.cs (~12 fields: source, sourceRadius, cooling, sharpness, spread, reach, arms, mirror, ramp, threshold, contrast, simFrames) | **FULL (reflection)** | same as Fire, above |  |

### Fill

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| kind | ShaperFillDef.cs:51 (enum) | **FULL** | ShaperWindow.Sections.cs:277-280 (Z.MiniRadio) |  |
| veil | ShaperFillDef.cs:60 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:300-301 (Val) |  |
| heightDelta | ShaperFillDef.cs:68 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:302-304 (Val) |  |
| composite | ShaperFillDef.cs:75 (enum) | **FULL** | ShaperWindow.Sections.cs:285-288 (Z.Segmented) |  |
| space | ShaperFillDef.cs:83 (enum) | **FULL** | ShaperWindow.Sections.cs:289-292 (Z.Segmented) |  |
| fit | ShaperFillDef.cs:86 (enum) | **FULL** | ShaperWindow.Sections.cs:293-296 (Z.Segmented) |  |
| quantiseLevels | ShaperFillDef.cs:383 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:325-326 (Val) |  |
| solidColor | ShaperFillDef.cs:96 | **FULL** | ShaperWindow.Sections.cs:309-311 (Z.Color, Solid kind) |  |
| gradientMode/gradient/gradientTint/gradientAngleDegrees/gradientCentreX/Y/gradientSize/gradientDepthPixels | ShaperFillDef.cs:100-129 | **FULL** | ShaperWindow.Sections.cs:329-364 (BuildGradientFill — MiniRadio, Z.Gradient, Val×N conditional on mode) |  |
| rampQuantity/rampGradient/rampTint/rampInputLow/rampInputHigh | ShaperFillDef.cs:139-151 | **FULL** | ShaperWindow.Sections.cs:366-396 (BuildRampFill; presets via T-0172 picker) |  |
| texture/textureMapping/textureTilesX/Y/textureOffsetU/V/textureAngleDegrees/textureTint/textureAnimated/textureFrameColumns/Rows/Count | ShaperFillDef.cs:160-204 | **FULL** | ShaperWindow.Sections.cs:398-431 (BuildTextureFill; Object<Texture2D>, Segmented, Val×N, Toggle-gated animated block) |  |
| stripParameterisation/stripSlots/stripRepeats/stripOrientationDegrees/stripOffset/stripReach/stripPlainColor | ShaperFillDef.cs:275-318 | **FULL** | ShaperWindow.Sections.cs:433-502 (BuildStripFill; drag-reorder slot list, +Add/×Remove) |  |
| (ShaperStripSlot.color/height) | ShaperFillDef.cs (nested struct, per grep ~L25/32 relative) | **FULL** | ShaperWindow.Sections.cs:481-484 (Z.Color + Dial, inside slot row) |  |
| heightField/heightFieldScale/heightFieldTint | ShaperFillDef.cs:328-346 | **FULL** | ShaperWindow.Sections.cs:504-514 (BuildHeightFieldFill) |  |
| steelCells/steelOctaves/steelSeed/steelGrain/steelRustAmount/steelRustReachPixels/steelBaseLow/steelBaseHigh/steelRustColor | ShaperFillDef.cs:351-375 | **FULL** | ShaperWindow.Sections.cs:516-537 (BuildSteelFill) |  |
| overPhaseGradient/overPhaseTint | ShaperFillDef.cs:213-216 | **FULL** | ShaperWindow.Sections.cs:539-547 (BuildOverPhaseFill) |  |
| proceduralKind/proceduralGradient/proceduralTint/proceduralScale/proceduralOffsetU/V/proceduralAngleDegrees | ShaperFillDef.cs:221-257 | **FULL** | ShaperWindow.Sections.cs:549-577 (BuildProceduralFill) |  |
| noiseKind | ShaperFillDef.cs:224 (enum) | **FULL** | ShaperWindow.Sections.cs:569-572 (Z.Segmented, Noise kind only) |  |
| gridLineWidth/gridVertical/gridHorizontal | ShaperFillDef.cs:260-264 | **FULL** | ShaperWindow.Sections.cs:581-589 (Val + Toggle×2, Grid kind only) |  |
| dotSize/dotStagger | ShaperFillDef.cs:267-270 | **FULL** | ShaperWindow.Sections.cs:593-599 (Val + Toggle, Dots kind only) |  |

### Border

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| enabled | ShaperBorderDef.cs:36 | **FULL** | ShaperWindow.Sections.cs:661 (box.SetHeaderToggle) |  |
| alignment | ShaperBorderDef.cs:49 (enum ShaperShellAlignment) | **FULL** | ShaperWindow.Sections.cs:665-668 (Z.Segmented) |  |
| width | ShaperBorderDef.cs:69 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:664 (Val) |  |
| joinsCoverage | ShaperBorderDef.cs:90 | **FULL** | ShaperWindow.Sections.cs:669-671 (Z.Toggle) |  |
| fill | ShaperBorderDef.cs:107 (nullable ShaperFillDef) | **FULL(structural)** | ShaperWindow.Sections.cs:675-687 (reuses BuildFillBody) — see Fill group |  |

### Height

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| technique | ShaperHeight.cs:81 (enum) | **FULL** | ShaperWindow.Sections.cs:1288-1291 (Z.MiniRadio) |  |
| depth/angle/steps/curve/taper | ShaperHeight.cs:95-107 | **FULL** | ShaperWindow.Sections.cs:1293-1298 (Val×5) |  |
| bevel | ShaperHeight.cs:110 (enum) | **FULL** | ShaperWindow.Sections.cs:1300-1303 (Z.MiniRadio) |  |
| bevelAmount/bevelSteps | ShaperHeight.cs:119-126 | **FULL** | ShaperWindow.Sections.cs:1306-1309 (Val×2, shown only when bevel != None) |  |

### Effects

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| typeName | ShaperEffects.cs:94 | **FULL** | ShaperWindow.Sections.cs:1619+ (ShowAddEffectMenu — 41-entry searchable catalog, never typed) |  |
| instance | ShaperEffects.cs:101 [SerializeReference] | **FULL** | ShaperWindow.Sections.cs:1554-1563 (ZuiReflect.BuildFields over inst.Settings) |  |
| stage | ShaperEffects.cs:104 (enum) | **N/A** | — | Never shown/set per-entry in the window; per code comment (Sections.cs:1435-1437) the stage is "which list it is in — never a per-row dropdown". ShaperDocumentRenderer.cs (grepped in full) never reads ShaperEffectRef.stage at all — dispatch is purely by which of the two lists (layer.effects vs document.effects) an entry sits in. The field is vestigial; correctly not exposed. |
| enabled | ShaperEffects.cs:106 | **FULL** | ShaperWindow.Sections.cs:1520-1524 (header Z.Toggle) |  |

### Cherry

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| sourceIndex | ShaperCherry.cs:24 | **FULL** | ShaperWindow.Cherry.cs:319-323 (Z.SliderInt, single-select popover) |  |
| lengthMultiplier | ShaperCherry.cs:29 | **FULL** | ShaperWindow.Cherry.cs:331-335 (Z.MicroSlider "Length ×") |  |
| minLengthMultiplier/maxLengthMultiplier | ShaperCherry.cs:31-33 | **FULL** | ShaperWindow.Cherry.cs:337-343 (Z.MicroSlider×2 "Min ×"/"Max ×") |  |
| useMinMaxLength | ShaperCherry.cs:36 | **FULL** | ShaperWindow.Cherry.cs:325-329 (Z.Toggle "Randomise length") |  |
| multiFrame | ShaperCherry.cs:41 | **FULL** | ShaperWindow.Cherry.cs:345-349 (Z.Toggle) |  |
| multiFrameSources | ShaperCherry.cs:44 (List<int>) | **FULL** | ShaperWindow.Cherry.cs:364-416 (BuildMultiFrameSourcesEditor — chip list, +Add current frame, × remove) |  |
| multiFrameRandomSeed | ShaperCherry.cs:47 | **FULL** | ShaperWindow.Cherry.cs:411-415 (Z.MicroSlider "Seed") |  |

### LayerMask

| Field | Engine (src) | Class | Window control (ctrl) | Note |
|---|---|---|---|---|
| sourceLayerId | ShaperLayerMask.cs:66 | **FULL** | ShaperWindow.Sections.cs:1352-1372 (picker menu of sibling layers, never typed) |  |
| invert | ShaperLayerMask.cs:69 | **FULL** | ShaperWindow.Sections.cs:1389-1390 (Z.Toggle) |  |
| mode | ShaperLayerMask.cs:71 (enum) | **FULL** | ShaperWindow.Sections.cs:1385-1388 (Z.Segmented) |  |
| quantity | ShaperLayerMask.cs:73 (enum) | **FULL** | ShaperWindow.Sections.cs:1403-1406 (Z.MiniRadio, availability-gated tooltip) |  |
| fullAt | ShaperLayerMask.cs:89 (ZUIValue) | **FULL** | ShaperWindow.Sections.cs:1411-1414 (Val, hidden for Coverage quantity — "two dials for one quantity" rule) |  |
## Ranked gap list — concrete, one-task-sized fixes

Coverage is close to complete, so this list is short. Ordered by how much a real authoring session would notice.

1. **`ShaperSwarmDef.merge.carveStrengthDial` has no control.** `BuildSwarmSection` (`ShaperWindow.Sections.cs:804-810`) draws `Merge width` and `Merge sharpness` for the swarm's interact-blend but never `merge.carveStrengthDial`, even though `node.blend` (the equivalent block on a plain shape combine) draws all three (`ShaperWindow.Sections.cs:714-720`). Fix: add one more `Val(...)` beside the other two — same pattern, same file, same helper. Rule violated: ui-layout-rules.md "Be consistent across the codebase" (the same kind of value — a `ShaperBlend`'s three dials — should use the same control everywhere; here two of the three sites do and one doesn't). Before adding it, confirm carve strength is actually read for a swarm's Add-only merge (`ShaperSwarmPlacement.cs`/`ShaperFillCompiler.cs`) — if the merge path never subtracts, the omission may be intentional and the fix is a one-line comment declaring it inert (matching the Solids `InertReason` pattern) rather than a new control.
2. **`ShaperCompositeDef.reasonNote` has no control anywhere in the shipping window.** It only appears in `Editor/Shaper/Audits/*.cs` test fixtures. Today every composite a user creates goes through the generator picker (`ShowGeneratorMenu`/`AssignGenerator`/`AssignSourceGenerator`, `ShaperWindow.Sections.cs:1047-1143`), which fills `reason`/`reasonNote` from `PyreCompositeCatalog` automatically, so the gap is invisible in normal use — but there is no way to review or hand-edit that declaration text from the window (only `reason` is shown, read-only, `ShaperWindow.Sections.cs:1000-1002`). Fix: add a read-only (or editable, for a `NotYetSplit`/custom composite) `Z.TextInput`/wrapping label showing `c.reasonNote` beside the existing Reason field in `BuildCompositeSection`. Low priority — it is compliance documentation, not a look-affecting dial.
3. **`ShaperLightResponse.castShadows`/`receiveShadows` tooltips don't yet carry `ShaperLightRig.ShadowsNotComputed`.** The engine defines that exact "saved but not computed yet" limitation sentence as a `const string` for precisely this purpose (`ShaperLightRig.cs:194-196`, "LR-7.2: a future window MUST show ConsistentNotIdentical... and attach each of the other three to the SPECIFIC CONTROL it qualifies"), but the two toggles at `ShaperWindow.Sections.cs:1225,1227` use only their own short tooltip text, not that string. Not a missing control (both toggles work and persist correctly) but a rulebook-adjacent honesty gap: a user who turns Cast/Receive Shadows on gets no in-control warning that nothing renders differently yet. Fix: append `ShaperLightRig.ShadowsNotComputed` to both tooltips.
4. **`ShaperLightRig.ConsistentNotIdentical`, `RimNeedsRelief`/`RimNeedsBlackAmbientRelief`, `SilhouetteIsFlatUntilExtrusion` — the other three LR-7.2 "limitation sentences" — were not confirmed wired to their specific controls in this pass.** `RimNeedsRelief`/`RimNeedsBlackAmbientRelief` should attach to the Rim strength control (`ShaperWindow.Sections.cs:1233`) whenever the layer's surface is flat or the ambient is black; `SilhouetteIsFlatUntilExtrusion` to the Surface Direction control (`normalKind`, `ShaperWindow.Sections.cs:1242-1246`) while the only provider is Constant; `ConsistentNotIdentical` wherever a document mixes Silhouette and Solids layers. This audit read the Rim strength/Spec tooltip text at `ShaperWindow.Sections.cs:1233-1234` and did not find these exact const strings quoted verbatim — worth a follow-up grep-and-wire pass rather than a re-declared gap here, since it wasn't exhaustively chased through every conditional branch.
5. **A non-root `ShaperNode`'s name is only editable from its parent bag's child row** (`ShaperWindow.Sections.cs:1183-1184`), not from inside the node once drilled into via "Open" — a minor navigation-only nit (rename, then reopen, still works; nothing is unreachable), not a missing control.

No other gaps were found. Every other field on every audited type has a compliant control.

## Verification buckets

- **Verified by reading source (this whole report):** every `src`/`ctrl` citation in the table above, the reflection-drawer type mapping in `Zui/Toolkit/ZuiReflect.cs`, the `ShaperEffectRef.stage` dead-field claim (grepped the full `ShaperDocumentRenderer.cs` for any reference — none), the `phase01` scratch-variable claim (`ShaperDocumentRenderer.cs:217,220,301`), and the `reasonNote` NONE claim (grepped `Editor/Shaper/**/*.cs` — only audit fixtures reference it).
- **Verified by eye:** nothing — this task is code-only (no editor rights); no screenshot or live window was taken. `ZuiAudit` was NOT run — per the task brief, note it as not run, for the PM to run once editor rights are available.
- **Not verified:** whether the ~812 reflection-drawn generator fields (nine `PyreForm`s + Fire + Fireball) each carry a genuinely useful per-field tooltip beyond `ZuiReflect`'s generic fallback — spot-checked Fire/Fireball's own hand-written `[Tooltip]`s but did not open all nine `PyreForm` source files field-by-field; whether the LR-7.2 limitation-sentence wiring (gap #4) is actually missing or just not found by this pass's targeted reads; and everything a mouse would catch that a source read cannot — row-packing/wrap behaviour at real window width, whether `Z.Pad`/`Val2D` controls actually drag smoothly, and any `ZuiAudit` geometry finding.
