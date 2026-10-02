# Laubrary UI separation — phases 0 and 1

30 September 2026. First implementation milestone; a basis for the next planning conversation.

The baseline and representative pilot are complete in a separate physical copy of the project: **D:\UNITY\Laubrary Dev - UI Separation**. The original project remains available for parallel development. The copy has its own Git history and an immutable starting snapshot.

## What now works

A normal control receives its default presentation from shared USS. A tool can add a parent class and provide descendant rules that change only the controls inside that window. This is the CSS-like behavior you requested: the controls do not contain branches for particular tools. Removing the parent class restores the normal presentation.

The pilot covers buttons, toggles, compact sliders, two-value ranges, band sliders, an editable envelope and a row that wraps at different widths. It also includes a small UXML shell with content and preview slots, so outer composition has a concrete designer-editable example.

For custom-drawn controls, the drawing and pointer calculations now read the same resolved presentation values. That matters when a designer changes a thumb's size or a curve's padding: the visible control and its hit area must continue to agree. Existing caller-supplied settings remain as compatibility defaults while the broader tools are migrated.

## What was checked

The comparison uses independently frozen original controls, styles and textures. Default and Colorful presentations match the reference exactly at narrow, normal and wide sizes. Numeric-entry and empty-state examples also match. The comparisons use zero pixel tolerance, exclude only Unity's title-tab strip, and include the complete application content. Repeating an unchanged reference capture produced no difference.

The parent-tag exercise changes the candidate's width, colour, band spacing, range-thumb size and envelope padding while the baseline stays unchanged. Removing the tag restores the defaults. A separate test also restores a caller's custom fallback rather than incorrectly restoring a universal constant.

Live checks cover dragging, fine adjustment, reset, range editing, painting across bands, envelope editing, numeric value changes and Undo. The drag/Undo checks also pass with the altered dimensions. Reopening and script reload preserve the fixture, and changing the skin preserves the actual bound controls. I inspected the rendered results and repeated the operating walkthrough.

These are editor probes and screenshot checks on this Windows setup at 225% scaling. They are not a claim of exhaustive physical-keyboard testing, other-platform coverage or acceptance of every Laubrary tool.

## The planning foundation

The source register screens 1,214 files and records 164 possible surfaces across 102 family entries. These are explicitly source-derived candidates, not 164 independently opened and verified windows. Every family has a route: separate existing Toolkit presentation, decide a legacy port/adapter boundary, plan runtime treatment, or retain it in the confirmation queue. Unknown and apparently UI-free families were not silently excluded.

The copy also contains the presentation contract, selector/token authoring guide, compatibility exceptions, baseline hashes, reproducible probes and before/after images. The original frozen snapshot is commit `9eb3d9c`.

## AHQ organization

The Laubrary Dev board has the task group **UI separation - phases 0 and 1**:

| Task | Result |
| --- | --- |
| T-0523 | Independent project copy, frozen baseline and presentation contract |
| T-0524 | Surface inventory and family migration register |
| T-0525 | Shared standard-control presentation and semantic tags |
| T-0526 | Range/band separation and resolved presentation metrics |
| T-0527 | Envelope presentation adapter and USS profile |
| T-0528 | Responsive comparison pilot and designer exercise |
| T-0529 | Integration, visual/interaction acceptance and handover |

The bounded inventory, control migrations and harness work were delegated to GPT-5.6 Terra workers. I retained architecture, integration and editor acceptance. Review caught and corrected compilation, selector collision, reference texture import, dragging/rebuild and state synchronization issues before acceptance.

## How to try it

Open the copied project and choose **Laubrary → UI Separation Pilot**. Two windows show the frozen and current controls. Drag Amount to change the preview, use Undo, and try Colorful, Tool override and Width in the toolbar. Reset restores the synthetic fixture. The windows do not edit your sound assets.

The next implementation scope is the shared foundations and remaining call sites from the original plan. This milestone does not yet refactor every tool or runtime UI, remove all legacy presentation arguments, or apply Colorful across Laubrary. It establishes a tested pattern for that work while preserving the existing look.
