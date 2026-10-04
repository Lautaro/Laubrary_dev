# GoreLab → Laubrary: port design and build contract

Project manager's contract for the port. Read together with `GORELAB_ALGORITHM_SPEC.md` (what the algorithms are), `GORELAB_INTEGRATION_BRIEF.md` (how Laubrary works) and `GORELAB_GOLDEN.json` (acceptance numbers). Where this document and the brief disagree, this document wins. Branch `feat/gorelab`, project `D:\Unity\Laubrary Dev - GoreLab`.

## 1. What is being delivered

1. A new Laubrary tool, **GoreLab**, that adds 3D-aware dismemberment to existing 2D sprite characters, as a **sidecar**: a Zoe (or a bare Lauminary version) does not know GoreLab exists. The gore data lives in its own asset (`GoreRig`) that points at the target. Meta layers (MetaMapper) stay a separate asset kind.
2. The **cut engine**: a faithful C# port of the prototype's engine, validated against `GORELAB_GOLDEN.json`.
3. **Wound recipes** (damage types) as small plug-in classes: Slice, Cut (knife), Bullet (straight-on), Shotgun, Remove head. A game adds a kind by writing one class; GoreLab is not edited. Wound kinds at the engine level (removers) are also pluggable (`IRemoverKind`).
4. A **live component** (`GoreBody`) that attaches to a spawned character, shows wounds by swapping the displayed sprite, and lets flying pieces and blood leave.
5. An **editor window** (UI Toolkit via ZUI, compact, tabbed, undo/redo) for tagging members on frames, painting masks, and testing recipes.
6. A real **eight-direction Zoe** (the Doom imp walking) built from the prototype's sprites, a **GoreRig** carrying the user's existing head and torso tags, and a **demo scene**: imps standing in place, damageable by mouse (every tagged member is hittable, the mouse never chooses a part), with a small panel to pick the damage type and reset the wounds.

## 2. Decisions (taken by the PM so nobody waits)

- **Palette-rule exception.** CLAUDE.md's Zoe-palette rule forbids bolting reaction components onto a character. The owner asked explicitly for an additive sidecar the Zoe cannot see. GoreLab is therefore a deliberate, owner-requested exception. Do NOT edit CLAUDE.md; the final report names it for the owner to record.
- **Damage kind stays inside GoreLab.** No change to Combat2D. A wound request carries its recipe and a 3D-aware aim line. `GoreBody` may also listen to `Health.Damaged` for hits that come from elsewhere (point + direction, default recipe), but the demo drives wounds directly.
- **Rigs** live in the host project (`Assets/GoreLab/` by default); several rigs may target one Zoe.
- **Targets:** a rig targets a Zoe or a `LauminaryVersion`; both reach the same frame list.
- **Style:** the wound colours are data from day one (`GoreStyle`, Fleshy default). No style editor yet.
- **Tabs:** use `Z.Segmented` as the tab row (no new ZUI control).
- **Death row / death animation:** out of scope for v1. Walk only.
- **Retail imp art:** committed under the demo folder only; the final report flags that it must not be copied into `Samples~` / shipped without the owner's decision.
- **Sprite pivot rule (decided from the prototype's placement `left = floor((W - w) / 2)` on an even canvas):** the imp frames use a custom pivot whose pixel x is `ceil(w / 2)` (normalised `ceil(w/2) / w`), y = 0 (bottom). The brief's `floor(w/2)` is wrong for odd widths.

## 3. Mirroring, the one rule that cannot be got wrong

The 8-direction resolver mirrors only when the authored members face RIGHT (angles 0..180). The retail imp's drawn side columns face LEFT. So:
- Commit **horizontally flipped copies** of retail columns 1, 2, 3 as the authored right-facing members (angles 135, 90, 45); columns 0 and 4 are authored as they are (180 front, 0 back). Left-facing views are produced by the resolver with `flipX = true` of those members.
- The rig's tags for the flipped copies are the prototype's tags **mirrored** (centre x → `w-1-x`... exact rule in spec section 2: mirror the pixels and the tag vectors, do NOT mirror the wound).
- At runtime `GoreBody` bakes **per view = (drawn sprite, flipX)**. For `flipX == true` it bakes from the horizontally mirrored pixels and the mirrored tags, shows the result with the renderer's `flipX` set to false, and uses a pivot mirrored to match (`1 - px`). Reason: turning an imp around is a rotation, so a wound must not appear mirrored; showing a baked texture through `flipX` would reflect it. Slots are cached by (sprite, flip).

## 4. Assembly layout and file ownership

Paths are under `Assets/Packages/Laubrary/` unless stated.

| Folder | Owner | Content |
|---|---|---|
| `Runtime/GoreLab/Core/` | **P1 Engine** | pure C# engine: tag maths, rng/hash/noise, solids, removers, evaluator, chunk extraction, frame cut, mirroring helpers. Already present: `GoreTypes.cs` (the contract types) and `GoreDefaults.cs`. P1 may ADD members but must not rename or change the meaning of anything in `GoreTypes.cs`; every deviation is written down in section 12 of this file at once. |
| `Assets/Tests/GoreLab/` | **P1 Engine** | asmdef + golden tests (run by reflection, never the Test Runner) |
| `Runtime/GoreLab/Data/` | **P2 Data and recipes** | `GoreRig`, `GoreMemberDef`, `GoreFrameTags`, `GoreMemberFrame`, input-building helpers |
| `Runtime/GoreLab/Wounds/` | **P2 Data and recipes** | `IWoundRecipe` implementations, `WoundRequest` helpers, recipe registry (`TypeCache`/reflection) |
| `Runtime/GoreLab/Live/` + `Runtime/GoreLab/com.Lautaro-Arino.Laubrary.GoreLab.asmdef` | **P3 Live** | `GoreBody`, view slots, bake scheduler seam, pieces and blood via Chunks, world↔sprite conversion |
| `Editor/GoreLab/` + its asmdef | **P4 Editor** | `GoreLabWindow`, stage, tabs, frame strip, undo, `[InitializeOnLoad]` link, menu `Laubrary/GoreLab` (the only menu item) |
| `Assets/Demos/GoreLabDemo/` | **P5 Content** then **P6 Demo** | sprites, Lauminary version, Zoe, rig with imported tags, scene, demo scripts |

**While P1 works, P2/P3/P4 must NOT write into `Assets/`.** They write the same relative paths under `D:\Unity\Laubrary Dev - GoreLab\_incoming\<Pn>\` (for example `_incoming\P2\Assets\Packages\Laubrary\Runtime\GoreLab\Data\GoreRig.cs`) so the open Unity editor does not recompile half-written code. The PM moves the files into `Assets/` once the engine compiles. They cannot compile-check; write carefully against the contract types, and list everything uncertain in the hand-back.

Nobody runs git commands that change state; the PM commits. Nobody drives the Unity editor except P1 (for its golden run) and, later, the integrator.

## 5. Contract: the shape of each piece

### 5.1 Engine (P1) — namespace `Laubrary.GoreLab`, no UnityEngine types

```csharp
public static class GoreTagMath {
    public static void Normalize(ref MemberTag t);                    // the prototype's normHead + fixFwd
    public static MemberTag Mirror(in MemberTag t, int spriteWidth);  // mirrorHead: tag of the horizontally mirrored sprite
    public static (double x, double y, double z) East(in MemberTag t);// up x forward
    public static bool InsideOutline(in MemberTag t, double px, double py);   // the 2D outline test
}
public static class GoreRng { public static double Hash(int a, int b, int seed); public static Func<double> Rng(int seed); public static double VNoise3(double x, double y, double z, int seed); }  // bit-exact with the prototype
public static class GoreRemovers {
    public static void RegisterKind(IRemoverKind kind);
    // built-ins: Plane, Capsule, evaluated by the same code path as custom kinds
}
public static class GoreCut {
    // Cut one frame with every remover, member by member, merged with the head owning overlapping pixels. Newest group (highest .group) = the chunk.
    public static void CutFrame(in GoreFrameInput frame, IReadOnlyList<GoreRemover> removers, in GoreCutConfig cfg, GoreStyle style, GoreFrameResult result);
    public static void MirrorFrame(GoreGrid grid, GoreMemberInput[] members, out GoreGrid mirroredGrid, out GoreMemberInput[] mirroredMembers); // pixels, tags and masks, for the flipX view
}
```
The inner per-pixel evaluation is a static function over plain arrays and a small scratch struct, with NO allocation per pixel and no LINQ/foreach-on-IEnumerable in hot paths, so that one frame can later become a Burst job. `GoreFrameResult` buffers are reused when passed in again.

### 5.2 Data (P2) — namespace `Laubrary.GoreLab`

```csharp
[CreateAssetMenu(menuName = "Laubrary/GoreLab/Gore Rig", fileName = "GoreRig")]
public sealed class GoreRig : ScriptableObject, Laubrary.PreviewKit.IVisualPreview {
    public Laubrary.Zoetrope.Zoe zoe;                         // target (or)
    public Laubrary.Launimator.LauminaryVersion reel;         // bare animation target
    public List<GoreMemberDef> members;                       // default: Head (Ball), Torso (Box)
    public List<GoreFrameTags> frames;                        // one per DRAWN sprite
    [SerializeReference] public List<IWoundRecipe> recipes;   // the damage types this rig offers; defaults are created on first use
    public GoreCutConfig cut = GoreCutConfig.Default();
    public GoreStyle style = GoreStyle.Fleshy();
    public bool TryGetFrame(Sprite s, out GoreFrameTags f);   // dictionary built lazily, rebuilt when frames change
    public IEnumerable<Sprite> EnumerateTargetSprites();      // from the Zoe's view / the reel, de-duplicated
}
[Serializable] public sealed class GoreMemberDef { public string name; public MemberKind kind; public Color editorColour; }
[Serializable] public sealed class GoreFrameTags { public Sprite sprite; public string label; public List<GoreMemberFrame> members; }   // parallel to rig.members
[Serializable] public sealed class GoreMemberFrame { public bool present; public bool skip; public MemberTag tag; public int[] behind; public int[] exempt; }  // masks: sprite-local pixel indices (top-left, y down)
public static class GoreRigInputs { public static GoreMemberInput[] Build(GoreFrameTags f, int w, int h, bool mirrored); }  // turns authored data into engine input (mask arrays), mirroring tags/masks when asked
```
`IVisualPreview.RenderPreviewTexture` shows the first frame with the member shapes drawn over it (the editor stage's drawing code is shared through a static helper in Data or a tiny shared file so there is one render path). No sub-assets inside the rig.

Recipes (P2), each in its own file, `[Serializable]`, tunables as public fields with `[Tooltip]` and a short `[Header]`:
`SliceRecipe`, `CutRecipe` (knife), `BulletRecipe`, `ShotgunRecipe` (with a "straight on" option), `RemoveHeadRecipe`. Each is a faithful port of the matching prototype generator (spec sections 9.1–9.6) and produces removers through `Generate`. A static `GoreRecipes.CreateDefaultList()` and `GoreRecipes.Discover()` (all non-abstract `IWoundRecipe` types via `TypeCache` in the editor and reflection at runtime) support the editor's "Add recipe" menu.

### 5.3 Live (P3) — namespace `Laubrary.GoreLab`

```csharp
[DefaultExecutionOrder(10000)]
public sealed class GoreBody : MonoBehaviour {
    public GoreRig rig;
    public bool ApplyWound(IWoundRecipe recipe, Vector2 worldFrom, Vector2 worldTo, int seed = -1, bool flipSide = false);
    public void ResetWounds();
    public bool HasWounds { get; }
    public event Action<GoreWoundEvent> Wounded;
    public IGoreBakeScheduler scheduler;   // default: ImmediateScheduler (bakes at once). Seam for the lazy/queued/Burst versions.
}
public interface IGoreBakeScheduler { void Request(GoreBakeJob job); }   // today: runs the job immediately
```
Behaviour: caches the character's `ZonedAnimationPlayer` and `SpriteRenderer`; in `LateUpdate` reads `CurrentSprite` and `flipX`; if the rig has tags for that sprite and the body has removers, ensures the view slot for (sprite, flip) is baked for the current remover version (bake = copy source pixels → mirror if flip → `GoreCut.CutFrame` → write into the slot's reusable `Texture2D` with `SetPixels32` + `Apply` → one reusable `Sprite` per slot), then swaps `sr.sprite` (and sets `sr.flipX = false` for a flip slot, restoring the player's flip otherwise). Does nothing when the renderer is disabled or no wounds exist. World↔sprite pixel conversion reuses `Laubrary.MetaMapper.MetaMapSprite`. A wound: convert the drag to sprite pixels of the CURRENT VIEW, build a `WoundContext`, call the recipe, append removers (group = next group), bake the shown view, then take the engine result's pieces, gibs and bleed points for flying pieces (pooled `Chunk`s), crumbs and blood droplets (`Chunks`, plus short bleeding from the bleed points that decays, spec section 12). Frames whose needed member tag is missing play as drawn (the engine's `missing` result). Every runtime texture/sprite has an owner that destroys it.

Performance seams to keep open (do NOT implement yet): bake one view at a time on demand (done by design), the `IGoreBakeScheduler` seam, no per-call allocation in the bake path (reuse arrays, textures, results), engine data in flat arrays.

### 5.4 Editor (P4)

`GoreLabWindow : ZuiAssetWindow<GoreRig>` (UI Toolkit). Left: a row of tabs as `Z.Segmented` (**Shape, Rotate, Paint, Frame, Test**; the tab IS the mode) plus the active tab's controls; right: the stage (checkerboard, the frame's sprite as the base, `Painter2D` overlay with member outlines, sphere/box wireframes with U/F/E/W/S marks, paint masks; `ZuiPanZoom`; model: MetaMapper's `MetaStage`), above or beside it the frame strip (grouped by direction, mirrored directions shown read-only). Behaviours: spec section 11 (marquee creation, shape stays put, Shape vs Rotate, the 3D marks, Default orientation per direction and the orientation warning, Paint behind/in front with brush/erase/fill/clear, Hide-far-side hold, opacities, member chooser). **Test** tab: pick a recipe from `rig.recipes`, drag on the stage to wound the shown frame with the SAME engine, Reset. Undo: `Undo.RecordObject(rig, ...)` once per gesture, collapsed per drag, visible Undo/Redo icon buttons + Ctrl+Z/Y; every control has a tooltip; compact style (ui-rules 8b: borderless small buttons, rows shared, icons for undo/redo/delete); no unrequested menus; `LauAssetEditors.RegisterOpen<GoreRig>` in an `[InitializeOnLoad]` link class.

### 5.5 Content and demo (P5, P6)

P5 (needs the editor): importer-correct PNGs (Point, no compression, readable, PPU 16, custom pivot per section 2), a hand-built `LauminaryVersion` (set `Walk8`, members 180/135/90/45/0, `mirrorBuiltIn`), the imp `Zoe` (`ZonedLauminaryView`, `Always → Walk8` motion pose, high max health), a `GoreRig` with the user's tags imported from `GORELAB_USER_TAGS.json` (left-facing columns converted to the flipped copies with mirrored tags), all via an ephemeral script that is deleted afterwards.
P6: scene `Assets/Demos/GoreLabDemo/GoreLabDemo.unity`: ProtoGuy-style camera, eight imps (one per direction, walking in place via pose override), the mouse driver (press-drag-release = the swipe/aim line; the imp it touches is wounded; every tagged member is eligible), a ZuiRuntime panel: damage-type toggles (Slice, Cut, Bullet, Shotgun, Remove head), Reset wounds, a "shotgun: straight on" option. No builder menu left behind.

## 6. Rules for every builder

- Read `D:\Unity\UNITY_DEV_GUIDE.md` sections "Coding best practices", "UI in Unity", "Testing" and the skill file `C:\Users\Lauta\.claude\skills\laubrary\references\authoring.md` before writing code. Editor/runtime UI work additionally requires `D:\AgentGuide\ui-rules.md` and `...\references\ui-layout-rules.md` in full.
- Comments only for WHY or genuinely complex WHAT; never history ("was X before"). Readable code over clever. No unrequested menu items, windows, public surface.
- Everything user-tunable is a serialized field with a tooltip. Tooltips describe effects.
- Doubles in the engine (to reproduce the goldens); float is fine in Unity-facing code.
- Determinism: no `UnityEngine.Random`/`System.Random`; hash and the prototype's rng only.
- Reports to the PM are written for a reader who has not seen the code: what the piece does, what is verified, what is not.

## 7. Acceptance

- Engine: every golden case's integer outputs match (pixel states, changed counts, chunk mask, part sizes, gib counts, body hash); floats within 1e-6; any single-pixel mismatch is investigated as a borderline case and documented.
- Recipes: the generator cases (planeFromSwipe, shotFromSwipe, straightShot, gashFromSwipe) reproduce the golden removers.
- Live: a wounded imp shows consistent wounds across the four frames of its direction and across directions (including flipped views), unwounded frames are untouched, Reset restores everything, and there is no growth of textures per wound.
- Editor: opens a rig, tags a member on a frame, undo/redo works, the Test tab wounds a frame; `ZuiAudit` clean.
- Demo: the Handover Walk is done in the editor with screenshots: open scene, press Play, damage each type, reset.

## 12. API changes announced during the build

(builders append here: what changed against sections 5.x and why)

- **P1 (engine), 2026-10-04 — values:** `GoreCutConfig.Default().jag` changed 1.0 → 1.2 (the prototype's default, spec section 10). `GoreDefaults.FleshyStyle().blood` now has the prototype's FIVE blood colours (added rgb(244,78,56)); `GoreStyle.blood` stays a `uint[]`, so read its `Length`, do not assume 4.
- **P1 — additions (nothing renamed):** `GoreTagMath.Depth / ToUnit / ToScreen` (the prototype's toUnit / toScreen, needed by the shotgun, bullet and knife recipes); `GoreSolid.Intersect(tag, ox, oy, oz, out zf, out zb, out pfx, out pfy, out pfz)` and `GoreSolid.FrontDepth(tag, x, y)` (the prototype's makeSolid, for straightShot / gashFromSwipe: `FrontDepth(tag, X, Y)` = `solidOf(toUnit(B, X, Y, 0)).zf`); `GoreSolidShape`; `GoreRng.ToInt32(double)` (JavaScript `|0`, use it wherever a ported formula feeds a computed double into `Hash`); `GoreColour.Mix(c1, c2, t)` (the prototype's GL.mix, e.g. for tinting gibs toward blood); `GoreRemovers.Plane(...)`, `GoreRemovers.Capsule(...)` (factories), `GoreRemovers.TryGetKind`; `GoreCut.CutMember(...)` (one member alone, the prototype's cutHead3D; CutFrame calls it per member).
- **P1 — meaning of existing fields, as implemented:** (a) `GoreRemover.noiseIndex` is NOT read by the engine: the ragged-edge noise stream is the remover's position among the removers of its member in the list handed to `CutFrame`, exactly as in the prototype (so recipes need not fill it; appending keeps older removers' noise unchanged). (b) Overlap ownership generalised from "head owns against torso": a present, non-skipped member owns its outline pixels against every LATER member in the list (default Head, Torso = the prototype's rule). (c) `GoreMemberInput.skip` means "not cut, not missing, owns nothing". The prototype's noHead flag only stopped ownership and would still cut a head tag if one existed on a noHead frame; with `skip` that head is not cut. No golden case covers the difference. (d) A `MemberTag` with a zero up/forward vector is treated as unset by `Normalize` (up from the angle, forward toward the viewer); `rx == 0` means unset (→ 5, the prototype's default). (e) Ball depth = rx always (the prototype used `rz || rx` for every kind; no ball tag carries rz). (f) A custom remover kind without a `back` direction sends chunks and blood along (0,-1).
- **P1 — not ported:** blood clean-up of source sprites (`cleanBlood`, spec 11.7) is not part of section 5.1 and nobody owns it in section 4; the goldens `cleanBlood_head/all` are therefore unverified. The editor/content builders (P4/P5) need it if source sprites with painted blood are cut.
