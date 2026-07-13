# Laubrary editor tool conventions

Standing rules for building IMGUI editor tools (ZUI-based windows, property drawers, inspectors) within
Laubrary. Add to this file as new conventions get established — it's the canonical place future tool work
should check first, alongside each module's own doc comments.

## `EditorGUIUtility.labelWidth` is global and leaks across unrelated sections of the same window

It's not scoped to whatever `Begin*`/`End*` group you're inside — set it once anywhere in `OnGUI` and it
stays in effect for every labelled field drawn afterward THAT SAME CALL, in any method, any section, until
something else changes it. A window that sets it wide for one area's long labels (Pyre sets `112f` for the
left panel's `"Taper (centre↔edge)"`-style dial names) silently forces that SAME reservation onto every
compact, short-labelled field drawn later too — a 5-character label like "Asset" still eats 112px, leaving
whatever's left (however carefully `ZUI.FitWidth` computed the TOTAL) for the actual content. This is exactly
what caused Pyre's "Zoe Preview" fields to truncate even after they were sized "correctly."

Two ways to avoid it, pick whichever fits:
- **`ZUI.NarrowLabel(label)`** (`ZUIFields.cs`) — a scope that temporarily sets `labelWidth` to match the
  label's own actual text width, restoring the ambient value on dispose. Wrap any `FitWidth`-sized or
  otherwise deliberately compact labelled field in it.
- **`GUIContent.none` + a separate stacked `Label`** — skips Unity's prefix-label reservation entirely (no
  label content = no label space claimed), at the cost of the label sitting on its own line above the control
  instead of beside it. Often reads better anyway in a narrow column (see Pyre's Tint fields).

## Never let a LIVE-EDITED value decide which CONTROL TYPE gets drawn

Shipped and broke once: Pyre's "Attach id" field conditionally drew either `EditorGUILayout.Popup` or
`EditorGUILayout.TextField` depending on whether a dropdown's option list was available — and that list was
computed from a `clip` string read fresh from a `TextField` THIS SAME FRAME (i.e. it can change on every
keystroke while the user is actively typing). The result: garbled, overlapping controls — not just on that
row, but on OTHER rows drawn earlier in the same method. Unity's IMGUI runs a Layout pass and a Repaint pass
per frame and matches controls between them by CALL ORDER; if the two passes end up calling a genuinely
DIFFERENT SEQUENCE of GUI functions (a `Popup` in one, a `TextField` in the other — not just a different
VALUE passed to the same call), that mismatch corrupts rect/control-ID matching for the rest of the draw call,
not just the one control.

`if (condition) DrawX(); else DrawY();` where `DrawX`/`DrawY` are DIFFERENT control types is only safe when
`condition` is guaranteed identical across a single Layout+Repaint pair — i.e. based on a value that doesn't
change mid-frame (a plain field read at the top of the method, a value already committed via
`EditorGUI.EndChangeCheck()`), never a value fresh off a text field or other live-editable control from
earlier in the SAME draw call. The fix: read the STORED field for anything that gates a structural difference
(`spec.previewSubjectClip`, not the `clip` local just returned by this frame's `TextField`), and for "maybe
show a picker" cases, prefer a mechanism that doesn't touch the persistent layout at all — a `GenericMenu`
opened from a small button (a modal overlay, not part of the Layout/Repaint-matched hierarchy) instead of
swapping the field itself for a `Popup`.

## Explanatory text belongs in a tooltip, not the UI

Box/section titles and field labels must stay short and literal. Don't embed *why* or *how* something works
into visible UI text — e.g. a box titled `"Live preview subject (not baked)"` is wrong: "(not baked)" is an
explanation, not a name, and it clutters the window with prose the user has to read every time regardless of
whether they need it.

Explanations (what a control does, why it exists, a non-obvious behavior, a gotcha) belong in a **tooltip**,
surfaced via a small "?" glyph: `ZUI.HelpIcon(string tooltip)` (`Zui/Scripts/Editor/ZUIFields.cs`). Hovering it
shows the full explanation via Unity's native `GUIContent` tooltip — no custom tooltip system needed. Place it
inline near the relevant title/field, typically right-aligned in its own row:

```csharp
using (Box("Live preview subject"))
{
    using (ZUI.HRow()) { GUILayout.FlexibleSpace(); ZUI.HelpIcon(
        "Plays through the same real gameplay components the subject uses in-game — nothing here is baked. " +
        "These fields are preview-time wiring only; they aren't part of the runtime blast."); }
    // ... the box's actual controls ...
}
```

This keeps the UI scannable at a glance (title tells you what it is) while the full explanation is still one
hover away for anyone who needs it.

## No infinite-width controls

A slider, colour field, or gradient field left to `GUILayout`'s default "fill whatever's left" behavior
stretches to match whatever the window happens to be wide right now — often 1000+ px in a wide panel. A value
like `1.882` or a colour swatch doesn't get any easier to read at that width; it just gets harder to scan,
and it steals space from everything else sharing the row. Give these controls an explicit width sized to what
their own content actually needs, then let `GUILayout.FlexibleSpace()` soak up the remainder instead of the
control itself:

```csharp
const float CompactSliderWidth = 150f;   // enough for "Label: 12.34" inline, or label + a small input field
const float CompactColorWidth  = 130f;   // a swatch + short label

using (ZUI.HRow())
{
    value = ZUI.MicroSlider(value, min, max, "Zoom", showInputField: true,
                             options: new[] { GUILayout.Width(CompactSliderWidth) });
    tint = EditorGUILayout.ColorField(new GUIContent("Tint"), tint, true, true, false, GUILayout.Width(CompactColorWidth));
    GUILayout.FlexibleSpace();
}
```

Rough widths that read clearly at Laubrary's default editor font size: **~150px** for a slider with an inline
label + value (or a small input field), **~130px** for a labelled colour swatch, **~110px** for a short text
field. Widen only if the content genuinely needs it (an asset name/path field showing a full name benefits
from more room — that's a legitimate exception, not a violation of this rule). This applies to `ZUI.Slider`/
`ZUI.MicroSlider`/`EditorGUILayout.ColorField`/`GradientField`/similar value controls; it does NOT mean every
control must be narrow — box/section widths, preview viewports, and text-heavy fields still get to use the
space they actually need.

**A fixed width cap is a starting point, not a substitute for checking the content fits.** A blind pixel
number sized for a short value (a clip name like "Shot") will truncate a longer one (a 40-character asset
name or MetaLayer id) the moment the content changes — and truncated text is worse UX than a slightly wider
control. When a field's content length genuinely varies at runtime (an asset name, a user-typed string, a
dropdown's current selection), measure it and size to fit instead of guessing a fixed number — see
`ZUI.FitWidth(label, value, min, max)` (`ZUIFields.cs`), which uses `GUIStyle.CalcSize` on the label + current
value, clamped to a sane range. It grows/shrinks with whatever the field currently shows, so a short value
doesn't waste space and a long one doesn't truncate. When a group of controls doesn't all fit comfortably on
one row even with fitted widths, give the one whose content varies most its own row (see Pyre's "Zoe Preview"
panel: `Asset` — a name that can be long — gets its own row; `Clip`/`Attach id`, both normally short, share a
second row) rather than forcing everything onto one row and letting Unity's layout squash whichever loses.

**Once controls aren't fighting for space, don't cram them together either.** If a row has room to spare
(three compact sliders in a panel wide enough for ten), each can afford a bit more than the bare minimum, and
the gaps between them should be `ZUI.HorizontalSpace()` calls placed BETWEEN each pair — not one
`FlexibleSpace()` dumped at the end of the row. Distributed gaps read as deliberate separation between
distinct controls; space is not the enemy, cramming and truncation are.

For **compact controls grouped together** (an asset picker + a 2D position pad + a couple of stacked
sliders, e.g. Pyre's "Preview backdrop" Image mode), pick a shared square size (`ZUI.PositionPad` defaults to
56px) and use `ZUI.HRow()` to keep them side by side in one row instead of each claiming its own full-width
row underneath.

## Numeric fields: don't let float noise dictate how many decimals get shown

A slider value computed via `Mathf.Lerp`/`InverseLerp` (dragging a track, or any float round-trip through
normalized 0–1 space) accumulates float32 mantissa noise — "0.4" lands as `0.40000000596...`. Left alone,
`EditorGUI.FloatField` reveals that full trail the moment the field is focused, which is exactly what "does
this input need 87 decimals" looks like to a user — the field isn't wrong, the VALUE it's showing has noise
nobody put there on purpose. `ZUI.MicroSlider`/`Slider` round every drag-computed value to 5 decimal places
at the source (`ZUISlider.cs`'s `RoundValue`) specifically so no downstream display ever has noise to reveal.
If you're writing a NEW numeric control (not going through `ZUI.Slider`/`MicroSlider`), apply the same
rounding to any value that passed through a `Lerp`/normalized-space round-trip before it's ever stored or
displayed — don't rely on formatting a noisy value to hide the noise; clean the value once, upstream.

**Gotcha: `EditorGUI.FloatField`'s number text supports its OWN drag-to-scrub, a completely separate
interaction path from a slider TRACK's own drag handling.** Rounding only where the track computes its value
(`SamplePosition`) fixes dragging the bar but does nothing for scrubbing the number field itself — that path
has its own float accumulation and needs the SAME rounding applied to whatever it returns. `DrawMicroSlider`
rounds both paths for exactly this reason; if you build a custom slider+field combo, round every path that
can produce a value, not just the one you tested first.

**Sometimes the right fix isn't cleaning up noise, it's constraining precision on purpose.** A playback speed
multiplier, a percentage meant to move in whole points, a frame count — these have no legitimate use beyond N
decimals, and rounding to 5dp "noise cleanup" still leaves room for a meaningless `1.23457`. Reach for
`ZUI.MicroSlider(value, min, max, label, decimals, ...)` (the overload taking an explicit `int decimals`) when
a value's PRACTICAL precision is genuinely coarser than float noise alone would justify — it quantizes every
interaction path (track drag AND field scrub/type) to exactly that many decimal places, not just cleans up
accidental noise.

## Spacing between grouped controls: `ZUI.HorizontalSpace()` / `VerticalSpace()`, not a bare `GUILayout.Space`

When you pack several controls into one row (see above), give them breathing room with
`ZUI.HorizontalSpace()` between each — it reads `ActiveSheet.horizontalSpacing` from the active ZUI Style
Sheet, so it's a genuinely **user-adjustable** value (tunable in the Style Editor), not a hardcoded pixel gap.
`HorizontalSpace(float scale)` / `HorizontalSpace(string scaleName)` scale that base amount up for a bigger
gap or look up a named scale from the sheet. The vertical equivalent is `ZUI.VerticalSpace()`. Prefer these
over a bare `GUILayout.Space(12f)` — the literal number can't be retuned globally later, the sheet-driven
value can.

```csharp
using (ZUI.HRow())
{
    DrawThing1();
    ZUI.HorizontalSpace();
    DrawThing2();
    ZUI.HorizontalSpace();
    DrawThing3();
}
```

## `ZUI.MiniRadioVertical`'s height can't be forced

`ZUI.MiniRadioVertical` auto-measures each item's height from its own label text (`itemH = labelStyle.CalcSize(...)`)
and silently ignores any `GUILayout.Height(...)` passed via its `options` param — so it can't be made to match
another control's exact height (e.g. lining a mode-selector radio stack up against a fixed-size thumbnail next
to it). When exact height matching another control matters, build the stack by hand from the Rect-based
`ZUI.Toggle(Rect, bool, string, style, onColor, cornerMask)` overload instead — reserve one
`GUILayoutUtility.GetRect(width, totalHeight)`, split it into N equal sub-rects yourself, and draw each with
`ZUICornerMask.Top/Square/Bottom` for the pill-stack look. See `PyreWindow.DrawModeRadio` for a working
example.
