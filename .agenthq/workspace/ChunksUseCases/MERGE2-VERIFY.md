# Merge 2 verification — T-0359, T-0360, T-0363, T-0364 (dev 3e0859f7, compiled clean)

Same rules as merge 1 (`MERGE1-VERIFY.md` preamble; `T-0366/VERIFY.md` shows how the last verifier drove the window). Test on UseCases recipes, the Scratch folder, or duplicates — never the owner's `Assets/Demos/ChunksDemo/*.asset` or `Assets/Zoetrope/Floating Disc.asset`. **New → Create calls `AssetDatabase.SaveAssets()`, which saves every dirty asset in the project** (it re-wrote two owner assets last time) — before any Create, check `git status` of the owner's Chunks/Zoetrope assets, and restore them with git afterwards if they were touched only by that save. A repair for this is in flight (R3); don't fix it yourself.

## T-0359 — preview honesty
1. Red Pyre + cyan tint: the frame on the stage shows darkened while it plays (matches a Play-mode burst).
2. Clear the Blast picker: nothing drawn for that card. A lone Single blast takes roughly a third of the stage.
3. Fracture with a readable source: the real pieces fly apart on the stage. Both sources cleared: stage empty for it, card line "Nothing to cut: no source. A Zoe that fires this cuts its own frame." Unreadable sprite: the card says Read/Write is off.
4. Splash with a readable sprite: dots in that sprite's colours from its footprint; swapping the sprite changes them. No sprite anywhere: white dots from the origin + the card line.
5. Seed 0 tooltips on Fling / Fracture / Splash / Debris explain "new roll every burst"; type 5 → the "Pins…" text. Stage tooltip mentions seed 0 when an unseeded card is shown.
6. Ring + Fling with upward bias: every arc peak stays inside the stage. Drag "On screen" slowly: no zoom-in during the drag, a glide after release. Dragging a blast disc holds the zoom.
7. Regressions: disc drag still sets Offset; card-coloured rims + firing numbers still show; Fling ticks still show; no console errors after closing the window or a script reload.

## T-0360 — compact card + Fixed/Range
1. A Pyre Blast in Single mode reads Blast → Pattern → Offset → Rotation → Scale/On screen → Tint/Alpha → Seed, with no Add-alternate row.
2. Switch to Line/Ring: Blast and Pattern don't move; the pattern rows (incl. Alternates) appear below. Back to Single with a pool already authored: the Alternates stay visible (no stranded data).
3. Right-click Scale / Alpha / Spin: a Fixed/Range menu; both modes drag correctly, one undo per drag; double-click reset still works. A 1–1 range opens as Fixed.
4. Every other MicroMinMax (Debris Scatter, Fracture, Splash, Fling cards) still looks and behaves as before.

## T-0363 — Palette Splash
1. Zoe-triggered burst (on `UC Floating Disc (walk copy)`, killed from code like the UC4 walk did) with the Splash's Source/Sprite empty: particles carry the Zoe's live colours and come from its footprint.
2. Source field: the picker offers the Floating Disc Zoe by name; with it set, a standalone burst uses its first frame.
3. Spread 180 on a standalone burst: a full 360° spray (runtime and preview agree).
4. One burst, one frame: a recipe with Fracture + Splash fired from the Zoe copy — the splash colours match the cut frame.
5. Behaviour change check: compare the owner's `Floating Disc Blowup.asset`, `Sparks.asset`, `Ring Blast.asset` bursts (standalone, Play mode, NOT saved) before-vs-now as far as you can tell from captures — report any whose Palette Splash spread visibly doubled (Spread < 180 now means twice the cone).

## T-0364 — Zoe row Private → Public
Only on `UC Floating Disc (walk copy)` (or a fresh duplicate of it).
1. Make a Spawn Chunks row Private, then click Public: the row shows a new non-empty library asset; a new `.asset` exists under `Assets/Chunks` with the same tuning; console logs "[Zoetrope] … made public: <path>".
2. **Undo concern (PM):** press Ctrl+Z once after step 1. Report exactly what the row points at afterwards (the old embedded recipe? nothing/missing?), and whether the library asset still exists with correct data. A row left pointing at a destroyed sub-asset is a FAIL.
3. Toggle Public → Private → Public a few times: no console errors, no broken references.

## Also
- Console clean through the walk. Report pass / fail (capture) / untested per check; post failures as todo messages on the owning task.
