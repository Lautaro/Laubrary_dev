# Chunks — every-dial sweep (T-0216, 2026-09-03)

**Question:** does changing a dial change the picture?

Every control found by the coverage sweep was driven through its own handler and the stage's content compared before and after. **89 of the 121 drivable dials move the picture. 33 do not, and every one of those 33 is accounted for below — three were defects, and they are fixed.**

## How the picture was compared

The stage paints one thing: the geometry `ChunkPreviewSim` produces for an instant, plus the framing that scales it and the cue ticks on its clock strip. So the comparison hashes exactly that — every cone, path and guide (position, radius, angle, alpha, colour, shape, draw order, label), at **three instants across the recipe's clock** (15 %, 50 %, 85 %), plus the framing reach and every cue.

Three instants rather than one, on purpose: a dial that only shows late (a life curve's tail, a stagger's last point) is invisible in a single frame, and a sweep that looked at one frame would call it dead.

**Cross-checked against real pixels.** Eight controls were also driven with a `PrintWindow` capture of the stage before and after, and the two verdicts compared:

| control | content hash | stage pixels |
|---|---|---|
| Pyre Blast · Arc | moved | moved |
| Floor · Rest on floor | moved | moved |
| Debris Scatter · Visual | moved | moved |
| Tint · Mode | same | same |
| Tint · Strength | same | same |
| Pyre Blast · Rotation | same | same |
| Debris Scatter · Gravity | moved | **same** |
| Fragment Fracture · Alpha over life | moved | **same** |

Six agree exactly. The two that disagree disagree in the safe direction: the capture shows ONE instant (whatever the transport was sitting on), the hash samples three, so a change that only shows at another moment reads as "same" in the picture and "moved" in the hash. In eight samples the hash never said "same" where the pixels moved — which is the direction a sweep hunting dead dials has to be right about. Both captures are in `data/`.

## The three defects it found, all fixed

1. **Rest on floor did nothing.** The preview integrated the floor — bounce, friction, the settle threshold — but ignored the dial that says what happens *after* a piece settles. At runtime, Rest on floor OFF cuts a settled chunk's life to that instant plus a beat, so it is gone; the preview kept a floor full of debris the burst will have cleared. The preview now reports when a flight settled and drops the piece at the same moment `Chunk.Update` does.

2. **Spin was a dead dial in sampled + tumbling mode.** `DebrisScatter.Fire` reads Tumble speed OR Spin, never both — a tumbling sampled cut takes its rate from Tumble speed. The card showed both, so turning Spin in that mode did nothing and said nothing about why. Spin is now absent in that mode, exactly as the card's other mode branches are.

3. **Pixels/unit promised something it does not do.** The dial's tooltip said it set "a Sampled cut's world size". It does not: `Chunk.Init` normalises whatever sprite it is handed to the Size dial's world units, so a chunk is exactly as big as Size says whatever Pixels/unit is set to. Pixels/unit is the generated sprite's RESOLUTION — a crisper chunk, not a bigger one. The tooltip now says so. (The sweep flagged it as a dial that writes a field and never moves the picture; the picture was right and the tooltip was wrong.)

## The 30 that legitimately do not move the picture

**A schematic does not draw a chunk's pixels (12).** Debris Scatter's Visual mode, Pixels/unit, Sample px, Tumble shade, the whole Tint box (Mode, Colour, Strength, Edge px) and every field of a pixel modifier decide what one chunk LOOKS like. The stage draws a chunk as a small oriented square, and deliberately: the preview's job is where things go and when, and computing a real sampled cut means a texture read that cannot happen sixty times a second. `ChunkPreviewSim`'s own header states this for the fracture and the splash; it is the same trade for debris.

**A blast is drawn as a disc, and a disc has no orientation (5).** Pyre Blast's Rotation mode, its Fixed angle, its Random angle range, and Trajectory's Face velocity all decide which way a spawned blast points. Nothing in the schematic can show that today. *Improvement, not a defect: a short tick from each disc's centre would make all five readable at once — filed, not built.*

**A card's own name (9).** Every capability's Name field. It names the card and the timing lane; there is nothing for it to draw.

**Renaming a layer (1).** The Layer Plan's slot name is rewritten across every producer pointed at it, inside one undo step — but the slot's POSITION does not move, so neither the depth nor the colour of anything changes. Correct.

**Damage (1).** Hits' damage is a game's balance number. Only its radius has a schematic tell (design decision 3), and that one does move the picture.

**Conditional, and correct in the state tested (2).** A Pyre Blast's Seed and its Pattern seed change nothing while the blast has no alternates pool, no position jitter and a fixed scale — there is nothing random left for a seed to fix. Both move the picture the moment any of those is authored.

## What the sweep also confirmed

- Every dial's edit goes through the window's undo path — the sweep restores between controls and the state comes back byte-identical each time.
- Every one of the 177 controls has a tooltip.
- The 43 things the sweep classified as actions rather than dials (a card's ▲▼×, Add layer, Add cue, Add alternate, Add capability, Add modifier, the transport's buttons) write no recipe field by design.

## Data

`data/sweep_*.tsv` — one row per control per state, with the verdict and the fields written.
