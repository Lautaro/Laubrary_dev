# T-0020 — Review of the clusters left out of the T-0017 commit sweep

**Reviewed 2026-08-25** against `D:\UNITY\Laubrary Dev`, branch `feat/lathe`, HEAD `4854ce00`. Read-only investigation; nothing was changed, deleted or committed while producing this document. Every factual claim below was checked twice, by two independent passes over the repo.

## Short answer to "is this still relevant?"

**Mostly no — but not because anyone decided it.** Every item T-0017 deliberately held back is now committed. Nothing is dangling, nothing is lost, and the one item with real breakage risk turned out to be harmless — for a reason that is worth reading, because it is not the reason the task assumed.

The catch: the exclusions were not resolved by a judgement call, they were **overrun by a blanket commit**. `2ee3fee1` "Baseline before Kiln→PyrePlus port framework work" (2026-08-20) committed **559 files in one go**, which swept in the agent-tooling state, the unclear-ownership content and all three suspicious GUID changes together. So "commit, delete or move?" got answered as "commit" for nearly all of it, by accident rather than by choice.

That leaves exactly one live decision: whether to now remove the handful of items that genuinely are debris. Everything else is settled and correct.

## Per-item verdicts

### Cluster 1 — verification-blocked work

| Item | Status |
|---|---|
| Zoe MotionState / AnimationArbiter pipeline (`d8b5cdf5`) | Committed, ancestor of HEAD. Reported as never walked on screen by a human. |
| Pyre CherryFraming (`d5c020d3`) | Committed, ancestor of HEAD. Reported as having no real mouse drag-and-drop test, and no runtime playback. |

Both commits exist and are safely in `feat/lathe`'s history — the T-0017 SHAs were accurate, and both are confirmed ancestors of HEAD.

The verification gap itself is **not repo-checkable** — it comes from the project memory notes written 2026-08-15, and there is no evidence in the repository either way. What can be confirmed is that no AgentHQ task currently covers either walk, which is consistent with the gap still being open.

**Recommendation:** these are the only items here with real remaining value. A Handover Walk on each is worth scheduling — but that is ordinary follow-up work, not repo hygiene, and it does not belong to T-0020.

### Cluster 2 — the three suspicious GUID-only `.meta` changes

**Verdict: no breakage, nothing to fix, do NOT restore the old GUIDs.** All three were Unity correctly repairing duplicate-GUID collisions.

All three changed in the same commit `2ee3fee1`, which contains **exactly three** GUID changes across all 559 files — so the original detection was precisely scoped.

The cause is the same in all three cases: **a second file already held the same GUID, and Unity regenerated one of the colliding pair on import.** This is the classic result of duplicating an asset together with its `.meta`.

- **`ReactionFx.cs.meta`** — `7614360e…` → `4d70169e…`. The old GUID is still live, as the defining GUID of `_Quarantine\ZoeEventSystem\ZoeEvent.cs.meta`.
- **`ReactionFxPlayer.cs.meta`** — `762f0efd…` → `96fd2957…`. The old GUID is still live, as the defining GUID of `_Quarantine\ZoeEventSystem\ZoeEventPlayer.cs.meta`.
- **`Assets/Pyre/New Pyre.asset.meta`** — `cba7608d…` → `a9119707…`. Verified at `2ee3fee1^`: **both** this file and `Assets\Pyre\Small Explosion.asset.meta` held `cba7608d…`; at `2ee3fee1` only Small Explosion still does. The file has since been deleted outright by `60309923` (the Pyre rename), so there is no longer a `.meta` to restore anything to.

**Why nothing broke.** Verified by grep over `Assets/`, `Packages/` and `ProjectSettings/`, and by `git log --all -S<guid>` for each GUID: no scene, prefab or asset has *ever* referenced any of these GUIDs, in any commit on any ref. Two independent reasons why:

- `ReactionFx` is a plain `[System.Serializable]` class (`ReactionFx.cs:102-103`), not a `MonoBehaviour` or `ScriptableObject` — nothing can address it by script GUID at all.
- `ReactionFxPlayer` *is* a `MonoBehaviour` (`ReactionFxPlayer.cs:16`), so this was the genuine risk. It survives because the component is never authored into a scene or prefab — it is only ever attached at runtime, at `Assets\Packages\Laubrary\Runtime\Zoetrope\ZoeSpawner.cs:91` and `Assets\Packages\Laubrary\Runtime\Mirage\MirageSubject.cs:286`, both via `AddComponent<T>()`, which resolves by type rather than by serialized GUID. Its *new* GUID likewise appears in no file except its own `.meta`, confirming it is unauthored today.

**Do not restore the old GUIDs.** For the two scripts it would re-create a live collision with the quarantined copies; for `New Pyre.asset.meta` there is nothing left to restore.

**One thing to watch:** the collision is dead only because `_Quarantine/` sits **outside** `Assets/`, where Unity cannot see it. If that folder is ever moved back under `Assets/`, both GUID collisions come straight back.

### Cluster 3 — debris

| Item | What it actually is | Verdict |
|---|---|---|
| `Assets/_Recovery/` | 12 files, 314 KB — 6 anonymous crash-recovery scenes (`0.unity`, `0 (1).unity` … `0 (5).unity`). No filename matches any real scene. **Not** swept in by the blanket commit: half of it has been tracked since February 2026 (`0.unity` since `d2206a64`, 2026-02-27); only `0 (3)`–`0 (5)` arrived with `2ee3fee1`. | **Debris — safe to delete** |
| `Screenshots/` | 3 PNGs, ~1.0 MB. One (`pyreplus_window_real.png`) is the probe script's only output; the other two come from other capture runs. | **Debris — safe to delete** |
| `Assets/_Scratch.meta` | Orphan. `Assets/_Scratch/` exists, is **empty**, and is gitignored (`.gitignore:82`) — but its `.meta` sidecar got committed. | **Debris — safe to delete** |
| `_Quarantine/` | 13 files, 218 KB, at the repo root **outside `Assets/`**, so Unity never compiles it. No `.asmdef`. Confirmed unreferenced — the only mention of `ZoeEventPlayer` anywhere under `Assets/` is one prose comment at `Runtime\Zoetrope\Locomotion.cs:37`. | **Leave — inert, a separate open decision, and see the GUID warning above** |
| `.agenthq/` | 186 tracked files, 7.8 MB — the live task tracker, including this task. | **Keep tracked** — it is the durable audit trail |
| `.codex/` + `AGENTS.md` | Not debris. `.codex/config.toml` declares `[mcp_servers.coplay-mcp]` for **Codex CLI**, and `AGENTS.md` is Codex's equivalent of `CLAUDE.md`. Deliberate cross-tool support. | **Keep tracked, but see below** |

**`AGENTS.md` is stale, not merely a copy.** It differs from `CLAUDE.md` by 14 lines, and is missing exactly the two newest sections: *"Pyre — the rename is DONE, there is no PyrePlus"* and *"Preview overlays — an effect draws its own"*. A Codex session reading `AGENTS.md` would not receive the PyrePlus-naming rule at all. Worth re-syncing if Codex CLI is still in use.

### Cluster 4 — "unclear ownership"

**This cluster's premise was wrong on three of four items.** They all belong here:

- **`Assets/ZoundsData/` + `Assets/ZoundsProject.json`** — genuine Zounds authoring data in the tool's own documented default location. `Runtime\Zounds\ZoundsProject\ZoundsProject.cs` (lines 80–85) hardcodes `Assets/ZoundsData/{SystemFiles,Library,Sources,Themes}`, and `Editor\Zounds\ZoundsWindow.cs:233` creates `Assets/ZoundsProject.json` by name. **Keep.**
- **`Assets/AddressableAssetsData/`** — not an unused feature. `com.unity.addressables` `2.9.0` is declared in `Packages/manifest.json`, exactly 16 `.cs` files use `UnityEngine.AddressableAssets` and **all 16 are under `Runtime\Zounds\` or `Editor\Zounds\`**, and the folder contains a `Zounds Default Local Group.asset`. It is live config backing real code. **Keep.**
- **`Assets/SpriteSheets/BigCity_3.png`** (+ `BigCity_3.png.regionslicer.json` — note the real filename is `.png.regionslicer.json`) — no scene, prefab or asset references it; the only thing carrying its GUID is its own RegionSlicer sidecar. But it sits with three similarly-unreferenced tracked siblings (`Gunstar Super Heroes - Blue.gif`, `NewSheet.png`, `Saint Dragon.png`) in what is evidently a raw-art staging folder for Launimator's RegionSlicer. **Leave — treat as WIP art, not debris**, unless the whole staging folder is being pruned.
- **`Assets/Editor/CoplayProbe.cs`** — the one item in this cluster that *is* what the task suspected. An `EnumWindows` + `PrintWindow` P/Invoke scratch script that screenshots any window whose title contains "Laubrary Dev" and writes it to `Screenshots/pyreplus_window_real.png` (line 69). No `[MenuItem]`, and **no in-repo caller of any kind** — nothing under `Assets/` references `CoplayProbe`. It sits loose in `Assets/Editor/` rather than under any Laubrary tool convention. **Debris — safe to delete.**

## The one decision left

Four things are genuine debris and nothing references any of them: `Assets/_Recovery/`, `Screenshots/`, `Assets/Editor/CoplayProbe.cs` and `Assets/_Scratch.meta`. With the folder `.meta` sidecars that would otherwise be left orphaned, that is **20 tracked files, about 1.35 MB**.

They were **not** deleted as part of this review, deliberately: this repo lost 11 days of work to an over-eager git operation on 2026-08-16, the task itself asks for a human decision per item, and deleting assets under a possibly-open Unity editor triggers reimports. All of it is recoverable from git history in any case.

To remove them in one go:

```
cd "D:\UNITY\Laubrary Dev"
git rm -r --quiet "Assets/_Recovery" "Assets/_Recovery.meta" "Screenshots" "Assets/_Scratch.meta"
git rm --quiet "Assets/Editor/CoplayProbe.cs" "Assets/Editor/CoplayProbe.cs.meta"
git commit -m "Remove crash-recovery dumps, probe screenshots and the Coplay screenshot probe"
```

All six paths were verified to exist and be tracked. `Assets/_Recovery.meta` is included on purpose — dropping a folder's contents while keeping its `.meta` leaves exactly the orphan-`.meta` condition this document calls debris. Close Unity first, or let it reimport afterwards.

**`Assets/Editor.meta` deliberately left alone.** At the time of the first check, `CoplayProbe.cs` was the only tracked file in `Assets/Editor/`, so its `.meta` looked like it should go too. On re-check, another task had added `Assets/Editor/_T0087Probe.cs` to that folder. Delete `Assets/Editor.meta` **only** if `Assets/Editor/` is genuinely empty at the time — check first with `git ls-files "Assets/Editor/"` and `ls "Assets/Editor"`.

## Two things found en route, outside T-0020's scope

1. **`60309923` deleted three `New Pyre`-family assets outright rather than renaming them** — `Assets/Pyre/New Pyre.asset` (2,611 lines), `Assets/Pyre/Small Explosion.asset` (1,444 lines) and `Assets/Demos/PyreDemo/New Pyre.asset`, each with its `.meta` (six deletion entries). Git finds no rename target for any of them even with `--find-copies-harder`. Nothing referenced them by GUID, so nothing broke — but if any were wanted, all are recoverable from `60309923^`. Worth confirming the deletion was intended as part of the PyrePlus→Pyre consolidation.

2. **The PyrePlus→Pyre rename left a lot of "Plus" behind.** 64 tracked paths under `Assets/` still contain `" Plus"`, **54 of them in `Assets/Pyre/Imported/`** (`Bars Tentacle Plus.asset`, `Blob Explosion Plus.asset`, `Curl Plus.asset`, … 27 assets plus their metas). Several `PyrePlus`-named paths also survive outside `Assets/Pyre/`: `Runtime/PyrePlus.meta`, `Editor/PyrePlus.meta`, `Assets/Demos/PyrePlusDemo.meta`, `Assets/Demos/PyreDemo/PyrePlusDemo/`, `Assets/Dev/PyrePlusPlayback3D.meta`, `PYREPLUS_DESIGN.md` and `PYREPLUS_ADVANCED_DESIGN.md`. Given the project rule that there is no "PyrePlus" any more, this is a real loose end rather than a cosmetic one. Separately, `Assets/Pyre/New Pyre Plus§.asset` carries a literal `§` (U+00A7) in its filename, which looks like a typo.

## Also worth flagging

`feat/lathe` has **no upstream configured** — `git config branch.feat/lathe.remote` and `.merge` are both unset, and `origin/feat/lathe` does not resolve. Everything on this branch, including all 15 bulk-safeguard commits, is unpushed as far as this checkout knows. Given that the whole point of the safeguard series was protecting against another data-loss event, that safety net currently stops at the local disk. (For contrast, `master` tracks `origin/master` and is 112 ahead.) Network was unavailable from the investigation sandbox, so whether a `feat/lathe` branch exists server-side could not be confirmed either way.

## Not verified

- Whether the *contents* of the `Assets/_Recovery/` crash scenes duplicate any live scene. Only their filenames were compared.
- The human-verification status of the two Cluster 1 items — that comes from project memory, and the repository holds no evidence either way.
- Whether `feat/lathe` exists on the remote, per the note above.
