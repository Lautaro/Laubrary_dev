# UI separation pilot contract

Authorised: the 30 September planning draft (AHQ T-0522), initially phases 0 and 1, then continued by the owner's instruction to move forward in the isolated project. Coordinator owns architecture, integration and live editor. Source project remains untouched. This standalone copy includes current working assets; initial Git commit and baseline hashes identify its starting point.

## Presentation ownership

- Each control keeps its existing public API and legacy selector names while adding semantic aliases where useful. Default USS must style a normal instance with no window-specific configuration.
- A tool window can apply a parent class such as `lau-tool-zounds` or `lau-tool-pilot`. Rules such as `.lau-tool-pilot .zui-slider` override the default rule through normal USS specificity. Two differently tagged roots coexist. Controls never look up tool names or install window-specific styling themselves.
- Existing `zui-root` / Zounds skin classes and default skins remain compatible. Expose static cosmetics and custom-painter inputs in USS; remove conflicting inline static assignments in the migrated controls. Keep live positions, values and measured geometry in C#. Legacy per-call presentation arguments remain explicit compatibility exceptions until their call sites migrate.
- Custom painting reads typed USS custom properties using `CustomStyleResolvedEvent`, caches resolved values, and repaints/reflows when needed. No pixel-perfect appearance drift is accepted silently. Reset style values when a parent override is removed; do not retain the previous root's values.
- Existing interactive APIs, value events, pointer capture, keyboard behaviour, Undo wiring and data ownership remain unchanged. No new runtime dependencies or schema changes.
- Baseline comparison uses frozen original control sources and styles, not two windows using the same modified classes. Snapshot sizes, states and display scaling. Check identical dimensions and nonblank captures before diffing.

## Pilot scope and ownership

Coordinator: Zui.cs factory integration, root/default stylesheet attachment, responsive composition pilot, frozen baseline controls, comparison harness, live verification, documentation and changelog.

Standard-controls worker (T-0525): ZuiMicroSlider.cs, ZuiToggleButton.cs, ZuiMicroMinMax.cs if appropriate, and a dedicated `ZuiPilotStandard.uss`. No edits to ZuiToolkit.uss or Zui.cs; coordinator attaches dedicated sheets. Preserve old classes and add semantic classes.

Bands/range worker (T-0526): ZuiSkinBandSliders.cs, ZuiSkinRangeSlider.cs, ZuiSkinMinMax.cs, ZuiSkinSlider.cs and dedicated `ZuiPilotBands.uss`. Same constraints.

Envelope worker (T-0527): ZuiSkinEnvelope.cs and dedicated `ZuiPilotEnvelope.uss`. Extract style definitions used by the pilot into USS so no original look changes. Do not alter envelope evaluation/interaction algorithms or runtime definitions. Discuss any required legacy compatibility bridge with coordinator.

Inventory worker (T-0524): Documentation/UISeparation/Inventory only; read-only elsewhere.

Workers read all UI guides before edits, do not drive Unity, do not commit, do not write shared styles/factories, and report verification honestly. No static presentation writes should be added to migrated code. Dynamic exceptions are recorded and measured. Baseline and candidate both remain operable in the pilot for acceptance.
