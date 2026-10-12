# Owner's request: commands, bound columns, envelope columns and related fixes (2026-10-12)

This is the owner's wording, kept verbatim. Every ticket in the ZTracker overhaul group that came from it points here. Where a ticket paraphrases, this text wins.

## Commands and dynamic parameters

Track commands and dynamic parameters. Control parameters from instruments, tracks, fx chains, song etc.

If we have 2 chars as id then we could still have many more combinations than would be necessary. It's only the readability that suffers.

I suggest bound columns and the possibility to stack commands or have them in bound columns.

How about having bound columns as an optional view when editing.

We can have several layers of displaying commands.

Most generic: user clicks + and - to set how many fx columns should be shown. If there are more commands than columns in a row you get a +2 appearing. Clicking it expands so there are as many fx columns as the row that has the most.

On top of this user can set a temp bound column by adding a command id at the column. Then that column is reserved to show only entries of that command. Ids of built in commands are reserved but user can choose any other combination for a dynamic parameter. Bound columns has an alternative view of the value. If enabled instead of showing the value as chars it's shown as a horizontal bar. 0 is to the left and max is to the right. This is a fast way for user to plot values by sight where the exact number is not that important.

User can give a parameter such a dynamic id (should be validated so it's not taken already) and at the same time add a short descriptive friendly name to make it easier to identify what the command does.

When typing in commands in fx column an intellisense style popover can help out. It should be context aware. For example in an fx column only show ids attached to an instrument playing on that row in that track or for example a dsp fx that sits on that track.

We can also color code commands to make them easier to identify. Custom user commands could be color text dark bg. For user defined commands the text could be dark on color bg.

On top of this we will have a new type of optional column. Envelope. It will be a line style envelope but turned 90 degrees. It follows the track downwards. Points to the left is 0 and to the right is 1. This way commands can be programmed through the pattern with smooth transitions. Several envelope columns can be added, just like fx columns.

All type of columns should be selectable and copy and paste should work as normal.

There seems to be a bug where the first row of a selection does not appear to be selected.

Also add to copy paste also insert which pushes the rows that would be over written before pasting into the pattern.

Also, for FM synth we need a better set of configurations. I will post an image with a collection that should be available. Most are not 4op but rather 2 or 3 only.

## Standing instruction for this batch

Use AgentHQ, group "ZTracker overhaul". Previously created tickets are done first (T-0572, the animated reorder revision, was the only one still open). One ticket per task, kept updated while subagents work.

## FM configurations image (sent later the same day)

`FM_SINGLE_CARRIER_SETUPS.png` in this folder: "Single-carrier FM setups, 1 carrier, up to 4 operators". C is the carrier; A, B, D are modulators. The ten setups, in the image's order: 1. A→C. 2. A→B→C. 3. A+B→C. 4. A→B→D→C. 5. A+B+D→C. 6. A→B→C and D→C. 7. A+B→D→C. 8. A→B, A→D, and B+D→C. 9. A→B→C and A→C. 10. A→B→D→C and A→C. The owner's point: most useful setups use only 2 or 3 operators, so the editor must offer these (and the right operator count for each) rather than only four-operator layouts.

## FM feedback (added later the same day)

"Also for modulators, allow feedback." Every modulator (A, B, D) can feed back on itself: a per-operator feedback amount, usable in every one of the ten setups. Defaults to off so existing sounds do not change.
