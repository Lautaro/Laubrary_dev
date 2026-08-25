# Assembling a split sprite character — the system, and what was fixed

Task T-0077. **Fourth revision, 2026-08-24.** Revisions 1 and 2 were write-ups that changed nothing and both had the join number wrong. Revision 3 built the mechanism (a `Pivot` anchor mode) and got standing close, which is why the reply to it was "it's better, but it's still off when running". This revision says exactly why running was still off, fixes it, and states the general rule the earlier revisions got wrong.

---

## 1. The answer in one paragraph

Revision 3 said the join is **one number**, in every direction, standing or walking. Half of that is right and half is the remaining bug. One number joins the *torso sheet* to the *legs sheet* — both are internally well drawn, so nothing per-direction is needed. But that number is between two **registered sheets**, and each sheet still needs its own registration point. The three run strips do not share the idle sheet's: the east and south run cycles stand one pixel higher on the ground than the idle sheet does. Registration is **per sheet**; the join is one constant *on top of* it. Revision 3 registered all four legs sheets to the canvas centre and stopped there, which is why standing came good and running did not.

## 2. Ground truth, and why it is trustworthy this time

Two artist-supplied references, both native resolution, both matched by exact-pixel template matching with **no scale to fit and no threshold to choose**:

- `.agenthq/attachments/T-0077/20260824T162806Z_Prototype-Trooper.png` — sixteen standing directions, assembled. All 16 torsos located at match **1.0000**, and all 16 legs placements recovered at **1.0000** (occlusion-aware: pixels the torso covers are excluded from the score). 364/364, 231/231, 223/223 and so on — every pixel, every channel.
- `.agenthq/attachments/T-0077/20260824T183208Z_proto_run.gif` — the run cycles for north, south and east, torso and legs combined. A clean 3× nearest-neighbour upscale of a 154×131 native composite; the upscale factor was pinned **structurally** (161,392 3×3 blocks checked, 0 non-uniform), not by match score. The north torso matches the raw `Upper-N.png` at **292/292 exact RGBA**, which also proves the GIF is raw sprite compositing with no lighting or tint applied.

Because both matches are exact, these are measurements, not estimates.

## 3. The numbers

Legs offset relative to the torso canvas, in source pixels, y positive downward. **Standing**, from the 16-direction reference:

| | N | NNE | NE | ENE | E | ESE | SE | SSE |
|---|---|---|---|---|---|---|---|---|
| dx | 0 | +1 | 0 | 0 | −1 | −1 | −1 | 0 |
| dy | 21 | 20 | 21 | 20 | 22 | 22 | 22 | 22 |

| | S | SSW | SW | WSW | W | WNW | NW | NNW |
|---|---|---|---|---|---|---|---|---|
| dx | 0 | +3 | +2 | +2 | +4 | +1 | 0 | 0 |
| dy | 21 | 20 | 21 | 21 | 22 | 20 | 21 | 20 |

**Running**, from the GIF — a free per-frame search over dy 10…26 on all 8 frames of each direction returns a single value per direction, identical on every frame:

| | N | E | S |
|---|---|---|---|
| dx | 0 | 0 | 0 |
| dy | **21** | **22** | **22** |

The shipped constant is (0, 21). So it is exact for running north, and one pixel high for running east and running south. That is the reported symptom.

**There is no per-frame bob.** The torso's position is bit-identical in all 8 frames of every direction in the artist's GIF, and the legs artwork's waist row is bit-identical across all 8 frames of the north and south sheets. An earlier pass on this task reported a 4–8 px per-frame anchor movement for north and south; that was an artefact of fitting a target which is mostly hidden behind the torso, and it is refuted by the wider search above.

## 4. What was changed

Two numbers, in data, no code:

`Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/draft/ProtoGuy_draft.asset` — the 8 recipe cells of **`LegsWalk_E`** and the 8 of **`LegsWalk_S`** move their registration pivot from `y = 16/35 = 0.4571429` to `y = 17/35 = 0.4857143`, and the lauminary is re-baked. `LegsWalk_N` is left alone, because north was already exact.

The walk strips were exported pre-cropped by (5, 9) out of the same 56×56 canvas the idle sheets use, so the canvas centre sits at `16/35` in walk-cell space. That is what revision 3 set, and it registers the walk sheets to the same canvas point as everything else. The measurement above says the artist did **not** draw the east and south run cycles on that canvas point — they sit one pixel high — so registering them to the canvas is registering them to the wrong place. The pivot is where that gets corrected, per sheet.

Measured in the running game, before and after — legs-to-torso offset in source pixels, against the artist's value:

| | before | after | artist | residual |
|---|---|---|---|---|
| N | 21 | 21 | 21 | 0 (control — must not move, and did not) |
| E | 21 | **22** | 22 | **0** |
| S | 21 | **22** | 22 | **0** |

Composite score against the artist's frames, shadow band excluded (§7): E **0.909 → 0.998**, S **0.974 → 1.000**, N 1.000 → 1.000. As a pixel count: east went from 58 wrong pixels to 1, south from 13 to 0. The footfall lines up better too — comparing the ground shadow's bottom row standing against running, south is now 81 vs 81 where it was 81 vs 80, and east 82 vs 81 where it was 82 vs 80.

## 5. The system — what the earlier revisions got wrong

**Registration belongs to the sheet; the join is one constant between two registered sheets.** `FrameRef.pivot` is per cell, so a sheet's registration is one number authored once and copied to its cells — and a sheet whose directions genuinely were not drawn on a common origin *could* carry one per cell, though for this character they were, so it should not. Nothing in the tool needed to change to express any of this. The rule that makes it work is: **register each sheet against a physical landmark the artist held constant — here the ground line the character stands on — and never against the canvas, the content bounding box, or one assembled reference picture.** The canvas is why the walk strips went wrong (they were exported on a different crop); the bounding box is why `Edge` mode moved the torso between gaits; the reference picture is the trap in §6.1.

What revision 3 got right and should be kept:

- **`AttachAnchorMode.Pivot` is the correct default.** The join is one authored offset between two registered origins, needing no per-frame painting, and — unlike `Edge` — it does not move when a clip's content bounds change. That was a real defect: `Edge.Top` sat 14 / 11 / 10 / 9 px above the pivot on the idle and three walk sheets, so changing gait moved the torso.
- **A `MetaLayer` anchor must never degrade silently into a bounds edge.** It now degrades to the nearest painted frame, then to the part's own origin, and warns once naming the part and the layer. The original defect was not a missing number; it was a missing number producing a plausible-looking wrong answer with nothing in the console to say so.

What revision 3 got wrong is narrower than it looks, and worth stating precisely because the obvious over-correction (§6.1) is worse than the bug. It was right that a constant joins the two halves. It was wrong that setting every legs sheet to the canvas centre *is* registration. The canvas is not a landmark — it is where the exporter happened to put the pixels, and the three run strips were exported on a different crop from a canvas the artist was no longer aligning to. Revision 3 checked its work by confirming all five sheets resolved to canvas (28, 28), which they did, and which is exactly the check that cannot see this bug.

## 5b. Three things found by actually driving the character, which no amount of pixel measurement would have shown

The first three revisions of this document, and the first half of this one, were arithmetic. Driving ProtoGuy around in Play mode and photographing him found three separate faults, two of them larger than the alignment bug:

**5b.1 The legs were steered by the aim channel, not the heading channel.** `DirectionChannel` is `Heading = 0, Aim = 1, Fixed = 2`. `ProtoGuy.asset`'s **Legs** part has a pose whose own channel is `Heading`, and then **both** of its rules set `overrideChannel: true, channel: 1` — Aim. So the walk cycle was chosen by where the crosshair pointed, not by where the character was travelling. Run west while aiming east and he moon-walks; run north, south, west and north-east while aiming east and all four render **identically**, all striding east. In the demo the aim falls back to the mouse cursor, so this fires constantly in normal play.

This also explains the sentence that opened this whole task — *"some of the walking animations look good with some of the torso directions"*. The walk set has four facings and the torso has sixteen; steering the walk by aim means the legs are a four-way quantisation of the **same** angle the torso renders at sixteen-way, so they agree at N/E/S/W and disagree everywhere else. Standing does not show it, because the idle set is a genuine sixteen-way sheet and matches the torso at every angle — which is exactly why standing looked fine and running did not.

Fixed by dropping the override on the **walk** rule so it uses the pose's own `Heading`. The **idle** rule is deliberately left on `Aim`: a character standing still turning his feet toward the crosshair is a real choice, it is what has been on screen, and the report was that standing looks right. Consequence worth knowing: stopping from a run now plants the feet toward the crosshair, so if you were running north while aiming east the legs turn east as you stop.

Verified by capture with the aim pinned due east while the heading was swept. Before, running north, south, north-east and east all produced **byte-identical** frames, and running west played the east cycle **backwards** — the moon-walk. After: north plays `LegsWalk_N`, south plays `LegsWalk_S`, west plays `LegsWalk_E` mirrored and in forward order, and the torso still holds due east throughout, so aim remains independent. North-east still resolves to the north cycle, which is §6.2 and not this bug.

**5b.2 `Time.timeScale` was 0.04.** Not a runtime value — `ProjectSettings/TimeManager.asset` carries `m_TimeScale: 0.04` as an uncommitted change against a committed `1`. Every Play session was running at **4% speed**, so the walk cycle crawled while the character slid. Anything judged by eye in Play mode before this was seen at that speed. Restored to 1.

**5b.3 The demo camera is not pixel-perfect, so the sprite crawls while moving and only while moving.** The capture measured one source texel rendering as a **mix of 16 and 17 screen pixels**, with the mix shifting as the character moves — 5x16/14x17 at one position, 7x16/12x17 at another. Standing still it is frozen and clean. This is the classic "fine standing, off running" pixel-art artefact and it is independent of every alignment number in this document. The demo camera is orthographic size 3 at PPU 16, which is 96 source pixels tall, against a Game view of 1606 px — 16.729 screen px per texel. **Not changed**, because it means editing a committed demo scene and it is a presentation decision: either add a `PixelPerfectCamera` (reference resolution on the project's 416x260 / PPU 16 convention) or constrain the Game view to an integer multiple.

## 6. Still open — and why each is not a bake or code fix

**6.1 The standing table in §3 is the reference sheet's hand-assembly, and must NOT be chased.** It is tempting to make standing exact in all sixteen directions by giving each `UpperAimRotation` cell its own pivot, `((28 + dx)/56, (49 − dy)/56)` — the table is computed and sitting at `D:\UNITY\Laubrary Dev\PROTOGUY_registration_table.txt` as Part B. **Do not apply it.** Both sheets are already internally well registered, measured on landmarks rather than on the reference: the torso's head sits on row 17 in **all sixteen** directions with a horizontal spread of only 1.5 px, and the idle legs' ground-shadow centre is 28 ± 0.5 px in all sixteen. Two sheets that are each internally consistent are joined by a constant, full stop. The 1–4 px per-direction variation is how the artist laid the figures out in that one reference picture, not a property of the artwork — and applying it would take a torso sheet that is currently near-perfect and de-register it, widening the head's drift across the aim circle from 1.5 px to 6.0 px horizontally and from 0 px to 2 px vertically, with a worst adjacent-direction jump of 3.6 px. The waist would get exact and the head would start swimming, on every aim sweep. That is a bad trade and the table is kept only as the record of why.

The run correction in §4 is a different thing and survives this test: it is per **sheet**, not per direction, and it is independently confirmed by the ground shadow. The south run strip's shadow bottoms out on canvas row 38 where the south idle sheet bottoms out on row 39 — the run cycle literally stands one pixel higher off the ground — which is the same one-pixel correction the GIF template match returns, from a completely different signal.

**6.2 Running collapses the legs from sixteen facings to four.** `LegsIdle16` is a sixteen-frame rotation sheet, so standing resolves to 22.5° steps. `LegsWalk3` has three members at 0°/90°/180° with mirroring, so running resolves to **four 90°-wide sectors — up to 45° of facing error**. Run north-east and the legs point due north or due east under a torso aimed exactly north-east. This is the largest running-versus-standing difference in the whole character and no registration change can touch it: there is no north-east or south-east run artwork to resolve to. It needs either those two sheets from the artist, or a deliberate decision to use `DirectionMode.MembersRotate` and rotate the nearest walk sheet through the residual angle.

**6.3 Running due west puts the waist 4 px right of standing due west.** `LegsWalk3` has no west member, so west is the east sheet mirrored about canvas x 28. The idle sheet's east and west legs are both drawn with the waist centred at x 26.5 — the artist did not draw west as a mirror of east — so mirroring cannot reproduce it. One pivot cannot fix both: mirroring is symmetric about the registration point, so moving it to make west right makes east wrong by the same amount. This needs a real west walk sheet, or splitting the error between the two.

**6.4 `LauminationSet` members embed a stale full copy of their laumination.** `LauminationSetMember.laumination` is a value, not a reference, so `LegsWalk3` carries copies whose `recipe` is empty and whose `frames` still point at the **raw source PNGs** rather than the baked atlas, while `LegsIdle16` and `UpperAim16` carry copies pointing at the atlas. This is currently harmless — playback goes through `res.Laumination.name` and re-resolves against the version's real animations, so only the *name* is read off the copy — but it is a live trap: any future code that reads `frames`, `recipe` or `pivot` off a resolved member gets pre-bake data. `LauminationSet.framePivot` and `LauminationSetMember.pivotOverride` are declared, serialized and read by nothing.

## 7. Two things that will mislead the next person

**The legs sprites contain a baked ground-shadow ellipse; the artist's reference composites do not.** Rows 23–29 of every walk cell are a fixed shadow blob in the shadow/outline colour (22, 7, 11), identical on all 8 frames. Any silhouette or bounding-box comparison between a legs sprite and an artist reference will score badly for that reason alone and will mis-rank offsets. Exclude it.

**`MetaFrame.cells` is a `byte[]`, so Unity YAML writes TWO hex characters per byte, and rows are stored bottom-up.** Parsing it one character per byte, or without flipping rows, yields plausible-looking but wrong coordinates. This inverted a conclusion once already on this task.

## 8. What is verified, and how

- **The running character was driven in Play mode after the fix and photographed, and the live render now reproduces the artist's run frames exactly: 24 of 24 frames — all eight of north, east and south — match at a legs score of 1.0000, every pixel.** Before the fix, east scored 0.719 and south 0.798 against the same reference.
- Both artist references matched at exact-pixel precision; every offset in §3 is a 1.0000 match or a clean unimodal peak over a free search, not a fit.
- All 56 baked sprite cells were measured in the live editor and register to source-canvas (28, 28); `AtlasBaker.Compose` applies the recipe pivot to the **full source cell**, not the trimmed rect, so per-frame trimming cannot move a frame's registration.
- The torso attach is pure transform arithmetic — `Upper.position = Legs.position + (0, 1.3125)` — and `ResolveAnchor` returns for `Pivot` mode before any sprite-bounds code. No clip animates a transform; all five carry a sprite-swap curve and zero float curves.
