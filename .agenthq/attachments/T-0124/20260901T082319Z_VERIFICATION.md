# T-0124 — VERIFICATION

Started as a feasibility prototype for carving real 3D relief into a mesh from Shaper's Tapestry height-field
data, by displacing vertices along their own normal at bake/editor time (never a runtime shader). Mid-task the
PM read Lathe's actual modifier code and corrected the scope: Lathe already has a mature, reflection-discovered
modifier pipeline with a real editor UI, so **this task ships a real, working modifier** —
`TextureReliefMeshModifier` — rather than stopping at a disposable scratch script. Both the original
disposable prototype (Parts 1–4 below) and the shipped production modifier (Part 5) are documented here, in
the order they actually happened, because the prototype's findings are exactly what informed the shipped
class and its known scope limits.

Built and verified in the separate worktree `D:\UNITY\Laubrary Dev - Shaper` (branch on port 7801), because
that is where BOTH pieces this task needed already live side by side: the 245 imported Tapestry height-field
presets (`Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/`, T-0111) and — the first real finding of this
task — the "Solids" 3D mesh generator, which turned out to be **Lathe**, not Pyre. The shipped modifier file
was then ported byte-identical into the primary `D:\UNITY\Laubrary Dev` repo (same branch, `feat/lathe`) and
independently confirmed to compile and be discovered there too — see Part 5.

**What's disposable and what's shipped, explicitly:** the ORIGINAL exploratory scripts under
`D:\UNITY\Laubrary Dev - Shaper\Assets\Temp\` (`T0124_MeshReliefPrototype.cs`, `T0124_SeamCheck.cs`,
`T0124_DumpHeightFields.cs`, plus the later verification harnesses `T0124_LiveModifierVerify.cs`,
`T0124_ShippedClassBoxCheck.cs`, `T0124_UndoCheck.cs`, `T0124_ScrollAndShoot.cs`, `T0124_PrintWindow.cs`,
`T0124_CleanupWindow.cs`) are all disposable scratch — none committed, none referenced by production code.
**`TextureReliefMeshModifier.cs` is the one file this task ships as real code**, at
`Assets/Packages/Laubrary/Runtime/Lathe/Modifiers/TextureReliefMeshModifier.cs` in BOTH
`D:\UNITY\Laubrary Dev - Shaper` and `D:\UNITY\Laubrary Dev` — a proper `LatheMeshModifier` subclass with a
`[LatheModifierInfo("Texture Relief", "Surface")]` attribute, so it IS discoverable from Lathe's own "+ Add
modifier" picker exactly like `SurfaceReliefMeshModifier`/`SurfaceStampMeshModifier` already are. Nothing was
committed in either repo — both are left as uncommitted working-tree changes for review, same convention as
every other Shaper Wave task.

## Part 0 — where "Solids" actually lives (had to be verified, not assumed)

The task briefing's own hint ("Solids… is staying") pointed at Pyre, but a direct search for `solid` across
both codebases found it in neither Pyre nor any Wave-2/3-ported Shaper code — it is **Lathe**
(`Runtime/Lathe/`, `Editor/Lathe/`, namespace `Laubrary.Lathe`, current branch `feat/lathe`), a Laubrary tool
that already builds real 3D primitive meshes (box/sphere/cylinder/cone/ring, `PrimitiveSolidModule` +
`LatheMeshBuilders`) with a modifier stack (`LatheMeshModifier`) applied to real vertex/normal/triangle
buffers (`LatheMeshData`). `SHAPER_THE_DESIGN.md`'s "Solids — the lit 3D primitive family" is describing
Lathe's solids, not a Pyre feature. Both `D:\UNITY\Laubrary Dev` and `D:\UNITY\Laubrary Dev - Shaper` have an
identical copy of Lathe (the worktree is a full clone), so either editor would have worked; this prototype
used the Shaper worktree because the height-field presets live only there.

**Lathe already has two hand-rolled "fake relief" modifiers** (`SurfaceReliefMeshModifier` — a periodic
sin-wave ridge/groove/bump/wave pattern; `SurfaceStampMeshModifier` — scatters little primitives across a
surface) that displace-along-normal using the exact same box-projected UV (`LatheMeshData.BoxProject`) this
prototype reused. This task is effectively "what SurfaceReliefMeshModifier does, but sampling a real
authored height field instead of a formula" — the scaffolding to build a production version already exists
and already matches the shape a real implementation would take.

**The "Box faces never close" bug is unrelated and does not apply here.** That bug lives in a completely
different codebase — Pyre's `Primitive3D` (`Runtime/Pyre/Primitive3D.cs`), a 2D-analytic pseudo-3D pixel
shader family built in the `D:\UNITY\Bakery` sandbox, never merged into the canonical repo. Lathe's own
`LatheMeshBuilders.BuildBox` was read directly: each of the 6 faces is built from its own 4 explicit corner
verts forming one closed quad (24 verts / 12 tris total), and the rendered "undisplaced" contact-sheet frames
below show a solid, correctly-occluding cube with no gaps. Confirmed by inspection AND by render — not a
carried-over assumption.

## Part 1 — does the source material actually look like "circuit board / etched ridges"?

Yes, decisively, for one of the two available categories. The 245 presets split into exactly two families
(`manifest.tsv`): 105 `lines_*` and 140 `plates_*`. A representative sample was decoded from their raw RFloat
`Texture2D` data (`preset.field`, normalized min→max purely for viewing) and rendered flat:

- `plates_GEN10_001`, `plates_GEN11_001` — rounded rectangles, octagons and dot-pairs laid out like PCB
  component pads and connector arrays. This is the "plates with rings of dots and ridges" look named in the
  task almost exactly.
- `lines_GEN1_001` — genuinely reads as circuit traces: parallel double/triple-line "wires" bending at
  45°/90°, with small dot clusters at junctions (solder points). This is closer to "circuit-board etching"
  than "plates" is.

So: **the source material question is settled, no new generator needed.** `plates_GEN10_001` was used as the
displacement input for the rest of this prototype (a family of flat raised/inset plate shapes is the easiest
to visually confirm reads correctly as 3D relief under lighting, versus `lines_GEN1_001`'s thin traces, which
would need far more subdivision to resolve at all — noted as a real quality-vs-density tradeoff in Part 4).

Attached: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0124\renders\source_material_plates_GEN10_001.png` and
`D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0124\renders\source_material_lines_GEN1_001.png` (copies of the
originals rendered to
`D:\UNITY\Laubrary Dev - Shaper\Assets\Temp\HeightFieldPreviews\plates_GEN10_001.png` /
`...\plates_GEN11_001.png` / `...\lines_GEN1_001.png` / `...\lines_GEN4_001.png` in the Shaper worktree, where
the dump script — `D:\UNITY\Laubrary Dev - Shaper\Assets\Temp\T0124_DumpHeightFields.cs` — still sits).

## Part 2 — what was built and measured

Script: `D:\UNITY\Laubrary Dev - Shaper\Assets\Temp\T0124_MeshReliefPrototype.cs`. Pipeline, mirroring
`SurfaceReliefMeshModifier`'s own shape exactly:

1. Build a Lathe Box (`LatheMeshBuilders.BuildBox`, size 1 → half-extent 1 unit) — **24 verts / 12 tris**.
2. Optional subdivision pass: a standard edge-midpoint 4-way triangle split, run 3 times, using an
   index-pair cache so triangles that already share a vertex INDEX (the two triangles inside one Box quad
   face) get the same interpolated midpoint — **486 verts / 768 tris** after 3 iterations.
3. Per-vertex: `uv = BoxProject(pos, normal) * tiling` (same projection `ToMesh()`'s own UVs and
   `SurfaceReliefMeshModifier` use), wrapped into `[0,1)`, `height = field.GetPixelBilinear(u,v).r`,
   `pos += normal.normalized * (height * scale)`. Normals cleared afterward so `LatheMeshData.ToMesh()`
   recalculates them from the displaced geometry — the identical "escape hatch" comment already documents in
   `SurfaceReliefMeshModifier`/Taper/Noise/Twist.
4. Rendered with a lighting rig that is a direct copy of `Editor/Lathe/LathePreview.cs`'s own two-directional-
   light + flat-ambient rig (not a bespoke one), via `PreviewRenderUtility`, so "does it read as relief under
   Shaper's/Lathe's existing lighting" is tested against the real lighting model, not an approximation of it.

**Timings** (`System.Diagnostics.Stopwatch`, sample+displace loop only, asset load and mesh build excluded):

| Pass | Verts | Time |
|---|---|---|
| Low-poly (no subdivision) | 24 | 0.38–0.56 ms |
| Subdivided, readable scale | 486 | 0.09–0.13 ms |

The low-poly number reads *higher* despite fewer vertices — that's JIT/first-texture-sample warm-up noise
(confirmed by re-running: the FIRST `PreviewRenderUtility` render in a domain also came back blank once, a
known Unity editor quirk, fixed by discarding a warm-up render before the real captures). The real number is
"however you count it, a few hundred vertices costs a fraction of a millisecond" — the task's own prediction
(**"cost is close to a non-issue"**) is confirmed, not just assumed.

**Quality — subdivision matters, exactly as predicted.** Full 6-frame contact sheet at
`D:\UNITY\Laubrary Dev - Shaper\Assets\Temp\HeightFieldPreviews\` (`00`–`05`); the three that matter are
copied into this task's own folder as attachments:

- `01_lowpoly_undisplaced.png` / `03_subdivided_undisplaced.png` (Shaper worktree only, not attached —
  they're pixel-identical to each other) confirm subdivision alone changes nothing visually and that the Box
  mesh is solid/closed before any of this prototype's code touches it.
- Attached as `renders/relief_lowpoly_unresolved.png` (source: `02_lowpoly_displaced_readable.png`) — the
  24-vert box displaced by `plates_GEN10_001` at scale 0.15. The field's actual pad/ridge shapes are **not
  resolved at all** — with only 4 verts per face there's nothing for the pattern to bend, so it reads as a
  single smooth diagonal warp, not relief.
- Attached as `renders/relief_subdivided_reads_correctly.png` (source: `04_subdivided_displaced_readable.png`)
  — the SAME field, SAME scale, on the 486-vert subdivided box. The flat-topped raised plate shapes read
  clearly as real 3D geometry with correct specular highlights along their edges — this is the shot that
  answers the task's central question, and the answer is **yes, it reads as real relief, not a texture**.

## Part 3 — adversarial self-check: degenerate geometry at extreme values

Required by the task, run deliberately against my own prototype rather than skipped. Same subdivided mesh,
same field, `scale = 2.0` instead of `0.15` (field peaks at 1.346, so worst-case displacement ≈ 2.7 units on
a box of half-extent 1 — well past "through the opposite face").

- **Inverted face normals: 512 of 768 triangles (66.7%)** — a face's post-displacement winding now points the
  *opposite* way from its pre-displacement normal, the direct signature of geometry folding over itself.
  Visually confirmed too: attached `renders/relief_EXTREME_selfintersecting.png` (source:
  `05_subdivided_displaced_EXTREME.png`) shows visibly torn, self-intersecting shards, not a rougher relief.
- Near-zero-area (fully collapsed) triangles: 0 — at this scale the mesh folds rather than collapses flat.

**This is real and not hardened against.** At extreme field values a naive per-vertex-along-normal
displacement WILL self-intersect; a production version needs either an authored/clamped scale-vs-mesh-size
relationship, per-vertex displacement clamping, or a warning when a solid's own bounds are smaller than the
field's likely displacement range. Named as a scope limit, not papered over.

**Second, separate finding: face-to-face seams open even at the "readable" scale.** Script:
`D:\UNITY\Laubrary Dev - Shaper\Assets\Temp\T0124_SeamCheck.cs`. Lathe's `BuildBox` gives each of the 6 faces
its OWN 4 corner vertices for correct hard-edge shading — two faces meeting at a box edge share a *position*,
not a vertex *index*. Displacing each vertex along its own (per-face) normal therefore moves two
originally-coincident edge vertices in two different directions. Measured directly: of 92 groups of
originally-coincident vertices (shared box edges/corners after subdivision), **16 groups opened a gap**, up
to **0.21 world units** (on a box of half-extent 1) even at the tame `scale = 0.15` used for the "good" render.
This is inherent to displacing a hard-edged, per-face-unwelded mesh this way — subdivision does not fix it —
and would need either edge-vertex welding before displacement (with a re-split back to hard edges after,
recomputing normals per triangle) or an authored decision to accept soft/rounded edges on any solid that gets
this treatment. Not visible in the readable-scale renders above only because the camera angle happens not to
frame a seam edge directly — it is a real, measured artifact, not a hypothetical one.

## Part 4 — honest scope limits

1. **True boolean/CSG carving was correctly not attempted** — out of scope by the task's own instruction. This
   prototype only ever raises/lowers a surface along its own normal; it cannot cut a tunnel through a solid or
   change silhouette from a grazing angle the way real CSG would.
2. **Seam-cracking at hard mesh edges (Part 3) is unresolved.** A production version needs a real answer
   (weld-then-resplit, or accept it and design around it) before shipping on multi-face primitives — it will
   not go away on its own, and gets worse as `scale` increases.
3. **Self-intersection at extreme field values (Part 3) is unresolved** — no clamping/guard was built, only
   measured and reported.
4. **Only one primitive (Box) and one preset (`plates_GEN10_001`) were tested end-to-end.** `lines_GEN1_001`
   (the thin-trace family) was only spot-checked visually as flat 2D — it was NOT run through the
   displacement pipeline, and its much finer detail would need noticeably more subdivision (or a
   higher-resolution field sample) to resolve at all; this is a real follow-up, not assumed to "just work."
5. **UV mapping is the same fixed box-projection every other Lathe modifier already uses — confirmed BROKEN
   on a curved primitive, not just theoretically worse.** Part 5 below ran the real shipped modifier on a
   48-segment/24-ring Sphere (1,225 verts — plenty of density) and the result is not readable relief at all,
   it's chaotic spiky noise: `BoxProject` picks a different dominant axis per vertex, and on a continuously
   curved surface neighbouring vertices flip which axis they project onto near every diagonal direction, so
   the sampled height jumps discontinuously between adjacent vertices. Box-projection is fine on a Box's flat
   faces (this is what every render in Parts 2–3 and 5 uses) but is NOT a usable UV convention for Sphere,
   Cylinder, or Cone with this modifier as shipped — a real production rollout needs a per-primitive-aware
   projection (true spherical/cylindrical UV) before it's usable on anything but flat-faced primitives.
6. **Subdivision is a simple uniform 4-way linear split** (not adaptive, not curvature- or
   detail-aware) — fine for this prototype's box at 3 iterations (768 tris), but a real feature would want
   either a higher fixed iteration count exposed as an authored dial, or adaptive subdivision that puts
   density only where the field has detail, to avoid paying full uniform density everywhere for detail that
   exists in only part of the surface.

## Part 5 — the shipped modifier, verified through real production code paths

`TextureReliefMeshModifier` (`Runtime/Lathe/Modifiers/TextureReliefMeshModifier.cs`, both repos): a proper
`LatheMeshModifier` — `Texture2D heightField`, `[Range(0.1,20)] float tiling = 3`,
`[Range(-2,2)] float amount = 0.15` — whose `Apply()` is exactly the algorithm the disposable prototype (Part
2) already proved out: `BoxProject(pos, normal) * tiling` → wrap to `[0,1)` → `heightField.GetPixelBilinear`
→ displace along the normal → `data.normals.Clear()`. Guards `heightField == null || !heightField.isReadable`
so a missing/unreadable texture silently no-ops instead of throwing on every preview repaint. Not a copy of
the prototype's logic living twice — the prototype's own algorithm graduated into this file.

**Discoverability (does "+ Add modifier" actually list it) — tested via reflection, in BOTH projects, not
assumed from reading the attribute:** `LatheWindow.ModifierCatalog()` is a private static method; invoked
directly via `System.Reflection.MethodInfo.Invoke` (script:
`D:\UNITY\Laubrary Dev - Shaper\Assets\Temp\T0124_LiveModifierVerify.cs` step 1, and
`D:\UNITY\Laubrary Dev\Assets\Temp\T0124_CatalogCheckPrimary.cs` for the ported copy). Result in the Shaper
worktree: `FOUND -- group='Surface' label='Texture Relief'`. Result in the primary project, after copying the
file and confirming `check_compile_errors` reports clean: `FOUND in primary project's LatheWindow modifier
catalog, group=Surface`. This is the exact scan the real "+ Add modifier" button runs — not a re-implementation
of it.

**Operable in the real, live `LatheWindow` — not just unit-tested in isolation.** A scratch `LatheSpec` asset
(`Assets/Temp/T0124_TestLathe.asset`, Shaper worktree, outside Lathe's own `Assets/Lathe` browse folder so it
never appears in the real asset picker) was built with one Sphere solid, then `TextureReliefMeshModifier` was
added to its `modifiers` list via `Activator.CreateInstance` — the literal call `ShowModifierPicker`'s own menu
item makes — with `heightField` set to the real `plates_GEN10_001` preset texture. The actual `LatheWindow`
`EditorWindow` was opened, bound to this spec via its own (reflection-invoked) `SetAsset`, and screenshotted
live via Win32 `PrintWindow` (the same reliable-regardless-of-z-order technique used elsewhere in this
project). Attached: `renders/live_editor_UI_TextureRelief_card.png` — shows the real "Modifiers" section with
a "Texture Relief" card: On/Remove row, a `Height Field` object-picker correctly showing the assigned
`plates_GEN10_001_F` texture (ZUI's `ObjectField`-based reflection drawer, which already handles any
`UnityEngine.Object`-derived field type — no new ZUI work was needed), and `Tiling`/`Amount` MicroSliders. The
"+ Add modifier" button is visible below it, ready to add another.

**Undo-safety — functionally tested, not just pattern-matched against the window's code.** Lathe's own
convention (`LatheWindow.Modifiers.cs`: `ZuiReflect.Options.OnBeforeChange = () => Undo.RecordObject(spec, "Edit
Lathe")`) needs no per-modifier Undo code at all — every reflected field on every modifier gets it for free.
Verified this actually works for the new class, not just that the pattern is present: script
`D:\UNITY\Laubrary Dev - Shaper\Assets\Temp\T0124_UndoCheck.cs` recorded `relief.amount` (0.15), mutated it to
1.234 through the exact `Undo.RecordObject` → mutate → `EditorUtility.SetDirty` sequence the window uses, then
called `Undo.PerformUndo()`. Result: **`amount` reverted to 0.15 — Undo worked.** Worth checking directly
rather than assuming, since a `[SerializeReference]` list element (which is what a `LatheSolid.modifiers` entry
is) is exactly the kind of nested managed-reference state this project has previously seen behave unexpectedly
under Undo/serialization (see the `serializereference-data-loss-hazard` project memory) — here it didn't.

**Curved-primitive UV distortion — the one thing this live pass surfaced that the Box-only prototype couldn't.**
The live Sphere test (`renders/live_sphere_before_no_modifier.png` /
`renders/live_sphere_after_boxproject_distortion.png`) does NOT read as relief — it's chaotic spiky noise, not
"circuit-board plating on a ball." Diagnosed as `BoxProject`'s per-vertex dominant-axis selection flipping
discontinuously across a continuously curved surface (see scope limit 5 above) — confirmed NOT a vertex-density
problem, since the sphere had 1,225 verts (2.5× the subdivided box's 486). A second render,
`renders/shipped_modifier_on_subdivided_box.png`, ran the SAME shipped class's real `Apply()` method directly
against a manually-subdivided Box `LatheMeshData` (Lathe has no subdivision modifier yet, so this stands in for
one) and reproduces the clean, readable relief from Part 2 exactly — proving the shipped class itself is
correct, and that the Sphere result is a UV-projection limitation, not a bug in the displacement math.

## Summary — is this worth pursuing, and how much more work is a real version?

**Yes — and a first real version already exists.** The core technique works exactly as the task's
cost/quality analysis predicted: bake-time cost is a non-issue (sub-millisecond even at hundreds of vertices),
quality is bandwidth-limited by vertex density and genuinely fixed by adding density, normals recompute
correctly and light the result as real geometry (not a lit texture), and the source material — Tapestry's
plate/dot presets in particular — already looks like the "tech marking" aesthetic that motivated the idea,
with zero new content needed. `TextureReliefMeshModifier` ships this on Box-family (flat-faced) primitives
today, in both repos, discoverable and operable through Lathe's real UI, with Undo working correctly — not a
hypothetical, a verified fact per Part 5.

**Remaining effort to call this production-complete**, given the infrastructure (`LatheMeshModifier`,
`LatheMeshData`, box-projected UV, "clear normals to force recalculation") is already proven and now has one
real modifier built on it:

- **Curved-primitive UV support (Sphere/Cylinder/Cone)** — **medium**, and now the highest-priority gap: the
  modifier is confirmed NOT usable on anything but flat-faced primitives today (Part 5's Sphere finding). Needs
  a per-primitive-aware projection, not a bigger tweak to `BoxProject`.
- **A subdivision pre-pass** (Lathe has none yet) — **small to medium**; this task's own linear 4-way
  edge-midpoint split (used to stand in for one in Parts 2 and 5) is a reasonable starting point for a real
  `SubdivideMeshModifier`, authored iteration count instead of a hardcoded value.
- **Seam-cracking at hard mesh edges** (Part 3, still present in the shipped class exactly as measured) —
  **medium**, the real unknown-sized piece of this estimate; honest options range from "accept it as a known
  v1 limitation" (cheap) to "weld/resplit edges before and after displacement" (a real geometry-processing
  feature, not a one-line fix).
- **A displacement-magnitude guard** against the self-intersection finding (Part 3, also still present as
  shipped — `amount`'s `[Range(-2,2)]` bounds it somewhat but does not prevent self-intersection within that
  range on a small solid) — **small** (a clamp relative to the solid's own bounds, or an authored warning, is
  enough for v1).

None of this requires CSG, non-manifold-geometry handling, or anything the task's own scope cut named as
risky — it is a natural, incremental extension of Lathe's existing modifier stack, and the first increment is
already merged into the working tree (uncommitted, pending review).
