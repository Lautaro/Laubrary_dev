# Chunks editor — PM handover walk (T-0217, 2026-09-03 ~03:30)

Walked by the PM session (Fable 5.1) on the live editor for `D:\UNITY\Laubrary Dev` (dev @ 452c4d67), through a throwaway helper that drove the REAL window: real `New`/`Create`/`Add capability…` buttons, real menu rows, the sliders' own notifying path, `Undo.PerformUndo`, PrintWindow captures of the window plus a pixel hash of the stage. Helper and the walk's own recipe asset were deleted afterwards; tree is clean. Captures are in this folder (`NN-*.png`, with `*.stage.png` crops).

The user's sentence: *"I want to build a crate-smash chunk — some debris and a blast — and see roughly where things go and in what order, then watch it play."*

## Verified by eye (real window, real captures)

| Row | What I did | What I saw |
|---|---|---|
| 1.1 cold open | Opened the window with nothing selected | The library grid (7 recipes with thumbnails) and a `New` flow: name field, `Create`, `Cancel` (`01-cold-open.png`, `02-new-empty.png`) |
| 1.2 New | `Create` with the default name | An EMPTY recipe: no cards, one `Add capability…` button, preview stage with only the origin cross, no Timing section (`02-new-empty.png`) |
| 2.x Add menu | Pressed `Add capability…` | Nine rows; Trajectory/Trail/Hits greyed with no producer; after adding a Pyre Blast, Trajectory became available while Trail/Hits stayed greyed (`03-add-menu.png`) |
| 6.x Pyre Blast card | Picked Pyre Blast | One card: Name · Delay hidden (lone capability), Blast picker + Add alternate, Pattern Single/Line/Ring, Offset pad, Rotation, Scale range, On-screen, Seed (`04-one-blast.png`) |
| 12.x lone rule | One timed capability | No Delay dial, no Timing section; after adding Debris Scatter both Delay dials and the Timing lanes appeared, and the stage rect stayed byte-identical (`05-two-caps.png`) |
| 14.x transport | Pressed Play, captured six times ~1.5 s apart | Six different times, six different stage hashes, the clock wrapped under Loop; the button read `❚❚ Pause` while playing and `▶ Play` after pausing; Pause held the time for 1.5 s (`06-play-1..6.png`) |
| 3.x / 15.x dial | Paused at t=0.50, dialled Debris Spread 180→25 | The cone narrowed and the debris squares clustered inside it; stage hash changed (`12-t05-before.png` → `13-t05-spread25.png`) |
| 18.x undo/redo | Ctrl+Z / Ctrl+Y equivalents | Undo restored the exact pre-dial pixel hash; redo restored the dialled one (`14`, `15`) |
| 12.x reorder | ▲ on the second card | Stack order swapped (Debris Scatter first); stage rect unchanged |
| 12.x On | Switched the first card off and on | Card body inert, its lane kept (dim), stage rect unchanged (`16-first-off.png`) |
| 14.x dial mid-play | Dialled Spread while playing | Still playing afterwards, next frames reflected it |
| 19.x reload | Forced a domain reload with the Ring Blast open | Window came back on the same recipe with the same playhead time |
| 15.x demo | Opened `Ring Blast` and played | Five numbered ring points with trajectory arcs, the cue's tick on the ruler, four captures with four different hashes (`17-ring-1..4.png`) |
| 17.1 Mirage | Pressed `Preview in Mirage` | A Mirage window opened (not watched playing) |

## Verified by probe only
- Capability ids, delays and enabled flags read back from the asset after every edit; `ChunkClock` lengths (1.10 s two-capability recipe, 0.94 s Ring Blast) shown in the readout.
- The audit's numbers (T-0216): 153/156 fields have a control, 89/121 dials change the picture with all 33 exceptions explained, ZuiAudit 0 findings at both sizes, the ring-formation runtime bug fixed and re-verified in Play mode.

## Not verified
- **No human mouse yet.** Every drag (splitter, resize bar, pads, sliders, lane playhead) was synthesized or set by value. One-undo-per-drag is implemented (T-0216) but has not been felt.
- Mirage's own playback of the composed effect was not watched, only that it opened.
- Asset pickers were never operated through the browser (the harness cannot synthesize a pick); the walk assigned the Pyre through the card's own Dial path.
- Window minimum 820×480 was captured by T-0210, not re-walked here.

## Findings for the owner (not fixed in this programme)
1. **Reopening the window forgets the last recipe** — it comes back on the library. Pyre behaves the same; it is an AssetKit-level gap (`ZuiAssetWindow` never persists the last asset), so a fix there helps every tool.
2. **`ProbeChunk` (Assets/_T0075_Probe) still sits in the library** beside the real recipes, plus the legacy `ArenaDebris`/`Debris` — the browser-hygiene point from T-0119. Owner's call to delete.
3. The Ring Blast demo's card shows `Blast · none ·` with two alternates below it: the recipe was authored with an empty primary and a two-entry pool. It works, but reads oddly; either the primary should be required or the card should say "picked from the alternates" when the primary is empty.
4. A large Pyre (Green Lantern) draws as a disc that fills most of the stage; the disc radius follows the Pyre's preview size. Consider capping the schematic disc.
5. Long cue names truncate on the Timing ruler (`RingBla…`).
6. The T-0216 handover's own numbered list (Layer Plan sort fields, pool-pick order vs preview, `LauAssetElement` chip staleness at the AssetKit level, `ZuiViewBar`'s native dropdown, Trajectory fixed Life only showing when shorter than the blast, unlabelled layer name fields) stands.
