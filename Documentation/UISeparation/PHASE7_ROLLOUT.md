# Phase 7 — rollout consolidation and compatibility closure

## Decision

The shared Laubrary skin is now a packaged presentation layer for retained UI Toolkit trees. It owns stable visual rules, while each tool continues to own its live data, interaction, authored content, and custom drawing. The package contains the stylesheet assets and a package-local authoring guide, so a consumer does not depend on the development host's documentation tree.

This is a consolidation decision, not permission to delete every old-looking value. A compatibility path is removed only after it has no callers and the affected surface has passed the same appearance and interaction checks as its replacement. The review found no completed path meeting that bar, so the remaining paths are deliberately retained with an owner and exit condition below.

## Complete surface disposition

The source register records 164 source-level entries, grouped into 102 owning families in `Inventory/migration-classification.csv`. That family table is the canonical disposition for each registered entry: every source entry inherits the route of its owning family, including entries with no visual renderer evidence. This avoids treating a lexical search label as a migration decision.

Sixty-two family entries are explicitly held for confirmation: sixty have no UI or insufficient renderer evidence, one needs its renderer identified, and one needs its external/sample route decided. They are accounted for, but they are not silently claimed as migrated.

| Family route | Families | Disposition and owner |
| --- | ---: | --- |
| Retained-mode separation | 6 | Static presentation is in the shared skin; the relevant tool owner owns any future visual change. |
| Retained-mode separation with a bounded legacy leaf | 14 | Static presentation is in the shared skin; a custom painter or immediate-mode leaf remains with its tool owner until a functional replacement is intentionally designed. |
| Legacy editor port or adapter decision | 9 | Explicitly excluded from presentation-only migration; the corresponding inspector or utility-window port owns its future conversion. |
| Runtime presentation adapter and consumer review | 11 | Explicitly separated from editor styling; runtime owner must validate input, scaling, persistence, and player behavior. |
| No UI or insufficient renderer evidence | 60 | Explicitly held for owning-tool confirmation, rather than silently counted as migrated or excluded. |
| Renderer route needs confirmation | 1 | Explicitly held for the owning tool to identify its renderer and acceptance baseline. |
| External or sample route needs confirmation | 1 | Explicitly held for the sample/external owner to decide whether it belongs to the package skin. |

The Phase 3 surfaces — BackSplash, Cabinets, Choreographer, Lathe, Lathe Mold, and Tapestry — remain migrated under their measured contracts. The dense retained-mode tools, including Pyre and Chunks, remain on the shared presentation boundary; custom painting inside those windows is a leaf, not a separate unsupported surface.

## Compatibility paths retained on purpose

| Path | Why it stays | Exit condition and owner |
| --- | --- | --- |
| Explicit widths supplied by old callers | A caller may still need a fixed control width, and removing the override changes layout rather than merely moving a style rule. | Migrate all callers and compare normal and narrow states; the control-family owner removes it only after the installed-package check passes. |
| Painter colours supplied by code | These support existing callers and restore correctly when a scoped skin rule is removed. | Remove only after there are no direct colour-setting callers and the control painter and hit area still agree; the control-family owner owns this. |
| Immediate-mode editors, drawers, popovers, and canvases | USS cannot style an event-time rectangle tree or a custom drawing algorithm. | A functional port must preserve serialized editing, Undo, focus, input, and saved view behavior; the corresponding tool owner owns it. |
| Zounds comparison editors | Zounds intentionally keeps the old editors as comparison references beside the colorful UI Toolkit editors. | Retire only after the Zounds owner declares the comparison route unnecessary and completes its own side-by-side handover walk. |

## Acceptance status

### Verified by probe

- The shared shell stylesheet and its metadata are inside the Laubrary package, and the loader resolves adjacent presentation sheets for both the development host and an installed package location.
- The package changelog no longer points installed consumers at a host-only Phase 3 document; it links to the package-local styling guide instead.
- Phase 3 records 13 of 15 captured states with identical resolved properties and pixels. The remaining two differences reproduced when the moving Choreographer window was compared against itself.
- The Phase 4 parity record remains the frozen reference for Pyre and Chunks. Chunks matches its captured states exactly; Pyre's populated variation is reproduced by the same-build control rather than attributed to the shared skin.
- Zounds remains on its separate colorful skin and is excluded from the general Laubrary appearance contract.

### Verified by eye

- The earlier Phase 3 side-by-side capture set was visually reviewed in the warm-editor comparison session that produced its parity record.

### Not verified in this consolidation

- A fresh installed-consumer Unity editor smoke run was not performed in this documentation-only closure.
- A live development and consumer compile probe was attempted, but Unity's pipeline did not answer inside its command timeout; this is not interpreted as a passing compile result.
- Hover, pointer-held dragging, deliberate-error states, and a complete human handover walk across every retained immediate-mode surface remain owned by their future functional ports.
- The 62 held confirmation routes require their named owners to identify a visual renderer or confirm that no user interface exists before a future programme can claim full migration.

## Risk rule

Do not convert a held discovery entry into a migrated or retired entry based on source-text screening alone. Update the family classification, capture a warm-session baseline, verify interaction, and then update this rollout record and the package styling guide in the same change.
