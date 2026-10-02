# T-0304 + T-0305 — the layout recursion storm, and the swarm's inverted "Appearance order"

Everything below was measured in the running Shaper-worktree editor (`Application.dataPath = D:/UNITY/Laubrary Dev - Shaper/Assets`, confirmed at the start of every probe and after both recompiles). Probes: `workspace/T-0304/probes/`.

## 1. T-0304 — one 22px image inside one field, chasing a 0.44px rounding error

**The driver is `Z.Field`'s height stamp (T-0287/T-0288), not the column flow, not the scrollbar, not the splitter.**

Reproduced first, on the exact configuration the card names — Pyre bound to `Assets/Pyre/New Pyre Plus.asset`, window 1600×900, dial pane 560, rebuilt: **422 errors / 5s idle**, and it never stops.

Bisection, by hiding one subtree at a time (`display:none`) with a full `Rebuild()` before each step so the tree was never a mutated one:

| hidden | errors / 4s idle |
|---|---|
| nothing (baseline) | 358 |
| the `Shape` section | **0** |
| … its Torch form box | 358 → the box's child 7 alone: **0** |
| … that box's `Colour ramp` field | **0** |
| … the field's `ZuiGradientControl` output row | **0** |
| … that row's `Image` (the 22px objective-preview strip) | **0** |
| that row's `★ Library` button instead | 392 (still storming) |

The `Image`'s **height** is the trigger, not its texture, its width, its flex settings or its measure function: `flexBasis:0` → 368, `minWidth:0` → 384, `image = null` → 406, `ScaleToFit` → 362, fixed `width:300` → 362, `flexGrow:0` → 346 — all still storming; `height:24` → **0**, `height:16` → **0**, `height:auto` → **0**.

**Why 22px is special, and why the band.** This editor's UI scale puts a 22px request off the device-pixel grid: the strip requests 22 and resolves to 21.78, so the `ZuiGradientControl` around it resolves to 167.111 while the `.zui-field` wrapper pinned at that value resolves to 166.667. Yoga rounds an element's absolute EDGES, so a row's resolved height depends on the fractional y it inherits from everything stacked above it — which is why no single card reproduces it (the aggregate above the ramp is what sets that fraction) and why it only fires in a narrow band of pane widths (the pane width is what decides the aggregate). `StampFieldHeight` guarded its write with `Mathf.Approximately`, whose relative epsilon here is ~2e-5 — two orders of magnitude below a 0.44px flip — so it rewrote the height every pass, every write re-ran layout, and the panel logged "Layout update is struggling to process current layout (consider simplifying to avoid recursive layout)" forever.

**The fix** (`Assets/Packages/Laubrary/Zui/Toolkit/Zui.cs`, `StampFieldHeight`): re-stamp only when the needed height moves by more than **1.5 layout pixels** — the same absolute-tolerance reasoning, and the same constant, `Z.Split` already documents at `Zui.cs:107` for exactly this class of device-pixel snapping. It is above rounding at any editor UI scale and two orders of magnitude below the 16-20px overflows T-0287/T-0288 introduced the stamp to fix.

**Live after the fix**, same asset, same window: the ramp field settles at stamp 166.667 against a `need` of 167.111 and stays there — sampled four times, identical, 0 errors.

**Proof sweep** — pane width set, window rebuilt at that width, 5s of pure idle, `LogEntries.GetCountsByType` after a `Clear()`:

- **Pyre, 360 → 1400 in 10px steps (105 widths, window 1700×900): 0 errors at every width.** The pre-fix band 540-620 measured 340-580 per 4-5s in this same session.
- **Shaper, 360 → 1400 in 10px steps (105 widths, `ZUI.Split.shaper.window.split.v1` driven, every section switched on): 0 errors at every width.**
- Shaper again with the swarm ON and a Circle spawn, at panes 360 and 560 across all three timings: 0 errors each.

**No overflow reintroduced.** Worst stamp residual over every laid-out `.zui-field`: **0.444px** in Pyre (on `Colour ramp`, the T-0288 case itself), **0.000px** in Shaper. The T-0284 vertical-overflow probe reports 1 hit of 1602 in Pyre and 3 of 1411 in Shaper, and every one of them is a scroll viewport or the preview backdrop doing its job (the left pane's own `ScrollView`, Shaper's filmstrip `ScrollView`, `BackSplashElement`) — no `.zui-field` overflows anywhere.

**Cleared by measurement, not by argument:** `Z.ColumnFlow`'s 1-vs-2 column decision is not involved — with `ColumnWidth 360` the bucket is 1 for every pane width from 520 to 719, so the guard returns early through the whole broken band; the vertical scrollbar is not involved either (forcing `ScrollerVisibility.AlwaysVisible`, which pins the pane width, left 440 errors); and `ClampedLeftPaneWidth()` is untouched, so the 820px minimum window still lands the pane at 560 — that width is simply no longer a bad one.

## 2. T-0305 — "Appearance order" now follows the shape, which is what the engine reads

`spawnOrderChaos` drives a permutation the compiler builds from the SHAPE, not the timing: `ShaperCompiler.cs:465`, `placed || swarm.spawnOrderChaos > 0f` where `placed = swarm.shape != ShaperSwarmShape.None`. The card drew it only inside the Window and FrameStep branches of `BuildSwarmTimingBox`, which followed the field's own doc comment rather than the engine.

It is now built once, before the timing branches, added to all three, and declared inert through the section's existing `Inert(...)` helper when `shape == None`, with the reason as its tooltip (and prepended onto its caption's tooltip by `StampReason`).

Re-measured on a scratch copy (`Assets/Shaper/AuditT0305.asset`, deleted afterwards), swarm on, count 8, spawner radius 24, changed pixels summed over frames 0/4/8/12 with `spawnOrderChaos` 0 → 1:

| state | pixels moved | drawn | greyed |
|---|---|---|---|
| shape None, Stagger | 0 | yes | greyed, with reason |
| shape None, Window | 0 | yes | greyed, with reason |
| shape None, FrameStep | 0 | yes | greyed, with reason |
| shape Circle, Stagger | **54809** | yes | live |
| shape Circle, Window | 41144 | yes | live |
| shape Circle, FrameStep | 45499 | yes | live |

(The absolute counts are larger than T-0303's 4224/6774/6354 because they were taken on a different document; the 0-vs-live split is the same and is what the rule follows.)

Geometry checked at panes 360, 560 and 1400 and all three timings: the dial sits inside its box every time (`insideBox=True`), `enabled=False` exactly in the shape=None states, drawn and enabled otherwise.

`ShaperSwarmDef.spawnOrderChaos`'s doc comment was corrected to say the same thing — it was the source of the wrong rule.

## Files touched

- `Assets/Packages/Laubrary/Zui/Toolkit/Zui.cs` — `StampFieldHeight` tolerance (T-0304).
- `Assets/Packages/Laubrary/Editor/Shaper/ShaperWindow.Sections.cs` — `BuildSwarmTimingBox` (T-0305).
- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperSwarmDef.cs` — doc comment only, no serialized field touched (T-0305).

No Pyre file was edited. Not committed (programme rule 3).

## State left behind

Pyre closed (it was not open when this started), Shaper unbound at its found position 60,20 1600×1150, its section toggle bar restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`, `ZUI.Split.shaper.window.split.v1` deleted (it had no stored value), every `T0304.*` pref deleted, the scratch document deleted with its `.meta`, console cleared, **0 dirty assets project-wide**, `scriptCompilationFailed = False`. `Assets/Pyre/New Pyre Plus.asset` is 216435 bytes — byte-length identical to the T-0303 baseline — and `git status` on `Assets/Pyre` shows only the `Green Lantern.asset` modification that was already there.
