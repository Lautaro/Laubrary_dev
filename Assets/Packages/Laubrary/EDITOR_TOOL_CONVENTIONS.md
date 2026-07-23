# Laubrary editor tool conventions

The canonical rulebook for building IMGUI editor tools (ZUI-based windows, property drawers, inspectors) now lives in the global Claude Code skill, not here:

**`~/.claude/skills/laubrary/references/ui-layout-rules.md`**

That file is shared by every project using ZUI — Laubrary Dev, PreviewLab, and any future consumer — so a fix made once applies everywhere instead of drifting between per-project copies. This file used to hold that content directly; it drifted out of sync with `references/zui.md`'s own partial copy more than once before both were merged into the one file above (2026-07-21).

If you're a human (or a tool) browsing the vendored package source without that skill loaded, open the file at the path above directly — it covers: no-infinite-width controls and their per-kind size targets, the two distinct causes of an over-wide control, the `EditorGUIUtility.labelWidth` global-leak gotcha, row-packing vs. spatial-pair 2D controls, tooltip-not-title-text labeling, the live-edited-value-decides-control-type IMGUI bug class, numeric precision rounding, spacing conventions, and the editor-window UI audit tooling (`Editor/UIAudit/EditorWindowAuditSection.cs`, menu items under `Laubrary/`).
