# LauAsset Lock — design proposal

*AgentHQ T-0075. Written 2026-08-24, revised 2026-08-25. **This is a proposal, not a spec** — it presents options and recommends; nothing here is built and no C# was changed writing it.*

> **2026-08-25 revision.** The three technical claims this document could not verify have now been measured in the live editor — see `D:\UNITY\Laubrary Dev\LAUASSET_LOCK_MEASUREMENTS.md` and §13.1. One of them (§5.1, on how object references are serialized) turned out to be **false**, and correcting it removes a whole mandatory workstream from Phase 2. The other two tightened §5.3b and §11.2. Every affected section has been corrected in place and is marked. The eight questions in §14 are still unanswered and still gate the size of the job.

**Status: nothing exists.** There is no lock, no snapshot, no revert, no read-only state and no banner anywhere in Laubrary today. The only adjacent thing that exists is `LauminaryVersion` (`Runtime/Launimator/LauminaryVersion.cs:19-39`), and its immutability is UI-only — see §5.4. Everything below is design.

---

## 0. The request, restated

> *"Any LauAsset is set to be edited and autosaved immediately. This makes it possible to ruin or slightly alter an asset. We need both the freedom to just change things and not have to save each change. We also need the possibility to protect certain versions of an asset and say 'Hey this is what the game should look like atm, it should not be possible to change without a conscious decision'."*

Two sentences, two different features. The rest of this document is mostly about not letting them collapse into one.

---

## 1. Two problems, not one

> **"Don't let me ruin it by fiddling" is a SAFETY NET. "Don't let this change without a conscious decision" is a RELEASE GATE. They want opposite things from the editor and they must not share a mechanism.**

| | **Problem A — the safety net** | **Problem B — the release gate** |
|---|---|---|
| The fear | *"I nudged nine dials, three of them made it worse, and I can't remember which."* | *"Someone (me, last Tuesday) changed the boss explosion and the game shipped with it."* |
| Scope | one asset, one session, one person | the whole content graph, forever, the whole team |
| Wants the editor to be | **maximally permissive** — fiddle wildly, cheaply undo | **restrictive** — refuse, or at least loudly announce |
| Runtime involvement | **none** | **the entire point** |
| Right mechanism | cheap rolling snapshots + one-click revert | a sealed copy the game actually uses, plus enforcement |
| Costs if wrong | you lose an afternoon | you ship the wrong game |

The tell that they are different: **Problem A is solved perfectly by a feature that has no runtime component at all**, and **Problem B is not helped even slightly by an undo history**. Conflating them produces the classic bad design where "lock" means both "I'd like a checkpoint" and "this is canon", so every checkpoint pollutes the release surface and every release gate gets weakened until it's convenient enough to use casually.

**Recommendation: build them as two features that share one storage substrate and one banner control, and nothing else.** Problem A ships first (§11, §13 Phase 1) because it is cheap, has zero runtime risk, and delivers value the day it lands. Problem B is the expensive one and needs the decisions in §14 answered first.

---

## 2. The constraint that shapes everything: references are direct object references

Every consumer in this project holds a **direct serialized `ScriptableObject` reference** on a component, prefab or other SO. There is no resolver, no catalog, no `LauAssetRef<T>`, no GUID indirection anywhere:

- `Assets/Demos/ArenaDemo/ArenaEnemy.cs:21-23` — `public List<Pyre> hitBlasts`
- `Assets/Demos/ColosseumDemo/ShmupEnemy.cs:25-26`
- `Assets/Demos/ProtoGuyDemo/ProtoGuySpawner.cs:22` — `public Zoe zoeDef`
- `Assets/Demos/GalleryDemo/GalleryDirector.cs:17-28`
- `SpawnPyreFx.cs:27` — `public PyreAsset blast`

`Resources.Load` is used only by Rulesets/Cabinets/SpriteFxSettings/TextSplash/ZUI/Zounds; Addressables only by Zounds. Nothing else asks a system "give me the asset called X" — it already **has** the asset.

> **Therefore: "at runtime the locked version is used anyway" CANNOT be implemented as a reference redirect. The pointer is baked into the scene/prefab file. The only things you can change are the CONTENT the pointed-at object holds, or the pointer itself — and the pointer is not yours to change.**

Three ways out, honestly costed:

### 2a. Content swap on the same object — *recommended*

Keep the reference. Before Play/build, overwrite the live asset's serialized fields with the sealed snapshot's; after Play/build, put the draft back.

- **Cost to build:** low-medium. `EditorJsonUtility.FromJsonOverwrite` + `playModeStateChanged` + a build pre/post processor. Two existing build-processor precedents in-repo (`Editor/TextSplash/SplashBorderAutoBake.cs:331` — `IPreprocessBuildWithReport` only, no post half; `Editor/Zounds/ZoundsPreprocessBuild.cs:10`) — the Zounds one is literally copy-before-build / clean-up-after-build, the exact shape needed.
- **Cost at runtime:** zero. The game never learns this happened.
- **Risk:** **this is the dangerous option**, because a failed restore destroys the user's draft. §7 is entirely about making the restore crash-safe. Do not build this without the recovery sidecar.
- **Also:** the asset file on disk churns twice per build, which source control will show as a modification. §8.

### 2b. Introduce an indirection layer

`LauAssetRef<T>` wrapping a GUID, resolved through a provider that picks draft-vs-sealed. Clean, no mutation, no restore problem, mode switching is a one-line policy change.

- **Cost:** **a migration of every reference in the project and every consumer field, plus every Laubrary tool's picker UI, plus a runtime resolve cost, plus losing Unity's own dependency tracking** (a GUID in a string field means the build system no longer knows the asset is referenced — it stops getting included in the build unless it's under `Resources/` or Addressables, which is a second migration). That last point alone is disqualifying for a retrofit.
- **Verdict: correct if you were starting today; wrong as a retrofit.** Not recommended. Worth revisiting only if Laubrary ever grows a real content-catalog need for other reasons.

### 2c. Build-time materialise into a separate asset set

Copy sealed content into a parallel folder at build time and rewrite references in a temp copy of the scenes/prefabs.

- **Cost:** high — you are writing a mini asset pipeline, and reference rewriting across prefab variants and nested prefabs is where this kind of thing goes to die.
- **Verdict: no.** It buys nothing over 2a except avoiding the file churn, at ten times the complexity.

**Decision: 2a (content swap), with the entire safety burden pushed into the restore path — and deferred to Phase 3.** Stated precisely, because it is easy to misread as "build the swap": 2a is the only *viable* mechanism if you want the runtime substitution the request describes, so it is the one to design toward — but §8 and §13 both recommend shipping Phases 1–2 **without any swap at all**, because a verify-only gate delivers most of the release property at a tenth of the risk. Choose 2a as the destination; do not treat it as the starting point. Runner-up 2b, explicitly deferred and probably permanently.

---

## 3. The ladder — four tiers, opt in per project AND per asset

The user asked for everything to be opt-in and configurable, "as a project will have different needs in different stages". That maps naturally onto a ladder where each tier is a strict superset of the one below, a project sets a **ceiling** (which tiers are even available/enforced), and an individual asset sets **its own tier** at or below that ceiling.

| Tier | Name | What it protects | Runtime effect | Build size | Who it's for |
|---|---|---|---|---|---|
| **0** | Open | nothing | none | — | today; early prototyping, solo, everything churning |
| **1** | **Soft lock** | against *accidental* edits — you must consciously unlock | **none** | **S** | mid-production; "this one's settled, stop poking it" |
| **2** | **Sealed** | the shipped content, per asset | Play + build serve the **seal**, not the draft | **L** | pre-alpha onward; the answer to "what does the game look like right now" |
| **3** | **Project modes** | the whole team's shared understanding | Test mode: Play shows the draft. Locked mode: Play shows seals. **Builds always seal.** | **M** on top of T2 | team; anything with a release cadence |

### Tier 1 — Soft lock

An asset is marked locked. Tool windows show a red banner and every dial is **disabled**; the toolbar grows an **Unlock** button that requires an explicit click (and reverts to locked when? — §14 Q3). The Inspector shows the same banner via a decorator (§9.4). Nothing about the runtime changes: the game uses whatever is in the file, and the file is whatever it was when you locked it.

- **Protects:** fat-finger edits, "I opened the wrong asset", another team member idly dragging a slider.
- **Does NOT protect:** anything, once you click Unlock. It is a speed bump, not a wall. It also does not protect against edits made by *code* (a baker, a migration script, `Undo` replaying).
- **Why it's still worth building first:** it is ~90% of the *felt* benefit for a solo dev, and it requires **no snapshot, no swap, no play-mode dance, no build hook**. It is the cheapest possible thing that answers "it should not be possible to change without a conscious decision" **literally as written**.

### Tier 2 — Sealed

Locking now also **captures a snapshot** (§5). The draft asset stays fully editable — you fiddle freely — but entering Play (subject to Tier 3's mode) and building both *may* serve the sealed content via §2a's swap. Banner says *"you are editing a draft the game will not use."*

> **Read this before building the ladder from this table: Tier 2 describes the full capability, not the recommended default configuration.** §8 recommends `buildPolicy = Verify` (fail the build on drift, never swap) and §12.1 recommends `playModePolicy = Draft always`. Under those defaults a Tier 2 asset swaps on **neither** Play nor build — it captures a seal, shows the banner, offers revert, and blocks a drifted build. Swapping is a deliberate opt-in that arrives in Phase 3 (§13). This is intentional — the swap is the only part of the feature that can destroy work (§7) — but it means "Tier 2" out of the box is a **gate**, not a substitution. If you want the substituting behaviour the request describes, that is `buildPolicy = Swap` + `playModePolicy = Follow mode`, and it is the last thing to be built, not the first.

- **Protects:** the shipped/played result, per asset, from anything that happens in the editor.
- **Does NOT protect:** the graph (§6). Sealing a `Zoe` and not its `WeaponDef` means the played result is a sealed body with a live weapon — **this is the single worst failure mode in the feature** and §6 exists to answer it.
- **Also does not protect:** generated side-products. A sealed `Pyre` whose baked sprite sheet PNG was re-baked after sealing plays back with the new pixels unless the bake output is part of the seal (§5.5).

### Tier 3 — Project modes

One project-wide switch with two positions:

- **Test mode** — Play in the editor uses **drafts**. Builds still use **seals**. This is exactly the user's own description: *"a test mode where runtime in the editor will show it how it is in the editor but the locked version is always available for builds."* Banner: *"Test mode: Play uses your draft. The build will not."*
- **Locked mode** — Play uses **seals**. Editing still allowed on drafts (they just don't show up). Banner: *"Locked mode: Play uses the sealed version. Your edits are not being shown."*

- **Does NOT protect:** anything, if the mode is per-user (§12.2 recommends exactly that). Two people on the same commit can be in different modes, so "what the game looks like when I press Play" is **not a shared fact** — it is a per-machine one. That is the price of not having the mode land in source control and flip under someone mid-session, and the banner is the only thing that makes it visible.
- **Also does NOT protect:** an asset that is Tier 0 or Tier 1. The mode switch only chooses between draft and seal, and an unsealed asset has no seal to choose — it plays live in **both** modes. So "Locked mode" does not mean "the whole game is locked"; it means "every *sealed* asset shows its seal", which is a much weaker and much easier-to-misread claim. The banner wording in §9.2 has to carry this.
- **Also does NOT protect:** against the mode being wrong for a *reason you can't see* — a Play session where nothing looks odd because no sealed asset happened to be on screen.

Builds are **always** sealed regardless of mode. That asymmetry is deliberate: a mode switch is a per-person convenience; a build is the artefact you hand someone. Making the build follow the mode means one person's forgotten toggle ships the wrong content, which is precisely the disaster the feature exists to prevent.

> **Bold claim, stated so it can be argued with: the build must never honour Test mode. If Lautaro wants a "build my drafts" build, that should be a separate, loudly-named build menu item (`Laubrary/Build with Drafts`), not a mode the editor can be quietly left in.**

### Per-asset vs per-project

The project ceiling is a single enum in project settings (§12). An asset's own tier lives in the lock side-table (§4). An asset can be Tier 1 while its neighbour is Tier 2. An asset cannot exceed the project ceiling — raising the ceiling is what "the project entered a new stage" means.

---

## 4. Where the lock flag and the snapshot bytes live

Two separate storage questions with two different right answers.

### 4a. The flag (small, frequently read, must survive rename/move)

| Option | Rename/move | SCM diff noise | Merge conflicts | Teammate without the library | Verdict |
|---|---|---|---|---|---|
| **GUID side-table** (LauTag pattern, `Editor/AssetKit/LauTagLibrary.cs:59-75`) | **survives** (GUID is the key) | one file changes per lock — small | **one file, everyone edits it — this is the real cost** | sees nothing; asset looks unlocked | **recommended** |
| Field on each asset | survives | touches the asset itself — the thing you're trying to freeze | none | sees the flag | rejected: requires changing **every** LauAsset type, and there is no base class to change (`LauAssetHook.cs:52-58` — LauAsset is a duck-typed predicate, not a type) |
| Sidecar file (`SheetProvenance.cs` pattern, `PathFor(p) => p + ".source.json"`) | **breaks on rename** unless something re-links | one small file per asset — clean | none | sees the file, ignores it | strong runner-up |
| Importer `userData` (`LauAssetBrowser.cs:43-51` `"LaubraryBaked:"` precedent) | survives | `.meta` churn | rare | sees it | rejected: `.meta` is a bad place for authored intent, and it's easy to lose to a reimport |

The `LauTagLibrary` header states the reason it exists in that shape, and it applies verbatim here: *"annotating by GUID means zero existing LauAsset type needs to change to become taggable (fully additive, per the plan this follows)"* (`LauTagLibrary.cs:8-14`). A lock has exactly the same additivity requirement.

**Recommendation: a `LauLockLibrary` singleton SO at `Assets/Laubrary/LauLockLibrary.asset`, GUID-keyed, shaped like `LauTagLibrary` and provided by a `ZuiToolStateProvider`-shaped `[InitializeOnLoad]` static with the same debounced save** (`Zui/Toolkit/ZuiToolStateProvider.cs` — 0.5s deadline, flushed on idle `Tick`, `beforeAssemblyReload` and `EditorApplication.quitting`, and `SaveAssetIfDirty` so it never flushes anyone else's work). Note it goes in `Assets/Laubrary/` in the **host** project, not the package, for the reason that file already documents: *"Laubrary ships no authored assets, and a team's editor arrangement belongs to the project that authored the content."*

**The merge-conflict cost is real and should be stated plainly:** one shared file means two people locking two different assets on two branches produce a conflict. It is a small, textual, easily-resolved conflict (two entries in a list), but it exists. If it becomes painful the escape hatch is to shard the library by folder, which the API can hide.

### 4b. The payload (large, rarely read, must be diffable-ish and must not confuse the asset browser)

| Option | Notes | Verdict |
|---|---|---|
| **Sidecar JSON** next to the asset (`Foo.asset.lauseal.json`) | Human-readable, diffs meaningfully, doesn't get picked up by `FindAssets("t:Pyre")`. (It is *not* invisible to Unity — a `.json` under `Assets/` is imported as a `TextAsset` with its own `.meta`, exactly like `SheetProvenance`'s `.source.json`; it just isn't a *Pyre*.) Breaks on rename unless the library's GUID entry stores the path and a mover fixes it up. | **recommended** |
| Sub-asset inside the `.asset` (`AddObjectToAsset`) | Rename-proof for free. **But it doubles the asset's serialized size, shows up in the Project window's expand arrow, is visible to `LoadAllAssetsAtPath`, and — worst — it means the seal is inside the thing being swapped**, so a botched swap can eat the seal too. | rejected |
| Folder-per-version (Launimator pattern, `LauminaryRepo.cs:71-73`) | Real, proven, self-contained (`CommitNewVersion` at `LauminaryRepo.cs:342-369` deep-copies animations and re-owns source frames into `vN/Source/`). Gives free multi-version history. **But it makes the seal a real asset**, so it shows in browsers, in `t:` searches, in pickers — and every tool's `AssetLibrary<T>` browser would need to filter it out. | runner-up; the right answer **if** §14 Q1 says "keep N versions, not one" |
| `AssetDatabase.CopyAsset` into a hidden folder | Same as above but hidden from Unity (`Seals~/` with a tilde is invisible to the AssetDatabase). Cheap. But then the copy is not an asset, so you can't `LoadAssetAtPath` it — you'd be parsing YAML yourself. | rejected |

**Recommendation: one sidecar JSON per sealed asset, holding the seal plus metadata (sealed-when, sealed-by, a content hash of the draft at seal time — the `SheetProvenance` "identity follows content, not name" idea at `SheetProvenance.cs:11-15` applies here too).** Runner-up: folder-per-version, and it becomes the recommendation the moment history is wanted.

---

## 5. Capturing and restoring a snapshot

### 5.1 The three candidate mechanisms

| Mechanism | Captures | Restores | Verdict |
|---|---|---|---|
| **`EditorJsonUtility.ToJson` / `FromJsonOverwrite`** | full serialized state **including `[SerializeReference]` managed refs** (unlike `JsonUtility`, which cannot); object references come out as **`{"fileID", "guid", "type"}`** — persistent and portable, *measured*, see below | overwrites **in place**, so the object identity is preserved — which is exactly what §2a needs | **recommended, no caveat** |
| `Object.Instantiate` (the `PyreFrameFill.CloneForWorker` pattern, `Runtime/Pyre/PyreFrameFill.cs:86-96` — *"Instantiate goes through serialization, which is what makes the copy complete by construction"*, `hideFlags = HideAndDontSave`) | same completeness | produces a **new object**, so restoring means copying field-by-field or `EditorUtility.CopySerialized` | good for the *in-memory* half (holding the draft while swapped); wrong for on-disk persistence |
| `AssetDatabase.CopyAsset` | the file, byte-perfect, sub-assets included | a file copy back | simplest and most faithful, **but** it destroys and recreates the object, breaking the live reference §2a depends on |

**Recommendation: `EditorJsonUtility.ToJson` for the sidecar payload, `FromJsonOverwrite` for the swap.** It is the only one of the three that writes *into the existing object*, which is the whole trick that lets §2a work without touching a single reference.

> **MEASURED 2026-08-25 — this section previously claimed the opposite, and the claim was false.** An earlier revision of this document asserted that `EditorJsonUtility` emits `UnityEngine.Object` references as a session-local `{"instanceID": N}`, and therefore that a GUID-remapping layer was mandatory before a seal could be persisted. That was measured in the live editor and is **wrong**. `EditorJsonUtility.ToJson` emits references as `{"fileID": …, "guid": …, "type": …}` — exactly the two values `AssetDatabase.TryGetGUIDAndLocalFileIdentifier` returns — for assets on disk, and uses the same form inside nested `[SerializeReference]` payloads. `instanceID` appears only ever as the literal `0`, for a null reference and for a live in-memory object that has no asset backing it. **Consequence: the remapping layer is deleted from Phase 2 outright.** A persisted seal is portable across restarts, reimports and machines with no post-processing at all. The one real residual is that a reference to a non-persisted in-memory object cannot be sealed — it serialises as `0` — which is a refuse-and-report case, not a remapping case. Full evidence: `D:\UNITY\Laubrary Dev\LAUASSET_LOCK_MEASUREMENTS.md`.

### 5.2 `[SerializeReference]` — the known hazard, flagged loudly

Laubrary uses `[SerializeReference]` heavily: **94 `[SerializeReference]` attributes across 53 files** in `Assets/Packages/Laubrary/` (108 mentions of the bare token across 56 files, once comments and doc text are included), including inside the exact types you'd want to seal — `Runtime/Pyre/Pyre.cs`, `Runtime/Pyre/PyreForm.cs`, `Runtime/Chunks/ChunkSpec.cs`, `Runtime/Lathe/LatheSolid.cs`, `Runtime/SpriteFx/SpriteFxFilter.cs`, `Runtime/Daemon/Brain.cs`, `Runtime/Rulesets/RuleSet.cs`, and `WeaponDef.muzzle` (`[SerializeReference] public ICombatFx muzzle`, `Runtime/Zoetrope/WeaponDef.cs:38`).

Project memory `serializereference-data-loss-hazard` records a **real incident**: managed references null out when deserialized against a broken/changed assembly, and *"never save from probes"*. That hazard lands directly on this feature:

- A seal captured under assembly A and restored under assembly B (a class renamed, moved namespace, or a `.dll` that failed to compile) **silently nulls the managed refs**. You do not get an exception; you get a `Pyre` with no forms.
- Worse: with §2a, the null-out happens **on the swap into the live asset**, which then gets autosaved. The draft is gone and the seal restored garbage.

**Mitigations, all of which should be in Phase 2:**

1. **Never swap while `EditorUtility.scriptCompilationFailed`.** Refuse the whole operation, banner it, log it.
2. **Store the type-name manifest with the seal.** On restore, verify every `"rid"` type string in the JSON still resolves before writing anything. If one doesn't: refuse, name the type, tell the user.
3. **Round-trip verify at seal time.** `ToJson` → `FromJsonOverwrite` onto a throwaway `Instantiate` clone → `ToJson` again → compare. If the two JSONs differ, the seal is not faithful; refuse to seal and say why. This is cheap and catches the whole class of problem at the moment the user is paying attention.
4. **Never overwrite a draft without having written the draft's own JSON to the recovery sidecar first** (§7.4).

### 5.3 References to other assets inside a seal

`ToJson` writes cross-asset references as a *pointer*, not as contents — so a seal of a `WeaponDef` contains a reference to its `AmmoDef`s, not their data. Restoring the seal restores the pointers, which is correct and cheap. **But it means a seal is not self-contained**, which is §6's entire subject. If the pointed-at asset was deleted since sealing, restore yields a null. Detect and report at restore time; do not silently null. (Per §5.1, the pointer is *already* emitted as GUID + local file ID, so it needs no conversion — measured, not assumed.)

### 5.3b Sub-assets — a real hole, called out rather than papered over

**`EditorJsonUtility.ToJson` serialises exactly one object. If a LauAsset has sub-assets — additional objects stored inside the same `.asset` file via `AssetDatabase.AddObjectToAsset` — a seal captures the main object and silently omits every one of them.** That is the worst possible failure shape: sealing appears to succeed, the seal looks complete, and the omission only surfaces later as a null or a stale sub-object after a restore.

Three options, in increasing order of cost:

| Option | How | Cost | When it's right |
|---|---|---|---|
| **Refuse** | At seal time, call `AssetDatabase.LoadAllAssetsAtPath(path)`; if it returns more than the main object plus its `.meta`, **refuse to seal** with a message naming the sub-assets. | trivial | Phase 2 default. Honest, and if no LauAsset actually uses sub-assets it costs nothing forever. |
| **Capture the set** | Seal becomes an ordered array of `{localFileId, typeName, json}` rather than one blob; restore walks it, matching by local file ID, and reports added/removed/retyped sub-assets instead of guessing. | medium | Once a real LauAsset needs it. |
| **Copy the file** | Seal the whole `.asset` file with `AssetDatabase.CopyAsset` into a hidden folder; restore by copying back. Captures sub-assets perfectly and needs no JSON at all. | medium, but see §5.1 | Only if sub-assets turn out to be common — it reintroduces every problem §5.1 rejected `CopyAsset` for. |

**Recommendation: Refuse in Phase 2 — but the naive form of that guard is now known to be wrong.** This was settled empirically on 2026-08-25 (`D:\UNITY\Laubrary Dev\LAUASSET_LOCK_MEASUREMENTS.md`): **2 of 110 LauAssets carry a sub-asset**, and in both cases it is a `MirageView` holding a `Texture2D` named `Thumbnail` that `MirageView.SetThumbnail` (`Runtime/Mirage/MirageView.cs:62`) writes into the file; one also carries a Unity `ImportLog` marked `HideInHierarchy`. So the answer is neither "none" nor "Pyre and Zoe do" — it is "a regenerable preview and an editor artefact". A guard written as `LoadAllAssetsAtPath(path).Length > 1 → refuse` would therefore refuse both `MirageView` assets from day one, over a thumbnail nobody wants sealed. **The guard must exclude Unity's own bookkeeping objects (`ImportLog`, anything `HideInHierarchy`/`HideAndDontSave`) and treat a regenerable preview as non-blocking rather than as data loss.** The multi-object seal format stays unbuilt: nothing in the LauAsset set holds a sub-asset whose contents actually matter. Watch `SplashBorderFontBaker.cs:297`, which uses `AddObjectToAsset` on a `TMP_FontAsset` — outside the LauAsset set today, and the first thing that would change this answer.

### 5.4 What `LauminaryVersion` already teaches

`LauminaryVersion.versionNumber` is `0` for the editable draft and `1..N` for committed snapshots (`Runtime/Launimator/LauminaryVersion.cs:21-22`). `LauminaryRepo.CommitNewVersion` (`LauminaryRepo.cs:342-369`) deep-copies and re-owns source frames so a version stands alone. It is the only working precedent in the codebase and it is a good one.

**But its immutability is enforced only by hiding UI** (`LauminaryBrowserWindow.cs:470,491-500`). Nothing prevents writing to a committed version's asset — the Inspector will happily edit it. And nothing at runtime chooses draft-vs-committed: `LauminaryView.version` and `ZonedLauminaryView.version` are plain direct references to a specific `LauminaryVersion`, so what the game shows is whatever was dragged in.

> **The honest reading: Launimator solved the *storage* half of Problem B years ago and never solved the *enforcement* or *runtime selection* halves. This design should absorb Launimator rather than duplicate it — a committed `LauminaryVersion` should become a first-class seal, so "lock" means the same thing everywhere.** That is a Phase 4 nice-to-have, not a Phase 1 blocker, but the shape should be chosen now so it stays possible.

### 5.5 Generated and baked side-products

A seal of the *spec* does not seal the *output*. Two live cases:

- **PyrePlus baked sprite sheets + AnimationClips** — real PNGs and `AnimationClip`s on disk, marked with the importer `userData` prefix `"LaubraryBaked:"` (`LauAssetBrowser.cs:43-51`, written by `PyreBaker.cs:30`). Per project memory `pyreplus-baking-and-laubrary-2026-08-23`, cross-tool consumption goes through `PyrePlusChunkAnimation`/`IChunkAnimation`, **never the baked PNG** — which helps, but the PNG is still what a consumer that references it directly will show.
- **Launimator generated prefab + controller** — `LauminaryVersion.prefab` / `.controller` are direct references to generated assets.

**Recommendation: a seal records the GUID + content hash of every declared side-product, and the banner warns when a side-product's hash no longer matches the one recorded at seal time** (*"this sealed asset's baked output has been re-baked since sealing"*). Actually *sealing the bytes* of a sprite sheet is out of scope — it's a build-artefact problem, and the honest answer is "re-bake from the seal", which Phase 4 can do properly.

---

## 6. The dependency-closure problem — the sharpest edge in the feature

`Zoe` → `ZoeWeaponSlot` → `WeaponDef` → `List<AmmoDef>` (`Runtime/Zoetrope/WeaponDef.cs:32`) → `Pyre` → `ChunkSpec`, plus `[SerializeReference] ICombatFx muzzle` (`WeaponDef.cs:38`), plus `Zoe.loadout` as `[SerializeReference] List<IActivatable>` (`Zoe.cs:104`) and `Zoe.cues` (`Zoe.cs:119`).

> **"What the game looks like right now" is a property of a GRAPH, not of an asset. A lock on one node of that graph is close to meaningless on its own — and, worse, it is *confidently* meaningless: it shows a green padlock while the thing it names visibly changes.**

### Three propagation models

| Model | How it works | Pro | Con |
|---|---|---|---|
| **Node-only** | lock exactly the asset you clicked | trivial; no surprises about what got touched | the padlock lies. Rejected as the *only* model. |
| **Closure capture** | sealing walks `AssetDatabase.GetDependencies` (or `EditorUtility.CollectDependencies`) and seals every LauAsset reachable, as one **lock set** | the seal actually means what it says | sealing one boss can seal forty assets, several shared with other bosses; unsealing gets ambiguous |
| **Opt-in cascade** | sealing shows the closure and lets you tick which members to include; unticked members become **watched** | honest, controllable, teaches the user the graph | more UI; the user has to make a judgement each time |

**Recommendation: opt-in cascade, defaulting to "seal the whole closure", with the closure shown before you commit.** The confirm dialog is not a nag — it is the moment the user learns that their boss references six shared assets, which is information they want.

Introduce **the lock set** as a first-class object: a named group (`"Vertical Slice 2026-09"`) with N member assets, each with its own seal, sealed together at one timestamp. Sealing and unsealing operate on the set. This also gives the release gate a name a human can say, which is the difference between a feature people use and one they don't.

### Watched dependencies — the answer to the sharpest failure mode

**Question: what happens when you edit an unlocked asset that a locked asset depends on?**

Doing nothing is unacceptable — that is the silent path to "the game changed and the padlock said it couldn't". Blocking the edit is also unacceptable — that would make one lock freeze half the project transitively, and shared `AmmoDef`s/`Pyre`s are shared *precisely because* they're still being tuned.

**Recommendation: allow the edit, and make it impossible to miss.**

1. The dependent asset's tool window shows an **orange** banner immediately: *"Depended on by 3 sealed assets — editing this changes what they play. (Boss Alpha, Boss Beta, Vertical Slice 2026-09)"*, with a button to open the lock set.
2. The **lock set itself** becomes *stale* and says so wherever it's shown: *"2 members have drifted since sealing."* Staleness is computed by comparing each member's current content hash to the one recorded at seal time — the `SheetProvenance.md5` idea (`SheetProvenance.cs:11-15`) reused.
3. **Crucially, staleness does not change what plays.** A sealed asset plays its seal. A watched-but-unsealed dependency plays live. So a stale set means "the seal is partly a lie" — and the banner says exactly that, in those terms: *"This set is partly live: 2 of its dependencies are not sealed and have changed."*
4. **Re-seal** is one button on the set, which re-captures the drifted members.

Costs: computing "who depends on me" requires a reverse index. `AssetDatabase.GetDependencies` is forward-only, so you build the reverse map by walking every lock-set member's dependencies at seal time and caching it in the lock library. Only sealed assets need it, which bounds the index — but **"invalidates when a member is edited" is not as cheap as it sounds**: with lock sets spanning dozens of shared assets, a naive implementation re-walks the graph on every dial nudge. Make it lazy and debounced (mark dirty on `ObjectChangeEvents`, rebuild on idle, exactly the debounce `ZuiToolStateProvider` already uses), and only re-walk the edited asset's own forward dependencies rather than the whole set. If that still bites, fall back to rebuilding the index only at seal time and on demand, and accept that the "depended on by a sealed asset" banner can be one edit stale.

---

## 7. Play mode — where this feature can silently destroy work

### 7.1 The fact that makes this dangerous

> **ScriptableObject asset edits made during Play mode PERSIST. Unlike scene changes, they are not rolled back on exit.** So if Tier 2/3 swaps seal-content into a live asset on entering Play, and anything goes wrong before the restore, the swapped-in seal **is now the asset** and the draft is gone. (Precisely: Unity keeps the mutated object in memory after exiting Play — it does not restore it from disk — so the seal becomes the truth the moment anything saves it, which for Laubrary's tools is immediate, since they `SetDirty` on every dial.)

This is not a hypothetical. It is the default outcome of every failure mode below.

### 7.2 The happy path

`EditorApplication.playModeStateChanged`:

- `ExitingEditMode` — for every asset the run will use in sealed form: capture the draft JSON, write it to the **recovery sidecar**, set the *swap-in-progress* flag in the lock library, flush both to disk, **then** `FromJsonOverwrite` the seal into the asset, `SetDirty`, and fire `AssetCacheInvalidation.Invalidate(asset)`.
- `EnteredPlayMode` — nothing.
- `ExitingPlayMode` — `FromJsonOverwrite` the recovery JSON back, `SetDirty`, `Invalidate`, clear the flag, delete the recovery sidecar.

**Every content swap MUST fire `AssetCacheInvalidation.Invalidate`** (`Runtime/Caching/AssetCacheInvalidation.cs:36-49`). Subscribers that will otherwise show stale content: `PyreBlastPlayer.cs:43-47` (clears `PyreRenderer`'s frame cache), `MirageSubject.cs:127-147`, `MirageRig.cs`, `LauAssetGridGUI.WatchInvalidation`. Note `MirageSubject` deliberately **defers its rebuild to `Update()`** because doing it inline caused Unity's *"Access version should be odd"* assertion — so the swap must not assume subscribers respond synchronously.

### 7.3 The failure modes

| Failure | What happens without care | Answer |
|---|---|---|
| **Domain reload** between the two callbacks | static state (`_swappedAssets`) is wiped; the restore never runs | never keep swap state in statics — it lives in the lock library asset, which survives reload (this is why the flag must be *flushed*, not just `SetDirty`ed) |
| **Enter Play Mode Options with reload disabled** | statics *survive*, but so do stale caches — and, more importantly, **`[InitializeOnLoad]` static constructors and `AssemblyReloadEvents.beforeAssemblyReload` do not run at all on entering Play**, so the §7.4 recovery check and the `ZuiToolStateProvider`-style reload flush both silently stop firing at that boundary | works fine either way if state is in the asset *and* the recovery check also runs from `playModeStateChanged`, not only from `[InitializeOnLoad]`; explicitly test both |
| **Editor crash / process kill mid-play** | asset is left holding the seal; user's draft gone | §7.4 |
| **Play → recompile → Play** | project memory `unity-editor-mid-play-compile-resets-state` warns that mid-Play compile silently resets fields; here it could also re-run `ExitingEditMode` logic | refuse to swap if a compile is pending; treat mid-play compile as "the run is void" and restore |
| **User edits a swapped asset during Play** | they are editing the *seal* believing it's the draft, and their edit is then discarded by the restore | banner during Play must be unmistakable (§9.2); consider `HideFlags.NotEditable` on swapped assets for the duration of the run — `SimpleMenuBase.cs:181` uses `HideFlags.DontSave \| HideFlags.NotEditable`, though only on a runtime-created scene `GameObject`, so it shows the flag combination is used in-repo but is **not** a precedent for applying it to an *asset* |
| **Two editors open on the same project** | project memory `unity-scheduler-tick-wedge`; both could swap | out of scope, but the recovery flag makes it detectable |

### 7.4 Crash-safe restore — the non-negotiable part

**The recovery sidecar is not an optimisation, it is the reason this is safe enough to build.**

1. Before any swap, write `Assets/Laubrary/LauLockRecovery/<guid>.draft.json` (a real folder so it survives, not a temp dir) and set `LauLockLibrary.swapInProgress = true` with the list of affected GUIDs, then **flush both to disk synchronously**.
2. On every domain load, an `[InitializeOnLoad]` static checks `swapInProgress`. If it is true and we are **not** in Play mode, the previous session died mid-swap: restore every listed draft from its recovery JSON, clear the flag, and **log a visible warning** naming the assets.
3. Only delete a recovery file after a verified restore (read back and compare hashes).

> **Rule: the recovery write happens before the swap write, and the recovery delete happens after the restore verify. If the process dies at any instant, the draft is recoverable.**

An acceptable Phase-2 simplification: **do not swap on Play at all in the first sealed release.** Ship Tier 2 as *build-only* sealing plus a banner, and add play-mode swapping in Phase 3 once the recovery machinery is proven by the build path. This is the recommendation — see §13.

---

## 8. Build mechanics

`IPreprocessBuildWithReport` / `IPostprocessBuildWithReport`, exactly the shape of `Editor/Zounds/ZoundsPreprocessBuild.cs:10-23` (pre: stage content; post: clean up) and `Editor/TextSplash/SplashBorderAutoBake.cs:331`.

Same swap, same recovery sidecar, plus these specifics:

- **Builds are always sealed** (§3), regardless of mode.
- **`OnPostprocessBuild` cannot be relied on when the build fails or is cancelled.** Unity's behaviour here is version-dependent and has changed across releases — sometimes the callback runs with `report.summary.result == Failed`, sometimes (an early abort, a compile error, a cancel) it does not run at all. Treat "it may not run" as the design assumption and **verify empirically on this project's Unity version** rather than trusting either answer. The recovery-sidecar check on the next domain load (§7.4) covers it, but the user may notice a moment where the asset holds the seal — so also arm an `EditorApplication.update` hook in `OnPreprocessBuild` that restores if `BuildPipeline.isBuildingPlayer` has gone false without a post callback. Note the timing: `BuildPipeline.BuildPlayer` blocks the main thread, so that hook does not tick *during* the build — it fires on the first editor tick after the build returns, which is early enough to matter but is not a watchdog while the build runs.
- **Source control will see the asset change twice.** During a build, `Foo.asset` becomes the seal and then becomes the draft again. If the build is interrupted, the working tree shows the seal as an uncommitted modification — confusing, and dangerous if someone commits it. **Mitigation: the banner and the recovery-check log must say the asset is mid-swap, and the recovery check must run before anyone looks.** Adding `Assets/Laubrary/LauLockRecovery/` to `.gitignore` is wrong — you want the recovery files committed-adjacent? No: recovery files are transient per-machine state; **do** gitignore them, and document that a crash mid-build is recovered locally, not shared.
- **Safer alternative worth considering:** don't swap the asset at all — instead, in `OnPreprocessBuild`, verify every sealed asset's current content hash **matches its seal**, and **fail the build with a clear message** if it doesn't (*"3 sealed assets have unsealed edits. Re-seal, revert, or lower them to Tier 1."*). This is a **gate rather than a swap**: zero mutation, zero recovery machinery, zero SCM churn, and it makes the release property true by refusing to build when it isn't. The cost is that it does not let you keep a draft and ship the seal simultaneously — which is a feature the user explicitly asked for, so it can't be the *only* mode.

> **Recommendation: build BOTH, and make it a setting. "Verify" (fail the build on drift) as the default because it is safe and honest; "Swap" (build the seal, keep the draft) as the opt-in for the workflow the user described.** Verify-mode is roughly a tenth of the work and can ship in Phase 2 on its own.

---

## 9. The red banner

### 9.1 It goes in ZUI, not in a window

ZUI has **no banner, warning strip, status bar, or red at all**. `Zui/Toolkit/ZuiToolkit.uss:5-22` defines `--zui-accent` (blue), `--zui-accent-soft`, `--zui-line`, `--zui-bg-inset`, `--zui-sub-fill`, `--zui-section-title` (green), `--zui-stage-bg`, `--zui-icon-neutral` — and nothing red. There is no `.zui-banner` / `.zui-warning` / `.zui-danger` selector in its 752 lines. Closest existing primitives: `Z.Help(text, HelpBoxMessageType)` (`Zui.cs:784-785`, Unity's stock look — too quiet, and it looks like a hint, not a warning), `Z.Frame(title, tooltip, children)` (`Zui.cs:206-220`), `Z.Box`/`Z.BoxKeyed`, `Z.Section`, `Z.Divider`, `Z.Text`, `Z.Icon(name, size)`, `ZuiChip.SetColourDot(Color)`. The IMGUI half has `ZUI.FillRect(rect, paletteColorName, fallback)` over a data-driven `ZUIStyleSheetAsset.palette`.

Per CLAUDE.md's ZUI-first rule — *"If no ZUI control fits, that's a smell — surface it and consider EXPANDING ZUI"* — **this is a new ZUI control, `Z.Banner`, not a hand-rolled strip in one window.**

```csharp
public enum ZuiBannerLevel { Info, Warning, Danger, Locked }

// Full-width strip: icon + bold title + regular body + optional trailing action buttons.
public static ZuiBanner Banner(ZuiBannerLevel level, string title, string body,
                               params (string label, string tooltip, Action onClick)[] actions);

// Convenience for the common one-liner.
public static ZuiBanner Banner(ZuiBannerLevel level, string title);
```

New USS variables in `.zui-root`, alongside the existing palette:

```css
--zui-danger:      rgb(214, 64, 64);
--zui-danger-soft: rgba(214, 64, 64, 0.18);
--zui-warn:        rgb(230, 160, 50);
--zui-warn-soft:   rgba(230, 160, 50, 0.16);
--zui-locked:      rgb(120, 200, 140);   /* "sealed and correct" — a calm green, not an alarm */
```

...and matching `.zui-banner` / `.zui-banner--danger` / `--warning` / `--locked` selectors: soft fill, 2px left border in the solid colour, 4px padding, `flexShrink: 0`, `whiteSpace: normal`. Add the same names to `ZUIStyleSheetAsset.palette` so the IMGUI half (`ZUI.FillRect`) can draw the same strip.

### 9.2 The distinct states, with actual wording

The banner is a communication surface, and its whole value is that a person reads it and changes what they were about to do. Each state gets its own words. Vague ("this asset is locked") is a failure.

| State | Level | Title | Body |
|---|---|---|---|
| Tier 1, locked | **Locked** (green) | **LOCKED** | *"This asset is locked. Editing is disabled. Click Unlock to change it — this is a deliberate act."* + action `Unlock` |
| Tier 1, temporarily unlocked | **Danger** (red) | **UNLOCKED FOR EDITING** | *"You unlocked a locked asset. Changes are live and will be kept. Re-lock when you're done."* + actions `Re-lock`, `Discard changes` |
| Tier 2/3, editing a draft the game won't use | **Danger** (red) | **THE GAME DOES NOT USE THIS** | *"You are editing the draft. The game plays the sealed version from {date}. Nothing you change here will appear until you re-seal."* + actions `Re-seal`, `Revert to sealed`, `Compare` |
| Tier 3, Test mode on | **Warning** (orange) | **TEST MODE — PLAY SHOWS YOUR DRAFT, THE BUILD WILL NOT** | *"Play mode is using your live edits. Builds always use the sealed version from {date}. Turn Test Mode off to play what ships."* + action `Turn off Test Mode` |
| Tier 3, Locked mode, during Play | **Danger** (red) | **PLAYING THE SEALED VERSION — YOUR EDITS ARE NOT SHOWN** | *"This asset is temporarily showing sealed content while Play mode runs. Do not edit it now; your changes will be discarded when Play stops."* |
| Depended on by a sealed asset (§6) | **Warning** (orange) | **A SEALED ASSET DEPENDS ON THIS** | *"Editing this changes what {N} sealed assets play: {names}. This asset is not itself sealed."* + action `Open lock set` |
| Lock set is stale | **Warning** (orange) | **SEAL IS PARTLY LIVE** | *"{N} dependencies of this seal have changed since it was sealed. What plays is not exactly what was sealed."* + action `Re-seal set` |
| Seal cannot be restored (§5.2) | **Danger** (red) | **SEAL CANNOT BE RESTORED** | *"The sealed data references a type that no longer exists: {typeName}. Restoring would destroy data. Nothing was changed."* |
| Recovered from a crashed swap (§7.4) | **Danger** (red) | **RECOVERED YOUR DRAFT** | *"The editor stopped while this asset was showing sealed content. Your draft has been restored from {timestamp}. Please check it."* |

### 9.3 Where it plugs in

**One insertion reaches ~16 tool windows.** `Editor/AssetKit/ZuiAssetWindow.cs:121-154` — `protected sealed override void BuildUI(VisualElement root)` opens with `root.Add(BuildToolbar())` at line 123 and already has `asset` in scope. Insert the banner immediately after the toolbar. Covered by that one line: BackSplash, Cartographer, Prop, TilesetBuilder, Choreographer, Chunk, Larder, LatheMold, Lathe, SpriteCatalog, Mirage, Pyre, SpriteFxStack, Tapestry, TextSplash, and `ZoetropeDefWindow<T>` (so Zoe, WeaponDef, AmmoDef).

IMGUI counterpart: `Editor/AssetKit/LaubraryAssetWindow.cs:98-121 OnZUI` (sealed; only `LazorWindow` uses it) — one more insertion.

**Because `ZuiWindow.Rebuild()` clears the root every time, the banner must be added in `BuildUI`, not in `CreateGUI`** — and the window must `Rebuild()` when lock state changes. Subscribe to a `LauLockLibrary.Changed` event, or piggyback on the existing `AssetCacheInvalidation.Invalidated` bus.

**Not covered by either base — needs per-tool work:** `AnimationAsepriteWindow`, `LauminaryBrowserWindow`, `LauminationBuilderWindow`, `RulesEditorWindow`, `ZoePreviewWindow`, `ZoundsWindow`, `Loom/GraphEditor`, `DashboardEditorWindow`. Phase 3.

### 9.4 The Inspector bypass — a banner in a tool window protects nothing

**Any LauAsset can be edited in the stock Inspector, which never goes near `ZuiAssetWindow`.** For a feature whose promise is "it should not be possible to change without a conscious decision", this is not a footnote — it is the main hole. Three answers, and you want all three:

1. **A `[CustomEditor(typeof(ScriptableObject), editorForChildClasses: true)]` decorator** drawing the banner above the default inspector. Cheap, informative, **and easy to get wrong** — a blanket child-class editor will fight every existing custom editor in the project. The obvious "safer" variant — register the decorator only for types where `LauAssetHook.IsAssetReference(t)` is true and no other `CustomEditor` exists — **is not directly expressible**: `[CustomEditor]` is a compile-time attribute, so you cannot conditionally register at `[InitializeOnLoad]`. What is actually available is a blanket child-class editor that *detects* a competing custom editor at `OnInspectorGUI` time and delegates to it (via `CreateEditor` on the real editor type, resolved by scanning `[CustomEditor]` attributes), or `Editor.finishedDefaultHeaderGUI` / a `[CustomPropertyDrawer]`-free header hook, which draws above *any* inspector without replacing it. Investigate the header hook first — it is the only one of these that does not fight existing editors at all.
2. **`HideFlags.NotEditable`** on a locked asset. This actually greys the Inspector out — real enforcement, not a sign. Nearest in-repo use of the flag: `SimpleMenuBase.cs:181` (`HideFlags.DontSave | HideFlags.NotEditable`) — but on a scene `GameObject`, not an asset, so it is a familiarity precedent rather than a proof this works on a `ScriptableObject` asset; verify it before relying on it. **Caveats: `hideFlags` is serialized**, so it persists into the file and will show as a diff (and setting it is itself an edit to the asset you are trying to freeze); it also makes the asset read-only to rename/delete in the Project window; and it does **not** stop code from writing fields, only the Inspector UI.
3. **`AssetModificationProcessor.OnWillSaveAssets`** — the real backstop. §10.

---

## 10. Enforcement — what actually stops a write

There are **472 `SetDirty|SaveAssets|Undo.RecordObject` occurrences across 99 files** (counted 2026-08-24 over `Assets/Packages/Laubrary/` only; the whole `Assets/` tree including demos and third-party plugins is 504 across 112). There is no choke point today. Any design that requires touching all 472 is dead on arrival. So the question is: what are the *few* places that catch *most* of it, and what leaks through each?

| Point | Catches | Leaks | Effort |
|---|---|---|---|
| **`AssetModificationProcessor.OnWillSaveAssets`** — return a filtered path list, dropping locked assets | **every write to disk, from any source, including the Inspector, including code** | in-memory edits still happen, and they are **not** discarded by a domain reload — Unity carries a dirty loaded object across reload, so the user's phantom change survives until the object is actually unloaded (an editor restart, a reimport). The asset also stays dirty, so Unity keeps re-offering it on every save. Confusing unless the banner explains it | **S** — one new file; **no `AssetModificationProcessor` exists anywhere in the project today** |
| **A shared gate in the ~10 `Dirty()`/`Dial()` helpers** — `TapestryWindow.cs:84-92`, `PyreWindow.cs:2885-2892`, `ChunkWindow.cs:60-68`, `LarderWindow.cs:63-70`, `LatheWindow.cs:85-92`, `LazorWindow.cs:128-136`, `SpriteFxStackView.cs:78-83`, `Loom/GraphEditor.cs:442,497`, `LauminationBuilderWindow.cs:516` | the great majority of *tool* edits, at the point where the user made them — so the tool can refuse **and say why** | anything not routed through a helper; the Inspector; bakers; migration code | **M** — ~10 edits, most of them the same 3-line shape (`Undo.RecordObject → mutate → SetDirty`); two of the listed ones are **not** that shape and need their own handling: `SpriteFxStackView.Dirty` only forwards to the host's `OnBeforeChange`/`OnChanged`, and `LauminationBuilderWindow.Dirty` is a repaint helper that touches no asset at all |
| **Disable the controls** — locked ⇒ `SetEnabled(false)` on the asset pane | everything the user could click | nothing else | **S** — one line in `ZuiAssetWindow.BuildUI` |
| **`HideFlags.NotEditable`** | the Inspector's UI | code; serialized-flag churn | **S** |
| **ZUI's own hooks** — controls take `OnBeforeChange`/`OnChanged` (`Zui/Toolkit/ZuiReflect.cs:98-103`) and **never dirty anything themselves** | could gate centrally at the ZUI layer | ZUI doesn't know about assets and shouldn't; wrong layer | — |

> **Recommendation: `OnWillSaveAssets` as the backstop + control-disabling as the everyday experience + the shared `Dirty()` gate as the explanation layer.** The backstop guarantees exactly one property and it is narrower than it sounds — **`OnWillSaveAssets` governs what reaches DISK, and nothing else.** It does not stop the in-memory object being mutated, and in-memory is precisely where Tiers 2 and 3 live during Play. So: the backstop makes "a locked asset's file cannot change" true; it does **not** make "a locked asset cannot behave differently this session" true. The disabled controls mean nobody ever hits the backstop; the gate means when they somehow do, they get a sentence instead of a mystery.

**What still leaks, stated honestly:** code paths that write to a locked asset in memory and never save (they'll appear to work until domain reload); a determined user editing the `.asset` YAML in a text editor; another Unity editor instance; Undo's own replay. None of these are accidents, which is the bar the user set — *"not possible to change without a conscious decision"*. Editing YAML in Notepad is a conscious decision.

**Extract the ~10 duplicated `Dirty()`/`Dial()` helpers into one `LauEdit.Dirty(asset, label, Action)` in AssetKit while you're in there.** It is a strict improvement independent of this feature (ten copies of the same three lines is a latent bug farm), and it creates the choke point every future feature will want.

---

## 11. The other half — "freedom to change without saving each change"

This half is Problem A, and it is **much cheaper than the locking half and should ship first**.

### 11.1 Revert-to-sealed / revert-to-checkpoint — the one-click undo-everything

If there is a seal, "put it back" is `FromJsonOverwrite(seal)` + `Invalidate`. One button. This alone converts *"I ruined it"* from a disaster into a click, and it is ~20 lines given §5.

### 11.2 Rolling auto-snapshots — the actual answer to the fiddling fear

Independent of locking. Every LauAsset gets **N rolling JSON snapshots** (recommend N = 10) in `Assets/Laubrary/LauHistory/<guid>/`, written on a debounce identical to `ZuiToolStateProvider`'s (0.5s idle, plus `beforeAssemblyReload`, plus `quitting` — `ZuiToolStateProvider.cs:22-33,70-93`) and coalesced so a slider drag produces one entry, not four hundred.

- **Cost: MEASURED 2026-08-25, and it is the bad answer.** Across all 110 LauAssets: total serialized JSON 5,022,331 bytes, median a trivial 1,684 bytes — but p75 is 71,721, the largest single asset is 329,687 bytes (322 KB), and 17 assets exceed 100 KB. **`Pyre` is 32 of the 110 assets and 93.4% of all the bytes.** A 10-deep ring on the largest single `Pyre` alone is 3.2 MB; a 10-deep ring over everything is roughly 48 MB. The optimistic "few KB per entry" guess was right for the median and badly wrong for the assets people actually iterate on. **Consequence: dedup ships WITH this feature, not after it.** And plain byte-identity dedup is not sufficient on its own — the measurement notes that nudging one slider on a `Pyre` produces a genuinely different 322 KB blob, so identity dedup skips only the true no-ops. Pair it with a **per-type N** (a shallow ring for `Pyre`, a deep one for everything else) as the cheaper of the two remaining levers; compression is the third. Full numbers: `D:\UNITY\Laubrary Dev\LAUASSET_LOCK_MEASUREMENTS.md`. Gitignore the folder either way — this is personal safety-net state, not shared truth.
- **UI:** a `History` button in the `ZuiAssetWindow` toolbar → a list of timestamps → hover to preview (every `IVisualPreview` LauAsset can render a thumbnail already) → click to restore.
- **Why this is the highest value-per-line item in the whole document:** it addresses the user's actual stated fear (*"possible to ruin or slightly alter an asset"*) with **no runtime component, no build hook, no play-mode dance, no swap, no recovery protocol, and no way to lose data** — restoring is itself snapshotted.
- **Undo already exists** and is not this. `Undo` dies on domain reload, is per-window, and does not survive closing Unity. Snapshots do.

### 11.3 A true scratch buffer — be honest: probably not worth it

*"Edits go to an in-memory copy and are only written when you press Commit."* It is the theoretically right answer and it fights the entire architecture: every tool edits `asset` directly, `AssetCacheInvalidation` is keyed on the real object, previews resolve the real object, `Undo.RecordObject` targets the real object, and ~103 files assume the object they hold is the asset. Making it a proxy means either (a) a clone with `HideAndDontSave` (the `PyreFrameFill.CloneForWorker` pattern, `PyreFrameFill.cs:86-96`) that every window must swap in and every preview must be pointed at, or (b) rewriting the edit path.

> **Recommendation: don't. §11.2 delivers ~90% of the felt benefit for ~5% of the cost and ~2% of the risk.** Revisit only if §11.2 ships and turns out to be insufficient — which would be surprising.

---

## 12. Configuration surface

### 12.1 Project-wide

There is currently **no `LaubrarySettings` and no `SettingsProvider` anywhere in Laubrary**. Recommend a `LauLockSettings` singleton SO at `Assets/Laubrary/LauLockSettings.asset`, `[InitializeOnLoad]`-provided with the debounced save from `ZuiToolStateProvider` — same shape, same folder, same host-project-not-package rationale.

| Setting | Default | Scope |
|---|---|---|
| `enabled` | **false** | project — the whole feature is opt-in, exactly as asked |
| `ceiling` (Open / SoftLock / Sealed / ProjectModes) | Open | project |
| `buildPolicy` (Verify / Swap / Ignore) | Verify | project |
| `playModePolicy` (Draft always / Follow mode / Sealed always) | Draft always | see 12.2 |
| `autoSnapshot` + `snapshotDepth` | **true**, 10 | project (the *behaviour*), see 12.2 |
| `blockInspectorEdits` | true | project |

**Is a `SettingsProvider` under Project Settings warranted?** Yes, and it is ~30 lines — but note the CLAUDE.md rule about not adding unrequested menu items. A Project Settings page is not a `Laubrary/` menu item and doesn't clutter that menu, so it's the *right* home for a project-wide toggle that isn't a tool. Recommend it, in Phase 3, not Phase 1.

### 12.2 Per-project vs per-user — and the Test-mode question

> **The Test-mode switch is the one genuinely contentious setting, and it deserves a real answer rather than a default.**

If Test mode is **per-project** it lands in source control and flips for the whole team — one person toggling it changes what everyone's Play button shows, arriving as a surprise in a `git pull`. If it is **per-user** (`EditorPrefs`, keyed by project path) it can't be shared, so "we're all testing the sealed build today" has to be said out loud rather than committed.

**Recommendation: per-user `EditorPrefs`, with the project-wide setting acting as the *default for a user who has never set it*.** Rationale: Test mode is a statement about *how I am working right now*, which is the textbook definition of per-user state, and the failure mode of per-user (someone forgets to switch) is contained by §3's rule that **builds ignore the mode entirely**. Runner-up: per-project, which is defensible for a team that wants a single shared answer — but then the banner must be very loud, because the mode changed without you doing anything.

Also per-user: which windows have the banner collapsed, snapshot browser state. Also per-project: everything in the table above, the lock library, all seals.

---

## 13. Phased plan

| Phase | Contents | Relative size | Standalone value? |
|---|---|---|---|
| **1 — Safety net** | `Z.Banner` + `--zui-danger`/`--zui-warn`/`--zui-locked` USS + palette entries. `LauLockLibrary` (GUID side-table, flag only). Tier 1 soft lock: flag, disabled controls, Unlock button, banner in `ZuiAssetWindow.cs:123` + `LaubraryAssetWindow.cs:98`. `OnWillSaveAssets` backstop. Rolling auto-snapshots + History/Restore (§11.2). Extract `LauEdit.Dirty` and route the ~10 helpers through it. | **1×** | **Yes — completely.** Nothing here depends on seals, runtime, Play or builds. It answers Problem A fully and Problem B *literally as the user phrased it* ("not possible to change without a conscious decision"). If Phase 2 never happens, Phase 1 was still worth it. |
| **2 — Sealing, build-verify only** | Seal capture (`ToJson` + round-trip verify + type manifest). Sidecar payload. `Revert to sealed`. Lock sets + closure UI. Reverse-dependency index + the "depended on by a sealed asset" banner + staleness. `IPreprocessBuildWithReport` in **Verify** mode (fail the build on drift). **No swapping anywhere.** | **1.5×** | Yes. Gives a true release gate with zero mutation risk. |
| **3 — Swapping** | Recovery sidecar + `swapInProgress` flag + `[InitializeOnLoad]` recovery check. Build **Swap** mode. Play-mode swap. Tier 3 project modes + Test mode. `SettingsProvider`. Banner into the 8 non-`ZuiAssetWindow` tools. Inspector decorator + `HideFlags.NotEditable`. | **2×** | Yes, and this is the phase that delivers the user's exact described workflow. **Do not start it until Phase 2's seals have been used in anger** — the swap is only as safe as the seal is faithful. |
| **4 — Consolidation** | Absorb `LauminaryVersion` commits as first-class seals. Side-product hashing + re-bake-from-seal. Multi-version history if §14 Q1 says so. | **1.5×** | Optional polish. |

---

### 13.1 Three things to measure before Phase 2 — ALL THREE ARE NOW MEASURED

These were the three claims this document could not verify without running code in the editor. They were measured on 2026-08-25 in the live editor (Unity 6000.3.10f1), read-only. Full evidence, raw output and method: `D:\UNITY\Laubrary Dev\LAUASSET_LOCK_MEASUREMENTS.md`. Summary of what each one did to the plan:

1. **What does `EditorJsonUtility.ToJson` emit for a `UnityEngine.Object` reference? — `{"fileID", "guid", "type"}`. §5.1's claim of a session-local `instanceID` was false and has been corrected in place.** The GUID-remapping layer this document called mandatory **is removed from Phase 2**. Seals are portable as written, with no post-processing. This is the single biggest reduction in Phase 2's cost.
2. **Do any LauAsset types carry sub-assets? — 2 of 110, both regenerable.** §5.3b survives as a cheap guard rather than collapsing, but the guard as originally worded would have refused both of those assets on day one. Corrected in place; the multi-object seal format stays unbuilt.
3. **How big is a real snapshot? — hundreds of KB, and `Pyre` dominates.** Largest single asset 322 KB; 17 of 110 over 100 KB; `Pyre` is 32 of 110 assets and **93.4% of all serialized bytes**. A 10-deep rolling ring over everything is ~48 MB. **Dedup-by-hash is no longer a follow-on — it ships with §11.2 or §11.2 does not ship.** Note the measurement also found that byte-identity dedup alone will not rescue `Pyre` (nudging one slider stores a fresh 322 KB), so a per-type cap on N is the cheaper companion.

Two remain unverified, both noted in place and neither blocking Phase 1: whether `OnPostprocessBuild` runs on a failed or cancelled build on this project's Unity version (§8), and whether `HideFlags.NotEditable` actually greys a ScriptableObject asset's Inspector here (§9.4).

---

## 14. Open questions for Lautaro

1. **One seal per asset, or a history of N?** One seal is simpler and matches "this is what the game looks like *atm*". N versions matches what Launimator already does (`LauminaryVersion` 1..N) and would let seals be named per milestone. This decision changes the payload storage recommendation in §4b (sidecar vs folder-per-version) and should be made before Phase 2.
2. **Is Verify-mode building (fail the build when a sealed asset has drifted) enough on its own?** It is dramatically cheaper and safer than swapping, and it delivers "the build is what was sealed" — it just doesn't let you keep a draft *and* ship the seal at the same time. If you'd actually be happy re-sealing or reverting before every build, Phase 3 mostly disappears.
3. **When a Tier-1 lock is unlocked, when does it re-lock?** On window close? On Play? Never, until clicked? A timer? "Never" is honest but people forget; "on window close" is safe but surprising. My lean: never automatically, but the red **UNLOCKED FOR EDITING** banner persists and the browser shows unlocked assets distinctly, so it's visible rather than silent.
4. **Locked mode Play: do you also want it to block editing, or just to not show your edits?** Blocking is safer (nobody edits a draft they can't see the result of), permissive is less annoying. This is a taste call about how you actually work.
5. **Test mode per-user or per-project?** §12.2 recommends per-user. If you'd rather the whole team flip together, say so — it changes the banner's urgency.
6. **How aggressive should closure sealing be by default?** "Seal everything reachable" is correct but can pull in dozens of shared assets on the first use and make the feature feel heavy. "Seal only what I ticked" is lighter but produces partly-live seals by default. My lean: default to the full closure, always show it before committing.
7. **Should rolling auto-snapshots (§11.2) be on by default once the feature is enabled, or opt-in per asset?** On-by-default is the whole point of a safety net; per-asset means you only get it where you remembered to ask, which is never where you need it. My lean: on for everything, gitignored, capped at N.
8. **Does `LauminaryVersion` get absorbed (Phase 4) or left alone?** Absorbing makes "lock" mean one thing project-wide, which is worth a lot. It also means touching a mature, load-bearing system with a documented history of serialization landmines (`LauminaryVersion.cs:11-17`). Not urgent, but the answer determines whether Phase 2 should keep the seal API deliberately Launimator-shaped.

---

## 15. Summary of recommendations

- **Two features, not one.** Rolling snapshots + revert for the fiddling fear; sealing + enforcement for the release gate. Same storage substrate, same banner, nothing else shared.
- **Content swap on the same object** (§2a) is the only viable runtime mechanism, because every reference in the project is a direct SO pointer. Indirection is correct-but-unaffordable.
- **Flag in a GUID-keyed side-table** shaped like `LauTagLibrary`; **payload in a per-asset sidecar JSON** captured with `EditorJsonUtility.ToJson`, with a `[SerializeReference]` round-trip verification at seal time and a type manifest checked at restore time. **No reference remapping is needed** — measured 2026-08-25: `ToJson` already emits `{fileID, guid, type}`, so a seal is portable as written (§5.1).
- **A seal is a graph property, not an asset property.** Lock sets, closure capture, reverse-dependency index, and an orange banner on every unsealed dependency of a seal.
- **Never swap without the recovery sidecar written and flushed first.** SO edits persist through Play; a failed restore eats the draft.
- **Builds never honour Test mode.** Default the build to Verify (fail on drift) rather than Swap.
- **`Z.Banner` is a new ZUI control** with new `--zui-danger`/`--zui-warn`/`--zui-locked` variables — ZUI has no red at all today — inserted once at `ZuiAssetWindow.cs:123` to reach ~16 tools, with nine distinct, specifically-worded states.
- **Enforcement = `OnWillSaveAssets` backstop + disabled controls + one shared `LauEdit.Dirty` gate.** Do not attempt to touch all 472 dirty sites.
- **Under the recommended defaults, nothing ever swaps.** Tier 2 as configured out of the box seals, banners, reverts and blocks a drifted build — the substitution behaviour is an explicit opt-in in Phase 3. The tier table (§3) describes the capability; §8 and §12.1 describe what is actually on.
- **Phase 1 stands alone and should be built first**, and it contains the item with the best value-per-line in the whole document: rolling auto-snapshots with one-click restore.
- **Those three claims have now been measured** (§13.1, evidence in `D:\UNITY\Laubrary Dev\LAUASSET_LOCK_MEASUREMENTS.md`). Net effect: Phase 2 got **cheaper** (no reference remapping, no multi-object seal format) and §11.2 got **stricter** (dedup is no longer optional, because `Pyre` alone is 93% of the bytes). Nothing else in this document changed.
