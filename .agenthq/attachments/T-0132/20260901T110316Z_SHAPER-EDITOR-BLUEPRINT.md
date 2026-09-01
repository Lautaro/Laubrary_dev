# Shaper editor — implementation blueprint (2026-09-01)

Produced by the PM session's own handover walk (T-0132) over the mock built by T-0130 (first vertical slice) and T-0131 (coordinator scope), against the plan in `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0126\SHAPER-UI-VISION-AND-DESIGN.md`. Mirrors the format of `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0123\CHUNKS-EDITOR-BLUEPRINT.md`. Where Chunks' blueprint recorded what a walked mock proved, this one does the same for Shaper: everything below marked DECIDED was actually driven — via the real code paths (`RebuildNodeBody`, `DrillInto`, `RefreshBreadcrumb`, the real add-effect menu, the real combine-mode handler), not by reading the design doc a second time — and confirmed either by a structural check (a `VisualElement` child-count/type dump proving a section was never built, not just visually hidden) or by a screenshot.

## 1. What the mock is, and where it lives

`D:\UNITY\Laubrary Dev\Assets\ShaperMock\Editor\` (`ShaperMockWindow.cs`, `ShaperMockData.cs`, `ShaperMockFillControl.cs`, `ShaperMockPreviewStage.cs`, `ShaperMockEffects.cs`), assembly `ShaperMock.Editor`, menu `Laubrary/Shaper Mock (Prototype)`. Mock data only — no production `ShaperNode`/`ShaperFillDef`/etc. is named anywhere in this assembly, per the T-0129 ground rule. Committed `8d9c7ba6` (vertical slice) and `f5e5101a` (coordinator scope) on `feat/lathe`. Two real ZUI additions landed as part of this: `ZuiBreadcrumb`/`Z.Breadcrumb` in shared `Assets/Packages/Laubrary/Zui/Toolkit/` (reusable by any future Laubrary tool), and a Shaper-native fill control kept mock-scoped (`ShaperMockFillControl`) since its final shape vs. a generalised `Z.Fill<T>` is still an open owner call (§8.2 below).

## 2. Layout — DECIDED, confirmed by direct measurement

`Z.Split` left-fixed/right-preview, exactly per design-doc §B1. Left pane: Canvas → Light Rig → Layers (built directly into the window's root, never inside the node-scoped container) → the current node's own card stack (Shape → [Fill] → [Border] → Swarm → Lighting response → [Effects]). Right pane: breadcrumb bar → live preview → transport (only when `frameCount > 1`).

**Window minimum size, measured, not guessed (resolves design-doc §J4.1):** the mock ships `minSize = (820, 520)`. First measurement attempt gave a false "clipped" reading because setting `EditorWindow.position` alone doesn't force a real UI Toolkit relayout in this Unity version; pinning `minSize = maxSize = target` *before* setting `position` gives a trustworthy result. Re-measured cleanly: at 820×520 the Canvas/Light Rig content (including the light rig's two-per-row dial blocks) renders with no clipping and no overlap. **Not separately re-measured at 820×520:** the widest single row across the whole tool (candidates: the IndexedStrip slot editor, a 3-segment-deep breadcrumb) — a quick manual check of those specific rows is worth doing once the real window is built, but nothing in this mock's testing suggests 820×520 is too narrow.

## 3. The node card — DECIDED, structurally verified

Confirmed by a live `VisualElement` child-count/type dump (not a screenshot — a screenshot can't prove a section was never *built*, only that it isn't visible) of `nodeBody` in three states:

| Node state | Children built | Matches design doc |
|---|---|---|
| Plain Primitive, root | Shape, Fill, Border(+Add), Swarm(+Enable), LightResponse | §B4 baseline |
| Bag member, `combineMode=Subtract`, not Composite | Shape, Border(+Add), Swarm(+Enable), LightResponse — **no Fill** | §B4/FC-3.3 — confirmed even after the member briefly held its own fill a moment earlier; switching to Subtract nulls it, matching the real combine-mode handler exactly |
| Root switched to Composite | Shape, Swarm(+Enable), LightResponse, Effects(list+Add) — **no Fill, no Border** | §B4/§B5 |

One clarification worth stating explicitly because it is easy to mis-remember from the design doc's prose alone: **Border's absence and Subtract's fill-exclusion are orthogonal rules.** Border is absent-by-default at every node regardless of combine mode (an opt-in "+ Add border" affordance); Subtract only ever suppresses Fill. A Subtract member with a border is a legitimate, expected state, not a contradiction.

## 4. Fill ownership and the nearest-ancestor note — DECIDED, confirmed

A fresh Bag member's `fill` starts `null` (`NewBagMember`) — confirmed live. The Fill section for such a node renders `"Painted by [ancestor]'s fill"` per §C3 rather than an empty section. Giving a node its own fill (`+ Add fill`) and later removing it (`✕ own fill`) both round-tripped correctly through `RebuildNodeBody`.

## 5. Breadcrumb and stable workspace — DECIDED, confirmed

Drilling into a Bag member via the real `DrillInto` method and popping back out via a breadcrumb-segment click both leave `document.canvas` and `document.lightRig.lights` byte-identical (measured: width/height/lights.Count unchanged across the whole drill-in/drill-out sequence) — Canvas/Light Rig/Layers are never rebuilt by node-scoped navigation, confirming §J2 holds in the actual build, not just by construction-by-inspection. `ZuiBreadcrumb` implements per-segment ellipsis via USS (`Overflow.Hidden` + `TextOverflow.Ellipsis`), confirmed in the control's own source, not re-derived from the design doc.

## 6. Effects add-menu — DECIDED, confirmed

Against a Composite node whose generator does not publish sheets, 2 of the mock's 7 catalog effects came back correctly greyed-with-reason ("needs edge distance, which this generator does not publish", "needs a heat map, which this generator does not publish") when queried through the exact same `NeedsSheetsUnmet` check the real add-menu uses — not a re-implementation of the check for testing purposes.

## 7. Requirements this mock does not (and was never meant to) satisfy

- Real Shaper data — every field here is a mock stand-in (a 7-generator composite catalog vs. the real 9; a 7-effect catalog vs. the real 41; an 8-preset HeightField picker vs. the real 245).
- Real caching — the cache-state strip animates from a simulated `EditorApplication.update` tick, not `ShaperFrameCache`.
- Colour-as-envelope (§H3's ZUI gap) — every colour field in the mock is a plain `Color`, matching the design doc's own "ship static-only in v1" posture. Not attempted here.
- The Wave-4 engine promotion (transform/sweep/shell/blend/primitive-geometry/swarm → `ZUIValue`) — the mock's own equivalent fields are plain floats/ints, matching the design doc's explicit fallback plan (§H2). When the real engine promotes these, the real window's rows upgrade from `Z.MicroSlider` to `Z.Value` with no layout change, same as the design doc already states.

## 8. Open for the owner — carried forward from the design doc, checked against what the mock resolved

1. ~~Left-pane minimum width and window minimum size~~ — **resolved this session**, see §2.
2. **`ZuiShaperFillControl` vs. a generalised `Z.Fill<T>`** — still open. The mock kept its fill control mock-scoped rather than force a shared-toolkit decision under exploration.
3. **Colour-as-envelope for Shaper fills/lights** — still open, not attempted (§7).
4. **Swarm native-eligibility dynamism** — still open. The mock's stand-in (`NativeSwarmAvailable(node) => node.kind == Composite`) is consistent with the design doc's own prediction that this is "almost certainly static," but the mock cannot confirm this against a real engine that doesn't have native swarm sources yet.
5. **Engine-promotion sequencing (before/during/after the real build)** — still open; explicitly the owner's call per the design doc, not re-litigated here.
6. **Whether the "stuck" (unhosted) effect needs a host before shipping** — still open; the mock's catalog is a 7-entry stand-in and doesn't model a stuck effect at all, so this mock provides no new evidence either way.

## 9. Traceability

| Finding / decision | Verified how | Where |
|---|---|---|
| Window min size 820×520 has no clipping | Live measurement (pinned `minSize=maxSize`, screenshot) | §2 |
| Absence rule holds for Primitive / Subtract-member / Composite | `VisualElement` child-count/type dump, three states | §3 |
| Border and Subtract are orthogonal | Live test: Subtract member with a border card still renders | §3 |
| Fill-ownership note renders for a fill-less node | Live: fresh Bag member, `fill == null`, note text confirmed | §4 |
| Canvas/Light Rig never rebuilt by breadcrumb drill | Live: object-level equality check across a full drill-in/out cycle | §5 |
| Breadcrumb segment ellipsis is real, not decorative | Source read of `ZuiBreadcrumb.cs` (`TextOverflow.Ellipsis`) | §5 |
| Add-effect menu greys with the real unmet-quantity reason | Live: `NeedsSheetsUnmet` invoked through the same code path as the menu, 2/7 blocked | §6 |
| No state leaks across a window close/reopen | Live: cold reopen, root back to `Primitive` default | (walk 2, T-0132 todo history) |

## 10. Verification state

Verified live by the PM session personally (not taken on faith from either building subagent), per this project's Handover Walk protocol: two cold passes from an empty document, driving the real private methods and the real add-menu/combine-mode logic via Coplay, with both screenshot and structural evidence. Not a substitute for a human clicking through it with a mouse — everything above was driven programmatically through the same code paths a click would hit, which proves the logic but not the actual pointer/hit-testing/hover experience. A first real mouse pass by the project owner is still worth doing before treating this blueprint as final.
