# T-0312 / T-0309 / T-0311 — the hierarchy-walk audit redo, 2026-09-09

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, editor on port 7801. `Application.dataPath` was confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the head of every probe and again at cleanup. Nothing below is asserted from a document; every number was read out of the running editor.

Probe library + how to run it: `probes/` and `README.md` in this folder. Raw dumps: `out/`.

---

## 1. The probe library (T-0312's first half)

`probes/zlib.cs` is now the ONE place the visual-tree walk lives, and it walks `e.hierarchy`, never `e[i]`. `probes/zrun.sh` concatenates it with any probe and evals the pair, so a probe inherits the whole library and can never re-introduce a content-container walk by copy-paste. `README.md` documents every delegate, the four audits, the three chrome false positives that are deliberately suppressed (with their measurements), and the two column-count gotchas.

**What the wider walk actually sees.** On the states audited here the hierarchy walk reaches, per window state:

| window state | elements walked | drawn | text elements | controls | leaf controls |
|---|---|---|---|---|---|
| Shaper, demo doc, layer 0, 1 column | 1319 | 898 | 324 | 251 | 144 |
| Shaper, demo doc, layer 1, 4 columns | 1339 | 933 | 343 | 228 | 111 |
| Pyre, scratch spec, Disc layer | 514 | 253 | 84 | 72 | 43 |
| Pyre, scratch spec, Torch layer (before the fixes) | 1369 | 736 | 248 | 180 | 72 |

For scale: T-0307 measured the same truncation sweep seeing **107** text elements through `e[i]` against **272** through `e.hierarchy[i]` on one state. The counts above are what the four audits were run over.

## 2. The four audits, re-run — corrected totals

Shaper: the committed demo document `Assets/Demos/ShaperDemo/ShaperDemoDoc.asset`, every toggle-bar section switched on, layer 0 and layer 1, at 1 / 2 / 4 `ZuiColumnFlow` columns. Pyre: a scratch spec `Assets/Pyre/AuditT0312.asset` with a plain **Disc** layer and a **Torch** layer. `ovfX(real)` excludes the chrome the library now suppresses (a `MinMaxSlider`'s two thumbs, the split dragline) so the before and after columns compare like with like.

| state | captions short | overflow-X (real) | off-window | controls with no tooltip | disabled with no reason |
|---|---|---|---|---|---|
| **Shaper before** — L0 c1 / c2 / c3, L1 c1 / c2 / c3 | 2, 2, 2, 2, 2, 2 | 0, 0, 1, 1, 1, 2 | 0 | **0** | **0** |
| **Shaper after** — L0 c1 / c2 / c4, L1 c1 / c2 / c4 | **0** everywhere | **0** everywhere | 0 | 0 | 0 |
| **Pyre before** — Disc, Torch | 1, 1 | 0, 10 | 0 | **0** | **0** |
| **Pyre after** — Disc, Torch (also re-run at the 820 px minimum window) | **0** | **0** | 0 | 0 | 0 |

Two things worth stating plainly rather than burying in the table:

- **Tooltips were already complete, and now that is a real result.** 0 of 251 Shaper controls and 0 of 180 Pyre controls lack an effective tooltip — measured over the whole hierarchy, not the subset earlier passes reached. Where a control has no tooltip of its own the audit counts its nearest tooltipped ancestor, which is what a hover actually shows.
- **No disabled control is unexplained.** 23–30 controls are greyed in Shaper and 1 in Pyre; every one of them carries a sentence saying why ("Position and range only apply to a Point light…", "This shape is swept around its centre, so the fraction dials do nothing here…"). They are listed with their reasons in each `out/audit-*.txt`.

The *before* Shaper sweep reached three columns, not four: at window 1700 the right pane's 320 px minimum clamps the flow to 1359 px and the fourth column needs ~1440. The *after* sweep was re-run at window 1950 and does reach four. Four columns give the narrowest per-column boxes of any state measured, i.e. the state most likely to overflow — and it is clean.

## 3. What the wider walk found, and the fixes

### 3.1 A `.zui-field` around a full-width strip overhangs its box by exactly the label's width — FIXED

`ZuiRampControl`'s inner row is `width: 100%`, which resolves against the space the whole box has rather than against what is left beside the field's label, so a `ZuiGradientControl` came out as wide as its box; `.zui-field` is `flex-shrink: 0`, so the field measured label + full box width and overhung. Measured: Shaper's fill `Colour bands` field **496.9 px inside a 415.1 px box** (81.8 over, at every column count), Pyre's Torch `Colour ramp` **389.3 px inside 313.3 px** (76.0 over).

Fixed in `Zui.cs`'s `Z.Field` by extending the `zui-field--wrap` rule T-0297 already added for a wrapping MiniRadio to cover a gradient/ramp strip — the field is allowed to shrink, and the strip (already `flex-shrink: 1`) shrinks with it. Re-read after: the `Colour ramp` field is now 313.3 px, exactly its box, with the strip at 237.3. Detected from the control's own class, so every reflected `ZuiGradient` / `IZuiRamp` field in every tool is fixed at once.

### 3.2 Every reflected `[Range] Vector2` dial overhung its box — FIXED

`ZuiReflect` drew a bounded `Vector2` as `Z.Field(label, Z.MinMax(...))`, and `opt.ControlWidth` is the width of Z.MinMax's **slider**, not of the composite — so the drawn control was ControlWidth + two 42 px fields + gaps = 252.9 px and the field around it reached 340.9 px inside a 313.3 px box. Nine dials in the Torch form alone (`Root spread`, `Root height`, `Tongue climb / peel / width / length / life`, `Ember rise`, `Ember size`) overhung by 8.4–27.6 px.

Switched to `Z.MicroMinMax`, which carries its own label inside the track and therefore costs exactly `ControlWidth` and needs no `Z.Field` at all. Two reasons, neither of them a number picked by an agent: the reflected bounded **scalar** beside it is already a `Z.MicroSlider`, and `ui-layout-rules` names MicroMinMax the preferred pair control ("prefer this one"), keeping `Z.MinMax` for where typed precision outweighs footprint — which a form dial with a declared `[Range]` is not. Re-read after: nine `ZuiMicroMinMax` controls at 150 px, **two per row** where each used to claim a full overflowing row, each with its `[Tooltip]` intact. Scope: 13 reflected `[Range] Vector2` fields exist in `Runtime/**`.

### 3.3 BackSplash's `Colour` + `Image` row clips the sprite picker at a 320 px pane — FIXED

Colour 137.3 + Image 167.1 + the row's 6.7 px gap = 311.1 inside a 292.0 px content box, i.e. whenever the host's settings pane sits at the minimum a `Z.Split` allows (Shaper's right pane at 4 columns). Both fields carry explicit widths and `.zui-field` is `flex-shrink: 0`, so nothing in the row could give. That one row now wraps — `flex-wrap` engages only on overflow, so it costs nothing at any width that already fits, and this is a static settings row rather than a contextual toolbar, so folding it moves no workspace under the pointer.

### 3.4 T-0311 — the `★` Library button clipped its glyph — FIXED

A 22 px slot, less the button's default 6+6 padding and 1+1 border, left **8.0 px of content for a 12.9 px glyph** — in every gradient in every tool. Zeroed the horizontal padding rather than widening the slot, the same call the dirty dot got in T-0307: the button sits on the ramp's Output row beside a flex-grow preview strip, so 22 px is the width the row was designed around. Re-read after: **content 20.4 px for a 12.9 px glyph, "fits", in both windows.** The identical 22 px `★` in `ZuiRampControl` (the standalone `IZuiRamp` path) got the same treatment.

The five sub-1.5 px shortfalls the card lists (`Position jitter` 0.9, `Shape` 1.3, the layer-row `●` select button 1.3, `Life (frames)` / `Ember size` / `Colour ramp` 0.9) are **below the device-pixel tolerance**, and the library applies that tolerance (`ZTOL = 1.5`) uniformly to every caption measurement rather than leaving each caller to remember it. None of them appears in any sweep above; no width was changed for them.

### 3.5 T-0309 — the asset toolbar's name field — FIXED

Unity draws an ObjectField as `Name (Type)`, and `ShaperDemoDoc (Shaper Document)` measured **210.2 px against the 161.8 px** the 200 px field leaves its label, so any name past ~14 characters was drawn cut off, in every Laubrary asset window. The card's constraint held: the row is `NoWrap` and its rightmost child ends 10.2 px from the edge at the declared 820 px minimum. But the row also ended in a `Z.Flexible()` spacer holding **197.3 px of spare** at that same width — the slack is at the end of the row, not missing from it. Three changes, all of them removals or re-defaults:

1. **The `(Type)` suffix is dropped.** Every asset the window can hold is a `T`; the window itself is the type label, so the parenthetical was 110 px of redundancy in the one place the row has none to spare.
2. **The field sizes to its content**, between a 200 px floor (`minWidth`, so the empty state is unchanged) and a `maxWidth` of exactly what the name needs — so it can never grow past the name it is showing. The max-width write is gated by the same 1.5 px absolute tolerance `StampFieldHeight` uses, and for the same reason (T-0304's layout-struggle loop).
3. **The trailing `Z.Flexible()` is gone.** It only held the row's tail — a Row already leaves its leftover at the end — but it took an equal share of the spare width away from the field.

Measured after, in **both** Shaper and Pyre:

| case | window | field | name needs | name has | rightmost child | slack |
|---|---|---|---|---|---|---|
| `ShaperDemoDoc` (13 ch) | 820 | 200.0 | 96.0 | 161.8 | 809.8 | 10.2 |
| `AuditT0312` (10 ch, Pyre) | 820 / 1600 | 200.0 | 65.3 | 161.8 | 809.8 / 1589.8 | 10.2 |
| 49-character name | 820 | 347.6 | 307.1 | **308.9** | 753.8 | **66.2** |
| 49-character name | 1600 | 347.6 | 307.1 | **308.9** | 753.8 | 846.2 |
| 113-character name (the pathological case) | 820 | 403.6 | 708.0 | 365.3 | **809.8** | **10.2** |

The last row is the failure mode T-0309 warned about, and it does not happen: `flex-grow` distributes only free space, so the field stops at the row's edge — `Delete` still ends at 809.8 with the same 10.2 px it always had. A 113-character name is still cut at an 820 px window, but it now shows 365 px of itself instead of 161.8.

## 4. Stability — the layout-struggle sweep, re-run

Every change above writes geometry (a max-width, a wrap, a shrink), which is exactly the shape of defect T-0304 chased: a size write that depends on the size it produced re-lays out forever and floods the console while the editor idles. Re-run with the same method — set the width, rebuild, clear the console, idle, `LogEntries.GetCountsByType`:

| window | widths | idle | errors |
|---|---|---|---|
| Shaper, dial pane 360 → 1400, step 80 (14 widths), demo doc layer 1, all sections on | 2 s each | **0 at every width** |
| Pyre, window 820 → 1700, step 110 (9 widths), Torch layer | 2 s each | **0 at every width** |

(Pyre has no `Z.Split` at all — its dial pane is a fixed 360 px `ScrollView` — so `ZUI.Split.pyre.window.split.v1` is inert there and the window width is the only thing that moves. That is recorded in the README so the next pass does not sweep a pref that does nothing.)

## 5. Files changed

Six, all editor/toolkit, **no Pyre runtime or form file, no `CHANGELOG.md`, no `CLAUDE.md`, not committed** (programme rule 3):

- `Assets/Packages/Laubrary/Zui/Toolkit/Zui.cs` — `Z.Field` lets a gradient/ramp strip's field shrink
- `Assets/Packages/Laubrary/Zui/Toolkit/ZuiReflect.cs` — reflected `[Range] Vector2` → `Z.MicroMinMax`
- `Assets/Packages/Laubrary/Zui/Toolkit/ZuiGradientEditor.cs` — the `★` Library button's padding
- `Assets/Packages/Laubrary/Zui/Toolkit/ZuiRampControl.cs` — the same `★`, standalone ramp path
- `Assets/Packages/Laubrary/Editor/AssetKit/ZuiAssetWindow.cs` — the asset name field (T-0309)
- `Assets/Packages/Laubrary/Editor/BackSplash/BackSplashZui.cs` — the `Colour` + `Image` row wraps

Compile: `recompile_status` **completed, `failed=false`, `errors=[]`** after the final edit.

## 6. State left behind

`Assets/Shaper` and `Assets/Pyre` are **byte-identical to the session-start baseline** — 36 files, every SHA-256 prefix matching `out/p0-base.txt` against `out/z-clean.txt`, nothing added, removed or changed. The three scratch assets this task created (`Assets/Pyre/AuditT0312.asset` and two long-named Shaper documents) were deleted with their `.meta`s through `AssetDatabase.DeleteAsset`. Every `T0312.*` pref deleted; both `ZUI.Split.*` prefs deleted (neither had a value at session start); `ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0` with `barMode` true; `Shaper.lastView` deleted; `Undo.ClearAll()`; console cleared; both tool windows closed (neither was open when this task started). The editor reports the same **10 pre-existing dirty shaders and nothing else**, and `scriptCompilationFailed = False`. The demo document was `dirty=False` before binding and after every one of the twelve rebuilds; nothing was ever saved to it. Play mode was never entered and the Test Runner was never run.

**One thing to look at that this task did not cause.** `git status` also shows `Assets/Demos/TextSplashDemo/Border Fonts/Splash Demo (LiberationSans SDF) Border Font.asset` modified (61 insertions / 15 deletions — a TMP dynamic-font atlas gaining glyphs). It was not in the working copy's modified set when this task began and no code in this task touches it; it is Unity populating a dynamic font atlas while the editor rendered. Flagged rather than reverted.

## 7. Verified how

**By probe, in the live editor:** every count in §2, every measurement in §3 (before and after), the toolbar table in §3.5 including the 113-character pathological case, the layout-error sweeps in §4, the compile, and the byte-identical asset baseline in §6.

**By eye:** nothing. No human and no screenshot has looked at any of these windows this pass. T-0307's archive note 4 still holds — `PrintWindow` returns a blank client area for these UI-Toolkit windows and a `CopyFromScreen` catches whatever the owner has on top — so editor-window screenshots remain a non-working verification channel here, and forcing a window to the foreground would have stolen focus on the owner's live desktop.

**Not verified:** (a) the `ZuiReflect` and `Z.Field` changes are systemic — they were measured in Shaper and Pyre, the two windows this task was scoped to, but every other tool that reflects a `[Range] Vector2` or draws a gradient field (Chunks, Cartographer, Larder, Zoetrope…) inherits them unmeasured; the change compiles and is class-detected, but no other window was opened. (b) Nobody has dragged a `ZuiMicroMinMax` handle on a Torch dial or clicked the `★` with a real mouse. (c) The wrapped BackSplash row was measured as no-longer-overflowing, not seen folding.
