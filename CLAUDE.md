# Laubrary Dev — project instructions

This is the **canonical development host for Laubrary** (`com.lautaro.arino.laubrary`). The package source
lives at `Assets/Packages/Laubrary/` (`Runtime/<Tool>/`, `Editor/<Tool>/`, `Samples~/`) and is copied out to
consumer projects' `Packages/com.lautaro.arino.laubrary/`. Git repo: `Laubrary_dev` (branch `master`).
The canonical Unity rules live in `D:\Unity\UNITY_DEV_GUIDE.md` (mandatory read).

## Coplay bridge — target THIS editor first (before any Coplay action)

This project has a project-scoped `coplay-mcp` server (`.mcp.json`). The Coplay MCP discovers *every* open
Unity editor, so before using any Coplay tool you MUST point the bridge at this project and verify it:

1. `list_unity_project_roots` — confirm `D:\Unity\Laubrary Dev` is present (open it in Unity if not).
2. `set_unity_project_root` → `D:\Unity\Laubrary Dev`.
3. Verify with `execute_script` logging `Application.dataPath` — it must resolve under `D:\Unity\Laubrary Dev`.

If `Application.dataPath` points elsewhere, `check_compile_errors` will look clean despite new code and
reflection won't find new types — re-run steps 1–3. `set_unity_project_root` is per-session.

## Tool conventions (mirror for every Laubrary tool)

- Runtime tool: `Runtime/<Tool>/` → asmdef `com.Lautaro-Arino.Laubrary.<Tool>`, rootNamespace `Laubrary.<Tool>`.
- Editor tool: `Editor/<Tool>/` → asmdef `com.Lautaro-Arino.Laubrary.<Tool>.Editor`, `includePlatforms:["Editor"]`,
  references the runtime asmdef. Demos live in `Assets/Demos/` and are copied to `Samples~/` via
  `Tools/Laubrary/Copy Demos to Package`. One package version for all of Laubrary; keep `CHANGELOG.md` updated.

## UI rule — ZUI for ALL UI (editor AND runtime)

ZUI is now part of Laubrary and is the **mandatory toolkit for all UI**, both editor windows and runtime/in-game
UI. Solve a UI problem once in ZUI and every tool benefits. Do NOT hand-roll IMGUI/`EditorGUILayout` chrome or
uGUI when ZUI provides it. (Legacy "no ZUI" comments in older tools like Rulesets are exactly that — legacy — and
should be migrated.) Editor windows extend `ZUIWindow` (override `OnZUIEnable`/`OnZUI`) and use the `this.Button/
Slider/Toggle/Label/MiniRadio/SliderRange/Box/...` wrappers; custom canvas painting (a 2D preview stage) may stay
raw IMGUI, matching the OutBurner editors. The ZUI editor toolkit is `Assets/ZUI/` (asmdefs `ZUI.Editor` +
`ZUI.Runtime`) — reference both from an `Editor` asmdef that needs it.

**Open packaging gap:** `Assets/ZUI/` currently lives OUTSIDE the package (`Assets/Packages/Laubrary/`), so a
Laubrary editor that references `ZUI.Editor` compiles here but would NOT ship self-contained to a consumer
project. To make "ZUI is part of Laubrary" real, ZUI needs to move into the package (or be a declared dependency).
Until then, package tools referencing ZUI only work in this dev host.

## Current work — Zoetrope tool

We are landing **Zoetrope** (a sprite-sheet → versioned 2D character/animation authoring tool; entity = a **Zoe**)
as a new Laubrary tool, extracted from the retired AssetScavenge project. It adds `Runtime/Zoetrope/` +
`Editor/Zoetrope/` and requires the `com.unity.2d.sprite` package (+ `com.unity.nuget.newtonsoft-json`). The tool
ships ZERO assets — Zoes are authored into the host project's `Assets/Zoetrope/…`, never into the package.
