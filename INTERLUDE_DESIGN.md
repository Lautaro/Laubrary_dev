# Interlude — design notes

*Working name. Moved here 2026-08-09 at Lautaro's instruction: Interlude is developed in Laubrary Dev; the OutBurner project handles only OutBurner.*

**Status: Interlude does not exist.** Confirmed 2026-07-30 across both repos — it appears in exactly two places, Cartographer's `TODO.md` (design notes, 2026-07-27) and one docstring on `PropSpot` in `Prop.cs`. It is a reserved seam and a name. Everything below is design, not description.

**Scope decision 2026-08-09 (Lautaro): v1 is a cutscene authoring tool for Cartographer STRICTLY.** *"I think biggest hurdle is to design a proper visual tool that makes it easy for user to author a cut scene over a specific level, menu screen, static image or whatever... Actually this is hard so lets make it less flexible for now."* Menu-screen / static-image / live-game-view backdrops are deferred; §11 explains why that deferral turns out to cost nothing structurally. §5 and §11–§13 were written or rewritten under this decision — **§5 as originally drafted is obsolete**, see §11.

---

## 1. What it is, and the framing that makes it reasonable about

Interlude plays **scripted in-engine sequences using the live game objects** — not a cutscene system, not a separate scene. The three motivating examples, from the original notes:

- a stage intro where the hero walks in and the level title flashes;
- a pickup where every enemy freezes while the hero flies up and releases a blast;
- **a hand-off where the hero boards a plane and the game becomes a shmup.**

### The trio — and it turns on *two kinds of time*

> **Cartographer = space. Arena = systemic time. Interlude = authored time.** The gameplay module is separate from all three.

Arena governs time that **emerges from rules** — scroll, spawn budgets, room exit conditions. Interlude governs time that **someone wrote down**. They are both "time", which is why fusing them is tempting, and they are opposite in kind, which is why it would be a mistake.

---

## 2. The seam: Interlude owns WHEN, the game owns WHAT

The precedent already exists and should simply be extended: `RoomCleared` is a **game-supplied `Func<bool>`** because *"Cartographer has no idea what an enemy is."* Interlude gets the same discipline.

| **The tool owns** | **The game owns** |
|---|---|
| the sequence document — ordered steps, parallel tracks, waits, durations | what "the hero" is; what an enemy is; what a plane is |
| orchestration: start, advance, finish, abort | every **verb with semantics** — board, freeze, fire, take damage, become a shmup |
| resolution by name (holds the string, never learns the meaning) | locomotion and animation state |
| movement along authored paths | the scene load itself |
| phase lifecycle; on/off transitions | |
| skip and interrupt policy | |
| the authoring surface | |

### The resolution mechanism — widen what exists, do not invent a second one

`PropSpot`'s docstring already states the principle: *"A sequence asks for `landing-pad` and gets a world position, without either side knowing anything about the other's types — this is the entire connection between a level and an Interlude, kept deliberately narrow."*

Extend that one mechanism three ways:

> **named spot → a position · named group → a set of actors · named verb → a game-supplied delegate**

All three are "the tool holds a string, the game answers it." So *"freeze every enemy"* becomes *invoke verb `freeze` on group `enemies`* — the tool stays ignorant, the sequence stays authorable, and nothing new is introduced.

### The hand-off division of labour

> **Arena initiates · Interlude performs · SubGame transports** — and Arena never learns the racer exists.

⚠️ **The scene load is a visible seam** unless the Interlude covers it or the next scene loads additively. Flagged in the original notes and still unsolved.

---

## 3. What a sequence needs FROM a level

Both are already in the Cartographer schema, put there early because they are *"cheap to add now and expensive to retrofit later"*:

1. **Named spots (anchors).** A Room or a functional Clump exposes named points looked up by string. **Procgen must guarantee a Room's declared spots exist**, while staying free to choose tiles and dressing around them. A spot is a clump slot carrying a name instead of a prefab — deliberately *not* a second mechanism.
2. **Reserved walkable areas.** A rectangle generation must leave traversable, for sequences needing space rather than a point. ⚠️ This is the honest fix for *"will the hero's scripted walk be blocked in a procgen level"* — **better than switching collision off, because collision-off still looks wrong the moment the hero walks through a wall the camera can see.** Collision-off survives only as an escape hatch for framings where the problem is never visible.

---

## 4. ⚠️ Attachment: a path must declare its COORDINATE SPACE

*(New, 2026-08-09. This is the answer to "how does a cutscene attach to a specific part of a level", and it was forced by one example from Lautaro: "I need to be able to paint the path the hero goes out of screen on while splashtext appears.")*

**"Walks out of screen" is a screen-space statement.** Authored in world coordinates it breaks the moment camera framing changes. So a path's declared space *is* its attachment mechanism:

| space | anchored to | portable to |
|---|---|---|
| **world** | level coordinates | that level only |
| **spot-relative** | a named spot (`landing-pad`) | any level declaring that spot |
| **screen** | the viewport frame | **everything — levels, menus, title cards** |

The hero-leaves-frame case is likely **spot-relative start → screen-space exit**: begin where he actually stands, leave through the frame edge. One path, two spaces, and it survives being reused over a menu.

⚠️ **This constrains sharing with Choreographer.** Choreographer's paths are explicitly *unitless*, which is exactly why they are reusable and exactly why they cannot express "the right edge of the screen" unaided. **The shared path type needs a space tag.**

---

## 5. The editor: an overlay over a pluggable backdrop

⚠️ **Superseded in part by §11 (2026-08-09).** The requirement below stands; the claim that *"the honest default is the real game view"* was written when Cartographer edited through the Scene view, and that path no longer exists. Read §11 before building anything from this section.

Lautaro's requirement: *"a dedicated overlaying of editable paths for interludes… like a meta layer on other visuals like a menu screen or a certain part of a level."*

The overlay must not care what is underneath it. **Backdrop is a plug**: a Cartographer level render, a live game view, a captured frame, a menu mock.

### ⚠️ This is already-scoped work, not a new framework

The parked grid-management research (see OutBurner `TASKS.md`, "RESEARCH PARKED 2026-08-03") landed on precisely this as its **top recommendation**:

> *"Highest-value extraction is NOT the grid — it is the **overlay/canvas kit**: five hand-copied sites of Painter2D-draws-beneath-children, four of never-bulk-stroke-mitered-closed-paths (the 2026-08-01 tessellator crash), and the full-length-lattice-lines rule; family-agnostic, zero semantics, used by every surface."*

Plus Lautaro's own reframe, that the **interaction vocabulary** is shared regardless of content: pointer↔cell hit-testing, hover feedback, marquee, Ctrl-add, drag-with-ghost, copy, delete, flip, Esc, zoom/pan, the reserved status line.

**A path editor is that vocabulary with "control point" as the object.** So Interlude's overlay is the **sixth consumer of an extraction already justified by five existing ones** — and the forcing function that pays for it.

### Sharing with Choreographer: share the path, not the editor

**Choreographer consumes** a path at runtime (N dancers, formations; *N-agnostic and unitless*). **Interlude authors** one. Extract the **path type and its evaluator**; leave both editors alone. Choreographer stays untouched and Interlude gets runtime motion for free.

---

## 6. ⚠️ The document is TRACKS, not a step list

*"Paint the path the hero goes out of screen on **while** splashtext appears"* — two things at once. The sequence document therefore needs **parallel tracks**, and the editor must show them together: one track carrying a path, another a UI event.

This is a bigger claim than it sounds. **A step list is much simpler to build and would have felt sufficient right up until that sentence.** Lock it in before anything is written.

---

## 7. Three places the tool/game line is genuinely hard

1. **Transform vs animation.** Choreographer moves the transform; the game owns animation state. If they disagree, feet slide. Decide which is authoritative **per step**, not globally.
2. **Skippable vs consequential.** A sequence that grants a weapon or opens a door **must still do so when skipped.** So each step declares which it is — and because that is generalisable, it belongs in the tool. Expensive to retrofit.
3. **Camera.** *"Frame spot X"* is a tool concern; *"do the chase-cam thing"* is game code. Resolve it the same way as everything else: the tool addresses a **named rig**, the game supplies rigs.

---

## 8. Build or compose?

The original note's open question, still unanswered and still worth an hour:

> **Interlude may not need to be a new tool.** Check whether it is a thin composition of three things Laubrary already has — **Choreographer** (authored movement, N-agnostic and unitless), **Switcheroo** (stateful on/off transitions with hooks), **Overture** (an `OvertureState` lifecycle with four transition phases).

Strip out what those three already do and the residue is **the sequence document plus its resolver** — smaller than "a new tool" implies.

⚠️ With the warning attached: a hardcoded cutscene is a fine placeholder, but **do not let the placeholder quietly become the architecture.**

---

## 9. ⚠️ The blocker nobody has cleared

`ScavengeGame.Start()` must be split into `BeginRoom(seed)` / `TeardownRoom()`. **Reaching an exit currently ENDS the run**, so there is nothing for an Interlude to sit between. (OutBurner-side work; recorded here because it gates the first real consumer.)

Related OutBurner task: **1e-4, "a hand-off Room exit"** — one enum case meaning *"this Room ends by handing off elsewhere"* rather than *"advance to the next Room"*.

---

## 10. Open questions

- What is a sequence **as data** — timeline, state machine, or script? (Tracks are settled; the step representation is not.)
- What may a step **address**? Hero, enemies, camera, UI, time itself?
- What does **"freeze every enemy"** mean mechanically — timescale, an actor flag, a game-supplied verb? (§2 argues the last.)
- **Who authors** — a tool, or C#? (§5 assumes a tool; the composition question in §8 could change that.)
- Does a sequence **own** the objects it drives, or borrow them? Determines what happens if one dies mid-sequence.

---

## 11. ⚠️ What Cartographer's authoring surface ACTUALLY is now (verified 2026-08-09, in OutBurner)

Everything above §11 was written against *"The Scene view is the canvas, this file is the brush"* (`CartographerWindow.SceneTools.cs`). **That is dead.** Lautaro flagged it as outdated and the update lives in OutBurner, not in this repo — see §13.

**Decree 2026-08-03, first half** (`CartographerWindow.Canvas.cs` header): *"the right pane IS the level editor — the window's one and only edit view (the Scene-view editing path was removed by decree 2026-08-03; a LevelInstance in a scene is a live READ-ONLY mirror now)"*, and from the window header, *"no workflow may require opening the same level in both this window and a scene."*

**Decree 2026-08-03, second half — the gesture model Interlude must obey rather than reinvent:** left-click applies the tool at the cell (drag continues); left-drag marquees a SELECTION, and clicking inside it applies the tool to every selected cell; Line/Rect/Pick keep press-drag-release spans; **right-click opens the pinnable Tool card** (`ZuiPinCard`); Alt erases and Ctrl picks from any tool; Fill flood-fills the contiguous region.

**What `LevelCanvas : VisualElement` is** (~2,000 lines inside `CartographerWindow.Canvas.cs`, plus rulers/guides in `CartographerWindow.Guides.cs`): a checkerboard, per-layer CPU-blitted `Image`s (point-filtered, decals interleaved by sorting, per-cell re-blit, proven at ~16k cells), a **pooled ghost** of real `Image` elements, a Painter2D **overlay**, prop markers for cell-less props, rulers numbering cell boundaries, draggable boundary-snapped guides, and a promotable mirror axis. View maths — fit, wheel-zoom toward the pointer, middle-drag pan, degenerate-geometry guard — are extracted into **`ZuiPanZoom`**, of which LevelCanvas is *"its first consumer."*

### ☠️ Three traps the canvas already paid for, which now bind Interlude

1. **`Painter2D` cannot draw a texture** (stated flat out at `PropWindow.cs:476`). Paths, control-point handles and spot pins go on the Painter2D overlay; any **actor ghost sprite must be a real pooled `Image`**, exactly as `ghostPool`/`SyncGhost` already does for brush tiles. Allocating per `PointerMoveEvent` stutters — pool and hide, never allocate.
2. **Painter2D content draws BENEATH its element's children** — the "paid-for SheetStage trap". Overlay z-ordering is a deliberate act (the ruler *numbers* are separate elements sitting in front of the Painter2D strips they label).
3. **Never bulk-stroke mitered closed paths** (`lineJoin = LineJoin.Bevel` — the 2026-08-01 tessellator crash), and never draw full-length lattice lines naively (13k cells → 52k segments hung the editor once).

### The authoring-aid-vs-data precedent, already settled

Guides are stored in the window's **EditorPrefs blob keyed by the level's GUID, deliberately NOT on the `LevelAsset`**, because *"putting them in the asset would dirty the level on every guide drag (making 'I nudged a construction line' a Ctrl+S-worthy change), park them in version control where two authors editing the same level would collide over each other's scaffolding, and ship them in the build."* The stored set is bounded by most-recently-used so a machine-global prefs string never leaks. Interlude splits the same way: the **sequence is level data** (it ships), while **playhead position, selected track, zoom and ghost toggles are view state** and belong in `WindowState` beside `GuideSet`.

### The decomposition question is further along than §5 says

`TASKS.md:214` (survey landed) still ranks the **overlay/canvas kit** as the highest-value extraction — but `TASKS.md:215` is Lautaro's reframe and it supersedes: the right decomposition is **three parts**, (1) a shared *grid interaction surface* (pointer↔cell hit-testing, hover, marquee, Ctrl-add, drag-with-ghost, copy, delete, flip, Esc, zoom/pan, the reserved status line — family-agnostic, *"the gesture is most of the code"*), (2) a pluggable *cell renderer*, (3) a pluggable *operation set*. A path editor is (1) with "control point" as the object.

## 12. The revised v1 — an overlay on a READ-ONLY level backdrop

**Extract the read-only half of `LevelCanvas` as `LevelBackdrop`**: checker + per-layer blit + `ZuiPanZoom` + a Painter2D overlay hook. That is precisely the part carrying no semantics. `LevelCanvas` keeps its editing on top of it unchanged; Interlude consumes it read-only. Two live consumers, and it is the survey's #1 recommendation cut down to one defensible extraction instead of a framework.

Why read-only is the right v1: **a cutscene is authored over a FINISHED level.** Interlude never needs to paint tiles, and skipping that avoids swelling a window already ~270KB across four files. It also means the backdrop is **just a picture** — which is what makes the deferred backdrops of §5 (menu mock, captured frame, live game view) cheap to add later. **Being Cartographer-strict therefore costs nothing structurally**, which is the whole reason the scope cut is safe.

**Two panes, one window** — because §6's tracks do not fit on a spatial canvas:

- **Top pane = WHERE.** `LevelBackdrop` plus a Painter2D pass drawing paths as control-point chains, spot pins labelled from `LevelInstance.SpotNames`, and `CartographerRoom.reservedAreas` tinted. Gestures follow the decree's vocabulary: left-click places/selects a point, left-drag marquees points, right-click opens the pinnable card, Alt deletes, Ctrl inserts.
- **Bottom pane = WHEN.** `ZuiTrackStrip`, **new in ZUI** — one row per role, blocks with start + duration. A path block on the hero row overlapping a splash-text block on the UI row IS §6's "while". ZUI has no track/timeline control today; the nearest neighbours to build it from are `ZuiStepSequencer` (a *value* painter — N equal-width bars, drag to paint levels — not a timeline) and `ZuiScrub`. Other customers already waiting: the deferred Mirage Zoe-event sequencer, SpriteFx stacks, Zoe events, Zequence.
- **The playhead binds them.** Scrubbing slides actor ghosts (pooled `Image`s) along their sampled paths on the backdrop, in edit mode, no Play needed. Selection syncs both ways.

**Free already, verified in both repos:** `LevelInstance.TryGetSpot(name, out worldPos)` + `SpotNames` (the §2 resolver, live); `PropSpot` with fractional, rotation/mirror-aware offsets; `CartographerRoom.reservedAreas` + `IsReserved` (§3, already in the schema); `CartographerRoom.railPath` as the precedent for an inline authored world path; and `PathCache` in `ChoreographySampler.cs` — Catmull-Rom, arc-length paced, takes a bare `List<Vector2>` — reusable as the path evaluator without touching Choreographer (§5's "share the path, not the editor").

**Friction to decide:** spots live on Props, so *"put a spot here"* means placing a spot-only prop in Cartographer (the marker path already handles cell-less props). For v1 that is a window switch. A quick-add would make Interlude a **writer** to the level, which is a bigger decision than it looks.

**Suggested phasing:** ① sync the repos (§13) → ② extract `LevelBackdrop` → ③ `ZuiTrackStrip` in ZUI → ④ the Interlude document + inline storage → ⑤ path tool on the backdrop → ⑥ scrub + ghosts → ⑦ runtime player + cast resolver (still gated on §9's `BeginRoom`/`TeardownRoom` split).

## 13. ⚠️ BLOCKER: Laubrary Dev and OutBurner have diverged

Laubrary Dev is nominally the canonical host, but as of 2026-08-09 **OutBurner's copy of the package is ahead on Cartographer and behind on ZUI**. Building Interlude here as-is means building on a Cartographer that no longer exists in its modern form, against a canvas class and a `ZuiPanZoom` this repo does not have.

| | Laubrary Dev | OutBurner |
|---|---|---|
| Canvas | `CartographerWindow.SceneTools.cs` (**the deleted path**) | `CartographerWindow.Canvas.cs` 135KB + `.Guides.cs` 54KB |
| `LevelAsset.cs` | 416 lines | **641 lines** (+ `ClumpPlacement`, `actorSortingOrder`) |
| `Prop.cs` | 170 lines | **252 lines** |
| Only there | — | `TilesetGridView.cs` (165KB), `ClumpMetaSubject.cs`, `ILevelMutator.cs` |
| ZUI toolkit (`Zui/Toolkit/`, 37 controls each) | **`ZuiAssetHook`, `ZuiChip`** | **`ZuiPanZoom`, `ZuiPinCard`** |

The divergence is exactly two ZUI controls each way, so that half is a small merge. Cartographer is a one-way sync (OutBurner → dev host). **`CartographerRoom.cs` is byte-identical in both** (128 lines), and `PropSpot` + `LevelInstance.TryGetSpot`/`SpotNames` are intact on both sides — so every seam finding in §2, §3 and §12 survives the sync either way.

### Three forks still unanswered (asked 2026-08-09, session ended before the answers)

1. **Repo strategy** — sync Cartographer + `ZuiPanZoom`/`ZuiPinCard` into the dev host and push `ZuiChip`/`ZuiAssetHook` back (recommended), build Interlude in OutBurner instead, or something else.
2. **Backdrop** — extract `LevelBackdrop` for a separate Interlude window (recommended), or add Interlude as a mode inside `CartographerWindow`.
3. **Storage** — the sequence inline on `LevelAsset` beside `rooms` (recommended; `CartographerRoom`'s own docstring says *"a bespoke cutscene path is authored inline instead of as a shared Choreography"*), or its own asset with AssetKit CRUD and a LauAsset browser card.
