# Launimator UI — design suggestions (T-0067)

Reviewer: ui-designer agent, 2026-09-07, against `dev` @ 173dc934. Every capture in this folder is the REAL window (PrintWindow of a floating editor window at 1500x1000 / 1300x850 logical, 225% DPI), loaded with real project data (ProtoGuy `UpperAimRotation`, 16 frames + 2 meta layers; the `Saint Dragon.png` sheet for the cold walk; a throwaway Sprite Catalog over the Hero sheet, deleted afterwards). Files named `annot-*.png` carry numbered red callouts that the per-window sections below refer to by number. No code was changed.

Rule citations are to the UI Guide (`~/.claude/skills/laubrary/references/ui-layout-rules.md`) by section name, e.g. **[Control choice]**, **[Labeling]**, **[Stable workspace]**, **[Handover walk]**, **[Thumbnails]**, **[Truncated text]**, **[Space economy]**, **[Card layout]**, **[Label = action]**, **[Pre-flight]**, **[Sane widths]**, **[Breathing room]** — plus the project rules in `D:\UNITY\Laubrary Dev\CLAUDE.md` (**[ZUI for all UI]**, **[Menus lean]**, **[Naming triangle]**) and the fundamental **[Never type a reference string]** rule.

---

## 1. One-page summary, ranked by impact

| # | Suggestion | Window(s) | Rule served | Effort |
|---|---|---|---|---|
| 1 | **Fold the Sprite Catalog window into the Builder's "Identify Sprites" half, and retire the standalone window.** It is a second, weaker slicer (grid-only, cols/rows typed blind, no marquee, no pick, no pivot), with zero catalogs in the project and one consumer (`LaubraryAssetWindow`). See §4 for the full proposal. | Sprite Catalog, Builder | [ZUI for all UI], [Thumbnails], [Handover walk] step 3 (a step that needs a typed magic number is a missing feature) | M |
| 2 | **Clip the two IMGUI stages (registration stage, play/meta stage) to their own rect.** Today a sprite larger than the stage paints straight over the "5 · Animation" header, the Playback controls and the "+ Sprite from file" row (annot-builder-split #9, annot-builder-bottom #1, annot-builder-walk #1). This is the single most "weird"-looking thing in the tool and it happens on the shipped ProtoGuy data. | Builder | [Stable workspace] (nothing may paint over what the user is working on); [Mechanical audit] cannot see it (IMGUI) | S |
| 3 | **Make the Builder a real two-pane tool: `Z.Split` with the sheet+canvas LEFT and palette / stage / sequence RIGHT, and put Save in a pinned footer.** Today the right pane is a 1161px column in a 775px viewport; play stage, zones, events and Save all sit below the fold, and the section order 1-2-3-4-5 reads top-to-bottom while the layout is actually left/right. | Builder | [Stable workspace] (`Z.Split` shape), [Pre-flight] 1 (vertical space is scarce), [Label = action] (Save must be reachable where the work is) | M |
| 4 | **Replace every "label + `Z.Slider` + value field" and every bounded bare `Z.Int` with `Z.MicroSlider`:** FPS (1–30), Ghost opacity (0–1), Meta opacity (0.1–1), Zoom (canvas and meta), α> (0–255), ± tol (0–255), Ghost before/after (0–99). The Zoom field already truncates its own value ("2.3560", annot-builder-walk #3). | Builder | [Control choice] bounded scalar → MicroSlider; [Truncated text] | S |
| 5 | **Move every on-screen instruction sentence into the tooltip of the thing it describes** (nine of them in the Builder alone — mode hint, canvas title, palette hint, registration hint, point-mode note, vector-mode two-liner, strip hint, zones note, orphan note; two in the Catalog; one in the Aseprite bridge). | All | [Labeling] — "never an on-screen Label explaining what to do next" | S |
| 6 | **Delete the "Sections / Toggle Bar" mode switch and the "Hide sheet & canvas" fold; the `Z.Split` from #3 replaces both.** Two different ways to reshape the window, neither of which is the standard tool shape. | Builder | [Stable workspace], [Menus lean] spirit (no speculative chrome) | S |
| 7 | **Lauminary Browser: thumbnails for lauminaries and animations, `Z.MicroSlider` for fps, radios for Version, and a preview that fills the window instead of a fixed 180px strip above 40% dead space.** | Browser | [Thumbnails] (a Lauminary is a visual asset), [Control choice], [Sane widths] | M |
| 8 | **Zones: `Z.EnumDropdown` → `Z.Segmented` (PlayThrough / Loop); frame-events "frame" `Z.Int` → picker of the sequence frames; Frame-events zound button already a picker (good) — make it a `ZuiChip`.** | Builder | [Control choice] enum → radios, never a dropdown; [Never type a reference string] | S |
| 9 | **Retire the raw-IMGUI leftovers: `NamePromptWindow` (native `EditorGUILayout` modal), `RecentSheetsPopup` (native `GUILayout` list), the two `GenericMenu` context menus → `ZuiMenu`, and the "Prototyping only" warning banner → window tooltip / About.** | Builder | [ZUI for all UI], [Control choice] "no native controls", [Labeling] | S–M |
| 10 | **Merge "Laumination ↔ Aseprite" into the Builder** (the Builder already has Edit in Aseprite / Sync edits on the palette) and drop the `Laubrary/Launimator/` submenu's first item; keep "Set Aseprite Path…" and "Repair…" as the utility pair. | Aseprite bridge | [Menus lean], [Label = action] | S |

Everything in the table is a suggestion; nothing was implemented. Items 2, 4, 5, 8 are near-mechanical and could go in one small task; 1, 3, 7 are the design work.

---

## 2. What was walked, and what the walk found

**The user's sentence:** "I have a sprite sheet PNG and I want a named animation on my character, with a muzzle point on it, that I can see play."

Walked cold in the Builder (window open, ProtoGuy `UpperAimRotation` bound, then a fresh sheet) — see, click, how-did-they-know:

1. **Get a sheet in.** See: `1 · Sheet [None (Texture 2D)] [Load] [Recent ▾]` plus a URL row. Click: the object picker, then **Load**. How they knew: they didn't — assigning the object field does nothing until Load is pressed (`picked` is only read by Load, `BuildSheetSection`), and nothing says so. **Gap:** the field should load on assignment; "Load" is a redundant second click. Also the URL/Download row is permanently visible although downloading is the rare path — fold it under the Recent ▾ menu. [Label = action], [Pre-flight] 1.
2. **Identify sprites.** See: Mode `Grid / Box / Pick`, then a row of grid numbers, a PPU/pivot row, an alpha/BG-key row, a Box L/T/W/H row with **Add Region (0)**. Click: drag a marquee on the canvas — but the canvas is a whole section lower ("3 · Canvas") below ~230px of controls, and at the empty state the count on Add Region is 0 so it reads disabled. How they knew: the subtle sentence beside Mode says "Marquee a box on the canvas, set its grid, Add Region." — an on-screen instruction standing in for an affordance. **Gap:** [Labeling]; and the controls-above-canvas order breaks [Stable workspace] (changing Mode adds/removes rows and moves the canvas). The right fix is the canvas as the left pane's workspace with a one-row contextual toolbar ABOVE it that never changes height.
3. **Set the grid.** Cols/Rows are bare `Z.Int`s (fine — unbounded), but Space/Pad/α/tol are bounded (0–255) and should be sliders. The marquee rect is exposed as four typed ints (L/T/W/H) — that is a power feature, fine in a tooltip'd popover, wrong as a permanent row.
4. **Pick a sprite.** Palette thumbnails are numbered 1..N with no names and no size; the selected one gets a magenta frame. Registration stage appears with a green crosshair and a yellow box, and the sprite paints outside the stage (**bug**, callout 9 / walk #1). "Selected Sprite" is a wall of 16 buttons on 4 rows (arrows, Baseline, Head, Add → seq, Trim, Duplicate, Delete, Flip H/V, rotate ×2, Reset, −/+, Smooth) — [Card layout] says pack the short ones, but they're already packed; the issue is there is no hierarchy: nudge/registration tools and lossless transforms are the same visual weight as Delete. Suggest: nudge arrows live ON the registration stage (overlay corner buttons), transforms in a right-click `ZuiMenu` on the palette thumb (the menu already exists as a `GenericMenu`), and only **Add → seq / Trim / Duplicate / Delete** stay as buttons.
5. **Build the sequence.** Double-click a thumb (undiscoverable; the PaletteHelp tooltip is the only place it is written) or press **Add → seq**. The sequence strip is a 96px IMGUI band with thumbs on the top 50px and dead space under (annot-builder-bottom #5). Drag-to-reorder exists but is again documented only by an on-screen sentence (#6).
6. **Paint a meta point/vector.** Toggle **Meta layers** (a `Z.Toggle` that changes the play stage's meaning — reasonable), `+ Layer`, type an id (correct: declaration), pick Mode `Shape/Point/Vector` (correct: `Z.MiniRadio`). Then the play stage becomes the paint editor. Finding: the stage caption says "Editing 'Muzzle' · frame 7/16" while the strip highlights frame 1 (yellow = playhead) AND frame 7 (green = selected) with no legend; a two-line instruction paragraph explains the mouse buttons (#4); `F11/16` and `origin=(0.10,0.16) dir=(-0.75,-0.66)` are debug read-outs shown as body text (#3). Vector data per frame worked (the red vector on the stage is the authored muzzle). **Gap:** one frame indicator, one highlight colour, read-outs in the stage caption, instructions in the stage tooltip (already there — the tooltip on `_playIM` says the same thing, so the on-screen copy is pure duplication).
7. **Preview it.** ▶ is a 36px button with FPS beside it as a `Z.Slider` + field (pre-MicroSlider look, callout 10). The stage is 360x245 and only reachable after scrolling in split mode. Works.
8. **Save.** `Save → lauminary 'ProtoGuy'` is the LAST element of a 1161px column (y=959 of 1000 after scrolling to the bottom). It works, but the one action every session ends with is the least reachable control in the window. [Label = action] — the action should be where the work is: a pinned footer row `[Save to 'ProtoGuy']  Animation: UpperAimRotation   [Open Lauminary Browser]`.
9. **Second time / re-entry.** Reopening via the quicklist chip (annot-builder-split #3) rebinds correctly; the "Sections/Toggle Bar" choice and each section's fold are persisted (EditorPrefs / static dictionary). Not re-walked by hand after a domain reload — see "not verified".

**Sprite Catalog, walked cold:** New → assign Sheet → guess Cols/Rows (default 8×8 for a 17×1 strip) → Generate grid cells → 17 rows of `[blank thumb] [sprite_000] [×]` → Slice & apply (which rewrites the texture's import settings to Multiple — a destructive step on a shared PNG with no warning). The stage shows the sheet at 1:1 in the top-left of an otherwise empty 1000px area with cell names painted over the sprites, overlapping each other (annot-catalog #5). At no point can you SEE a cell before generating, adjust one, or set a pivot (the data has one). This is the "weird" the owner means, in my judgement: it looks like a stripped-down duplicate of the Builder's step 2 wearing the LauAsset chrome (Tags section, New/Browse/Duplicate/Rename/Delete), for a type that nothing in the project uses.

**ZuiAudit** (`ExpandAll` then `Audit`, windows open at real size): Lauminary Browser 0 findings / foldedSkipped 0; Sprite Catalog 0 / 0; Aseprite bridge 0 / 0; Laumination Builder 0 findings but foldedSkipped stays 1 after ExpandAll (one fold the audit cannot open), so the Builder result is not authoritative. Nothing above is an audit finding — the IMGUI overflow, control choice, labeling and packing are exactly the categories the guide says the audit does not catch.

---

## 3. Per window

### 3.1 Laumination Builder (`Laubrary/Laumination Builder`, `LauminationBuilderWindow` + 4 partials, 4.6k lines)

**(a) For:** cut sprites out of a sheet, order them into one named animation with events/meta-layers/zones, preview it, save it into a lauminary's draft.

**(b) What is wrong today** — `annot-builder-split.png` (bound to ProtoGuy, split layout), `annot-builder-bottom.png` (left half folded, scrolled to the bottom), `annot-builder-walk.png` (fresh sheet, 18 cells, 3 in sequence), `annot-builder-empty.png` (first run):

| Callout (split) | Finding | Rule |
|---|---|---|
| 1 | Two permanent banners (copyright disclaimer + "Editing X for lauminary Y") take the top 90px of every session. The second is status, the first is a one-time notice. | [Labeling] — explanation belongs in a tooltip; [Stable workspace] — reserve one status line, not two help boxes |
| 2 | "◀ Hide sheet & canvas" — a bespoke fold that reflows the entire window. | [Stable workspace] — the standard shape is `Z.Split`; a split's divider IS the fold |
| 3 | Quicklist "ProtoGuy — animations (5)" as a chip row top-right, only when bound; on an orphan it shows an orphan list instead. Good idea, wrong place — it competes with the Sheet row for the same line and wraps at narrow widths. | [Card layout] 4 — a wide control earns its own row; put it in the pinned footer beside Save, or in the left pane header |
| 4 | "Sections / Toggle Bar" — a second window-layout mode switch. | [Stable workspace]; two competing layout systems in one window |
| 5 | Mode radios + an on-screen sentence that changes with the mode. | [Labeling] |
| 6 | α >, ± tol are bounded 0–255 bare ints; "Pick ☉" eyedropper label toggles to "Click sheet…" (label changes width mid-row). | [Control choice] slider; [Stable workspace] variable-width content goes LAST |
| 7 | Zoom = `Z.Slider` + external value field; value clips to "2.3560" in walk #3. | [Control choice] MicroSlider; [Truncated text] |
| 8 | "3 · Canvas — drag to marquee; drag interior/edges to move/resize" as a section TITLE. | [Labeling] — titles are short and literal |
| 9 | Registration stage paints outside its 220px rect — the sprite covers the "5 · Animation" header. Reproduced in all three layouts (bottom #1 boxed; walk #1 shows the PLAY stage doing the same over the Playback controls). Root cause: `DrawRegistrationCanvas`/`DrawPlayBox` draw with `GUI.DrawTexture` at a scale derived from the FRAME box, not clipped by `GUI.BeginClip(rect)`. | [Stable workspace]; a bug, not a style choice |
| 10 | FPS = `Z.Slider` + field. | [Control choice] MicroSlider |
| 11 | Ghosts `<` `>` two 40px ints with single-character labels. | [Control choice] (bounded 0–99 → slider or a single "Ghosts ±N" MicroSlider); [Labeling] |
| 12 | Layer rows: `●/○` active radio as two buttons, id text field (correct — declaration), colour = a `Button` with a background colour opening a native `PopupWindow` palette, `X`. | [Control choice] — `Z.Color` exists; the `ZuiMenu` should hold the palette |
| 13 | Section 4/5 squeezed into a 44%-wide right pane that is 1161px tall in a 775px viewport; play stage, strip, zones, events and Save below the fold. | [Pre-flight] 1, [Stable workspace] |

Bottom capture (`annot-builder-bottom.png`): #2 stage caption "frame 7/16" vs strip highlighting 1 AND 7; #3 debug read-outs `F11/16`, `origin=… dir=…` as body text; #4 two-line mouse-button paragraph; #5 sequence strip 96px with the lower half empty; #6 another instruction line; #7 "Zones" is a `Z.Toggle` that reveals a track — fine, but the zone rows then use `Z.EnumDropdown` for PlayThrough/Loop [Control choice]; #8 "+ event @ frame 7" right-aligned inside an otherwise empty box — the button label carries state (frame number) so its width changes as you scrub [Stable workspace]; #9 Save is the final element of the page; #10 meta Zoom slider + field.

Empty state (`annot-builder-empty.png`): #1 two banners on an empty window; #2 a fold button for a pane that has nothing in it; #3 `None (Texture 2D) [Load] [Recent ▾]` — the only way to discover the four sheets already in `Assets/SpriteSheets` is to click Recent; #4 URL/Download always visible; #5 ~85% of the window blank with no "drop a PNG here / pick a recent sheet" invitation. [Handover walk] 4 — the empty state is a first-class screen.

Code-level items not visible in a capture: two `GenericMenu` context menus (palette, strip) instead of `ZuiMenu`; `NamePromptWindow` (new-animation name, marquee auto-add) is a 40-line native `EditorGUILayout` modal; `RecentSheetsPopup` is a native `GUILayout` list with a red "x" delete that calls `EditorUtility.DisplayDialog`; `Z.Object<Texture2D>` for "+ Sprite from file" is a sanctioned gap but is labelled with a "+" as if it were a button [Label = action]; 6 `tooltip` mentions in 3.9k lines is misleading — tooltips are passed positionally to almost every `Z.*` call and are generally good, the problem is the DUPLICATED on-screen copies.

**(c) Suggestion** — one `Z.Split("launimator.split")`, left = sheet workspace, right = animation workspace, footer pinned:

```
┌ Sheet [Saint Dragon ▾ Recent] 347×190  PPU [16]  Pivot (Center|Bottom|TopLeft|Custom)   ⓘ ┐  ← one row; ⓘ = disclaimer tooltip
├──────────────────────────────┬──────────────────────────────────────────────────────────┤
│ (Grid|Box|Pick) Cols[6] Rows[3] Space·Pad ▸ │ Palette (18)  [Edit in Aseprite][Sync][Clear all]  │
│ Alpha-trim  α>[====8] BG-key ■ tol[==12] ☉ │ ▦▦▦▦▦▦▦▦▦▦ ▦▦▦▦▦▦▦▦  (thumb + name on hover)      │
│                     [Add Region (18)]       ├───────────────────────────┬──────────────────────┤
│ ┌────────────────────────────────────────┐  │ Registration stage        │ Sel 57×63  ←→↓↑      │
│ │ CANVAS  (marquee / pick)               │  │  (clipped, zoom to fit)   │ Baseline Head Trim   │
│ │                                        │  │                           │ Add→seq Dup Delete   │
│ │                                        │  │                           │ (Flip/Rot: right-click)│
│ │                                        │  ├───────────────────────────┴──────────────────────┤
│ │                                        │  │ ▶ FPS[====12] Loop gap (None|Pause|Idle)  Meta ▢ │
│ │                                        │  │ ┌ stage: play OR paint (clipped) ┐ Layers…       │
│ │                                        │  │ └────────────────────────────────┘               │
│ └────────────────────────────────────────┘  │ strip: [1][2][3] … (thumb height = strip height)  │
│ Zoom [====2.4] Fit                          │ Zones ▸   Frame events (0) [+ event]              │
├──────────────────────────────────────────────┴──────────────────────────────────────────────────┤
│ [ Save to 'ProtoGuy' ]  UpperAimRotation   quicklist: LegsWalk_N · LegsWalk_E · … · ●UpperAim… │  ← pinned footer, always visible
└───────────────────────────────────────────────────────────────────────────────────────────────┘
```

- Contextual toolbar above the canvas is ONE fixed-height row; Mode-dependent controls change `visibility`, not `display` [Stable workspace]. The Box L/T/W/H exact-rect editor goes into a right-click `ZuiMenu` on the marquee.
- Both IMGUI stages wrap their drawing in `GUI.BeginClip(rect)`/`EndClip` and compute their scale from the STAGE size (fit), never from the frame box alone.
- `Z.MicroSlider` for FPS, both Zooms, both Opacities, α, tol, Ghosts. `Z.Segmented` for Zone behaviour. `Z.Color` for a layer's colour, palette inside the colour's own `ZuiMenu`.
- All on-screen instruction sentences → the tooltip of the stage/strip/section they describe (most already have the same text in a tooltip; delete the visible copy).
- `NamePromptWindow` → `Z.Popover` with a `Z.TextInput` + OK/Cancel; `RecentSheetsPopup` → `ZuiMenu.Custom` with thumbnails (a sheet is a visual asset — [Thumbnails]); the two `GenericMenu`s → `ZuiMenu`.
- Sheet assignment loads immediately; "Load" disappears. URL download moves into the Recent ▾ menu as its last item ("Download from URL…").
- Empty state: the canvas area shows one centred `Z.Help`-styled invitation ("Drop a PNG here, pick a Recent sheet, or paste a URL") with the Recent thumbnails inline — the invitation IS the affordance.

**(d) Effort:** L for the split/footer restructure (mostly moving existing `Build*` methods into new hosts — the sections are already independent builders); S for the clip fix; S for the slider/enum swaps; S for tooltip moves; M for the three raw-IMGUI retirements.

### 3.2 Sprite Catalog (`Laubrary/Sprite Catalog`, `SpriteCatalogWindow : ZuiAssetWindow<SpriteCatalog>`, 241 lines) — "the sprite grabber"

**(a) For:** name the cells of one sheet and slice it into individual `Sprite` assets (a `SpriteCatalog` asset = sheet + PPU + list of {name, cell, pivot, sprite}).

**(b) What is wrong** — `annot-catalog.png` (after Generate grid cells on the Hero sheet, 17×1):

| # | Finding | Rule |
|---|---|---|
| 1 | A "Tags" section header (inherited from `ZuiAssetWindow`) sits directly above Sheet and Pixels per unit, so the two fields read as belonging to Tags. Tags themselves are "· none ·" with a "Tags…" button. | [Breathing room], [Labeling]; the LauAsset chrome is a cost for a type that is never browsed or tagged — [Thumbnails] corollary "do not make something a LauAsset just to get a picker" |
| 2 | Cols/Rows are typed blind (defaults 8×8 whatever the sheet), then "Generate grid cells" REPLACES the list. No marquee, no visual grid until after generating, no cell-size mode, no spacing/padding. | [Handover walk] 3 — a step that needs a guessed number is a missing feature |
| 3 | On-screen paragraph "Splits the whole sheet into Cols×Rows cells with default names. Rename below, then Slice." | [Labeling] |
| 4 | 17 blank 24px thumbnail squares until Slice & apply has run. | [Thumbnails] — a blank thumbnail is worse than none; the cell rect is known, so the thumb can be cut from the sheet immediately |
| 5 | Stage draws the sheet at 1:1 in the top-left of a ~1000×800 area; cell names painted over the sprites, overlapping (boxed). No zoom, no fit, no selection, no hover. | [Sane widths] (a preview viewport uses the space it needs — and should fit), [Truncated text] |
| 6 | "Slice & apply" rewrites the source texture's importer (Sprite Mode Multiple + sprite sheet metadata) — destructive on a shared PNG, no confirm, and the Builder's own slicer deliberately does NOT do this (it bakes an owned atlas). | project rule "Don't overwrite user assets" (memory `no-overwriting-user-assets`) |
| 7 | New/Browse/Duplicate/Rename/Delete asset chrome for a type with 0 instances in the project and one consumer (`Editor/AssetKit/LaubraryAssetWindow.cs`). | [Menus lean] spirit |

**(c) Proposal — retire the window, keep the data type, host the feature in the Builder:**

1. The Builder's step 2 already does everything the Catalog does, better (marquee, grid by count OR cell size, spacing/padding, alpha-trim, BG-key, pick mode, per-cell pivot, transforms). Add ONE thing to it: a per-cell **name** (typed in the palette thumb's right-click menu or on the Selected Sprite row — a declaration, so a text field is right). The palette then IS a catalog.
2. Add a **"Export named sprites…"** action to the palette toolbar (next to Edit in Aseprite) that writes a `SpriteCatalog` asset next to the sheet and bakes the named cells as `Sprite` sub-assets of an OWNED atlas PNG (`LauminaryAtlasPacker` already does this for animations) — never touching the source PNG's importer. That is the only thing the Catalog window can do that the Builder cannot.
3. `LaubraryAssetWindow`'s consumer keeps working (same `SpriteCatalog` type, same `entries`).
4. Delete `SpriteCatalogWindow.cs` + the `Laubrary/Sprite Catalog` menu item. Net: one window fewer, one menu item fewer, no duplicated slicer, and the "grabber" is the one with the good canvas.

If the owner would rather keep a standalone window (e.g. for non-animation UI sprites), the minimum fix is: hide the Tags section (`AutoInsertTagsSection => false`), make the stage a fit-to-view zoomable canvas with click-to-select and drag-marquee, thumbs cut from the sheet immediately, `Z.MicroSlider`s for Cols/Rows with the sheet's aspect as the range hint, `Z.Value2D` per-entry pivot, and a confirm dialog before Slice & apply explains it will change the PNG's import settings. That is M effort and still leaves two slicers.

**(d) Effort:** M for the merge (names + export action in the Builder, delete the window); M for the keep-and-fix alternative.

### 3.3 Lauminary Browser (`Laubrary/Lauminary Browser`, `LauminaryBrowserWindow`, 662 lines)

**(a) For:** list lauminaries and orphaned animations, pick one, rename/duplicate/delete, set fps, open an animation in the Builder, commit the draft as a version, preview.

**(b) What is wrong** — `annot-browser.png` (ProtoGuy selected):

| # | Finding | Rule |
|---|---|---|
| 1 | Left list is a column of full-width buttons ("Orphaned (0)", "ProtoGuy (latest v0)") with no thumbnail. A Lauminary is a VISUAL asset. | [Thumbnails] — visual asset → the thumbnail must always resolve |
| 2 | Top-right: a text field pre-filled "Hero" (hard-coded `_newName = "Hero"`, line 32) + "New lauminary". A stale default from the demo; reads as if a "Hero" already exists. | [Label = action] — "New lauminary" should create and then rename inline; the pre-filled name is a trap |
| 3 | Version is a native dropdown (`Z.Dropdown`). Versions are few (draft + v1..vN). | [Control choice] — a short pick-one is `Z.MiniRadio`/`Z.Segmented`; `Z.Dropdown` is the native-control path |
| 4 | Animation rows: `[name button] 8f [10] fps ………… [Edit][Rename][Dup][X]` stretched across 1180px with a flexible gap. fps is a bare `Z.Float` (natural range 1–60). No thumbnail per animation. | [Card layout] 1 (header: identity + one universal control + gap + ×) is right, but four action buttons is not "one control"; [Control choice] fps → MicroSlider; [Thumbnails] |
| 5 | "Idle" pre-filled text field + "New animation" (hard-coded `_newAnimName = "Idle"`, line 34). | same as #2 |
| 6 | Preview: a fixed 180px-tall black strip across the full width, sprite tiny, "Frame 6/8", 40% of the window empty below it. `Frame 6/8` and `Preview: LegsWalk_N` are two separate labels. | [Sane widths] — a preview viewport uses the space it needs; here it uses the wrong axis |
| 7 | Duplicate / [name field] Rename / Delete… sit under the list with a 150px gap; they act on the selected lauminary but sit nowhere near it. | [Breathing room] inverted — related controls separated |
| 8 | "Refresh" button top-left. A browser should refresh itself on focus (it already has `OnFocus`). | [Label = action] — if it is needed, something is not watching the asset database |

All destructive/failure paths use `EditorUtility.DisplayDialog` (7 calls) — acceptable for confirm/error, but the delete confirms could be one `Z.Popover` with a red button for consistency with `AssetKit`.

**(c) Suggestion:**

```
┌ Lauminaries ─────────┬ ProtoGuy   draft (Draft|v1|v2)   [Save draft as new version]  [Dup][Rename][Delete…] ┐
│ ▦ ProtoGuy    v0     │ Animations (5)                                                  [+ New animation]     │
│ ▦ Hero        v0     │ ▦ LegsWalk_N        8f  fps[====10]                    [Edit] [Rename] [Dup] [×]    │
│ ▦ Floating Disc v0   │ ▦ LegsWalk_E        8f  fps[====10]                    …                              │
│ ▦ Bullets     v0     │ ▦ UpperAimRotation 16f  fps[====12]                    …                              │
│ ─ Orphaned (0) ─     │ ┌──────────────────────────────────────────────────────────────────────────────────┐ │
│                      │ │ PREVIEW (fills remaining height, sprite fit-scaled)          ▶  Frame 6/8       │ │
│                      │ └──────────────────────────────────────────────────────────────────────────────────┘ │
└──────────────────────┴────────────────────────────────────────────────────────────────────────────────────┘
```

- `Z.Split`, left list of `ZuiChip`-style rows with a first-frame thumbnail (`FramePreview.cs` already renders one) and the version count; actions for the selected lauminary in the detail header, not under the list.
- "New lauminary" / "New animation" create with a generated unique name and open an inline rename (the Rename/OK/Cancel inline row already exists — reuse it), no pre-filled "Hero"/"Idle".
- fps → `Z.MicroSlider(1..60)`; Version → `Z.Segmented`.
- Preview fills the remaining height; one caption line "LegsWalk_N · frame 6/8" in the stage; ▶/❚❚ on the stage corner.
- Drop "Refresh" (refresh on focus + `AssetPostprocessor` hook, which `ExternalRefresh` already provides for the Builder).

**(d) Effort:** M.

### 3.4 Laumination ↔ Aseprite (`Laubrary/Launimator/Laumination ↔ Aseprite`, `AnimationAsepriteWindow`, 94 lines)

**(a) For:** pick a lauminary + one of its draft animations, export it to `.aseprite`, open Aseprite, sync edits back.

**(b) What is wrong** — `aseprite-bridge.png`: a `Z.Object<Lauminary>` picker (sanctioned gap) + on-screen instruction "Pick a lauminary to edit one of its draft animations in Aseprite." [Labeling]; then a `Z.Dropdown` for the animation [Control choice — a reference pick should be a `ZuiChip`/radio list]; then Sync. Functionally this duplicates the Builder palette's "Edit in Aseprite" / "Sync edits" pair, at animation rather than sprite granularity.

**(c) Suggestion:** move the animation-level round-trip into the Builder's pinned footer as a `ZuiMenu` on the animation name (`UpperAimRotation ▾` → "Edit in Aseprite…", "Sync from Aseprite", "Rename…"), delete this window and its menu item. `Laubrary/Launimator/` then holds only "Set Aseprite Path…" and "Repair Lauminary Assets" — within the project's one-submenu-≤3 exception, and arguably both belong in a "Launimator settings" gear on the Builder instead.

**(d) Effort:** S.

### 3.5 Popovers and dialogs

- `RecentSheetsPopup` (`PopupWindowContent`, raw `GUILayout`): a list of sheet names with an "x" delete per row and a `DisplayDialog` confirm. → `ZuiMenu.Custom` with a thumbnail grid (sheets are visual) and the delete under right-click; the Download-from-URL row moves here. [ZUI for all UI], [Thumbnails]. S.
- `NamePromptWindow` (`EditorWindow.ShowModalUtility`, raw `EditorGUILayout`): label, text field, Cancel/OK. → `Z.Popover` anchored to the button that opened it. [ZUI for all UI]. S. (Not captured live — it is modal and would have wedged the automated session; described from `AnimationBuilderWindow.AutoSlice.cs:206-258`.)
- `ColorPalettePopup` for a meta layer's colour → `Z.Color`'s own menu. S.
- `ZoundPickerPopup` for a frame event's zound — already a picker, correct by [Never type a reference string]; render the chosen value as a `ZuiChip` rather than a `Z.Button` whose label is the value. S.
- Two `GenericMenu`s (palette right-click, strip right-click) → `ZuiMenu`. S.

---

## 4. The sprite-grabber proposal in one paragraph

The Builder's "Identify Sprites" half is already the good grabber — marquee, grid-by-count or cell-size, spacing/padding, alpha-trim, BG-key eyedropper, pick mode, per-cell pivot and lossless transforms — and the standalone Sprite Catalog is a grid-only copy of it with no canvas interaction, blank thumbnails, a misleading inherited "Tags" header, an importer-rewriting Slice, and zero instances in the project. Give the Builder's palette cells a name and an "Export named sprites…" action that writes a `SpriteCatalog` + owned atlas, delete `SpriteCatalogWindow`, and fix the two things that make the Builder's grabber itself look weird: clip the registration/play stages to their rects, and turn the controls-above-canvas stack into one fixed-height toolbar over a `Z.Split` left pane.

---

## 5. Verification buckets

- **Verified by eye (captures in this folder):** every finding in §3 that cites a callout number; the stage overflow bug in three separate layouts on two different sheets; the ZuiAudit numbers.
- **Verified by probe:** window/scroll geometry (right pane 1161px content in 775px viewport; Save at y=959/1000 after scrolling); `_newName="Hero"` / `_newAnimName="Idle"` defaults; `GenericMenu`/`EditorGUILayout`/`PopupWindow`/`DisplayDialog` counts; SpriteCatalog has 0 instances and 1 consumer; no project `.meta` was dirtied by loading sheets (git status clean apart from pre-existing `Assets/Temp` files).
- **Not verified:** no real mouse was dragged — marquee, pick, strip drag, meta painting were driven through the window's own methods (`LoadSheet`, `AddRegion`, `SelectSingle`, `AddSelectedToSequence`) so the ergonomics of the gestures themselves are not assessed; `NamePromptWindow` and `RecentSheetsPopup` not captured live; the "Toggle Bar" layout mode not captured (described from code); no domain-reload re-entry pass.

## 6. Files

- `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0067\annot-builder-split.png` — Builder, ProtoGuy bound, split layout, 13 callouts.
- `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0067\annot-builder-bottom.png` — Builder, left folded, scrolled to the bottom, 10 callouts.
- `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0067\annot-builder-walk.png` — Builder after the cold walk on Saint Dragon, 8 callouts.
- `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0067\annot-builder-empty.png` — Builder first run, 5 callouts.
- `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0067\annot-browser.png` — Lauminary Browser, 8 callouts.
- `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0067\annot-catalog.png` — Sprite Catalog after Generate grid cells, 7 callouts.
- Raw captures: `builder-empty.png`, `builder-protoguy-meta.png`, `builder-protoguy-collapsed.png`, `builder-protoguy-collapsed-bottom.png`, `builder-walk-sheet.png`, `browser-protoguy.png`, `catalog-fresh.png`, `catalog-grid.png`, `aseprite-bridge.png` (full DPI), `view-*.png` (half size).
- Tooling: `mkshot.py` (writes a `shot-<TAG>.cs` from the T-0081 PrintWindow template), `annotate.py` (PIL callouts; coordinates are in `view-*.png` space).
