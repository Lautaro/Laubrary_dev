T-0204 — X4 Layers section = Pyre's model

Verified this pass, on top of the code committed in `da4a13dd` (Shaper worktree, `feat/shaper`, HEAD `431fbd93`).

## Verified by probe (a temporary `T0204_LifetimeProbe.cs` inside `Editor/Shaper/`, deleted afterward; scratch document `Assets/Shaper/T0204_ProbeDoc.asset`, deleted afterward — `ShaperDemoDoc.asset` was never touched)

- `Application.dataPath` resolves under `D:/UNITY/Laubrary Dev - Shaper/Assets` (correct worktree). `check_compile_errors` clean before and after the probe.
- **Lifetime MicroMinMax, real pointer path, at the exact dead state:** a fresh layer's default full-range Lifetime (`low=0, high=23` on a 24-frame document) is the state `ZuiMicroMinMax`'s `Grab.Both` used to swallow. Synthesized `PointerDownEvent`/`PointerMoveEvent`/`PointerUpEvent` (via `PointerEventBase<T>.GetPooled(Event)`, public API, no reflection) at the band's dead centre and dragged toward the low edge: `before=(0,23) after=(3,12) moved=True`. Screenshot `T0204_02_lifetime_dragged.png` shows the row reading "3 – 12" and the preview frame 0 correctly rendering blank (frame 0 is now outside the layer's window) — confirms both the control and the renderer downstream agree.
- **Height toggle, real click:** synthesized `PointerDownEvent`/`PointerUpEvent` on the actual "Height" `Z.Toggle` button. `T0204_03_height_mask_on.png` shows it latched on and the "Height: Flat" card appearing directly below the row (absence rule working from a live click, not just data).
- **Mask toggle:** verified by setting `layer.mask.sourceLayerId` directly (menu-driven picker verified by code reading, not a live click — see "not verified" below) and rebuilding; `T0204_03_height_mask_on.png` shows "Mask" latched on and the "Mask: Layer B (Pyre)" card appearing.
- **Toggle bar roster:** every screenshot's top bar reads `Sections | Toggle Bar | Views | Canvas | Layers | Shape | Transform | Fill | Border | SpriteFX | Global SpriteFX | Swarm | Lights | Lighting | Tags` when a non-composite layer is selected — Tags and Views are present and clickable segments, alongside every other section.
- **Fill/Border follow the selection:** with the composite (hosted-Pyre) layer selected, `T0204_04_pyre_layer_selected.png`'s toggle bar reads `Views | Canvas | Layers | Shape | Transform | SpriteFX | Global SpriteFX | Swarm | Lights | Lighting | Tags` — Fill and Border are both ABSENT, matching the card (a composite node draws neither).
- **Hosted Pyre shape has no "Layer frames" dial:** the "Pyre Disc" card in `T0204_04_pyre_layer_selected.png` shows Fill/Adjust/Alpha/Size/Edge/Border/Position/Sweep/Shell — no frame-count control anywhere.
- **One lifetime, proven as data, not just an absent dial:** set the Shaper layer's own Lifetime to `(5, 18)` on the 24-frame document and rebuilt; read the HOSTED `PyreLayer`'s own fields back: `src.frames=24` (== document.frameCount), `src.layer.startFrame=5, src.layer.endFrame=18` (== the Shaper layer's own Lifetime). `framesMatches=True lifetimeMatches=True`.

## Verified by eye (screenshots, `T-0204/T0204_0{1..5}_*.png`)

- Layout: Lifetime + Z on one horizontal row, Height/Mask toggles immediately after, "Draws into the picture" toggle below — matches Pyre's own model and the owner's "should stack horizontally."
- No leftover "Layer frames" row anywhere in the hosted Pyre card.
- Toggle bar and card set stay in agreement across both a plain shape and a composite node.

## Not verified

- The Mask toggle's on-click → menu → pick-a-layer flow was exercised by directly writing `sourceLayerId` and rebuilding (proves the absence-rule render is correct), not by a live synthesized click through the floating `ZuiMenu` popup — driving a real click through a separate popup window reliably from a probe was judged not worth the added fragility for this pass.
- No second human/editor session cross-check beyond this one; only this task's own probe drove the window.
- Undo of any of these edits was not separately probed (the underlying `Change()`/`Undo.RegisterCompleteObjectUndo` plumbing is unchanged from the rest of the window, which is already Undo-safe).

## Files touched (commit `da4a13dd`)
- `Assets/Packages/Laubrary/Zui/Toolkit/ZuiMicroMinMax.cs`
- `Assets/Packages/Laubrary/Editor/Shaper/ShaperWindow.cs`
- `Assets/Packages/Laubrary/Editor/Shaper/ShaperWindow.Sections.cs`
- `Assets/Packages/Laubrary/Editor/Shaper/ShaperCompositeSourceUI.cs`
- `Assets/Packages/Laubrary/Editor/PyreShaper/PyreLayerShaperUI.cs`
- `Assets/Packages/Laubrary/Runtime/PyreShaper/PyreLayerCompositeSource.cs`
- `Assets/Packages/Laubrary/CHANGELOG.md`

(Note: an earlier slice of the same diff — the `fillSection`/`borderSection`/`responseSection` reset, `BuildCompositeBody`'s new context fields, `BuildHeightSection`'s placeholder removal — landed inside sibling task T-0203's commit `156e1aea` rather than mine, because the worktree is shared live across concurrent task agents with no staging isolation; content is correct and present in HEAD either way.)
