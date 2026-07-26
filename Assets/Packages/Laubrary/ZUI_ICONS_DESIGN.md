# ZUI — embedded icon library (`Icons~`)

**Status:** IMPLEMENTED in the dev host (steps 1–8), 2026-07-26 — commits `fa9e661` (loader + wiring) and `dbbe788` (the folder move). Verified live: 1208 icons load off disk and decode, `GetAvailableIcons`=1208 all-sentinel, `FindIcon` resolves sentinel + stale-full-path + bare-name, the AssetDatabase refresh dropped the 1208 entries with no missing-GUID/broken-ref errors. **Step 9 (re-sync the 5 consumer projects) is still open** — a separate out-of-repo pass. Raised 2026-07-26 while upgrading the five consumer projects to Laubrary 0.9.0.

**Goal in one line:** ZUI's 1208 system icons should stay usable by name, but stop being Unity assets — no import cost, and never listed in any asset picker.

## Why

Measured on 2026-07-26 against the dev host and all five consumer projects (Asteroid+, OutBurner, TrueEye, RiskyRemake, TinyWar):

| | |
|---|---|
| Icons shipped in `Zui/SystemAssets/Icons/` | 1208 PNGs, 5.79 MB (9.69 MB with `.meta`), avg 4.9 KB |
| Named explicitly in ZUI code (the built-in aliases) | 21 |
| Named anywhere in the whole package | 35 |
| **Bound by GUID from any authored `.asset`, in any of the six projects** | **0** |
| Bytes in a player build | **0** — not under `Resources/`, not scene-referenced |

So the cost is entirely editor-side: 1208 texture imports per project (the long first-import after a package sync), 1208 Library artifacts, and 1208 entries polluting every `Texture2D` object picker in the project. The ~1173 icons beyond the code-referenced ones exist only so the Style Editor's icon picker has a palette to browse — a legitimate reason to exist somewhere, but not a reason to be mandatory Unity assets.

## What does NOT work (recorded so it isn't re-proposed)

**Moving them to `Samples~/` fails both goals.** A `~`-suffixed folder is invisible to Unity — no import, no `.meta`, no GUIDs, no AssetDatabase entries — so `AssetDatabase.FindAssets` / `LoadAssetAtPath` cannot see inside it and every icon lookup returns null. The Package Manager's "Import" button does not unhide them in place; it **copies** them to `Assets/Samples/Laubrary/<version>/<name>/`, where they are fully visible in every asset picker (the opposite of the goal) and at a path `k_SystemIconsPath` does not point at.

**Packing into one atlas + a lookup asset** would cut picker pollution from 1208 entries to 1 and import cost to 1 file, but they still import, still appear, and the picker needs UV slicing. Strictly more work for a strictly worse result than the design below. Not chosen.

## Design

Keep the PNGs inside the package but in a Unity-invisible folder, and load them off disk instead of through the AssetDatabase:

- `Zui/SystemAssets/Icons/` → **`Zui/SystemAssets/Icons~/`** (the trailing `~` is what makes Unity ignore it; all `.meta` files for the icons are deleted with the move).
- ZUI resolves an icon by reading the file with `File.ReadAllBytes` and decoding it with `ImageConversion.LoadImage` into a `Texture2D` held in a static name→texture cache.

**This is only viable because ZUI already resolves icons by name, not by object reference** — that is the enabling fact, verified in code: `ZUIStyleSheetAsset.iconAliases` is a `List<ZUIAssetAlias>` of name→path *strings*; the one object-reference field, `iconLibrary`, is already marked `[HideInInspector] // legacy — kept for serialization`; and `ZUIAssetLibrary.FindIconDirect` is a pure name-resolution chain (alias → system folder → sheet data folder → `Resources` → null) already wrapped in `#if UNITY_EDITOR`. No authored data has to change.

**Sentinel path scheme.** Embedded icons have no asset path, but several call sites pass icon paths around as strings. Give them a stable pseudo-path — `zui://icons/<file>.png` — returned by `GetAvailableIcons()` and accepted by `FindIconDirect`. That keeps every existing string-based code path and every already-authored alias working, and gives the two editor windows a cleaner discriminator than the folder-path prefix comparison they do today.

## To-do

Work in `Assets/Packages/Laubrary/Zui/`. Ordered; each step should compile before starting the next.

1. **Add the embedded-icon loader** to `Scripts/Runtime/ZUIAssetLibrary.cs`: a static `Dictionary<string, Texture2D>` cache plus `Texture2D LoadEmbeddedIcon(string name)` that resolves the file, does `File.ReadAllBytes` → `new Texture2D(2,2)` → `ImageConversion.LoadImage`, sets `hideFlags = HideFlags.HideAndDontSave` and `filterMode = FilterMode.Bilinear`, caches, and returns. Return null (do not throw) on a missing file.
2. **Resolve the absolute folder path.** `InstallPath` (line ~50) returns a *project-relative* path, found by locating `ZUIAssetLibrary.cs` through `AssetDatabase` — that still works, since the script itself stays a normal asset. Convert to absolute with `Path.GetFullPath(InstallPath + "/SystemAssets/Icons~")`; in the editor the CWD is the project root, so this resolves correctly for both the dev-host layout (`Assets/Packages/Laubrary/Zui`) and the consumer layout (`Packages/com.lautaro.arino.laubrary/Zui`). Add `k_SystemIconsPathAbs` next to `k_SystemIconsPath` (line 75) rather than changing the existing property's meaning.
3. **Route system-icon lookup to the loader.** In `FindIconDirect` (line 133), replace the `FindTextureInFolder(k_SystemIconsPath, name)` call at line 146 with the embedded loader, and add a branch that recognises the `zui://icons/` sentinel prefix ahead of the existing `name.StartsWith("Assets/")` branch. Leave the sheet `dataFolderPath` branch AssetDatabase-based — those are the user's own icons and *should* stay pickable.
4. **Make the picker enumerate the invisible folder.** In `GetAvailableIcons` (line ~236), replace `ScanFolder(k_SystemIconsPath, "t:Texture2D", result)` at line 238 with a `Directory.GetFiles(k_SystemIconsPathAbs, "*.png")` enumeration emitting `(name, "zui://icons/<file>.png")`. Leave the two `dataFolder` `ScanFolder` calls untouched.
5. **Fix the two path-prefix discriminators** that classify an icon as system-vs-custom by comparing against `k_SystemIconsPath`: `Scripts/Editor/ZUIStyleEditorWindow.cs:5094` and `Scripts/Editor/ZUITextureEditor.cs:676`. Switch both to testing the `zui://icons/` prefix. Also update the diagnostic label at `ZUIStyleEditorWindow.cs:5159`.
6. **Check the built-in aliases** in `Scripts/Editor/ZUI.cs:198-220` — they are declared as `sysIcons + "/arrow-up.png"` and friends. Either keep building them from `k_SystemIconsPath` and let step 3's sentinel branch handle the old form, or switch them to bare names (which already resolve through the system branch). Bare names are cleaner; whichever is chosen, all 21 must still resolve.
7. **Do the move:** `git mv Zui/SystemAssets/Icons Zui/SystemAssets/Icons~` and delete the 1208 `.cs`-adjacent `.meta` files plus `Icons.meta`. Safe because 0 authored assets reference any icon by GUID — re-verify that before pulling the trigger, since it is the whole safety argument.
8. **Verify in the dev host:** every ZUI window still shows its icons; the Style Editor icon picker still lists ~1208 entries and can assign one; a sheet authored with an embedded icon survives a domain reload and an editor restart.
9. **Re-sync the five consumers** and confirm the icon count drops to ~0 imported assets under `Zui/SystemAssets/`. See the consumer-upgrade recipe — replace the package folder wholesale; do not merge.

## Acceptance criteria

- Selecting any `Texture2D` object field anywhere in a consumer project lists **no** ZUI system icons.
- A fresh package sync into a consumer imports **0** icon assets; `Library` gains no icon artifacts.
- All 21 code-referenced aliases render, in both the dev-host and consumer package layouts.
- The Style Editor icon picker still browses the full set and can assign an icon to a sheet.
- Player build size is unchanged (it was already 0 for icons — this must not accidentally add a `Resources` dependency).

## Caveats and non-goals

- Textures from `ImageConversion.LoadImage` are transient objects, not assets. They need `HideFlags.HideAndDontSave` so they survive scene changes without leaking into a scene. A domain reload clears the static cache; it refills lazily, which is fine.
- Editor-only by construction. `System.IO` is always available there, and ZUI's icon path is already `#if UNITY_EDITOR`. The `Resources.Load` fallback at the end of `FindIconDirect` serves genuine runtime UI and must stay untouched.
- **Out of scope but adjacent:** `Zui/SystemAssets/Resources/ZUIRuntimeDefaultSheet.asset` (192 KB) *is* force-included in every player build because anything under a `Resources/` folder always is — and in consumer projects two of its three references point at `Demos/UI Demo/9slice.png` and `ZUIDemo/DemoFrame.png`, which are dev-host-only assets. That is the one genuinely unavoidable ZUI build payload, and it ships slightly broken. Worth a separate pass.
- Fonts (`Zui/SystemAssets/Fonts`, `k_SystemFontsPath`) are deliberately left alone — there are few of them and `Font` pickers are not meaningfully polluted.
