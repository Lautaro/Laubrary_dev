# Cartographer — design document

> **Status: AUTHORITATIVE. Written 2026-07-31 from a first-principles brief by the project owner.** This supersedes `TODO.md` as the design of record. `TODO.md` is still worth reading for its *gotchas* and its "friction found by building a real level" notes — hard-won and still true — but where the two disagree about what Cartographer IS, this document wins.
> 
> **Written to be built by someone who has not seen the conversation.** Every decision states its reasoning, because a decision without a reason gets reversed by the next person who finds it inconvenient.

---

## 1. What Cartographer is

**A tile-based level editor.** You paint tiles from a tileset onto layers, place reusable multi-tile structures, drop free-floating sprites, and save the result as an asset the game plays on.

That is the whole of it. It is not a clump editor, not a procgen toy, and not a gameplay system. It authors levels.

### The requirements, as given

1. Tile-based level editor.
2. Tiles come from a **tileset**; tilesets belong to a **biome**.
3. **Layers** of tilesets.
4. A tileset may have **no tile = transparent**.
5. There may be a **background tile** (called a "default tile" in the original brief; renamed 2026-08-03 for what it does — it is the layer's background, not a fallback).
6. A tile may be a **group of interchangeable tiles** — painting picks one, with policies: random, round-robin, never-repeat-last, …
7. **Free sprites on layers** — behave like tiles but any size and shape.
8. **Reusable multi-tile structures** — a chunk of level you author once and stamp many times.
9. **Animation for everything** — a tile cycles through sprites.
10. **Metadata** on tiles, structures and layers, for gameplay (traversable / not, etc.). Cartographer *provides the metadata tools*; it does **not** interpret them.
11. The playable level is a **LauAsset**, with a **thumbnail showing the level itself** (or part of it, if it is long).
12. **Procgen and HYBRID levels** — fully generated maps, and maps where a hand-authored foundation is then filled out by generation rules. *Not required for the first build, but the model must not make it hard.* See §3.9, which is where that constraint actually bites.

---

## 2. Decisions taken, and why

### 2.1 Build on Unity's Tilemap. Do not write a renderer.

`UnityEngine.Tilemaps` already provides chunked rendering, culling, a coordinate system, collider baking (`CompositeCollider2D`, `PlatformEffector2D`), per-tile collider shapes, and — critically — **`TileBase.GetTileData` and `GetTileAnimationData`**, which are the exact hooks requirements 6 and 9 need. A custom renderer would mean reimplementing chunking and culling for no gain.

**What Unity does NOT give us, and we must own:** the level as an *asset*, tilesets, variant policies with deterministic resolution, per-cell metadata, free sprites, and the authoring window. That is where the work is.

### 2.2 In Unity, not Tiled

Assessed and rejected 2026-07-30, and the reasoning has only strengthened:

- **Procgen must land on the same representation as hand-authoring.** Tiled gives a second authoring path producing `.tmx` tiles that must then converge with generated levels — two sources, one runtime, forever.
- **Half our data is Unity object references** — tile assets, prefab hooks, tag assets, Reels. Tiled can carry strings; resolving them at import is a silent-failure machine.
- **Round-trip is the trap.** Import a level, edit it in Unity, re-export from Tiled: the Unity edits are gone. Aseprite avoids this only because *nothing downstream edits the PNG* — a level is not terminal in that way.
- **`preview == runtime`** is a first-class Laubrary principle. An external editor structurally cannot honour it.

Tiled's painting ergonomics are genuinely better than anything we will write. If that becomes the bottleneck, add a **one-way `.tmx` import** for raw tile geometry only — never a round trip.

### 2.3 The level is an ASSET; the scene instantiates it

Requirement 11 is explicit. It also fixes a real defect: today's level is a MonoBehaviour living in a scene, so it cannot be browsed, previewed, reused across scenes, or picked in a field.

- **`LevelAsset`** is the authoring truth and the thing a game references.
- **`LevelInstance`** (MonoBehaviour) takes a `LevelAsset` and builds the `Grid` + `Tilemap` layers + decals + colliders, in the editor (`[ExecuteAlways]`) and at runtime.
- Editing writes to the asset; the instance rebuilds. There is exactly one source of truth.

⚠️ This reverses `TODO.md`'s "hand-authored levels are saved as real Tilemap-backed scenes/prefabs". Deliberate, per the brief.

### 2.4 The window holds the tools; the Scene view is the canvas

Painting happens **in the Scene view**, at real size, over the real level — that is the only place a level can be judged. The **window** owns the level browser, the layer stack, the palette, the active tool and its settings.

This is what Unity's own Tile Palette does, what today's stamper already does well, and it is the half of the current tool worth keeping.

---

## 3. Data model

Namespace `Laubrary.Cartographer`. Names chosen to say what the thing is — the current tool's biggest usability failure is that its main window is named after an internal asset type.

### 3.1 `LevelTile : TileBase`  *(the atom)*

One tile. Owns its variants, its animation and its metadata, so requirements 4, 6, 9 and 10 are all answered by one type that Unity renders natively.

```
displayName
variants      : List<Sprite>        // 1 = a plain tile; N = an interchangeable group (req 6)
variantPolicy : Random | RoundRobin | NeverRepeatLast | Weighted
weights       : List<float>         // Weighted only
animation     : List<Sprite>, fps   // req 9; empty = static
colliderShape : None | Sprite | Grid
tags          : List<LevelTag>      // req 10, per-tile
```

**Transparency (req 4) is the absence of a tile.** An empty cell draws nothing. Do not invent a "blank tile" — Unity already has one and it is `null`.

#### The variant-resolution fork — read this before implementing

Two of the four policies depend on **order of painting**, not on position:

- **Random / Weighted** *could* be resolved at draw time from a hash of the cell position — stable, stores nothing.
- **RoundRobin / NeverRepeatLast** are meaningless without a sequence. There is no "previous cell" at draw time.

**Therefore: resolve the variant at PAINT time and store the chosen index per cell.** All four policies then work uniformly, the level is deterministic, a painted level never re-rolls itself when Unity refreshes a chunk, and an author can hand-override one cell's variant. Cost is one byte per painted cell, which is nothing.

⚠️ Resolving at draw time is the tempting shortcut and it is wrong — `GetTileData` is called on chunk refresh, so a random tile visibly *flickers* as the camera moves. Do not do it.

### 3.2 `Tileset`  *(the palette)*

```
displayName
tiles       : List<LevelTile>
backgroundTile : LevelTile   // req 5 — see below
```

**"Background tile" (req 5)** is deliberately supported in two places because the brief allows both readings, and they are not the same feature:

- **Tileset default** — the tile a new paint uses when the author has not picked one. A convenience.
- **Layer default** — a tile that *fills every unpainted cell inside the level's bounds*. This is the useful one: a floor layer needs no painting at all, and a level gains a base surface for free.

### 3.3 `Biome`

```
displayName
tilesets    : List<Tileset>     // req 2 — the palettes this biome offers
clumps      : List<Clump>       // which structures are legal here (gates procgen)
tint, background
```

A biome is *what a level may be built from*. The window's palette is filtered by the level's biome — that is the whole point of the association.

### 3.4 `Clump`  *(the reusable multi-tile structure — req 8)*

A reusable multi-tile structure — requirement 8. **This is what a Clump already is**, and it is the single best-designed thing in the current tool. It survives unchanged, name included.

```
displayName
cells         : List<{ Vector2Int offset, LevelTile tile, string layer }>
tags          : List<LevelTag>
prefabChoices : List<{ GameObject prefab, float weight }>   // resolved at place time
spots         : List<{ string name, Vector2 offset }>       // named points gameplay looks up
```

**Name settled: it stays `Clump`.** A rename to `Clump` was proposed because the brief said "Idk what Clumps are tbh" — but the owner's answer was that the name is fine once you know what it does. The confusion was never really the word; it was that the WINDOW was named after this type while claiming to be the level editor. Fixing the window's subject fixes the confusion, and renaming on top of that would churn every existing asset for nothing.

### 3.5 `Decal`  *(the free sprite — req 7)*

A sprite placed on a layer at any position, rotation and scale, **not bound to the grid**.

```
sprite | animation (List<Sprite>, fps)
position (float2), rotation, scale
layer, sortingOrder
tags   : List<LevelTag>
prefab : GameObject   // optional — this is what makes a decal a PROP rather than scenery
```

Kept as its own list rather than smuggled into the tile system, because a tile's identity is *"it occupies cell (x,y)"* and a decal's is *"it sits at this arbitrary transform"*. Forcing one to be the other makes both worse.

### 3.6 `LevelTag`  *(metadata — req 10)*

An **asset**, not a string. Discoverable in a picker, survives a rename, cannot become a typo. This is today's `ClumpTag` promoted to a first-class concept.

```
name, description, editorColour
```

Tags attach to **tiles, patterns, decals and layers**. Cartographer **never interprets a tag** — it stores them and answers questions:

```
IReadOnlyList<LevelTag> TagsAt(Vector2Int cell, string layer)
bool HasTag(Vector2Int cell, LevelTag tag)          // any layer
IEnumerable<Vector2Int> CellsWith(LevelTag tag)
```

That is the line the brief draws and it is the right one: *"provide tools for metadata"*, and let the game decide that `Untraversable` means an enemy will not path there. The one exception already proven useful is collision baking, where a `solidTag` **narrows** a solid layer — keep that, and keep it narrowing-only.

If key/value metadata is needed later, add `LevelTag` subclasses carrying typed payloads rather than a `Dictionary<string,string>` — typed data survives refactors, stringly-typed data does not.

### 3.7 `LevelLayer`

```
name
tileset      : Tileset        // req 3 — layers OF tilesets
backgroundTile : LevelTile    // fills unpainted cells in bounds (see 3.2)
sortingOrder, opacity, visible, locked
solid, oneWay, colliderShape, solidTag
tags         : List<LevelTag>
```

### 3.8 `LevelAsset`  *(the deliverable — req 11)*

```
displayName, biome, bounds : RectInt
layers     : List<LevelLayer>
paints     : List<{ string layer, Vector2Int cell, LevelTile tile, byte variant, Origin origin }>
placements : List<{ Clump pattern, Vector2Int origin_, int rotation, bool mirrorX, Origin origin }>
decals     : List<Decal>                                     // Decal carries Origin too
rooms      : List<Room>        // unchanged from today
```

**Both `paints` and `placements` are stored, and that is deliberate.** Single-tile painting is the primary interaction and must not be forced through a fake 1×1 pattern. Clump *placements* are recorded separately so a stamped structure keeps its identity — its tags, its prefab hook, its named spots, and the ability to rebuild or regenerate. A Tilemap alone remembers neither; that lesson is already burnt into the current tool and must not be un-learnt.

**Rebuild order:** layer background fill → pattern placements (in order) → single paints (so a hand touch-up always wins over the structure beneath it) → decals.

Storage is **sparse** (a list of painted cells), not a dense array. Levels are mostly empty, and a dense array of a 200×100×4 level is 80 000 entries of nothing.

### 3.9 Hand-authored, generated, and hybrid — the constraint to design for now

Requirement 12 is not being built yet, but it decides two things about the model **today**, and retrofitting either is expensive.

**(a) Authorship must be recorded per element, not per level.** A hybrid level is one where a human placed some things and a generator placed the rest — and regenerating must replace *only* the generated ones. So every paint, placement and decal carries:

```
origin : Authored | Generated
```

Regeneration is then `remove where origin == Generated, then run the rules`. Without that flag the only safe regeneration is "throw the level away", which is precisely what a hybrid level must not do. **One byte, added now, is the difference between hybrid being a feature and being a rewrite.**

**(b) The generator must write through the same calls the editor uses.** Today's `LevelGenerator` already gets this right — it is a *composer*, not a painter: every clump goes through `PlaceClump`, so a generated level carries the same placement records and rebuilds the same way as a hand-built one. **Preserve that property exactly.** The moment a generator writes tiles directly to a Tilemap, generated and authored levels become different kinds of thing and hybrid becomes impossible.

Two further shapes worth leaving room for, without building them:

- **Rules keyed on tags.** "Fill every cell tagged `Floor` that has no neighbour above with a wall" is the natural form of a fill rule, and it works because tags already exist on tiles, patterns and layers. This is the strongest argument for the tag system being real data rather than strings.
- **Reserved regions.** `Room.reservedAreas` already means "generation must leave this traversable". Generalise it to "this rect is authored, generation may not touch it" and the hybrid case has its escape hatch.

### 3.10 Thumbnail

`LevelAsset` implements `IVisualPreview` — mandatory for any browsable asset per authoring rule 10, and explicitly requested.

Extend the existing `CartographerPreview` (already rasterises tiles for Clump and Biome cards) to walk a level's layers in sort order and blit each cell's sprite into a texture. **For a level too large to read at thumbnail size, render a centred crop at 1:1 rather than scaling the whole thing to mush** — a legible corner identifies a level; a grey smear does not.

---

## 4. Authoring UX

### 4.1 The window — `Laubrary ▸ Cartographer`

A `ZuiAssetWindow<LevelAsset>`. **The level is the subject.** The browser lists levels with their thumbnails; opening one targets it for editing.

Boxes, top to bottom:

- **Level** — name, biome, bounds, and the scene `LevelInstance` it is currently previewing through.
- **Layers** — reorderable list: visibility, lock, name, tileset, background tile (a read-out; set from the Tileset box), sorting, collision flags. Active layer is what painting affects.
- **Palette** — the active layer's tileset, drawn as a sprite grid. Click to select the paint tile. A tile with variants shows a badge with its policy.
- **Clumps** — the biome's clumps as thumbnails; selecting one switches to the stamp tool.
- **Decals** — a sprite field plus the placed list.
- **Tool** — Paint / Erase / Rect fill / Line / Pick (eyedropper) / Stamp / Decal, plus tool settings (rotation, mirror, variant override).
- **Tags** — attach to the active layer; inspect what is tagged under the cursor.

### 4.2 The Scene view

The canvas. Ghost the tool's footprint at the hovered cell, click to apply, **drag to continue**, press-drag-release for a line or rect, Alt to erase, Ctrl to pick. Today's stamper already does the ghost, drag-repeat, line mode and alt-remove well — **port that interaction wholesale, it is the best-tested part of the current tool.**

Every edit routes through `Undo.RecordObject` on the `LevelAsset`. A level editor that cannot Ctrl+Z is not usable.

---

## 5. Runtime

- **`LevelInstance`** builds the level: creates the `Grid`, one `Tilemap` per layer, applies background fill + placements + paints, spawns decals (SpriteRenderers, or prefabs where a decal names one), resolves pattern prefab hooks, publishes named spots, and bakes colliders.
- **Collision** as today: per-layer `solid` / `oneWay` / `colliderShape`, composite for top-down, `PlatformEffector2D` for side-scrolling, `solidTag` narrowing a solid layer per cell.
- **Queries** for gameplay: `TagsAt`, `HasTag`, `CellsWith`, `TryGetSpot`, `WorldToCell`/`CellToWorld`.
- **Procgen** stays a *composer*: a generator emits `placements`/`paints` into a `LevelAsset` through the same calls the editor uses, so a generated level is indistinguishable from a hand-built one. Keep `LevelRecipe`; keep the "biome gates what may be placed" rule.
- **Rooms and `Arena`** are untouched by this work. They are runtime *progression* (exit conditions, scroll, camera), not authoring, and they already work.

---

## 6. What of today's Cartographer survives

**Keep — genuinely good, reuse directly:**

| Thing                                  | Why                                                                                                   |
| -------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `Clump` → `Clump`                    | The concept is exactly requirement 8. Cells + tags + prefab choices + named spots is the right shape. |
| `placements` + `RebuildFromPlacements` | Hard-won: a Tilemap forgets which structure painted what. Do not regress this.                        |
| `ClumpTag` → `LevelTag`                | Tag-as-asset over stringly-typed. Already the right call.                                             |
| `LevelLayer` + `BuildColliders()`      | Layer flags and composite/one-way/slope collider baking work.                                         |
| `solidTag` narrowing                   | Tags narrow a solid layer, never widen a non-solid one — one place to look when something blocks.     |
| `CartographerRuleTile`                 | Family-based neighbour matching for auto-tiling. Optional-package-guarded already.                    |
| `CartographerPreview`                  | Tile→pixels rasteriser; extend it to levels for the thumbnail.                                        |
| `LevelRecipe` / `LevelGenerator`       | Deterministic seed+dials procgen that composes through the same placement call.                       |
| Scene-view stamper interaction         | Ghost, drag-repeat, line mode, alt-remove. The best-tested part of the tool.                          |
| `RoomDirector`→`Arena`, rooms          | Orthogonal to authoring. Leave alone.                                                                 |

**Discard or rebuild — actively in the way:**

| Thing                                           | Why it must go                                                                                                                                                                   |
| ----------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| The window being `ZuiAssetWindow<Clump>`        | **The core defect.** You open a level editor and it asks which *clump* you want; the level is a field inside a box. Renaming the window did not fix this — the subject is wrong. |
| `CartographerLevel` as a scene MonoBehaviour    | Cannot be browsed, previewed, reused or picked. Requirement 11 makes it an asset.                                                                                                |
| No tileset concept                              | Tiles are referenced ad hoc by clumps. There is no palette, and no biome→palette link. Requirements 2 and 3 are simply absent.                                                   |
| No variants, no animation, no per-cell metadata | Requirements 6, 9, 10 do not exist in any form today.                                                                                                                            |
| No decals                                       | Requirement 7 does not exist.                                                                                                                                                    |
| Thin `CartographerBiome`                        | Holds tile refs and allowed clumps but is not wired to layers or the palette.                                                                                                    |
| No background tile                                 | Requirement 5 does not exist.                                                                                                                                                    |

**Honest summary: this is a rebuild that reuses perhaps 40% of the existing code.** The data-model instincts were right and the Scene-view interaction is good. The window, the level's home, and everything about tilesets/variants/animation/decals/metadata is new. **Do not try to evolve the current window** — start the window fresh with `LevelAsset` as its subject and port the stamper into it.

---

## 7. Build order

Each phase ends with something demonstrable. Do not start the next until the last is verified in the editor.

1. **Data model** — `LevelTile`, `Tileset`, `LevelTag`, `Clump`, `LevelLayer`, `LevelAsset`, `Decal`. **Include the `Origin { Authored, Generated }` flag on paints, placements and decals from the start** (§3.9) — it is one byte and it is what makes hybrid levels possible later instead of a rewrite. No UI. Verify by constructing one in code and asserting it serialises.
2. **`LevelInstance`** — build a hand-constructed `LevelAsset` into a scene: grid, layers, background fill, paints, placements, decals, colliders. This is the moment the whole model is proven.
3. **Variant resolution** — paint-time resolution with all four policies, stored per cell. Verify determinism: the same level rebuilt twice is identical, and no cell re-rolls on a chunk refresh.
4. **Animation** — animated `LevelTile` via `GetTileAnimationData`; animated decals. Verify in play mode *and* in the editor.
5. **The window** — level browser with thumbnails, layer stack, palette. Selecting and inspecting only; no painting yet.
6. **Scene-view painting** — port the stamper: paint, erase, line, rect, pick, stamp, decal. Undo on every edit.
7. **Metadata tools** — tag assets, attaching, and the query API. Plus a Scene-view overlay showing tagged cells, because invisible metadata is unverifiable metadata.
8. **Procgen** — port `LevelRecipe`/`LevelGenerator` onto the new model, composing through the same calls the editor uses, and writing `Origin.Generated`. Hybrid regeneration ("remove generated, re-run rules, leave authored alone") falls out of that and should be demonstrated even if no rule set ships yet.
9. **Migration** — convert `Warehouse Room` (89 placements, 4 patterns) to a `LevelAsset`, repoint `ScavengeGame`, delete the old `CartographerLevel` component.

---

## 8. Decisions (were open questions — all answered 2026-07-31)

1. **The structure type stays `Clump`.** See §3.4. No rename, no asset churn.
2. **Decals belong to a layer** and inherit its sorting, so "behind the wall layer" is expressible. §3.5.
3. **Level bounds are an explicit `RectInt` on the asset.** A default-fill layer needs to know where to stop and a thumbnail needs to know what to frame; auto-growing bounds make both ambiguous. §3.8.
4. **Tile animation: plain `Sprite[] + fps` in core, with a Reel-backed variant in a bridge module.** Core Cartographer must not depend on Launimator; a project that has Launimator gets the Animation Builder for authoring tile animations through the bridge. Same shape as every other Laubrary bridge.
5. **One `Grid` per level for now — and a correction worth knowing.** The concern raised was that an L-shaped corridor would mean "a huge tilemap that is mostly empty". **It would not cost anything: Unity's `Tilemap` is already chunked and sparse** — it allocates only the chunks that contain tiles, so an L-shaped level pays for the L, not for its bounding box. Multiple grids are therefore *not* needed for sparsity. They would only be needed for **different cell SIZES in one level**, which stays out of scope: differently-sized things are multi-cell Clumps or Decals, and Decals now cover the free-size case properly. Revisit only if a real level wants two grid resolutions at once.
6. **The level asset owns its rooms.** Vocabulary, since the brief flagged it as unclear:
   - A **Unity scene** is what you press Play on — camera, player, game systems, and one or more level instances.
   - A **Cartographer level** is the *environment*: an asset that can be persisted, duplicated, edited, browsed and previewed, exactly as requested. It is not a scene and does not contain the player.
   - Rooms describe the pacing *of that environment*, so they live on the level and a level asset is playable as-is. If one level ever needs two different pacings, override rooms on the `LevelInstance` rather than moving them out.

---

## 9. Before building

Read the `laubrary` skill's `references/authoring.md` (placement, asmdefs, demo content, versioning) and `references/ui-layout-rules.md` **in full** — the window is a substantial piece of UI and the layout rules are mandatory, not advisory. Note especially:

- **No menu item may generate assets** (authoring rule 8). Demo content is committed files.
- **Never put a tool's main item alone in a submenu** (`UNITY_DEV_GUIDE.md` rule 7).
- **Visual preview is mandatory** for any asset a tool lets you pick or browse — `LevelTile`, `Tileset`, `Clump` and `LevelAsset` all need `IVisualPreview`.
- Verify compile after every change, and **eyeball the window** — a headless probe cannot catch a paint bug.
