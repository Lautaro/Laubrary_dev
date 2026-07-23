# Cartographer — design notes & task list

**Status (2026-07-16)**: design phase done, Phase 0 (scaffolding) landed. Moved into this canonical Laubrary
repo from a working copy that had been developing inside Asteroid+'s embedded Laubrary package (started
2026-07-11) — that copy also had the Scavenger half of Phase 2 landed; Scavenger has NOT been ported over yet
(see the Phase 2 note below) — everything else is not started. Start here, then read the phase list at the
bottom.

A standalone Laubrary tool for building arcade-style retro pixel 2D levels — top-down or side-scrolling.
First consumer is **OutBurner** — a SEPARATE Unity project at `D:\Unity\OutBurner`. In OutBurner it will live
in its own separate scene, not integrated with the racing scene/car — a distinct mode/module within the game,
not a reskin of the track system.

Modeled directly on OutBurner's existing track-generation precedent (all paths below are inside
`D:\Unity\OutBurner`, NOT this project):
- `Assets/OutBurner/Stage/RoadZoneDefinition.cs` — the biome precedent: a named bundle of theming/assets/
  rules (parallax, road palette, ground textures, decoration list), CRUD'd via `Assets/OutBurner/Editor/
  BiomeEditorWindow.cs`, which instantiates the REAL runtime components in a hidden preview scene so the
  editor and gameplay share identical code paths — carry that "preview == runtime" principle into
  Cartographer's own authoring tools, don't build a separate lightweight preview renderer.
- `Assets/OutBurner/Stage/StageGenerator.cs` + `Assets/OutBurner/Stage/StageParams.cs` — the deterministic
  seed+dials procgen precedent (seeded `System.Random`, same seed+dials always produces the same result).
  `Assets/OutBurner/Stage/DecoEntry.cs` contains `BiomeDecor.Place`, the scatter/line placement algorithm
  Cartographer's own clump-scattering should mirror.
- `Assets/OutBurner/Editor/TrackDesignerWindow.cs` — the live-preview authoring-window precedent (dials on
  the left, live 3D belt preview on the right, regenerates on every dial change).
- `Assets/OutBurner/ROADMAP.md` explicitly flags this seed+dials pattern as meant to generalize beyond
  racing — this tool is that generalization, not a tangent.

Cartographer ports that philosophy onto a 2D tile grid.

## Confirmed decisions

- **Unity's native Tilemap** (`UnityEngine.Tilemaps`), not Tiled, for v1. Reasoning: procgen is a hard
  requirement, and Tiled would force two divergent authoring paths (hand-painted `.tmx` vs. procedurally
  generated data) that both have to land on the same runtime representation. Tiled import may be added later
  as an optional secondary path — not a launch blocker either way.
- **One shared data model for top-down and side-scrolling.** Biome/Clump/Layer/Generator concepts are
  shared; collision setup differs per mode (`CompositeCollider2D` for solid top-down terrain vs.
  `PlatformEffector2D`/one-way platforms for side-scrolling) — planned for explicitly, not assumed automatic.
- **Clumps double as "structures."** One concept, not two asset types — a "structure" is just a clump placed
  on a different layer (Structures instead of Terrain).
- **Tags on clumps are open-ended**, not a fixed enum (user said "untraversable, damage, slows down, is
  starting platform, or whatever") — matches how Bestiarium treats `Faction` as data, not hardcoded. Exact
  mechanism (plain strings vs. a small tag-asset registry) still open — leaning tag-asset for
  discoverability/typo-safety.
- **Biome vs. Room — two distinct concepts, not one renamed.** A **Biome** is the reusable authoring asset:
  theming/tileset/rules (matches the OutBurner `RoadZoneDefinition` precedent above) — what tiles/clumps are
  valid, how it looks. A **Room** is a placed SECTION of an actual level: it has a Biome (which tileset/
  environment it draws from) plus its own EXIT CONDITION, independent of the Biome itself. A Room's exit
  condition can be distance-based (travel N units), time-based (survive/hold for N seconds while enemies spawn
  per the Biome's own rules — "you will be passing this room for X seconds and infinite enemies spawn
  according to this biome's rules"), or kill-based (stay until a specific enemy set is cleared). Multiple Rooms
  can share the same Biome (e.g. several distinct timed rooms all drawing "Forest" tiles/rules).
- **Procgen v1 = deterministic seeded scatter/placement**, reusing OutBurner's `BiomeDecor.Place` pattern —
  NOT a full wave-function-collapse/constraint solver. Revisit only if scatter proves genuinely insufficient
  once it's actually in use.
- **Procgen levels regenerate from seed at runtime** (no baked procgen asset); hand-authored levels are saved
  as real Tilemap-backed scenes/prefabs. Two sources, one runtime representation.
- **Live preview is a first-class requirement for every authoring surface** (rule tiles, clumps, procgen
  dials) — same "preview == runtime" principle as Pyre and `BiomeEditorWindow`'s live rig. Changes to a rule/
  clump/recipe should be instantly visible in-editor, not require a bake-and-check step.

## Open decisions (revisit before/while implementing)

- Tag mechanism: plain strings vs. a tag-asset registry.
- Whether Clumps are authored as concrete tile references tied to one biome, or abstract "tile role" patterns
  resolved per-biome at use time (started simple/concrete for v1; abstract version is a possible v2 — lets a
  clump's SHAPE be reused across biomes with different sprites filled in, at the cost of an extra indirection
  layer to build and author).
- Whether to build a custom category-aware `RuleTile` subclass (see below) now or defer until the basic
  Biome/Clump/Generator loop is proven.
- **Scavenger dependency for sprite sourcing (see Phase 2) is currently UNRESOLVED.** Scavenger (sprite-sheet
  download + cherry-pick tooling, extracted from Asteroid+'s local Zoetrope-equivalent Animation Builder) still
  lives only in `D:\UNITY\Asteroid+\Packages\Laubrary\Editor\Scavenger` — it was NOT ported over with
  Cartographer, because it's actively load-bearing there (Asteroid+'s own embedded Zoetrope Animation Builder
  depends on it; deleting it without a replacement plan would break that project). Canonical Laubrary Dev
  currently has only the PRE-extraction ancestor of that code embedded inside `Editor/Launimator` (different
  namespace `Laubrary.Launimator.Editor`, different asset paths, `internal` visibility, hard-depends on
  `RegionSlicerPersistence` instead of taking delegates) — it has not been refactored into a standalone
  Scavenger module here. Before building Cartographer's Clump Stamper (which needs sprite sourcing), decide:
  port Scavenger here properly (and likely refactor Launimator to depend on it, mirroring what Asteroid+'s
  Zoetrope already did), or have Cartographer's sprite sourcing depend on Launimator's existing (internal,
  differently-shaped) tooling instead. Don't silently duplicate a third copy.

## Room / scroll / camera needs (2026-07-16, not yet designed — raw notes)

For side-scrolling levels specifically, captured before any implementation:

- **Infinite scroll** must be possible.
- **Room exit conditions** (see Biome vs. Room above): distance, time-with-continuous-spawns, or kill-all.
- **Scroll behavior is independent of the Room's exit condition** — any Room type could pair with: the scene
  autoscrolling (repeating/regenerating the Room's Biome content until the exit condition is met), the scroll
  staying put until done, or the player being able to push the screen while the Room still only ever generates
  its own Biome's content regardless of how far they push.
- **Camera modes**, need to support switching between:
  - **Rail** — camera follows a fixed, authored path, independent of player position.
  - **Free** — player moves freely; camera can optionally apply a "player lock" (name TBD) — a clock-position
    anchor for where the player sits on screen relative to scroll direction. 12 o'clock = player pinned near
    the LEADING edge of scroll (shows more of what's ahead); 6 o'clock = player pinned near the TRAILING edge
    (shows more of what's behind). Alters how much lead-space vs. behind-space is visible, and presumably
    reacts to the player's own movement direction, not just a fixed setting.
  - **Focus** — camera locks onto a specific point/target until released, independent of player position (e.g.
    a boss fight where the camera focuses on different ground installations that must be destroyed in a
    specific order).

## Functional clumps (special-function tiles)

A clump needs to carry more than visuals:
- e.g. a "landing pad" clump that triggers a placed prefab interacting with the player.
- e.g. a clump reserving space for "a destructible enemy spawner chosen from a predefined set of
  alternatives" — resolved at generation time; a natural fit for procgen (ties into Randomizers' weighted-
  choice tooling for the "pick one of N alternatives" part).

Needs: an optional hook on a Clump (or per-cell within a clump) referencing a prefab, or a weighted LIST of
prefab alternatives, to instantiate/bind when the clump is placed — plus some interaction-callback surface
for gameplay code to hook into. Exact shape TBD; the prefab/interaction specifics are inherently game-
specific, so this likely follows Lazor's established split (`Packages/Laubrary/Runtime/Lazor` = Shapes-free
core owned by Laubrary; the project supplies its own binding, e.g. this project's
`Assets/Scripts/Lazor/LazorCharacterView.cs`): Laubrary owns the clump/slot concept, the consuming project
supplies the actual prefab bindings and interaction behavior.

## Interchangeable tile groups / variant tiles (researched — confirmed feasible in vanilla Unity)

Question asked: does Unity's Tilemap support a group of tiles as interchangeable (e.g. 3 roof-tile versions
used randomly for visual diversity), including the harder case where BOTH roof-interior tiles AND roof-edge
tiles (which follow adjacency rules) each independently have several random variants?

- Unity's `RuleTile` already supports "N random variants for one matched condition" via its Random output
  mode — one rule matches a condition (e.g. "surrounded by the same tile") and picks randomly among that
  rule's own sprite list. This alone covers "3 roof tile versions, used randomly."
- The harder combined case works out of the box too: rule-matching (which determines interior vs. edge) and
  variant-picking (random among the MATCHED rule's sprites) are independent, orthogonal axes in RuleTile's
  model. Author an edge-detection rule with 3 edge sprites in Random mode, and an interior rule with its own
  2-3 sprites in Random mode — no custom code needed for this part.
- Real limitation: RuleTile's neighbor matching is normally by exact `TileBase` reference (or generic any/
  none), not by a shared CATEGORY — so a "roof" rule checking "is my neighbor part of the roof family
  (interior OR edge)" needs each sibling tile listed explicitly. Workable but manual.
- Planned improvement (not required for v1, but a strong quality-of-life win): a custom `CartographerRuleTile`
  subclassing Unity's `RuleTile`, overriding the neighbor-match check to compare a category/family tag
  instead of exact tile reference — makes "any tile in this family" checks first-class instead of requiring
  manual sibling-listing. Prioritize once the basic Biome/Clump/Generator loop works.

## Phased task list

### Phase 0 — scaffolding
- [x] `Runtime/Cartographer` + `Editor/Cartographer` asmdefs created (empty references, matching Larder's
  minimal-start pattern — add references as real code needs them).
- [x] This doc.
- [x] Moved into canonical Laubrary Dev from the Asteroid+ working copy (2026-07-16).

### Phase 1 — core data model
- [ ] `CartographerBiome` (ScriptableObject, `LaubraryAssetWindow<T>`-based CRUD window) — tileset refs, which
  Clumps are valid in it, theming (tint, parallax/background refs).
- [ ] `Clump` type — tile pattern (offsets + `TileBase` refs) + open-ended tags + optional functional hook
  (prefab or weighted prefab alternatives).
- [ ] Tag system (resolve the open decision above; implement).
- [ ] Runtime `Level`/`Map` component: a `Grid` + named `Tilemap` layers (Terrain, Structures, ...).
- [ ] `CartographerRoom` — a placed level section: references a Biome + an exit-condition spec (distance/time/
  kill-count) + a scroll-behavior spec (autoscroll-repeat / locked-in-place / player-pushable-content-locked).

### Phase 2 — manual authoring
- [ ] **Scavenger port** — still living only in `D:\UNITY\Asteroid+\Packages\Laubrary\Editor\Scavenger` (see
  the Open decisions note above); NOT yet ported to this repo. Resolve the Launimator-overlap question before
  porting, not after.
- [ ] Clump Stamper tool — pick a Clump, click to place it onto a Tilemap, rotate/mirror. The actual new
  value Cartographer adds over vanilla Unity (Tile Palette only handles single tiles, not multi-tile stamps).
  Sprite sourcing (download a sheet, cherry-pick tiles out of it) should reuse Scavenger once it's ported,
  rather than rebuilding it a third time.
- [ ] Biome CRUD window (AssetKit-based, live preview).
- [ ] `CartographerRuleTile` (category-based neighbor matching) — once the basics work.

### Phase 3 — procgen
- [ ] `LevelRecipe`/`GenParams` (seed + dials, mirroring `StageParams`).
- [ ] Generator: deterministic terrain fill + clump scatter (mirroring `BiomeDecor.Place`).
- [ ] Live preview for the generator window — regenerate + redraw on dial change, same principle as
  `TrackDesignerWindow`.
- [ ] Functional-clump resolution at generation time (weighted alternative picking via Randomizers).

### Phase 4 — collision / gameplay-mode split
- [ ] Top-down collision setup (`CompositeCollider2D`-based).
- [ ] Side-scrolling collision setup (`PlatformEffector2D`, one-way platforms, slopes).
- [ ] Gameplay-tag → collision/behavior wiring (project-side binding, mirroring Lazor's core/binding split).

### Phase 5 — scroll & camera (side-scrolling)
- [ ] Room exit-condition runtime (distance / time-with-spawns / kill-all) driving Room transitions.
- [ ] Scroll-behavior implementations: autoscroll-repeat, locked-in-place, player-pushable-content-locked.
- [ ] Camera rail mode.
- [ ] Camera free mode + "player lock" clock-position anchor (name TBD), reacting to movement direction.
- [ ] Camera focus mode (lock onto a target point/sequence until released).

### Phase 6 — OutBurner integration
- [ ] New, separate scene in OutBurner using Cartographer.
- [ ] First real Biome + Clump set authored for OutBurner's retro pixel art style.

## Before extending this tool

Read the `laubrary` skill's `references/authoring.md` first (placement/asmdef/demo-scene/versioning rules —
standing rules for every Laubrary tool, not just this one). Verify compile after every change
(`check_compile_errors`) and screenshot-verify any editor window before calling a step done.
