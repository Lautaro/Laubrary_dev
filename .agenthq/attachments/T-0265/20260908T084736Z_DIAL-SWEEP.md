# T-0265 — every dial x every node kind, on SAVED documents

Every control was perturbed on a document built the way the window builds it, written to disk,
re-imported (so Unity materialises the same serialized objects the owner's documents carry) and
re-loaded; frames 0, mid and last were rendered through `ShaperDocumentRenderer` and changed pixels
counted. Each control was measured in the STATE its own card shows it in — the box it lives in
switched on, the fill kind that owns it selected, its texture/gradient/figure precondition met, and
nothing else on. A second layer exists only as a mask source (`contributesToPicture = false`) so it
can never hide the node under test.

`sweep-before.tsv` / `sweep-after.tsv` carry every row: document, category, node kind, card, control,
type, measurement state, the value that moved it, and the changed-pixel counts at mid / 0 / last.

## Documents

| document | node kind | controls | the layer's own pixels | canvas |
|---|---|---:|---:|---|
| Rectangle (Primitives) | Primitive | 63 | 2124 | 96x64 |
| Ellipse (Primitives) | Primitive | 62 | 5896 | 96x64 |
| Diamond (Primitives) | Primitive | 62 | 4404 | 96x64 |
| Triangle (Primitives) | Primitive | 62 | 3264 | 96x64 |
| Pill (Primitives) | Primitive | 62 | 3832 | 96x64 |
| Polygon (Primitives) | Primitive | 64 | 5472 | 96x64 |
| Star (Primitives) | Primitive | 241 | 2752 | 96x64 |
| Image (Primitives) | Primitive | 65 | 0 | 96x64 |
| Text (Primitives) | Primitive | 65 | 909 | 96x64 |
| Box (Solids) | Solid | 78 | 2000 | 96x64 |
| Pyramid (Solids) | Solid | 254 | 900 | 96x64 |
| Can (Solids) | Solid | 78 | 1904 | 96x64 |
| Orb (Solids) | Solid | 78 | 1264 | 96x64 |
| Gem (Solids) | Solid | 78 | 564 | 96x64 |
| Ring (Solids) | Solid | 78 | 840 | 96x64 |
| Combine children (Bag) | Bag | 59 | 0 | 96x64 |
| Fork Blast (Explosions) | Composite | 123 | 4300 | 96x64 |
| Inferno (Explosions) | Composite | 123 | 4179 | 96x64 |
| Arc Burst (Kiln/Energy Explosion) | Composite | 447 | 1716 | 96x64 |
| Disc (Pyre) | Composite | 574 | 1340 | 96x64 |
| Gem (Pyre) | Composite | 398 | 1532 | 96x64 |
| Box (Pyre) | Composite | 398 | 3076 | 96x64 |
| Pyramid (Pyre) | Composite | 574 | 1584 | 96x64 |
| Can (Pyre) | Composite | 398 | 2588 | 96x64 |
| Orb (Pyre) | Composite | 398 | 1992 | 96x64 |
| Ring (Pyre) | Composite | 398 | 1768 | 96x64 |
| Crescent (Pyre) | Composite | 398 | 940 | 96x64 |
| Star (Pyre) | Composite | 574 | 462 | 96x64 |
| Polygon (Pyre) | Composite | 398 | 900 | 96x64 |
| Streak (Pyre) | Composite | 398 | 48 | 96x64 |
| Sparkle (Pyre) | Composite | 398 | 500 | 96x64 |
| Sprite (Pyre) | Composite | 398 | 1340 | 96x64 |
| Text (Pyre) | Composite | 398 | 620 | 96x64 |
| Fire (Pyre) | Composite | 398 | 75 | 96x64 |
| Fireball (Pyre) | Composite | 398 | 405 | 96x64 |
| Bag (2 members) (Bag) | Bag | 239 | 1218 | 96x64 |

**Not swept:** Plasma Bloom, Kiln Orb, Explosive Jet, Jet, Radial Jet, Torch, Fire and Fireball
(documents 19-26). Those generators cost seconds a frame even on a quarter canvas; the run was cut so
the structural findings could be fixed and re-measured inside this task. Their own dials are
unmeasured; every other card on them is the set measured on Fork Blast, Inferno and Arc Burst.

## Two documents read as all-zero and are not findings

**Image (Primitives)** has no Sprite assigned, and **Combine children (Bag)** is an empty bag: both layers draw
nothing at all (own pixels 0), so every control on them necessarily measures 0. The bag is covered instead by
**Bag (2 members)**, which has two real members. A Sprite primitive with a sprite assigned was not swept.

## Card x node kind — how many of the card's controls move the picture

| document | kind | card | before | after |
|---|---|---|---|---|
| Rectangle (Primitives) | Primitive | Swarm | 24/33 act | not re-run |
| Rectangle (Primitives) | Primitive | Sweep | 2/5 act | not re-run |
| Rectangle (Primitives) | Primitive | Layer | 4/5 act | not re-run |
| Rectangle (Primitives) | Primitive | Document | 1/2 act | not re-run |
| Ellipse (Primitives) | Primitive | Position | 8/9 act | not re-run |
| Ellipse (Primitives) | Primitive | Swarm | 20/33 act | not re-run |
| Ellipse (Primitives) | Primitive | Sweep | 2/5 act | not re-run |
| Ellipse (Primitives) | Primitive | Layer | 4/5 act | not re-run |
| Ellipse (Primitives) | Primitive | Document | 1/2 act | not re-run |
| Diamond (Primitives) | Primitive | Swarm | 23/33 act | not re-run |
| Diamond (Primitives) | Primitive | Sweep | 2/5 act | not re-run |
| Diamond (Primitives) | Primitive | Layer | 4/5 act | not re-run |
| Diamond (Primitives) | Primitive | Document | 1/2 act | not re-run |
| Triangle (Primitives) | Primitive | Swarm | 23/33 act | not re-run |
| Triangle (Primitives) | Primitive | Sweep | 2/5 act | not re-run |
| Triangle (Primitives) | Primitive | Layer | 4/5 act | not re-run |
| Triangle (Primitives) | Primitive | Document | 1/2 act | not re-run |
| Pill (Primitives) | Primitive | Swarm | 22/33 act | not re-run |
| Pill (Primitives) | Primitive | Sweep | 2/5 act | not re-run |
| Pill (Primitives) | Primitive | Layer | 4/5 act | not re-run |
| Pill (Primitives) | Primitive | Document | 1/2 act | not re-run |
| Polygon (Primitives) | Primitive | Swarm | 22/33 act | not re-run |
| Polygon (Primitives) | Primitive | Sweep | 2/5 act | not re-run |
| Polygon (Primitives) | Primitive | Layer | 4/5 act | not re-run |
| Polygon (Primitives) | Primitive | Document | 1/2 act | not re-run |
| Star (Primitives) | Primitive | Edge (border) | 45/72 act | 53/72 act |
| Star (Primitives) | Primitive | Fill | 42/68 act | 50/68 act |
| Star (Primitives) | Primitive | Layer/height | 5/9 act | 5/9 act |
| Star (Primitives) | Primitive | Layer/mask | 4/5 act | 4/5 act |
| Star (Primitives) | Primitive | Swarm | 23/33 act | 23/33 act |
| Star (Primitives) | Primitive | Sweep | 2/5 act | 2/5 act |
| Star (Primitives) | Primitive | Layer/lighting | 4/11 act | 4/11 act |
| Star (Primitives) | Primitive | Layer | 4/5 act | 4/5 act |
| Star (Primitives) | Primitive | Document | 1/2 act | 1/2 act |
| Image (Primitives) | Primitive | Fill | 0/2 act | not re-run |
| Image (Primitives) | Primitive | Position | 0/9 act | not re-run |
| Image (Primitives) | Primitive | Shell | 0/3 act | not re-run |
| Image (Primitives) | Primitive | Swarm | 0/33 act | not re-run |
| Image (Primitives) | Primitive | Sweep | 0/5 act | not re-run |
| Image (Primitives) | Primitive | Shape/primitive | 1/6 act | not re-run |
| Image (Primitives) | Primitive | Document | 1/2 act | not re-run |
| Image (Primitives) | Primitive | Layer | 0/5 act | not re-run |
| Text (Primitives) | Primitive | Swarm | 23/33 act | not re-run |
| Text (Primitives) | Primitive | Sweep | 2/5 act | not re-run |
| Text (Primitives) | Primitive | Shape/primitive | 4/6 act | not re-run |
| Text (Primitives) | Primitive | Document | 1/2 act | not re-run |
| Text (Primitives) | Primitive | Layer | 4/5 act | not re-run |
| Box (Solids) | Solid | Position | 0/9 act | 9/9 act |
| Box (Solids) | Solid | Shell | 0/3 act | 0/3 act |
| Box (Solids) | Solid | Swarm | 0/33 act | 0/33 act |
| Box (Solids) | Solid | Sweep | 0/5 act | 0/5 act |
| Box (Solids) | Solid | Shape/solid | 15/19 act | 15/19 act |
| Box (Solids) | Solid | Layer | 4/5 act | 4/5 act |
| Box (Solids) | Solid | Document | 1/2 act | 1/2 act |
| Pyramid (Solids) | Solid | Edge (border) | 0/72 act | 0/72 act |
| Pyramid (Solids) | Solid | Fill | 38/68 act | 49/68 act |
| Pyramid (Solids) | Solid | Layer/height | 0/9 act | 0/9 act |
| Pyramid (Solids) | Solid | Layer/mask | 3/5 act | 3/5 act |
| Pyramid (Solids) | Solid | Position | 0/9 act | 9/9 act |
| Pyramid (Solids) | Solid | Shell | 0/3 act | 0/3 act |
| Pyramid (Solids) | Solid | Swarm | 0/33 act | 0/33 act |
| Pyramid (Solids) | Solid | Sweep | 0/5 act | 0/5 act |
| Pyramid (Solids) | Solid | Layer/lighting | 6/11 act | 6/11 act |
| Pyramid (Solids) | Solid | Shape/solid | 15/19 act | 15/19 act |
| Pyramid (Solids) | Solid | Document | 1/2 act | 1/2 act |
| Pyramid (Solids) | Solid | Layer | 4/5 act | 4/5 act |
| Can (Solids) | Solid | Position | 0/9 act | 9/9 act |
| Can (Solids) | Solid | Shell | 0/3 act | 0/3 act |
| Can (Solids) | Solid | Swarm | 0/33 act | 0/33 act |
| Can (Solids) | Solid | Sweep | 0/5 act | 0/5 act |
| Can (Solids) | Solid | Shape/solid | 14/19 act | 14/19 act |
| Can (Solids) | Solid | Layer | 4/5 act | 4/5 act |
| Can (Solids) | Solid | Document | 1/2 act | 1/2 act |
| Orb (Solids) | Solid | Position | 0/9 act | 7/9 act |
| Orb (Solids) | Solid | Shell | 0/3 act | 0/3 act |
| Orb (Solids) | Solid | Swarm | 0/33 act | 0/33 act |
| Orb (Solids) | Solid | Sweep | 0/5 act | 0/5 act |
| Orb (Solids) | Solid | Shape/solid | 13/19 act | 13/19 act |
| Orb (Solids) | Solid | Layer | 4/5 act | 4/5 act |
| Orb (Solids) | Solid | Document | 1/2 act | 1/2 act |
| Gem (Solids) | Solid | Position | 0/9 act | 7/9 act |
| Gem (Solids) | Solid | Shell | 0/3 act | 0/3 act |
| Gem (Solids) | Solid | Swarm | 0/33 act | 0/33 act |
| Gem (Solids) | Solid | Sweep | 0/5 act | 0/5 act |
| Gem (Solids) | Solid | Shape/solid | 16/19 act | 16/19 act |
| Gem (Solids) | Solid | Layer | 4/5 act | 4/5 act |
| Gem (Solids) | Solid | Document | 1/2 act | 1/2 act |
| Ring (Solids) | Solid | Position | 0/9 act | 6/9 act |
| Ring (Solids) | Solid | Shell | 0/3 act | 0/3 act |
| Ring (Solids) | Solid | Swarm | 0/33 act | 0/33 act |
| Ring (Solids) | Solid | Sweep | 0/5 act | 0/5 act |
| Ring (Solids) | Solid | Shape/solid | 13/19 act | 13/19 act |
| Ring (Solids) | Solid | Layer | 4/5 act | 4/5 act |
| Ring (Solids) | Solid | Document | 1/2 act | 1/2 act |
| Combine children (Bag) | Bag | Fill | 0/2 act | not re-run |
| Combine children (Bag) | Bag | Position | 0/9 act | not re-run |
| Combine children (Bag) | Bag | Shell | 0/3 act | not re-run |
| Combine children (Bag) | Bag | Swarm | 0/33 act | not re-run |
| Combine children (Bag) | Bag | Sweep | 0/5 act | not re-run |
| Combine children (Bag) | Bag | Document | 1/2 act | not re-run |
| Combine children (Bag) | Bag | Layer | 0/5 act | not re-run |
| Fork Blast (Explosions) | Composite | Fill | 1/2 act | not re-run |
| Fork Blast (Explosions) | Composite | Swarm | 23/33 act | not re-run |
| Fork Blast (Explosions) | Composite | Sweep | 2/5 act | not re-run |
| Fork Blast (Explosions) | Composite | Shape/generator | 34/64 act | not re-run |
| Fork Blast (Explosions) | Composite | Layer | 4/5 act | not re-run |
| Fork Blast (Explosions) | Composite | Document | 1/2 act | not re-run |
| Inferno (Explosions) | Composite | Fill | 1/2 act | not re-run |
| Inferno (Explosions) | Composite | Swarm | 24/33 act | not re-run |
| Inferno (Explosions) | Composite | Sweep | 2/5 act | not re-run |
| Inferno (Explosions) | Composite | Shape/generator | 35/64 act | not re-run |
| Inferno (Explosions) | Composite | Layer | 4/5 act | not re-run |
| Inferno (Explosions) | Composite | Document | 1/2 act | not re-run |
| Arc Burst (Kiln/Energy Explosion) | Composite | Document/light | 0/13 act | 0/13 act |
| Arc Burst (Kiln/Energy Explosion) | Composite | Edge (border) | 0/72 act | 0/72 act |
| Arc Burst (Kiln/Energy Explosion) | Composite | Fill | 8/68 act | 8/68 act |
| Arc Burst (Kiln/Energy Explosion) | Composite | Layer/height | 0/9 act | 0/9 act |
| Arc Burst (Kiln/Energy Explosion) | Composite | Layer/mask | 3/5 act | 3/5 act |
| Arc Burst (Kiln/Energy Explosion) | Composite | Swarm | 24/33 act | 24/33 act |
| Arc Burst (Kiln/Energy Explosion) | Composite | Sweep | 2/5 act | 2/5 act |
| Arc Burst (Kiln/Energy Explosion) | Composite | Layer | 4/5 act | 4/5 act |
| Arc Burst (Kiln/Energy Explosion) | Composite | Layer/lighting | 0/11 act | 0/11 act |
| Arc Burst (Kiln/Energy Explosion) | Composite | Document | 1/2 act | 1/2 act |
| Arc Burst (Kiln/Energy Explosion) | Composite | Shape/generator | 33/212 act | 33/212 act |
| Disc (Pyre) | Composite | Document/light | 1/13 act | 1/13 act |
| Disc (Pyre) | Composite | Edge (border) | 0/72 act | 0/72 act |
| Disc (Pyre) | Composite | Fill | 8/68 act | 8/68 act |
| Disc (Pyre) | Composite | Layer/height | 0/9 act | 0/9 act |
| Disc (Pyre) | Composite | Layer/mask | 3/5 act | 3/5 act |
| Disc (Pyre) | Composite | Swarm | 24/33 act | 24/33 act |
| Disc (Pyre) | Composite | Sweep | 2/5 act | 2/5 act |
| Disc (Pyre) | Composite | Shape/generator | 182/339 act | 182/339 act |
| Disc (Pyre) | Composite | Layer/lighting | 0/11 act | 0/11 act |
| Disc (Pyre) | Composite | Document | 1/2 act | 1/2 act |
| Disc (Pyre) | Composite | Layer | 4/5 act | 4/5 act |
| Gem (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Gem (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Gem (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Gem (Pyre) | Composite | Shape/generator | 33/339 act | not re-run |
| Gem (Pyre) | Composite | Document | 1/2 act | not re-run |
| Gem (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Box (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Box (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Box (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Box (Pyre) | Composite | Shape/generator | 30/339 act | not re-run |
| Box (Pyre) | Composite | Document | 1/2 act | not re-run |
| Box (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Pyramid (Pyre) | Composite | Document/light | 0/13 act | not re-run |
| Pyramid (Pyre) | Composite | Edge (border) | 0/72 act | not re-run |
| Pyramid (Pyre) | Composite | Fill | 8/68 act | not re-run |
| Pyramid (Pyre) | Composite | Layer/height | 0/9 act | not re-run |
| Pyramid (Pyre) | Composite | Layer/mask | 3/5 act | not re-run |
| Pyramid (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Pyramid (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Pyramid (Pyre) | Composite | Shape/generator | 32/339 act | not re-run |
| Pyramid (Pyre) | Composite | Layer/lighting | 0/11 act | not re-run |
| Pyramid (Pyre) | Composite | Document | 1/2 act | not re-run |
| Pyramid (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Can (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Can (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Can (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Can (Pyre) | Composite | Shape/generator | 29/339 act | not re-run |
| Can (Pyre) | Composite | Document | 1/2 act | not re-run |
| Can (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Orb (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Orb (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Orb (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Orb (Pyre) | Composite | Shape/generator | 30/339 act | not re-run |
| Orb (Pyre) | Composite | Document | 1/2 act | not re-run |
| Orb (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Ring (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Ring (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Ring (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Ring (Pyre) | Composite | Shape/generator | 29/339 act | not re-run |
| Ring (Pyre) | Composite | Document | 1/2 act | not re-run |
| Ring (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Crescent (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Crescent (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Crescent (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Crescent (Pyre) | Composite | Shape/generator | 264/339 act | not re-run |
| Crescent (Pyre) | Composite | Document | 1/2 act | not re-run |
| Star (Pyre) | Composite | Document/light | 1/13 act | not re-run |
| Star (Pyre) | Composite | Edge (border) | 0/72 act | not re-run |
| Star (Pyre) | Composite | Fill | 8/68 act | not re-run |
| Star (Pyre) | Composite | Layer/height | 0/9 act | not re-run |
| Star (Pyre) | Composite | Layer/mask | 3/5 act | not re-run |
| Star (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Star (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Star (Pyre) | Composite | Shape/generator | 183/339 act | not re-run |
| Star (Pyre) | Composite | Layer/lighting | 0/11 act | not re-run |
| Star (Pyre) | Composite | Document | 1/2 act | not re-run |
| Star (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Polygon (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Polygon (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Polygon (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Polygon (Pyre) | Composite | Shape/generator | 185/339 act | not re-run |
| Polygon (Pyre) | Composite | Document | 1/2 act | not re-run |
| Polygon (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Streak (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Streak (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Streak (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Streak (Pyre) | Composite | Shape/generator | 188/339 act | not re-run |
| Streak (Pyre) | Composite | Document | 1/2 act | not re-run |
| Streak (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Sparkle (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Sparkle (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Sparkle (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Sparkle (Pyre) | Composite | Shape/generator | 16/339 act | not re-run |
| Sparkle (Pyre) | Composite | Document | 1/2 act | not re-run |
| Sparkle (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Sprite (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Sprite (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Sprite (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Sprite (Pyre) | Composite | Shape/generator | 182/339 act | not re-run |
| Sprite (Pyre) | Composite | Document | 1/2 act | not re-run |
| Sprite (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Text (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Text (Pyre) | Composite | Swarm | 23/33 act | not re-run |
| Text (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Text (Pyre) | Composite | Shape/generator | 22/339 act | not re-run |
| Text (Pyre) | Composite | Document | 1/2 act | not re-run |
| Text (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Fire (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Fire (Pyre) | Composite | Swarm | 23/33 act | not re-run |
| Fire (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Fire (Pyre) | Composite | Shape/generator | 30/339 act | not re-run |
| Fire (Pyre) | Composite | Document | 1/2 act | not re-run |
| Fire (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Fireball (Pyre) | Composite | Fill | 1/2 act | not re-run |
| Fireball (Pyre) | Composite | Swarm | 24/33 act | not re-run |
| Fireball (Pyre) | Composite | Sweep | 2/5 act | not re-run |
| Fireball (Pyre) | Composite | Shape/generator | 15/339 act | not re-run |
| Fireball (Pyre) | Composite | Document | 1/2 act | not re-run |
| Fireball (Pyre) | Composite | Layer | 4/5 act | not re-run |
| Bag (2 members) (Bag) | Bag | Edge (border) | 53/72 act | 53/72 act |
| Bag (2 members) (Bag) | Bag | Fill | 0/68 act | 0/68 act |
| Bag (2 members) (Bag) | Bag | Layer/height | 5/9 act | 5/9 act |
| Bag (2 members) (Bag) | Bag | Layer/mask | 3/5 act | 3/5 act |
| Bag (2 members) (Bag) | Bag | Swarm | 23/33 act | 23/33 act |
| Bag (2 members) (Bag) | Bag | Sweep | 2/5 act | 2/5 act |
| Bag (2 members) (Bag) | Bag | Layer/lighting | 4/11 act | 4/11 act |
| Bag (2 members) (Bag) | Bag | Combine (member) | 2/4 act | 2/4 act |
| Bag (2 members) (Bag) | Bag | Layer | 4/5 act | 4/5 act |
| Bag (2 members) (Bag) | Bag | Document | 1/2 act | 1/2 act |

## The controls that went from 0 to acting

| document | kind | card | control | before | after |
|---|---|---|---|---:|---:|
| Box | Solid | Position | originX | 0 | 3335 |
| Box | Solid | Position | originY | 0 | 2684 |
| Box | Solid | Position | rotationDegrees | 0 | 1558 |
| Box | Solid | Position | scaleX | 0 | 6144 |
| Box | Solid | Position | scaleY | 0 | 1438 |
| Box | Solid | Position | skewX | 0 | 820 |
| Box | Solid | Position | skewY | 0 | 1629 |
| Box | Solid | Position | translateX | 0 | 2052 |
| Box | Solid | Position | translateY | 0 | 1798 |
| Can | Solid | Position | originX | 0 | 3404 |
| Can | Solid | Position | originY | 0 | 2743 |
| Can | Solid | Position | rotationDegrees | 0 | 1782 |
| Can | Solid | Position | scaleX | 0 | 6112 |
| Can | Solid | Position | scaleY | 0 | 1528 |
| Can | Solid | Position | skewX | 0 | 832 |
| Can | Solid | Position | skewY | 0 | 1833 |
| Can | Solid | Position | translateX | 0 | 2669 |
| Can | Solid | Position | translateY | 0 | 1840 |
| Gem | Solid | Position | originX | 0 | 1578 |
| Gem | Solid | Position | originY | 0 | 1178 |
| Gem | Solid | Position | rotationDegrees | 0 | 475 |
| Gem | Solid | Position | scaleX | 0 | 5947 |
| Gem | Solid | Position | skewY | 0 | 487 |
| Gem | Solid | Position | translateX | 0 | 910 |
| Gem | Solid | Position | translateY | 0 | 1030 |
| Orb | Solid | Position | originX | 0 | 3538 |
| Orb | Solid | Position | originY | 0 | 3055 |
| Orb | Solid | Position | rotationDegrees | 0 | 724 |
| Orb | Solid | Position | scaleX | 0 | 6134 |
| Orb | Solid | Position | skewY | 0 | 1092 |
| Orb | Solid | Position | translateX | 0 | 1887 |
| Orb | Solid | Position | translateY | 0 | 1820 |
| Pyramid | Solid | Fill | gradient | 0 | 624 |
| Pyramid | Solid | Fill | gradientCentreX | 0 | 618 |
| Pyramid | Solid | Fill | gradientCentreY | 0 | 618 |
| Pyramid | Solid | Fill | gradientDepthPixels | 0 | 452 |
| Pyramid | Solid | Fill | gradientMode | 0 | 619 |
| Pyramid | Solid | Fill | proceduralAngleDegrees | 0 | 619 |
| Pyramid | Solid | Fill | proceduralGradient | 0 | 624 |
| Pyramid | Solid | Fill | proceduralOffsetU | 0 | 623 |
| Pyramid | Solid | Fill | proceduralOffsetV | 0 | 623 |
| Pyramid | Solid | Fill | proceduralScale | 0 | 622 |
| Pyramid | Solid | Fill | space | 0 | 624 |
| Pyramid | Solid | Position | originX | 0 | 1465 |
| Pyramid | Solid | Position | originY | 0 | 1030 |
| Pyramid | Solid | Position | rotationDegrees | 0 | 943 |
| Pyramid | Solid | Position | scaleX | 0 | 6116 |
| Pyramid | Solid | Position | scaleY | 0 | 2068 |
| Pyramid | Solid | Position | skewX | 0 | 705 |
| Pyramid | Solid | Position | skewY | 0 | 963 |
| Pyramid | Solid | Position | translateX | 0 | 1264 |
| Pyramid | Solid | Position | translateY | 0 | 1508 |
| Ring | Solid | Position | originX | 0 | 3011 |
| Ring | Solid | Position | originY | 0 | 2688 |
| Ring | Solid | Position | scaleX | 0 | 840 |
| Ring | Solid | Position | skewY | 0 | 780 |
| Ring | Solid | Position | translateX | 0 | 1460 |
| Ring | Solid | Position | translateY | 0 | 1438 |
| Star | Primitive | Edge (border) | fill.gradient | 0 | 1075 |
| Star | Primitive | Edge (border) | fill.gradientCentreX | 0 | 1074 |
| Star | Primitive | Edge (border) | fill.gradientCentreY | 0 | 1074 |
| Star | Primitive | Edge (border) | fill.proceduralAngleDegrees | 0 | 1057 |
| Star | Primitive | Edge (border) | fill.proceduralGradient | 0 | 1074 |
| Star | Primitive | Edge (border) | fill.proceduralOffsetU | 0 | 1050 |
| Star | Primitive | Edge (border) | fill.proceduralOffsetV | 0 | 1057 |
| Star | Primitive | Edge (border) | fill.proceduralScale | 0 | 1059 |
| Star | Primitive | Fill | gradient | 0 | 2752 |
| Star | Primitive | Fill | gradientCentreX | 0 | 2750 |
| Star | Primitive | Fill | gradientCentreY | 0 | 2750 |
| Star | Primitive | Fill | proceduralAngleDegrees | 0 | 2718 |
| Star | Primitive | Fill | proceduralGradient | 0 | 2752 |
| Star | Primitive | Fill | proceduralOffsetU | 0 | 2725 |
| Star | Primitive | Fill | proceduralOffsetV | 0 | 2727 |
| Star | Primitive | Fill | proceduralScale | 0 | 2738 |

## Still 0 after the fixes, by card (on the re-measured documents)

- **Arc Burst (Kiln/Energy Explosion) — Document** (1): layerSpacing
- **Arc Burst (Kiln/Energy Explosion) — Document/light** (13): ambientColour, ambientIntensity, lights[0].colour, lights[0].enabled, lights[0].intensity, lights[0].kind, lights[0].pitch, lights[0].posX, lights[0].posY, lights[0].posZ, lights[0].range, lights[0].specular, lights[0].yaw
- **Arc Burst (Kiln/Energy Explosion) — Edge (border)** (72): alignment, enabled, fill.composite, fill.dotSize, fill.dotStagger, fill.fit, fill.gradient, fill.gradientAngleDegrees, fill.gradientCentreX, fill.gradientCentreY, fill.gradientDepthPixels, fill.gradientMode, fill.gradientSize, fill.gradientTint, fill.gridHorizontal, fill.gridLineWidth, fill.gridVertical, fill.heightDelta, fill.heightFieldScale, fill.heightFieldTint, fill.kind, fill.noiseKind, fill.overPhaseGradient, fill.overPhaseTint, fill.proceduralAngleDegrees, fill.proceduralGradient, fill.proceduralKind, fill.proceduralOffsetU, fill.proceduralOffsetV, fill.proceduralScale, fill.proceduralTint, fill.quantiseLevels, fill.rampGradient, fill.rampInputHigh, fill.rampInputLow, fill.rampQuantity, fill.rampTint, fill.solidColor, fill.space, fill.steelBaseHigh, fill.steelBaseLow, fill.steelCells, fill.steelGrain, fill.steelOctaves, fill.steelRustAmount, fill.steelRustColor, fill.steelRustReachPixels, fill.steelSeed, fill.stripOffset, fill.stripOrientationDegrees, fill.stripParameterisation, fill.stripPlainColor, fill.stripReach, fill.stripRepeats, fill.stripSlots[0].color, fill.stripSlots[0].height, fill.stripSlots[1].color, fill.stripSlots[1].height, fill.textureAngleDegrees, fill.textureAnimated, fill.textureFrameColumns, fill.textureFrameCount, fill.textureFrameRows, fill.textureMapping, fill.textureOffsetU, fill.textureOffsetV, fill.textureTilesX, fill.textureTilesY, fill.textureTint, fill.veil, joinsCoverage, width
- **Arc Burst (Kiln/Energy Explosion) — Fill** (60): fit, gradient, gradientAngleDegrees, gradientCentreX, gradientCentreY, gradientDepthPixels, gradientMode, gradientSize, gradientTint, heightDelta, heightFieldScale, heightFieldTint, kind, noiseKind, overPhaseGradient, overPhaseTint, proceduralAngleDegrees, proceduralGradient, proceduralOffsetU, proceduralOffsetV, proceduralScale, proceduralTint, quantiseLevels, rampGradient, rampInputHigh, rampInputLow, rampQuantity, rampTint, solidColor, space, steelBaseHigh, steelBaseLow, steelCells, steelGrain, steelOctaves, steelRustAmount, steelRustColor, steelRustReachPixels, steelSeed, stripOffset, stripOrientationDegrees, stripParameterisation, stripPlainColor, stripReach, stripRepeats, stripSlots[0].color, stripSlots[0].height, stripSlots[1].color, stripSlots[1].height, textureAngleDegrees, textureAnimated, textureFrameColumns, textureFrameCount, textureFrameRows, textureMapping, textureOffsetU, textureOffsetV, textureTilesX, textureTilesY, textureTint
- **Arc Burst (Kiln/Energy Explosion) — Layer** (1): zOffset
- **Arc Burst (Kiln/Energy Explosion) — Layer/height** (9): angle, bevel, bevelAmount, bevelSteps, curve, depth, steps, taper, technique
- **Arc Burst (Kiln/Energy Explosion) — Layer/lighting** (11): castShadows, intensityScale, normalConstant, normalKind, receiveLighting, receiveShadows, rimPower, rimStrength, specular, specularPower, specularTint
- **Arc Burst (Kiln/Energy Explosion) — Layer/mask** (2): fullAt, sourceLayerId
- **Arc Burst (Kiln/Energy Explosion) — Shape/generator** (179): form.bolt.flashAmp, form.cage.backOpa, form.cage.ballOpa, form.cage.breakouts, form.cage.coolK, form.cage.coolStart, form.cage.depthGamma, form.cage.dissolveStart, form.cage.ghostDeepP, form.cage.ghostHi, form.cage.ghostLo, form.cage.ghostP, form.cage.jitter, form.cage.ribs, form.cage.snapHi, form.cage.snapLo, form.cage.spanHi, form.cage.spanLo, form.cage.sparks, form.core.bodyAmp, form.core.bodyOpa, form.core.coolStart, form.core.crawlers, form.core.curl, form.core.detail, form.core.dissolveStart, form.core.forkP, form.core.ghostDeepP, form.core.ghostHi, form.core.ghostLo, form.core.ghostP, form.core.reachHi, form.core.reachLo, form.core.rough, form.core.whips, form.crown.coolStart, form.crown.crackleP, form.crown.dissolveK, form.crown.dissolveStart, form.crown.ghostLo, form.crown.ghostSpan, form.crown.lobeAmp, form.crown.lobes, form.crown.reachHi, form.crown.reachLo, form.crown.rough, form.crown.swapEnd, form.crown.swapStart, form.crown.widthHi, form.crown.widthLo, form.ghostDeepHi, form.ghostDeepLo, form.lattice.burstStart, form.lattice.chords, form.lattice.coolStart, form.lattice.detail, form.lattice.dissolveStart, form.lattice.flyHi, form.lattice.flyLo, form.lattice.ghostFloor, form.lattice.inner, form.lattice.outer, form.lattice.rimFloor, form.lattice.rough, form.lattice.veilP, form.lichten.branchAmp, form.lichten.branchMax, form.lichten.branchMin, form.lichten.branchScale, form.lichten.branchSpread, form.lichten.coolStart, form.lichten.depth, form.lichten.detail, form.lichten.dissolveStart, form.lichten.fadeAmt, form.lichten.ghostDeepP, form.lichten.ghostHi, form.lichten.ghostLo, form.lichten.ghostP, form.lichten.hollowStart, form.lichten.reachHi, form.lichten.reachLo, form.lichten.roots, form.lichten.rough, form.lichten.sparks, form.lichten.veilP, form.pinch.axisRatio, form.pinch.branchMax, form.pinch.branchMin, form.pinch.branchScale, form.pinch.branchSpread, form.pinch.coolStart, form.pinch.depth, form.pinch.dissolveStart, form.pinch.ghostDeepP, form.pinch.ghostHi, form.pinch.ghostLo, form.pinch.ghostP, form.pinch.jetSpread, form.pinch.lensOpa, form.pinch.ring, form.pinch.ringCoolStart, form.pinch.ringSpan, form.pinch.rough, form.pinch.tilt, form.stipple.arcs, form.stipple.branchMax, form.stipple.branchMin, form.stipple.breakHi, form.stipple.breakLo, form.stipple.coolStart, form.stipple.crossP, form.stipple.depth, form.stipple.dissolveStart, form.stipple.ghostDeepP, form.stipple.ghostHi, form.stipple.ghostLo, form.stipple.ghostP, form.stipple.hairP, form.stipple.lenHi, form.stipple.lenLo, form.stipple.rough, form.stipple.shards, form.stipple.speedHi, form.stipple.speedLo, form.swarmSize, form.terminal.coolStart, form.terminal.detail, form.terminal.dissolveStart, form.terminal.fireHi, form.terminal.fireLo, form.terminal.ghostDeepP, form.terminal.ghostHi, form.terminal.ghostLo, form.terminal.ghostP, form.terminal.nodes, form.terminal.reachHi, form.terminal.reachLo, form.terminal.rough, form.terminal.sweepDepth, form.terminal.sweepStart, form.weave.arcs, form.weave.chords, form.weave.coolStart, form.weave.dieHi, form.weave.dieLo, form.weave.dissolveStart, form.weave.ghostDeepP, form.weave.ghostHi, form.weave.ghostLo, form.weave.ghostP, form.weave.jitter, form.weave.shutterDepth, form.weave.shutterStart, form.weave.spanHi, form.weave.spanLo, form.weave.spurs, shapeFill.angleDeg, shapeFill.center, shapeFill.centerXAnim, shapeFill.centerYAnim, shapeFill.color, shapeFill.dotSize, shapeFill.dotSpacing, shapeFill.dotStagger, shapeFill.fit, shapeFill.gradient, shapeFill.gradientAnim, shapeFill.gridAngle, shapeFill.gridHorizontal, shapeFill.gridLineWidth, shapeFill.gridSpacing, shapeFill.gridVertical, shapeFill.mode, shapeFill.noiseKind, shapeFill.space, shapeFill.texture, shapeFill.zoom, shapeFill.zoomAnim
- **Arc Burst (Kiln/Energy Explosion) — Swarm** (9): dieTogether, firstSpawnPhase, gridReverse, instanceLife, interact, merge.carveStrengthDial, merge.sharpnessDial, spawnPhaseStep, spawnTiming
- **Arc Burst (Kiln/Energy Explosion) — Sweep** (3): enabled, extentFractionDial, startFractionDial
- **Bag (2 members) (Bag) — Combine (member)** (2): carveStrengthDial, sharpnessDial
- **Bag (2 members) (Bag) — Document** (1): layerSpacing
- **Bag (2 members) (Bag) — Edge (border)** (19): fill.fit, fill.gradientTint, fill.heightDelta, fill.heightFieldScale, fill.overPhaseTint, fill.proceduralTint, fill.quantiseLevels, fill.rampTint, fill.stripOffset, fill.stripPlainColor, fill.stripSlots[0].height, fill.stripSlots[1].height, fill.textureAnimated, fill.textureFrameColumns, fill.textureFrameCount, fill.textureFrameRows, fill.textureTilesX, fill.textureTilesY, joinsCoverage
- **Bag (2 members) (Bag) — Fill** (68): composite, dotSize, dotStagger, fit, gradient, gradientAngleDegrees, gradientCentreX, gradientCentreY, gradientDepthPixels, gradientMode, gradientSize, gradientTint, gridHorizontal, gridLineWidth, gridVertical, heightDelta, heightFieldScale, heightFieldTint, kind, noiseKind, overPhaseGradient, overPhaseTint, proceduralAngleDegrees, proceduralGradient, proceduralKind, proceduralOffsetU, proceduralOffsetV, proceduralScale, proceduralTint, quantiseLevels, rampGradient, rampInputHigh, rampInputLow, rampQuantity, rampTint, solidColor, space, steelBaseHigh, steelBaseLow, steelCells, steelGrain, steelOctaves, steelRustAmount, steelRustColor, steelRustReachPixels, steelSeed, stripOffset, stripOrientationDegrees, stripParameterisation, stripPlainColor, stripReach, stripRepeats, stripSlots[0].color, stripSlots[0].height, stripSlots[1].color, stripSlots[1].height, textureAngleDegrees, textureAnimated, textureFrameColumns, textureFrameCount, textureFrameRows, textureMapping, textureOffsetU, textureOffsetV, textureTilesX, textureTilesY, textureTint, veil
- **Bag (2 members) (Bag) — Layer** (1): zOffset
- **Bag (2 members) (Bag) — Layer/height** (4): bevelSteps, curve, steps, taper
- **Bag (2 members) (Bag) — Layer/lighting** (7): castShadows, normalConstant, normalKind, receiveShadows, rimPower, rimStrength, specularTint
- **Bag (2 members) (Bag) — Layer/mask** (2): fullAt, sourceLayerId
- **Bag (2 members) (Bag) — Swarm** (10): dieTogether, firstSpawnPhase, gridReverse, instanceLife, interact, lifetimeStagger, merge.carveStrengthDial, merge.sharpnessDial, spawnPhaseStep, spawnTiming
- **Bag (2 members) (Bag) — Sweep** (3): enabled, extentFractionDial, startFractionDial
- **Box (Solids) — Document** (1): layerSpacing
- **Box (Solids) — Layer** (1): zOffset
- **Box (Solids) — Shape/solid** (4): gemCrown, gemPavilion, gemSides, ringInner
- **Box (Solids) — Shell** (3): alignment, enabled, thicknessDial
- **Box (Solids) — Swarm** (33): count, dieTogether, distribution, enabled, evenSpacing, firstSpawnPhase, gridReverse, instanceLife, interact, lifetimeStagger, merge.carveStrengthDial, merge.sharpnessDial, merge.widthDial, orient, pathProgress, pathSpread, positionJitterX, positionJitterY, rotationJitterDegreesDial, scaleByIndex, scaleJitterDial, shape, spawnMode, spawnOrderChaos, spawnPhaseStep, spawnTiming, spawnerOffsetX, spawnerOffsetY, spawnerPitchDegrees, spawnerRadius, spawnerRotationDegrees, spawnerYawDegrees, timing
- **Box (Solids) — Sweep** (5): enabled, extentDegreesDial, extentFractionDial, startDegreesDial, startFractionDial
- **Can (Solids) — Document** (1): layerSpacing
- **Can (Solids) — Layer** (1): zOffset
- **Can (Solids) — Shape/solid** (5): depth, gemCrown, gemPavilion, gemSides, ringInner
- **Can (Solids) — Shell** (3): alignment, enabled, thicknessDial
- **Can (Solids) — Swarm** (33): count, dieTogether, distribution, enabled, evenSpacing, firstSpawnPhase, gridReverse, instanceLife, interact, lifetimeStagger, merge.carveStrengthDial, merge.sharpnessDial, merge.widthDial, orient, pathProgress, pathSpread, positionJitterX, positionJitterY, rotationJitterDegreesDial, scaleByIndex, scaleJitterDial, shape, spawnMode, spawnOrderChaos, spawnPhaseStep, spawnTiming, spawnerOffsetX, spawnerOffsetY, spawnerPitchDegrees, spawnerRadius, spawnerRotationDegrees, spawnerYawDegrees, timing
- **Can (Solids) — Sweep** (5): enabled, extentDegreesDial, extentFractionDial, startDegreesDial, startFractionDial
- **Disc (Pyre) — Document** (1): layerSpacing
- **Disc (Pyre) — Document/light** (12): ambientColour, ambientIntensity, lights[0].colour, lights[0].enabled, lights[0].intensity, lights[0].kind, lights[0].pitch, lights[0].posX, lights[0].posY, lights[0].posZ, lights[0].range, lights[0].yaw
- **Disc (Pyre) — Edge (border)** (72): alignment, enabled, fill.composite, fill.dotSize, fill.dotStagger, fill.fit, fill.gradient, fill.gradientAngleDegrees, fill.gradientCentreX, fill.gradientCentreY, fill.gradientDepthPixels, fill.gradientMode, fill.gradientSize, fill.gradientTint, fill.gridHorizontal, fill.gridLineWidth, fill.gridVertical, fill.heightDelta, fill.heightFieldScale, fill.heightFieldTint, fill.kind, fill.noiseKind, fill.overPhaseGradient, fill.overPhaseTint, fill.proceduralAngleDegrees, fill.proceduralGradient, fill.proceduralKind, fill.proceduralOffsetU, fill.proceduralOffsetV, fill.proceduralScale, fill.proceduralTint, fill.quantiseLevels, fill.rampGradient, fill.rampInputHigh, fill.rampInputLow, fill.rampQuantity, fill.rampTint, fill.solidColor, fill.space, fill.steelBaseHigh, fill.steelBaseLow, fill.steelCells, fill.steelGrain, fill.steelOctaves, fill.steelRustAmount, fill.steelRustColor, fill.steelRustReachPixels, fill.steelSeed, fill.stripOffset, fill.stripOrientationDegrees, fill.stripParameterisation, fill.stripPlainColor, fill.stripReach, fill.stripRepeats, fill.stripSlots[0].color, fill.stripSlots[0].height, fill.stripSlots[1].color, fill.stripSlots[1].height, fill.textureAngleDegrees, fill.textureAnimated, fill.textureFrameColumns, fill.textureFrameCount, fill.textureFrameRows, fill.textureMapping, fill.textureOffsetU, fill.textureOffsetV, fill.textureTilesX, fill.textureTilesY, fill.textureTint, fill.veil, joinsCoverage, width
- **Disc (Pyre) — Fill** (60): fit, gradient, gradientAngleDegrees, gradientCentreX, gradientCentreY, gradientDepthPixels, gradientMode, gradientSize, gradientTint, heightDelta, heightFieldScale, heightFieldTint, kind, noiseKind, overPhaseGradient, overPhaseTint, proceduralAngleDegrees, proceduralGradient, proceduralOffsetU, proceduralOffsetV, proceduralScale, proceduralTint, quantiseLevels, rampGradient, rampInputHigh, rampInputLow, rampQuantity, rampTint, solidColor, space, steelBaseHigh, steelBaseLow, steelCells, steelGrain, steelOctaves, steelRustAmount, steelRustColor, steelRustReachPixels, steelSeed, stripOffset, stripOrientationDegrees, stripParameterisation, stripPlainColor, stripReach, stripRepeats, stripSlots[0].color, stripSlots[0].height, stripSlots[1].color, stripSlots[1].height, textureAngleDegrees, textureAnimated, textureFrameColumns, textureFrameCount, textureFrameRows, textureMapping, textureOffsetU, textureOffsetV, textureTilesX, textureTilesY, textureTint
- **Disc (Pyre) — Layer** (1): zOffset
- **Disc (Pyre) — Layer/height** (9): angle, bevel, bevelAmount, bevelSteps, curve, depth, steps, taper, technique
- **Disc (Pyre) — Layer/lighting** (11): castShadows, intensityScale, normalConstant, normalKind, receiveLighting, receiveShadows, rimPower, rimStrength, specular, specularPower, specularTint
- **Disc (Pyre) — Layer/mask** (2): fullAt, sourceLayerId
- **Disc (Pyre) — Shape/generator** (157): layer.borderFill.gradientAnim, layer.clipByChannel, layer.clipInvert, layer.coalesce, layer.density, layer.fireballArms, layer.fireballContrast, layer.fireballCooling, layer.fireballMirror, layer.fireballReach, layer.fireballSharpness, layer.fireballSource, layer.fireballSpread, layer.fireballThreshold, layer.fuseShadeRange, layer.fuseSoftness, layer.fuseThreshold, layer.gemEdgeGlow, layer.gemEdgeGlowFill.angleDeg, layer.gemEdgeGlowFill.center, layer.gemEdgeGlowFill.centerXAnim, layer.gemEdgeGlowFill.centerYAnim, layer.gemEdgeGlowFill.color, layer.gemEdgeGlowFill.dotSize, layer.gemEdgeGlowFill.dotSpacing, layer.gemEdgeGlowFill.dotStagger, layer.gemEdgeGlowFill.fit, layer.gemEdgeGlowFill.gradient, layer.gemEdgeGlowFill.gradientAnim, layer.gemEdgeGlowFill.gridAngle, layer.gemEdgeGlowFill.gridHorizontal, layer.gemEdgeGlowFill.gridLineWidth, layer.gemEdgeGlowFill.gridSpacing, layer.gemEdgeGlowFill.gridVertical, layer.gemEdgeGlowFill.mode, layer.gemEdgeGlowFill.noiseKind, layer.gemEdgeGlowFill.space, layer.gemEdgeGlowFill.texture, layer.gemEdgeGlowFill.zoom, layer.gemEdgeGlowFill.zoomAnim, layer.gemInnerGlow, layer.gemInnerGlowFill.gradientAnim, layer.gemInnerGlowFill.mode, layer.gemLineFill.angleDeg, layer.gemLineFill.center, layer.gemLineFill.centerXAnim, layer.gemLineFill.centerYAnim, layer.gemLineFill.color, layer.gemLineFill.dotSize, layer.gemLineFill.dotSpacing, layer.gemLineFill.dotStagger, layer.gemLineFill.fit, layer.gemLineFill.gradient, layer.gemLineFill.gradientAnim, layer.gemLineFill.gridAngle, layer.gemLineFill.gridHorizontal, layer.gemLineFill.gridLineWidth, layer.gemLineFill.gridSpacing, layer.gemLineFill.gridVertical, layer.gemLineFill.mode, layer.gemLineFill.noiseKind, layer.gemLineFill.space, layer.gemLineFill.texture, layer.gemLineFill.zoom, layer.gemLineFill.zoomAnim, layer.gemLineWidth, layer.gemSpecPower, layer.gemSpecular, layer.gemSpecularFill.angleDeg, layer.gemSpecularFill.center, layer.gemSpecularFill.centerXAnim, layer.gemSpecularFill.centerYAnim, layer.gemSpecularFill.color, layer.gemSpecularFill.dotSize, layer.gemSpecularFill.dotSpacing, layer.gemSpecularFill.dotStagger, layer.gemSpecularFill.fit, layer.gemSpecularFill.gradient, layer.gemSpecularFill.gradientAnim, layer.gemSpecularFill.gridAngle, layer.gemSpecularFill.gridHorizontal, layer.gemSpecularFill.gridLineWidth, layer.gemSpecularFill.gridSpacing, layer.gemSpecularFill.gridVertical, layer.gemSpecularFill.mode, layer.gemSpecularFill.noiseKind, layer.gemSpecularFill.space, layer.gemSpecularFill.texture, layer.gemSpecularFill.zoom, layer.gemSpecularFill.zoomAnim, layer.heat, layer.heightFromChannel, layer.heightLightAngle, layer.heightRelief, layer.matteAlphaSource, layer.matteBlurAmount, layer.matteChannel, layer.matteCombine, layer.matteDisplaceAmount, layer.matteFlags, layer.matteHueDegrees, layer.matteInvert, layer.matteRole, layer.matteScope, layer.matteStrength, layer.matteWriteLuma, layer.particleSpin, layer.playbackGlow, layer.playbackLoopDuration, layer.playbackPixelGrid, layer.playbackPixelLevels, layer.playbackPixelated, layer.playbackScale, layer.playbackScrub01, layer.playbackSpeed, layer.playbackTint, layer.playbackZoom, layer.rampCoverage, layer.rampFusion, layer.rampLightAngle, layer.rampLighting, layer.rampRelief, layer.rampRimScale, layer.shapeFill.color, layer.shapeFill.gradientAnim, layer.shapeOffsetX, layer.shapeOffsetY, layer.shapePitch, layer.shapeRotation, layer.shapeScale, layer.shapeScaleSnap, layer.shapeYaw, layer.swarmChaos, layer.swarmCount, layer.swarmCustomX, layer.swarmCustomY, layer.swarmDieTogether, layer.swarmEvenPath, layer.swarmFirstFrame, layer.swarmFrameStep, layer.swarmGridReverse, layer.swarmOrient, layer.swarmParticleLife, layer.swarmPathSpread, layer.swarmProgress, layer.swarmRoll, layer.swarmScale, layer.swarmScaleByIndex, layer.swarmShapeKind, layer.swarmSpawnChaos, layer.swarmSpawnMode, layer.swarmSpawnTiming, layer.swarmTilt, layer.swarmTiming, layer.swarmTurn, layer.textBorder.gradientAnim, layer.textFill.gradientAnim
- **Disc (Pyre) — Swarm** (9): dieTogether, firstSpawnPhase, gridReverse, instanceLife, interact, merge.carveStrengthDial, merge.sharpnessDial, spawnPhaseStep, spawnTiming
- **Disc (Pyre) — Sweep** (3): enabled, extentFractionDial, startFractionDial
- **Gem (Solids) — Document** (1): layerSpacing
- **Gem (Solids) — Layer** (1): zOffset
- **Gem (Solids) — Position** (2): scaleY, skewX
- **Gem (Solids) — Shape/solid** (3): aspect, depth, ringInner
- **Gem (Solids) — Shell** (3): alignment, enabled, thicknessDial
- **Gem (Solids) — Swarm** (33): count, dieTogether, distribution, enabled, evenSpacing, firstSpawnPhase, gridReverse, instanceLife, interact, lifetimeStagger, merge.carveStrengthDial, merge.sharpnessDial, merge.widthDial, orient, pathProgress, pathSpread, positionJitterX, positionJitterY, rotationJitterDegreesDial, scaleByIndex, scaleJitterDial, shape, spawnMode, spawnOrderChaos, spawnPhaseStep, spawnTiming, spawnerOffsetX, spawnerOffsetY, spawnerPitchDegrees, spawnerRadius, spawnerRotationDegrees, spawnerYawDegrees, timing
- **Gem (Solids) — Sweep** (5): enabled, extentDegreesDial, extentFractionDial, startDegreesDial, startFractionDial
- **Orb (Solids) — Document** (1): layerSpacing
- **Orb (Solids) — Layer** (1): zOffset
- **Orb (Solids) — Position** (2): scaleY, skewX
- **Orb (Solids) — Shape/solid** (6): aspect, depth, gemCrown, gemPavilion, gemSides, ringInner
- **Orb (Solids) — Shell** (3): alignment, enabled, thicknessDial
- **Orb (Solids) — Swarm** (33): count, dieTogether, distribution, enabled, evenSpacing, firstSpawnPhase, gridReverse, instanceLife, interact, lifetimeStagger, merge.carveStrengthDial, merge.sharpnessDial, merge.widthDial, orient, pathProgress, pathSpread, positionJitterX, positionJitterY, rotationJitterDegreesDial, scaleByIndex, scaleJitterDial, shape, spawnMode, spawnOrderChaos, spawnPhaseStep, spawnTiming, spawnerOffsetX, spawnerOffsetY, spawnerPitchDegrees, spawnerRadius, spawnerRotationDegrees, spawnerYawDegrees, timing
- **Orb (Solids) — Sweep** (5): enabled, extentDegreesDial, extentFractionDial, startDegreesDial, startFractionDial
- **Pyramid (Solids) — Document** (1): layerSpacing
- **Pyramid (Solids) — Edge (border)** (72): alignment, enabled, fill.composite, fill.dotSize, fill.dotStagger, fill.fit, fill.gradient, fill.gradientAngleDegrees, fill.gradientCentreX, fill.gradientCentreY, fill.gradientDepthPixels, fill.gradientMode, fill.gradientSize, fill.gradientTint, fill.gridHorizontal, fill.gridLineWidth, fill.gridVertical, fill.heightDelta, fill.heightFieldScale, fill.heightFieldTint, fill.kind, fill.noiseKind, fill.overPhaseGradient, fill.overPhaseTint, fill.proceduralAngleDegrees, fill.proceduralGradient, fill.proceduralKind, fill.proceduralOffsetU, fill.proceduralOffsetV, fill.proceduralScale, fill.proceduralTint, fill.quantiseLevels, fill.rampGradient, fill.rampInputHigh, fill.rampInputLow, fill.rampQuantity, fill.rampTint, fill.solidColor, fill.space, fill.steelBaseHigh, fill.steelBaseLow, fill.steelCells, fill.steelGrain, fill.steelOctaves, fill.steelRustAmount, fill.steelRustColor, fill.steelRustReachPixels, fill.steelSeed, fill.stripOffset, fill.stripOrientationDegrees, fill.stripParameterisation, fill.stripPlainColor, fill.stripReach, fill.stripRepeats, fill.stripSlots[0].color, fill.stripSlots[0].height, fill.stripSlots[1].color, fill.stripSlots[1].height, fill.textureAngleDegrees, fill.textureAnimated, fill.textureFrameColumns, fill.textureFrameCount, fill.textureFrameRows, fill.textureMapping, fill.textureOffsetU, fill.textureOffsetV, fill.textureTilesX, fill.textureTilesY, fill.textureTint, fill.veil, joinsCoverage, width
- **Pyramid (Solids) — Fill** (19): fit, gradientTint, heightDelta, overPhaseTint, proceduralTint, quantiseLevels, rampGradient, rampInputLow, rampTint, stripOffset, stripPlainColor, stripSlots[0].height, stripSlots[1].height, textureAnimated, textureFrameColumns, textureFrameCount, textureFrameRows, textureTilesX, textureTilesY
- **Pyramid (Solids) — Layer** (1): zOffset
- **Pyramid (Solids) — Layer/height** (9): angle, bevel, bevelAmount, bevelSteps, curve, depth, steps, taper, technique
- **Pyramid (Solids) — Layer/lighting** (5): castShadows, normalConstant, normalKind, receiveShadows, rimPower
- **Pyramid (Solids) — Layer/mask** (2): fullAt, sourceLayerId
- **Pyramid (Solids) — Shape/solid** (4): gemCrown, gemPavilion, gemSides, ringInner
- **Pyramid (Solids) — Shell** (3): alignment, enabled, thicknessDial
- **Pyramid (Solids) — Swarm** (33): count, dieTogether, distribution, enabled, evenSpacing, firstSpawnPhase, gridReverse, instanceLife, interact, lifetimeStagger, merge.carveStrengthDial, merge.sharpnessDial, merge.widthDial, orient, pathProgress, pathSpread, positionJitterX, positionJitterY, rotationJitterDegreesDial, scaleByIndex, scaleJitterDial, shape, spawnMode, spawnOrderChaos, spawnPhaseStep, spawnTiming, spawnerOffsetX, spawnerOffsetY, spawnerPitchDegrees, spawnerRadius, spawnerRotationDegrees, spawnerYawDegrees, timing
- **Pyramid (Solids) — Sweep** (5): enabled, extentDegreesDial, extentFractionDial, startDegreesDial, startFractionDial
- **Ring (Solids) — Document** (1): layerSpacing
- **Ring (Solids) — Layer** (1): zOffset
- **Ring (Solids) — Position** (3): rotationDegrees, scaleY, skewX
- **Ring (Solids) — Shape/solid** (6): aspect, depth, gemCrown, gemPavilion, gemSides, roll
- **Ring (Solids) — Shell** (3): alignment, enabled, thicknessDial
- **Ring (Solids) — Swarm** (33): count, dieTogether, distribution, enabled, evenSpacing, firstSpawnPhase, gridReverse, instanceLife, interact, lifetimeStagger, merge.carveStrengthDial, merge.sharpnessDial, merge.widthDial, orient, pathProgress, pathSpread, positionJitterX, positionJitterY, rotationJitterDegreesDial, scaleByIndex, scaleJitterDial, shape, spawnMode, spawnOrderChaos, spawnPhaseStep, spawnTiming, spawnerOffsetX, spawnerOffsetY, spawnerPitchDegrees, spawnerRadius, spawnerRotationDegrees, spawnerYawDegrees, timing
- **Ring (Solids) — Sweep** (5): enabled, extentDegreesDial, extentFractionDial, startDegreesDial, startFractionDial
- **Star (Primitives) — Document** (1): layerSpacing
- **Star (Primitives) — Edge (border)** (19): fill.fit, fill.gradientTint, fill.heightDelta, fill.heightFieldScale, fill.overPhaseTint, fill.proceduralTint, fill.quantiseLevels, fill.rampTint, fill.stripOffset, fill.stripPlainColor, fill.stripSlots[0].height, fill.stripSlots[1].height, fill.textureAnimated, fill.textureFrameColumns, fill.textureFrameCount, fill.textureFrameRows, fill.textureTilesX, fill.textureTilesY, joinsCoverage
- **Star (Primitives) — Fill** (18): fit, gradientTint, heightDelta, heightFieldScale, overPhaseTint, proceduralTint, quantiseLevels, rampTint, stripOffset, stripPlainColor, stripSlots[0].height, stripSlots[1].height, textureAnimated, textureFrameColumns, textureFrameCount, textureFrameRows, textureTilesX, textureTilesY
- **Star (Primitives) — Layer** (1): zOffset
- **Star (Primitives) — Layer/height** (4): bevelSteps, curve, steps, taper
- **Star (Primitives) — Layer/lighting** (7): castShadows, normalConstant, normalKind, receiveShadows, rimPower, rimStrength, specularTint
- **Star (Primitives) — Layer/mask** (1): fullAt
- **Star (Primitives) — Swarm** (10): dieTogether, firstSpawnPhase, gridReverse, instanceLife, interact, lifetimeStagger, merge.carveStrengthDial, merge.sharpnessDial, spawnPhaseStep, spawnTiming
- **Star (Primitives) — Sweep** (3): enabled, extentFractionDial, startFractionDial
