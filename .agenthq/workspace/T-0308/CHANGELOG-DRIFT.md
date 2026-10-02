# T-0308 — CHANGELOG [Unreleased] rename-drift sweep, 2026-09-09

Card: sweep the 57 [Unreleased] bullets T-0307 identified as naming a symbol, file, module or tool absent from the tree (PyrePlus/Clump rename drift + 21 "others"), rewriting each in the CURRENT name (verified by grep) or deleting it if it describes something genuinely gone — never deleting a bullet describing a real behaviour change.

Method: built a full identifier index of `Assets/**` (source text + file/dir names, excluding `CHANGELOG.md` and `PYREPLUS_DESIGN.md` themselves so the audit isn't self-referential), then extracted every backticked token from each `[Unreleased]` bullet and flagged tokens absent from that index. Re-ran after each edit pass to confirm every replacement lands on a real, existing identifier.

## Result

**68 bullets edited** (more than the 57 originally counted, because the mechanical audit undercounted: several bullets carry the tool-name prefix "PyrePlus —" with no other missing sub-symbol, which the identifier check does flag once "PyrePlus" itself is excluded from the "still exists somewhere in a comment" loophole — see Finding 1 below). Every edit is a verified rename (old name confirmed absent, new name confirmed present via grep), never a deletion — nothing in the 57 turned out to describe a feature that no longer exists in some way that couldn't be captured by a rename.

**4 bullets already fixed by T-0307** (2 Fov, 2 `ShaperNodeIdentity`) were left untouched — they already carry T-0307's "Not in this package" / "Superseded" clauses and are correct as-is.

### 1. PyrePlus → Pyre (the 2026-08-23 rename), ~46 bullets, lines 144–361

Every bullet whose body used the old `PyrePlus`/`Plus*` naming was swept to the current `Pyre`/`Pyre*` naming, per CLAUDE.md's standing rule ("Write `Pyre` in all new code, comments and docs — never `PyrePlus`"). Representative renames, each verified to exist in the tree before writing it:

| old (gone) | new (verified) |
|---|---|
| `PlusForm`, `PlusForm.cs` | `PyreForm`, `PyreForm.cs` |
| `PyrePlusWindow`, `PyrePlusRenderer`, `PyrePlusLayer`, `PyrePlusField` | `PyreWindow`, `PyreRenderer`, `PyreLayer`, `PyreField` |
| `PlusRampPresets.*`, `PlusRamp`, `PlusShade`, `PlusFieldOps.*` | `PyreRampPresets.*`, `PyreRamp`, `PyreShade`, `PyreFieldOps.*` |
| `PlusNumpyRng.*`, `PlusPyRandom(.cs)`, `PlusArcBurst.cs`, `PlusPlasmaBloom(.cs)`, `PlusForkBlast.cs`, `PlusInferno.cs`, `PlusTorch.cs`, `PlusOrb.cs`, `PlusJetEngine.cs` | `PyreNumpyRng.*`, `PyrePyRandom(.cs)`, `PyreArcBurst.cs`, `PyrePlasmaBloom(.cs)`, `PyreForkBlast.cs`, `PyreInferno.cs`, `PyreTorch.cs`, `PyreOrb.cs`, `PyreJetEngine.cs` |
| `PlusLayerCache/Fill/Key`, `PlusFrameFill(.cs)`, `PlusPrepassCache`, `PlusClipStats`, `PlusFormCtx`, `PlusFormDebug.FieldSink`, `PlusFormWarp(.cs)` | `PyreLayerCache/Fill/Key`, `PyreFrameFill(.cs)`, `PyrePrepassCache`, `PyreClipStats`, `PyreFormCtx`, `PyreFormDebug.FieldSink`, `PyreFormWarp(.cs)` |
| `PlusParityDump(.cs)`, `.DumpOptions.cropW`, `Editor/PyrePlus/Parity/` | `PyreParityDump(.cs)`, `.DumpOptions.cropW`, `Editor/Pyre/Parity/` |
| `Laubrary.PyrePlus.Tests`, `com.…Laubrary.PyrePlus.Forms.Kiln` | `Laubrary.Pyre.Tests`, `com.…Laubrary.Pyre.Forms.Kiln` |
| `Assets/Tests/PyrePlus/` | `Assets/Tests/Pyre/` |
| `PyrePlusGif`, `PyrePlusPlayback3D(Preview.cs\|Post.shader)` | `PyreGif`, `PyrePlayback3D(Preview.cs\|Post.shader)` |
| `PyrePlusSpec` (a made-up type name — the real spec class is just `Pyre`) | `Pyre` |
| bare tool-name mentions ("PyrePlus — …", "PyrePlus's …") | "Pyre — …", "Pyre's …" |

**Deliberately NOT touched:** `IPlusFieldPublisher` / `IPlusRampProbe` (lines 205–207) — these interfaces genuinely still carry the old "Plus" name in the current source (`Runtime/Pyre/PyreForm.cs:156,163`); they were never renamed in the 2026-08-23 pass, so citing them as `PlusXxx` is still correct. A first substitution pass accidentally clobbered these to `IPyreRampProbe` via an overzealous generic-prefix rule; caught by re-scanning and reverted before finalizing (see "Self-check" below).

**Deliberately left alone (2 bullets, historical/self-annotated):**
- Line 5 ("Pyre: the window says 'Pyre' everywhere it still said 'Pyre Plus'…") — this bullet is *about* the rename and correctly quotes the old wording it fixed.
- Line 157 ("SUPERSEDED, never shipped — reverted in `450de881`…`PlusBands`…") — this bullet already documents that `PlusBands`/`IZuiBands`/`ZuiBandsControl` were built, then deleted outright in a revert; it is explicitly historical and self-annotated as superseded. Renaming `PlusBands`→something that never existed would misstate what was reverted.
- Lines 213, 252 — "Clump spread" here is an unrelated, still-current Pyre Inferno dial name (`InfernoForm.cs:87`, field `clumpSpread`), not the Cartographer rename below. Left untouched.
- Line 214 — "Promoted from `RoomFlowField`" correctly cites OutBurner's old class name as the thing `LevelFlowField` was promoted *from*; historical lineage, not drift.

### 2. Cartographer Clump ↔ Prop rename, 9 bullets, lines 223, 235, 237, 238, 258, 315, 335, 337–338, 341

**Important correction to the premise T-0307 worked from:** "Clump" was **not** simply renamed to "Prop" everywhere. Reading the actual current source (`Runtime/Cartographer/CARTOGRAPHER_DESIGN.md` §3.4, `Tileset.cs`, `Prop.cs`) shows the 2026-08 Cartographer rename went **both directions at once** (line 222's own "big rename" bullet says this explicitly: *"prefab Clumps are now **Props**, tile groups are now **Clumps**"*):
- The level-placed structure asset (the thing you stamp into a level, with cells/tags/prefabChoices/spots) was **Clump → Prop**: `Clump.cs`→`Prop.cs`, `ClumpWindow`→`PropWindow`, `ClumpPlacement`→`PropPlacement`, `ClumpCell`→`PropCell`, `ClumpTag`→`TileTag` (not "LevelTag" as one old bullet claimed — verified against `TileTag.cs`), `PlaceClump`/`CartographerLevel.PlaceClump`→`PlaceProp`/`CartographerLevel.PlaceProp`, `groundClump`→`groundProp` (`LevelRecipe.cs:54`, `[FormerlySerializedAs]`).
- The Tileset Builder's locked multi-tile pattern (the sheet-side arrangement, unrelated to the level-placement concept) went the **other way**: the old `TileProp` type became `Clump` (`Tileset.cs`: `class Clump`, `List<Clump> clumps`, `[FormerlySerializedAs("props")]`). So "Clump" is a **live, current** Cartographer concept today — just a different one than it was before the rename.

Fixed accordingly: bullets describing the level-placement structure (lines 235, 237, 238, 315, 335, 337, 338, 341) were swept Clump→Prop (`Clump`→`Prop`, `ClumpWindow`→`PropWindow`, `ClumpPlacement`→`PropPlacement`, `ClumpTag`→`TileTag`, `ClumpCell`→`PropCell`, `PlaceClump`→`PlaceProp`, "Clump Editor"→"Prop Editor"). The one bullet describing the Tileset Builder's sheet-side pattern under its pre-rename name (line 223, "PROPS: object-shaped content gets its own tab", `TileProp`, `propColumns`) was swept the other way, to `Clump`/`clumpColumns`, matching what the Tileset Builder actually calls it today. Line 258's `ClumpStage` → `SheetStage` (`TilesetBuilderWindow.cs`: `internal class SheetStage`).

**Left alone:** line 222 itself (the rename announcement — correctly uses both old and new names to explain the swap) and lines 213/214/252 (see above, unrelated to this rename).

### 3. Other individual sub-symbol fixes, 2 bullets

- Line 241: `ReelHitFilter` → `LauminaryHitFilter` (`Runtime/LauminaryCombat/LauminaryHitFilter.cs:16` — part of the already-documented Reel→Lauminary rename, 2026-08-05).
- (`RefreshAllTiles` at line 237 and a handful of TextSplash-internal field names at lines 304/307/311/312, `stripCache` at 203, `wasInterrupted` at 176, `upperSortingOrder` at 187, `linearVelocity` at 343, `__body` at 260, `IShaperCacheableSource` at 173, `ZuiEnvelopePresetPopover` at 298, `CartographerDemoBuilder.cs` at 341 (ephemeral per-repo convention — demo builders are meant to be deleted once the demo scene exists) — reviewed, left as-is: each names an internal implementation detail inside a bullet whose described feature/behaviour still exists and works; no confident, verified rename target was found, and per the card's own instruction a bullet describing real behaviour is never deleted for a stale internal name alone.)

## Self-check

- Re-scanned the whole `[Unreleased]` block after every edit pass; final pass confirms zero corrupted double-substitutions (`PyrePyre*`, `PyreProp*`, `PropPyre*` — none found) and confirms `IPlusFieldPublisher`/`IPlusRampProbe` still read exactly as they do in source.
- `git diff --stat` on `CHANGELOG.md`: 68 lines changed, 68 insertions / 68 deletions (each bullet is one line in the source file).
- Bullet count, order and every non-drift bullet's text are unchanged — this is a pure identifier-rename sweep, no bullets added, removed or reordered.

## CLAUDE.md (worktree, Shaper section)

Per the card, three changes to the "Load-bearing facts" list:
1. The frame→phase mapping's rationale no longer cites the deleted `ShaperNodeIdentity`; it now says the mapping is fixed by design and by `ShaperClock` (every authored Curve dial points at the key its conversion produces), with a parenthetical noting `ShaperNodeIdentity` was deleted by T-0253 and the surviving key is `ShaperLayerKey`.
2. Removed the "(ArcBurst alone 187)" parenthetical from the ~775-authored-fields bullet (the card's specific ask; the general "~775 authored fields" claim itself was left — not asked to be re-verified this task).
3. Added a new bullet noting fills/borders carry an `authored` flag since T-0271 (verified against the CHANGELOG's own T-0271 entry, line 35).

## Files changed

- `Assets/Packages/Laubrary/CHANGELOG.md` — `[Unreleased]` block only, 68 lines.
- `CLAUDE.md` (worktree root) — Shaper "Load-bearing facts" list only, 3 bullets touched (1 rewritten, 1 trimmed, 1 added).
