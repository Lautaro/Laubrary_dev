# Shaper — the second working copy and the second Unity editor

**T-0100, Wave 0. Set up 2026-08-30.** Implements section B10 of `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\SHAPER_THE_DESIGN.md`.

This is the operating manual for the two-copy setup. It lives in the **main** copy on purpose — the board and the docs stay in one place; only the *code* moves to the second copy.

---

## 1. What exists now

| | Main copy | Shaper copy |
|---|---|---|
| Folder | `D:\UNITY\Laubrary Dev` | `D:\UNITY\Laubrary Dev - Shaper` |
| Branch | `feat/lathe` | `feat/shaper` |
| Created from | — | `feat/lathe` @ `272e6df7` |
| Unity editor | its own, already open | its own, second instance |
| AgentHQ board | **the live board** | frozen snapshot, do not use |
| What gets built here | everything except Shaper | Shaper only |

It is a **git worktree**, not a copied folder: one repository, one history, one set of branches. Bringing Shaper home is `git merge`, not a manual replay. Confirm with `git worktree list` from either folder.

There is a third worktree already on disk, `D:\UNITY\Laubrary Dev - TapestryPort` on `feat/tapestry-kiln-ports`. It has never had Unity opened on it. It is unrelated to Shaper and is left exactly as it was.

### Removing it later

From the main copy: `git worktree remove "D:\UNITY\Laubrary Dev - Shaper"` (add `--force` if it still has uncommitted work). That deletes the folder and deregisters it; the `feat/shaper` branch itself survives independently.

---

## 2. Opening the Shaper editor

Normally: just open `D:\UNITY\Laubrary Dev - Shaper` from Unity Hub, or leave it open. It is a normal Unity 6000.3.10f1 project.

From a script, **quote the paths explicitly**. Windows PowerShell 5.1's `Start-Process -ArgumentList @(...)` array form does *not* quote arguments containing spaces, and "Laubrary Dev - Shaper" has three of them — the first launch attempt died silently in under a second with no log at all because of exactly this:

```powershell
$wt   = 'D:\UNITY\Laubrary Dev - Shaper'
$log  = "$wt\Logs\shaper-editor.log"
$args = '-projectPath "{0}" -logFile "{1}"' -f $wt, $log
Start-Process -FilePath "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe" -ArgumentList $args
```

Passing `-logFile` is worth doing for the second instance: the two editors would otherwise both want the one default `Editor.log`. `Logs/` is gitignored, so the log file never enters the repository.

The first launch has to build the whole `Library/` from scratch — the main copy's is 8.6 GB — so expect a long import the first time and a fast start every time after.

### `Assets\Plugins\` is gitignored and a fresh worktree cannot compile without it

This is the one thing that will bite anyone who makes another working copy of this project. `.gitignore` excludes **all of `Assets/Plugins/`** — the paid Asset Store packages (Shapes, Febucci Text Animator, Sirenix, …) are deliberately not in the repository. `Assets\Packages\Laubrary\Runtime\VectorRendering\LazorRenderer.cs` uses the `Shapes` namespace, so a worktree without that folder fails to compile with a wall of `CS0246` and Unity stops at an **"Enter Safe Mode?"** modal before the editor ever opens.

The fix is a straight copy — it is small (1693 files, 13 MB) and it lands gitignored on the other side too, so it never shows up in `git status`:

```powershell
robocopy "D:\UNITY\Laubrary Dev\Assets\Plugins" "D:\UNITY\Laubrary Dev - Shaper\Assets\Plugins" /E
```

It has already been done for the Shaper copy. Two related gitignored items were checked and deliberately **not** copied: `Packages\com.bezi.sidekick\` (a third-party AI editor plugin, nothing in the project depends on it) and `Packages\Coplay\Editor\` (Coplay's own logs and formatting cache, regenerated on demand). Because Sidekick is an embedded package, the Shaper copy's `Packages\packages-lock.json` differs from the main copy's by exactly that one block — leave it uncommitted, or take main's side if it ever conflicts.

### Two phantom diffs in the Shaper copy — both expected, neither is drift

`git status` in the Shaper copy is not empty and is not supposed to be. It shows exactly two files, and both are noise:

- `Packages\packages-lock.json` — the missing Sidekick block described above. Real, expected, leave it.
- `Packages\Coplay\.gitignore` — shows as modified but its content hashes **identical** to the committed version; it is a line-ending bookkeeping artefact from Coplay initialising in a second worktree. Don't chase it, and don't commit it.

Anything else appearing in `git status` there is genuine and worth looking at.

### A blocked editor lies about why it is unreachable

While that Safe Mode modal was up, Coplay's `list_unity_project_roots` **did** list the Shaper copy, but every actual tool call failed with `Unity Editor is not running at the specified project root`. That message is misleading: the editor was running fine, it was sitting on a modal dialog and had never finished starting, so its in-editor server was never listening. If you see that error against an editor you know is running, go and look at the editor's actual windows before believing it.

Its first launch also logged `Timeout while updating assemblies` against Coplay's own DLLs — the API Updater timing out under the CPU load of a first import. It went away on the next launch and needed no action.

---

## 3. Driving the right editor — the one rule that will bite

Both folders have the **identical** `productName` (`Package Developer`) and `companyName` (`DefaultCompany`), because `ProjectSettings.asset` is a tracked file. Unity does put the folder name in the window title (`Laubrary Dev - Shaper - Untitled - …` vs `Laubrary Dev - Untitled - …`), so a human can tell them apart at a glance — but nothing you can *query* distinguishes them except the path. Every programmatic check has to be a path check.

Coplay discovers every open editor and `set_unity_project_root` is per-session, so before any Coplay action:

1. `list_unity_project_roots`
2. `set_unity_project_root` → the folder you actually mean
3. `execute_script` logging `Application.dataPath`, and read the **whole path**, not the project name

Step 3 has a ready-made probe: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0100\WhichEditorProbe.cs`, which reports `Application.dataPath`, `productName` and the editor's process id in one line. It sits outside `Assets/` so neither editor imports it; point `execute_script` at that absolute path from either copy.

The Shaper copy's own `CLAUDE.md` / `AGENTS.md` have been rewritten so their Coplay section names the Shaper path. Before that fix they were verbatim copies naming the main copy — an agent working in the Shaper folder would have obediently pointed the bridge at the main editor and then reported "no compile errors" about code the main editor has never seen. That block in the Shaper copy is marked worktree-local and is meant to be deleted at merge-back.

The `unity` CLI (`C:\Users\Lauta\AppData\Local\Unity\bin\unity.exe pipeline list`) is the quickest independent read of which editors are live and on which paths.

---

## 4. The board stays in the main copy

`.agenthq/` is tracked, so the Shaper copy contains a **frozen snapshot** of the board as it stood at commit `272e6df7`. It is stale from the moment it was created and must never be used:

- Never create, edit or tick a task through `D:\UNITY\Laubrary Dev - Shaper\.agenthq\`.
- Above all, never let anything allocate a task id from that copy's `counters.json`. The real allocator in the main copy has already moved past it, so an id taken from the snapshot would collide with a task that already exists.
- Talk to AgentHQ over its HTTP API (`http://127.0.0.1:8778`) as normal. The server is backed by the main copy, so the API is always right regardless of which folder you are standing in.

Shaper tasks should say in their own text that the code lives in `D:\UNITY\Laubrary Dev - Shaper`.

There is one other main-copy-hardcoded script to know about: `sync-laubrary-to-consumers.ps1` has `$Src = 'D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary'` written into it, so running it from the Shaper folder would publish the **main** copy's Laubrary to consumer projects, not Shaper's. Don't run it from there.

---

## 5. Merge cadence — the proposal

**Weekly, main → Shaper, run from the Shaper copy, on the same day each week.** Absorbing the main branch's drift a week at a time is nearly free; absorbing five weeks of it in one go at the end is the merge nightmare the design was worried about. Suggested slot: Monday, before starting that week's Shaper work, so any conflict is dealt with while nothing else is half-finished.

```powershell
cd "D:\UNITY\Laubrary Dev - Shaper"
git fetch
git merge feat/lathe          # main → Shaper. Never the other direction until Shaper ships.
```

Rules that go with the cadence:

- **Merge in one direction only** until Shaper is finished. Shaper → main happens once, at the end, deliberately.
- **Do the merge with both editors closed, or at least with the Shaper editor closed.** A merge that rewrites hundreds of `.cs`/`.asset` files under a live editor triggers a mass reimport mid-write and is the kind of thing that has already cost this project a data-loss incident.
- **If a week is skipped, catch up before writing new code**, not after.
- Shaper's own commits are **additive — new files in new assemblies**. Nothing on this branch should be editing Pyre's files. If a Shaper change genuinely needs an edit to an existing shared file, make that edit on the **main** branch and let it arrive through the weekly merge; that keeps the shared files single-authored and the merges clean.

### Where the two branches can actually collide

The design names four shared surfaces. These are the real files:

| Surface | Path (in either copy) | Already dirty on main today? |
|---|---|---|
| Shared UI toolkit | `Assets\Packages\Laubrary\Zui\` | no |
| Package version | `Assets\Packages\Laubrary\package.json` | no |
| Changelog | `Assets\Packages\Laubrary\CHANGELOG.md` | **yes** |
| Menu registration | `[MenuItem("Laubrary/…")]` scattered across ~30 files under `Assets\Packages\Laubrary\Editor\` | **yes**, `Editor\Zoetrope\ZoetropeWindows.cs` |

So two of the four are under active edit on main right now — conflicts there are a near-certainty rather than a hypothetical, and they are the reason the cadence is weekly rather than "whenever".

**Conflict conventions, agreed up front:**

- `CHANGELOG.md` — Shaper does **not** write a changelog entry per commit. It writes **one** entry when it ships. Until then, take main's side of any conflict here (`git checkout --theirs` during a main→Shaper merge) without thinking about it.
- `package.json` version — same: Shaper never bumps it. Main owns the version number; take main's.
- ZUI — if Shaper needs something ZUI doesn't have, the project rule already says expand ZUI rather than hand-roll. Make that expansion **on the main branch** and pull it in with the weekly merge, so ZUI stays single-authored and never conflicts.
- Menu registration — Shaper's menu items live in Shaper's own new editor files, so they collide with nothing. Do not add a Shaper entry to an existing menu file.

Follow those four and the weekly merge should be a fast-forward-ish no-op most weeks.

---

## 6. Starting state, and what it does *not* contain

`feat/shaper` starts at `272e6df7`, the last commit on `feat/lathe`. The main copy had **221 uncommitted paths** on top of that when this was set up — 43 modified `.cs` and 92 untracked files, mostly an in-flight Zoetrope/Combat2D/ZoeCharacter feature. None of it is in the Shaper copy, by design: a worktree checks out committed history, and committing someone else's work-in-progress to snapshot it would have been the wrong call.

That number drifts on its own (it was already 222 an hour later, from a new `.agenthq/planning/Shaper.json`) — treat it as a description of the situation, not a checksum to assert against.

That in-flight work is self-contained (no committed file at `272e6df7` references any of the new untracked types), so `272e6df7` should stand on its own. It arrives in the Shaper copy through the ordinary weekly merge as soon as it is committed on `feat/lathe`.

Two related notes:

- `origin/feat/lathe` is level with local `feat/lathe` (0 ahead, 0 behind), so the "this branch exists on no remote" alarm at the end of B10 is **already resolved** — the 33-day rebuild is backed up. What is *not* backed up is the 221 uncommitted paths, which exist on one disk only.
- `feat/shaper` has not been pushed. Push it at its first real commit so the same alarm doesn't reappear for Shaper: `git push -u origin feat/shaper`.

---

## 7. Merge-back checklist (for the end, not now)

1. Delete the worktree-local block at the top of the Shaper copy's `CLAUDE.md` and `AGENTS.md`, and restore their Coplay section to the main copy's paths.
2. Write the single `CHANGELOG.md` entry and bump `package.json` if the release warrants it.
3. Merge `feat/shaper` into the main branch, from the main copy.
4. `git worktree remove "D:\UNITY\Laubrary Dev - Shaper"`.
