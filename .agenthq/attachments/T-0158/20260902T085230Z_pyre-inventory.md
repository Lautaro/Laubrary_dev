# Pyre — capability inventory (for the Shaper parity audit)

Scope: `Assets/Packages/Laubrary/Runtime/Pyre/` (+ `Forms/Kiln/`), `Editor/Pyre/`, bridge folders `Runtime/ZoetropePyre/`, `Runtime/Chunks/PyreSpawn/`, `Runtime/Chunks/Motion/PyreMotionModule.cs`, `Editor/Chunks/ChunkWindow.Pyre*.cs`, and the Shaper bridge `Runtime/PyreShaper/` + `Editor/PyreShaper/`. 79 source files total (63 in the Pyre core, 6 Shaper-bridge, 4 Zoetrope bridge, 4 Chunks bridge, 2 asmdef-only). All paths below are relative to `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\`. Every claim was checked against source on 2026-09-02, branch `feat/shaper`, HEAD `1fa4f21a`.

Modifiers are NOT Pyre-owned code: `PyreModifier` and every concrete modifier live in `Runtime/SpriteFx/SpriteFxModifiers.cs` (+ `SpriteFxSimulationModifiers.cs`, `SpriteFxRelight.cs`, `SpriteFxColorRemap.cs`) and are consumed by Pyre through the `com.Lautaro-Arino.Laubrary.SpriteFx` asmdef reference (`Runtime/Pyre/Pyre.asmdef`).

---

## 1. Asset model

| Fact | Where |
|---|---|
| The authored asset is `Pyre : ScriptableObject` (NOT LauAsset-derived; plain SO), `[CreateAssetMenu(menuName = "Laubrary/Pyre", fileName = "Pyre…")]`, `partial` (second half in `PyreChunksDirect.cs`). | `Runtime/Pyre/Pyre.cs:1182`, `Runtime/Pyre/PyreChunksDirect.cs:33` |
| Spec-level fields: `canvasSize` (square, `Width`/`Height` both = canvasSize), `frameCount`, `seed`, `background` Color, `pixelsPerUnit`, `backgroundUseFill` + `backgroundFill : ZuiFill`, `layers : List<PyreLayer>`, `[SerializeReference] globalModifiers : List<PyreModifier>`, plus ~15 `[HideInInspector]` preview/transport prefs stored ON the asset (`previewZoom`, `previewFps`, `previewGifScale`, `previewGifDither`, `previewFrame`, `previewShowFrame`, `previewStrip`, `previewStripSize`, `previewShowShape`, `previewShowTrace`, `cherryEnabled`, `cherryFrames`, `previewCherryStripSize`, `previewDelay`, `previewZoundFrame`, `previewZoundName`, `previewBackSplash`). | `Runtime/Pyre/Pyre.cs:1185-1257` |
| Layer model: `[Serializable] class PyreLayer` — ~180 public serialized fields per layer (loose count), incl. `[SerializeReference] PyreForm form` and `[SerializeReference] SimulationModifier simulationModifier`, `List<PyreModifier> modifiers`. | `Runtime/Pyre/Pyre.cs:180-1180` (form @318, modifiers @756, simulationModifier @766) |
| Two adapter SO assets also exist: `PyreChunkAnimation` (`[CreateAssetMenu("Laubrary/Pyre/Pyre Chunk Animation")]`, implements `IChunkAnimation, IVisualPreview`) and `PyreSpawnSource` (`[CreateAssetMenu("Laubrary/Pyre/Pyre Spawn Source")]`, implements `IChunkEffectSpawner, IVisualPreview`). | `Runtime/Pyre/PyreChunkAnimation.cs:15-16`, `Runtime/Pyre/PyreSpawnSource.cs:18-19` |
| Window base: `PyreWindow : ZuiAssetWindow<Pyre>` (AssetKit, UI-Toolkit/ZUI). Base provides New / Duplicate / Rename / Delete: New → `AssetLibrary<T>.Create` + `Undo.RegisterCreatedObjectUndo`; Duplicate → `AssetLibrary<T>.Duplicate` + undo; Rename → inline `Z.TextInput` row + `AssetLibrary<T>.Rename`; Delete → confirm dialog then `AssetLibrary<T>.Delete` (not undoable, by design). A browse grid (`LauAssetGridGUI`, animated thumbnails) is inherited. | `Editor/Pyre/PyreWindow.cs:20`, `Editor/AssetKit/ZuiAssetWindow.cs:21,163-207,226-255` |
| Default storage folder `Assets/Pyre`; `TypeLabel = "Pyre Plus"`, `NewAssetName = "New Pyre Plus"` (stale "Plus" naming survives in UI strings + undo labels). | `Editor/Pyre/PyreWindow.cs:36-38` |
| Thumbnails: `RenderThumbnail` = middle frame; `AnimateThumbnails = true` with `UpdateAnimatedThumbnail` cycling frames at `previewFps`. | `Editor/Pyre/PyreWindow.cs:40-58` |
| LauTag: the inherited `TagsSection` (an IMGUI island wrapping `LauTagField.Draw(asset)`) is added to the toggle bar as "Tags". | `Editor/AssetKit/ZuiAssetWindow.cs:64,138-142`, `Editor/Pyre/PyreWindow.cs:403,491` |
| Object pickers used inside the window are raw `Z.Object<T>` (Sprite for `spriteImage`, `TMP_FontAsset` for `textFont`, `GameObject` prefab for `playbackPrefab`) — NOT ZuiChip / LauAssetPicker. Text inputs via `Z.TextInput` (layer name, text string). Colour via `Z.Color` (playback tint). | `Editor/Pyre/PyreWindow.cs:836,1650,1776,1798,2067,2074` |
| LauAsset registry: Pyre is offered by Mirage's picker (`MirageAssetPicker.SupportedTypes = { Zoe, Pyre, ChunkSpec }`). `PyreSpawnSource` registers Create/Open with `LauAssetEditors` so its chip in Chunks has working New/Edit. | `Runtime/Mirage/MirageAssetPicker.cs:44,50`, `Editor/Pyre/PyreSpawnSourceEditorLink.cs:16-23` |
| Views: a `ZuiViewStore` asset ("Pyre Plus Views") is minted on demand at `Assets/Pyre`, undo-registered; the "Views" section saves/recalls fold presets. | `Editor/Pyre/PyreWindow.cs:345,503-509` |

## 2. Forms / shapes / generators

Two parallel systems: (a) the `ShapeForm` enum with a hard-coded if-chain in `PyreRenderer` ("the legacy if-chain", `Runtime/Pyre/PyreForm.cs:3`), and (b) `[SerializeReference] PyreForm` plug-ins held in `PyreLayer.form` (`Runtime/Pyre/Pyre.cs:318`).

### 2a. `ShapeForm` enum cases (`Runtime/Pyre/Pyre.cs:111-117`, docs @41-110)

| Case | What it does | Status |
|---|---|---|
| Disc | flat soft disc | live |
| Gem | true-3D lit faceted octahedral crystal, hard edge lines, two glows | live |
| Crescent | disc with an offset disc masked out (bite/angle/centre envelopes) | live |
| Sparkle | random lit pixel cells inside the disc, deterministic twinkle | live |
| Sprite | stamp a Sprite's pixels, scaled/rotated, optional gradient tint | live (makes spec non-parallel-safe, `PyreRenderer.cs:179-181`) |
| Box | true-3D lit cuboid via the Gem facet pipeline | live |
| Pyramid | true-3D lit square pyramid | live |
| Can | true-3D lit 16-sided cylinder | live |
| Orb | analytic lit sphere; spin/tilt rotate the LIGHTING frame not the silhouette | live |
| Ring | flat two-sided tilted annulus (Saturn ring) | live |
| Text | TMP SDF characters, one particle per char, fill + border + optional 3D extrusion; overrides swarm count | live |
| Streak | anchor-biased comet-tail capsule with own length/width envelopes | live |
| Star | filled star polygon (arms, length, base width, skew) | live |
| Fire | STATEFUL grid simulation (reuses `FireSim`), replay harness, built-in or swarm-driven emitters | live |
| Fireball | STATEFUL "doom-fire" cellular sim (reuses `FireballSim`), kaleidoscope arms | live |
| Polygon | flat regular convex N-gon | live |
| Inferno | — | `[System.Obsolete("Migrated to PyreForm … retired enum slot")]` `Pyre.cs:114`; draws as Disc if still set |
| ForkBlast | — | `[System.Obsolete(…)]` `Pyre.cs:115`; same |
| Playback3D | PROOF-OF-CONCEPT: a real 3D ParticleSystem prefab rendered in a `PreviewRenderUtility` scene, optional pixelated downsample | EDITOR-PREVIEW ONLY — "There is deliberately no runtime bake here yet" `PyreRenderer.cs:262`; UI box titled "Playback 3D (POC)" `PyreWindow.cs:1767` |

### 2b. `PyreForm` plug-ins (all `[Serializable]`, `[MovedFrom("Laubrary.PyrePlus.Forms.Kiln")]`, asmdef `com.Lautaro-Arino.Laubrary.Pyre.Forms.Kiln`)

| Form | `PyreFormInfo` | What it does | Where |
|---|---|---|---|
| ArcBurstForm | "Arc Burst", group "Kiln/Energy Explosion" | Kiln agent4 gen-4 electric arc burst: core/weave/bolt/crown/lattice/terminal/cage/stipple/pinch/lichtenberg draw stages, Burst-compiled stroke rasteriser (managed fallback with warning) | `Forms/Kiln/ArcBurstForm.cs:34-38`, `PyreArcBurst.cs:452-991`, `ArcRaster.cs:66` |
| ForkBlastForm | "Fork Blast", "Explosions" | swarm-of-puffs radial detonation, closed-form | `Forms/Kiln/ForkBlastForm.cs:13-15`, `PyreForkBlast.cs` |
| InfernoForm | "Inferno", "Explosions" | stateless volumetric fireball (port of the "Contained Fireball Explosion Lab" HTML prototype); overrides `HandlesGeometry` | `Forms/Kiln/InfernoForm.cs:13-15`, `PyreInferno.cs` |
| JetForm | "Jet", "Kiln/Flame" | directional flamethrower stream on the shared jet engine, 5 published draw presets | `Forms/Kiln/Jet/JetForm.cs:26-28`, `JetDraws.cs` |
| RadialJetForm | "Radial Jet", "Kiln/Flame" | same puff physics emitted into a 124–360° arc, 8 presets | `Forms/Kiln/Jet/RadialJetForm.cs:24-26`, `RadialJetDraws.cs` |
| ExplosiveJetForm | "Explosive Jet", "Kiln/Flame" | detonation schedule + fracture + debris on the jet engine, 10 presets | `Forms/Kiln/Jet/ExplosiveJetForm.cs:42-44`, `ExplosiveJetDraws.cs` |
| OrbForm | "Orb", "Kiln/Energy Projectile" | Kiln agent2 gen-2 energy orb | `Forms/Kiln/OrbForm.cs:34-36`, `PyreOrb.cs` |
| PlasmaBloomForm | "Plasma Bloom", "Kiln/Energy Explosion" | Kiln agent3_fork gen-5 directional plasma bloom | `Forms/Kiln/PlasmaBloomForm.cs:107-109`, `PyrePlasmaBloom.cs` |
| TorchForm | "Torch", "Kiln/Flame" | grounded flame (torch/brazier/campfire), 5 presets | `Forms/Kiln/TorchForm.cs:37-39`, `PyreTorch.cs` |

Base contract: `PyreForm` (abstract `DisplayName`, `Render(in PyreFormCtx, Color32[])`; virtual `Kind` {WholeLayer, PerParticle, Stateful}, `UsesFill`, `HandlesGeometry`, `Prepare`, `Clone`, `ContentHash`) at `Runtime/Pyre/PyreForm.cs:182-290`. Ctx carries W/H, life, seed, layerSalt, fill, alpha, swarm instances, prepared geo/pixel modifiers, phase, frameIndex/frameCount (`PyreForm.cs:96-140`). Forms are drawn in the window entirely by reflection (`ZuiReflect`) — `Editor/Pyre/PyreWindow.Forms.cs:114-123`. Supporting infrastructure: `PyreSupersample` (k× render + premultiplied box down-filter, `PyreSupersample.cs:1-24`), `PyrePrepassCache<T>` (one-shot per-authoring-state solves, `PyrePrepassCache.cs`), `PyreClipStats` (whole-clip field statistics, "NEVER NORMALISE PER FRAME", `PyreClipStats.cs:1-30`), `PyreFieldOps` (blur/smear/warp/bloom over float planes, `PyreFieldOps.cs`), bit-exact `PyreNumpyRng` (PCG64) and `PyrePyRandom` (MT19937) for Kiln parity (`Forms/Kiln/PyreNumpyRng.cs`, `PyrePyRandom.cs`), `PyreFormWarp` (generic post-render inverse-warp so field-accumulating forms get geometry modifiers for free, `PyreFormWarp.cs:1-10`; "warp for swarms … not built" @16).

## 3. Modifiers / effects

Defined in SpriteFx, consumed by Pyre. Base `PyreModifier` (`Runtime/SpriteFx/SpriteFxModifiers.cs:132`), families `GeometryModifier` @191, `PixelModifier` @685, `PostModifier` @1769, `EdgeModifier` @2589, `SimulationModifier` (`SpriteFxSimulationModifiers.cs:44`). Applied per layer in list order (geometry folds sample positions before the edge test, pixel recolours lit pixels, post runs over the finished frame) — `Runtime/Pyre/PyreRenderer.cs:11-16`; spec-wide `globalModifiers` wrap each layer in Pyre1's order (`PyreRenderer.cs:1243,1293`).

Add-menu is reflection-driven: every non-abstract `PyreModifier` with a parameterless ctor, grouped Geometry / Pixel / Post; **EdgeModifier and SimulationModifier subclasses are deliberately excluded** from the stack picker (`Editor/Pyre/PyreWindow.Modifiers.cs:381-405`). The Simulation slot is a separate single polymorphic slot per layer, filled by its own reflection menu (today only `PixelFluidModifier`) — `PyreWindow.Modifiers.cs:158-255`. No spec-wide simulation slot ("deferred follow-up", `PyreWindow.Modifiers.cs:103`).

| Group | Concrete classes (`Runtime/SpriteFx/SpriteFxModifiers.cs` unless noted) | Picker status |
|---|---|---|
| Geometry | Skew @203, Scale @228, Rotate @266, Wobble @302, SunburstWobble @339, RingWave @386, PointBlast @437, Profile @546, Ground @583, Sunburst @803, PulseRings @854, Turbulence @2677, Sphere @3108, **Curl @2906, CurlProgress @3031, Smudge @3191, PinWarp @3364** | bold four are listed but BLOCKED in the add-menu with a reason tooltip ("Needs click-to-place pins/strokes/vortices in Pyre1's own preview canvas — not authorable here") — `PyreWindow.Modifiers.cs:354-359` |
| Pixel | Tint @710, Contrast @746, Brightness @762, Saturation @778, Posterize @893, OrderedDither @926, VoronoiCrack @966, LayerDissolve @1271, Wipe @1323, ColorTint @1474, ColorReplace @1626, ColorRemap (`SpriteFxColorRemap.cs:142`) | all addable |
| Post | Dissolve @1181, Bloom @1811, Outline @1911, ChromaticAberration @2126, BallisticShockwave @2224, Fuse @2483, EdgeSmooth @2758, DropShadow @3410, Kaleidoscope @3505, Relight (`SpriteFxRelight.cs:243`) | all addable |
| Edge | EdgeWarp @2609 | excluded (no silhouette-edge stage in Pyre's raster) |
| Simulation | PixelFluid (`SpriteFxSimulationModifiers.cs:115`) | only via the per-layer Simulation slot |

Modifier bodies are drawn by `ZuiReflect` with `Undo.RecordObject(spec, "Edit Pyre Plus modifier")` (`PyreWindow.Modifiers.cs:327-329`); reorder by `ZuiReorder` grip, enable toggle, remove button, per-instance fold state (`PyreWindow.Modifiers.cs:266-320`). Collapsed headers show the enabled count "(N)" (`@42-51,124`).

## 4. Fills / borders / colour

| Capability | Where |
|---|---|
| `ZuiFill` is the universal colour source: modes Solid / OverLife / Linear / Radial; texture kinds None / Sprite / Noise / Grid / Dots; noise Value / Ridged / Steps; FillSpace Stamped / Fixed; FillFit Uniform / Stretch. | `Zui/Scripts/Runtime/ZuiFill.cs:29-64` |
| Per-layer fills: `shapeFill`, `gemSpecularFill`, `gemLineFill`, `gemEdgeGlowFill`, `gemInnerGlowFill`, `textFill`, `textBorder`, `borderFill`; spec `backgroundFill`. Edited with `Z.Fill` / `ZuiFillControl`, undo-recorded. | `Runtime/Pyre/Pyre.cs:324,368-380,412,421,521,1202`; `Editor/Pyre/PyreWindow.cs:749-760,2614-2622` |
| `PyreRamp` (list of `PyreRampStop`, `PyreRampSpace` LinearLight/Srgb, implements `IZuiRamp` so ZuiReflect draws it as one `ZuiRampControl`), `PyreLut` baked LUT (from ramp / Gradient / ZuiFill / ZuiGradient), `Banded` cel bands, `Quantise`, `DualRamp` crossfade, `Palette2D` grid, `AdditiveEmissive` tone-map with blow-out. | `Runtime/Pyre/PyreShade.cs:21-317` |
| Ramp presets shipped per port: Ember/EmberSoot, 13 Jet ramps (+soot windows), 5 Plasma, 5 Orb, 5 Torch (+ `TorchGradient`). | `Runtime/Pyre/PyreShade.cs:339-644` |
| First-class border on flat 2D forms: `borderEnabled`, `borderWidth : ZUIValue`, `borderFill`, `borderOverMatte` ("Draw over matte"); deferred-border compositing in the frame composer. | `Runtime/Pyre/Pyre.cs:517-528`; `PyreRenderer.cs:940`; `Editor/Pyre/PyreWindow.cs:2677-2713` |
| Text border: `textBorderWidth` + `textBorder` fill; text fill modes PerCharGradient / PerCharStep / TextGradient with `textGradientAngle`. | `Runtime/Pyre/Pyre.cs:123,406-421`; `PyreWindow.cs:2091-2112` |
| Gem/solid lighting: ambient, diffuse, specular, spec power, line width, edge/inner glow envelopes. | `PyreWindow.cs:1503-1577` |
| Dithering: `OrderedDitherModifier` (pixel) and `PosterizeModifier`; GIF export has its own Bayer 4×4 alpha dither toggle (`previewGifDither`). | `SpriteFxModifiers.cs:893,926`; `Editor/Pyre/PyreGif.cs:26-44`; `PyreWindow.cs:572-582` |
| Recolour: `ColorTint`, `ColorReplace`, `ColorRemap`, `Tint`, `Saturation`, `Contrast`, `Brightness` modifiers. | §3 |
| Matte system: `MatteRole` Draw / WriteMatte / LumaMatte; 4 numbered channels with `MatteCombine` Max/Add/Subtract; `clipByChannel`; heightmap from channel (`heightRelief`, `heightLightAngle`); luma-matte channels flags Alpha/Brightness/Saturation/Hue/Blur/Displace with `MatteScope` NextLayer/AllAbove and ZUIValue strength/blur/displace/hue; invert; "strength from edge". | `Runtime/Pyre/Pyre.cs:135-173,210-260`; `PyreRenderer.cs:1047,1839`; `PyreWindow.cs:972-1103` |
| Coalesce render modes: Off / Fuse (MetaBlob iso-surface: threshold, shade range, softness) / Ramp (HeightBalls density relief: fusion, coverage, lighting, relief, light angle, rim boil). | `Pyre.cs:173,268-307`; `PyreRenderer.cs:1488-1676`; `PyreWindow.cs:2493-2560`; `PyreField.cs` |
| Background: solid colour or `ZuiFill`. | `Pyre.cs:1194-1202` |

## 5. Animation and time

| Capability | Where |
|---|---|
| Clock: `frameCount` (1..64 in UI) with life = `frameIndex/(frameCount-1)` (`LifeOfFrame`); `previewFps` (1..30 UI) is ALSO the bake fps and GIF delay source. No separate authored `frameRate` field. | `Runtime/Pyre/PyreForm.cs:109,122`; `PyreWindow.cs:731-733,624`; `PyreBaker.cs:90` |
| Per-layer lifetime window `startFrame` / `endFrame` (-1 = last), range slider in the Shape section. | `Pyre.cs:197-199`; `PyreWindow.cs:1267-1278` |
| `ZUIValue` envelopes everywhere (~60 `Val(…)`/`Val2D(…)` dials in the window): modes Static / MinMax / Curve / Steps / Oscillation. Every ZUIValue eval takes a unique field id from a registry so RNG streams never correlate. | `Zui/Scripts/Runtime/ZUIValue.cs:20`; `PyreWindow.cs:1055-2701` (Val list); `PyreRenderer.cs:29-56` |
| Swarm: `swarmEnabled`, count 2..200, particle life, spawn mode Area/Path, shape kinds Circle/Triangle/Square/Pentagon/Hexagon/Custom/Line, timing Window/FrameStep, orient None/Outward/PathTangent, chaos/spawn order/reverse/even spacing/spread/die together, per-particle scale by index, spawner offset/scale/rotation/pitch/yaw, live swarm turn/tilt/roll/scale, particle own path X/Y + spin. | `Pyre.cs:18-39,642-753`; `PyreWindow.cs:2138-2471` |
| Transport: ▶ Play / ❚❚ Pause, frame scrubber (`SliderInt`) + "frame N/M" readout, Zoom 1..16 (integer), Speed (fps), Delay (blank seconds between loops, cherry mode), Frame toggle (canvas border), Strip toggle (contact sheet, click a tile to jump), Tile px slider. Loop is always on in the preview. | `PyreWindow.cs:517-640`; `PyreWindow.Preview.cs:204-221` |
| Cherry Framing: opt-in sub-sequence of picked source frames; slots with length multiplier / min-max random length / MultiFrame random pick with seed; drag-reorder, multi-select, right-click editor, Delete key; own tile size; preview-only Zound trigger at a slot. | `Runtime/Pyre/CherryFrame.cs:24-70`; `Editor/Pyre/PyreWindow.CherryFraming.cs:76-127`; `Pyre.cs:1247-1252` |
| Preview frame cache: per-layer content-keyed (64-bit FNV-1a over all serialized layer fields + index + spec part) LRU buffer store; background multi-core fill (`PyreLayerFill` / `PyreFrameFill` deep-clone the spec per worker); "fill readout" in the transport. Playback only steps onto composited frames. | `Runtime/Pyre/PyreLayerKey.cs:1-10`, `PyreLayerCache.cs`, `PyreLayerFill.cs`, `PyreFrameFill.cs`; `Editor/Pyre/PyreWindow.FrameCache.cs:1-12,450` |
| Stateful sims (Fire, Fireball, SimulationModifier) replay deterministically from frame 0 on scrub via a CWT + content-hash replay cache. | `PyreRenderer.cs:362,557,736,1395-1402` |
| Swarm preview overlays: "Show shape" outline, "Show trace" (spawner path polyline), spawn dots, draggable position handle, click-to-add / drag / right-click-remove Custom polyline points. | `PyreWindow.Preview.cs:279-610`; `PyreWindow.cs:2214-2221` |
| NOT present: onion skin / ghost frames, preview pan, mouse-wheel zoom (zoom is the slider only). Grep for onion/ghost/WheelEvent/pan in `Editor/Pyre/` finds nothing; the only `MouseDrag` handlers are the position handle and custom points. | `PyreWindow.Preview.cs:536,578` |

## 6. Output paths

| Capability | Where |
|---|---|
| **Bake**: `PyreBaker.Bake(spec)` → sprite-sheet PNG next to the asset (unique-path guard, never overwrites), `TextureImporter` Multiple/Point/PPU=spec, centre pivot, importer `userData = "…Pyre"` baked-marker so `LauAssetBrowser` can hide it, then an `AnimationClip` (`.anim`, `SpriteRenderer.m_Sprite` binding, `loopTime = true`, fps = `previewFps`). Button "Bake" in the transport. | `Editor/Pyre/PyreBaker.cs:21-120`; `PyreWindow.cs:587-591` |
| **GIF**: `PyreGif.Export(spec, path, scale, ditherAlpha)` — self-contained deterministic GIF89a encoder, median-cut ≤255 colours + 1 transparent index, NETSCAPE loop forever, nearest upscale 1..8×, optional Bayer alpha dither, self-verifying LZW decoder. "GIF…" button + "GIF scale" + "GIF dither" in the transport (SaveFilePanel). | `Editor/Pyre/PyreGif.cs:1-92,319-326,472-513`; `PyreWindow.cs:557-582,668-670` |
| **Runtime playback without baked assets**: `PyreBlastPlayer : MonoBehaviour` (`[RequireComponent(SpriteRenderer)]`; `spec`, `fps`, `loop`, `destroyOnFinish`, `playOnAwake`, `pooled`, `Play()`, `Stop()`) renders through `PyreRenderer.GetFrames` (shared cache). `PyreBlastPool` = one static pool of holders. | `Runtime/Pyre/PyreBlastPlayer.cs:14-72`; `PyreBlastPool.cs:12` |
| **Renderer API**: `RenderFrame(spec, i) : Color32[]`, `RenderFrameTexture`, `RenderSheet(out cols,out rows)`, `SheetLayout`, `FrameRect`, `GetFrames(spec) : Sprite[]` (cached), `ClearFrameCache`, `IsParallelSafe(spec, out reason)`, `ComputeSpawns`, `ComputeSpawnTrace`, `ApplySwarmSpin/Scale`, `Hash(a,b,c,d)`. | `Runtime/Pyre/PyreRenderer.cs:181-214,1895-2135,5059-5186` |
| **Chunks / Zoe adapters**: `PyreChunkAnimation` (frames hand-over via `IChunkAnimation`), `PyreSpawnSource` (`IChunkEffectSpawner.SpawnEffect` spawns a live blast), and the Pyre asset ITSELF implements `IChunkEffectSpawner, IChunkAnimation, IVisualPreview` directly (`PyreChunksDirect.cs`, "do not tidy up as duplication"). | `Runtime/Pyre/PyreChunkAnimation.cs:16-41`; `PyreSpawnSource.cs:19-65`; `PyreChunksDirect.cs:33-99` |
| **Chunks modules**: `PyreSpawnModule` (source or random pool of spawners, offset, rotation modes InheritBurst/Fixed/Random, seed, formation) and `PyreMotionModule` (velocity/gravity/drag/spread/face-velocity/life) with their ChunkWindow sections "Blasts" and "Pyre Movement". | `Runtime/Chunks/PyreSpawn/PyreSpawnModule.cs:29-234`; `Runtime/Chunks/Motion/PyreMotionModule.cs:18-75`; `Editor/Chunks/ChunkWindow.PyreSpawn.cs`, `ChunkWindow.PyreMotion.cs` |
| **Zoetrope bridge** (asmdef `com.Lautaro-Arino.Laubrary.Zoetrope.Pyre`): `SpawnPyreFx : IEffect` (Zoe event palette entry; blast, fps, sorting order, base scale + scale per amount), `PyreChunksFx : ICombatFx` (blast + chunks at a point), `SpawnChunkFx : IEffect`. | `Runtime/ZoetropePyre/SpawnPyreFx.cs:19-48`, `PyreChunksFx.cs:21-44`, `SpawnChunkFx.cs:26-57` |
| **Mirage**: a Pyre entry spawns a `PyreBlastPlayer` looping at the spec's PPU. | `Runtime/Mirage/MirageRig.cs:263-274` |
| **Demos** call `PyreBlastPlayer` directly (Arena, Colosseum, Daemon, Gallery). | `Assets/Demos/ArenaDemo/ArenaEnemy.cs:159`, `ColosseumDemo/ShmupEnemy.cs:115`, `DaemonDemo/ColosseumAgentBody.cs:93` |
| **Kiln parity harness**: `PyreParityDump` (editor-only, no window/menu) writes golden frames, `.npy` field planes, `ramp_probe.json`, `meta.json` and runs the comparer. Forms publish planes through `IPlusFieldPublisher` / `IPlusRampProbe`. | `Editor/Pyre/Parity/PyreParityDump.cs:1-41,257-261`; `Runtime/Pyre/PyreForm.cs:156-163` |
| **Shaper bridge (already built)**: `PyreFormCompositeSource : IShaperCompositeSource` hosts an unmodified `PyreForm` for Shaper (`Render(w,h,phase01,seed,target)`), `PyreCompositeCatalog` classifies the nine generators, `ShaperEffectApplier`/`ShaperEffectCatalog`/`ShaperEffectStageRunner` run Shaper's authored effects through SpriteFx modifiers; `PyreShaperCompositeAudit` is a measured audit + contact sheet. | `Runtime/PyreShaper/PyreFormCompositeSource.cs:35-37`, `PyreCompositeCatalog.cs:43`, `ShaperEffectApplier.cs:58`, `ShaperEffectContract.cs:95`; `Editor/PyreShaper/PyreShaperCompositeAudit.cs:20` |

## 7. Editor window UX

| Fact | Where |
|---|---|
| `[MenuItem("Laubrary/Pyre")]` opens `PyreWindow` (partial across 7 files: `.cs`, `.Forms`, `.Modifiers`, `.Preview`, `.FrameCache`, `.CherryFraming`). Pure UI Toolkit/ZUI (`Z.*` builders, `ZuiSection`, `Z.BoxKeyed`, `Z.ColumnFlow(360)`) except the preview canvas and the Tags IMGUI island. | `Editor/Pyre/PyreWindow.cs:20-22,307-345` |
| Layout: left `ScrollView` dial column (resizable, 360..1443 px, dragged splitter @419-441) + right pane preview; Cherry panel in the right pane. | `PyreWindow.cs:309-345,389` |
| `ZuiSectionToggleBar("Pyre", …)` with 8 sections: Tags, Views, Canvas, Layers, Global Mod, Shape, Swarm, Modifiers. | `PyreWindow.cs:488-498` |
| Canvas section: Size 16..256, PPU 1..64, Frames 1..64, Seed int, Background fill. | `PyreWindow.cs:705-760` |
| Layers section: "+ Add layer" (front), "Duplicate", per-row enable toggle, inline rename (`Undo "Rename layer"`), "Dup", "✕" delete, selection highlight, matte indicators; reorder. | `PyreWindow.cs:779-926` |
| Shape section: form picker menu (490px `Z.Menu` listing enum forms + PyreForm plug-ins), Alpha/Size/Turn/Tilt/Roll/Spin/Offset envelopes, per-form boxes (Solid/Light/Lines/Glow, Sparkle, Sprite, Streak, Star, Polygon, Playback 3D (POC), Fire, Fireball, Text, Border, Matte, Position). | `PyreWindow.cs:1130-2130` |
| Swarm section (with "Show shape"/"Show trace" toggles), Transform box, Swarm spin box, Coalesce Off/Fuse/Ramp boxes. | `PyreWindow.cs:2138-2560` |
| Modifiers section (selected layer's stack + Simulation slot) and Global Mod section (spec-wide). | `PyreWindow.Modifiers.cs:27-155` |
| Preview: IMGUI canvas; integer zoom 1..16; `BackSplashPainter.Draw` backdrop from `spec.previewBackSplash` (settings stored on the asset); optional canvas frame; filmstrip contact-sheet mode; Playback3D live/pixelated preview header; swarm overlays. | `PyreWindow.Preview.cs:69-145,204-279`; `PyreWindow.cs:84-90` |
| Undo: 11 `Undo.RecordObject` sites in `PyreWindow.cs` + 1 each in `.Forms` and `.Modifiers`; every `Val`/`Val2D`/`FillRow` routes through `Undo.RecordObject(spec, "Edit Pyre Plus")`; the generic `Dirty(...)` helper (@2896) is the path for MicroSlider/Toggle edits. Views asset creation undo-registered. Delete asset not undoable (confirm dialog). Cherry-Framing file has 0 direct `Undo.RecordObject` (relies on `Dirty`/`DirtyRepaintOnly`). | `PyreWindow.cs:568,759,838,1625,2585,2609,2617,2896`; `PyreWindow.Forms.cs:123`; `PyreWindow.Modifiers.cs:329` |
| Tooltips everywhere; per-form tooltip factories (`FillTooltip`, `SizeTooltip`, `SpinTooltip`, …) explain why a dial is inert for the current form. | `PyreWindow.cs:2800-2888` |

## 8. Determinism and conventions

| Rule | Where |
|---|---|
| "never UnityEngine.Random, never Time": every random draw is `new System.Random(Hash(seed, particleIndex, fieldId, layerSalt))`; preview, bake and runtime byte-identical; frames render standalone (pure function of (spec, frameIndex)) so they parallelise. | `Runtime/Pyre/PyreRenderer.cs:1-16,2254,5034-5077`; `PyreFrameFill.cs:1-6` |
| Field-ID registry: unique ids per ZUIValue/draw; ids 1..15 taken, 16+ = per-modifier 8-wide blocks, new single ids must be NEGATIVE (-19 onward); form dials auto-derive a block. | `PyreRenderer.cs:29-56,96-143` |
| Noise = pure hash of (coords, seed); FNV-based `Hash`, `Hash01`, `Hash2`, `HashUniform`, `HashMix` helpers (≈140 call sites). | `Runtime/Pyre/PyreFieldOps.cs:11`; `PyreRenderer.cs:5059` |
| Kiln ports use bit-exact numpy/CPython RNG replicas seeded from (spec seed + layer salt) only. | `Forms/Kiln/PyreNumpyRng.cs:9`, `PyrePyRandom.cs:8`, `PyreInferno.cs:51` |
| Layer salt = layer index; content key folds it in so moving a layer legitimately re-renders. | `PyreLayerKey.cs:8-10` |
| Whole-clip statistics fitted once ("NEVER NORMALISE PER FRAME"). | `PyreClipStats.cs:3-6` |
| `ShapeForm` enum is APPEND ONLY (serialized as ints). | `Pyre.cs:93,106` |
| `[MovedFrom]` on `PyreForm`, all Kiln forms, and all modifier bases so PyrePlus-era assets deserialize. | `PyreForm.cs:182`, `Forms/Kiln/*Form.cs`, `SpriteFxModifiers.cs:132-191` |
| **Exceptions found (see Surprises)**: `CherryFrame.PickSourceFrame` / `ResolveLength` use `UnityEngine.Random.Range` when `multiFrameRandomSeed == 0` / for min-max length (`CherryFrame.cs:65,75`); Chunks' `PyreSpawnModule` / `PyreMotionModule` fall back to `Random.value` / `Random.Range` when `seed == 0` (`PyreSpawnModule.cs:128-129`, `PyreMotionModule.cs:83`). No "BC-x" rule identifiers appear anywhere in the Pyre or SpriteFx source (0 matches); the determinism contract is referenced by name ("PYREPLUS_DESIGN.md 'Determinism contract'") instead. | — |

## 9. Checklist table (parity audit rows)

| # | Capability | Where in Pyre | Description |
|---|---|---|---|
| 1 | Pyre asset (ScriptableObject) | `Runtime/Pyre/Pyre.cs:1182` | Authored effect spec, `[CreateAssetMenu("Laubrary/Pyre")]` |
| 2 | Square canvas size | `Pyre.cs:1185`, `PyreWindow.cs:714` | 16..256 px, Width = Height |
| 3 | Frame count | `Pyre.cs:1186`, `PyreWindow.cs:731` | 1..64 frames; life = i/(N-1) |
| 4 | Global seed | `Pyre.cs:1187`, `PyreWindow.cs:738` | int seed drives every draw |
| 5 | Pixels-per-unit | `Pyre.cs:1195`, `PyreWindow.cs:722` | 1..64, applied to bake + runtime scale |
| 6 | Background colour / fill | `Pyre.cs:1194-1202`, `PyreWindow.cs:749` | solid or ZuiFill backdrop composited into frames |
| 7 | Preview prefs stored on asset | `Pyre.cs:1221-1257` | zoom, fps, gif, strip, overlays, cherry, backsplash |
| 8 | Layer stack (ordered list) | `Pyre.cs:1207`, `PyreWindow.cs:779` | N layers, front-of-stack add |
| 9 | Layer enable / hide | `Pyre.cs:183`, `PyreWindow.cs:831` | skipped by renderer |
| 10 | Layer rename inline | `Pyre.cs:184`, `PyreWindow.cs:836` | text input in row |
| 11 | Layer duplicate | `PyreWindow.cs:789,887,926` | selected or per-row |
| 12 | Layer delete | `PyreWindow.cs:900` | undoable |
| 13 | Layer reorder | `PyreWindow.cs:779-926`, `PyreLayerKey.cs:8` | index = salt; re-renders |
| 14 | Layer lifetime window | `Pyre.cs:197-199`, `PyreWindow.cs:1267` | startFrame/endFrame range slider |
| 15 | Matte: numbered channels write/clip | `Pyre.cs:210-216`, `PyreWindow.cs:972-1078` | Draw / WriteMatte / clipByChannel, Max/Add/Subtract |
| 16 | Matte: heightmap from channel | `Pyre.cs:231-237`, `PyreWindow.cs:1006-1103` | relief + light angle |
| 17 | Luma matte (6 channels) | `Pyre.cs:143-152,247-260`, `PyreRenderer.cs:1047` | Alpha/Brightness/Saturation/Hue/Blur/Displace, scope NextLayer/AllAbove |
| 18 | Coalesce Fuse (metaball) | `Pyre.cs:276-280`, `PyreRenderer.cs:1598` | threshold / shade range / softness |
| 19 | Coalesce Ramp (height relief) | `Pyre.cs:297-307`, `PyreRenderer.cs:1676` | fusion / coverage / lighting / relief / rim |
| 20 | Form: Disc | `Pyre.cs:113` | soft disc |
| 21 | Form: Gem | `Pyre.cs:113`, `PyreRenderer.cs:4135` | 3D faceted crystal |
| 22 | Form: Crescent | `Pyre.cs:432-441` | bite/angle/centre |
| 23 | Form: Sparkle | `Pyre.cs:447` | density, size px |
| 24 | Form: Sprite stamp | `Pyre.cs:456-457`, `PyreWindow.cs:1650` | Sprite picker + tint |
| 25 | Form: Box | `Pyre.cs:113` | 3D cuboid |
| 26 | Form: Pyramid | `Pyre.cs:113` | 3D pyramid |
| 27 | Form: Can | `Pyre.cs:113` | 3D cylinder |
| 28 | Form: Orb (enum) | `PyreRenderer.cs:4411` | analytic lit sphere |
| 29 | Form: Ring | `PyreRenderer.cs:4601`, `PyreWindow.cs:1492` | tilted annulus, inner radius |
| 30 | Form: Text (TMP SDF) | `Pyre.cs:397-425`, `PyreRenderer.cs:3781` | string, font, spacing, fill modes, border, extrusion depth |
| 31 | Form: Streak | `Pyre.cs:466-481`, `PyreRenderer.cs:3656` | length/width envelopes, anchor, soft tip, taper |
| 32 | Form: Star | `Pyre.cs:490-492` | arms, length, base width, skew |
| 33 | Form: Polygon | `PyreWindow.cs:1741` | N-gon sides |
| 34 | Form: Fire (stateful sim) | `Pyre.cs:544-587`, `PyreRenderer.cs:362` | 20 dials, steps, threshold, contrast, swarm emitters |
| 35 | Form: Fireball (stateful sim) | `Pyre.cs:599-603`, `PyreRenderer.cs:736` | arms, core radius, cooling, sharpness, spread, reach |
| 36 | Form: Playback3D (POC, editor-only) | `Pyre.cs:616-632`, `PyreRenderer.cs:262`, `PyrePlayback3DPreview.cs` | prefab ParticleSystem preview, speed/scale/zoom/glow/scrub/loop/pixelated; NO bake |
| 37 | Retired enum slots Inferno/ForkBlast | `Pyre.cs:114-115` | `[Obsolete]`, draw as Disc |
| 38 | Plug-in form model (`PyreForm`, SerializeReference) | `PyreForm.cs:182`, `Pyre.cs:318` | reflection-drawn dials, Prepare/Render/Clone/ContentHash |
| 39 | Form: Arc Burst | `Forms/Kiln/ArcBurstForm.cs:34` | electric arc explosion, Burst rasteriser |
| 40 | Form: Fork Blast | `Forms/Kiln/ForkBlastForm.cs:13` | puff detonation |
| 41 | Form: Inferno | `Forms/Kiln/InfernoForm.cs:13` | volumetric fireball |
| 42 | Form: Jet | `Forms/Kiln/Jet/JetForm.cs:26` | flamethrower stream + 5 presets |
| 43 | Form: Radial Jet | `Forms/Kiln/Jet/RadialJetForm.cs:24` | arc jet + 8 presets |
| 44 | Form: Explosive Jet | `Forms/Kiln/Jet/ExplosiveJetForm.cs:42` | detonation schedule + 10 presets |
| 45 | Form: Orb (Kiln) | `Forms/Kiln/OrbForm.cs:34` | energy projectile |
| 46 | Form: Plasma Bloom | `Forms/Kiln/PlasmaBloomForm.cs:107` | directional plasma bloom |
| 47 | Form: Torch | `Forms/Kiln/TorchForm.cs:37` | grounded flame + 5 presets |
| 48 | Form supersampling | `PyreSupersample.cs:24` | k× render, premultiplied downsample |
| 49 | Form prepass cache | `PyrePrepassCache.cs` | one-shot per-authoring-state solves |
| 50 | Whole-clip statistics | `PyreClipStats.cs:22` | fit once, never per frame |
| 51 | Float-plane field ops | `PyreFieldOps.cs` | blur/smear/warp/bloom pre-shade |
| 52 | Generic post-render geometry warp for forms | `PyreFormWarp.cs:26` | inverse-warp resample; forms may opt out |
| 53 | Bit-exact numpy / CPython RNG | `Forms/Kiln/PyreNumpyRng.cs`, `PyrePyRandom.cs` | Kiln parity |
| 54 | Kiln parity dump harness | `Editor/Pyre/Parity/PyreParityDump.cs:31` | golden frames, npy fields, ramp probe, comparer |
| 55 | Shape Fill (ZuiFill) | `Pyre.cs:324`, `PyreWindow.cs:2614` | Solid/OverLife/Linear/Radial + Sprite/Noise/Grid/Dots textures |
| 56 | Gem material fills (spec/line/edge/inner) | `Pyre.cs:368-380`, `PyreWindow.cs:1545-1577` | four ZuiFills |
| 57 | Solid lighting dials | `PyreWindow.cs:1503-1541` | ambient, diffuse, specular, spec power |
| 58 | Facet lines + glows | `PyreWindow.cs:1550-1577` | line width, edge glow, inner glow envelopes |
| 59 | Border on flat 2D forms | `Pyre.cs:517-528`, `PyreRenderer.cs:940` | width envelope, fill, draw-over-matte |
| 60 | Text fill modes + border + depth | `Pyre.cs:406-425`, `PyreWindow.cs:2091-2119` | per-char gradient/step, text gradient angle |
| 61 | PyreRamp + LUT + presets | `PyreShade.cs:45-125,339-644` | linear-light stops, banded, dual ramp, Palette2D, additive emissive |
| 62 | Alpha envelope | `PyreWindow.cs:1292-1308` | ZUIValue over life |
| 63 | Size envelope | `PyreWindow.cs:1317` | px, ZUIValue |
| 64 | Edge softness envelope | `Pyre.cs:332`, `PyreWindow.cs:1588` | feathered rim |
| 65 | Particle turn/tilt/roll/spin envelopes | `PyreWindow.cs:1413-1422` | pseudo-3D orientation |
| 66 | Position offset (2D envelope) + draggable handle | `PyreWindow.cs:1399-1426`, `PyreWindow.Preview.cs:509` | on-canvas handle |
| 67 | Particle own path X/Y + spin | `Pyre.cs:642-647` | motion after birth on own clock |
| 68 | ZUIValue modes | `Zui/Scripts/Runtime/ZUIValue.cs:20` | Static/MinMax/Curve/Steps/Oscillation |
| 69 | Swarm on/off + count + particle life | `Pyre.cs:660`, `PyreWindow.cs:2229-2233` | 2..200 particles |
| 70 | Swarm spawn mode Area/Path | `Pyre.cs:18,662` | inside shape vs along outline |
| 71 | Swarm shape kinds | `Pyre.cs:26` | Circle/Triangle/Square/Pentagon/Hexagon/Custom/Line |
| 72 | Custom polyline editing on canvas | `PyreWindow.Preview.cs:487-610` | click-add, drag, right-click remove |
| 73 | Swarm timing Window/FrameStep | `Pyre.cs:39,703`, `PyreWindow.cs:2250` | spawn timing envelope |
| 74 | Swarm orient None/Outward/PathTangent | `Pyre.cs:33,711` | per-particle facing |
| 75 | Swarm distribution/spawn-order chaos, reverse, even spacing, spread | `PyreWindow.cs:2312-2379` | placement controls |
| 76 | Swarm die-together | `Pyre.cs:724` | |
| 77 | Swarm scale by index | `Pyre.cs:715` | |
| 78 | Spawner transform (offset/scale/rotation/pitch/yaw/snap) | `Pyre.cs:727-733`, `PyreWindow.cs:2401-2437` | |
| 79 | Swarm live turn/tilt/roll/scale | `Pyre.cs:742-753`, `PyreWindow.cs:2452-2471` | rigid whole-cloud motion |
| 80 | Swarm preview overlays (shape/trace/dots) | `PyreWindow.Preview.cs:279-469` | toggles on asset |
| 81 | Per-layer modifier stack | `Pyre.cs:756`, `PyreWindow.Modifiers.cs:27` | reorder/enable/remove/fold, reflection bodies |
| 82 | Spec-wide global modifiers | `Pyre.cs:1218`, `PyreWindow.Modifiers.cs:98` | wrap every layer |
| 83 | Per-layer Simulation slot | `Pyre.cs:766`, `PyreWindow.Modifiers.cs:158` | single stateful modifier, PixelFluid |
| 84 | Reflection add-menu with grouped sections | `PyreWindow.Modifiers.cs:344-416` | Geometry/Pixel/Post |
| 85 | Geometry modifiers (13 addable) | `SpriteFxModifiers.cs:203-3108` | Skew…Sphere |
| 86 | Canvas-authored geometry modifiers blocked | `PyreWindow.Modifiers.cs:354-359` | Curl, CurlProgress, Smudge, PinWarp |
| 87 | Pixel modifiers (12) | `SpriteFxModifiers.cs:710-1626`, `SpriteFxColorRemap.cs:142` | Tint…ColorRemap |
| 88 | Post modifiers (10) | `SpriteFxModifiers.cs:1181-3505`, `SpriteFxRelight.cs:243` | Dissolve…Relight |
| 89 | Ordered dither / posterize | `SpriteFxModifiers.cs:893,926` | pixel-art quantisation |
| 90 | Outline / drop shadow / bloom | `SpriteFxModifiers.cs:1811,1911,3410` | post |
| 91 | Modifier field-id blocks (no RNG correlation) | `PyreRenderer.cs:29-56` | |
| 92 | Deterministic hash RNG | `PyreRenderer.cs:5034-5077` | System.Random(Hash(...)) |
| 93 | Parallel frame rendering (multi-core) | `PyreFrameFill.cs`, `PyreLayerFill.cs`, `PyreRenderer.cs:181` | IsParallelSafe gate |
| 94 | Per-layer content-keyed preview cache (LRU) | `PyreLayerKey.cs`, `PyreLayerCache.cs`, `PyreWindow.FrameCache.cs` | edit one layer → re-render one layer |
| 95 | Stateful sim replay on scrub | `PyreRenderer.cs:362,736,1395` | CWT + content hash |
| 96 | Play / Pause transport | `PyreWindow.cs:530` | |
| 97 | Frame scrubber + readout | `PyreWindow.cs:77-78,600-611` | |
| 98 | Preview zoom slider (1..16 int) | `PyreWindow.cs:619` | no wheel zoom, no pan |
| 99 | Preview speed (fps) | `PyreWindow.cs:624` | doubles as bake/GIF fps |
| 100 | Loop delay between iterations | `PyreWindow.cs:636`, `Pyre.cs:1250` | cherry mode |
| 101 | Canvas frame outline toggle | `PyreWindow.cs:538` | |
| 102 | Filmstrip / contact-sheet mode | `PyreWindow.cs:542-552`, `PyreWindow.Preview.cs:204` | click tile to jump |
| 103 | BackSplash backdrop | `PyreWindow.Preview.cs:271`, `Pyre.cs:1257` | settings on asset |
| 104 | Cherry Framing sub-sequence | `PyreWindow.CherryFraming.cs`, `CherryFrame.cs` | pick, reorder, variable length, multi-frame |
| 105 | Preview-only Zound trigger | `Pyre.cs:1251-1252` | fire a Zound at a cherry slot |
| 106 | Bake → PNG sheet + AnimationClip | `PyreBaker.cs:32-108` | unique path, Point filter, baked marker |
| 107 | GIF export (scale, alpha dither) | `PyreGif.cs:51`, `PyreWindow.cs:557-582` | deterministic encoder |
| 108 | Runtime player (no baked assets) | `PyreBlastPlayer.cs:14` | fps/loop/destroyOnFinish/pooled |
| 109 | Runtime pool | `PyreBlastPool.cs:12` | Get() → configure → Play() |
| 110 | Frame cache shared runtime/editor | `PyreRenderer.cs:5165-5186` | GetFrames / ClearFrameCache |
| 111 | IChunkAnimation adapter asset | `PyreChunkAnimation.cs:16` | frames for Chunks/Zoe |
| 112 | IChunkEffectSpawner adapter asset | `PyreSpawnSource.cs:19` | spawns live blast |
| 113 | Pyre asset is itself a Chunks source | `PyreChunksDirect.cs:33` | direct pick, no adapter |
| 114 | IVisualPreview (animated thumbnails) | `PyreChunkAnimation.cs:29-41`, `PyreChunksDirect.cs:90-99` | browser previews |
| 115 | Chunks Pyre Spawn module (+ formation, pool, rotation, seed) | `PyreSpawnModule.cs:29` | "Blasts" section |
| 116 | Chunks Pyre Motion module | `PyreMotionModule.cs:18` | velocity/gravity/drag |
| 117 | Zoe event effect SpawnPyreFx | `ZoetropePyre/SpawnPyreFx.cs:19` | scale by event scalar |
| 118 | Combat FX PyreChunksFx | `ZoetropePyre/PyreChunksFx.cs:21` | blast + debris |
| 119 | Mirage placement | `Mirage/MirageRig.cs:263` | looping blast at PPU |
| 120 | Mirage/LauAsset picker support | `Mirage/MirageAssetPicker.cs:44-50` | |
| 121 | PyreSpawnSource chip New/Edit link | `PyreSpawnSourceEditorLink.cs:16` | |
| 122 | New / Duplicate / Rename / Delete asset | `ZuiAssetWindow.cs:163-255` | AssetKit base |
| 123 | Asset browse grid with animated thumbs | `ZuiAssetWindow.cs:77-91`, `PyreWindow.cs:40-58` | |
| 124 | LauTag tags section | `ZuiAssetWindow.cs:138-142`, `PyreWindow.cs:491` | |
| 125 | Views (fold presets) | `PyreWindow.cs:345,503` | ZuiViewStore |
| 126 | Section toggle bar (8 sections) | `PyreWindow.cs:488-498` | |
| 127 | Resizable dial pane / column flow | `PyreWindow.cs:318-333,419-441` | |
| 128 | Undo coverage on dials/fills/modifiers/forms | `PyreWindow.cs:568,759,2585-2617`, `.Forms.cs:123`, `.Modifiers.cs:329` | RecordObject "Edit Pyre Plus…" |
| 129 | Per-form inert-dial tooltips | `PyreWindow.cs:2800-2888` | explains why a dial does nothing |
| 130 | Raw `Z.Object<T>` pickers (Sprite, TMP font, prefab) | `PyreWindow.cs:1650,1776,2074` | not ZuiChip |
| 131 | Shaper bridge: PyreForm as IShaperCompositeSource | `PyreShaper/PyreFormCompositeSource.cs:35` | hosts a form unmodified |
| 132 | Shaper bridge: effect applier over SpriteFx | `PyreShaper/ShaperEffectApplier.cs:58` | runs authored effect refs |
| 133 | Shaper bridge: composite catalog + audit | `PyreShaper/PyreCompositeCatalog.cs:43`, `Editor/PyreShaper/PyreShaperCompositeAudit.cs:20` | nine-generator classification |
| 134 | `[MovedFrom]` migration of PyrePlus-era assets | `PyreForm.cs:182`, Kiln forms, `SpriteFxModifiers.cs:132` | |

---

## Surprises / half-built parts

1. **Playback3D is preview-only.** Advertised as a form but `PyreRenderer.cs:262` has "deliberately no runtime bake"; bake/GIF/runtime of a Playback3D layer produce nothing for it. UI labels it "(POC)".
2. **Determinism contract has holes outside the renderer.** `CherryFrame.cs:65,75` calls `UnityEngine.Random.Range` (multi-frame pick with seed 0, and min-max length) — the cherry preview is non-deterministic. Chunks' `PyreSpawnModule.cs:128-129` / `PyreMotionModule.cs:83` also fall back to `Random` when seed = 0. No "BC-n" rule tags exist anywhere in Pyre or SpriteFx source.
3. **Four geometry modifiers are shown but blocked** (Curl, CurlProgress, Smudge, PinWarp) because they need click-to-place authoring that this window never got (`PyreWindow.Modifiers.cs:351-359`). EdgeWarp is excluded entirely; the only Simulation modifier is PixelFluid.
4. **No spec-wide Simulation slot** — "deferred follow-up" (`PyreWindow.Modifiers.cs:103`).
5. **PyreFormWarp for swarms "not built"** (`PyreFormWarp.cs:16`); field-accumulating forms get modifiers only whole-layer.
6. **`previewFps` is the only fps** — a `[HideInInspector]` preview pref doubles as the authored bake / clip / GIF frame rate (`PyreBaker.cs:90`).
7. **"Pyre Plus" naming survives** in `TypeLabel`, `NewAssetName`, undo labels, view keys `pyreplus.*` despite the rename being "done".
8. **Two `Orb`s** — the `ShapeForm.Orb` analytic sphere and the Kiln `OrbForm` energy projectile are unrelated.
9. **Pyre is NOT a LauAsset subclass** (plain `ScriptableObject`); LauTag/LauAsset integration comes from the AssetKit window base and Mirage's picker, not from the asset type.
10. **Pickers are raw `Z.Object<T>`**, not ZuiChip/LauAssetPicker (contradicts the "never type a reference" / chip-everywhere rule).
11. **No onion skin, no pan, no wheel zoom** in the preview; zoom is integer 1..16 via slider only.
12. **The Shaper bridge already exists** (`Runtime/PyreShaper`, `Editor/PyreShaper`): Shaper can already host any of the nine `PyreForm` generators and run SpriteFx modifiers as effects — the parity gap is the enum-form raster (§2a), the swarm/matte/coalesce/layer machinery, and the window UX, not the Kiln generators.
13. **Arc Burst silently degrades** to a managed fallback with a warning when Burst compilation is off (`ArcRaster.cs:66`).
14. **`PyreClipStats` "NEVER NORMALISE PER FRAME"** and the negative-field-id rule (`PyreRenderer.cs:50-56`) are load-bearing conventions carried only in comments.
