# F8 — Pyre behaviour checklist: the acceptance test for Shaper (T-0189)

**What this is.** Every user action a Pyre window supports, the visible result the user is entitled to expect from it, and how to check that result *temporally* (over seconds, with the clock running) rather than by asking an API whether a field changed. Derived from Pyre's own source, not from docs or memory: every row carries a `file:line` citation into `Assets/Packages/Laubrary/…` in the `feat/shaper` worktree.

**Why it exists.** The owner's complaint — "how come no agent understood that the animation transport was supposed to move forward, update the image and loop back" — is a complaint about a class of failure that no compile check and no reflection probe can see. A probe proves a *part* works. A human completes a *task*. This document is the task list: walk it in the editor, top to bottom, on a document you author from empty.

**How to read the Shaper column.** `Same` = the Shaper code implements the same observable behaviour. `Differs` = it does something, but not this. `Missing` = there is no code for it. `Unknown` = I could not settle it from source alone and the PM must walk it. The column is a *reading of code*, never a claim of having seen it run — I had no editor rights on this task. Nothing here is verified by eye.

**Reading conventions.** "within N s" means *from the user's gesture*, on an ordinary 96×64, 16-frame document. "Diff the preview texture" means capture the preview's pixels at two times and compare — a transport that has stopped and a transport that is playing identical frames are indistinguishable any other way, and that ambiguity is exactly how a broken Play button survived review.

---

## 1. Library / asset chrome

| # | User action | Expected visible result | How to verify (temporally) | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 1.1 | Open the tool from its menu with nothing selected | The library browser fills the window; there is no disabled workbench and no blank panel | Open cold, screenshot within 2 s | `Editor/Pyre/PyreWindow.cs:307` (BuildAsset only runs once an asset is selected) | Same — `Editor/Shaper/ShaperWindow.cs:183-186` |
| 1.2 | Press New | A new asset appears in the browser, is selected, and its preview shows *something* — never an empty canvas | Watch the preview 2 s after New; it must be non-blank | `Editor/Pyre/PyreWindow.cs:920` (a new layer is a full default layer) | Same — `ShaperWindow.cs:71-74`, and `NewLayer` sizes the rect to a quarter-canvas so the first render is legible (`ShaperWindow.cs:272-286`) |
| 1.3 | Hover a browser cell of an animated asset | The cell thumbnail *animates* — successive frames, on the asset's own fps, looping | Hover 3 s; diff the cell's pixels at t=0 / t=1 / t=2 | `PyreWindow.cs:45-63` (`AnimateThumbnails`, time-based frame pick at `previewFps`) | Same — `ShaperWindow.cs:101-108`, drives `ShaperClock.WrapFrame` at `frameRate` |
| 1.4 | Duplicate an asset | The copy appears beside the original and is selected; Undo restores | Ctrl+Z once; the copy is gone | AssetKit base (`ZuiAssetWindow`) | Same (same base) |
| 1.5 | Delete an asset | A confirm dialog appears first — deletion is not undoable | Cancel, then confirm | AssetKit base | Same (same base) |
| 1.6 | Switch to another asset while playback is running | Playback stops or restarts on the *new* asset; the frame resets to 0; nothing keeps driving the old asset's clock | Switch mid-play; check the readout says frame 1 and the picture is the new asset | `PyreWindow.cs:148-154` (`OnAssetChanged` → frame 0, cache destroyed, cherry reset) | Same — `ShaperWindow.cs:80-86` (also unsubscribes the tick) |
| 1.7 | Rename the asset | The browser cell and window title follow immediately | Rename, watch the cell within 1 s | AssetKit base | Same |
| 1.8 | Close and reopen the window on the same asset | Fold state, section toggles and (Pyre) the selected layer come back | Reopen; compare | `PyreWindow.cs:100-124` (layer selection persisted on the spec) | Differs — Shaper's `selectedLayer` is `[SerializeField]` on the *window* (`ShaperWindow.cs:60`), so it survives a domain reload but is reset on every asset switch, and is not per-asset |

## 2. Canvas section

| # | User action | Expected visible result | How to verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 2.1 | Drag Canvas size | The preview picture resizes live and stays centred; the frame cache reallocates without a visible tear | Drag continuously 2 s; the preview must never go blank mid-drag | `PyreWindow.FrameCache.cs:139-157` (structural change reallocates textures) | Same in intent — `ShaperPreviewStage.cs:418` (`EnsureShape`) plus the explicit hold-the-old-picture guard at `ShaperPreviewStage.cs:422-427`. Shaper splits it into Width/Height (`ShaperWindow.cs:299-302`) |
| 2.2 | Raise Frames from 16 to 32 | The scrubber's range grows to 32 immediately; the readout says `/32`; the filmstrip grows to 32 tiles; playback keeps running | Drag Frames while playing; readout and tile count must follow within 1 s | `PyreWindow.cs:646-660` (`RefreshTransportReadout` on every frame change and rebuild) | **Differs** — the scrubber's `max` is captured at `BuildTransport` time (`ShaperWindow.cs:777`, `808`) and there is no `RefreshTransportReadout` equivalent; the range follows only on a full `Rebuild()`. `Change()` does not call `Rebuild` (`ShaperWindow.cs:927-940`) |
| 2.3 | Lower Frames from 16 to 4 while parked on frame 12 | The transport clamps to frame 3, not to a dead index; the picture updates | Watch the readout after the drag | `PyreWindow.cs:652-657` | Unknown — `currentFrame` is clamped in `BuildTransport` (`ShaperWindow.cs:778`) and in `PlaybackTick` via `WrapFrame`, but a paused window that never rebuilds may show a stale index. PM must walk it |
| 2.4 | Change Seed | Every deterministic draw changes; the preview repaints; the result is stable across repeats of the same seed | Set seed 5, screenshot; set 6; set 5 again — must match the first shot | `Runtime/Pyre/Pyre.cs:1187` | Same — `ShaperWindow.cs:319-330`, with a deliberate uint-safe path |
| 2.5 | Set a background colour | It composites *under* every layer and reaches the bake | Bake and open the PNG | n/a (Pyre has no document background) | Shaper-only — `ShaperWindow.cs:332-340` |
| 2.6 | Every canvas edit | One Ctrl+Z undoes it | Edit, Ctrl+Z, the value returns | `PyreWindow.cs:2574-2585` (`Val`/`Dial` record Undo) | Same — `ShaperWindow.cs:927-940` (`Change`) and `950-973` (`Val`) |

## 3. Layers list

| # | User action | Expected visible result | How to verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 3.1 | Click a layer row | The row highlights; the Shape / Swarm / Modifiers cards below re-point at that layer within 1 frame | Click; the cards' values must change | `PyreWindow.cs:941-955` (`SelectLayer` → `RebuildAllForSelection`) | Same — `ShaperWindow.cs:449-450` (`selectedLayer` then `Rebuild`) |
| 3.2 | **Drag the ≡ grip up/down** | The row follows the cursor, drops at the new index, the *stack order changes in the preview* (what was behind is now in front), and the dragged layer stays selected | Drag a bright layer under a dark one; the picture must change within 1 s | `PyreWindow.cs:815-828` (`ZuiReorder.MakeGrip`, wrap is the drag unit) | Same mechanism — `ShaperWindow.cs:433-443`. **Differs in one detail:** Pyre makes a *wrap* (row + its folded matte box) the drag unit so folded content travels with the row; Shaper's drag unit is the bare `row` (`ShaperWindow.cs:433`), which is correct only while nothing folds under a row |
| 3.3 | Toggle a layer's enable checkbox off | That layer vanishes from the preview within ~1 s; every other layer is unchanged; the row shows it is off | Toggle while playing; the picture must change and playback must not stop | `PyreWindow.cs:831-832`, `850` ("off" tag) | Differs — the toggle works (`ShaperWindow.cs:446-447`) but there is no "off" indicator text on the row |
| 3.4 | Press "+ Add layer" | A new layer appears **selected**, with default dials, and something visible appears in the preview | Add on an empty-ish document; the preview must change | `PyreWindow.cs:915-924` | Same — `ShaperWindow.cs:363-368`; the new layer is canvas-sized so it is visible |
| 3.5 | Press per-row "Dup" | An exact copy lands *directly after* this row, is selected, and the preview shows the doubled contribution | Dup a layer, then move its Z; the two must separate | `PyreWindow.cs:887-898` | Same — `ShaperWindow.cs:472-483` |
| 3.6 | Press "×" on the only layer | The window refuses and says why (a notification), rather than leaving an unrenderable document | Delete down to one, then press × | `PyreWindow.cs:900-902` (`ShowNotification`) | **Missing** — `ShaperWindow.cs:485-490` removes unconditionally; a zero-layer document is reachable |
| 3.7 | Rename a layer in place | The name field accepts typing without stealing selection; the change is undoable | Type, click away, Ctrl+Z | `PyreWindow.cs:836-848` (Undo recorded; pointer-down also selects the row) | Differs — `ShaperWindow.cs:452-453` renames through `Change` (undoable) but does **not** select the row on pointer-down, so typing in a non-selected row's name edits a layer whose dials are not on screen |
| 3.8 | Toggle a layer's matte icon | The matte box folds out/in under the row, the row's matte indicator appears, and the composite changes | Toggle; the picture must change | `PyreWindow.cs:869-881`, `908-911` | Shaper has a per-layer **Mask** section instead (`ShaperWindow.Sections.cs:1259-1267`), not a per-row folded box. Different model — walk it as its own feature, not as this row |
| 3.9 | Set a layer's lifetime window (frames 4–9) | Outside 4–9 the layer paints nothing; inside it paints; the transition is visible as you scrub | Scrub 0→15 slowly, watching the layer appear and disappear | n/a (Pyre expresses this through envelopes) | Shaper-only — `ShaperWindow.cs:388-406`; gated on `frameCount > 1` |

## 4. Shape picker + per-form cards

| # | User action | Expected visible result | How to verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 4.1 | Change the shape/form | The card body below swaps to that form's own dials — and *only* that form's; rows that do not apply are absent, not disabled | Switch Disc→Star→Text; count the rows each time | `PyreWindow.cs:1299-1320` (rows hidden per form) | Same in kind — `ShaperWindow.Sections.cs:556-666` (per-primitive bodies) |
| 4.2 | Drag any shape dial | The preview updates *continuously during the drag*, not only on release | Drag for 2 s; diff the preview at 3 points inside the drag | `PyreWindow.cs:2574-2585` → `MarkDirty` → `CachedFrame` composites the on-screen frame synchronously (`FrameCache.cs:205-213`) | Same in intent — `ShaperWindow.cs:960-971` invalidates + refreshes on every change. **Risk:** a Shaper frame is ~30 ms, and the cache is invalidated *wholesale* on every edit (`ShaperPreviewFrameCache.cs:65-69`), so a drag re-renders from scratch each tick |
| 4.3 | Drag a dial **while playback is running** | The picture changes *and playback keeps advancing* — the transport does not stop, stutter to a halt, or reset to frame 0 | Play, then drag a size dial for 3 s; the frame readout must keep incrementing throughout | `FrameCache.cs:187-206` + `PyreWindow.cs:185-211` (playback only steps onto ready frames; an edit restarts the fill from the frame on screen, it does not stop the clock) | **Differs / high risk** — `Change`/`Val` drop *every* cached frame (`ShaperPreviewFrameCache.cs:65`), and `PlaybackTick` refuses to advance onto an uncached frame (`ShaperWindow.cs:912). So a sustained edit can pin playback in place until the pre-baker catches up. The status line names this ("caching n/N", `ShaperWindow.Preview.cs:159-162`) but the transport still visibly stalls |
| 4.4 | Right-click any animatable dial | A menu opens offering Static / Min-Max / Envelope / Steps / Oscillation, plus curve-display toggles, shape generators, a shape recall grid, and copy/paste | Right-click on the slider itself (not a margin); the menu must appear | `Zui/Toolkit/ZuiValueControl.cs:182-198`, `746-845` | Same — Shaper's `Val()` builds the same control (`ShaperWindow.cs:950-973`) |
| 4.5 | Switch a dial to Envelope and drag a point | The preview changes *over frames*, not uniformly: scrubbing 0→last shows the value sweeping | Set an envelope 0→1, then scrub; diff the preview at frames 0, mid, last | `ZuiValueControl.cs:240-241`, `284`; evaluation at `PyreWindow.Preview.cs:684-693` | Same control; Shaper resolves `ZUIValue` against `phase01` via `ShaperClock.PhaseOfFrame` (`Runtime/Shaper/ShaperClock.cs:53-57`) |
| 4.6 | Right-click an *envelope canvas* point | The point is removed (the envelope's own gesture survives — it is not swallowed by the mode menu) | Right-click a point; it disappears, no menu opens | `ZuiValueControl.cs:191-193`, `270-272`; `Zui/Scripts/Editor/ZUIEnvelope.cs:745-754` | Same (shared control) |
| 4.7 | Every control has a tooltip that says the *effect*, not the label | Hover each; read | — | Pyre tooltips throughout, e.g. `PyreWindow.cs:538-546` | Largely same; spot-check per card |

## 5. Life envelopes / animation defaults — "does a fresh layer animate out of the box?"

This group is the heart of the owner's complaint. In Pyre, a brand-new layer **already animates** with no authoring at all, because several of its dials default to *curves*, not constants.

| # | Pyre default | What the user sees with zero authoring | Verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 5.1 | `frameCount = 16` | A new asset is animated, not a still | Press Play on a brand-new asset | `Runtime/Pyre/Pyre.cs:1186` | **Differs — the single biggest gap.** `ShaperDocument.frameCount = 1` (`Runtime/Shaper/ShaperDocument.cs:121`), and the transport is only built at all when `frameCount > 1` (`ShaperWindow.cs:753`). **A new Shaper document has no Play button, no scrubber, no filmstrip and no cherry panel until the user first discovers the Frames dial.** |
| 5.2 | `previewFps = 12` | Playback runs at a legible 12 fps | Count frames over 4 s | `Pyre.cs:1222` | Same value — `ShaperClock.DefaultFrameRate = 12` (`Runtime/Shaper/ShaperClock.cs:41`) |
| 5.3 | `alpha` defaults to a **Curve** 0 → 1 → 1 → 0 | A new particle fades in, holds, fades out — visible motion with no authoring | Play a new asset; diff the preview at frames 0 / mid / last | `Pyre.cs:325`, `980-991` | **Missing** — every Shaper dial defaults `Static` (`Runtime/Shaper/ShaperPrimitives.cs:91-160`, all `new ZUIValue(x)`). Nothing in a new Shaper document changes over phase |
| 5.4 | `size` defaults to a **Curve** 4 → 22 → 16 px | The disc grows fast then settles — the shape visibly changes size over life | Same diff | `Pyre.cs:326`, `992-1001` | Missing (as 5.3) |
| 5.5 | `density` / `heat` default to Curves (0.15→0.9→0.5, 0.95→0.55→0.12) | The cloud gains body and cools from fire to smoke over life | Same diff | `Pyre.cs:293-295`, `1002-1026` | n/a — Shaper has no ramp-cloud model. Its analog is Fill; check whether any Fill default varies with phase (**Unknown**) |
| 5.6 | `streakLength` defaults to a Curve 5 → 26 → 18 | A Streak lengthens then eases back | Same diff | `Pyre.cs:465`, `1027-1038` | n/a |
| 5.7 | `fireIntensity` / `fireballSource` default to ignite-hold-fade Curves | A Fire/Fireball layer burns and dies without authoring | Same diff | `Pyre.cs:541`, `598`; `1039-1064` | n/a |
| 5.8 | `swarmSpawnTiming` defaults to (0,0)→(1,0.5) | Swarm particles are *staggered* over the first half of the timeline, so the cloud builds up rather than appearing at once | Play; early frames must show fewer particles than late ones | `Pyre.cs:698`, `1076-1088` | Unknown — `Runtime/Shaper/ShaperSwarmDef.cs` not audited on this task; PM should check its dial defaults for any non-Static value |
| 5.9 | `swarmCustomX/Y` default to a visible zigzag | Switching to a Custom path shows a real path immediately, not a degenerate point | Switch to Path+Custom | `Pyre.cs:687-688`, `1089-1112` | Unknown (same file) |
| 5.10 | **The acceptance test for this whole group** | Create a new asset, press Play, touch nothing else: the picture must visibly change frame to frame and loop | Record 3 s of the preview; adjacent frames must differ | the defaults above, collectively | **Fails today** by 5.1 alone: there is no Play button on a new document |

## 6. Swarm (Pyre) / Swarm section (Shaper)

| # | User action | Expected visible result | How to verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 6.1 | Enable Swarm | One particle becomes N placed particles, immediately visible | Toggle; count blobs | `Pyre.cs:660-661` (`swarmEnabled`, `swarmCount = 8`) | Section exists (`ShaperWindow.Sections.cs:753-757`); behaviour Unknown |
| 6.2 | With Swarm on, the preview draws an **overlay**: a cyan outline of the spawn shape, a warm dot at every real spawn point, each labelled with the frame it spawns on | The dots are the *placement truth*, not a guess | Toggle a swarm dial; the dots must move with it | `Editor/Pyre/PyreWindow.Preview.cs:279-338`, `440-484` | **Missing** — Shaper's stage draws only a single node-position handle (`ShaperPreviewStage.cs:134-146`, `242-277`). No spawn outline, no spawn dots, no per-dot frame labels |
| 6.3 | **Drag the shape-position square** on the preview | The whole cloud follows the cursor live; on mouse-up ONE undo step covers the whole drag | Drag, Ctrl+Z once — the cloud returns to its start, not to a mid-drag position | `PyreWindow.Preview.cs:528-559` (live offset held uncommitted; `Dirty` only on MouseUp) | Partly — Shaper's handle has the same commit-on-up shape (`ShaperPreviewStage.cs:279-301`, `ShaperWindow.cs:736-746`) but moves a *node*, not a swarm |
| 6.4 | With an *animated* offset, the position handle draws hollow and refuses to drag | A dial with no single value cannot be dragged to one — and the UI says so by its appearance | Set offset to Curve; the square goes hollow | `PyreWindow.Preview.cs:304-307`, `509-525` | Unknown — `ShaperPreviewStage.cs:267-269` varies the handle's fill and picking mode by a `draggable` flag; whether the condition is "the dial is Static" must be walked |
| 6.5 | Left-click empty canvas in Path+Custom mode | A new path point is appended and the outline immediately re-routes through it | Click three times; the polyline must grow each time | `PyreWindow.Preview.cs:614-627` | Missing |
| 6.6 | Right-click a Custom path point | It is removed, unless only two remain (then nothing happens) | Right-click down to two points | `PyreWindow.Preview.cs:603-608` | Missing |
| 6.7 | Turn on "Show trace" | An amber spine appears showing the path the spawn point sweeps over the whole timeline, under the outline | Toggle; the spine appears within 1 frame | `PyreWindow.Preview.cs:287-289`, `347-391` | Missing |
| 6.8 | Rotate the swarm while playing | The drawn outline *rotates on screen with the animation* — the overlay is sampled at the current frame's life, not at life 0 | Play with a rotation curve; the cyan outline must turn | `PyreWindow.Preview.cs:274-278`, `296-303` | Missing |

## 7. Modifiers / SpriteFX + Global

| # | User action | Expected visible result | How to verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 7.1 | Press "+ Add modifier" | A picker opens grouped Geometry / Pixel / Post; picking one appends a card and the preview changes | Add a Blur-like effect; the picture must soften within 1 s | `Editor/Pyre/PyreWindow.Modifiers.cs:81`, `345-362` | Same shape — `ShaperWindow.Sections.cs:1437-1439` (`ShowAddEffectMenu`) |
| 7.2 | **Drag a modifier card's grip** | Apply order changes and the composite visibly changes (blur-then-tint ≠ tint-then-blur) | Reorder two effects whose order matters; diff | `PyreWindow.Modifiers.cs:277` | Same — `ShaperWindow.Sections.cs:1457-1469` |
| 7.3 | Toggle a modifier's enable | It stops applying; the card and its dials stay | Toggle twice; the picture must return exactly | `PyreWindow.Modifiers.cs` card header | Same in kind (`BuildEffectCard`); exact parity Unknown |
| 7.4 | Press a modifier's "X" | The card is removed, undoably | Remove, Ctrl+Z | `PyreWindow.Modifiers.cs:297` | Same |
| 7.5 | A **Global** modifier changes every layer at once | Add one; all layers change together | Add a global tint on a 3-layer document | `PyreWindow.Modifiers.cs:107-151` | Same split — per-layer "SpriteFX" + document-wide "Global SpriteFX" (`ShaperWindow.Sections.cs:1391-1409`) |
| 7.6 | The collapsed section header shows how many effects are enabled | Fold the section; the count is still visible | Fold | Pyre shows the list; Shaper adds a suffix | `ShaperWindow.Sections.cs:1422-1427` — Shaper-only nicety |
| 7.7 | Editing a *global* modifier only re-composites; it does not re-render every layer | The edit lands faster than a per-layer dial edit does | Time both on a heavy document | `FrameCache.cs:4-10` (layer buffers keyed by content) | **Differs** — Shaper has no per-layer buffer cache; any edit drops all frames (`ShaperPreviewFrameCache.cs:31-38`) |

## 8. Transport + preview — the core of the complaint

| # | User action | Expected visible result | How to verify (temporally) | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 8.1 | **Press ▶ Play on a 16-frame document** | Within 1 s: the frame readout increments, the preview image changes on every frame, and at the end it **wraps from the last frame back to 0 and keeps going** | Capture the preview at t = 0, 0.5, 1.5, 3 s and diff pixels; read the frame readout at each. Adjacent captures must differ; the readout must have wrapped at least once by 3 s at 12 fps / 16 frames | `PyreWindow.cs:185-211` (accumulator → `frame = (frame + 1) % frameCount`), `212` (repaint + readout) | Same rule — `ShaperWindow.cs:882` (`ShaperClock.AdvanceFrames`), `904` (`ShaperClock.WrapFrame`). **But it exists only when `frameCount > 1`** (`ShaperWindow.cs:753`), which is not the default |
| 8.2 | The Play button's own label flips to ❚❚ Pause | Instantly, on the press | Press; read the button | `PyreWindow.cs:530-534` | Same — `ShaperWindow.cs:780-783` |
| 8.3 | Press Pause | Frames stop advancing; the picture holds on the frame that was showing; the readout stops | Capture at t and t+2 s: identical, and the readout is unchanged | `PyreWindow.cs:179` | Same — `ShaperWindow.cs:871-875` |
| 8.4 | Press Play again after pausing | Playback resumes (Pyre: from where it stopped) | Note the frame, pause, play, watch the next frame | `PyreWindow.cs:530-534` (no reset of `frame`) | Differs deliberately — Shaper restarts the *cherry* sequence from slot 0 on every press (`ShaperWindow.cs:788-790`); the plain frame order resumes in place |
| 8.5 | Playback runs at the authored rate | 12 fps means 12 distinct frames per second, ±1 | Record 4 s, count distinct frames | `PyreWindow.cs:183` (`acc += dt * previewFps`) | Same — `ShaperClock.cs:81-91` |
| 8.6 | An editor stall (compile, breakpoint) does not make playback "catch up" by skipping | After a stall, playback resumes from where it was, not several frames on | Force a compile mid-play | `PyreWindow.cs:181` (dt clamped to 0.1 s) | Same, and documented as such — `ShaperClock.cs:75-84` |
| 8.7 | **Drag the Frame scrubber while playing** | Playback *pauses*, the Play label resets to ▶, and the preview holds exactly the frame under the handle — the picture follows the handle continuously during the drag | Play, then drag slowly across the whole range; the picture must change with every step and must not resume when you release | `PyreWindow.cs:601-610` | Same — `ShaperWindow.cs:808-819`; it additionally clears `cherryRunning` so the scrub is not overridden by the sequencer |
| 8.8 | Scrub to a frame that has never been rendered | The frame appears — synchronously if need be — rather than showing blank or a stale neighbour | Invalidate (edit a dial), then immediately scrub far away | `FrameCache.cs:205-213` (`CachedFrame` composites *now* for the on-screen frame) | Same — `ShaperPreviewStage.cs:419` computes synchronously; `ShaperPreviewFrameCache.cs:77-86` |
| 8.9 | The frame readout | Reads `frame N/M`, 1-based, and tracks the scrubber and the tile highlight in both directions | Scrub, click a filmstrip tile, play — all three must agree | `PyreWindow.cs:628`, `649-660` | **Differs** — Shaper has no `frame N/M` label. The scrubber's own caption is the only frame display (`ShaperWindow.cs:808`), plus a status line that only speaks while playing or under cherry (`ShaperWindow.Preview.cs:138-163`) |
| 8.10 | Drag **Zoom** | The picture magnifies; the frames are *not* re-rendered (no stall, no cache churn) and a bake is unaffected | Drag zoom during playback; playback must not hitch | `PyreWindow.cs:619-623` (`DirtyRepaintOnly`) | Same, structurally guaranteed — `ShaperWindow.Preview.cs:77-85` and `ShaperPreviewStage.cs:448-452` (a layout inset, never a re-render) |
| 8.11 | Toggle **Frame** (canvas border) | A thin border appears at the canvas edge; it is cosmetic and never baked | Toggle; bake; the PNG has no border | `PyreWindow.cs:538-541`; drawn at `PyreWindow.Preview.cs:121-129` | Same — `ShaperWindow.Preview.cs:67-75` |
| 8.12 | Toggle **Strip** (filmstrip / contact sheet) | The single frame is replaced by a sheet of every frame; a **Tile px** slider appears beside the toggle | Toggle; count tiles = frame count | `PyreWindow.cs:542-553`; drawn at `PyreWindow.Preview.cs:204-254` | **Differs** — Shaper's filmstrip is a permanent row under the scrubber (`ShaperWindow.cs:840-853`), not a mode; there is no Strip toggle and no Tile-px slider (tile size is a const, `ShaperFilmstrip.cs:30`) |
| 8.13 | **Click a filmstrip tile** | The transport jumps to that frame; the tile highlights; the single-frame view (Pyre) or the preview (Shaper) shows it | Click tiles at random; the readout must follow each click | `PyreWindow.Preview.cs:218-228` | Same — `ShaperWindow.cs:841-851` (also pauses and clears cherry) |
| 8.14 | Watch the filmstrip fill after an edit | Tiles fill in progressively in the background; a not-yet-rendered tile is empty, never wrong | Edit a dial, then watch the strip for 3 s | `PyreWindow.Preview.cs:246` (`IsFrameReady`) | Same shape — `ShaperFilmstrip.cs:39-41`, `103-121` |
| 8.15 | Watch the **cache readout** during a fill | A `rendering n/N` readout is visible *only while filling*, and its tooltip explains what the fill is doing and what the last one cost | Edit a heavy dial; hover the readout | `FrameCache.cs:432-452` | Differs — Shaper shows `n/M cached` permanently plus a per-frame tick strip (`ShaperWindow.cs:830-837`, `860-865`) — arguably better, but always-on rather than transient, and its tooltip carries no timing |
| 8.16 | The readout row never reflows | Text appearing/disappearing must not shift the controls around it | Watch the row across a whole fill | `PyreWindow.cs:629-632` (reserved width, `visibility` not `display`) | Same discipline — `ShaperWindow.cs:825-836`, `ShaperWindow.Preview.cs:115-118` |
| 8.17 | Playback never renders synchronously | Play stays smooth even on a heavy document: it steps only onto frames already composited, and waits at the render front rather than freezing the editor | Play a heavy document immediately after an edit; the editor must stay responsive | `PyreWindow.cs:187-208`, `FrameCache.cs:16` | Same rule — `ShaperWindow.cs:906-912`. The *visible* consequence differs: Pyre holds `acc` at one beat and moves the instant the frame lands; Shaper simply returns, and the status line explains the hold (`ShaperWindow.Preview.cs:105-107`) |
| 8.18 | Drag the **preview resize bar** | The preview island grows/shrinks vertically; the setting survives a domain reload | Drag, force a recompile, reopen | `PyreWindow.cs:435-451` | **Missing** — Shaper has a left/right splitter (`ShaperWindow.cs:222`, `Z.Split`) but no preview-height drag bar |
| 8.19 | Drag the **vertical splitter** past 720 px | The dial pane splits into a second column (and a third past 1080) | Drag wide; count columns | `PyreWindow.cs:413-430` + `ColumnFlow` | Same — `ShaperWindow.cs:205` (`Z.ColumnFlow(360f)`), `222` |
| 8.20 | Set **Delay** > 0 | Between loop iterations the preview holds **blank** for that long, then restarts from frame 0 — a real gap, not a held frame | Set 1 s, play, watch for a visible empty beat once per loop | `PyreWindow.cs:636-639`; blank sentinel drawn at `PyreWindow.Preview.cs:77-85` | **Differs** — Shaper's loop gap is authored inside the cherry panel and only applies under cherry framing (`ShaperWindow.Cherry.cs:148-151`; `ShaperCherry.AdvanceOneBeat`). Pyre's Delay applies to a plain loop too (`PyreWindow.cs:633-635` says so explicitly) |
| 8.21 | Set a preview backdrop image/colour | It paints *behind* the picture, is per-asset, survives reopening, and never reaches a bake | Set a bright backdrop, bake, open the PNG | `PyreWindow.cs:81-92`, `PyreWindow.Preview.cs:270` | Same, and structurally argued — `ShaperWindow.Preview.cs:8-26`, `179-204` |
| 8.22 | Pick a backdrop image | The panel *rebuilds* to show the extra controls the image needs | Pick; new controls appear | `PyreWindow.cs:676-680` | Same — `ShaperWindow.Preview.cs:202` |

## 9. Cherry framing

| # | User action | Expected visible result | How to verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 9.1 | Turn cherry framing on | Playback stops following frame order and follows the authored slot list instead | Play with a 2-slot sequence on a 16-frame document: only those 2 frames must ever show | `PyreWindow.cs:192-202`, `244-284` | Same — `ShaperWindow.cs:885-901`, `Runtime/Shaper/ShaperCherry.cs` |
| 9.2 | Cherry on with **zero slots** | The preview is blank — and the UI *says why* rather than looking broken | Enable with no slots; read the status line | `PyreWindow.cs:247` (`frame = -1`) — Pyre shows a bare "…" (`PyreWindow.Preview.cs:82`) | **Shaper is better here** — an explicit sentence ("Cherry framing is on with no slots — nothing to play"), `ShaperWindow.Preview.cs:148-149` |
| 9.3 | Press "+ Add slot" | A slot playing the frame currently on screen is appended and selected; the grid grows | Scrub to frame 7, add, check the card | `PyreWindow.CherryFraming.cs:110`, `213` | Same — `ShaperWindow.Cherry.cs:129-147` |
| 9.4 | **Drag one slot card onto another** | The slots reorder, and playback order changes accordingly | Reorder, play, watch the order | `PyreWindow.CherryFraming.cs:311-336` | Same — `ShaperWindow.Cherry.cs:266-292` |
| 9.5 | Shift-click / Ctrl-click slots, then drag | The whole selection moves together | Select 3, drag | `PyreWindow.CherryFraming.cs:311-336` | Same — `ShaperWindow.Cherry.cs:272-273` |
| 9.6 | **Right-click a slot card** | A settings popover opens *anchored to that card*, with Source frame, Variable/Randomise length, Length ×, Min/Max, MultiFrame, Duplicate, Delete | Right-click; the panel must appear at the card | `PyreWindow.CherryFraming.cs:337-395` | Same — `ShaperWindow.Cherry.cs:293-358` |
| 9.7 | Press Delete with slots selected | The selection is removed; Undo restores it | Select 2, Delete, Ctrl+Z | `PyreWindow.CherryFraming.cs:396-446` | Same — `ShaperWindow.Cherry.cs:418-443`, keyboard hook at `156-157` |
| 9.8 | Edit the sequence *while playing* | Playback restarts the sequence from slot 0 rather than holding a stale slot index | Add a slot mid-play; the sequence must restart cleanly | `PyreWindow.cs:224-239` (`ResetCherryPlayback` after any cherry edit) | Same — `ShaperWindow.Preview.cs:211-219`, called from `ShaperWindow.Cherry.cs:143` |
| 9.9 | Set the loop gap > 0 | The preview goes fully blank between passes for that long, then restarts | Set 1 s; watch two loops | `PyreWindow.cs:262-274` | Same — `ShaperWindow.Cherry.cs:148-151`; the blank is honoured by the stage (`ShaperPreviewStage.cs:397-407`) |
| 9.10 | Set the Zound cue slot | Entering that slot fires the cue **once per visit**, not once per beat | Give the slot Length × 4 and listen | `PyreWindow.cs:281-284` — **Pyre records the choice but never fires it** | **Shaper is ahead** — it actually fires, on slot *entry* (`ShaperWindow.Preview.cs:238-256`) |
| 9.11 | Under cherry, the Frame scrubber | Parks on the source frames the sequence names and holds — and the UI explains that this is not a stalled transport | Play a 2-slot sequence; read the status line | n/a (Pyre offers no explanation) | Shaper-only fix — `ShaperWindow.Preview.cs:90-163` |

## 10. Bake / GIF

| # | User action | Expected visible result | How to verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 10.1 | Press Bake | A PNG sprite sheet + AnimationClip appear **beside the asset**, sliced one sprite per frame, point-filtered | Bake, then inspect the imported sheet's sub-sprites | `Editor/Pyre/PyreBaker.cs:32-60` | Same in kind — `Editor/Shaper/ShaperBaker.cs`, driven from `ShaperWindow.Bake.cs:135`, `170` |
| 10.2 | Bake twice | The second bake never overwrites the first — it gets a versioned name | Bake twice; two files | `PyreBaker.cs:19-20`, `47` | Unknown — must be confirmed in `ShaperBaker.cs` |
| 10.3 | The bake matches the preview | The baked frames are byte-identical to what was on screen | Bake, then compare a frame against a preview capture | `PyreBaker.cs:9-10` (same renderer) | Same claim, and the effect applier is passed on both paths (`ShaperPreviewStage.cs:415-419` cites this) |
| 10.4 | Press GIF… | A Save dialog opens; on OK an animated, looping, transparent GIF is written at the preview's own fps and the folder is revealed | Export, open the GIF in a browser — it must loop and animate | `PyreWindow.cs:557-560`, `662-672` | Differs in placement — GIF is a toggle inside the Bake box, written by the Bake button (`ShaperWindow.Bake.cs:103-133`), not a separate transport button |
| 10.5 | GIF scale | Only the exported file's pixel size changes; the live preview is untouched | Export at 1× and 8×; compare file sizes and the unchanged preview | `PyreWindow.cs:561-571` | Same — `ShaperWindow.Bake.cs:121`, window-held (`ShaperWindow.Preview.cs:43`) |
| 10.6 | GIF dither on/off | A soft/feathered edge stipples (on) or is cut hard at 50 % alpha (off) | Export both on a soft-edged effect | `PyreWindow.cs:572-584` | Same — `ShaperWindow.Bake.cs:126` |
| 10.7 | Bake with cherry framing on | The output is honest about whether it preserves the sequence | Read the Bake button's tooltip with cherry on | n/a | Shaper-only — state-dependent tooltip, `ShaperWindow.Bake.cs:12-14`, `149-167` |

## 11. Views / Tags / section chrome

| # | User action | Expected visible result | How to verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 11.1 | Use the section toggle bar | Clicking a name shows that section alone; the bar and the sections' own headers agree | Click through every entry | `PyreWindow.cs:488-498` | Same — `ShaperWindow.cs:226-245` |
| 11.2 | Save a **View** | The current fold/gear/shown-control state of every box is captured under a name and restored on demand; the store is a committed asset, the "last view" is per-user | Save, change folds, restore | `PyreWindow.cs:453-511` | **Missing** — no `ZuiViewBar` or view store anywhere in `Editor/Shaper/` |
| 11.3 | Reopen the window | The view you left it in is re-applied | Close/reopen | `PyreWindow.cs:407` (`viewBar.RestoreLast`) | Missing (as 11.2) |
| 11.4 | Tag the asset | The Tags section behaves as every other Laubrary tool's | — | base class | Same |
| 11.5 | Fold a section, then rebuild the window | The fold state survives (keyed, not title-derived) | Fold, edit something that rebuilds, check | `PyreWindow.cs:776-783` (stable key) | Same — every `Z.Section` in Shaper passes an explicit key |

## 12. Undo

| # | User action | Expected visible result | How to verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 12.1 | Any dial edit → Ctrl+Z | The value returns **and the preview repaints to match** — the cache does not keep showing the post-edit frames | Edit, Ctrl+Z, diff the preview against a pre-edit capture | `PyreWindow.cs:292-296` (`OnBeforeRebuild` sets `previewDirty`, because undo bypasses `Dirty`) | Unknown / **likely gap** — `ShaperWindow.OnBeforeRebuild` (`ShaperWindow.cs:160-172`) disposes the stage and filmstrip but never invalidates a *surviving* cache. Disposal probably makes this moot; PM must undo a dial edit and confirm the picture actually changes back |
| 12.2 | A drag gesture → Ctrl+Z | **One** undo covers the whole drag, not one per mouse-move | Drag a handle across the canvas, Ctrl+Z once | `PyreWindow.Preview.cs:534-547` (mutation only on MouseUp) | Same shape — `ShaperPreviewStage.cs:279-301`, `ShaperWindow.cs:736` |
| 12.3 | Add/remove/reorder a layer → Ctrl+Z | The list returns to its previous order and selection lands somewhere valid | Reorder, Ctrl+Z | `PyreWindow.cs:818-828` | Same — `ShaperWindow.cs:433-443` |
| 12.4 | Backdrop edit → Ctrl+Z | **One** press undoes it, not two | Change the backdrop colour, Ctrl+Z once | Pyre's BackSplashZui records its own Undo | Same, and explicitly guarded against double-recording — `ShaperWindow.Preview.cs:195-198` |
| 12.5 | Every mutation goes through the Undo wrapper | No field is written directly | Code review, not a walk | `PyreWindow.cs:2574-2609` | Same contract, stated — `ShaperWindow.cs:14-15`, `924-973` |

## 13. Persistence / re-entry

| # | User action | Expected visible result | How to verify | Pyre citation | Shaper |
|---|---|---|---|---|---|
| 13.1 | Force a domain reload (recompile) with the window open | The window comes back on the same asset, same section folds, same pane widths; playback state may reset but the window must not be blank or throw | Recompile; watch the window | `PyreWindow.cs:67`, `96` (`[SerializeField]` pane sizes) | Same in kind — `ShaperWindow.cs:60-61` (`[SerializeField]` selection/frame), `ShaperWindow.Preview.cs:38-44` |
| 13.2 | Close the window mid-playback | No tick keeps firing on a destroyed window; no leaked textures or threads | Close during a heavy fill; the console must stay clean | `PyreWindow.cs:132-141`, `171-177` (the `this == null` guard is load-bearing) | Same guard — `ShaperWindow.cs:174-181`, `869-875`; stage/filmstrip disposed |
| 13.3 | Reopen on the same asset | The preview shows the same frame content it did before, without re-authoring anything | Reopen, compare | — | Same |
| 13.4 | Edit the asset from the Inspector, then return | The window's preview reflects the external edit | Edit `frameCount` in the Inspector, refocus the window | Pyre: `previewDirty` via rebuild | **Unknown** — neither tool obviously watches for external edits; worth walking once |

---

## What to walk first

If the PM has time for only ten rows, walk these — each is a place where the code reading says Shaper most likely fails a user's expectation today. They are ordered by how badly a failure hurts a first-time user, not by section number.

1. **5.1** — a new document defaults to `frameCount = 1`, and the transport is only built when `frameCount > 1`: no Play button, no scrubber, no filmstrip, no cherry panel until the user finds the Frames dial. This alone reproduces "the transport did not move".
2. **5.3** — no Shaper dial defaults to a Curve (`ShaperPrimitives.cs:91-160` are all `new ZUIValue(x)`), so even after raising Frames, pressing Play shows sixteen identical pictures. Pyre's `alpha` and `size` curves are what make a fresh layer animate at all.
3. **4.3** — editing a dial while playing drops the whole frame cache, and playback refuses to step onto an uncached frame, so a sustained drag can pin the transport in place.
4. **2.2** — raising Frames does not widen the scrubber's range or any readout until a full window rebuild happens; `Change()` does not rebuild.
5. **8.9** — there is no `frame N/M` readout at all; the only frame display is the scrubber's own caption plus a status line that is silent while paused.
6. **8.12** — the filmstrip is a permanent row, not a toggled mode, and has no Tile-px control.
7. **8.20** — Pyre's blank loop Delay applies to a *plain* loop; Shaper's loop gap only exists under cherry framing.
8. **6.2** — the preview has no swarm overlay: no spawn outline, no spawn dots, no per-dot spawn-frame labels, so a swarm cannot be authored by eye.
9. **3.6** — deleting the last layer is not refused, so a zero-layer, unrenderable document is reachable.
10. **11.2** — no saved-Views bar or view store exists anywhere in `Editor/Shaper/`.

**Row count: 109.**
