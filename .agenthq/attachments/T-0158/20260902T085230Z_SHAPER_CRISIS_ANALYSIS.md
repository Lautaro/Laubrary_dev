# Shaper crisis analysis — where Shaper actually stands (T-0158)

**Date:** 2026-09-02. **Branch/commit audited:** `feat/shaper` @ `1fa4f21a` in `D:\UNITY\Laubrary Dev - Shaper`. **Author:** the crisis-manager session (Fable 5.1), from three verified source inventories that sit beside this file: `pyre-inventory.md` (134 capability rows), `shaper-inventory.md` (24-rule standards scorecard + checklist), `agenthq-digest.md` (52-task ledger over the Shaper node and the standalone 3D Shaper board). Every claim below was checked against source with a `file:line` in those appendices; nothing here comes from the mock UI or from memory. Nothing was verified by operating the editor window with a mouse — that is called out explicitly in §8.

---

## 1. The one-page verdict

**Your two suspicions are both correct, and the second one is worse than you think.**

1. **Shaper was not built on the Laubrary tool standard.** The window extends the plain `ZuiWindow`, not the AssetKit asset window (`ZuiAssetWindow<T>`) that Pyre and fifteen other Laubrary tools use. That single choice is why you saw a raw Unity object picker and a file-explorer save dialog: the AssetKit base is the thing that provides the library browser, thumbnails, New/Duplicate/Rename/Delete, the Tags section and the "New" that lands in a conventional folder. Nobody on either board ever decided *against* AssetKit — the topic simply never came up. Zero mentions across every Shaper planning doc and task.

2. **Shaper does not carry Pyre's capabilities.** Counting against the 134-row Pyre inventory, Shaper today fully covers roughly a third, partially covers a fifth, and is missing or has deliberately dropped the rest (the matrix is §4). The big missing families are not fringe: the stateful **Fire / Fireball** generators, **Text**, **Sprite-as-shape**, the whole **matte/mask** system, most of the **swarm** vocabulary (Pyre has ~25 swarm dials, Shaper has ~8), per-layer **animated transforms** (Pyre's size/position/turn/tilt/roll/spin envelopes — in Shaper every node transform is a plain un-animatable float), **GIF export**, the **cross-tool interface** (Chunks/Zoe/Mirage cannot consume a Shaper output at all), and **layer lifetime windows**. Only three of those were dropped by an explicit ruling (Sparkle, Streak, Playback3D); the rest were never scheduled.

3. **Some of what exists does not work.** Three defects were found in the shipped engine + window that no task recorded: (a) **30 of the 41 addable effects can never run** — the add-menu stamps them `PreComposite` but the document renderer only ever executes the `PostComposite` stage, and the row offers no stage switch; (b) **no effect has any authorable parameter** — every effect runs at its class defaults; (c) **the document's light rig has no UI at all** — you can author how a layer *responds* to light, but the lights themselves (count, kind, colour, yaw/pitch, position) are Inspector-only. Also: the frame cache and background pre-baker (the whole of wave T-0115) are built and wired to nothing, so the preview re-renders every frame uncached; and every hosted Pyre generator's animatable dials are frozen to their static value.

4. **The process that produced this was visible on the board all along.** Every engine wave (T-0105..T-0115) closed as "done" with the same three caveats in its own words: nothing seen animated, nothing timed, no human has operated anything. Several found real defects *behind fully green audits*. Then the mock was built against the docs rather than the engine (T-0147 later found the mock had missed node Transform entirely and regressed 19 animatable fill dials to plain floats on a false citation). So the pattern you have been fighting — "pointing out every detail the mock forgot" — is structural: verification was API-level throughout, and the Handover Walk was never done on a real Shaper window by a human.

**What is genuinely good and should be kept:** the engine core is sound and better than Pyre where it overlaps. Deterministic (zero `Random` uses, hash-seeded everything), one shared render path for preview == bake == runtime clip, systematic Undo through one `Change` wrapper (essentially complete for data edits), zero IMGUI, ZUI controls chosen correctly (radios not dropdowns, MicroSliders, Pads, `Z.Value` for envelopes), a single flat `Laubrary/Shaper` menu item, non-square canvas, real height + bevel + light-rig shading, CSG bags with sweep/shell, borders on any node, seven fill kinds including Tapestry, an authored frame rate, and a bake that emits PNG sheet + AnimationClip + a `ShaperClip` that preserves cherry framing (which a Unity AnimationClip cannot).

---

## 2. What Shaper was supposed to be (the boards, distilled)

From the four T-0098 design documents (the decision document is `planning/Shaper.json` = `SHAPER_THE_DESIGN.md`) and the 3D Shaper board:

- **3D Shaper** was a standalone browser app for hand-building 2.5D pixel-art models: SDF primitives fused into silhouettes, a height map + z-buffer, a light rig with placeable lights, extrusion/bevel profiles, a palette-strip edge ring, a keyframe timeline, and *no export of any kind*. It reached "substantially finished against its own contract" on 2026-08-29 and stopped. The owner ruled that its maths be ported into Unity as Shaper's only primitive engine (Solids the named exception), and that nothing either tool ever produced needs to survive the refactor.
- **Merged Shaper**, per the design: a document = canvas + frames/rate + one light rig + ordered layers; each layer holds exactly one generator that produces one of **shape / fill / border / composite**; shapes nest as bags; fills emit albedo + height and *the light rig makes them shine* ("reflective, shiny, bevelled and lit are not fills"); borders on any node; the nine big Pyre generators come across as **composite** sources, *referenced not copied*; effects are universal with an explicit pre/post stage shown in the UI; swarm on every generator; caching + Burst-shaped buffers promoted to a contract; the whole thing "resolves to a sequence of frames, and those frames bake to a sprite sheet or an animation".
- **Explicitly ruled OUT:** Sparkle, Streak, Pin warp, Edge warp, the hand-drawn-path authoring surfaces, Vortex field (folds into swirl), the 3D playback generator, 3D Shaper's fixed hexagon/octagon/five-lobe star. *Everything else in Pyre is implicitly expected to survive* — the design says "When Shaper reaches parity the old tool goes as a whole."
- **Never addressed anywhere on either board:** AssetKit / library browser / New-Duplicate-Delete, LauAsset participation (Mirage/Zoe pickers), `IVisualPreview` thumbnails, a demo scene, a CHANGELOG entry, Samples~, the Handover Walk on the real window, per-layer pre-composite effects (later "deliberately not built"), layer Z-order semantics (a signed `zOffset` shades but never re-orders), Fire/Fireball/Text/Sprite/matte parity.

---

## 3. Laubrary tool-standard audit

Scorecard against `authoring.md`, `ui-layout-rules.md` and the project `CLAUDE.md` (full table with citations in `shaper-inventory.md` §9). **14 pass, 6 fail, 2 partial, 2 not verifiable from code.**

| Rule | Verdict | What it means for you |
|---|---|---|
| Asset-editing tool sits on the AssetKit window base (library browser, New/Duplicate/Rename/Delete, Tags) — what Pyre does | **FAIL** | This is the raw object picker + save dialog you hit. `ShaperWindow : ZuiWindow` (`Editor/Shaper/ShaperWindow.cs:36`), document picked via `Z.Object` (`:161`), New via `EditorUtility.SaveFilePanelInProject` (`:183`). No Duplicate, Rename or Delete of a document exists anywhere. Pyre is `ZuiAssetWindow<Pyre>` (`Editor/Pyre/PyreWindow.cs:20`). |
| Pickable visual asset implements `IVisualPreview` ("treat as build-breaking") | **FAIL** | Neither `ShaperDocument` nor `ShaperClip` implements it, so no browser, chip or Mirage picker could ever show a thumbnail. |
| Visual LauAsset guarantees a thumbnail | FAIL (follows) | No browser/picker path exists. |
| Demo = committed `.unity` scene in `Assets/Demos/ShaperDemo/` | **FAIL** | The folder holds ~200 Tapestry height-field preset `.asset`s and four editor probes. No scene. Nothing in the project references a `ShaperDocument` or `ShaperPlayer`. |
| CHANGELOG entry under `[Unreleased]` for every Laubrary code change | **FAIL** | `grep -i shaper CHANGELOG.md` → nothing, across ~33k lines of new code. |
| Samples~ mirror | FAIL (follows) | |
| Dev probes not shipped in the tool asmdef | PARTIAL | Seven `*Audit.cs` files = **14,933 lines, 84% of `Editor/Shaper`** compile into the shipping editor asmdef. They are CLI-invoked static probes, not tests, referenced by nothing. Every consumer project will compile them. |
| Create-asset menu consistency | PARTIAL | `Laubrary/Shaper Document` vs nested `Laubrary/Shaper/Tapestry Height Field Preset`. |
| Runtime/Editor split, asmdef naming, zero shipped assets | PASS | |
| ZUI window base, zero native controls, enum → radios, bounded scalar → MicroSlider, `ZUIValue` → `Z.Value`, X/Y → Pad | PASS | 0 IMGUI calls; only two text inputs and both *declare* names (compliant). |
| Undo on every data edit, recorded before mutation | PASS | One `Change` wrapper (`ShaperWindow.cs:656-665`) + `Val`/`Dial`; sampled edit sites all wrapped. Not yet verified by pressing Ctrl+Z in a real window (T-0146 says the same). |
| Tooltips on every control | PASS (sampled) | |
| Menu flatness — one `Laubrary/Shaper` | PASS | But the merged-in mock still registers `Laubrary/Shaper Mock (Prototype)` (`Assets/ShaperMock/Editor/ShaperMockWindow.cs:28`) in this worktree, so the menu currently shows two Shapers. |
| Determinism, one shared core | PASS | |
| Handover Walk + `ZuiAudit` before "done" | NOT VERIFIABLE | Comments cite a walk; no human-mouse pass recorded on any Shaper surface, mock or real. |

**Two standards notes that cut the other way, so you are not misled:**
- Pyre is *also* a plain `ScriptableObject`, not a "LauAsset subclass" — there is no `LauAsset` base type in the package. "LauAsset" is the AssetKit editor-side treatment (browser, chip, tags, `IVisualPreview`) that a tool opts into by using the AssetKit window base and implementing `IVisualPreview`. So the fix for Shaper is adopting that base + the interface, not changing what `ShaperDocument` inherits from.
- Pyre's own in-window object pickers (Sprite, TMP font, prefab) are also raw `Z.Object`, and `authoring.md` §4 still says "subclass `ZUIWindow` and draw in `OnZUI()`" (the IMGUI generation). The standard doc is slightly stale; Pyre's actual code is the better reference, and `ZuiAssetWindow<T>` is the base to copy.

---

## 4. Pyre → Shaper parity matrix

Legend: **HAVE** = present and at least as capable · **PARTIAL** = present but narrower · **MISSING** = absent and not ruled out by any decision · **DROPPED** = absent by an explicit owner ruling · **BROKEN** = present in code but does not work. Citations for the Pyre side are in `pyre-inventory.md` §9 (row numbers), for the Shaper side in `shaper-inventory.md`.

### 4a. Asset, window, workflow
| Pyre capability | Shaper | Note |
|---|---|---|
| Asset browser grid with animated thumbnails; New / Duplicate / Rename / Delete-with-confirm (AssetKit) | **MISSING** | The reported symptom. |
| LauTag tags section | MISSING | |
| Views (fold presets, `ZuiViewStore`) | MISSING | |
| Offered in Mirage's asset picker; `IVisualPreview` | MISSING | |
| Section toggle bar | HAVE | 14 sections vs Pyre's 8 |
| Two-pane split with resizable dial column | HAVE | `Z.Split` |
| Undo on dials / fills / modifiers | HAVE | systematic wrapper; unverified by eye |
| Per-form inert-dial tooltips | HAVE (Solids) | 6×14 inertness table |
| Preview prefs stored on the asset | HAVE (BackSplash) / by design not for zoom | zoom is window state — arguably better |

### 4b. Canvas and document
| Pyre | Shaper | Note |
|---|---|---|
| Square canvas 16..256 | HAVE+ | non-square W×H, pixel size |
| Frame count, seed | HAVE | `frameCount`, `uint seed` |
| Authored fps | HAVE+ | Pyre only had a hidden preview fps |
| Pixels-per-unit on the asset | PARTIAL | baker hardcodes default 16; no document field |
| Background colour / fill composited into frames | MISSING | only a preview-only BackSplash backdrop |
| Layer lifetime window (start/end frame) | **MISSING** | no field on `ShaperLayer` |
| Layer stack: enable, rename, reorder | HAVE | |
| Layer duplicate | **MISSING** | grep `duplicate` in the window → nothing |
| Global (spec-wide) modifier stack | PARTIAL/BROKEN | see 4f |

### 4c. Shapes / generators
| Pyre form | Shaper | Note |
|---|---|---|
| Disc, Polygon, Star, Crescent (via subtract), Ring | HAVE | 7 SDF primitives + CSG bags; true N-gon; parameterised Star (the only primitive with animatable dials) |
| Gem, Box, Pyramid, Can, Orb (enum), Ring (3D) | HAVE | Solids ×6 on the shared light rig, 14 animatable dials |
| **Fire** (stateful grid sim, 20 dials) | **MISSING** | GUG "keep as-is"; a `ShapeForm` enum case, so the composite bridge cannot host it |
| **Fireball** (stateful doom-fire sim) | **MISSING** | same |
| **Text** (TMP SDF glyphs, per-char fills, border, extrusion) | **MISSING** | GUG "keep as-is" |
| **Sprite stamp** (a Sprite's alpha as the shape) | **MISSING** | Shaper has a Texture *fill*, not a sprite *shape* |
| Sparkle, Streak | DROPPED | owner ruling (B11/C9) |
| Playback3D | DROPPED | owner ruling; was preview-only POC anyway |
| Nine `PyreForm` generators (Arc Burst, Fork Blast, Inferno, Jet, Radial Jet, Explosive Jet, Orb, Plasma Bloom, Torch) | PARTIAL | all nine reachable via reflection-driven dials, but (a) every `ZUIValue` on the hosted form is frozen to its static value — **they do not animate over the Shaper phase**; (b) T-0112 exercised only Orb end-to-end; (c) by design they cannot be re-filled or bordered |
| Supersample / prepass cache / clip-stats / field ops / Kiln RNGs | HAVE (inherited) | travel with the hosted forms |
| Generic post-render geometry warp for forms | MISSING | `PyreFormWarp` not used by the bridge |

### 4d. Fills, borders, colour
| Pyre | Shaper | Note |
|---|---|---|
| Solid / Linear / Radial fill | HAVE+ | + Angular, By-edge-distance, Ramp-by-quantity, IndexedStrip, HeightField, TapestrySteel |
| **OverLife** (colour over time) | MISSING | T-0139 said feasible; owner call pending |
| Procedural textures Noise / Grid / Dots | MISSING | design lists "Procedural (noise/plasma/Tapestry)"; only Tapestry steel exists |
| Animated texture | MISSING | listed in design C4 |
| `PyreRamp` + LUT + ~35 shipped ramp presets, banded / dual-ramp / Palette2D / additive-emissive tone-map | PARTIAL | Ramp-by-quantity with a `ZuiGradient`; no presets, no dual ramp, no Palette2D, no emissive tone-map |
| Border on 6 flat forms | HAVE+ | border on any node, own fill, alignment |
| Gem material fills + lighting dials | HAVE | Solids colours + light response |
| Dither / posterize / recolour modifiers | BROKEN | they are in the effects catalog but are `PreComposite`, so never run (4f) |
| Matte channels (write/clip, Max/Add/Subtract), heightmap-from-channel, luma matte ×6 | **MISSING** | T-0103 R5 dropped `MatteRole` ("coverage IS a mask, reference on the consumer") but no consumer-side mask reference was built; design C2 said masking "unchanged in principle". Decision needed. |
| Coalesce Fuse (metaball) / Ramp (height relief) | PARTIAL | soft-blend inside a bag ≈ fuse; height techniques ≈ relief; no cross-layer coalesce |

### 4e. Animation and motion
| Pyre | Shaper | Note |
|---|---|---|
| `ZUIValue` envelopes (Static/MinMax/Curve/Steps/Osc), frame-aware | HAVE | via `ShaperValue`, hash-seeded |
| Alpha, size, edge-softness envelopes per layer | PARTIAL | fill `veil` ≈ alpha; no per-layer alpha/size envelope |
| **Turn / tilt / roll / spin / position-offset envelopes** (i.e. animated transform) | **MISSING** | `ShaperTransformBlock` is five plain floats/Vector2s (`ShaperMatrix.cs:111-120`); T-0126 §H2 recommended a "Wave-4 promotion" that was never filed |
| Particle own path X/Y + spin | MISSING | |
| Draggable on-canvas position handle | MISSING | |
| Swarm: count 2..200, Area/Path spawn, 7 shape kinds + custom polyline on canvas, timing Window/FrameStep, orient, chaos/order/reverse/spacing/spread, die-together, scale-by-index, spawner transform, live turn/tilt/roll/scale, preview overlays | **PARTIAL (narrow)** | Shaper swarm = count, position jitter, rotation/scale jitter, lifetime stagger, seed, merge, interact, sim cap 6 |
| Transport: play/pause, scrub, fps | HAVE | |
| Zoom 1..16 | HAVE | window state |
| Loop delay, canvas-frame toggle, filmstrip contact sheet (click to jump) | MISSING | |
| Cherry Framing: slots, length multiplier, min-max random, multi-frame + seed | HAVE | data model mirrors Pyre incl. deterministic draws (Pyre's own has a `Random` hole) |
| Cherry: thumbnail grid, drag-reorder, multi-select, right-click editor, Delete key, Zound trigger | PARTIAL | slot rows only (documented scope call) |
| Per-layer content-keyed preview cache, background multi-core fill, "fill readout" | **BROKEN (unwired)** | `ShaperFrameCache`/`ShaperFramePrebaker`/`ShaperCachedEvaluator` exist, nothing in the window uses them; ~30 ms/frame uncached |
| Parallel frame rendering | MISSING | |
| Stateful-sim replay on scrub | n/a | no sims |

### 4f. Modifiers / effects
| Pyre | Shaper | Note |
|---|---|---|
| Per-layer modifier stack (reorder, enable, remove, fold, reflection-drawn bodies) | **MISSING** | effects are document-level only; per-layer pre-composite "deliberately NOT built" (T-0156) |
| Spec-wide global modifiers | BROKEN | document list exists, but: no parameters (`ShaperEffectRef` = type name + stage + enabled; effects run at class defaults), and only the `PostComposite` stage is executed by `ShaperDocumentRenderer.cs:100` while the add-menu stamps **30 of 41** entries `PreComposite` with no stage switch → those 30 never run |
| Geometry ×13, Pixel ×12, Post ×10 addable | see above | catalog lists 41 incl. the 4 Pyre itself blocks + EdgeWarp |
| Per-layer Simulation slot (PixelFluid) | MISSING | |
| Preview overlay per effect (capability interface) | n/a | none exist yet |

### 4g. Outputs and cross-tool
| Pyre | Shaper | Note |
|---|---|---|
| Bake → PNG sheet + AnimationClip, never-overwrite, baked marker | HAVE+ | + `ShaperClip` preserving cherry |
| **GIF export** (scale, alpha dither) | **MISSING** | T-0149 open, not started |
| Runtime player + pool (no baked assets needed) | HAVE (different) | `ShaperPlayer` plays a pre-baked `ShaperClip`; Pyre renders live. Deliberate (30 ms/frame). |
| **`IChunkAnimation`** adapter so Chunks/Zoe consume it | **MISSING** | T-0151 open; no tool outside Shaper references `ShaperClip`/`ShaperPlayer` |
| `IChunkEffectSpawner`, Chunks Pyre Spawn / Motion modules | MISSING | |
| Zoe palette effect (`SpawnPyreFx`), combat FX | MISSING | |
| Mirage placement | MISSING | |
| Demo scenes using it | MISSING | |
| Kiln parity dump harness | HAVE (inherited) | |

**Rough tally over the 134 Pyre rows:** HAVE/HAVE+ ≈ 45 · PARTIAL ≈ 25 · MISSING ≈ 50 · DROPPED ≈ 6 · BROKEN ≈ 8 (rows counted once under their worst status; the exact per-row mapping is derivable from the two inventories and is not worth arguing to the unit — the shape of it is what matters).

---

## 5. Things that are broken or misleading right now (no task on the board for any of them)

1. **30 of 41 effects silently never run** (`ShaperDocumentRenderer.cs:100` vs add-menu `ShaperWindow.Sections.cs:1118`). The row even prints "Runs at PreComposite." A user adds Posterize, sees nothing, and concludes Shaper is broken.
2. **No effect has parameters** (`ShaperEffectApplier.cs:73-83`). Bloom at defaults, Outline at defaults, etc.
3. **Light rig has no window UI** (grep `lightRig|.lights` in `ShaperWindow*.cs` → nothing). The design's central idea — "shine belongs to the lights" — is un-authorable from the tool.
4. **Frame cache + pre-baker built, not wired** — the whole T-0115 wave is dead code in the window; the design promoted responsiveness to a *contract* (B9).
5. **Hosted Pyre generators do not animate** (`PyreFormCompositeSource.cs:45` resolves every `ZUIValue` to static).
6. **Node transforms are not animatable** — the single largest UX regression vs Pyre for anyone making an explosion that grows/rotates.
7. **Two "Shaper" menu items** in this worktree (real + merged-in Mock).
8. **~15k lines of CLI audit probes ship in the editor asmdef.**
9. **Layer Z-order**: `zOffset` changes shading but never re-orders layers (T-0153 flagged, awaiting your call).
10. **Bake cannot carry cherry into the AnimationClip** — only `ShaperClip` can; the UI does not yet say which output preserves cherry (T-0154).
11. **`ShaperDocument` lives inside `ShaperLightRig.cs`** rather than its own file — cosmetic, but it is the tool's root type.

---

## 6. Decisions only you can make (surfaced, not invented)

Each of these is blocking or shaping real work; I have suggested a default so nothing stalls, but they are yours.

| # | Decision | My suggested default |
|---|---|---|
| D1 | Does Shaper adopt AssetKit (`ZuiAssetWindow<ShaperDocument>`, default folder `Assets/Shaper`, browser, tags, `IVisualPreview`)? | **Yes** — it is the Laubrary standard and the direct fix for what you saw. |
| D2 | Fire, Fireball, Text, Sprite-as-shape: port into Shaper (as native generators or composite sources), or drop by ruling? | Port Fire/Fireball/Text as **composite sources** (they make finished pictures; fits the escape hatch) and Sprite as a **shape primitive** (alpha → coverage). |
| D3 | Matte/masking: build the consumer-side mask reference R5 promised, or accept CSG-in-a-bag as the only masking? | Build a per-layer "mask by layer N" reference (simple, matches R5). |
| D4 | Promote transform / sweep / shell / blend / primitive-geometry / swarm dials to `ZUIValue` (the never-filed Wave-4)? | **Yes** — without it Shaper cannot make a growing explosion; cache keys already fold `phase01`. |
| D5 | Per-layer effects (pre-composite): build, or keep document-level only and remove the 30 dead entries from the menu? | Build per-layer, since the design (C2, C8) promised it and Pyre users expect a per-layer stack. Short term: hide the 30 that cannot run. |
| D6 | Layer Z-order: does `zOffset` re-order layers or only shade? | Re-order (a depth buffer across layers), since that is what "Z offset" reads as. |
| D7 | Swarm parity: port Pyre's full swarm vocabulary, or accept the narrow Shaper swarm? | Port the spawn shapes + timing + orient at least; leave live turn/tilt to D4. |
| D8 | Colour-over-life fill (OverLife) and procedural Noise/Grid/Dots fills: in scope? | Yes, both — they are the fills people reach for first. |
| D9 | The 7 audit files: move to an excluded folder / test asmdef, or delete? | Move behind a `defineConstraints` asmdef; they found real bugs and will again. |
| D10 | Retire Pyre at parity (design D2), and when does the mock's menu item go? | Mock item: remove now. Pyre: keep until the parity matrix in §4 has no MISSING rows you care about. |

---

## 7. Proposed follow-up tasks (prioritised)

Ordered by "unblocks a human using the tool" first, then parity, then hygiene. Each is sized for one agent-session. I have **not** created these — say which to file.

**P0 — make it operable and honest**
1. **Adopt AssetKit**: rebase `ShaperWindow` on `ZuiAssetWindow<ShaperDocument>` (default folder, `TypeLabel`, thumbnails via `IVisualPreview` on `ShaperDocument`, Tags, New/Duplicate/Rename/Delete), remove the `Z.Object` + save-panel path. Reference: `PyreWindow.cs:20-58`.
2. **Fix effects**: give `ShaperEffectRef` a serialised modifier instance (reflection-drawn dials like Pyre's stack), execute pre-composite per layer *or* hide the 30 entries that cannot run; show the stage honestly.
3. **Light rig UI**: a Lighting section on the document (ambient + list of lights with kind/colour/intensity/yaw/pitch/position/range/specular).
4. **Wire the cache**: preview + transport use `ShaperFrameCache` + `ShaperFramePrebaker`; cached-frame ticks on the scrubber (design B9 requirement 2).
5. **Handover Walk of the real window by a human**, cold, twice, from empty — after 1–4. Then remove the mock's menu item.

**P1 — Pyre parity the design promised**
6. Animatable transforms (D4): `ZUIValue` on translate/rotation/scale/skew + swarm dials; on-canvas position handle.
7. Hosted Pyre forms animate over phase (evaluate `ZUIValue` with the Shaper clock instead of static).
8. GIF export (T-0149, already filed).
9. `IChunkAnimation` + `IVisualPreview` on `ShaperClip`, Zoe palette effect, Mirage placement (T-0151 + new).
10. Layer lifetime window, layer duplicate, background colour/fill composited into frames.
11. Fire / Fireball / Text / Sprite-shape per D2.
12. Swarm vocabulary per D7; masking per D3; OverLife + procedural fills per D8; ramp presets.
13. Layer Z-order per D6.

**P2 — Laubrary hygiene**
14. Demo scene `Assets/Demos/ShaperDemo/ShaperDemo.unity` with a document + `ShaperPlayer`; Samples~ copy.
15. CHANGELOG `[Unreleased]` entry for the whole Shaper body of work.
16. Move the seven audit files out of the shipping asmdef (D9); move `ShaperDocument` to its own file; unify the two Create-menu paths.
17. Close the stale open tasks T-0142/T-0146/T-0147 or re-scope them to the list above so the board reflects reality.

---

## 8. What was and was not verified

- **Verified by reading source** (all three inventories, every `file:line` re-checkable): the window base class, the picker and save-panel code, the effect stage mismatch, the absence of light-rig UI, cache non-use, Random-free generator paths, Undo wrapper coverage, every MISSING/HAVE row above, the menu items, the demo folder contents, the CHANGELOG grep.
- **Verified by reading the boards**: every decision and ruling quoted in §2 and §6; the "done with caveats" pattern in §1.4.
- **Not verified**: nothing in this report was confirmed by operating the Shaper window with a mouse, pressing Ctrl+Z, or looking at a rendered frame. Your own observation (object picker + file dialog) is the only by-eye evidence in this document, and the code agrees with it exactly. A human handover walk is item 5 above and should happen before any "Shaper is ready" claim, per the UI guide.
- **Not done**: no code was changed, no task other than T-0158 was created, nothing was committed.
