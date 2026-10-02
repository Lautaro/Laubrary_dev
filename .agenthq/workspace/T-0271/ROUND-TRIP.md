# T-0271 — save/load round trip: what Unity conjures on save, and what it was doing to the picture

Everything below was measured on 2026-09-08 in the Shaper worktree editor (`D:\UNITY\Laubrary Dev - Shaper`, port 7801, `dataPath` verified as `D:/UNITY/Laubrary Dev - Shaper/Assets` on every probe). Test documents lived under `Assets/Shaper/Audit0271` and were deleted at the end; the demo document was loaded and rendered but never saved.

## 1. The mechanism, measured rather than assumed

Unity does not write `null` for a plain `[Serializable]` class field. It writes a **default-constructed instance**, using the C# field initialisers — not zeroes. Measured directly by building a document whose every fill and border was null in memory, saving it, and reading the YAML back (`phantom-yaml-probe.cs`):

```
IN MEMORY:  root.fill=null root.border=null m1.fill=null
YAML:       fill:  { kind: 0, veil: { m_static: 1, ... } }        <- the initialiser value, not 0
            border:{ enabled: 0, alignment: 1, width: { m_static: 2 } }
AFTER RELOAD: root.fill=obj Solid white Over | root.border=obj enabled=False | m1.fill=obj Solid white
```

That is why `fill != null` answers the wrong question on every document an author actually has, and why the border's `enabled = false` default (69e4716d) worked: a border is meaningless until switched on, so a phantom with `enabled=false` is inert. A fill has no such state — a phantom fill is bit-identical to a legitimately authored white Solid.

## 2. Every plain-class field on the authored model, and which of them can be a phantom

Walked by reflection over the whole serialized graph of a window-built document (bag + two members), listing every field whose type is a `[Serializable]` class (`phantom-inventory-probe.cs`, section A). **A phantom is possible only where the field is null in memory** — a field with a `= new X()` initialiser exists identically before and after a save and can never mismatch.

| field | type | storage | null in memory? | verdict |
|---|---|---|---|---|
| `ShaperNode.fill` (`ShaperNode.cs:276`) | `ShaperFillDef` | plain | **yes** | **PHANTOM — fixed** (`authored` flag + migration) |
| `ShaperNode.border` (`ShaperNode.cs:299`) | `ShaperBorderDef` | plain | **yes** | **PHANTOM — render already inert via `enabled=false` (69e4716d); the CARD was still lying — fixed** |
| `ShaperBorderDef.fill` (`ShaperBorderDef.cs:148`) | `ShaperFillDef` | plain | **yes** (when a border is authored without one) | **PHANTOM — no pixel effect** (`new ShaperFillDef()` is bit-identical to the `DefaultRootFill()` the resolver substitutes for null), **fixed anyway** so the card and the engine agree |
| `ShaperDocument.previewBackSplash` (`ShaperDocument.cs:203`) | `BackSplashSettings` | plain | **yes** | **PHANTOM — provably a no-op, left alone.** Preview-only, never read by `ShaperDocumentRenderer` (the field's own doc comment, and the only consumers are `ShaperPreviewStage.cs:528` and the panel that edits it). Its one visible effect would be the backdrop colour, and `BackSplashSettings.cameraColor` defaults to `new Color(0.08f, 0.08f, 0.10f)` (`Runtime/BackSplash/BackSplash.cs:112`) which is EXACTLY the fallback the stage already paints when the field is null (`ShaperPreviewStage.cs:146`). The window also does `previewBackSplash ??= new BackSplashSettings()` the moment it builds the panel (`ShaperWindow.Preview.cs:271`), so the phantom is what the window would have created anyway. |
| `ShaperLayer.root`, `ShaperLayer.height`, `ShaperNode.children`, `ShaperCompositeDef.source` | — | `[SerializeReference]` | yes | **safe** — `[SerializeReference]` round-trips null as null |
| everything else reachable from `ShaperDocument` — `blend`, `transform`, `sweep`, `shell`, `swarm` (+ `swarm.merge`), `primitive`, `composite`, `solid`, `response`, `mask`, `zOffset`, `lightRig` (+ `lights[]`), `effects[]`, `cherryFrames[]`, and all ~120 `ZUIValue` / `ZuiGradient` dials under them | various | plain | **no** (all have `= new X()`) | **safe — no phantom possible** |

There are exactly **four** phantom-capable fields on the whole model, and the sweep in §4 is the empirical confirmation that no fifth one is hiding behind an initialiser that fails to round-trip.

## 3. What the phantom fill was doing — the size of the bug

`ShaperFillResolver.Walk` reads `node.fill` and, when a node owns a fill, that node becomes the paint owner for its own coverage. On a saved document every member of a bag owned a phantom white Solid, so the bag's own authored fill painted nothing.

Measured on a saved bag with two ellipse members, editing the bag's own fill to red and counting changed pixels (`phantom-inventory-probe.cs`, section C):

| | pixels the bag's own Fill dial changes on a SAVED document |
|---|---|
| pre-T-0271 rule (`fill != null`) | **0** |
| after this fix | **6144** (the whole 96×64 canvas) |

The same document's YAML: **260 478 bytes, 6 `fill` blocks and 3 `border` blocks for a 3-node document** — of which exactly one fill was authored (the layer root's, seeded by `NewLayer`). Eight of the nine were conjured.

## 4. The round trip, every node kind

`roundtrip.tsv` — 33 documents, each built the way the window builds one (`ShaperWindow.NewLayer` + the shape picker's own `entry.Apply`), rendered at frames 0 / 8 / 15 in memory, then `CreateAsset` → `SaveAssetIfDirty` → `ForceReserializeAssets` → `ImportAsset(ForceUpdate)` → `LoadAssetAtPath`, and rendered again.

**33 rows, 0 differing.** Every node kind the picker offers: 9 primitives, 6 solids, 15 composites (both hosted families), a bag, plus six fixtures built to exercise the fill-ownership rule directly.

The `prefix_*` columns say what the *same saved documents* would have painted under the old rule, which is the proof the fix was needed rather than cosmetic:

| fixture | pre-fix wrong pixels (f0 / f8 / f15) |
|---|---|
| Bag (2 members, nothing authored) | 980 / 0 / 980 |
| Bag with an authored bag fill | 980 / 980 / 980 |
| Bag with an authored child fill | 440 / 440 / 440 |
| Bag whose child carries a PRE-FLAG all-default fill | 980 / 980 / 980 |
| every primitive, solid and composite | 0 |

Six composites (Plasma Bloom, Jet, Torch, Fire, Fireball, Kiln Orb) were skipped on cost, exactly as T-0265 skipped them. Two rows are blank pictures and prove nothing on their own — `Image` (no sprite assigned) and `Combine children` (an empty bag); the bag fixtures below them cover the bag case with a real picture.

## 5. The card, read back on a SAVED document

`card-readback-probe.cs`, reading the buttons out of the built visual tree of a real `ShaperWindow` on a saved bag:

```
bag  (fill seeded by NewLayer, authored)     : … [Remove fill] [Add edge]
m0   (never given a fill -> phantom on disk) : [Add fill] [Add edge]
m1   (authored green fill)                   : … [Remove fill] [Add edge]
IsAuthored: bag=True m0=False m1=True   border IsAuthored: bag=False m0=False
```

Before this task the same saved document showed **Remove fill** and **Remove edge** on all three, i.e. the card claimed every node of every saved document owned a fill and an edge nobody had given it.

## 6. The demo document

Loaded from `Assets/Demos/ShaperDemo/ShaperDemoDoc.asset`, rendered at all 16 frames as the engine is now, then re-rendered with the pre-T-0271 rule forced on the same in-memory copy. **Total differing pixels across all 16 frames: 0. Worst frame: 0.** Its 2 nodes both carry genuinely authored fills (promoted by the migration), no phantoms. The asset was never saved and was re-imported from disk afterwards; `git status` reports it unmodified.

## 7. The fix

| file:line | change |
|---|---|
| `Runtime/Shaper/ShaperFillDef.cs:80` | `[HideInInspector] public bool authored = false;` — false is load-bearing, since Unity writes constructed defaults for a phantom |
| `Runtime/Shaper/ShaperFillDef.cs:88` | `[NonSerialized] bool m_phantomChecked` — caches the negative answer only |
| `Runtime/Shaper/ShaperFillDef.cs:540` | `static bool IsAuthored(def)` — the one test replacing `fill != null`, with the in-place migration |
| `Runtime/Shaper/ShaperFillDef.cs:560` | `static ShaperFillDef Authored(def)` — the null-coalescible form |
| `Runtime/Shaper/ShaperFillDef.cs:564` | `DiffersFromDefault()` — `JsonUtility.ToJson` against a cached default, deliberately not a hand-written field list |
| `Runtime/Shaper/ShaperFillDef.cs:522` | `DefaultRootFill()` sets `authored = true` |
| `Runtime/Shaper/ShaperBorderDef.cs:43-63` | the same flag, cache and `IsAuthored` for a border |
| `Runtime/Shaper/ShaperFillResolver.cs:385` | `ShaperFillDef def = ShaperFillDef.Authored(node.fill);` — **the bug** |
| `Runtime/Shaper/ShaperFillResolver.cs:491` | `if (!ShaperBorderDef.IsAuthored(node.border)) return -1;` |
| `Runtime/Shaper/ShaperFillResolver.cs:555` | `ShaperFillDef.Authored(node.border.fill) ?? DefaultRootFill()` |
| `Runtime/Shaper/ShaperCompiler.cs:1342` | `if (!ShaperBorderDef.IsAuthored(node.border)) return child;` |
| `Editor/Shaper/ShaperWindow.Sections.cs:582` | the Fill card asks `IsAuthored`, not `== null` |
| `Editor/Shaper/ShaperWindow.Sections.cs:588` | "Add fill" creates `new ShaperFillDef { authored = true }` |
| `Editor/Shaper/ShaperWindow.Sections.cs:1142` | the Edge card asks `ShaperBorderDef.IsAuthored` |
| `Editor/Shaper/ShaperWindow.Sections.cs:1146` | "Add edge" sets `authored = true` |
| `Editor/Shaper/ShaperWindow.Sections.cs:1179` | the edge's own fill card asks `IsAuthored` |
| `Editor/Shaper/ShaperWindow.Sections.cs:1227` | `SeededBorderFill` reads the node's fill through `IsAuthored` |
| `Editor/Shaper/Audits/ShaperBorderAudit.cs:81`, `ShaperFillAudit.cs:109`, `ShaperLightAudit.cs:173` | the three audit `Solid(...)` helpers set `authored = true` — `Solid(Color.white)` with default arguments IS a default-constructed fill and would otherwise read as a phantom (the audits compile unconditionally, no `#if`, and were recompiled clean) |

No Pyre file was touched. No serialized field was renamed, removed or re-defaulted; two were added.

## 8. The one edge case, stated honestly

A fill authored **before** this flag existed and left at **every** default is indistinguishable from a phantom — both are a default-constructed `ShaperFillDef` with no `authored:` key in the YAML, byte for byte. It reads as absent. Consequences, measured:

- on a **layer root** — invisible. FC-3.2 substitutes `DefaultRootFill()`, which is the identical white Solid: fixture "Primitive default-valued fill (pre-flag)", pre-fix difference **0 pixels**.
- on a **child** — the child stops covering its owner's paint with flat white and inherits it instead: fixture "Bag child pre-flag default fill", **980 pixels** on that document.

The alternative that would tell them apart is `[SerializeReference]` on `ShaperNode.fill`, which changes the field's storage and would deserialize **every** fill in **every** existing document as null. That is a far larger loss than the case above, so it was not taken.

Two smaller consequences of the same shape, both documented at their fields: a border added and then switched off with no other change reads as absent (the picture is the same either way; the card offers "Add edge" again), and an in-code creator of a fill or border that leaves every value at its default — the audits under `Editor/Shaper/Audits/` build theirs with non-default values, and were checked — would need to set the flag itself. The one audit helper that could produce an all-default fill, `Solid(Color.white)`, now sets it.
