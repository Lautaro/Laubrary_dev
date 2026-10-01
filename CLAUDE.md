# Laubrary Dev — project instructions

This is the **canonical development host for Laubrary** (`com.lautaro.arino.laubrary`). The package source
lives at `Assets/Packages/Laubrary/` (`Runtime/<Tool>/`, `Editor/<Tool>/`, `Samples~/`) and is copied out to
consumer projects' `Packages/com.lautaro.arino.laubrary/`. Git repo: `Laubrary_dev` (branch `master`).
The canonical Unity rules live in `D:\Unity\UNITY_DEV_GUIDE.md` (mandatory read).

## ⚖️ Framework or game? The balance every Laubrary change must hold

This applies to every agent touching Laubrary, here or in a consumer's embedded copy (OutBurner develops part of Laubrary inside its own `Packages/` folder). Two goals pull against each other, and neither may win by neglect.

1. **Laubrary is a framework for the core functions 2D games share.** Shooting, health and hits, characters and animation, levels and collision, draw order, fog, menus, saving, audio. It does them through assets and editors, and it is modular: a game adds its own kinds of things (its own weapon behaviour, its own effect, its own enemy brain) by writing a small piece of native code that plugs into a seam Laubrary offers, and that piece then shows up in Laubrary's editors without Laubrary itself being edited. Every such seam widens what every game can do.
2. **Laubrary must not grow to carry one game's special cases.** A feature that only makes sense for the game it was written for, pushed into the library, is paid for by every other game that uses it: more fields to ignore, more fixed lists, more code to load and understand. That is bloat.
3. **So hold the balance, and check it regularly.** The answer is almost never "put all of it in Laubrary" or "keep all of it in the game". It is: Laubrary owns the general *concept*, the *data* an author edits, and the *seam*; the game owns the specific *rules* and the *decision of when*. Drift toward either extreme usually happens by accident, one convenient shortcut at a time, so it has to be looked for on purpose.

**How to decide where a piece goes.** Ask: would a second, different 2D game (a top-down shooter, a platformer, a 3/4-view brawler) need this same idea, in some form? If yes, the idea belongs in Laubrary, expressed so the game can fill in its own specifics: an asset field, an editor control, a seam (an interface or a plug-in type that appears in a dropdown). If only this game would ever want it, it stays in the game and plugs into a seam Laubrary already offers. If the seam is missing, adding the *seam* to Laubrary is the right move; adding the *special case* is not.

**Warning signs of drift toward the game-in-the-library extreme:** a Laubrary enum or fixed list gaining a game's specific entry; a Laubrary field whose name only makes sense in one game; a Laubrary method branching on what kind of game is running.

**Warning signs of drift toward the library-too-thin extreme:** game code overwriting, at runtime, something a Laubrary editor lets an author set (so the editor shows one thing and the game does another); a magic number in game code for something every game of the genre needs (a character's foot size, how many rows of an object are solid); the same fix being written a second time in a second game; a comment saying "keep this local until it earns a reusable model" that nobody ever comes back to.

**Worked examples from OutBurner (2026-10-02).**
- *Balanced: weapons.* Laubrary supplies the weapon (rate, damage, pellets, spread, projectile pooling), the hit pipeline, and two seams: how a projectile moves, and a veto any game can attach to a hit. OutBurner adds its own weapon kinds and tuning, its own range-limited movement through the movement seam, and its own "no hits through a shelf" rule through the veto seam, without Laubrary knowing any of it exists.
- *Drifting thin: the warehouse's collision and depth.* Draw order is in Laubrary and is general (a tall object sorts by its base; a character sorts by its feet), which is right. But the collision that decides depth is game code with numbers in it: which rows of a shelf are solid is a hard-coded "bottom two rows" that overwrites what the tile editor says about each tile; the line bullets stop on is a hard-coded strip generated at load; every character's foot circle is a hard-coded size, and the player's is moved down by a hard-coded amount to suit one character's gun. Any 3/4-view game needs "this object's footprint is these rows" and "this character stands on a circle this big, here", so those belong on the tile/prop and the character assets in Laubrary, with the game keeping only its own rules about bullets and melee.

**Regular assessment.** Re-check the balance (a) whenever a new game or subgame starts using a Laubrary tool, (b) whenever game code overrides something a Laubrary editor exposes, and (c) whenever the same fix appears in a second place. Write what you find, both directions, in the task or handover you are working on, and raise a task for any drift rather than leaving it in a comment.

## Coplay bridge — target THIS editor first (before any Coplay action)

This project has a project-scoped `coplay-mcp` server (`.mcp.json`). The Coplay MCP discovers *every* open
Unity editor, so before using any Coplay tool you MUST point the bridge at this project and verify it:

1. `list_unity_project_roots` — confirm `D:\Unity\Laubrary Dev` is present (open it in Unity if not).
2. `set_unity_project_root` → `D:\Unity\Laubrary Dev`.
3. Verify with `execute_script` logging `Application.dataPath` — it must resolve under `D:\Unity\Laubrary Dev`.

If `Application.dataPath` points elsewhere, `check_compile_errors` will look clean despite new code and
reflection won't find new types — re-run steps 1–3. `set_unity_project_root` is per-session.

## Unity MCP server (`unity-mcp`) — second bridge, no targeting dance needed

This project's `.mcp.json` also registers Unity's own MCP server (`unity.exe mcp --project-path <this project>`), tools `mcp__unity-mcp__*`. It is pinned to this project, so it never needs `set_unity_project_root`. Prefer it for `eval`/`eval_file` (resolves `Laubrary.*`, returns a real value, no domain reload), `get_console_logs`, `get_scene_hierarchy` / `find_gameobjects` / `get_component_properties`, prefab edits and test status. Keep Coplay for UI construction, Input System, animator and generation tools. During a recompile/play-mode domain reload a call can time out — wait and retry, same as Coplay. Full rules: `D:\Unity\UNITY_DEV_GUIDE.md`, "`unity mcp`" bullet.

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

## Zounds — developed HERE again as of 2026-09-26; HH2Lab's copy is frozen

**This reverses the 2026-09-11 decision that sent Zounds development to HH2Lab.** That earlier rule said "do not develop Zounds features here first" and pointed at HH2Lab's copy as the newer one. As of 2026-09-26 the owner's direction is the opposite: **this project is the active home for Zounds, and specifically for the Scriptable-Audio-Pipeline work on branch `x/zounds-sap`.** HH2Lab wound its audio work down the same day and settles on what it already has; do not start new Zounds work there, and do not treat its copy as the authority for new features.

The current programme is tracked in this project's own task list under the Zounds SAP node (T-0406 is the umbrella). Read that before touching audio code — it records the plan, the current half-finished state of the branch, and what "finished" has been defined to mean.

The point of the work, in one paragraph: Unity's ordinary way of generating audio in C# attaches the audio mixer thread to the scripting runtime, and from then on any garbage collection anywhere in the game freezes that thread and produces an audible click. Removing allocations reduces how often that happens but cannot eliminate it. The fix is structural — move audio generation onto Burst-compiled code that the mixer thread reaches through a plain function pointer, never touching the managed runtime. **A single remaining use of the managed audio callback anywhere in the project re-attaches the thread and silently destroys the benefit for the whole application**, so removing the last use matters as much as adding the replacement, and the result can only be proven in a real player build — in the editor, with Burst compilation off, the immunity is not there at all.

### How audio actually plays here, as of 2026-09-27 — read this before touching playback

**A sound plays from its ORIGINAL audio, with its effect chain applied as it goes. There is no rendered-in-advance file in the path, and nothing renders one automatically.** If you find yourself reaching for a pre-rendered file, or adding something that writes one, you are working against the architecture.

The pooled audio source deliberately holds **no clip**. It still matters, because it is what carries the sound into the mixer — bus routing, group volume, distance attenuation and 3D position all come from it. That is also why there is no separate mixing graph here: the earlier native version of this engine needed one, with its own voice pool and buses, because it produced audio outside Unity's mixer and had to do its own summing. These voices go through an audio source, so the mixer provides all of it and concurrency is bounded by the existing source pool. **Do not port a voice graph into this.**

Three consequences worth knowing before they surprise you:

- **A source clip must be imported as Decompress On Load.** Unity will not expose samples for a streaming or compressed-in-memory clip, so such a sound cannot go through the chain; it falls back to a rendered file and says why, once per session. There is a menu check under Laubrary > Zounds > Checks that reports this per sound, and it is the first thing to run when a chain appears to do nothing.
- **An edit is heard on a sound that is already playing.** The authoring UI pushes each parameter change to every voice playing that sound. Each voice resolves the change against the layout IT started with, never the sound's current layout, because the edit being delivered may already have rebuilt that.
- **A chain's declared decay is added to a sound's length.** A delay or reverb still sounds after the source runs out, and without this the sound would be declared over and its audio source recycled mid-tail.

**The seven old per-sound effect settings (gain, equaliser, compression, normalisation, fade, volume and pitch curves) are no longer an authoring surface.** They only ever worked by being rendered into a file. They are still readable, because a sound that still carries them has them converted to an equivalent chain — on the fly at play time, or permanently through the conversion action in the Zounds menu. Do not reintroduce UI for editing them directly; the chain editor is where that belongs.

Rendering to a file still exists as something you can ask for, because bouncing a sound is occasionally wanted. It is no longer something that happens as a side effect of an edit.

### Game code control (ZPOC), as of 2026-09-29 — read before touching tokens, modifiers or snapshots

Game code shapes a playing sound through its token: ZPOC values on modifiers (`token.SetZpoc`), the Code modifier, tracks (`token.Track`), snapshots and glides (`token.GlideToSnapshot`, which returns a handle that can glide back), and project-wide values (`ZoundEngine.SetGlobalZpoc`). The design document is `D:/Claude@GDrive/Zounds Programmatic Control Research 2026-09-29.md`; the CHANGELOG entry has a worked example. Rules that are easy to break:

- **Nothing at runtime writes to the saved sound.** Values live on the token and in each voice's own copy of the chain; a play is always per instance.
- **Values reach the voice as fixed-size commands through the audio graph and are eased there, once per control block.** A value or glide sent before the voice exists is kept by the generator and applied when the voice is made; any new command kind must follow the same rule, or it is silently lost on a replayed token or a value set before Play (this was a real bug).
- **The order a value is resolved in is part of the UI contract:** the play's own value, then the parent Zequence play's, then the project-wide value, then the resting value (which a snapshot moves). The editor states it wherever a value is driven; keep that text in step with any change.
- **Every lookup that finds nothing reports once to the diagnostics list (the Problems tab) and does nothing.** Never throw, and never allocate on a repeated miss (kept check 25 measures it).
- **Amber means game code**, blue means a modulator. Do not reuse amber for anything else in the Zounds UI.
- Settings that size a voice's memory (a delay's longest time) are deliberately left out of snapshots.

### Non-destructive editing in a Zequence, as of 2026-10-01 — read before touching trims, curves or the Zequence window

A Zequence of placed, trimmed Klips is the non-destructive edit; the design and the decisions taken for it are in `D:/Claude@GDrive/Zounds Non-Destructive Editing Research 2026-09-29.md` (section 13). Rules that are easy to break:

- **A curve that follows the waveform says where its x axis is anchored** (`ZoundModifier.curveAnchor`): the trim (older sounds) or the source file's own seconds. Anything that reads or draws such a curve goes through `CurveAnchor`; never write a second mapping. A sound converts on its first curve or trim edit, never on load, and a preset-linked chain never converts.
- **A track can play its own excerpt** (`ZoundEntry.ownTrim` + `trimStart`/`trimEnd`, source seconds), passed to the play as `ZoundArgs.excerpt`. A play of an excerpt reads old trim-anchored curves from a converted COPY of the chain; the saved sound is not touched.
- **A trim made in a Zequence never changes the sound anywhere else** (`TimelineEdits.EditsKlipTrim`): only a local Klip played by that one track has its own trim edited.
- **Pieces split from one sound share one local Klip.** Anything that removes or converts a track must check `CompositeZoundEditing.SharedLocally` before deleting or moving that Klip.
- **The timeline draws a track through the engine's own source-to-time integration** (`ZoundSapPlayback.TryMapSourceToPlay`), so a drawn piece ends exactly where a play does. The waveform comes from an in-memory summary, never a file.
- Kept checks 30 (anchoring), 31 (excerpts, play from here) and 32 (the verbs, the bake) measure these.

Background documents: the authoritative technical foundation is `D:/Claude@GDrive/Zounds GC-Stutter-Free Audio Architecture Research 2026-09-26.md` (sections 1 and 7 for the plan, 8 for the validation test). `D:/Claude@GDrive/HH2 Audio Effect Chains Architecture.md` describes the existing engine the new path has to match. The 2026-09-25 native-DSP roadmap is **partly superseded** — its phases assume a hand-written C++ plugin as the target, which the 2026-09-26 research replaces; read it for history, not direction. The 2026-09-11 lifetime health report is at `D:/Claude@GDrive/Zounds Lifetime Health Report 2026-09-11.md`.

## Cartographer and MetaMapper — developed HERE since 2026-10-02

The level tool (Cartographer: level editor, tileset builder, prop editor) and MetaMapper (named metadata layers on sprites, props and levels) were built inside OutBurner's embedded copy and came home on 2026-10-02 (`bed7c454`), by the owner's decision: they are meant to end up in Laubrary, so they are developed here. Edit them in this tree; OutBurner receives them by the three-way merge in its `HANDOVER.md` §7.7 (sync point: this `bed7c454` ↔ OutBurner `77fc36e2`). OutBurner still owns Fov and Lattice. Planned next for MetaMapper: a Shapes layer kind (rectangles and circles) for colliders, a MetaMapper panel in the Zoe editor, a generic "build colliders from a layer" helper, and moving Launimator's meta layers onto it (its design doc, `METAMAPPER_DESIGN.md`, is in the OutBurner repo).

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
fallbacks today, and prime expansion candidates): a **text-input field**, an **object/asset picker**, and an
**enum popup/dropdown**. (A **colour field** was listed here too until 2026-09-03 — that was stale: `Z.Color`
exists in `Zui/Toolkit/Zui.cs` and is already what `ZuiReflect` draws every reflected `Color` with.) Genuinely bespoke *canvas painting* (a 2D preview stage, a thumbnail
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
