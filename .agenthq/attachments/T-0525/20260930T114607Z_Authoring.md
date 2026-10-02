# UI separation authoring

The package sheet defines reusable defaults only. A tool or comparison harness applies its own stylesheet after the defaults and uses a root class to override semantic descendants. Controls do not inspect a tool name and default sheets do not contain tool-root selectors.

| Surface | Legacy selectors retained | Semantic selectors | Default sheet responsibility |
| --- | --- | --- | --- |
| Painted MicroSlider | `zui-microslider`, `zui-microslider__caption`, `zui-microslider__value`, `zui-microslider__numfield` | `zui-slider`, `zui-slider__label`, `zui-slider__value`, `zui-slider__input` | Compact track metrics, label/value/input geometry, visibility-state presentation, and painter palette defaults. |
| Button-style toggle | `zui-togglebutton`, `zui-togglebutton--on`, `zui-togglebutton--mark` | `zui-toggle` | Button metrics and the normal, hover, and selected appearance. |
| Painted MicroMinMax | `zui-microslider`, `zui-microminmax`, `zui-microslider__caption`, `zui-microslider__value` | `zui-range`, `zui-range__label`, `zui-range__value` | Compact track metrics, label/value geometry, and painter palette defaults. |
| Skin bands and ranges | Existing skin selectors remain | `zui-slider`, `zui-minmax`, `zui-range`, `zui-band-sliders` and their parts | Static layout metrics and resolved custom-property defaults documented in `BandsRange.md`. |

An override belongs in a tool or harness sheet, for example `PilotOverride.uss`, rather than `ZuiPilotStandard.uss`:

```uss
.lau-tool-pilot .zui-slider {
    --zui-slider-fill-left: rgba(38, 58, 98, 0.92);
    --zui-slider-fill-right: rgba(86, 142, 222, 0.85);
    --zui-slider-track-color: rgba(0, 0, 0, 0.34);
}

.lau-tool-pilot .zui-range {
    --zui-range-fill-left: rgba(38, 58, 98, 0.92);
    --zui-range-fill-right: rgba(86, 142, 222, 0.85);
    --zui-range-track-color: rgba(0, 0, 0, 0.34);
}
```

Removing `lau-tool-pilot` causes UI Toolkit to resolve the default custom properties again. The painted controls reset their cached colours before reading each `CustomStyleResolvedEvent`, so a previous root's palette cannot persist. The standard default sheet deliberately uses compound selectors for the painted controls because `zui-slider` and `zui-range` are also general semantic aliases on skin controls.

Static presentation belongs in USS: dimensions, padding, border radius, borders, default colours, text alignment, absolute child anchors, and visibility selected by semantic state classes. Live values and measurements remain in C#: fill and band extents, pointer capture and hit-testing, Shift fine-drag calculations, Undo gesture lifetime, numeric input synchronization, reset and mode-menu behavior, label measurement, and repaint invalidation. Public painter colour fields remain compatibility fallbacks; a resolved USS palette wins while a stylesheet defines it.

## Inventory review

The baseline manifest records 64 Toolkit C# files, one Toolkit USS file, and 66 corresponding meta files. This is the phase-0 package basis. It excludes the nine current `Assets/Editor` C# files because that folder is the pilot harness and frozen baseline comparison apparatus, not a source surface to classify for migration. The current package contains 65 C# files and five USS files because pilot work has added presentation and stylesheet artifacts after the manifest was frozen.

The current lexical scan finds `AddToClassList` in 44 Toolkit C# files and `style.` in 47. These are candidate counts only: they include dynamic geometry, interaction state, and unrelated controls, and they do not establish that every match is static presentation or that each file is a distinct UI surface. Verified migrated surfaces are only the ones supported by the worker documentation and source review: painted standard controls, skin bands/ranges, and the envelope surface. Any inventory report should label lexical matches and verified surfaces separately, and should retain the package-only phase-0 denominator above.

Final editor evidence is recorded separately in `ACCEPTANCE.md`; this inventory paragraph describes source-review coverage only.

## Sheet and asset ownership

`Z.Attach` adds the presentation sheets beside the shared Toolkit sheet, supporting both this development path and an installed package path. Tool sheets attach afterwards. There is no global mutable selected skin. `ZS.Attach` also tags its root `lau-tool-zounds`. Separate popup roots must explicitly receive their chosen tool class and tool stylesheet; that context does not cross a detached UI tree automatically.

`ZuiPilotStandard.uss`, `ZuiPilotBands.uss`, `ZuiPilotEnvelope.uss` and `ZuiPresentation.uss` are hand-authored presentation sources. Existing Zounds image/skin generation continues to own its existing generated assets; it does not overwrite these new sheets. The copied baseline textures retain independent GUIDs with matching import settings. Never regenerate the reference during a comparison.

`Z.MicroSlider` and `Z.MicroMinMax` use an omitted-width sentinel (`-1`) and get the historical 150-pixel default from USS. An explicit nonnegative width remains an inline compatibility override. Migrate those callers to a semantic variant when designers must own their width. Range geometry arguments and envelope definitions likewise remain fallback bridges, while resolved custom USS properties take precedence. Numeric custom painter properties use unitless values, interpreted as UI units; ordinary USS dimensions use `px`.

Aliases can meet existing selectors: the older Toolkit-native envelope already used `zui-envelope`. The skinned variant explicitly suppresses that native frame because its painter draws the frame itself. This is an example of why adding a semantic alias requires testing the full existing stylesheet, not just the new sheet.
