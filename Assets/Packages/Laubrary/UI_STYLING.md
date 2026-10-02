# Laubrary UI styling

The shared retained-mode UI adopts the current Zounds visual vocabulary: slate and blue surfaces, blue selected settings and choices, orange labelled actions, and distinct focus and disabled states. Zounds retains specialized information: bright amber denotes game-code-driven values and blue denotes modulation. The earlier blanket exemption for Zounds is superseded; meaningful audio semantics and explicit skin rules remain supported.

Start with the package's [designer guide](Documentation~/UI_DESIGNER.md). It documents stylesheet order, stable selectors, generated-field roles and sizing, common cards and tool regions, custom-painter properties, detached-root context and acceptance boundaries.

Put consumer-specific design in the host project's optional `Assets/LaubraryUI.uss`. Scope controls below a tool class such as `.lau-tool-pyre`, or use the shared semantic vocabulary for a common treatment. The packaged defaults remain reusable. Match selector specificity: later sheets win at equal specificity, while explicit inline caller dimensions remain supported compatibility overrides.

Static spacing, fonts, neutral colours, borders and supported layout preferences belong in USS. Serialized values, authored colours, pointer behavior, Undo, user-resized dimensions, measured coordinates, preview geometry and value-composite algorithms remain in code/data. USS can arrange declared regions; it does not add missing controls or replace editing behavior.

Generated fields and manually built controls can share family, role, group and size classes through `ZuiFieldPresentation.Stamp`. Binding paths and `zui-binding--*` classes identify members for tooling; they are not the stable design contract. Do not use translated labels, child indices or instance identifiers as designer selectors.

Keep source implementation, rendered checks and workflow checks separate. Verify empty/populated and narrow/normal surfaces, selected/hover/focus/disabled/error states, associated floating panels, Undo, repeated editing and reopening. Source coverage does not certify every Laubrary tool, inspector, IMGUI leaf, runtime interface, display scale or installed consumer. The integration host maintains a per-surface evidence ledger.
