# ProtoGuy alignment — diagnosis and recommended system

Task T-0077. Written 2026-08-24. **No code, asset or art was changed to produce this document** — it is analysis plus a recommendation awaiting your go-ahead. The only files added are this document and the two proof images it links to.

---

## TL;DR

1. **Your suspicion is right.** The artist *did* draw everything on one shared canvas. If you simply place the torso and the legs at the same position, with no painted points and no offsets at all, you get a correctly assembled character in every direction, for idle *and* all three walk cycles. I verified this by compositing the raw PNGs — see `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_overlay_proof.png`.
2. **The waist meta-layer is the wrong tool for this job**, and it is what is causing the misalignment rather than fixing it. Recommendation: stop using it for the legs↔torso seam.
3. **The actual bug is not "some directions are wrong".** It is that the **legs have no `Waist` layer at all** — zero painted points on any legs animation, and none anywhere else in the project either (§2). The attach code silently falls back to a different anchor (the top of the legs' bounding box), which lands at a different height on every legs clip — that per-clip 3–5 px difference is the part you can see varying. Underneath it sits a much larger constant error the same fallback causes in *every* clip, idle included; §7 has the measured numbers and the one check you should run before acting on any of this.
4. There is a **second, much smaller** issue: the walk sheets' registration pivot is 2 px off the artist's true canvas centre, because the walk art was exported on a cropped canvas.
5. **Launimator already has the right mechanism** — the Laumination Builder's *Registration* stage, with its green pivot crosshair. It just was not used for this. The recommended system is built almost entirely out of things that already exist.

---

## 1. What the art actually is

| Sheet group | Files | Source canvas | Registration pivot in the recipe |
|---|---|---|---|
| `LegsIdleRotation` | `Assets/Demos/ProtoGuyDemo/Sprites/LegsIdle/Legs-*.png` (16 dirs) | **56 × 56** | `(0.5, 0.5)` |
| `UpperAimRotation` | `Assets/Demos/ProtoGuyDemo/Sprites/Upper/Upper-*.png` (16 dirs) | **56 × 56** | `(0.5, 0.5)` |
| `LegsWalk_N/E/S` | `Assets/Demos/ProtoGuyDemo/Sprites/LegsWalk/Legs-*-walk.png` (3 dirs × 8 frames) | **46 × 35** per cell | `(0.5, 0.5)` |

Two independent measurements agree that the art is registered:

- **The torso's top edge is at pixel row 39 in all 16 direction files — zero deviation.** That is not something that happens by accident; it means every `Upper-*.png` was rendered from the same camera onto the same canvas.
- **The idle legs sit within 0.5 px horizontally and 3 px vertically of each other across all 16 directions**, the vertical wobble being ordinary per-direction foreshortening in the art, not a registration error.

Overlaying `Upper-<dir>.png` directly on top of `Legs-<dir>.png` at identical canvas coordinates — no offsets, no points — produces a coherent character in every direction. That is `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_overlay_proof.png` (top row = idle, bottom row = walk).

### The one place the artist broke the shared canvas

`LegsWalk` was exported on a **46 × 35** canvas instead of 56 × 56. It is a plain crop of the same character canvas, and the crop is recoverable: matching the near-static hip band of all 8 walk frames against the idle legs gives **offset (5, 9) from the top-left of the 56 × 56 canvas**, consistently for all three directions (the vertical `9` is unanimous with no ambiguity at all; the horizontal `5` is exactly a symmetric crop, `(56 − 46) / 2`).

Unity's default centre pivot on that cropped canvas lands at `(5, 10)` — **1 px low**, which after the atlas baker's rounding becomes a **2 px** error in the baked frame. So the walk legs currently render about 2 px lower than the artist intended, relative to everything else.

**That 2 px is the entire art-side problem.** Everything larger than that is the code path described next.

---

## 2. Root cause of what you are seeing

Attachment is resolved once per frame in `Assets/Packages/Laubrary/Runtime/ZoetropeLaunimator/CompositeZonedPlayer.cs:86-102`, via `ResolveAnchor` at line 109. The critical behaviour is at lines 111-118:

> If the anchor is in `MetaLayer` mode but the named layer has nothing painted on the current frame — **or does not exist at all** — it silently falls through to `Edge` mode and uses the sprite's bounding box instead. No warning, no log.

And the authored data is this:

| Laumination | Frames | `Waist` points painted |
|---|---|---|
| `UpperAimRotation` | 16 | **16 / 16** ✔ |
| `LegsIdleRotation` | 16 | **0 / 16** ✘ |
| `LegsWalk_N` | 8 | **0 / 8** ✘ |
| `LegsWalk_E` | 8 | **0 / 8** ✘ |
| `LegsWalk_S` | 8 | **0 / 8** ✘ |

(Live data: `Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/draft/ProtoGuy_draft.asset`. The `Waist` layer is declared at line 1216, on the torso animation only.)

**Re-verified inside the running editor.** This table was originally read off the YAML on disk; it has since been re-read from the deserialised objects in the open Unity editor, which is the same data the tool and the runtime actually see. The result is identical, and stronger than the disk read: the three walk lauminations and `LegsIdleRotation` have **`metaLayers.Count == 0`** — not an empty `Waist` layer, *no layers at all* — while `UpperAimRotation` has exactly two (`Muzzle`, Vector, 16/16 authored; `Waist`, Point, 16/16 painted). The in-editor read and the on-disk YAML agree exactly, so what is stored really is what the runtime sees. (The asset also reports **not dirty**, i.e. no pending unwritten change on the asset object — corroboration, not proof, since that flag says nothing about an editor window holding its own unwritten working copy.)

Also confirmed live: the torso's 16 painted `Waist` points span **9 px in x and 3 px in y**, and each sits **7.5–10.5 px above the bottom of its own baked 43 × 32 frame**. Note that frame box is the union across all 16 directions, so it is not the same as that direction's content bottom — the amount of torso content actually *below* the painted point is only **0–8 px**, and in ten of the sixteen directions it is 2 px or less.

**⚠ Your legs points are not anywhere in the project.** You describe having set points on *both* animations. Every laumination that could hold them was searched: the live draft (above), and both older-generation assets `D:\UNITY\Laubrary Dev\Assets\Demos\ProtoGuyDemo\Lauminaries\ProtoGuyLegs.asset` and `...\ProtoGuyUpper.asset` (38 and 31 `metaLayers: []` respectively; the only layer that exists at all in either is a `MuzzleVec` on one torso direction). There is no `Waist` data on any legs animation, in any version, anywhere on disk. So either those points were never committed by the Builder, or they were painted into a session that did not persist them — **and if you open the Laumination Builder on `LegsIdleRotation` and can still see your painted waist points there, that is a separate and serious Builder save bug that needs its own ticket.** Please check that first; it changes nothing about the recommendation below, but it is the difference between "you forgot" and "the tool lost your work".

So the torso side of the seam uses a real painted point, and **the legs side has been running on the fallback the whole time**. In `Assets/Demos/ProtoGuyDemo/ProtoGuy.asset:99-108`, `Upper.parentAnchor` is `mode: 1` (MetaLayer) with `edge: 1` (Top) — so the legs contribute "the top of my bounding box", which is a different height on every clip:

| Legs clip | Baked frame | Edge-Top, measured from the pivot | Error vs idle |
|---|---|---|---|
| `LegsIdleRotation` | 27 × 28 | **+14 px** | — (the one you tuned against) |
| `LegsWalk_E` | 39 × 24 | **+11 px** | **3 px** |
| `LegsWalk_S` | 21 × 23 | **+10 px** | **4 px** |
| `LegsWalk_N` | 21 × 22 | **+9 px** | **5 px** |

At PPU 16 that is a 0.19–0.31 world-unit jump in the torso's height the moment the legs switch clip. On top of that, walking **west** plays `LegsWalk_E` mirrored, which moves the bounding-box centre by a further **3 px horizontally**.

Meanwhile the torso's own 16 hand-painted waist points vary by **9 px in x and 3 px in y** across the direction wheel. So the final seam is *(a per-clip legs constant) minus (a per-direction torso variable)* — two unrelated error sources that happen to cancel for some combinations and not others. That is exactly *"some walking animations look good with some torso directions"*.

**The idle case is not a working baseline that the walks failed to match** — all four are equally arbitrary, and one of them was calibrated by hand. In fact the measurement in §7 says the idle path should look wrong too, by a much larger margin than the walk-vs-idle difference; read that section before treating idle as the thing to match the walks to.

---

## 3. Why the meta-layer is the wrong tool here

A per-frame painted point is the right mechanism when **the joint genuinely moves inside the frame** — a heavy weight shift, a lean, a squash. It is the wrong mechanism when the art is already registered, because:

- **It is 56 points of work** for ProtoGuy (16 idle + 24 walk + 16 torso) where the correct answer is **5 crosshair placements**, one per source sheet.
- **Every painted point is a hand-placed pixel, so it carries hand-placed error.** The torso's 9 px of x-spread is not in the art — the art's torso top edge is pixel-identical across all 16 directions. That spread was *introduced* by painting. The meta-layer made the data noisier than the source it was describing.
- **Missing data fails silently and plausibly.** A frame with no point does not freeze or error; it produces a wrong-but-believable pose. That is the most expensive failure mode a tool can have, and it is what hid this bug.
- **It does not scale with content.** Every new walk direction, every re-bake with a different frame count, is another 8 points that must not be forgotten.

---

## 4. Recommended system

A three-tier anchor model, cheapest-first. Tier 1 handles ProtoGuy completely.

### Tier 1 — Shared registration (the default; zero authoring)

**Both parts sit at the same transform position, anchored by their own registration pivot.** This is what the artist's shared canvas already gives you for free, and it is stable across frames, directions, clips and re-bakes because the pivot is baked once per laumination and never depends on pixel content.

This needs one small addition: a **`Pivot` anchor mode**. Today `AttachAnchorMode` only offers `Edge` and `MetaLayer`, and *every* `Edge` option (including `Center`) reads the sprite's **bounding box**, which is content-dependent — `bounds.center` is already 1.5 px off the pivot on `LegsWalk_E`, because the trimmed frame is asymmetric about the registration point. There is currently **no way to say "use this part's own registered origin"**, which is precisely what this art wants.

The change is additive and small: a new enum member in `Assets/Packages/Laubrary/Runtime/ZoetropeLaunimator/CompositeLauminaryView.cs`, one branch in `ResolveAnchor` returning `partGo.transform.position + offset`, and the same branch in the editor's mirror of that function (`Assets/Packages/Laubrary/Editor/Zoetrope/ZoetropeWindows.cs:1117`). It should be the **default** for a new part.

### Tier 2 — Per-sheet registration crosshair (for art that is *not* on a shared canvas)

**This already exists and is the piece that was missed.** The Laumination Builder has a *Registration* stage — "drag the selected sprite to move its pivot relative to the green crosshair" (`Assets/Packages/Laubrary/Editor/Launimator/LauminationBuilderWindow.cs:1105`), plus a `Baseline` button that snaps the pivot to content bottom-centre, a `PivotMode` selector and a custom-pivot field. The pivot is stored per recipe frame in the Lauminary asset and flows through the atlas bake into the shipped sprite.

That is the correct home for "this sheet was cropped differently" — set the crosshair once on the sheet, and every frame, every consumer and every future re-bake inherits it. It is the fix for the LegsWalk 2 px issue.

The one gap worth closing: the crosshair is placed **per sheet in isolation**, with nothing to place it *against*. A "show another laumination as a ghost underlay" option in the Registration stage would let you drag the walk legs until they sit under the idle legs, which is the actual task. Without it you are aligning blind.

### Tier 3 — Per-frame meta point (override only)

Keep it, but demote it to what it is good at: **overriding tiers 1–2 on the specific frames where the art genuinely moves the joint.** Two rules make it safe:

- **Resolve up the chain, never sideways.** Painted point on this frame → nearest painted frame in the same laumination → the part's registration pivot. Never to a bounding-box edge, because a bounding box is content-dependent and therefore direction-dependent — it is the one thing guaranteed to reintroduce the bug. `TryGetMetaPointNearest` already exists in `Assets/Packages/Laubrary/Runtime/Launimator/ZonedAnimationPlayer.cs:566` and does the middle step; the attach path just never calls it.
- **Never fail silently.** If an anchor is set to `MetaLayer` and the layer is absent or the frame unpainted, warn once per part naming the laumination and frame. This bug would have been a two-minute find instead of a four-agent investigation.

---

## 5. Concrete steps for ProtoGuy

In recommended order. Steps 1–2 alone should fix the visible problem — but run the §7 editor check first, so you know the ~20 px constant error is what you are actually looking at.

1. **Switch both sides of the Legs↔Upper seam to shared registration.** In `Assets/Demos/ProtoGuyDemo/ProtoGuy.asset`, the `Upper` part's `parentAnchor` and `childAnchor` both become the new `Pivot` mode with `offset (0, 0)`. Requires the Tier-1 enum addition above.
2. **Correct the LegsWalk registration pivot** from `(0.5, 0.5)` to **`(0.5, 0.4571)`** — that is `16/35`, the artist's true canvas centre inside the cropped 46 × 35 cell. Applies to all 24 walk recipe frames (`LegsWalk_N`, `_E`, `_S`). Then re-bake the atlas.
3. **Delete the torso's 16 hand-painted `Waist` points.** With shared registration they are unused, and leaving them invites the next person to wire them back up. (The `Muzzle` vector layer is unrelated and fully authored 16/16 — leave it alone.)
4. Re-check the seam in the Zoetrope preview across all 16 directions × idle/walk. Note the preview mirrors the runtime resolver exactly, including the fallback, so it is a valid check.

---

## 6. Other defects found on the way

Not part of the alignment fix; listed so they are not lost. **None of these have been touched.**

- **Mirror maths is wrong for half the circle.** `Assets/Packages/Laubrary/Runtime/Launimator/LauminationSetResolver.cs:218-239` computes `mirroredAngle = angleDeg - 180f`. Mirroring across the vertical axis is `360 − angle`. With ProtoGuy's `LegsWalk3` set (members at 0°/90°/180°) it is coincidentally correct at 270° but **wrong everywhere else between 180° and 360°** — e.g. heading 200° resolves to 20° and plays `LegsWalk_N` mirrored, where the correct source is 160° → `LegsWalk_S`. **The left half of the walk circle plays the wrong leg cycle.** This is a separate, visible bug and deserves its own ticket.
- **Set members store animations by value, and the copies have gone stale.** `LauminationSetMember.laumination` is a `Laumination` value, not a reference (`Assets/Packages/Laubrary/Runtime/Launimator/LauminationSet.cs:77`), so Unity serialises a full copy per member — and in the draft asset *every* copy has `metaLayers: []`, including the torso's. Harmless today because playback re-looks-up the real object by name, but `ResolveRotationSheet` reads `frames.Count` off the stale copy, so a re-bake with a different frame count would silently break direction mapping.
- **Two orphan assets.** `Assets/Demos/ProtoGuyDemo/Lauminaries/ProtoGuyLegs.asset` and `Assets/Demos/ProtoGuyDemo/Lauminaries/ProtoGuyUpper.asset` are referenced by nothing but their own `.meta` files; the live data is the `Assets/Launimator/...` draft. Deletion candidates, but confirm before removing.
- **`idleClip: LegsWalk_N` is set on the Upper part too** (`Assets/Demos/ProtoGuyDemo/ProtoGuy.asset:131`), so the torso shows a *legs* sprite for one frame after spawn before the pose animator overrides it.

---

## 7. What is verified, and what is not

**Verified by measurement:** every number in sections 1 and 2 — canvas sizes, per-direction bounding boxes, the recovered `(5, 9)` crop offset, the per-clip Edge-Top values, the torso's painted-point spread, and the meta-layer coverage table. Four agents measured the source PNGs, the baked atlas and the live editor objects independently and agreed. (One earlier pass mis-parsed the mask data — `MetaFrame.cells` is a `byte[]`, which Unity's YAML writes as **two hex characters per byte** — and briefly produced wrong point coordinates. Every figure surviving here has been re-derived with that handled; if you extend this analysis, that is the trap.)

**Verified against the running editor:** the meta-layer coverage table, the torso's 9 px / 3 px point spread, and the whole seam table below were computed from the live deserialised objects and real baked `Sprite`s in the open Unity editor, not from the YAML — see §2. This is the strongest form of the central claim: the legs carry no meta layers at all.

**Verified by eye:** that a shared-origin composite produces a correct character, in 8 directions for idle and 3 × multiple frames for walk — `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_overlay_proof.png`. The walk-pivot correction of step 2 is shown before/after in `D:\UNITY\Laubrary Dev\PROTOGUY_ALIGNMENT_walk_pivot_fix.png` (blue = idle reference, red = the current centre pivot, green = the corrected pivot).

**Not verified:** the recommendation has **not been implemented.** No code was written, no pivot was changed, nothing was re-baked. The `Pivot` anchor mode does not exist yet; the `(0.5, 0.4571)` walk pivot has been derived from the pixels but not applied and re-baked. The predicted result is a seam accurate to the pixel, but that prediction is from measurement, not from a running build.

**Still open — and it is the one thing you should sanity-check before acting.** The first draft of this document worried that with no legs waist points the torso ought to be flung roughly 24 px clear of the legs, which would contradict the idle case looking fine. That worry has now been confirmed by direct measurement against the real baked `Sprite` objects in the running editor, using the exact arithmetic of `CompositeZonedPlayer.LateUpdate` and `PixelToWorld`:

| Legs clip | Legs `Edge.Top` | Where the torso pivot ends up | Content seam (+overlap / −gap) | Directions showing a gap |
|---|---|---|---|---|
| `LegsIdleRotation` | +14 px | **23.5 – 26.5 px above the legs pivot** | −3.5 … +5.5 px | **9 / 16** |
| `LegsWalk_E` | +11 px | 20.5 – 23.5 px | −1.5 … +7.5 px | 3 / 16 |
| `LegsWalk_S` | +10 px | 19.5 – 22.5 px | −0.5 … +8.5 px | 1 / 16 |
| `LegsWalk_N` | +9 px | 18.5 – 21.5 px | −0.5 … +8.5 px | 1 / 16 |

Shared registration wants that middle column to be **0**. It is 18–27 px — well over a world unit at PPU 16 — for *every* clip including idle. The per-clip 3/4/5 px deltas of §2 are real and sit on top of this, but they are the small part; the constant ~20 px lift is the large part, and it does not discriminate between idle and walk.

So this path cannot be what you were looking at when idle "looked good", because it puts the torso a whole body-height too high in all four clips. The likely reconciliation is the missing legs points flagged in §2: with legs points present (in a Builder session, or in a preview that had them), the seam would resolve properly and look right — which is consistent with your description, and with the idle case being the one you tuned. **Confirm this in the editor before acting on §5**: spawn the ProtoGuy composite in Play mode and look at whether the torso is floating clear of the legs. If it is, everything here holds. If it is not, something else is moving the torso that this analysis has not found, and that must be identified first.

None of this changes the recommendation — §4 exists precisely because a mechanism that fails this silently, and that depends on data this easy to lose, is the wrong mechanism for registered art.
