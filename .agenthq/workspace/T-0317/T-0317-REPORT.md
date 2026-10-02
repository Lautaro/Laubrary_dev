# T-0317 — Chunks + Launimator: 21 greyed controls now say WHY, not just WHAT (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, code-only (no Coplay, no Unity CLI, no compile, no commit — per ShaperHarmony RULES.md and the card). Files touched, tooltip/greying text only:

- `Assets/Packages/Laubrary/Editor/Launimator/LauminaryBrowserWindow.cs`
- `Assets/Packages/Laubrary/Editor/Launimator/LauminationBuilderWindow.cs`
- `Assets/Packages/Laubrary/Editor/Chunks/ChunkWindow.cs`

No `.meta`, no serialized field, no `CHANGELOG.md`, no Pyre file touched.

## Pattern followed

Copied the idea behind Shaper's `Inert()` / `InertVal()` helpers (`Editor/Shaper/ShaperWindow.Sections.cs:190-232`) — never referenced that assembly. The shape: a control's tooltip is its **effect description while enabled**, and is **overwritten with the reason while disabled** ("X, so Y" — the condition that's currently true, then what it means). No new helper type was introduced across assemblies (Launimator/Chunks each have no such helper today); each site sets `.tooltip` directly, conditioned on the same boolean already driving `.SetEnabled(...)`, with a `TrackPropertyValue`/local-function keeping it live where the state can change without a full window `Rebuild()` (the Chunks sprite list, and the Launimator URL field).

## The 21 controls — before → after

### Lauminary Browser (4) — all gated on "nothing selected in the list above"

| # | control | before (what it does, always) | after, while greyed |
|---|---|---|---|
| 1 | `Duplicate` button | "Create a copy of the selected lauminary (draft only)." | "No lauminary is selected, so there is nothing to duplicate." |
| 2 | rename name field | "New name for the selected lauminary." | "No lauminary is selected, so there is nothing to rename." |
| 3 | `Rename` button | "Rename the selected lauminary." | same as #2 |
| 4 | `Delete…` button | "Delete the selected lauminary and every one of its versions (asks first)." | "No lauminary is selected, so there is nothing to delete." |

The user can always change this condition (select a row in the list); no "cannot change" case here.

### Laumination Builder (14)

| # | control | before | after, while greyed | user-changeable? |
|---|---|---|---|---|
| 5 | `Restore` | "Reload slicing state from this sheet's sidecar." | no sheet: "No sheet is loaded, so there is no saved slicing to restore or clear." / sheet but no sidecar: "This sheet has no saved slicing sidecar yet — nothing has been Saved for it." | yes — load a sheet, then Save once |
| 6 | `Clear` | "Delete this sheet's saved slicing sidecar…" | same two-case text as #5 | yes |
| 7 | `Download` | "Save the image to Assets/SpriteSheets and load it as the sheet." | "Type a URL above first — there is nothing to download yet." | yes, immediately (typing) |
| 8 | transparent-colour swatch | "The colour treated as transparent." | "\"BG color\" below is off, so no background colour is being keyed." | yes — flip the BG color toggle |
| 9 | tolerance int field | "Per-channel match tolerance (0..255)." | same as #8 | yes |
| 10 | marquee `L` field | "Marquee left edge, in source pixels." | "No marquee is drawn on the canvas yet — drag one out first." | yes — drag on canvas |
| 11 | marquee `T` field | "Marquee top edge…" | same as #10 | yes |
| 12 | marquee `W` field | "Marquee width…" | same as #10 | yes |
| 13 | marquee `H` field | "Marquee height…" | same as #10 | yes |
| 14 | `Clear Box` | "Drop the current marquee." | same as #10 | yes |
| 15 | `Add Region (0)` | "Commit the marquee's grid cells into the sprite palette (undoable)." | same as #10 | yes |
| 16 | `Edit in Aseprite` | "Export the selected sprite(s)…" | "No sprite is selected in the palette below, so there's nothing to send to Aseprite." | yes — select a sprite |
| 17 | `Sync edits` | "Pull the edited .aseprite back…" | "Nothing has been sent to Aseprite yet with 'Edit in Aseprite', so there's nothing to sync back." | yes — use Edit in Aseprite first |
| 18 | `Reverse` | "Reverse the order of the selected frames." | 0 selected: "No frames are selected in the sequence below, so there's nothing to reverse." / 1 selected: "Only one frame is selected — reversing needs at least two." | yes — select ≥2 frames |

### Chunks (3) — one root cause

| # | control | before | after, while greyed |
|---|---|---|---|
| 19 | `Sprites` list foldout header | (inherited the field's static tooltip) | "The list is empty, so there is nothing here to fold or resize by typing — use the + below to add a sprite." |
| 20 | `Sprites` list (outer `PropertyField`, counted separately by the probe) | same static tooltip | same reason as #19 |
| 21 | the list's `0` size TextField | (inherited the same static tooltip) | same reason as #19 |

**Root cause, read from the code, not assumed:** `Editor/Chunks/ChunkWindow.cs` never calls `SetEnabled` on this `PropertyField` or anything inside it (grepped — confirmed absent). The greying is Unity's own `ListView`: it disables its foldout header when the bound array is empty (nothing to fold), and the header's size `TextField` is its child, so it inherits the disabled state through `enabledInHierarchy`. All three inert leaves had no tooltip of their own, so UI Toolkit's tooltip bubbling fell back to the one tooltip on the outer `PropertyField`, which described the field's *purpose*, not why the header/size are dead right now. Fixed at the one place that actually reaches all three: `listField.tooltip` is now computed from `spritesProp.arraySize`, refreshed live via `TrackPropertyValue` (growing the list through the ListView's own `+` footer button doesn't trigger a window `Rebuild()`, so a one-time set at construction would have gone stale).

Chunks' 4th named control, `Save`, was already correct ("This Chunk matches what is on disk — nothing to save.") and was not touched.

## Self-review (bucket: self-reviewed, not compiled)

- Re-read every diff hunk in the three files above end-to-end (shown in full to the PM alongside this report).
- Grepped `Editor/Chunks/*.cs` and `Editor/Launimator/*.cs` for every symbol touched (`SetEnabled`, `listField`, `spritesProp`, `hasSelection`, `clearBoxButton`, `SetDownloadEnabled`) to confirm no other call site references the renamed/introduced locals and nothing was left dangling.
- Confirmed `UnityEditor.UIElements` is already imported in `ChunkWindow.cs` (needed for `TrackPropertyValue`).
- Confirmed every edited control's `.SetEnabled(...)` boolean is unchanged — only the tooltip text is now conditional on that same boolean (or, for Chunks, on the array size the built-in `ListView` itself reacts to). No new behaviour, no new control, no field/enum/serialized change.
- Did **not** touch the single line in `Editor/Chunks/ChunkWindow.cs` that builds its toggle bar, per the card's carve-out for the sibling agent.
- **Not verified:** not compiled (code-only task, no Unity CLI); not opened in the editor; no human/screenshot has looked at any of these tooltips live. The `TrackPropertyValue` callback's live-update behaviour for the Chunks sprite list is read from the API contract, not exercised.

## Proposed probe-library rule change (T-0312's `inert` rule) — NOT applied, per instructions

T-0313's finding: `ZAudit`'s inert rule (and every "0 disabled without a reason" claim built on it, including T-0312's) counts a disabled control as `inertWithReason` the moment it has **any** non-empty effective tooltip — so a tooltip that only restates the control's normal effect ("Reverse the order of the selected frames.") scores identically to one that states why it's dead ("No frames are selected…"). This task's fixes make the distinction real for these 21 controls, but the rule itself still can't tell the difference, so a future control can regress silently and still audit clean.

**Proposed tightening**, to hand to whichever agent owns `workspace/T-0312/probes/zlib.cs` / `Zui/Toolkit/ZuiAudit.cs`:

> A disabled control's tooltip counts as `inertWithReason` only if it is **state-dependent** — i.e. the tooltip text captured while the control is disabled is **different from** the tooltip text the same control would show enabled (or, cheaper to check mechanically: different from a static/constant string literal passed at construction). Concretely: the probe already walks each state twice in this programme (T-0313's own tables re-audit before/after); extend that walk to capture each disabled control's tooltip in **two adjacent states that flip its `SetEnabled` boolean** (naturally occurring in almost every audit sweep already, since the programme re-samples empty/bound, 0/N-selected, etc.) and flag `inertNoReason` whenever the tooltip string is IDENTICAL across both samples despite the enabled state differing — that is exactly the signature of "the tooltip describes the effect, not the reason," since a real reason necessarily changes wording when the condition that produced it goes away. A single-state audit that never observes the flip should keep reporting `inertWithReason` (conservatively — it has no evidence either way) rather than guessing from wording alone (regex-matching for "X, so Y" would be brittle and English-specific).
>
> Cheaper interim signal if a two-state walk isn't practical everywhere: flag `inertNoReason` when the disabled tooltip textually matches (case-insensitive, punctuation-insensitive) the tooltip already declared as a `const`/literal at the same call site for the ENABLED case — i.e. when disabling didn't change the string at all. This catches the exact bug pattern found here (Lauminary Browser's four controls, Chunks' `Sprites`) without needing a second sample, at the cost of missing a reason that happens to read identically to the effect by coincidence (rare, and a false negative is the safe failure direction for this rule).
>
> Either way: **reword every "0 disabled without a reason" claim in the programme's existing write-ups** (T-0312's included) to "every greyed control has a tooltip" until the rule is tightened — the two are not currently the same claim, and this report is the second time that gap has produced a real miss (18 of Launimator's 18 relevant controls, plus Chunks' one root cause reaching 3 leaves).

This section is a proposal only — `ZuiAudit.cs` and `workspace/T-0312/probes/zlib.cs` were not edited, per the card (another agent's deliverable).

## CHANGELOG

`CHANGELOG:` Chunks + Launimator: 21 greyed controls (Lauminary Browser ×4, Laumination Builder ×14, Chunks' Sprites list ×3) now say WHY they're disabled instead of restating what they do when enabled.
