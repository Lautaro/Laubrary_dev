# Chunks use-case walks — rules for every task under ChunksUseCases-2026-09-16

**Why this programme exists.** The owner (2026-09-16): "the biggest blocker now is to get Chunks to be useful and user friendly. It requires the UI to be impeccable." The Chunks window rebuild (node ChunksEditor-2026-09-02) was verified part by part, but nobody has ever built the owner's real use cases from an empty window. These walks do exactly that, and their findings are the work list. "It works" is not the bar; "a person who has never seen this window builds it without friction" is.

## The six use cases (owner's words, T-0119 description + 2026-09-16)

1. Spawn several Pyres, and for each change spawn parameters like spawn time, position, perhaps scale, alpha or tint.
2. Spawn Pyres and fling them.
3. Spawn a specific order of Pyres in specific positions over a timeline.
4. Spawn a particle spray based on the sprite of a Zoe.
5. Fracture and fling the sprite of a Zoe.
6. Fracture the Floating Disc Zoe, have a Pyre fire off behind the fragments. Fragments are flung in different directions. Spray particles in 360 directions. Several smaller Pyres fire off in between the fragments. The layering should be something like, back to front... (owner's list, top of the list = as written):
   ```
   Big pyre
   Fragment 1
   Pyre 1
   Fragment 2
   Pyre 2
   Particles
   Fragment 3
   ```
   Read that list as the owner wrote it and decide (and say) which end is front; the point is that single fragments and single Pyres INTERLEAVE in depth.

Owner principles from T-0119 that every walk judges against: minimal cognitive load; a recipe shows only the surfaces its capabilities bring; exactly ONE timeline and only when it earns its place; bespoke helper assets must not clutter the shared browser (private/inline); a reference is always a picker.

## Mandatory reads, in order
1. `D:\Unity\UNITY_DEV_GUIDE.md`, `D:\UNITY\Laubrary Dev\CLAUDE.md`.
2. `D:\AgentGuide\ui-rules.md` in full (incl. THE HANDOVER WALK) and `C:\Users\Lauta\.claude\skills\laubrary\references\ui-layout-rules.md`. These are the yardstick for every friction you log.
3. `D:\UNITY\Laubrary Dev\.agenthq\workspace\ChunksEditor\PROGRAMME_RULES.md` — its editor, code, git and handover rules apply here unchanged (use THIS session's trailer: `Claude-Session: https://claude.ai/code/session_016JzvMqGyCk98RKgYCjTRc4`).
4. `workspace\ChunksEditor\CHUNKS-QUICK-MANUAL.md` (what a user is told) and `CHUNKS-DESIGN-DECISIONS.md` (what was decided, §8 is binding unless the owner reopens it).
5. `C:\Users\Lauta\.claude\projects\D--UNITY-Laubrary-Dev\memory\editor-walk-probe-gotchas.md` and `printwindow-editor-screenshot.md` — they have cost whole sessions before. `workspace\T-0217\PM-HANDOVER-WALK.md` describes how the last walk drove the window (PrintWindow + NavigationSubmitEvent on Buttons, PointerDown/Up on `zui-menu__item` rows, MicroSlider's private `SetValue(v, notify:true)`, `Undo.IncrementCurrentGroup()` before every scripted action).

## How a walk is done
- **Start from an empty window**: `Laubrary/Chunks`, New. Build the recipe ONLY through the window's own controls (synthesised UI events on the real elements, or the window's own methods where a gesture cannot be synthesised — say which). Never write the asset's fields directly; that would hide exactly the friction we are looking for.
- Save each finished recipe as `Assets/Demos/ChunksDemo/UseCases/UC<n> <short name>.asset` (New in the window, then rename through the window/AssetDatabase — never save a SerializeReference asset from a CLI eval).
- **Watch it move**: the preview playing over time (captures at several playhead times, not one still), and a real burst in Play mode (a scene with a `ChunkEmitter`, or Mirage's handoff). For Zoe use cases use the Floating Disc Zoe (`Assets/Demos/` — find it) and trigger the recipe the way a game would (a Zoe reaction/death effect row) as well as standalone.
- **Log every friction**, each as: what you were trying to do · what you had to do · what should have happened · severity (BLOCKER = the use case cannot be built; MAJOR = built only with a workaround or hunting; MINOR = cosmetic/wording) · the UI-guide rule it breaks, if any. Count clicks/steps per use case. Name anything shown that the recipe does not use.
- Report per use case: `workspace\<task id>\WALK.md` + captures. End with "Verdict: buildable as-is / buildable with workarounds / not buildable", and the three buckets (verified by probe / by eye / not verified).
- **Walks do not fix code.** If a fix is a one-line obvious bug that blocks the rest of the walk, stop and say so in the handover; the PM files and dispatches fixes. Design gaps become proposals in the report (with the smallest change that would close them), not code.
- Do not delete or modify existing assets in `Assets/Demos/ChunksDemo/` (the owner's); work on your own new assets only. Do not touch `Assets/Mirage/MirageStage.unity` or `Test 2.asset` (uncommitted owner edits).
- Commit only your own new assets + reports, by path.
