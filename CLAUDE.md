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

## Pyre — the rename is DONE, there is no "PyrePlus"

**Executed 2026-08-23.** PyrePlus was renamed to plain **Pyre** and the ORIGINAL Pyre was deleted outright (not kept as a shim). `Runtime/Pyre/` + `Editor/Pyre/` hold the ex-PyrePlus code under plain-Pyre names — `PyreRenderer`, `PyreWindow`, `PyreBaker`, `PyreChunkAnimation`, asmdef `com.Lautaro-Arino.Laubrary.Pyre`, namespace `Laubrary.Pyre`. `[MovedFrom]` attributes were applied across the `SerializeReference` form types so existing authored assets still deserialize. `Runtime/PyrePlus/` and `Editor/PyrePlus/` are empty leftovers.

**Write `Pyre` in all new code, comments and docs — never `PyrePlus`.** There is no old-vs-new split left to reason about. Any doc or memory still saying "PyrePlus" predates 2026-08-23; correct it rather than following it. The runtime spawn API is `PyreBlastPool.Get()` → set `spec`/`fps`/`loop` → subscribe `Finished` → `Play()` (worked example: `Runtime/ZoetropePyre/SpawnPyreFx.cs`).

⚠️ The `feat/lathe` branch was renamed to **`dev`** (2026-09-01) because its name no longer described its contents. The old `origin/feat/lathe` remote branch was deliberately left in place, untouched, pending an explicit decision to delete it.

## Shaper — which folder am I in?

Shaper spans **two worktrees of this same repo**, each with its own Unity editor. Check which one you are in before anything else:

- **`D:\UNITY\Laubrary Dev`** (branch `dev`) — holds the *mock* UI at `Assets/ShaperMock/`. No Shaper engine here.
- **`D:\UNITY\Laubrary Dev - Shaper`** (branch `feat/shaper`) — holds the real engine at `Assets/Packages/Laubrary/Runtime/Shaper/` + `Editor/Shaper/`, and since the 2026-09-01 merge it also has the mock, so the port can be done against both.

Both projects have an identical `productName`, so **nothing inside the editor tells you which one you are driving.** Always verify `Application.dataPath` resolves to the folder you meant before trusting a `check_compile_errors` result — it will otherwise report clean while pointed at the wrong editor, which has bitten this project repeatedly.

Load-bearing facts, each verified against source rather than docs (2026-09-01):

- **`ShaperDocument` is a `ScriptableObject`** with `[CreateAssetMenu("Laubrary/Shaper Document")]`, matching Pyre's own spec-asset pattern. It has no custom `name` field — use `Object.name`. It carries `frameCount` + `frameRate` (the animation clock) alongside `phase01`.
- **The frame→phase mapping is `i/(N-1)`, and is NOT open for revision.** `ShaperNodeIdentity` folds `phase01` into every cache key, so changing it silently invalidates every key and re-points every authored Curve dial. `ShaperClock` is the single home for that conversion; never write a second one.
- **`ZUIValue` vs plain float is per-field, never a category rule.** The engine mixes both inside one struct (Star's sides/radius are plain, its length/baseWidth/skew are `ZUIValue`). Always check the real field before drawing a control.
- **The nine composite generators expose ~775 authored fields** (ArcBurst alone 187). Any generator UI must be reflection-driven over the assigned `PyreForm`; hand-listing dials is not maintainable and will silently expose a fraction of the engine.
- Deterministic draws only: `UnityEngine.Random` and `System.Random` are banned in generator paths (BC-1.3). Hash from `seed` instead.

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

**Packaging gap: CLOSED (verified 2026-09-01).** This section used to say ZUI lived outside the package and so wouldn't ship self-contained. That is no longer true and was misleading work as recently as this session: all **132** ZUI `.cs` files live INSIDE the package at `Assets/Packages/Laubrary/Zui/`, under three asmdefs (`ZUI.Editor`, `ZuiRuntime`, `com.Lautaro-Arino.Laubrary.Zui.Editor`), and many package editor asmdefs already reference `ZUI.Editor` (AssetKit, BackSplash, Cabinets, Cartographer, Chunks…). The only thing still at `Assets/ZUI/` is a single authored asset, `ZUIEnvelopePresets.asset` — no code, no asmdef. A new package editor tool may reference ZUI freely.

## Preview overlays — an effect draws its own, the window hardcodes none

If ONE effect needs its own drawing on a preview (a light's radius, a mask's boundary, a warp's pivot), the effect implements a capability interface and the host discovers it — **never** hardcode a per-effect toggle into a preview window's chrome, where it then sits permanently for every stack that doesn't contain that effect (exactly what "Light radius" did to `SpriteFxStackWindow`). For SpriteFx that interface is `ISpriteFxPreviewOverlay` in `Runtime/SpriteFx/SpriteFxPreviewOverlay.cs`; the host half (collect → toggle strip → draw) is `Editor/SpriteFx/SpriteFxPreviewOverlays.cs` and needs no edit at all. Copy `RelightModifier` (`Runtime/SpriteFx/SpriteFxRelight.cs`) as the worked example. Another tool (Pyre layers, Chunks, Lathe…) gets its OWN small interface in its own Runtime asmdef, copying that shape — not a reference to the SpriteFx one. Full recipe: the laubrary skill's `references/authoring.md` §15.

## Zoe palette — a character shows things only through its declared list

A Zoe shows a visual **only** by being asked, by name, for a state it declares. Game code decides *which* named state
plays and *when*; Laubrary alone decides what mechanically happens to the character (health, death, disposal, movement,
aiming, hit detection). Those two halves never swap.

**The rule, concretely.** If the look you need isn't on the character, **add a row to its declared list** and raise it by
name. Do **NOT** reach around the palette by spawning a Pyre/Chunks/SpriteFx effect straight from game code onto a Zoe,
by calling a view's `PlayClip` directly, or by bolting a bespoke "play this on hit" component onto the character. Those
all work, and every one of them makes the character's own editor window lie about what it can do — which is precisely the
bypass this model exists to prevent. This applies to **you, the assistant**, at least as much as to a human: the failure
mode is quietly re-solving in native game code what Zoe is for.

**Check it, don't just assume it (T-0096).** Every row in a Zoe's declared list shows whether anything has actually
requested it by name, and `Laubrary/Zoetrope/Palette Health` reports project-wide what got requested but never declared,
plus a heuristic scan for exactly the bypass patterns above (a direct Pyre/Chunks/SpriteFx spawn, a raw `PlayClip`, a
bolted-on reaction component) outside Laubrary's own package. Each row also has a ▶ Preview button (see it play without
Play mode) and a copy-name button (paste the exact declared name into a `Raise("...")` call instead of retyping it), and
`Zoe.DescribeDeclaredStates()` prints a character's whole declared list as text for writing gameplay code away from the
editor. Use these — don't hand-roll a bypass because it's faster than opening the character asset.

**Nothing plays on its own.** There is deliberately no built-in default-picker, not even when a character declares exactly
one hurt look or one death look. If gameplay code did not explicitly answer "which look?", nothing plays. An "if there's
only one, just use it" convenience must be written as ordinary game code that explicitly answers, never as a Laubrary
behaviour that fires by itself. (Owner's decision, T-0089 — a hidden exception here would undo the whole split.)

**Movement, aiming and hit detection are the exception, and stay Laubrary's.** For those, game code does not choose
behaviour at all — you pick from a shelf of Laubrary modules, and when a project needs a kind that doesn't exist yet the
answer is **a new module added to Laubrary**, not bespoke logic in one game. Laubrary is not a no-code game editor: it
removes from game code only what is genuinely reusable, and native code still drives whatever is specific to that game.

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
