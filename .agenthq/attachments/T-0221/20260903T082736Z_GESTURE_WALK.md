# T-0221 — the owner's five gestures, walked at both sites

Every gesture below was driven by **real UITK pointer events** (`PointerDownEvent` / `PointerMoveEvent` / `PointerUpEvent` with the same `localPosition`, `button` and `clickCount` a mouse produces), dispatched at the control in a live, laid-out window — not by calling the data API behind it. Each step's effect was read back off the ramp and photographed.

Both sites were exercised in **my own** `ShaperWindow` instance on **my own** scratch document (`Assets/_T0221Scratch`, since deleted), so the owner's window and assets were never driven.

| # | Gesture | Hosted Pyre ramp (Orb ▸ Membrane) | Fill gradient (`ZuiGradient`) |
|---|---|---|---|
| a | add a stop by clicking the bar | double-click at t=0.25 → **8 → 9 stops** (`02`) | double-click at t=0.25 → **2 → 3 stops** (`09`) |
| b | drag a stop | stop 2: 0.25 → **0.56** (`03`) | stop 1: 0.25 → **0.60** (`10`) |
| c | click a stop, pick its colour inline | popover: colour swatch + eyedropper, `Pos 0.560`, `Remove stop` (`04`) | same popover (`11`) |
| d | reach 10+ stops | **14 stops** (`05`) | **14 stops** after the library apply (`12`, `14`) |
| e | ★ library, apply a saved gradient | saved the 14-stop ramp, applied it back: 14 → 14 (`06`, `07`) | applied the **same** entry: 11 → **14** (`13`, `14`) |

Screenshots are the numbered PNGs in this folder.

## The result worth looking at

`14-fill-gradient-library-applied.png`: a **14-stop palette saved from a Pyre ramp, applied whole into a Fill gradient**. Before this task that same trip was capped at 8 keys in two separate places (the library stored a `UnityEngine.Gradient`, and `ZuiGradient` was backed by one), so six stops were dropped in transit and the ramp came back visibly re-shaped. The output strip, the stop editor's 14 markers and the live preview all agree.

## Gesture vocabulary — stated plainly, because one differs from the brief's wording

- **Adding a stop is a DOUBLE-click on the bar**, not a single click. That is deliberate: it is the same vocabulary `ZuiEnvelope` uses for inserting a point, and ZUI's own rule is that the same gesture means the same thing across controls. A single click on the bar currently does nothing, so switching to single-click is available at no cost if the owner prefers it — say the word.
- The new stop **carries the colour the ramp already evaluates there**, so inserting never changes the picture — you get a handle on what was already being drawn.
- A drag is **clamped between its neighbours** (the Pyre drag asked for 0.60 and settled at 0.56 because the next stop sits there). That is the control refusing to reorder the list under the indices the UI is holding, not a stuck drag.
- The per-stop popover carries a colour field, a `Pos` MicroSlider and `Remove stop`. **There is no per-stop numeric field list anywhere** — the whole ramp is one row plus its marker lane, at 2 stops or at 14.

## Site parity, structurally

Queried from the live tree rather than inferred:

- Fill gradient: `ZuiSection / ZuiBox / ZuiFillControl / … / ZuiRampControl` over a **`ZuiGradient`**, `showLibrary: false` (its ★ sits on the gradient editor's own output row, so a gradient shows one ★, not two).
- Hosted Pyre Orb: `… / ZuiRampControl` over a **`PyreRamp`**, with its own ★.

Same class, same gestures, same library, one control.
