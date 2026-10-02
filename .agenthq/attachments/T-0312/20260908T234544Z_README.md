# T-0312 — the shared probe library, and how to run it

Every archived probe in this programme walked the visual tree with `e[i]` / `e.childCount`, which enumerate an element's **content container**, not its hierarchy. Anything parented outside a container's content was therefore never visited — a `ZuiSection`'s own bar, a `ZuiColumnFlow`'s non-rightmost columns (its `contentContainer` IS the rightmost column), a `ZuiBox`'s header row, the whole `ZuiViewBar`. Measured on one Shaper window with a single line changed: **107** laid-out text elements via `e[i]` against **272** via `e.hierarchy[i]`.

`probes/zlib.cs` is the one place that walk now lives. Nothing in this folder uses `e[i]`.

## Running a probe

```sh
sh "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/probes/zrun.sh" \
   "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/probes/<probe>.cs"
```

`zrun.sh` concatenates `zlib.cs` with `<probe>.cs` into `probes/.combined.cs` and hands the pair to `unity.exe command eval_file --project-path "D:/UNITY/Laubrary Dev - Shaper"`. An `eval_file` body cannot declare methods, so everything the library exposes is a delegate; a probe may use any of them and must end with `return <string>;`. **Pass absolute paths** — `unity.exe` resolves a relative path against its own working directory, not yours.

Big reports do not have to fit through the return value: `ZDump(name, text)` writes to `EditorPrefs["T0312.out"]` (default `workspace/T-0312/out`) and returns the path.

## What `zlib.cs` exposes

| delegate | what it gives you |
|---|---|
| `ZType(name)` / `ZWin(typeName)` | a `System.Type` by short name across every loaded assembly; the live `EditorWindow` of that type (`"ShaperWindow"`, `"PyreWindow"`) |
| `ZWalk(e, list)` / `ZAll(root)` | **the** recursive hierarchy walk |
| `ZDisplayed` / `ZLaidOut` / `ZDrawn` | no ancestor is `display:None`; the `worldBound` is a real rect; both — i.e. what a user can see |
| `ZOwnText(e)` / `ZCaption(e)` | the element's own text or `label` property; its best human identity (own text → first descendant text → nearest preceding sibling's text → the parent's label) |
| `ZCls(e)` / `ZPath(e)` | its USS classes; its last 6 ancestors as `Type#name` |
| `ZTip(e)` | the **effective** tooltip — its own, else the nearest ancestor's, which is what a hover actually shows |
| `ZIsCtrl(e)` / `ZIsLeafCtrl(e)` | is it an interactive control (`Button`, `BaseField<>`, `BaseSlider<>`, any named ZUI control) rather than chrome or layout; and does it contain no control of its own |
| `ZNeed(te)` / `ZHave(te)` | the width a `TextElement`'s string needs vs the width its content box gives it |
| `ZContentWorld(e)` | an element's **content** box in world space (`VisualElement.LocalToWorld(Rect)` is internal in this Unity version) |
| `ZAudit(win, tag)` | all four audits in one walk, returned as text |
| `ZCount[...]` | the counts that audit produced |
| `ZSummary(tag)` | those counts as one line |
| `ZDump(name, text)` | write a report under `T0312.out`, return its path |
| `ZTOL` | 1.5 px — the device-pixel tolerance T-0304 established, applied to every measurement |

## The four audits, and the rule each applies

- **captions** — a laid-out, non-wrapping `TextElement` whose string measures wider than its content box by more than `ZTOL`.
- **overflow** — a drawn element spilling out of its **parent's content box**, or past the **window root**, by more than `ZTOL`. Horizontal spill is always reported (the UI guide: clipping on a one-pane window is a bug signal); vertical spill is reported only outside a `ScrollView`, where scrolling is the legitimate answer.
- **tooltips** — a drawn control whose effective tooltip is empty ("every control gets a tooltip").
- **inert** — a drawn control that is disabled (`enabledInHierarchy == false`) with no effective tooltip to say why. Disabled controls that DO explain themselves are listed separately, with their reason, so the reader can judge the wording.

`ZAudit` also lists **every leaf control** with its label, tooltip, class, resolved rect and enabled state — the raw material for any further question.

### Known chrome, deliberately not reported

Three false positives were measured and are skipped by name/class in `ZSkipName` / `ZSkipCls`, each for a stated reason: `TwoPaneSplitView`'s 11 px grab dragline over its 0.89 px anchor, a `MinMaxSlider`'s two thumbs (7.1 px outside the dragger they bracket, by design, at every width), and a `ScrollView`'s own viewport/content/scroller elements.

## The probes in this folder

| probe | what it does |
|---|---|
| `p0-base.cs` | records the session-start baseline (compile state, dirty assets, `Assets/Shaper` + `Assets/Pyre` SHA-256s, open windows, prefs) |
| `s-shaper.cs` | opens Shaper, switches **every** toggle-bar section on, binds `T0312.doc`, selects layer `T0312.layer`, sizes the window `T0312.winw` and the dial pane `T0312.pane` |
| `s-pyre.cs` | creates (once) `Assets/Pyre/AuditT0312.asset` — layer 0 a plain **Disc**, layer 1 a **Torch** — opens Pyre and binds it |
| `a-audit.cs` | runs `ZAudit` over `T0312.unit`, dumps it, returns the counts + the actionable findings + the laid-out `ZuiColumnFlow` column count |
| `d-overflow.cs` | full ancestor chain (widths, flex, wrap, classes) for every horizontal overflow |
| `d-subtree.cs` / `d-row.cs` | the subtree of a named `.zui-field`, or of the row holding it |
| `d-toolbar.cs` | the asset toolbar row: every child, its extents, and the slack to the window edge |
| `t-star.cs` | every `★` library button: glyph width needed vs content box given |
| `t-longname.cs` / `t-hugename.cs` | seed a 49- and a 113-character document name for the toolbar growth checks |
| `w-idle.cs` + `w-count.cs` + `sweep-idle.sh` | T-0304's layout-struggle detector: rebuild at a width, clear the console, idle, count errors |
| `sweep-shaper.sh` / `sweep-pyre.sh` | the matrices — Shaper layer 0/1 × 1/2/4 columns, Pyre Disc/Torch |
| `z-clean.cs` | restores the editor to `p0-base.txt` |

**Column counts.** Shaper's dial stack is `Z.ColumnFlow(360f)`, so the count follows the *flow's* width: 1 below 720, 2 at 720, 3 at 1080, 4 at 1440. Two things bite. The count must be read in a **later** eval than the one that set the width — a flow redistributes on the `GeometryChangedEvent` after the rebuild, so a same-call read always says 1. And the fourth column needs a window around **1950 px**: at 1700 the right pane's 320 px minimum clamps the flow to 1359 and only three appear. Pyre has no split at all — its dial pane is a fixed 360 px `ScrollView`, so `ZUI.Split.pyre.window.split.v1` is inert there and Pyre is always one column.
