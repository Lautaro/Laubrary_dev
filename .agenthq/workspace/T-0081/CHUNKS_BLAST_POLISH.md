# T-0081 — blast card polish + the Spawn Formation conflict (D-15, D-16, D-17, D-10, D-12)

**Files changed:** `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Editor\Chunks\ChunkWindow.PyreSpawn.cs` (edited in place — the previous agent's card list, fold keying, grip reorder, header `×`, per-blast state keys and `+ Add blast…` placement are all untouched) and `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Editor\Chunks\ChunkWindow.Formation.cs` (rewritten around one parameterised builder). **Nothing else was touched** — no runtime file, no `ChunkWindow.cs`, no `ChunkWindow.Preview.cs`, nothing under `Zui\`, no menu item, no data-model change, no git command.

**No Unity editor was involved at any point** — no Coplay, no unity-editor-mcp, no `unity.exe`, no probe, no Play mode. Nobody has looked at the result with their eyes.

**It does compile, though, and that is not a code-reading claim.** I ran Unity's own Roslyn (`Editor\Data\DotNetSdkRoslyn\csc.dll` via `Editor\Data\NetCoreRuntime\dotnet.exe`, 6000.3.10f1) over **all 16 source files of the `com.Lautaro-Arino.Laubrary.Chunks.Editor` assembly**, referencing that assembly's real dependencies out of `Library\ScriptAssemblies\*.dll` plus the editor's `UnityEngine` modules and the netstandard 2.1 ref/shims — i.e. the same inputs Unity compiles it with, minus the assembly's own stale DLL. Result: **zero errors, zero warnings**, including the current in-progress `ChunkWindow.cs` / `ChunkWindow.Preview.cs` from the other agent as of the time of the run. Output went to `Temp\` and was deleted. This proves every type, member and overload I call exists with the signature I used; it proves nothing about how any of it looks.

---

## Requirement → code trace

### Task 1 — D-15 / D-16: stop giving short controls whole empty rows

- **D-15, the `Several` toggle.** `BuildBlastBody` (`ChunkWindow.PyreSpawn.cs`) — the toggle no longer has a row. It is now the second child of the **Blast row**: `body.Add(Z.Row(Z.Field("Blast", …, LauAssetElement.Build(…)), Z.HSpace(), Z.Toggle("Several", …)))`. The `Z.Column` wrapper and the `Z.Row(Z.Toggle(...))` that produced the ~90%-empty row are gone with the old `BuildBlastSeveral`. Chosen row-mate for two reasons: the chip is capped at 220px by `.zui-chip`'s `max-width`, so that row provably has room; and the pair reads as one sentence — *this blast, fired several times*. It is also the audit's own recommendation for D-15.
- **D-16, `Start angle °` on a solo row.** Fixed structurally rather than by moving one control: the blast card's shape dials are no longer full-pane stacked rows at all. `BuildFormationDials` (`ChunkWindow.Formation.cs`) puts Length/Angle° (Line) or Radius/Arc°/Start angle° (Ring) plus Scatter into a **narrow `dials` column beside a fixed-size preview canvas** — the exact shape the audit calls *correct* for the standalone section (`"the same pattern is correct in Formation.cs:91 only because those dials sit in a narrow column beside a fixed preview"`). In a column that is one MicroSlider wide, `Start angle °` is a column entry, not a row with 250px of nothing to its right. The dead horizontal space it used to leave is now occupied by the preview that D-12 asked for.
- **Also packed, same pass:** the stagger group went from four rows (Seconds, Jitter / Order / Seed, and in the card a separate `Scatter · Stagger s · Jitter` row) to **two** — `Z.Row(Seconds, Jitter, Shape seed)` then `Z.Field("Order", …)`. The standalone section's `Seconds`/`Jitter` also dropped from `Wide` (150px number fields) to `Num` (70px), per *"a value like 1.882 doesn't get easier to read at 1000px"*.
- **Deliberately NOT packed, and why:** (a) the **Offset/Centre offset** control — an X/Y spatial pair is explicitly excluded from row-packing, so it stays one `Z.Vector2Field`; it was never split into two floats and is not now. (b) The **Rotation** row — it has slack only in `Burst` mode; in `Fixed` it also carries a `Wide` angle slider and in `Random` a `Z.MinMax`, so anything packed there would fit at one of three settings and blow the row out at the other two, and the layout would jump every time the mode changed ("Stable workspace"). The judgment is stated in a comment at the call site. (c) The **Blast chip** and the **Order** radio are genuinely wide controls and keep their rows, per *"only a WIDE control earns its own row"*.

### Task 2 — D-17: the offset control is now an actual 2D pad

`BuildBlastBody`, the `Z.Vector2Field` call: the options chain gained **`.Expanded()`** — `ZuiValue2DControl.Options.Expanded(bool on = true)`, `ZuiValue2DControl.cs:213`, which sets `startExpanded` and is consumed once per control identity at `ZuiValue2DControl.cs:269-270` (`if (!fold.seeded) { fold.seeded = true; fold.expanded = _opt.startExpanded; }`). That routes `Build()` to `BuildExpanded` instead of `BuildCollapsed`, so the user gets the side panel (label · Reset · ⋯ · ▲), the X/Y numeric block and a **96×96 drag plot** — `_opt.plotSize` is only read in the expanded branch (`:411-412`), which is why the existing `.WithPlotSize(96f)` was doing nothing. I kept the side panel (it carries the label and Reset — dropping it via `WithoutSidePanel()` would cost both) and kept the plot at the 96f that was already authored, so the vertical cost is about one plot's height. The misleading comment above the control was rewritten to say what is now true and to name `.Expanded()` as the reason it is true.

### Task 3 — D-10: the conflict is stated on both sides, on screen

Four code paths, all state readouts of the current configuration — none of them a paragraph explaining how the system works (that is all tooltip content, per *"Labeling — tooltip, not title"*, which forbids instructional prose on screen):

1. **Blast side, in the card:** `BlastSupersededLine(ChunkSpec)` in `ChunkWindow.PyreSpawn.cs`, added as the FIRST child of the body by `BuildBlastBody` when `first`. Superseded ⇒ *"Not firing here — the Spawn Formation section is firing this blast at its own points."* Not superseded ⇒ **empty text on the same reserved line**.
2. **Formation side, in the section:** `FormationSupersedeLine(ChunkSpec)` in `ChunkWindow.Formation.cs`, added by `BuildSpawnFormation` immediately under the header. Two real states, both true statements about the current config: first blast armed ⇒ *"Firing \"Proper Blast\" — the first blast above — at these points instead of its own placement."*; first blast has no source and no pool ⇒ *"Nothing to fire: the first blast above has no effect picked, so these points spawn nothing."* That second one is a genuine, currently-silent dead end — `SpawnFormationRunner.Fire` returns early on `!spawner.HasSpawner` and nothing anywhere says so.
3. **Blasts section header, while collapsed:** `BlastCountSuffix` now appends `" — first not firing"` inside the existing count parens, so a folded section reads `Blasts (3 — first not firing)`.
4. **Spawn Formation header, while collapsed:** `s.SetHeaderSuffix(() => m.enabled ? " — firing the first blast" : "")`, so a folded section reads `Spawn Formation — firing the first blast`.

Both (1) and (2) go through **one** shared helper, `SupersedeStatusLine(text, tooltip)` in `ChunkWindow.Formation.cs`, so the two sides read as one statement made twice rather than two differently-worded warnings.

**The decision, and the justification (also written into the code as a comment above `BlastSupersededLine`):**

- **A reserved status line, not an element that appears.** The version I replaced added a `Z.Text` only while the conflict was live and added nothing otherwise, so switching Spawn Formation on *grew a line inside the card* and shoved everything below it down — precisely the "Stable workspace" failure the rulebook describes. The line is now always present (fixed `height: 18`, `whiteSpace: NoWrap`, `overflow: Hidden`, copied from the canonical reserved line at `TilesetBuilderWindow.cs:202-208`) and only its **text** changes.
- **Why not a header suffix alone:** a suffix only draws while a section is COLLAPSED (`ZuiSection.Apply`, `:160-162`), so on its own it would say nothing at exactly the moment the user is inside the section authoring the thing. It is used as the *second* signal, for the folded case, where it cannot double up with the line.
- **Why not disabled-with-reason:** greying out the superseded card's Offset / Several / Layer slot would be honest about the runtime but wrong for the workflow — a user is usually tuning a blast *for after* they switch the formation back off, and values they can still see but not touch read as "this tool is broken" rather than "this is overridden".
- **Only card 0 reserves the line**, because only `c.pyreSpawn` can be superseded (`ChunkModules.Run` substitutes the formation for `pyreSpawn`, never for a `blastGroups` entry). That is a structural per-card difference decided at build time, not something that appears and vanishes, so it costs no stability.
- **The standalone Spawn Formation module was NOT deleted, disabled, retitled `(legacy)` or otherwise weakened.** No authored data was touched.

### Task 4 — D-12: the per-blast formation preview. **Done — the parameterisation succeeded.**

The two obstacles the old comment named are both gone:

- **Hard-wired to `c.spawnFormation`** → `void BuildFormationDials(VisualElement host, SpawnFormation f, string undoPrefix, string centreName, float previewSize, string staggerBoxKey)`. It takes the formation, not the spec. `BuildSpawnFormation` calls it with `(s, m.formation, "Formation", "the burst's origin", FormationPreviewBox, "chunks.formation.stagger")`; `BuildBlastShape(PyreSpawnModule m, int index)` in `ChunkWindow.PyreSpawn.cs` calls it with `(box, m.formation, "Blast", "this blast's offset", FormationPreviewCard, null)`. The blast card's dials are now a **call, not a copy** — ~80 lines of duplicated dial code deleted from `ChunkWindow.PyreSpawn.cs`.
- **The single `_formationPreviewEl` field** → `readonly List<IMGUIContainer> _formationPreviews`, and `BuildFormationPreview(SpawnFormation f, float size)` is per-call. `DialF` now calls `RepaintFormationPreviews()`, which marks every live canvas dirty. The list maintains itself through `AttachToPanelEvent` / `DetachFromPanelEvent` registered on each canvas, so it holds exactly the previews currently on a panel regardless of which section builds first, how many previews one rebuild makes, or whether a card is folded — and it cannot accumulate stale elements across a session's rebuilds.
- The two contextual differences are parameters, not forks: `centreName` composes the tooltips truthfully on each side ("centred on **the burst's origin**" vs "centred on **this blast's offset**"), and `staggerBoxKey` decides whether the stagger dials get their own keyed sub-box (standalone, which owns a whole section) or an in-line `Z.Divider("Stagger", …)` (a blast card, already inside a box titled "Shape & stagger" — a box called "Stagger" inside it would say the same word twice, one fold level down).
- `previewSize`: **150px** standalone (unchanged), **120px** inside a card, because a card's host is a box inside a section and 150 + gutter + a `Wide` MicroSlider column overflows a half-screen pane. Sized to the host, not shrunk arbitrarily.
- No third file was needed. `SpawnFormation.Resolve` and `SpawnPlacement` are read-only uses that were already there.

---

## Capability-preservation list

Every control in both files after the change, and where it lives. Nothing was dropped.

| Control | Where now | Change |
|---|---|---|
| Section "Blasts" + collapsed count suffix | `BuildPyreSpawn` / `BlastCountSuffix` | suffix now also reports the supersede |
| Card fold caret, `≡` grip (or 16px spacer), enable toggle, inline name field + derived placeholder, `×` (Clear on card 0 / Remove on a group) | `BuildBlastCard` | untouched |
| Grip drag-reorder, delay carrying | `MoveBlastGroup` → `MutateBlastGroups` | untouched, byte for byte |
| `+ Add blast…` | `BuildAddBlastButton` | untouched |
| Clear-the-first-blast + its timeline lane | `ClearFirstBlast` | untouched |
| Blast picker chip (`LauAssetElement`, pool-overridden tooltip variant) | `BuildBlastBody` row 1 | now shares its row with `Several` |
| **Supersede status readout** | `BlastSupersededLine` | **reworked** — reserved line, was an appearing `Z.Text` |
| `Several` toggle | `BuildBlastBody` row 1 | **moved** off its own row |
| Pool box: entry chips, per-entry `×`, `+ Add to pool…`, per-blast fold key | `BuildPyreSpawnPool` | untouched |
| Shape, Count | `BuildFormationDials` row 1 | shared with the standalone section |
| Length, Angle ° (Line) / Radius, Arc °, Start angle ° (Ring), Scatter | `BuildFormationDials` dials column | now a column beside the preview |
| **Live point preview (order-coloured)** | `BuildFormationPreview` | **new for a blast card**; unchanged for the standalone section |
| Stagger seconds | `BuildFormationDials` stagger row | label `Stagger s` → **`Seconds`**, now under a "Stagger" divider/box that supplies the context |
| Stagger jitter | same row | unchanged (`Num` width both sides now) |
| Shape/formation seed | same row | standalone label `Seed` → **`Shape seed`**; the blast card's `Shape seed` label is unchanged. Deliberate: a card already has a `Seed` on the Scale row meaning something else |
| Stagger order radio | `BuildFormationDials` | unchanged |
| Rotation mode radio + Fixed angle + Random angle range | `BuildPyreSpawnRotation` | untouched |
| Scale `Z.MinMax` + Seed | `BuildBlastBody` | untouched |
| Offset / Centre offset 2D control (label, Reset, ⋯ menu, ▲ collapse, X/Y numeric fields, drag plot) | `BuildBlastBody` | **`.Expanded()`** — the pad and the numeric block are now visible instead of a 120×18 strip; the collapsed strip is still one click away |
| Layer slot radio / `+ Add depth slot…` / `New slot…` / the two-item back-front menu | `BuildPyreSpawnLayerSlot`, `ShowNewSlotForBlastMenu` | untouched |
| Spawn Formation section, its header enable toggle, all its dials | `BuildSpawnFormation` + `BuildFormationDials` | **kept in full**; gained a status line and a collapsed header suffix |
| `DialF`, `Num2F`, `Int2F` | `ChunkWindow.Formation.cs` | signatures unchanged (other partials may call them) |

**Undo:** every spec mutation still routes through `Dial` / `DialF` / `DialAndRebuild` / `MutateBlastGroups`, and the one sanctioned exception — `Z.Vector2Field`'s `onBeforeMutate` → `Undo.RecordObject(c, "Blast offset")`, one undo step per drag gesture — is preserved exactly as it was. Nothing new writes to the asset outside that contract. No `EditorGUILayout`/`GUILayout`, no raw UITK `Toggle`/`Slider`/`EnumField`, no `Z.EnumDropdown`, no min/max left as two fields, every new label carries a tooltip, and every new control has an explicit width or is a `Z.Field`-wrapped fixed-width control.

---

## Risks the PM should check by eye

1. **Card width, the big one.** A blast card's body now hosts a 120px preview plus a dial column that must fit a `Wide` (150px) MicroSlider plus its label. If the Chunks window is docked narrow, the Ring dials will clip. Open a spec with a ring blast, drag the window narrow, and look. The knob is `FormationPreviewCard` (`ChunkWindow.Formation.cs:33`).
2. **The three-across stagger row** (`Seconds · Jitter · Shape seed`) inside a card. The row it replaces was also three-across (`Scatter · Stagger s · Jitter`) and fits in the audit screenshot, so this should too — but `Shape seed` is a longer label than `Stagger s`, so confirm nothing is clipped at the right.
3. **The expanded 2D pad's footprint.** Side panel (108) + numeric block (82) + plot (96) ≈ 290px wide and ~100px tall, in **every** open blast card. If that reads as too heavy in a list of four blasts, the cheap dials are `.WithPlotSize(72f)` and/or `.WithoutSidePanel()` — but note the side panel is where the control's **label** and **Reset** live, so dropping it loses both.
4. **`.Expanded()` seeds fold state ONCE per module instance per domain reload** (`ZuiValue2DControl.cs:269-270`). Consequence: a pad the user collapses stays collapsed, correctly — but a pad that was already collapsed *before this change*, in this same editor session, will still be collapsed after a plain rebuild and only picks up the new default after a domain reload. If it looks like the fix didn't land, recompile/reload once before believing it.
5. **The reserved status lines cost 18px** on every first-blast card and on the Spawn Formation section, whether or not there is anything to say. That is the price of the "Stable workspace" rule; if it reads as a gap, the answer is to shrink the line, not to make it conditional again.
6. **`ZuiAudit` has not been run** — it needs a laid-out window. Please run `Laubrary/Audit Focused Editor Window` with the Blasts section and a Several-on card expanded, and confirm `foldedSkipped == 0`.
7. **Two `SetHeaderSuffix` providers now read across sections** (`BlastCountSuffix` reads `c.spawnFormation`; the Formation section's reads `m.enabled`). They are re-evaluated in `ZuiSection.Apply`, i.e. on fold and on rebuild — every path that changes either flag goes through `DialAndRebuild`, so they should always be current. Worth one check: turn Spawn Formation on, fold the Blasts section, confirm it says `— first not firing`.
8. **The preview list is maintained by `AttachToPanelEvent`/`DetachFromPanelEvent`.** If a preview ever stops repainting under a drag, that is the first place to look — the symptom would be a stale ring in a card while the numbers change.
9. **`PreviewBorder` is `ChunkWindow.Preview.cs`'s** (`:143`) and I call it, as the old code did. It is the other agent's file; if they rename or move it, `BuildFormationPreview` needs updating with it. It compiled clean against their current version.
10. **Not verified by eye, at all.** No card has been opened, no dot dragged, no ring previewed, no status line read on screen by a human or a probe. What IS verified: the whole assembly compiles clean with Unity's own compiler against the project's real assemblies (see the top of this report), and every requirement above is traced to a named method or branch.
