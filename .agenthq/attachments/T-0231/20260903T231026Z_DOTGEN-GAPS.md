# DotGen — gaps found while authoring the demo (T-0231)

First eyes-on the module cards (W2.2, T-0229) and the gizmo overlays (W2.3, T-0230) — both were built and
committed code-only, with no editor available to their authors. This is what a real render turned up.
Nothing here blocked authoring either demo asset; both are complete and correct. No code was changed.

## 1. A card's rename-in-place name field clips the last character of a long default name

**Seen three times, independently, on three different card kinds:**
- Radial Clusters' Gradient selector, authored name "Light sweep" → renders **"Light swee"**.
- Diamond Grids' Random selector, authored name "Broken cells" → renders **"Broken cell"**.
- Windows' Margin selector, default instance name "Edge margin" → renders **"Edge margi"**.

Screenshots: `workspace/T-0231/02_dotgen-demo-scrolled-radial-cards.png` (Selectors section, top card),
`03_dotgen-demo-diamond-grid-random-cards.png` (Selectors section), `07_windows-margin-selector-and-fill.png`
(Selectors section).

The card header's `Z.TextInput` for the module name appears to be one character narrower than the text it is
given at these lengths — every case clips exactly the LAST character, at the pane's default 1400 px window
width / 360 px left-pane column. Shorter names ("Gather", "Fade out", "Cull") render in full. This is cosmetic
(the underlying `DotModule.name` field is correct — confirmed by reading `margin.name`/`drawer.name` etc. from
the document in the driving script; only the on-screen field is short). Did not block authoring since the full
name is readable elsewhere (the Selector-picker rows show it uncut — see the Fill drawer card in the same
screenshots, "Edge margin" reads correctly there). Left unfixed as out of this task's scope (code-only demo
task with editor rights, not a mandate to patch W2.2's cards); flagging for whoever picks up the card polish
pass. A one- or two-pixel width bump on that TextInput, or trimming its internal padding, would likely fix it.

## 2. A ZuiMenu popover opened via a scripted click needs a scripted close too

Not a product bug — a note for whoever else drives this window from a script. Clicking "+ Selector" through a
reflected `Button.clickable` delegate (the technique this task used throughout to drive real controls headlessly)
opens the real `ZuiMenu` popover exactly as a mouse click would. If the popover's own item is then bypassed (e.g.
by calling the window's `AddSelector(...)` directly instead of clicking the popover's "Margin selector" row), the
popover is never told to close — real Item clicks call `close()` internally, and this task's shortcut skipped
that. It showed up in one intermediate screenshot as a stray dropdown overlaying the Generator card. Not a defect
in `ZuiMenu`/`ZuiPopover` (`ZuiPopover.Show()` returns a closeable handle exactly for this); resolved for the
final screenshots by removing the stray popover element and confirmed it touched no document data. Worth
remembering: click the popover's own item, not a shortcut past it, next time a script needs one clean pass.

## 3. `Z.Fill`'s Flat↔Gradient switch is a right-click context menu, not reachable by this task's click-driver

`DotGenCardsFill.cs`'s own tooltip says "Right-click the swatch to switch it between a flat colour and a
gradient." Every other control this task drove (buttons, MiniRadio options, toggles, sliders, the anchor grid)
has a synthesizable commit path — a bound click delegate or a `SetValue`-style method reachable by reflection. A
right-click context menu on a swatch does not have an equivalent the driver could reach without simulating a real
native context-menu popup, which is a different mechanism from everything else in the window. Not treated as a
missing affordance — the control exists and works, this is a driving-technique gap, not a UI gap. Both
demonstration Fill drawers were left on Flat (`ZuiFill.Mode.Solid`), which is one of the three valid choices the
POC's step 9 names ("Select Flat, Gradient, or Random from list") — the requirement was met by the default, not
worked around.
