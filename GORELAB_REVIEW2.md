# GoreLab port: second independent review

Reviewer: independent reviewer, 2026-10-04, branch `feat/gorelab` at commit e9564618 ("GoreBody.Attach; demo uses the rig's tuned recipes"). Compared against the web prototype in `D:\CODEZ\GoreLab`, the port design, the algorithm specification and the first review (`GORELAB_REVIEW.md`). No source file was edited. A throwaway rig created in a temporary folder for the walk was deleted again, the review window was closed, the editor was returned to Edit mode, and `git status` is clean, exactly as before the review.

## 1. Verdict

The engine and the game side are in good shape and I verified that by running them: 43 of the 45 prototype golden cases reproduce exactly (the two that do not run are the blood clean-up cases, which were deliberately not ported), and in the demo every damage type wounds every one of the eight imps wherever the imp actually has a tagged body part. Since the first review, three of its main complaints were fixed: the invisible first shape on an untagged frame, the missing frame speed tools (there is now Copy to next, Copy to rest and Auto-tag), and the Test tab's bullets not matching the game's (they now share one hole-visibility rule and the same wound counter); a Target choice and a Play walk toggle were also added.

It is **not yet ready to hand over as "set it up yourself from an empty state"**. The authoring window can now take a new user from nothing to a tagged, tested rig, but three things still stop a human: (1) getting the rig onto a character in a game still needs code and nothing in the tool says so; (2) the window cannot show which frame is selected and only shows completeness for one body member at a time, so on a 32-frame character you cannot tell what is left to do; (3) the copy tool copies a shape to the same pixel coordinates on frames of a different width, so every copied shape is off by a pixel or two and the user does not know why. There is also one small runtime bug (Reset leaves the old wounded picture on screen until the animation next changes frame, shown mirrored on left-facing characters) and a real trap for any game other than this demo: the game can only wound sprites whose textures are marked readable, while the editor window reads any sprite, so a rig that tests perfectly in the window can silently do nothing in play.

It is ready to hand to the owner **as a working preview to try**, with the list in section 6 as the next round.

## 2. Port versus prototype

| Area | Status | Notes |
|---|---|---|
| Tagging model (per frame, per member: centre, half sizes, depth, squareness, up and forward vectors) | Ported faithfully | Tag normalisation and mirroring reproduce the prototype goldens. One deliberate generalisation: any number of members, and each member owns overlapping pixels against every later member (with Head then Torso this is exactly the prototype's "head owns the pixel"). |
| 3D removers (flat slice plane with ragged edge, capsule tunnel, stacking in groups) | Ported faithfully | Line-of-sight solids for the ball and the rounded box, the ragged-edge noise, the capsule "hull of two spheres and a cylinder" quirk and group stacking all match the goldens bit for bit (the integer outputs) and to 1e-6 (the floats). |
| Line-of-sight evaluation per pixel, chunk extraction, pieces, crumbs, bleed points | Ported faithfully | All cut, frame-cut and bake goldens pass. The working buffers are reused between cuts and grow only. |
| Slice | Ported faithfully | Plane from the swipe, member chosen by "crosses most solid pixels", fly side away from the neck, Flip side. Generator goldens pass. |
| Cut (knife) | Ported faithfully | Groove of overlapping shallow capsules with breaks, sideways and depth wobble; goldens pass. |
| Bullet | Ported faithfully | Lands on the visible part of the aim line, digs straight in; up to ten candidate spots scored by "on how many frames of this walk direction would the hole be visible"; member picked from the wound counter and rig seed exactly as the prototype. The editor and the game now use the same scoring rule. |
| Shotgun (cone and straight-on) | Ported faithfully | Both modes match the goldens. |
| Remove head | Ported faithfully | One neck plane, never twice. Deliberate difference: which member is "the head" is a typed number in its settings (see 4.12). |
| Mirrored views are a rotation, not a reflection | Ported faithfully | A left-facing view is baked from mirrored pixels and mirrored tags and shown unflipped; I saw correct wounds on the three mirrored demo imps. The mirrored-frame goldens pass. |
| Paint masks: behind (stays as dark gore) and in front (never cut) | Ported faithfully | Paint, Erase, right-drag erase, brush 1 to 6, Fill shape, Clear; a pixel is in at most one layer. |
| Frames missing a needed tag play as drawn | Ported faithfully | Game falls back to the drawn sprite; the Test tab shows the red "not set up" warning, and the amber "the wound is on a side this frame does not show". |
| Undo / redo | Ported (verified by the first review; not re-run by me) | Every edit records the rig first and a drag is one step. Undo/Redo icons cannot grey out (Unity has no query for it). |
| Flying pieces, crumbs and bleeding | Ported with deliberate simplification | Pieces and crumbs fly as pooled bodies and land at the feet; bleeding pulses from the wound of the frame on screen and fades. No stain or floor pool where drops land (the spec's minimum asked for one). Spin is random rather than the prototype's off-centre kick. |
| Frame speed tools | Partly ported | Copy to next / Copy to rest copy shape, orientation and both masks; Auto-tag guesses a head, then a torso under it. Missing: the prototype's pixel-matching Carry (searching shifts and rotations, rotating the 3D vectors with the pose, a match percentage), and "Same shape on all". Auto-tag works one direction at a time, the prototype did every frame at once. |
| Test: Target choice, Play walk | Ported | Auto / Head / Torso, and the walk plays the direction's frames so a wound can be watched across the walk. Turning the walker is done by stepping directions on the Frame tab, not with arrows. |
| Fly-direction arrow during a test drag | Missing | The amber "what will come off" preview is there; the arrow is not. |
| Blood clean-up of painted blood in the source sprites | Missing (known, documented) | Its two goldens are not run. Cut imps keep their painted mouth blood. |
| Cut settings (seed, ragged amplitude and frequency, bone core) and wound colours | Ported as data, no editor | They live on the rig, but the GoreLab window shows none of them (see 4.9). |

Maths checked by reading as well as by the goldens: the slice-plane normal is built from the swipe's screen normal read through each member axis, as in the prototype; the hole-visibility score projects the capsule's mid point back to the screen with the frame's own tag, counts only frames that carry the member, skips pixels painted "in front", and for a non-head member skips pixels under the head, which is the prototype's rule. The per-member random streams use seed + 7919 times the member's position, which equals the prototype's "+7919 for the torso" for the two-member case.

## 3. Demo scene

Verified by running it (Play mode, wounds applied through the demo's own mouse handler with computed drag lines, then a game-view capture):

- Eight imps, one per direction, walking in place: yes.
- Mouse damage: a press-drag-release line is a swipe for Slice and Cut and an aim line for shots; a plain click becomes a short default line. The imp is chosen by where the line ends; the body part is never chosen by the mouse (the wound request asks for "any tagged member", and each damage type picks the member itself). Requirement met.
- Every damage type on every imp: with lines at head height all five types hit all eight imps (the only head misses were bullets on two side views where my aim line stopped short of the head, which is my test geometry). At chest height every type hits five imps; on the three backward-facing imps (back, and both back-side directions) only the shotgun hits, because the owner's imported data has no torso tags on those directions. The tool behaves correctly ("nothing visible changed, try another spot"), but a person clicking those three imps' bodies will think the demo is broken. The spec lists this as a data gap; it should be closed by tagging those torsos or said on screen.
- Small panel: damage-type choice, a Straight on toggle for the shotgun, Reset wounds, a hint line and a last-result line. It uses the rig's own tuned damage types.
- Reset: clears all wounds, but see bug 6.1: right after Reset the wounded picture stays until each imp's next walk frame, and on the three left-facing imps it is shown mirrored for that moment. I caught this by pausing, pressing Reset, and reading what each imp was displaying.
- Textures: the count went up while pieces were flying and came back down as they expired; no growth across wound/reset cycles.
- By eye: front imp sliced across the head, side imp with a knife groove, back imp with its head removed, front-side imp with a shotgun blast; wounds sit on the body and the mirrored imps' wounds look like the same body turned. The floating dark-red pixels where a removed head's outline was (noted in the first review) are still there.
- Panel style (minor): the Straight on toggle appears only for the shotgun and pushes the Reset button down when it appears; the instructional hint line is prose on screen. Both are small against the UI rules for a demo.

## 4. The editor window, walked cold from an empty state

What I did: opened the window with no rig, created a new rig through the window's own create path in a temporary folder, pointed it at the imp character, and looked at every state with real captures of the window (and the window's own layout audit, which reported zero findings on every capture). I tried to send synthetic pointer drags to draw a shape; they did not register in this session, and at that point the window was also changed by someone at the machine (it switched to the Test tab and Play walk was turned on and off while I was not sending anything), so I stopped driving it rather than fight a human for the window. **No drawing gesture was performed by me in this review; shape drawing, copy and undo were judged by reading the code plus the first review's synthetic walk.** No human has dragged in this window as far as I know.

Ranked by how much each blocks a person setting up a character from nothing:

1. **Getting the rig into a game needs code, and nothing says so (blocks a non-coder).** A Zoe character is spawned at run time, so there is no prefab to drop the wound component on. The demo adds it in code right after spawning and calls the wound function from its own mouse script. There is now a one-line helper for attaching it, but no menu, no field on the rig, no tooltip in the window and no reference page in the Laubrary skill tells the user that a character needs the wound component, which rig to give it, or that wounds only happen when game code (or the optional Health hook, off by default and fixed to the rig's first damage type) asks for them. Suggest: a "Gore rig" slot on the character or a spawn hook so the spawner attaches it itself, plus an API reference page in the Laubrary skill.
2. **You cannot see which frame is selected (annoys on every step).** The selected thumbnail in the strip looks the same as its neighbours in every capture (front frame 1 was selected each time). The only way to know is the small "Front 1/4" text at the bottom of the stage.
3. **"Done" only covers the chosen member.** The check marks on thumbnails and the tick/ellipsis on a direction's title describe only the member currently chosen in the Head/Torso switch. To know whether a frame is fully set up you switch members and scan again. With the demo's own data this hides that three directions have no torso at all. Suggest one small mark per member on each thumbnail.
4. **Copied shapes land in the wrong place on frames of a different width (misleading).** Copy to next / Copy to rest copy the shape to the same pixel coordinates on the next frame. Tags are stored relative to each frame's own top-left corner and the imp's frames differ in width by up to eight pixels, while the stage centres each frame, so a copied head sits one to four pixels off the head on the next frame. The paint masks are copied the same way. The tooltip says "then nudge it into place", but the user is not told why it is always off. At minimum, shift the copy by the difference in frame placement (the frames are bottom-centred on the stage, or aligned by pivot in the game); better, port the prototype's pixel-matching carry with its match percentage.
5. **The frame strip does not fit and scrolls both ways.** At a 1000-point window the three mirrored directions are off-screen behind a horizontal scroll, and an unneeded vertical scroll arrow sits beside the strip (noted in the first review, still there). Directions could wrap into a second row or shrink thumbnails.
6. **The first screen of a new rig repeats itself and points the wrong way.** With no character chosen, the strip says "No frames: set the rig's target (Frame tab)" and the stage says "No frames: set the rig's target on the Frame tab", while the Character and Animation fields are already on the current tab right beside them. Say it once, and point at the field that is on screen.
7. **The Test tab's guide text lies on an untagged frame.** On a rig with nothing tagged, the Test tab says "Drag across the frame to wound it"; the user drags, nothing happens, and only after the first drag does the red "not set up" warning appear. The guide should say the frame is not set up before the drag.
8. **Damage-type settings still render badly.** The section headings inside a damage type's settings ("Swipe", "Which side flies") still draw as small blue chips floating beside the fields instead of as dividers (first review item 8, unfixed). Every setting is a bare number box; values with a natural range (plane miss limit 0 to 1, neck tolerance, toughness) would be sliders under the UI rules.
9. **No place to edit the rig's cut and style settings.** The random seed, the ragged-edge amplitude and frequency, the bone core switch and the wound/blood colours are part of the rig but the window shows none of them; the user has to find the asset's raw Inspector. A small "Rig" group on the Test tab would do.
10. **Asset chrome eats the top of the window.** The generic asset row (picker, New, Browse, Duplicate, Rename, Delete and an unlabelled empty text box with no visible purpose) plus an asset "Tags" block take about three rows before the tool starts. Against the compact-style rule the tab row should be near the top. The unlabelled text box needs at least a placeholder and tooltip.
11. **Auto-tag works one direction at a time and the torso guess needs the head first.** Fine as a design, but the button gives no hint of the order until it fails with a toast; the prototype did all frames at once.
12. **Remove head asks for the head as a typed number** ("Head Member: 0"). Under the UI rules a reference to a declared thing is picked from the declared list, never typed; it should be a choice among the rig's member names.
13. **Smaller things.** Flip side is shown for every damage type but only affects Slice. The Character and Animation fields both stay visible on the Frame tab though only one is used. Undo/Redo never grey out (platform limit, noted). No fly-direction arrow in the Test drag. Turning the test walker is by the direction step buttons on another tab, not from the Test tab.

What is good and should be kept: the empty state has a working path (New, then pick a character, then frames appear grouped by direction with the mirrored directions marked read-only); the tab is the mode; one guide line on the stage always says what a drag will do; warnings sit in fixed slots so nothing jumps; the layout audit is clean; the Test tab uses the game's engine and now the game's bullet rule; tooltips exist on every control I could find in the code.

## 5. Modularity and performance seams

- **New damage type = one class: true.** A damage type is a small serialisable class with a name and one "turn this swipe into removers" function. The window finds every such class in the project, offers it under "Add…" and draws its public fields as controls with no editor change. The game calls whatever damage type it is handed. Caveats: a new type must be marked serialisable and have a parameterless constructor or it is silently not offered; and the demo's panel hard-codes the five standard types, so a new type does not appear in the demo.
- **New remover kind: true, with a constraint.** A game registers its own remover kind with an id and two functions (which stretch of a line of sight it removes, and what colour the exposed surface is), and the per-pixel loop calls it through the same path as the built-ins. The constraint: a remover's data is one fixed record (two end points, a normal, a distance, a radius, a back direction), so a custom kind must repurpose those fields. Fly direction for a custom kind is always straight up.
- **Bake scheduler seam: present, not implemented, as asked.** Each visible view asks a scheduler to bake it; the default runs the bake at once. The live component only shows a baked picture whose version is current or older, so a queued or spread-out scheduler can be dropped in. The wound itself bakes immediately on purpose, because the flying pieces are read from that bake.
- **Flat arrays: yes.** Pixels, masks and per-pixel working state are plain arrays reused between cuts; removers are plain structs; the inner loop does no per-pixel allocation and no LINQ. Two notes for the later Burst/threaded step: the working buffers are one shared static set, so two cuts cannot run at the same time; and each bake still copies the bleed-point list into a fresh array (a small per-bake allocation against the design's "no per-call allocation").
- One view baked at a time, on demand: yes. A view is (drawn frame, mirrored or not), each with one reusable texture and sprite.

## 6. Ranked fixes

Blocking (before handing it to someone to set up on their own):

1. Attaching gore to a character without code: let the character or the spawner carry a rig reference and attach the wound component itself, and write the API reference page for the Laubrary skill (attach, wound, reset, events, the Health hook).
2. Readable-texture trap: the game refuses sprites whose textures are not marked readable (it logs a warning and the wound returns "nothing changed") while the editor window reads them fine. Either read them the same way at run time, or have the window warn on the rig ("these frames cannot be wounded in a game: enable Read/Write") and offer to fix the import settings. Related, not verified: the game reads a sprite's packed rectangle while the window reads its declared rectangle, so on a tightly packed atlas with trimmed sprites the game's wounds would be offset from the authored tags.
3. Show the selected thumbnail, and show completeness for every member per thumbnail.
4. Make Copy to next / to rest compensate for frame placement (or port the pixel-matching carry), so copied shapes and masks land on the body.

Should fix:

5. Reset leaves the wounded picture up until the next animation frame (and mirrored on left-facing characters): on reset, and whenever the swap is undone, put the animation's current frame back on the renderer, not only the flip. On a still or single-frame pose the wound would stay forever.
6. Tag the torso on the back and back-side directions in the demo rig, or the demo's three back-facing imps cannot be wounded below the head except by the shotgun.
7. Damage-type settings: proper dividers, sliders for bounded values, a member picker instead of the typed head number.
8. Rig settings (seed, ragged edge, bone, colours) editable in the window.
9. Test tab guide on an untagged frame should say "not set up" before the drag; the "no frames" message once and pointing at the visible field.
10. Frame strip fits the window (wrap or smaller), no stray vertical scroll arrow.

Nice to have:

11. Fly-direction arrow during the test drag; turn left/right from the Test tab; Auto-tag for every direction at once; Same shape on all.
12. Floor stains and pools where blood lands; the prototype's off-centre spin for pieces.
13. Blood clean-up of painted blood in source sprites (the two remaining goldens).
14. Demo panel: reserve the Straight on row so Reset does not move; list the rig's damage types instead of a fixed five; move the hint into tooltips.
15. Compact the asset chrome above the tabs; label or remove the empty text box.

## 7. What was verified, and how

Verified by running:

- All GoreLab test methods (14 of 14) by reflection in the live editor, plus a per-case listing: 43 of 45 prototype golden cases pass (every engine case, and every damage-type generator case in both canvas and sprite coordinates); the two blood clean-up cases are skipped because that feature was not ported. This covers the two named test classes, not a Test Runner suite.
- The demo in Play mode: eight imps present; every damage type driven through the demo's own mouse handler on every imp at head and chest height (results in section 3); Reset; the reset display bug (paused, pressed Reset, read each imp's displayed picture, stepped one frame); texture count during and after wounds; console clean of GoreLab warnings and errors.
- The demo rig's data: 20 drawn frames, head tagged on all, torso on 12 (missing on all back and back-side frames), masks present on front heads and side torsos.

Verified by eye (captures of the real window and of the game view): the window's empty state; a new rig with no character; the frame strip and stage after choosing the imp; the Test tab with its damage-type settings; the wounded imps in the game view. The window's layout audit reported zero findings on each capture.

Not verified: I performed no drag in the window (synthetic drags did not register this session, and the window was in use by someone else partway through), so shape drawing, Copy to next/rest, Auto-tag, painting, undo/redo and Ctrl+Z/Y were judged from the code and from the first review's walk only. The trimmed-atlas offset risk is from reading, not tested. Phone-width and docked layouts were not checked. The wounded imps were not compared pixel for pixel with the prototype. No human has dragged in this window yet, as far as this review can tell.

## Follow-up 2026-10-04 (after second review)
Done: attach button + code-free attach, unreadable textures read through a GPU copy, declared sprite rect in the game, Reset restores the drawn frame, strip shows selected frame and per-member dots, Copy to next/rest shifts by frame size difference. Not done: demo rig torsos on back directions (owner tags), damage-type settings layout, rig settings in window.
