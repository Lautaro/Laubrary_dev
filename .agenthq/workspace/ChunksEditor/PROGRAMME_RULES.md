# Chunks editor programme — rules for every task agent

These rules apply to every task under the AgentHQ node `ChunksEditor-2026-09-02` ("Chunks editor — capability stack in Pyre's style"). Read them before touching anything.

## Where you work
- **Working copy: `D:\UNITY\Laubrary Dev`**, branch `dev`. Run every command from there. There is a SECOND worktree at `D:\UNITY\Laubrary Dev - Shaper` with its own editor: never touch it, never point a tool at it.
- Package source: `Assets/Packages/Laubrary/Runtime/Chunks/` and `Editor/Chunks/`. ZUI lives in `Assets/Packages/Laubrary/Zui/`. Pyre (`Runtime/Pyre/`, `Editor/Pyre/`) is **read-only reference** for this programme: copy patterns, never edit it (the Pyre→Chunks assembly reference stays as it is).
- `Assets/ChunksMock/` is a throwaway mock (untracked). It is inspiration for the card/timing/guide interaction, **not** the spec, and you do not edit it or copy its hand-rolled controls into production.
- Task deliverables (reports, screenshots) go to `D:\UNITY\Laubrary Dev\.agenthq\workspace\<your task id>\`.

## Mandatory reads, in this order
1. `D:\Unity\UNITY_DEV_GUIDE.md` (global Unity rules; the "never run the Unity Test Runner" rule; Coplay/Unity-CLI targeting).
2. `D:\UNITY\Laubrary Dev\CLAUDE.md` (Coplay targeting, menus, undo, ZUI-for-all-UI, no unrequested surface).
3. `C:\Users\Lauta\.claude\skills\laubrary\references\ui-layout-rules.md` — **a hard gate before any UI code** — and `authoring.md` beside it; `zui.md` is the control reference.
4. `D:\UNITY\Laubrary Dev\.agenthq\workspace\ChunksEditor\CHUNKS-DESIGN-DECISIONS.md` — the build spec. Then `workspace/T-0123/CHUNKS-EDITOR-BLUEPRINT.md` (the mock's decisions) and, for `file:line` facts, `workspace/ChunksEditor/CHUNKS-INVENTORY.md` once it exists.
5. Your task's own description and todos on the board (`GET http://127.0.0.1:8778/api/task?project=Laubrary_Dev&id=<id>`). Check todos off as you go (`POST /api/task/todo/check` with `{project,id,todoIndex,checked:true,actor:"agent"}`).

## The Unity editor — one driver at a time
- The editor for this project is the one whose project path is `D:\UNITY\Laubrary Dev` (Unity CLI pipeline port 7800 as of 2026-09-02; verify with `& "C:\Users\Lauta\AppData\Local\Unity\bin\unity.exe" status`). Coplay: `set_unity_project_root` to `D:\UNITY\Laubrary Dev` and verify `Application.dataPath` before trusting any result — two editors with the same productName are open.
- **Your task description says whether you have editor rights.** *Code-only*: you do not call Coplay or the Unity CLI at all; write the code, self-review it, commit, hand over; the PM compiles it and sends errors back. *Editor rights*: you are the only agent compiling/refreshing/probing during your task; if you see a compile error from a sibling task's new file, report it in your handover instead of fixing or deleting that file.
- Never run the Test Runner. Never enter Play mode to verify what a probe can answer. Never exercise a Save/Rebuild path against a real user asset; test on a duplicate. Never save a `SerializeReference` asset from a CLI `eval` — it destroys managed-reference ids; use an Editor script + `AssetDatabase.SaveAssets` or the window itself.
- Compile check that actually works: `EditorUtility.scriptCompilationFailed` plus a probe for a symbol you just added (the CLI console is cumulative and can hide fresh errors).

## Code rules (non-negotiable)
- **ZUI only**, UI Toolkit `Z.*` factories; no `EditorGUILayout`/`GUILayout`, no raw `EnumField`/`Toggle`/`Slider`/`Foldout`. Enum → `Z.Segmented`/`Z.MiniRadio`; bounded scalar → `Z.MicroSlider`; min/max → `Z.MicroMinMax`; bool → `Z.Toggle`; X/Y → `Z.Pad`; a reference is ALWAYS a picker (`LauAssetElement.Build` / the existing picker hooks), never a typed string. A missing ZUI control is a smell: add it to ZUI (say so in the handover), don't hand-roll it in the window.
- **Undo** on every data edit through the window's `Dial(...)` wrappers; never mutate the asset outside them. `Undo.RegisterCreatedObjectUndo` for created assets.
- **Tooltip on every control**, written as the effect, not the label. No on-screen instruction text.
- **No new `[MenuItem]`s**, windows, or speculative surface. Chunks has exactly one menu item: `Laubrary/Chunks`.
- **Stable workspace**: nothing the user is working on moves when contextual UI appears; rebuild the card, not the window; carry scroll offset and playhead across any rebuild.
- Deterministic preview: hash from seeds; no `UnityEngine.Random`/`System.Random` in code the preview runs.
- Comments explain why, never history. Write `Pyre`, never `PyrePlus`.

## Git
- Commit on `dev` when your task compiles (code-only: when self-reviewed). Stage **only your own files by path** — never `git add -A` / `git add .`; other agents work in the same tree. Never `git stash`, never rewrite history. If `.git/index.lock` exists, wait a few seconds and retry.
- Message: one line `Chunks: <what changed> (<task id>)`, a body from a systems perspective, then the trailers `Co-Authored-By: Claude <model> <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_015LwfYMJASQMWgpiRvfmYfN`.
- Add one line under `## [Unreleased]` in `Assets/Packages/Laubrary/CHANGELOG.md`. Do **not** bump `package.json`.

## Handing over
`POST http://127.0.0.1:8778/api/task/handover` with `{project:"Laubrary_Dev", id, text, details, technicalDetails, proof:[{text,checked}], attachments:[absolute paths], newStatus:"done"}`. `text` is written for the owner: systems perspective, no process narration, no class names. **Three buckets, always:** verified by probe / verified by eye (a real window, a real capture) / not verified. "It compiles" is bucket one. Every requirement in your task must be traced to the method/control that serves it before you claim it — a caveat is not an implementation. If you are blocked on a decision, `POST /api/task/question` and hand over with `newStatus:"blocked"`; make ordinary judgment calls yourself and say which you made.
