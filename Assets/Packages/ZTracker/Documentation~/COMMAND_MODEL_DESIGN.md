# Command model and dynamic parameters: design note (T-0595 / T-0596)

This note fixes the model every later commands ticket builds on: FX column counts and stacking (T-0597), bound columns and the bar view (T-0598), context-aware suggestions (T-0599), colour coding (T-0600), envelope columns (T-0601) and selection/copy/paste (T-0602). The owner's wording is in [COMMANDS_OWNER_SPEC.md](COMMANDS_OWNER_SPEC.md) and wins over anything here.

## 1. What a dynamic parameter is

A dynamic parameter is a setting the user has given a two-character command id and a short friendly name, so a row in a track's FX column can set it. Each one is stored once in the song as a definition with:

- a **stable identity** (a generated key that never changes, used by anything that must survive a rename: envelope columns, bound columns, the engine's compiled record),
- the **command id** (two characters, renamable),
- the **friendly name** (1 to 24 characters, entered together with the id),
- the **scope** (Instrument, Track, Effect or Song) and the **target** inside that scope,
- the **value range** (what `00` and `FF` mean, in the target's own units),
- an optional **colour** (transparent means "use the scope's default colour"; T-0600 decides the drawing).

The id is what a person types in a pattern cell. Everything else that points at the parameter (a column, an automation lane) points at the stable identity, so renaming an id never breaks a column.

## 2. Ids: characters, case, reserved and taken

- Exactly two characters, each a digit 0 to 9 or a letter A to Z. Ids are case-insensitive: typing `fc` means `FC`, and the song stores upper case. Readability is the only cost the owner accepted; 1,296 combinations are far more than any song needs.
- **Reserved for built-in commands:** every id starting with `0` (sample, instrument and mixer commands, `0A` … `0Y`) and every id starting with `Z` (song tempo and flow, `ZT`, `ZL`, `ZK`, `ZG`, `ZB`, `ZD`). The whole family is reserved, including combinations no built-in uses yet, so a future built-in can never collide with a song's own id.
- **Taken by an imported device:** an id whose first character is a device position (`1` … `9`, `A` … `Y`) addresses a parameter of an imported source device (the existing `abzz` device addressing). Such an id is taken while any track of the song declares a device at that position. Songs authored in ZTracker have no imported devices, so for them every non-reserved id is free.
- **Taken by another dynamic parameter:** ids are unique per song.
- The volume and pan columns' one-letter commands (`Ix`, `Jx`, …) and the MIDI markers (`M0` … `M3`) live in other columns and cannot collide with FX-column ids.

Validation answers with one plain reason, shown inline under the field while the user types: "Type two characters", "Only letters A–Z and digits 0–9", "Reserved for built-in commands (0…)", "Reserved for song commands (Z…)", "Taken by imported device 3 on track Lead", "Taken by ‘Filter cutoff’". The friendly name is validated at the same time ("Give it a name", "At most 24 characters").

If a song nevertheless contains a clash (edited outside ZTracker), the compiler keeps the imported device meaning and reports `DYNAMIC_ID_SHADOWED`; a duplicate dynamic id keeps the first definition and reports `DYNAMIC_ID_DUPLICATE`. Nothing is silently guessed.

## 3. Addressing: what a definition points at

The target uses the song's existing parameter-address record (the same one automation lanes use), filled per scope:

| Scope | Owner | Parameter | Examples |
| --- | --- | --- | --- |
| Instrument | the instrument's identity | a typed instrument parameter by its canonical name, or one of the eight macros | `volume`, `pan`, `instFilterCutoff`, `fmOperators.0.feedback`, macro 3 |
| Track | the track's identity | one of the mixer values | `preVolume`, `prePan`, `width`, `postVolume`, `postPan` |
| Effect | the track's identity and the effect's identity in that track's chain | the setting's index in that effect | a filter's cutoff, a delay's mix |
| Song | none | a song value | `bpm` |

Identities, never names or positions, select the owner, so reordering effects, renaming tracks or moving instruments in the instrument list does not retarget a command. Instrument effect chains are not addressable in this version (one instrument chain is copied per track that plays it); that can be added as a fifth scope later without changing anything above.

## 4. How a row's command finds its target, and what "in context" means

A command in a track's FX column is looked up by its id in the song's definitions; the definition names its target explicitly, so the command always drives exactly that parameter wherever it is written. Context decides which ids **belong** on a row, for suggestions (T-0599), colours and warnings:

- **Song** ids belong everywhere.
- **Track** ids belong on their own track.
- **Effect** ids belong on the track whose chain holds the effect.
- **Instrument** ids belong on a row where that instrument is playing in that track: the instrument last selected in any of the track's note columns at or above that row in the pattern. When no note column of the track has selected an instrument yet in that pattern, every instrument the track uses anywhere in the song counts.

The registry exposes this as one call (`ZTrackerCommandRegistry.InContext(song, pattern, track, row)`), so every later ticket asks the same question the same way. A command written outside its context still plays; the later suggestion and colour tickets decide how to flag it.

Dynamic commands belong in track FX columns. A note column's local sample-effect column only takes the built-in sample commands; a dynamic id there is preserved and reported, as any other out-of-place command is.

## 5. Values

A command's byte `xx` (00 … FF) becomes a fraction `u = xx / 255`, and the target receives `min + (max − min) × u` in its own units (reversed ranges are allowed). The same fraction is used everywhere:

- a bound column's bar view draws `u` from left (0) to right (1) (T-0598),
- an envelope column's points are `u` values 0 … 1 (T-0601),
- an automation lane aimed at a dynamic parameter carries `u` values.

Ranges default to the useful part of each target (for example 0 … 2 for a gain, −1 … 1 for a pan, the effect setting's own minimum and maximum) and can be changed in the commands list; the hard limits of the target clamp them.

## 6. Song file and migration

The song gains two fields: a **command model version** and the **list of definitions**. A song saved before this change has neither; Unity reads the missing version as 0 and the missing list as empty, and such a song plays exactly as before (checked by rendering, bit for bit). Opening never changes a song. The first time a parameter is given an id, that same Undo step sets the command model version to 1; nothing else in the song changes. A version newer than this ZTracker understands is shown read-only and refused for editing, the same rule the song's schema version already follows. The song's own schema version is not bumped, so the old projection and every existing reader are unaffected.

A rename changes the definition's id and rewrites every pattern cell that used the old id, in one Undo step. A removal deletes the definition only; cells that used it stay in the pattern (nothing the user typed is erased) and are reported as `DYNAMIC_PARAMETER_UNDEFINED` until the id is defined again or the cells are cleared.

## 7. Engine

At preparation each valid definition compiles to one fixed-size, reference-free record in the prepared song: which kind of destination it is, the destination's indices, the range, the authored value and its smoothing state. A command compiles to the existing addressed-parameter row operation pointing at that record. Nothing on the audio side looks at a string, a list or a definition.

- **When:** at the row's tick zero, with the other row setters; a song tempo command applies at row entry, before the row's duration is fixed, like `ZT`.
- **Smoothing:** a change ramps linearly to its new value over 5 ms. Track gains, pans and width are interpolated per sample; instrument parameters, macros and effect settings are stepped every 16 samples during a ramp, and not at all otherwise. A value equal to the current one starts no ramp, so a default command is bit-identical to no command. Tempo is a timing value and is not ramped.
- **Before a voice exists:** an instrument value is stored on the instrument and every later note reads it; mixer and effect values exist without voices. Nothing is lost when the command comes first.
- **Stop, Play, seek, loop:** Play and Stop restore every target's authored value. Seek replays the song silently up to the target row and lands on the value that row would have, without a ramp. Looping keeps the current value, as other mixer and macro values do.
- **Envelope hook:** an automation lane whose target kind is *Dynamic* and whose parameter is the definition's stable identity drives the same record. For a linear lane the engine ramps from each evaluation point to the next one over exactly the time between them, so the value follows the drawn line continuously instead of jumping at ticks. T-0601's envelope column stores its points as such a lane.
- **Rules kept:** no allocation or managed work on the audio side, fixed-size records, and live edits of names, colours and ids do not need a restart; adding, removing or retargeting a definition does (it changes the compiled layout), and says so.

## 8. What later tickets read

| Ticket | Reads |
| --- | --- |
| T-0597 FX columns | ids are plain two-character commands; stacking needs nothing new |
| T-0598 bound columns | a column stores the definition's stable identity; the bar is `u` |
| T-0599 suggestions | `ZTrackerCommandRegistry.InContext` plus the built-in catalogue |
| T-0600 colours | `scope` and `color` (transparent = scope default) |
| T-0601 envelope columns | an automation lane with target kind Dynamic and the stable identity |
| T-0602 copy/paste | cells keep the typed id; columns keep the stable identity |
