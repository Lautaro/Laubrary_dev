"""Unit-level check of the T-0246 lost-session rule, run against a throwaway project.

Imports server.py / store.py from D:\\CODEZ\\AgentHQ WITHOUT starting the server, repoints
the registry at a temp folder, and exercises reconcile_orphaned_agents() and
sweep_lost_tasks() through every branch of the rule. Exit code 0 = every assertion held.
"""
import json
import re
import sys
import tempfile
import time
from pathlib import Path

sys.path.insert(0, r"D:\CODEZ\AgentHQ")
import store          # noqa: E402
import server         # noqa: E402
from store import Project  # noqa: E402

tmp = Path(tempfile.mkdtemp(prefix="agenthq-t0246-"))
root = tmp / "proj"
root.mkdir()
store.REGISTRY = tmp / "registry.json"
store.save_registry([{"id": "TestProj", "name": "Test", "root": str(root)}])
proj = Project("TestProj")
proj.ensure_scaffold()


def iso_ago(seconds: int) -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(time.time() - seconds))


def backdate(tid: str, seconds: int) -> None:
    """Rewrite `updated` and `work_started` in the task file so it looks quiet for N s."""
    p = proj.task_path(tid)
    text = p.read_text(encoding="utf-8")
    stamp = iso_ago(seconds)
    text = re.sub(r"^updated: .*$", f"updated: {stamp}", text, flags=re.M)
    text = re.sub(r"^work_started: .*$", f"work_started: {stamp}", text, flags=re.M)
    p.write_text(text, encoding="utf-8")


class FakeProc:
    def __init__(self, alive: bool):
        self.alive = alive
        self.pid = 4242

    def poll(self):
        return None if self.alive else 0


def new_claimed_task(title: str, note: str | None = "about to start -- doing X") -> str:
    tid = proj.create_task(None, title, "desc", "normal")
    proj.start_agent_run(tid)                      # what POST /api/task/claim does
    if note:
        proj.add_agent_record(tid, "implementer", "claude", note)   # POST /api/task/agent
    return tid


def fm(tid: str) -> dict:
    return proj.load_task(tid)["frontmatter"]


def tags(tid: str) -> list[str]:
    return [t for t in fm(tid).get("tags", "").split(",") if t]


failures = []


def check(cond: bool, label: str) -> None:
    print(("PASS " if cond else "FAIL ") + label)
    if not cond:
        failures.append(label)


# ---- 1. sweep: quiet PM claim, PM session NOT alive -> lost, with reason + last note
server.PM_SESSIONS.clear()
server.WATCHER["slots"].clear()
t1 = new_claimed_task("quiet claim, no PM")
backdate(t1, 15 * 60)
lost = server.sweep_lost_tasks(force=True)
check(f"TestProj:{t1}" in lost, "1a quiet claim with no PM session is swept")
check(fm(t1)["status"] == "open", "1b ...status is open")
check("lost" in tags(t1) and "unfinished" in tags(t1), "1c ...tags lost + unfinished")
check("nothing has touched it for 15 min" in fm(t1).get("lost_reason", ""), "1d ...lost_reason names the quiet time")
sysnotes = [a for a in proj.load_task(t1)["agents"] if a.get("role") == "system"]
check(sysnotes and "Reason:" in sysnotes[-1]["note"], "1e ...system note carries the reason")
check(sysnotes and "Last word from the agent: about to start -- doing X" in sysnotes[-1]["note"],
      "1f ...system note quotes the agent's last note")

# ---- 2. sweep: PM claim, PM session ALIVE -> left alone, however quiet
server.PM_SESSIONS["TestProj"] = {"proc": FakeProc(True), "started": iso_ago(3600)}
t2 = new_claimed_task("quiet claim, PM alive")
backdate(t2, 60 * 60)
lost = server.sweep_lost_tasks(force=True)
check(f"TestProj:{t2}" not in lost and fm(t2)["status"] == "in_progress",
      "2 quiet claim on a board with a live PM session is left alone")

# ---- 3. sweep: PM session dead (poll() != None) -> that counts as not alive -> lost
server.PM_SESSIONS["TestProj"] = {"proc": FakeProc(False), "started": iso_ago(3600)}
lost = server.sweep_lost_tasks(force=True)
check(f"TestProj:{t2}" in lost and "lost" in tags(t2), "3 same claim once the PM session is dead is swept")

# ---- 4. sweep: recent sign of life (fresh contact) -> left alone even with no PM
server.PM_SESSIONS.clear()
t4 = new_claimed_task("recent contact, no PM")
backdate(t4, 30 * 60)
server.note_agent_contact("TestProj", t4, "ticked a todo")
lost = server.sweep_lost_tasks(force=True)
check(f"TestProj:{t4}" not in lost and fm(t4)["status"] == "in_progress",
      "4 recent API contact keeps a task in progress")

# ---- 5. sweep: recently claimed (updated just now) -> left alone (grace window)
t5 = new_claimed_task("just claimed, no PM")
lost = server.sweep_lost_tasks(force=True)
check(f"TestProj:{t5}" not in lost and fm(t5)["status"] == "in_progress",
      "5 a claim made moments ago is inside the grace window")

# ---- 6. sweep: live classic slot -> never touched
t6 = new_claimed_task("classic slot live")
backdate(t6, 60 * 60)
server.WATCHER["slots"][server._slot_key("TestProj", t6)] = {
    "proc": FakeProc(True), "task": {"project": "TestProj", "id": t6, "title": "x"},
    "started": iso_ago(3600), "log": None}
lost = server.sweep_lost_tasks(force=True)
check(f"TestProj:{t6}" not in lost and fm(t6)["status"] == "in_progress",
      "6 a task with a live WATCHER slot is never swept")
server.WATCHER["slots"].clear()

# ---- 7. sweep: stale contact (older than grace) with no PM -> lost, reason names the call
t7 = new_claimed_task("stale contact, no PM")
backdate(t7, 40 * 60)
server.AGENT_CONTACT[server._slot_key("TestProj", t7)] = {"time": iso_ago(20 * 60), "what": "ticked a todo"}
lost = server.sweep_lost_tasks(force=True)
check(f"TestProj:{t7}" in lost and "last called the AgentHQ API 20 min ago (ticked a todo)" in fm(t7)["lost_reason"],
      "7 stale contact is swept and the reason names the last API call")

# ---- 8. throttle: a second non-forced sweep inside the interval is a no-op
t8 = new_claimed_task("throttle probe")
backdate(t8, 60 * 60)
check(server.sweep_lost_tasks() == [], "8a non-forced sweep inside LOST_SWEEP_INTERVAL_S does nothing")
check(f"TestProj:{t8}" in server.sweep_lost_tasks(now=time.time() + 60), "8b ...and runs once the interval has passed")

# ---- 9. startup reconcile: PM alive -> claims left alone; PM absent -> lost with restart reason
server.PM_SESSIONS["TestProj"] = {"proc": FakeProc(True), "started": iso_ago(60)}
t9 = new_claimed_task("startup, PM alive")
fixed = server.reconcile_orphaned_agents()
check(f"TestProj:{t9}" not in fixed and fm(t9)["status"] == "in_progress",
      "9a startup sweep leaves a PM-owned board's claim alone")
server.PM_SESSIONS.clear()
fixed = server.reconcile_orphaned_agents()
check(f"TestProj:{t9}" in fixed and "lost" in tags(t9)
      and "server restarted" in fm(t9)["lost_reason"],
      "9b startup sweep with no PM marks it lost with a restart reason")

# ---- 10. re-claim clears the verdict
proj.start_agent_run(t9)
check("lost" not in tags(t9) and "unfinished" not in tags(t9) and "lost_reason" not in fm(t9),
      "10 re-claiming clears lost/unfinished tags and lost_reason")

# ---- 11. no agent note at all -> note says so instead of quoting nothing
server.PM_SESSIONS.clear()
t11 = new_claimed_task("no note", note=None)
backdate(t11, 15 * 60)
server.sweep_lost_tasks(force=True)
sysnotes = [a for a in proj.load_task(t11)["agents"] if a.get("role") == "system"]
check(sysnotes and "left no note" in sysnotes[-1]["note"], "11 lost note handles a task with no agent note")

# ---- 12. the task file round-trips: lost_reason survives parse/render
check(fm(t1).get("lost_reason") and fm(t1)["status"] == "open", "12 lost_reason persisted in the task file")

# =====================================================================================
# Addendum (2026-09-07, after the 07:45Z mis-marking of T-0239 / T-0245 / T-0246)
# =====================================================================================
import os                      # noqa: E402
import subprocess              # noqa: E402


def set_frontmatter(tid: str, key: str, value: str | None) -> None:
    """Direct file edit of one frontmatter key (value None = drop the line), bypassing
    update_task so the file carries exactly the stale metadata the old code left behind."""
    p = proj.task_path(tid)
    text = p.read_text(encoding="utf-8")
    if re.search(rf"^{key}: .*$", text, flags=re.M):
        if value is None:
            text = re.sub(rf"^{key}: .*\n", "", text, flags=re.M)
        else:
            text = re.sub(rf"^{key}: .*$", f"{key}: {value}", text, flags=re.M)
    elif value is not None:
        text = text.replace("\n---\n", f"\n{key}: {value}\n---\n", 1)
    p.write_text(text, encoding="utf-8")


# ---- 13. the EXACT startup sequence main() runs, with the persisted PM session file:
#          pm-sessions.json names a PID that is alive (this very process), the board has
#          in_progress tasks claimed via /api/task/claim with 'pm'/'implementer' roster
#          entries and notes, the slot list is empty, and the claims are 40 min quiet.
#          reconcile_pm_sessions() -> reconcile_orphaned_agents() -> first watcher sweep
#          must leave every one of them at in_progress.
server.PM_SESSIONS.clear()
server.WATCHER["slots"].clear()
server.AGENT_CONTACT.clear()
server.PM_SESSIONS_FILE = tmp / "pm-sessions.json"
store.write_json(server.PM_SESSIONS_FILE, {
    "TestProj": {"family": "claude", "pid": os.getpid(), "started": iso_ago(2 * 3600)},
    "OtherBoard": {"family": "claude", "pid": os.getpid(), "started": iso_ago(2 * 3600)},
})
t13a = proj.create_task(None, "startup: PM claim, pm+implementer entries", "desc", "normal")
proj.start_agent_run(t13a)
proj.add_agent_record(t13a, "pm", "claude", "claimed by the board PM")
proj.add_agent_record(t13a, "implementer", "claude", "subagent working, will report back")
t13b = new_claimed_task("startup: PM claim, implementer only")
for t in (t13a, t13b):
    backdate(t, 40 * 60)
adopted = server.reconcile_pm_sessions()
check("TestProj" in adopted and "TestProj" in server._alive_pm_boards_locked(),
      "13a reconcile_pm_sessions re-adopts the persisted PM session as alive before the sweep")
fixed = server.reconcile_orphaned_agents()
check(not any(k.startswith("TestProj:") for k in fixed)
      and fm(t13a)["status"] == "in_progress" and fm(t13b)["status"] == "in_progress",
      "13b startup orphan sweep leaves the re-adopted PM board's 40-min-quiet claims alone")
lost = server.sweep_lost_tasks(force=True)
check(not any(k.startswith("TestProj:") for k in lost)
      and fm(t13a)["status"] == "in_progress" and fm(t13b)["status"] == "in_progress"
      and "lost" not in tags(t13a) and "lost_reason" not in fm(t13a),
      "13c the watcher's first sweep after startup leaves them alone too")

# ---- 14. a DONE task carrying stale lost metadata is never touched by either sweep
t14 = proj.create_task(None, "done with stale lost metadata", "desc", "normal")
proj.start_agent_run(t14)
set_frontmatter(t14, "status", "done")
set_frontmatter(t14, "tags", "lost,unfinished")
set_frontmatter(t14, "lost_reason", "stale verdict from an older restart")
backdate(t14, 3 * 3600)
before = proj.task_path(t14).read_text(encoding="utf-8")
server.PM_SESSIONS.clear()   # no PM protection at all -- status alone must protect it
fixed = server.reconcile_orphaned_agents()
lost = server.sweep_lost_tasks(force=True)
after = proj.task_path(t14).read_text(encoding="utf-8")
check(f"TestProj:{t14}" not in fixed and f"TestProj:{t14}" not in lost and before == after,
      "14a a done task is not touched by the startup sweep or the runtime sweep (file byte-identical)")
notes_before = len([a for a in proj.load_task(t14)["agents"] if a.get("role") == "system"])
proj.mark_agent_stopped(t14, None, reason="direct call on a done task")
notes_after = len([a for a in proj.load_task(t14)["agents"] if a.get("role") == "system"])
check(fm(t14)["status"] == "done" and notes_after == notes_before
      and "lost" not in tags(t14) and "lost_reason" not in fm(t14),
      "14b mark_agent_stopped(None) on a done task adds no verdict/note and the write settles the stale one")

# ---- 15. handing a lost task over as done drops lost/unfinished and lost_reason
t15 = new_claimed_task("lost then handed over as done")
backdate(t15, 15 * 60)
server.sweep_lost_tasks(force=True)
check("lost" in tags(t15) and fm(t15).get("lost_reason"), "15a precondition: task is lost")
proj.add_handover(t15, "finished after all", [], "done")
check(fm(t15)["status"] == "done" and "lost" not in tags(t15) and "unfinished" not in tags(t15)
      and "lost_reason" not in fm(t15),
      "15b handover to done clears lost + unfinished tags and lost_reason")

# ---- 16. clearing the tag via /api/task/edit (edit_task) also drops lost_reason
t16 = new_claimed_task("lost then tags cleared by edit")
backdate(t16, 15 * 60)
server.sweep_lost_tasks(force=True)
proj.edit_task(t16, None, None, None, None, "")
check("lost" not in tags(t16) and "lost_reason" not in fm(t16) and fm(t16)["status"] == "open",
      "16a edit_task(tags='') drops lost_reason with the tag")
t16b = new_claimed_task("lost, tags edited but lost kept")
backdate(t16b, 15 * 60)
server.sweep_lost_tasks(force=True)
proj.edit_task(t16b, None, None, None, None, "lost,unfinished,keepme")
check("lost" in tags(t16b) and fm(t16b).get("lost_reason"),
      "16b edit_task keeping the lost tag keeps lost_reason")

# ---- 17. a done task whose file still carries lost/lost_reason is settled by ANY write
#          (e.g. confirming it) -- this is the T-0246 case repaired without a manual tag edit
t17 = proj.create_task(None, "done, stale lost metadata, then confirmed", "desc", "normal")
set_frontmatter(t17, "status", "done")
set_frontmatter(t17, "tags", "lost,unfinished,uncommitted")
set_frontmatter(t17, "lost_reason", "stale")
proj.confirm_task(t17)
check(fm(t17)["status"] == "done" and tags(t17) == ["uncommitted"] and "lost_reason" not in fm(t17),
      "17 confirming a done task drops lost/unfinished + lost_reason, keeps other tags")

# ---- 18. status set to done via set_status (the /api/task/status route) does the same
t18 = new_claimed_task("lost then set_status done")
backdate(t18, 15 * 60)
server.sweep_lost_tasks(force=True)
proj.set_status(t18, "done")
check("lost" not in tags(t18) and "lost_reason" not in fm(t18), "18 set_status(done) settles the verdict")

# ---- 19. a slow `tasklist` (TimeoutExpired) reads as alive, not dead
if os.name == "nt":
    _real_run = server.subprocess.run

    def _slow_run(*a, **kw):
        raise subprocess.TimeoutExpired(cmd="tasklist", timeout=5)
    server.subprocess.run = _slow_run
    try:
        check(server._pid_alive(123456) is True, "19a _pid_alive: tasklist timeout -> assume alive")
    finally:
        server.subprocess.run = _real_run
    check(server._pid_alive(2 ** 31 - 7) is False, "19b _pid_alive: a PID that does not exist -> dead")

print()
print(json.dumps({"failures": failures, "tmp": str(tmp)}))
sys.exit(1 if failures else 0)
