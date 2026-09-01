# L — Rebuild strategy: a verdict on "clone the project and build the new app beside the old"

Architect L, T-0098. Every number below was measured on `feat/lathe` in the working tree and in git history on 2026-08-30. Paths are absolute or relative to `Assets/Packages/Laubrary/`. No source changed; Unity not opened; no tree-mutating git command run.

---

## Verdict

- **The owner's strategy is right, and he has already run it once in this project — successfully.** `23c13700` (2026-07-23) is titled *"PyrePlus slice 1: parallel Pyre rework — Shape section + live renderer"*. It created a complete second Pyre — `Runtime/PyrePlus/`, `Editor/PyrePlus/`, `Runtime/PyrePlus/Forms/Kiln/`, `Tests/PyrePlus/`, own asmdefs, own namespace `Laubrary.PyrePlus` — beside the original, which kept compiling and rendering. It ran **33 days**, carried an explicit converter (`PyreToPlusConverter.cs`, header: *"PURE + additive: ConvertFromPyre never mutates or saves the SOURCE"*), and ended at `60309923` (2026-08-25) *"Rename PyrePlus -> Pyre package-wide"* — 366 files, +76,456 / -106,076, 203 renames, 124 deletions. **The Pyre both reports diagnose IS that rebuild.** It is five weeks old.
- **But he ran it in the SAME project, not in a project clone — and that difference is the whole recommendation.** Route (iii), new assembly beside old inside one Unity project, is not a hypothetical middle path I invented; it is this codebase's proven method. **Recommend route (iii). Reject the whole-project clone.** The clone buys exactly one thing the same-project route does not (freedom to break the host), and pays for it with a second 8.8 GB `Library`, a second editor, loss of compiler and test-suite cross-checking, and a merge-back that is a GUID-collision problem instead of a rename.
- **The single highest-leverage design decision is in neither report: do not copy the Kiln ports — share them.** `Forms/Kiln/` + `Forms/Kiln/Jet/` = **10,691 lines across 25 files**, already in their own assembly (`com.Lautaro-Arino.Laubrary.Pyre.Forms.Kiln`), already behind one 362-line contract (`PyreForm.cs`). Extract that contract into a third, tiny assembly that **both** old Pyre and new Pyre2 reference. This deletes the worst failure mode of copy-don't-modify — divergence in the irreplaceable code — at a cost of one `.asmdef`.
- **Copy-and-adjust's real failure mode here is not divergence or staleness. It is the finish.** The last run left **96 stale `Plus` identifiers, 7 misnamed test files and 3 empty leftover folders** in the tree, and its merge-back was squeezed into *"bulk WIP-safeguard commit 3/15"*. That is the measured price, and it must be a named, budgeted slice with a declared finish line.
- **On the owner's point 3 he is right, and the reports contain the exact failure he fears.** "Shape → Generator throughout" and the four fusions (29 → 20 picker entries) are tidiness with no user-felt problem behind them; the Gem/Box/Pyramid/Can fusion actively contradicts the reports' own strongest finding by hiding four visible pictures behind one control's mode. Named in §5, with a five-question gate.
- **`CLAUDE.md:26` is stale and must be corrected before anything starts.** It says the rename is "staged but uncommitted (~209 staged renames)". `git diff --cached` is **empty**; `60309923` is in history. The tree does carry 129 modified + 71 untracked files, and `feat/lathe` **exists on no remote** — so the caution is right for the wrong reason, and an agent obeying the stated reason may try to reconstruct a staged state that does not exist.

---

## 1. What is actually in Pyre, sized

All counts are physical lines, measured 2026-08-30.

### 1.1 By area

| Area | Files | Lines | Kind |
|---|---:|---:|---|
| **Core renderer + layer model** — `Runtime/Pyre/*.cs` (excl. `Forms/`) | 22 | **10,376** | mixed (b) and (c) |
| **Plug-in scene forms + Kiln ports** — `Runtime/Pyre/Forms/Kiln/` | 15 | **7,557** | **(a) irreplaceable** |
| — plus the Jet subtree — `Forms/Kiln/Jet/` | 10 | **3,134** | **(a) irreplaceable** |
| **Editor window** — `Editor/Pyre/*.cs` | 10 | **6,358** | (b) rewrite wants to change |
| **Parity harness** — `Editor/Pyre/Parity/PyreParityDump.cs` | 1 | **297** | **(c) reusable as-is** |
| **Tests** — `Assets/Tests/Pyre/` | 16 | **3,273** (~165 `[Test]`) | **(c) reusable as-is** |
| **Cross-tool bridges** — `Runtime/ZoetropePyre/`, `Runtime/Chunks/PyreSpawn/`, `Runtime/Chunks/Motion/`, `Editor/Chunks/` | 7 | **1,387** | (c) |
| **Total, every file with "Pyre" in its path** | **81** | **32,382** | |

Largest single files: `PyreRenderer.cs` 5,192 · `PyreWindow.cs` 2,932 · `Pyre.cs` 1,263 · `PyreArcBurst.cs` 1,116 · `ExplosiveJetProgram.cs` 1,006 · `PyreInferno.cs` 915 · `PyreJetEngine.cs` 796 · `PyreWindow.Preview.cs` 718 · `PyreTorch.cs` 669 · `PyreShade.cs` 663 · `PyreOrb.cs` 656 · `OrbForm.cs` 638 · `ArcBurstForm.cs` 623 · `PyreGif.cs` 612 · `PyrePlasmaBloom.cs` 614.

The SpriteFx modifier library is a **separate tool**, not part of Pyre: `Runtime/SpriteFx/` = 21 files, of which `SpriteFxModifiers.cs` alone is **3,592** lines, `SpriteFxBurst.cs` 765, `SpriteFxFilter.cs` 599, `SpriteFxRelight.cs` 575; `Editor/SpriteFx/` adds `SpriteFxStackWindow.cs` 1,057 and `SpriteFxStackView.cs` 385. Pyre references it (`Pyre.asmdef` -> `com.Lautaro-Arino.Laubrary.SpriteFx`); it does not reference Pyre. **It is outside the scope of any Pyre rebuild and must not be copied.**

The baking / runtime playback path is small and cleanly separable: `Editor/Pyre/PyreBaker.cs` 137, `Editor/Pyre/PyreGif.cs` 612, `Runtime/Pyre/PyreBlastPlayer.cs` 97, `PyreBlastPool.cs` 34, `PyreChunkAnimation.cs` 54, `PyreChunksDirect.cs` 195. **1,129 lines** for everything between "a spec" and "a sprite sheet playing in a scene".

### 1.2 Inside the 5,192-line `PyreRenderer.cs`

179 methods, 4,833 lines inside method bodies, bucketed by method name:

| Bucket | Lines | Verdict |
|---|---:|---|
| Built-in shape drawing (`DrawOrb` 160, `DrawRing` 154, `DrawStreakBody`, `DrawStarBody`, `DrawFacetSolid`, `Rot` 183+84) | **1,165** | **(a)** tuned constants and operation order — the *content* |
| Swarm / particles (`ComputeSpawns` x4 = 149+139+91+56, `PlaceParticle` 111, `GridLayout*` 122) | **918** | **(a)** tuned |
| Compositing / blend / buffers (`Over` 85, supersample, clear, resize) | **445** | **(c)** reusable |
| Matte / grouping / height (`RenderPlusRampField` 135, `RenderPlusFusedField` 52) | **433** | **(b)** the model a rewrite exists to change |
| Text (`RenderTextLine` 52, `SampleTextColor` 50, `EnsureTextGlyphs` 39) | **310** | (a) |
| Fill / colour / shade | **279** | **(c)** but coupled to `ZuiFill` |
| Fire / fireball sim | **268** | (a) |
| **Border** (`BuildBorderBuffer` 72 + gate) | **115** | **(c)** built and working; gated to 6 of 29 generators |
| Dispatch / orchestration (`RenderLayer` and friends) | **89** | **(b)** — the four-mechanism priority order at `PyreRenderer.cs:241` |
| Unclassified (51 methods) | 811 | mixed |

**Read that table as the answer to "what is irreplaceable".** ~2,650 lines of `PyreRenderer.cs` are *drawing content* — shape maths, particle placement, flame sim. The structure a rewrite exists to change is the **89 lines of dispatch plus the 433 of matte/grouping**, plus most of the 6,358-line editor. **The tuned content is roughly 30x the size of the structure that is wrong.** Any plan whose method is "copy it all and adjust" will spend 95% of its effort transcribing things that were never the problem.

`PyreWindow.cs` measures 202 methods / 2,708 in-method lines, of which **2,065 fall outside every feature bucket** — it is overwhelmingly layout plumbing, which is why it is the cheapest large thing to rebuild rather than port.

### 1.3 The three categories, stated

**(a) Irreplaceable tuned content — do not rewrite, do not copy; share it or carry it across untouched.**
- `Forms/Kiln/` + `Jet/` = **10,691 lines / 25 files**. `ExplosiveJetProgram.cs:14-17` cites `D:/CODEZ/Kiln/projects/Flame/agents/agent3_fork_explosive/flame3/jet.py` and a `diff -r` result. **That external source is live on this machine** — `D:/CODEZ/Kiln/` exists, with `parity_compare.py`, `docs/contract_schema.md`, and 13 project directories including `Flame`, `Energy Explosion`, `Energy Projectile`. Fidelity is therefore *re-verifiable*, which is better than the reports assumed; it is not *cheap*, which is what matters.
- The ~2,650 lines of drawing/particle/flame content inside `PyreRenderer.cs`.
- `PyreClipStats.cs:3` ("NEVER NORMALISE PER FRAME", from a documented post-mortem); `PyreNumpyRng.cs` (230) and `PyrePyRandom.cs` (146), which exist to be bit-compatible with Python's RNG.

**(b) Structure a rewrite would deliberately change.**
- `RenderLayer`'s four unrelated dispatch mechanisms (`PyreRenderer.cs:241`) — 89 lines.
- The matte/grouping/height model — 433 renderer lines plus `Pyre.cs`'s `MatteRole`/`MatteCombine`/`heightFromChannel` and ~137 lines of `PyreWindow.cs`.
- The border gate: `PyreRenderer.cs:950-952` (six-form allow-list), `PyreRenderer.Layers.cs:92` (second condition `layer.form == null`), `PyreWindow.cs:1371` (control not constructed at all).
- `PyreWindow.cs` in bulk.

**(c) Genuinely reusable as-is.**
- `Assets/Tests/Pyre/` — 3,273 lines, ~165 tests, of which ~104 guard the Kiln ports directly (Torch 15, ExplosiveJet 19, Jet 16, RadialJet 16, Orb 11, PlasmaBloom 13, ArcBurst 13). **This is the safety net that makes any of this possible; it must be pointed at the new code from day one.**
- `PyreParityDump.cs` (297) + `D:/CODEZ/Kiln/parity_compare.py` — the external-fidelity oracle.
- `PyreShade.cs` 663, `PyreFieldOps.cs` 484, `PyreField.cs` 248, `PyreSupersample.cs` 97, `PyrePrepassCache.cs` 71, `PyreLayerCache.cs` 117 — shared capability code with no model opinion.
- The baking/playback path, 1,129 lines.

---

## 2. Does the strategy survive contact with Unity?

### 2.1 The precedent, in full

This is the most important evidence in the document, so it is stated with citations.

| Date | Commit | What |
|---|---|---|
| 2026-07-23 | `23c13700` | *"PyrePlus slice 1: **parallel Pyre rework** — Shape section + live renderer"*. New asmdefs, namespace `Laubrary.PyrePlus`, beside a still-working `Laubrary.Pyre`. **A live renderer on commit one.** |
| 07-28 -> 08-23 | ~40 commits | PyrePlus-only development: shape picker, Crescent 2D pad, Playback3D POC, `PlusForm` plug-in model + `Forms.Kiln` (`8a7c8470`, message notes *"legacy intact"*), Plasma Bloom / Torch / Orb / Arc Burst ports with *"exact-RNG parity on all 10 draws"*, the parity harness (`5db07a38`), baking (`a358d7cf`). |
| (during) | `fe588f81` | *"batch-import 28 vanilla Pyre assets to `Assets/PyrePlus/Imported/` (**converter output, sources untouched**)"* — `PyreToPlusConverter.cs`, whose header states the conversion is pure and reports every dropped or approximated field per layer through a `warnings` list. **This is precisely the owner's "duplicate and adjust, never modify", already built and already used once.** |
| 2026-08-25 | `60309923` | *"Rename PyrePlus -> Pyre package-wide"* — 366 files, **+76,456 / -106,076**, 203 R / 124 D / 18 A. The old Pyre (`BlastRenderer.cs` 167 KB, `Layer.cs` 77 KB, `PyreWindow.Layers.cs` 71 KB, `FireSim.cs`, `FireballSim.cs`, `PyreEnums.cs`, `BlastBaker.cs`, `PyreLayerLibrary.cs`) **deleted outright**. |

**Old Pyre received zero feature commits during the 33-day parallel window.** Every commit touching `Runtime/Pyre` or `Editor/Pyre` between 2026-07-23 and 2026-08-25 was a PyrePlus commit, a cross-cutting sync, or the rename itself. **The divergence failure mode did not fire** — empirically, in this repo, with this owner. That is the strongest single piece of evidence in favour of his proposal, and it deserves to be stated before any objection.

### 2.2 Two Unity projects on one machine

The standing rule in `C:\Users\Lauta\.claude\projects\D--UNITY-Laubrary-Dev\memory\unity-scheduler-tick-wedge.md` is *"do not run two agents against one Unity editor"* — the 2026-07-23 wedge (20+ min on `Scheduler.tick`, kill and restart required), with a milder recurrence on 2026-08-23 (T-0069) in which two AgentHQ sessions edited `SpriteFxStackWindow.cs` simultaneously and *"each agent was told it was the only one on the editor and neither could tell otherwise"*.

A project clone does **not** violate that rule — it is two editors, one agent each. But it is worse in a way the rule does not cover:

- **Disk and time.** `Assets` 693 MB, `Library` **8.8 GB**, `.git` 159 MB; Unity `6000.3.10f1`; 108 assembly definitions. A clone copies ~850 MB and must then *regenerate* an 8.8 GB Library by full reimport. Hours, once, and again after every large sync back from the host.
- **The Coplay bridge is per-session and stateful.** `CLAUDE.md:11` requires `set_unity_project_root` before any tool call. With two Pyre-bearing projects open, an agent that mis-points the bridge edits the wrong tree and both editors recompile. Nothing guards this, and the wedge memory's own lesson is that misattribution after the fact is expensive.
- **CPU.** Two editors share one machine's compile pipeline. The wedge's actual diagnosis was main-thread saturation.

**Net: the clone does not break the rule; it multiplies the class of accident the rule exists for.**

### 2.3 Can the new app live in the SAME project? Yes — and it already did

- **Assembly definitions.** `Assets/Packages/Laubrary` is a plain folder in `Assets/` — it is **not** in `Packages/manifest.json`. Compilation is driven purely by the 108 `.asmdef` files. Adding `com.Lautaro-Arino.Laubrary.Pyre2` + `.Pyre2.Editor` beside `.Pyre` + `.Pyre.Editor` is one file each; assembly names are unique strings and there is no collision.
- **Namespaces.** `Laubrary.Pyre2` alongside `Laubrary.Pyre`. One real gotcha the last run hit and wrote down for us: `PyreToPlusConverter.cs`'s header warns that the `Laubrary.Pyre` *namespace* shadows the `Pyre` *class*, so a converter needs `using PyreAsset = Laubrary.Pyre.Pyre;`, and that a type existing in both namespaces resolves to the **enclosing** one, not the `using`-imported one. **That comment is a free specification for the next converter.**
- **Blast radius is tiny.** Exactly **7** asmdefs reference `Laubrary.Pyre`: `Pyre`, `Pyre.Forms.Kiln`, `Pyre.Editor`, `Zoetrope.Pyre`, `Mirage`, `Laubrary.Demos`, `Laubrary.Pyre.Tests`. An eighth assembly disturbs none of them.
- **GUIDs.** 66 `.meta` files under `Runtime/Pyre` + `Editor/Pyre`. Copying a folder *within* one project makes Unity mint fresh GUIDs — correct and automatic. Copying a folder *between two clones of the same project* carries the **original** GUIDs, so merging a clone's `Pyre2/` back into the host collides with the host's still-present `Pyre/` metas. **This is the clone route's worst mechanical property and it has no clean fix** — you either hand-regenerate 66+ GUIDs or you delete the host's Pyre first, which defeats the point of keeping the reference.

**What the same-project route buys that a clone structurally cannot:**
1. **The C# compiler type-checks both implementations at once.** Every shared-contract change is caught the moment it is made, not at merge.
2. **One test run covers both.** `Laubrary.Pyre.Tests` already references `Pyre`, `Pyre.Editor` and `Pyre.Forms.Kiln`; adding `Pyre2` lets a single fixture assert the new renderer against the old, frame by frame. In a clone the old renderer is in another process.
3. **Both windows on screen side by side, same editor, same spec.** The fastest possible feedback loop, and unavailable in a clone.
4. **The merge-back is a rename, which this project has already done and priced.** In a clone it is a folder copy with GUID collisions *plus* manual reconciliation of however many weeks of drift in ZUI, SpriteFx, Chunks and AssetKit — all of which Pyre depends on and all of which are under active development right now (129 modified, 71 untracked, spanning Lathe/Tapestry/Chunks/ZUI).

**What the clone buys:** freedom to break the host project without consequence. Since the same-project route leaves the old Pyre compiling, shipping and untouched, that freedom is worth very little.

### 2.4 `[MovedFrom]` and `SerializeReference` under duplication

`SerializeReference` in Pyre: `Pyre.cs` (13 sites), `PyreLayerKey.cs` (3), `PyreForm.cs` (2), `ArcBurstForm.cs` (1). `[MovedFrom]` appears **11 times**, all in Pyre, all of the form

```
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.PyrePlus.Forms.Kiln",
                                             "com.Lautaro-Arino.Laubrary.PyrePlus.Forms.Kiln", null)]
```

on the ten Kiln form classes plus `PyreForm` itself (`PyreForm.cs:181`). **This is exactly what made `60309923` land**: it let assets authored against `Laubrary.PyrePlus.Forms.Kiln.OrbForm` deserialize as `Laubrary.Pyre.Forms.Kiln.OrbForm`.

The rule that follows is not obvious and matters:

> **`[MovedFrom]` handles a MOVE. It cannot handle a DUPLICATE.** It rewrites one old type identity to one new one. If `Laubrary.Pyre.OrbForm` and `Laubrary.Pyre2.OrbForm` both exist and both claim the same predecessor, the claim is ambiguous. You cannot annotate the new copy `[MovedFrom("Laubrary.Pyre")]` while `Laubrary.Pyre` is still alive.

Therefore, under **either** duplication route:
- During the parallel phase, **use a converter, not `[MovedFrom]`**. Old assets stay old-typed, new assets are new-typed, and a `Pyre -> Pyre2` converter modelled on `PyreToPlusConverter.cs` bridges them. That is what the last run did, and its per-layer `warnings` list is the right pattern: it surfaces what was dropped rather than silently guessing.
- **`[MovedFrom]` is added only at the final rename**, on the types whose fully-qualified name actually changes. Budget ~11 attributes.
- **If the Kiln forms are shared rather than copied (§4), their `SerializeReference` identity never changes at all and they need no `[MovedFrom]`, ever.** A second, independent reason to share them.

### 2.5 ZUI

**The premise in the brief is incorrect, and the correction is load-bearing.** `Assets/ZUI/` contains exactly two files: `ZUIEnvelopePresets.asset` and its `.meta`. **ZUI's code lives inside the package**, at `Assets/Packages/Laubrary/Zui/`, in three assemblies: `ZUI.Editor` (`Zui/Scripts/Editor/`), `com.Lautaro-Arino.Laubrary.ZuiRuntime` (`Zui/Scripts/Runtime/`, home of `ZuiFill.cs`, 664 lines) and `com.Lautaro-Arino.Laubrary.Zui.Editor` (`Zui/Toolkit/`, home of `ZuiFillControl.cs`, 837 lines).

Consequences: ZUI **does not complicate a clone** (it travels with the package folder) and **does not complicate the same-project route** either (both Pyre and Pyre2 reference the same two assemblies). What it does complicate is report 2's slice 4 — `ZuiFill` is a shared runtime type consumed by other tools' *source*, so making fills pluggable is a package-wide API change regardless of route. Unchanged by this decision, and correctly identified by report 2 §3.

### 2.6 Git hazards, flagged

1. **`CLAUDE.md:26` is factually wrong today.** It states *"As of 2026-08-24 the whole rename is staged but uncommitted on `feat/lathe` (~209 staged renames plus deletions of the old files)"*. `git diff --cached --name-status` returns **zero lines**; the rename is commit `60309923`, dated 2026-08-25. An agent following that line will refuse safe git operations, and may attempt to reconstruct a staged state that does not exist. **Correct it before this work starts.**
2. **The caution is still warranted, for a different reason.** The tree carries **129 modified + 71 untracked** files across Lathe, Tapestry, Chunks, ZUI and demos. Branching for new work is safe; `git checkout` / `restore` / `stash` are not.
3. **`feat/lathe` exists on no remote.** `git branch -r --contains feat/lathe` is empty; origin carries only `master`, `release`, `SmartStats`, `GamaSave-system`, `fix-demo-scenes-for-packages`. **The entire current Pyre — the whole PyrePlus rebuild — exists only on this disk.** Push `feat/lathe` before starting a second large rework. Unrelated to the question, and the single largest risk in the repository.
4. **The last merge-back was buried.** `60309923` is *"bulk WIP-safeguard commit 3/15"* — a 366-file package-wide rename executed inside a housekeeping batch. That is how the residue in §3 survived review. Do not repeat it.

---

## 3. The honest failure modes of copy-don't-modify

Each is named, evidenced from this codebase where possible, and rated.

### 3.1 Divergence — two implementations alive, bugs fixed in one

**Did not fire last time.** Zero feature commits landed on old Pyre during the 33-day parallel window (§2.1). **Manageable, and there is a structural fix that makes it near-impossible:** freeze the old assembly by convention (one line in `CLAUDE.md`) *and* by construction — put the 10,691 lines of Kiln ports behind a shared contract assembly so that the code most damaged by a divergent bug-fix physically cannot be duplicated. Residual risk is confined to the ~10,376-line core, where a duplicate fix is cheap and detectable by the shared test suite.

**Rating: manageable — low risk, cheap mitigation.**

### 3.2 The reference copy going stale

Real but harmless *if the window is short*. Pyre depends on ZUI, SpriteFx, Chunks, AssetKit and BackSplash, all under active development. Over 33 days the last run absorbed several cross-cutting syncs (`ff6dbe5b` OutBurner sync, `0b20ba9a` new ZUI controls, `2b8c619c` Reel->Lauminary rename) without incident, because **both copies were in one project and both had to compile against the same ZUI**. In a clone, the reference copy freezes against a ZUI snapshot and the two diverge silently until merge.

**Rating: manageable in the same project; a genuine cost of the clone. This is the second-strongest argument against route (ii).**

### 3.3 The "adjust" step silently reproducing the structure the exercise exists to escape

**This is the biggest risk and it demonstrably fired last time.** The four-mechanism dispatch at `PyreRenderer.cs:241`, the border constructed only for six generators (`PyreWindow.cs:1371`), grouping as an integer matched across two layers inside a panel named "Matte", `coalesce` offered where it is never read — **none of these are legacies of the pre-July Pyre. They are properties of PyrePlus, the rebuild.** The rebuild was five weeks ago and it produced the codebase both reports now diagnose as tangled.

That is the single most important sentence in this document. Copying code carries its assumptions; a rebuild whose method is "use the old code as a mold" will re-mint the old model unless something external forbids it.

**Mitigation, and it is the only one that works:** the design document must state the target model **as prose and as a contract, before any file is copied**, and the copy must be judged against the contract rather than against the original. Concretely, for this project the contract is: *one Generator field, one dispatch path, every generator declares its capabilities, every capability that exists has exactly one named control that is always constructed and greyed out with a reason when it cannot act.* Any copied routine that cannot be expressed through that contract is a finding, not a port.

**Rating: high risk, and it is the reason the design document is not optional. Mitigable only by writing the contract first and reviewing against it.**

### 3.4 Incomplete ports where the old one is silently still used

**Fired, mildly, and the evidence is still in the tree.** After `60309923`:
- **96 occurrences of `Plus` identifiers survive in shipped Pyre code** — 73 in `Runtime/Pyre` (17 of 47 files), 19 in `Editor/Pyre` (4 of 11), 4 in `Tests/Pyre`. They include **public API surface**: `IPlusFieldPublisher` (`PyreForm.cs:156`) and `IPlusRampProbe` (`PyreForm.cs:163`), both of which `PyreParityDump.cs` depends on by name; and renderer entry points `RenderPlusRampField`, `RenderPlusFusedField`, `RenderPlusFireLayer`, `StepPlusFire`.
- **7 test files are still named `Plus*Tests.cs`**: `PlusCapabilityTests`, `PlusFieldOpsTests`, `PlusFormWarpTests`, `PlusFrameFillTests`, `PlusLayerCacheTests`, `PlusParityDumpTests`, `PlusShadeTests`.
- **3 empty leftover folders**: `Assets/PyrePlus/`, `Runtime/PyrePlus/`, `Editor/PyrePlus/` — `CLAUDE.md:22` acknowledges them.
- The external Kiln repo still carries `D:/CODEZ/Kiln/docs/pyreplus_briefing.md`, so the residue crosses a repository boundary.

The *dangerous* variant — the old implementation still being called — **did not** occur, because the old one was deleted outright rather than kept as a shim (`CLAUDE.md:22` records this deliberately). **Deleting rather than shimming is what prevented it, and it should be repeated.**

**Rating: manageable, but only with an explicit residue sweep as a named deliverable. The evidence is that without one it does not happen.**

### 3.5 The cost of finishing — when is the old one deleted, and who decides

**This is where the last run was weakest, and the failure is documented in the commit itself.** `60309923` is *"bulk WIP-safeguard commit 3/15"*: a 366-file, ±180,000-line package-wide rename performed inside a fifteen-commit housekeeping batch alongside "Project settings + gitignore tweaks" and "AgentHQ bookkeeping snapshot". A change of that size gets no review inside a batch like that, which is exactly why §3.4's residue survived.

There is also a *psychological* cost the owner should price: the memory file `todo-retire-pyre-for-pyreplus.md` is dated 2026-08-24 — the retirement was still a TODO the day before it happened. The parallel state persisted longer than intended, and the decision to end it was made under time pressure.

**Mitigation:**
- **The finish criterion is written into the design document on day one, as a checklist, not a feeling.** Proposed: (1) every Kiln parity test passes against the new renderer via `PyreParityDump.Compare`; (2) the ~165-test suite is green pointed at the new assembly; (3) every generator in the old picker is reachable in the new one or has a written verdict saying it was cut and why; (4) the converter runs on a sample of specs with an empty `warnings` list, or with every warning individually accepted; (5) the owner has rendered and eyeballed one frame per generator.
- **The rename-over is its own commit, on its own day, reviewed as a diff.** Not inside a batch.
- **Who decides: the owner, against that checklist.** The agent's job is to report the checklist state, not to declare completion.

**Rating: manageable, and the single most under-planned part of the last run. Budget it as a slice.**

---

## 4. Three routes, compared squarely

Common ground first, because it changes the comparison: **routes (i) and (iii) are not alternatives.** Report 2's slices 1-3 are *bug fixes and interface fixes in the existing model*; slices 4-7 are *model changes*. The right plan does the first three in place, in Pyre, this week, and the last four in a new assembly. Presenting them as competing routes is a false choice that both reports and the owner's proposal inherit.

### (i) Report 2's additive in-place route — seven slices, one codebase

- **Cost.** Lowest of the three. Slice 1 (ungate the border) is literally three edits: the six-form allow-list at `PyreRenderer.cs:950-952`, the second condition `layer.form == null` at `PyreRenderer.Layers.cs:92`, and `if (IsFlat2DBorderForm(s.shapeForm)) BuildBorderBox(s);` at `PyreWindow.cs:1371`. Slice 2 is interface-only. Slice 3 is a correctness fix to one over-loaded value.
- **Risk.** Rises sharply at slices 4-6. Making fills pluggable changes `ZuiFill`, which other tools' source consumes. Slice 6 means retrofitting an accumulation plane that structurally harvests only opacity into one that must carry height, heat and normal — building the new model *inside* the old one's constraints. That is exactly failure mode 3.3, with no firewall.
- **Makes easy.** Everything shipping continuously; no merge; no residue; no second window; no converter.
- **Makes hard.** Any change to the four-mechanism dispatch, because every existing generator depends on it simultaneously. There is no point at which you can have the new dispatch and the old one both correct.
- **Time to see something: days.** Slice 1 is visible in the existing window on the existing preview the same afternoon. **This is the route's decisive advantage and it should not be given up.**

### (ii) The owner's clone-and-rebuild-beside, in a whole-project clone

Strongest version of his own case, stated fairly: *nothing in the working project can break; the reference implementation is permanently and completely available including its assets, scenes and demos; the agent cannot be tempted to "just fix" the old one; and the whole thing can be abandoned by deleting a directory.* Those are real and they are the reasons the strategy is fundamentally sound.

- **Cost.** ~850 MB copied, an 8.8 GB Library regenerated by full reimport, a second editor, and a merge-back that is a GUID-collision problem (§2.3) rather than a rename. Plus the reference copy freezing against a moving ZUI/SpriteFx/Chunks (§3.2).
- **Risk.** The compiler and the test suite never see both implementations. Drift is discovered at merge, which is the worst possible time. The Coplay-bridge mis-pointing hazard (§2.2) is new and unguarded.
- **Makes easy.** Radical restructuring without fear; deleting the experiment.
- **Makes hard.** Comparing outputs (two processes); running one test suite over both; the merge; keeping in step with five dependencies under active development.
- **Time to see something: weeks, with no partial credit.** Nothing renders until the clone opens, the Library rebuilds, the new assembly compiles and one generator draws. There is no equivalent of "three edits this afternoon".

### (iii) New code beside old, inside the same project — new assembly + namespace, old Pyre still compiling and runnable, switch at the layer level

- **Cost.** Two `.asmdef` files, one namespace, one converter (a rewrite of a file that already existed and whose header documents its own pitfalls), and one rename at the end whose price is measured: 366 files, 203 renames, ~11 `[MovedFrom]` attributes, plus a residue sweep of ~96 identifiers.
- **Risk.** 3.3 — reproducing the old model — is the same as in (ii) and is mitigated the same way (contract first). Everything else is lower: divergence is caught by the compiler, staleness cannot happen, the merge is a rename this project has done.
- **Makes easy.** Side-by-side comparison in one editor on one spec. One test suite over both. Sharing the 10,691 irreplaceable lines instead of copying them. Shipping the old tool continuously throughout.
- **Makes hard.** Two Pyres visible in the same menu for the duration — a genuine confusion cost the last run paid, and the reason `CLAUDE.md:20-24` now shouts *"the rename is DONE, there is no PyrePlus"*. Mitigate by naming the new window unmistakably and putting a dated end-condition in `CLAUDE.md` from day one.
- **Time to see something: about a week, and this is measured, not estimated.** `23c13700`, the *first* PyrePlus commit, was "Shape section + **live renderer**". The owner saw a working parallel renderer on day one of the last run.

### Recommendation

**Route (iii), unambiguously — with three amendments that make it strictly better than the last run.**

1. **Do slices 1, 2 and 3 in place, in the existing Pyre, first.** They are bug and interface fixes, they cost days, they are visible immediately, and — critically — **they are how you find out whether the diagnosis is even right** before spending a month on a new model. Ungating the border exercises the real border implementation against 23 generators that have never fed it; if that goes badly, the whole shape/border seam premise needs revisiting and you will have learned it for a week's work instead of a quarter's.
2. **Extract `PyreForm` (362 lines) into its own tiny assembly, referenced by old Pyre, new Pyre2 and `Pyre.Forms.Kiln` alike.** The 10,691 lines of bit-exact ports are then **shared, never copied, never diverged, and never need a `[MovedFrom]`**. This converts report 2's strongest surviving anti-rewrite argument from an objection into a non-issue. It is one file and it is the highest-leverage decision available.
3. **Write the finish checklist and the residue sweep into the plan on day one** (§3.5), and do the rename-over as its own reviewed commit.

**What would change my mind:**
- **If Unity could not host two Pyre assemblies simultaneously** — e.g. if `PyreForm`'s `SerializeReference` graph or the `[CreateAssetMenu]`/window registration collided irreconcilably. It can; this was done for 33 days. But if a *specific* collision surfaces in week one that has no clean workaround, the clone becomes the fallback and I would switch.
- **If the target model turns out to require changing `Pyre.Forms.Kiln` itself** — i.e. if the new contract cannot be expressed through `PyreForm` without editing the nine scene forms. Then the ports are no longer shareable, the "carry them across untouched" plan collapses, and the calculus changes materially. **Test this in week one by writing the new `PyreForm2` contract and checking that `OrbForm` (638 lines) and `ExplosiveJetForm` satisfy it unmodified.** This is the single riskiest unknown in the plan and it should be resolved before anything else is built.
- **If the owner intends to work on Pyre2 from a different machine or wants a second person on it.** Then physical separation earns its cost and route (ii) is right.
- **Not a reason to switch:** "the old code will tempt an agent to edit it". That is a `CLAUDE.md` line and a frozen-assembly convention, not an 8.8 GB Library.

---

## 5. The owner's point 3, answered directly

> *"So i am worried that making u-turns, deprecating things etc... could create pattern that you interpret as a goal in itself."*

**He is right, the reports contain the failure, and here it is by name.** I have applied one test throughout: *what would a user have been trying to do, and been unable to do, that this fixes?* If the answer is "nothing — it would read better", it is pattern-chasing.

### 5.1 Pattern-chasing — tidiness with no user-felt problem behind it

| Recommendation | Where | Why it is tidiness |
|---|---|---|
| **"Shape → Generator throughout"** | R1 §10, and it is R1's headline rename | A global rename of one word across 32,382 lines, plus assets, docs, memory files and the external Kiln repo. **No user is blocked by the word "Shape".** The real defect R1 identified is that four unrelated mechanisms decide what a layer draws and only one is in the picker — and that is fixed by *unifying the dispatch and the picker*, which the rename does not do and does not help do. **This is the clearest pattern-chase in either document**, and the project has just paid the bill for one package-wide rename: 366 files, 203 renames, and residue still in the tree five days later (§3.4). Do the dispatch unification; call the field whatever you like. |
| **The four fusions, sold as "29 → 20"** | R1 §10 | The stated benefit is a smaller number of picker entries. **A fusion is not free**: four buttons become one button plus a mode, which is one more decision and one more hidden state. The Gem/Box/Pyramid/Can case is the worst — they share one routine (`DrawFacetSolid`, `PyreRenderer.cs:4152`, 50 lines) but they are **four different pictures**. Collapsing four visible pictures behind one control's mode is precisely the structural pathology K §4 identifies as what makes Pyre's features unfindable. **The consolidation recommendation contradicts the reports' own strongest finding.** Same objection, weaker, for Star+Polygon. (The three Jets and the two height techniques are different: those are genuinely one thing wearing three/two hats, and fusing them removes a *lie*, not a picture.) |
| **"Guard the word 'layer'"** | R1 §10 | Pure vocabulary hygiene. Worth one paragraph in a glossary; not a change. |
| **"'Fuse' means three things"** | R1 §7, §10 | Renaming three unrelated things because a word repeats. No user is blocked by the repetition; a user *is* blocked by "Fuse" not saying what it does — which is a **label** fix on one control, not a three-way rename project. |
| **"The two empty leftover folders"** | R1 §10 | `Assets/PyrePlus/`, `Runtime/PyrePlus/`, `Editor/PyrePlus/` are genuinely empty. Deleting them costs nothing and buys nothing. Fine to do; not a deliverable, and not evidence of anything. |
| **"Two declared-but-never-reachable generator kinds… makes the contract look more capable than it is"** | R1 §10 | Real, but it is a **doc-comment** fix on `PyreFormKind`, not a deletion project. Ranking it beside reachability defects inflates it. |

### 5.2 Genuinely load-bearing — a user would feel every one of these

| Recommendation | Evidence | The user-felt problem |
|---|---|---|
| **Ungate the border** | `PyreRenderer.cs:950-952`; `PyreRenderer.Layers.cs:92`; `PyreWindow.cs:1371` | A working, built feature (115 renderer lines) that **23 of 29 generators cannot reach, with the control not even constructed** — no greyed row, no tooltip, no trace. A user authoring a Torch has no path from the interface to the knowledge that borders exist. |
| **Split the effect list into four** | R2 §7; `E-spritefx-triage.md` | "I added an effect and nothing happened." The single most common failure a user can hit. |
| **Fix the five meanings of the per-pixel value** | `E-spritefx-triage.md:143` | One of the five is a hardcoded zero that silently kills an effect in one host. A correctness bug, not a tidy-up. |
| **Cull Pin Warp** | `SpriteFxModifiers.cs:3350` — `SetFrame` has exactly one repo-wide hit, its own declaration; its doc names `BlastRenderer`, deleted in `60309923` | Keyframe animation is the entire point of the effect and it cannot run. Keeping it means one day rebuilding the most elaborate authoring surface in the set for something that still would not animate. |
| **`coalesce` offered where it cannot act** | `PyreRenderer.cs:1497-1498` (read only inside `RenderSwarm`); UI gated only on `swarmEnabled` at `PyreWindow.cs:2492-2495` | The interface lies to the user on roughly half the generator list. |
| **Delete Fork Blast** | `ExplosiveJetForm.cs:16-21` — the source itself says it *replaces* `ForkBlastForm` and closes a 47-row divergence table row by row | The author's own written supersession. The cleanest evidence class in the whole task, and it applies to exactly one item. |
| **Promote the three hidden Mass generators into the picker** | Reachability | Capabilities that exist and cannot be found by browsing. |
| **Rename the two masks** to "Cut-out mask (solidity)" / "Light mask (brightness)" | R1 §5 | The one rename that *is* load-bearing: the two controls read different physical quantities, share one box and one master switch, and the name is the only thing that could distinguish them. Note this is R1 §5's **only** surviving recommendation once the usage evidence is withdrawn (K §2a). |

**The asymmetry is the point.** Every load-bearing item is a *reachability* or *correctness* defect — something a user hits. Every pattern-chase is a *naming* or *counting* item — something a reader notices. That is the discriminator, and it generalises.

### 5.3 The test any future change to Pyre must pass before removing or renaming anything

1. **Name the user and the moment.** Who was blocked, and what were they trying to do when they hit this? If the only answer is "a future reader of the code", it is a comment, not a change.
2. **Would the old name still be true?** If the thing does what its name says, and the complaint is that the word is also used elsewhere, do not rename it — disambiguate at the point of use. Renaming to relieve a collision costs a package-wide sweep and buys a reader's convenience.
3. **Does it remove or hide a picture?** If a user can currently produce a rendered frame they could not produce afterwards — **or could only produce after one extra decision** — this is a capability change, not a cleanup, and must be costed and argued as one. Fusion is subject to this test.
4. **What is the residue budget, and who declares it paid?** Name the finish line before starting. Prior art, measured: `60309923` moved 366 files and left 96 stale identifiers, 7 misnamed test files and 3 empty folders in the tree. A change without a named finisher does not finish.
5. **Is the evidence a fact about the code, or a fact about taste?** Admissible: reachability (is the control constructed?), dead hooks with no caller, hardcoded values that neutralise a branch, and the author's own written supersession. **Inadmissible: "nothing uses it", "it looks messy", "it doesn't fit the pattern", and "this would be fewer entries".**

A change that cannot answer 1 and 5 does not get made. A change that trips 3 gets argued on its merits as a feature decision, by the owner, not shipped as housekeeping.

---

## 6. What the design document must contain

One page. It exists to prevent failure mode 3.3 — the copy silently re-minting the old model — and nothing else it does matters as much.

**A. The one-sentence model.** What a layer *is*, in the new world, written before any code. If it cannot be said in one sentence it is not ready. (Working draft from the two reports: *a layer is one Generator producing a substance, plus operations on that substance, plus one or more fills consuming it.*)

**B. Decisions that must be LOCKED before a line is written.**
1. **The `PyreForm` contract** — is it unchanged? Verify by checking `OrbForm` (638 lines) and `ExplosiveJetForm` satisfy the new contract **unmodified**. This gates everything: if yes, the 10,691 Kiln lines are shared and untouched; if no, the plan changes shape. **Resolve first.**
2. **One dispatch path.** The four mechanisms at `PyreRenderer.cs:241` become one, and the document says which one and what happens to the other three.
3. **The capability contract.** Every generator declares what it produces and consumes; every control is **always constructed** and greyed with a written reason when it cannot act. This is the structural rule that makes the interface findable, and it must be a contract, not an intention.
4. **Assembly, namespace, window name, and the dated end-condition** for the parallel period, written into `CLAUDE.md` on day one.
5. **What is deliberately NOT carried across** — a written list, with a reason per item. Fork Blast, 3D Playback, Pin Warp, the aux-map gate, the dead outline effect. Silence here is how things get carried by accident.

**C. What must be MEASURED before it is designed.**
1. Whether the border stage actually works on the 23 generators that have never fed it (do slice 1 in place, first — §4).
2. Which generators refuse whole-buffer treatment (report 2 §12 slice 5 flags at least one; find out which, by test).
3. A rendered frame per generator, from today's Pyre, as the visual baseline. **Both reports end by saying nothing was checked by looking at Pyre running. With usage evidence withdrawn by ruling, direct observation is the only remaining empirical evidence about what is worth keeping.**
4. The parity baseline: run `PyreParityDump.Compare` against `D:/CODEZ/Kiln` today and record the current pass state, so a later regression is attributable.

**D. Deliberately LEFT OPEN.**
- Which fills become pluggable and in what order (report 2 slice 4 is a package-wide API change; it can follow).
- Extrusion and the warp-target selector (genuinely new; more valuable once everything can receive them).
- The Shaper relationship — it is arriving as the real 3D story and should not be designed around prematurely.
- The final picker taxonomy. Fusion decisions (§5.1) are feature decisions the owner makes by looking at pictures, not architecture decisions to lock up front.

**E. The finish checklist**, copied verbatim from §3.5, with a named owner for each line.

**F. The residue sweep**, as a numbered deliverable with a target: zero `Plus`-era identifiers, zero misnamed test files, zero empty folders, `CLAUDE.md` and `D:/CODEZ/Kiln/docs/` updated.

---

*Method: line counts, method-level bucketing, asmdef graph, GUID/meta counts and residue counts computed over the working tree on 2026-08-30. Git history read for `Assets/PyrePlus`, `Runtime/PyrePlus`, `Editor/PyrePlus`, the `60309923` rename diffstat, and the branch/remote state. `D:/CODEZ/Kiln` verified present. `CLAUDE.md`, `unity-scheduler-tick-wedge.md`, `PYRE_GUG.md` §10, `PYRE_SHAPE_FILL_BORDER.md` §1 and §12, and `K-usage-blind-rederivation.md` read in full. `F-decomposability.md` treated as superseded by `G-verification-2.md` per its correction banner. No usage-count evidence was used anywhere in this document. No source changed; Unity not opened; no tree-mutating git command run.*
