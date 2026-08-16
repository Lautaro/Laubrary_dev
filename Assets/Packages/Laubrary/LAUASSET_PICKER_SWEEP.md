# LauAsset inline pickers — the full sweep

Every place in Laubrary where an asset REFERENCE is picked inline, with a verdict against the rules below. Swept 2026-08-05 over `Assets/Packages/Laubrary/` (the whole package, Editor + Zui).

## The rules a site is vetted against

1. **No empty thumbnail holder.** A type that cannot render itself must not reserve or draw an image slot. If it carries a display colour, that colour goes in the image's place; otherwise the name stands alone.
2. **No inline button row.** No `Recall… / New ▾ / ✎` stealing a row of width. Those live on the context card, opened by clicking — or right-clicking — the chip.
3. **The reference reads as a reference.** An accentuated chip (`ZuiChip`), so every reference in a window is spottable at a glance.
4. **A name is never typed.** A reference to something declared elsewhere is PICKED. This covers laumination names, event ids, MetaLayer ids and weapon slots as well as asset objects.
5. **Nothing regresses.** The chip accepts drag-and-drop from the Project window, because the object field it replaces did.

**What is NOT a LauAsset picker** (and correctly keeps a plain object field): Unity's own asset types — `Sprite`, `Texture2D`, `TileBase`, `TMP_FontAsset`, `AudioClip`, prefabs, scene object references. These are picked from Unity's project browser; a chip would pretend to own them. The dividing line is implemented once, in `LauAssetHook.IsAssetReference`: a `ScriptableObject` that either renders its own preview (`IVisualPreview`), has a `LauAssetEditors` registration, or lives in a `Laubrary.*` namespace.

## Which types are visual?

`IVisualPreview` is the answer — implementing it IS the claim that a thumbnail exists, and nothing here keeps a second list to drift.

| Visual (thumbnail in the chip) | Non-visual (name, plus its colour if it has one) |
|---|---|
| Zoe, Pyre, WeaponDef, AmmoDef, LauminaryVersion, ChunkSpec, SpriteChunkAnimation, PyreChunkAnimation, LauminaryAnimationChunkAdapter, TextSplash, BackSplash, Tileset, Prop, LevelTile, LevelAsset, CartographerBiome | Faction, DamageType, TileTag, Choreography, SpriteFxSpec, SpriteCatalog, CartographerGenRecipe |

Sprites and Textures count as visual too — being an image is the whole of what they are.

**Note on Choreography and SpriteFxSpec:** both are arguably visual (a motion path, an effect stack) and neither implements `IVisualPreview` today, so both render as names. That is honest — they cannot currently generate a thumbnail — and the fix is to implement the interface, at which point they gain one everywhere at once with no UI change.

---

## A. Sites already on the chip (`LauAssetElement.Build`)

These were on the old thumbnail+buttons row and are now chips by virtue of the control being rewritten in place. Each is listed with what it picks so the "is that type visual?" question is answerable per site.

| # | File | Field | Picks | Visual? |
|---|---|---|---|---|
| A1 | `Editor/Cartographer/CartographerWindow.cs` | `layer.tags[idx]` | TileTag | no → colour chip |
| A2 | `Editor/Cartographer/CartographerWindow.cs` | `level.biome` | CartographerBiome | yes |
| A3 | `Editor/Cartographer/CartographerWindow.cs` | `genRecipe` | gen recipe | no → name |
| A4 | `Editor/Cartographer/CartographerWindow.cs` | `layer.tileset` | Tileset | yes |
| A5 | `Editor/Cartographer/CartographerWindow.cs` | `layer.solidTag` | TileTag | no → colour chip |
| A6 | `Editor/Cartographer/PropWindow.cs` | `paletteSource` | Tileset | yes |
| A7 | `Editor/Cartographer/PropWindow.cs` | `b.props[idx]` | Prop | yes |
| A8 | `Editor/Cartographer/PropWindow.cs` | `prop.tags[i]` | TileTag | no → colour chip |
| A9 | `Editor/Cartographer/PropWindow.Stamper.cs` | `recipe` | gen recipe | no → name |
| A10 | `Editor/Cartographer/PropWindow.Stamper.cs` | `r.biome` | CartographerBiome | yes |
| A11 | `Editor/Cartographer/PropWindow.Stamper.cs` | `level.solidTag` | TileTag | no → colour chip |
| A12 | `Editor/Chunks/ChunkWindow.cs` | `c.animationSource` | animation source | yes |
| A13 | `Editor/Chunks/ChunkWindow.cs` | `c.trailSource` | animation source | yes |
| A14 | `Editor/Mirage/MirageWindow.cs` | `entry.content` | Zoe / Pyre (curated union) | yes |
| A15 | `Editor/Mirage/MirageWindow.cs` | `entry.choreography` | Choreography | no → name |
| A16 | `Editor/Pyre/PyreWindow.Preview.cs` | `spec.previewSubjectAsset` | preview subject | yes |
| A17 | `Editor/PyrePlus/PyrePlusWindow.Import.cs` | `_importSrc` | import source | yes |
| A18 | `Editor/SpriteFx/SpriteFxFilterEditor.cs` | `filter.stack` | SpriteFxSpec | no → name |
| A19 | `Editor/Zoetrope/ZoetropeWindows.cs` | child object property | varies | varies |
| A20 | `Editor/Zoetrope/ZoetropeWindows.cs` | object property | varies | varies |
| A21 | `Editor/Zoetrope/ZoetropeWindows.cs` | list element property | varies | varies |
| A22 | `Editor/Zoetrope/ZoetropeWindows.cs` | `a.visual` (AmmoDef) | visual asset | yes |

## B. Systemic paths — one fix, every tool

| # | Path | What it draws | Verdict |
|---|---|---|---|
| B1 | `Zui/Toolkit/ZuiReflect.cs` → `AssetRefOrObjectField` | every REFLECTED object field (each modifier, each serialized object, each tool that reflects fields) | **routed to the chip** via `ZuiAssetHook`; a Sprite/Texture/font still gets an object field |
| B2 | `Zui/Toolkit/ZuiReflect.cs` (list element) | an object field inside a reflected LIST | **routed to the chip** |
| B3 | `Zui/Toolkit/ZuiSerialized.cs` (`ObjectReference`) | every serialized object reference drawn by ZUI | **routed to the chip** |
| B4 | `Editor/AssetKit/LauAssetGridGUI.cs` (`DrawGrid`) | the browser/picker grid behind every asset window and every Recall popup | **fixed**: a set with no visual types draws as a list of names + colour chips instead of a grid of blank squares |

## C. Raw `Z.Object<T>` sites — per-site verdict

| # | File | Type | Verdict |
|---|---|---|---|
| C1 | `Editor/AssetKit/ZuiAssetWindow.cs` | the window's own `T` | **keep the object field.** This is the window's SUBJECT header (with Browse beside it), not an inline reference inside a form. |
| C2 | `Editor/BackSplash/BackSplashWindow.cs`, `BackSplashZui.cs` | `Sprite` | keep — Unity asset |
| C3 | `Editor/Cartographer/CartographerWindow.cs:460` | `LevelTile` | **VIOLATION → fixed** (LevelTile is a Laubrary ScriptableObject with a preview) |
| C4 | `Editor/Cartographer/CartographerWindow.SceneTools.cs` | `Sprite` | keep |
| C5 | `Editor/Cartographer/PropWindow.cs` ×2 | `TileBase` | keep — a Unity tile asset |
| C6 | `Editor/Cartographer/PropWindow.Stamper.cs:48` | `CartographerLevel` | **keep the object field.** Looked like a violation in the grep; it is not — `allowSceneObjects: true`, it picks the level component in the OPEN SCENE. There is no asset set to browse, so a chip would open an empty card. (Vetting each site individually is what caught this.) |
| C7 | `Editor/Cartographer/TilesetBuilderWindow.cs` | `Texture2D` | keep |
| C8 | `Editor/Choreographer/ChoreographerWindow.cs` | `Sprite` | keep |
| C9 | `Editor/Chunks/ChunkWindow.cs`, `ChunkWindow.Preview.cs` | `Sprite` | keep |
| C10 | `Editor/Launimator/AnimationAsepriteWindow.cs:32` | `Lauminary` | **VIOLATION → fixed** |
| C11 | `Editor/Launimator/LauminationBuilderWindow.cs`, `SpriteCatalogWindow.cs` | `Texture2D` | keep |
| C12 | `Editor/Mirage/MirageWindow.cs:259` | `Sprite` ("Add sprite") | keep — adds a background, and it is an ACTION not a field |
| C13 | `Editor/Mirage/MirageWindow.cs:540` | `Sprite` (content-as-sprite) | keep — the Sprite arm of the content union, beside the chip |
| C14 | `Editor/Mirage/MirageWindow.cs` (Damage step) | `DamageType` | **VIOLATION → fixed** |
| C15 | `Editor/Pyre/PyreWindow.Layers.cs`, `PyrePlusWindow.cs` | `Sprite`, `TMP_FontAsset` | keep |
| C16 | `Editor/SpriteFx/SpriteFxStackWindow.cs` | `Sprite` | keep |
| C17 | `Editor/TextSplash/TextSplashWindow.cs` | `TMP_FontAsset` | keep |
| C18 | `Editor/Zoetrope/ZoetropeWindows.cs:832` | `DamageType` | **VIOLATION → fixed** |

## D. IMGUI property drawers

| # | File | What | Verdict |
|---|---|---|---|
| D1 | `Editor/ZoetropeLaunimator/ZonedLauminaryViewDrawer.cs` | `version` (LauminaryVersion) as `EditorGUI.PropertyField`, plus an "Idle Clip" popup | IMGUI island inside the Zoe window's view row — folded into task A3, which rebuilds that row |
| D2 | `Editor/ZoetropeLaunimator/LauminaryViewDrawer.cs` | same shape for the retired plain view | same |

## E. Direct `LauAssetPicker.Show` callers (a "Recall" button rather than a field)

| # | File | Verdict |
|---|---|---|
| E1 | `Editor/AssetKit/LauAssetElement.cs` | the chip's own `Browse…` card item — correct |
| E2 | `Editor/BackSplash/BackSplashGUI.cs` ×2 | Recall/Save of a BackSplash PRESET: an action pair on a settings block, not a reference field. Keep — but the pair belongs on a context card too (logged below) |
| E3 | `Editor/Mirage/MirageWindow.cs:250` | "Add Previewable" — an ACTION that appends an entry, not a field. Keep |

## F. Name references (same rule, different shape)

A reference to something declared elsewhere by NAME. `Z.Pick` (a chip that opens the declared list) is the control.

| # | Site | Verdict |
|---|---|---|
| F1 | Mirage sequence steps: event id, laumination, weapon slot, muzzle point | **on `Z.Pick`** (built with the sequence) |
| F2 | Zoe window: event `Laumination` dropdown | native `DropdownField` — **fixed** |
| F3 | Zoe window / `ZonedLauminaryViewDrawer`: `Idle Clip` popup | IMGUI popup — folded into A3 |
| F4 | Zoe event `id` text field | **correct as a text field** — this is the DECLARATION site |
| F5 | `WeaponDef.muzzleLayerId` / `muzzleEventName` text fields | typed strings on the asset — retired by task A2 |

## How each category was vetted (2026-08-05)

- **A (22 chip sites)** — read every call site. All pass: each is either inside a `Z.Field(label, tip, chip)` or a `Z.Row(chip, ×)`, with a real tooltip and a sensible create-folder. Two were FIXED while vetting: `ZoetropeWindows`' two shared helpers put the label on a row of its own ABOVE the chip (correct when the control was 40px tall and full width, wasteful now) → label beside the chip; and both helpers created new assets into the `Assets` root → now `Assets/<TypeName>`.
- **B (systemic)** — verified by probe in the live editor: `ZuiAssetHook.TryBuild` returns a `ZuiChip` for `Zoe` and for `Faction`, returns null for `Sprite`, and `ZuiReflect.AssetRefOrObjectField` returns a `ZuiChip` for `WeaponDef` and an `ObjectField` for `Sprite`. So a reflected or serialized reference to a Laubrary asset is a chip, and a Unity asset still is not.
- **C (18 raw object fields)** — each judged individually. 4 were real violations and are fixed; 1 (C6) looked like a violation to the grep and turned out to be a SCENE reference, which must stay an object field; the rest are Unity asset types and correctly keep theirs.
- **D** — D1 is replaced for the Zoe window (the row is built natively now); the IMGUI drawer stays for the default Inspector, where a UITK chip cannot go.
- **E** — verdicts only; nothing to change.
- **F** — F1 built with the Mirage sequence, F2 fixed, F3 replaced by the native view row, F4 confirmed correct as a text field (it is the declaration), F5 belongs to the weapons task.

## Deferred, with reasons

- **E2, the BackSplash Recall/Save pair** — a preset load/save pair on a settings block. It is not a reference field (nothing is "assigned"), so the chip does not apply; the pair would still read better as a context card on the block's own header. Logged, not done.
- **Choreography / SpriteFxSpec previews** — implement `IVisualPreview` on both and they gain thumbnails everywhere at once.
- **D2, `LauminaryViewDrawer`** — the drawer for the retired plain `LauminaryView`. Left as-is: nothing in the package authors that view any more, and rewriting a drawer for a retired type is work with no reader.
- **F5, `WeaponDef.muzzleLayerId` / `muzzleEventName`** — typed name strings on the asset. They are retired by the weapons task (the muzzle becomes an ordinary effect entry at a MetaPoint), so they are not worth converting to pickers first.
</content>
