# Foundation containers — T-0531

This batch moves only fixed container presentation from `ZuiBox` and `ZuiSection` into `ZuiFoundationContainers.uss`. The stylesheet is intentionally separate from the existing toolkit stylesheet so the coordinator can attach it after the legacy sheet during comparison work.

## Semantic parts added

- `zui-box__headercontent`, `zui-box__headerlead`, and `zui-box__gear`
- `zui-box__settings`, `zui-box__settings-caption`, `zui-box__settings-toggle`, `zui-box__settings-toggle--group`, and `zui-box__settings-group-label`
- Existing `zui-section__headerbtn` and `zui-section__toggle`

Every selector remains ordinary class-based USS. A window root can override default presentation with a more-specific descendant selector such as `.lau-tool-zounds .zui-section__headerbtn`; the controls do not inspect the owning tool or mutate global skin state.

## Preserved state and exceptions

- Fold, gear-open, and shown-control visibility remain direct `display` writes because they are live view state.
- The group-toggle left indent remains a direct write because it is a caller-selected value, not a fixed cosmetic.
- Box accent border colour and width remain direct writes because their identity colour is per card instance.
- Flexible spacers remain direct layout writes because their position depends on optional header content and help affordances.
- `ZuiFoldCard` keeps its direct open/closed display write; `ZuiFrame` has no fixed presentation writes to extract.

No existing selector or public API was removed. Existing tool-specific rules can override the new defaults through normal selector specificity.
