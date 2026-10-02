# Phase 5 — legacy editor compatibility boundary

## Decision

The semantic stylesheet is a UI Toolkit mechanism. It can own the stable appearance of a retained UI Toolkit tree, but it cannot style an immediate-mode control tree that Unity redraws from scratch each event. A conversion of an old inspector, property drawer, popup, or editor window therefore has to be a functional UI port, not a presentation-only move.

This record applies the scope authorised by T-0553: keep the already migrated UI Toolkit shells on the shared semantic stylesheet, retain the compatibility cases below, and do not count an IMGUI occurrence as migrated merely because it sits in a tool that otherwise has a UI Toolkit window. It is an explicit Phase 5 supported-exclusion record, not a claim that the excluded surfaces have disappeared.

## What is already covered

The dense active authoring windows are already on the semantic stylesheet. The Phase 4 parity record verifies the Pyre and Chunks reference states, and the broader dense-window pass records semantic presentation for the other named Toolkit tools. Their custom drawing leaves remain inside those migrated windows; they are not separate legacy shells.

## Supported exclusions

| Surface group | Why it remains outside semantic USS | Boundary that preserves the user experience | Future owner |
|---|---|---|---|
| Custom-drawn preview and graph leaves | These areas paint pixels, handles, curves, or a live canvas rather than building standard controls. A stylesheet cannot describe their drawing algorithm. | Keep the surrounding window and its controls in UI Toolkit; retain only the small drawing leaf as an IMGUI container or custom painter. This covers the preview leaves in Pyre, Chunks, Lazor, TextSplash, Tapestry, and graph-style tools. | The tool that owns the canvas, when its drawing model is deliberately redesigned. |
| Legacy Zounds comparison windows | Zounds deliberately keeps working old editors beside the new colorful UI Toolkit editors so their appearance and behavior can be compared. The main menu routes to the new editor; the old window is available only from its comparison menu. | Keep the old window, Klip editor, Zequence editor, and their dependent old controls unskinned and reachable only through the existing comparison route. Do not let the general Laubrary skin change Zounds' colorful design. | Phase 7, which explicitly owns retirement of completed compatibility adapters. |
| Immediate-mode property drawers | These extend Unity's old per-rectangle drawing callback. They run inside whichever inspector hosts them, including legacy inspectors, so a UI Toolkit-only replacement would remove compatibility rather than separate static presentation. | Retain the existing drawer behavior until the hosting inspector is ported together with its serialized binding, height calculation, mixed-value display, and undo path. This includes the SmartStats modifier drawers, the StatefulUI flags drawer, the Overture managed-state drawer, and the Zoetrope/Launimator view drawers. | The corresponding inspector-port task. |
| Immediate-mode custom inspectors | These inspectors build reflection-driven or serialized forms through Unity's immediate-mode callback. Moving only colours, spacing, or labels would not reach them; replacing them requires a complete retained-mode form with the same serialization, Undo, multi-object, and prefab-override behavior. | Retain the current inspector presentation until a per-family port has an empty, populated, mixed-value, prefab-override, and Undo baseline. This covers the legacy SimpleMenu, SimpleUI, StatefulUI, Story, Daemon, and Overture inspector families. | One task per inspector family, not this presentation-only phase. |
| Immediate-mode standalone utility windows and native popovers | The remaining utilities use event-time absolute rectangles, custom hit testing, native popup focus, or live data rows. Their layout and input model are intertwined, so semantic USS has no stable tree to own. | Retain the working utility behavior rather than partially restyling it. The examples are Notifyer's live log, the tag picker, Daemon's graph/inspection utility, Launimator's short-lived prompt and recent-sheet popovers, and the still-legacy Zounds popovers. | A dedicated functional port with a frozen interaction baseline. |

## Explicit non-exclusions

An IMGUI reference inside an otherwise retained-mode window is not automatically excluded. The owner must first decide whether it is only a drawing leaf or whether it is a full interactive form. A form is a future migration candidate; a drawing leaf remains supported only while it is confined to the leaf boundary above.

Native Unity menus and `GenericMenu` routes are also not styled by USS. They stay as native platform menus; this does not authorize a second in-window menu or any new menu entry.

## Acceptance and verification

The Phase 3 method remains the acceptance method for every later functional port: take before and after captures in one warm editor session, compare resolved styles and pixels, and check real interaction separately. The frozen Pyre and Chunks references remain the general-layout reference; Zounds remains a separate colorful-skin reference.

Verified by source review: the retained leaves are bounded custom-painting or immediate-mode callback paths rather than UI Toolkit control trees, and the old Zounds editors are explicitly retained for side-by-side comparison.

Not verified by eye in this phase: pointer-held hover, dragging, deliberate errors, and a complete handover walk through every excluded legacy surface. No functional behavior was changed, so this record does not claim those interactions were revalidated.

## Handoff rule

Any future port that removes one of these exclusions must remove its row from this document in the same change, add a real baseline record, preserve menus, Undo, serialized state, and saved view identity, and state separately what was verified by probe, by eye, and not verified.
