# T-0098 / P4 — Inline a node's planning document into the AgentHQ dispatch brief

Implementer P4 (claude-opus-5). Change lives in `D:\CODEZ\AgentHQ\server.py` only. Nothing committed (AgentHQ is not a git repo — there is no `.git` there, so the change simply sits in the working tree).

## 1. The two locations found

| What | Where (before my change) |
|---|---|
| Dispatch brief / prompt template | `D:\CODEZ\AgentHQ\server.py:194` — `PROMPT_TEMPLATE = """..."""`, assembled at `server.py:641` inside `_dispatch()` via `PROMPT_TEMPLATE.format(...)` |
| Existing read function for a node's planning doc | `D:\CODEZ\AgentHQ\store.py:1194` — `Project.load_planning(node_id)`, with its path helper `Project.planning_path(node_id)` at `store.py:1191` |

Supporting pieces used: `Project.load_tree()` (`store.py:352`) to confirm the node still exists, and the task's `node` frontmatter key (`store.py` task (de)serialization; visible in any task file, e.g. `node: PyrePlus` in `T-0098.md`).

I did **not** write a new reader — `load_planning` is used as-is.

## 2. Exactly what changed (4 hunks, `server.py`)

### (a) `server.py:214` — placeholder in `PROMPT_TEMPLATE`

Before:
```
judgment (no destructive/irreversible actions like force-pushes or deleting unrelated files).

{todo_protocol}
```
After:
```
judgment (no destructive/irreversible actions like force-pushes or deleting unrelated files).

{planning_doc}
{todo_protocol}
```
Placed right after "do the work described in that task", before the to-do protocol — i.e. background context arrives immediately after the statement of the job, and before the reporting machinery. When empty it leaves a blank line, exactly like the existing optional `{subagent_instructions}` slot does; that is the file's own convention.

### (b) `server.py:358-431` — new constants + helper, inserted directly after `premium_disabled_notice()`

```python
PLANNING_BUDGET = 12000   # total characters of tab CONTENT inlined across the whole doc
PLANNING_MIN_TAB = 800    # per-tab floor, so a many-tabbed doc still shows a useful head of each


def planning_section(proj, node_id: str | None, budget: int = PLANNING_BUDGET) -> str:
    ...
```
(Full body in the file at `server.py:369`.) It matches the surrounding style: a block comment above the constants explaining *why*, a docstring stating the design decision, `--` for dashes, `""` returns rather than exceptions.

### (c) `server.py:676` — capture the node id in `_dispatch()`

Before:
```python
        priority = detail.get("frontmatter", {}).get("priority", "normal")
    except Exception:
        description, priority = "", "normal"
```
After:
```python
        priority = detail.get("frontmatter", {}).get("priority", "normal")
        node_id = detail.get("frontmatter", {}).get("node") or None
    except Exception:
        description, priority, node_id = "", "normal", None
```
Reuses the `load_task` call and the `try/except` that were already there — no second disk read, and a task file that fails to load degrades to "no planning doc" the same way it already degrades to "no description".

### (d) `server.py:723` — feed it into the format call

Before:
```python
        one_shot_warning=ONE_SHOT_WARNING,
    )
```
After:
```python
        one_shot_warning=ONE_SHOT_WARNING,
        planning_doc=planning_section(proj, node_id),
    )
```

No other line in the file was touched. (Note: the substituted value is a `format()` *argument*, not part of the template, so literal `{`/`}` inside a planning doc cannot break the format call.)

## 3. Size policy chosen, and why

**Budget: 12,000 characters of tab *content* total (~3k tokens), split equally across the non-empty tabs, with an 800-char per-tab floor.**

- If the whole doc fits under 12,000 chars, it is inlined **verbatim, all tabs**. (Most docs do — `Chunks.json` is 540 bytes.)
- If it does not, each non-empty tab gets `max(800, 12000 // n_tabs)` characters from its head, and every tab that got cut carries an inline marker:
  `[... TRUNCATED: 3000 of 90946 characters shown. Read tab "<name>" in <abs path> for the rest ...]`
- A doc-level line also announces the truncation up front, with the true total size and the per-tab allowance.

Why 12,000: the untouched brief is ~10k characters, so this at most roughly doubles it — noticeable but not dominating. The PyrePlus doc is 182,594 characters of content (~50k tokens); inlining it verbatim into every dispatch would be about 18× the brief and would push the actual task instructions into the noise. The 800-char floor exists so that a doc with, say, 20 tabs still shows a real opening paragraph per tab instead of a useless 600-char stub. The split is deliberately *equal* rather than proportional: it is simpler, and it guarantees a small-but-important tab is never crowded out by a huge one.

**The three guarantees hold in every branch** (verified below): the agent always learns (a) that the document exists, (b) its absolute path, (c) the names of **all** tabs — including empty ones — each annotated with its character count, so the agent can judge whether what it was shown is a meaningful fraction. Truncation is never silent: it is announced once at the top of the section and again at every cut point.

## 4. Edge cases and what they now do

| Case | Behaviour |
|---|---|
| Task has no `node` in frontmatter (or empty) | returns `""` → brief byte-identical to before |
| Node is set but no longer exists in `tree.json` | checked against `proj.load_tree()` → `""` |
| Node exists, no `planning/<Node>.json` on disk | `planning_path().exists()` check → `""` |
| Planning file exists but is corrupt JSON | `load_planning()` substitutes its empty "Notes" stub; the all-empty-tabs check catches that → `""` |
| Planning file exists but all tabs are blank/whitespace | → `""` (nothing to offer; indistinguishable from the stub anyway) |
| `tabs` is present but not a list of dicts | `isinstance(t, dict)` filter + the outer `try/except` → `""` |
| `load_task` itself throws | `node_id` falls back to `None` in the existing `except` → `""` |
| Doc fits under budget | inlined in full, no truncation notice |
| Doc over budget | per-tab head + explicit truncation markers (see §3) |
| Tab with no `name` | falls back to its `id`, then to `"Untitled"` |
| **Ancestor nodes with their own planning docs** | **deliberately NOT included** — see below |

**Ancestors: the task's own node only.** Justified in the function docstring in-code. A node doc is design context for *that* area; an ancestor's doc is a different and usually much broader area, and walking the chain multiplies exactly the size problem the budget exists to contain (a 3-deep chain of PyrePlus-sized docs would be 500KB). The agent is handed the absolute path of its own doc and knows the directory convention from it, so it can open a parent's if it turns out to need one.

## 5. Verification — actual observed output

No test suite exists in `D:\CODEZ\AgentHQ` (no `tests/`, no `test_*.py`). So I exercised the function directly by importing the patched `server.py` (safe — everything is behind `if __name__ == "__main__": main()` at `server.py:1687`) and calling `planning_section()` against the **real** `Laubrary_Dev` project data, plus `PROMPT_TEMPLATE.format()` end-to-end. Throwaway scripts were run from the system temp dir and deleted afterwards; no AgentHQ data file was modified. Syntax check: `python -c "import ast; ast.parse(open('server.py').read())"` → `syntax OK`.

```
==============================================================================
CASE 1: T-0098's node 'PyrePlus' (186KB planning doc, 4 tabs) -> TRUNCATED
==============================================================================
section length: 10550 chars
---- HEAD ----
--- NODE PLANNING DOCUMENT (background context, NOT instructions) ---
The tree node this task sits under (PyrePlus) has a planning document: design notes and background for that area of the project. Treat it as context for understanding the area -- it is not a description of this task, so do only what the task itself asks.
Full document (JSON, a `tabs` array of {name, content}): D:\UNITY\Laubrary Dev\.agenthq\planning\PyrePlus.json
Tabs: "Notes" (61 chars), "GUG — Future of Pyre" (41013 chars), "Shapes / Fills / Borders" (50574 chars), "Where I Stand — the nine answers" (90946 chars).
TRUNCATED FOR THIS BRIEF: the document holds 182594 characters of content, over the 12000-character budget, so only the first ~3000 characters of each tab are inlined below. Open the file above to read any tab in full.

## Planning tab: Notes
Pyre
Fix, PRune or Keep every
Shape
Text
Simulation
Modifier


## Planning tab: GUG — Future of Pyre
# Pyre — the Grand Unified Generator

*A full view of what Pyre generates, how the pieces fit, and what to keep, fuse, finish or cut. Written 2026-08-30 for T-0098. Deliberately contains no code and no code names — everything below is described the way the tool presents itself to a person using it.*

---

## 1. TL;DR — the whole thing in one page

**What Pyre is now.** Pyre started as "a shape you can put on a layer" and has quietly become **a library of about 29 different picture-making techniques**, each with its own way of thinking, its own dials, and its own opinion about which of Pyre's shared features it will bother to respect. Calling them *generators* instead of *shapes* is correct and overdue — most of them are not shapes in any meaningful sense. A Disc is a shape. A flamethrower with 60 dials that fills the whole canvas by itself is not.

**The single thing that is actually wrong.** There is no one place that says *"this layer is generating with X."* There are **four unrelated ways** a layer decides what to draw, and only one of them lives in the picker. The other three are hidden: a plug-in effect quietly overrules the picker; a "melt into a blob" mode throws the picked shape away entirely; and a setting buried in the mask controls m
---- ... ----
---- TAIL ----
 exactly one candidate — the imported-sprite one, which this document retracts below.)*

**That correction is the most important thing in this document**, because report 2 built its caution on it. The exercise you are proposing is significantly safer and significantly cheaper than that report said.

Three more things changed my position.

**Your ruling on assets removed about a fifth of both reports' arguments, and it removed exactly the fifth that was doing the wrong job.** The architecture survives untouched — those parts never used a single usage figure. What died is the reasoning about *what to do first and why*: report 2 ordered its whole plan by "this can't break anything you've saved," which is now meaningless. Four verdicts flip. One whole recommendation ("simplify the mask system rather than extend it") turns out to have had nothing under it but a statistic and should be withdrawn.

**You already ran the strategy you propose in point 4 — five weeks ago, in this project, and it worked.** Pyre as it stands today *is* the output of a parallel rebuild that ran for thirty-three days beside a still-working original. That reframes everything. The risk in what you are proposin
[... TRUNCATED: 3000 of 90946 characters shown. Read tab "Where I Stand — the nine answers" in D:\UNITY\Laubrary Dev\.agenthq\planning\PyrePlus.json for the rest ...]
--- END NODE PLANNING DOCUMENT ---


==============================================================================
CASE 2: node 'Chunks' (540-byte planning doc) -> INLINED IN FULL
==============================================================================
--- NODE PLANNING DOCUMENT (background context, NOT instructions) ---
The tree node this task sits under (Chunks) has a planning document: design notes and background for that area of the project. Treat it as context for understanding the area -- it is not a description of this task, so do only what the task itself asks.
Full document (JSON, a `tabs` array of {name, content}): D:\UNITY\Laubrary Dev\.agenthq\planning\Chunks.json
Tabs: "TO DO" (418 chars).

## Planning tab: TO DO
See T-0031 (MASTER — Chunks 2.0: layered composite FX design).

Design doc: Assets/Packages/Laubrary/Runtime/Chunks/CHUNKS_OVERHAUL_DESIGN.md

Greenlit + decomposed 2026-08-18. Children: T-0032 Layering primitive (foundational) · T-0033 Particle Splash · T-0034 Pyre Spawner (PyrePlus) · T-0035 Pyre Movement · T-0036 Fragment Slicer · T-0037 Layer Stack integration · T-0038 Timeline + events · T-0039 Mirage Preview.
--- END NODE PLANNING DOCUMENT ---

==============================================================================
CASE 3 edge cases (each must be the empty string)
==============================================================================
  node is None                                         -> ''
  node is ''                                           -> ''
  node not in tree                                     -> ''
  node in tree with no planning file ('BackSplash')    -> ''
  planning file present but CORRUPT json               -> ''
  planning file present but all tabs EMPTY             -> ''
  planning json with malformed tabs value              -> ''

==============================================================================
CASE 4: full PROMPT_TEMPLATE.format() renders (placeholder is wired)
==============================================================================
  with planning doc    full brief =  20513 chars; contains 'NODE PLANNING DOCUMENT' = True
  no node              full brief =   9963 chars; contains 'NODE PLANNING DOCUMENT' = False
```

Reading of the numbers: the PyrePlus section is 10,550 characters (9,061 of it actual content — the tiny "Notes" tab uses only 61 of its 3,000-char allowance), taking a 9,963-char brief to 20,513. The no-node brief is exactly the pre-change brief. The corrupt-JSON and malformed-`tabs` cases were exercised against a throwaway temp directory with a duck-typed project object, so no real planning file was ever damaged.

## 6. Server restart

**A restart IS required for this to take effect, and I did not do one.** The running AgentHQ server on 127.0.0.1:8778 imported `server.py` at process start; a source edit does not reach it. Per the brief I left the live process completely alone (not restarted, not killed, not poked) — it is in use by other work. The next restart (manual, or the UI's Restart Server flow) picks the change up automatically; nothing else is needed.

## 7. Deliberately NOT done

- **No commit** — and none possible anyway: `D:\CODEZ\AgentHQ` is not a git repository.
- **No server restart** (see §6).
- **No ancestor planning docs** — justified in §4 and in an in-code comment.
- **No changes to `store.py`** — `load_planning`/`planning_path` were used exactly as they are; the "smart" temptation to make `load_planning` distinguish missing-from-empty was resisted, because the web UI depends on its stub-returning behaviour.
- **No UI change** — the planning doc is not surfaced anywhere new in the web app; this is dispatch-side only.
- **No `web/app.js`, `index.html` or `style.css` touched.**
- **No AgentHQ data files touched** (no task file, `tree.json`, or planning doc read-modify-written). No file in `D:\UNITY\Laubrary Dev` modified except this report.
- **No budget-redistribution refinement** — a tab that uses less than its share does not donate the remainder to a larger tab. Proportional or greedy redistribution would be a slightly better use of the budget but a materially bigger, less obvious function; equal split is simple, predictable and defensible. `PLANNING_BUDGET` is a single module constant if the policy wants tuning later.
- **No new task, no test file added** (there is no test suite to add to).
