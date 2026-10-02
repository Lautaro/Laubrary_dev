# Phases 0 and 1 acceptance — 30 September 2026

The isolated representative pilot is accepted. This is not acceptance of the remaining Laubrary-wide migration or a consumer-package release.

## Environment and reference

Project: `D:\UNITY\Laubrary Dev - UI Separation`. Unity 6000.3.10f1, Windows DX11, editor scaling 2.25 pixels per UI unit. Frozen starting commit: `9eb3d9c`. Original working assets were physically copied; this is not a linked worktree. No refactor was applied to the original project.

Eight original controls and their original sheets/textures are retained independently in the comparison harness. Reference texture import settings match the source while their GUIDs remain independent. Comparing compressed default-import reference textures with uncompressed original-import candidate textures initially produced a false Colorful discrepancy; matching those settings removed it. An accidental second envelope frame introduced by an existing selector was also found and fixed rather than tolerated.

The strict comparison checks equal dimensions and rejects uniform images. It compares RGBA channels without resizing, registration, fuzzy masks or tolerance. The top 60 physical pixels are excluded because they are Unity's window-tab chrome, including different baseline/candidate titles and active-tab state. All application controls, toolbar, text, curves and preview pixels are included. An unchanged baseline captured twice is identical, establishing zero measured capture noise in this setup.

## Verified by probe

- Unity compilation completed without errors. Both frozen and current trees survive a script reload.
- Default and Colorful comparisons at 420, 620 and 900 UI-unit widths have zero differing content pixels. Numeric-entry and empty-band/envelope captures also match exactly. The scoped-override capture intentionally differs.
- Fourteen interaction assertions pass: slider drag/capture and Undo, Shift fine adjustment, default reset, band interpolation across skipped bars, untouched out-of-range values, band Undo/reset, range-thumb movement and Undo, envelope point movement and Undo, toggle action and Undo. The suite also passes while tool-scoped width, padding and thumb metrics are active.
- Additional checks cover interval panning with fixed span and Undo; numeric value-change entry and Undo; same-instance skin switching; no fixture-data changes from styles; close/reopen persistence; and independent baseline/candidate reconstruction after a domain reload.
- Adding the candidate parent class changes width 150→190, band gap 2→8, thumb width 12→18 and envelope left padding 6→18. Removing it restores all four. A separate caller-fallback case uses padding 11 without the profile, resolves to 18 under the override, then returns to 11 when removed.
- The baseline root stays at its original width and palette while the candidate root changes. Styling remains scoped to its own UI tree.
- Source review and diff checks cover all modified controls. The register separates 1,214 source files, 164 lexical surface candidates and 102 family classifications from actual live verification. Immutable Git-blob identities distinguish source changes from checkout line endings.

Probe bodies and raw structured results live beside this record and in `Evidence/`. `comparison-summary.json` and per-case comparison JSON retain dimensions, hashes and measured differences. The Python comparison helper was independently checked with known synthetic pixel differences and a size-mismatch rejection.

## Verified by eye

The coordinator inspected default, Colorful, narrow, wide, numeric-entry, empty-state and scoped-override screenshots. Labels and values are legible, the parameter row wraps, range fields fit their numbers, custom painters remain inside their bounds, and the altered parent style affects the intended candidate only. No unexplained appearance change was accepted.

The Handover Walk was repeated from reopened windows: open the pilot, adjust Amount and observe the preview, toggle Enabled, edit a range/bands/envelope, undo, reset, change presentation, resize and return. The actions were driven through live UI event/value-change automation and checked against rendered output. The second pass included changed tool-scoped metrics and re-entry. The UI-guide compliance pass covered labels/tooltips, numeric affordances, wrapping, discoverability of the skin/override/width controls, persistent editing state, Undo and a visible preview outcome. The pilot's display preferences are isolated from production controls.

## Not verified / outside this acceptance

Physical hardware keyboard entry and every contextual-menu gesture were not exhaustively replayed. Other display scales, other operating systems, consumer projects, runtime UI, every legacy editor, every popover and all real Zounds content states are later rollout work. The general-toolkit window mode retains the existing Zounds skin for the three skin-only controls because no historical general-toolkit look exists for those controls. It does not invent a new visual design.

Existing explicit width arguments, caller geometry and envelope-definition objects remain documented compatibility fallbacks. They do not prevent an optional USS override for the migrated presentation adapters, but broader call-site cleanup is still required in later phases. A new detached popup root must receive its presentation context explicitly. The surface register is a source-based planning register with unknowns retained, not a claim that 164 windows were opened and audited.

## Reproduction

Open the copied project and select `Laubrary/UI Separation Pilot`. Use Colorful, Tool override and Width in the toolbar. Edit `PilotOverride.uss` for the designer exercise, and `PilotLayout.uxml` for the outer composition. Neither requires changing the controls' C# implementation. The shared fixture is synthetic and resettable.

Run the checked-in `run_probe.ps1` with a probe name to execute a saved snippet against the explicitly pinned copied project. Import source changes before probing, allow layout to settle between mutations and captures, and do not use Unity Test Runner. Run `archive_evidence.ps1` after the documented captures to reproduce the strict comparison summary. Do not refreeze or regenerate the reference to make a comparison pass.
