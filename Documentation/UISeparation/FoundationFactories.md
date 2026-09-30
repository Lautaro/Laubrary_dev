# Foundation factory defaults

The factory defaults below are now semantic USS defaults in `ZuiFoundationLayout.uss`. A normal root receives them through `Z.Attach`; a tool root can override a specific part with a normal descendant selector. The factories do not inspect tool names or maintain an active skin.

| Factory family | Semantic class | Default preserved in USS |
| --- | --- | --- |
| `HSpace`, `VSpace`, `Flexible` | `zui-foundation-hspace`, `zui-foundation-vspace`, `zui-foundation-flexible` | 8px horizontal space, 6px vertical space, and a growing flexible gap. |
| `Icon` | `zui-foundation-icon` | 14px square. |
| `IconButton`, `IconToggle` | `zui-foundation-icon-button` | 20px square. |
| `Dropdown`, `EnumDropdown` | `zui-foundation-dropdown`, `zui-foundation-enum-dropdown` | 140px. |
| `Int`, `Color` | `zui-foundation-int-field`, `zui-foundation-color-field` | 60px. |
| `Curve`, `Gradient`, bound `Gradient` | `zui-curve-field`, `zui-gradient-field`, `zui-gradient-bound-field` | 180×24px curve, 180×20px gradient, 200px bound gradient width. |
| `MinMax` | `zui-minmax-field__slider`, `zui-minmax-field__number` | 130px track and 42px numeric endpoints. |

`Float`, `TextInput`, `Object`, `Slider`, `SliderInt`, `MicroSlider`, and `MicroMinMax` already follow this pattern using their existing semantic classes and USS defaults. Their source behavior is unchanged.

## Compatibility rule

Each migrated optional dimension now defaults to `-1`, meaning that the USS supplies the legacy visible default. A supplied dimension, including `0`, still writes an inline value exactly as before. This preserves existing callers that deliberately provide a width or spacing amount.

## Deliberate exceptions

The following remain compatibility work for their control-family rollout: `MiniRadioVertical` (button count and optional total height), `Stacked` (required width and child slider width), `Pad` and `PadRow` (drag-resolution metrics and paired readouts), and custom controls that receive dimensions in their constructors (`Envelope`, `Timeline`, `Value`, `Value2D`, ramps and similar painter-backed controls). Some of those arguments have historic defaults; this wave does not claim their presentation is fully separated. The dynamic icon texture remains inline content; its ordinary size moved to USS.
