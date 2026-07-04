# ZUI → Laubrary Migration Plan

**Status:** In progress — ZUI moved into the package + runtimes merged (v0.1.0). Consumer rollout pending.
**Canonical base:** this project (`Laubrary Dev`) — taken **as-is**, no reconciliation with forks.
**Date started:** 2026-07-04.

### Revised strategy (2026-07-04)
Do **not** reconcile the diverged forks now. `Zounds`, `ZTracker`, and `S Som I Stella` keep their own
flavour of ZUI. We make Laubrary.ZUI the shared, developable home from Laubrary Dev's current ZUI, roll
it out to Laubrary/ZUI consumers, and **later** assimilate anything worthwhile from the forks and merge
Laubrary.ZUI back into them. `ZUI_RECONCILE_MANIFEST.md` is therefore **deferred**, not part of this pass.

### Progress
- [x] **Move ZUI into the package** — `Assets/ZUI` → `Assets/Packages/Laubrary/Zui` (intact; SystemAssets
      travel with it). Install-path auto-detection verified resolving to the package; editor sheet loads.
- [x] **Merge the two runtimes** — data layer (`ZUI.Runtime`) + drawing (`ZuiRuntime`) → single assembly
      `com.Lautaro-Arino.Laubrary.ZuiRuntime`. `ZUI.Editor` + Choreographer repointed. Compiles clean.
- [x] Package bumped to **0.1.0** + CHANGELOG.
- [ ] Visual smoke test (Zhowcase gallery + Zeditor render) — **needs an eyeball in the Editor**.
- [ ] Optional: rename `ZUI.Editor` → `com.Lautaro-Arino.Laubrary.Zui.Editor` for naming consistency.
- [ ] Optional: code-level dedup of `ZuiScale`↔`ZUISpacingScale`, `Zui` primitives↔`ZUICore`.
- [ ] Roll out to Laubrary consumers (Template first, then ClaudeUI/OutBurner/TrueEye/RiskyRemake/3DLab/TinyWar).

---

## 1. Goal

Move the **entire ZUI framework** (editor toolkit + runtime) out of every project's vendored
`Assets/ZUI/` and into the **Laubrary package** as first-class assemblies, so ZUI ships and versions
with Laubrary instead of being hand-copied per project. Along the way, **merge the two "runtime"
assemblies** into one and share as much as is productive between the runtime drawing toolkit and the
editor toolkit.

## 2. Current state (as investigated 2026-07-04)

### Two things both called "runtime" — they are complementary, not duplicates
| Assembly | Location | Contents | Namespace | Role |
|---|---|---|---|---|
| `ZUI.Runtime` | `Assets/ZUI/Scripts/Runtime` (19 files) | **data/config types**: `ZUIStyleDef`, `ZUISheet`, `ZUIPalette`, `ZUIColor(/HSL/Ref)`, `ZUIAutoColor`, `ZUISpacingScale`, `ZUINineSlice`, `ZUIEnvelope*`, `ZUIValue`, `ZUICore`, `ZUIFlow`, `ZUIAssetLibrary`, `ZUIIconLibraryAsset`, `ZUIStyleSheetAsset`, `ZUISubDefs` | global (`ZUI`) | data layer the editor toolkit renders; ships in build |
| `com.Lautaro-Arino.Laubrary.ZuiRuntime` | `Packages/Laubrary/Runtime/ZuiRuntime` (10 files) | **immediate-mode drawing**: `Zui`, `ZuiStack`, `ZuiMenu`, `ZuiPanels`, `ZuiOverlay`, `ZuiStyles`, `ZuiScale`, `ZuiFaceButtons`, `ZuiGamepad`, `ZuiAudit` | `ZuiRuntime` (type `Zui`) | in-game HUD/overlay drawing |

### The editor toolkit
- `ZUI.Editor` assembly, ~28 editor `.cs`, **~21k–22.6k lines** depending on variant.
- Editor-only; references `ZUI.Runtime`.

### Divergence — the single biggest risk
ZUI has fractured into ~6 variants with **no single source of truth**:

| Variant | Files / lines | Projects |
|---|---|---|
| Baseline | 42 / 21,206 | ClaudeUI, 3DLab, AssetScavenge, RiskyRemake, TinyWar, Template |
| Ahead +21L | 42 / 21,227 | OutBurner, TrueEye |
| **Laubrary Dev** (chosen base) | 46 / 22,234 | this project — newest core (`ZUICore`, `ZUIFlow`, `ZUINineSlice`, `ZUISheet`), ahead on editor window/forms/slider |
| ZTracker | 42 / 22,602 | historical proving-ground — ahead on **showcase** (`Zhowcase` +497, `ZUI.cs` +75) + coupled overhaul (`ZhowcaseAnimTypes`, `ZhowcaseAnimations`, `ZUIEffectBaker`) |
| Zounds | 39 / 20,752 | stale back-port target |
| S Som I Stella | 23 / 12,520 | old fork under `Assets/Plugins/ZUI` |

**Laubrary Dev ↔ ZTracker is a two-way merge, not a fast-forward.** See `ZUI_RECONCILE_MANIFEST.md`.

## 3. Target architecture

Keep type names and namespaces **identical to today** so consumer code (`ZUI.*`, `ZuiRuntime.Zui.*`)
compiles unchanged after swapping the vendored folder for the package. This is the key to a cheap rollout.

```
Packages/Laubrary/
  Runtime/Zui/                 asmdef: com.Lautaro-Arino.Laubrary.Zui         (ships in build)
    Data/     <- absorbs ZUI.Runtime data/config types (global type `ZUI` kept)
    Drawing/  <- absorbs ZuiRuntime (namespace `ZuiRuntime`, type `Zui` kept)
  Editor/Zui/                  asmdef: com.Lautaro-Arino.Laubrary.Zui.Editor  (Editor-only, refs ...Zui)
    <- the ~28-file IMGUI toolkit (ZUIStyleEditorWindow, ZUISlider, Zhowcase*, ZUIEffectBaker, ...)
```

Naming follows existing package convention (`com.Lautaro-Arino.Laubrary.UIAudit`,
`...LaubraryTicker`). `UIAudit`'s IMGUI section already depends on `ZuiRuntime`, so it rides along.

### The two-runtime merge — dedupe candidates
Merging `ZUI.Runtime` (data) + `ZuiRuntime` (drawing) into one runtime assembly. Genuine overlaps to
reconcile rather than ship twice:
- `ZuiScale` (font-based UI scaling) vs `ZUISpacingScale`
- `Zui` fill/texture primitives (`White`, disc) vs `ZUICore`
- `ZuiStyles` vs `ZUIStyleDef` / `ZUISheet` color+style handling

**Name-collision trap** (already documented in `ZuiRuntime.cs`): a namespace segment named `ZUI`
collides with the global type `ZUI`. Resolution: keep the global type `ZUI` (data + editor) and keep
the drawing toolkit under namespace `ZuiRuntime` (type `Zui`). Do **not** introduce a `ZUI` namespace.

## 4. Consumer rollout

Laubrary is a **local embedded package** (`"file:com.lautaro.arino.laubrary"`) — every project holds
its **own physical copy** at whatever version it last synced. Current spread:

| Laubrary version | Has ZuiRuntime | Projects |
|---|---|---|
| 0.0.19 (canonical) | yes | Laubrary Dev |
| 0.0.18 | yes | ClaudeUI, OutBurner, TrueEye, RiskyRemake |
| 0.0.16 | no | 3DLab, TinyWar, Asteroid+ |
| 0.0.14 | no | Template, AssetScavenge (retired) |
| none (ZUI-only) | — | Zounds, ZTracker, S Som I Stella |

Per-project game-code dependence (files referencing the API, outside `Assets/ZUI`):
`ZUI.*` → 26–29 files each; `ZuiRuntime`/`Zui.*` → TrueEye 13, ClaudeUI 4, rest 0.

**Rollout per project:** delete vendored `Assets/ZUI/` (and any vendored `ZuiRuntime` copy) → bump the
package copy → repoint game asmdefs that referenced `ZUI.Editor`/`ZUI.Runtime` at
`com.Lautaro-Arino.Laubrary.Zui[.Editor]` → reimport → compile-check. Type names unchanged ⇒ no call-site edits expected.

**Template first** — new projects clone it, so it must land on the migrated package with no `Assets/ZUI`.

## 5. Ordered steps (each gated on compile)

1. **Reconcile editor ZUI into this project's `Assets/ZUI`** (the gate). Two-way merge per
   `ZUI_RECONCILE_MANIFEST.md`. Fold ZTracker's showcase overhaul (`Zhowcase` rewrite +
   `ZhowcaseAnimTypes`/`ZhowcaseAnimations`/`ZUIEffectBaker` as a coupled set) onto Laubrary Dev's
   newer core. **Requires Unity open + visual check of the Zhowcase window.**
2. **Merge the two runtimes** into `Packages/Laubrary/Runtime/Zui` and dedupe the overlaps (§3).
3. **Move the editor toolkit** into `Packages/Laubrary/Editor/Zui` with the new asmdef.
4. **Delete vendored `Assets/ZUI/`** from Laubrary Dev; confirm the package builds & Zhowcase renders.
5. **Bump package to 0.1.0**, update CHANGELOG.
6. **Roll out**: Template first, then each consumer (§4), each compile-checked.

## 6. Risks & mitigations
- **Blind merges without compile = death spiral.** Every step above is gated on Unity being open on
  the affected project. No source merge is committed unverified.
- **Dirty working trees.** Laubrary Dev currently has a large uncommitted delta (9-slice Mirror work,
  Zoetrope, package.json…). Commit/stash that to a clean baseline **before** step 1.
- **Duplicate-type collisions during transition.** While `Assets/ZUI` and a package `Zui` assembly
  both exist in one project, `autoReferenced` global types collide. Sequence the move so only one
  copy of each type is present per compile (delete-then-add, or `autoReferenced:false` bridge).
- **Diverged consumers silently regress.** OutBurner/TrueEye (+21L) and Zounds (stale) differ from the
  base; after rollout, spot-check each project's ZUI-drawn UI, not just that it compiles.

## 7. Open decisions
- Final runtime assembly name: keep `...ZuiRuntime` or rename to `...Zui`? (rename = cleaner, costs an
  asmdef-reference update in the 4 projects already on 0.0.18).
- Do we retire the `S Som I Stella` Plugins/ZUI fork or migrate it too?
- Zounds and ZTracker have no package yet — add Laubrary as a dependency during their rollout, or leave
  them ZUI-consumers via the package only?
