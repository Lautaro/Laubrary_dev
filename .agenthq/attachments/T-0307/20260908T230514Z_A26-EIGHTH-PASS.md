# A26 — eighth full pass (T-0307), 2026-09-09, Shaper worktree HEAD c4424d44 + this task's edits

Every number below was measured in the running Shaper-worktree editor. `Application.dataPath` was confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the start of every probe, after the recompile and at the end. Nothing is asserted from a doc or a previous handover.

Probes: `workspace/T-0307/probes/`. Sweeps: `pyre-sweep.txt`, `shaper-sweep.txt`, `pyre-resweep.txt`, `shaper-resweep.txt`.

**This pass found 6 things beyond trivia**, and fixed 8 pieces of trivia. Two of the six are wrong sentences in tooltips the card asked me to read against the engine, and both were corrected here because they are one-line text; four are filed as cards.

---

## 1. The Canvas card's "Depth between layers" named the wrong neighbour — FIXED

The tooltip said "a layer whose height rises more than this above **the one below it** breaks through **it**". A layer's base plane is already exactly `Depth between layers` above the one below it (`base(i) = i × layerSpacing + zOffset(i)`, `ShaperHeightCompiler.cs:218`), so rising cannot break it through downwards. Compositing is by nearest surface (`ShaperDocumentRenderer.CompositeDepth`), so what actually happens is the opposite: a layer that rises past that gap pushes through the layer **above** it.

Measured on a two-layer document, red under blue, in memory (`probes/s2-depth.cs`):

| state (spacing 4) | red px | blue px |
|---|---|---|
| flat | 0 | 468 |
| **BOTTOM layer raised 24** | **468** | **0** |
| TOP layer raised 24 | 0 | 468 |
| BOTTOM raised 24, spacing **64** | 0 | 468 |

The tooltip now says "a layer that rises more than this above its own base plane pushes through the layer ABOVE it", with the measurement in the code comment.

## 2. The Layers section header promised a stacking order the engine can override — FIXED

It read "Order is authored data — no stage reorders it, so what you see here **is** the stacking order." The same depth compositing above makes that false whenever a layer is raised or its Z pushed: the table in §1 is a layer list whose visible order is the reverse of its authored order. (The per-layer `Z` dial's own tooltip already said so; the section header contradicted it.) Now: "…and it is the stacking order wherever nothing contradicts it in depth: a layer raised, or its Z pushed, past Depth between layers comes through the one above."

## 3. The "Document seed" tooltip enumerated two consumers out of the whole compile — FIXED

It read "The seed every deterministic draw in this document derives from — **Min-Max light dials and cherry-frame picks**". The document seed is handed to the geometry compile, the fill resolver, the solids, the height stage and the effects pass (`ShaperDocumentRenderer.cs:134, :297, :315, :335, :358`), so it reaches every Min-Max dial anywhere in the document. Measured one Min-Max dial at a time, seed 1 → 777 summed over frames 0/4/8/12 (`probes/s3-seed2.cs`):

| the one dial set to Min-Max | pixels moved |
|---|---|
| shape: Rect half-extents 8..40 | **8832** |
| layer response: Intensity × 0.2..3 | **5716** |
| fill: Veil 0.1..1 | **5536** |
| transform: Translate X −20..20 | **3642** |
| a light's own Intensity 0.2..3 | 0 on this document — but the *dial* does follow the seed (`ShaperValue.Sample` returns 2.0093 / 2.5966 / 0.9128 at seeds 1 / 777 / 999, `probes/s4-lightseed.cs`); it moved no pixels only because that document's shading did not resolve the change |

The tooltip now names every Min-Max dial (shape, transform, fill, lighting) plus the cherry-frame picks.

## 4. Twelve user-facing tooltips cited internal design-doc codes and source line numbers — FIXED

`LR-1.1 … LR-4.4`, `B9's authorable range`, `(ShaperLightRig.cs:121-131)`, `(LR-4.4, ShaperLightRig.cs:26-36)` were printed **inside the tooltip an author hovers**, not in the comments beside them. Ten in `ShaperWindow.Lights.cs`, two on the Canvas card. All twelve now read as plain sentences; every code stayed where it belongs, in the file's own `//` and `///` comments (3 remain in Lights.cs, all comments).

Verified live after the recompile: **0 of 468 tooltips in the Shaper window carry `LR-n`, `HS-n`, `BC-n`, `B9'`, `Wn.n` or a `.cs:line`** (`probes/c5-tips.cs`).

## 5. "Appearance order" — the caption T-0305 added is drawn clipped — FIXED

The dial T-0305 moved into all three timing branches needs **93.8 px** of caption and a default 140 px `Dial` reserves **83.1 px**, so it was painted as "Appearance orde…". Given `width: 156f`, the idiom the Canvas card's own "Depth between layers" already uses. "Lifetime stagger" (84.9 vs 83.1) got `width: 148f`. Both are gone from the truncation sweep after the fix.

## 6. The unsaved-edits dot was drawn clipped in EVERY asset window — FIXED

`ZuiAssetWindow`'s `●` is a 12 px slot, but the default Label padding at this UI scale (0.89 left + 2.22 right) leaves **8.89 px of content for an 11.11 px glyph** (`probes/c4-dot.cs`). Measured in both Shaper and Pyre.

Fixed by zeroing the label's horizontal padding rather than widening the slot, because the toolbar row is `NoWrap` and has only **10.2 px of slack at the declared 820 px minimum window** (`probes/c3-toolbar-read.cs`) — buying width there would push `Delete` off the edge. Gone from the truncation sweep in both windows afterwards.

---

## Filed, not fixed

- **The asset toolbar's ObjectField truncates the asset name.** `AuditT0307 (Shaper Document)` needs 180.4 px and the 200 px field gives its label 161.8 px. It cannot be widened where it sits: at the declared 820 px minimum the toolbar's rightmost child ends 10.2 px from the window edge and the row does not wrap. Needs a real decision (wrap the row, shorten the display to the asset name, or let the field grow and re-clamp the rest), not a bigger number.
- **The Views bar has no rename.** Save as / Apply / Update / Delete view / a name field — renaming a view means Save-as under the new name then Delete view on the old one, a two-step the author has to invent.
- **Pyre's `★ Library` gradient button clips its glyph** (needs 12.9 px, has 8.0). Inside `ZuiGradientControl`, shared by every gradient in every tool. Four more captions are short by 0.9–1.3 px (`Position jitter`, `Shape`, the layer-row `●` select button in Shaper; `Life (frames)`, `Ember size`, `Colour ramp` in Pyre) — below the 1.5 px device-pixel tolerance T-0304 established, listed for completeness.
- **The CHANGELOG `[Unreleased]` block** — see §"CHANGELOG audit" below.
- **Every archived probe's tree walk misses elements.** See §"Archive notes".

---

## The T-0304 layout sweep, re-verified in both windows

Pane width set, window rebuilt at that width, then pure idle, then `LogEntries.GetCountsByType` after a `Clear()`.

| window | widths | step | idle | errors |
|---|---|---|---|---|
| **Pyre** (bound to `Assets/Pyre/New Pyre Plus.asset`, window 1700×900) | 360 → 1400 | 10 px, **105 widths** | 2 s each | **0 at every width** |
| **Shaper** (scratch document with a Circle swarm, every section switched on, window 1700×900) | 360 → 1400 | 10 px, **105 widths** | 2 s each | **0 at every width** |

The pre-fix band T-0304 measured (Pyre panes 540–620, 340–580 errors per 4–5 s) is silent. After this task's own edits the sweep was run again — Pyre 500→700 step 20 and Shaper 360→1400 step 80, 3 s idle each: **0 errors at every width in both** (`pyre-resweep.txt`, `shaper-resweep.txt`), so the two widened dials reintroduced nothing.

## T-0305, re-read in the live tree

Swarm on, count 8, read out of the window after a real `Rebuild()` in each state (`probes/t5-swarm.cs`):

| shape | timing | drawn | enabled | tooltip |
|---|---|---|---|---|
| None | Stagger / Window / FrameStep | yes | **no** (greyed) | "Does nothing without a spawn shape: every instance sits on the same spot…" |
| Circle | Stagger / Window / FrameStep | yes | yes | "0 brings in neighbouring positions one after another; 1 reveals them in a scrambled order." |

Geometry re-read in a **second** call (a same-call read returns pre-layout values): the caption sits at x 1105–1191 inside its `ZuiBox` at x 847–1263, y 576–589 inside 502–605 — inside its box.

## Views, end to end

Bar: `Views` | picker | **Apply** | **Update** | **Delete view** | name field | **Save as**. No rename.

| gesture | fold pattern before → after | store |
|---|---|---|
| hand-rearrange (every card) | 1111111111 → 0000000000 | — |
| **Save as** "T0307View" | — | preset added, 10 entries, asset not left dirty |
| hand-rearrange | 0000000000 → 1111111111 | — |
| **Apply** | 1111111111 → **0000000000** | restored what was saved ✔ |
| **Update** at 1111111111 | — | preset overwritten |
| hand-rearrange → **Apply** | 0000000000 → **1111111111** | restored the update ✔ |
| left all-closed, **domain reload** | → **11111111111** (all 11 open) | the view was re-applied on rebuild; picker on `T0307View`, `Shaper.lastView` intact ✔ |
| **Delete view** | — | preset removed, picker fell back to the other view, `Shaper.lastView` cleared ✔ |

The domain-reload row is the discriminating one: each `Z.BoxKeyed` persists its own fold in EditorPrefs, so the window came back all-closed and it was `RestoreLast()` that opened it.

## The demo document, opened cold and played 10 s under console watch

`Assets/Demos/ShaperDemo/ShaperDemoDoc.asset` (16 frames, 12 fps, 2 layers), bound through the window's own `SetAsset`, console cleared, `▶ Play` pressed as a user presses it, sampled off `EditorApplication.update` every 0.37 s (`probes/d1`, `d2`):

frames **2, 4, 7, 11, 14, 2, 7, 11, 0, 4, 9, 13, 0, 5, 9, 14, 2, 7, 11, 0, 4, 8, 13, 1, 6, 10, 15** over 10.88 s — ~4.3 frames per sample, forward, wrapping, never paused.

**Console over that window: 0 errors, 0 warnings, 0 logs.** The demo asset was `dirty=False` before, after binding and after 10 s of playback; nothing was written to it.

(The first attempt showed playback stopping at 6.4 s — that was my own polling probe, which presses the transport button. Re-run with a read-only peek; the numbers above are the clean run.)

## Two cold walks, with temporal samples

Walk 1 (cold), then a forced domain reload, then walk 2 (cold again). Identical in every respect:

| | walk 1 | walk 2 (after a domain reload) |
|---|---|---|
| empty state | 5 buttons, `Save` greyed, **0 without a tooltip** | identical |
| New → Create | `AuditA25a`, 1 layer, 16 frames, 12 fps, 96×64, **504 lit at frame 0**, `dirty=False` | `AuditA25b`, identical |
| play samples (0.37 s) | f4 f8 f13 f2 f3 f8 f12 f1 f5 f10 f14 f2 | f4 f8 f13 f2 f6 f10 f15 f3 f8 f12 f1 f5 |
| stop | `❚❚ Pause` → `▶ Play`, `playing=False` | identical |
| 16 frames | 0 blank, 16 distinct, lit 504 704 864 1120 1320 1536 1872 2128 2124 1944 1944 1872 1768 1700 1700 1536 | identical |
| doc dirty after play | False | False |

## Every archived probe, re-run unchanged

| probe | result | verdict |
|---|---|---|
| T-0271 `roundtrip-probe.cs` | **ROWS 33, DIFFERING 0** | reproduces |
| T-0283 `fill-state-probe.cs` | 36 rows, heightField four = **246 / 0 / 310 / 0** | reproduces |
| T-0278 `t0278_saved_probe.cs` | steps 4→32 **0**, bevel 3→16 **0**, Stepped→Dome **9163** (Rect) / **2753** (Star), all three frames of both | reproduces exactly |
| T-0267 `t0267_height` / `_sweep` / `_bagmember` | **3911** / **1042** / **1536 of 6144** | reproduce |
| T-0284 `solid-position-probe.cs` | Scale Y Box **1438** / Pyramid **1233** / Can **1528**; Orb, Gem, Ring **0**; Ring rotation **0** | reproduces exactly |
| T-0284 `mask-three-layer-probe.cs` | 2992 → **1304**, invert **1800**, frames 1304/1375/1814/1998/2065/2031/1945/1304, saved+reloaded **1304** | reproduces exactly |
| T-0284 `t0285-duplicate-sentinel-probe.cs` | `fileUnchanged=True, mtimeUnchanged=True` | reproduces (see archive note 2) |
| T-0284 `t0285-duplicate-graph-probe.cs` | frames 0/4/7 **0 changed**; src 4129, dup 4130, **SHARED 0**; 0 nulls | reproduces exactly |
| T-0284 `views-reapply-probe.cs` | 9/9 → 0/9 → 0/9 → **9/9** | reproduces |
| T-0284 `overflow-probe.cs` | **0 of 899** | reproduces |
| T-0284 `placement-pads-probe.cs` | Translate / Origin / Scale / Skew all 4 → 19 | reproduces exactly |
| T-0276 `a12verify.cs` (text / posterise / combine) | all three fixes reproduce with their sentences intact | reproduces |
| compile after this task's edits | `recompile_status` completed, `failed=false`, `errors=[]`; `scriptCompilationFailed=False` | — |

## CHANGELOG `[Unreleased]` audit against the tree

The block holds **300 bullets**. Every backticked token and CamelCase identifier in each was extracted and searched across the whole of `Assets/**` (source text *and* file names). **61 bullets name at least one identifier the tree does not contain.** Grouped:

| group | bullets | what it is |
|---|---|---|
| PyrePlus / `Plus*` names | **23** | the 2026-08-23 rename (`PlusRampPresets` → `PyreRampPresets`, `PlusNumpyRng` → `PyreNumpyRng`, `PyrePlusWindow` → `PyreWindow`, …). The behaviour exists; the notes name symbols that do not, and CLAUDE.md forbids writing "PyrePlus" in docs. |
| Cartographer `Clump*` / `Room*` names | **13** | the Clump → Prop rename (`[FormerlySerializedAs("groundClump")] public Prop groundProp`, `ClumpWindow` → `PropWindow`). Same shape. |
| **Fov** | **2** | **a whole module the package does not contain.** |
| Shaper node cache | **2** | describes fixes to code a later bullet deletes. |
| others (ZUI, TextSplash, Zoetrope, Story, …) | 21 | sub-symbols — private fields, helper classes since replaced — inside bullets whose feature does exist. |

**Four lines corrected here, under the licence in my brief** (a line describing something the tree does not do); every other line was left alone and is reported above rather than edited:

1. + 2. The two **`Fov`** bullets describe `Runtime/Fov` (`Laubrary.Fov`) — a fog-of-war bridge assembly **deleted from the package on 2026-08-04**, commit `a5844c13` ("drop the Fov module this project cannot compile"), because it cannot build without the Pixel-Perfect Fog Of War asset. `FovRoomWorld`, `FovConeRevealer`, `FovQuery`, `FovReveal`, `FovDebug`, `FovHideRenderers` are absent from `Assets/**`. Each bullet now ends with a **Not in this package** clause naming the commit and the reason.
3. + 4. The two bullets citing **`ShaperNodeIdentity.MixZuiValue`** and **`ShaperNodeIdentity.PrimitiveHash`** describe the T-0115 node cache, which bullet 41 of the same block (T-0253) deletes outright — none of `ShaperNodeIdentity`, `ShaperNodeCache`, `PrimitiveHash`, `IsCacheable` exists any more. Each now ends with a **Superseded** clause pointing at T-0253 and at `ShaperLayerKey`, which carries `phase01` and a dial's shape into the surviving key (`ShaperClock`'s own class doc says the same).

Related and worth the owner's eye: **the project `CLAUDE.md` still says "`ShaperNodeIdentity` folds `phase01` into every cache key"** — the fact is true, the type is gone; the surviving one is `Runtime/Shaper/ShaperLayerKey.cs`. Not edited (CLAUDE.md is outside this task's licence).

Everything else in the block that this pass checked behaviourally holds: `StampFieldHeight`'s 1.5 px tolerance, `spawnOrderChaos` in all three timings, `PyreWindow`/`ChunkWindow`/`ShaperWindow` `minSize = 820×520`, `kSplitMinLeftPane 360f` / `kSplitMinRightPane 320f`, the Views bar's `Apply`, `SaveAssetIfDirty` in place of `SaveAssets`, `Object.Instantiate` in place of `CopyAsset`, the MicroSlider and scrubber NaN guards, `Editor/Pyre` down to one "Pyre Plus" string (a comment explaining the retired name), and the view capture rooted at the whole window (both panes).

## Archive notes for the next pass

1. **Every archived probe's tree walk under-reports.** They all recurse with `e[i]` / `e.childCount`, which walks the **contentContainer**, not the hierarchy — so any element parented outside a container's content (a `ZuiSection`'s own bar, a `ZuiColumnFlow`'s non-rightmost columns, which T-0303 already caught in one place) is never visited. Measured here on one window: the same truncation sweep sees **107** text elements walking `e[i]` and **272** walking `e.hierarchy[i]`, and the `ZuiViewBar` is invisible to the first. Use `e.hierarchy.childCount` / `e.hierarchy[i]`; three of this pass's six findings are only visible that way.
2. `t0285-duplicate-sentinel-probe.cs` reported the WARM values (`dirty=True, inMemoryWidth=199`) on its first run here, where T-0303 documented the degraded cold reading. Its load-bearing assertions (`fileUnchanged`, `mtimeUnchanged`) reproduced either way; the cold/warm split depends on which earlier probe recreated `Assets/Shaper/AuditA19/`.
3. `a12verify.cs` is a Coplay `execute_script` file needing `{"mode":…}`, not a `unity eval_file` one (T-0303's note, confirmed).
4. **A window's title tag no longer reaches the OS caption.** `capture.cs` finds its window by setting `titleContent` to `ShaperCapTag` and matching the OS window text; the floating Shaper window's OS caption stayed `Shaper`, so `Snap` logged "no hwnd". Match the real caption instead. **And PrintWindow now returns a blank client area for these UI-Toolkit windows** (verified: a 3160×2359 capture of the Shaper window is white), while a `CopyFromScreen` of the same rect catches whatever the user has on top. Editor-window screenshots are not currently a working verification channel here.

## Not verified

- **T-0305 by eye.** Every state was read out of the live visual tree (drawn / greyed / tooltip / geometry) but **no human and no screenshot has looked at the Swarm card this pass** — see archive note 4. Forcing the window to the foreground would have stolen focus on the owner's live desktop, which I did not do.
- The three 0.9–1.3 px caption shortfalls and Pyre's `★` glyph were measured, not seen.
- The tooltip corrections in §1–§4 were read back out of the live window as text; nobody has hovered them.

## State left behind

`Assets/Shaper/` is back to exactly the two pre-existing scratch documents; `Assets/Pyre/` is **byte-identical to the session-start baseline** (every SHA-256 prefix matches, `New Pyre Plus.asset` still 216435 bytes / `32AEDD46…`). Deleted with their `.meta`s: `AuditT0307.asset`, `AuditA25a.asset`, `AuditA25b.asset`, `ShaperViews.asset`, and the `Audit0271/`, `Audit0277/`, `AuditA19/` folders the archived probes recreate. Every `T0307.*` and `T0304.*` pref and both SessionState keys deleted; `ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`; both `ZUI.Split.*` prefs gone (neither had a stored value at session start); `Shaper.lastView` deleted; `Undo.ClearAll()`; console cleared; every tool window closed. The editor reports the same **10 pre-existing dirty shaders and nothing else**, and `scriptCompilationFailed = False`.

`git status` lists five files this task changed — `Editor/AssetKit/ZuiAssetWindow.cs`, `Editor/Shaper/ShaperWindow.cs`, `ShaperWindow.Lights.cs`, `ShaperWindow.Sections.cs`, `CHANGELOG.md` — plus the modifications that were already present when it began. **No Pyre file was edited. Not committed** (programme rule 3).
