# UC4 walk: a particle spray based on a Zoe's sprite (T-0347)

Walked 2026-09-16 on the live Laubrary Dev editor (dev @ 097a3be8), after walker A's UC1–UC3. Recipe saved as `Assets/Demos/ChunksDemo/UseCases/UC4 Zoe Spray.asset`. The Zoe-triggered test used a **duplicate** of Floating Disc, `Assets/Demos/ChunksDemo/UseCases/UC Floating Disc (walk copy).asset`. The owner's `Assets/Zoetrope/Floating Disc.asset` was not touched. Saving the duplicate also generated its `.states.cs` file next to it. That is normal for any Zoe.

**The owner's sentence:** "Spawn particle spray based of the sprite of a zoe."

**How the windows were driven.** I used the same harness as walker A (see `T-0344/WALK.md`):
- Buttons got a submit event, and menu rows got a pointer down/up.
- Sliders were moved through their own notifying setter.
- The Sprite field's value was set directly (the Unity object picker cannot be synthesised).
- The Chunks picker on the Zoe row was driven through the browser's own pick callback.
- Each action started a new undo group.

Captures `01`–`07` are PrintWindow shots of the real windows. `20`–`22` are Play-mode camera renders from an unsaved stage scene.

## What was built

One **Palette Splash** card, named "Disc spray":
- Sprite `cell_001` (a Floating Disc frame), From footprint on.
- Count 30–40, Size 1–3 px, Speed 2–6.
- Direction 90, Spread 180 (the maximum).
- Gravity 6, Drag 0.4, Life 0.4–0.9.

The clock runs 0.90 s. No Timing section (one capability).

On the Zoe copy, the Death reaction got a second **Spawn Chunks** row pointing at UC4. The original private row was switched off rather than deleted.

## The walk

1. **`Laubrary/Chunks` → New → "UC4 Zoe Spray" → Create** (`01`). The recipe was empty. It landed in `UseCases/`, next to the recipe that was open.
2. **Add capability… → which kind?** (`02`).
   - Nothing in the menu mentions a Zoe.
   - "Palette Splash — Sprays small particles in the colours of a picture" is the closest match to "particle spray based on a sprite", so I picked it.
   - The other candidate, Debris Scatter → Sampled, is described only as "a spray of pieces"; its sprite link appears only once you open its card.
3. **Palette Splash card** (`03`). The card has 13 controls, and nothing on it mentions a Zoe.
   - **Sprite** is a plain Unity sprite field. **A Zoe cannot be picked there.**
   - To use Floating Disc's look I had to know that its frames live in the Launimator reel's `Floating Disc.png` and are named `cell_000`…`cell_008`.
   - Searching the sprite picker for "Floating Disc" finds nothing (probe: 0 sprite hits). Three reel textures in the project all use `cell_…` names.
4. **Set Name, Count, Speed, Life.** Spread was already 180, and the preview drew a **half disc**:
   - the spray only goes upward;
   - Spread tops out at 180;
   - the runtime uses ±Spread/2 (`PaletteSplash.Fire`).

   So a 360° spray is **not possible** with Palette Splash, even though its own tooltip says "180 = a full circle". Debris Scatter uses ±Spread, so its 180 really is a full circle. The two cards use the same word for different angles.
5. **Scrubbed to 0.05 / 0.30 / 0.60 s** (`04`).
   - The dots rise, spread and fall.
   - Every dot is drawn in the card's blue. Neither the sprite's colours nor its footprint appear, so nothing in the preview shows that the spray is "based on the sprite".
   - The "Alpha over life" envelope on a newly added card is an empty box with no curve (`03`). The runtime quietly falls back to a linear fade.
6. **Save Project.** The asset saved clean.
7. **Standalone burst in Play mode** (`20`): 30+ specks appeared, in the disc's own browns, olives and whites, emitted from the disc's footprint and falling. This matches the recipe.
8. **Wiring it to the Zoe (the copy).** Laubrary → Zoetrope → Zoes, then the copy's Reactions → Death (`05`).
   - The existing Death row is a **Private** Spawn Chunks. Clicking **Public** on it **does nothing**: the row stays Private, and there is no way to reach a library picker from it (confirmed in code: `BuildChunksRefRow`'s Public branch only rebuilds).
   - The only route is **+ Add effect → Spawn Chunks**, which adds a new row with a Public picker. I picked UC4 there (the browser listed all 11 recipes, including ProbeChunk and ArenaDebris), then switched the old row off with its ✔.
   - The row's "Live sample preview" said "This recipe has no Sampled-visual Debris Scatter — nothing else to preview". Its swatches were near-black, unlike the palette the runtime actually sampled. It says nothing about the Palette Splash in UC4.
9. **Zoe-triggered burst** (spawned the copy with `ZoeSpawner`, then `Health.Kill()`; `21`).
   - The death row fired UC4, and the specks flew in the disc's colours.
   - **They came from UC4's authored `cell_001`, not from the Zoe's live frame (`cell_000`).** Palette Splash never reads the live sprite the Zoe forwards; an authored sprite always wins.
   - `Kill()` also fired the Hit row, whose private Debris Scatter cut 21–31 pieces from the live `cell_000`. That is the Zoe's hit reaction, not UC4.
10. **Tried the "live" route: cleared UC4's Sprite** (`06`, `22`).
    - The card showed no warning, and the preview looked exactly the same.
    - **Standalone:** every speck was plain white, all from one point.
    - **Zoe-triggered:** the specks took the Zoe's live palette (6 colours sampled off its current frame), but all came from one point. The "From footprint" toggle, still on, did nothing.
    - So a Palette Splash can follow the live Zoe **only** with an empty Sprite, and then it loses both its footprint and its standalone look.
11. **Put `cell_001` back and saved** (`07`). This is the final state: a correct standalone spray, and the Zoe spray uses the authored frame's colours and footprint.

**Step count for a human:** about **28 actions**:

| Part | Actions |
|---|---|
| Menu | 1 |
| New / name / Create | 3 |
| Add + kind | 2 |
| Sprite picker, hunting for `cell_001` | ~3 |
| Name + 3 ranges (2 handles each) | 7 |
| Play + save | 2 |
| Zoe: open window and browse to the copy | 3 |
| Zoe: the failed Public click | 1 |
| Zoe: Add effect → Spawn Chunks | 2 |
| Zoe: chip + pick | 2 |
| Zoe: switch the old row off | 1 |
| Save | 1 |

## Judging points from the task

- **Standalone (sprite chosen in the recipe):** works, and the runtime looks right. But the sprite is found by a file-level name (`cell_001`) rather than by the Zoe, and the preview never shows it.
- **Triggered from the Zoe (live sprite forwarded, T-0252):** the burst fires from the Death row.
  - The live sprite is **not** used by Palette Splash. Only Debris Scatter (Sampled) and, since 097a3be8, Fragment Fracture read it.
  - The live **palette** reaches Palette Splash only when its Sprite is empty, and even then it emits from a single point.
- **Particle-system wrapper opinion:** see the end of this report.

## Friction log

| # | Trying to | Had to | Should have | Severity | Rule |
|---|---|---|---|---|---|
| J1 | spray "based on the sprite of a Zoe" | pick a raw sprite by file-level name (`cell_001`), because a Zoe cannot be picked and searching "Floating Disc" finds nothing | the Sprite field accepts a Zoe (or any `IChunkAnimation`, as Fracture's Source does), with a frame chooser, and falls back to a raw sprite | **MAJOR** | ui-rules §1 (a reference is a picker over the owner); owner "a reference is always a picker" |
| J2 | have the Zoe-triggered spray follow the Zoe's live frame | discover in Play mode that Palette Splash ignores the live sprite the Zoe forwards; clearing Sprite brings the live colours but emits from one point and turns the standalone spray white | Palette Splash reads `SampleSourceOverride` first, as Debris Scatter and (now) Fracture do, for colours and footprint; its tooltip says so | **MAJOR** | design §1 (Zoe palette spray); consistency across producers |
| J3 | spray in all directions | accept a half-circle at Spread 180 (the runtime uses ±Spread/2), even though the tooltip says "180 = a full circle"; Debris Scatter's Spread 180 IS a full circle | one meaning for Spread on every card (half-angle, 0–180 = full circle), or a range up to 360 here | **MAJOR** | ui-rules §1 "same kind of value, same control"; §2 tooltip must be true |
| J4 | see that the spray uses the disc's colours and shape | trust blue dots that do not change when the sprite is set, cleared or swapped | preview dots take the sprite's palette (a small precomputed palette is cheap) and emit from its bounds; without a sprite, white dots | **MAJOR** | design §6 "the preview shows what happens"; ui-rules §5 |
| J5 | attach a library recipe to the Zoe's existing Death row | find that "Public" on a Private row does nothing, then add a second row and switch the first off | Public on a Private row offers the library picker (the private copy stays in the file until replaced) | **MAJOR** (Zoe window, not Chunks) | ui-rules §2 label = action |
| J6 | know what the Zoe row will do with UC4 | read "no Sampled-visual Debris Scatter — nothing else to preview" and near-black swatches, which do not match the runtime palette | the live-sample preview covers every producer that reads the live frame, and says when the recipe's authored sprite overrides it | MINOR | ui-rules §5 |
| J7 | know the spray has no source | nothing warns when Sprite is empty; "From footprint" stays on with nothing to emit from | a subtle "(white, from the origin, unless a Zoe supplies colours)" on the empty Sprite, and From footprint dimmed | MINOR | ui-rules §2 conditional tooltips |
| J8 | read "Alpha over life" | see an empty curve box on a fresh card (the list is only filled by migration); the runtime silently uses a linear fade | a fresh card is born with the default fade curve drawn | MINOR | ui-rules §5 (no blank promise) |
| J9 | size specks in the disc's pixels | set Pixels/unit (32) by hand, although the disc is 16 ppu; Debris Scatter reads the project pixel scale, but Palette Splash does not | same pixel-scale default as Debris Scatter, or the source sprite's own ppu | MINOR | consistency |
| J10 | test the death reaction | note that `Health.Kill()` fires the Hit reaction as well as Death | (Zoe behaviour; flagged, not Chunks) | MINOR | — |
| J11 | find the right kind | choose between Palette Splash and Debris Scatter → Sampled; neither menu tooltip mentions Zoes or live frames | menu tooltips name the Zoe case ("…the colours of a picture or a Zoe's current frame") | MINOR | ui-rules §2 |

UC1's F9 (where New saves), F10 (Tags and Views open by default), F12 (the Replay glyph) and F14 (library tile) apply unchanged. F14's "reopen forgets the recipe" part is fixed by 097a3be8: the window came back on UC4 after every Play-mode reload.

## Verdict: buildable with workarounds

The spray exists and looks right in Play mode. The workarounds:
- picking the Zoe's frame as a raw `cell_…` sprite (J1);
- accepting a half-circle (J3);
- accepting that a Zoe-triggered spray uses the authored frame, not the live one (J2).

On the Zoe side, a second effect row is needed (J5).

## Would wrapping Unity's own Particle System help here? (owner's aside from T-0119)

**Opinion: not as the answer to this use case. It is worth having later as a separate, clearly-scoped "Particle System" producer for effects Chunks is bad at.**

Against using it for this case:
- **Sampling a sprite's pixels is the whole point here, and Shuriken does not do it natively.** Per-particle colour from a sprite's footprint means either a custom-emit script (`ParticleSystem.Emit` with `EmitParams` per pixel), which is what Palette Splash already does with SpriteRenderers, or a mesh/texture shape module that does not colour by pixel. The wrapper would re-implement the sampling anyway.
- **The Chunks preview is a deterministic schematic** built by the window's own simulation (design §6). A Particle System would need its own simulate-in-editor path (`ParticleSystem.Simulate` on a hidden object), with a different clock, seeding and draw path. The "one preview, one clock" promise gets harder, not easier.
- **Depth is per-renderer, not per-particle.** A Particle System is one renderer with one sortingOrder. That makes the owner's UC6 interleaving (single fragments and single Pyres alternating) impossible for its particles, while SpriteRenderer particles can be ordered one by one (see T-0349).
- **Pixel-art look.** Shuriken billboards filter, scale and sort differently from pixel-snapped SpriteRenderers, so the specks would stop matching the Zoe's pixel density without extra material work.

For it:
- **Performance at scale.** Hundreds or thousands of specks, sparks with sub-emitters, or noise/turbulence and collision are where a GameObject per particle loses badly, and the Particle System is the right tool.
- **Existing authored assets.** Designers' existing Particle System prefabs could be fired on the Chunks clock.

Suggested shape if it is built: a "Particle System" producer that spawns a picked prefab at its delay (like Pyre Blast does for Pyres), with a Layer slot. The preview shows a labelled footprint disc only, as design §7 already anticipates ("Generic Particle Burst", out of scope). Palette Splash stays the pixel-true, per-particle-orderable spray; J1–J4 are what it needs.

## Verified by probe
- Every card edit was read back from the asset. The clock was 0.40 → 0.90 s.
- Menu rows and their tooltips (9 kinds; Trajectory, Trail and Hits greyed with reasons).
- The sprite search: 0 sprite hits for "Floating Disc"; the disc frames are `cell_000`…`cell_008` at 16 ppu, readable.
- **Standalone burst:** 30–32 `SplashParticle` renderers on sorting layer Default, orders **500…531** (one per particle, see T-0349), with colours sampled from `cell_001`.
- **Zoe-triggered burst (copy):** the Death row with UC4 fired on `Kill()`, and the specks' colours matched `cell_001`, not the live `cell_000`. The Hit row's Debris Scatter cut `ChunkSample_cell_000` pieces (the live frame).
- **With Sprite empty:** standalone specks were all (1,1,1); Zoe-triggered specks took live palette colours, all emitted near the origin.
- **Code:** `PaletteSplash.ResolveSprite` never reads `ctx.SampleSourceOverride`, and its spread is `±spreadDeg/2`. `BuildChunksRefRow` Public on a private row only rebuilds.
- The Zoe copy's file holds two SpawnChunkFx refs (private and UC4 guid); the owner's Floating Disc is unchanged (git shows it clean).

## Verified by eye
- `01` empty recipe; `02` Add menu; `03` Palette Splash defaults with the half-disc cone and empty alpha curve.
- `04` the spray over time, blue schematic dots.
- `05` the Zoe copy's Death rows: the old Private row switched off, the new Public row on UC4, and its live-sample preview.
- `06` Sprite empty, with no warning.
- `20` standalone specks in the disc's colours.
- `21` the Zoe copy alive, the moment of death, and specks flying.
- `22` Sprite-empty standalone (white) beside the Zoe-triggered burst (live colours).

## Not verified
- No human mouse; the Unity sprite picker and the OS colour dialog were never opened.
- The Zoe copy was killed from code (`ZoeSpawner` + `Health.Kill()`) in an unsaved scene at timeScale 0.25 and 1. No real hit from a weapon was used.
- Mirage handoff was not run (MirageStage is off limits).
- Debris Scatter → Sampled as an alternative route to UC4 was read in code and seen via the Zoe's own Hit row, not built as its own recipe.
