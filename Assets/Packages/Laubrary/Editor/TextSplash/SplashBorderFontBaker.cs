using System.IO;
using UnityEngine;
using UnityEditor;
using TMPro;

namespace Laubrary.TextSplash.Editor
{
    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Bakes a splash's padded border font ONCE, at author time, into a committed project asset.
    ///
    /// <see cref="SplashFontBaker"/> can bake the same twin at runtime, and in the Editor it always succeeds — but it
    /// needs a `UnityEngine.Font` to rasterise from, and TMP deliberately clears `sourceFontFile` on a STATIC font
    /// asset (the stock `LiberationSans SDF` is one), keeping only the source file's GUID. Nothing but the
    /// AssetDatabase can turn a GUID back into a Font, so that fallback exists in the Editor and genuinely cannot
    /// exist in a player: the bake fails there, the border quietly falls back to the face's own ~4%-of-an-em ceiling,
    /// and a splash that looked right while authoring ships with a hairline. Baking now, into a file, is what makes
    /// the two agree — the asset is assigned to <see cref="TextSplash.bakedBorderFont"/> and the border pass reads it
    /// instead of rasterising anything.
    ///
    /// The saved asset must be LAYOUT-IDENTICAL to the face, because the border is a second TMP component running its
    /// own layout pass over the same string: a different advance, kern pair or line height and the border creeps out
    /// from under its letters, further with every character. That is <see cref="SplashFontBaker"/>'s
    /// `MirrorLayoutState`, which this path goes through unchanged — nothing about the metrics is re-derived here.
    ///
    /// No menu item and no button: <see cref="SplashBorderAutoBake"/> calls this whenever a splash asks for a border
    /// its font cannot draw, and again for every splash in the project before a build.</summary>
    public static class SplashBorderFontBaker
    {
        /// <summary>Bake <paramref name="spec"/>'s border font, save it as a project asset, and STAMP the spec with
        /// what was made and what it was made from — <see cref="TextSplash.bakedBorderFont"/> plus the record
        /// <see cref="TextSplash.NeedsBorderBake"/> reads. The stamp belongs here rather than in the caller because
        /// without it a bake is invisible: the splash would go on asking to be baked, and the automatic maintenance
        /// would oblige, every time it looked.
        ///
        /// The padding and atlas size are the spec's own — <see cref="TextSplash.ResolveBorderPadding"/> against the
        /// face's sampling size, and <see cref="TextSplash.borderAtlasSize"/> — read exactly as
        /// <see cref="SplashPlayer"/> reads them, so the committed asset is the asset a play would have produced.
        ///
        /// Re-baking updates the existing asset IN PLACE, keeping its GUID and every reference to it: a twin THIS
        /// baker made is re-baked where it lives, whatever it has since been renamed or moved to, and
        /// <paramref name="folder"/> only decides where a FIRST bake lands. A font asset the user assigned by hand is
        /// never written over (see <see cref="IsOwnOutput"/>) — and never re-rasterised either, when it already does
        /// the job (see <see cref="CanKeep"/>).
        ///
        /// Returns null — having said why in the console — when there is nothing to bake from, when the face already
        /// carries enough padding to draw the border by itself, or when the bake fails.</summary>
        /// <param name="spec">The splash whose border font this is.</param>
        /// <param name="folder">Project folder for a first bake, e.g. "Assets/TextSplash". Created if missing.
        /// Empty = the default folder beside the spec.</param>
        public static TMP_FontAsset Bake(TextSplash spec, string folder)
        {
            if (spec == null) return null;

            // The face the splash actually renders with, resolved exactly as SplashPlayer.ApplyLook resolves it — a
            // twin baked from a different font than the one it sits under would drift by design.
            TMP_FontAsset source = spec.font != null ? spec.font : TMP_Settings.defaultFontAsset;
            if (source == null)
            {
                Debug.LogWarning($"[TextSplash] \"{spec.name}\" has no font and TMP has no default font asset, so " +
                                 "there is nothing to bake a border font from.", spec);
                return null;
            }

            int padding = spec.ResolveBorderPadding(source.faceInfo.pointSize);
            if (padding <= source.atlasPadding)
            {
                Debug.Log($"[TextSplash] \"{spec.name}\" needs {padding} texels of border padding and " +
                          $"\"{source.name}\" already carries {source.atlasPadding}, so no baked border font is " +
                          "needed — the border pass draws with the face itself, in a build too.", spec);
                return null;
            }

            // A twin that is already good enough is KEPT, not re-made. An asset the user assigned by hand — or one
            // this baker saved before there was a stamp to prove it — carries no record of where it came from, so it
            // reads as stale forever; but its own metrics are that record. Stamping it costs nothing and replacing it
            // would cost the user a file they chose, plus a rasterisation nobody needed.
            if (CanKeep(spec, source, padding))
            {
                Stamp(spec, spec.bakedBorderFont, source);
                return spec.bakedBorderFont;
            }

            // Resolved BEFORE the bake: a folder that cannot be written to should cost nothing.
            string path = ResolvePath(spec, source, folder);
            if (string.IsNullOrEmpty(path)) return null;

            TMP_FontAsset baked = SplashFontBaker.BakeForAsset(source, padding, spec.borderAtlasSize);
            if (baked == null) return null;                     // BakeForAsset said why

            TMP_FontAsset saved = Save(baked, path);
            if (saved == null) return null;

            WarnIfSuperseded(spec, saved);
            Stamp(spec, saved, source);

            Debug.Log($"[TextSplash] Saved border font for \"{spec.name}\" at {path} — it carries its own atlas and " +
                      $"material, reaches {SplashFontBaker.MaxOutwardEm(saved):0.###} em of border, and is what the " +
                      "border pass uses in a player build. Nothing to press: it is re-baked by itself whenever the " +
                      "splash's font, border width, atlas padding or atlas size stops matching it.", saved);
            return saved;
        }

        /// <summary>Whether <paramref name="spec"/>'s committed twin is missing, stale, or was never made — the
        /// question the automatic maintenance asks before spending a rasterisation, and the one the build guarantee
        /// asks before letting a player ship. The face it reasoned about comes back in <paramref name="source"/>, so a
        /// caller does not resolve it a second time and risk resolving it differently.
        ///
        /// This is <see cref="TextSplash.NeedsBorderBake"/> plus the two cases that look like a missing bake and are
        /// not: a splash with no font at all (nothing to bake FROM — an unfinished asset, not a broken one), and a
        /// face whose own padding already draws the whole border. <see cref="Bake"/> declines both, so a maintenance
        /// loop that asked the asset directly would ask, be declined, and ask again on the next frame forever.</summary>
        internal static bool NeedsBake(TextSplash spec, out TMP_FontAsset source)
        {
            source = null;
            if (spec == null) return false;

            source = spec.font != null ? spec.font : TMP_Settings.defaultFontAsset;
            if (source == null) return false;

            if (spec.ResolveBorderPadding(source.faceInfo.pointSize) <= source.atlasPadding) return false;

            return spec.NeedsBorderBake(source);
        }

        /// <summary>Record on the spec what the twin IS and what it was baked FROM.
        ///
        /// <see cref="TextSplash.NeedsBorderBake"/> compares the live settings against exactly these three values, so
        /// an unstamped bake reads as no bake at all. The padding recorded is the one the saved asset ACTUALLY
        /// carries, not the one that was asked for: if those two ever disagree, an honest record is what stops the
        /// next caller believing a short bake was a good one — and what lets the maintenance notice it instead of
        /// baking the same shortfall over and over.</summary>
        static void Stamp(TextSplash spec, TMP_FontAsset twin, TMP_FontAsset source)
        {
            Undo.RecordObject(spec, "Bake Splash Border Font");

            spec.bakedBorderFont    = twin;
            spec.bakedBorderSource  = source;
            spec.bakedBorderPadding = twin.atlasPadding;
            spec.bakedBorderAtlas   = spec.borderAtlasSize;

            EditorUtility.SetDirty(spec);
        }

        /// <summary>Whether the twin already assigned can simply be KEPT — no rasterising, no new file.
        ///
        /// Two things make a twin correct: enough padding to reach the border, and a layout identical to the face's,
        /// because the border is a second TMP component running its own layout pass over the same string. The first
        /// is one number; the second is precisely what `SplashFontBaker.MirrorLayoutState` copies across, so
        /// comparing those same values against the face answers "would a fresh bake differ?" without doing one.
        ///
        /// The atlas must also be at least as big as the spec asks for — a smaller page is not wrong, but it is less
        /// than was requested, and keeping it would quietly overrule the request.</summary>
        static bool CanKeep(TextSplash spec, TMP_FontAsset source, int padding)
        {
            TMP_FontAsset twin = spec.bakedBorderFont;
            if (twin == null || twin == source) return false;

            // An object that is not a file cannot survive a reload, so stamping one would only look like success.
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(twin))) return false;

            if (twin.atlasPadding < padding) return false;
            if (twin.atlasWidth < spec.borderAtlasSize || twin.atlasHeight < spec.borderAtlasSize) return false;

            var a = twin.faceInfo;
            var b = source.faceInfo;
            return Mathf.Approximately(a.pointSize, b.pointSize)
                && Mathf.Approximately(a.scale, b.scale)
                && Mathf.Approximately(a.lineHeight, b.lineHeight)
                && Mathf.Approximately(a.ascentLine, b.ascentLine)
                && Mathf.Approximately(a.descentLine, b.descentLine)
                && Mathf.Approximately(a.baseline, b.baseline)
                && Mathf.Approximately(twin.normalSpacingOffset, source.normalSpacingOffset)
                && Mathf.Approximately(twin.boldSpacing, source.boldSpacing)
                && Mathf.Approximately(twin.tabSize, source.tabSize);
        }

        /// A hand-assigned twin that could be neither adopted nor written to is being dropped for a file the user
        /// never asked for, which is exactly the kind of thing that should not happen silently.
        static void WarnIfSuperseded(TextSplash spec, TMP_FontAsset saved)
        {
            TMP_FontAsset previous = spec.bakedBorderFont;
            if (previous == null || previous == saved || spec.bakedBorderSource != null) return;

            Debug.LogWarning($"[TextSplash] \"{spec.name}\" was pointed at \"{previous.name}\" as its border font, but " +
                             $"that asset does not lay out like \"{spec.name}\"'s face or does not carry enough " +
                             $"padding for its border, so a fresh one was baked and assigned instead. " +
                             $"\"{previous.name}\" is untouched on disk — delete it if nothing else uses it.", spec);
        }

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────
        // Saving
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────

        /// Shared with SplashPixelFontBaker: saving a generated TMP font asset into the project is the same job
        /// whichever bake produced it. That this (and EnsureFolder/Sanitize below) still lives on the BORDER baker
        /// is a known smell — the saving concern wants its own type once a third baker needs it.
        internal static TMP_FontAsset Save(TMP_FontAsset fresh, string path)
        {
            string assetName = Path.GetFileNameWithoutExtension(path);
            TMP_FontAsset target = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

            if (target != null) AdoptInPlace(fresh, target, path);
            else { AssetDatabase.CreateAsset(fresh, path); target = fresh; }

            // The main object's name is what TMP hashes the asset (and its material) by, so it is set before anything
            // reads it, and set to the FILE name — an asset whose object name disagrees with its file is a trap.
            target.name = assetName;

            AttachOwnData(target, assetName);
            KeepDynamicDataInBuilds(target);

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            // Read back what was actually written, then rebuild the glyph/character/kerning lookup dictionaries: those
            // are runtime-only state, so the object an import hands back has every table and none of the maps a
            // layout pass reads.
            TMP_FontAsset reloaded = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (reloaded != null) target = reloaded;
            if (target != null) target.ReadFontAssetDefinition();

            return target;
        }

        /// <summary>Pour a fresh bake into the asset that is already at this path, rather than replacing the file.
        ///
        /// Overwriting the existing OBJECT is what keeps the asset's GUID — and with it every reference to the font:
        /// the spec's own <see cref="TextSplash.bakedBorderFont"/> field, another spec sharing it, a scene that points
        /// at it. Deleting and re-creating the file would break all of them silently, one bake at a time.</summary>
        static void AdoptInPlace(TMP_FontAsset fresh, TMP_FontAsset existing, string path)
        {
            // Captured BEFORE the swap. Afterwards `existing` points at the FRESH pages and material, and everything
            // in this list except the asset itself is the previous bake's leftovers.
            Object[] stale = AssetDatabase.LoadAllAssetsAtPath(path);

            // CopySerialized carries hide flags across with everything else, and DontSave on a file is exactly how an
            // asset ends up written empty.
            fresh.hideFlags = HideFlags.None;
            EditorUtility.CopySerialized(fresh, existing);

            // TMP_FontAsset.OnDestroy destroys its atlas textures AND its material — which, after the copy, are the
            // asset's own data. The donor has to let go of them before it goes away, or it takes them down with it.
            fresh.atlasTextures = null;
            fresh.material = null;
            // TMP keeps every font asset it has read in a static lookup keyed by instance id and name hash; leaving a
            // destroyed one in there is how a later rebuild trips over a dead reference.
            TMP_ResourceManager.RemoveFontAsset(fresh);
            Object.DestroyImmediate(fresh);

            // The previous bake's pages and material: still inside the file, now referenced by nothing. Destroying
            // them is what keeps a re-bake from leaving a second (dead) atlas in the asset every time it runs.
            for (int i = 0; i < stale.Length; i++)
            {
                Object o = stale[i];
                if (o == null || o == existing) continue;
                Object.DestroyImmediate(o, true);
            }
        }

        /// <summary>Write the atlas pages and the material INTO the asset file.
        ///
        /// This is the classic way a saved font asset comes back empty: a font asset does not CONTAIN its atlas or its
        /// material, it references them, and a runtime-created Texture2D/Material that was never written to a file
        /// simply does not exist the next time the file is read — leaving a font asset with a null atlas that renders
        /// nothing and cannot be diagnosed from its inspector. `AddObjectToAsset` stores them in the same file, so
        /// those references resolve from then on.</summary>
        static void AttachOwnData(TMP_FontAsset target, string assetName)
        {
            Texture2D[] pages = target.atlasTextures;
            if (pages != null)
            {
                for (int i = 0; i < pages.Length; i++)
                {
                    Texture2D page = pages[i];
                    if (page == null) continue;
                    page.name = pages.Length > 1 ? $"{assetName} Atlas {i}" : $"{assetName} Atlas";
                    page.hideFlags = HideFlags.None;
                    FileIfLoose(page, target);
                }
            }

            Material mat = target.material;
            if (mat == null) return;

            // TMP's own naming convention for a font asset's material, and what its inspector looks for.
            mat.name = assetName + " Atlas Material";
            mat.hideFlags = HideFlags.None;
            FileIfLoose(mat, target);
        }

        /// Only objects that live nowhere yet: a bake always hands back loose ones, but filing an object that is
        /// already in an asset is an error, not a no-op.
        static void FileIfLoose(Object obj, Object asset)
        {
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(obj))) return;
            AssetDatabase.AddObjectToAsset(obj, asset);
        }

        /// <summary>Untick TMP's "Clear Dynamic Data On Build" on the saved asset.
        ///
        /// TMP ships a build pre-processor that wipes the character and glyph tables of EVERY dynamic font asset
        /// carrying that flag — and it defaults to on. That is precisely the data this bake exists to commit, so a
        /// build would strip the twin back to an empty atlas and the border would be back to re-rasterising at
        /// runtime, which is the thing that cannot be relied on in a player. The flag is internal to TMP, so it is
        /// written through the serialized property rather than the API.</summary>
        static void KeepDynamicDataInBuilds(TMP_FontAsset fontAsset)
        {
            var so = new SerializedObject(fontAsset);
            SerializedProperty prop = so.FindProperty("m_ClearDynamicDataOnBuild");

            if (prop == null)
            {
                Debug.LogWarning("[TextSplash] Could not find TMP's \"Clear Dynamic Data On Build\" flag on the baked " +
                                 "border font, so it may still be on. Untick it in the font asset's inspector — with " +
                                 "it on, a player build clears the very glyphs this bake just committed.", fontAsset);
                return;
            }

            if (!prop.boolValue) return;
            prop.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────
        // Where it lands
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────

        static string ResolvePath(TextSplash spec, TMP_FontAsset source, string folder)
        {
            // The face's own file is the one thing that must never be written to: this bake OVERWRITES whatever font
            // asset it lands on, and a spec whose border font was pointed at the face itself would otherwise lose the
            // project's real font to a padded twin.
            string facePath = AssetDatabase.GetAssetPath(source);

            // An already assigned twin is re-baked WHERE IT IS, so a user who renamed or moved it keeps that choice
            // and nothing pointing at it has to be repointed — but only a twin this baker can prove is its own.
            // Baking is unattended now, and an unattended bake that overwrote a font asset somebody assigned by hand
            // would destroy a file nobody asked it to touch.
            if (IsOwnOutput(spec, source))
            {
                string current = AssetDatabase.GetAssetPath(spec.bakedBorderFont);
                if (!string.IsNullOrEmpty(current) && current != facePath) return current;
            }

            if (string.IsNullOrEmpty(folder)) folder = DefaultFolder(spec);
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (!EnsureFolder(folder)) return null;

            string path = folder + "/" + FileName(spec, source);

            // Overwriting is only ever an update of a PREVIOUS BAKE. The face, or anything else already sitting at
            // that name, is somebody's file — so the bake steps aside rather than eating it.
            bool occupied = path == facePath
                         || (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) == null
                             && AssetDatabase.LoadMainAssetAtPath(path) != null);
            if (occupied) path = AssetDatabase.GenerateUniqueAssetPath(path);

            return path;
        }

        /// <summary>Whether the spec's current twin is one of this baker's own files, and so safe to write over. The
        /// stamp is the proof: it says a bake happened and what padding came out of it, and a font asset whose padding
        /// does not match that number is not the asset the stamp describes — somebody swapped it. Anything unproven
        /// gets a fresh file instead of being overwritten.</summary>
        static bool IsOwnOutput(TextSplash spec, TMP_FontAsset source)
        {
            TMP_FontAsset twin = spec.bakedBorderFont;
            if (twin == null || twin == source) return false;
            if (spec.bakedBorderSource == null) return false;
            return twin.atlasPadding == spec.bakedBorderPadding;
        }

        /// <summary>Named after the SPEC and the FACE both. Two splashes that share a font must not write to one file
        /// — the second bake would drag the first's padding along with it — and a splash that changes font gets a new
        /// file rather than silently redefining the old one, which anything else pointing at it still reads.</summary>
        static string FileName(TextSplash spec, TMP_FontAsset source)
            => $"{Sanitize(spec.name)} ({Sanitize(source.name)}) Border Font.asset";

        /// Its own folder beside the spec: where a user looks for it, in the host project either way (Laubrary ships
        /// no authored assets — a baked font belongs to the project that authored the splash, not to the package), and
        /// gathered rather than sprinkled through the folder the splashes themselves are authored in.
        const string k_SubFolder = "Border Fonts";

        static string DefaultFolder(TextSplash spec)
        {
            string specPath = AssetDatabase.GetAssetPath(spec);
            if (!string.IsNullOrEmpty(specPath))
            {
                string dir = Path.GetDirectoryName(specPath);
                if (!string.IsNullOrEmpty(dir)) return dir.Replace('\\', '/') + "/" + k_SubFolder;
            }
            return "Assets/TextSplash/" + k_SubFolder;
        }

        internal static bool EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            if (AssetDatabase.IsValidFolder(folder)) return true;

            string[] parts = folder.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
            {
                Debug.LogWarning($"[TextSplash] \"{folder}\" is not a folder the AssetDatabase can create a border " +
                                 "font in — it has to be inside this project's own Assets folder.");
                return false;
            }

            string built = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }

            return AssetDatabase.IsValidFolder(folder);
        }

        internal static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Splash";

            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++) name = name.Replace(invalid[i], '_');

            name = name.Trim();
            return string.IsNullOrEmpty(name) ? "Splash" : name;
        }
    }
}
