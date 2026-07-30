using System.IO;
using UnityEngine;
using UnityEditor;
using TMPro;

namespace Laubrary.TextSplash.Editor
{
    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Bakes a splash's PIXEL font — the bitmap face behind <see cref="SplashRasterMode.PixelFont"/> —
    /// once, at author time, into a committed project asset.
    ///
    /// It exists for the same reason <see cref="SplashBorderFontBaker"/> does, and the reason is worth restating
    /// because it is invisible until it ships: rasterising a font needs a `UnityEngine.Font`, and TMP clears
    /// `sourceFontFile` on a STATIC font asset (the stock `LiberationSans SDF` is one), keeping only a GUID.
    /// Nothing but the AssetDatabase turns a GUID back into a Font, so a runtime bake succeeds in the Editor and
    /// cannot succeed in a player. Baking into a file now is what makes the two agree.
    ///
    /// What it does NOT share with the border bake is the sizing. A border twin is the face at the face's own
    /// sampling size with more padding; a pixel font is the face at the size the SPLASH renders at, so that one
    /// font pixel lands on one buffer cell — <see cref="SplashPixelation.PixelSampling"/>. That makes the bake a
    /// function of the splash's font size AND its pixel size, so either one moving invalidates it.
    ///
    /// No menu item and no button, matching the border: <see cref="SplashBorderAutoBake"/> drives this whenever a
    /// splash in Pixel-font mode has no bake or a stale one.</summary>
    public static class SplashPixelFontBaker
    {
        /// Beside the spec, gathered rather than sprinkled — the same convention as "Border Fonts".
        const string k_SubFolder = "Pixel Fonts";

        /// <summary>Bake <paramref name="spec"/>'s pixel font, save it as a project asset, and STAMP the spec with
        /// what was made and what it was made from. The stamp belongs here rather than in the caller because
        /// without it a bake is invisible: the splash would go on asking to be baked and the automatic maintenance
        /// would oblige, every time it looked.
        ///
        /// Returns null — having said why — when there is nothing to bake from or the bake fails.</summary>
        /// <param name="spec">The splash whose pixel font this is.</param>
        /// <param name="folder">Project folder for a first bake. Empty = a default folder beside the spec.</param>
        public static TMP_FontAsset Bake(TextSplash spec, string folder)
        {
            if (spec == null || spec.pixelation == null) return null;

            TMP_FontAsset source = spec.font != null ? spec.font : TMP_Settings.defaultFontAsset;
            if (source == null)
            {
                Debug.LogWarning($"[TextSplash] \"{spec.name}\" has no font and TMP has no default font asset, so " +
                                 "there is nothing to bake a pixel font from.", spec);
                return null;
            }

            int sampling = spec.pixelation.PixelSampling(SizeOf(spec));

            // Resolved BEFORE the bake: a folder that cannot be written to should cost nothing.
            string path = ResolvePath(spec, source, folder);
            if (string.IsNullOrEmpty(path)) return null;

            TMP_FontAsset baked = SplashFontBaker.BakePixelFontForAsset(source, sampling, out int atlas);
            if (baked == null) return null;                    // BakePixelFontForAsset said why

            TMP_FontAsset saved = SplashBorderFontBaker.Save(baked, path);
            if (saved == null) return null;

            // Saving round-trips the asset through the importer, and Unity's texture import does not preserve the
            // point filtering the bake set in memory — so it is re-asserted on what actually landed on disk. With
            // bilinear atlas filtering this mode measures 18 components instead of 8; it is not a detail.
            PointFilterAtlases(saved);

            Stamp(spec, saved, source, sampling, atlas);

            Debug.Log($"[TextSplash] Saved pixel font for \"{spec.name}\" at {path} — RASTER_HINTED at {sampling} " +
                      $"px/em on a {atlas}x{atlas} atlas, point-filtered. Nothing to press: it re-bakes itself " +
                      "whenever the splash's font, size or pixel size stops matching it.", saved);
            return saved;
        }

        /// <summary>Whether <paramref name="spec"/>'s pixel font is missing, stale, or was never made — the question
        /// the automatic maintenance asks before spending a rasterisation. The face it reasoned about comes back in
        /// <paramref name="source"/> so a caller does not resolve it a second time and risk resolving it
        /// differently.</summary>
        internal static bool NeedsBake(TextSplash spec, out TMP_FontAsset source)
        {
            source = null;
            if (spec == null || spec.pixelation == null) return false;

            source = spec.font != null ? spec.font : TMP_Settings.defaultFontAsset;
            if (source == null) return false;

            return spec.pixelation.NeedsPixelFontBake(source, SizeOf(spec));
        }

        /// <summary>The size the bake is keyed on. A Pixel-font bake is per-size by construction, so an ANIMATED
        /// size scalar has no single right answer — the scalar's value at life 0 is taken, which is the size the
        /// splash starts at, and the window says so rather than letting it look exact.</summary>
        internal static float SizeOf(TextSplash spec)
            => spec != null && spec.size != null ? spec.size.Evaluate(0f, 0) : 96f;

        static void Stamp(TextSplash spec, TMP_FontAsset font, TMP_FontAsset source, int sampling, int atlas)
        {
            if (spec == null) return;
            spec.pixelation.bakedPixelFont = font;
            spec.pixelation.bakedPixelSource = source;
            spec.pixelation.bakedPixelSampling = sampling;
            spec.pixelation.bakedPixelAtlas = atlas;
            EditorUtility.SetDirty(spec);
        }

        /// Re-assert point filtering on every atlas page of a SAVED asset, and on the importer that will re-create
        /// it, so a reimport cannot quietly restore bilinear.
        static void PointFilterAtlases(TMP_FontAsset fa)
        {
            if (fa == null) return;
            Texture2D[] pages = fa.atlasTextures;
            if (pages == null) return;
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] == null) continue;
                pages[i].filterMode = FilterMode.Point;
                pages[i].wrapMode = TextureWrapMode.Clamp;
            }
            EditorUtility.SetDirty(fa);
        }

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────
        // Where it lands
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────

        static string ResolvePath(TextSplash spec, TMP_FontAsset source, string folder)
        {
            // The face's own file must never be written to: this bake OVERWRITES whatever font asset it lands on.
            string facePath = AssetDatabase.GetAssetPath(source);

            // An already-baked font is re-baked WHERE IT IS, so a user who moved or renamed it keeps that choice —
            // but only one this baker can prove is its own output. An unattended bake that overwrote a font asset
            // somebody assigned by hand would destroy a file nobody asked it to touch.
            if (IsOwnOutput(spec))
            {
                string current = AssetDatabase.GetAssetPath(spec.pixelation.bakedPixelFont);
                if (!string.IsNullOrEmpty(current) && current != facePath) return current;
            }

            if (string.IsNullOrEmpty(folder)) folder = DefaultFolder(spec);
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (!SplashBorderFontBaker.EnsureFolder(folder)) return null;

            string path = folder + "/" + FileName(spec, source);

            bool occupied = path == facePath
                         || (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) == null
                             && AssetDatabase.LoadMainAssetAtPath(path) != null);
            if (occupied) path = AssetDatabase.GenerateUniqueAssetPath(path);

            return path;
        }

        /// <summary>Whether the spec's current pixel font is one of this baker's own files, and so safe to write
        /// over. The stamp is the proof — an asset with no recorded source was assigned by hand.</summary>
        static bool IsOwnOutput(TextSplash spec)
        {
            var px = spec != null ? spec.pixelation : null;
            if (px == null || px.bakedPixelFont == null) return false;
            return px.bakedPixelSource != null;
        }

        /// <summary>Named after the SPEC and the FACE both. Two splashes sharing a font must not write to one file
        /// — they bake at different sampling sizes — and a splash that changes font gets a new file rather than
        /// silently redefining the old one, which anything else pointing at it still reads.</summary>
        static string FileName(TextSplash spec, TMP_FontAsset source)
            => $"{SplashBorderFontBaker.Sanitize(spec.name)} " +
               $"({SplashBorderFontBaker.Sanitize(source.name)}) Pixel Font.asset";

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
    }
}
