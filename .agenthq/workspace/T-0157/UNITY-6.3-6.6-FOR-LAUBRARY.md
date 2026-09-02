# Unity 6.3 → 6.6, judged against Laubrary

*Research pass v2, 2026-09-02. No code changed. Checked against Unity's own per-version "What's New" manuals and package changelogs, not recalled from memory — much of 6.4–6.6 is past my training cutoff, so anything I could not verify is marked **unconfirmed** rather than guessed.*

**v2 changed the rules.** v1 ranked things partly by upgrade cost and LTS timing. The owner's correction: *"do not consider cost of upgrading or refactor. I just want to know what is interesting."* So this version ranks purely on **what capability it unlocks**, and covers **all of Laubrary**, not just Shaper. Version availability is a footnote at the bottom, not a gate.

---

## TLDR — the genuinely interesting ones

**The big three, in order of what they'd let Laubrary DO that it currently can't:**

1. **Custom 2D Lights + Custom 2D Shadows** — you can now define *your own light type and your own shadow shape*. This is the one that changes what Laubrary can be. Today every Laubrary tool bakes lighting into pixels; this would let a Shaper document or a Pyre blast be an actual **live light source in someone's game**, casting real shadows on their scene. That's a new product category, not an optimisation.

2. **Custom 2D render passes in URP** *(you didn't list this — I found it)* — you can inject your own rendering step into the 2D pipeline. SpriteFx is 41 pixel effects that currently run on the CPU over baked buffers. This is the door to those same effects running **live on the GPU, on anything in the scene**, not just on things Laubrary baked.

3. **RenderSprite / RenderSpriteInstanced** — draw enormous numbers of sprites with no GameObjects, while keeping 2D lights, masks and batching. **v1 told you to ignore this and that was too narrow a read** — see the reversal note below.

**Also strongly interesting:** Sprite BlendShape (real squash-and-stretch deformation for Launimator/Zoe), Physics Core 2D (1024 independent physics worlds — deterministic isolated sims for Chunks), Sprite Atlas Analyzer (aims straight at your worst recorded bug), and Scriptable Audio generators (a procedural audio pipeline that mirrors what Laubrary already does for visuals — Zounds' obvious future).

**One thing that is a deadline, not a feature:** `GetInstanceID` and friends become **compile errors** in 6.5. Laubrary ships to other projects, so this breaks in *someone else's* build.

---

## Verdicts that CHANGED now that cost is off the table

v1 dismissed four things. Three of those dismissals were doing cost's work, not judgement's.

### RenderSprite — **reversed, partially**
v1's reasoning: Shaper's measured bottleneck is ~30 ms/frame of CPU *evaluation*, not GameObject overhead, so removing GameObjects fixes nothing. **That is still true for Shaper** and I stand by it there.

But it was wrong to generalise from Shaper to the library:
- **Chunks** shatters things into debris. Debris is many small short-lived sprites — exactly the shape this API exists for.
- **Choreographer** moves N dancers along a shared path. N is the whole point of the tool.
- **Pyre** pools blast players; a pool exists *because* GameObjects are expensive.
- **Larder** generates many wares; **Lazor** does vector rendering.

For those, "thousands of sprites, no GameObjects, still lit and masked" is a genuine capability increase. **Interesting for Chunks, Choreographer, Pyre, Larder, Lazor. Still not interesting for Shaper.**

### Custom 2D Lights and Shadows — **reversed**
v1 said "a different product sharing a word" and stopped there. With cost removed, the real question is what it unlocks — and the answer is significant, because these APIs don't just let you *use* Unity's lights, they let you **define a new light type with a custom shape**, and a **custom shadow shape**.

That means a Laubrary shape could *become* a light. Shaper already computes silhouettes, height fields and surface normals; Pyre already produces glowing forms. Today all of that is frozen into a sprite sheet at bake time. With these providers, a Pyre explosion could genuinely illuminate a player's scene, and a Shaper silhouette could cast a correctly-shaped shadow.

It doesn't replace the deterministic CPU rig — that's what makes the bake self-contained and reproducible. It's an **additional output mode**: "bake it" *or* "light the scene with it".

### Physics Core 2D — **reversed**
v1 said "churn, don't adopt yet" (a cost argument). What it actually offers:
- **Up to 1024 independent physics worlds** (was 128). A physics world you can run in isolation is how you make a *deterministic, reproducible* simulation — which is exactly Laubrary's house style. Interesting for **Chunks** (bake a debris sim identically every time) and for previewing physics in **Mirage** without touching the real scene.
- **Writing transforms via callbacks and tweening events** — a hook between physics and animation. Interesting for **Choreographer** and **Combat2D**.
- **2D physics on any 3D plane** — pairs with "Render 3D as 2D" for anything isometric.
- Better trigger filtering — relevant to **Overlapper**, whose 2D ergonomics are already flagged as awkward.

### Scriptable Audio generators — **reversed**
v1 said "not your problem". But look at what it is: audio clips become **generators you can nest inside other generators**, for sequencing, blending and looping with custom logic. That is *structurally the same idea* as Shaper's node tree and Pyre's layered forms, applied to sound. **Zounds** is the natural home. If Laubrary ever wants procedural audio to sit alongside procedural visuals, this is the pipeline it would be built on.

### Content Directories and URP depth reading — **unchanged, still low interest**
Content Directories is a better asset-packaging system (auto de-duplication, lower memory). Real, but it's a *consumer project's* concern — Laubrary ships source, not builds. URP's depth-buffer read is a mobile bandwidth optimisation on DX12/Vulkan only; a CPU-bake toolkit never touches that path.

---

## The rest, by what it unlocks

### Sprite Atlas Analyzer — **the one aimed at a wound you actually have**
A window (Window > Analysis > Sprite Atlas Analyzer) that reports in detail on every sprite atlas in the project, with six built-in reports for common production problems.

Why it matters here specifically: Laubrary's worst recorded incident was an atlas rebuild that **silently merged frames and dropped sprite entries** (56 → 43, one sprite containing two pairs of legs), discovered only by eye. This tool inspects exactly that class of damage, and can verify project-wide that the never-filter/never-compress rule actually holds. **Tools: Launimator, Pyre, Shaper, Larder — anything that bakes sheets.**

### Sprite BlendShape — **real deformation**
`Sprite.AddBlendShape` gives sprites blend shapes: the groundwork for **FFD** (free-form deformation) — squash, stretch and bend a sprite *without* rigging bones to it. Bone rigging is heavy authoring; blend shapes are cheap expressive motion. **Tools: Launimator, Zoetrope/Zoe, and Choreographer** if dancers should deform as they move.

### Render 3D as 2D — **already available to you**
3D Mesh Renderers and Skinned Mesh Renderers can sit in a 2D scene, receive 2D lights, respect Sprite Masks, and sort correctly among sprites. **Bakery** exists to bake lit 3D into pixel turntables — this lets that 3D coexist live with the 2D instead of only via a bake. Also relevant to **Mirage** staging and to **Shaper's Solids**, which are pseudo-3D facet shapes doing by hand what a real mesh could do.

### 2D Animation multithreading — **confirmed, and it's already in your version**
The 2D IK system was refactored for multithreading via Burst; transform access jobs now run genuinely in parallel; deformation, outline extraction and Sprite Skin registration were all optimised. **Tools: Launimator, Zoetrope/Zoe.** Free speed on work you already do.

### The UI Toolkit family — **the blind spot in v1**
ZUI is mandatory for *all* Laubrary UI, editor and runtime, and v1 barely assessed this. Four things matter:

- **UI Toolkit Profiler modules** — layout time, binding time, events dispatched, and *the reasons a rendering batch broke*. ZUI windows are dense (the new Shaper window builds 724 elements) and **UIAudit** is a vision-free UI linter that currently reasons from metrics. This gives both of them real measured data instead of inference. **The most directly useful UI item on the list.**
- **Panel Renderer** — replaces UI Document for putting UI Toolkit content in a scene, with better **world-space UI** support. That's the interesting part: world-space UI is exactly what **WorldSpaceUINavigation** exists for, and ZUI's runtime story currently rides on UI Document.
- **Advanced Text Generator by default** — full parity with the old text system plus 10–40% CPU improvement. Affects **ZUI runtime, TextSplash, SimpleMenu, StatefulUI**. Unity claims parity; pixel-exact text still deserves a look with your own eyes.
- **Mesh management rework** — notably better UI rendering on WebGL/WebGPU. Only interesting if Laubrary consumers ship to web.

### uGUI additions
- **Layout groups now compute maximum sizes**, propagating up a hierarchy — including `GridLayoutGroup`. **Tools: SimpleMenu, SimpleUI, StatefulUI, UI.**
- **TextMeshPro text updates without allocating** (span-based). Interesting for anything updating text every frame — **Dashboard**'s on-screen log, runtime HUDs.
- **RectTransform coplanar fitting** — keep a tooltip or popup inside its container automatically. **Tools: SimpleMenu, StatefulUI, WorldSpaceUINavigation.**
- **TMP asset-loading callbacks** — hook font material and colour-gradient loading yourself. Niche; matters only if fonts are loaded dynamically.

### Tilemap and profiling
- **Tile Palette improvements** — filter by name, lock brushes/palettes to the active tilemap, flood-fill preview, brush name shown while painting, and a chunk debug visualisation. **Tool: Cartographer.** Quality-of-life, but Cartographer is a tile tool and these are the daily frictions of tile work.
- **New 2D Profiler modules** — separate **2D Animation**, **Tilemap** and **2D Graphics** modules (light textures, light batches, light triangles, shadows). **Tools: Launimator/Zoe, Cartographer, and anything doing 2D lighting.**

### Sprite Editor / skinning workflow
Hover-to-highlight in the Skinning Editor, zoom range control, bone colour editing in the Scene view. Comfort improvements for **Launimator/Zoe** rigging.
⚠️ One behavioural change worth knowing: **editing sprite data that comes from source files is now blocked by default** (overridable in Project Settings). If any Launimator workflow edits sprite data originating from a `.psb`/`.psd`, that will change under you.

---

## Not confirmed — I looked, and could not verify these

These appeared on your list but I found **no matching entry** in Unity's release documentation or the relevant package changelogs. They may be package-level items under different names, marketing phrasing, or roadmap talk:

- **Entity-ID Tilemaps / faster tile setting** — 6.6's tilemap work is Tile Palette UX plus a Tilemap Profiler module. No entity-ID tile-setting API found.
- **Selectable APIs** — I found layout-group max-size calculation and RectTransform fitting, but nothing named around `Selectable`.
- **UI Toolkit computed styles moved to unmanaged memory** — I found *other* UI Toolkit performance work (mesh management, profiler modules) but not this specific change.
- **2D Rendering Layer Masks for Light2D**, **GPU-skinned sprites casting 2D shadows**, **Delaunay sprite mesh generation** — not found as headline features. The 2D Animation changelog does mention refactored internal triangulation/tessellation, which may be the Delaunay item seen from outside.

Treat these as "possibly real, unverified" rather than either confirmed or dismissed.

---

## Index — read it by tool

| Tool | Worth looking at |
|---|---|
| **Shaper** | Custom 2D Lights/Shadows (become a live light) · Render 3D as 2D (Solids) · Sprite Atlas Analyzer · custom 2D render passes |
| **Pyre** | Custom 2D Lights/Shadows · RenderSprite · Sprite Atlas Analyzer · custom 2D render passes |
| **SpriteFx** | **Custom 2D render passes** (effects live on GPU, on anything) · Custom 2D Lights |
| **Chunks** | **RenderSprite** (debris counts) · Physics Core 2D (1024 isolated worlds, deterministic sims) |
| **Launimator / Zoe** | **Sprite BlendShape** (FFD) · 2D Animation multithreading · Sprite Atlas Analyzer · 2D Animation Profiler · Sprite Editor changes ⚠️ |
| **Choreographer** | RenderSprite (N dancers) · Sprite BlendShape · Physics Core 2D tweening hooks |
| **Cartographer** | Tile Palette filtering/locking/flood-fill preview · Tilemap Profiler |
| **Bakery** | **Render 3D as 2D** — its whole premise, live |
| **Mirage** | Render 3D as 2D · Physics Core 2D isolated worlds (preview without touching the scene) |
| **ZUI (editor)** | **UI Toolkit Profiler modules** · Advanced Text Generator |
| **ZUI (runtime)** | **Panel Renderer** (world-space) · Advanced Text Generator · mesh management |
| **UIAudit** | UI Toolkit Profiler modules — real data instead of inferred metrics |
| **SimpleMenu / SimpleUI / StatefulUI / UI** | Layout-group max sizes · RectTransform coplanar fitting · Advanced Text Generator |
| **WorldSpaceUINavigation** | **Panel Renderer** world-space support |
| **Combat2D / Overlapper** | Physics Core 2D — transform callbacks, trigger filtering, contact filter modes |
| **Zounds** | **Scriptable Audio generators** — nestable procedural audio |
| **Dashboard / TextSplash** | Allocation-free text updates · Advanced Text Generator |
| **Larder / Lazor** | RenderSprite · Sprite Atlas Analyzer |
| **Everything shipped** | `EntityId` — see below |

---

## The one hard deadline

**`EntityId` replaces `InstanceID` (6.5).** Object identity moves from a 32-bit int to a 64-bit struct, and the old integer APIs — `Object.GetInstanceID`, `Resources.InstanceIDToObject`, `Selection.instanceIDs` — **produce compilation errors**, not warnings.

Most call sites are a one-word type change. The exception is anything that **stores, serialises or does arithmetic on** an identifier, because 8 bytes don't fit in 4. Laubrary is consumed by other projects, so this surfaces in someone else's build on their schedule, not yours. Worth knowing where your `GetInstanceID` calls are before that day, regardless of when you upgrade.

---

## Footnote — when each thing is actually available

Not a ranking, just facts. You're on **6.3 LTS** (6000.3.10f1).

| Available now (6.3) | 6.4 | 6.5 | 6.6 |
|---|---|---|---|
| Render 3D as 2D · 2D Animation multithreading · Sprite Atlas Analyzer *(separate `2D Tooling` package)* | Custom 2D render passes in URP | Custom 2D Lights/Shadows · RenderSprite · Sprite BlendShape · Physics Core 2D · Panel Renderer · Advanced Text Generator default · Scriptable Audio · **EntityId break** · UI Toolkit mesh management · TMP loading callbacks | 2D/Tilemap/2D-Graphics Profilers · UI Toolkit Profilers · Tile Palette improvements · Sprite Editor changes · Content Directories · URP depth read · uGUI layout/RectTransform/span APIs |

Note that Physics Core 2D was renamed (from `LowLevelPhysics2D`) and moved namespace between versions — so if you do adopt it, expect the API you read about to have a different name than the one you saw first.

---

### Sources
- Unity Manual — *New in Unity 6.3 / 6.4 / 6.5 / 6.6*
- `com.unity.2d.animation` changelog (needle-mirror)
- `com.unity.2d.tooling` — Sprite Atlas Analyzer docs
- Unity Scripting API — `Sprite.AddBlendShape`, `Graphics.RenderSprite`
