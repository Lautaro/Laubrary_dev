# LauAsset Lock — the three measurements from LAUASSET_LOCK_DESIGN.md §13.1

Measured 2026-08-25 against the live editor: **Unity 6000.3.10f1**, project `D:\UNITY\Laubrary Dev`, **PID 33528**, pipeline port **7800**. All three were run as read-only probes through `unity command eval_file` (no domain reload, no Play mode, no recompile). Nothing was written: no `SetDirty`, no `SaveAssets`, no `CreateAsset`, no `DeleteAsset`, no serialized field on any project asset was touched. The only objects created were two throwaway in-memory `ScriptableObject` instances used for measurement 1's control case, both `DestroyImmediate`d in a `finally` block and never associated with a path.

## Answers in one line each

- **(1) The design doc is wrong.** `EditorJsonUtility.ToJson` emits a reference to a persisted asset as `{"fileID", "guid", "type"}` — the same GUID + local-file-ID triple Unity's YAML uses — *not* a session-local `instanceID`; `instanceID` appears only as the literal value `0`, which means "null or non-persisted". The GUID-remapping work §5.1 calls mandatory is **not needed and should be deleted from Phase 2**.
- **(2) The sub-asset hole is real but narrow.** Of **110** LauAssets, **2** have more than one object at their path — both are `MirageView`, both carrying a `Texture2D` named `Thumbnail` (plus, on one, a Unity-generated `ImportLog`). It is regenerable preview data, not authored truth, so §5.3b does not collapse to "none" but it also does not justify a multi-object seal format.
- **(3) Dedup-by-hash is needed up front.** The largest snapshot is **329,687 bytes (322 KB)**, **17 of 110** assets exceed 100 KB, and the whole LauAsset set is **5,022,331 bytes**; a 10-deep ring on the single largest `Pyre` is **3.2 MB** and on everything touched is **~48 MB**. §11.2's "optimistic guess is a few KB per entry" is true of the median (1,684 bytes) and false of the p75 (71,721 bytes).

---

## Measurement 1 — what `EditorJsonUtility.ToJson` emits for a `UnityEngine.Object` reference field

### The claim as the design doc states it

§5.1's caveat: *"`EditorJsonUtility` emits `UnityEngine.Object` references as `{"instanceID": N}`, and an instanceID is session-local — it is not stable across an editor restart, a reimport, or another machine. ... So the sidecar writer must post-process the JSON, replacing each `instanceID` with the GUID + local file ID from `AssetDatabase.TryGetGUIDAndLocalFileIdentifier`, and the reader must map them back before `FromJsonOverwrite`."* §13.1 item 1 restates it: *"If this is wrong, the seal format is simpler than §5.1 claims. If it is right, remapping is mandatory and it is not optional work."*

### Raw measured output

Subject: `Assets/Demos/PreviewDemo/PreviewWeapon.asset` — a `WeaponDef` with `ammoTypes[0]` assigned to `Assets/Demos/PreviewDemo/PreviewAmmo.asset` (an `AmmoDef`). This is exactly the `WeaponDef`-with-an-`AmmoDef` case §13.1 asked for.

```
weapon asset : Assets/Demos/PreviewDemo/PreviewWeapon.asset
ammoTypes[0] : PreviewAmmo (Laubrary.Zoetrope.AmmoDef) at Assets/Demos/PreviewDemo/PreviewAmmo.asset
ammoTypes[0].GetInstanceID() = 59674
ammoTypes[0] GUID = 90dae11213b4ad3439f7415e41f1faca   localFileId = 11400000

--- EditorJsonUtility.ToJson(weapon, true) VERBATIM ---
{
    "MonoBehaviour": {
        "m_Enabled": true,
        "m_EditorHideFlags": 0,
        "m_Name": "PreviewWeapon",
        "m_EditorClassIdentifier": "com.Lautaro-Arino.Laubrary.Zoetrope::Laubrary.Zoetrope.WeaponDef",
        "displayName": "Preview Weapon",
        "automatic": false,
        "fireRate": 2.0,
        "damage": 10.0,
        "projectileSpeed": 8.0,
        "spreadDeg": 0.0,
        "projectilesPerShot": 1,
        "ammoTypes": [
            {
                "fileID": 11400000,
                "guid": "90dae11213b4ad3439f7415e41f1faca",
                "type": 2
            }
        ],
        "muzzle": {
            "rid": 1452078146085191784
        },
        "muzzleOffset": {
            "x": 0.6000000238418579,
            "y": 0.20000000298023225
        },
        "fireZoundName": "",
        "references": {
            "version": 2,
            "RefIds": [
                {
                    "rid": 1452078146085191784,
                    "type": {
                        "class": "PyreChunksFx",
                        "ns": "Laubrary.ZoetropePyre",
                        "asm": "com.Lautaro-Arino.Laubrary.Zoetrope.Pyre"
                    },
                    "data": {
                        "blast": {
                            "fileID": 11400000,
                            "guid": "1854130e91419a94f9c580ea5030a45d",
                            "type": 2
                        },
                        "blastFps": 24.0,
                        "chunks": {
                            "instanceID": 0
                        },
                        "sortingOrder": 10
                    }
                }
            ]
        }
    }
}
--- end ---
```

Control case A — a reference to a **sub-asset** (a `Sprite` inside a `.png`), to confirm the local file ID is a real per-object id and not a constant:

```
asset : Assets/BackSplash/Castle.asset [BackSplash]  field: image
target: Sprite "Background13" (sub-asset of Assets/BackSplash/Source/Background13.png)
instanceID=60846  GUID=866c877b4deb2864d99414e91ef88449  localFileId=21300000
        "image": {
            "fileID": 21300000,
            "guid": "866c877b4deb2864d99414e91ef88449",
            "type": 3
        },
```

Control case B — the same array holding, in order, a **non-persisted in-memory object**, a **persisted asset**, and a **null**, to find the exact boundary where `instanceID` appears:

```
assignment check: count=3  [0]=TEMP_InMemory_Ammo  [1]=PreviewAmmo  [2]=null
tmpA isPersistent=False  instanceID=-912036
realAmmo isPersistent=True  instanceID=59674  path=Assets/Demos/PreviewDemo/PreviewAmmo.asset

--- ToJson(tmpWeapon) ammoTypes fragment: [0]=in-memory, [1]=asset, [2]=null ---
        "ammoTypes": [
            {
                "instanceID": 0
            },
            {
                "fileID": 11400000,
                "guid": "90dae11213b4ad3439f7415e41f1faca",
                "type": 2
            },
            {
                "instanceID": 0
            }
        ],
        "muzzle": {
            "rid": -2
        },
```

### Verdict

**§5.1's caveat is factually wrong for the case it matters in.** A reference to a persisted asset is emitted as `{"fileID": <localFileId>, "guid": "<guid>", "type": <n>}` — precisely the GUID + local file ID form the doc says the writer must construct by hand. `TryGetGUIDAndLocalFileIdentifier` returned `90dae11213b4ad3439f7415e41f1faca` / `11400000` for the target, and those are the exact two values already in the JSON. The `type` field is Unity's standard source tag (`2` for a native `.asset`, `3` for an object imported from a source file such as the `Sprite` inside a `.png`) — the same triple that appears in the YAML `.asset` file itself.

`instanceID` **does** appear in the output, but only ever as the literal `0`, and it is what Unity writes for "no object": measured identically for an explicit `null` and for a live, valid, non-persisted in-memory `ScriptableObject` whose real instance ID was `-912036`. A non-zero session-local instance ID was never emitted anywhere in any probe.

Two secondary facts, both measured, both relevant to the seal format:

- **The same GUID form is used inside `[SerializeReference]` payloads.** The nested `PyreChunksFx.blast` field, which lives in the `references.RefIds[].data` block, was emitted as `{"fileID", "guid", "type"}` too — so managed references carrying object pointers need no special treatment either. A null `[SerializeReference]` is `{"rid": -2}`.
- **A reference to a non-persisted object cannot survive a seal at all.** It is not corrupted into a stale pointer, it is silently flattened to null. For a `ScriptableObject` seal this is very nearly a non-issue (an asset field pointing at an unsaved runtime object is already broken data), but it is a distinct, smaller hole and it deserves the same "detect and report, do not silently null" treatment §5.3 prescribes for deleted targets.

**Measured vs reasoned:** everything above is measured in the live editor. Nothing here is reasoning-from-documentation — and in particular I did *not* have to reason about instance-ID stability across a restart, because the premise (that a non-zero instance ID is emitted at all) is false. No editor restart was performed and none is needed.

### What this changes in the design

- **§5.1's blockquote caveat is invalidated and should be struck**, along with its conclusion that *"the sidecar writer must post-process the JSON"*. The mechanism is already restart-safe and machine-portable for cross-asset references.
- **§5.3's closing parenthetical** (*"the on-disk form of that pointer must be GUID + local file ID, because what `EditorJsonUtility` actually emits is a session-local `instanceID`"*) is invalidated for the same reason. The rest of §5.3 stands: a seal is still a pointer, not contents, so §6's closure problem is untouched.
- **§13.1 item 1 resolves in the "simpler than §5.1 claims" direction.** The remap-and-unmap layer comes out of Phase 2's scope entirely. `ToJson` → sidecar → `FromJsonOverwrite` is a straight round trip.
- **§5.1's recommendation of `EditorJsonUtility` is strengthened, not weakened** — it was recommended *despite* a caveat that turns out not to exist.
- **New, small item for Phase 2:** at seal time, flag any `{"instanceID": 0}` that corresponds to a field the user believes is populated (i.e. a reference to a non-persisted object), rather than sealing a silent null. This is a few lines and belongs next to §5.2's round-trip verify, which would catch it anyway.

---

## Measurement 2 — do any LauAsset types carry sub-assets?

### The claim as the design doc states it

§5.3b: *"`EditorJsonUtility.ToJson` serialises exactly one object. If a LauAsset has sub-assets ... a seal captures the main object and silently omits every one of them."* §13.1 item 2: *"If none do, the seal format stays a single blob and §5.3b collapses to a guard."*

### Raw measured output

Method: `AssetDatabase.FindAssets("t:ScriptableObject")`, restricted to `.asset` files under `Assets/`, main asset loaded, its `Type` passed by reflection to `Laubrary.AssetKit.Editor.LauAssetHook.IsAssetReference`, then `AssetDatabase.LoadAllAssetsAtPath` on every match.

```
scanned .asset ScriptableObjects under Assets/: 158
LauAssets (IsAssetReference == true): 110
LauAssets with MORE THAN ONE object at their path: 2
ToJson failures: 0

--- SUB-ASSET DETAIL ---
  Assets/Mirage/MirageDemo.asset  [main=MirageView]  objects=3
      + type=UnityEngine.Texture2D  name="Thumbnail"  hideFlags=None
      + type=UnityEditor.AssetImporters.ImportLog  name="Import Logs"  hideFlags=HideInHierarchy
  Assets/Mirage/Test 2.asset  [main=MirageView]  objects=2
      + type=UnityEngine.Texture2D  name="Thumbnail"  hideFlags=None
```

Census of the affected type, and the 28 distinct LauAsset types present:

```
MirageView assets: 2   of which carry a Texture2D sub-asset: 2

--- all LauAsset type names present ---
  AmmoDef, BackSplash, Brain, CartographerBiome, Choreography, ChunkSpec, Faction,
  LatheMoldAsset, LatheSpec, Lauminary, LauminaryVersion, LauTagLibrary, LazorShape,
  MirageView, Prop, Pyre, SimpleMenuSettings, SimpleUISettings, SpriteChunkAnimation,
  SpriteFxSpec, TextSplash, TileTag, WareSpec, WeaponDef, Zoe, ZoundsEditorPresets,
  ZoundsWindowProperties, ZuiToolStateStore
```

Provenance — every `AddObjectToAsset` call site in `Assets/`:

```
Assets/Packages/Laubrary/Runtime/Mirage/MirageView.cs:62      AssetDatabase.AddObjectToAsset(capture, path);
Assets/Packages/Laubrary/Editor/TextSplash/SplashBorderFontBaker.cs:297   AssetDatabase.AddObjectToAsset(obj, asset);
Assets/Plugins/Shapes/Scripts/Editor/Utils/PrimitiveGenerator.cs:65       (third-party, not a LauAsset)
Assets/Plugins/Shapes/Scripts/Editor/Windows/ShapesConfigWindow.cs:223    (third-party, not a LauAsset)
```

### Verdict

**Not "none" — but close, and the two cases are not authored data.** Exactly one LauAsset *type* carries a sub-asset today: `MirageView`, via `MirageView.SetThumbnail` (`Runtime/Mirage/MirageView.cs:54-75`), which embeds a captured `Texture2D` named `Thumbnail` and thereafter overwrites its pixels in place rather than accumulating orphans. Both `MirageView` assets in the project have one. The `ImportLog` on `MirageDemo.asset` is Unity's own import-diagnostics object (`hideFlags=HideInHierarchy`), not project data, and would be present or absent independently of anything this feature does — a sub-asset guard must not count it.

The other Laubrary `AddObjectToAsset` site, `SplashBorderFontBaker` (`Editor/TextSplash/SplashBorderFontBaker.cs:297`), attaches atlas textures and a material into a `TMP_FontAsset`. `TMP_FontAsset` is in the `TMPro` namespace, implements no `IVisualPreview` and has no `LauAssetEditors` registration, so `IsAssetReference` returns false for it and it is outside the sealed set — confirmed by measurement, since the `TextSplash` asset itself (which references that font rather than containing it) came back with exactly one object at its path. This is the near-miss to watch: the pattern exists in Laubrary's own editor code, so "no LauAsset has sub-assets" is a property of the current type set, not a structural guarantee.

The important qualifier for the design decision: the `Thumbnail` texture is **regenerable preview output**, not authored truth. Losing it in a seal costs a re-capture, not work.

### What this changes in the design

- **§5.3b does not collapse to nothing, but it does collapse to the cheap option.** Its "Refuse" row remains the right Phase 2 default, and its "Capture the set" and "Copy the file" rows stay unbuilt. The measured evidence does not support paying for a multi-object seal format up front.
- **But a naive "refuse if `LoadAllAssetsAtPath().Length > 1`" guard is wrong as written** — it would refuse to seal both `MirageView` assets in the project on day one, over a thumbnail, and would intermittently refuse any asset that happens to have an `ImportLog` attached. The guard needs two exclusions before it ships: ignore `UnityEditor.AssetImporters.ImportLog` (and anything with `HideFlags.HideInHierarchy` that is not project data), and treat a regenerable preview sub-asset as non-blocking. For `MirageView` specifically the correct behaviour is to seal the main object and let the thumbnail re-capture, not to refuse.
- **§5.3b's sentence "if no LauAsset actually uses sub-assets it costs nothing forever" needs softening** — one does, and the mechanism is available to any future tool. The guard should name the offending sub-objects in its message so the next type that adopts the pattern surfaces immediately rather than silently.
- **§13.1 item 2 resolves as "one type, regenerable data".** Phase 2's seal format stays a single JSON blob.

---

## Measurement 3 — how big is a real snapshot?

### The claim as the design doc states it

§11.2: *"Cost: unmeasured, and this is the one number in the document that should be measured before committing to the feature. ... The optimistic guess is a few KB per entry, but a `Pyre` with a dozen `[SerializeReference]` layer forms is not obviously small."* §13.1 item 3: *"If a snapshot is hundreds of KB, rolling auto-snapshots need dedup-by-hash before they ship, not after."*

### Raw measured output

Metric: `System.Text.Encoding.UTF8.GetByteCount(EditorJsonUtility.ToJson(asset, false))` — unindented, which is what a sidecar would actually store — over the same 110-asset LauAsset set as measurement 2.

```
--- TOP 20 BY ToJson UTF-8 BYTES ---
   1.   329687  Assets/Pyre/Imported/SparkleBurst Plus.asset  [Pyre]
   2.   293301  Assets/Pyre/Imported/Rose Blast Plus.asset  [Pyre]
   3.   253453  Assets/Pyre/Imported/Explotion Twirl Plus.asset  [Pyre]
   4.   252444  Assets/Pyre/Imported/New Pyre Plus.asset  [Pyre]
   5.   251150  Assets/Pyre/New Pyre Plus§.asset  [Pyre]
   6.   250823  Assets/Pyre/Imported/NoiseField Ball Plus.asset  [Pyre]
   7.   234394  Assets/Pyre/Imported/SmokePuff Plus.asset  [Pyre]
   8.   215137  Assets/Pyre/Imported/Fluid Blast Plus.asset  [Pyre]
   9.   206451  Assets/Pyre/Imported/Proper Blast.asset  [Pyre]
  10.   194024  Assets/Pyre/Imported/ProperBlast Plus.asset  [Pyre]
  11.   189619  Assets/Pyre/Imported/HollowBlast Plus.asset  [Pyre]
  12.   152291  Assets/Pyre/New Pyre Plus.asset  [Pyre]
  13.   144604  Assets/Pyre/Imported/SparkleBlast Plus.asset  [Pyre]
  14.   138888  Assets/Pyre/Imported/Old School Explo 2 Plus.asset  [Pyre]
  15.   138078  Assets/Pyre/Imported/Metafield Pyre 1 Plus.asset  [Pyre]
  16.   136514  Assets/Pyre/Imported/Metafield Pyre Plus.asset  [Pyre]
  17.   110506  Assets/Pyre/Green Lantern.asset  [Pyre]
  18.   101663  Assets/Pyre/Imported/Directional Grenade Blast 2 Plus.asset  [Pyre]
  19.   101228  Assets/Pyre/Imported/Directional Grenade Side Blast Plus.asset  [Pyre]
  20.    99343  Assets/Pyre/Imported/Directional Grenade Blast 1 Plus.asset  [Pyre]

count=110  totalBytes=5022331  medianBytes=1671  meanBytes=45657  minBytes=191
```

Distribution and per-type concentration (a second probe over the same 110 assets; its `p50=1684` and the first probe's `medianBytes=1671` are the same data under the two usual mid-index conventions on an even-sized set, not a discrepancy):

```
n=110
p50=1684  p75=71721  p90=189619  p95=250823  max=329687
>1KB: 79   >10KB: 38   >100KB: 17
total=5022331 bytes   x10 rolling ring = 49046 KB if every LauAsset were touched

--- per-type (count / totalBytes / maxBytes), types with total > 20KB ---
  Pyre                   n= 32  total= 4690503  max=  329687
  LauminaryVersion       n=  7  total=  177983  max=   91148
  ChunkSpec              n=  6  total=   36293  max=    7382
  SpriteFxSpec           n=  3  total=   34507  max=   23987
  TextSplash             n=  1  total=   20946  max=   20946
```

The three types §13.1 named specifically, plus neighbours:

```
Zoe                  n=  6  total=   10853  max=    2759  Assets/Demos/ProtoGuyDemo/ProtoGuy.asset
WeaponDef            n=  3  total=    2458  max=     844  Assets/Demos/PreviewDemo/PreviewWeapon.asset
AmmoDef              n=  2  total=    1336  max=     696  Assets/Zoetrope/Hero Gun Ammo.asset
Brain                n=  1  total=    1879  max=    1879  Assets/Demos/DaemonDemo/DemoBrain.asset
CartographerBiome    n=  1  total=     642  max=     642  Assets/Demos/CartographerDemo/Settlement.asset
LatheSpec            n=  1  total=    5369  max=    5369  Assets/Lathe/New Lathe.asset
Lauminary            n=  4  total=    1145  max=     295  Assets/Launimator/Reels/Floating Disc_4da19404/Floating Disc.asset
MirageView           n=  2  total=    4014  max=    2330  Assets/Mirage/MirageDemo.asset

Laubrary ScriptableObject types matching *Level*/*Map*/*Grid*:
  Laubrary.Cartographer.LevelAsset
  Laubrary.Cartographer.LevelRecipe
  Laubrary.Cartographer.LevelTile
```

### Verdict

**Hundreds of KB, exactly as §11.2 feared — but concentrated almost entirely in one type.** The largest snapshot is **329,687 bytes (322 KB)**, and **17 of 110** LauAssets exceed 100 KB. Every one of those 17 is a `Pyre`. `Pyre` is 32 of 110 assets (29% by count) but **4,690,503 of 5,022,331 bytes — 93.4% of all LauAsset JSON in the project**. Its `[SerializeReference]` layer-form graph is precisely the cost §11.2 suspected.

The distribution is sharply bimodal, which is why a single "typical snapshot size" is misleading: the **median is 1,684 bytes** — the doc's "few KB per entry" guess is correct for the median asset — while the **p75 is 71,721 bytes** and the p90 is 189,619. Put plainly: three quarters of LauAsset types make snapshots that cost nothing, and one type makes snapshots ~200x larger than the median.

Concrete ring costs at the recommended N=10:

- Largest single `Pyre`: **3.22 MB** for one asset's history.
- All 32 `Pyre` assets, if each were touched in a session: **44.7 MB**.
- All 110 LauAssets: **~47.9 MB**.

`Zoe` is a non-issue: the biggest is 2,759 bytes, so a 10-deep ring on every `Zoe` in the project costs 108 KB total. `LevelAsset` was checked and the type **exists** (`Laubrary.Cartographer.LevelAsset`) but **zero instances are authored in this project**, so it contributed nothing and its real size is unknown — worth re-measuring once Cartographer levels get authored, since a tile grid is the other shape that serialises large.

### What this changes in the design

- **§13.1 item 3 resolves in the "hundreds of KB" direction, so its own stated consequence applies: dedup-by-hash ships with the feature, not after it.** Concretely, the mitigation §11.2 already names — *"skip a snapshot when the JSON is byte-identical to the previous one"* — moves from a listed contingency into Phase 1's definition of done. It is also the cheapest of the three mitigations and the one with the best hit rate, since a dial nudge that gets undone, or a re-save with no change, produces a byte-identical blob.
- **Byte-identity dedup alone is not sufficient for `Pyre` and the design should say so.** Nudging one slider on a `Pyre` changes a handful of bytes inside a 322 KB blob, and dedup-by-hash will correctly decide it is different and store the whole thing again. Ten nudges is 3.2 MB of near-duplicate text. Two options, both cheap, and Phase 1 should pick one: **per-type N** (§11.2 already floats "lower N per type" — e.g. N=10 default, N=3 for anything whose last snapshot exceeded ~64 KB), or **compress the payload** (JSON of this shape gzips extremely well; it is repetitive `rid`/`type`/float-array text). My lean is per-type N first, because it is a config value rather than a format change and it keeps the sidecar human-readable, which §4b wants.
- **§11.2's `.gitignore` recommendation is confirmed as non-optional rather than merely tidy.** At ~48 MB per full sweep of the project, committing this folder would be a repository problem, not a cosmetic one.
- **§11.2's headline claim survives.** "Highest value-per-line in the document" is still defensible: for 93 of 110 assets the snapshot is under 100 KB and for the median asset it is under 2 KB, so the feature is genuinely nearly free everywhere except `Pyre` — and `Pyre` is exactly the tool where "I ruined it by fiddling" is most likely, so it is the one that most needs the safety net. The right response is to pay for `Pyre` deliberately, not to weaken the feature.

---

## What could not be measured, and why

- **Nothing in §13.1's three items was left unmeasured.** All three ran cleanly in the live editor.
- Three things adjacent to them were deliberately **out of scope** and are stated here so they are not mistaken for verified: (a) `FromJsonOverwrite` restore fidelity was **not** exercised, because doing so means writing into an object and this task was read-only — §5.2's round-trip-verify recommendation therefore remains unverified; (b) `LevelAsset` snapshot size is unknown because the project authors zero instances of the type; (c) the two extra empirical checks §13.1 lists in its closing paragraph — whether `OnPostprocessBuild` runs on a failed or cancelled build (§8), and whether `HideFlags.NotEditable` actually greys a ScriptableObject Inspector here (§9.4) — were not part of this task and were not run.
- One reasoning-not-measurement note, flagged for honesty: measurement 1's control case established that a **non-persisted** object reference is emitted as `{"instanceID": 0}`. That it therefore restores as `null` follows from the same value being emitted for an explicit `null`, which is measured — but the restore itself was not executed, per the read-only constraint above.
