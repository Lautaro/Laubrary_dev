# How to start the Shaper build

*Answer to "How do I start it? Do I start any of the 17 tasks or is there a master task?" — written 2026-08-30 for T-0098, verified against the tracker's own code and the live board rather than from memory.*

---

## The short answer

**There is no master task, and there is no task you have to start first by hand.** The seventeen tasks T-0099…T-0115 are peers: each one is a bounded piece of work, none of them drives the others, and nothing in the tracker knows that one depends on another. Ordering exists only as words inside the descriptions (the wave numbers) and as the priority flags on the two Wave 0 tasks.

So you have two ways to start, and **the second one is the one to use.**

**Option A — start them one by one yourself.** Open a task, press **Request resolve** (the button that appears while a task is Idle), and an agent picks it up within about five seconds. You would do that seventeen times, deciding each time what runs next.

**Option B — switch the Shaper group itself on, and let the board run the queue.** On the tree, the Shaper group has a supervise toggle (the 🧭 compass icon next to the group's name). Turn it on and the tracker takes over the sequencing: every five seconds it looks at the group, and **if nothing in it is currently requested or running, it promotes exactly one waiting task and starts it.** When that one finishes, the next starts. As long as nobody overrides it by hand, nothing runs in parallel inside the group, so the second Unity editor is never contested and you are never asked to pick the next task. Be aware that this is a habit of the queue rather than a lock: pressing **Request resolve** on a second task in the group yourself, or using **Force now**, will start a second agent alongside the first, because neither of those paths looks at what else in the group is already running.

**Recommendation: Option B.** It is what the group was laid out for, and it is the closest thing that exists to the "board overwatcher" idea — it is not a persistent thinking overseer, it is a re-decision made fresh every five seconds, which is what makes it safe: add a task, re-prioritise one, or cancel one mid-build and the next choice simply takes that into account.

---

## The order it will actually run them in

The rule is **highest priority first, then lowest task number** — not the wave numbers in the titles. Because the numbers already ascend with the waves, the two orders agree almost exactly. The run order will be:

1. **T-0099** — push the branch to a remote *(high)*
2. **T-0101** — render a visual baseline, one frame per generator *(high)*
3. **T-0100** — create the second working copy and second editor
4. **T-0102 … T-0115** — in plain numerical order, Wave 1 then Wave 2 then Wave 3

The single deviation from the written waves is that **T-0101 runs before T-0100**. That is harmless and arguably right: the baseline is rendered from the Pyre that exists today, in the copy that exists today, so it does not need the second copy to exist yet.

---

## Three things to know before you flip the switch

**Turn the switch off to stop the build.** Un-supervising the group does not interrupt whatever is running, it simply stops the next task from being promoted. That is the brake.

**A task that gets stuck does not jam the queue.** If an agent hits something it cannot decide alone it hands the task back with a question. That state does not count as "running", so on the next tick the group promotes the following task and carries on. The consequence to be aware of is the opposite of a deadlock: **questions can quietly pile up while the build keeps moving**, so check the board rather than assuming silence means nothing needs you.

**The first task in the queue does the one thing that reaches outside this machine.** T-0099 publishes the branch holding the entire current Pyre. The remote itself already exists and already carries five other branches — what is missing is this one branch, which today lives on one disk only. Either run that one yourself before switching the group on, or expect the agent to check with you before it pushes. Everything after it is local work.

---

## Two smaller facts, both already settled

**The tracker restart that the last handover said was outstanding has already happened.** The tracker running now was started after the change landed, and it is serving the new behaviour — a task's group design document is being pasted into each agent's briefing automatically. Nothing to do here.

**At most four agents run at once across the whole board through the normal queue.** The project's own setting says six, but a lower board-wide ceiling wins; the one thing that ignores both is **Force now**, which starts its task regardless. This does not affect the Shaper group while it is supervised, because supervision runs it one at a time by design — it only matters if you also start work in other groups alongside it.
