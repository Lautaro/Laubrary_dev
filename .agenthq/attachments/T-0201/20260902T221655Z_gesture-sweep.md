# T-0201 — the real-gesture pass

Twenty controls in the OPEN window, driven by their own `value` setters — the same path a pointer drag takes,
callbacks and all — against a scratch copy of the demo document (the demo asset itself is never edited by a
probe). "picture moved" re-renders the document cold after each gesture.

Read it with two things in mind. **A ZUI control is a composite**: the outer element carries a `value` that is
plain data, and the inner field is the one wired to the change callback, so the rows arrive in pairs and it is
the INNER row that proves the wiring (rows 2/4/6/18 below). And **several of these controls are honestly
view-only**: the frame scrubber, playback rate, the cherry-preview toggle and pixels-per-unit change what you
are looking at or what a bake would produce, not the frame itself, so "no" is the correct answer for them.

Value-carrying controls found in the open window: 150.

| # | control | gesture | picture moved |
|---|---|---|---|
| 1 | `Canvas width in samples. B9's authorable range is 32–256.  ·` | Single 96 → 48.25 | no |
| 2 | `FloatField` | Single 48 → 24.25 | yes |
| 3 | `Canvas height in samples.  ·  Drag to set; Shift = fine.  · ` | Single 152 → 76.25 | no |
| 4 | `FloatField` | Single 76 → 38.25 | yes |
| 5 | `Canvas units per sample. 1 makes a "canvas pixel" in a dial ` | Single 2.48778 → 1.49389 | no |
| 6 | `FloatField` | Single 0 → 0.5 | yes |
| 7 | `Canvas pixels between consecutive layers' base planes, and s` | Single 0.97749 → 0.738745 | no |
| 8 | `FloatField` | Single 0 → 0.5 | no |
| 9 | `How many frames this document resolves to. 1 is a still docu` | Single 16 → 8.25 | no |
| 10 | `FloatField` | Single 8 → 4.25 | no |
| 11 | `Playback rate in frames per second — how fast frames are sho` | Single 12 → 6.25 | no |
| 12 | `FloatField` | Single 0 → 0.5 | no |
| 13 | `unity-alpha-progress` | Single 0 → 0.5 | no |
| 14 | `Screen pixels per world unit for a baked sprite — the same c` | Single 16 → 8.25 | no |
| 15 | `FloatField` | Single 0 → 0.5 | no |
| 16 | `unity-alpha-progress` | Single 100 → 50.25 | no |
| 17 | `The ambient's strength. Default 0.18 reproduces Pyre's own R` | Single 0.22 → 0.36 | no |
| 18 | `FloatField` | Single 0 → 0.5 | yes |
| 19 | `unity-alpha-progress` | Single 100 → 50.25 | no |
| 20 | `Multiplies this light's contribution.  ·  Drag to set; Shift` | Single 1.05 → 0.775 | no |
