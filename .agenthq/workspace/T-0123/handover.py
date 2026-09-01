import json, urllib.request

W = r"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0123"

body = {
    "project": "Laubrary_Dev",
    "id": "T-0123",
    "text": "The Chunks mock editor was fixed where the handover walk found it wrong, and the result is now written up as a build spec for the real tool. The biggest change: the little timing bar used to disagree with its own dials — a piece set to start at 0.15 seconds actually started at 0.65 — and it now shows one honest track per piece on a single shared clock, so what you dial is what you see.",
    "details": "What changed in the mock, in plain terms:\n\n- Timing now tells the truth. Each part of a recipe gets its own track on one shared clock, they can overlap, and the number in the dial is the moment the part starts.\n- Nothing jumps around any more. Adding a layer while scrolled down no longer throws the list back to the top; switching a part off no longer resizes the picture on the right; and the little marker you drag along the clock stays where you put it.\n- Less wasted space. Every card is one to two rows shorter, because controls that used to sit alone on a line now share one.\n- A recipe with only one timed part no longer shows timing dials that do nothing.\n- New layers are numbered instead of all being called \"New Layer\".\n\nHow to see it: open Unity, then Laubrary > Chunks Mock (Prototype), and pick \"Crate Smash\" from the Recipes button. \"Barrel Pop\" is the one to open if you want to see a recipe that needs only one part and therefore shows nothing belonging to the others.\n\nThe build spec is the real deliverable and it is attached. It also lists four things the mock does NOT have and the real tool must, so nobody mistakes the prototype for a finished design — undo support being the important one.\n\nNot yet verified: nothing in that list of four is built, and none of it was attempted here.\n\nStill open for you to decide (all written up in the spec): which kinds of parts the real tool should ship with, whether a layer plan should draw anything in the picture on the right, and how a recipe relates to the existing Chunks data.",
    "technicalDetails": "All changes are confined to D:\\UNITY\\Laubrary Dev\\Assets\\ChunksMock\\Editor\\ChunksMockWindow.cs (691 -> 926 lines). Re-verified at the end: Assets/Packages (the whole Laubrary package), Packages/, Assets/Demos/ and every CHANGELOG.md are untouched; git status over those paths is empty and the only working-tree change is the untracked Assets/ChunksMock/.\n\nP1 (timing model) - DECIDED absolute multi-lane, not sequential. Z.Timeline is by contract a single strip of CONSECUTIVE bands, so it cannot express lanes; a new nested MockTimingTracks : VisualElement (Painter2D lanes + absolutely-positioned Labels, pointer-captured scrub, 1/2/5-family tick step) replaces it. Ruler length is computed from ALL timed capabilities including disabled ones, or a toggle rescales the ruler; a disabled capability keeps its lane, drawn dim. A tick whose label would fall under the playhead readout is dropped, and the first/last tick labels are clamped inside the bar (they rendered as '.00s' before). This is a declared ZUI deviation: the production tool must be built on a new ZUI lane control, not on a copy of this hand-roll - blueprint section 4.\n\nP2 (scroll jump) - ZuiWindow.OnBeforeRebuild is overridden to read the outgoing ScrollView's scrollOffset; BuildUI re-applies it from a one-shot GeometryChangedEvent on the contentContainer (a ScrollView clamps the offset against content height, which is unknown before layout). Same mechanism carries the playhead seconds.\n\nP3 (guide jump) - the Timing section's presence is keyed to the RECIPE containing >= 2 timed capabilities, not to how many are ENABLED. Enable state therefore never adds or removes a section, so no reserved-visibility trick is needed. TimedCapabilities() replaces EnabledTimed().\n\nP4 (row packing) - BuildTiming became AddTimingRow(card, cap, params leading[]), so a card can pack its own trailing control onto the timing row. Formation 5 rows -> 3, Burst 4 -> 3, Splash 3 -> 2.\n\nP6 - the Delay/Duration pair is emitted only when TimedCapabilities().Count > 1.\n\nP8 - layer default name is 'Layer ' + (count + 1).\n\nP5 and P7 were deliberately NOT fixed: both need edits to Assets/Packages/Laubrary/Zui/Toolkit (Z.Split's persisted width, ZuiTimeline's occluded tick), which is production code this task excludes. Both are recorded as blueprint requirements.\n\nVerification harness (reusable, in this task's workspace): run.sh wraps 'unity command eval_file' against port 7800 and unwraps the JSON; act.ps1 + win32.ps1 drive DPI-aware Win32 SetCursorPos/mouse_event and capture with PrintWindow; walk/*.cs are the eval probes (_open, _load, _geom, _state, _audit, _hidden, _scrollprobe). The ops runner needs an explicit 'focus' op before each batch because the unity CLI steals foreground and a click into an unfocused editor window is silently swallowed.",
    "proof": [
        {"text": "Compiles clean and the Editor console is empty of errors after the final change (recompile_status failed=false, get_console_logs severity=Error returned 0)", "checked": True},
        {"text": "P1 fixed, seen: 'Crate Smash' draws a Pyre Formation lane 0.00-0.50s and a Generic Particle Burst lane 0.15-0.95s on one shared ruler - the burst's Delay dial reads 0.15 and its band now starts at 0.15", "checked": True},
        {"text": "P2 fixed, measured with a real mouse: an eleven-layer plan scrolled to offset 120.00, a real click on 'Add layer' added a row (content 660.4 -> 686.7pt) and the offset stayed 120.00 (it was 44.44 -> 0.00 before)", "checked": True},
        {"text": "P3 fixed, measured with a real mouse: the spatial guide's rect is identical before and after toggling a capability off - (484.9, 68.4, 511.1, 578.7) both times, against a ~31pt re-centre before", "checked": True},
        {"text": "P4 fixed, measured: Pyre Formation 5 rows -> 3, Generic Particle Burst 4 -> 3, Palette Splash 3 -> 2, no row wrapping at the 820x480 window minimum", "checked": True},
        {"text": "P6 decided and verified on 'Barrel Pop': one timed capability, so no Timing section anywhere and no Delay/Duration dials on the card", "checked": True},
        {"text": "P8 fixed, seen: successive layers default to Layer 3, Layer 4 ... Layer 11 instead of eleven rows called 'New Layer'", "checked": True},
        {"text": "Playhead survives a rebuild: dragged to 0.693087s, then a capability toggle rebuilt the window and it was still 0.693087s", "checked": True},
        {"text": "ZuiAudit: 0 findings, and all 15 display:none subtrees enumerated (13 MicroSlider alternate value displays, 2 unused scrollers) so nothing foldable was hidden from the audit", "checked": True},
        {"text": "UI-guide compliance pass run rule by rule over every changed surface; it produced two fixes of its own (a missing tooltip on the new timing control, a missing ellipsis on a clipped band name) and one declared deviation recorded in the blueprint", "checked": True},
        {"text": "Isolation holds: git status over Assets/Packages, Packages, Assets/Demos and every CHANGELOG.md is empty - the only working-tree change is the untracked Assets/ChunksMock/", "checked": True},
        {"text": "NOT verified: nothing in blueprint section 7 exists - no Undo anywhere, no multi-lane ZUI control, no layout reset, and ZuiTimeline's occluded tick label is unfixed", "checked": False},
    ],
    "attachments": [
        W + r"\CHUNKS-EDITOR-BLUEPRINT.md",
        W + r"\shots\r11-final.png",
        W + r"\shots\r06-burst-off-timing.png",
        W + r"\shots\r10-barrel-pop.png",
        W + r"\shots\r07-left.png",
        W + r"\shots\r08-min-size.png",
    ],
    "newStatus": "done",
}

req = urllib.request.Request(
    "http://127.0.0.1:8778/api/task/handover",
    data=json.dumps(body).encode("utf-8"),
    headers={"Content-Type": "application/json"},
)
print(urllib.request.urlopen(req).read().decode("utf-8")[:400])
