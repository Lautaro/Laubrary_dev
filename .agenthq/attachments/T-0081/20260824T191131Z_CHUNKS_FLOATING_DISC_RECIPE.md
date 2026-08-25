# Recipe — blow up a character, with blasts behind, between and in front of the pieces

This is the worked example of the thing Chunks exists for: **one authored asset that composes several effects into one explosion, with depth you chose rather than depth you got.** Nothing here needs a line of code. Every value below is set by clicking in the Chunks window.

The shipped example is `Assets/Demos/ChunksDemo/Floating Disc Blowup.asset`, fired by the **C** key in `Assets/Demos/ChunksDemo/ChunksDemo.unity`.

![The effect](../../../../Demos/ChunksDemo/FloatingDiscBlowup.png)

## What it does

The Zoe **Floating Disc** breaks into four recognisable pieces of its own artwork. Behind them, one big **Old School Explo 2 Plus**. Between them and in front of them, two rings of **Proper Blast**. The character is hidden while its pieces are in the air.

## Why this needed new capability

Two things blocked it before, and both are worth knowing because they shaped the fix.

**A spec held exactly one blast.** `pyreSpawn` was a single reference, and the standalone Spawn Formation module had no blast of its own — it borrowed `pyreSpawn`'s and *superseded* it. So a spec could fire one blast, or several copies of that same blast, never two different ones at two depths. "Behind AND in front" was not expressible. `ChunkSpec.blastGroups` is the fix: a list of `PyreSpawnModule`, each with its own blast, its own layer slot, and its own optional formation.

**The pickers were dead ends.** `pyreSpawn.source` is typed to `IChunkEffectSpawner`, whose only implementor was the `PyreSpawnSource` wrapper asset — and zero of those existed in the project, so the picker opened an empty browser and its "New" made a blank wrapper you could only fill from the raw Inspector. Same story for fracturing a character: the Fragment Slicer took a raw `Sprite`, so a Zoe could not be picked at all. Now `Laubrary.Pyre.Pyre` implements `IChunkEffectSpawner` + `IChunkAnimation` + `IVisualPreview` directly, and `Laubrary.Zoetrope.Zoe` implements `IChunkAnimation` — so you pick the real asset, with a real thumbnail, in one click. The wrappers remain, and are now purely the override case (this Pyre, at 12 fps, looping, in this one place).

Both implementations live on the Pyre/Zoetrope side, never in Chunks: Pyre and Zoetrope already reference Chunks, so Chunks referencing them back is an assembly cycle Unity refuses to compile.

## Authoring it, step by step

![The authoring surface](../../../../Demos/ChunksDemo/FloatingDiscBlowup_Authoring.png)

**1. Declare the depths first.** In the **Layers** section, add four slots in back-to-front order: `Back Blast`, `Between`, `Fragments`, `In Front`. Base order 600, step 10. This list is the whole of "behind / between / in front" — everything below just picks a slot from it. (Leave the sorting layer empty unless you have a real one; an unknown name is a warning Chunks deliberately refuses to cause.)

**2. Fracture the character.** In **Fragment Slicer**, set **Source Visual** to the Zoe `Floating Disc`. The slicer cuts the first frame of whatever it is handed, so a Zoe, a Pyre or a plain sprite all work — the Source Sprite field below is the fallback and says so when it is being overridden. Pieces 4, Min Area 8 px (the disc is only 15×15 px; the old default of 24 would reject every cut), spread 360°, slot `Fragments`.

**3. The blast behind.** The first blast section (**Pyre Spawn**) takes `Old School Explo 2 Plus`, scale 3, slot `Back Blast`.

**4. The blasts between and in front.** Press **+ Add another blast…** twice. Each new group gets its own name, its own blast and its own slot. Group 2 "Between": `Proper Blast`, **Several** on, Ring of 4, radius 0.5, slot `Between`. Group 3 "In front": the same blast, Ring of 4, radius 0.8, smaller scale, slot `In Front`. Two groups holding the *same* blast at different depths is the normal case, not an edge case — it is the whole point of the list.

**5. Stagger it.** In the **Timeline**, give each lane a delay: background 0, fragments 0.06, between 0.10, in front 0.16. Lanes are keyed by the group's position, not its name, so renaming a group keeps its delay.

**6. Turn off what you are not using.** Emission count 0. Plain debris chunks take the emitter's flat sorting order, *not* the layer stack, so any non-zero count would punch straight through the depths you just authored.

## Things that will bite you

**A Pyre asset can be mostly empty.** Measured on the two used here: `Old School Explo 2 Plus` is bright for frames 1–7 and then a dark smoke tail to frame 15; `Proper Blast` has content only in frames 1–8 and its remaining **18 frames are completely blank**. That is a property of those authored assets, not of Chunks — but it means a blast's visible life can be much shorter than its frame count suggests, and it is worth checking before blaming the recipe when something "doesn't show".

**The fracture needs a readable texture.** `FragmentCutter` returns nothing when the source texture is not Read/Write Enabled, which is the Unity default for imported art. The slicer now logs a specific one-time warning naming the sprite instead of silently producing nothing.

**Sizes are in world units, and blasts are 1 unit at scale 1.** All the Pyre blasts here render 64 px at 64 PPU. The Floating Disc is 0.94 units. If an effect looks like nothing is happening, check the camera's orthographic size before changing the recipe.
