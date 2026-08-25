# Chunks — UI tidy-up pass (2026-08-24)

The pass the previous handover deferred as "known unfixed UI drift: on/off switches stretch full width — pre-existing, shared with the older sections, wants a whole-window pass." Audited every Chunks editor surface against the canonical rulebook (`~/.claude/skills/laubrary/references/ui-layout-rules.md`), fixed 32 findings, and verified the result by measurement and by eye.

## The headline: the stretched switch was a ZUI bug, not a Chunks bug

`Z.Toggle` resolves to `ZuiToggleButton`, which is a `Button`. `ZuiToolkit.uss` already solved cross-axis stretching for the native `.unity-toggle` — comment and all, at lines 44-55 — but when `Z.Toggle` was rerouted to the new button-style control, that guard was left behind. `flex-shrink: 0` was set; `align-self` was not. In any column body (a `ZuiSection`/`ZuiBox` body) the control therefore inherited `align-items: stretch` and filled the pane.

Fixed centrally in `Assets/Packages/Laubrary/Zui/Toolkit/ZuiToolkit.uss` with three selectors: `align-self: flex-start` on `.zui-togglebutton`, `align-self: center` for the row/field/hgroup/box-titlerow/section-header contexts where the cross axis is height, and `align-self: stretch` restored for `.zui-menu`, where a full-width row is deliberate.

This was fixed in the wrapper rather than at ~73 call sites because the rulebook requires it, and because the alternative — a hard `.W(150f)` per site — would have to be re-guessed per label and would clip long ones.

**Blast radius, deliberately project-wide:** 133 `Z.Toggle`/`Z.ToggleButton` call sites across ~15 tools; roughly 73 sit in a column and are now content-sized. Two of those sites are systemic multipliers — `ZuiReflect.cs` and `ZuiSerialized.cs` render every `bool` of every reflected modifier in Pyre, Chunks, Tapestry, SpriteFx and Lathe — so the real number of healed controls is far higher. Pyre was checked before and after and is unchanged except for the improvement.

`ZuiAudit` could not see this class of defect (its stretch check requires `unity-base-field`, which a `Button` never carries), which is why it survived. It now flags a `zui-togglebutton` that fills a column parent, testing `align-self` for both `Stretch` and `Auto` — an inherited `align-items` resolves the child to `Auto`, so checking `Stretch` alone would never fire.

## What else changed, by file

| File | Change |
|---|---|
| `Editor/Chunks/ChunkWindow.cs` | 7 min/max pairs → one `Z.MinMax` range slider each (Count, Speed, Spin, Life, Size, Sample Px, Tumble). `Direction °` → 0-360 `Z.MicroSlider`. "Use Floor" and Hit Detection's "Enabled" → section HEADER toggles, matching every 2.0 module. `Z.Box("Tint")` → `Z.BoxKeyed`. Three row merges. |
| `Editor/Chunks/ChunkWindow.Splash.cs` | `Direction °` → 0-360 MicroSlider. Four full-width rows packed into two. |
| `Editor/Chunks/ChunkWindow.Slicer.cs` | Same angle fix; Speed/Spin and Life/Gravity/Drag packed into two rows. State-composed tooltip on "Burst Aim". |
| `Editor/Chunks/ChunkWindow.PyreMotion.cs` | `Z.HSpace()` between Upward Bias and Gravity, matching the file's own convention. |
| `Editor/Chunks/ChunkWindow.Modifiers.cs`, `.Layers.cs` | Three titled `Z.Box` → `Z.BoxKeyed` (`chunks.sampled.modifiers`, `chunks.layers.slots`, `chunks.layers.modules`). |
| `Editor/Chunks/ChunkSpecEditor.cs` | `InspectorElement.FillDefaultInspector` removed — it was rendering the whole spec as native checkboxes, bare fields and enum dropdowns. Verified first that all 51 serialized members are reachable in the window; none were orphaned. Reserved-space status line + preview slot switched by `visibility`, not `display`, so nothing reflows. Button width set. |
| `Editor/Chunks/ChunkFollowEmitterEditor.cs` | The explanatory tooltip on the disabled "Splash Follows Burst" indicator moved onto an enabled row wrapper — a disabled UITK element does not reliably receive the pointer events a tooltip resolves from, so the text was unreachable. |
| `Runtime/Chunks/ChunkSpec.cs` | `RenderPreviewTexture` no longer returns null for a spec with no authored art. A procedural spec's look *is* its `colorOverLife` gradient, so it now draws a deterministic scatter of tinted squares. Six of seven chunk assets previously rendered as blank squares in the browser and in the inspector's preview slot. |
| `Zui/Toolkit/ZuiShapeBrowser.cs` | Filter bar carries the `zui-row` class rather than only the inline equivalent, so the sheet's row exemption keeps its toggles vertically centred. |

## Ranges

Every `Z.MinMax` bound was checked against the authored values in all 7 `ChunkSpec` assets so that no existing value is clamped: Count 0-64 (authored 0-22), Speed 0-20 (2.5-9), Spin 0-720 (90-720, top value sits exactly on the cap and stays reachable), Life 0.05-8 (0.5-2.0), Size 0.01-2 world units (0.05-0.6), Sample Px 1-64 (2-24), Tumble 0-720 (180-720). Where a sibling section already bounded the same kind of value, that bound was reused rather than a new one invented.

## Verification

- Editor compiles clean, confirmed by `EditorUtility.scriptCompilationFailed` **plus** probing for a newly added symbol — the console buffer is cumulative and reports a stale pass otherwise.
- **Measured, not eyeballed:** every `zui-togglebutton` in the laid-out Chunks window is 94-141px inside a 1079px pane, 0 stretched, resolving `FlexStart`; the one toggle inside a row resolves `Center`. Pyre: 9 toggles, 0 stretched, in-row ones still `Center`.
- Screenshotted before/after via `EnumWindows` + `PrintWindow(PW_RENDERFULLCONTENT)`: every section, the browser, the all-modules-enabled state, the empty state, and the inspector.
- UI-guide compliance sweep over all 14 Chunks editor files: no `EditorGUILayout`/`GUILayout`, no `Z.EnumDropdown`, no raw UITK `Toggle`/`Slider`/`EnumField`, no leftover min/max pairs. The single `EditorGUI.DrawRect` is inside the Formation preview's `IMGUIContainer` — the sanctioned bespoke-canvas island.

**Not verified:** nobody has driven this with a mouse. Hover-tooltip behaviour on the disabled Follow-Emitter indicator is a reasoned fix, not an observed one — hover it with Follow Burst Direction off to confirm the long "OFF:" text actually surfaces. `ZuiAudit` reports 0 findings but `foldedSkipped` cannot reach 0 in this window, because conditional UI legitimately uses `display: none`; the toggle geometry was measured directly for that reason.
