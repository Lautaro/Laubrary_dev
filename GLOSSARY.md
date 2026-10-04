# Glossary — Laubrary Dev

Words we use for in-game concepts and tool entities. Stick to these in code and in
conversation; call out drift.

## Laubrary tools (entities)

| Tool | Entity | Meaning |
|------|--------|---------|
| **Choreographer** | **Dancer** | One sampled pose (position + facing) in a group motion; only ever a sampled pose, never a stateful MonoBehaviour. **Choreography** = the authored asset. |
| **Zoetrope** | **Zoe** | A versioned 2D sprite character/animation. **FrameRef** = one sheet cell + pivot. **SpriteCatalog** = named static sprites from a sheet. |
| **Larder** | **Ware** | A procedurally generated pixel-art shelf product seen from the front (book, can, box, crate, carton). |
| **Pyre** | **Blast** | A procedurally baked pixel-art explosion/hit animation. |
| **GoreLab** | **Gore Rig / Body member / Remover / Wound recipe** | A Gore Rig is the sidecar asset that tags a character's body members (head ball, torso box) on every drawn frame. A remover is one thing a wound takes out of a member (a slice plane, a capsule), stored in the member's own coordinates. A wound recipe is a damage type (Slice, Cut, Bullet, Shotgun, Remove head) that turns a swipe into removers. Not to be confused with a Launimator direction-set member. |

## Larder (lootable store interior)

- **Ware** — one shelf product. Described by a **WareSpec** (ScriptableObject) and painted by the
  shared **WareGenerator** into a pixel sprite.
- **WareKind** — Book, Can, Box, Crate, Carton.
- **WareShape** — silhouette family: Rectangular, RoundedRect, Round (can/cylinder), Spherical.
- **FillMode** — how the body is coloured: Solid, Gradient, InnerGlow, InnerShadow.
- **Label** — a fake white patch with scribbled ink, laid Horizontal, Diagonal, or as a narrower
  CenterPatch.
- **Corner** — corner treatment: colour Triangle, Rounded, or CutOff.
- **Band** — a coloured stripe across the ware (Horizontal, Vertical, Diagonal).
- **Lid** — a thin top strip (gray/black/white), on or off.
- **Damage stage** — a pre-generated silhouette of the ware with its top torn off along an
  irregular, scorched (**edge-burn**) edge. Stage 0 = intact; a ware carries 2–4 stages.
- **ShelfWare** — the runtime component: shows the current stage, and on **Hit** spawns a
  colour-sampled pixel **burst** (WareDebris), advances damage stage or is destroyed, and
  **rattles** (WareShake). Survivors left on the shelf can shake; broken tops can be flung.

## Pyre (explosions & hits)

- **Blast** — one whole explosion, a **BlastSpec** ScriptableObject baked to a sprite-sheet
  animation. Painted by the shared **BlastRenderer**.
- **Layer** — a back-to-front rendering band of the blast (e.g. smoke behind, fire in front).
- **Wave** — a timed burst of shapes inside a layer, alive from a start frame to an end frame,
  changing size/position/colour/alpha across its life.
- **WaveShape** — Disc, Ring, DissolvingDisc, SparkleField, Crescent.
- **SpawnMode / EndMode** — how a shape enters (Instant/FadeIn/GrowIn) and leaves
  (Instant/FadeOut/Disintegrate/Shrink) its life window.
- **Deform** — global squash / skew / wobble applied to the whole blast for directional hits.

## Game (abandoned-city store loot scene)

- **Loot scene** — a stocked store interior the player fights through.
- **Horde** — the mass of zombies.
- **Shelf** — a row that holds Wares; the destructible backdrop the player shoots among.
