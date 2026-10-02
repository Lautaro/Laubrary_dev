# Shaper harmony + structural audit programme — CLOSED 2026-09-09

Node `ShaperHarmony-2026-09-07`, cards T-0253..T-0341, branch `feat/shaper` in the worktree `D:\UNITY\Laubrary Dev - Shaper`, commits 02ef1b01 .. 1ec2eb91 (one commit per round, each named with its task id, so any round can be reverted on its own). Closed on the owner's decision: "stop when 21 is in and leave what is left as tasks."

## What the programme did

- **Stages 1–5 of the harmony plan** (2026-09-07): dead subsystems deleted (~3,800 lines), honest dials (ShowIf, grey-with-reason, working defaults), label map, toggle bar 13→8, Save affordance, ZUI folded-curve look.
- **21 structural-audit rounds** (2026-09-08..09), each measuring on saved documents and, from round 11, by eye. Every round found something until round 20; rounds 19–21 were Shaper-only closing passes and found 3, 1 and 4 items respectively. Highlights: phantom serialized border flattening every saved Solid; one Position box per node; the first-Save-as-empty-picker bug; the split divider handle stranded inside the right pane at the window's own minimum size; the section bar hiding Fill on every document after a press on a hosted-generator document; 29 hosted dials that did nothing at their defaults now greyed with the engine's own reason; Fire/Fireball proven pixel-equal to Pyre with matched sims.
- **Scope correction (2026-09-09):** rounds 13–18 widened to other Laubrary tools (Larder, Lathe, Choreographer, Loom, Lazor, Zoe Preview, Cartographer, Dashboard, BackSplash). That was PM scope creep; the owner let those fixes stay ("I'll decide if we discard them") and the five cards they spawned (T-0329..T-0333) are PARKED as blocked. The non-Shaper owner questions moved from T-0260 to T-0335.

## What is left (open cards on this node, none claimed)

- **T-0338 (high)** — a Bag member's `fill.authored` flag gets set without the author asking and the document silently stops animating; mechanism proven, writer not identified.
- **T-0339** — 48 declared dials on Torch/Jet/Radial Jet/Explosive Jet move 0 px at defaults with no reason on screen (+133 tooltip-only conditions).
- **T-0340** — Pyre's own window very likely shows the same 29 guarded dials live and silent (Pyre, not Shaper; filed because the finding came from here).
- **T-0341 (high)** — the owner's handover walk: no human has operated any of it.
- **T-0298, T-0300, T-0301** — blocked on owner answers.
- **T-0260** — 14 Shaper-only owner questions (stage-6 cuts, Stepped profiles, ember colour, tag filter, duplicate picker names, Views dropdown, Solidity greying, frame-window greying).

## Standing lessons (also in project memory)

- Probes prove parts; a human completes a task. Static probes called Shaper clean for 18 rounds; the first Shaper-only by-eye pass found three real defects at the window's own minimum size.
- Measure hosted generators at a MID frame — they paint nothing at phase 0.
- `-automated` makes every UI Toolkit window paint white; a backgrounded-shell relaunch dies with the shell; the CLI's 30 s timeout does not stop the editor — bound every probe.
- A "keep going" audit loop stays on the tool it was opened for.
