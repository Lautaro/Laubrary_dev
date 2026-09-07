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

print()
print(json.dumps({"failures": failures, "tmp": str(tmp)}))
sys.exit(1 if failures else 0)
