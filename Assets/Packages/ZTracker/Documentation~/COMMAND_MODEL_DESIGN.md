# Command model and dynamic parameters: design note (T-0595 / T-0596)

This note fixes the model every later commands ticket builds on: FX column counts and stacking (T-0597), bound columns and the bar view (T-0598), context-aware suggestions (T-0599), colour coding (T-0600), envelope columns (T-0601) and selection/copy/paste (T-0602). The owner's wording is in [COMMANDS_OWNER_SPEC.md](COMMANDS_OWNER_SPEC.md) and wins over anything here.

## 1. What a dynamic parameter is

A dynamic parameter is a setting the user has given a two-character command id and a short friendly name, so a row in a track's FX column can set it. Each one is stored once in the song as a definition with:

- a **stable identity** (a generated key that never changes, used by anything that must survive a rename: envelope columns, bound columns, the engine's compiled record),
- the **command id** (two characters, renamable),
- the **friendly name** (1 to 24 characters, entered together with the id),
- the **scope** (Instrument, Track, Effect or Song) and the **target** inside that scope,
- the **value range** (what `00` and `FF` mean, in the target's own units),
- a **colour** (view only; see section 9). A new definition is given an automatic colour that no other command of the song uses yet, stored with it so it never changes with a rename; a definition saved without one (transparent) is drawn with a colour derived from its stable identity.

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
- **Smoothing:** a change ramps linearly to its new value. Track gains, pans and width are interpolated per sample over 5 ms; instrument parameters, macros and effect settings are read once per render slice, so they ramp over 10 ms in 4-sample steps (the renderer shortens its slices only while such a ramp runs). Measured: the largest sample-to-sample step during any of these changes stays within the steady signal's own largest step. A value equal to the current one starts no ramp, so a default command is bit-identical to no command. Tempo is a timing value and is not ramped.
- **Before a voice exists:** an instrument value is stored on the instrument and every later note reads it; mixer and effect values exist without voices. Nothing is lost when the command comes first.
- **Stop, Play, seek, loop:** Play and Stop restore every target's authored value. Seek replays the song silently up to the target row and lands on the value that row would have, without a ramp. Looping keeps the current value, as other mixer and macro values do.
- **Envelope hook:** an automation lane whose target kind is *Dynamic* and whose parameter is the definition's stable identity drives the same record. For a linear lane the engine ramps from each evaluation point to the next one over exactly the time between them, so the value follows the drawn line continuously instead of jumping at ticks. T-0601's envelope column stores its points as such a lane.
- **Rules kept:** no allocation or managed work on the audio side, fixed-size records, and live edits of names, colours and ids do not need a restart; adding, removing or retargeting a definition does (it changes the compiled layout), and says so.

## 8. What later tickets read

| Ticket | Reads |
| --- | --- |
| T-0597 FX columns | ids are plain two-character commands; stacking needs nothing new |
| T-0598 bound columns | a column stores the definition's stable identity (a built-in, whose id never changes, is stored as its id); the bar is `u`; binding never moves a stored command, because the engine keeps per-column command memory |
| T-0599 suggestions | `ZTrackerCommandRegistry.InContext` plus the built-in catalogue |
| T-0600 colours | `color` (transparent = derived from the stable identity); built-ins by kind (section 9) |
| T-0601 envelope columns | an automation lane with target kind Dynamic and the stable identity (section 10) |
| T-0602 copy/paste | cells keep the typed id; columns keep the stable identity (section 11) |

## 9. Suggestions and colours (T-0599 / T-0600)

**Suggestions.** The first character typed in a command field opens a list under the cell (above it when there is no room below), never over it. In a track FX column it lists, in this order, the song commands that belong on the row (`ZTrackerCommandRegistry.InContext`, given the sequence slot so a note column keeps the instrument it selected in an earlier slot, as the engine's per-column instrument memory does), the track's imported device commands, then the built-in commands of that column type; local sample-effect and volume/pan columns list their built-ins. Each row shows the id as a colour chip, the friendly name and the scope (Instrument, Track, Effect, Song, Device or Built-in). Typing ranks ids that start with the text first, then names with a word that starts with it, then (from three characters) names with a word containing it. Two characters that name any command (offered or not) are entered at once, as are two characters that match nothing; otherwise the list stays open and its first row enters exactly what was typed. Up/Down/PageUp/PageDown choose, Enter or Tab enters, Esc cancels (Edit mode is untouched), Backspace removes a character, a click enters a row; the grid keeps keyboard focus throughout. The list is built once when it opens and typing only re-ranks an index array and rebinds a fixed pool of eight rows (measured: 427 commands open in about 2 ms, a narrowing keystroke 0.1 ms, 0 bytes allocated per re-ranking). A song command typed on a row it does not belong to still plays; the help line says so.

**Colours** are view only: nothing that saves, renders or copies reads them.

| Kind | Built-in ids | Text colour | Contrast on grid / beat row / selection |
| --- | --- | --- | --- |
| Pitch | 0A 0U 0D 0G 0V, U D G | #86C0FF | 8.0 / 7.2 / 5.1 |
| Volume | 0I 0O 0T 0C 0M 0L, I O | #8BE07A | 9.4 / 8.6 / 6.0 |
| Pan | 0N 0P, J K | #C8AEFF | 7.9 / 7.2 / 5.0 |
| Sample | 0S 0B 0E 0Y, B Y | #F2C66D | 9.5 / 8.6 / 6.0 |
| Timing | 0Q 0R 0X, C Q R | #FF9E7A | 7.6 / 6.8 / 4.8 |
| Track and devices | 0J, imported device ids | #5FD7D7 | 8.8 / 8.0 / 5.6 |
| Song | ZT ZL ZK ZG ZB ZD | #FFA6D8 | 8.5 / 7.7 / 5.4 |

Built-ins are this coloured text on the dark grid (#1A2732, measured on the ZTracker skin). Song commands are dark text (#141414) on their own colour; the ten automatic colours (#8FD3FF #A6E39A #F5D76E #F7A8C8 #C5B3FF #FFB97A #7FE0D0 #D9E37A #E0B8F0 #B8C9E8) give 9.9 to 13.4:1, and 7.5 to 9.1:1 when the cell is selected (the colour is blended toward the selection blue). A colour chosen in Song › Commands that is too dark for dark text gets white text. An id that names nothing is white on red (#C62828, 5.6:1); no automatic colour is red. The cursor cell keeps the cursor's own colours. The same colours are used in FX columns (including local sample-effect and volume/pan commands), bound columns (the title, a dimmed id, and the bars), the suggestion list and Song › Commands. Another command inside a bound column keeps its own colours and is marked by an amber rule above and below.

## 10. Envelope columns (T-0601)

An envelope column is a line drawn down the pattern that drives one command smoothly: a point at the left edge is `u = 0` (what `00` means for that command), at the right edge `u = 1` (what `FF` means), so a song command spans its own range and a built-in uses its own curve.

**Which commands.** Any song command (dynamic parameter), and the built-ins that set a continuous value: `0L` (track pre gain, the same silence…+3 dB curve as the typed command) and `0P` (track pre pan, 00 left, 80 centre, FF right). Built-ins that are events or per-note effects (slides, retrigger, cut, sample offset…) and the song tempo (it changes the row grid itself) are not offered.

**Storage.** A track keeps its envelope columns in `TrackData.envelopeColumns`, each with a permanent `uid` and the `command` it drives (a song command's permanent key, so a rename keeps the binding, or the built-in id; empty while unbound). The points live per pattern, in that pattern track's automation lane whose id is `envelope:<uid>` and whose target is kind *Dynamic* with the command (section 7's hook), created with the first point and removed with the last. Points sit on rows (0 … lineCount − 1); each point's `segment` says how the line leaves it: straight to the next point (the default) or held until the next point, then a jump. Curves were not added: the engine ramps straight between evaluation points, so a curve would only be approximated and cost a per-segment shape parameter; a curve can be drawn with several straight points. Songs saved earlier read with no envelope columns and every older point as "as the lane says", so they open unchanged and render bit for bit as before (four songs, including the demo, compared against the code before the change).

**Where it drives the command.** Only between its first and last point in a pattern. Before the first point and after the last, the command keeps whatever value it last had — an earlier pattern's line, a typed command, or after Play/Stop/Seek its authored value. Between points it is interpolated (straight) or held (held segment). Rows between two points that have no point of their own are on the line; nothing is stair-stepped per row.

**With typed commands for the same command** (in an FX column of the same track for a built-in, anywhere for a song command): last writer wins by row. A row that carries a typed command for that command takes the typed value for that whole row; the line resumes on the next row. A typed command before the first or after the last point simply holds, because the line does not drive the command there. For a track with a `0L`/`0P` envelope column, typed `0L`/`0P` on that track write the same engine record as the column, so they too ramp over 5 ms instead of stepping.

**Smoothness.** The engine evaluates the line at every tick and at every point and ramps exactly from one evaluation to the next (per sample for track gains/pans, 4-sample steps for instrument and effect settings), so the rendered value follows the drawn line. Wherever it would otherwise jump onto a straight segment — the line begins mid-song, a held segment ends into a straight one, or a typed row ends — it glides onto the line within one tick instead. A held segment's jump into another held value is smoothed over 5 ms. At Play, Stop/Play and Seek the first row lands exactly on the line. Measured (`TrackerEnvelopeCheck`): a straight line on a song command stays within 0.00006 of the drawn value (on a 0…1 range); held values do not move; a `0L` line follows the dB curve within 0.0013 gain (straight ramps between ticks); seek lands within 0.00005 of the line; 64/333/1024-frame partitions agree; 0 bytes allocated while ramping.

**Seek, loop, stop.** Seek replays silently and lands on the value the target row would have. Stop/Play restore the authored value. Looping keeps the last value until the line begins again on the next pass.

**Engine records.** A song command's column drives that command's existing record. A `0L`/`0P` column gets one record per track and command, created only when such a column or lane exists, mapped through the typed command's curve; songs without one compile exactly as before. Lane points carry a per-segment flag; a lane carries an "envelope" flag (drive only within its span) and a record remembers the row on which a typed command took it. All fixed-size, no managed work on the audio side. Adding or moving points is a live edit; binding a column to a built-in adds a record, which (like adding a song command) applies at the next Play.

**Editing.** Pattern view: + / − on the track header (beside the FX ones, on headers wide enough for both) and an Env count in the track settings; a new column opens its bind menu under its title (song commands and the built-ins, searchable; Alt+B in the column; right-click its title). Edit mode: click adds a point at the pointer, dragging a point moves it (rows snap, the value is free; Alt snaps to sixteenths), dragging across rows draws a point per row, right-click a point deletes it, right-click the line picks straight or held (also in the Cell box). Keyboard: Left/Right nudge by 8/255 (Alt by 1), two hex digits set the value, Delete removes the point; Ctrl+Left/Right leave the column. One Undo step per gesture. Row height is unchanged (20 px); the column is 48 px wide.

**Copy and paste.** Envelope cells take part in selection, copy, cut, paste, Paste Insert and clear like any column: a cell copies the points in its row. Paste Insert moves the column's points down by the pasted height (points pushed past the last row are dropped and counted). Points can be pasted into another envelope column (a different command just reinterprets the 0…1 values); pasting into an unbound column is refused with a note. Other column types never take envelope cells (section 11).

## 11. Pasting across column types (T-0602)

A copied cell lands in the column under it when both hold the same kind of thing: the same column type; volume and pan (a 00–80 value or a column command; a command that does not belong there is kept and reported, as when typed); a note column's sample command and a track FX command (id with id, value with value); two envelope columns. Anything else (a note into a volume column, an envelope into an FX column, an event anywhere else) is refused, counted and named on the status line ("⚠ Paste skipped 2 cells: a note cell cannot go into a volume column."), and Paste Insert does not push a column whose cells it refuses. The rule is `PasteFits` in the window; `CheckClipboardMatrix` exercises every column type with select, copy, cut, paste, Paste Insert, clear and Undo.
