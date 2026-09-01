# T-0098 — Investigator B: the compatibility rule (mattes, SpriteFx, fuse, hand-drawn paths)

Research only. No code changed, no Unity run, no Coplay. All paths below are relative to `Assets/Packages/Laubrary/` unless written in full. Line numbers are from the working tree as of 2026-08-30 on `feat/lathe`.

**The one-line answer to the owner's complaint.** Compatibility is *not* per-generator-per-effect and it is *not* arbitrary. There are exactly **five pipeline stages** a thing can plug into (per-sample geometry warp, per-lit-pixel recolour, whole-buffer post, silhouette-edge, simulation-slot). A generator "supports" an effect if and only if that generator's draw routine calls that stage's hook. Roughly half the enum generators skip the geometry hook, two skip everything except post, and one (`Playback3D`) draws nothing at all in the bake. **Nothing in the UI says which hooks a generator calls** — so the same modifier appears in the same list under every generator and silently does nothing under half of them. Publishing three booleans per generator would close the whole gap.

---

## 1. The matte/mask system

**Plain answer.** There are two matte systems, they do genuinely different jobs, and they overlap only in the trivial "hide part of a layer" case. Path A (**WriteMatte → numbered channel → `clipByChannel`**) is a *coverage stencil*: an invisible layer deposits its **alpha** into one of four scalar planes, and Draw layers above multiply their own alpha by that plane. It only ever removes opacity. Path B (**LumaMatte → `matteFlags`**) is a *luminance mask with six effects*: an invisible layer's **luminance × alpha** becomes a 0..1 mask which then imposes any combination of Alpha / Brightness / Saturation / Hue / Blur / Displace on the layers above. Only its Alpha bit duplicates path A, and even then path A reads coverage while path B reads brightness, so a black-but-opaque shape is a full stencil in A and an empty mask in B. Path C is a *third* thing sharing path A's plumbing: `matteWriteLuma` makes a WriteMatte deposit luminance instead of coverage, and `heightFromChannel` makes some other Draw layer **stop drawing its own shape entirely** and instead render that accumulated plane as a relief-lit heightmap. Path C is not a mask at all — it is a generator that eats mattes.

**And here is the important part: all three paths are generator-agnostic by construction.** Every one of them operates on the layer's *finished RGBA buffer*, after the form, its post modifiers and its sim have all run. They never ask what drew the pixels. So the answer to "can every generator be a matte source / be matted?" is **yes, with exactly one exception, and one soft failure mode**.

### Evidence — the matte never sees the generator

* Role is decided from serialized flags only, never from `shapeForm` or `layer.form`: `PyreRenderer.Layers.cs:94-100` (`p.isMatte = matteOn && layer.matteRole == MatteRole.WriteMatte;` etc.).
* A matte layer is rendered by the **same** `RenderLayerBody` as any Draw layer — form, border, post modifiers, sim included (`PyreRenderer.Layers.cs:151-178`), because matte layers never get `fastPath` set (`PlanFrame` `continue`s at `:102`/`:103` before `p.fastPath` is assigned), so they always render into isolated scratch.
* Harvesting is pure buffer math: `WriteMatteCoverage` reads `scratch[i].a` (or `Luma(scratch[i]) * a`) — `PyreRenderer.cs:908-920`. `BuildMatteMask` reads `Luma(c) * (c.a/255)` — `PyreRenderer.cs:1070-1083`.
* Application is pure buffer math: `CompositeLayer` multiplies alpha by the clip plane (`PyreRenderer.cs:922-937`); `ApplyMatte` runs the six channels in a fixed order Displace → Blur → Saturation → Hue → Brightness → Alpha (`PyreRenderer.cs:1099-1122`).
* The heightmap consumer **discards the layer's own generator**: it draws the channel through `layer.shapeFill` and never calls the form at all (`PyreRenderer.cs:1849-1884`, `if (field == null) return;` then a straight loop over `field`).

### Table — who can participate, and on which side

"Matte source" = can be set to WriteMatte or LumaMatte and produce a useful mask. "Can be matted" = can be a Draw layer that is clipped by a channel and/or shaped by an armed LumaMatte.

| Generator | Matte source? | Can be matted? | Note |
|---|---|---|---|
| Disc, Crescent, Ring, Streak, Star, Polygon, Sparkle | ✅ | ✅ | — |
| Gem, Box, Pyramid, Can (facet solids), Orb | ✅ | ✅ | — |
| **Sprite** | ✅ | ✅ | Falls back to a Disc raster if the texture is not Read/Write (`PyreRenderer.cs:2948-2956`), so the matte is then a disc, not the sprite. |
| **Text** | ✅ | ✅ | Needs a usable font (`_textReady`, `PyreRenderer.cs:166`); with no font each glyph degrades to a Disc, so the matte is a row of discs. |
| Fire, Fireball (stateful sims) | ✅ | ✅ | They render into forced-isolated scratch (`PyreRenderer.Layers.cs:113`), so their pixels are harvested normally. |
| **Kiln plug-in forms** (Inferno, Orb, Torch, Arc Burst, Plasma Bloom, Fork Blast, Jet family) | ✅ | ✅ | `layer.form != null` forces scratch too (`Layers.cs:114`). |
| **`Coalesce = Fuse`** (MetaBlob field pass) | ✅ | ✅ | The fused blob is composited into the same scratch (`PyreRenderer.cs:1591`). |
| **`Coalesce = Ramp`** (HeightBalls field pass) | ✅ | ✅ | Same (`PyreRenderer.cs:1595`). |
| **`Playback3D`** | ❌ **produces an all-zero mask** | ❌ **nothing to matte** | `RenderLayer` returns immediately: `if (layer.shapeForm == ShapeForm.Playback3D) return;` — `PyreRenderer.cs:269`. Deliberate ("EDITOR-PREVIEW ONLY … renders NOTHING here"), but nothing in the matte UI says so. |
| Height consumer (`heightFromChannel >= 0`) | ❌ *by construction* | ✅ | `p.isHeightConsumer` is only assigned after the `isMatte`/`isLuma` branches have `continue`d (`Layers.cs:102-105`), so a layer is a writer **or** a consumer, never both. It can still be clipped and luma-matted (both are checked on it at `Layers.cs:99` / `:106`). |

**The soft failure mode (this is probably half of what feels "unpredictable").** WriteMatte's *default* deposit is **alpha only** — colour is irrelevant. LumaMatte's deposit is **luminance × alpha**. So the *same* generator, used as a matte, behaves completely differently between the two roles: a dark smoke layer is a perfect WriteMatte stencil and a near-useless LumaMatte (mask ≈ 0 everywhere ⇒ the layers above are unaffected, or with `matteInvert` fully affected). Nothing in `BuildMatteBox` (`Editor/Pyre/PyreWindow.cs:970-1010`) warns about this, and the failure looks exactly like "the matte doesn't work with this shape".

**Second trap, editor-only.** When the *selected* layer is `Playback3D`, `PyreWindow.Preview.cs:94-96` swaps the whole composited canvas for `DrawPlayback3DPreview` — the 3D preview *replaces* the layer stack blit (`PyrePlayback3DPreview.cs:5`). So while authoring a Playback3D layer you cannot see the mattes, the other layers, or anything else in the stack.

**Do the two paths overlap?** Only on one bit. `MatteChannel.Alpha` and `clipByChannel` both scale opacity. Everything else is exclusive: path A has four *combinable numbered* channels with Max/Add/Subtract (`MatteCombine`, `Pyre.cs:164`) and an invert; path B has one *armed* mask with scope (NextLayer / AllAbove, `Pyre.cs:155-159`), a strength envelope, an α-source modulation, and five non-alpha effects path A cannot express at all. They are not redundant. What *is* confusing is that they share one "Matte" box and one `matteEnabled` master switch (`Pyre.cs:203`), and that `matteChannel` (an int, path A) and `matteFlags` (a `MatteChannel` flags enum, path B) are two different things with near-identical names — `Pyre.cs:140-141` calls this out in a comment, which is a sign the naming is a known hazard.

---

## 2. The SpriteFx modifier compatibility rule

**Plain answer.** The hypothesis is **partly right and partly wrong, and the wrong part is the important one.** Category (a) — pure post-process filters that work on any finished buffer — is real and is the majority. Category (c) — needs geometry-level access before rasterisation — is real, and is where nearly all the incompatibility lives. Category (d) — needs a simulation grid — is real but is a *slot*, not a list entry, and is already handled. **Category (b) does not exist.** There is no auxiliary channel any generator writes and any modifier reads. `SpriteFxAuxMap` derives its map *from the finished picture's own pixels* — luma, saturation, alpha, Sobel edges, chamfer distance fields (`SpriteFxAuxMap.cs:16-27`) — so it needs nothing from the generator. `RelightModifier` likewise **invents** its height field from the picture (`SpriteFxRelight.cs:463`, using `SfxReliefSource` = Silhouette/Slab/Brightness, `:46-52`). `PyreShade.cs` is a colour-ramp/LUT helper, not an aux channel. So the refinement is:

> **The real rule: a modifier plugs into one of five *stages*. A generator supports a modifier iff that generator's draw routine calls that stage's hook. Nothing else matters — not the shape, not the colour, not the sprite.**

### The five stages and who calls them

| Stage | Base class | Applied where |
|---|---|---|
| **Geometry** — per-sample inverse warp of the coordinate grid | `GeometryModifier` (`SpriteFxModifiers.cs:191`) | Pyre: inside each raster body, via `ApplyGeo`/`ResolveSample`. SpriteFx Stack: `RunWarp`, a whole-buffer resample (`SpriteFxBurst.cs:679`). |
| **Pixel** — per-lit-pixel recolour / drop | `PixelModifier` (`:661`) | `ApplyPix` inside each raster body; `RunManagedPixel`/Burst job in a Stack. |
| **Post** — whole-buffer pass | `PostModifier` (`:1745`) | `ApplyLayerPost` per layer + the global loop in `FrameComposer.Finish` (`Layers.cs:141-165`); `post.Apply` in a Stack. |
| **Edge** — perturb the silhouette hit-test only | `EdgeModifier` (`:2565`) | **NOWHERE.** No caller of `EdgeOffset`/`EdgeSoftness` exists in the repo (grep over `Runtime/`+`Editor/` returns only the declaration and two comments). |
| **Simulation** — stateful replay grid | `SimulationModifier` (`SpriteFxSimulationModifiers.cs:44`) | Pyre only, via the dedicated `layer.simulationModifier` slot (`Pyre.cs:766` → `PyreRenderer.ApplyLayerSim`, `:1410`). Not a list entry. |

### Which Pyre generators call the Geometry hook — the actual incompatibility

Measured by counting `ApplyGeo` / `ResolveSample` / `anyGeo` occurrences inside each draw routine's body in `Runtime/Pyre/PyreRenderer.cs`:

| Draw routine (generator) | Geometry | Pixel |
|---|---|---|
| `DrawParticle` disc raster — Disc (`:2810`, `ApplyGeo` ×2, `anyGeo` ×5) | ✅ | ✅ |
| `DrawCrescentBody` (`:3140`) | ✅ (`ResolveSample`) | ✅ |
| `DrawStarBody` (`:3254`), `DrawPolygonBody` (`:3375`), `DrawSparkleBody` (`:3470`), `DrawSpriteBody` (`:3553`), `DrawStreakBody` (`:3672`) | ✅ (`ResolveSample`) | ✅ |
| **`RenderTextLine` (`:3859`) / `DrawTextChar` (`:3925`)** | ❌ **0 occurrences** | ✅ |
| **`DrawFacetSolid` — Gem/Box/Pyramid/Can (`:4152`)** | ❌ **0** | ✅ |
| **`DrawOrb` (`:4418`)** | ❌ **0** | ✅ |
| **`DrawRing` (`:4609`)** | ❌ **0** | ✅ |
| `RenderPlusFusedField` — `Coalesce = Fuse` (`:1617`) | ✅ (`:1648`) | ✅ (`:1669`) |
| **`RenderPlusRampField` — `Coalesce = Ramp` (`:1697`)** | ❌ **0** (`anyPix` only, `:1813`) | ✅ (`:1832`) |
| **`RenderFireLayer` (`:392`) / `RenderFireballLayer` (`:757`)** | ❌ — **`mods` is not even a parameter** | ❌ |
| **`RenderHeightConsumer` (`:1849`)** | ❌ — no `mods` parameter | ❌ |
| **`Playback3D`** | ❌ nothing renders (`:269`) | ❌ |
| Kiln plug-in forms | ✅ *indirectly* — whole-buffer resample after `Render` via `PyreFormWarp.BuildMap/Apply` (`:349-350`), unless the form sets `HandlesGeometry` (only `InfernoForm.cs:19`) and warps per sample itself | ✅ — the form must call `ctx.pix` itself; all seven Kiln forms do (verified: `OrbForm.cs:560/573`, `TorchForm.cs:292/305`, `InfernoForm.cs:186`, plus `ArcBurstForm`, `ForkBlastForm`, `PlasmaBloomForm`, `JetFormBase`) |

**So: eight of the fifteen live enum generators silently ignore every one of the 15 Geometry modifiers**, and Fire / Fireball / Playback3D / the height consumer additionally ignore all 11 Pixel modifiers. Post modifiers work on everything that produced pixels. That is the whole "works with some and not others" phenomenon.

Note also that Geometry means **two different things** in the two hosts. In Pyre it is a per-sample warp *inside the shape raster*, with `GeoCtx.center`/`radius` set to the shape's own frame. In a SpriteFx Stack it is a **whole-buffer resample** with `GeoCtx` pinned to the canvas centre and `radius = min(halfW, halfH)` (`SpriteFxBurst.cs:682-683`). Same modifier, different semantics — an effect tuned in one will not land identically in the other.

### The 41 modifiers, classified

41 concrete modifiers (memory's "~42" is close; the exact count is 41 + 5 abstract bases). Classification = which stage, therefore what it needs.

**Geometry (15)** — need a generator that calls the geometry hook. Fail silently on Text / Gem / Box / Pyramid / Can / Orb / Ring / Ramp / Fire / Fireball / Playback3D / height consumer:
Skew (`:203`), Scale (`:228`), Rotate (`:266`), Wobble (`:302`), Sunburst wobble (`:339`), Ring wave (`:386`), Blast (`:437`), Profile (`:546`), Ground (`:583`), Sunburst (`:779`), Pulse rings (`:830`), Turbulence (`:2653`), Sphere/fake depth (`:3084`), **Curl (`:2882`)**, **Vortex field (`:3007`)**, **Smudge (`:3167`)**, **Pin warp (`:3340`)**. *(That is 17 lines; Curl/CurlProgress/Smudge/PinWarp are also in §4 because they additionally need hand-authored data.)*

**Pixel (11)** — need only lit pixels. Work on every generator **except** Fire, Fireball, Playback3D, height consumer:
Tint (`:686`), Contrast (`:722`), Brightness (`:738`), Saturation (`:754`), Posterize (`:869`), Ordered dither (`:902`), Voronoi crack (`:942`), Layer dissolve (`:1247`), Wipe (`:1299`), Colour tint (`:1450`), Colour replace (`:1602`), Colour remap (`SpriteFxColorRemap.cs:142`).

**Post (12)** — pure image filters over a finished buffer. Work with **everything** that produced pixels, including Fire/Fireball:
Dissolve (`:1157`), Bloom (`:1787`), Outline (`:1887`), Chromatic aberration (`:2102`), Ballistic shockwave (`:2200`), Fuse/blob melt (`:2459`), Edge smooth (`:2734`), Drop shadow (`:3386`), Kaleidoscope (`:3481`), Fake light (`SpriteFxRelight.cs:243`).
Five of these declare `OutwardReachPx()` (Bloom, Outline, Chromatic aberration, Drop shadow, Fake light) — they draw *outside* the silhouette, and on a tight canvas they run and are invisible. That is a **canvas-size** dependency, not a generator dependency, and it is the one real "needs something from its host" case: the host must grow the buffer (`SpriteFxStack.OutwardReach`, `SpriteFxBurst.cs:492`).

**Edge (1)** — Edge warp (`:2585`). **Dead everywhere.** No host calls `EdgeOffset`. Correctly excluded from both add-menus (`PyreWindow.Modifiers.cs:384-385`, `SpriteFxStackView.cs:343`), so it is unreachable but also unauthorable — it is orphaned code, not a trap.

**Simulation (1)** — Pixel fluid (`SpriteFxSimulationModifiers.cs:115`). Works in Pyre via its own slot with its own UI (`PyreWindow.Modifiers.cs:170-227`). **But** `SpriteFxStackView.Catalog()` (`:329-380`) filters out only `EdgeModifier`, so `PixelFluidModifier` *is* offered in the SpriteFx Stack add-menu — under the "Colour & mask" section, because it is neither Geometry nor Post — and `SpriteFxStack.RunStack`'s switch (`SpriteFxBurst.cs:640-667`) has no `SimulationModifier` case, so it does **absolutely nothing** there. That is a live, reachable, silent no-op.

**One more orphan.** `SpriteFxAuxMapGate` (`SpriteFxAuxMapGate.cs:35`) — a whole per-list gate with 12 authored dials, a `previewHeatmap` mode and a documented `RunStack` mechanism — is **not a field on anything**. A repo-wide grep for `SpriteFxAuxMapGate` outside its own file returns only three comments. Nothing constructs it, nothing calls `Prepare`/`Bake`. It is fully built, fully documented, and completely unreachable. Its doc comment ("see `SpriteFxStack.RunStack` for the mechanism this drives") describes code that does not exist.

### The owner's actual question: how many facts must we publish per generator?

**Three booleans and one flag, per generator.** That is the whole answer, and it is derivable from the code today without any behaviour change:

1. `SupportsGeometryModifiers` — does its raster call `ApplyGeo`/`ResolveSample`, or (for a plug-in form) does the post-hoc `PyreFormWarp` resample apply? *Discriminates 8 generators.*
2. `SupportsPixelModifiers` — does it call `ApplyPix` / `ctx.pix`? *Discriminates Fire, Fireball, Playback3D, height consumer.*
3. `ProducesPixels` — false only for Playback3D. Implies "no post either, no matte either, nothing". *Discriminates 1, but it is the most confusing one because it silently kills everything at once.*
4. `SwarmRole` — one of `PlacesParticles` / `IgnoresSwarm` / `SwarmAsEmitters`. This is what makes `Coalesce` meaningful: `Coalesce` is only read inside `RenderSwarm` (`PyreRenderer.cs:1497-1498`), which is only reached when `layer.form == null && !Fire && !Fireball && !Playback3D && swarmEnabled` (`RenderLayer`, `:248-282`). Today the Coalesce radio is drawn for plug-in forms and Playback3D with no warning at all (`PyreWindow.cs:2491-2495`; the Fire/Fireball note at `:2177-2190` is the *only* case that warns).

Coverage check against real complaints: (1)+(2) explain every silent Geometry/Pixel no-op; (3) explains Playback3D; (4) explains "Coalesce does nothing on my Inferno layer". The `OutwardReachPx` canvas issue is a fifth fact but it belongs on the **modifier**, not the generator, and the code already publishes it.

**Would a capability/tag system in the `ISpriteFxPreviewOverlay` spirit solve it?** *Partly — and the shape needs one change.* `ISpriteFxPreviewOverlay` (`SpriteFxPreviewOverlay.cs:141-171`) is the right *pattern*: the capability lives on the object, the host discovers it by reflection, and the host has zero per-effect code (`SpriteFxPreviewOverlays.cs:73`). Copy that. But note the direction is inverted for this problem. The overlay interface is declared by the **modifier** and consulted by the **host window**. Here the capability must be declared by the **generator** (the `ShapeForm` case or the `PyreForm` subclass) and consulted at *modifier-list build time* — the UI needs to grey out or badge a modifier row based on the currently-selected layer's generator, which is a different join than "collect every effect that wants an overlay". Concretely:

* For plug-in forms this is easy and already half-done: add `SupportsPixelModifiers`/`SupportsGeometryModifiers` next to the existing `PyreForm.HandlesGeometry` (`PyreForm.cs:195-201`) and `UsesFill`. The precedent is already there.
* For the 15 built-in `ShapeForm` enum cases there is no object to hang a virtual on, so it has to be a static table in `PyreRenderer` — the same shape as the existing `IsFlat2DBorderForm(ShapeForm f)` (`PyreRenderer.cs:950-952`), which is *exactly this pattern already*: one small static predicate that answers "does this form support the border stage?". Extend that idea to three more predicates and the data exists.
* The mechanism to *show* it also already exists: `DisabledInPicker` (`PyreWindow.Modifiers.cs:354-360`) already greys an add-menu row with a reason string. It is currently a hardcoded 4-entry dictionary; making it a function of `(modifierType, selectedLayer)` is the whole UI change.

So: yes, a capability system in that spirit solves it, and three of the four pieces (a per-form virtual, a per-enum static predicate, a greyed-with-reason menu row) already exist in the codebase and just need to be joined up.

---

## 3. The old "fuse" function

**Plain answer.** There are **three** things called fuse, not two, and the owner's memory is accurate — there *was* an old per-shape fuse toggle in the layer/shape view. It has **already been retired**: it was `Layer.fuse` in the original Pyre, which was deleted outright in the 2026-08-23 PyrePlus→Pyre rename, and its explicit replacement is `LayerCoalesce.Fuse`. There is nothing left to retire there. What *does* still exist and is easy to mistake for it is `FuseModifier` — "Fuse (blob melt)" — a post-process modifier in the modifier stack. That one is **not** redundant with `LayerCoalesce.Fuse` and should be kept.

### The three

**(3a) `Layer.fuse` — the old per-shape control. ALREADY GONE.**
Declared at `Layer.cs:548` in commit `cad2949` (pre-rename): `public bool fuse = false;` with the tooltip *"Ring/Rosing + Disc only: fuse every shape into ONE gradient-shaded metaball field (like MetaBlob, but fed by this layer's own procedurally-placed discs)"*. Its UI was in the layer view (`Editor/Pyre/PyreWindow.Layers.cs:237-238`, old commit) and the renderer gated it hard: `bool fuseActive = layer.fuse && layer.shape == LayerShape.Disc && …` (`BlastRenderer.cs:671`, old commit). All of those files were **deleted** by `60309923` ("Rename PyrePlus -> Pyre package-wide"). The current `Runtime/Pyre/Pyre.cs` has no `fuse` bool — only `LayerCoalesce` and the three `fuse*` dials (`fuseThreshold` `:276`, `fuseShadeRange` `:278`, `fuseSoftness` `:280`) which belong to Coalesce. **Verdict: already retired, nothing to do.** Worth noting the retirement was deliberate and documented: `PyreToPlusConverter.cs:192/256-258` mapped `MetaBlob → dst.coalesce = LayerCoalesce.Fuse` and copied `metaThreshold/metaShadeRange/metaSoftness → fuseThreshold/fuseShadeRange/fuseSoftness`.

**(3b) `LayerCoalesce.Fuse` — the new field pass. KEEP.**
`Pyre.cs:173`. A *render mode* for the swarm: instead of Over-compositing each particle, the whole placed cloud is collected as metaball circles and read as one scalar field → threshold → gradient-shaded iso-surface (`RenderPlusFusedField`, `PyreRenderer.cs:1617`). It produces real metaball **necks** between overlapping particles and shades the result through the layer's Fill by field value. It is **pre-rasterisation** and **within one layer's swarm only**. Caveat worth recording: *"The form is ignored in both modes: a coalescing particle is always a circle/dome"* (`PyreRenderer.cs:1552-1553`) — so setting Coalesce on a Star or Streak layer throws that generator's shape away, which is another silent surprise the UI does not state. And it is a total no-op for plug-in forms, Fire, Fireball, Playback3D and swarm-off layers (see §2), yet the radio is still drawn for them.

**(3c) `FuseModifier` "Fuse (blob melt)" — the post-process melt. KEEP.**
`SpriteFxModifiers.cs:2459-2555`. A `PostModifier`: premultiplied 4-channel box blur of the finished buffer (`BoxBlur4`, `:2532`) → threshold the blurred alpha with a softness ramp → re-solidify, with `colorBleed` controlling how much colour crosses the new seam. Dials: radius / threshold / softness / colorBleed.

### Are (3b) and (3c) redundant? No.

| | `LayerCoalesce.Fuse` | `FuseModifier` |
|---|---|---|
| Stage | pre-raster field pass | post-process on finished pixels |
| Input | particle centres + radii + alpha weights | any RGBA buffer |
| Scope | **one layer's swarm only** | the layer (as a layer post) or **the whole frame** (as a global post, `Layers.cs:146-161`) — so it can fuse *across layers*, and fuse Text to a Sprite to a Fire |
| Works with | swarm-on enum-form layers only | **every generator that produces pixels**, and every SpriteFx Stack on an arbitrary sprite |
| Shading | re-shades the field through the Fill (true metaball gradient) | preserves each pixel's own colour, blended by `colorBleed` |
| Silhouette quality | exact iso-surface, real necks | blur-and-threshold approximation |

They solve different problems and neither can do the other's job. `FuseModifier` is also **in live use** — `Assets/Pyre/Imported/Curl Plus.asset` contains one.

**Verdict: keep both (3b) and (3c); (3a) is already gone.** The only action worth taking is naming: three things called "fuse" plus a fourth unrelated `fuse*` dial-prefix on the Coalesce box is the actual source of the owner's uncertainty. Renaming `FuseModifier`'s display name to something like "Blob melt" (it already carries the parenthetical) would make the ambiguity disappear without touching serialized type names.

---

## 4. The hand-drawn-path SpriteFx

**Plain answer.** There are **four** of them, they are all `GeometryModifier`s, they all still render correctly, and their authoring UI is **completely gone**. It lived in the original Pyre's live-canvas preview overlay, which was deleted in the 2026-08-23 rename. Both add-menus now hard-block all four with a reason string pointing at a window that no longer exists. Two of the four have **authored data in real assets in this repo**, so deleting the classes would silently break those assets. And `PinWarpModifier` is additionally broken at runtime in a way nobody has noticed.

### The four

| Modifier | Hand-authored data | Line |
|---|---|---|
| **Smudge** | `List<SmudgeStroke> strokes`, each `List<Vector2> points` — drag-to-paint strokes; each pixel within `size` px of a stroke is dragged backward along its tangent, with a `grow` front advancing along every stroke in parallel | `SpriteFxModifiers.cs:3152` (stroke), `:3167` (modifier) |
| **Pin warp** | `List<PinDot> dots`, each with a `radius` and a **keyframed** `List<PinKeyframe>` — click to place, then scrub frames and drag to keyframe | `:3260` (keyframe), `:3274` (dot), `:3340` (modifier) |
| **Curl (swirl)** | `List<VortexPoint> vortices` — click-to-place swirl centres | `:2826` (point), `:2882` (modifier) |
| **Vortex field (progress)** | same `List<VortexPoint>` | `:3007` |

(`SphereModifier` also has a `List<Vector2> points` in a naive grep, but its real dials are `strength`/`originX`/`originY`/`radius` — `:3084-3105`. It is a plain parametric lens, **not** hand-drawn. Not one of these.)

### Is the drawing UI reachable today? No — and the code comments are stale.

* Both add-menus block all four identically, with the reason *"Needs click-to-place pins in **Pyre1's own preview canvas** — not authorable here."* — `Editor/Pyre/PyreWindow.Modifiers.cs:354-360` and `Editor/SpriteFx/SpriteFxStackView.cs:321-327` (duplicated verbatim; each file's comment says "Duplicated in the other").
* Both comments claim *"click-place/drag-keyframe/paint-stroke input only exists in **PyreWindow.Preview.cs**"* (`PyreWindow.Modifiers.cs:352`, `SpriteFxStackView.cs:319`). **This is false for the current file of that name.** A repo-wide grep for `PinWarpModifier|SmudgeModifier|CurlModifier|CurlProgressModifier|SmudgeStroke|PinKeyframe|VortexPoint|PinDot` across all of `Assets/**/*.cs` returns **only** the class definitions in `SpriteFxModifiers.cs` and the eight `DisabledInPicker` lines above. `Editor/Pyre/PyreWindow.Preview.cs` (717 lines) contains **zero** occurrences of pin / smudge / vortex / stroke. The comment is talking about the *deleted* Pyre1 file — the same file `PyreWindow.Modifiers.cs:6-10` refers to when it says *"Pyre's `PyreWindow.BuildModBody` is a ~350-line switch, and several of its cases are wired into Pyre's preview-overlay authoring state (pin/vortex/stroke editing lives in `PyreWindow.Preview.cs`) … **Pyre has no such overlay**"*. So the codebase already knows this, in one sentence, and the `DisabledInPicker` reason strings never got updated.
* The only capability the preview *does* have is `ISpriteFxPreviewOverlay`, and it is **draw-only** — `DrawPreviewOverlay(SpriteFxOverlayCanvas)` with no input, no hit-testing, no drag (`SpriteFxPreviewOverlay.cs:141-171`). It cannot be used as-is to rebuild the authoring; it would need an input half added.

### Does anything actually use them? Yes — two assets.

| Asset | Contains |
|---|---|
| `Assets/Pyre/Imported/Bars Tentacle Plus.asset` | `SmudgeModifier` ×1 |
| `Assets/Pyre/Imported/MetaBall Blast Plus.asset` | `SmudgeModifier` ×1 |
| `Assets/Pyre/Imported/Curl Plus.asset` | `CurlModifier` ×1 (+ `FuseModifier` ×1) |
| `Assets/Pyre/Imported/Explotion Twirl Plus.asset` | `CurlModifier` ×2 |

No asset in the repo uses `PinWarpModifier` or `CurlProgressModifier`.

### The extra breakage nobody has noticed: Pin warp is inert-by-animation

`PinWarpModifier` evaluates every pin at `currentFrame` (`:3355` onward), and `currentFrame` is only ever set by `public void SetFrame(int frame)` at `:3350`, whose doc comment says *"Called once per rendered frame by **BlastRenderer**"* — Pyre1's renderer, deleted. **A repo-wide grep for `SetFrame(` (excluding `SetFrameIndex`) returns exactly one hit: the declaration itself.** So even if a Pin warp were authored, `currentFrame` stays 0 forever and every pin holds its first keyframe's position for the whole clip — the keyframe animation, which is the entire point of the modifier, is dead. `PostModifier` got its equivalent hooks re-plumbed in the port (`SetLife`/`SetSeed`/`SetFrameIndex`, `:1755-1765`, wired at `SpriteFxBurst.cs:650-652`); `PinWarpModifier.SetFrame` was missed.

### Verdicts

* **Smudge — KEEP, and rebuild the authoring.** Two assets depend on it, and it is the only effect in the whole set that can smear an arbitrary picture along a chosen path. It is also the *cheapest* to re-author: `SmudgeStroke` is just a `List<Vector2>` in canvas-centre pixels, so the UI is "drag records points, release closes the stroke" — no keyframes, no per-item state.
* **Curl (swirl) — KEEP, and rebuild the authoring.** Two assets depend on it. `VortexPoint`s are click-to-place, i.e. an even simpler input than Smudge.
* **Vortex field (progress) — CULL, unless the owner wants it.** Nothing in the repo uses it, and it duplicates Curl's authoring surface (same `List<VortexPoint>`, same `IVortexHost`) with a progress-driven variant. Reviving two vortex authoring UIs to serve zero assets is not worth it. If it is kept, it should be kept *behind the same* vortex editor as Curl, not as a second one.
* **Pin warp — CULL, or fix `SetFrame` first.** Zero assets use it, it needs the most elaborate UI of the four (place → scrub → drag → keyframe, with per-dot rest positions and sorted keyframe lists), *and* it is currently animation-dead. Rebuilding the hardest authoring UI in the set for an effect that no asset uses and that would still need a renderer hook re-plumbed is the worst ratio here. If the owner does want it, the `SetFrame` call must be restored in `RenderLayerBody`/`CollectMods` and in `SpriteFxStack.RunStack`'s `GeometryModifier` case — otherwise it will be rebuilt and *still* not animate.
* **Regardless of the above: fix the four `DisabledInPicker` reason strings** (×2 files, 8 lines). They currently tell the user to go to a window that was deleted, which is worse than saying nothing.

---

## UNVERIFIED / open questions

**Not verified (no Unity run, per the brief):**

1. Every claim here is from **static reading of the source**. Nothing was confirmed by rendering a frame. In particular the "silently does nothing" claims for Geometry-on-Text / Geometry-on-Orb / Coalesce-on-a-Kiln-form are inferred from the absence of an `ApplyGeo`/`ResolveSample` call in the relevant function body — a strong inference, but a rendered A/B would be the real proof.
2. **Not verified:** whether the *editor preview* path (`PyreWindow.FrameCache.cs`, `PyreWindow.Preview.cs`) reproduces the bake exactly for the matte cases. `PyreRenderer.Layers.cs:6-18` documents a known fast-path/quantisation divergence between straight-draw and cached-layer composition; I did not check whether any matte case falls on the diverging side.
3. **Not verified:** whether a Kiln form's `PyreFormWarp` whole-buffer resample gives a *visually equivalent* result to a per-sample warp. `PyreForm.cs:197-201` presents it as a free-for-all equivalence; `InfernoForm` opting out (`HandlesGeometry => true`) suggests it is not always adequate, but I did not read `PyreFormWarp.cs` closely enough to say when it degrades.
4. **Not verified:** I did not enumerate which of the 12 Post modifiers behave badly on a *tightly cropped* Pyre canvas. `OutwardReachPx` is declared by 5 of them, and `SpriteFxStack` grows the buffer accordingly (`SpriteFxBurst.cs:492`), but **I did not confirm that Pyre's own `ApplyLayerPost`/`FrameComposer.Finish` honours `OutwardReach` at all** — `PostModifier.SetPicture`'s doc (`SpriteFxModifiers.cs:1766-1780`) explicitly says Pyre's renderer "knows nothing about this one" and leaves `pictureW/H` at 0. If Pyre never pads, then Bloom/Outline/Drop shadow/Fake light are partially clipped in Pyre and not in a Stack — that would be a real second axis of "works here, not there" and is worth a follow-up.

**Open questions for the owner:**

5. `SpriteFxAuxMapGate` is fully built and completely unreachable (no field of that type exists anywhere). Was it abandoned mid-wiring, or is a host meant to hold one? It is ~154 lines of dead but high-quality code with a documented `RunStack` mechanism that does not exist.
6. `EdgeModifier`/`EdgeWarpModifier` is unreachable in both hosts and correctly hidden from both add-menus. Delete, or is a silhouette-edge stage still planned?
7. `PixelFluidModifier` is offered in the **SpriteFx Stack** add-menu and does nothing there. Should `SpriteFxStackView.Catalog()` exclude `SimulationModifier` the way `PyreWindow.Modifiers.cs` does, or should `RunStack` grow a simulation case?
8. `Coalesce` throws away the layer's shape (`PyreRenderer.cs:1552`). Is that intended to be permanent, or should Fuse/Ramp stamp the actual form instead of a circle?
9. Playback3D: it has a full editor preview and renders nothing in the bake. Should the UI mark it as preview-only (it currently sits in the shape picker alongside 15 real forms, `PyreWindow.cs:1162`), or is the bake path being built?
