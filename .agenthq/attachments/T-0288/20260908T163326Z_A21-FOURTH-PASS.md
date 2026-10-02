# A21 — fourth full pass (T-0288), 2026-09-08, worktree HEAD 0ca3c2ed + this task's edits

Every number below was measured in the running Shaper-worktree editor (`Application.dataPath` confirmed `D:/UNITY/Laubrary Dev - Shaper/Assets` at the start and at the end). Nothing is asserted from a doc or a previous handover.

## 1. Every archived probe, re-run unchanged

| probe | result | verdict |
|---|---|---|
| T-0271 `roundtrip-probe.cs` | **ROWS 33, DIFFERING 0** | reproduces; re-run again after this task's edits, still 33/0. |
| T-0277 via T-0283's `fill-state-probe.cs` | 36 of 36 rows identical to T-0284's, incl. the heightField four (246 / 0 / **310** / 0) | reproduces. |
| T-0278 `t0278_saved_probe.cs` | steps 4→32 **0**, bevel 3→16 **0**, Stepped→Dome **9163** (Rect) / **2753** (Star), all three frames on both documents | reproduces exactly. |
| T-0267 `t0267_height.cs` / `t0267_sweep.cs` / `t0267_bagmember.cs` | **3911** / **1042** / **1536 of 6144** | all three reproduce, before and after this task's edits. |
| T-0267 `t0267_affordances.cs` | not re-run | T-0276 established it hand-builds the objects and bypasses the call sites it claims to test; the three companion probes above ARE the test. |
| T-0284 `solid-position-probe.cs` | Scale Y Box **1438** / Pyramid **1233** / Can **1528** / Orb, Gem, Ring **0**; Skew X same shape; Ring rotation **0** | reproduces exactly. |
| T-0284 `mask-three-layer-probe.cs` | unmasked 2992 → masked **1304**, invert **1800**, frames 1304/1375/1814/1998/2065/2031/1945/1304, saved+reloaded **1304** | reproduces exactly. |
| T-0284 `t0285-duplicate-graph-probe.cs` | frames 0/4/7 **0 changed px** against 2768 / 2830 / 2768; graph src 4129, dup 4130, **SHARED 0**; 0 null-where-source-had-a-value | reproduces exactly. |
| T-0284 `t0285-duplicate-sentinel-probe.cs` | file byte-identical, mtime unchanged, window switched to the copy — but this run read `dirty=False, inMemoryWidth=32` where T-0284 read `dirty=True, 199` | **probe-sequence artefact, not a regression.** The archived script calls `AssetDatabase.Refresh()` between creating the sentinel and editing it, which can reload the object under the reference it holds. A minimal repro without that Refresh (`a21-dup1.cs`) reads **width 199, dirty True** after pressing the real Duplicate button, i.e. T-0285's claim holds. |
| T-0284 `views-reapply-probe.cs` | picked the other view 0/10 → 10/10; rearranged → 0/10; **re-picked the same view → still 0/10**; away and back → 8/10 | reproduces. This is UI Toolkit's ChangeEvent semantics and is unchanged by T-0286 — T-0286 added an *Apply button*, which this archived script never presses. See §3. |
| T-0284 `overflow-probe.cs` | hosted **Torch**: 1 hit of 1663; hosted **Jet**: 1 hit of 1370 — in both cases the ScrollView's own scroll content, the known exclusion | T-0287's fix holds. See §3. |
| T-0284 `placement-pads-probe.cs` | Translate / Origin / Scale / Skew all go 4 → 19 elements on a real left `PointerDownEvent` | reproduces. |
| T-0276 `a12verify.cs` (text / posterise / combine) | one-line Text greys Line spacing + Align, two lines frees them; Posterise greyed on Solid / HeightField / OverPhase and live on Gradient / Procedural; Combine greys Carve strength under Add and Keep overlap, Blend width + Sharpness under Cut out | all three of T-0276's fixes reproduce with their own sentences intact. |
| T-0280 `probe-T0280Sweep.cs` | not re-run | template needing `__GEN__`/`__STAGE__`/`__PATHS__` substitution; T-0284 re-derived it instead and reported the same population. This pass covered the picker's whole catalogue by a different route (§4). |
| compile | `recompile_status` → completed, failed=false, errors=[]; `EditorUtility.scriptCompilationFailed` False; `get_console_logs severity=error` **0 entries** | after every edit. |

## 2. The cold walk, twice

Opened from `Laubrary/Shaper` with every instance closed first, both times.

- **Empty state.** No document bound; 5 live controls, `Save` greyed with "No Shaper is open, so there is nothing to save."; `New`, `Browse`, `▶ Animate all`, `Refresh` live; every one carries a tooltip. 169 elements the first time, 197 the second (the browser had assets to list by then).
- **New document**, through the window's own two-step New → name → **Create**: one layer, 16 frames, 96×64, one light, **504 lit pixels at frame 0** — identical on both walks and identical to T-0276's and T-0284's number.
- **Play, temporal, walk 1** (12 fps, editor focused): 8 samples 1.2 s apart — frames 9, 4, 15, 11, 10, 5, 0, 12, eight distinct hashes, the loop wrapped twice, minimum lit 504, the stage never blanked.
- **Play, temporal, walk 2** (after the fixes): 8 samples ~2.4 s apart — frames 2, 12, 10, 4, 14, 9, 3, 1, eight distinct hashes, wrapped, minimum lit 704.
- **A note that is NOT a defect but reads like one from a probe.** Samples taken while the editor is in the BACKGROUND repeat: three consecutive reads 3 s apart all showed frame 15. `EditorApplication.update` is throttled by Unity when the editor is unfocused, so playback genuinely slows to about 1.5 fps and then resumes at 12 fps the moment it has focus. Every temporal claim above was taken with the editor focused.
- **Re-entry.** Closed every instance and reopened from the menu: the window comes back on the **empty state**, not on the document it was left on — the shipped behaviour, unchanged from T-0276 and T-0284. A **second New in the same session** then produced `AuditA21c` with the same 504 lit pixels and one layer, so nothing is captured stale across two invocations.
- **Two domain reloads** (the two recompiles this task made) were survived: the window rebuilt, the toggle bar restored its section visibility, and the archived probes reproduce identically afterwards.

## 3. T-0286 and T-0287, verified

**T-0286 — the Views bar's Apply button.**
- By eye (`a21-lightscard-174417.png`, `a21-fixed3-182353.png`): the bar now reads `Views [dropdown] [Apply] [Update]` / `[Delete view] [name] [Save as]`.
- By probe: with one saved view showing in the dropdown, every `ZuiBox` closed by hand (**0/10 open**), pressing **Apply** restored **8/10** — the state the view holds — with no dropdown value change at any point. Its tooltip states the gap it closes.
- Also checked: **Save as** refreshes the dropdown in the same gesture (`choices` went `[A19View2]` → `[A19View2, A21View]`, value `A21View`). An earlier reading of an empty `choices` list with a stale `value` came from the archived probe's own Save-as sequence, not from the shipped path.

**T-0287 — a wrapped variant radio no longer paints over the next box.**
- By eye on a **hosted Torch card** (`a21-torchhosted-180727.png`): "Flame type" draws Emberbed / Surge / Barbs / Curl on one line and **Lash on its own second line**, with the "Placement" box header undisturbed below it.
- By probe: T-0284's own `overflow-probe.cs`, unchanged — hosted Torch **1 hit of 1663**, hosted Jet **1 hit of 1370**, in both cases the ScrollView's scroll content only.
- **But the fix as it landed had a defect of its own, and this pass found and fixed it — see §5 finding 1.**

## 4. What this pass covered that no pass had covered

### The Lights card, end to end
- **Every dial, per light kind**, measured as changed pixels on a one-layer 96×64 document (`a21-lights1.cs`):

  | dial | Directional | Point |
  |---|---|---|
  | enabled off / colour / intensity / ambient colour / ambient × / kind flip / +2nd light | 504 each | 504 each |
  | specular 0.9 → 0 | **0** | 504 |
  | yaw, pitch | 504 | **0** (declared inert, greyed) |
  | posX, posY, posZ, range | **0** (declared inert, greyed) | 504 |

  The greyed set matches the engine's own inert set exactly, in both directions. The Directional **specular 0** is not a dead dial and not a fixture artefact: a Blinn-Phong highlight needs the normal to turn, and the default layer's `normalKind` is `Constant`, so a Directional light's half-vector is identical at every pixel. Give the same layer a Dome height stage with `normalKind = Profile` and the same sweep moves **164 px**. Fixed as a tooltip, §5 finding 5.
- **The card by eye** (`a21-lightscard-174417.png`): three cards, each with fold caret, name, `≡` grip, enable ✔, a Directional|Point segmented, `×` and `?`; Directional cards show Position / Position Z / Range visibly greyed, the Point card shows Direction greyed. 0 controls without a tooltip.
- **Add / remove / reorder, through the real controls.** `+ Add light` pressed 9 times, each `×` pressed to remove, the enable ✔ clicked four times (True→False→True→False, and the section header tooltip switches to "No lights — Silhouette layers render unlit…" while nothing is enabled), the kind segmented clicked to Point (greying flips correctly), and a light dragged with a real PointerDown/Move/Up on its `≡` grip: `Key Light2 Light3` → `Light2 Light3 Key`, cards following.
- **The cap was not enforced** — §5 finding 2.

### The Canvas card, while playing
Every dial driven by a real press on the slider track, with the transport running:
- **Width** 96 → 236 mid-play: the preview resized and playback carried on (next sample frame 6, lit 1872).
- **Frames** 16 → 73 mid-play: the transport refilled, the readout read "frame 16/73", and playback reached frame 60 four seconds later.
- **Background** set to red through the real ColorField mid-play: lit jumped to **15104** = 236×64, the whole canvas, and playback carried on.
- Canvas scale, Depth between layers and Document seed all take a real drag. No blank, no exception, no stall.

### The Bake box, every toggle
- State read back: `Sprite sheet PNG` is on and **disabled** (it is infrastructure the other outputs slice from) and a click on it changes nothing; AnimationClip and ShaperClip default on; GIF off with **GIF scale and GIF dither both disabled**, and both enable/disable live with the GIF toggle.
- The state-dependent Bake tooltip fires exactly on its one combination: with cherry on, AnimationClip ticked and ShaperClip unticked it gains "This document's cherry sequence will NOT be preserved by this bake…", and loses it again when ShaperClip is re-ticked.
- **A real bake, pressed through the button**, on this task's own document with all four outputs on, produced `AuditA21.png`, `AuditA21.anim`, `AuditA21 Clip.asset` and `AuditA21.gif` in `Assets/Shaper` — the folder the "Destination" readout names.
- **The never-overwrite claim holds.** A second press produced `AuditA21_1.png`, `AuditA21_1.anim`, `AuditA21 Clip_1.asset` and `AuditA21_1.gif`; the first GIF's mtime was unchanged. All eight outputs deleted afterwards.

### Cherry framing, slot editing through the real panel
- Turned on through its own header checkbox. `+ Add slot` appends the frame the preview is showing (pressed at frames 0, 5, 11 → slots 0, 5, 11).
- A source frame is selected by a real click (`sourceSelected` {6} → {2} → {8}) and `+ Add selected` appends it.
- **Right-clicking a slot opens its editor popover** — the only way to edit a slot. It carries Source frame, Variable length, Length ×, Min, Max, MultiFrame, Duplicate, Delete. **Duplicate** took 3 slots → 4 and **Delete** took 4 → 3, leaving the sequence 0/5/11 intact. The scrim dismisses on an outside click.
- Two findings, both filed: **T-0289** (two of Length ×/Min/Max are always dead, none greyed) and **T-0290** (the Min/Max row wraps and MultiFrame is drawn on top of Max — visible in `a21-cherry-175955.png`).
- **Not verified:** shift-range and ctrl-toggle selection, and double-click-to-add. `PointerEventBase.shiftKey`/`ctrlKey` are not writable, so those gestures cannot be synthesized; they need a real keyboard and mouse.

### The shape picker, every column at defaults
All **43 entries in 9 columns** applied to a fresh one-layer 96×64 document through the entry's own `Apply`, rendered at frames 0/4/8/12:

| column | entries | result |
|---|---|---|
| Primitives | 9 | 8 draw (Rectangle 504→2124, Ellipse 5896, Diamond 4404, Triangle 3264, Pill 3832, Polygon 5468, Star 2750, Text 909); **Image draws nothing** |
| Solids | 6 | all draw (Box 2000, Pyramid 900, Can 1904, Orb 1264, Gem 564, Ring 840) |
| Bag | 1 | Combine children 2304 |
| Explosions | 2 | Fork Blast 5425, Inferno 4431 |
| Kiln/Energy Explosion | 2 | Arc Burst 1716, Plasma Bloom 3566 |
| Kiln/Energy Projectile | 1 | Orb 925 |
| Kiln/Flame | 4 | Explosive Jet 3168, Jet 1094, Radial Jet 2539, Torch 1304 |
| Simulations | 2 | Fire 70, Fireball 255 |
| Pyre | 16 | all draw (Disc 1340 … Fireball 654) |

- **"Image" is the one entry that draws nothing at its defaults**, and its tooltip did not say so — fixed, §5 finding 6.
- **Eleven labels appear in more than one column** (Orb three times; Polygon, Star, Text, Box, Pyramid, Can, Gem, Ring, Fire, Fireball twice). Checked and found CLEAN: each column's tooltip names the engine it comes from — "a shaded pseudo-3D box", "Pyre's box — the real Pyre layer, with its own life envelopes", "bake this generator's own picture into the node". That is the T-0235 convention (a repeated name inside its own titled group), not drift.
- Recorded, not filed: **Pyre / Sprite** with no sprite assigned renders the same pixel counts as **Pyre / Disc** (812 / 1340 / 1020 at frames 4/8/12), i.e. Pyre's sprite form falls back to a disc. It draws, so it is not the Image case, but it is not what its own name promises either.

### The transport
- **Scrub while playing** does exactly what its tooltip says: a real press at 50 % of the Frame track set `playing=False`, `currentFrame=8`, the readout to "frame 9/16" and the button back to `▶ Play`.
- **Loop gap.** Set to 1.5 s and sampled 14 times over 24 s: `previewFrame` reads **-1** and the readout reads **"gap"** on every gap sample, `plainLoopBlankUntil` advances one gap per loop, and — read from the stage element rather than from its texture — `_image.style.backgroundImage` is **null** during the gap and set outside it. The gap really is blank, not a held frame. (A probe that reads the stage's `_tex` instead sees the last frame's pixels and will wrongly report a held frame; the texture is not cleared, the background-image reference is.)
- **Strip toggle**: `previewStrip` True → False → True, the filmstrip element disposed and rebuilt with it, and the toggle's tooltip changes to match which state it is in.
- **Tile size** could be clicked but **not dragged** — §5 finding 3.

## 5. What this pass found and fixed

| # | finding | measurement | fix |
|---|---|---|---|
| 1 | **T-0287's own fix self-locks, and a control that grows after its first layout is left overhanging its row.** `Z.Field` stamped an explicit `style.height` from a `GeometryChangedEvent` **on the wrapper**. Once the wrapper's height is pinned it stops changing when its content changes, so the callback fires once and never again. | Pyre's own Torch card, live: the `.zui-field` for **"Colour ramp"** carried `style.height = 187.11px` around a `ZuiGradientControl` measuring **227.56px** — 20.4 px over the row above and 20.0 px over the row below, a real overlap in the overflow scan. Clearing the stamp by hand made it resolve to 227.56 on the next pass, then re-stamp correctly. | The stamp is now driven by the **control's** geometry, not the wrapper's, through one shared `StampFieldHeight(wrap, label, control)` that takes `max(control, label) + margins + wrapper padding`. A pinned wrapper no longer goes deaf. Re-measured: field 227.56 = control 227.56, overflow gone; T-0287's original case (Torch "Flame type", Jet "Jet type") still clean in Pyre and in Shaper. |
| 2 | **The Lights card's cap of 8 was not enforced in either direction.** `ShaperLightRig.MaxLights` is 8 and `ShaperLightLaw` clamps to it when it shades, so a ninth light is authored, drawn on a card and saved while contributing nothing. | Pressed `+ Add light` nine times from three lights: **10 lights, button still enabled**, ten cards on screen. Render check on a fresh rig at an intensity that does not saturate: 7→8 lights changes **504 px**, 8→9 changes **0**, 8→10 changes **0**. And once the card had been BUILT at the cap, removing lights left the button **stuck disabled** at one light. Both halves are the same cause: `atCap` was computed once in `BuildLightsSection` and `RebuildLightList` never re-read it. | `addLightButton` is a field and `RefreshAddLightButton()` runs from `RebuildLightList()`, which every add, remove, enable-toggle and reorder already routes through. Re-measured: nine presses now give exactly **8 lights** and a disabled button whose tooltip says why; removing back to one **re-enables** it. |
| 3 | **The transport's "Tile size" slider destroyed itself on the first press of a drag.** Its callback answered every value change with `FillTransport()`, which clears the whole transport — including the slider under the pointer. | Real drag, synthesized: press at 90 % set **118** and detached the element the same instant (`panel=null`, and the slider now in the tree is a different object); three further moves across the whole track left it at **118**. Every other transport slider survives its own callback and drags correctly (Rate 26→4, Loop gap 3.40→0.40, Frame 13→2, Width 222→54). | `ShaperFilmstripElement.SetTileSize` restyles the strip and its tiles in place — no teardown, no re-render, textures and pre-baker untouched — and the window calls that instead of `FillTransport()`. Re-measured: the slider survives (`sameObject=True`) and the drag tracks **118 → 92 → 66 → 40**, with the strip's own height and every tile following (40 → 103 px). This is the shape Pyre's cherry panel already uses: its Tile px slider rebuilds the GRIDS, not the box it lives in. |
| 4 | **The cherry Zound-cue row wrapped by four tenths of a pixel and drew its picker outside its own row.** | Live geometry: the row's slider + picker need **336.00 px** and the group resolved to **335.56**; Yoga rounded that tie into a wrap, and the wrapped line is not reserved, so the `(none)` Zound picker was drawn at y=1278 while its group ends at y=1268 — **18.7 px outside**, on top of the Bake box below it. | The row is `flexShrink: 0` and `flexWrap: NoWrap`. Two controls that both carry an explicit width, inside a field with 828 px to spend, have nothing to gain from wrapping, and a sub-pixel tie will land differently at another DPI. Re-measured: both controls on one line, and a full-tree overflow scan of the whole window (popover included) now returns **1 hit of 1108** — the ScrollView's own content, the known exclusion. |
| 5 | **A light's "Specular" tooltip claimed it "applies to either kind" and stopped there**, which is true and useless: on the default layer a Directional light's highlight cannot appear at all. | Directional, flat normals: 0 px over the dial's whole range, at pitch 36, 60, 70 and 90, at ambient 0.18 and 0, at intensity 0.943 and 0.2. Directional + Dome height with `normalKind = Profile`: **164 px**. Point, flat normals: **504 px**. | The tooltip now says it needs a surface that turns, names Flat normals as the condition, and says a Point light's highlight still shows. |
| 6 | **The one shape-picker entry that draws nothing did not say so.** | "Image" renders **0 lit pixels at every frame** at its defaults; the other 42 entries all draw. Its tooltip described what a sprite would do and never mentioned that one is required. | Tooltip now ends "Nothing is drawn until you assign a Sprite in the Shape card below." |
| 7 | **"Ambient" was printed twice in one row** — the label of the rig's colour swatch and the caption of the slider beside it ("Ambient ×"). | Read on screen in the Lights card. | The slider is **"Strength"**. Seen in `a21-fixed3-182353.png`: the row reads `Ambient [swatch] Strength 0.18`. A duplicate-caption scan of the whole window with every section open confirms no new repeat was introduced. |

## 6. Filed rather than fixed

- **T-0289** — the cherry slot popover draws Length ×, Min and Max live together and exactly two of the three are dead at any moment, decided by the Variable-length toggle right above them. Measured against `ShaperCherryFrame.ResolveLength`. Not fixed: the popover is built by the shared `Editor/Pyre/PyreCherryPanel.cs`, a Pyre file.
- **T-0290** — the same popover's Min/Max row wraps to two lines in a one-line-tall row, so the MultiFrame button is drawn 8.5 px inside the Max slider. The `Z.Row`/`Z.HGroup` version of the defect T-0287 fixed for `Z.Field`. Not fixed: shared Pyre panel plus a shared-ZUI change that needs its own cross-host pass.
- **T-0291** — `ZuiMicroSlider.ValueFromX` has no NaN guard, so a press that lands before the slider's first layout returns NaN, and the transport's `Mathf.RoundToInt(NaN)` writes **int.MinValue** into `currentFrame`. Self-heals on the next `FillTransport`, and a physical mouse cannot normally reach it, so it is filed rather than fixed.
- **T-0292** — the same control is captioned "Tile size" in the transport (T-0257 renamed it) and "Tile px" in the cherry panel, 250 px apart in the same pane. The rename did not reach the shared Pyre panel.

## 7. Looked at and found CLEAN

- **Duplicate-caption scan of the whole window**, every section open, every box expanded: 148 distinct captions, 49 repeated — and every repeat is one of four sanctioned shapes: a toggle-bar button beside the section title it shows; an enum's OPTION names shared by the node's fill and the border's fill, each in its own titled box; the same dial on two light cards; or a glyph (`?`, `▾`, `×`, `≡`, `✔`, `●`). Two same-word/different-meaning pairs are recorded for the owner rather than changed, because both readings are deliberate: **"Height"** (the Canvas card's dimension vs the layer's Height-stage toggle, which is paired with "Mask") and **"Position"** (a light's position vs the node's Position box). **"Fit"** appears three times: twice as the fill mapping in two fill boxes, once as the preview's zoom-to-fit button in the other pane.
- **The section toggle bar tracks section visibility exactly.** A desync seen mid-pass (four sections lit off while visible) was self-inflicted — a probe had force-set `ZuiSection.IsOpen` behind the bar's back and then clicked every segment on. Driven only through its own segments, each click hides and shows the matching section, and a `Rebuild()` preserves the state.
- **T-0285's Duplicate**, re-verified with a minimal repro that does not call `AssetDatabase.Refresh()` mid-setup: an unrelated saved document edited in memory to width 199 and left dirty is **still 199 and still dirty** after the real Duplicate button is pressed, and the window switches to the copy.
- The four placement pads still expand on a real click (4 → 19 elements each).

## 8. Files changed

`Zui/Toolkit/Zui.cs`, `Editor/Shaper/ShaperWindow.cs`, `Editor/Shaper/ShaperWindow.Lights.cs`, `Editor/Shaper/ShaperWindow.Cherry.cs`, `Editor/Shaper/ShaperFilmstrip.cs`, `Editor/Shaper/ShaperShapePicker.cs`. **No Pyre file touched.** No serialized field renamed, no default changed, no render code touched, no new menu item, no new control. No commit (programme rule 3).

## 9. Cleanup

`Assets/Shaper` is back to the two pre-existing scratch documents (`New Shaper.asset`, `New Shaper 1.asset`) and nothing else: `AuditA21/A21b/A21c.asset`, `AuditA21x/`, `AuditA19/`, `Audit0271/`, `Audit0277/`, `ShaperViews.asset` and all eight bake outputs are deleted with their `.meta` files. Every `A19.*`/`A21.*`/`A21Cap.*`/`ShaperCap.*` EditorPref is removed and the window title is back to "Shaper". `Assets/Demos/ShaperDemo` is unmodified.

**One asset outside the remit was written and has been reverted.** `Assets/Pyre/New Pyre Plus.asset` was already dirty in memory when this task first opened Pyre's window (left that way by an earlier session), and something on this task's path flushed it to disk — the diff is a pure re-serialisation against the current `ZuiGradient` shape (`gradient` → `legacyGradient` plus the new `stops`/`space`/`stopsAuthored` fields) with `previewLayerSel: 2147483647 → 0`. Nothing authored changed. The Pyre window was closed, the object's dirty flag cleared and unloaded, the file restored with `git checkout` and force-reimported, and `git status` now shows it unmodified. `git status` at the end lists only the six source files above plus the modifications that were already present when this task began (`.mcp.json`, `CLAUDE.md`, `Assets/Pyre/Green Lantern.asset`, the TextSplash border font, and the `Samples~` tree).
