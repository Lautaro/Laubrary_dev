# LauAsset Lock — decision sheet

*AgentHQ T-0075. Written 2026-08-25. Companion to `D:\UNITY\Laubrary Dev\LAUASSET_LOCK_DESIGN.md` — this is the short version you actually have to act on.*

**Read this instead of the design doc if you only have five minutes.** The design doc is 69 KB and ends with eight open questions. Nothing can be built until those are answered, so this sheet restates each one as a **recommended default you can approve, amend, or reject**, and — the part the design doc does not do — it says **which questions actually block which phase**. Six of the eight do not block starting.

---

## The one-line summary of the whole feature

You get **two** separate things that happen to share plumbing:

- **A safety net** — every Laubrary asset quietly keeps its last N states, and a History button puts any of them back with one click. Nothing is locked, nothing changes about how the game runs, you just stop being able to permanently ruin an asset by fiddling. *This is the cheap half and the one that answers the fear you actually described.*
- **A release gate** — you can mark an asset "this is what the game looks like right now", and from then on it takes a deliberate act to change it, with a red banner everywhere warning you when what you are looking at is not what the game will use.

They are deliberately not the same feature. The design doc's main argument is that letting them collapse into one is how this goes wrong.

---

## What blocks what

| Question | Subject | Blocks |
|---|---|---|
| **Q3** | when a soft-lock re-locks itself | **Phase 1 — starting** |
| **Q7** | are auto-snapshots on by default | **Phase 1 — starting** |
| Q1 | one seal per asset or a history of N | Phase 2 |
| Q2 | is "fail the build on drift" enough on its own | Phase 2 (and decides whether Phase 3 exists at all) |
| Q6 | how much gets pulled in when you seal something | Phase 2 |
| Q8 | does Launimator's existing version system get absorbed | Phase 2's API shape only |
| Q4 | does locked-mode Play also block editing | Phase 3 |
| Q5 | is Test mode per-person or per-project | Phase 3 |

**So: answering Q3 and Q7 alone is enough to start.** The design doc says "Phase 1 is safe to start without answering anything", which is *nearly* true — those two questions decide Phase 1 behaviour and are cheap to answer. Both are pre-answered below.

---

## The eight decisions, with a recommended answer for each

Reply with **"approve"** to take all eight as written, or name the numbers you want changed. Anything you do not mention is taken as approved.

### Q3 — When you unlock a locked asset, when does it lock itself again? ⛔ blocks Phase 1

**Recommended: never automatically.** It stays unlocked until you click Re-lock. Instead of a timer, the protection is that it is *loud* — a red "UNLOCKED FOR EDITING" banner sits in the window the whole time, and the asset browser shows unlocked assets differently from locked ones, so a forgotten unlock is visible rather than silent.

*Why not auto-relock:* every automatic rule (on window close, on Play, after five minutes) eventually re-locks you mid-thought and throws away the thing you were about to do. Being visible beats being clever.

### Q7 — Is the auto-snapshot safety net on for everything, or opt-in per asset? ⛔ blocks Phase 1

**Recommended: on for everything, capped, and never committed to git.** Opt-in per asset means you only have a safety net where you remembered to ask for one, which is never the asset you actually ruin.

*One caveat that came out of the measurements:* snapshots are not free. Pyre assets are 93% of all the data involved and the biggest single one is 322 KB per snapshot, so a naive ten-deep history of everything is roughly 48 MB on disk and grows every time you touch a slider. The recommendation is therefore **on by default, but with a shallower history for Pyre specifically than for everything else**, plus skipping snapshots that are byte-identical to the previous one. If you would rather it were simply off by default and you turn it on per tool, say so — that is a legitimate call, it just makes the feature much less useful.

### Q1 — One protected version per asset, or a numbered history of several?

**Recommended: one.** "This is what the game should look like at the moment" is singular by nature, and one version is meaningfully simpler to store and reason about.

*The counter-argument, which is real:* Launimator already keeps numbered versions, so several-per-asset would make the two systems match, and would let you name a protected version per milestone ("vertical slice", "demo build"). If milestone-named versions sound useful to you, say so now — it is much cheaper to decide this before anything is built than after.

### Q2 — Is "refuse to build when a protected asset has been changed" enough on its own?

**Recommended: yes, make that the default and build it first.** It gives you the guarantee that matters — the build is what you protected — by simply refusing to build when it isn't, with a message naming the assets. Zero risk, roughly a tenth of the work of the alternative.

*What you give up:* you cannot simultaneously keep a work-in-progress draft **and** ship the protected version. You would re-protect or revert before building. The thing you described in the original request — keep fiddling, but builds quietly use the protected copy — is the alternative, and it is by far the most dangerous part of the whole design (it involves temporarily swapping an asset's contents, which is the one operation that can destroy work if the editor crashes at the wrong moment). **If you would genuinely be happy re-protecting before a build, then the entire risky third of this project disappears.** That is the single highest-leverage answer on this sheet.

### Q6 — When you protect an asset, how much of what it depends on gets protected with it?

**Recommended: everything it reaches, but always shown to you first for confirmation.**

*Why this matters more than it sounds:* protecting a character but not the weapon it holds gives you a protected body swinging a live, still-changing weapon — the design doc calls this the worst failure mode in the feature. Protecting everything reachable is the only correct answer; the risk is that the first time you use it, it pulls in dozens of shared assets and feels heavy. Showing the list before committing is what makes that survivable.

### Q8 — Does Launimator's existing version system get folded into this?

**Recommended: not now — leave it alone, and revisit once this has been used in anger.** It is a mature, load-bearing system with a documented history of serialization landmines, and there is no urgency.

*The only thing this decides today* is whether the new protection code is written in a deliberately Launimator-compatible shape so a later merge is easy. Recommendation is yes, keep it compatible, but do not touch Launimator.

### Q4 — In "locked" play mode, should editing be blocked as well as hidden?

**Recommended: block it.** If pressing Play shows you the protected version, then editing the draft while watching a result that does not reflect your edits is just a way to get confused and lose work.

*This is a taste call about how you work* and it only comes up in Phase 3. If you find blocking annoying, the alternative is to allow it and rely on the red banner.

### Q5 — Is "test mode" a per-person setting or a per-project one?

**Recommended: per person**, with the project setting acting as the starting value for someone who has never set it.

*Reasoning:* "am I currently testing against my drafts or against the protected versions" is a statement about how *you* are working this afternoon, not a fact about the project. Per-project means it lands in git and flips for everyone when someone else commits it, arriving as a surprise. The failure mode of per-person (you forget which mode you are in) is contained by a hard rule elsewhere in the design: **builds ignore the mode entirely and always use protected versions.**

---

## Two things that are true regardless of your answers

- **Nothing is built.** There is no lock, no snapshot, no revert, no banner in Laubrary today. This whole thing is still a plan.
- **Out of the box, nothing would ever be silently substituted.** Under the recommended defaults, protecting an asset gets you a warning banner, a one-click revert, and a build that refuses to run when things have drifted. The behaviour where the game quietly plays a different version than the one you are editing is a deliberate opt-in that arrives last, because it is the only part that can lose work.

---

## If you approve as-is, the first thing built is

1. The red/orange/green banner control, which Laubrary's UI toolkit currently has no equivalent of — it has no red at all today.
2. The soft lock: mark an asset locked, its controls grey out, an Unlock button, and a banner explaining why.
3. A backstop that catches writes to a locked asset from anywhere, including the standard Unity inspector — not just from Laubrary's own tool windows.
4. The rolling safety net and its History / Restore button.

That set stands on its own. If the rest never happens, it was still worth building.

**Full reasoning, alternatives considered, and every technical detail: `D:\UNITY\Laubrary Dev\LAUASSET_LOCK_DESIGN.md`. Evidence for the measured claims: `D:\UNITY\Laubrary Dev\LAUASSET_LOCK_MEASUREMENTS.md`.**
