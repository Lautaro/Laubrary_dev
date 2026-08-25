# Chunks window — UI audit (T-0081)

Read-only audit. Sources: `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Editor\Chunks\*.cs` (14 files, 4323 lines), the two screenshots in `.agenthq\attachments\T-0081\`, `C:\Users\Lauta\.claude\skills\laubrary\references\ui-layout-rules.md` (read in full), `C:\Users\Lauta\.claude\skills\laubrary\references\zui.md`, plus the ZUI source under `Assets\Packages\Laubrary\Zui\` to verify every control named in a proposed fix actually exists. No Unity editor was touched; nothing was modified.

---

## 0. What a cold user actually sees in the screenshot

`.agenthq\attachments\T-0081\20260824T195609Z_FloatingDiscBlowup_Authoring.png` (window title `ChunksShotProbe` — a probe harness rendering the real builders), top to bottom:

1. An asset toolbar: `Floating Disc Blowup (Chunk ⊙)` · New · Browse · Duplicate · Rename · Delete.
2. A green section heading **Tags**, whose entire body is one row reading `Tags   · none ·`. The row's only affordance (a `Tags…` button, `LauAssetField`/`LauTagField.cs:33`, pushed right by a `GUILayout.FlexibleSpace`) is not visible anywhere in either screenshot.
3. Immediately below it, floating in the middle of the pane with no label attached to it, the words **`Proper Blast`** in plain grey text. Above and around it, roughly 80–120 px of empty dark rectangle.
4. A box **`▾ Pool`** containing one button **`+ Add blast…`**.
5. A lone latched button **`Several`** occupying a full-width row on its own, ~90% of the row empty.
6. A box **`▾ Shape & stagger`**: `Shape [Line|Ring]` + `Count 4`; `Radius 0.5` + `Arc ° 360`; `Start angle ° 20` **alone on its own row**; `Scatter / Stagger s / Jitter`; `Order [Sequential|Reverse|From Centre|Random]`; `Shape seed 11`.
7. Un-boxed rows: `Rotation [Burst|Fixed|Random]` + `Angle range ° 0 …… 360`; `Scale 0.8—1.05` + `Seed 11`; **`Centre offset`** rendered as a thin ~120×18 px horizontal strip with a single cyan dot and a `…` button; `Layer slot [Back Blast|Between|Fragments|In Front]`.
8. A green section heading **`Blast 3 — In front`** with a ticked checkbox.
9. `Name [In front]` — a real, editable, boxed **text input**.
10. ~90 px of empty dark space.
11. The word **`Blast`** at the far left margin.
12. ~60 px more empty space, then **`Proper Blast`** again as plain grey text, indented to roughly x=175.
13. `▾ Pool` / `+ Add blast…`, `Several`, `Shape & stagger` … the entire block from step 4 repeats verbatim.

**The single most important thing a cold user sees: two blocks of identical-looking dials, separated by a heading that says "Blast 3", with no visible way to add a fourth or delete this one, and asset references rendering as bare unstyled words that look exactly like the contents of the `Name` text box two rows above them.** That is precisely the reading the user reported, and it is correct — see D-01 and D-02.

The earlier screenshot (`20260824T191131Z_…`, window `ChunkProof`) shows a pre-fix state where the "Several" switch sat *inside* a box also titled "Several" (the double-word case `ChunkWindow.PyreSpawn.cs:178-180` was written to fix). Current code matches the later screenshot.

---

## 1. The user's literal complaints

### 1a. "Why is there a section called Blast 3? What if user wants less or more?"

#### D-01 — CRITICAL — A blast group cannot be removed, reordered or renamed unless it is switched ON, and even then the affordance is buried inside the body

`ChunkWindow.PyreSpawn.cs:63` (`if (!m.enabled) return;`) and `ChunkWindow.PyreSpawn.cs:65` (`if (extra) s.Add(BuildBlastGroupIdentityRow(c, m, index));`).

**What a user sees:** the ▲ / ▼ / × buttons for a blast group live on the `Name` row *inside the section body*. The body is only built when the group's header checkbox is ticked (`:63`). So the moment a user unticks "Blast 3" to audition the effect without it, **the group becomes undeletable and unmovable** — there is no × anywhere. Folding the section (clicking the header) hides them too. The `×` is also two levels deep from the identity it belongs to.

**Rule violated:** `ui-layout-rules.md` → "Card layout" #1: *"The header carries identity + the one control every card has, and nothing else. Fold caret, grip, mute, icon, the card's NAME, then that universal control, then a flexible gap, then the remove ×."* The remove × belongs on the header, unconditionally. Also HANDOVER WALK step 3 (reachability): a step that cannot be performed at all is a missing feature.

**Fix:** `ZuiSection` has no `AddHeaderContent` (that is `ZuiBox.AddHeaderContent`, `Zui/Toolkit/ZuiBox.cs:73`), but it does have **`ZuiSection.SetHeaderMenu(iconName, tooltip, open)`** (`Zui/Toolkit/ZuiSection.cs:131`) — already used by Pyre at `PyreWindow.cs:1134`. Give every blast section a header menu (`Z.Menu` → `.Item("Move up"/"Move down"/"Duplicate blast"/"Remove blast")`), built for **both** the enabled and disabled cases, before the `:63` early-out. Better still, adopt D-04's list model, where the × is a per-row control and the problem cannot recur.

#### D-02 — CRITICAL — There is no discoverable "add a blast" affordance, and the one that exists is named almost identically to an unrelated button

`ChunkWindow.PyreSpawn.cs:37` + `:263-279` (`BuildAddBlastGroupRow`, label `"+ Add another blast…"`) vs `ChunkWindow.PyreSpawn.cs:359` (label `"+ Add blast…"`, inside every blast's `Pool` box).

**What a user sees:** the button that creates a new blast group sits **after every blast section has been drawn in full** (`:31-37` builds blast 1, then each group, *then* the add row). With three enabled blasts, each ~500 px tall, "+ Add another blast…" is roughly 1500 px below the first thing that mentions blasts, and it is not inside any section — it is a naked button between the last blast and "Pyre Movement". Meanwhile a button called **`+ Add blast…`** is visible inside *every* blast's `Pool` box, three or four screens higher. The two labels differ by one word and do completely different things (`+ Add blast…` adds a random-pick alternative *within* the current blast; `+ Add another blast…` creates a whole new depth-layered blast). The screenshot shows `+ Add blast…` prominently and `+ Add another blast…` nowhere — which is exactly why the user asked "what if user wants less or more?".

**Rules violated:** HANDOVER WALK step 3 ("you cannot name where they click FIRST" / a step that requires being told is a missing affordance) and step 4 (the empty state is a first-class screen). Also "Label = action": on an empty spec the label reads "Add **another** blast" when there is no first one enabled.

**Fix:**
- Rename the pool button to something that cannot be confused with creating a blast: `"+ Add alternative…"` or `"+ Add to pool…"` (`:359`).
- Move blast creation to a **list at the top of the blast area** (see D-04), not to a trailing button. If the list model is deferred, at minimum put the add button *above* the blast sections as well, and word it `"+ Add blast…"` / `"+ Add blast (its own depth)…"` consistently regardless of how many exist.

#### D-03 — HIGH — The numbering is genuinely off by one and the first blast is not called a blast at all

`ChunkWindow.PyreSpawn.cs:48`:
```csharp
string title = extra ? "Blast " + (index + 2) + " — " + m.DisplayName : "Pyre Spawn";
```
Same `+2` at `ChunkWindow.Layers.cs:451` (`$"Blast {i + 2}: …"`), `ChunkWindow.Timeline.cs:186` (`$"{i + 2}. …"`) and in the runtime key `ChunkModules.cs:73-74` (`BlastGroupTrack => "Blast " + (index + 2)`).

**What a user sees:** blast #1 is headed **"Pyre Spawn"** (an implementation name — the module class — not "Blast 1"). `blastGroups[0]` is headed "Blast 2". `blastGroups[1]` is "Blast 3". So with two extra blasts the window shows *Pyre Spawn, Blast 2, Blast 3* — the user counts three blasts and sees the numbers 2 and 3. There is no "Blast 1" anywhere in the product. That inconsistency is what the complaint is reacting to: the number is neither an ordinal the user chose nor one that matches what they see.

**Rules violated:** `ui-layout-rules.md` → "Be consistent across the codebase" (the same KIND of thing must present the same way); "Labeling — tooltip, not title" (a title should be short, literal and true).

**Fix (UI-only, no data migration):** title the first blast **"Blast 1 — <DisplayName>"** and the extras **"Blast 2…"**, `"Blast " + (extra ? index + 2 : 1)`. Do **not** change `ChunkModules.BlastGroupTrack` — that string is a persisted timeline key (`ChunkWindow.PyreSpawn.cs:320` matches `StartsWith("Blast ")` when re-keying), and renumbering it would orphan authored delays. The window label and the stored key are allowed to differ; the code comment at `ChunkModules.cs:62-72` already argues for exactly that separation. In the same pass, give the Layer-Stack row (`Layers.cs:451`) and the timeline lane (`Timeline.cs:186`) a row for blast 1 too, so all three places agree.

#### D-04 — HIGH — The window has no *list* of blasts; N blasts means N full-length stacked sections

`ChunkWindow.PyreSpawn.cs:29-38`.

**What a user sees:** each blast is a full section with ~15 controls, a Pool box and a Shape & stagger box. Authoring the user's stated goal ("one explosion behind the pieces, several between, several in front") produces 5–7 of these stacked vertically. Reading "what is in this effect" means scrolling several thousand pixels; comparing two blasts is impossible because they are never on screen together.

**The comparison the user asked for — how Pyre does it:** `PyreWindow.cs:773-798` (`BuildLayerList`) draws a compact **one-row-per-layer stack** at the top — `≡ grip · enable toggle · ●/○ select · inline rename field · state indicators · Dup · ✕` — plus a `+ Add layer` / `Duplicate` row (`:787-789`), and the *selected* layer's dials render in the sections below (`layerSel`). Twelve layers cost twelve rows, not twelve panels.

**Fix (the main structural recommendation):** replace the N-sections model with the Pyre model.
- One `Z.Section("Blasts", …)` holding a row per blast, built from `Z.Row` + `ZuiReorder.MakeGrip` (exists, `Zui/Toolkit/ZuiReorder.cs`, already used at `Layers.cs:167` and `Modifiers.cs:82`) + `Z.Toggle("")` + a `Z.Button("●"/"○")` select + a `ZuiChip` for the blast asset + a small `Z.Text` showing the resolved layer slot + `Dup` + `×`; then `Z.Button("+ Add blast…")` / `Z.Button("Duplicate")` under the list.
- One `Z.Section("Blast — <name>", …)` below it holding the selected blast's dials, built by today's `BuildBlastSection` body verbatim.
- This resolves D-01, D-02, D-03, D-10 and D-11 at once, and adds the missing **Duplicate** (there is no way today to copy a tuned blast — see D-11).

#### D-05 — MEDIUM — Every blast's `Pool` box shares ONE view/fold key, so folding one folds them all

`ChunkWindow.PyreSpawn.cs:338-341`:
```csharp
var box = Z.BoxKeyed("Pool", "…", "chunks.pyrespawn.pool");
```
`BuildPyreSpawnPool` is called for the first blast *and* every group (`:80`), always with the literal key `"chunks.pyrespawn.pool"`. Compare `BuildBlastSeveral` (`:189-191`), which correctly keys per index.

**What a user sees:** collapsing "Pool" on Blast 3 also collapses it on Blast 2 and on Pyre Spawn; a saved view captured against one is applied to all.

**Rule violated:** `ui-layout-rules.md` → "A view-captured box → `Z.BoxKeyed(title, tooltip, "stable.key")`" — the key must be stable *and unique per instance*.

**Fix:** thread the index through, `(index >= 0 ? "chunks.blastgroup." + index : "chunks.pyrespawn") + ".pool"`, exactly as `:191` already does for the Several box.

#### D-06 — MEDIUM — An unresolved layer-slot name silently shows nothing selected in a blast, but is shown honestly in the Fragment Slicer

`ChunkWindow.PyreSpawn.cs:418` (`Z.MiniRadio(stack.IndexOf(m.layerName), names, …)`) vs `ChunkWindow.Slicer.cs:231-239`, which appends the stale/unknown name as a visible `"(none)"`/`"<name>"` option so the user can see what the asset actually stores.

**What a user sees:** a blast whose `layerName` no longer matches any declared slot renders a radio strip with **nothing latched**, and no explanation. The blast silently falls back to the flat emitter order at runtime.

**Rule violated:** "Be consistent across the codebase"; and the layer-stack file's own doctrine (`Layers.cs:112-118`) that a broken reference must be *shown*, not snapped away.

**Fix:** copy the Slicer's shape into `BuildPyreSpawnLayerSlot` — append the stored name with a "(missing)" suffix and latch it.

#### D-07 — MEDIUM — Newly added blasts all land in the same slot, defeating the feature they were added for

`ChunkWindow.PyreSpawn.cs:274-276` creates `new PyreSpawnModule { enabled = true, source = picked }`; `PyreSpawnModule.cs` field default is `public string layerName = "Blast";`.

**What a user sees:** add three blasts to get "one behind, several in front" and all four (the first plus three) sit in the slot named `"Blast"` — i.e. at identical depth. Nothing on screen says so at the moment of creation; the user has to scroll to Layer Stack → Modules to discover it. On a *fresh* spec the stack is empty (`LayerSpec.layers = new List<string>()`), so `BuildPyreSpawnLayerSlot` returns `null` (`:408`) and **there is no depth control in the blast section at all**.

**Fix:** when adding a blast, offer the depth in the same gesture the way `Layers.cs:552-566` (`ShowNewSlotForModuleMenu`) already does for a module — a `Z.Menu` on "+ Add blast…" with `Behind everything` / `In front of everything` / `Same depth as the others`, declaring the slot and assigning it in ONE `Dial` (the `AddSlotForModule` helper at `Layers.cs:570` already exists and does exactly this). This is the shortest path from the user's sentence ("this one behind, those in front") to the data.

---

### 1b. "I see textinputs where the user has to type in what appears to be names of assets"

There are exactly **three** `Z.TextInput` call sites in the whole Chunks editor (verified by grep across all 14 files):

| # | Site | Classification | Verdict |
|---|---|---|---|
| 1 | `ChunkWindow.Layers.cs:207` — a layer slot's name | (i) legitimately DECLARING a new name | Correct. `isDelayed`, and renames cascade to every reference via `RepointModuleLayer` (`:253`). Keep. |
| 2 | `ChunkWindow.PyreSpawn.cs:142` — a blast group's `label` | (iii) legitimate declaration that **reads** like an asset field | See D-08. |
| 3 | `ChunkWindow.Timeline.cs:264` — a Code Event's hook name | (i) legitimately DECLARING a name | Correct; the sibling Zound slot (`:283`) is properly a picker and explicitly refuses to degrade to a text field. Keep. |

So the user is **not** literally being asked to type an asset name anywhere. What is happening is worse in one sense and easier to fix in another: the *pickers* have lost their visual identity and now render as bare text, so the user cannot tell a picked reference from typed text. That is D-09, and it is the root cause of the complaint.

#### D-08 — HIGH — The `Name` field reads as "type the asset's name here"

`ChunkWindow.PyreSpawn.cs:139-144`.

**What a user sees:** a section headed `Blast 3 — In front`; directly under it a boxed, editable field labelled **`Name`** containing `In front`; below that, a large empty area, then the word `Blast`, then `Proper Blast`. The reasonable reading is "Name = what this blast is called, i.e. the asset" — especially since `PyreSpawnModule.DisplayName` (`PyreSpawnModule.cs:221-233`) *does* fall back to the blast asset's own name when the label is empty, so the field genuinely does show an asset name by default.

Compounding it: in the screenshot the user has typed `In front` — which is also the name of a **layer slot** shown two rows further down (`Layer slot [Back Blast|Between|Fragments|In Front]`). The name field is being used to restate the depth, because the depth is not visible on the header. That is a symptom of D-01/D-03, not of the text field itself.

**Rules violated:** "Labeling — tooltip, not title" (a label must be literal about what it names); and Card-layout #1 (identity belongs on the header, where the user already read "Blast 3 — In front" — the field then restates it as a second, editable copy of the same string).

**Fix, in order of preference:**
1. **Move the name onto the header row** as an inline rename field, exactly as Pyre does at `PyreWindow.cs:836-848` (`Z.TextInput` with `zui-audit-allow-stretch`, the rulebook's sanctioned name-field stretch exception). One string, one place, no duplicate row, no dead space. In the list model (D-04) this is free.
2. If it must stay in the body, relabel it **`Label`** or **`Group name`** (never bare `Name` next to an asset picker) and put it on the same row as the blast chip, not on a row of its own.
3. Auto-derive harder: when the label is empty, show the resolved `DisplayName` as placeholder-ish subtle text rather than an empty box, so "empty means it uses the blast's own name" is visible instead of documented in a tooltip.

#### D-09 — CRITICAL — `ZuiChip` has **no stylesheet at all**, so every asset reference in Chunks renders as a giant blank image stacked above bare grey text

This is the root cause of the user's "textinputs … names of assets" reading, and it is systemic, not a Chunks bug.

Evidence:
- `Zui/Toolkit/ZuiChip.cs` adds the USS classes `zui-chip`, `zui-chip__label`, `zui-chip__thumb`, `zui-chip__dot`, `zui-chip--empty`, `zui-chip--drop` (`:46-47`, `:105`, `:122`, `:137`).
- `grep -rn "zui-chip" --include=*.uss Assets/` returns **nothing**. The only file in the entire project that mentions `zui-chip` is `ZuiChip.cs` itself. `Assets/Packages/Laubrary/Zui/Toolkit/ZuiToolkit.uss` is the single stylesheet, and it has no chip rules.
- Consequence 1: `ZuiChip` is a plain `VisualElement`, whose UITK default `flex-direction` is **column**. `Thumbnail` does `Insert(0, _thumb)` (`:123`), so the image is placed **above** the label, not beside it.
- Consequence 2: the `Image` has no width/height from USS, so it lays out at the source texture's natural size. `LauAssetGridGUI.GetThumbnail` (`LauAssetGridGUI.cs:17-26`) returns `IVisualPreview.RenderPreviewTexture()` or `AssetPreview.GetAssetPreview` — typically 96–128 px square. That is the ~90–120 px of empty dark rectangle in the screenshot (a Pyre-blast preview frame is mostly transparent, so it reads as *blank*, not as a picture).
- Consequence 3: no pill background, no border, no accent tint, no `--empty` dashed style. The label is an unstyled `Label`. `· none ·` (the empty state, `:104`) therefore renders as the plain grey `· none ·` seen on the Tags row.

**What a user sees:** `Blast` at the left margin, ~60 px of nothing, then the words `Proper Blast` floating in space. Identical in weight and colour to any other text in the window. There is nothing to indicate it is clickable, nothing to indicate it is a reference, and nothing to distinguish it from the contents of the `Name` box directly above it.

**Rules violated:** "LauAsset thumbnails — a thumbnail is a PROMISE, and a blank one is worse than none" (a blank square that claims a picture failed to load — here it is worse, it is 120 px tall); "Space economy" / "Vertical space is the scarce resource" (a full picture-height row per reference); the chip's own file header doctrine ("References want to be SPOTTABLE… the pill's tint and weight make 'this points at something else' visible at a glance") — which is currently false; and Root-setup/stretch expectations generally.

**Blast radius:** 24 `LauAssetElement.Build` / `new ZuiChip` call sites across 9 files — `ZoetropeWindows.cs` (5), `CartographerWindow.cs` (5), `PropWindow.cs` (3), `PropWindow.Stamper.cs` (3), `ChunkWindow.cs` (2), `ChunkWindow.PyreSpawn.cs` (2), `SpriteFxFilterEditor.cs` (1), `ChunkWindow.Slicer.cs` (1), `LauAssetHook.cs` (1). **Every Laubrary window that shows a LauAsset reference is affected.** This is one USS block, not a per-window fix — and per the rulebook's own lesson ("when a wrapper resolves to the wrong control, fix the wrapper, not the call sites"), it must be fixed in `ZuiToolkit.uss`.

**Fix:** add the missing block to `Assets/Packages/Laubrary/Zui/Toolkit/ZuiToolkit.uss`:
```css
.zui-chip { flex-direction: row; align-items: center; align-self: flex-start; flex-grow: 0; flex-shrink: 0;
            border-width: 1px; border-radius: 3px; border-color: var(--zui-accent);
            background-color: …; padding: 1px 6px 1px 4px; height: 20px; }
.zui-row .zui-chip, .zui-field .zui-chip { align-self: center; }
.zui-chip:hover { … }
.zui-chip--empty { border-color: var(--zui-line); opacity: 0.7; }  /* dashed-ish, dimmer */
.zui-chip--drop { border-color: …; }
.zui-chip__thumb { width: 16px; height: 16px; margin-right: 4px; }
.zui-chip__dot   { width: 10px; height: 10px; margin-right: 4px; border-radius: 2px; }
.zui-chip__label { -unity-text-align: middle-left; }
```
Note the `align-self` pair — the rulebook's "Stretching is a CROSS-axis bug, and `flex-grow: 0` does not prevent it" section applies verbatim: a chip in a `ZuiSection` body (a column) will otherwise stretch the full pane width. Also add a `ZuiAudit` check for a `zui-chip` whose resolved height exceeds ~24 px, so this can never silently regress again (the rulebook: "A rule that is not machine-checked will drift back").

**Also needed for the same complaint:** once styled, the chip should carry a small affordance cue that it is clickable. The file header says right-click gives New/Edit/Clear (`LauAssetElement.cs:46-75`), which is undiscoverable with no visual chrome at all; a caret or a faint "▾" inside the pill (or a `SetHeaderMenu`-style glyph) makes it findable. Flag for the user's decision — it is a ZUI-level design call, not a Chunks one.

---

## 2. Whole-window fitness — the cold HANDOVER WALK

**Goal, in the user's words:** *"this character shatters, one explosion behind the pieces, several between, several in front."*

### 2a. The empty state (a brand-new ChunkSpec, nothing enabled)

Sections built, in order (`ChunkWindow.cs:92-113`): Tags · Emission · Physics · Life/Look · Floor/Collision · Sampled Pseudo-3D Debris (+Tint +Modifiers) · Preview · Animated Content · Hit Detection · Trail · Particle Splash · Fragment Slicer · **Pyre Spawn** · [+ Add another blast…] · Pyre Movement · Spawn Formation · Layer Stack · Timeline · [Preview in Mirage]. **19 top-level sections in one unbroken vertical `ScrollView`** (`ChunkWindow.cs:87`).

| Step | What they SEE | What they CLICK | How they knew | Verdict |
|---|---|---|---|---|
| 1. "make the character shatter" | Nine sections about *debris* first (Emission, Physics, Life/Look, Floor, Sampled, Preview, Animated Content, Hit Detection, Trail), none of which shatter a character. The one that does is **Fragment Slicer**, 12th from the top. | scroll, then tick "Fragment Slicer" | Nothing. "Fragment Slicer" is a good name but it is below nine unrelated sections and there is no overview. | **GAP** — ordering. The composed-effect modules are stacked *under* the legacy plain-debris dials by explicit design (`ChunkWindow.cs:102-105`), which optimises for the old workflow at the cost of the new one. |
| 2. give it the character art | `Source Visual` (chip) and `Source Sprite` (ObjectField) | click the chip | The chip is **invisible as a control** (D-09) — it is either the word `· none ·` or the asset's name in plain text. | **GAP** — D-09. |
| 3. "one explosion behind the pieces" | The section is called **"Pyre Spawn"**. Nothing in the window uses the word "explosion" or "blast" at top level. | tick "Pyre Spawn", then click the `Blast` chip | Nothing. A user hunting for "explosion" has to guess that "Pyre Spawn" is it. | **GAP** — D-03 (name it "Blast 1"). |
| 4. put it *behind* the pieces | On a fresh spec the layer stack is empty, so `BuildPyreSpawnLayerSlot` returns `null` (`PyreSpawn.cs:408`) — **there is no depth control in the blast section at all.** | scroll to "Layer Stack" → "+ Add slot…" → "The standard stack (6 slots)" → scroll to "Modules" box → pick a slot per module | Nothing whatsoever. The blast section is silent about depth existing. | **GAP, critical** — D-07. The tooltip on the section explains it, but a tooltip is not an affordance. |
| 5. "several between" | Needs a second blast. | ??? | The `+ Add another blast…` button is the *last* thing in the blast block, below whichever blast sections are open. On an empty spec (Pyre Spawn off, so no body) it does at least sit directly under the "Pyre Spawn" header — but it says "**another**" when the user has not made a first one. | **GAP** — D-02. |
| 6. make those several fire as a ring | Two different, equally-plausible routes: the per-blast **`Several`** toggle (`PyreSpawn.cs:182`) and the standalone **`Spawn Formation`** section (`Formation.cs:26`). They use the same `SpawnFormation` type and draw near-identical dials. Turning on Spawn Formation **supersedes** the first blast entirely (`ChunkModules.cs:49-52`). | one of them, coin-flip | Nothing. Only the first blast prints a warning when superseded (`PyreSpawn.cs:125-127`); the Spawn Formation section's own tooltip explains it, invisibly. | **GAP** — D-10. |
| 7. see it | `Preview in Mirage` at the very bottom (`MiragePreview.cs:50`), + a per-blast ring preview that **does not exist** (only the standalone Formation section has one, `Formation.cs:104-110`). | scroll to the bottom | Nothing points at it from the blast sections. | **GAP** — D-12. |

**Count: 7 of 7 steps require being told.** Per HANDOVER WALK step 3, every one of those is a defect, not a workflow.

### 2b. Second pass and re-entry (walk step 5)

- Adding a blast group correctly carries timeline delays (`MutateBlastGroups`, `:300-332`) — good, and well reasoned.
- Removing a group **shifts every later group's section fold key** (`"chunks.blastgroup." + index`, `:56`), so after removing Blast 2, Blast 3's saved fold/view state is silently inherited from the removed one. Minor, but it is the same class of positional-key drift the file's own comment worries about. (Not separately numbered; fold it into D-04's list model, where selection is by object, not index.)
- The window has **no `ZuiSectionToggleBar` and no `ZuiViewBar`**, unlike Pyre (`PyreWindow.cs:402`, `:462`, `:488`). Re-entering a 19-section window means re-folding by hand every time. See D-13.

---

## 3. Rulebook violations across all panels

#### D-10 — HIGH — The same effect is authorable in two competing places, and one silently eats the other

- Per-blast "Several" + "Shape & stagger" (`ChunkWindow.PyreSpawn.cs:176-259`) vs the standalone **Spawn Formation** section (`ChunkWindow.Formation.cs:26-135`). The code comment at `PyreSpawn.cs:171-175` admits the duplication is a copy, not a call, and names the honest fix (parameterise `BuildSpawnFormation` by `(SpawnFormation, previewElement)`).
- At runtime, an enabled Spawn Formation **replaces** the first blast (`ChunkModules.cs:49-52`) but not the extra groups.
- A layer slot is authored in **three** places: the module's own section (`PyreSpawn.cs:411`, `Slicer.cs:243`), the Layer Stack's "Modules" box (`Layers.cs:493`), and implicitly again via the group's `Name` (D-08). The Layers file argues for the central box (`Layers.cs:12-14`) and the module files argue for the local picker; both are present, so the same value has two live editors on screen.
- Timing likewise: a per-blast `Stagger s` inside "Shape & stagger" (`PyreSpawn.cs:240`) and a per-blast lane delay on the Timeline (`Timeline.cs:182-194`).

**Fix:** (a) delete the standalone **Spawn Formation** section outright — every blast now has its own `Several`, so the standalone module is a strictly weaker duplicate that additionally supersedes blast 1. If it must stay for back-compat, mark it `(legacy)` in the title and make the supersede warning a *reserved* status line on the blast section rather than an element that appears and disappears (`PyreSpawn.cs:126` currently adds/removes a `Z.Text`, which reflows everything below it — a "Stable workspace" violation). (b) Keep exactly one editor per layer slot: given the user's workflow ("one behind, several in front"), the *blast's own header* is the right place, and the Layer Stack "Modules" box should become a read-only overview.

#### D-11 — MEDIUM — No way to duplicate a blast

Nothing in `ChunkWindow.PyreSpawn.cs` offers Duplicate. Authoring "several between and several in front" means hand-configuring 4–6 near-identical blasts (count, radius, arc, scatter, stagger, order, seed, scale, rotation…). Pyre has both a per-row `Dup` (`PyreWindow.cs:887`) and a toolbar `Duplicate` (`:789`).
**Fix:** per-blast `Dup` in the list row (D-04) or in the header menu (D-01), deep-copying the `PyreSpawnModule` and inserting it just after, one `Dial`.

#### D-12 — MEDIUM — The per-blast formation has no preview; the standalone one does

`ChunkWindow.Formation.cs:104-110` gives the standalone module a live 150 px canvas showing every resolved point coloured by firing order — *"the only thing that makes a stagger order legible without pressing Play"* (`:163-165`). `ChunkWindow.PyreSpawn.cs:189-258` draws the identical dials with **no preview at all**. So the better-supported control is the one the design is moving away from.
**Fix:** parameterise `BuildFormationPreview(SpawnFormation f)` — it is already pure w.r.t. `f` (`Formation.cs:152-173`); the only obstacle is the single `_formationPreviewEl` field, which should become a per-call local captured by the `IMGUIContainer`'s own closure plus a list of elements to dirty. Then add it to the per-blast box.

#### D-13 — MEDIUM — 19 stacked sections, no section toggle bar, no saved views, no split

`ChunkWindow.cs:83-116`. Pyre: `PyreWindow.cs:402` (`BuildSectionToggleBar`), `:462` (`ZuiViewBar`), `:488` listing eight sections.
**Rule:** "Vertical space is the scarce resource"; "The standard tool shape helps here: `Z.Split(stateKey, …)` puts controls in a left pane and the workspace in the right".
**Fix:** add `new ZuiSectionToggleBar("Chunks", ("Tags", TagsSection), ("Emission", …), …)` — the base already exposes `TagsSection` (`ZuiAssetWindow.cs:142`) for exactly this. Hold each `ZuiSection` in a field (Pyre's `layersSection` pattern) rather than a local. Consider `Z.Split` with the Preview + Formation + Slicer stages in a right pane, which also fixes D-15.

#### D-14 — MEDIUM — Sections are inconsistently keyed; ten of them have no `stateKey`

Keyed: `chunks.splash`, `chunks.slicer`, `chunks.pyrespawn`, `chunks.blastgroup.N`, `chunks.pyremotion`, `chunks.formation`, `chunks.layers`, `chunks.timeline`.
Unkeyed (`Z.Section(title, tooltip)` only): `"Emission"` (`ChunkWindow.cs:120`), `"Physics"` (`:158`), `"Life / Look"` (`:178`), `"Floor / Collision"` (`:222`), `"Sampled Pseudo-3D Debris"` (`:245`), `"Preview"` (`Preview.cs:61`), `"Animated Content"` (`:313`), `"Hit Detection"` (`:324`), `"Trail"` (`:345`), `"Tags"` (`ZuiAssetWindow.cs:137`).
**Rule:** an unkeyed container falls back to keying by `title+tooltip`, so a later reword orphans the persisted state.
**Fix:** give each a `"chunks.<name>"` key in the same pass as D-13 (the toggle bar makes the fold state user-visible, which makes orphaning noticeable).

#### D-15 — MEDIUM — "Several" is a whole row holding one short toggle

`ChunkWindow.PyreSpawn.cs:181-185`: `Z.Column` → `Z.Row(Z.Toggle("Several", …))`. In the screenshot this is a full-pane-width row ~90% empty.
**Rules:** "Space economy — pack rows, don't stack by default"; Card-layout #3 ("The card's OWN short fields flow into the space to the RIGHT of those picker rows").
**Fix:** put `Several` on the same row as the `Blast` chip (both short once D-09 lands), or on the section header via `SetHeaderMenu`/a second header toggle. The `Z.Column` wrapper at `:181` then disappears entirely.

#### D-16 — LOW/MEDIUM — `Start angle °` claims a solo row while `Radius`+`Arc °` share one

`ChunkWindow.PyreSpawn.cs:229-233` (and the same pattern is *correct* in `Formation.cs:91` only because those dials sit in a narrow column beside a fixed preview).
**Fix:** fold it into the preceding `Z.Row` (`:220-227`) — three short controls fit the width the screenshot shows going unused.

#### D-17 — LOW/MEDIUM — `Centre offset` renders as a thin 120×18 strip, not a 2D pad

`ChunkWindow.PyreSpawn.cs:103-114` builds `Z.Vector2Field(..., new ZuiValue2DControl.Options().WithRange(…).WithDefault(…).WithPlotSize(96f), …)` but never calls `.Expanded()`. `ZuiValue2DControl.Options.startExpanded` defaults to **false** (`ZuiValue2DControl.cs:194`), and `BuildCollapsed` (`:313-343`) draws a **120 × 18 px** thumbnail strip plus a `…` menu button. `plotSize: 96f` is only honoured in the expanded branch (`:411-412`).
**What a user sees:** exactly what the screenshot shows — `Centre offset` followed by a wide thin bar with a dot in it and a `…` button. It reads as a 1-D slider. The comment above it ("Wide by nature (plot + numeric block) — it earns its own row") describes a control that is not on screen.
**Rule:** "A spatial X/Y pair → `Z.Value2D` / `Z.Pad` (a 2D pad you drag), never two packed float fields" — the point is the *drag surface*; an 18 px strip is not one.
**Fix:** add `.Expanded()` (`ZuiValue2DControl.cs:213`). If the vertical cost is unacceptable, `.WithPlotSize(72f).WithoutSidePanel()`. Separately worth raising with the user: this control still draws a `…` MenuButton (`:341`, `:429`), which `zui.md` and the rulebook both say is deprecated in favour of right-click — a ZUI-level inconsistency, not a Chunks one.

#### D-18 — LOW/MEDIUM — The `Tags` section is a box titled with the same word as its one field, in the window's prime slot

`ZuiAssetWindow.cs:135-143` → `Z.Section("Tags", …)` whose whole body is `LauTagField.Draw` (`LauTagField.cs:22-37`), which itself starts with `GUILayout.Label("Tags", GUILayout.Width(34))`.
**Rule:** "Never title a box that holds exactly one field — a box titled 'Amount' wrapping a single field labelled 'Amount' says nothing twice."
Two further problems, both visible in the screenshots: the field's only action (`Tags…`, `LauTagField.cs:33`) is pushed to the far right by a `FlexibleSpace` inside a full-width IMGUI island and does not appear in either screenshot; and this least-used control occupies the top of every asset window, above everything the tool is actually for.
**Fix (base class, benefits every Laubrary tool):** drop the section and render the tag row as one line in the toolbar (which is what the old IMGUI `LaubraryAssetWindow` did — the comment at `:128-133` says so), or keep the section but drop the inner `"Tags"` label and cap the island's width so the `Tags…` button is adjacent to the chips rather than a pane-width away. Note this is *not* a Chunks file; flag it to the PM as a shared fix.

#### D-19 — LOW — Remove-button glyph is inconsistent: `×` in some lists, `X` in others

`Layers.cs:200` and `PyreSpawn.cs:161`, `:350` use `"×"`; `Timeline.cs:247` and `Modifiers.cs:102` use `"X"`; `PyreWindow.cs:900` uses `"✕"`.
**Rule:** "Be consistent across the codebase."
**Fix:** pick one (`×`) and use it in all four Chunks sites. (`✕` is confirmed rendering in Pyre, so either is safe; `X` the letter is the odd one out.)

#### D-20 — LOW — A long blast name can draw past the pane; every other label in the file is hand-truncated

`ChunkWindow.PyreSpawn.cs:48` concatenates `m.DisplayName` raw. `.zui-section__title` is `white-space: nowrap` with no `text-overflow`, so a 40-character asset name draws past the header. Compare `Layers.cs:451` and `Timeline.cs:186`, which both call `LsEllipsize`.
**Rule:** "Truncated text — If you must draw a fixed-width title that can overflow, hand-truncate the STRING with an ellipsis before drawing."
**Fix:** `LsEllipsize(m.DisplayName, 24)` — the helper already exists (`Layers.cs:588`) and is already visible to this partial class.

#### D-21 — LOW — `Pyre Movement` believes it has nothing to move whenever only extra blast groups are enabled

`ChunkWindow.PyreMotion.cs:22`:
```csharp
bool hasSpawner = c.pyreSpawn.enabled || c.spawnFormation.enabled;
```
`blastGroups` are not counted. But the runtime *does* apply motion to them: `PyreSpawnModule.SpawnOne` calls `ctx.Spec?.pyreMotion?.Apply(...)` (`PyreSpawnModule.cs` ~line 76 of the shown range) and every group is dispatched through the same path (`ChunkModules.cs:57-59`).
**What a user sees:** a spec with Pyre Spawn off and two extra blasts on shows Pyre Movement's whole body **greyed out** (`:43`) and its tooltip saying "Has nothing to move yet: turn on Pyre Spawn or Spawn Formation" — which is false. The dials are unusable for a configuration that works perfectly at runtime.
**Fix:** `bool hasSpawner = c.pyreSpawn.enabled || c.spawnFormation.enabled || AnyBlastGroupEnabled(c);` — the runtime already has that predicate (`ChunkModules.cs:95`, currently `static` private; make it public or inline the loop).

#### D-22 — LOW — `Dial` re-slices the debris preview on every edit in the window, including edits with nothing to do with slicing

`ChunkWindow.cs:60-68` — `Dial` unconditionally calls `RefreshChunkPreview()`. Every blast dial, every timeline number, every layer rename pays for a re-cut of the subject sprite. `Timeline.cs:270-273` and `Slicer.cs:127-130` both add `isDelayed`/no-rebuild workarounds *because* of this cost, and `Timeline.cs:449-453` explicitly avoids mutating during a drag for the same reason. Not a layout defect, but it is shaping the UI (delayed fields, no live rebuilds) and is worth a targeted fix: route slicing-relevant edits through a `SlicerDial`-style helper (the pattern already exists at `Slicer.cs:45` and `Formation.cs:140`) and leave plain `Dial` preview-free.

#### D-23 — INFO — Tooltips are, overall, very good

Verified by reading every call site: every `Z.*` factory in these files passes a real tooltip (they are compile-required), several are correctly composed per-state (`SplashSourceTip`, `LsBaseTip`, `ReshuffleTip`, the Slicer's precedence wording, the `poolWins` wording at `PyreSpawn.cs:71-74`), and the one empty tooltip (`ChunkFollowEmitterEditor.cs:126`) is deliberate and documented (`:120-125` — a disabled UITK element cannot resolve a tooltip, so it is hosted on an enabled wrapper row). No missing-tooltip defects found. The *problem* in this window is not documentation — it is that the affordances the tooltips describe are not on screen.

#### D-24 — INFO — On-screen instructional prose: only two, both borderline-legitimate

`PyreSpawn.cs:126-127` ("Superseded by the Spawn Formation section below…") and `Layers.cs:460` ("No modules enabled."). Neither is an instruction paragraph of the kind the rulebook bans; both are *status*. But `:126` is conditionally **added and removed**, which reflows everything under it — a "Stable workspace" violation. Convert it to a permanently reserved single line whose text changes (fixed height, `NoWrap`, `Overflow.Hidden`), exactly as `ChunkSpecEditor.cs:81-85` already does correctly.

---

## 4. Same information authored in two places (summary)

| Value | Place 1 | Place 2 | Place 3 |
|---|---|---|---|
| A blast's depth | the blast's own `Layer slot` radio, `PyreSpawn.cs:411` | Layer Stack → Modules → `Blast N: …`, `Layers.cs:450` | implicitly, via the group's typed `Name` (users name groups after slots — see the screenshot) |
| Fragments' depth | `Slicer.cs:243` (`Layer`) | Layer Stack → Modules → `Fragments`, `Layers.cs:415` | — |
| Splash / Formation depth | not in their own sections | Layer Stack → Modules, `Layers.cs:408`, `:429` | — (an inconsistency in itself: two modules have a local picker, two do not) |
| "several blasts in a shape" | per-blast `Several` + `Shape & stagger`, `PyreSpawn.cs:176` | standalone `Spawn Formation`, `Formation.cs:26` (which *supersedes* blast 1) | — |
| When a blast fires | per-blast `Stagger s` / `Jitter`, `PyreSpawn.cs:240-245` | Timeline lane delay, `Timeline.cs:182` | — |
| A blast's identity | header title, `PyreSpawn.cs:48` | `Name` text field, `:139` | timeline lane label, `Timeline.cs:186`; Layer Stack row label, `Layers.cs:451` |

Recommendation: one owner per value. Depth → the blast's own header/row (it is the sentence the user says); the Layer Stack "Modules" box becomes a read-only *overview* of the composed order. Timing → the Timeline for *when a blast starts*, the blast's own Stagger for *the spacing within one blast's shape* (that split is defensible and should be said in the labels, e.g. rename the per-blast one to `Stagger between points`).

---

## 5. ZUI-expansion assessment — is any needed control missing?

Checked against `zui.md` **and** the actual source in `Assets\Packages\Laubrary\Zui\`.

**Everything the proposed fixes need already exists:**

| Need | Control | Verified at |
|---|---|---|
| Header affordance for remove/dup/move on a section | `ZuiSection.SetHeaderMenu(icon, tooltip, open)` | `ZuiSection.cs:131`; used at `PyreWindow.cs:1134` |
| Header checkbox | `ZuiSection.SetHeaderToggle` | `ZuiSection.cs:199` |
| A compact reorderable list of blasts | `Z.Row` + `ZuiReorder.MakeGrip` | `Layers.cs:167`, `Modifiers.cs:82`, `PyreWindow.cs:818` |
| Card with a caller-built header that folds | `ZuiFoldCard.Wire` | `Modifiers.cs:124`, `PyreWindow.Modifiers.cs:319` |
| Inline rename on a row | `Z.TextInput` + `zui-audit-allow-stretch` | `PyreWindow.cs:836-846` |
| Add-menu with per-item actions | `Z.Menu(anchor).Section().Item()` | `Layers.cs:286`, `Timeline.cs:332` |
| Fold every section from one bar | `ZuiSectionToggleBar(prefsKey, params (label, section))` | `ZuiSectionToggleBar.cs:51`; `PyreWindow.cs:488` |
| Saved views | `ZuiViewBar` / `ZuiViewStore` | `PyreWindow.cs:462-508` |
| Left-controls / right-workspace split | `Z.Split(stateKey, width, left, right)` | `zui.md` §UI Toolkit half |
| 2D offset pad | `Z.Vector2Field` / `ZuiValue2DControl` (needs `.Expanded()`) | `ZuiValue2DControl.cs:213` |
| Reference chip | `ZuiChip` / `LauAssetElement.Build` | `ZuiChip.cs` — **exists but is unstyled, see D-09** |

**No new ZUI control is required.** There is, however, one genuine ZUI-level *gap* and one ZUI-level *bug*:

- **BUG (ZUI, not Chunks):** `ZuiChip` ships with zero USS (D-09). This is the single highest-value fix in this audit and it lives in `ZuiToolkit.uss`, benefiting 24 call sites across 9 tools at once.
- **GAP (pre-existing, already documented by the code that hit it):** there is no ZUI control for a **multi-lane track editor**. `ChunkWindow.Timeline.cs:23-28` raises this explicitly and deliberately writes its canvas in `ZuiTimeline`'s idiom so it can be promoted later. `Z.Timeline` (one banded bar + playhead) is a different control. This audit agrees with that assessment — it is a legitimate bespoke-canvas island today, and a fair future ZUI-expansion candidate, but nothing in this task needs it.
- Minor, worth raising with the user: `ZuiValue2DControl` still draws a `…` MenuButton (`:341`, `:429`) after the ⋯ was deprecated project-wide in favour of right-click.

---

## 6. Priority order for an iterative fix programme

**Pass 1 — make references look like references (fixes the literal complaint 1b, one file):**
D-09 (ZuiChip USS + a `ZuiAudit` height check).

**Pass 2 — make blasts addable, removable and countable (fixes complaint 1a):**
D-04 (Pyre-style blast list with select/dup/remove/reorder) → which subsumes D-01, D-02, D-11 and most of D-03. Then D-03 (rename "Pyre Spawn" → "Blast 1"; keep the persisted `BlastGroupTrack` key untouched), D-08 (name onto the row), D-07 (offer depth at creation time), D-05, D-06.

**Pass 3 — one owner per value:**
D-10 (retire the standalone Spawn Formation; single owner for layer slot and for timing), D-12 (per-blast formation preview), D-21.

**Pass 4 — window fitness and layout hygiene:**
D-13 (section toggle bar + optional `Z.Split`), D-14 (section keys), D-15, D-16, D-17, D-18 (base class — needs PM sign-off since it touches every Laubrary tool), D-19, D-20, D-22, D-24.

---

## 7. Verification buckets

- **Verified by reading the code:** every `file:line` claim above; the three text-input sites; the `+2` numbering in four places; the `"chunks.pyrespawn.pool"` shared key; `startExpanded == false`; `hasSpawner` omitting `blastGroups`; `ZuiChip` having no USS anywhere in the project (grep over all `.uss`); the 24 chip call sites; the existence and signature of every ZUI control named in a fix.
- **Verified by eye (screenshots only):** the unstyled-chip rendering (blank tall block + bare text), the "Blast 3 — In front" header, the `Name` text field, the lone `Several` row, the solo `Start angle °` row, the thin `Centre offset` strip, the missing `Tags…` button, the two `+ Add blast…` buttons with no `+ Add another blast…` in frame.
- **NOT verified:** nothing was run in the Unity editor — no `ZuiAudit` pass, no live layout measurement, no interaction test. **No human has clicked anything in this window as part of this audit.** In particular: the exact pixel height of an unstyled chip depends on the specific preview texture returned per asset (inferred from `LauAssetGridGUI.GetThumbnail` + UITK `Image` default sizing, consistent with the screenshots but not measured); and the `Tags…` button's absence from the screenshots may be a crop rather than a layout fault.
