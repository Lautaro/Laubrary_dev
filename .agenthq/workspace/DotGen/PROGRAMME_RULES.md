# DotGen programme — rules for every task agent

These rules apply to every task under the AgentHQ node `DotGen-2026-09-03`. Read them before touching anything. The owner is away for the whole programme: **no task may end waiting on him.** Make the call, write down which call you made and why, and continue.

## Where you work
- **Working copy: `D:\UNITY\Laubrary Dev`**, branch `dev`. Run every command from there. There is a SECOND worktree at `D:\UNITY\Laubrary Dev - Shaper` with its own editor (Unity CLI port 7801): never touch it, never point a tool at it.
- Package source: `Assets/Packages/Laubrary/Runtime/DotGen/` and `Editor/DotGen/` (you create them in W1.1/W2.1). ZUI lives in `Assets/Packages/Laubrary/Zui/`. Pyre (`Runtime/Pyre/`, `Editor/Pyre/`) and Chunks (`Editor/Chunks/`) are **read-only reference**: copy patterns, never edit them.
- The POC: `D:\UNITY\Laubrary Dev\.agenthq\workspace\DotGen\POC-DotFoundry-Specification.md` (normative maths/ranges/defaults) and `POC-DotFoundry.html` (behavioural reference — read its `<script>` for the algorithms; it is the same pipeline in JS).
- Task deliverables (reports, screenshots, probes' output) go to `D:\UNITY\Laubrary Dev\.agenthq\workspace\<your task id>\`.

## Mandatory reads, in this order
1. `D:\Unity\UNITY_DEV_GUIDE.md` (global Unity rules; never run the Test Runner; Coplay/Unity-CLI targeting).
2. `D:\UNITY\Laubrary Dev\CLAUDE.md` (bridge targeting, menus, undo, ZUI-for-all-UI, no unrequested surface).
3. `C:\Users\Lauta\.claude\skills\laubrary\references\ui-layout-rules.md` — **a hard gate before any UI code** — and `authoring.md` beside it; `zui.md` is the control reference.
4. `D:\UNITY\Laubrary Dev\.agenthq\workspace\DotGen\DOTGEN-DESIGN-DECISIONS.md` — the build spec (PM decisions; it wins over the POC where they differ). Then the two POC files. Then, once they exist, `workspace/DotGen/DOTGEN-INVENTORY.md` (file:line facts) and `DOTGEN-BEHAVIOUR-CHECKLIST.md` (acceptance rows).
5. Your task's own description and todos on the board (`GET http://127.0.0.1:8778/api/task?project=Laubrary_Dev&id=<id>`). Check todos off as you go (`POST /api/task/todo/check` with `{project,id,todoIndex,checked:true,actor:"agent"}`).

## The Unity editor — one driver at a time
- The editor for this project is the one whose project path is `D:\UNITY\Laubrary Dev` (Unity CLI pipeline **port 7800**, PID per `& "C:\Users\Lauta\AppData\Local\Unity\bin\unity.exe" status`). Prefer the `mcp__unity-mcp__*` tools (pinned to this project; `eval`/`eval_file` resolve `Laubrary.*`). If you use Coplay: `set_unity_project_root` to `D:\UNITY\Laubrary Dev` and verify `Application.dataPath` before trusting any result — two editors with the same productName are open.
- **Your task description says whether you have editor rights.** *Code-only*: you do not call Coplay, unity-mcp or the Unity CLI at all; write the code, self-review it against the compiler in your head (namespaces, asmdef references, `using`s), commit, hand over; the PM compiles it and sends errors back. *Editor rights*: you are the only agent compiling/refreshing/probing during your task; if you see a compile error from a sibling task's new file, report it in your handover instead of fixing or deleting that file.
- Never run the Test Runner. Never enter Play mode. Never exercise a Save/Rebuild path against a real user asset; test on a duplicate. Never save a `SerializeReference` asset from a CLI `eval` (it destroys managed-reference ids) — use an Editor script + `AssetDatabase.SaveAssets` or the window itself.
- Compile check that actually works: `EditorUtility.scriptCompilationFailed` plus a probe for a symbol you just added (the CLI console is cumulative and can hide fresh errors).
- Screenshots of a window: the `printwindow-editor-screenshot` recipe (PrintWindow flag 2 on the window's HWND found by title) — see `D:\Unity\UNITY_DEV_GUIDE.md` "Screenshotting a SPECIFIC editor window". Open your own window instance with a unique title via `CreateInstance` + `ShowUtility`, never `GetWindow` (the owner's own window layout must not change).

## Code rules (non-negotiable)
- **ZUI only**, UI Toolkit `Z.*` factories; no `EditorGUILayout`/`GUILayout`, no raw `EnumField`/`Toggle`/`Slider`/`Foldout`/`PopupField`. Enum → `Z.Segmented`/`Z.MiniRadio` (never a dropdown — the POC's `<select>`s are radios here); bounded scalar → `Z.MicroSlider` with `defaultValue`; min/max pair → `Z.MicroMinMax`; bool → `Z.Toggle`; X/Y → `Z.Pad`; a reference is ALWAYS a picker, never a typed string (a *declaration* — a generator's or module's own name — is the one legitimate `Z.TextInput`). A missing ZUI control is a smell: add it to `Zui/Toolkit/` as a shared control (say so in the handover), don't hand-roll it in the window.
- **Undo** on every data edit through the window's `Dirty(...)` wrapper; never mutate the asset outside it. `Undo.RegisterCreatedObjectUndo` for created assets. One undo per drag gesture.
- **Tooltip on every control**, written as the effect, not the label. No on-screen instruction text (the POC's callout paragraph goes into tooltips).
- **No new `[MenuItem]`s**, windows, or speculative surface. DotGen has exactly one menu item: `Laubrary/DotGen`.
- **Stable workspace**: nothing the user is working on moves when contextual UI appears; rebuild the section body, not the window; keep the left-pane scroll offset across a rebuild.
- **Deterministic**: hash from seeds (POC §6, bit-exact); no `UnityEngine.Random`/`System.Random` in the DotGen assemblies.
- Comments explain why, never history. Write `Pyre`, never `PyrePlus`. Write `DotGen`, never `Dot Foundry`, except when citing the POC files.

## Git
- Commit on `dev` when your task compiles (code-only: when self-reviewed). Stage **only your own files by path** — never `git add -A` / `git add .`; other agents work in the same tree and the owner has uncommitted files there (`.mcp.json`, `CLAUDE.md`, `Assets/Temp/`, `qwen-delegate.ps1`, `Assets/Demos/ChunksDemo/Ring Blast.asset`) that you must never stage. Never `git stash`, never rewrite history. If `.git/index.lock` exists, wait a few seconds and retry. Always add the `.meta` beside every new file/folder you create (let the editor generate them when you have editor rights; when code-only, the PM's compile pass generates them and commits them).
- Message: one line `DotGen: <what changed> (<task id>)`, a body from a systems perspective, then the trailers `Co-Authored-By: Claude <model> <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01EYyzJuZuH4gHjcn3vRisgR`.
- Add one line under `## [Unreleased]` in `Assets/Packages/Laubrary/CHANGELOG.md` when you ship user-visible behaviour. Do **not** bump `package.json`.

## Handing over
`POST http://127.0.0.1:8778/api/task/handover` with `{project:"Laubrary_Dev", id, text, details, technicalDetails, proof:[{text,checked}], attachments:[absolute paths], newStatus:"done"}`. `text` is written for the owner: systems perspective, no process narration, no class names. **Three buckets, always:** verified by probe / verified by eye (a real window, a real capture) / not verified. "It compiles" is bucket one. Every requirement in your task must be traced to the method/control that serves it before you claim it — a caveat is not an implementation. **Never hand over `blocked`** in this programme: if a decision is needed, make it, record it under "Decisions I made" in `details`, and continue. Only a genuine environment failure (editor gone, disk full) justifies stopping — and then say exactly what the PM must do.
