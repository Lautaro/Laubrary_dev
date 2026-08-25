# Assembling a split sprite character — the system, and what was built

Task T-0077. **Third revision, 2026-08-24 — and this one is implemented, not proposed.** The two earlier revisions were specifications that changed nothing in the project; both were rejected. This revision describes a system that is now in the code and in ProtoGuy's data.

Both earlier revisions also got the join *number* wrong. Revision 1 said zero offset. Revision 2 said (−1, +22) px, derived from a 9× upscaled JPEG screenshot of one direction. The artist has since supplied `Prototype-Trooper.png` — all sixteen directions, assembled, **at native resolution** — which measures the answer directly instead of inferring it. It is (0, +21) px. See §2.

---

## 1. The answer in one paragraph

Registration belongs to the **sprite**, not to the attachment. Each of ProtoGuy's two halves is drawn centred in its own 56×56 canvas, and each is internally consistent across all of its own frames — the torso's artwork starts on row 16 in all sixteen directions, the legs' ground shadow is centred on the canvas centre-line in all sixteen. So the entire join is **one number**: the torso sits 21 source pixels above the legs' origin, in every direction, standing or walking. What was pulling the character apart was that nothing in the project stored that number, and the mechanism it fell back on — "read the joint off the edge of whatever box this clip happened to bake into" — produces a *different* answer for each clip, which is exactly why changing gait moved the torso.

## 2. Ground truth, measured at native resolution

`.agenthq/attachments/T-0077/20260824T162806Z_Prototype-Trooper.png` is the artist's own assembled character in all 16 facings, 1:1, RGBA, no scaling and no JPEG loss. Each figure was located by exact-pixel template match of `Sprites/Upper/Upper-<dir>.png`, then the legs offset found by exact-pixel match of `Sprites/LegsIdle/Legs-<dir>.png` over the legs area not occluded by the torso.

13 of the 16 figures matched **perfectly — every pixel, every channel**. (Two are un-scoreable because the reference's direction labels touch the artwork; one, WNW, matched at 0.984.) Because these are exact matches there is no scale to fit and no threshold to choose, which is what made the previous revision's estimate wrong.

The recovered offsets, in source pixels, legs relative to the torso canvas, y positive downward:

| | N | NNE | NE | ENE | E | ESE | SE | SSE |
|---|---|---|---|---|---|---|---|---|
| dx | 0 | 1 | 0 | 0 | −1 | −1 | −1 | 0 |
| dy | 21 | 20 | 21 | 20 | 22 | 22 | 22 | 22 |

| | S | SSW | SW | WSW | W | WNW | NW | NNW |
|---|---|---|---|---|---|---|---|---|
| dx | 0 | 3 | 2 | 2 | 4 | 1 | 0 | 0 |
| dy | 21 | 20 | 21 | 21 | 22 | 20 | 21 | 20 |

**These are hand-assembly slop, not a real per-direction relationship.** dy is 21 ± 1 and dx is 0 ± 1 across three quarters of the circle; only the western quadrant drifts to +2…+4 in x. The single best constant is **(0, +21)**, mean error 1.6 px, worst case 4 px (due west only), exact in four directions.

Side by side, artist assembly against the constant: `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_offsets_vs_artist.png` (top two rows the artist's own numbers, bottom two the single constant). They are indistinguishable by eye, including in the west quadrant.

**Why a constant is not merely "good enough" here but actually more correct:** the torso and the legs rotate *independently* at runtime — the torso follows the crosshair while the legs follow the walk heading. A per-direction table is indexed by a single direction and has nothing to say about the torso-facing-east-while-legs-face-north case, which is the normal case in a twin-stick shooter. A constant is defined for every combination.

## 3. Why the halves came apart — the mechanism

`ProtoGuy.asset` joined the two parts with `MetaLayer` anchors named `Waist` on **both** sides. Only the torso side had points painted. All four legs lauminations carry `metaLayers: []`.

`CompositeZonedPlayer.ResolveAnchor` used to respond to that by falling through into `switch (anchor.edge)` and taking the **top of the legs sprite's bounds** — silently, with no console message, and using an `edge` value that is not even editable in the UI while the mode is MetaLayer, so nobody ever chose it.

That is not a slightly-wrong point, it is a different *reference frame*, and it is per-clip: the baker sizes each clip's frame box to that clip's own content, so `Edge.Top` sat 14 px above the pivot on the idle sheet and 11 / 10 / 9 px on the three walk sheets. Change gait, move the torso. Meanwhile the torso side was using its painted `Waist` points, which wander 9 px horizontally between facings. The result is exactly what was reported: **some walk cycles line up with some torso directions.**

## 4. What was built

### 4.1 A `Pivot` anchor mode — `Runtime/ZoetropeLaunimator/CompositeLauminaryView.cs`

`AttachAnchorMode` gains a third member, **appended** (`Edge=0, MetaLayer=1, Pivot=2`) so no existing serialized anchor is reinterpreted. `Pivot` means *this part's own registered origin, plus the offset* — a point that does not move when the artwork's content bounds change, which is precisely the property `Edge` lacks and the reason the torso jumped between clips.

### 4.2 MetaLayer no longer degrades into a different reference frame — `Runtime/ZoetropeLaunimator/CompositeZonedPlayer.cs`

A MetaLayer anchor that cannot resolve now degrades in two honest steps: first to the **nearest painted frame** (a quiet frame in an otherwise-painted layer is a gap, not a new joint), then to the part's **own origin** — and **warns once**, naming the part and the layer. It never silently substitutes a bounds edge. The same rule is mirrored in the editor's offline resolver (`Editor/Zoetrope/ZoetropeWindows.cs`), which previously disagreed with the runtime and so drew a schematic that did not match what rendered.

This is the change that matters most for the next character. The original defect was not that a number was missing — it was that a missing number produced a plausible-looking wrong answer with nothing in the console to say so.

### 4.3 The anchor UI is now three-way — `Editor/Zoetrope/ZoetropeWindows.cs`

The mode picker was a binary if/else, so a third mode would have fallen into the `else` branch and shown a MetaLayer id field. It now shows the Edge picker for Edge, the id field for MetaLayer, and only the offset row for Pivot. **The offset fields are in world units, not pixels** — at PPU 16, 21 px is `1.3125`.

### 4.4 The walk sheets are re-registered — `Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/draft/`

The three `LegsWalk` strips were exported **pre-cropped by (5, 9)** out of the same 56×56 canvas the idle sheets use, so a cell-centre pivot (`0.5`) is a physically different point on them than on the idle sheets. The canvas centre in walk-cell space is `16/35 = 0.4571429`; all 24 walk recipe cells were set to it and the lauminary re-baked.

Measured before and after, from the live baked sprites: the walk clips' pivots moved from 13 px to 11 px above their content, which puts all five lauminations — three walk sheets, the idle rotation and the torso rotation — on the **same** source-canvas centre. Verified by arithmetic on each clip's baked rect: `LegsWalk_N` (10, 11) in a 21×22 box, `LegsIdleRotation` (13, 14) in 27×28 and `UpperAimRotation` (20, 20) in 43×32 all resolve to canvas (28, 28).

The crop offset of (5, 9) is confirmed three ways: the walk shadow's centre-line sits at cell x 23 against canvas x 28, and the content's top row is exactly 9 rows higher on all three walk sheets than on the corresponding idle sheets.

### 4.5 Mirrored direction sets picked the wrong member — `Runtime/Launimator/LauminationSetResolver.cs`

A three-member walk set covers sixteen headings by mirroring, and the mirror arithmetic was `θ − 180` where reflecting about the vertical axis is `360 − θ`. The two agree only at due west. **18 of 35 sampled headings across the western half chose the wrong clip** — walking north-west played the south walk cycle. Fixed, with the tests that had encoded the old rule rewritten and two regression tests added that fail under the old formula.

Independent of alignment, but the same reported symptom: legs that look wrong for the direction you are moving.

### 4.6 ProtoGuy's own data — `Assets/Demos/ProtoGuyDemo/ProtoGuy.asset`

- Both anchors → `Pivot`. The torso's parent anchor carries the whole join: **offset (0, 1.3125)** world units. The child anchor is zero.
- `idleClip` was `LegsWalk_N` on **both** parts, so the torso rendered a legs sprite until its first pose update, and the hurtbox was measured off it. Now `LegsIdleRotation` for the legs and `UpperAimRotation` for the torso.

## 4.7 — Verified in Play mode, not just on paper

Driven live in `Assets/Demos/ProtoGuyDemo/ProtoGuyDemo.unity`:

- `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_playmode_proof.png` — walking on a held `W`, aiming at the mouse cursor, standing, and firing on a held `Space` with the projectile visible.
- `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_playmode_16.png` — eight standing facings, plus **eight walk headings with the torso held due east**: the mismatched combination the original report was about.
- `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_seam_closeup.png` — the waist at 5×, standing against walking at the same facing.

Measured live: the torso part sits at exactly `(0, 1.3125)` relative to the legs part, on every clip. Weapon and muzzle resolve on the torso and the projectile leaves in the aimed direction. The mirrored walk set resolves symmetrically across all sixteen headings (N/NNE/NE unflipped ↔ NNW/NW flipped, and so on).

## 5. The rule to carry to the next character

1. **Two parts that an artist drew on a shared canvas need one number, not a painted point per frame.** Author it once as a `Pivot`-mode offset. Fifty-six hand-placed points is fifty-six chances to be a pixel out, and on this character they measurably were, by up to nine.
2. **Never derive a joint from artwork bounds.** A baked box is sized by whatever the artist happened to draw, so it differs per clip and moves silently on any redraw or re-bake.
3. **Keep per-frame meta points for joints that genuinely travel inside the picture** — a hand gripping a weapon through a swing. They belong on top of sheet registration, not instead of it.
4. **Fix registration at import.** The walk sheets needed their own number only because they were exported pre-cropped. Ask for exports on a common untrimmed canvas, or record the crop at import, and the whole set shares one origin.
5. **A missing anchor must fail loudly.** This whole task existed because a missing one produced a plausible wrong answer instead of a message.

## 6. Known residuals and things not done

- **The west-quadrant 2–4 px.** The artist's own assembly nudges the western facings up to 4 px further right than the constant. Indistinguishable by eye in the side-by-side, and not reproducible without a per-direction table that cannot express independent torso/legs facing (see §2). Left as measured, not papered over.
- **`LegsWalk3` has three members for a sixteen-way aim channel**, so walk direction quantises to N/E/S/W. That is the authored art, not a defect — but it is why walking diagonally shows a cardinal walk cycle.
- **Two orphan assets**, `Assets/Demos/ProtoGuyDemo/Lauminaries/ProtoGuyLegs.asset` and `ProtoGuyUpper.asset`, are referenced by nothing. `ProtoGuyUpper.asset` carries a `MuzzleVec` layer that does not exist in the live draft and has sent two separate investigations down a dead end. Worth deleting under its own ticket.
- **`LauminationSet.framePivot` and `LauminationSetMember.pivotOverride`** are declared, serialized into asset YAML, documented as a shared registration pivot — and **read by nothing**. They look exactly like the feature this task needed and are not. Delete or implement, under their own ticket.
- **`Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/` was backed up before the re-bake** to `Temp/t0077b/BACKUP_ProtoGuy_25cf2dfd`, because that bake has a known non-idempotent reimport race. The bake came back clean: 56 sprite cells before and after, every clip's rect unchanged.
