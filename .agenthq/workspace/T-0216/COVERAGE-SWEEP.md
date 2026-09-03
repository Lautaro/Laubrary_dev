# Chunks — coverage sweep (T-0216, 2026-09-03)

**Question:** does every authored field of a chunk recipe have a control in the Chunks window that edits it?

**Answer: 153 of 156 do — 98.1 %.** Three fields have no control anywhere; the rest are all reachable, and 140 of them were driven through the window's own controls to prove it.

## How this was measured — by mutation, not by matching labels

A coverage report that pairs a field with a control by reading their names proves nothing: the pairing is the reader's guess, and a field whose control was never written simply gets guessed at too. So this sweep never guesses. It:

1. reflects every serialized field of `ChunkSpec` and of all nine capability classes into a flat path → value map (the v0 layout, kept on `ChunkSpec` for one release and marked `[HideInInspector]`, is excluded — it is read only by the upgrade and is authored nowhere);
2. enumerates every control in the window's Recipe and Preview sections by walking the real element tree;
3. for each control: restores the recipe to a known state, **drives that control through its own handler** (a press-and-release across a slider's track, a submit on a toggle or a radio segment, a value change on a numeric field), and diffs the map.

Whatever moved is what that control edits. A field nothing moves is a miss, and it cannot be missed by a mis-guessed label.

Two things this forced into the open that a label-matching pass would not have found:

- **A `ZuiBox` proxies its indexer to its content container**, so walking a card by index skips its whole HEADER. The first version of this sweep could not see a single card's **On** toggle, its reorder arrows or its remove button, and reported a clean pass anyway. Walking `hierarchy` instead added 51 controls.
- **Several dials exist only inside a branch.** Debris Scatter shows different fields per Visual mode, and its sampled block shows nothing at all until a Sample source is assigned; a Pyre Blast's formation dials appear only for Line/Ring; "inherit"/"until" switches hide the dial they replace. The sweep therefore runs **ten times**, once per state, and unions the results:

| state | what it sets up | controls |
|---|---|---|
| `base` | Squares, Single, every inherit on | 150 |
| `sprites` | Visual = Sprites (the bound sprite list) | 150 |
| `animated` | Visual = Animated (the animation picker) | 150 |
| `sampled` | Visual = Sampled + a real sample source + one pixel modifier | 176 |
| `sampled-edge` | as above, tint mode = Edges only (the Edge px dial) | 177 |
| `sampled-wide` | as above, values mid-range so both range handles must move | 177 |
| `line` | Pattern = Line | 158 |
| `ring` | Pattern = Ring | 159 |
| `branches` | every inherit off, fixed flight time, one cue | 163 |
| `fixed-angle` | Rotation = Fixed | 177 |

**177 distinct controls** were seen across the ten states. The recipe under test is a duplicate of the Floating Disc Blowup demo, grown to hold **all nine capability kinds**, each added through the window's own `Add capability…` menu by a real pointer press on the menu row. No user asset was written; the demo recipes were opened read-only and read back not dirty.

## The result

- **Universe:** 156 authored serialized paths (list `.Count` bookkeeping and the preview backdrop's own chrome excluded).
- **Driven through a control:** 140 (89.7 %).
- **Reachable but not drivable by this harness:** 13.
- **No control at all:** 3.

### The three real gaps

`LayerPlan.layers.baseOrder`, `.step` and `.sortingLayerName` — the Layer Plan's own `LayerSpec` carries the number the first slot starts at, the gap between slots, and the Unity Sorting Layer the whole effect lives in. The card offers none of them. It was built that way on purpose (design decision 10: the Layer Plan card shows name, reorder and remove only), and at the time that was harmless. **It is not harmless any more.** §8.16 made the runtime's own numbers the truth for draw order, and those numbers are `baseOrder + index × step` for a slotted output against `emitter.sortingOrder + stack index` for an unslotted one — so `baseOrder` and `step` now decide whether unslotted output draws in front of the plan or behind it, and there is no way to author them. Filed rather than built, because adding them contradicts an explicit line of the spec.

### Reachable, not drivable here (13)

| what | why |
|---|---|
| Fragment Fracture **Source** and **Fallback sprite**, Palette Splash **Sprite**, Pyre Blast **Blast**, Trail **Trail source**, Debris Scatter **Animation** and **Sample source** (7) | each is a real picker on screen; driving one means completing an asset-browser pick, which this harness does not synthesize |
| Debris Scatter **Sprites** (the bound list) | a `PropertyField`, edited through Unity's own list UI |
| Cues **zoundName** | the Zound picker is present and correctly shows itself unavailable — this project contains no Zound to pick |
| **delay** on Trajectory, Trail, Hits, Layer Plan and Cues (5, counted above in the 16 not driven) | deliberately absent: a modifier acts on its target's timing and a coordinator has no moment, so no Delay dial is drawn (design decisions 11 and 15) |

## What else this pass established

- **Every one of the 177 controls carries a tooltip** — zero `tooltip-missing`, on the control or an ancestor.
- **`ZuiAudit` is clean at 1000×720 and at 820×480**, with every card expanded: 0 findings at both sizes, and no horizontal scrollbar at either.
- `foldedSkipped` is **31**, not 0. It cannot be 0: every one of the 31 is a control's own hidden internal — 23 `ZuiMicroSlider` optional numeric-input fields (hidden until a user turns one on from the slider's right-click menu), 4 `ColorField` gradient containers, 2 `ColorField` HDR labels, 2 Unity mixed-value labels. Not one is a card body or a section, so the clean result does cover the window's real content. The full enumeration is in the handover.

## Data

`data/sweep_*.tsv` — one row per control per state: card, kind, label, what was done to it, whether the picture changed, and which fields it wrote.
