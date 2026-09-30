# Foundation field presentation extraction

This phase gives the shared reflection, serialized-property and managed-reference controls semantic presentation parts while keeping their public APIs and data behavior unchanged. `ZuiFoundationFields.uss` supplies the default layout when the coordinator attaches it through `Z.Attach`; a window can override it through a normal parent selector such as `.lau-tool-zounds .zui-foundation-flow` or `.lau-tool-pilot .zui-foundation-property`. The controls never inspect a tool name and there is no active global skin.

## Semantic parts

| Part | Owner | Default responsibility |
| --- | --- | --- |
| `zui-foundation-object` | reflected and serialized object-reference fields | Stable semantic target for tool-specific object-field presentation. |
| `zui-foundation-flow` | reflected field flow host | Row flow, wrapping and cross-axis start alignment. |
| `zui-foundation-hue-row`, `zui-foundation-hue-swatch` | reflected hue field | Keep the compact slider-and-swatch pair from shrinking. |
| `zui-foundation-enum-field` | reflected enum field wrapper | Allows a wrapped radio group to respond to a narrower pane. |
| `zui-foundation-compact-row` | compact reflected list element | Keeps its fields, flexible gap and remove affordance on one centered row. |
| `zui-foundation-property`, `zui-foundation-property--default-width` | serialized `PropertyField` fallback | Prevents shrinking and applies the legacy 420px cap only when no width was supplied. |
| `zui-foundation-mref-spacer` | managed-reference header | Takes remaining header space before the type button. |

The existing `zui-mref*` and `zui-no-decorators` selectors remain intact for compatibility. Their existing default styling is left in the shared toolkit sheet; this phase adds the new semantic seam without changing their visible rules.

## Compatibility exceptions retained in C#

Explicit per-call widths remain inline. They express caller intent and must continue to win over default USS. This includes `ObjectField` widths and a `PropertyField` width provided to `ZuiSerialized.Property`.

The reflected flow host still assigns each child’s basis after it has inspected whether the child is curve-shaped. It repeats that measurement when an animatable value changes modes. This is live layout behavior, not static skinning, and moving it to USS would lose the current reflow behavior.

Managed-reference open/closed state still changes the body’s `display` in C#. It is serialized fold state and interaction behavior. The stylesheet owns only the static header/body presentation.

## Frozen comparison baseline

`Assets/Editor/UISeparationPilot/FoundationBaseline/` contains frozen copies of `ZuiReflect`, `ZuiSerialized` and `ZuiManagedRef` under `Laubrary.Zui.FoundationBaseline`. They retain the original logic and use the live parent namespace only for the unmodified toolkit helpers. The coordinator’s comparison harness can therefore exercise separate source implementations.
