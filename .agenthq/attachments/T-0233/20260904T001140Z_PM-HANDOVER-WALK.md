# DotGen — PM handover walk (T-0233, 2026-09-04)

Walked personally by the PM session (Fable 5.1) on the real `DotGenWindow` in the Laubrary Dev editor (port 7800, `Application.dataPath` verified), on a fresh window instance (`CreateInstance` + `ShowUtility`, title `PMWalk-DotGen`), never on the owner's own window. Captures are in `.agenthq/workspace/T-0233/`. Method per `ui-layout-rules.md` "THE HANDOVER WALK": cold, from the empty state, twice, following the data to the picture, verified over time (a dial moves → the picture moves → undo moves it back).

## 0. Requirement → code trace (coverage before polish)

| Owner's requirement | Where it lives |
|---|---|
| "UI and functionality pretty much exactly like the POC" | `Runtime/DotGen/` (model, 10 modules, evaluator, renderer — 209/209 checklist rows traced by T-0232; demo = 308 dots / 32 areas, bit-exact hash01) + `Editor/DotGen/` (Frame, Hierarchy, Generator, Placement, Selectors, Mutators, Drawers sections; POC §17 preview navigation; §13 gizmos) |
| "made with ZUI the same way Pyre is made" | `DotGenWindow : ZuiAssetWindow<DotGen>`, `Z.ColumnFlow` dial pane + splitter + right-pane preview, `Z.*` controls only (T-0232 native-control grep: one sanctioned IMGUI preview island), `ZuiReflect` cards, `Dirty`/`Undo.RecordObject` on every edit |
| "section/toggle bar on top" | `ZuiSectionToggleBar("DotGen", Tags, Views, Frame, Hierarchy, Generator, Placement, Selectors, Mutators, Drawers)` — captures 04/05 |
| "assets like Pyre assets" | `DotGen : ScriptableObject`, `[CreateAssetMenu("Laubrary/DotGen")]`, `IVisualPreview`, Tags section, `previewBackSplash` on the asset — same shape as `Pyre` |
| "browsing for assets just like Pyre" | inherited `ZuiAssetWindow` toolbar (assign / New / Browse / Duplicate / Rename / Delete) + thumbnail grid — capture 01 |
| "preview can have a backsplash just like Pyre" | `BackSplashZui.Build(doc.previewBackSplash …)` panel under the preview; drawn behind the frame like `PyreWindow.DrawBackdrop` — visible in every capture |
| Tool name DotGen, one menu item | `[MenuItem("Laubrary/DotGen")]` only |

## 1. The walk, as the user would do it

**Goal in the user's words:** "I open DotGen, make a new document, see the demo picture, tweak a dial and watch it change, hover a card to see what it does, undo, and export a PNG."

| Step | Saw | Did | How they knew |
|---|---|---|---|
| Open `Laubrary/DotGen` with no asset | The library browser with two real thumbnails (DotGen Demo, Three Rows of Buildings) and **New / Browse** in the toolbar (capture 01) | clicked **New** | the only primary action on an otherwise empty screen |
| Name row appears | "Asset name [New DotGen] Create Cancel" | typed `PM Walk Doc`, clicked **Create** | standard Laubrary create row |
| Document opens | the §20 demo: readout **308 dots · 32 areas**, tree Root Frame / Radial Clusters / Diamond Grids, picture in the preview (capture 02) | — | — |
| Tweak a dial | Generator section for the selected Radial Clusters; **Spawn chance 78** | dragged it to 30 | it is a slider under the selected generator |
| Watch | readout 195 dots · 23 areas, picture changed (hash 538710bd → 814c1b83) | Ctrl+Z | — |
| Undo | 308 / 32, original hash, redo brings 195 back, undo again 308 — **one step per drag** | — | — |
| Reset a dial | tooltip reads "…Double-click to reset to 100." | double-click (55 → 100), one Ctrl+Z → 55 | the tooltip says so |
| Hover a card | hovering the **Gather** (warp) card draws its point-warp centres over the picture (capture 03) | — | Gizmos: Hovered is the default and is shown in the preview chrome |
| Hover a tree row | hovering **Diamond Grids** in the tree draws its area diamonds + anchors (capture 04) | — | — |
| New seed | seed 4821 → 837941, 307 dots / 35 areas, new picture; Ctrl+Z restores | clicked **New seed** in Frame | button beside the seed |
| Hierarchy | **+ Child generator** on Radial Clusters → "Generator" child, 1267 dots / 112 areas; **Delete** → back to 308; undo/undo restores; **Duplicate** on Diamond Grids → "Diamond Grids copy", 344 dots; undo; **▲** moves Diamond Grids above Radial Clusters (picture + count change: instance indices follow evaluation order, as in the POC); undo | — | buttons under the tree |
| Toggle bar | **Toggle Bar** mode shows one button per section; switching Hierarchy and Generator off hides those sections (captures 04, 05) | — | the two-mode switch at the top, same as Pyre |
| Second pass | deleted the document, clicked **New** again → identical 308 / 32 / hash | — | — |
| Follow the data | opened a copy of *Three Rows of Buildings*, selected Houses, dragged **Height Max 260 → 120**: boxes shrink (captures 06 → 07); Ctrl+Z restores the hash | — | — |

## 2. Three buckets

**Verified by probe (this walk):** New creates the demo (twice, identical); counts 308/32; drag → one Undo step; redo; double-click reset to the type default + one Undo; New seed changes the picture and undoes; add/delete/duplicate/move child each one Undo step with the expected names and counts; the buildings document's Height Max changes the raster hash and undo restores it; the repaired buildings asset evaluates with 24 areas and its module graph (79 managed references) survived the save.

**Verified by eye (captures 01–07):** empty-state browser with real thumbnails; the full window layout (toggle bar, Tags, Views, Frame, Hierarchy, Generator, Placement/Selectors/Mutators/Drawers cards) in Pyre's shape; the point-warp gizmo on card hover; the generator-area gizmo on tree-row hover; Toggle Bar mode hiding sections; the buildings picture shrinking under the dial; the exported buildings PNG showing three rows of boxes with lit windows and no dot markers.

**Not verified by anyone (owner-owned):** a literal mouse — no real drag, wheel zoom, double-click-fit, splitter or resize-bar drag anywhere in the programme (every gesture was the control's own gesture path or a synthesized event); the two modal dialogs (Export PNG save panel, Reset-to-demo confirm) — the export bytes were proven equal to the preview render by T-0232, the dialog itself was never seen; the Views bar (save/recall) and Tags were never exercised; `Z.Fill`'s right-click Flat↔Gradient switch; the ZuiMenu add-pickers by a real click (T-0231 exercised them programmatically and screenshotted the results); frame size 2048 performance (~50–60 ms estimated by T-0228, not measured); the 300-box / 250-instance caps at their limits; Pyre's own dials after the shared `ZuiReflect` enum change (T-0232 checked layout by eye and ZuiAudit; the owner has not).

## 3. Found during the walk

1. **W3.2's demo repair (g) had not reached the asset.** The committed PNG showed lit windows and no dots, but `Three Rows of Buildings.asset` still had `showDots: 1` on Houses/Windows and the windows fill at #26384a — T-0232's GUID-remint restored a pre-fix backup after the export. **Fixed by the PM** through an Editor script (dots off on both, windows fill #79b4d2, PNG re-exported byte-equivalent to the committed one, `Samples~` mirror refreshed). Lesson for the guide: a "restore from backup" step after an edit needs a diff, not a checksum of the backup.
2. **Over-undo unwinds the asset creation** (RegisterCreatedObjectUndo) and the window then shows the browser; redo recreates the object but the window does not re-point at it. Shared AssetKit behaviour, same in Pyre; not DotGen's.
3. **Placement card header now shows a lone caret** after fix (d) removed its title (capture 06). Cosmetic; a ZuiBox with no title should hide its header row or the caret should sit on the Method row. Not fixed.
4. **Reorder changes counts**, not just overlap (318 vs 308 after moving Diamond Grids first) because instance indices follow evaluation order and feed the Random selector — identical in the POC (its `instanceIndex` is the same running index), so not a defect; noted so nobody "fixes" it.
5. A PM probe clicking a button by text hit the toolbar's asset **Delete** (confirm dialog) instead of the hierarchy Delete — the two share a label. Dismissed via Win32 Cancel; nothing deleted. Not a DotGen bug (Pyre's toolbar has the same pair), but a reason to keep "Delete" tooltips distinct — they are.

## 4. UI-Guide compliance pass over everything touched

Sections are green `Z.Section`s with stable keys; every enum is a radio (`Z.Segmented` ≤3 / `Z.MiniRadio`), no dropdown; every bounded scalar a `Z.MicroSlider` with a default; references are pickers (Selector rows); names are the one text field; tooltips on every control (T-0232 sweep); no on-screen instruction text (micro-headings moved to tooltips by T-0232); card headers do not wrap; anchor is a shared `Z.AnchorGrid`; the preview never moves when cards appear (right pane); ZuiAudit 0 findings on both documents with ExpandAll (T-0232). Residual drift: item 3 above (lone caret), and the Frame section's colour rows still put the toggle to the right of a full-width colour field rather than packing two per row — acceptable at 360 px, noted.
