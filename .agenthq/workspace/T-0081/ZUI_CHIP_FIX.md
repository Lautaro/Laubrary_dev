# T-0081 — ZuiChip had no stylesheet at all

## The bug, confirmed

`ZuiChip` styles itself **entirely** through USS class names and `ZuiToolkit.uss` — the only `.uss` file in the project — contained **zero** `zui-chip` rules. Verified before touching anything: `grep -n "zui-chip" ZuiToolkit.uss` returned nothing, and `grep -rn 'AddToClassList("zui-chip'` across the whole `Assets` tree returned only the six sites inside `ZuiChip.cs` itself (no call site adds its own chip modifier class, so no extra rules were needed). The PM's measured tree dump (137x79 column, 64px image band, no frame) is exactly what a bare `VisualElement` does: it defaults to `flex-direction: column`, so the thumbnail stacked above the name, and a UITK `Image` with no explicit width/height measures to its **texture**, which is where the 64px band came from.

## What was added

**`Assets\Packages\Laubrary\Zui\Toolkit\ZuiToolkit.uss`** — three palette variables in `.zui-root` plus one new `/* Reference chip (ZuiChip) */` block placed inside the existing "Controls" section (between `.zui-radio__on` and `.zui-no-decorators`, i.e. among the other control rules, not bolted on at the end).

- `--zui-chip-fill` / `--zui-chip-fill-hover` / `--zui-chip-line` — the accent hue at 0.14 / 0.30 / 0.55 alpha. Deliberately **below** `--zui-accent-soft` (0.35), which already means "this toggle is latched ON": a reference must be spottable without being misread as a pressed button. Declared as custom properties in `.zui-root` because that is how this sheet declares its palette (per its own header comment) — retune the whole toolkit's reference look in one place.
- `.zui-chip` — `flex-direction: row`, `align-items: center`, `flex-shrink: 0`, 1px `--zui-chip-line` border, `--zui-chip-fill` background, padding 6/5/1/1, `overflow: hidden` (same treatment as `.zui-microslider`), and `border-radius: 9px`. The radius is the one deliberate departure from the toolkit's uniform 3px: a chip's whole job is to not look like a settings control, and roundness is the cheapest always-on signal that separates "points at something else" from "press me" while keeping the same hairline weight, palette and padding scale. It still reads as the same toolkit next to `.zui-togglebutton` / `.zui-segmented`.
- `.zui-chip { max-width: 220px }` — matching the existing `.zui-mref__type` precedent in this same file. Added because `MaxW()` has **zero call sites** in the entire project (`grep -rn "MaxW("` finds only the declaration), so without a default cap the `__label` ellipsis rules could never fire and any long asset name would size the pill to its text and shove its row-mates (e.g. the `×` remove button in `ChunkWindow.PyreSpawn.cs:346-353`) off the pane. `MaxW()` sets an **inline** `max-width`, which overrides USS, so any call site that later wants a different cap still wins.
- `.zui-chip:hover` — brighter fill + full-accent border. Hover is the primary "this is clickable" signal, so it responds at least as loudly as `.zui-menu__item:hover` and `.zui-box__titlerow:hover` do.
- `.zui-chip__label` — `flex-shrink: 1` + `overflow: hidden` + `white-space: nowrap` + `text-overflow: ellipsis` + `middle-left`. No font-size override: the name is the chip's **content**, not chrome, so it stays body size (the 10px treatments in this sheet are all for chrome — `.zui-cell__name`, `.zui-mref__type`, `.zui-divider__label`).
- `.zui-chip__thumb` — an explicit **16x16** with `flex-shrink: 0`, `margin-right: 5px`, `border-radius: 2px`. 16px sits in the toolkit's existing inline-glyph gutter scale (`.zui-menu__icon` is 14px; box/section icons use a 5px right margin). The explicit size is the load-bearing part — it is what stops the Image measuring to its texture. `flex-shrink: 0` so the **name** absorbs truncation, never the picture.
- `.zui-chip__dot` — same 16x16 footprint as the thumb it replaces, `border-radius: 8px` (a true circle) plus a `--zui-line` hairline so a dark colour is still visible against the pill.
- `.zui-chip__pick` — **new class**, see below. `margin-left: 5px`, `font-size: 10px`, `opacity: 0.55`, `flex-shrink: 0`; `.zui-chip:hover .zui-chip__pick { opacity: 1 }`.
- `.zui-chip--empty` — hollow (`background-color: rgba(0,0,0,0)`, i.e. **no** reference tint at all) with a neutral `--zui-line` border, and `.zui-chip--empty > .zui-chip__label` dimmed to 0.55 and italic. See the dashed-border caveat below.
- `.zui-chip.zui-chip--drop` — accent-soft fill + accent border, and a companion rule restoring the label to full opacity / non-italic so an empty chip lights up properly when a valid asset is dragged over it. Written with **two** classes so it outranks `.zui-chip:hover` (which is also live mid-drag) and placed last so the source-order tiebreak also favours it. Only fill and border **colour** change — never border width or padding, which would reflow the pill's contents by a pixel every time a drag passes over.

**`Assets\Packages\Laubrary\Zui\Toolkit\ZuiChip.cs`** — two changes, both additive:

- A trailing `Label("▾")` with class `zui-chip__pick` and `pickingMode = PickingMode.Ignore`, added once in the constructor. **No public API changed** (`Set`, `Thumbnail`, `SetColourDot`, `MaxW`, `OnActivate`, `OnContext`, `Accepts`, `OnDrop` are untouched) and **no call site changes**. It stays trailing for free because `Thumbnail` and `SetColourDot` both `Insert(0, …)` ahead of the label. `PickingMode.Ignore` matters: the whole pill is the click target, so the caret must never swallow the `PointerDownEvent` that drives `OnActivate`/`OnContext` (the same reason `_label` already ignores picking).
- The `Set(…)` doc comment's promise of a **dashed** border corrected to describe what is actually rendered.

## Why a caret is the "it's clickable" signal

The rulebook's "Label = action" section and the handover walk's missing-affordance theme both say a cold user must be able to tell at a glance that a control opens something. Tint and hover alone cannot carry that — hover only helps a user who **already suspects** the thing is interactive, and the tint on its own is what got read as a text field. A persistent glyph is the only always-visible option.

I chose a caret over a bespoke picker glyph (an ObjectField-style circle-select) for two reasons: every other ZUI chooser already draws `▾` for "there is more here when you click" (`ZuiBox.cs:128/182`, `ZuiFoldCard.cs:69/77`, `ZuiManagedRef.cs:49/91`), so the chip joins an existing language instead of inventing a second one; and `▾` is **proven to render in the editor font** in this project, whereas `⊙`/`◎` are not and a missing glyph would render as a tofu box — a worse affordance than none. The caret also stays visible in the empty state, which is precisely when the user most needs to see the thing is pickable.

## Anti-stretch guard

`.zui-root .zui-chip { align-self: flex-start }`, with `align-self: center` for the `.zui-row` / `.zui-field` / `.zui-hgroup` / `.zui-box__titlerow` / `.zui-section__header` contexts where the cross axis is height and the row's vertical centring must survive. This is the exact shape of the existing `.unity-toggle` and `.zui-togglebutton` guards in the same file, copied deliberately.

It is needed for the documented reason: `flex-grow` governs only the **main** axis, and a `ZuiChip` is a bare `VisualElement` sitting in a UITK container that defaults to `align-items: stretch`, so in a `ZuiBox`/`ZuiSection` body it fills the whole pane regardless of `flex-grow: 0`. That is the same failure that stretched every `Z.Toggle` across ~15 tools in the 2026-08-24 incident, and a chip is used in as many places, so it would have reproduced at the same scale.

The `zui-audit-allow-stretch` opt-out convention **does** exist (`ZuiAudit.cs:95-98, 108`; used by `LatheWindow.Solids.cs:75`, `LauminaryBrowserWindow.cs:388`, `LauminationBuilderWindow.cs:607`, `PyreWindow.cs:846`, `TapestryWindow.Layers.cs:77`) and is respected: `.zui-root .zui-chip.zui-audit-allow-stretch { align-self: stretch }`. It ties the row selectors on specificity (three classes each), so it is declared **last** among the align-self rules and wins on source order — that ordering is load-bearing, do not move it.

## `LauAssetElement` and blank textures — one real risk, reported not fixed

`Assets\Packages\Laubrary\Editor\AssetKit\LauAssetElement.cs:30-31` reads `LauAssetGridGUI.GetThumbnail(current, null, thumbCache)` and assigns it only `if (tex != null)`, so `ZuiChip.Thumbnail`'s null-removes-the-Image guard is correctly honoured at that level.

The risk is one level down, at **`Assets\Packages\Laubrary\Editor\AssetKit\LauAssetGridGUI.cs:25`**:

```csharp
return AssetPreview.GetAssetPreview(item);   // Unity-owned — never cached or destroyed here
```

Two problems, neither of which I can settle without the editor:

1. `AssetPreview.GetAssetPreview` returns a **non-null but not-yet-rendered** texture path for some asset types, and for a plain `ScriptableObject` with no custom preview Unity can hand back a preview that is effectively blank/transparent rather than null. That is a **non-null, empty** texture — exactly the case that defeats the `Thumbnail == null` guard and produces the blank square the LauAsset thumbnail rule forbids.
2. It is also **asynchronous and uncached** (the comment on that line acknowledges it is never cached). It returns null on the first call while the preview is still generating, then a texture on a later rebuild — so the same chip can render with no image gutter one frame and with one the next. Non-deterministic layout for the same asset.

The clean fix is a readability/emptiness check at that line (or routing non-visual LauAssets past the preview path entirely, per the rulebook's "non-visual asset → no thumbnail slot at all"), but `LauAssetGridGUI.cs` / `LauAssetElement.cs` are outside my edit scope so I have **not** touched them. Routing this is the PM's call.

Note also that `LauAssetElement.Build` reads `current.name` and the chip caches nothing, so a renamed asset only updates on the next rebuild — pre-existing, unrelated to this change, mentioned only so it is not mistaken for regression.

## Blast radius

- **31** matching lines across **11** files for `grep -rn "ZuiChip\|LauAssetElement.Build" Assets --include=*.cs`.
- Of those, **23** are live `LauAssetElement.Build(…)` call sites — i.e. 23 chips authored by hand — in Cartographer (`CartographerWindow`, `PropWindow`, `PropWindow.Stamper`), Chunks (`ChunkWindow`, `ChunkWindow.PyreSpawn`, `ChunkWindow.Slicer`), SpriteFx (`SpriteFxFilterEditor`) and Zoetrope (`ZoetropeWindows`).
- Plus `LauAssetHook.cs:45`, which routes **every** reflected/serialized `LauAsset` reference through the same builder — so the real on-screen count is materially higher than 23 and effectively covers every tool that reflects a LauAsset field.
- Every one of them is affected with **zero source changes**: all styling is class-driven and the caret is added inside the constructor.

## Verified without the editor

- Every class `ZuiChip.cs` adds now has a rule. Cross-checked mechanically: `grep -o 'zui-chip[a-z_-]*' ZuiChip.cs | sort -u` yields exactly `zui-chip`, `zui-chip--drop`, `zui-chip--empty`, `zui-chip__dot`, `zui-chip__label`, `zui-chip__pick`, `zui-chip__thumb` — and all seven are matched by selectors in the sheet. No new instance of the bug being fixed.
- No call site anywhere in `Assets` adds its own `zui-chip*` class, so there is no orphan modifier left unstyled.
- USS syntax: braces balance (121/121), and **every property name used in the new block already appears elsewhere in this same file** (`align-items`, `align-self`, `background-color`, `border-color`, `border-radius`, `border-width`, `flex-direction`, `flex-shrink`, `font-size`, `height`, `margin-left`, `margin-right`, `max-width`, `opacity`, `overflow`, `padding-*`, `text-overflow`, `white-space`, `width`, `-unity-font-style`, `-unity-text-align`) — i.e. all of them are known-good against UI Toolkit in this project, none invented.
- Encoding/line-endings: both files decode as clean UTF-8 (no BOM); `ZuiToolkit.uss` is 100% CRLF with no bare LF introduced, `ZuiChip.cs` is 100% LF as it already was. `git diff --stat` shows 25 changed lines in the `.cs` and 209 added in the `.uss`, confirming no whole-file rewrite.
- Specificity reasoning was done by hand and is documented inline where it is load-bearing (`--drop` beating `:hover`; `allow-stretch` beating the row rules by source order).

## NOT verified — needs the editor / a human eye

- **Nothing has been rendered.** USS is not compiled, so no automated check here can prove UI Toolkit accepts the block; only a laid-out window can. The single highest-risk item is `border-radius: 9px` combined with `overflow: hidden` — UITK's rounded clipping is fine in principle (`.zui-microslider` already pairs radius with overflow) but the pill is taller relative to its radius, so the corners should be eyeballed.
- **The 220px default cap is a judgement call made blind.** It is the same number `.zui-mref__type` uses, but nobody has seen a real Chunks/Zoetrope row at it. If common asset names truncate too eagerly, raise it here (one line) rather than per call site.
- **Whether `▾` reads as "picker" rather than "dropdown"** to this user specifically. It is the toolkit's existing chooser glyph, but it is a semantic call the user may want to overrule.
- **The empty state cannot be dashed.** UI Toolkit has no `border-style` property, so `ZuiChip`'s own doc comment promised something USS cannot express. It is rendered instead as hollow + neutral hairline + dimmed italic name. Whether that reads strongly enough as "unfilled" is an eyeball question. Do **not** "fix" it by adding `border-style: dashed` — it is silently dropped.
- **Drag-and-drop feedback** (`--drop`) has not been exercised by an actual drag.
- **`ZuiAudit` does not know about chips.** Its cross-axis stretch check is hardcoded to `zui-togglebutton` (`ZuiAudit.cs:108`) and its BaseField check to `unity-base-field`, so a chip that regains the stretch bug in future would be invisible to the audit exactly as the toggle was. Extending that check to `zui-chip` is the right follow-up, but `ZuiAudit.cs` was outside my edit scope this task.
- **The 64px-band symptom is diagnosed, not observed fixed.** The explicit `.zui-chip__thumb` size is the correct cause-level fix, but confirm the Chunks row now measures roughly 16px-tall content inside a ~20px pill rather than a 79px block.
