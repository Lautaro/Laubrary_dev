# T-0246 — a task nobody is working on must read "Session lost", not pulse "In Progress"

Trigger: Laubrary_Dev T-0245 sat pulsing "in progress" after the 2026-09-07T07:15Z AgentHQ server restart with an empty slot list and no note. T-0239 had the opposite failure on the same restart: it was marked lost while its PM worker was still running.

## The rule

For every task whose status is `in_progress`, evaluated in this order:

1. **A live classic WATCHER slot exists** -> not touched. The reaper in `watcher_loop` owns it, exactly as before.
2. **The board's PM session is alive** (`PM_SESSIONS[pid]` present and its process still running, or still starting -- the same test `/api/agent/status` publishes as `pmActive[pid].alive`) -> not touched, however quiet the task is. A PM claim (`POST /api/task/claim` + `POST /api/task/agent`) has no slot to check; the PM reports back when it is done.
3. **Otherwise, no sign of life for 10 minutes** (`LOST_AFTER_S`) -> marked lost. "Sign of life" is the most recent of: the in-memory agent-contact record (any agent-authored API call -- `/api/task/claim` now counts as one), the task's own `updated` stamp (any write by anyone, deliberately lenient), and `work_started`.

"Marked lost" reuses the app's existing orphan path (`store.Project.mark_agent_stopped(task, None, reason=...)`): status `open`, tags `lost` + `unfinished`, a new frontmatter key `lost_reason` with the human-readable reason, and a system note in the Agents roster that carries the reason **and quotes the agent's last own note** ("Last word from the agent: ...") so the card says how far it got. Re-claiming or re-dispatching the task (`start_agent_run`) clears `lost`, `unfinished` and `lost_reason` again.

Two moments apply the rule:

- **At startup** (`reconcile_orphaned_agents`) -- now runs AFTER `reconcile_pm_sessions()` re-adopts still-running PM sessions, so a PM-owned board's claims are skipped (fixes the T-0239 mis-marking). Tasks that are swept get the reason "the AgentHQ server restarted while this task was in progress, and no PM session for this board survived the restart to keep working it".
- **Every 30 s while the watcher runs** (`sweep_lost_tasks`, step 1c of `watcher_loop`) -- the new runtime sweep that catches a PM claim whose PM session went away untracked and a slot that vanished without the reaper demoting the task (the T-0245 case). The reason names the last API call and how long ago ("...the agent last called the AgentHQ API 20 min ago (ticked a todo)") or, with no contact record, how long nothing has touched it.

Card/UI:

- A distinct red **Session lost** pill (replacing the generic Unfinished pill when both tags are present) whose tooltip is the `lost_reason`.
- The In Progress pill only pulses when an agent is actually live (`isAgentLive`: a real slot, or agent contact in the last 5 minutes). On a PM-owned board with nothing heard from the task it now shows the static stale style with the tooltip "Claimed by this board's PM session, but nothing has touched it for a while -- it stays In Progress while that PM session is alive; if the session ends without reporting, it is marked Session lost". Previously `pmOwnsBoard` alone forced the pulse.

## Files and lines touched (all in D:\CODEZ\AgentHQ, uncommitted)

- `D:\CODEZ\AgentHQ\server.py`
  - 1390, 1398-1405, 1420-1428: `reconcile_orphaned_agents` skips boards with a live PM session and passes a restart reason.
  - 1434-1520: new `_alive_pm_boards_locked()`, `LOST_AFTER_S` / `LOST_SWEEP_INTERVAL_S`, and `sweep_lost_tasks(now=None, force=False)`.
  - 2348-2354: `watcher_loop` step 1c calls `sweep_lost_tasks()` (throttled, never raises).
  - 2963: `/api/task/claim` counted as an agent contact.
  - 3337-3348: `main()` re-adopts PM sessions before the orphan sweep.
- `D:\CODEZ\AgentHQ\store.py`
  - 906, 909: `start_agent_run` also clears the `lost` tag and `lost_reason`.
  - 956: `mark_agent_stopped` gains `reason: str | None = None`.
  - 1090-1102: reads the agent's last non-system note before mutating.
  - 1110-1116: orphan path adds tag `lost` and sets `lost_reason`.
  - 1167-1176: the "Agent session was lost" note now states the reason and the last agent note.
- `D:\CODEZ\AgentHQ\web\app.js`
  - 83-88: `tagBadgesHtml` renders the Session lost pill with `lost_reason` as tooltip.
  - 2548 (list row) and 2990 (detail header): pill pulses on `live` only; new PM-board tooltip.

Diff hygiene: the repo held another session's uncommitted edits (41 hunks in server.py, 13 in store.py, 21 in app.js). Every edit was made with exact-string replacement; a `git diff -U0` hunk index shows exactly 8 server.py, 7 store.py and 3 app.js hunks carrying T-0246 markers and no other hunk touched. No `git checkout`, `stash` or `commit` was run. The server was NOT restarted.

## Verification

- `ast.parse` on server.py and store.py, and `node --check web/app.js`: syntax clean.
- `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0246\test_lost_sweep.py` -- imports the edited modules without starting the server, repoints `store.REGISTRY` at a temp project, and drives `sweep_lost_tasks` / `reconcile_orphaned_agents` with fake PM/slot processes. 19/19 assertions pass, covering: quiet claim with no PM -> lost with reason, tags, system note quoting the last agent note; PM alive -> left alone even after an hour; PM dead -> swept; fresh contact -> kept; just-claimed -> kept (grace); live slot -> never touched; stale contact -> reason names the last call; throttle honoured; startup order (PM alive -> kept, PM absent -> lost with restart reason); re-claim clears the verdict; no-note wording; `lost_reason` survives the task-file round trip.

Run it with: `python "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0246\test_lost_sweep.py"`.

## Not verified

- The running server still executes the pre-edit code -- the Python changes take effect only after the owner restarts AgentHQ. The app.js side is served from disk and is live already, but the `lost` tag / `lost_reason` it renders will not appear on any task until the new server code produces them.
- Not exercised in a real browser: the Session lost pill and the PM-board stale tooltip were checked by reading the template only.
- The 10-minute grace is a judgement call: a PM subagent that goes silent for longer than that on a board whose PM session is NOT tracked by AgentHQ (a PM started by hand rather than via the board's PM button) will be marked lost. A tracked PM session protects its board indefinitely.

## Addendum (2026-09-07, after the 07:45Z mis-marking of T-0239 / T-0245 / T-0246)

**What was observed.** Right after the 07:48:13Z (09:48 local) restart, T-0239 and T-0245 were `open` with tags `lost,unfinished` and `lost_reason` "no live agent process or PM session was tracking this task", even though `pmActive.Laubrary_Dev.alive` was true; T-0246, already `done`, carried the same tags and reason.

**Root cause: the marking did not happen on the 07:48 restart at all.** `D:\CODEZ\AgentHQ\watcher-events.log` shows the three `ORPHANED Laubrary_Dev/T-0239 / T-0245 / T-0246` lines at **07:45:01Z**, immediately followed by `SERVER_START pid=159056` at 07:45:02Z, then further starts at 07:45:22Z, 07:46:55Z and 07:48:13Z. At 07:45:01Z the files were mid-edit: `store.py` already had the `lost` tag / `lost_reason` default (last saved 07:45:06Z, so the note still used the old "(most likely the AgentHQ server restart..." wording), while `server.py` had NOT yet received the PM-skip in `reconcile_orphaned_agents` or the `reconcile_pm_sessions()`-before-sweep reorder in `main()` (last saved 07:46:03Z). So the 07:45:02Z server ran the OLD startup sweep (no PM check, unconditional `mark_agent_stopped(tid, None)`) against the NEW store, producing exactly the observed mix: new `lost_reason` default text + old note wording + all three in_progress tasks marked (T-0246 was legitimately `in_progress` at 07:45 -- `work_started` 07:39:37Z -- and was handed over as `done` at 07:54Z afterwards, with the stale tags and reason left on it because nothing stripped them on the status change). The final 07:48:13Z start (complete code) logged no ORPHANED/LOST line for any real task. The three ordering/key/second-entry-point hypotheses were checked against the committed code and all hold: `reconcile_pm_sessions()` adopts synchronously (`tasklist` per PID) before the sweep; the board key is the registry id `Laubrary_Dev` in both `PM_SESSIONS` and `pm-sessions.json`; the only other `returncode=None` caller is `sweep_lost_tasks`, which also skips PM-alive boards, and `_reconcile_pm_orphaned_tasks` writes a different ("PM session ended") note and did not fire.

**What was actually wrong in the committed code, and is now fixed (pending restart):**

1. A `lost` verdict outlived the facts. Handing a lost task over as `done`, confirming it, or `/api/task/status done` left `lost`,`unfinished` and `lost_reason` in place; clearing the tags by hand via `/api/task/edit` left `lost_reason` behind. `store.py` gains `_settle_lost_verdict(fm)` (after `_tags_remove`, ~line 419) which `update_task` (line ~870) applies after EVERY mutate: status `done`/`cancelled` drops `lost` + `unfinished`; no `lost` tag drops `lost_reason`. Because it sits in the single write path it covers every route (handover, confirm, set_status, edit_task, mark_agent_stopped, ...). T-0246's own leftover `lost_reason` will be settled by the first write to it after the restart.
2. Hardening (not the observed cause): `_pid_alive` in `server.py` (~line 1963) returned False when `tasklist` hit its 5 s timeout, i.e. "unknown" read as "PM session dead" -- which would reopen every claim on the board (`_reconcile_pm_orphaned_tasks`) and let the sweep mark them lost. A `TimeoutExpired` now reads as alive; every other failure still reads as dead.

**Tests.** `test_lost_sweep.py` extended from 19 to 32 assertions, all passing: 13a-c reproduce the exact `main()` startup sequence (persisted `pm-sessions.json` with an alive PID, in_progress claims 40 min quiet with `pm` + `implementer` roster entries and notes, empty slot list) through `reconcile_pm_sessions()` -> `reconcile_orphaned_agents()` -> first `sweep_lost_tasks()`: nothing marked; 14a-b a `done` task with stale lost metadata is byte-identical after both sweeps and a direct `mark_agent_stopped(None)` adds no verdict/note; 15-18 handover-to-done, `edit_task(tags='')`, confirm and `set_status(done)` all drop `lost_reason` (and the tags on done), while an edit that keeps the `lost` tag keeps the reason; 19a-b the `tasklist` timeout -> alive, nonexistent PID -> dead.

**Diff hygiene.** `git diff HEAD -U0 -- server.py store.py` shows exactly three hunks, all T-0246-addendum: server.py @1963 (+6), store.py @419 (+21) and @870 (+1). Other paths in `git diff HEAD --stat` (`.agenthq/tasks/T-0126.md`, `_uiprobe/**`, `web/style.css`) belong to another session and were not touched. `app.js` needed no change. Nothing committed; the server was not restarted.
