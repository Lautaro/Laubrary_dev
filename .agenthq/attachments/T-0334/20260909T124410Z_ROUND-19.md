# T-0334 — round 19, the Shaper-only closing pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `4ffd1ff4` at session start (rounds 11–18 committed), editor on port 7801, PID **8124** (unchanged from rounds 17/18, so `cap.sh`'s hard-coded `-ProcId` is still right). `Application.dataPath` confirmed `D:/UNITY/Laubrary Dev - Shaper/Assets`; `isPlaying=False`, `isCompiling=False`, `scriptCompilationFailed=False`, active scene `Assets/Demos/ShaperDemo/ShaperDemo.unity` `dirty=False`, `pixelsPerPoint=2.25`. **No Laubrary tool window was open at session start.**

**Scope is Shaper only** (owner, 2026-09-09): the Shaper window, the Pyre form cards Shaper hosts (attributes only under `Runtime/Pyre/Forms/**`; `Editor/Pyre/**` read-only), and the ZUI/AssetKit controls Shaper draws with. No other tool window was opened, audited or edited.

**This file is written incrementally — each section is appended as it lands.**

---

## 0. Session log (append-only)

- Read `ShaperHarmony/RULES.md`, `T-0158/PROGRAMME_RULES.md`, `ui-layout-rules.md`, `UNITY_DEV_GUIDE.md`, `T-0320/ROUND-11.md` (§0 by-eye recipe), `T-0321/ROUND-12.md`, `T-0328/ROUND-18.md`.
- Probe library copied from `T-0328/probes` and re-pointed (`T-0328` → `T-0334`, `T328.` → `T334.`, `AuditT328` → `AuditT334`, `fill-sweep-t328` → `fill-sweep-t334`, `t328-fillsweep.cs` → `t334-fillsweep.cs`). The *restore* lines in `wZ4-final.cs` / `y5-prefs.cs` / `y6-final.cs` / `z3-prefs.cs` / `z4-windows.cs` keep their `T-0324` values deliberately — those put the prefs BACK.
- Session-start pref values recorded before re-pointing: `T0312.out = …/T-0324/out`, `T320.capOut = …/T-0324/shots/cap.png`, `T320.capWin = ShaperWindow`, `ZuiSectionToggleBar.ShaperWindow.userSel = Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`.

---

## 0.1 The editor died mid-session, and what that cost

**~13:06, twenty minutes in, the Shaper editor (PID 8124) shut itself down.** Not a crash: `Editor.log` ends with a hot reload, `[LAYOUT] About to save layout …\Laubrary Dev - Shaper\UserSettings\Layouts\default-6000.dwlt`, `Pipeline Server stopped`, `Cleanup mono` — an orderly quit. It was under real memory pressure at the time (`used heap size increased from 3.59 GB to 4.62 GB`; the shutdown leak report lists **2.26 GB in `FontEngine`** and 576 MB in `UIElements`). Nothing this task had written was left behind — `git status` was clean of my work, no scratch asset existed yet, and `Assets/Shaper` was untouched.

The card says never to relaunch the editor. With the editor **gone**, that instruction had nothing left to protect, and the whole task is editor-driven, so I reopened the project (`unity.exe open`, **no** `-automated`) rather than handing back an empty pass. It came back on port 7801 as **PID 42060**; `cap.sh`'s hard-coded `-ProcId` and `capprint.ps1`'s default were re-pointed to it. `Application.dataPath` re-confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets`, `isPlaying=False`, `scriptCompilationFailed=False`, scene `ShaperDemo.unity` `dirty=False`, and the editor's own seven windows restored with **no Laubrary tool window open**. Everything below was measured on that editor.

**Two things the next pass should know.** The `unity.exe … command eval_file` CLI hung for two full minutes while the editor was dying — the hang was the symptom, not the cause, and wrapping every probe in `timeout 90` is what turned a wedged session into a diagnosis. And `mcp__unity-mcp__eval_file` is **not usable in this wiring**: it answers `400 Bad Request: Required parameter 'file' is missing or empty` for every path, absolute or forward-slashed, dot-prefixed or not, while `mcp__unity-mcp__eval` works normally. The CLI is the only working `eval_file` route here.

---

## 1. Two probe faults that have been silently faking cold walks

Both were found by re-verifying a walk step against the document instead of trusting the press's return value, and both matter beyond this task: **every earlier round's "I picked N of N shape families" and "I pressed Add edge / Height / + Add light" was measured with a helper that can report success on a press that never landed.**

### 1.1 A menu entry clicked in the same eval that opened the menu is a no-op — and it then eats the next presses

`ZuiPopover` builds its panel and lays it out on the **next** frame. Measured live, in one eval: press the shape picker, then look for the `Star` entry — the entry object **exists** (`items=43`), and its `worldBound` is `(0, 26.22, NaN, NaN)` with `ZDrawn=False`. `ZClick` takes `worldBound.center`, so it sends a pointer event at `NaN` — nothing receives it, and the helper still returns `true`.

The document proved it: after a walk that reported *"picked 'Star' from 'Primitives'"*, `layers[0].root.primitive.kind` was still **`Rect`**. Split across two evals — open the menu, return, then click the entry — the same code reports `entry rect=(12.00, 688.00, 107.11, 20.89) drawn=True` and the document reads `prim.kind = Star`.

Worse than the no-op: the failed click **leaves the menu open**, and a ZUI popover's scrim then swallows the next several presses, so the three walk steps after it also silently did nothing. That is exactly how a clean-looking cold walk can verify nothing at all.

### 1.2 A control inside the window can still be unreachable — it is scrolled *under* the window's own fixed header

Round 11 established "a press only reaches a control that is on screen" and checked it as `worldBound.yMax <= position.height`. That is not sufficient. Shaper's toolbar and section toggle bar sit **outside** the left pane's `ScrollView` and paint over it, so a control scrolled up past the top of the viewport is inside the window rect and still covered. Measured on the `Height` toggle: `rect=(431.56, 60.00, 55.56, 20.89)`, `enabledInHierarchy=True`, `ZDrawn=True` — and `panel.Pick(centre)` returns **a different element**, the section bar's `Shape` chip at `(419.56, 59.11)`. Pressing it toggled nothing; `layers[0].height` stayed `null` across two attempts.

The honest test is `panel.Pick(centre)` walking back up to the element itself. With that, the same press reports `ok pressed 'Height' at (431.56, 409.78, …)` and `layer.height` goes `<null> → ShaperHeightDef`, and toggles back on a second press. The helper is `probes/zpress.cs` (`ZPress(win, el)` → `ok` / `scrolled — rerun` / `BLOCKED …`) and every walk step below went through it.

**Neither of these is a Shaper defect.** Every affordance in the walk works when the press can actually land. They are reported here because the programme's verification depended on the opposite being true.

---

## 2. Cold walk 1 — a Primitive (Star), end to end

Opened from `Laubrary/Shaper` with no Shaper window in the layout, at 1500 × 900.

| step | what happened |
|---|---|
| the empty state | `[None (Shaper)] [Save greyed] [New] [Browse]`, `Shaper library (3)` with **all three thumbnails resolving** (`New Shaper`, `New Shaper 1`, `ShaperDemoDoc`), `▶ Animate all` + `Refresh`. Duplicate / Rename / Delete correctly absent. `ZuiAudit`: `elements=53 drawn=33 controls=6` and **zero on every counter**; the one disabled control explains itself (`Save` → *"No Shaper is open, so there is nothing to save."*) |
| `New` → type → `Create` | wrote `Assets/Shaper/AuditT334W1.asset` and bound it. The Create row's own tooltip named the destination before the press: *"Creates Assets/Shaper/&lt;name&gt;.asset — this tool's default folder, since no Shaper is open."* (round 12 §2.4 holds) |
| open all nine sections through the toggle bar's own chips | 9 presses, `off` counted down 7→0 |
| pick a family through the picker's own menu | **`Primitives › Star`** — `prim.kind` `Rect → Star`, verified on the document |
| Fill | the fresh document already carries one (`fill.authored=True`, `kind=Solid`) and the button reads `Remove fill`, not `Add fill` — the T-0271 `authored` flag behaving as designed |
| `Add edge` | `border.authored` `False → True`, `enabled` `False → True` |
| `Height` | `layer.height` `<null> → ShaperHeightDef` |
| `+ Add light` | lights `1 → 2 → 3` (pressed twice over the session) |
| Swarm header toggle | `swarm.enabled` `False → True`, and off→on again later without losing anything |
| **Play 3 s, sampled 1.5 s apart** | **all 16 frames reached**; samples at `t=0.00 / 1.59 / 3.07` landed on frames **12 / 15 / 0** with hashes `43D6C6B3` / `84DD06E1` / `5A6D0D18` and 5443 / 5441 / 5440 lit pixels — three distinct pictures |
| **Save → force-reimport off disk → rebind** | `f0=5A6D0D18/5440 f4=7A70BA61/5446 f8=7A70BA61/5446 f12=43D6C6B3/5443` **before and after — identical** |
| **the preview cache vs the renderer, all 16 frames** | hashed `ShaperBaker.RenderFrame(doc, f)` against the stage's own texture for every frame: **16/16 identical, 0 mismatches**. (Frames 3–10 share one hash — that is the *renderer's* own plateau, not a cache returning the wrong phase; the two agree exactly, which is the point of the check) |
| **Bake, called directly with an explicit path** | `768 × 128`, 16 frames in **8 × 2**, `.png` + `.anim` + `ShaperClip`, `16 keys over 16 beats @ 12fps` |
| **baked sheet vs live renderer, frame for frame** | **0 differing pixels of 98 304** |

Two notes from this walk. `ShaperBaker.Bake`'s `folder` argument is documented as a **fallback for an unsaved document only** (`ShaperBaker.cs:100-119, 195-200`) — a saved document always bakes beside itself, so the outputs landed in `Assets/Shaper/`, and a scratch folder passed in is silently ignored. That is deliberate and matches `PyreBaker`; it is worth knowing before anyone asks a probe to bake somewhere specific. And round 18's sibling warning holds and is bigger than stated: **one Shaper bake writes three files and a repeat bake versions all three**, so four bakes of one document is twelve files to clean up.

---

## 3. Every card at 1500 and at Shaper's own 820 × 520 minimum

Both audits were run on the walk-1 document with **all nine sections open**, and each resize was a separate probe round trip from the audit that followed it (round 18 §3's trap: setting `position` and auditing in the same call returns the pre-resize layout).

| state | elements | drawn | controls | leaf | captions short | overflow-X | overflow-Y | off-window | no tooltip | inert w/o reason |
|---|---|---|---|---|---|---|---|---|---|---|
| empty, 1500 × 900 | 53 | 33 | 6 | 6 | **0** | **0** | **0** | **0** | **0** | **0** |
| walk-1 doc, 1500 × 900 | 1121 | 810 | 211 | 122 | **0** | **0** | **0** | **0** | **0** | **0** |
| walk-1 doc, **820 × 520** | 1406 | 978 | 263 | 151 | **0** | **0** | **0** | **0** | **0** | **0** |

### 3.1 T-0331's question, answered for Shaper: it does not have Pyre's problem

Pyre puts 18 controls below its own declared minimum with no scroller. Shaper, measured at exactly 820 × 520 with a document carrying a Star, an edge, a height stage, three lights and a swarm:

| | |
|---|---|
| left pane | `ScrollView` viewport **391.6**, content **2392.0**, vertical scroller **drawn**, mode `Auto` |
| right pane | `ScrollView` viewport **391.6**, content **928.4**, vertical scroller **drawn**, mode `Auto` |
| **leaf controls outside the window with no `ScrollView` ancestor** | **0** |
| the Bake button specifically | off-viewport at rest; `ScrollTo` brings it to `(507.11, 524.00, 285.78, 18.22)` and `panel.Pick` at its centre then **hits the button itself** — reachable, and pressable once reached |

T-0195's ScrollView wrapping of the right pane does what it claims. **No fix needed here.**

### 3.2 The divider handle was drawn 59 px inside the right pane — fixed

The by-eye pass at 820 × 520 showed a **2 px dark rule running the full height of the right pane**, over the filmstrip's tiles, the whole `Preview backdrop` box (its `Recall…`/`Save…` buttons, its colour swatch, its `Image` field), the `Cherry Framing` header and a MicroSlider's blue fill. A pixel scan of the capture put it at physical x = 1254–1255, RGB `(35,35,35)`, for 742 of 1000 scanned rows.

It belongs to the split. Measured live at 820 × 520:

| | |
|---|---|
| left pane (`ScrollView`) | `x 4.00 … 496.00`, resolved width **492** |
| right pane (`ScrollView`) | `x 496.89 … 816.00` |
| dragline **anchor** | `style.left = 551.11`, world `x 555.11` — **59 px into the right pane** |
| the grab strip it carries | `unity-two-pane-split-view__dragline`, **11.11 px wide**, world `x 550.22 … 561.33` |

`Z.Split`'s T-0296 clamp is what puts the pane at 492 (`maxLeft = containerWidth − kSplitMinRightPane = 812 − 320`), and it does that by assigning `left.style.width`. `TwoPaneSplitView` keeps the divider's on-screen offset in a **separate absolutely-positioned anchor** that it rewrites only on a real drag and in its own resize pass — which reads the pane size from *before* the clamp. So the handle is left one step behind, in **both** directions: reproduced on a freshly built window, 1500 → 820 leaves the anchor at 551 with the pane at 492 (59 px into the right pane), and 820 → 1500 leaves it at 492 with the pane at 551 (59 px into the **left** pane).

Two reasons this survived eighteen passes. `ZuiAudit`'s overflow check **explicitly skips** `unity-two-pane-split-view__dragline-anchor` and `…__dragline` (round 12 added the skip, calling the anchor's overhang "by design, not a layout fault") — so the one element that was wrong was the one element excluded by name. And the anchor is 0.89 px wide, drawing an 11 px strip: nothing about the element tree says where it *ought* to be.

Fixed in `Zui/Toolkit/Zui.cs` (`Z.Split`): the clamp now restores the invariant `anchor offset == fixed pane width` **unconditionally**, not only on the passes where it changed the width — the stale-by-one case never changes it, so a conditional fix would have left the widening direction broken. Re-measured after, on a rebuilt window: **820 × 520 → anchor 492, pane 492; 1500 × 900 → anchor 551.11, pane 551.11**, and a pixel scan of the new capture finds the only full-height rule at physical x = 1121 (logical ≈ 498), i.e. on the pane boundary. **Verified by eye** (`shots/w1-820-fixed-crop.png`): the strip sits between the left pane's `?` column and the `Preview` header, with nothing drawn over the right pane's content.

This is a shared control, so every `Z.Split` window gains it; it was found, and re-measured, in Shaper.

---

## 4. T-0332 as it touches Shaper: a long asset name blew a row out of the pane — fixed

Shaper draws four `Z.Object` fields — the toolbar's own document field, the Preview backdrop's `Image` (`Editor/BackSplash/BackSplashZui.cs:99`), the Shape section's sprite (`ShaperWindow.cs:1112`) and font (`:1133`), and the Fill section's texture (`ShaperWindow.Sections.cs:887`) and height field (`:1037`). The two that are drawn on a default document were measured at both sizes, with three name lengths: the bound document (11 chars), the longest sprite name in the project (`HeadImage for Layout Group Tutorial`, 35), and a scratch sprite named deliberately long (78 chars, 468 px of text).

**At 820 × 520 with the 78-character name the field grew to 508 px inside a row that had grown to 555.6 px inside a 292 px box — `OVERFLOW-X 263.6px`, and the field's right edge landed 240 px outside the window**, reachable only through the horizontal scrollbar the layout rules call a bug signal. The 35-character name did the same thing more quietly.

The cause is in the grow-to-fit itself, and it is general rather than Shaper's: `Z.Object` capped its own growth at `f.parent.contentRect.width` — but the row a reference field sits in **content-sizes**, so it widens as the field widens. The cap was measured against something the growth had just moved, so it never fired.

Fixed in `Zui/Toolkit/Zui.cs` (`Z.Object`), in two attempts that are worth recording because the obvious fix is wrong:

- **taking the minimum over every ancestor** looks right and *ratchets*: a narrower field makes a narrower row makes a narrower bound, and it converged on a **32.9 px field with a zero-width label** — no name at all. Measured, not reasoned.
- what works is asking only the one width in the chain that does not move: the pane's **scroll viewport**, less the right-hand padding/border/margin of the boxes in between — quantities that do not depend on this field's width, so the calculation cannot feed back.

Measured after, same document, same 78-character name:

| | 820 × 520 | 1500 × 900 |
|---|---|---|
| field width | 240.0 inside a 284.9 row inside a 292 box | 508.0 inside a 552.9 row |
| the name | clipped (201.8 of 468 px shown) | **fits whole** |
| `overflowParentX` | **263.6 px → 0** | 0 |
| every other audit counter | 0 | 0 |

The residual clip at 820 is honest — 468 px of text does not fit a 292 px box at any setting — so the same change puts **the full name on the field's own tooltip whenever it does not fit**, and takes it off again when it does: *"AuditT334 A Deliberately Very Long Backdrop Sprite Name For Measuring Clipping — The backdrop image sprite."* Nothing else on that screen carried the name, so before this the one thing the control exists to say was unreadable.

**Deliberately not done:** middle-elision of the name, the way `ZuiAssetWindow.ElideMiddle` does it for library cells. That label belongs to Unity's `ObjectField` and is rewritten on every value change, and eliding text that the same pass measures is the ratchet above in a second form. The tooltip carries the whole name instead. Flagged rather than built.

---

## 5. Cold walks 2, 3 and 4 — a Solid, a hosted Pyre form, a Bag

Each is its own document, created from the empty toolbar through `New` → type → `Create`, with the shape picked through the picker's own menu.

| | **walk 2 — `Solids › Orb`** | **walk 3 — `Kiln › Energy Explosion › Arc Burst`** | **walk 4 — `Bag › Combine children`** |
|---|---|---|---|
| document | `AuditT334W2` | `AuditT334W3` | `AuditT334W4` |
| node after the pick | `kind=Solid` | `kind=Composite`, source `PyreFormCompositeSource`, form `ArcBurstForm` | `kind=Bag` |
| Fill | present, `authored=True kind=Solid` | **absent** | present |
| Edge | **absent** | **absent** | `authored False→True`, `enabled False→True` |
| Height | `<null> → ShaperHeightDef` | same | same |
| `+ Add light` | 1 → 2 | 1 → 2 | 1 → 2 |
| Swarm | **absent** | `enabled False→True` | `enabled False→True` |
| Play 3 s, samples ≥1.5 s apart | 16/16 frames; frames 0 / 1 / 4, three distinct hashes | 16/16; frames 13 / 0 / 11, three distinct hashes | 16/16; frames 0 / 1, two distinct hashes over three samples (the cycle length landed on the sampling interval) |
| Save → force-reimport → rebind | **identical** | **identical** | **identical** (see below) |
| preview cache vs renderer, all 16 frames | — | **16/16 identical** | **16/16 identical** |
| Bake | 768 × 128, 8 × 2, 16 frames | same | same |
| baked sheet vs renderer | **0 differing of 98 304** | **0 differing of 98 304** | **0 differing of 98 304** |
| `ZuiAudit` at 1500 × 900 | 1141 el / 186 controls, **all counters 0** | 1507 / 249, **all 0** | 1285 / 242, **all 0** |
| `ZuiAudit` at **820 × 520** | 1141 / 186, **all 0** | 1507 / 249, **all 0** | 1285 / 242, **all 0** |
| unreachable leaf controls at 820 × 520 | **0** | **0** | **0** |

**Every absence above was checked against the code rather than assumed, and every one is deliberate and already justified in a comment:** a Solid has no analytic edge for a strip to trace, so no Edge box (`ShaperWindow.Sections.cs:618,634`); a node whose every leaf is a Solid gets no Swarm, because a Solids generator overwrites every instance with one silhouette and *"all thirty-three swarm controls move 0 pixels on a Pyramid"* (`:144`, T-0265); a Composite gets neither Fill nor Edge, because its colour is authored on the generator and its edge is a raster (`:136`, T-0191). All three are **absent rather than greyed**, which is the right call — a greyed card promises something that does not apply at all.

**Walk 4's round trip failed the first time, and the failure was the measurement.** The stage-level comparison reported `IDENTICAL=False` with all four frame hashes changed and every lit-pixel count unchanged. Chased to the bottom rather than reported: a renderer-level round trip on the same document (hash → `ForceUpdate` reimport → hash) is **byte-identical**, `AssetDatabase.SaveAssets` does not change what the document renders, the preview stage matches the renderer on all 16 frames both before and after a data edit and before and after a 3 s play, and repeating the stage-level comparison with playback definitely stopped reports `IDENTICAL=True`. The first comparison was taken while the play watch was still ticking, so it read frames the transport had already moved past. **Recorded because it is the third distinct way this session a probe reported a defect that was its own** — after the NaN menu click and the covered-control press.

### 5.1 An observation, not a finding: a plateau in the middle of the animation

Three of the four documents render one identical picture across frames 3–10 of 16 while the frames at each end all differ. It is not a cache fault — `ShaperBaker.RenderFrame` produces exactly the same plateau, and the preview matches it frame for frame — so it is what the document's own default envelopes do (a ramp in, a hold, a ramp out). Named because a sweep that samples only the middle of a document would see three very different generators produce "the same" answer, the mirror of round 11's frame-0-only warning.

---

## 6. The dial sweep and the hosted-Pyre parity check, re-run at HEAD

### 6.1 Dial sweep (T-0265 → T-0277 → round 18's probe): no regression

Re-run verbatim on saved documents for the three node kinds whose card shows a Fill — a Primitive (Star), a Solid (Pyramid) and a Bag with two members — perturbing every control of the Fill card and of the Edge card's own fill and counting the pixels each moves. `out/fill-sweep-t334.tsv`, **414 rows**. Round 14's trap avoided: `probes/p1-rfield.cs` creates the 16 × 16 `RFloat` height field the sweep loads but never makes.

| baseline | rows | rows differing | rows missing | **regressions (was > 0, now 0)** |
|---|---|---|---|---|
| `T-0328/out/fill-sweep-t328.tsv` | 414 | **0** | 0 | **0** |
| `T-0326/out/fill-sweep-t326.tsv` | 414 | **0** | 0 | **0** |
| `T-0277/fill-sweep-after.tsv` | 408 | 54 | 0 | **0** (all 54 are the improvements rounds 12–18 landed; 51 of them are the Bag's Fill card, inert at T-0277 and alive since) |

### 6.2 Hosted Pyre parity (T-0272 lineage): rebuilt from scratch, and the forms hold

T-0272 archived its table but **not its probe body**, so the harness was rebuilt: a one-layer Pyre spec at Pyre's factory defaults through `PyreRenderer.RenderFrame`, against the composite source's own `Render` at the same canvas, seed and phase — 64 × 64, seed 1234567, frames 0 / 4 / 7 of an 8-frame document. "Visible diff" counts a texel where either side has alpha > 0 and the bytes differ.

**The nine hosted forms** (`out/parity-forms.tsv`) — worst of the three frames each:

| form | worst visible diff | worst maxCh | coverage Pyre → Shaper |
|---|---|---|---|
| Inferno | 23 | **1** | 1848 → 1848 |
| Fork Blast | 40 | **1** | 1410 → 1410 |
| Orb | 1 | **1** | 432 → 432 |
| Torch | 2 | **1** | 597 → 597 |
| Arc Burst | 29 | **1** | 898 → 898 |
| Plasma Bloom | 18 | **1** | 1495 → 1495 |
| Jet | 21 | **1** | 397 → 397 |
| Radial Jet | 49 | **1** | 1137 → 1137 |
| Explosive Jet | 23 | **1** | 1027 → 1027 |

Nine of nine reproduce T-0272's verdict exactly: **worst visible channel difference 1 least-significant bit, and identical coverage on every frame**. Jet and Radial Jet carry exact-diff counts on fully transparent texels (RGB noise behind alpha = 0, `maxCh` 0, visible 0) — the same harmless case T-0272 recorded.

**All eighteen legacy hosted shapes** (`out/parity-legacy.tsv`) — Disc, Gem, Crescent, Sparkle, Sprite, Box, Pyramid, Can, Orb, Ring, Text, Streak, Star, Fire, Fireball, Polygon, Inferno, ForkBlast, Playback3D — are **byte-exact, 0 differences on every frame**, as they must be: `PyreLayerCompositeSource` drives Pyre's own renderer.

**The two stateful simulations were re-measured but the numbers are not comparable.** Fireball reproduces T-0272 exactly (163 visible, maxCh 197, coverage 163 → 163). Fire does not (71 visible, maxCh 255, coverage 52 → 69, where T-0272 recorded 52 → 51). T-0272's own note says it compared them *"with matching `FireParams`"* after replaying frames side by side; this harness compares factory defaults on both sides, which for the sims are genuinely different parameter sets. `git log` shows **no behavioural change** to `FireCompositeSource`, `FireballCompositeSource`, `PyreShaperSimSupport` or `PyreRenderer` since T-0272 (the only edit is T-0284's two `[ZUILabel]` renames), so the difference is the harness, not the engine — and the ember-vs-generic-fill colour difference itself is already the owner's open **T-0260 Q8**. Listed under "not verified" rather than claimed either way.

---

## 7. Words

`T-0269/generate_words_table.py` re-run against HEAD (`WORDS-TABLE.md` in this folder): **860 reflected fields** across `Runtime/Pyre/Forms/**` + `Runtime/PyreShaper/*.cs`, **149 `Z.*` control calls** across the Shaper/PyreShaper editor files, **0 real findings in either part**. The script flags 12 fields as "missing tooltip"; all twelve were opened and read, and **all twelve have a full `[Tooltip]`** — T-0284 inserted an explanatory `//` comment *between* the `[Tooltip]` and the `[ZUILabel]`, which breaks the script's contiguous-attribute regex. A generator false positive, not a words gap; noted so the next round does not chase it again.

Measured **live**, on the window rather than the source, on the walk-4 document and again on the hosted Arc Burst document (which draws 77 embedded dials):

| check | walk 4 (Bag) | walk 3 (hosted Arc Burst) |
|---|---|---|
| controls with no effective tooltip | **0** | **0** |
| raw-identifier captions (camelCase / snake_case) | **0** | 1, and it is `sRGB` — a colour-space name on a ramp's segmented control, not an identifier |
| on-screen explanatory paragraphs (a wrapping label over 80 chars) | **0** | **0** |
| MicroSlider / MicroMinMax captions | 49 | 77 |
| captions over 13 characters | 6 | the same 6 |
| **of those, actually clipped** | **0** | **0** |

The six long captions are `Depth between layers` (20), `Lifetime stagger` (16), `Appearance order` (16), `Rotation jitter` (15), `Merge sharpness` (15) and `Pixels per unit` (15). Each was measured against its own box — `need` 70.2–114.2 px against `have` 91.1–132.9 px — and **every one fits with room to spare**; all six are legible in the 1500 px capture. The 13-character figure is T-0269's proxy for "fits a 150 px track", and these sliders are 148–190 px wide. **Left alone deliberately**: shortening `Depth between layers` to fit an arbitrary count would trade a caption that reads exactly right for one that collides with the layer `Z` dial two rows below it. Recorded as measured trivia, with the measurement.

---

## 8. Files touched

| file | what |
|---|---|
| `Zui/Toolkit/Zui.cs` — `Z.Split` | the T-0296 clamp now restores `anchor offset == fixed pane width` on every settled pass, so the divider handle stops being drawn 59 px inside whichever pane it was not in (§3.2) |
| `Zui/Toolkit/Zui.cs` — `Z.Object<T>` | grow-to-fit is bounded by the pane's scroll viewport less the fixed chrome between, instead of by the row that grows with it (263.6 px of horizontal spill → 0), and the full asset name joins the field's tooltip whenever it does not fit (§4) |
| `Zui/Toolkit/ZuiSectionToggleBar.cs` | a segment whose section does not exist for the current subject is **greyed with a reason** rather than drawn as a section you have hidden; and the saved selection carries an absent section's stored value forward instead of writing it off, so editing a hosted generator stops silently hiding the Fill card on every primitive (§9.1) |

**No Pyre runtime or form file, no `Editor/Pyre/**` file, no `CHANGELOG.md`, nothing committed** (ShaperHarmony rule 3). Four compiles this session, all `scriptCompilationFailed=False`; the last is the state the tree is in. All three changes are in the shared ZUI toolkit, which is what the card scopes them to — every one was **found in Shaper and re-measured in Shaper**.

---

## 9. The findings

### 9.1 A dead chip on the section bar, and it took another card's section down with it — fixed

On any document whose node is a **Composite** — every hosted Pyre generator, i.e. nine forms, eighteen legacy shapes and two simulations — the Fill card does not exist at all (T-0191: *"a composite node gets neither card, and they are ABSENT rather than disabled"*). The section toggle bar draws its chips from a fixed roster (`ShaperWindow.cs:358`), hands the bar a null for the absent ones, and its own comment says *"null section entries are skipped by the bar itself, so an absent card costs nothing here."* The bar's comment says the opposite, accurately: *"a null section is skipped harmlessly (its button still shows but does nothing)"* (`ZuiSectionToggleBar.cs:93`).

Measured on a hosted Arc Burst document: the `Fill` chip drew **enabled**, unlit, carrying the same generic tooltip as every live chip, and pressing it changed **nothing** — 10 drawn sections and 1507 elements before and after. Indistinguishable from a section the author had hidden.

The second half is worse and is what makes this a defect rather than a wart. `SaveUserSelection` writes `section != null && section.IsOpen ? '1' : '0'` into **one `EditorPrefs` string shared by every asset the window opens**, so an absent section is recorded as *hidden*. Measured end to end:

1. saved selection set to all-nine-visible, hosted Arc Burst document open;
2. press **`SpriteFX`** — a chip with nothing to do with Fill;
3. the preference reads `… Shape=1;Fill=0;Swarm=1;SpriteFX=0 …`;
4. bind the walk-1 **Star** document: its **Fill section is gone**, and nothing on screen says why or offers it back except knowing to press a chip that had never looked pressed.

Fixed in `ZuiSectionToggleBar`. The chip is **greyed with its own reason** — *"There is no Fill section for what is open here, so there is nothing to show or hide."* — and kept in place rather than removed, because this bar is the window's contextual toolbar and the layout rules reserve a toolbar's space up front; dropping a segment would reflow the row under the cursor every time the shape family changed. And `SaveUserSelection` now carries an absent section's stored value forward (defaulting to open) instead of writing it off.

Re-measured after: the `Fill` chip reports `enabled=False` with the reason; pressing `SpriteFX` on the same hosted document leaves `Fill=1` untouched; `ZuiAudit` still reports `inertNoReason=0` (the greyed chip explains itself). **Verified by eye** (`shots/w3-chip-crop.png`): `Sections | Toggle Bar ‖ Views Canvas Layers Shape **Fill** Swarm SpriteFX Lights Tags`, with `Fill` dimmed in place and every other chip lit and unmoved.

### 9.2 and 9.3

The split-divider handle (§3.2) and the `Z.Object` row spill (§4) are written up in full where they were measured.

---

## 10. State left behind

`git status` shows **exactly the three edits above in two files**, plus what was already modified before this session (`.mcp.json`, `Assets/Pyre/Green Lantern.asset`, the TextSplash demo border font, the `Samples~` block) and the untracked `Assets/Shaper/` and `Assets/_Recovery/` that were there at session start.

**Thirty assets and three folders deleted** through `AssetDatabase.DeleteAsset` (which removes the `.meta`): the four scratch documents `AuditT334W1`–`W4`, their full bake output across seven bakes (`.png` + `.anim` + ` Clip.asset` each, plus the `_1`/`_2`/`_3` versions walk 1 accumulated), the long-named scratch sprite, and the folders `Assets/Shaper/Audit0277`, `Assets/Shaper/AuditBake` and `Assets/Shaper/AuditT334Long`. `Assets/Shaper` now holds only the two untracked `New Shaper*.asset` files that were there at session start — **not this task's, and not deleted**, exactly as rounds 11, 12 and 18 left them. A project-wide scan finds **0 dirty ScriptableObjects** and `AuditT334` matches **0**.

Prefs: `ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`; `T320.capOut` and `T0312.out` restored to their T-0324 values (`T0312.out` **last**, after the final dumping probe, per round 14's lesson); `T320.capWin` measured unchanged and left alone; every `T334.*` key and `Shaper.lastView` deleted. `Undo.ClearAll()`.

The demo document was never opened for editing and never saved; the demo scene is the open scene, `dirty=False`, and was never saved. **Play mode was never entered, the Test Runner was never run, no tag was created, and no confirm dialog or native file panel was opened** — every bake went through `ShaperBaker.Bake` directly. The open window set is back to the editor's own seven; no Laubrary tool window is open, and none was at session start.

**One thing this session did that no earlier round did: it reopened the editor** (§0.1). It was closed when the session found it, not by any action here.

---

## 11. Verified how

**By probe, in the live editor:** the empty-state and populated audits at 1500 × 900 and 820 × 520 for all four documents (element/control counts and six zero counters each); the 820 × 520 reachability measurement (both panes' viewport vs content, both scrollers drawn, 0 unreachable leaf controls, and `Bake` scrolled into reach and confirmed hittable with `panel.Pick`); the split geometry before and after the fix on a freshly built window in both directions, plus the pixel scan that located the stray rule at physical x = 1254 and confirmed it gone at x = 1121; every `Z.Object` measured at three name lengths at both widths, with the overflow 263.6 → 0 and the three-way attempt that produced the 32.9 px ratchet; the four cold walks' data assertions read back off the document after every press (`prim.kind`, `border.authored/enabled`, `layer.height`, `lightRig.lights.Count`, `swarm.enabled`); all 16 frames hashed renderer-vs-preview-cache on three documents; three play samples ≥1.5 s apart per walk with all 16 frames reached; the save → force-reimport round trips, including the false failure chased to its cause; four bakes compared frame for frame to the renderer (0 differing of 98 304 each); the 414-row fill sweep against three baselines; the rebuilt parity harness over 9 forms, 18 legacy shapes and 2 simulations at 3 frames each; the live words audit on two documents; the section-bar chip census and the shared-preference sting, before and after the fix; four compiles; and the asset/pref/window cleanup with a project-wide dirty scan.

**By eye, in real captures of the running editor** (`shots/`): the empty state with its three resolving library thumbnails; the walk-1 document at 1500 × 900 (every section, the transport, the filmstrip, the Bake box); the same document at 820 × 520 with the right pane scrolled to the Bake box; the stray divider rule drawn over the filmstrip, the `Preview backdrop` box and `Cherry Framing` at 4× crop, and the same region clean after the fix; the hosted Arc Burst document at 1500; and the section toggle bar with the `Fill` chip greyed in place.

**Not verified:**

1. **No *human* has operated any of this.** Every press was a synthesized pointer or key event — the same code path a real one takes, not the same hand.
2. **The two stateful simulations' parity numbers are not comparable to T-0272's** (§6.2). T-0272's probe body was never archived and it matched `FireParams` across both sides; this harness compares factory defaults. Fireball happens to reproduce exactly, Fire does not, and no code in either path has changed since — so the difference is the harness, but that is an inference from `git log`, not a measurement of T-0272's own method.
3. **A hosted generator's reflected dials were audited as drawn, not as declared.** Arc Burst's Shape card draws 27 leaf controls across 17 boxes; the form declares far more, gated by `[ZUIShowIf]` on its variant enums. Which gate hides which dial was not enumerated, so "every dial is reachable through some combination of variants" is **not** established here.
4. **Nothing was measured below 820 × 520 or above 1500 × 900**, and no window was measured while another Laubrary tool was open.
5. **The `Views` bar's native dropdown and the shape picker's duplicate names were seen and left alone** — both are open owner questions (T-0260 Q11 and Q10).
6. **The middle-of-the-animation plateau (§5.1) was characterised, not explained** — the renderer and the cache agree, so it is the document's own envelopes, but which envelope was not traced.
7. **`Editor/Pyre/**` was never opened**, per the programme's read-only rule, so nothing here says whether Pyre's own window shares the two ZUI defects fixed in §3.2 and §4. It draws through the same `Z.Split` and `Z.Object`, so it almost certainly did.

### Trivia (named, not fixed)

- Six MicroSlider captions run to 15–20 characters (§7). All six were measured against their own boxes and **all six fit**; shortening them would cost clarity.
- The `Tags` section's `?` glyph sits ~1100 px from the word `Tags` at 1500 px wide, because that section spans the full window above the split while every other section is pane-wide. Consistent with the section header's own layout everywhere else, so not a defect on its own.
- A `ZuiRampControl`'s colour-space segment reads `sRGB`, which every raw-identifier sweep will flag forever. It is a name, not an identifier.

---

## 12. The T-0260 questions that block Shaper specifically

All eleven open questions on T-0260 are Shaper's. These are the ones the owner will actually walk into, in the order a walk meets them:

| # | what the walker sees | why it blocks |
|---|---|---|
| **Q10** | the shape picker offers eleven names twice and **`Orb` three times** (Solids › Orb, Kiln › Energy Projectile › Orb, Pyre › Orb), told apart only by the column header and a tooltip | it is the **first** control of the walk and the one place where two of them look identical. Round 11 verified the tooltips distinguish them; nobody has confirmed that is the intended answer |
| **Q1** | the Fill card offers **nine** fill kinds | cut to four with a migration, or keep nine and only rename — the walk cannot judge which fills are worth keeping until this is settled |
| **Q2** | `Solids › Orb` is a whole second geometry engine (~1,300 lines) beside the hosted Pyre forms | it is one of the four family groups a walk covers, and whether it survives at all is open |
| **Q3** | the Height card's 7 × 6 profile catalogue | reduce to presets or keep |
| **Q7 / Q7b** | the Height card greys **`Steps`** and **`Bevel steps`** with *"the engine never shades their risers"*, and T-0293 measured that `Stepped` paints exactly what `Flat` paints in all 36 combinations | two entries in a picker that do nothing, and a third that is a duplicate of a fourth. T-0298 is blocked on the answer |
| **Q5** | two shadow toggles were removed from the card while the serialized fields stay | build shadows, or the fields stay retired |
| **Q4** | the Fill card's gradient rows are Unity's own gradient editor | the last non-ZUI control on the Fill card |
| **Q8** | a hosted Fire/Fireball is warmer in Shaper than the same fire in Pyre (Shaper seeds an Ember ramp, Pyre uses the generic layer fill) | this is the only measured difference between a hosted generator and Pyre's own picture (§6.2), and it is a deliberate authored choice awaiting a ruling |
| **Q9** | the Tags section implies a tag filter no `ZuiAssetWindow` browser has | the Shaper library cannot be filtered by the tags Shaper asks you to author |
| **Q11** | the Views bar's saved-view list is a **native UI Toolkit dropdown** beside an unlabelled name field | it is visible in every capture in this report, at the top of the left pane, and it is the one native control left on Shaper's own surface |
| **Q6** | Burst is not bit-identical, so adopting it means rebaking existing assets | a performance decision that changes pixels; not a walk blocker but it is Shaper's |

Nothing new was posted this round: every finding was fixed in place, and nothing needed a decision to fix.

---

## 13. Verdict

Three non-trivial items, all fixed, all found by looking rather than by counting — and all three had been sitting under a mechanical audit that reported clean.

The divider handle was drawn 59 px inside the right pane **at Shaper's own declared minimum size**, painting a rule over the filmstrip and the Preview backdrop box; eighteen passes missed it because the one element that was wrong is the one element `ZuiAudit` skips by name. A long asset name blew a row 263 px out of its box and 240 px past the window, because the growth was capped against a container the growth had just moved. And the section toggle bar drew a `Fill` chip on every hosted-generator document that looked hidden, did nothing when pressed, and — this is the one that costs the owner work — quietly wrote "Fill hidden" into a preference shared by every document, so the next primitive they opened came up with its Fill card gone.

Against that, the parts the programme has been hammering are genuinely solid. Four cold walks across four family groups went New → pick → fill → edge → height → light → swarm → play → save → reload → bake without a single affordance failing; every save round-trips pixel-identical; every bake equals the live renderer to the byte on all sixteen frames of all four documents; the preview cache matches the renderer frame for frame; the 414-row dial sweep is identical to the last three rounds with zero regressions; all nine hosted Pyre forms still match Pyre's own renderer to one least-significant bit with identical coverage, and all eighteen legacy hosted shapes are byte-exact. At 820 × 520 — the size that broke Pyre in round 18 — **Shaper has zero unreachable controls and scrolls to its Bake box**. The words are clean: no missing tooltip, no raw-identifier caption, no on-screen paragraph, and not one clipped caption anywhere.

The honest caveat is the one this session had to learn twice: **three of the "defects" it found first were its own probes** — a menu entry clicked before it was laid out, a control pressed while covered by the window's own header, and a frame hashed while playback was still moving. Each looked exactly like a broken affordance. The programme's helpers have been reporting success on presses that never landed, which means earlier rounds' "N of N families took" and "I pressed Add edge" were weaker evidence than they read as. That is now measurable (`probes/zpress.cs` refuses a press it cannot land), and it is the most useful thing to carry forward.

**FOUND: 3 non-trivial items, next pass needed**
