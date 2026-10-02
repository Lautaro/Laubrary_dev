# Laubrary UI separation — shared foundations

30 September 2026. Continuation of the accepted planning draft and completed phases 0–1.

The first shared-foundation rollout is implemented and verified in the independent refactor project. No decision was needed to proceed. The original development project and its editor remain outside this refactor; only its shared AHQ board receives status and attachments.

## What changed

Ordinary fields now receive their default dimensions from stylesheets: numeric inputs, sliders, text and object fields, dropdowns, colour fields, curves, gradients and the standard numeric range. Spacing and icon defaults follow the same rule. Existing calls that deliberately supply dimensions keep working and retain their precedence.

Cards, section headers, generated fields and the shared asset-browser shell now expose more named presentation parts. Their fixed layout and appearance moved into stylesheets while editing, folding, filtering, selection, saving and Undo remain in code.

The responsive column layout now takes its preferred width and gutter from optional style properties. The existing ordering and balancing algorithm stays intact. Removing a tool override restores the caller's original preferred width and the default gutter.

Your parent-tag requirement is demonstrated directly: a normal control gets the shared default; placing it under a tagged tool window changes its width and layout through descendant selectors. Another window remains unchanged. Removing the tag restores the exact original appearance.

Most existing popovers already live beneath their tool window and inherit its presentation naturally. The fallback for a bare host now receives the complete set of default sheets and its owner's tool tag. A separate-root presentation snapshot is also available for future detached windows. It can explicitly carry a legacy skin class. Each snapshot belongs to one surface; there is no global active skin. A detached snapshot is intentionally fixed at opening time, so a later change to its owner takes effect when the surface is reopened with a new snapshot.

## Evidence

| Check | Result |
| --- | --- |
| Frozen foundation reference versus candidate at 420, 720 and 900 logical pixels wide | Zero changed UI pixels at every width. |
| Parent-tag override at 720 pixels | Candidate field widths, gutter and column count changed; reference stayed at its defaults. |
| Remove that parent tag | Returned to zero changed UI pixels. |
| Real BackSplash editing view and asset-library view versus frozen reference | Zero changed UI pixels in both states. |
| Existing representative pilot, normal and Colorful appearance | Zero changed UI pixels after reopening into matching interaction states. |
| Existing pilot interaction regression | All 14 assertions passed. |
| Foundation interaction checks | Numeric edits reached the visible preview; Undo restored data; cards and sections folded; explicit caller widths survived; popovers opened and closed. |
| Two independent presentation snapshots | Each retained its own width override; no arbitrary state classes or duplicate sheets leaked across roots. Explicit legacy skin selection was retained. |
| Bare-host popup | Received all foundation sheets and the tool tag; Escape dismissed it. |
| BackSplash workflow, twice from empty selection | Browser double-click opened the fixture; colour editing changed the visible preview and saved asset; Undo restored it. A cold reopen restored selection and saved colour without explicitly assigning the asset again. |
| Compilation and source guard | Current assemblies loaded in the isolated editor; no failed final compiler jobs; presentation guard and its deliberate-failure self-test passed. |

Screenshot checks use this workstation's 225% display scale, exact pixel comparison, matching dimensions and nonblank captures. Only the top 60 physical pixels of Unity's window-tab chrome are excluded. The harness rejects overlapping reference/candidate window positions, because those can cause a native capture helper to select the same window twice. Frozen control sources are checked against the pre-foundation commit, allowing only namespace/import and blank-line adaptations. Reference helper calls are frozen too.

**Verified by probe:** the table's layout, edit, Undo, browser event, popup, scope-isolation, persistence and compilation checks. Browser actions used the real control event route; numeric and colour edits used field callbacks. These are not claims of physical mouse/keyboard testing.

**Verified by eye:** normal, narrow, wide and overridden foundation views; the real browser and editing view; the visible blue-to-orange preview change and restored state. The UI-guide compliance pass ran over the changed surfaces. It caught a blank icon in the new comparison fixture, which was replaced with a real menu glyph. Production appearance was preserved rather than redesigned.

**Not verified:** every consuming tool, every branch/worktree, another display scale or operating system, a clean downstream package installation, native colour/curve picker workflows, all keyboard routes, player builds or runtime UI. The two workflow walks cover the shared browser through BackSplash; they do not certify every tool that inherits that browser.

## Boundaries and remaining work

This is the first shared-foundation rollout, not completion of all Laubrary UI separation. Explicit caller widths and older painter/constructor metrics remain compatibility paths. Some still have historical defaults and must be migrated with their respective control families. The legacy immediate-mode asset picker is also still a later renderer-migration task. Complex timeline/lane controls, remaining tool composition, legacy windows and runtime UI are not declared complete.

The new source guard records specific remaining inline presentation occurrences with reasons and multiplicity. It rejects new or changed occurrences and expired exceptions. It is a review aid, not proof that every possible painter or style API has been found.

Further rollout remains authorised in the isolated project; there is no new approval gate. Preserve current appearances while moving tool-specific compatibility arguments into their tool stylesheets, and continue whole-tool verification as each family migrates. Do not merge into the original project merely because this wave passes.

## Where to find it

- Project: `D:/UNITY/Laubrary Dev - UI Separation`.
- AHQ group: **UI separation - shared foundations**, tasks **T-0530–T-0536**.
- Live comparison menu: **Laubrary → UI Separation Foundations**.
- Reproducible probes, frozen references, technical authoring notes and screenshots: `Documentation/UISeparation`, with this wave's images in `Evidence/Foundations`.
- This same report is attached to the AHQ integration task and copied to the synced Drive folder.
