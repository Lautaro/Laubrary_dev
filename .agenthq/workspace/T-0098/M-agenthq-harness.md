# AgentHQ as a harness for the Pyre re-architecture — findings (Investigator M)

Scope: answers owner's Q5 verbatim (task T-0098, Q2 body) — is AgentHQ a good system for a massive
multi-month project, how should it be structured, and is a "board overwatcher" worth building.
Research-only; no AgentHQ data, Unity/C# source, or tasks were modified. One live write permitted
(the agent-record POST) was made after this file.

## Bottom line

- AgentHQ today is a flat-file task tracker + a **stateless dispatch watcher**: it spawns a fresh,
  one-shot `claude -p`/`codex exec` subprocess per `requested` task, records role/model, and reaps
  the result. There is no persistent process, no session memory, and no cron/scheduling beyond a
  5-second poll loop (`server.py:187` `CHECK_INTERVAL`, `server.py:830` `watcher_loop`).
- Of the owner's three options, **a group of tasks under one tree node** is the best fit and is
  already the pattern this exact project used for a comparably large effort — `PyrePlus.KilnPorts`
  (17 tasks, phases + numbered ports, `tree.json`). A dedicated **board** buys nothing extra (a
  board is just a registry pointer to a project root, `CLAUDE.md:9`) and is wrong unit besides —
  Pyre isn't a separate codebase. **One task with an enormous to-do list is what T-0098 already
  organically became** (31 to-dos, 3 sessions, 11 agent-roster entries, $47.57, 86.5 min) and it is
  visibly straining: to-dos have no sub-discussion depth for a multi-week item, and everything
  bottlenecks through one task's read state.
- **The context-reload problem is real and measured, not anecdotal.** By this, round 3, a freshly
  dispatched session must read 2 reports (92 KB) + 11 evidence files (452 KB) = **~544 KB / ~140K
  tokens of prior-work-only reading** before it can safely add a fourth. The dispatch brief
  (`PROMPT_TEMPLATE`, `server.py:194`) auto-supplies almost none of this — it gives the task detail
  URL and generic protocol text, nothing project- or node-specific.
- The cheapest fixes need **no new AgentHQ code**: a standing per-node context doc, a curated
  reading list, and a compacted running-state file are all things the **planning tab already
  supports** (`store.py:1186-1207`) — it already exists and is already being used for exactly the
  right content (the two GUG reports are filed there) but is **never referenced by the dispatch
  brief**, so an agent only finds it if it thinks to call `GET /api/planning`.
- A persistent **board overwatcher is not worth building now**. AgentHQ's whole design (stdlib-only,
  polling, one-shot subprocess dispatch, `CLAUDE.md:117-124`) has no primitive for a long-lived
  agent process at all — every dispatched CLI process is explicitly told it is *not* one
  (`ONE_SHOT_WARNING`, `server.py:371`) — and the project's own standing rule against two agents
  touching one open Unity editor concurrently is a structural argument against ever letting an
  overwatcher run in parallel with a human's own Coplay session. Get the conventions in point 3
  running first; revisit an overwatcher only if reading overhead is still the bottleneck afterward.

---

## 1. What AgentHQ actually is and does today

**Source location.** `D:\CODEZ\AgentHQ\` — `server.py` (88 KB, HTTP server + dispatch/watcher),
`store.py` (86 KB, all file I/O), `web/` (vanilla JS/HTML/CSS frontend, no build step),
`README.md` (user-facing docs, API list), `CLAUDE.md` (engineering guide for agents working on
AgentHQ itself). Stdlib-only Python 3.12, no database (`CLAUDE.md:5-9`).

**Data model** (`CLAUDE.md:11-31`, verified against the live `.agenthq/` folder for this project):
- **Project/"board"** = one row in `D:\CODEZ\AgentHQ\registry.json` (`{id, name, root}`), holding
  no data of its own — everything lives under `<project_root>/.agenthq/`. Confirmed live: `GET
  /api/registry` lists 20 boards, one of which is `Laubrary_Dev` → `D:/UNITY/Laubrary Dev`.
- **Tree / nodes** (`tree.json`) = a flat `{id, name, parent, muted, supervised}` list — the
  group/subgroup structure a task's `node:` field points into. Confirmed live via `GET
  /api/tree?project=Laubrary_Dev`: 16 root nodes (BackSplash, Chunks, PyrePlus [`muted: true`],
  …) plus nested children, e.g. `PyrePlus.KilnPorts` and 5 children under
  `IncidentRecovery-2026-08-16`. A node buys: (a) task filtering/grouping in the UI, (b) one
  **planning doc** per node (see below), (c) an optional **supervised** auto-promotion behavior.
- **Task** = one Markdown file, `tasks/T-NNNN.md`: YAML-ish frontmatter (`id, title, node, status,
  priority, assignee, confirmed, agent_role/model/subagent_count, total_cost_usd,
  total_active_minutes, session_count, …`) + fixed body sections `## Description / Todos /
  Questions / Handover Log / Agents` (`CLAUDE.md:33-51`). Every section is append-only, parsed by
  regex in `store.py` (`_Q_HEAD_RE`, `_H_HEAD_RE`, `_A_HEAD_RE`, `_TD_HEAD_RE`) — hand-editing a
  file is fully supported as long as the heading shape matches exactly.
- **Planning documents** = freeform Markdown tabs, one JSON per node (or `_root` for the whole
  board): `.agenthq/planning/<node>.json`. Confirmed live: `planning/PyrePlus.json` is 93 KB and
  holds 3 tabs — `notes`, `GUG - Future of Pyre`, `Shapes / Fills / Borders` — the exact place
  this task's two big deliverables were filed (`GET/POST /api/planning`, `server.py:1200-1204,
  1505-1508`; `Project.load_planning/save_planning`, `store.py:1194-1207`).
- **To-do list** = per-task, numbered `T1..Tn` entries with their own nested message threads
  (`**Message** (...)`), a `checked`/`by:` claim-vs-verify distinction, and a `blocking` flag that
  drives an `unread`/"Message Pending" pill (`CLAUDE.md:33-51`, extensively commented). This is the
  in-task granularity mechanism the owner is implicitly asking whether to lean on harder (option c).
- **Questions / Handover Log / Agents** = the discussion thread, the append-only "what happened"
  record, and the append-only agent/subagent audit trail (role, model, `parent` index, optional
  `declined: true`) respectively.

**Dispatch: manual vs automatic, one-shot vs persistent.**
- Setting a task's `status: requested` is the "ready for an agent" signal. The **watcher**
  (`WATCHER` global + `watcher_loop`, `server.py:830`), once started via `POST /api/agent/start`,
  polls every `CHECK_INTERVAL` (5 s, `server.py:187`) via `scan_for_requested()` (`server.py:522`)
  and dispatches up to `MAX_CONCURRENT` (4, `server.py:188`) and each board's own
  `max_concurrent_agents` (this project: 6, `.agenthq/settings.json`). So dispatch is
  **automatic once the watcher is running**, or a user can force one task immediately via `POST
  /api/task/dispatch` → `force_dispatch()` (`server.py:917`), bypassing the queue.
- Every dispatch is a **brand-new, one-shot subprocess** — `claude -p "<prompt>"` or `codex exec -`
  (`_dispatch`, `server.py:598`). There is no persistent agent process anywhere in this codebase.
  The prompt itself (`ONE_SHOT_WARNING`, `server.py:371-385`) explicitly tells the dispatched agent
  it is not an interactive/persistent session and that anything backgrounded and not waited on is
  simply abandoned when the process exits — this is a deliberate, load-bearing design constraint,
  not an oversight.
- **What persists between sessions**: only what got written back through the API into the task
  file (frontmatter, to-dos, questions, handover text, agent roster) or into a planning doc /
  attachment. Nothing about a session's own working memory, plan, or reasoning survives except
  what it chose to write down. `in_progress` status itself is recovered by
  `reconcile_orphaned_agents()` at server startup, since the watcher's live-process bookkeeping
  (`WATCHER["slots"]`) is in-memory only (`CLAUDE.md:93-99`).
- **What a dispatched agent receives automatically** (`PROMPT_TEMPLATE`, `server.py:194-227`):
  project name/root/base URL, task id, its assigned role+model+one-line reasoning
  (`classify_task()`, `server.py:456-520`), the task-detail GET URL (description + prior Q&A +
  prior handovers), a generic to-do/handover/subagent-reporting protocol, and (only if
  `multi_agent` was flagged) subagent-decomposition instructions. It does **not** automatically
  receive: the node's planning doc, sibling tasks in the same node, the tree, or any node/board
  standing-context document — none of these are referenced anywhere in `PROMPT_TEMPLATE` or its
  sub-templates. Separately, because the subprocess's cwd is the project root, a real Claude Code
  session picks up this project's own `CLAUDE.md` automatically (harness behavior, not an AgentHQ
  feature) — that is a free win already happening, but AgentHQ has no equivalent for a
  narrower/task- or node-scoped document.

**Handovers / questions / roster mechanics**, all already conventions, no new build needed:
`GET /api/task?...&reader=agent` marks the user's unacknowledged messages seen; `POST
/api/task/handover` accepts `text/details/technicalDetails/proof/attachments/newStatus` and copies
named attachments into the task before recording it (`README.md:199-260`); the Agents section
records role/model **before** the subprocess starts, so a crash still leaves a decision trail
(`CLAUDE.md:~90`, confirmed live: T-0098's roster has 11 entries, one per session dispatch and
subagent, matching `agent_subagent_count: 8` in frontmatter for the 8 sub-agents across 3 sessions).

---

## 2. The three structuring options, judged

**(a) Its own board.** A board is nothing more than a `{id, name, root}` registry pointer to a
project root (`registry.json`, `CLAUDE.md:9`) — it exists to let AgentHQ track a *separate
codebase*. Pyre is not a separate codebase; it is one subsystem inside Laubrary Dev. Making it a
board would mean either (i) pointing a "board" at the same `D:\UNITY\Laubrary Dev` root a second
time under a different id — which buys literally nothing since data lives under the shared
`.agenthq/` of that one root and would just create ambiguity about which board's task list an
agent is meant to update — or (ii) actually forking the codebase into its own root, which the
owner's own point 4 (clone-and-mold approach) *does* eventually call for, but that is a Unity/git
decision, not an AgentHQ one; AgentHQ registering a second, real board pointed at that clone once
it exists is trivial and free (`POST /api/project/add`). **Verdict: not useful as a way to
structure *this* board's work; becomes automatically correct, for free, if/when the clone from
point 4 is created — register it as its own board at that point, nothing more.**

**(b) A group of tasks on the existing board, under a tree node.** This project has already run
this exact pattern at comparable scale: `PyrePlus.KilnPorts` — 3 phase tasks + 11 numbered
port tasks + 4 follow-on tasks (T-0042 through T-0064), all tagged `kiln-port`, muted once done
(confirmed live via `GET /api/tasks?project=Laubrary_Dev&node=PyrePlus&all=1`). What a node buys,
concretely: (i) one shared **planning doc** with multiple tabs — the natural home for a standing
design doc, decision log, and reading-list that every task under it can point to; (ii) list-view
filtering so the whole body of work is visible as a unit; (iii) an optional **supervised**
auto-promotion behavior (`tree.json` `supervised` flag, `Project.promote_next_in_group`,
`store.py:471`) — while the watcher runs, on each tick it checks a supervised node and, if nothing
in it is `requested`/`in_progress`, auto-promotes the single best `open` task (highest priority,
then oldest) to `requested` (`README.md:185-191`). This is deliberately **not a dependency
graph** — no fixed build order, no blocked-on-X field exists anywhere in the codebase (confirmed:
no `depends`/`blocked_by` field in `store.py`/`server.py`). Ordering has to be expressed as
relative priority + which tasks even exist yet, same as `KilnPorts` did with its Phase-N naming
and by filing later ports as separate tasks only once ready. **Review**: the owner reviews at the
task level (confirm/reject each), same granularity regardless of grouping. **Degradation**: a
half-done task under a node degrades gracefully — it just sits `open`/`in_progress` while siblings
proceed; nothing else in the group is blocked by it unless supervision + priority say so.
**Verdict: this is the right unit for the Pyre re-architecture.**

**(c) One task with an enormous to-do list.** This is what T-0098 has already become by necessity
(31 to-dos as of this write, 3 sessions, $47.57, 86.5 active minutes) because the owner kept adding
follow-up questions to the same thread rather than opening new tasks. It works for a *discussion*
that stays in one place, but it strains at real "many independent workstreams" scale: (i) all
to-dos share one flat numbering with no sub-grouping — task list UI has no way to see "the shape
system" vs "the fill system" as separate swimlanes; (ii) the Questions section is genuinely one
thread per question, but a to-do's own nested message thread has no further nesting — a
multi-week workstream under one to-do has nowhere to accumulate its own handover-style history;
(iii) every session dispatched against this one task inherits the *entire* growing evidence trail
regardless of which sub-part it's actually assigned to, which is exactly the reload cost measured
in point 3. **Verdict: fine for the current "figure out the plan" discussion phase (which is
what T-0098 actually is), wrong as the vehicle for the *build* phase once a plan is approved** —
at that point each of the plan's "7 slices" (per `PYRE_SHAPE_FILL_BORDER.md`'s route) should
become its own task under a `Pyre` (or renamed) node, the same way KilnPorts split phases into
numbered tasks once phases were known.

**Recommendation (not purely one option): hybrid, matching KilnPorts' own precedent.** Keep design
discussion (what T-0098 is doing right now) as one task until the plan is settled — do not
prematurely fragment a conversation that owner and agent are still having. Once a plan is
approved, close/confirm T-0098 and open a new node (or reuse/rename `PyrePlus`, unmuting it) whose
planning tab holds the design doc as the single source of truth, with the build broken into one
task per shippable slice, each task's Description linking back to the specific section(s) of the
design doc it implements — do not restate the design in every task.

---

## 3. The context-reload problem, measured

**What a fresh dispatch gets automatically** (re-stated precisely from point 1): task id, role/
model, the task-detail GET URL, and generic protocol boilerplate. **Nothing project- or
node-specific is pre-loaded into the prompt** — no planning-doc contents, no evidence-file list,
no "read these N files first." An agent only discovers the planning doc, the two big reports, or
the `workspace/T-0098/` evidence files because a *previous* agent's handover text mentioned their
paths in `technicalDetails` (which it did, diligently, both times) — that is a **convention
carried by the agent's own handover discipline, not a structural feature of AgentHQ.**

**Measured cost, this task, as of round 3 (all figures read from `tasks/T-0098.md` frontmatter and
`.agenthq/workspace/T-0098/` directory listing):**

| Session | Dispatched | Cost | Active min | Subagents | Deliverable |
|---|---|---|---|---|---|
| 1 | 2026-08-30T07:36:17Z | (rolled into total) | (rolled into total) | 4 (A,B,C,Verifier-D) | `PYRE_GUG.md` (41 KB) |
| 2 | 2026-08-30T08:32:45Z | (rolled into total) | (rolled into total) | 3 (E,F,Verifier-G) | `PYRE_SHAPE_FILL_BORDER.md` (51 KB) |
| 3 (in progress) | 2026-08-30T10:38:08Z | not yet finalized | not yet finalized | 4 so far (H,I,J,K) + this one (M) | this file + round-3 synthesis |

Frontmatter total through end of session 2: **`total_cost_usd: 47.5655`, `total_active_minutes:
86.5`, `session_count: 2`** (session 3 not yet closed out, hence not yet reflected). 11 Agents-
roster entries total. Evidence corpus on disk right now: **11 files, 463,217 bytes (~452 KB)** in
`.agenthq/workspace/T-0098/` (A through K) **plus** the two filed reports, 41,260 + 50,879 bytes
(~90 KB) — **≈544 KB (~140K tokens at ~4 bytes/token) that a hypothetically-perfect round-4 agent
would need to read in full to avoid contradicting or duplicating round 1–3 findings**, before
doing any new work. This is why round 2 and round 3 both opened with a verifier pass that found
new errors in the *prior* round's draft (5 errors in round 1, 12 in round 2) — the fact-checking
cost is compounding, not flat, because each round's verifier is checking against a larger prior
corpus while under the same pressure to move fast.

**What AgentHQ already supports that would cut this, unused:**
- **Planning doc as a standing-context injection point** — exists (`store.py:1186-1207`), already
  holds exactly the right content (both GUG reports are filed there as tabs), but `PROMPT_TEMPLATE`
  never tells a dispatched agent to fetch `GET /api/planning?project=...&node=...` before starting.
  This is the single cheapest fix available: **either** (a pure convention, zero AgentHQ code) put
  "read the node's planning doc first" into the task's own Description on creation, **or** (a
  small AgentHQ change) have `_dispatch()` fetch the node's planning doc and inline a summary/link
  into the prompt automatically for every task on a node that has one.
- **Attachments** — exist per-task, but the workspace evidence files (A–K) are deliberately *not*
  attachments; they live in `.agenthq/workspace/T-0098/`, a convention this task's own agents
  invented (not an AgentHQ concept — grep of `server.py`/`store.py` finds no `workspace` handling
  at all). That is fine as a convention but means AgentHQ's attachment list undercounts what a
  reader actually needs; the handover text's explicit file-path callouts are doing the linking
  work attachments would otherwise do.
- **A curated reading list on the task** — nothing built-in beyond "put it in the Description or
  a handover's `technicalDetails`," which is exactly what happened here and works, but is
  easy to skip under time pressure and isn't enforced by the prompt template.
- **A compacted running-state file the agent must update** — no such mechanism exists; the closest
  equivalent is the Handover Log itself (append-only, one entry per session), which already
  functions as a running state log but is prose, not a compact structured digest. A short
  "STATE.md" or a "State" planning tab that each session is required to overwrite (not append to)
  with a <1-page current-best-understanding digest would be new — but it's a **convention**
  (a to-do item saying "before you finish, update planning tab X"), not new AgentHQ code.
- **No project/node-level CLAUDE.md-equivalent inside AgentHQ's own model.** The nearest thing is
  the planning doc, which is freeform and per-node, so it can serve this role today by convention
  (name a tab "Standing Context" and keep it current), but nothing forces an agent to read it.

**Cheapest fix ranking (no persistent agent needed), roughly cost-ordered:**
1. **Convention, zero build**: every task filed under a "big project" node gets a Description line
   "Before starting, read the node's planning tab '<name>' and the files it links." Free, works
   today, only as reliable as the humans/agents who file tasks remembering to write it.
2. **Convention, zero build**: require a "State" tab (planning doc) that gets *overwritten* each
   session with a compact digest — cuts the ~544 KB down to whatever fits in one page, at the cost
   of losing the full audit trail (mitigate by keeping the full evidence files as an appendix link,
   not deleting them).
3. **Small AgentHQ feature**: have `_dispatch()` auto-fetch and inline the node's planning doc (or
   just its tab titles + a "fetch these first" note) into `PROMPT_TEMPLATE` when one exists for the
   task's node. Estimated size: small — `Project.load_planning` already exists; this is a few lines
   in `_dispatch()`'s prompt-building block (`server.py:641-660`) plus one new template slot.
4. **Small AgentHQ feature**: a per-node (not per-task) "pinned reading list" of file paths, shown
   in the UI and injected the same way. New data shape (`tree.json` node gets a `pinned: [...]`
   array) + the same prompt-injection change as #3. Slightly more work than #3 because it needs a
   UI affordance to edit the list, not just consume an existing planning doc.

None of 1–4 requires a persistent process; all are compatible with (and independent of) whatever
the owner decides about an overwatcher.

---

## 4. The board-overwatcher idea, costed

**Can a session be long-lived at all, in this stack?** No — by explicit design. Every dispatch is
a fresh CLI subprocess (`_dispatch`, `server.py:598`) and the prompt itself asserts this is not an
interactive/persistent session and warns against assuming any backgrounded work will be resumed
(`ONE_SHOT_WARNING`, `server.py:371-385`). AgentHQ's watcher (`watcher_loop`, `server.py:830`) is
itself long-lived, but it is a dumb 5-second poll-and-spawn loop with **zero context** — it holds
no memory of what any task is *about*, only frontmatter status. There is no cron, no scheduling
primitive, and no notion of "an agent that keeps thinking between ticks" anywhere in this codebase.

**What building a real overwatcher would require, concretely:**
- A genuinely long-running agent process (not a CLI one-shot) — outside AgentHQ's current process
  model entirely; this is a Claude-harness-level capability (a persistent session, or a
  `ScheduleWakeup`/loop-style re-invocation pattern), not something `server.py`/`store.py` provide
  or were designed to provide. AgentHQ would need a *new* dispatch mode alongside `_dispatch()`
  that either keeps a subprocess alive and re-prompts it, or repeatedly re-invokes a session with
  compacted prior state — real, non-trivial design work, not a small feature.
  Rough size: **large** — new process-lifecycle code, new failure/restart handling for a
  process that's expected to run for days, and a way for a human to still intervene without racing
  it (the API already assumes any writer could be the user OR an agent at any moment; an
  overwatcher that "coordinates all subagents" would need to become the *primary* writer for long
  stretches, which changes today's assumption that the user is always free to hand-edit a task).
- Context management for a run spanning days/weeks: **compaction is not solved by making the
  session longer** — the same 544 KB (and growing) evidence corpus is still too large to keep
  verbatim in one context window past a few more rounds regardless of whether the reading agent is
  one continuous process or a fresh dispatch; a persistent agent still needs the point-3 fixes
  (a compact running-state digest) to avoid drowning in its own accumulated history. It doesn't
  remove the need for the cheap fixes — it just changes who benefits from them.
- Coordination of subagents "until switched off": AgentHQ has no notion of an agent that spawns
  and supervises *other AgentHQ dispatches* — today's subagents are Claude Code's own Agent tool,
  scoped to one dispatched session's lifetime, reported back via `POST /api/task/agent`
  (`SUBAGENT_REPORTING`, `server.py:~236`). An overwatcher "coordinating all subagents" across
  the whole board would need its own dispatch authority — effectively reimplementing (or being
  granted) what `_dispatch()`/`watcher_loop()` already do, which is a second, competing scheduler
  unless it *is* given control of the existing one.

**Failure modes, weighed against the codebase's own standing rules:**
- **Context drift / stale mental model**: exactly the risk the owner is trying to avoid on the
  *other* side of the coin — a long-lived agent's understanding of "what Pyre is" would go stale
  the moment code changes under it from any other source (the owner directly, a differently-
  dispatched task, a hand-edit), with no mechanism in AgentHQ to notify it of a change outside its
  own writes.
- **Single point of failure**: one process holding the only up-to-date plan for a multi-month
  project is a strictly worse failure mode than today's file-based model, where `CLAUDE.md:15`
  explicitly celebrates that *any* text editor can read/write task state and the app "picks the
  change up on its next poll" — a design principle a stateful overwatcher process directly cuts
  against.
- **Cost of a large context kept warm**: T-0098 alone has already spent $47.57 across 3 dispatched
  sessions covering roughly 6 hours of wall time; a process kept alive continuously for a
  multi-month project, even idling between real work, has a materially different (and much less
  predictable) cost shape than today's pay-per-dispatch model.
- **The Unity single-editor rule directly collides with "coordinates all subagents."** Per
  project memory `unity-scheduler-tick-wedge.md`: two agents touching one open Unity editor
  concurrently has caused a real 20+ minute editor wedge once and a near-miss a second time
  (T-0069, two AgentHQ-dispatched sessions editing `SpriteFxStackWindow.cs` at once, "neither
  could tell" it wasn't alone). An overwatcher whose entire pitch is coordinating multiple
  subagents working the same Pyre codebase simultaneously would need to *itself* enforce serial
  access to whatever open Unity editor is involved — today nothing in AgentHQ knows an editor is
  even open, let alone arbitrates it. This isn't a detail to patch later; it's the single largest
  reason parallel dispatch against one Unity project is dangerous, and an overwatcher is a machine
  specifically for causing more of it, not less, unless that arbitration is built first.

**Verdict.** Not worth building now. The stated problem — "agents have to pick up again and read
up on context" — is real and measured (point 3), but it is a **reading-cost problem**, and every
cheap fix in point 3 addresses it directly without any of the above risk or cost. Revisit an
overwatcher only if, after adopting the point-3 conventions, sessions are still burning a large
fraction of their budget on re-reading rather than working — that would be evidence the problem is
throughput, not context-loading, and only then does a different architecture start to pay for
itself.

---

## 5. Concrete recommendation

**Board/node/task layout.**
- Keep `Laubrary_Dev` as the one board — do not create a separate board (point 2a).
- Finish the current design-discussion phase as the single task it already is (T-0098) — do not
  fragment an ongoing conversation. Confirm/close it once the owner is satisfied with the plan.
- On approval, **unmute the `PyrePlus` node** (or rename it — Pyre's own naming conventions
  already forbid "PyrePlus" in new material, see this repo's `CLAUDE.md` "Pyre" section) and file
  one task per shippable slice from the approved route (`PYRE_SHAPE_FILL_BORDER.md` section 12's
  7-slice plan is already close to task-shaped), the same way `PyrePlus.KilnPorts` split phases
  into numbered port tasks. Use priority, not a dependency field, to express "this needs that
  first" (there is no dependency graph — point 2).
- If/when the owner's point-4 clone-and-mold approach happens (new app built alongside old Pyre as
  a mold), register that clone as its **own new board** at that time — free, correct, and exactly
  what a board is for (point 2a).

**Where documents live.**
- The `PyrePlus` planning doc keeps being the single source of truth for the design — it already
  holds both GUG reports as tabs. Add a **"State" tab**, overwritten (not appended) each session,
  holding a <1-page current-best-understanding digest — this is the compaction point-3 identifies
  as most valuable and cheapest.
- Keep the full evidence trail (`workspace/T-0098/A..K` and future letters) as the append-only
  audit/citation layer underneath the State tab, not as required reading for every new session.
- Every new task's Description should explicitly say "read the `PyrePlus` planning tab 'State'
  first, and 'GUG - Future of Pyre' / 'Shapes / Fills / Borders' for full reasoning if needed" —
  this is a one-line convention, not a feature.

**What every task's brief should point at.** Per point 3's ranking: at minimum, the task
Description names the planning tab(s) to read first (fix #1, free). If AgentHQ gets even one
small change from this list, make it fix #3 (auto-inline the node's planning doc into
`PROMPT_TEMPLATE` when one exists) — it converts a convention that depends on the task-filer
remembering into something structural, for a small, scoped change.

**What an agent must write back.** Already well-covered by existing conventions (handover
`technicalDetails` with file:line citations, the Agents roster, the to-do list as a plan) — the
one addition: whichever agent's turn ends a work session should also refresh the State planning
tab, not just the Handover Log, so the *next* dispatch has a single short document to start from
instead of needing to reconstruct the digest from prose handovers itself.

**Small AgentHQ features worth building, ranked:**
1. **Auto-inline node planning doc into dispatch prompt** (point 3, fix #3). Small — a few lines
   in `_dispatch()`'s prompt assembly (`server.py:641-660`) plus a new `PROMPT_TEMPLATE` slot;
   `Project.load_planning` already does the read.
2. **Per-node pinned reading list** (point 3, fix #4). Small-medium — new `tree.json` field, one
   new API route to edit it, and the same prompt-injection hook as #1. Skippable if #1 alone
   (pointing every task at the planning doc, which can itself just list file paths) covers it.
3. **A "supervised → include a standing note" option**: since supervised nodes already
   auto-promote the next task (`promote_next_in_group`, `store.py:471`), extend it so a supervised
   node can carry one paragraph of standing guidance surfaced identically at every auto-promotion —
   effectively #1 but scoped to the group-oversight mechanism specifically, for boards that use
   that feature. Medium — touches `scan_group_oversight`/`promote_next_in_group` and needs a UI
   field.
4. **Anything overwatcher-shaped**: explicitly not recommended now (point 4). If revisited later,
   it is a large, multi-week build (new dispatch mode, new coordination/locking model, new failure
   handling) and should not be attempted until 1–3 have been tried and found insufficient.

**What NOT to build**: a dependency graph (the shallow "no fixed order" re-derivation the watcher
already does, per `README.md:185-191`, is a deliberate design choice, not a gap); a separate board
for Pyre before a real separate codebase exists; any change to the one-shot dispatch model itself.
