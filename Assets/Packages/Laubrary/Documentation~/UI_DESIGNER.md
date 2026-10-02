# Laubrary UI designer guide

This is the package-facing contract for the retained-mode UI integration begun on 1 October 2026. It describes actual source routes and presentation boundaries; the integration host records fresh rendered and workflow acceptance separately. The owner authorized adoption of the current Zounds look across shared controls.

## Editing entry point and stylesheet order

Place consumer overrides in the Unity project's optional `Assets/LaubraryUI.uss`. Shared defaults live beside `Zui/Toolkit/ZuiToolkit.uss` inside the package. `ZuiWindow` attaches them automatically; roots outside that base, such as standalone inspectors and popups, must call `Z.Attach` or apply a captured presentation context.

`Z.Attach` loads `ZuiToolkit.uss`, then `ZuiPilotStandard.uss`, `ZuiPilotBands.uss`, `ZuiPilotEnvelope.uss`, `ZuiSharedEnvelope.uss`, `ZuiPresentation.uss`, `ZuiFoundationContainers.uss`, `ZuiFoundationFields.uss`, `ZuiFoundationLayout.uss`, `ZuiFoundationFlow.uss`, `ZuiFoundationAssetBrowser.uss`, `ZuiFoundationToolShell.uss`, `ZuiFieldPresentation.uss` and `ZuiSharedTheme.uss`. The host override follows these defaults.

Tool sheets can follow the shared theme. Zounds attaches `Editor/Zounds/Uitk/Skin/ZoundsSkin.uss` and `Editor/Zounds/Uitk/ZoundsUitk.uss`, then removes/reappends the host sheet so it remains last. Detached context application likewise reappends the host after captured sheets. A custom tool that adds its own sheets after attachment must preserve this intended order itself.

Later rules win only at equal specificity. Use root-qualified scoped selectors, such as `.zui-root.lau-tool-pyre .zui-button`, to match or exceed the shared rule being overridden. An explicit inline width or live coordinate remains authoritative; determine its ownership before attempting to retune it.

## Scope, theme and density

The priority authoring roots expose `.lau-tool-pyre`, `.lau-tool-zounds`, `.lau-tool-chunks`, `.lau-tool-mirage` and `.lau-tool-zoe`. The window base uses the protected `PresentationTool` property for a stable scope; the four ordinary priority windows override it explicitly and Zounds supplies its scope through `ZS.Attach`. Controls never know the tool name.

One root may carry a `lau-theme-*` axis and a `lau-density-*` axis alongside its tool scope. The built-in density classes are `lau-density-compact` and `lau-density-roomy`. Unset density means the shared default. Compact and roomy change box/section/row spacing and basic control padding, not field widths, preview dimensions or authored geometry. An arbitrary `lau-theme-*` name is a selector scope for supplied rules, not a hidden theme loader.

For a two-treatment fixture, apply compact or roomy to the same ZUI root, never both:

```csharp
root.EnableInClassList("lau-density-compact", compact);
root.EnableInClassList("lau-density-roomy", !compact);
```

The common theme uses dark slate/blue surfaces, blue latched settings and choices, warm orange labelled actions, pale blue focus, and distinct disabled treatment. Icon actions stay quieter. Main colour properties are `--zui-accent`, `--zui-accent-soft`, `--zui-line`, `--zui-focus`, `--zui-control-text`, `--zui-section-title`, `--zui-bg-inset`, `--zui-sub-fill`, `--zui-sub-fill-2`, `--zui-stage-bg`, `--zui-action`, `--zui-action-hover` and `--zui-action-active`. Chip/icon/selected-row properties remain available in the shared theme sheet. Properties affect only their consumers; they do not remap every authored colour.

Zounds' bright amber still communicates game-code control and blue communicates modulation. The darker action orange serves a separate purpose. Waveform colours, palettes, preview content and a value-to-colour legend remain authored information unless a supported explicit paint override is supplied. The deprecated Zounds IMGUI editor is not a reference design.

## Detached roots

An in-tree popover inherits real ancestors normally. A detached root receives an immutable `ZuiPresentationContext` snapshot captured at opening: ancestor sheets outer-to-inner with duplicates removed; `lau-tool-*` classes; the nearest declared `lau-theme-*` and `lau-density-*` axes; and explicitly requested existing legacy classes. Keep one value per axis on each element and apply a snapshot to a fresh detached root.

The snapshot does not copy arbitrary ancestor structure, every inline style or unsaved field state. It does not observe later owner class/sheet-list changes. Reopen or capture a new context to apply a changed theme/density. Copying a scope class does not recreate selectors that depended on other ancestor conditions. Host-sheet ordering is preserved after captured tool sheets; verify equal-specificity host overrides in the actual detached panel too.

## Fields and size intentions

`ZuiFieldPresentation.Stamp` adds metadata to an existing field root without another label wrapper. Reflection, serialized-property generation and manually built controls can share this vocabulary:

| Axis | Actual classes |
| --- | --- |
| Field root | `zui-generated-field` |
| Family | `zui-family--scalar`, `zui-family--toggle`, `zui-family--text`, `zui-family--choice`, `zui-family--color`, `zui-family--spatial`, `zui-family--reference`, `zui-family--animated`, `zui-family--gradient`, `zui-family--collection`, `zui-family--composite`, `zui-family--other` |
| Role | `zui-role--identity`, `zui-role--reference`, `zui-role--timing`, `zui-role--spatial`, `zui-role--parameter`, `zui-role--status`, `zui-role--content` |
| Size | `zui-size-compact`, `zui-size-standard`, `zui-size-wide` |
| Group | `zui-group--<normalized-group>` |

Groups default to a declared header or owning type. Tokens use lower-case letters/digits with hyphens between other characters. Declare or override a meaningful group for a durable designer contract. Default roles use type/member-name hints; hosts can supply deliberate roles and sizes through `ZuiReflect.Options.PresentationFor`.

Stable member/property paths produce `zui-binding--*` classes and `zui-field:<path>` element names for inspection and tooling. These binding identities are separate from presentation roles. Prefer role/group/family selectors over binding paths, translated labels, child indices or unique instance identities.

A hand-built scalar joins the generated-field vocabulary with the same stamping API:

```csharp
var lifetime = Z.Field("Lifetime", "How long the result lasts.",
    Z.Float(value, "How long the result lasts.", changed));
ZuiFieldPresentation.Stamp(lifetime, new ZuiFieldPresentation(
    "Example.Settings.lifetime", ZuiFieldPresentation.Family.Scalar,
    ZuiFieldPresentation.Role.Timing, "Motion", ZuiFieldPresentation.SizeIntent.Compact));
```

This single host rule sizes both stamped hand-built and generated compact timing scalars, including a scalar-root or labelled-wrapper shape:

```css
.zui-root .zui-generated-field.zui-role--timing.zui-size-compact .zui-float-field,
.zui-root .zui-generated-field.zui-role--timing.zui-size-compact.zui-float-field {
    width: 100px;
}
```

Omitted self-contained scalar dimensions defer to USS. A supplied width remains an intentional compatibility override. Size intent does not replace measured composite geometry: vectors keep component widths; animatable values reserve mode/readout/envelope space; gradients and pads need their real canvas dimensions; serialized fallback property editors retain bounded compatibility sizing. Test a generated and hand-built instance together before extending a sizing rule to composite controls.

## Regions and cards

| Purpose | Supported selectors |
| --- | --- |
| Outer layout | `.lau-tool-shell`, `.lau-tool-shell__split`, `.lau-tool-shell__column`, `.lau-tool-shell__scroll`, `.lau-tool-shell__pane`, `.lau-tool-shell__chrome` |
| Controls side | `.lau-tool-shell__side`, `.lau-tool-shell__side--fixed`, `.lau-tool-shell__side--resizable`, `.lau-tool-shell__side--compact`, `.lau-tool-shell__side--catalog` |
| Preview and resize | `.lau-tool-shell__preview`, `.lau-tool-shell__stage`, `.lau-tool-shell__resize-grip`, `.lau-tool-shell__resize-grip--horizontal`, `.lau-tool-shell__resize-grip--vertical` |
| View/status content | `.lau-tool-shell__readout`, `.lau-tool-shell__status-line`, `.lau-tool-shell__overlay-strip`, `.lau-tool-shell__overlay-segments` |
| Repeated card | `.lau-card`, `.lau-card__header`, `.lau-card__body`, `.lau-card__actions` |
| Semantic region | `.lau-region-identity`, `.lau-region-content` |

`ZuiBox` supplies card aliases beside existing `zui-box*` classes. Body exists on normal boxes; header exists with a title; actions exists when header content is supplied. The chain editor marks its identity header as a card header and identity region. The shared asset editor host supplies the content-region scope. Not every host declares every region, and a selector cannot create an absent slot.

Use headers for identity/universal controls, bodies for parameters and actions slots for trailing actions. Keep card headers non-wrapping and preserve remove/mute/fold affordances. Short related fields share rows; genuinely wide controls can own a row. User-resized stages and panes stay live. Contextual controls/readouts reserve geometry so they do not move the workspace during a gesture.

Narrow behavior is host-specific: some rows wrap, some stages retain minima, and dense audio rows keep readable control widths with horizontal scrolling. Specialized `zs-chain-editor__*` parts retain their coordinate algorithms. A shared shell selector does not prove every width fits or replace the structure needed to regroup data.

## Custom painters

Shared action buttons, toggles, radio choices and segments have a subtle metallic finish over their normal USS-resolved fill and border. `.zui-button-surface` identifies this finish. The unitless `--zui-button-metallic-strength` (0–1) controls its intensity; `--zui-button-fill-top`, `--zui-button-fill-middle`, `--zui-button-fill-bottom`, `--zui-button-border-top` and `--zui-button-border-bottom` supply translucent gradient colors. Normal `background-color`, per-side border colors and widths, radii and state rules remain authoritative. A custom background image replaces the fill finish. `.zui-button-plain` or strength 0 disables the finish; tool-scoped overrides work normally and removing a custom-property rule restores the defaults.

Section headers expose `.zui-section__controls`, a non-wrapping, vertically centered trailing slot immediately before the help icon. Add reusable controls through `ZuiSection.HeaderControls`; pointer gestures in this slot do not fold the section. `Z.QuietToggle` supplies a compact borderless alternative for secondary view options, with `.zui-quiet-toggle`, `.zui-quiet-toggle--on`, and normal hover/focus/active/disabled states. Its value-dependent tooltip describes the current state; it intentionally has no metallic surface or bright orange selected fill. Shared defaults are 16 units high; header instances use a stable 42-unit width, both overridable through ordinary USS.

For a section with `SetHeaderToggle`, an unchecked enable checkbox hides the body and locks header folding while leaving the checkbox reachable. `.zui-section--disabled` provides its muted title/icon presentation. The stored fold state is preserved, so enabling resumes the previous open/closed choice. Silent checkbox refreshes follow the same rules. Header controls may remain visible while folded; the host determines whether its view aids are meaningful while disabled.

Typed numeric custom properties are unitless, for example `--zui-range-thumb-width: 10;`, not `10px`. Their interpretation is the logical dimension, device-pixel stroke or unit interval consumed by that painter. Normal USS dimensions/fonts still use normal units; custom colours use standard USS colour syntax. A variable is supported only when the control reads it.

| Painter | Supported property vocabulary |
| --- | --- |
| Micro slider | `--zui-slider-fill-left`, `--zui-slider-fill-right`, `--zui-slider-track-color` |
| Micro min/max | `--zui-range-fill-left`, `--zui-range-fill-right`, `--zui-range-track-color` |
| Skinned range | `--zui-range-thumb-width`, `--zui-range-thumb-height`, `--zui-range-track-height`, `--zui-range-value-width`, `--zui-range-label-width` |
| Bands | `--zui-band-gap`, `--zui-band-cap-thickness`, `--zui-band-baseline-thickness` |
| Column flow | `--zui-column-flow-column-width`, `--zui-column-flow-gutter`; placement/count remain algorithmic |
| Envelope frame/plot | Prefix `--zui-envelope-` with `background`, `border-color`, per-edge `border-top/right/bottom/left`, per-edge `padding-top/right/bottom/left`, `grid-color`, `grid-rows`, `grid-thickness`, `curve-color`, `curve-thickness`, `curve-hover-thickness`, `hit-radius-extra`, `selected-color`, `selected-stroke-thickness` |
| Envelope handles | Prefix `--zui-envelope-<state>-` with `radius`, `hover-radius`, `border-width`, `fill`, `hover-fill`, `border-color`; states are `editable`, `x-editable`, `y-editable`, `not-editable` |
| Envelope decorations | Prefix `--zui-envelope-` with `frame-line-color`, `frame-edge-color`, `frame-label-color`, `axis-label-color`, `value-label-color`, `legend-border-color`, `marker-label-size`, `marker-label-spacing`, `axis-label-size`, `value-label-size` |

The envelope resolver also supports `selection-box-fill-alpha`, `hover-saturation-scale`, `hover-value-scale`, `uncertainty-fill-alpha`, `uncertainty-stroke-alpha`, `uncertainty-stroke-thickness`, `dotted-white-mix`, `dotted-min-width` and `ghost-opacity`, all with the `--zui-envelope-` prefix. Handle/padding metrics are shared by paint and picking. Removing an override resolves fallbacks again; verify clickable and visible geometry after reset.

Generic and audio envelope adapters share one canvas/editor. `.zui-envelope--standard` supplies normal dimensions; `.zui-envelope--audio` supplies the overlay profile. Frame/index numbering, axis captions and colour legends remain optional authored decorations. Audio uncertainty ellipses, live-play curves, point contexts and endpoint permissions remain supported; waveform trim/loop affordances stay with their owning host.

Existing callers continue to use `Z.Envelope`/`ZuiEnvelope`, `ZuiSkinEnvelope` or audio `EnvelopeTK` with their existing callbacks. The shared canvas exposes a `ZuiEnvelopeConfiguration` through `configuration`; adapters configure their gesture/picking/bend/readout profiles automatically. This object also carries minimum count, optional per-view point permissions and authored frame/axis/colour decorations. It is not a theme selector or serialized point store. Caller-owned begin/update/mutation callbacks remain responsible for Undo and persistence before/after data edits.

## Verification boundary

Immediate-mode leaves are not USS-controlled. Custom-painted canvases consume explicit painter inputs, not arbitrary child selectors. Native pickers, old inspectors and runtime HUDs need their own verified presentation route. No claim that all Laubrary tools are converted follows from a shared theme update.

For a design change, inspect the actual part tree, apply a scoped rule, check narrow/normal empty/populated layouts and selected/hover/focus/disabled/error states, open an associated detached panel, then remove the rule to confirm reset. Complete a disposable editing task, check its visible result, Undo, repeat and reopen. Record source/probe, eye and workflow evidence separately. Clean consumer installation and another display scale remain independent checks.
