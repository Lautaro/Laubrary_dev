# Chunk asset inventory — T-0081

Scope: every `ChunkSpec` asset in the project, everything under `Assets/Chunks/`, and everything under `Assets/Demos/ChunksDemo/`. `ChunkSpec.cs` lives at `Assets/Packages/Laubrary/Runtime/Chunks/ChunkSpec.cs` (script guid `187d847a7ab02394ebe5d6f4ead62de0`). Searching the whole `Assets` tree for `.asset` files whose `m_Script` guid matches that value found exactly 8 `ChunkSpec` assets — no more exist anywhere, including `Assets/Packages/Laubrary/Samples~/` (that folder is empty of ChunkSpec assets; it doesn't even contain any `.asset` files). Two of the 8 live outside the two folders named in the task (one per other demo) and are confirmed still in active use by their own demos, so they are out of scope for deletion but are listed for completeness.

## Table

| Asset | Path | Referenced by | Verdict |
|---|---|---|---|
| Sparks | `Assets/Demos/ChunksDemo/Sparks.asset` | `ChunksDemo.unity` (radialSpec, left-click), also appears in `GalleryDemo.unity` and `Assets/_Recovery/0 (2).unity` | KEEP |
| WallDebris | `Assets/Demos/ChunksDemo/WallDebris.asset` | `ChunksDemo.unity` (directionalSpec, Space key) | KEEP |
| SampledDebris | `Assets/Demos/ChunksDemo/SampledDebris.asset` | `ChunksDemo.unity` (sampledSpec, T key) | KEEP |
| Floating Disc Blowup | `Assets/Demos/ChunksDemo/Floating Disc Blowup.asset` | `ChunksDemo.unity` (composedSpec, C key) | KEEP |
| Composed | `Assets/Demos/ChunksDemo/Composed.asset` | none found anywhere in `Assets` (scenes, prefabs, assets, scripts) | DELETE |
| Floating Disc Debris | `Assets/Chunks/Floating Disc Debris.asset` | none found anywhere in `Assets` | DELETE |
| FloatingDiscBlowup.png | `Assets/Demos/ChunksDemo/FloatingDiscBlowup.png` | none found (not referenced by guid or filename anywhere) | DELETE (orphan art) |
| FloatingDiscBlowup_Authoring.png | `Assets/Demos/ChunksDemo/FloatingDiscBlowup_Authoring.png` | none found | DELETE (orphan art) |
| FloatingDiscBlowup_Sequence.png | `Assets/Demos/ChunksDemo/FloatingDiscBlowup_Sequence.png` | none found | DELETE (orphan art) |
| ArenaDebris | `Assets/Demos/ArenaDemo/ArenaDebris.asset` | `ArenaDemo.unity` | OUT OF SCOPE — keep, belongs to a different demo |
| Debris | `Assets/Demos/ColosseumDemo/Debris.asset` | `ColosseumDemo.unity`, `GalleryDemo.unity`, `Assets/_Recovery/0 (2).unity` | OUT OF SCOPE — keep, belongs to a different demo |

`Assets/Chunks/PyreSpawnSources` does **not** exist on disk — it's only a picker create-location string in `Assets/Packages/Laubrary/Editor/Chunks/ChunkWindow.PyreSpawn.cs` (lines 77, 348) that would create the folder the first time a user saves a new spawn source from that picker. No leftover folder to clean up.

`Assets/Chunks/` currently contains exactly one file pair: `Floating Disc Debris.asset` + its `.meta`. Nothing else is in that folder.

## Per-asset detail (the four the demo actually fires)

**Sparks** (`radialSpec`, fires on left-click anywhere): a fast, bright radial "pop" — 12–22 chunks launching in a full 180° cone (`spreadDeg: 180`) at 3.5–8 units/sec, snappy gravity (24) and quick lifetime (0.6–1.2s), tinted from warm white through orange via `colorOverLife` (classic spark-fade look), settling on a floor at y=-3.5. Pure procedural pixel-square debris (no sprites, no Chunks 2.0 modules) — the simplest of the four. Name is accurate: it reads as sparks.

**WallDebris** (`directionalSpec`, fires on Space at a fixed wall origin, direction 60°): a tight directional cone (`spreadDeg: 32` around 60°) of 8–14 grey-blue chunks (`colorOverLife` goes light-blue-grey → darker blue-grey) that fan up-and-out from a wall point and rest on the floor at y=-3.5 with friction 0.6. Also plain procedural debris, no Chunks 2.0 modules. Name is accurate — reads as masonry/wall-fragment debris.

**SampledDebris** (`sampledSpec`, fires on T at the mouse): samples 8–24px chunks directly out of a runtime-built demo sprite's own pixels (via `DemoSprites`, wired in code, not in the asset) and tumbles them (squash+shade, `tumbleShadeStrength: 0.7`) for a pseudo-3D look; larger and longer-lived than the other two (size 0.35–0.6, life 1.2–2s), resting on a floor at y=-3. No Chunks 2.0 modules. Name is accurate.

**Floating Disc Blowup** (`composedSpec`, fires on C at the "Floating Disc" character in the scene): the fully-authored Chunks 2.0 composite. `fragmentSlicer` is enabled and cuts the actual `Assets/Zoetrope/Floating Disc.asset` visual into 5 large recognisable pieces that fly apart and settle. `pyreSpawn` (label "Background") is enabled and fires the Pyre blast `Old School Explo 2 Plus` behind everything. `blastGroups` adds two more Pyre-blast layers using `Proper Blast`: one "Between" the fragments and one "In Front" of them, each placed via a ring `spawnFormation` and staggered. `layers` orders four named slots (Back Blast → Between → Fragments → In Front) and `timeline` (enabled) staggers when each track fires (Pyre Spawn immediately, Fragments +0.08s, Between +0.26s, In Front +0.46s). Net effect: the disc visibly cracks into chunky pieces with an explosion glowing behind it and two more explosion flashes weaving between and in front of the debris — a rich, deliberately layered "character blows up" effect. Name is accurate and specific (names the exact character + effect); this is the asset referenced in the task description as "the C key fires the Floating Disc Blowup recipe."

## Per-asset detail (the orphans)

**Composed** (`Assets/Demos/ChunksDemo/Composed.asset`): sits in the same folder as the four live specs and looks superficially similar to Floating Disc Blowup — it has `particleSplash` enabled (a "Splash" layer, 6–14 small particles) and `fragmentSlicer` enabled (3 generic pieces, no `sourceVisual` set, `source: {fileID: 0}` — i.e. it has no visual to slice, so `fragmentSlicer` would produce nothing meaningful if fired), plus `useFloor: true`. `pyreSpawn`, `pyreMotion`, `spawnFormation` and `timeline` are all present but **disabled**, and `blastGroups` is empty. Reads as an earlier, unfinished draft of the composed-effect idea — superseded once `Floating Disc Blowup` was authored with a real source visual and real blast groups. Not referenced by any scene, prefab, or script. Name doesn't lie exactly (it is "a composed spec"), but it's indistinguishable from Floating Disc Blowup by name alone and is dead weight now.

**Floating Disc Debris** (`Assets/Chunks/Floating Disc Debris.asset`): a plain (non-Chunks-2.0) spec that samples pixels from the same `Floating Disc.png` sprite used elsewhere and tumbles them (10–18 chunks, `tumble: true`, sample px 2–5, `useFloor: false`) — essentially a smaller-scale "sampled debris from the disc" idea, conceptually similar to `SampledDebris` but hardcoded to the disc sprite instead of the runtime `DemoSprites` shape. Not referenced by any scene, prefab, or script — nothing in the project fires it. It also lives directly under `Assets/Chunks/`, a folder with no other content and no obvious ownership (not the demo folder, not a package folder) — it reads like a scratch/test asset left over from developing the tool. Name is accurate about what it does, but it's an orphan.

**The three PNGs** (`FloatingDiscBlowup.png`, `FloatingDiscBlowup_Authoring.png`, `FloatingDiscBlowup_Sequence.png`): none is referenced by GUID or by filename anywhere in `Assets` — not by `Floating Disc Blowup.asset` (which sources its visuals from `Assets/Zoetrope/Floating Disc.asset` and Pyre blast assets, not these PNGs), not by the demo scene, not by any script. The naming pattern (`_Authoring`, `_Sequence`) strongly suggests these were reference/preview images generated while designing the Floating Disc Blowup effect (an authoring snapshot and a baked-sequence strip) rather than assets Unity actually loads at runtime. They are pure orphans by reference-search, though they may have documentation value to a human — flagged as UNCERTAIN below rather than an outright delete recommendation, since a human glance at the images may tell you they're worth keeping as reference art.

## RECOMMENDATION

**KEEP (used by the demo):**
- `Assets/Demos/ChunksDemo/Sparks.asset` — left-click radial "pop"; name conveys it correctly.
- `Assets/Demos/ChunksDemo/WallDebris.asset` — Space-key directional wall burst; name conveys it correctly.
- `Assets/Demos/ChunksDemo/SampledDebris.asset` — T-key sampled pseudo-3D tumble debris; name conveys it correctly.
- `Assets/Demos/ChunksDemo/Floating Disc Blowup.asset` — C-key full composed character-blowup (fragments + 3 layered Pyre blasts, staggered timeline); name conveys it correctly and specifically.

**DELETE (orphaned / weird / superseded):**
- `Assets/Demos/ChunksDemo/Composed.asset` — unreferenced draft, superseded by Floating Disc Blowup, half-configured (fragmentSlicer has no source visual).
- `Assets/Chunks/Floating Disc Debris.asset` — unreferenced scratch/test spec; also empties out the otherwise-pointless `Assets/Chunks/` folder entirely (nothing else lives there).

**UNCERTAIN:**
- `Assets/Demos/ChunksDemo/FloatingDiscBlowup.png`, `FloatingDiscBlowup_Authoring.png`, `FloatingDiscBlowup_Sequence.png` — all three are unreferenced by any GUID or path string anywhere in the project, so by pure reference-graph logic they're dead weight and safe to delete. What makes them uncertain rather than a clean DELETE: their names suggest they may be intentional reference/documentation images (an "authoring" snapshot and a "sequence" strip) kept for a human to look at when working on the effect, not assets meant to be loaded by Unity at runtime — that judgment call needs a human glance at the actual images, which this read-only inventory did not make (no image content was rendered/opened, only reference-searched).

No leftover `Assets/Chunks/PyreSpawnSources` folder exists — it's currently just a string constant in the picker code, never actually created on disk, so there is nothing to delete there.
