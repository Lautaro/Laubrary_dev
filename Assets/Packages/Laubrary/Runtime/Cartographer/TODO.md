# Cartographer — design notes & task list

## Resume here (state as of 2026-07-27)

**Phases 1–5 are done bar two items.** The tool authors clumps, edits biomes, scaffolds levels, manages layers, stamps (rotate / mirror / drag-repeat / line mode), records placements, builds collision (composite, one-way, slopes, tag-narrowed), runs Rooms with scroll + catch-up + camera modes, and generates levels from a seeded recipe. It has built a real side-scrolling level.

**Everything is in ONE window:** `Laubrary ▸ Cartographer ▸ Clump Editor`. There are deliberately no other menu commands except `Build Demo Assets` — creation happens in the window, not in a submenu.

**What is left, in the order to do it:**

1. **Live generator preview** — regenerate + redraw on every dial change, the `TrackDesignerWindow` treatment. The Generate box's button is a manual stand-in. This is the only unfinished piece of a landed phase.
2. **Scavenger sprite sourcing** — still unresolved (see Open decisions). ⚠️ Needs Lautaro's call, not a unilateral one: it means refactoring Launimator inside Asteroid+.
3. **Phase 6's real biome** — needs actual pixel art, not code. The integration scene exists in OutBurner.
4. **Colour-system compatibility** — `ZuiSwatchBinding` cannot drive a Tilemap (one material for the whole map). Per-tile solid colour wants a small `TilemapSwatchBinding` over `Tilemap.SetColor`; per-layer gradient/cycling wants the LUT shader on the `TilemapRenderer`. Neither is built.

**Two things to know before touching the code:**

- **`CartographerRuleTile` is conditionally compiled.** `RuleTile` ships in the OPTIONAL `com.unity.2d.tilemap.extras` package, so it is wrapped in `#if TILEMAP_EXTRAS_INSTALLED` with a versionDefine on the runtime asmdef (the pattern Zounds uses for Addressables). **Laubrary Dev does not have that package, so the type genuinely does not exist here — that is correct, not a bug.** OutBurner has 6.0.1. When verifying, check its base resolves as `RuleTile<>`, not merely that the type was found.
- **`RoomDirector` is a tickable on purpose** — `Tick(dt, playerPos)` applied to its own state, `Update` only calls it. That is what lets every scroll / catch-up / camera behaviour be verified in EDIT mode by driving `Tick` directly: no play mode, no wall-clock waiting, exact numbers. It has already caught a real bug that way (`PlayerCaught` firing per tick instead of per crossing). Keep that shape.

**Test content:** `Assets/Demos/CartographerDemo/` via the `Build Demo Assets` command — it generates its own placeholder tiles so it works in a bare project, and never overwrites, so tweaks survive a re-run. OutBurner additionally has `Assets/OutBurner/Cartographer/CartographerTest.unity`, a real level with 13 placements across 3 layers.

> **2026-07-27 — brought back from OutBurner.** Phases 1, 2 and most of 4 were developed inside OutBurner's
> embedded copy (the tool's first real consumer, so the ergonomics surfaced fast) and promoted here once the
> authoring loop worked. This project is authoritative again; syncs no longer need `-Skip OutBurner`. The
> friction list further down is what building a real level with it actually taught.

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
- **Differently-sized level elements come from Clumps and the decoration layer, not from multiple grids** (decided 2026-07-27). A Unity `Grid` has one cell size, so a big object is either a multi-cell Clump (a whole building as one stamp) or a plain sprite on a decoration layer, which is not grid-bound at all and can be any size. Stacking several Grids at different cell sizes technically works but doubles the authoring surface for every layer — rejected unless a real need turns up.

## Open decisions (revisit before/while implementing)

- ~~Tag mechanism: plain strings vs. a tag-asset registry.~~ **Resolved 2026-07-27 → tag asset** (`ClumpTag`), per the lean already recorded here: discoverable in a picker, survives a rename, and can never silently become a typo. It carries a description and an editor colour but deliberately no behaviour — Phase 4's tag-to-collision wiring stays the consuming project's job.
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

### What happens when a forced scroll catches the player (decided 2026-07-27)

Four behaviours, all used in shipped games. This is a **per-Room setting on the scroll spec**, not a global one, because different rooms want different answers.

- **Lock** *(default)* — the player simply cannot leave the visible area; they get pushed by the view boundary but nothing else happens. Most shmups, Contra. Safest default because it never kills anyone by surprise.
- **Push** — the trailing screen edge shoves the player along, and they can be crushed against solid geometry. Mario 3 airships.
- **Kill** — touching the trailing edge is fatal.
- **Wait** — the scroll pauses until the player catches up. Honest option to have, but it stops being a forced scroll, so it belongs to a room that wants pacing pressure without failure.

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

## Hooks for scripted sequences ("Interlude") — 2026-07-27, design notes only

A separate planned tool (working name **Interlude**) plays scripted in-engine sequences using the live game objects: a stage intro where the hero walks in and the level title flashes, a pickup where every enemy freezes while the hero flies up and releases a blast, a hand-off where the hero boards a plane and the game becomes a shmup. It is not part of Cartographer and should not be built into it — but it needs two things *from* a level, and those two things are Cartographer's job.

**Keep the tools separate. Named spots are the entire connection.** Cartographer says "there is a point here called `landing-pad`". Interlude says "walk the hero to `landing-pad`". Neither needs to know how the other works, and neither gets a reference to the other's types.

- **Named spots (anchors).** A Room, or a functional Clump inside it, can expose named points that gameplay code and sequences look up by string. Procgen must guarantee that a Room's declared spots exist; it stays free to choose tiles and dressing around them. This reuses the functional-clump prefab hook already planned above rather than adding a second mechanism — a spot is a clump slot that carries a name instead of a prefab.
- **Reserved walkable areas.** A Room can mark a rectangle that generation must leave traversable, for sequences that need space to move rather than a single point. This is the honest fix for "will the hero's scripted walk be blocked in a procgen level" — better than switching collision off during a sequence, because collision-off still looks wrong the moment the hero walks through a wall the camera can see. Keep collision-off as an escape hatch for framings where the problem is never visible.

Both are cheap to add to the Room/Clump schema now and expensive to retrofit later, so put the fields in when Phase 1 lands even though nothing consumes them yet.

## Phased task list

### Phase 0 — scaffolding
- [x] `Runtime/Cartographer` + `Editor/Cartographer` asmdefs created (empty references, matching Larder's
  minimal-start pattern — add references as real code needs them).
- [x] This doc.
- [x] Moved into canonical Laubrary Dev from the Asteroid+ working copy (2026-07-16).

### Phase 1 — core data model  ✅ landed 2026-07-27
- [x] `CartographerBiome` (ScriptableObject) — tileset refs, which Clumps are valid in it, theming (tint,
  background sprite). The `LaubraryAssetWindow<T>` CRUD window is Phase 2, where it is already listed.
- [x] `Clump` type — `ClumpCell` list (offset + `TileBase` + target layer) + open-ended tags + weighted
  `prefabChoices` + named `spots`.
- [x] Tag system — resolved in favour of a `ClumpTag` **asset** (see Confirmed decisions).
- [x] Runtime `CartographerLevel` component: a `Grid` + named `Tilemap` layers, the room list, `PlaceClump`,
  and the named-spot registry.
- [x] `CartographerRoom` — a placed level section: Biome + exit condition (`RoomExit`) + scroll behaviour
  (`RoomScroll`) + `ScrollCatchUp` + `reservedAreas`. A `[Serializable]` class held inline on the level, not
  an asset, so bespoke rooms never bloat the asset library.
- [x] `Clump` and `CartographerBiome` implement `IVisualPreview` through one shared `CartographerPreview`
  tile-to-pixels path. **Both verified drawing** (2026-07-27) against the demo assets below — clump 64×48,
  biome 48×16, inspected by eye.
- [x] `Laubrary/Cartographer/Build Demo Assets` menu command — generates its own placeholder tiles, an
  `Untraversable` tag, a `Hut` clump and a `Settlement` biome into `Assets/Demos/CartographerDemo/`.
  Never overwrites, so edits to the demo assets survive a re-run.

### Phase 2 — manual authoring
- [x] **Clump Editor** (`Editor/Cartographer/ClumpWindow.cs`, menu `Laubrary/Cartographer/Clump Editor`) —
  landed 2026-07-27. `ZuiAssetWindow<Clump>` + a Painter2D paint grid: palette (seeded from a Biome and from
  the clump's own tiles), target-layer + paint/erase brush, grid sizing, tags and spots. Click/drag paints,
  Alt or right-button erases. `ZuiAudit` 0 findings / foldedSkipped 0 at 1000×620.
- [ ] **Scavenger port** — still living only in `D:\UNITY\Asteroid+\Packages\Laubrary\Editor\Scavenger` (see
  the Open decisions note above); NOT yet ported to this repo. Resolve the Launimator-overlap question before
  porting, not after. **Not a blocker for the Stamper** (decided 2026-07-27): Scavenger is sprite-sheet
  DOWNLOADING and cherry-picking, whereas authoring and stamping only need tiles already in the project, which
  a plain object field supplies. Resolve it when sprite sourcing is actually wanted, not before.
- [x] **Clump Stamper** (`Editor/Cartographer/ClumpWindow.Stamper.cs`) — landed 2026-07-27. A Level box in the
  Clump Editor holds the target level, a stamp-mode toggle, rotation (0/90/180/270) and mirror-X. In the Scene
  view it ghosts the transformed footprint at the hovered cell and stamps on click; Alt-click removes the
  placement under the cursor. Goes through `CartographerLevel.PlaceClump`, the same call a generator will use.
  **No menu command** — level creation is a button in the window, per the "no submenu commands, create in the
  browser" rule.
- [x] **Level scaffold** — the same Level box builds a Grid + Terrain/Structures tilemaps wired to a
  `CartographerLevel`, in one undo step, and selects it.
- [x] **Placement records** — `CartographerLevel.placements` remembers (clump, origin, rotation, mirror) for
  every stamp, because a Tilemap only stores tiles and otherwise loses which clump painted what — taking the
  clump's tags, prefab hook and any hope of rebuilding with it. `RebuildFromPlacements()` re-stamps the lot.
  Verified: 3 stamps → 24 Terrain + 12 Structures tiles, and a rebuild reproduces exactly that with no
  duplication.
- [ ] Sprite sourcing (download a sheet, cherry-pick tiles out of it) should reuse Scavenger once it's ported,
  rather than rebuilding it a third time.
- [x] **Biome editing** — landed 2026-07-27, but **inline in the Clump Editor, not its own window**: a Biome box
  edits whichever biome the palette is drawing from (name, tint, terrain tiles, allowed clumps, plus a
  "+ This clump" shortcut). A separate window would have needed its own menu entry, which the "no submenu
  commands" rule rules out, and a biome is short enough that it reads better beside the palette it feeds.
- [x] **`CartographerRuleTile`** (category-based neighbour matching) — landed 2026-07-27. Tiles carry a
  `family` string; any two tiles sharing one satisfy each other's neighbour rules, so a roof interior, a roof
  edge and three roof variants stop needing to list each other by hand. The ordinary "This"/"Not this" rule
  buttons honour the family too, so an author who sets one gets family matching without learning new options.
  ⚠️ **`RuleTile` lives in the OPTIONAL `com.unity.2d.tilemap.extras` package**, so the file is guarded by a
  `TILEMAP_EXTRAS_INSTALLED` versionDefine (the same pattern Zounds uses for Addressables) — Cartographer never
  forces that dependency on a consumer. Laubrary Dev does NOT have the package, so **this type compiles away to
  nothing here** and is only live in projects that install it (OutBurner has 6.0.1). Verified in both: absent in
  Laubrary Dev, present in OutBurner.

## Friction found by building a real level (2026-07-27)

Built `Assets/OutBurner/Cartographer/CartographerTest.unity` with the tool — ground strip, 3 huts, 4 one-way platforms, 3 layers, colliders. It works, and it surfaced three things in priority order:

0. **Line mode — added 2026-07-27.** Press, drag out a run, release to commit. Nothing is placed until release, so the span can be adjusted while the ghost previews the whole run; locked to the dominant axis, since a run of ground or a wall is the case and a free-angle line of square stamps is not. The ghost and the commit share one `LineOrigins`, so the preview cannot drift from what lands. Verified: a line from x=0 to x=47 with the 8-wide clump gives 6 origins (0/8/16/24/32/40), and a vertical run of a 1-tall clump over 5 cells gives 6.

1. **~~Repeat-stamping is the bottleneck~~ — FIXED (drag-to-repeat).** Hold and drag and the same gesture keeps laying clumps. The rule is **overlap rejection**, not one-per-cell: a candidate is skipped if its footprint touches the previous stamp's, so a drag tiles clumps edge-to-edge in whatever direction you move, and a 1×1 clump still stamps every cell. Verified: dragging across 48 cells with the 8-wide strip yields exactly 6 placements at origins 0/8/16/24/32/40 and 48 tiles — the ground strip in one gesture instead of six clicks. A separate **line mode** (click, drag, release to fill a span) is still worth having, but is no longer urgent.
2. **~~No layer management UI~~ — FIXED.** A Layers box on the Level box lists every layer with its name, `solid` and `oneWay` toggles and a remove button, plus the level's collision mode and "+ Add layer" (which creates the GameObject, Tilemap and renderer, undoable in one step). Removing a layer destroys its tilemap — undoable, and the tooltip says so.
   - Follow-on found while looking at it, also fixed: the **Brush's** layer radio was derived from the clump's own cells, so a level with a `Platforms` layer did not offer it when authoring a NEW clump. `KnownLayers()` now unions the target level's layers, so the brush offers exactly what is actually paintable.
3. **~~Silent cell drop on a layer-name mismatch~~ — FIXED.** A clump cell targeting a layer the level lacks was skipped with no message. Now warns, naming the clump, the missing layer, and the level's actual layers.

Also noted, lower priority: a clump wider than the authoring grid needs the Grid sliders bumped first (works, just not obvious), and the demo Hut is symmetric so mirroring cannot be *seen* on it — mirroring is verified numerically (a mirrored stamp's spot resolved to x = 20 + (-1.5) + 0.5 = 19.0), not visually.

### Phase 3 — procgen  (landed 2026-07-27)
- [x] **`LevelRecipe`** (asset) — seed + dials describing a KIND of level: biome, width, ground clump and how
  much the surface wanders, weighted scatter entries with per-100-cell density and height bands, and the rooms
  to copy onto the level. An asset so several Sites can share "ruined street" without duplicating dials.
- [x] **`LevelGenerator.Generate(recipe, seed, level)`** — deterministic from `System.Random(seed)`. It is a
  COMPOSER, not a painter: every clump goes through `PlaceClump`, so a generated level carries the same
  placement records, publishes the same named spots, and rebuilds the same way as a hand-built one. The biome
  gates scatter — a clump it does not allow is never placed however the dials are set. A crowded scatter roll
  is dropped rather than retried, because retrying skews density toward whatever fits.
- [x] **Weighted prefab resolution at generation time** — the generator passes its own seeded picker into
  `PlaceClump`, so a clump's prefab alternatives are chosen from the level seed. Regenerating a seed picks the
  same variants.
- [x] **Generate from the window** — a Generate box on the Level box (recipe, seed, Roll, Generate), building
  colliders afterwards. **Verified by signature, not by counts:** hashing the actual placement list shows
  1234 == 1234 and 1234 ≠ 9999 ≠ 1. Worth recording that the first attempt compared placement COUNTS and
  wrongly looked like seeds did not matter — two different levels place the same number of clumps constantly.
- [ ] Live preview that regenerates on every dial change (the `TrackDesignerWindow` treatment). The generate
  button is a manual stand-in; a real dial-driven preview is the remaining piece.

### Phase 3 — procgen (original notes)
- [ ] `LevelRecipe`/`GenParams` (seed + dials, mirroring `StageParams`).
- [ ] Generator: deterministic terrain fill + clump scatter (mirroring `BiomeDecor.Place`).
- [ ] Live preview for the generator window — regenerate + redraw on dial change, same principle as
  `TrackDesignerWindow`.
- [ ] Functional-clump resolution at generation time (weighted alternative picking via Randomizers).

### Phase 4 — collision / gameplay-mode split  (mostly landed 2026-07-27)
- [x] Top-down collision setup (`CompositeCollider2D`-based) — `CartographerLevel.BuildColliders()`, driven by
  a `CollisionMode` on the level and a per-layer `solid` flag. Idempotent, so it works as a rebuild button
  rather than a one-shot: clearing `solid` strips the components again.
- [x] Side-scrolling one-way platforms — a per-layer `oneWay` flag adds a `PlatformEffector2D` and sets
  `usedByEffector`, but only in SideScroll mode (in a top-down level "up" is not a direction you fall from, so
  the effector is dropped rather than silently doing nothing).
- [x] **Slopes** — a per-layer `colliderShape` (Square / Sprite outline). Sprite outline makes each tile collide
  on its own shape, which is how a triangular tile becomes a walkable slope; no custom geometry needed, it is
  the tile's own physics shape. Verified: 16/16 cells switch to sprite-outline collision.
- [x] **Gameplay-tag → collision** — an optional `solidTag` on the level. Left empty, the whole solid layer
  collides (the simple default). Set, only cells stamped from a clump carrying that tag collide, so decorative
  clumps can share a solid layer without blocking. Resolved the open question in favour of **tags NARROWING a
  solid layer, never widening a non-solid one** — a layer marked non-solid stays non-solid, so there is exactly
  one place to look when something unexpectedly blocks. Applied per CELL (not per tile asset), because the same
  Ground tile can be structural in one clump and dressing in another. Verified: Hut cells (tagged) collide,
  Ground Strip cells (untagged) do not, 8 vs 8.
- [ ] Project-side behaviour binding beyond collision (mirroring Lazor's core/binding split) — still open.

### Phase 5 — scroll & camera (side-scrolling)  (core landed 2026-07-27)
- [x] **Room exit-condition runtime** — `RoomDirector` advances through `level.rooms`, evaluating Distance /
  Time / ClearEnemies and raising `RoomStarted` / `LevelFinished`. ClearEnemies asks a `Func<bool> RoomCleared`
  the GAME supplies, because Cartographer has no idea what an enemy is; left null it ends immediately rather
  than hanging.
- [x] **Scroll-behaviour implementations** — Auto (view advances at speed), Locked, PlayerPushed (the view only
  ever moves forward, and only as far as the player has pushed it).
- [x] **`ScrollCatchUp` runtime** — Lock/Push pin the player to the trailing edge, Wait gives the distance back
  so the view never leaves anyone behind, Kill raises `PlayerCaught` **on the crossing** (latched — the first
  test showed it firing once per tick while the player stayed behind, which would tell a game it died dozens
  of times).
- [x] **Rooms are authorable** — a Rooms box on the Level box adds sections and edits name / biome / exit
  condition (with only the relevant parameter shown) / scroll / speed / catch-up. Before this the Room schema
  was unreachable data.
- [x] **`RoomCameraBinder`** — points a Camera at `RoomDirector.ViewCenter`, with optional smoothing. Kept a
  separate component on purpose: the director decides where play is looking, which is equally useful to a
  minimap, a cutscene, or a test with no camera at all.
- **Testability note:** the whole director is `Tick(dt, playerPos)` applied to its own state, so all of the
  above is verified in EDIT mode by calling it directly — no play mode, no waiting, no flaky timing. Worth
  preserving if this grows.
- [x] **Camera modes** — a per-Room `RoomCamera`: **Follow** (the director's view centre, the usual case),
  **Rail** (an authored `railPath` sampled by `RoomProgress`, independent of where the player is — for a
  set-piece approach), **Focus** (locked to a target until the room ends — a boss, or the installation you
  must destroy). Exposed as `RoomDirector.CameraPosition`, kept separate from `ViewCenter` so a Rail or Focus
  camera can look elsewhere WITHOUT changing the scroll or the catch-up rules. An un-authored rail falls back
  to the ordinary view rather than teleporting to the origin.
- [x] **Player lock / "clock anchor"** — realised as a per-Room `playerLead` from -1 (trailing edge, so almost
  the whole view is what lies ahead) through 0 (centred) to +1 (leading edge). Used by player-pushed scroll,
  which previously hardcoded "player at the leading edge" and therefore showed you everything BEHIND you.
  Default -0.5. A clock face was considered and rejected: along a single scroll axis it is one number, not two.
- [ ] Camera free mode + "player lock" clock-position anchor (name TBD), reacting to movement direction.
- [ ] Camera focus mode (lock onto a target point/sequence until released).

### Phase 6 — OutBurner integration
- [ ] New, separate scene in OutBurner using Cartographer.
- [ ] First real Biome + Clump set authored for OutBurner's retro pixel art style.

## Before extending this tool

Read the `laubrary` skill's `references/authoring.md` first (placement/asmdef/demo-scene/versioning rules —
standing rules for every Laubrary tool, not just this one). Verify compile after every change
(`check_compile_errors`) and screenshot-verify any editor window before calling a step done.
