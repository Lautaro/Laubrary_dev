# DotGen — quick manual

One page, written from actually driving the window (T-0231). DotGen makes a still picture out of areas and
dots: a **generator** owns an area; a **placement** fills that area with dots; **selectors** score those dots;
**mutators** move or remove them through a selector; **drawers** paint areas or cells. A generator can carry a
whole child generator on every surviving dot, which is how one thing becomes a composition.

## Open

`Laubrary/DotGen` — the tool's only menu item. It opens on whatever document you last had open.

## Browse, New, and the rest of the asset toolbar

The top row is the same shape every Laubrary asset window uses: an object field to jump straight to a
document, **New** (asks for a name, then creates it as the demonstration composition — you always start from
something, never a blank frame), **Browse** (a thumbnail grid of every DotGen document in the project),
**Duplicate**, **Rename**, **Delete** (once a document is open — Delete asks first, since file deletion cannot
be undone).

## The Hierarchy

A tree of generators. Each row shows its placement kind and how many dots it is making right now, so you can
tell where the weight of the picture is without clicking anything. The buttons below it act on whichever row is
selected: **+ Child generator** (sized for whatever the parent hands out — a full copy of the parent's area, or
the exact cell around the parent dot, depending on the parent's placement), **▲ / ▼** (reorder among siblings —
earlier siblings draw behind, later ones in front), **Duplicate** (copies the generator and everything below
it), **Delete**. The root generator can't be moved, duplicated or deleted — it's the one thing every document
has to keep.

## The Generator card

The selected generator's own area: enabled, Dot output (turns the dot MARKERS off without stopping the dots
from spawning children or feeding drawers), colour, dot size, shape, width/height, the nine-point anchor grid
(which point of the area sits on whatever it's attached to — Bottom anchoring is what lets an area grow
upward), rotation. A child generator gets an extra Attachment block: size relative to the parent's whole area or
to the exact cell around the parent dot, every Nth parent dot, spawn chance, instance limit.

## Placement, Selectors, Mutators, Drawers

Four sections below the Generator card, all following the same shape:

- **Placement** — one radio (Grid / Box Row / Radial Grid) plus that method's own dials. Switching methods
  **keeps** each one's settings — trying Box Row and going back to Grid does not erase your Grid.
- **Selectors** — a list of cards, each scoring the generator's dots from 0 to 1 (Margin: distance from the
  edge; Gradient: a directional ramp or wave; Random: a deterministic mask). Selectors have no drag handle —
  their order doesn't matter, since anything that reads one does so by picking its NAME.
- **Mutators** — an ordered list (Nudge, Warp field, Cull), each with a Selector picker at the top of its card
  (pick a name from this generator's own declared selectors, or "All dots"). Order matters here, so mutator
  cards have a drag grip.
- **Drawers** — an ordered list, today just Fill drawer: paints either whole generator areas or the cells
  around surviving dots. Painting cells reveals a Selector picker (Use: Selected keeps the cells the selector
  weighted high, Inverse keeps the rest) and the cell's own shape/size. The paint itself is a `Z.Fill` — a flat
  colour by default; right-click the swatch to switch it to a gradient.

Every card: fold caret, drag grip (mutators/drawers only), enable toggle (switching a card off keeps every
setting, just stops it acting), a rename-in-place name field, and a remove `×`. An add button below each list
names what a single-kind list adds (**+ Fill drawer**) or opens a small menu when there's more than one kind
(**+ Selector**, **+ Mutator**).

## Gizmos — seeing the process, not just the picture

The Frame section's **Gizmos** control: **Hovered** (default — move the pointer over a card and its overlay
appears on the picture; leave it and the overlay clears), **Selected** (the clicked card's overlay stays put),
**All** (every one of the selected generator's own overlays at once), **Off**. A hovered/selected card also
gets a cyan outline, so the overlay and the card that owns it read as one thing. None of this touches the
picture itself — gizmos live only in the editor preview, never in the render or the PNG export.

## The preview

Zoom 25–800%, mouse wheel toward the cursor, drag to pan, double-click or the **0** key to fit. **+ / − / =**
also zoom. The chrome row below the picture shows the current zoom, a **Fit** button, which gizmo mode is
active, and a legend of every generator with Dot output on (its colour and name). A **Preview backdrop** panel
below that lets you drop in a background colour or image, so a design can be judged against the surface it'll
actually sit on.

## Frame, Export, Reset

The Frame section owns document-wide settings: **Seed** (+ "New seed" to reroll), **Frame size** (the render
resolution, also the export size), Background / Guide grid / Frame edge colours, Gizmos mode, and a readout —
"`N` dots · `M` areas" — that always matches what's actually evaluated. **Export PNG…** writes the current
picture to a file you choose, at exactly the frame size shown; a chosen path inside `Assets/` gets imported
automatically. **Reset to demo** asks first, then replaces the whole hierarchy with the documented
demonstration composition — useful for starting over without leaving the document.

## Undo

Every edit — a slider drag, a toggle, adding or removing a card, reordering, renaming — is one `Ctrl+Z` step.
Which generator is selected is not itself an undo step (so clicking around the tree doesn't fill up your undo
history), but everything that changes the document is.
