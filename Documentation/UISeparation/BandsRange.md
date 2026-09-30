# Bands and range controls

T-0526 moves static presentation for skin sliders, min/max controls, range sliders, and band sliders into `ZuiPilotBands.uss`. Existing selector names remain and each element now has a semantic alias: `zui-slider`, `zui-minmax`, `zui-range`, and `zui-band-sliders` with named part classes.

Band geometry resolves the band gap, overflow-cap thickness, and baseline thickness from typed USS custom properties. These are unitless numbers because the resolver reads `float`; the default values are 2, 2, and 1. Cap and baseline height are assigned from the resolved metric in the dynamic layout pass, so their height and position share one source. Non-finite values fall back to the default and negative values clamp to zero. Removing a parent override resets the metrics to the frozen defaults. Dynamic positions, value spans, hit testing, labels measured from text, and active/hover state remain in C#.

Range sliders expose optional typed unitless properties: `--zui-range-thumb-width`, `--zui-range-thumb-height`, `--zui-range-track-height`, `--zui-range-value-width`, and `--zui-range-label-width`. Each resolves on `CustomStyleResolvedEvent`, clamps to finite nonnegative values, and falls back to its constructor geometry when absent or after a parent override is removed. The constructor geometry still owns value format, bipolar mode, and value-field visibility.

Compatibility exceptions retained in code are dynamic value geometry: percentage spans, absolute element placement, thumb centring from its resolved width, and compact-label reservations. Those values are inputs to interaction or layout measurement rather than static presentation.
