# Chunks window fitness — T-0081, the whole-window pass

Scope: `Assets\Packages\Laubrary\Editor\Chunks\ChunkWindow.cs` and `Assets\Packages\Laubrary\Editor\Chunks\ChunkWindow.Preview.cs` only. No other file was touched. The Unity editor was NOT opened, no Coplay/unity-editor-mcp/`unity.exe` call was made, and **nothing here was compiled or run** — every claim below is traced to source, not to a probe.

Read in full before starting: `ui-layout-rules.md`, `zui.md`, `CHUNKS_UI_AUDIT.md` §0 / §2 / D-13 / D-14, `PYRE_UI_PATTERNS.md` §6 / §10 / §11 / §15.

---

## 1. Requirement-to-code trace

### Defect 1 — nineteen sections in one unbroken ScrollView, no way to fold or filter

| Requirement | Code that serves it |
|---|---|
| A section toggle bar over every top-level section | `ChunkWindow.BuildAsset` → `var bar = new ZuiSectionToggleBar("Chunks", _barUnits.ToArray()); barHost.Add(bar);` (`ChunkWindow.cs`, end of `BuildAsset`). The shared widget at `Zui\Toolkit\ZuiSectionToggleBar.cs:51` is used as-is; nothing new was written. |
| Each `ZuiSection` held so the bar can address it | `ChunkWindow.Unit(body, c, label, build)` — records `body.childCount`, runs the builder, and registers the first `ZuiSection` the builder added into `_barUnits`. This replaces Pyre's "hold each section in a field" pattern **because seven of the seventeen sections are built in partial files I am not allowed to edit** (`PyreSpawn`, `Formation`, `Layers`, `Timeline`, `Slicer`, `PyreMotion`, `Splash`). Reading the section back off the container gets the identical result with the whole change confined to the shell — and it keeps working while another agent edits those files. |
| `TagsSection` included | `_barUnits.Add(("Tags", TagsSection))` before the builders run; the base exposes it at `ZuiAssetWindow.cs:142`. |
| The bar is chrome at the very top | `barHost` is added to `root` first; `TagsSection` is re-parented (`root.Add(TagsSection)`) so it drops *below* the bar — `VisualElement.Add` detaches before it reattaches. Exactly `PyreWindow.cs:400-403`. |
| Solo / bulk show-hide | Comes free with `ZuiSectionToggleBar` (right-click a segment, `ToggleSolo`, `:124`). Not reimplemented. |

Constructor verified against its declaration: `public ZuiSectionToggleBar(string prefsKey, params (string label, ZuiSection section)[] sections)` (`ZuiSectionToggleBar.cs:51`). `_barUnits` is `List<(string label, ZuiSection section)>`, so `.ToArray()` matches the `params` array element type exactly.

**Stable-workspace compliance** — `ChunkWindow.ReserveBarHeight(barHost, bar)`. The rulebook forbids chrome above the workspace changing the geometry below it, and the shared widget switches its button strip with `display` (`ZuiSectionToggleBar.Apply()`, `:113`), which collapses its own height on a mode switch. The host therefore **reserves the space**: a `GeometryChangedEvent` on the bar records the tallest height it has laid out at the current width and pins that as the host's `minHeight`; the reservation is re-applied at the top of the next `BuildAsset` (`if (_barReservedH > 0f) barHost.style.minHeight = _barReservedH;`), so it survives rebuilds. It only ever grows, it is measured on the *bar* rather than the host (so writing the host's `minHeight` cannot feed back into its own measurement), and a width change resets it — that being a deliberate window resize, not contextual UI moving under a cursor. **Residual, honest:** the very first switch into Toggle Bar mode at a given width still grows the bar once, because the widget uses `display` rather than `visibility`. Fixing that properly means editing `ZuiSectionToggleBar.cs`, which is out of scope here — logged as a risk below.

**Not done, deliberately:** `Z.ColumnFlow(360f)` and `Z.Split` (PYRE §10, audit D-13's "consider"). Both are real improvements and both are structural changes to the window shape that the brief did not ask for; adding them alongside a reorder would make an un-compilable change hard to review. Logged as follow-up.

### Defect 2 — section order optimised for the workflow nobody uses

`ChunkWindow.BuildAsset`, the two commented blocks "── the COMPOSED EFFECT first ──" and "── then the plain-debris dials ──". The nine plain-debris sections now follow the seven composed modules instead of preceding them. The old comment at the head of the composed block ("They stack UNDER the plain-debris dials…") was the explicit statement of the defect and has been replaced with a comment stating the new reading order and why.

Confirmed before moving: **every composed module builds no body until it is switched on** — `Slicer.cs:78` `if (!m.enabled) return;`, `Splash.cs:28`, `Timeline.cs:70`, and the same shape in Formation/Layers/PyreMotion. So a plain-debris author pays for this ordering with seven collapsed header rows, not seven sections of content.

### Defect 3 — D-14, ten sections with no `stateKey`

Nine of the ten are in my two files (the tenth, `"Tags"`, is in the base class and was left alone as instructed). All nine now carry a stable key:

| Section | Key | Line |
|---|---|---|
| Emission | `chunks.emission` | `ChunkWindow.cs:193` |
| Physics | `chunks.physics` | `:232` |
| Life / Look | `chunks.lifelook` | `:252` |
| Floor / Collision | `chunks.floor` | `:297` |
| Sampled Pseudo-3D Debris | `chunks.sampled` | `:321` |
| Animated Content | `chunks.animated` | `:390` |
| Hit Detection | `chunks.hits` | `:402` |
| Trail | `chunks.trail` | `:424` |
| Cut Preview | `chunks.preview` | `ChunkWindow.Preview.cs:66` |

Verified against `ZuiSection`'s own constructor (`ZuiSection.cs:66`): `_key = stateKey ?? (title ?? "section") + "" + (tooltip ?? string.Empty)` — i.e. the title+tooltip fallback D-14 describes is real. Note also that section fold state lives in a **static in-memory dictionary** (`s_open`, `ZuiSection.cs:60`), not `EditorPrefs`, so it is domain-reload-scoped; the keys still matter because that dictionary is shared by every ZUI window in the process. No collision with the existing `Z.BoxKeyed(..., "chunks.sampled.tint")` — `ZuiBox` and `ZuiSection` keep separate state.

### Defect 4 — the preview stage rendered an unreadable blur

Three code changes, all in `ChunkWindow.Preview.cs`:

1. **`DrawStage()`** — the subject stage is now a bespoke IMGUI canvas island (`_stageView = new IMGUIContainer(DrawStage)`) drawn with `Z.DrawPixels(new Rect(0,0,r.width,r.height), _stageView, _stageTex)`. It was a UI Toolkit `Image` with `scaleMode = ScaleToFit`. Signature verified: `Z.DrawPixels(Rect viewport, VisualElement container, Texture buffer, Material material = null)` → `ZuiPixelPlacement` (`Zui.cs:934`, implementation `ZuiPixel.cs:249`), and `ZuiPixelPlacement.zoom` is a `public readonly int` (`ZuiPixel.cs:52`). The rect passed is **local** because `ZuiPixel` adds the island's own panel origin itself (`ZuiPixel.PanelOrigin`, `:255`); this matches the one existing call site, `TextSplashWindow.cs:1527`. Guarded to `EventType.Repaint` only, since `GUI.DrawTexture` is a repaint-time API.
2. **`DrawDebrisCell(int i)`** — the six debris thumbnails got the same treatment for the same reason (a cut is a handful of authored pixels blown up into a 38pt cell). `UnityEngine.UIElements.Image` → `IMGUIContainer`, with a parallel `Texture2D[] _debrisTex` holding what each slot draws. The strip's fixed geometry is unchanged: same `DebrisCell` size, same six always-present slots, same reserved height.
3. **`SetStageReadout(w, h, zoom)`** — a permanently-reserved single line under the stage (`_stageReadout`, fixed `height = 14f`, `whiteSpace = NoWrap`, `overflow = Hidden`) whose **text** changes and whose geometry never does, reading e.g. `Subject 15 x 15 px  ·  13x`. This is what discharges "never present an upscaled tiny icon as if it were a preview": it states the subject's real pixel size and the whole-number zoom, so a coarse preview is legibly a coarse *source* rather than a broken renderer. It is set from what was actually drawn (the buffer's own size and the zoom `ZuiPixel` settled on) and guarded on change so a repaint restating the same numbers cannot dirty the label and ask for another repaint.

The section was also retitled **"Preview" → "Cut Preview"**, because a bare "Preview" read as a rival to the window's own "Preview in Mirage" action, and because what it shows is specifically where the *sampled cuts* land.

---

## 2. What I found about the 15×15, and where the root cause lives

**It is a real preview, not an asset icon.** The stage texture is `_stageTex`, allocated by `EnsureStageTex(W, H)` where `W`/`H` come from `src.textureRect` of the chosen subject sprite, and filled from that sprite's own `GetPixels32()` / `GetPixels(x,y,W,H)` (`RefreshChunkPreview`). Nothing in this path ever touches `AssetPreview`. So a 15×15 stage means the demo recipe's chosen subject sprite genuinely is 15×15 texels — honest data from a tiny source, not a thumbnail standing in for a preview.

**Why it read as a smear.** 196 points of stage ÷ 15 source pixels = **13.07 points per authored pixel** — a fractional blow-up. Identical authored pixels therefore rasterized 13 or 14 device pixels wide, and the cut outlines, which are exactly **one source pixel thick** (`OutlineCut` writes single-pixel borders), landed as ragged bands bleeding into the art they exist to sit on top of. The texture's own `filterMode` was already `FilterMode.Point` (`EnsureStageTex`), so point filtering was *not* the missing half — the missing half was whole-device-pixel placement, which is precisely the trap `ZuiPixel` exists to remove and which the rulebook says to read before drawing any image whose pixels are meant to read as pixels. `Z.DrawPixels` also restates `FilterMode.Point` and `TextureWrapMode.Clamp` on every draw (`ZuiPixel.cs:227-228`), so the sampling can no longer be quietly reset by anything upstream.

**What it will look like now, stated honestly.** A 15×15 subject in a 196pt box will floor to a 13× zoom: 195 device points of crisp, identical 13px blocks, corner-snapped, centred, with 1px outlines rendering as clean 13px-wide lines. It is legible. It is still *coarse*, because 225 texels is all the source has — which is exactly what the new readout says out loud instead of letting the user assume the renderer is at fault.

**A related fault whose root cause is NOT in my files — reported, not fixed.** `Assets\Packages\Laubrary\Editor\AssetKit\LauAssetGridGUI.cs`, `GetThumbnail`: line **39** is `return AssetPreview.GetAssetPreview(item);`, the fallback taken whenever an item is not an `IVisualPreview` or its preview came back blank (the `IsBlank` drop at `:24-35`). `AssetPreview.GetAssetPreview` is asynchronous — it returns `null` or an unfinished texture on the first call and a real one only on a later repaint/rebuild. That result is then cached by the *caller* (`_visualThumbs` in `ChunkWindow`), so a null/blank first answer can stick. This feeds the **`LauAssetElement` chips** — Animated Content, Trail, and the blast/pool chips — and has nothing to do with the Cut Preview stage. That file is read-only for me; flagging with file:line as instructed.

**A second one, same class, also out of scope:** `ChunkWindow.Slicer.cs:271` (`_slicerImage = img;`, a `UnityEngine.UIElements.Image` fed at `:360`) has the *identical* fractional-blow-up problem as the stage I just fixed, on the Fragment Slicer's own preview. Same one-line-per-draw fix (`Z.DrawPixels` in an `IMGUIContainer`). Not touched — it is outside my two files.

---

## 3. Before / after section list

**BEFORE — 17 `ZuiSection`s + 1 action row = 18 top-level units.** (The audit's "19" counted `+ Add another blast…` as a separate top-level unit; a concurrent pass has since moved it inside the Blasts section, so it is no longer top-level. Nothing was removed by me.)

| # | Before | After | # |
|---|---|---|---|
| — | *(no toggle bar)* | **Section toggle bar** *(new chrome)* | — |
| 1 | Tags | Tags | 1 |
| 2 | Emission | Fragment Slicer | 2 |
| 3 | Physics | Blasts | 3 |
| 4 | Life / Look | Pyre Movement | 4 |
| 5 | Floor / Collision | Spawn Formation | 5 |
| 6 | Sampled Pseudo-3D Debris | Layer Stack | 6 |
| 7 | Preview | Particle Splash | 7 |
| 8 | Animated Content | Timeline | 8 |
| 9 | Hit Detection | *Preview in Mirage (action row)* | 9 |
| 10 | Trail | Emission | 10 |
| 11 | Particle Splash | Physics | 11 |
| 12 | Fragment Slicer | Life / Look | 12 |
| 13 | Blasts | Floor / Collision | 13 |
| 14 | Pyre Movement | Sampled Pseudo-3D Debris | 14 |
| 15 | Spawn Formation | **Cut Preview** *(was "Preview")* | 15 |
| 16 | Layer Stack | Animated Content | 16 |
| 17 | Timeline | Hit Detection | 17 |
| 18 | *Preview in Mirage (action row)* | Trail | 18 |

**AFTER — 17 `ZuiSection`s + 1 action row = 18 top-level units, plus the new toggle bar.** Diffed by hand, both directions: every one of the 17 sections present before is present after; none added, none removed, none disabled. The only textual change is `Preview` → `Cut Preview`. Counts: **17 → 17.**

---

## 4. Adjacencies found in the old order, and how each was preserved

Read every comment in `BuildAsset` before moving anything. Four genuine adjacencies were encoded there; all four survive.

1. **Preview must sit directly under Sampled Pseudo-3D Debris.** The old comment said `// the preview-subject stage … — right under the slicing it visualises`. Verified in code rather than trusting the comment: `RefreshChunkPreview` calls `SampledChunkSprites.Sample(src, c.samplePxMin, c.samplePxMax, c.pixelsPerUnit, out cut, c.tintMode, c.tintColor, c.tintStrength, c.edgeThicknessPx, c.modifiers)` — every one of those dials belongs to `BuildSampled`, and **none** belongs to Fragment Slicer. So despite being called "the slicing preview" in the brief, this stage visualises the **sampled cuts**, and it stays glued to them (positions 14 → 15). This is also why it was retitled "Cut Preview" rather than moved up with the composed modules.
2. **Timeline docks at the BOTTOM of the composed modules.** The old comment: `they stack … above the timeline, which is the backbone the design doc docks at the bottom`. Timeline is still the last *section* of the composed block (position 8 of the block).
3. **Preview in Mirage sits directly after Timeline.** `MiragePreview.cs:60` reserves 8px of space with the comment `this is an action, not another dial in the Timeline block` — i.e. its placement is defined relative to Timeline. It is still immediately after Timeline, which now also puts "see it" right at the end of the composed-effect block, which is the fix for walk step 7 ("nothing points at it from the blast sections").
4. **Blasts → Pyre Movement → Spawn Formation → Layer Stack is one contiguous run.** Pyre Movement governs how the blasts above it travel; Spawn Formation *supersedes* the first blast (`ChunkModules.cs:49-52`, warned about at `PyreSpawn.cs:125-127`); Layer Stack is where the blasts' Layer slot gets its slots from. Their relative order is byte-for-byte unchanged — the whole run just moved up as a block.

Only two sections changed position *within* their block: **Fragment Slicer** moved to the front of the composed block (it is walk step 1, "make the character shatter"), and **Particle Splash** moved from the front of that block to just before Timeline (it is a standalone add-on with no stated relationship to anything either side of it — the audit and the code both treat it as independent: `"a standalone splash of colour with no fragment, pyre or timeline involved"`, `Splash.cs:19-20`).

---

## 5. Risks the PM should check by eye

Ordered by how likely they are to actually bite.

1. **Nothing here was compiled.** Two files changed, ~180 lines touched. The riskiest single call is `new ZuiSectionToggleBar("Chunks", _barUnits.ToArray())` — verified against the declaration, but a `params` + named-tuple-element mismatch is exactly the kind of thing that only a compiler settles.
2. **`Unit()` assumes each builder adds its section as a DIRECT child of `body`.** True for all nine builders today (`root.Add(s)` / `root.Add(sec)` in each). If the concurrent agent working on `ChunkWindow.PyreSpawn.cs` / `ChunkWindow.Formation.cs` wraps a section in a container, that section silently drops out of the toggle bar — no error, just a missing button. **Check the bar has 17 buttons.**
3. **The bar has 17 buttons and it wraps** (`ZuiSectionToggleBar` sets `flexWrap = Wrap.Wrap` on itself, `:57`). In a narrow window that is two or three rows of chrome above the content. `ReserveBarHeight` stops a *mode switch* from changing that height, but the first-ever switch into Toggle Bar mode at a given width still grows it once. The real fix is `visibility` instead of `display` inside `ZuiSectionToggleBar.Apply()` — a shared-widget change, out of scope here. Judge whether 17 buttons is too many; if so, the honest answer is fewer sections, not shorter labels.
4. **Bar labels are abbreviated and no longer match every section title** (`Slicer`/Fragment Slicer, `Movement`/Pyre Movement, `Formation`/Spawn Formation, `Layers`/Layer Stack, `Splash`/Particle Splash, `Life`/Life / Look, `Floor`/Floor / Collision, `Sampled`/Sampled Pseudo-3D Debris, `Hits`/Hit Detection). Pyre does the same (`Global Mod`), but it is a real readability trade — eyeball whether any of them fails to point at its section.
5. **Adding `stateKey`s orphans the existing session fold state once**, for all nine sections, since the key changes from `title+tooltip` to `chunks.<name>`. That is the unavoidable one-time cost of the D-14 fix; it will look like "my folds reset" on the first run after this change and never again.
6. **The `IMGUIContainer` conversion is the one change with real visual risk.** If `contentRect` is degenerate on the first repaint the stage paints nothing that frame (guarded, so no exception) — check the stage is not permanently blank. Also confirm the hint Label still swaps in correctly for the four failure messages (`No Chunk selected`, `Pick a Subject sprite…`, `Enable Read/Write…`, `This sprite has no pixels…`), since the show/hide now targets `_stageView` rather than an `Image`.
7. **Texture lifetime around the debris islands.** `DisposeDebris` now nulls `_debrisTex` **before** destroying the sprites and their textures, precisely because a painting island reads that array on every repaint and would otherwise get one frame pointing at a destroyed texture. Worth a look under rapid Resample clicking and on asset switch (`OnAssetChanged` → `DisposeChunkPreview`).
8. **The `_stageReadout` text is set from inside a repaint.** It is change-guarded, so it should settle after one frame; if the zoom ever oscillates between two values the label would repaint continuously. Watch for a busy-looking window.
9. **`Z.ColumnFlow(360f)` and `Z.Split` were NOT adopted.** With modules switched on, the window is still a tall single column — the reorder and the toggle bar reduce the hunting, they do not reduce the 3551pt. That is the next structural pass, and it is where the audit's D-15 (the preview/formation/slicer stages moving into a right pane) also gets fixed.
10. **No section icons were added.** PYRE §11 says section icons are what make Pyre's headings read apart, and `Z.Section` takes an `icon` argument. I did not add any because `Z.Icon` returns null on an unresolved name and I cannot verify a name resolves without the editor — a silently-null icon is a wasted change, and guessing seventeen of them would be icon-spam. Worth a follow-up pass with the editor open.
