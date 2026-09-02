# Shaper — capability + standards-compliance inventory (code as of feat/shaper @ 1fa4f21a, 2026-09-02)

Scope: `Assets/Packages/Laubrary/Runtime/Shaper/` (45 .cs, 15,747 lines), `Editor/Shaper/` (13 .cs, 17,813 lines — of which 14,933 lines are seven `*Audit.cs` files), plus the Pyre bridge `Runtime/PyreShaper/` (7 files, 744 lines) + `Editor/PyreShaper/PyreShaperCompositeAudit.cs` (443). All paths below are relative to `Assets/Packages/Laubrary/` unless absolute. `Assets/ShaperMock/Editor/` (3,973 lines, menu `Laubrary/Shaper Mock (Prototype)` at `ShaperMockWindow.cs:28`) is a throwaway mock and is NOT treated as authoritative anywhere in this document.

---

## 1. Asset model

| Fact | Verified at |
|---|---|
| `ShaperDocument` is a plain `ScriptableObject` (not a LauAsset — no `LauAsset` base type exists in the package at all; "LauAsset" is an AssetKit editor-side concept: `Editor/AssetKit/LauAssetBrowser.cs:33`, `LauAssetElement.cs:20`, `LauAssetHook.cs:25`). | `Runtime/Shaper/ShaperLightRig.cs:396-397` — `[CreateAssetMenu(menuName = "Laubrary/Shaper Document", fileName = "Shaper Document")] public class ShaperDocument : ScriptableObject` |
| It lives in `ShaperLightRig.cs`, not in a file of its own (promoted from a `[Serializable]` class by T-0152; the file comment explains the history). | `ShaperLightRig.cs:378-396` |
| No custom `name` field — uses `Object.name` (deliberate, shadowing warning documented). | `ShaperLightRig.cs:389`, `Editor/Shaper/ShaperWindow.cs:26` |
| Document fields: `canvasWidth=96`, `canvasHeight=64`, `pixelSize=1`, `List<ShaperLayer> layers`, `layerSpacing=0.75`, `ShaperLightRig lightRig`, `phase01`, `uint seed`, `[Min(1)] int frameCount=1`, `float frameRate=ShaperClock.DefaultFrameRate`, `bool cherryEnabled`, `List<ShaperCherryFrame> cherryFrames`, `List<ShaperEffectRef> effects`, `[HideInInspector] BackSplashSettings previewBackSplash`. | `ShaperLightRig.cs:401-517` (offsets 5,7,9,12,28,31,38,42; frameCount at 447; frameRate 460; effects 516; previewBackSplash 498) |
| Second asset type: `ShaperHeightFieldPreset : ScriptableObject`, `[CreateAssetMenu("Laubrary/Shaper/Tapestry Height Field Preset")]` — note this one is NESTED under `Laubrary/Shaper/` while the document is at `Laubrary/Shaper Document`; the two Create-menu paths are inconsistent with each other. | `Runtime/Shaper/ShaperHeightFieldPreset.cs:20-21` |
| Third asset type: `ShaperClip : ScriptableObject` — deliberately NO `CreateAssetMenu` (bake output only). | `Runtime/Shaper/ShaperClip.cs:34-38` |
| **How the window obtains a document: a `Z.Object<ShaperDocument>(…)` field in a "Document" row.** `Z.Object<T>` is ZUI's thin wrapper that returns a raw UnityEditor `ObjectField` (`Zui/Toolkit/Zui.cs:427-440`). It is NOT a ZuiChip/LauAsset picker — `LauAssetHook.TryBuild` only produces a chip for types `IsAssetReference` claims, and nothing installs `ShaperDocument` there. | `Editor/Shaper/ShaperWindow.cs:153-179` (field at 161-171), `Zui/Toolkit/Zui.cs:427-440`, `Editor/AssetKit/LauAssetHook.cs:31-45` |
| **"New…" button opens a Unity save dialog**: `EditorUtility.SaveFilePanelInProject("New Shaper Document", "Shaper Document", "asset", …)`, then `CreateInstance<ShaperDocument>()`, seeds one primitive layer sized to a quarter of the canvas, `AssetDatabase.CreateAsset`, `Undo.RegisterCreatedObjectUndo`, `SaveAssets`, `PingObject`. Save location is wherever the user picks in the dialog (default folder = whatever the panel opens on; no `DefaultFolder` convention). | `ShaperWindow.cs:173-176` (button), `181-202` (`CreateDocumentAsset`), `219-234` (`NewLayer`) |
| **AssetKit is NOT used for the document.** The window extends `ZuiWindow`, not `ZuiAssetWindow<ShaperDocument>` (the UITK AssetKit base that 16 other tools use, including `PyreWindow : ZuiAssetWindow<Pyre>`). Consequently there is no library browser grid, no thumbnails, no tag section, and none of the base's `New/Duplicate/Rename/Delete` chrome. The Editor asmdef DOES reference `AssetKit.Editor`, but only `ShaperBaker` uses it, for `LauAssetBrowser.BakedMarkerPrefix`. | `ShaperWindow.cs:36`; `Editor/AssetKit/ZuiAssetWindow.cs:21` (`abstract class ZuiAssetWindow<T> : ZuiWindow`), users at `Editor/Pyre/PyreWindow.cs:20`, `Editor/Chunks/ChunkWindow.cs:22`, `Editor/Larder/LarderWindow.cs:18` etc.; `Editor/Shaper/ShaperBaker.cs:35,54`; asmdef `Editor/Shaper/com.Lautaro-Arino.Laubrary.Shaper.Editor.asmdef` |
| **Duplicate / Delete / Rename of a document: ABSENT.** Grep for `Duplicate|Rename|DeleteAsset` in Editor/Shaper hits nothing but a doc comment. `AssetLibrary<T>` provides all four for free (`Create`, `Duplicate`, `Rename`, `Delete`). | grep result; `Editor/AssetKit/AssetLibrary.cs:38,50,60,72` |
| LauTag / ZuiChip / LauAssetPicker: zero references in Runtime/Shaper or Editor/Shaper. | grep result (only hits are `AssetKit.Editor` using + `LauAssetBrowser.BakedMarkerPrefix`) |
| `IVisualPreview` (authoring.md §10, "mandatory" for any pickable visual asset): NOT implemented by `ShaperDocument` or `ShaperClip`. Ten other runtime types implement it (BackSplash, ChunkSpec, Tileset, LauminaryVersion…). | grep of `: .*IVisualPreview` in Runtime — no Shaper hit |
| `[MovedFrom]`: none in Shaper (no rename history to protect). | grep |
| Serialization shape: flat `[Serializable]` classes, with `[SerializeReference]` only on `ShaperLayer.root`, `ShaperLayer.height`, `ShaperNode.children`, `ShaperCompositeDef.source`, `PyreFormCompositeSource.form`. | `ShaperLightRig.cs:319,357`; `ShaperNode.cs:113`; `ShaperCompositeDef.cs:95`; `Runtime/PyreShaper/PyreFormCompositeSource.cs:33` |

**What AssetKit would give for free (for comparison):** `ZuiAssetWindow<T>` (`Editor/AssetKit/ZuiAssetWindow.cs:21-156`) — `BuildAsset(root, asset)` hook, `TypeLabel/DefaultFolder/NewAssetName`, `RenderThumbnail/AnimateThumbnails`, `InitializeNewAsset`, `SetAsset`, `RefreshBrowse`, a `TagsSection`, and a sealed `BuildUI` that draws the library browser + New/Duplicate/Rename/Delete over `AssetLibrary<T>` (`AssetLibrary.cs:16-72`: `FindGuids`, `Enumerate`, `Create`, `Duplicate`, `Rename`, `Delete`). The IMGUI-era equivalent `LaubraryAssetWindow<T>` (`Editor/AssetKit/LaubraryAssetWindow.cs:43-144`) has the same contract with `DrawAsset(T)` + `OnZUI`.

---

## 2. Node / layer model

**Document → Layer → Node tree.**

- `ShaperLayer` (`ShaperLightRig.cs:314-376`): `name`, `enabled`, `[SerializeReference] ShaperNode root`, `ShaperLightResponse response`, `ZUIValue zOffset`, `[SerializeReference] ShaperHeightDef height` (nullable; "Add height"/"Remove height" buttons in the window).
- `ShaperNode` (`ShaperNode.cs:87-184`): `name`, `enabled`, `ShaperNodeKind kind`, `ShaperCombineMode mode`, `ShaperBlend blend` (`width`, sharpness, carve), `ShaperTransformBlock transform`, `ShaperSweep sweep` (`enabled`, `startDegrees`, `extentDegrees`, fractions), `ShaperShell shell` (`enabled`, `thickness`, `alignment`), `ShaperSwarmDef swarm`, `ShaperPrimitiveDef primitive`, `[SerializeReference] List<ShaperNode> children`, `ShaperCompositeDef composite`, `ShaperSolidDef solid`, `ShaperFillDef fill` (nullable), `ShaperBorderDef border` (nullable).
- **Node kinds**: `ShaperNodeKind { Primitive, Bag, Composite, Solid }` (`ShaperNode.cs:18`). Nesting = a `Bag` node's `children` list, reorderable (`ShaperWindow.Sections.cs:824-888`, `ZuiReorder.MakeGrip` at 847). A Composite node's own fill/border are structurally ignored (`ShaperNode.cs:114-122`); a Solid keeps its fill (`ShaperNode.cs:124+`).
- **Combine ops**: `ShaperCombineMode { Add, Subtract, Intersect }`, `ShaperShellAlignment { Centred, Inward, Outward }` (`ShaperOps.cs:11,14`); soft blends via `SmoothMinShaped/SmoothMaxShaped`, `SubtractSoftRaw`, `RadialWedge`, `LongitudinalSlab`, `Shell` (`ShaperOps.cs:36-130`).

**Primitives** — `ShaperPrimitiveKind { Rect, Ellipse, Diamond, Triangle, Capsule, NGon, Star }` (`ShaperPrimitives.cs:9-18`); SDF functions `ShaperSdf.Rect/Ellipse/Diamond/Triangle/Capsule/NGon/Star` (`ShaperSdf.cs:50,126,209,227,245,266,300`). Dials (`ShaperPrimitives.cs:60-100`): rect halfW/halfH/cornerRadius; ellipse rx/ry; diamond rx/ry; triangle base/height; capsule halfLength/radius; ngon radius/sides/rotation/cornerRadius; star radius/points + **`ZUIValue` starLength/starBaseWidth/starSkew** (the only animatable primitive dials — confirms "ZUIValue is per-field"). Sweep axis `ShaperSweepAxis { Radial, Longitudinal }` (`:25`).

**Solids** (T-0155) — `ShaperSolidForm { Box, Pyramid, Can, Orb, Gem, Ring }` (`ShaperSolids.cs:8-16`); 14 dials `ShaperSolidDial { Size, Centre, Aspect, Depth, GemSides, GemCrown, GemPavilion, RingInner, Yaw, Tilt, Roll, LineWidth, EdgeGlow, InnerGlow }` (`:88-93`); every scalar on `ShaperSolidDef` is a `ZUIValue` (`:37-80`) plus three `Color`s; a 6×14 inertness table `ShaperSolids.InertReason` drives greyed dials (`ShaperWindow.Sections.cs:130-138` doc).

**Composite** (T-0112) — `ShaperCompositeDef` (`ShaperCompositeDef.cs:89-134`): `[SerializeReference] IShaperCompositeSource source`, `reason` (`AuthoredData | NotYetSplit`), `reasonNote`, `halfExtentX/Y=64`, `bakeWidth/Height=128`. `IShaperCompositeSource.Render(w,h,phase01,seed,Color32[])` (`:42-55`). The engine deliberately does NOT reference Pyre (`:34-36`); see §3.

**Swarm** (T-0113) — `ShaperSwarmDef` (`ShaperSwarmDef.cs:109-165`): `enabled`, `uint seed`, `Vector2 positionJitter`, `rotationJitterDegrees`, `ShaperBlend merge`, `interact`, `SimulationHardCap=6`; `ShaperSwarmImplementation { None, Generic, Native }` (`:14-34`); hosts may implement `IShaperSwarmNativeSource` / `IShaperSimulationSource`. Available on every node kind (`ShaperNode.cs:104-107`). Window section: Count, Position jitter (Z.Pad), Rotation/Scale jitter, Lifetime stagger, Seed, Merge width/sharpness, Interact (`ShaperWindow.Sections.cs:638-700`).

**Fills** — `ShaperFillKind { Solid, Gradient, RampByQuantity, Texture, IndexedStrip, HeightField, TapestrySteel }` (`ShaperFillContract.cs:228-268`); `ShaperFillDef` (`ShaperFillDef.cs:48-292`) carries every kind's dials in one flat class: `veil`, `heightDelta`, `composite {Over, Add}`, `space {Stamped, Fixed}`, `fit {Uniform, Stretch}`, gradient mode `{Linear, Radial, Angular, ByEdgeDistance}` + `ZuiGradient` + angle/centre/size/depth, ramp quantity (`ShaperQuantity { Coverage, Height, EdgeDistance, Heat, Density, Soot, Depth, Age, SurfaceDirection }` `:34-45`) + gradient + input low/high, texture + mapping `{Fitted, Tiled}` + tiles/offset/angle/tint, strip parameterisation `{Angular, Projection}` + `List<ShaperStripSlot>` + repeats/orientation/offset/reach, heightField texture + scale + tint, TapestrySteel cells/octaves/seed/colours/rust/grain, `quantiseLevels`. Almost every scalar is a `ZUIValue`. Fill programs are compiled (`ShaperFillCompiler.cs:214`), resolved (`ShaperFillResolver.cs`, 1,531 lines) and executed as ops (`ShaperFillOps.cs`). A root node with no fill gets `ShaperFillDef.DefaultRootFill` (FC-3.2, `ShaperWindow.cs:205-207`).

**Borders** — `ShaperBorderDef` (`ShaperBorderDef.cs:29-107`): `enabled`, `alignment` (Inward default), `ZUIValue width`, `joinsCoverage`, nested `ShaperFillDef fill`. Resolved by `ShaperBorder` (`ShaperBorder.cs:97`); refused on Composite nodes (`ShaperNode.cs:118-120`).

**Height / normals / lighting (T-0114+)** — `ShaperHeightDef` (`ShaperHeight.cs:78-137`): `technique {Flat, Linear, Stepped, Dome, Round, Taper, Pyramid}`, `depth`, `angle`, `steps`, `curve`, `taper`, `bevel {None, Linear, Rounded, Cove, Ogee, Stepped}`, `bevelAmount`, `bevelSteps` — all `ZUIValue`. Normals: `ShaperNormalKind { Constant, Profile }` (`ShaperNormals.cs:11-34`). Light rig: `ShaperLightRig { ambientColour, ambientIntensity, List<ShaperLight> lights }`, `ShaperLight { name, enabled, kind {Directional, Point}, colour, intensity, yaw, pitch, posX/Y/Z, range, specular }` (`ShaperLightRig.cs:8-136`); per-layer `ShaperLightResponse { receiveLighting, intensityScale, castShadows, receiveShadows, rimStrength, specular, specularPower, specularTint, normalKind, normalConstant, rimPower }` (`:208-304`). Compiled by `ShaperLightCompiler`/`ShaperLightLaw` (`ShaperLightCompiler.cs:143`, `ShaperLightLaw.cs`). **The window authors the per-layer RESPONSE ("Lighting" section, `ShaperWindow.Sections.cs:889-949`) but has NO UI for the document's `lightRig` / `lights` list** — grep `lightRig|\.lights|ShaperLight\b` across `Editor/Shaper/ShaperWindow*.cs` returns nothing; lights can only be edited via the default Inspector.

**Effects** — `ShaperEffectRef { typeName, stage, enabled }` on the DOCUMENT (`ShaperEffects.cs:66-75`, `ShaperLightRig.cs:516`); `ShaperEffectStage { PreComposite, PostComposite }` (`:45-53`); `IShaperEffectApplier` (`:97`). The catalog lives in the Pyre bridge: `ShaperEffectCatalog.All` = 41 entries (`ExpectedTotal = 41`, `Runtime/PyreShaper/ShaperEffectContract.cs:95-160`): 10 Post (Dissolve, Bloom, Outline, ChromaticAberration, BallisticShockwave, Fuse, EdgeSmooth, DropShadow, Kaleidoscope, Relight), 1 Simulation (PixelFluid), 9 Pixel (Contrast, Brightness, Saturation, Posterize, ColorTint, ColorReplace, ColorRemap, OrderedDither, Wipe), 7 padded Geometry (Skew, Scale, Rotate, Wobble, CurlProgress, Smudge, PinWarp), 10 sheet-gated Geometry (SunburstWobble, RingWave, PointBlast, Sunburst, Turbulence, Curl, Profile, PulseRings, Sphere, Ground), 3 sheet-gated Pixel (Tint, VoronoiCrack, LayerDissolve), 1 Stuck (EdgeWarp). Each entry has a `ShaperEffectPortability` and a `defaultStage`. **Two load-bearing limits:** (a) `ShaperEffectRef` carries no parameters — `ShaperEffectApplier.Apply` does `Activator.CreateInstance(type)` and runs the modifier at its class defaults, resolving every `ZUIValue` to `staticValue` (`Runtime/PyreShaper/ShaperEffectApplier.cs:73-79, 83`); the Effects section draws only enable/stage-label/reorder/remove (`ShaperWindow.Sections.cs:999-1089`), so **no effect dial is authorable**. (b) `ShaperDocumentRenderer.RenderPhase` calls `effects.Apply(..., ShaperEffectStage.PostComposite, ...)` only (`Runtime/Shaper/ShaperDocumentRenderer.cs:100`); grep for a non-comment `PreComposite` in Runtime/Shaper + Editor/Shaper + the applier finds only the enum member (`ShaperEffects.cs:48`). The add-menu stamps each entry with its catalog `defaultStage` (`ShaperWindow.Sections.cs:1118-1119`), which is `PreComposite` for the 9 Pixel + 7 Geometry + 13 sheeted + EdgeWarp = 30 of 41 entries, and there is no stage switch in the row — **so 30 of the 41 addable effects are never applied by the document renderer** (the row's "Runs at PreComposite." label notwithstanding). `ShaperEffectStageRunner.RunPreComposite` exists (`ShaperEffectStageRunner.cs:43`) but nothing in the document path calls it.

**Cache layer** — `ShaperCacheKey`, `ShaperCacheMixer`, `ShaperNodeCache`, `ShaperCachedEvaluator`, `ShaperCacheBudget`, `ShaperTiltedResolveCache`, `IShaperCacheableSource`, `ShaperNodeIdentity` (folds `phase01` and every authored dial into the key; `IsNonDeterministic` flags any `ZUIValue.Mode.MinMax` — `ShaperNodeIdentity.cs:250-291`). **None of these are used by the window or the preview stage** (grep `ShaperFramePrebaker|ShaperFrameCache|ShaperCachedEvaluator|ShaperNodeCache` in `Editor/Shaper/ShaperWindow*.cs` + `ShaperPreviewStage.cs` → no hits); the preview renders every frame uncached via `ShaperDocumentRenderer.RenderFrame` (`ShaperPreviewStage.cs:183`), at a measured ~30 ms/frame (`ShaperWindow.Preview.cs:133-134`).

---

## 3. Pyre-inherited generators

- **Adapter**: `PyreFormCompositeSource : IShaperCompositeSource` with `[SerializeReference] public PyreForm form` (`Runtime/PyreShaper/PyreFormCompositeSource.cs:31-33`); `Render` builds a `PyreFormPrepareCtx`/`PyreFormCtx` and calls the unmodified `form.Render` (`:37-52`). **Named simplification: every `ZUIValue` on the hosted form resolves to `staticValue`** (`:45`) — a Pyre form's animated dials do NOT animate over the Shaper phase.
- **Catalog**: `PyreCompositeCatalog.All` has exactly nine entries — Inferno, Fork Blast, Orb, Torch, Arc Burst, Plasma Bloom, Jet, Radial Jet, Explosive Jet — each with a palette-dependent/indifferent flag and a debt note; all nine resolve to `ShaperCompositeReason.NotYetSplit` (`Runtime/PyreShaper/PyreCompositeCatalog.cs:52-62`).
- **Concrete `PyreForm` types in the repo = exactly these nine** (`Runtime/Pyre/Forms/Kiln/{ArcBurst,ForkBlast,Inferno,Orb,PlasmaBloom,Torch}Form.cs`, `Jet/{ExplosiveJet,Jet,RadialJet}Form.cs`; `JetFormBase` is abstract). The window enumerates `TypeCache.GetTypesDerivedFrom<PyreForm>()` filtered to non-abstract with a parameterless ctor, grouped by `PyreFormInfoAttribute.Group`, in a searchable `Z.Menu` (`Editor/Shaper/ShaperWindow.Sections.cs:771-793`). **So all nine are reachable, and no PyreForm is unreachable.** What is NOT reachable: anything in Pyre that is not a `PyreForm` — the `ShapeForm` enum-driven layer types on `Pyre.cs:111` (e.g. Playback3D) and Pyre's own layer/modifier stack are not hosted.
- **Dial UI is reflection-driven**: `ZuiReflect.FlowFields(host, form, new ZuiReflect.Options{…})` in a `Z.BoxKeyed(form.DisplayName + " dials")` (`ShaperWindow.Sections.cs:741-747`) — the one `ZuiReflect` site in the tool. Composite-level dials (Reason readout, Half extent X/Y, Bake W/H 16–512) are hand-drawn (`:724-736`). Assigning a form copies the catalog reason/note when the display name matches, otherwise `NotYetSplit` (`:798-822`).
- The "~775 authored fields / ArcBurst 187" figure from CLAUDE.md is not re-derivable statically here (it depends on Pyre's reflected field set); the mechanism that would expose them all is the `ZuiReflect.FlowFields` call above.
- Composite bake resolution is independent of canvas (`bakeWidth/Height`, `ShaperCompositeDef.cs:129-130`); the compiled result is a `ShaperCompiledComposite { width, height, coverage[], pixels[] }` (`:67-77`).

---

## 4. Animation

- **Clock**: `ShaperClock` (`Runtime/Shaper/ShaperClock.cs`) — `DefaultFrameRate=12`, `MinFrameRate=1`, `MaxFrameRate=30`; `PhaseOfFrame(i, N) = N<=1 ? 0 : Clamp01(i/(N-1))` (the fixed `i/(N-1)` mapping); `WrapFrame`; `AdvanceFrames(ref acc, dt, fps, maxDelta=0.1)`. `ShaperDocument.PhaseOfFrame` delegates to it (`ShaperLightRig.cs:464`); `ShaperFrameCache.PhaseOfFrame` too (`ShaperFrameCache.cs:63`). No second conversion exists (grep).
- **Document clock fields**: `frameCount` (`[Min(1)]`, default 1 = still image), `frameRate`, `phase01` (`ShaperLightRig.cs:434-460`). Window "Canvas" section exposes Width, Height, Pixel size, Layer spacing, Frames, Rate, Seed (`ShaperWindow.cs:237-281`; seed is a `Z.Int` clamped to int range with rationale at `:265-275`).
- **Animatable dials**: `ZUIValue` per field (Static / Min-Max / Curve / Steps / Oscillation, right-click menu), rendered via the window's `Val()` helper → `Z.Value` with `frameCount` passed so curve timing is in frames (`ShaperWindow.cs:675-688`). Evaluation goes through `ShaperValue` (hash-based Min-Max, never Random — `ShaperValue.cs:21-33`), not `ZUIValue.Evaluate` directly (`ShaperFillCompiler.cs:198-199`).
- **Transport** (only when `frameCount > 1`, `ShaperWindow.cs:532-533`): `▶ Play / ❚❚ Pause` button, Rate dial, `Z.SliderInt` frame scrubber; `EditorApplication.update += PlaybackTick` (`:554-638`), accumulator via `ShaperClock.AdvanceFrames`; scrubbing pauses.
- **Cherry framing** (`ShaperCherry.cs`: `ShaperCherryFrame { sourceIndex, lengthMultiplier, min/max, useMinMaxLength, multiFrame, multiFrameSources, multiFrameRandomSeed }`, `ShaperCherryState`, `ShaperCherry.AdvanceOneBeat`, `BlankFrame=-1`). Authoring panel = slot rows, not Pyre's thumbnail grid (scope call documented at `ShaperWindow.Preview.cs:135-160`); shown only when `frameCount > 1` (`ShaperWindow.cs:540`).
- **Cache / prebake**: `ShaperFrameCache` (`ShaperFrameCache.cs:27-112`: `IsFrameCached`, `CountCachedFrames`, `ComputeFrame`, `NextUncachedFrame`) and the editor-only `ShaperFramePrebaker` (`Editor/Shaper/ShaperFramePrebaker.cs:26-89`: one frame per `EditorApplication.update` tick, `MaxMillisecondsPerTick=8`, `Progressed`/`Completed` events). **Neither is wired into the window or preview** (see §2 cache note).

---

## 5. Output

| Output | State | Where |
|---|---|---|
| Sprite-sheet PNG (sliced, `SpriteImportMode.Multiple`, ≤8 columns, PPU 16 default, `importer.userData = BakedMarker` so `LauAssetBrowser` can hide it) | BUILT | `Editor/Shaper/ShaperBaker.cs:113-230` (`MaxSheetColumns` :72, `DefaultPixelsPerUnit` :63, `BakedMarker` :54) |
| Unity `AnimationClip` (`m_Sprite` ObjectReference curve on `SpriteRenderer`, `frameRate = clamp(doc.frameRate)`, `loopTime = true`, follows `PlaybackOrder` incl. cherry beats and blank keys) | BUILT — cannot express per-pass cherry variation (freezes pass 0) | `ShaperBaker.cs:249-273` |
| `ShaperClip` asset (frames[], frameRate, seed, cherry data, `sourceDocumentName`; `PlaybackDocument` rebuilds a hidden `ShaperDocument` so cherry re-runs live) | BUILT | `ShaperBaker.cs:276-310`; `Runtime/Shaper/ShaperClip.cs:38-119` |
| Bake destination: same folder as the document asset if it is one, else `folder` arg (default `"Assets"`); unique-named `<docname>.png/.anim/.asset` | BUILT | `ShaperBaker.cs:182-190` |
| Window entry: "Bake" → `ShaperBaker.Bake(document)`, logs + pings the sheet | BUILT | `ShaperWindow.cs:640-652` |
| **GIF export** | **ABSENT** — grep `\bgif\b` across Runtime/Shaper + Editor/Shaper: zero hits (T-0149 confirmed open) | — |
| Runtime player: `ShaperPlayer : MonoBehaviour` (`clip`, `framesOverride`, `fps`, `loop`, `destroyOnFinish`, `playOnAwake`, `pooled`, `Finished` event, `Play/Stop`) + `ShaperPlayerPool.Get/Release` | BUILT | `Runtime/Shaper/ShaperPlayer.cs:34-130`, `ShaperPlayerPool.cs:17-37` |
| **`IChunkAnimation` parity** (`Runtime/Chunks/IChunkAnimation.cs:9`: `Sprite[] GetFrames(); float Fps; bool Loop`) | **ABSENT** — grep `IChunkAnimation|ChunkAnimation` in Shaper: zero hits; `ShaperClip` does not implement it (T-0151 confirmed open). Existing implementers: `Zoe` (`Zoetrope/ZoeChunkAnimation.cs:26`), `SpriteChunkAnimation`, `LauminaryAnimationChunkAdapter`. | — |
| Cross-tool consumers of `ShaperClip`/`ShaperPlayer` outside Shaper | **NONE** (grep across Runtime + Editor excluding Shaper folders → empty). Zoe/Chunks/Mirage can consume only the generic AnimationClip/sprite sheet today. | — |
| Runtime document renderer (single shared path preview == bake): `ShaperDocumentRenderer.RenderFrame/RenderPhase/RenderPhaseInto/RenderFrameInto/CompositeOver/Encode` | BUILT | `Runtime/Shaper/ShaperDocumentRenderer.cs:40-248`; consumed by `ShaperPreviewStage.cs:183` and `ShaperBaker.cs:161` |
| Runtime asmdef deps: `ZuiRuntime`, `Pooling`, `BackSplash` (no Pyre — the Pyre bridge is a separate asmdef `com.Lautaro-Arino.Laubrary.Pyre.Shaper`) | — | `Runtime/Shaper/com.Lautaro-Arino.Laubrary.Shaper.asmdef`; `Runtime/PyreShaper/*.asmdef` |

---

## 6. Editor window

- **Class**: `public partial class ShaperWindow : ZuiWindow` across `ShaperWindow.cs` (696), `ShaperWindow.Sections.cs` (1,127), `ShaperWindow.Preview.cs` (305); plus `internal sealed class ShaperPreviewStage : VisualElement` (200) (`ShaperWindow.cs:36`; `ShaperPreviewStage.cs:21`). `ZuiWindow` contract = `BuildUI(root)`, `OnBeforeRebuild`, `OnDisable` (`Zui/Toolkit/ZuiWindow.cs:16-29`). Note authoring.md §4 still says "subclass `ZUIWindow` and draw in `OnZUI()`" — that is the IMGUI generation; `ZuiWindow`/`ZuiAssetWindow` (UITK) is what Pyre and 15 other current tools use, so `ZuiWindow` is the right base family — but the ASSET variant of it is the one Shaper skipped.
- **Menu**: exactly one item, `[MenuItem("Laubrary/Shaper")]` → `GetWindow<ShaperWindow>("Shaper")`, minSize 820×520 (`ShaperWindow.cs:40-45`). No other `[MenuItem]` anywhere in Editor/Shaper or Runtime/Shaper (grep). The mock's `Laubrary/Shaper Mock (Prototype)` is outside the package (`Assets/ShaperMock/Editor/ShaperMockWindow.cs:28`).
- **ZUI vs raw counts** (grep over `ShaperWindow*.cs` + `ShaperPreviewStage.cs`): `EditorGUILayout.|EditorGUI.|GUILayout.|GUI.` = **0 real uses** (the single hit is a comment at `ShaperPreviewStage.cs:76`). Raw UITK `new …`: 19 × `new VisualElement`, 1 × `new ScrollView`, zero raw `Slider/Toggle/EnumField/PopupField/Foldout/Button/TextField`. ZUI factories used: `Z.Field` 51, `Z.HGroup` 39, `Z.Button` 25, `Z.Section` 16, `Z.Color` 14, `Z.Toggle` 12, `Z.Text` 11, `Z.Segmented` 9, `Z.MiniRadio` 8, `ZuiReorder` 7, `Z.Pad` 6, `Z.MicroSlider` 6, `Z.BoxKeyed` 5, `Z.Object` 3, `Z.Value` 2, `Z.TextInput` 2, `Z.SliderInt` 2, `Z.Menu` 2, `Z.Int` 2, `Z.Gradient` 2, `ZuiReflect` 1, `ZuiSectionToggleBar` 1, `Z.Split` 1, `Z.Box` 1. Enum → `MiniRadio`/`Segmented` throughout (no `Z.EnumDropdown`). The two `Z.TextInput`s are for DECLARING a layer/member name (`ShaperWindow.cs:343`, `Sections.cs:860`) — not reference strings, so compliant.
- **Sections & toggle bar**: `ZuiSectionToggleBar("ShaperWindow", …)` (`ShaperWindow.cs:126-141`) over Canvas, Layers, Shape, Transform (`ShaperWindow.cs`) + Fill, Border, Modifiers, Swarm, Generator, Children, Solid, Lighting, Height, Effects (`Sections.cs:120-127`). `Z.Split` left pane (document bar, canvas, layers, selected-layer sections) / right pane (preview). Breadcrumb for nested Bag members (`Sections.cs:220`).
- **Preview stage**: `ShaperPreviewStage` owns a `Texture2D`, no pixel path of its own, renders through `ShaperDocumentRenderer.RenderFrame(doc, frame, ShaperEffectApplier.Instance)` (`ShaperPreviewStage.cs:3-9, 170-188`); BackSplash backdrop via shared `BackSplashZui.Build(document.previewBackSplash, …, owner: document)` exactly as Pyre (`ShaperWindow.Preview.cs:95-121`); cherry panel (`:140-280`); preview chrome (`:56-93`).
- **Undo coverage**: one systemic wrapper — `Change(Action)` does `Undo.RecordObject(document)` BEFORE the mutation, then `SetDirty` + `RefreshPreview` (`ShaperWindow.cs:656-665`); `Dial()` routes its setter through `Change` (`:668-670`); `Val()` passes a pre-edit `RecordObject` callback to `Z.Value` (`:675-688`); `Gradient()` helper does the same for `ZuiGradient` (`Sections.cs:511-525`); `BackSplashZui` records its own (`Preview.cs:111-113`). Sample counts: `Change(` 16 + 11 + 64 = 91 sites, `Val(`/`SolidVal(` 62, `Dial(` 46; every `Z.Toggle/Color/Segmented/MiniRadio/TextInput/Int/SliderInt` data edit I sampled wraps `Change(` (e.g. `ShaperWindow.cs:395-398`, `:340-343`, `Sections.cs:453-456`, `:1024`, `:1104`) — the only non-wrapped callbacks mutate window VIEW state (`playing`, `currentFrame`, `selectedLayer`), which the guide says needs no Undo. Layer/child reorder via `ZuiReorder.MakeGrip` also wraps `Change` (`ShaperWindow.cs:324-331`, `Sections.cs:847-850`). Asset creation uses `RegisterCreatedObjectUndo` (`:194`). Verdict: Undo coverage is essentially complete for data edits.
- **View state keying**: `Z.BoxKeyed` used with stable keys for Sweep/Shell/Slots/Border fill/generator dials (`Sections.cs:425,570,605,620,741`).
- **Empty state**: document-less window shows a first-class empty screen (`ShaperWindow.cs:102`); a new document is seeded with one quarter-canvas Rect layer so first render is legible (`:203-234`, documented as a Handover-Walk finding).

---

## 7. Audit / probe files

Seven `Shaper*Audit.cs` files in `Editor/Shaper/` = 14,933 lines (84% of the editor folder): `ShaperBorderAudit` (2,282), `ShaperCacheAudit` (418), `ShaperFieldAudit` (1,934), `ShaperFillAudit` (3,208), `ShaperHeightAudit` (2,849), `ShaperLightAudit` (3,625), `ShaperSwarmAudit` (617); plus `Editor/PyreShaper/PyreShaperCompositeAudit.cs` (443). Each is a `public static class` of `public static string RunAll()` + numbered probes (`BT1_…BT14`, `V1_…V13`, `FT1_…FT19`, …) returning report strings, some writing PNG contact sheets (`ShaperBorderAudit.cs:2000-2013 BorderContactSheet(path)`). Their own headers state "Plain static methods, no `[MenuItem]` and no `EditorWindow` — invoked through the Unity CLI" (`ShaperBorderAudit.cs:11`, `ShaperCacheAudit.cs:16`, `ShaperFieldAudit.cs:17`, `ShaperFillAudit.cs:12`). They are not NUnit tests (no `[Test]`), have no `[InitializeOnLoad]`, and nothing outside the audit files references them (the only cross-refs are doc comments in Runtime files). They compile into the shipping `Shaper.Editor` asmdef. Recommendation: they are dev-only verification instruments; move to a `Tests~`/excluded folder or a separate `defineConstraints`-gated asmdef before Shaper ships to consumers — as-is every consumer project compiles ~15k lines of probes.

Also dev-only under the demo folder: `Assets/Demos/ShaperDemo/TapestryHeightFields/Editor/{CanvasVerifyProbe, HeightFieldImportTool, HeightFieldScaleProbe, SteelQuantiseProbe}.cs` in their own asmdef `ShaperTapestryImport.Editor.asmdef`.

---

## 8. Determinism

- Grep `UnityEngine\.Random|System\.Random|Random\.` over Runtime/Shaper + Editor/Shaper: every hit is a doc comment stating the ban (`ShaperCherry.cs:53-55`, `ShaperCompiler.cs:321`, `ShaperEvaluator.cs:40`, `ShaperFillContract.cs:334-335`, `ShaperFillOps.cs:9`, `ShaperLightLaw.cs:158`, `ShaperNodeIdentity.cs:257`, `ShaperPlayer.cs:30`, `ShaperSwarmDef.cs:118`, `ShaperValue.cs:21`, `ShaperFillAudit.cs:1088,1762`, `ShaperLightAudit.cs:963`). **Zero executable uses** — PASS on BC-1.3.
- Seed handling: document `uint seed` (`ShaperLightRig.cs:438`), swarm `uint seed` (`ShaperSwarmDef.cs:119`), TapestrySteel `steelSeed` `ZUIValue` (`ShaperFillDef.cs:266`), cherry `multiFrameRandomSeed` (`ShaperCherry.cs:47`), clip `seed` (`ShaperClip.cs:50`). Min-Max dials are hash-drawn from seed (`ShaperValue.cs`, `ShaperFillContract.cs:334`); avalanche hash in `ShaperCompiler.cs:321`. `ShaperNodeIdentity.IsNonDeterministic` flags any `ZUIValue.Mode.MinMax` for cache purposes (`ShaperNodeIdentity.cs:256-291`).
- One shared core: preview (`ShaperPreviewStage.cs:183`), bake (`ShaperBaker.cs:161`) and the runtime clip playback doc (`ShaperClip.cs:95-113`) all go through `ShaperDocumentRenderer`/`ShaperCherry`. PASS on authoring.md §7. Caveat: hosted Pyre forms and effects both resolve `ZUIValue` to `staticValue` (`PyreFormCompositeSource.cs:45`, `ShaperEffectApplier.cs:83`) — deterministic, but not animated.

---

## 9. Standards-compliance scorecard

| # | Rule (source) | Verdict | Evidence |
|---|---|---|---|
| 1 | Runtime/Editor split, no editor code in Runtime (authoring §2) | PASS | `Runtime/Shaper` has no `UnityEditor` using; `ShaperFramePrebaker` (the one `EditorApplication.update` user) is in `Editor/Shaper` (`ShaperFramePrebaker.cs:12`) |
| 2 | Tools ship zero assets; bake into host `Assets/` (authoring §2) | PASS | Baker writes beside the doc asset or `Assets` (`ShaperBaker.cs:182-190`); package folders contain no `.asset` |
| 3 | Asmdef naming `com.Lautaro-Arino.Laubrary.<Tool>` / `.Editor`, rootNamespace `Laubrary.<Tool>`, Editor `includePlatforms:["Editor"]` referencing runtime (authoring §3, CLAUDE.md) | PASS | `Runtime/Shaper/com.Lautaro-Arino.Laubrary.Shaper.asmdef` (rootNamespace `Laubrary.Shaper`); `Editor/Shaper/…Shaper.Editor.asmdef` (`includePlatforms:["Editor"]`, references runtime, `ZUI.Editor`, `ZuiRuntime`) |
| 4 | Editor window on the ZUI window base (authoring §4, CLAUDE.md "ZUI for ALL UI") | PASS | `ShaperWindow : ZuiWindow` (`ShaperWindow.cs:36`) |
| 5 | Asset-editing tool uses the AssetKit asset window base (`ZuiAssetWindow<T>`/`LaubraryAssetWindow<T>`) with library browser + New/Duplicate/Rename/Delete (CLAUDE.md "Undo — every tool" para: "The AssetKit base already makes New/Duplicate undoable and gates Delete"; authoring §1 "match Pyre") | **FAIL** | `ZuiWindow` not `ZuiAssetWindow<ShaperDocument>` (`ShaperWindow.cs:36`); no Duplicate/Rename/Delete/browser (grep); Pyre is `ZuiAssetWindow<Pyre>` (`Editor/Pyre/PyreWindow.cs:20`) |
| 6 | Pickable visual asset implements `IVisualPreview` (authoring §10, "treat as build-breaking") | **FAIL** | Neither `ShaperDocument` nor `ShaperClip` implements it (grep) |
| 7 | Pickers, never typed reference strings (ui-layout-rules "Control choice" 🔑; memory `never-type-a-reference-string`) | PASS | Only two `Z.TextInput`s, both DECLARE names (`ShaperWindow.cs:343`, `Sections.cs:860`); effects/generators chosen from `Z.Menu` (`Sections.cs:771-793, 1090-1127`); document via `Z.Object` (sanctioned gap, `ui-layout-rules.md:15`) |
| 8 | No native controls / no `EditorGUILayout`/`GUILayout`/raw UITK where a `Z.*` exists (ui-layout-rules; CLAUDE.md) | PASS | 0 IMGUI calls; raw UITK limited to `VisualElement`/`ScrollView` containers + the sanctioned preview stage |
| 9 | Enum → radios/segmented, never dropdown | PASS | 8 `Z.MiniRadio`, 9 `Z.Segmented`, 0 `Z.EnumDropdown` |
| 10 | Bounded scalar → `Z.MicroSlider`; `ZUIValue` → `Z.Value`; X/Y pair → `Z.Pad` | PASS | `Dial`→`Z.MicroSlider` (`ShaperWindow.cs:668-670`); `Val`→`Z.Value` (`:675-688`); 6 `Z.Pad` uses |
| 11 | Undo on every data edit, RecordObject BEFORE mutation (authoring §5; CLAUDE.md; ui-layout-rules "Undo-safe") | PASS | `Change` wrapper (`ShaperWindow.cs:656-665`), `Val` pre-edit callback (`:687`), `RegisterCreatedObjectUndo` (`:194`); sampled edit sites all wrapped (§6) |
| 12 | View-captured boxes keyed (`Z.BoxKeyed`) | PASS | `Sections.cs:425,570,605,620,741` |
| 13 | Every control has a tooltip (authoring §6) | PASS (sampled) | every `Dial/Val/Z.*` call site sampled carries a tooltip string, e.g. `ShaperWindow.cs:422-465`, `Sections.cs:605-640` |
| 14 | Menu flatness: one item at `Laubrary/<Tool>`, nothing speculative (CLAUDE.md "Menus") | PASS | single `[MenuItem("Laubrary/Shaper")]` (`ShaperWindow.cs:40`) |
| 15 | Create-asset menu consistency (same CLAUDE.md flatness spirit applied to `Assets/Create`) | PARTIAL | `Laubrary/Shaper Document` (`ShaperLightRig.cs:396`) vs nested `Laubrary/Shaper/Tapestry Height Field Preset` (`ShaperHeightFieldPreset.cs:20`) |
| 16 | Determinism, one shared core, no `Random` in generator paths (authoring §7; CLAUDE.md BC-1.3) | PASS | §8 |
| 17 | Demo = committed `.unity` scene + assets in `Assets/Demos/<Tool>Demo/` (CLAUDE.md "Demos — ship SCENES") | **FAIL** | `Assets/Demos/ShaperDemo/` contains only `TapestryHeightFields/Presets/*.asset` (~200 height-field presets), a manifest and four editor probes; `find -name '*.unity'` → none; no scene or prefab references `ShaperDocument`/`ShaperPlayer` anywhere |
| 18 | `CHANGELOG.md` entry under `[Unreleased]` for every Laubrary code change (authoring §14) | **FAIL** | `grep -i shaper CHANGELOG.md` → zero hits (package at 0.9.0, `package.json:3`) |
| 19 | Samples~ mirror of demos (CLAUDE.md "Tool conventions") | FAIL (follows from 17) | `Samples~/` has no Shaper entry |
| 20 | Cool name earned by a UI (CLAUDE.md "Naming") | PASS | Shaper has an authoring window |
| 21 | Preview overlay belongs to the effect, host hardcodes none (authoring §15, CLAUDE.md) | N/A-PASS | No per-effect overlay toggles exist in the window chrome (grep `Overlay` → none); nothing to violate yet |
| 22 | Mechanical `ZuiAudit` + Handover Walk before "done" (authoring §14, ui-layout-rules) | NOT VERIFIABLE FROM CODE | Comments cite a walk (`ShaperWindow.cs:203-217`); no audit artefact in repo |
| 23 | Dev probes not shipped in the tool asmdef (authoring §9 spirit / general packaging) | PARTIAL | seven audit files (14,933 lines) compile into `Shaper.Editor` (§7) |
| 24 | LauAsset thumbnails: a visual asset must guarantee a thumbnail (ui-layout-rules "LauAsset thumbnails") | FAIL (follows from 5/6) | No browser or picker exists, so no thumbnail path either |

**Totals: PASS 14 · FAIL 6 (#5, #6, #17, #18, #19, #24) · PARTIAL 2 (#15, #23) · N/A or unverifiable 2 (#21, #22).**

---

## 10. Capability checklist

| Capability | Where | Description / state |
|---|---|---|
| **Asset model** | | |
| ShaperDocument SO + CreateAssetMenu | `Runtime/Shaper/ShaperLightRig.cs:396-517` | canvas, layers, light rig, clock, seed, cherry, effects, preview backdrop |
| Document pick / New | `Editor/Shaper/ShaperWindow.cs:153-202` | `Z.Object` (raw ObjectField) + save-panel New; no Duplicate/Rename/Delete/browser |
| ShaperClip (bake product) | `Runtime/Shaper/ShaperClip.cs:38-119` | frames + fps + seed + cherry; rebuilds a hidden playback doc |
| ShaperHeightFieldPreset | `Runtime/Shaper/ShaperHeightFieldPreset.cs:20-38` | Tapestry height-field texture preset asset |
| IVisualPreview / LauAsset chip / tags | — | absent |
| **Generators (shape sources)** | | |
| Primitive SDFs ×7 | `ShaperPrimitives.cs:9-18`, `ShaperSdf.cs:50-300` | Rect, Ellipse, Diamond, Triangle, Capsule, NGon, Star (Star has 3 ZUIValue dials) |
| Bag (CSG tree) | `ShaperNode.cs:113`, `ShaperOps.cs` | Add/Subtract/Intersect, soft blend, sweep, shell, reorderable children |
| Solids ×6 | `ShaperSolids.cs:8-93` | Box, Pyramid, Can, Orb, Gem, Ring; 14 ZUIValue dials with inertness table |
| Composite (hosted Pyre form) ×9 | `Runtime/PyreShaper/PyreCompositeCatalog.cs:46-62`, `PyreFormCompositeSource.cs:31-52`; UI `Sections.cs:702-822` | all 9 Kiln forms; dials via `ZuiReflect`; ZUIValues static-only |
| Swarm modifier | `ShaperSwarmDef.cs:14-165`; UI `Sections.cs:638-700` | on any node; generic or native sim; seeded jitter |
| Transform block | `ShaperMatrix.cs:110`; UI `ShaperWindow.cs:473-512` | per-node transform |
| **Fills** | | |
| Solid / Gradient / RampByQuantity / Texture / IndexedStrip / HeightField / TapestrySteel | `ShaperFillContract.cs:228-268`, `ShaperFillDef.cs:48-292`; UI `Sections.cs:248-513` | veil, heightDelta, Over/Add, Stamped/Fixed, Uniform/Stretch, quantise |
| Published quantities | `ShaperFillContract.cs:34-94` | Coverage, Height, EdgeDistance (+ Heat, Density, Soot, Depth, Age, SurfaceDirection reserved) |
| **Borders** | `ShaperBorderDef.cs:29-107`, `ShaperBorder.cs:97`; UI `Sections.cs:533-584` | alignment, ZUIValue width, joinsCoverage, own fill |
| **Height / normals / lighting** | | |
| Height def (7 techniques, 6 bevels) | `ShaperHeight.cs:16-137`; UI `Sections.cs:950-998` | per-layer, ZUIValue dials |
| Normals | `ShaperNormals.cs:11-34` | Constant / Profile |
| Light rig (ambient + N lights) | `ShaperLightRig.cs:8-136` | data only — NO window UI |
| Per-layer light response | `ShaperLightRig.cs:208-304`; UI `Sections.cs:889-949` | receive/cast/shadows/rim/specular |
| **Effects** | | |
| 41-entry catalog from SpriteFx PixelModifiers | `Runtime/PyreShaper/ShaperEffectContract.cs:95-160`; UI `Sections.cs:999-1127` | add/enable/reorder/remove only; no parameters; only PostComposite stage actually runs (`ShaperDocumentRenderer.cs:100`) |
| **Animation** | | |
| Clock `i/(N-1)` | `ShaperClock.cs` | 1–30 fps, default 12 |
| ZUIValue dials (Static/MinMax/Curve/Steps/Osc) | `ShaperValue.cs`, `ShaperWindow.cs:675-688` | frame-aware curve timing |
| Transport (play/pause/rate/scrub) | `ShaperWindow.cs:554-638` | only when frameCount > 1 |
| Cherry framing | `ShaperCherry.cs`; UI `ShaperWindow.Preview.cs:140-280` | slot rows; bake-aware |
| Frame cache + prebaker | `ShaperFrameCache.cs`, `Editor/Shaper/ShaperFramePrebaker.cs` | built, unused by window |
| **Outputs** | | |
| PNG sheet + AnimationClip + ShaperClip | `Editor/Shaper/ShaperBaker.cs:113-330` | Bake button `ShaperWindow.cs:640-652` |
| GIF | — | absent (T-0149) |
| ShaperPlayer + pool | `ShaperPlayer.cs:34-130`, `ShaperPlayerPool.cs` | runtime sprite cycler |
| IChunkAnimation | — | absent (T-0151); no cross-tool consumer |
| **Window UX** | | |
| Base / menu | `ShaperWindow.cs:36-45` | `ZuiWindow`, `Laubrary/Shaper` |
| Sections + toggle bar | `ShaperWindow.cs:126-141`, `Sections.cs:120-127` | 14 sections |
| Preview stage + BackSplash + chrome | `ShaperPreviewStage.cs`, `ShaperWindow.Preview.cs:56-121` | shared renderer, ~30 ms/frame uncached |
| Undo | `ShaperWindow.cs:656-688` | `Change`/`Dial`/`Val` wrappers |
| Empty state + seeded first layer | `ShaperWindow.cs:102, 203-234` | |
| Dev audits (7 files, 14.9k lines) | `Editor/Shaper/*Audit.cs` | CLI-invoked static probes, shipped in asmdef |
