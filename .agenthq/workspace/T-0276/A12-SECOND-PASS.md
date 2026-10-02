# A12 — second full pass (T-0276), 2026-09-08, worktree HEAD deb9db03

Every number below is a pixel count from a render, taken on the document the row names, at the frames the row names. Nothing here is asserted from a doc or a previous handover.

## 1. Every earlier probe, re-run unchanged

| probe | result | verdict |
|---|---|---|
| T-0267 `t0267_affordances.cs` | reproduces its own pre-fix numbers (Height on 0, Sweep on 0, Add member 18432/18432 opaque) | **not a regression** — the archived script builds `new ShaperNode`/`new ShaperHeightDef` by hand and so bypasses the three call sites T-0267 fixed. Its three companion probes drive the fixed paths and all reproduce: Height on + `normalKind=Profile` **3911**, Sweep on seeded **1042**, `+ Add member` quarter-canvas **1536/6144**. Source confirms all three fixes still present (ShaperWindow.cs:769, ShaperWindow.Sections.cs:1310-1315, ShaperWindow.cs:534). |
| T-0271 `roundtrip-probe.cs` | **ROWS 33, DIFFERING 0** | no regression. The pre-fix column still shows the bug it closed: bag with an authored bag fill would be 980 px wrong, authored child fill 440, nothing authored 980. |
| T-0277 `fill-state-probe.cs` | 27 of 35 rows reproduce exactly; 8 heightField rows read 0 where the handover says 671 | **probe defect, not a regression** — the archived script loads `rfield0277.asset`, which it never creates. Re-measured with a real RFloat field: Solid (Pyramid), one layer **310 px**; primitive ellipse, one layer **0**. Same verdict, so the shipped greying stands. Filed as **T-0283**. |
| T-0278 `t0278_saved_probe.cs` | reproduces exactly — steps 4→32 **0**, bevel steps 3→16 **0**, technique Stepped→Dome **9163** (Rect) / **2753** (Star), all three frames | no regression; the engine gap and its absenting are unchanged. |
| T-0280 `probe-T0280Sweep.cs` | not re-run | see "not verified". |
| compile | `recompile_status` → `completed, failed=false, errors=[]`, before and after every edit | T-0281 and T-0274 shipped code-only/uncompiled; both compile clean at HEAD. |

## 2. The cold walk

Opened from the menu with every instance closed first, on a **new** document made through the window's own two-step New → name → **Create**, then on the **demo** document (loaded, never saved). Every button pressed through a real `NavigationSubmitEvent` — measured first, on a probe button added to the tree, that `NavigationSubmitEvent` fires a ZUI button's callback and `PointerDown/Up` and `ClickEvent` do not.

- **Empty state**: no document bound, `Save` greyed, `New` and `Browse` live, the library browser showing every document as a rendered thumbnail. Captured.
- **New document**: one layer, 16 frames, 96×64, one key light, **504 lit pixels at frame 0** — it draws something immediately.
- **Play, new document**: `▶ Play` → `❚❚ Pause`, `playing=True`. Stage samples ~1.5 s apart: frame 6 lit 1872 hash `512ccc8b…`, frame 8 lit 2124 hash `ef58db14…`, frame 4 lit 1320 hash `2e2dcdf7…` — it moves, and it wrapped (8 → past 15 → 4).
- **Play, demo document**: all 16 frames render non-blank (144…4454…144). Samples at 15:35:27 frame 10 lit 3739, 15:35:38 frame 5 lit 4219, 15:35:47 frame 14 lit 1210, three distinct hashes; the window's own status line read "Playing — frame 12/16" in the capture. `docDirty=False` at every sample and after Pause.
- **Editing while playing**: the first dial on a fresh layer, `Half width`, is a **Curve** (T-0190 seeds growth), and the card handles it correctly — its `ZuiMicroSlider` has `pickingMode = Ignore` so it cannot be dragged, and its tooltip says "Animated: the track shows…". A static sibling (`Corner`) has `pickingMode = Position`. No dead-looking dial there.

## 3. What this pass found and fixed

| # | finding | measurement | fix |
|---|---|---|---|
| 1 | **The Combine card always shows two dead dials, and which two depends on the choice beside them.** | Saved two-member bag, per mode — Add: Blend width **364**, Sharpness **764**, Carve strength **0**. Cut out: **0**, **0**, **1048**. Keep overlap: **260**, **512**, **0**. | Both greyed with the condition, keyed on the member's own mode. Card readback after: Add → Carve strength disabled; Cut out → Blend width + Sharpness disabled; Keep overlap → Carve strength disabled. |
| 2 | **Posterise is drawn on the default fill kind, where it can never act.** | 34×13 ellipse, Posterise 0→3: Solid **0**, Height field **0**, One colour over time **0**, against Gradient 1456, Colour bands 1456, Brushed metal 1456, Pattern 1456, Texture 96, Ramp 136. | `PosteriseReason(f)` greys the three, each with the reason ("a single colour has no range to band"). Readback confirms Solid/HeightField/OverPhase disabled, Gradient/Procedural live. |
| 3 | **The Text card's Line spacing and Align do nothing on the default string.** | Default `textString` is `"TEXT"`, one line: Line spacing over its whole range **0**, all three Align choices **0**. With `"AB\nCD"`: Line spacing **1129**, Align Left **301**, Right **312**. | Both greyed while the string has no newline, with "Press Return in the Text field above to start a second line". Seen greyed in a capture. |
| 4 | **"Loop gap" was the caption of two different fields, both in the right pane at once** — the transport's `loopDelaySeconds` and Cherry's `cherryLoopDelaySeconds`. T-0257 had split them; a later caption-length pass merged them back and left T-0257's own comment contradicting the label under it. | Built-tree caption scan: `REPEATED "Loop gap" x2`. | Cherry's is **"Sequence gap"** (12 chars). |
| 5 | **"Frame" was the caption of two unrelated controls a few rows apart** — a preview toggle that draws the canvas outline, and the transport's frame-number scrubber. | Same scan: `REPEATED "Frame" x2 → [ZuiToggleButton @616,457] [Label @620,532]`. | The toggle is **"Canvas edge"**. Seen in a capture. |
| 6 | **"Lifetime" printed twice side by side in one row** — a `Z.Field` label plus the `MicroMinMax`'s own caption. | Same scan: `REPEATED "Lifetime" x2 @4,184 and @63,183`. | The `Z.Field` wrapper is gone and its fuller sentence moved onto the control — the same fix the file already applies to the Frame scrubber (:1463). |
| 7 | **"Duplicate" twice on one screen with two different nouns, and one of them was a second control for an action that already had one.** The Layers card's "Duplicate" duplicates the SELECTED layer; every layer row's "Dup" duplicates that row's layer; the asset toolbar's "Duplicate" copies the whole document. | Caption scan: `REPEATED "Duplicate" x2 → [Button @408,31] [Button @96,155]`. | The Layers-card button is gone. Every row keeps its own "Dup", which names the layer it acts on. |
| 8 | **"Delete" twice on one screen, one of them irreversible file deletion.** `ZuiViewBar`'s button removes a saved VIEW; every `ZuiAssetWindow` toolbar's deletes the ASSET FILE and says so ("cannot be undone"). | Caption scan with every section open: `REPEATED "Delete" x2`. | The view bar's is **"Delete view"**, with a tooltip saying the asset is untouched. Shared control, so every Laubrary tool gains it. |
| 9 | **"Start ƒ" and "Extent ƒ"** — a florin standing for "fraction", explained nowhere on the control; the file's own comment calls them "the two ƒ dials". | Read on screen in a capture. | **"Start along"** and **"Extent along"**, the word the tooltips already used. Seen renamed and correctly greyed on a Text node in a capture. |
| 10 | **Creating a document wrote every dirty asset in the project.** This is the mechanism behind T-0265's "ShaperDemoDoc.asset was modified during this task". | Walked the real New → Create path: `AssetLibrary<T>.Create` calls `SaveAssets()`, then the window calls `SaveAssets()` again. | The four calls on that path (`ZuiAssetWindow` Confirm, `AssetLibrary.Create/Duplicate/Rename`) narrowed to `SaveAssetIfDirty(<that asset>)`. The rest filed as **T-0282**. |

## 4. What this pass looked at and found CLEAN

- **A bag's own Fill card on a SAVED document works again.** T-0277's largest open finding ("on a saved bag the bag's ENTIRE Fill card is dead", 1754 px in memory → 0 saved) is closed by T-0271's `authored` flag, which landed after it. Re-measured on a saved two-member bag with a Cut-out member: the bag fill's `solidColor` moves **1152 px**, `veil` 1152, `composite` 1152, `kind` 1152.
- **Ring solid**: `aspect`, `depth` and `roll` measure 0 and all three are already declared inert for a Ring by the engine's own `ShaperSolids.InertReason` table, with its own sentences. Everything else on the card is live (size 11196, inner 2208, yaw 2592, tilt 2940, line width 1032, edge glow 2172, inner glow 1836, and both glow colours).
- **Masked layer**: source, invert, mode and quantity all live (2688 / 7284 / 7284 / 4596). `Fully masked at` measures 0 under Coverage and is **correctly not drawn** for Coverage at all.
- **Texture fill** and **Pattern fill**: every dead dial traces to a condition T-0277 already greys or tooltips (tiles under Fit-once, the frame group at a 1×1 grid, the removed tints).
- **Colour bands**: the strip's own slot colour reaches the picture (**728 px**), and `Fit`, which read 0 at the default one repeat, moves **398 px** at four repeats — a fixture artefact, not a dead dial.
- **Sprite primitive**: `spriteFitMode` and `spriteSoftnessDial` first read 0 because the engine's own 50-unit half-extents overflow a 96×64 canvas. On a 34×13 box with a soft-alpha sprite, fit mode moves **248 px** and softness **36 px**. Both live.
- **Gradient/Linear Fit** reads 0 only at angle 0; 1448 at 30°, 1456 at 45° and 90°.
- **The four placement pads** (Translate/Origin/Scale/Skew) draw as a 120×18 collapsed thumbnail, not the 140 px plot the code comment names — but that is `ZuiValue2DControl`'s designed collapsed state, its tooltip says "Click to expand the 2D editor", and Pyre draws the same control the same way. Recorded, not filed.

## 5. Files changed

`Editor/Shaper/ShaperWindow.cs`, `ShaperWindow.Sections.cs`, `ShaperWindow.Preview.cs`, `ShaperWindow.Cherry.cs`, `Editor/AssetKit/AssetLibrary.cs`, `Editor/AssetKit/ZuiAssetWindow.cs`, `Zui/Toolkit/ZuiViewBar.cs`. **No Pyre file touched.** No commit (programme rule 3).

## 6. A gotcha worth carrying forward

A probe that mutates a loaded asset in memory leaves the wrong instance in the AssetDatabase cache, and `AssetDatabase.ImportAsset(..., ForceUpdate)` does **not** clear it — the object comes back with the probe's edits still on it even though `IsDirty` is false and the file on disk is untouched. Only `Resources.UnloadAsset(obj)` followed by a fresh `LoadAssetAtPath` restores it. This happened here on the demo document (its Star became a Text primitive during a card readback); it was restored and `git status` reports the file unmodified.
