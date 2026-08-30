# Laubrary Dev — project instructions

This is the **canonical development host for Laubrary** (`com.lautaro.arino.laubrary`). The package source
lives at `Assets/Packages/Laubrary/` (`Runtime/<Tool>/`, `Editor/<Tool>/`, `Samples~/`) and is copied out to
consumer projects' `Packages/com.lautaro.arino.laubrary/`. Git repo: `Laubrary_dev` (branch `master`).
The canonical Unity rules live in `D:\Unity\UNITY_DEV_GUIDE.md` (mandatory read).

## ⚠️ YOU ARE IN THE SHAPER WORKING COPY — read this before anything else

This folder is `D:\UNITY\Laubrary Dev - Shaper`, a **git worktree** of the same repository as the main copy at `D:\UNITY\Laubrary Dev`, checked out on branch **`feat/shaper`**. It exists so Shaper can be built in its own Unity editor without blocking the rest of Laubrary. Full setup notes, including the merge cadence, live in the main copy at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0100\SHAPER_WORKTREE_SETUP.md`.

Four rules follow from that:

1. **Point Coplay at THIS folder, never the main copy.** See the next section — the paths there are Shaper's. Two Unity editors are open on two folders whose `productName` is identical, so nothing inside the editor will tell you that you are driving the wrong one.
2. **The AgentHQ task board is NOT here.** The live board lives in the main copy at `D:\UNITY\Laubrary Dev\.agenthq\`. The `.agenthq\` folder in *this* copy is a frozen snapshot from commit `272e6df7`: treat it as read-only junk, never create/edit/tick a task through it, and never let anything allocate a task id from its `counters.json` — that allocator has already moved on in the main copy. Talk to AgentHQ over its HTTP API (`http://127.0.0.1:8778`) as usual; the server is backed by the main copy.
3. **Do not run `sync-laubrary-to-consumers.ps1` from here.** Its `$Src` is hardcoded to the main copy, so running it in this folder would silently publish the *main* copy's Laubrary to consumers rather than yours.
4. **Shaper is additive — new files in new assemblies.** Do not edit Pyre's files here. The only files this branch and the main branch are expected to collide on are listed in the setup notes.

This section, and the Shaper paths in the section below, are **worktree-local**: delete them when `feat/shaper` is merged back into the main branch.

## Coplay bridge — target THIS editor first (before any Coplay action)

This project has a project-scoped `coplay-mcp` server (`.mcp.json`). The Coplay MCP discovers *every* open Unity editor, so before using any Coplay tool you MUST point the bridge at **this working copy** and verify it:

1. `list_unity_project_roots` — confirm `D:\UNITY\Laubrary Dev - Shaper` is present (open it in Unity if not).
2. `set_unity_project_root` → `D:\UNITY\Laubrary Dev - Shaper`.
3. Verify with `execute_script` logging `Application.dataPath` — it must resolve under `D:\UNITY\Laubrary Dev - Shaper`. Check the **full path**: the project name alone is identical in both copies and cannot tell them apart.

If `Application.dataPath` points anywhere else — above all if it points at `D:\UNITY\Laubrary Dev`, the main copy — you are driving the wrong editor, and `check_compile_errors` will look clean despite new code while reflection won't find new types. Re-run steps 1–3. `set_unity_project_root` is per-session.

## Tool conventions (mirror for every Laubrary tool)

- Runtime tool: `Runtime/<Tool>/` → asmdef `com.Lautaro-Arino.Laubrary.<Tool>`, rootNamespace `Laubrary.<Tool>`.
- Editor tool: `Editor/<Tool>/` → asmdef `com.Lautaro-Arino.Laubrary.<Tool>.Editor`, `includePlatforms:["Editor"]`,
  references the runtime asmdef. Demos live in `Assets/Demos/` and are copied to `Samples~/` via
  `Tools/Laubrary/Copy Demos to Package`. One package version for all of Laubrary; keep `CHANGELOG.md` updated.

## Menus — never add one the user didn't ask for

Do **NOT** add editor menu items, windows, or tools that weren't explicitly requested. Stashing functionality in
the `Laubrary/` menu "in case it's needed" is clutter, not help — the user has repeatedly had to hunt down and
delete unrequested items. Add a `[MenuItem("Laubrary/…")]` **only** when the user asked for that specific tool or
action. Keep the `Laubrary/` menu lean: it should contain the way to *open each tool* and genuine tool actions —
nothing speculative. If you think an extra menu item is warranted, propose it in prose and let the user decide.
(This is the general rule in `D:\Unity\UNITY_DEV_GUIDE.md` → Coding best practices, restated here because it keeps
recurring.)

**Keep it as FLAT as possible.** No redundant nesting: never a single-item submenu that just echoes the tool name
(`Laubrary/Pyre/Pyre` → `Laubrary/Pyre`). Put every item directly under `Laubrary/` root. The *only* exception is a
large multi-tool like Launimator, which may keep **one** submenu capped at **~3** items — and even then the core
windows go at root, with just secondary/utility actions tucked in the submenu.

## Undo — every tool, always

Editor tools must be Undo-safe (general rule in `D:\Unity\UNITY_DEV_GUIDE.md` → Coding best practices #7, restated
here because it's mandatory for Laubrary). `Undo.RecordObject(asset, "…")` before a serialized edit (inside a
`BeginChangeCheck/EndChangeCheck`); `Undo.RegisterCreatedObjectUndo` for created assets. The `AssetKit` base already
makes **New/Duplicate** undoable and gates **Delete** behind a confirm dialog (asset delete can't be undone). When
you build/port a tool, wire its per-field dial edits through `Undo.RecordObject` too — several older tools
(Rulesets, some Pyre/Larder dials) still edit without recording undo and should be retrofitted.

## Demos — ship SCENES, not scripts that build scenes

A Laubrary tool's demo is a **real saved `.unity` scene** (plus any authored `.asset` it needs), committed to
`Assets/Demos/<Tool>Demo/`. Do **NOT** write an editor menu-item / `[MenuItem]` builder that constructs the demo
scene or its assets on demand — that's the same authoring work, plus a permanent stray menu item and code file
nobody invokes. Author the scene directly (via Coplay: create GameObjects, wire components, `save_scene`). If you
use a throwaway script to bootstrap it, keep that script **ephemeral** (scratchpad, uncommitted) and **delete any
committed builder once the scene/asset exists**. The scene is the deliverable; the builder is a middleman.

## Naming — cool names are earned by a UI

A system gets a **cool/thematic name** (Pyre, Launimator, Zoetrope, Daemon…) ONLY if it has a **visual UI /
authoring component**. A pure **code backbone** with no editor window gets a **plain descriptive name** — e.g. the
combat backbone is `Combat2D` (namespace `Laubrary.Combat2D`; "2DCombat" is not a valid C# identifier), not a cool
name, because it has no authoring window. A cool name promises a tool to open; don't hang one on infrastructure.

## UI rule — ZUI for ALL UI (editor AND runtime)

ZUI is now part of Laubrary and is the **mandatory toolkit for all UI**, both editor windows and runtime/in-game
UI. Solve a UI problem once in ZUI and every tool benefits. Do NOT hand-roll IMGUI/`EditorGUILayout` chrome or
uGUI when ZUI provides it. (Legacy "no ZUI" comments in older tools like Rulesets are exactly that — legacy — and
should be migrated.) Editor windows extend `ZUIWindow` (override `OnZUIEnable`/`OnZUI`) and use the `this.Button/
Slider/Toggle/Label/MiniRadio/SliderRange/Box/...` wrappers; custom canvas painting (a 2D preview stage) may stay
raw IMGUI, matching the OutBurner editors. The ZUI editor toolkit is `Assets/ZUI/` (asmdefs `ZUI.Editor` +
`ZUI.Runtime`) — reference both from an `Editor` asmdef that needs it.

**ZUI-first — use the standard controls, then expand ZUI (don't route around it).** When you write OR port a UI to
ZUI, reach FIRST for ZUI's standard controls, containers and helpers (Button, Toggle, Slider/MicroSlider, Label,
MiniRadio/CycleButton, Envelope/CurveField, Box/FoldoutBox/AreaBox, HRow/Flow/Field, IntField/FloatField,
Blocks/Form, spacing). Do NOT drop to raw `EditorGUILayout`/`GUILayout` for something ZUI already provides. **If no
ZUI control fits, that's a smell** — surface it and consider EXPANDING ZUI (add the control there so every tool
gains it) rather than silently hand-rolling raw IMGUI in one window. Known current ZUI gaps (legitimate raw
fallbacks today, and prime expansion candidates): a **text-input field**, an **object/asset picker**, a **color
field**, and an **enum popup/dropdown**. Genuinely bespoke *canvas painting* (a 2D preview stage, a thumbnail
grid) stays raw — that's not a missing control. (Corrected 2026-07-21: a **scroll container** was listed here
too, but `ZUI.ScrollView(ref scroll)`/`ZUI.ScrollScope` (`ZUIFields.cs`) already exists and is already used —
e.g. `LaubraryAssetWindow`'s browser, `MirageWindow.DrawAsset`. Verify a claimed gap against the actual code
before trusting this list — it drifts.)

**Open packaging gap:** `Assets/ZUI/` currently lives OUTSIDE the package (`Assets/Packages/Laubrary/`), so a
Laubrary editor that references `ZUI.Editor` compiles here but would NOT ship self-contained to a consumer
project. To make "ZUI is part of Laubrary" real, ZUI needs to move into the package (or be a declared dependency).
Until then, package tools referencing ZUI only work in this dev host.

## Naming — the Zoetrope/Launimator/Zoe triangle

Three related but distinct things share this naming, easy to confuse:

- **Launimator** — the sprite-sheet → versioned 2D animation authoring tool (`Runtime/Launimator/` +
  `Editor/Launimator/`; entity = a **Reel**/`ReelVersion`). Was originally called "Zoetrope"; renamed once
  "Zoetrope" was needed for the composition module below. Requires `com.unity.2d.sprite` (+
  `com.unity.nuget.newtonsoft-json`). Ships ZERO assets — Reels are authored into the host project's
  `Assets/Launimator/…`, never into the package.
- **Zoetrope** (current meaning) — the composition/catalog module (`Runtime/Zoetrope/` + `Editor/Zoetrope/`,
  formerly "Bestiarium") that assembles a **Zoe**'s recipe: body/weapon/FX/behaviors as one asset. Named for
  the antique device that creates the illusion of one continuous living thing from separate frames — a fit
  for "assembles parts into one entity," which raw frame animation (now Launimator) never quite was.
- **Zoe** — the top-level composed entity asset (formerly `CharacterDef`) a project spawns as one thing:
  body, weapon, FX, behaviors. Lives in the Zoetrope module.

Bridge folders are named for which two systems they connect: `ZoetropeLaunimator` (Zoetrope↔Launimator,
formerly `BestiariumZoetrope`), `ZoetropePyre` (formerly `BestiariumPyre`), `ZoetropeDaemon` (formerly
`BestiariumDaemon`), `PyreZoetrope`/`PyreLaunimator` (Pyre's own Editor-side preview bridge — folder name
unchanged, still internally points at Launimator). Full design rationale: `ZOE_ARCHITECTURE_DESIGN.md`.
