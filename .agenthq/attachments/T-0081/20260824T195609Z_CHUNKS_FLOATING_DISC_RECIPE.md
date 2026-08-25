# Recipe — blow up a character, with blasts behind, between and in front of the pieces

This is the worked example of the thing Chunks exists for: **one authored asset that composes several effects into one explosion, with depth you chose rather than depth you got.** Nothing here needs a line of code. Every value below is set by clicking in the Chunks window.

The shipped example is `Assets/Demos/ChunksDemo/Floating Disc Blowup.asset`, fired by the **C** key in `Assets/Demos/ChunksDemo/ChunksDemo.unity`.

![The effect](../../../../Demos/ChunksDemo/FloatingDiscBlowup.png)

## What it does

The Zoe **Floating Disc** breaks into five recognisable pieces of its own artwork. Behind them, one big **Old School Explo 2 Plus**. Between them and in front of them, two rings of **Proper Blast**. The character is hidden while its pieces are in the air.

![The sequence](../../../../Demos/ChunksDemo/FloatingDiscBlowup_Sequence.png)

The three depths are separated **in time as well as in space**, because that is what makes depth legible: the background blooms first and stays wide, the small "between" pops appear among the pieces while they are still close in, and the big "in front" pops burst last and directly over them. Stacked at one instant on one radius, the same three layers read as a single orange smear — which is exactly what the first version of this recipe did.

## Why this needed new capability

Two things blocked it before, and both are worth knowing because they shaped the fix.

**A spec held exactly one blast.** `pyreSpawn` was a single reference, and the standalone Spawn Formation module had no blast of its own — it borrowed `pyreSpawn`'s and *superseded* it. So a spec could fire one blast, or several copies of that same blast, never two different ones at two depths. "Behind AND in front" was not expressible. `ChunkSpec.blastGroups` is the fix: a list of `PyreSpawnModule`, each with its own blast, its own layer slot, and its own optional formation.

**The pickers were dead ends.** `pyreSpawn.source` is typed to `IChunkEffectSpawner`, whose only implementor was the `PyreSpawnSource` wrapper asset — and zero of those existed in the project, so the picker opened an empty browser and its "New" made a blank wrapper you could only fill from the raw Inspector. Same story for fracturing a character: the Fragment Slicer took a raw `Sprite`, so a Zoe could not be picked at all. Now `Laubrary.Pyre.Pyre` implements `IChunkEffectSpawner` + `IChunkAnimation` + `IVisualPreview` directly, and `Laubrary.Zoetrope.Zoe` implements `IChunkAnimation` — so you pick the real asset, with a real thumbnail, in one click. The wrappers remain, and are now purely the override case (this Pyre, at 12 fps, looping, in this one place).

Both implementations live on the Pyre/Zoetrope side, never in Chunks: Pyre and Zoetrope already reference Chunks, so Chunks referencing them back is an assembly cycle Unity refuses to compile.

## Authoring it, step by step

![The authoring surface](../../../../Demos/ChunksDemo/FloatingDiscBlowup_Authoring.png)

**1. Declare the depths first.** In the **Layers** section, add four slots in back-to-front order: `Back Blast`, `Between`, `Fragments`, `In Front`. Base order 600, step 10. This list is the whole of "behind / between / in front" — everything below just picks a slot from it. (Leave the sorting layer empty unless you have a real one; an unknown name is a warning Chunks deliberately refuses to cause.)

**2. Fracture the character.** In **Fragment Slicer**, set **Source Visual** to the Zoe `Floating Disc`. The slicer cuts the first frame of whatever it is handed, so a Zoe, a Pyre or a plain sprite all work — the Source Sprite field below is the fallback and says so when it is being overridden. Pieces 4, Min Area 8 px (the disc is only 15×15 px; the old default of 24 would reject every cut), spread 360°, slot `Fragments`.

**3. The blast behind.** The first blast section (**Pyre Spawn**) takes `Old School Explo 2 Plus`, scale 4, slot `Back Blast`. It is deliberately much larger than everything else: a backdrop reads as a backdrop by being wider than what sits on it.

**4. The blasts between and in front.** Press **+ Add another blast…** twice. Each new group gets its own name, its own blast and its own slot. Group 2 "Between": `Proper Blast`, **Several** on, Ring of 4, radius 0.5, scale 0.8–1.05, slot `Between`. Group 3 "In front": the same blast, Ring of 5, radius 1.15, scale 1.35–1.7, slot `In Front`. Two groups holding the *same* blast at different depths is the normal case, not an edge case — it is the whole point of the list.

Note the scales: the *nearer* ring is the *bigger* one. Small-behind / big-in-front is ordinary perspective, and it does more to sell the depth than the sorting order does — the sorting order only decides who wins where they overlap.

**5. Stagger it.** In the **Timeline**, give each lane a delay: background 0, fragments 0.08, between 0.26, in front 0.46. Lanes are keyed by the group's position, not its name, so renaming a group keeps its delay.

Inside each group, **Stagger s** 0.08 / 0.09 with **Order** `Sequential` makes its ring sweep round rather than pop all at once. Keep **Jitter** well under Stagger s — jitter larger than the beat it decorates turns the sweep into noise (the tool now caps it at the stagger value for you, but authoring it sanely is still clearer).

**6. Turn off what you are not using.** Emission count 0. Plain debris chunks take the emitter's flat sorting order, *not* the layer stack, so any non-zero count would punch straight through the depths you just authored.

## Things that will bite you

**A Pyre asset can be mostly empty.** Measured on the two used here: `Old School Explo 2 Plus` is bright for its first frames and then a dark smoke tail; `Proper Blast` has only one enabled layer, windowed to frames 0–10 of 26, so its remaining **15 frames draw nothing at all**. That is a property of those authored assets, not of Chunks — but it means a blast's visible life can be much shorter than its frame count suggests, and it is worth checking before blaming the recipe when something "doesn't show".

**A directly-picked Pyre now plays at its own authored rate.** Chunks used to spawn every directly-picked Pyre at a hardcoded 24 fps while the Pyre window and the Pyre baker both used the asset's own `previewFps` (12 by default). The same blast therefore ran at *double speed* when Chunks fired it — every effect here was on screen for half as long as it was authored to be, which is a large part of why the blasts "weren't there". Chunks now honours `previewFps`. If you re-time an old recipe, expect every delay to want roughly doubling.

**The fracture needs a readable texture.** `FragmentCutter` returns nothing when the source texture is not Read/Write Enabled, which is the Unity default for imported art. The slicer now logs a specific one-time warning naming the sprite instead of silently producing nothing.

**Sizes are in world units, and blasts are 1 unit at scale 1.** All the Pyre blasts here render 64 px at 64 PPU. The Floating Disc is 0.94 units. If an effect looks like nothing is happening, check the camera's orthographic size before changing the recipe.
