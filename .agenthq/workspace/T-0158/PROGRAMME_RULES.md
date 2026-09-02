# Shaper completion programme — rules for every task agent

These rules apply to every task under the AgentHQ node "Shaper completion (T-0158 programme)". Read them before touching anything. They are short because the documents they point at are mandatory reads, not optional background.

## Where you work
- **Working copy: `D:\UNITY\Laubrary Dev - Shaper`**, a git worktree on branch `feat/shaper`. Run every command from there. Never `cd` to `D:\UNITY\Laubrary Dev` (the main copy) and never edit files there, except that task deliverables (reports, screenshots) go to `D:\UNITY\Laubrary Dev\.agenthq\workspace\<your task id>\`.
- Package source: `Assets/Packages/Laubrary/Runtime/Shaper/`, `Editor/Shaper/`, and the Pyre bridge `Runtime/PyreShaper/`, `Editor/PyreShaper/`. Pyre's own files under `Runtime/Pyre/` and `Editor/Pyre/` are **read-only reference** for this programme: copy patterns from them, never edit them.
- `Assets/ShaperMock/` is a throwaway mock. It is **not** the spec and you do not touch it.

## Mandatory reads, in this order
1. `D:\Unity\UNITY_DEV_GUIDE.md` (global Unity rules; note the "never run the Unity Test Runner" rule and the Coplay/Unity-CLI targeting rules).
2. The project `CLAUDE.md` in the worktree root (Shaper worktree rules, Coplay targeting, menus, undo, ZUI-for-all-UI).
3. `C:\Users\Lauta\.claude\skills\laubrary\references\authoring.md` and `ui-layout-rules.md` — **a hard gate before any UI code**. `zui.md` beside them is the control reference.
4. `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0158\SHAPER_CRISIS_ANALYSIS.md` and, for `file:line` facts, `shaper-inventory.md` + `pyre-inventory.md` in the same folder.
5. Your task's own description on the board (`GET /api/task?project=Laubrary_Dev&id=<id>`).

## The Unity editor — one driver at a time
- Two editors are open with the **same productName**. The Shaper worktree editor is the one whose project path is `D:\UNITY\Laubrary Dev - Shaper` (Unity CLI pipeline port 7801 as of 2026-09-02; verify with `unity status`). Coplay: `set_unity_project_root` to the worktree and verify `Application.dataPath` before trusting any result.
- **Your task description says whether you have editor rights.** If it says *code-only*, you do not call Coplay or the Unity CLI at all; write the code, self-review it, commit, and hand over. The PM compiles it and sends errors back to you. If it says *editor rights*, you are the only agent allowed to compile/refresh/probe during your task, and you may see compile errors from a sibling task's new file — report them in your handover instead of fixing or deleting that file.
- Never run the Test Runner. Never enter Play mode to verify something a probe can answer. Never exercise a Save/Rebuild path against a real user asset; test on a duplicate.

## Code rules (non-negotiable)
- **ZUI only**, UI Toolkit `Z.*` factories, no `EditorGUILayout`/`GUILayout`, no raw `EnumField`/`Toggle`/`Slider`. Enum → `Z.MiniRadio`/`Z.Segmented`; bounded scalar → `Z.MicroSlider`; `ZUIValue` → the window's `Val()`; X/Y → `Z.Pad`; bool → `Z.Toggle`. A reference is always a picker, never a typed string.
- **Undo** on every data edit through the window's existing `Change(...)` / `Dial(...)` / `Val(...)` wrappers (`Editor/Shaper/ShaperWindow.cs:656-688`). Never mutate the document outside them.
- **Tooltip on every control**, written as the effect, not the label.
- **No new `[MenuItem]`s**, no new windows, no speculative surface. Shaper has exactly one menu item: `Laubrary/Shaper`.
- **Deterministic**: no `UnityEngine.Random`, no `System.Random` in generator paths; hash from seeds (`ShaperValue`, `ShaperCompiler` avalanche hash).
- **`i/(N-1)` frame→phase mapping is fixed** (`ShaperClock`). Never write a second conversion.
- **Never state a Shaper fact from a doc or memory**: verify in engine source and cite `file:line` in your handover.
- Comments explain why, never history. No "PyrePlus" anywhere.

## Git
- Commit on `feat/shaper` when your task is done and compiles (or, for code-only tasks, when you have self-reviewed it). Stage **only your own files by path** — never `git add -A` / `git add .`, other agents are working in the same tree. If the index is locked, wait a few seconds and retry.
- Commit message: one line `Shaper: <what changed> (<task id>)`, a body from a systems perspective, and the trailer lines the session rules require (`Co-Authored-By: Claude <model> <noreply@anthropic.com>` plus the `Claude-Session:` line if you have one).
- Add one line under `## [Unreleased]` in `Assets/Packages/Laubrary/CHANGELOG.md` describing the behaviour change. Do **not** bump `package.json`.
- Never `git stash` (shared stash stack). Never rewrite history.

## Handing over
`POST http://127.0.0.1:8778/api/task/handover` with `{project:"Laubrary_Dev", id, text, details, technicalDetails, proof:[{text,checked}], attachments:[paths], newStatus:"done"}`. The text is written for the owner, systems perspective, no process narration. **Three buckets, always:** verified by probe / verified by eye / not verified. A subagent's or your own "it compiles" is bucket one, not bucket two. If you are blocked on a decision, post a question with `POST /api/task/question` and hand over with `newStatus:"blocked"` — do not guess on anything destructive, but do make ordinary judgment calls yourself and say which you made.
