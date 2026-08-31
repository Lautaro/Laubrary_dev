# T-0098 — Verifier D: adversarial check of `PYRE_GUG.md`

Research only. No code changed, no Unity run, no Coplay, no subagents spawned. Every finding below carries a `file:line` citation or a re-runnable command. Report line numbers refer to `D:\UNITY\Laubrary Dev\PYRE_GUG.md` as of 2026-08-30.

**Verdict in one line.** The report's central architecture is sound and its big claims hold up — the five-stage rule, the four-mechanism dispatch, the "no effect reads generator-written data" claim, the hand-drawn-effect verdicts, the Playback3D and PyrePlus cull cases, and the headline usage numbers (123 / 305 / 78% / 20) all survive independent verification. **One delete-gating claim is outright false (§11.4), two counted claims are wrong (the 15/11 effect counts, the "five outward-reaching post effects"), one usage statistic is materially overstated in the wrong direction (the mask usage), and the biggest interpretive gap is that three of the four "projects" are clones of the fourth — the report never says so.**

---

## Method

Asset census re-run independently, not taken from the report. Scope: every `.asset` file under `D:\UNITY`, skipping `Library/Temp/obj/bin/.git/node_modules/Logs/.vs/UserSettings/Build/.idea/MemoryCaptures` (7,488 `.asset` files scanned, ~40 s — no full-drive grep needed). A file counts as a Pyre spec iff it contains a `^\s*shapeForm:` line. Layers were split out of the `  layers:` block on `^  - ` boundaries; `form:` is serialised as a bare `form:` line with `rid:` on the following line (NOT inline `form: {rid: N}` — that is why a naive inline regex reports zero plug-in usage), and each rid was resolved against the file's own `RefIds:` `type: {class: …}` table.

---

## 1. WRONG — claims that are factually false

### 1.1 §11 item 4 (line 300): "Two saved layers point at a retired generator slot and are silently drawing plain Discs instead." — **FALSE. Zero layers do this.**

Four layers across the corpus carry `shapeForm: 17` (the retired `ForkBlast` slot). **All four also carry a non-null `form` plug-in reference**, and `PyreRenderer.cs:248-252` dispatches on `layer.form` first and returns, so the retired enum value is never read:

| Asset | `shapeForm: 17` layers | plug-in actually set |
|---|---|---|
| `Laubrary Dev\Assets\Pyre\New Pyre Plus§.asset` | 2 | `ArcBurstForm`, `RadialJetForm` |
| `Laubrary Dev - TapestryPort\Assets\Pyre\Floating Disc Hit Plus.asset` | 1 | `ForkBlastForm` |
| `Laubrary Dev - TapestryPort\Assets\PyrePlus\New Pyre Plus§.asset` | 1 | `ForkBlastForm` |

Measured count of layers with a retired `shapeForm` **and** `form == null`: **0**. Nothing anywhere is silently drawing a Disc because of a retired slot. This is orchestrator-original — no evidence file makes this claim — and it is the only item in §11 that asserts live data corruption. **Delete or replace it.**

(The claim is *nearly* true of something else — see §4.1 below, which found two genuinely dead SerializeReference classes.)

### 1.2 §1 line 13, §2 line 40, §6 line 138: "**fifteen** warping effects" — **there are 17.** §6 line 139: "**11** recolour effects" — **there are 12.**

Counted by resolving every concrete class in `Runtime/SpriteFx/` + `Runtime/Pyre/` to its stage base:

| Stage | Actual concrete count | Report / B says |
|---|---|---|
| `GeometryModifier` | **17** | 15 |
| `PixelModifier` | **12** | 11 |
| `PostModifier` | **10** | 12 (B) |
| `EdgeModifier` | 1 | 1 |
| `SimulationModifier` | 1 | 1 |
| **Total** | **41** | 41 ✅ |

The 17 geometry: Skew, Scale, Rotate, Wobble, SunburstWobble, RingWave, PointBlast, Profile, Ground, Sunburst, PulseRings, Turbulence, Curl, CurlProgress, Sphere, Smudge, PinWarp (`SpriteFxModifiers.cs:203,228,266,302,339,386,437,546,583,779,830,2653,2882,3007,3084,3167,3340`). The 12 pixel: the 11 in `SpriteFxModifiers.cs` plus `ColorRemapModifier` (`SpriteFxColorRemap.cs:142`).

This came in from `B-compatibility.md:92-99`, whose headings say "(15)", "(11)", "(12)" while listing 17, 12 and 10 names — and which **explicitly flagged its own error** at line 93 (*"That is 17 lines"*). The report inherited the wrong headline and dropped B's correction. The total (41) is right, so the three sub-counts must be 17/12/10.

### 1.3 §6 line 145 / §11 item 8: "**Five** post effects deliberately draw outside the silhouette — bloom, outline, drop shadow, chromatic aberration, **fake light**." — **Four. Fake light is not one of them.**

`RelightModifier` (display name "Fake light") explicitly overrides `OutwardReachPx() => 0`, with the comment *"Only ever recolours where pixels already are — the light never paints into empty space"* — `Runtime/SpriteFx/SpriteFxRelight.cs:401`. The four non-zero overrides are Bloom (`SpriteFxModifiers.cs:1790`), Outline (`:1891`), Chromatic aberration (`:2105`), Drop shadow (`:3389`). Inherited from `B-compatibility.md:100`.

### 1.4 §4 line 96: "Fire can use the swarm as multiple emitters; Fireball cannot. **That asymmetry is invisible and looks like a bug.**" — **It is not invisible; the UI states it explicitly.**

`Editor/Pyre/PyreWindow.cs:2179-2186` renders a note in the Swarm section reading *"Fireball is single-source — heat blooms from one central point, folded into kaleidoscope arms. The Swarm doesn't place it (Fireball stays single-source by design)…"*, and the Fire branch of the same note points the user at the `Swarm emitters` toggle. The design recommendation (give Fireball the swarm option) is untouched by this; only the "invisible" justification is wrong.

### 1.5 §11 item 3 (line 299): "The melt-into-a-blob control is offered where it cannot act — on every plug-in scene, **both fire sims** and 3D Playback — with no warning. **Only the fire case warns.**" — **Inverted for the fire case.**

`RebuildSwarm` (`Editor/Pyre/PyreWindow.cs:2174-2192`) hits `return;` for Fire/Fireball *before* reaching the Coalesce radio (drawn at `:2491-2495`) whenever the swarm does not drive them. So for both sims the control is **not offered at all** — the note replaces the whole body. The set of places the radio *is* offered and is inert: plug-in forms, Playback3D, swarm-off layers, **and Fire with `fireSwarmEmitters` ON** (that branch skips the early return, `:2176`, yet `RenderLayer` still returns at `PyreRenderer.cs:258` before `RenderSwarm`). The last case is the genuinely unwarned one and the report misses it.

---

## 2. OVERSTATED — true in direction, wrong in magnitude or framing

### 2.1 §1 line 23 / §5 line 125: "the cut-out mask is used by **3 layers** and the light mask by **none**" / "*The most intricate subsystem in Pyre has, in practice, never been used.*" — **The conclusion is right and the number is too generous. Effective usage is ZERO on both.**

Measured over all 305 layers:

| Field | Result |
|---|---|
| `matteEnabled: 1` | **0 of 305** |
| `matteRole: 1` (WriteMatte) | 3 — but every one of them on a layer with `matteEnabled: 0` |
| `matteRole: 2` (LumaMatte) | 0 |
| `clipByChannel >= 0` | **0 of 305** |
| `heightFromChannel >= 0` | **0 of 305** |
| `matteWriteLuma: 1` | 3 (the same three layers) |

`matteEnabled` is the hard master gate — `PyreRenderer.Layers.cs:94-96` (`bool matteOn = layer.matteEnabled; p.isMatte = matteOn && …`) and `Pyre.cs:203` states it in so many words (*"matteRole/clipByChannel/heightFromChannel only act when this is true"*). So the three WriteMatte layers do nothing at all. Two further corrections: those three layers set `matteWriteLuma: 1`, i.e. they deposit **luminance** (path C), so they are not even an instance of the *cut-out/solidity* mask the sentence names; and all three are the same asset (`Floating Disc Hit Plus.asset`) in three clone projects, so it is **one** authored layer, not three.

Correct statement: *nothing anywhere writes an enabled matte, nothing clips by a channel, and nothing consumes a height channel — neither mask system has ever been used in a saved effect.*

### 2.2 §1 line 23 / §13 line 321: "123 saved effects, ~305 layers, across four projects" — **counts exact, but three of the four projects are clones and the report never says so.**

| Project | spec files | layers |
|---|---|---|
| Laubrary Dev | 32 | 79 |
| Laubrary Dev - TapestryPort | 31 | 77 |
| SplashText | 31 | 74 |
| Chunks Dev | 29 | 75 |
| **Total** | **123** | **305** ✅ |

But: **only 32 distinct spec filenames exist across all 123 files**, and only 75 distinct content hashes. The four projects hold four copies of essentially one authored set. `OutBurner\Assets\Pyre\` also contains a `PyreLayerLibrary.asset` but zero specs, which is why the project count is 4 and not 5.

The real authored corpus is therefore **≈32 effects / ≈79 layers**, not 123/305. Every ratio in the report survives (they are all fractions of the same inflated denominator), and the "grown ten times faster than it has been used" conclusion gets *stronger*, not weaker. But the document twice presents "four projects" as breadth of evidence when it is really one body of work counted four times, and the verification brief specifically called this out. **Add one sentence.**

### 2.3 §1 line 23: "**Ten** of the seventeen live built-in generators have never been used once" — **true only under the looser of two readings; 11 have never rendered.**

- Distinct `shapeForm` int values ever written: Disc, Gem, Crescent, Sparkle, Streak, Fire, Box = 7 → **10 unused**. This is what the report counted.
- Generators that have ever actually **drawn a pixel**: the sole `Gem` layer is `Green Lantern.asset`, whose `form` is an `OrbForm` that overrides it — so Gem has never rendered → **11 unused** (Gem, Sprite, Pyramid, Can, Orb, Ring, Text, Star, Fireball, Polygon, Playback3D).

"Never been used once" reads as the second. Either number is defensible; say which.

### 2.4 §6 line 138: "…silently do nothing on eight generators: Text, Gem, Box, Pyramid, Can, Orb, Ring, and the height-relief mass. **That's over half of the built-in list.**"

Seven of those eight are built-ins → 7/17 = 41%, not over half. It only clears half (10/17 = 59%) if you also count Fire, Fireball and Playback3D, which do ignore geometry (`RenderFireLayer`, `RenderFireballLayer` take no `mods` parameter at all — verified below) but are excluded from the list of eight. The sentence is internally inconsistent: pick one framing. §1 line 13's "roughly half the generators skip the bend stop" is fine (12 of 29 = 41%).

### 2.5 §5 line 121: "Essentially all of them, on both sides… The only exception is 3D Playback." — **a second exception was dropped.**

`B-compatibility.md:38` records that the height consumer can **never** be a matte source: `p.isHeightConsumer` is only assigned after the `isMatte`/`isLuma` branches have `continue`d (`PyreRenderer.Layers.cs:102-105` — verified, the two `continue`s are at `:102` and `:103`, `isHeightConsumer` at `:105`). A layer is a writer **or** a consumer, never both. The report keeps the "can be matted" half and loses the "cannot be a source" half.

### 2.6 §8 line 182: "twelve patterns" — 11 real ones plus `none`.

`D:\CODEZ\AgentHQ\3D Shaper\project_document.py:18` — `PATTERN_TYPES` has 12 entries, of which the first is `"none"`. `C-shaper3d.md` says "11 named 2D patterns" in §1 and "12 `PATTERN_TYPES`" in §3; the report picked 12 without noting the source contradicts itself. Trivial, but it is a user-facing capability count.

### 2.7 §11 item 7 (line 303): "…and **four more** describe behaviour that has since changed."

`A-generator-inventory.md:404-408` documents **two** stale-behaviour comments (`Pyre.cs:79-80` on Fire and the swarm; `Pyre.cs:611-612` on Playback3D drawing a placeholder) plus two dead-file citations. B separately found four stale `DisabledInPicker` reason strings, but those are already §11 item 6. As written, "four more" is unsupported.

---

## 3. FINE — verified correct (and where I can strengthen it)

Every one of these I re-derived from the code or the assets myself.

**Usage statistics.**
- 123 spec files, 305 layers, 4 projects — **exact** (see 2.2 for the clone caveat).
- **78% Disc**: 237 of 305 = **77.7%** ✅.
- **4 of 9 plug-in generators never used** ✅. Used: `OrbForm` (1), `TorchForm` (1), `ArcBurstForm` (1), `RadialJetForm` (1), `ForkBlastForm` (2). Never used: **Inferno, Plasma Bloom, Jet, Explosive Jet**.
- **"Mass" modes on 20 layers** ✅ — `coalesce: 1` (Fuse) on 12, `coalesce: 2` (Ramp) on 8, and all 20 are genuinely *effective* (checked: every one has `form == null`, `swarmEnabled: 1`, and a non-Fire/Fireball/Playback3D `shapeForm`, i.e. all four conditions `PyreRenderer.cs:248-282` requires). **Strengthening**: the third member of Family 3 — the mask-fed height field — contributes **0** of the 20, so §10's proposed "Height relief + mask-fed height field → one Height field" fuses a used technique with a never-used one.
- **No saved effect anywhere uses Playback3D** ✅ (`shapeForm: 18` count = 0).
- **Fork Blast: nothing in this project, two saved effects in a sibling clone** ✅ — both `ForkBlastForm` assets are under `Laubrary Dev - TapestryPort`, zero under `Laubrary Dev`.
- **Smudge used by two saved effects, Swirl by two, Vortex field and Pin warp by none** ✅ — across all 123 specs: `SmudgeModifier` in `Bars Tentacle Plus.asset` + `MetaBall Blast Plus.asset`; `CurlModifier` in `Curl Plus.asset` + `Explotion Twirl Plus.asset`; `CurlProgressModifier` 0; `PinWarpModifier` 0. Also 0 for `EdgeWarpModifier` and `PixelFluidModifier`.

**The five delete/cull gates (§3 of the brief), all confirmed:**

| Claim | Verified |
|---|---|
| (a) hand-drawn authoring unreachable | The four symbols appear in exactly three files repo-wide: `SpriteFxModifiers.cs` (definitions), `Editor/Pyre/PyreWindow.Modifiers.cs:354-360`, `Editor/SpriteFx/SpriteFxStackView.cs:321-327`. Both block-lists say *"Pyre1's own preview canvas"*. ✅ |
| (a) Pin warp frame hook has no caller | `grep -rn "SetFrame(" --include=*.cs` (minus `SetFrameIndex`) returns **exactly one line**: the declaration at `SpriteFxModifiers.cs:3350`. ✅ |
| (b) pixel-fluid offered in the Stack, unhandled | `SpriteFxStackView.Catalog()` filters only `EdgeModifier` (`:343`); `PixelFluidModifier` is concrete with an implicit parameterless ctor (`SpriteFxSimulationModifiers.cs:115`); `SpriteFxBurst.IsShaped` (`:480-483`) does not list it, so it is not batched; `RunStack`'s switch (`SpriteFxBurst.cs:640-667`) has cases for `GeometryModifier`/`PostModifier`/`PixelModifier` only. Falls through to nothing. ✅ |
| (c) auxiliary-map gate has no field of its type | `grep -rn "SpriteFxAuxMapGate\|AuxMapGate" --include=*.cs` over all of `Assets/` returns 7 hits: the class decl (`SpriteFxAuxMapGate.cs:35`), its own `Clone()` (`:143`,`:145`), and 4 comments. No field, no constructor call, no `Prepare`/`Bake` caller. ✅ |
| (d) edge-perturbing effect has no caller | `EdgeOffset` appears only as the abstract decl (`SpriteFxModifiers.cs:2570`) and one override (`:2618`); `EdgeSoftness` likewise (`:2576`, `:2630`). Excluded from both add-menus (`PyreWindow.Modifiers.cs:384`, `SpriteFxStackView.cs:343`). ✅ |
| (e) Playback3D renders nothing in the bake | `PyreRenderer.cs:269` — `if (layer.shapeForm == ShapeForm.Playback3D) return;`, ahead of every draw path, with the nine-line comment at `:262-268`. ✅ |
| (f) two PyrePlus folders empty | `Packages/Laubrary/Runtime/PyrePlus` and `.../Editor/PyrePlus` each contain only `.` and `..`. ✅ (Their `.meta` files and the two `PYREPLUS_*_DESIGN.md` docs also still carry the name — not claimed otherwise, but they are the other half of a clean-up.) |

**The compatibility claim (§4 of the brief) — re-measured by brace-matching each draw routine's body in `PyreRenderer.cs` and counting `ApplyGeo`/`ResolveSample`/`ApplyPix`:**

| Routine | lines | geometry hook | pixel hook |
|---|---|---|---|
| `DrawFacetSolid` (Gem/Box/Pyramid/Can) | 4152-4409 | **0** | 2 |
| `DrawOrb` | 4418-4599 | **0** | 2 |
| `DrawRing` | 4609-4784 | **0** | 2 |
| `RenderTextLine` / `DrawTextChar` | 3859-3915 / 3925-4054 | **0 / 0** | 0 / 1 |
| `RenderPlusRampField` (height relief) | 1697-1837 | **0** | 1 |
| `RenderFireLayer` | 392-459 | **0** | **0** |
| `RenderFireballLayer` | 757-801 | **0** | **0** |
| `RenderHeightConsumer` | 1849-1883 | **0** | **0** |
| `DrawParticle` (Disc) | 2810-3096 | 1 `ApplyGeo` | 1 |
| `DrawCrescentBody`/`Star`/`Polygon`/`Sparkle`/`Sprite`/`Streak` | — | 1-3 `ResolveSample` | 1 each |
| `RenderPlusFusedField` (Fuse) | 1617-1674 | 1 `ApplyGeo` | 1 |

Exactly the eight the report names for bend (Text, Gem, Box, Pyramid, Can, Orb, Ring, height-relief) and exactly the four it names for recolour (Fire, Fireball, Playback3D, mask-fed height field). ✅

**"No effect anywhere reads anything a generator wrote" (§6 line 143) — CONFIRMED, and I can strengthen it.** `SpriteFxAuxMap.BuildMap`'s only data input is `Color32[] px` — the finished buffer — from which it derives luma, alpha, saturation, hue, Sobel edges and chamfer distance (`SpriteFxAuxMap.cs:16-27`, `:39-55`). `RelightModifier` builds its height field by calling that same `BuildMap` on the buffer it was handed (`SpriteFxRelight.cs:~463`), choosing between `EdgeDistanceIn`/`Alpha`/`Luma` via `SfxReliefSource` (`:46-52`). There is no channel, no side buffer, no generator-authored map anywhere in the package. ✅

**Other spot-checks that hold:** seven of nine plug-ins ignore the Fill — five `UsesFill => false` declarations (`ArcBurstForm.cs:50`, `OrbForm.cs:51`, `PlasmaBloomForm.cs:121`, `TorchForm.cs:55`, `Jet/JetFormBase.cs:23`) with the Jet base covering three forms ✅. The `ShapeForm` enum is 19 cases with `Inferno`/`ForkBlast` `[Obsolete]` → 17 live ✅ (`Pyre.cs:111-118`). §10's "29 selectable techniques" = 17 live enum + 9 plug-ins + 3 field-pass ✅. **Shaper cannot export**: an independent grep of `D:\CODEZ\AgentHQ\3D Shaper` for `toDataURL|download|export|spritesheet` across all `.py` and `.html` returns **zero hits** ✅ — and its registries confirm 8 shapes, 7 surfaces, 4 component modes, 7 extrusion techniques (`project_document.py:16-29`) ✅.

**§11 item 8 can be upgraded from "unconfirmed" to CONFIRMED.** `grep -rn "OutwardReach"` returns callers only in `Editor/SpriteFx/SpriteFxStackWindow.cs:902` and `Runtime/SpriteFx/SpriteFxFilter.cs:367`; `grep -rn "SetPicture\|padX\|OutwardReach"` over `Runtime/Pyre` + `Editor/Pyre` returns **nothing**. Pyre's `ApplyLayerPost` (`PyreRenderer.cs:1354`, called at `PyreRenderer.Layers.cs:174`) runs post passes on the layer's own W×H scratch with no margin. `PostModifier.SetPicture`'s own doc says so (`SpriteFxModifiers.cs:1773-1776`: *"a host that drives post passes through its own path (Pyre's renderer … knows nothing about this one)"*). **So bloom, outline, chromatic aberration and drop shadow ARE clipped at Pyre's canvas edge and are not in a SpriteFx Stack — four effects, not five (see 1.3).** This is now a fact, not a ten-minute check.

---

## 4. NEW — real problems neither the report nor the evidence files found

### 4.1 Two retired modifier classes are still referenced by saved assets, and now deserialize to null

`SplashText\Assets\Pyre\Fluid Blast.asset`, `SplashText\Assets\PyrePlus\Imported\Fluid Blast Plus.asset` and `SplashText\Assets\Pyre\PyreLayerLibrary.asset` contain `type: {class: PerlinTurbulenceModifier…}` and `type: {class: AlphaMaskModifier…}` SerializeReference entries. **Neither class exists in the codebase** — `grep -rn "PerlinTurbulenceModifier\|AlphaMaskModifier" --include=*.cs` returns only comments recording their 2026-08-15 retirement (folded into Turbulence at `SpriteFxModifiers.cs:2644`, and into Wipe at `:10`/`:1279`). Those list entries are silently null modifiers in a live asset.

This matters twice over: it is a real bug of exactly the shape §11 was collecting, **and it is the live precedent for §10's Fork Blast cull** — the report says removing Fork Blast "needs a migration note rather than a straight delete"; here is what happens when that note isn't written.

### 4.2 A whole class of authored layers was excluded from the census without saying so

Each of the four projects also holds an `Editor.PyreLayerLibrary` asset (`…\Assets\Pyre\PyreLayerLibrary.asset`) containing **13 saved layers** on the **pre-rename schema** — they serialise `shape:` (the Pyre1 `LayerShape` enum), not `shapeForm:`. They are correctly outside the "123 specs" count, but they are authored Pyre content that would be affected by any generator removal, and the report never mentions them.

---

## 5. Internal consistency and arithmetic (§5 of the brief)

**Arithmetic: closes, but only by accident.** 29 = 17 live enum + 9 plug-ins + 3 field-pass ✅. Working §10's own tables: Keep = 14 entries; Fuse = 11 inputs → 4 outputs; Cull = Fork Blast; Undecided = Playback3D. 14 + 4 = 18. To reach "about 20" you must also silently keep **Ring** and **Orb (the enum)** and cut Playback3D.

**Two generators have no verdict anywhere.** §10 never mentions **Ring** or **Orb (the enum)**. Both appear in §3's Family 1 and in the §10 Rename note ("two different generators are both called Orb"), but neither is in Keep, Fuse, Cull or Decide. In a table that reads as exhaustive and is meant to drive deletions, that is a gap worth closing explicitly.

**One generator has no family.** §3's four families total 14 + 9 + 3 + 2 = **28** of 29. Playback3D is only reachable through the "two badges cut across the families" sentence (line 81). `A-generator-inventory.md:338-343` had the same structure but was explicit that Imports is *"a cross-cutting flag rather than an exclusive bucket"* — the report keeps the badge and loses the bucket, so one generator falls through.

**Family assignments are otherwise consistent** between §3, §6, §10 and the evidence files. **No generator is both kept and culled.** The one apparent tension — melt-into-a-blob is "Keep, as-is" in §10 but "dissolves into a material" in §9 — is a stated proposal, not a contradiction.

---

## 6. What the evidence files had and the report should not have dropped

1. **Geometry means two different things in the two hosts.** `B-compatibility.md:86`: in Pyre a geometry modifier is a per-sample warp *inside the shape raster* with `GeoCtx` set to the shape's own frame; in a SpriteFx Stack it is a **whole-buffer resample** with `GeoCtx` pinned to the canvas centre and `radius = min(halfW, halfH)` (`SpriteFxBurst.cs:682-683`). *"Same modifier, different semantics — an effect tuned in one will not land identically in the other."* The report has an entire section on why effects behave differently (§6) and never mentions this, even though it is a second, independent axis of exactly that.
2. **Plug-ins' size-blindness is cheap to fix.** §2's badge table and §3's Family 2 present "they cannot read the layer's size or spin at all" as a defining property. `A-generator-inventory.md:373` says the opposite is available: *"Adding `size` to [`PyreFormCtx`] is a cheap, non-breaking way to close the biggest 'ignores shared machinery' gap."* That's a dropped option, and it bears directly on whether Family 2 needs to exist as a separate contract.
3. **`PyreFormKind.PerParticle` / `.Stateful` are declared and never dispatched** (`A:364`, `A:403`) — dead API surface on the plug-in contract. A delete candidate that never made §10's Cull list.
4. **Sprite, Text and Playback3D also force the whole spec serial**, not just the sims (`PyreRenderer.cs:196`, `IsParallelSafe`). §4 attributes single-threading to simulation alone.

---

## 7. Things I did NOT verify

- Nothing was rendered. Every "silently does nothing" confirmation above is still static — I re-derived the *absence of a call site*, which is what the report itself says (§13) and is not evidence about what a frame looks like.
- I did not audit `C-shaper3d.md`'s reading of Shaper's own `GAP_ANALYSIS.md`/`PRODUCT_CONTRACT.md` beyond the schema registries and the export grep.
- I did not check whether Pyre's *editor preview* path reproduces the bake for the matte cases (`B` open question 2) — moot in practice, since no saved asset has a matte enabled (2.1).
- Dial counts (141 / 187 / 114 / 73 …) were taken on trust; `A:402` already labels its method indicative.
