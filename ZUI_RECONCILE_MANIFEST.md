# ZUI Reconciliation Manifest — Laubrary Dev (base) ← ZTracker

Input to **Step 1** of `ZUI_MIGRATION_PLAN.md`. Base = this project's `Assets/ZUI`. Source of the
missing showcase work = `ZTracker/.../Assets/ZUI`. Line counts captured 2026-07-04.

**This is a two-way merge.** Laubrary Dev is ahead on core/editor-window/forms/slider; ZTracker is
ahead on the showcase subsystem. Neither is a superset. Every "manual-merge" row must be diffed and
**compile + visually verified with Unity open** before it is committed.

## A. Files only in ZTracker → bring in (coupled showcase set)
These four move together; the two `Zhowcase*` partials and `ZUIEffectBaker` depend on ZTracker's
expanded `Zhowcase.cs`, so they **cannot** be folded onto Laubrary Dev's older 701-line `Zhowcase`.
Take ZTracker's `Zhowcase.cs` (1198L) as part of this set.

| File | ZTracker lines | Action |
|---|---|---|
| `Zhowcase.cs` | 1198 (vs base 701) | **Replace base** with ZTracker version, then re-apply any base-only edits (verify against §B). |
| `ZhowcaseAnimTypes.cs` | 137 | Add (partial of `Zhowcase`). |
| `ZhowcaseAnimations.cs` | 631 | Add (partial of `Zhowcase`). |
| `ZUIEffectBaker.cs` | 520 | Add (editor tool; depends on the above). |

## B. Shared files that differ — per-file merge decision
Delta = ZTracker − Laubrary Dev. Positive = ZTracker has more; negative = Laubrary Dev has more.

| File | LDev | ZTrk | Δ | Likely direction | Notes |
|---|---|---|---|---|---|
| `ZUIStyleEditorWindow.cs` | 6047 | 5701 | **−346** | keep LDev, port ZTrk deltas | Base is well ahead (Zeditor refactors). Diff for any ZTrk-only fixes. |
| `ZUIFormControls.cs` | 437 | 377 | −60 | keep LDev | Verify no ZTrk-only control lost. |
| `ZUIAssetLibrary.cs` | 344 | 298 | −46 | keep LDev | |
| `ZUIStyleDef.cs` | 1594 | 1556 | −38 | keep LDev | |
| `ZUISlider.cs` | 1775 | 1744 | −31 | **manual** | ZTrk carries bipolar-slider + `SliderFormatted` work (per ZTracker refactor doc). Confirm those landed in LDev or port them. |
| `ZUIDebug.cs` | 234 | 211 | −23 | keep LDev | |
| `ZUIColor.cs` | 585 | 576 | −9 | keep LDev | |
| `ZUIStyleSheetAsset.cs` | 359 | 351 | −8 | keep LDev | |
| `ZUISubDefs.cs` | 554 | 551 | −3 | keep LDev | |
| `ZUIToggle.cs` | 377 | 375 | −2 | keep LDev | |
| `ZUIColorRef.cs` | 60 | 62 | +2 | **manual** | tiny ZTrk-only addition — inspect. |
| `ZUIWindow.Draw.cs` | 533 | 541 | +8 | **manual** | ZTrk ahead — port. |
| `ZUIAnimation.cs` | 411 | 432 | +21 | **manual** | ZTrk ahead — likely showcase-anim related; port with §A. |
| `ZUI.cs` | 931 | 1006 | +75 | **manual (important)** | Core entry type. ZTrk +75L — likely showcase/anim API the §A files need. Merge carefully. |

## C. Files only in Laubrary Dev (keep as-is)
Not in ZTracker; base-only modern core — retain unchanged:
`ZUICore.cs`, `ZUIFlow.cs`, `ZUINineSlice.cs`, `ZUISheet.cs`, `ZUIEnvelopeEvaluator.cs`,
`ZUIValue.cs`, `ZUIValueControl.cs`.

## D. Not yet reconciled here
- **OutBurner / TrueEye (+21L over baseline)** and **Zounds (stale)** carry their own small deltas.
  After the base is finalized, diff each against it and capture any unique fixes before their rollout
  deletes the vendored copy. (Do not lose OutBurner/TrueEye's +21L blindly.)
- **S Som I Stella** (`Assets/Plugins/ZUI`, 23 files) is a deep old fork — treat separately.

## E. Verification gate for Step 1
After the merge, with Unity open on Laubrary Dev:
1. Compiles clean (no duplicate-type / missing-member errors).
2. **Zhowcase window opens and renders** every showcase section, including the new anim types and the
   effect baker.
3. `ZUIStyleEditorWindow` (Zeditor) opens and edits a sheet without regression.
4. Only then commit the reconciled `Assets/ZUI` as the canonical base.
