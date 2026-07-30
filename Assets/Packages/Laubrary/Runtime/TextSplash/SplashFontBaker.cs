using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

// The author-time baker (SplashBorderFontBaker) saves this baker's output as a real project asset, which needs the
// one uncached entry point below. Everything else about baking stays private to this file.
[assembly: InternalsVisibleTo("com.Lautaro-Arino.Laubrary.TextSplash.Editor")]

namespace Laubrary.TextSplash
{
    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Bakes the PADDED twin of a TMP font asset that the border pass draws with.
    ///
    /// A TMP glyph is a signed distance field stored in the font atlas, and the atlas only keeps a fixed ring of
    /// texels around each glyph — its PADDING. Outside that ring there is no distance information, so a dilated
    /// outline saturates: the furthest a border can reach outwards is `atlasPadding ÷ (2 × samplingPointSize)` of an
    /// em, which on a stock font asset (padding 9, sampling 86) is about 4% of the font size. The splash's border
    /// dial goes to 0.5 em — ten times further. There is no way to widen an existing asset in place either, because
    /// `TMP_FontAsset.atlasPadding`'s setter is internal and the distance data simply isn't in the texture.
    ///
    /// So the border gets its OWN font asset, rasterised from the same font file at the same sampling size with a
    /// much fatter ring. Normally that is a runtime object, cached per source/padding/atlas-size and destroyed with
    /// <see cref="ClearCache"/>. The exception is the author-time path (<c>BakeForAsset</c>, for
    /// <c>SplashBorderFontBaker</c>): rasterising needs a `UnityEngine.Font`, which a STATIC font asset can only
    /// produce through the AssetDatabase, so a build has to be handed a twin that was baked and saved beforehand.
    ///
    /// The one property everything hangs on: the twin must lay text out IDENTICALLY to the face, because the two are
    /// separate TMP components that each run their own layout pass over the same string. A different advance, kern
    /// pair or line height and the border slides out from under its letters, further with every character. See
    /// <see cref="MirrorLayoutState"/> for exactly what is carried across to guarantee that.</summary>
    public static class SplashFontBaker
    {
        // ── shader properties written onto the baked material ──
        static readonly int k_MainTex       = Shader.PropertyToID("_MainTex");
        static readonly int k_TextureWidth  = Shader.PropertyToID("_TextureWidth");
        static readonly int k_TextureHeight = Shader.PropertyToID("_TextureHeight");
        static readonly int k_GradientScale = Shader.PropertyToID("_GradientScale");
        static readonly int k_WeightNormal  = Shader.PropertyToID("_WeightNormal");
        static readonly int k_WeightBold    = Shader.PropertyToID("_WeightBold");

        const int k_MaxPadding = 512;
        const int k_MinAtlas   = 128;
        const int k_MaxAtlas   = 8192;

        /// Rectangle packing never reaches 100% — the capacity estimate assumes this much of a page is usable.
        const float k_PackEfficiency = 0.85f;

        /// Below this many glyphs per page the settings are effectively broken (every letter opens a new atlas).
        const int k_UselessCapacity = 8;

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────
        // Cache
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────

        readonly struct Key : IEquatable<Key>
        {
            readonly int _sourceId, _padding, _atlasSize;

            public Key(int sourceId, int padding, int atlasSize)
            {
                _sourceId = sourceId; _padding = padding; _atlasSize = atlasSize;
            }

            public bool Equals(Key o) => _sourceId == o._sourceId && _padding == o._padding && _atlasSize == o._atlasSize;
            public override bool Equals(object o) => o is Key k && Equals(k);
            public override int GetHashCode()
                => unchecked((_sourceId * 397) ^ (_padding * 92821) ^ (_atlasSize * 6151));
        }

        static readonly Dictionary<Key, TMP_FontAsset> s_Cache = new Dictionary<Key, TMP_FontAsset>();

        /// Combinations already proven unbakeable, so a per-frame caller can't re-attempt (and re-warn) forever.
        static readonly HashSet<Key> s_Failed = new HashSet<Key>();

        /// Source assets already warned about for a missing font file — one line each, not one per Show().
        static readonly HashSet<int> s_WarnedNoFont = new HashSet<int>();

#if UNITY_EDITOR
        static bool s_HooksInstalled;
#endif

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>A font asset equivalent to <paramref name="source"/> but baked with at least
        /// <paramref name="padding"/> texels of SDF padding, so a dilated border (and a deep bevel) have room.
        /// Cached; repeated calls with the same arguments return the same asset.
        /// Returns <paramref name="source"/> unchanged when it already has enough padding, and null when it cannot
        /// bake.</summary>
        /// <param name="source">The face's font asset. Its sampling size, metrics and OpenType features are mirrored.</param>
        /// <param name="padding">Wanted SDF padding in texels. 0 or less means "the face is fine as it is".</param>
        /// <param name="atlasSize">Square atlas edge, in pixels, for the baked asset.</param>
        public static TMP_FontAsset GetPadded(TMP_FontAsset source, int padding, int atlasSize)
        {
            if (source == null) return null;

            padding   = Mathf.Clamp(padding, 0, k_MaxPadding);
            atlasSize = Mathf.Clamp(atlasSize, k_MinAtlas, k_MaxAtlas);

            // Nothing to gain: the face already carries at least this much distance field around every glyph, so
            // handing back the face itself means one atlas instead of two and no lifetime to manage at all.
            if (padding <= 0 || source.atlasPadding >= padding) return source;

            var key = new Key(source.GetInstanceID(), padding, atlasSize);

            if (s_Failed.Contains(key)) return source;

            if (s_Cache.TryGetValue(key, out TMP_FontAsset cached))
            {
                // `== null` is Unity's destroyed-object check, not a reference test. A cached entry can be a
                // destroyed object (someone called ClearCache while holding the key, or a reload got there first),
                // and handing one out is exactly what produces "the object of type 'TMP_FontAsset' has been
                // destroyed but you are still trying to access it" in the consumer, one frame later and far away.
                if (cached != null) { StampHideFlags(cached); return cached; }
                s_Cache.Remove(key);
            }

            Font font = ResolveSourceFont(source);
            if (font == null) return source;                 // ResolveSourceFont warned once already

            TMP_FontAsset baked = Bake(source, font, padding, atlasSize);
            if (baked == null) { s_Failed.Add(key); return source; }

            InstallEditorHooks();
            s_Cache[key] = baked;
            return baked;
        }

        /// <summary>Usable outward dilation, in em, that a given font asset's padding can express.
        /// This is the ceiling on border thickness: ask for more and the outline simply stops growing, because the
        /// atlas holds no distance information past the padding ring.</summary>
        public static float MaxOutwardEm(TMP_FontAsset font)
        {
            if (font == null) return 0f;
            float sampling = font.faceInfo.pointSize;
            if (sampling <= 0f) return 0f;
            return font.atlasPadding / (2f * sampling);
        }

        /// <summary>Drop every cached asset and destroy what was generated.</summary>
        public static void ClearCache()
        {
            foreach (KeyValuePair<Key, TMP_FontAsset> entry in s_Cache)
            {
                TMP_FontAsset fa = entry.Value;
                if (fa == null) continue;

                // TMP keeps every font asset it has read in a static lookup keyed by instance id; leaving a
                // destroyed one in there is how a later rebuild trips over a dead reference.
                TMP_ResourceManager.RemoveFontAsset(fa);
                DestroyGenerated(fa);
            }

            s_Cache.Clear();
            s_Failed.Clear();
            s_WarnedNoFont.Clear();
        }

#if UNITY_EDITOR
        /// <summary>Bake a padded twin that is meant to be SAVED, not cached — the author-time path behind
        /// <c>SplashBorderFontBaker.Bake</c>.
        ///
        /// Same bake as <see cref="GetPadded"/> (same sampling size, same mirrored layout state), with the two
        /// differences a project asset needs: the result is never entered into the runtime cache — so the reload hook
        /// can never destroy an object that is by then a file on disk — and its hide flags are cleared, because
        /// `DontSave` is precisely the flag that makes `AssetDatabase.CreateAsset` write nothing.
        ///
        /// Returns null (having warned) when the face carries no reachable font file or the bake itself fails. The
        /// caller owns the returned object: save it or destroy it.</summary>
        internal static TMP_FontAsset BakeForAsset(TMP_FontAsset source, int padding, int atlasSize)
        {
            if (source == null) return null;

            padding   = Mathf.Clamp(padding, 0, k_MaxPadding);
            atlasSize = Mathf.Clamp(atlasSize, k_MinAtlas, k_MaxAtlas);
            if (padding <= 0) return null;

            Font font = ResolveSourceFont(source);
            if (font == null) return null;                   // ResolveSourceFont warned already

            TMP_FontAsset baked = Bake(source, font, padding, atlasSize);
            if (baked == null) return null;                  // Bake warned already

            ApplyHideFlags(baked, HideFlags.None);
            return baked;
        }

        /// <summary>Bake the BITMAP font behind <see cref="SplashRasterMode.PixelFont"/>: the face rasterized at
        /// the sampling size the splash actually renders at, grid-fitted by the hinter, so a glyph arrives already
        /// made of whole cells instead of being sampled into them.
        ///
        /// Four things here are deliberate and each one is a trap the border bake does not have:
        ///
        ///   RASTER_HINTED, not SMOOTH. Hinting is what snaps the outline onto the pixel grid; without it the
        ///     glyph is still rasterized at the right size but lands between cells. Measured, unhinted RASTER also
        ///     scored 8 parts and 2 holes, so native-size rasterization is doing most of the work — what hinting
        ///     specifically bought was a cap height that came out at the requested 12 px instead of 13 with a
        ///     stray top row. Worth having, not worth overselling.
        ///
        ///   PADDING 1, not 0. TMP's own bitmap convention is zero padding, but `ShaderUtilities.GetPadding`
        ///     returns `extraPadding + 1` for any material with no `_GradientScale` — which is every bitmap
        ///     material — so TMP inflates each glyph quad by one texel regardless. At padding 0 that ring samples
        ///     the NEIGHBOURING glyph in the atlas and every letter wears a sliver of the one packed beside it.
        ///
        ///   THE METRICS ARE NOT MIRRORED. The border twin copies the face's metrics because it has to lay out
        ///     identically to it. Here the opposite is true: the grid-fitted metrics this bake produces ARE the
        ///     product, and copying the face's un-fitted ones back over them would throw away the fit.
        ///
        ///   POINT filtering on the atlas, asserted rather than assumed. Measured, a bilinear atlas took the same
        ///     glyphs from 8 parts to 18 and from 0 part-covered cells to 256 — comfortably the single most
        ///     destructive thing that can happen to this mode.
        ///
        /// Returns null (having warned) when the face carries no reachable font file or the bake fails. The caller
        /// owns the returned object: save it or destroy it.</summary>
        internal static TMP_FontAsset BakePixelFontForAsset(TMP_FontAsset source, int sampling, out int atlasUsed)
        {
            atlasUsed = 0;
            if (source == null) return null;

            sampling = Mathf.Clamp(sampling, 4, 256);

            Font font = ResolveSourceFont(source);
            if (font == null) return null;                   // ResolveSourceFont warned already

            source.ReadFontAssetDefinition();

            int glyphs = source.characterTable != null ? source.characterTable.Count : 96;
            atlasUsed = PixelAtlasSize(sampling, glyphs);

            TMP_FontAsset baked = TMP_FontAsset.CreateFontAsset(
                font, sampling, k_PixelPadding, GlyphRenderMode.RASTER_HINTED,
                atlasUsed, atlasUsed, AtlasPopulationMode.Dynamic, true);

            if (baked == null)
            {
                Debug.LogWarning($"[TextSplash] Could not bake a pixel font from \"{font.name}\" at {sampling} px/em " +
                                 $"for \"{source.name}\". Font files need \"Include Font Data\" enabled in their " +
                                 "import settings to be rasterised.", source);
                return null;
            }

            baked.name = $"{source.name} (pixel {sampling})";

            // NOT EstimateCapacity: that measures the SOURCE's glyph rects, and the source is a ~90 px/em SDF face
            // while this bake is at ~12. Sizing the seed by the big face's cells made the capacity come out at a
            // dozen glyphs, and because Seed takes the FIRST n characters of the table those dozen were all
            // punctuation — the alphabet never entered the atlas, and "SPLASH!" baked as "!".
            int seeded = Seed(source, baked, PixelCapacity(sampling, atlasUsed));
            ConfigurePixelMaterial(baked);
            PointFilterAtlases(baked);
            baked.ReadFontAssetDefinition();
            ApplyHideFlags(baked, HideFlags.None);

            int pages = baked.atlasTextureCount;
            Debug.Log($"[TextSplash] Baked pixel font \"{baked.name}\" from \"{source.name}\" — RASTER_HINTED at " +
                      $"{sampling} px/em, padding {k_PixelPadding}, atlas {atlasUsed}×{atlasUsed}, {seeded} " +
                      $"character(s) on {pages} page(s).", baked);

            return baked;
        }
#endif

        /// TMP inflates every bitmap glyph quad by one texel (see BakePixelFontForAsset), so the atlas has to
        /// carry that texel or the inflation samples the next glyph along.
        const int k_PixelPadding = 1;

        /// <summary>A square atlas big enough for the whole seeded character set at this sampling size, rounded up
        /// to a power of two. Derived rather than dialled: unlike the border's padding — which the author tunes and
        /// which trades atlas area for border reach — there is exactly one right answer here, and a dial for it
        /// would only be a way to get it wrong.</summary>
        static int PixelAtlasSize(int sampling, int glyphCount)
        {
            // The 1.3 is headroom, and it is not decoration. The seed takes the first n characters of the face's
            // table, so an atlas that holds MOST of the face silently drops the tail — and a page that is one
            // power of two larger costs a quarter of a megabyte at Alpha8 while a missing letter is a hole in the
            // splash. Erring large is the cheap direction.
            float cell = PixelCell(sampling);
            float area = Mathf.Max(1, glyphCount) * cell * cell / k_PackEfficiency * 1.3f;
            int edge = Mathf.CeilToInt(Mathf.Sqrt(area));

            int size = k_MinAtlas;
            while (size < edge && size < k_MaxAtlas) size <<= 1;
            return Mathf.Clamp(size, k_MinAtlas, k_MaxAtlas);
        }

        /// One glyph's footprint at this sampling size: the em box, the padding ring on both sides, and the one
        /// texel of packing margin TMP leaves between neighbours.
        static float PixelCell(int sampling) => sampling + 2f * k_PixelPadding + 1f;

        /// <summary>How many glyphs a pixel atlas of this size actually holds, measured at the sampling size THIS
        /// bake rasterises at rather than the source face's. Getting this from the source is what produced an
        /// atlas containing nothing but punctuation.</summary>
        static int PixelCapacity(int sampling, int atlasSize)
        {
            float cell = PixelCell(sampling);
            float area = atlasSize * (float)atlasSize * k_PackEfficiency;
            return Mathf.Max(1, Mathf.FloorToInt(area / (cell * cell)));
        }

        /// <summary>Point-filter every atlas page. This is the single most important line in the pixel path: with
        /// bilinear filtering the same bake measured 18 components instead of 8 and 256 part-covered cells instead
        /// of none, because the hardware blends between texels that the whole mode exists to keep separate. Applied
        /// per PAGE because a spill onto a second page would otherwise arrive with TMP's default filtering.</summary>
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
        }

        /// <summary>Point the bitmap material at Laubrary's own premultiplied bitmap shader instead of TMP's.
        ///
        /// TMP builds a bitmap font asset's material from `TextMeshPro/Mobile/Bitmap`, which outputs STRAIGHT
        /// colour under a `SrcAlpha OneMinusSrcAlpha` blend — so into the rig's transparent-black buffer it stores
        /// alpha SQUARED, on a different scale from the RGB beside it and from every other pass in this tool. It
        /// is also in no Resources folder and referenced by no shipped material, so it is stripped from a player
        /// build and `Shader.Find` returns null there. The replacement fixes both at once by living in the
        /// package's own Resources folder. Falling back to TMP's shader is deliberate: a splash that renders with
        /// a squared edge is better than one that does not render.</summary>
        static void ConfigurePixelMaterial(TMP_FontAsset baked)
        {
            Material mat = baked != null ? baked.material : null;
            if (mat == null) return;

            Shader premul = ResolveBitmapShader();
            if (premul != null) mat.shader = premul;

            mat.SetTexture(k_MainTex, baked.atlasTexture);
            mat.SetFloat(k_TextureWidth, baked.atlasWidth);
            mat.SetFloat(k_TextureHeight, baked.atlasHeight);
        }

        static Shader _bitmapShader;
        static bool _bitmapResolved;

        /// <summary>The premultiplied bitmap shader, resolved once per domain out of the package's OWN Resources
        /// folder — the same keep-alive the presentation shader and the bevel preset material already rely on, and
        /// for the same reason: a shader reachable only by `Shader.Find` is stripped from a player build.</summary>
        internal static Shader ResolveBitmapShader()
        {
            if (_bitmapResolved) return _bitmapShader;
            _bitmapResolved = true;
            _bitmapShader = Resources.Load<Shader>("SplashBitmapPremultiplied")
                            ?? Shader.Find("Hidden/Laubrary/TextSplash/BitmapPremultiplied");
            if (_bitmapShader == null)
                Debug.LogWarning("[TextSplash] The premultiplied bitmap shader could not be found, so a pixel-font " +
                                 "splash falls back to TMP's own bitmap shader — which stores alpha squared, so " +
                                 "part-covered cells composite wrong. Reimport Laubrary's TextSplash Resources folder.");
            return _bitmapShader;
        }

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────
        // Baking
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────

        static TMP_FontAsset Bake(TMP_FontAsset source, Font font, int padding, int atlasSize)
        {
            // Read the face FIRST. ReadFontAssetDefinition is what upgrades a legacy kerning table into the font
            // feature table, and what fills in the face metrics TMP derives rather than serialises (cap line, mean
            // line, scale, strikethrough offset) — all of which are copied below, so they have to exist by now.
            source.ReadFontAssetDefinition();

            // FaceInfo.pointSize is a FLOAT in this TMP version, while CreateFontAsset takes an int sampling size.
            int sampling = Mathf.RoundToInt(source.faceInfo.pointSize);
            if (sampling <= 0) sampling = source.creationSettings.pointSize;
            if (sampling <= 0) sampling = 90;

            // Dynamic, not Static and not DynamicOS:
            //   Static  refuses to add glyphs at all (TryAddCharacters bails outright), so a Show() with text the
            //           initial bake didn't cover would render blanks.
            //   DynamicOS resolves missing glyphs by family+style name through the OPERATING SYSTEM's font list,
            //           which can hand back a different physical file than the project's font — different metrics,
            //           different border geometry, and nothing at all on a machine without that font installed.
            //   Dynamic keeps a direct reference to this very Font object, so anything rasterised later comes from
            //           the same file at the same sampling size and the same padding as the initial bake.
            // Multi-atlas support stays ON: overflowing into a second page costs memory and a draw call, but the
            // alternative is glyphs that silently do not render.
            TMP_FontAsset baked = TMP_FontAsset.CreateFontAsset(
                font, sampling, padding, source.atlasRenderMode,
                atlasSize, atlasSize, AtlasPopulationMode.Dynamic, true);

            if (baked == null)
            {
                // CreateFontAsset already logged the FontEngine reason; this says what it cost the user.
                Debug.LogWarning($"[TextSplash] Could not bake a padded border font from \"{font.name}\" for " +
                                 $"\"{source.name}\" — the border falls back to the face's own {source.atlasPadding}-texel " +
                                 $"padding, which tops out at {MaxOutwardEm(source):0.###} em. Font files need " +
                                 "\"Include Font Data\" enabled in their import settings to be rasterised.", source);
                return null;
            }

            // A distinct name is not cosmetic: TMP indexes font assets by a hash of their name, so a twin sharing
            // the face's name would collide with it in TMP's own lookups.
            baked.name = $"{source.name} (border pad {padding})";
            StampHideFlags(baked);

            // A bitmap-raster face has no distance field to dilate at all, so a padded twin would be meaningless.
            // Testing the generated material for _GradientScale is the version-proof way to ask "is this SDF?".
            if (baked.material == null || !baked.material.HasProperty(k_GradientScale))
            {
                Debug.LogWarning($"[TextSplash] \"{source.name}\" is not a distance-field font asset, so no padded " +
                                 "border font can be baked from it. Re-create it with an SDF render mode for a " +
                                 "border thicker than a hairline.", source);
                DestroyGenerated(baked);
                return null;
            }

            MirrorLayoutState(source, baked);

            // Re-read AFTER the metrics and feature records were replaced: this rebuilds the glyph/character/kerning
            // lookup dictionaries from the copied data and re-synthesises the control characters against the face
            // the mirrored faceInfo actually points at.
            baked.ReadFontAssetDefinition();

            int capacity = EstimateCapacity(source, padding, atlasSize);
            float pageMB = atlasSize * (float)atlasSize / (1024f * 1024f);   // SDF atlases are always Alpha8: 1 byte/px

            if (capacity < k_UselessCapacity)
            {
                Debug.LogWarning($"[TextSplash] Border padding {padding} on a {atlasSize}×{atlasSize} atlas leaves room " +
                                 $"for only about {capacity} glyph(s) per page of \"{source.name}\" — every few letters " +
                                 $"would open another {pageMB:0.#} MB page and another draw call. Raise the border atlas " +
                                 $"size or lower the padding (padding {padding} needs roughly " +
                                 $"{RecommendedAtlas(source, padding)} px to be workable).", source);
            }

            // Seed the twin with as much of the face's character set as one page can hold. Everything else is left
            // to dynamic population — which is the point of Dynamic mode — so an overridden Show() string still
            // rasterises at this padding rather than falling back to the face's thin one.
            int seeded = Seed(source, baked, capacity);

            ConfigureMaterial(source, baked);
            StampHideFlags(baked);

            int glyphs = baked.glyphTable != null ? baked.glyphTable.Count : 0;
            int pages  = baked.atlasTextureCount;
            int faceChars = source.characterTable != null ? source.characterTable.Count : 0;

            Debug.Log($"[TextSplash] Baked border font \"{baked.name}\" from \"{source.name}\" — padding {padding} " +
                      $"texels (face has {source.atlasPadding}), atlas {atlasSize}×{atlasSize}, {glyphs} glyph(s) from " +
                      $"{seeded} of the face's {faceChars} character(s) on {pages} page(s) (~{pages * pageMB:0.#} MB). " +
                      $"Max outward border {MaxOutwardEm(baked):0.###} em, up from {MaxOutwardEm(source):0.###} em. " +
                      "Characters outside the seeded set rasterise on demand.", baked);

            if (pages > 1)
            {
                Debug.LogWarning($"[TextSplash] Border font \"{baked.name}\" spilled onto {pages} atlas pages " +
                                 $"(~{pages * pageMB:0.#} MB, one extra draw call per page). Lower the border padding " +
                                 "or raise the border atlas size if that matters.", baked);
            }

            return baked;
        }

        /// <summary>Copy everything from the face that TEXT LAYOUT reads, so the padded twin advances, kerns, wraps
        /// and sits on the same baseline. This is the whole correctness story: the twin is a second TMP component
        /// running its own layout pass over the same string, and anything missed here shows up as a border that
        /// creeps away from its letters, further with every character on the line.</summary>
        static void MirrorLayoutState(TMP_FontAsset source, TMP_FontAsset baked)
        {
            // The face metrics struct in one move: point size, scale, units-per-em, line height, ascent / descent /
            // baseline, cap and mean line, tab width, sub/superscript and underline/strikethrough offsets. Taking
            // the whole struct rather than trusting the fresh FontEngine read to agree also carries across any value
            // the face was hand-edited to, and — because it carries faceIndex too — points every LATER dynamic
            // rasterisation at the same face of a multi-face font file.
            baked.faceInfo = source.faceInfo;

            // Per-asset style knobs. normalSpacingOffset and boldSpacing are added straight onto each advance, so a
            // mismatch there drifts the border a little further with every letter; the others change slant, tab
            // width or which weight of dilation a <b> run gets.
            baked.normalStyle         = source.normalStyle;
            baked.normalSpacingOffset = source.normalSpacingOffset;
            baked.boldStyle           = source.boldStyle;
            baked.boldSpacing         = source.boldSpacing;
            baked.italicStyle         = source.italicStyle;
            baked.tabSize             = source.tabSize;

            // OpenType features. Kerning pairs, ligatures and mark attachment all move glyphs relative to one
            // another, and every record is keyed by FONT-FILE glyph index — the same numbers in both assets, since
            // both rasterise the same file — so the records transfer verbatim and stay valid.
            CopyFeatures(source.fontFeatureTable, baked.fontFeatureTable);

            // Only chase features for glyphs added later if the face has any at all. A face baked without kerning
            // must not be shadowed by a border that kerns: that would drift precisely where the face does not.
            baked.getFontFeatures = HasFeatures(source.fontFeatureTable);

            // Fallbacks decide WHICH asset an unavailable character comes from, and the weight table does the same
            // for <b>/<i> runs. Fresh containers, never the face's own list instance, so editing one asset's
            // fallbacks can never silently rewrite the other's.
            baked.fallbackFontAssetTable = source.fallbackFontAssetTable != null
                ? new List<TMP_FontAsset>(source.fallbackFontAssetTable)
                : new List<TMP_FontAsset>();

            TMP_FontWeightPair[] from = source.fontWeightTable;
            TMP_FontWeightPair[] to   = baked.fontWeightTable;
            if (from != null && to != null)
                for (int i = 0; i < from.Length && i < to.Length; i++)
                    to[i] = from[i];
        }

        /// <summary>Add as much of the face's character set to the twin as one atlas page is estimated to hold, in
        /// unicode order (so the control characters and ASCII a splash actually uses come first). Returns how many
        /// characters were offered.</summary>
        static int Seed(TMP_FontAsset source, TMP_FontAsset baked, int capacity)
        {
            List<TMP_Character> table = source.characterTable;
            int want = Mathf.Min(capacity, table != null ? table.Count : 0);
            if (want <= 0) return 0;

            var unicodes = new uint[want];
            for (int i = 0; i < want; i++) unicodes[i] = table[i].unicode;

            // Retrieve OpenType features for the seeded glyphs only when the face itself carries them — same rule,
            // and same reason, as getFontFeatures above.
            baked.TryAddCharacters(unicodes, out uint[] missing, HasFeatures(source.fontFeatureTable));

            if (missing != null && missing.Length > 0)
            {
                Debug.LogWarning($"[TextSplash] {missing.Length} of {want} character(s) could not be baked into border " +
                                 $"font \"{baked.name}\" — those letters will wear no border. The font file may not " +
                                 "contain them, or a single padded glyph may be larger than the whole atlas.", baked);
            }

            return want;
        }

        /// <summary>Roughly how many glyphs of this face fit on one page at this padding. Each glyph occupies its
        /// tight atlas rect grown by the padding on every side (plus TMP's one-texel packing margin), and the face's
        /// own glyph rects are measured at the same sampling size, so they are directly comparable.</summary>
        static int EstimateCapacity(TMP_FontAsset source, int padding, int atlasSize)
        {
            double sumW = 0, sumH = 0;
            int n = 0;

            var glyphs = source.glyphTable;
            if (glyphs != null)
            {
                for (int i = 0; i < glyphs.Count; i++)
                {
                    var g = glyphs[i];
                    if (g == null) continue;
                    var r = g.glyphRect;
                    if (r.width <= 0 || r.height <= 0) continue;    // space and the control characters take no area
                    sumW += r.width; sumH += r.height; n++;
                }
            }

            float pt = Mathf.Max(1, source.faceInfo.pointSize);
            float cellW = (n > 0 ? (float)(sumW / n) : pt * 0.55f) + 2f * padding + 1f;
            float cellH = (n > 0 ? (float)(sumH / n) : pt * 0.75f) + 2f * padding + 1f;

            float area = atlasSize * (float)atlasSize * k_PackEfficiency;
            return Mathf.Max(0, Mathf.FloorToInt(area / Mathf.Max(1f, cellW * cellH)));
        }

        /// The atlas edge that would give this face a workable page at this padding — used only to make the
        /// "it will not fit" warning actionable.
        static int RecommendedAtlas(TMP_FontAsset source, int padding)
        {
            for (int size = k_MinAtlas; size <= k_MaxAtlas; size *= 2)
                if (EstimateCapacity(source, padding, size) >= k_UselessCapacity * 4) return size;
            return k_MaxAtlas;
        }

        static void ConfigureMaterial(TMP_FontAsset source, TMP_FontAsset baked)
        {
            Material mat = baked.material;
            if (mat == null) return;

            // TMP builds every runtime-created font asset's material from the MOBILE distance-field shader, which
            // has no bevel, no glow and no underlay — so a border asking for a chiselled edge would silently get a
            // flat one. Adopting the face's own shader gives the border the same feature set the face has, and keeps
            // a custom or SRP TMP shader working. Only the shader crosses over, not the face's cosmetic values: the
            // border pass sets its own fill, dilation and bevel.
            Material faceMat = source.material;
            if (faceMat != null && faceMat.shader != null && faceMat.shader != mat.shader &&
                faceMat.HasProperty(k_GradientScale))
                mat.shader = faceMat.shader;

            mat.SetTexture(k_MainTex, baked.atlasTexture);
            mat.SetFloat(k_TextureWidth, baked.atlasWidth);
            mat.SetFloat(k_TextureHeight, baked.atlasHeight);

            // The number that turns padding into reach: TMP reads the distance field in units of 1/_GradientScale,
            // and TMP's own convention is padding + 1 (the extra texel is the packing margin).
            mat.SetFloat(k_GradientScale, baked.atlasPadding + 1);

            mat.SetFloat(k_WeightNormal, baked.normalStyle);
            mat.SetFloat(k_WeightBold, baked.boldStyle);

            // Recompute _ScaleRatioA/B/C for the new gradient scale. TMP does this itself on every mesh rebuild, but
            // doing it now means a preview that reads the material before any rebuild sees consistent numbers.
            ShaderUtilities.UpdateShaderRatios(mat);

            mat.name = baked.name + " Atlas Material";
        }

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────
        // Source font, lifetime, helpers
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The `UnityEngine.Font` a font asset was rasterised from, or null with a one-time warning.
        /// A DYNAMIC font asset holds the Font object directly. A STATIC one does not: TMP deliberately clears that
        /// reference whenever the population mode is Static or DynamicOS and keeps only the source file's GUID, which
        /// nothing but the AssetDatabase can turn back into a Font — so the fallback below exists in the Editor and
        /// genuinely cannot exist in a build.</summary>
        static Font ResolveSourceFont(TMP_FontAsset source)
        {
            Font font = source.sourceFontFile;
            if (font != null) return font;

#if UNITY_EDITOR
            string guid = source.creationSettings.sourceFontFileGUID;
            if (!string.IsNullOrEmpty(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    font = AssetDatabase.LoadAssetAtPath<Font>(path);
                    if (font != null) return font;
                }
            }
#endif

            if (s_WarnedNoFont.Add(source.GetInstanceID()))
            {
                Debug.LogWarning($"[TextSplash] \"{source.name}\" has no reachable source font file, so no padded " +
                                 $"border font can be baked — the border is capped at the face's own " +
                                 $"{MaxOutwardEm(source):0.###} em. A static font asset keeps only a source-font GUID, " +
                                 "which resolves in the Editor but not in a player; use a dynamic font asset (or a " +
                                 "font asset whose source .ttf/.otf ships with the build) to bake at runtime.", source);
            }

            return null;
        }

        /// <summary>Everything generated here is a runtime object that no scene, prefab or asset file references.
        /// HideAndDontSave keeps it out of the hierarchy and, more importantly, out of reach of
        /// `Resources.UnloadUnusedAssets` — which would otherwise be free to collect an atlas the moment nothing on
        /// screen happens to be using it. Re-stamping on every hand-out also catches the extra atlas pages TMP
        /// creates for itself when a glyph overflows the first one.</summary>
        static void StampHideFlags(TMP_FontAsset fa) => ApplyHideFlags(fa, HideFlags.HideAndDontSave);

        /// The asset AND everything it owns: a font asset's atlas pages and material are separate objects, so a flag
        /// set on the asset alone leaves them free to be collected (or, saved the other way round, refused by the
        /// AssetDatabase) on their own.
        static void ApplyHideFlags(TMP_FontAsset fa, HideFlags flags)
        {
            fa.hideFlags = flags;

            if (fa.material != null) fa.material.hideFlags = flags;

            Texture2D[] pages = fa.atlasTextures;
            if (pages == null) return;
            for (int i = 0; i < pages.Length; i++)
                if (pages[i] != null) pages[i].hideFlags = flags;
        }

        /// TMP_FontAsset.OnDestroy takes its atlas textures and its material down with it, so the asset is the only
        /// thing that has to be destroyed by hand.
        static void DestroyGenerated(UnityEngine.Object obj)
        {
            if (obj == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        /// <summary>Hook the Editor's reload so the cache never outlives the statics that describe it.
        /// A domain reload (a recompile, and entering play mode with the default settings) resets every static field
        /// in this class, but a HideAndDontSave object is exactly the kind that does NOT go away with it — so
        /// without this, each reload would strand another set of atlases in memory with nothing left able to find or
        /// free them, and any reference that did survive would point at an asset the cache no longer knows about.
        /// Destroying everything BEFORE the reload leaves nothing dangling in either direction: consumers re-ask on
        /// the other side and get a freshly baked twin.
        ///
        /// Registered lazily, on the first successful bake, rather than from an initialiser attribute — there is
        /// nothing to clean up until something has been baked, and the registration dies with the same reload it is
        /// registered for, so it is re-installed the next time it is needed. With the domain reload turned off (fast
        /// enter play mode) nothing fires and nothing needs to: the statics and the objects both survive intact.</summary>
        static void InstallEditorHooks()
        {
#if UNITY_EDITOR
            if (s_HooksInstalled) return;
            s_HooksInstalled = true;
            AssemblyReloadEvents.beforeAssemblyReload += ClearCache;
#endif
        }

        static void CopyFeatures(TMP_FontFeatureTable from, TMP_FontFeatureTable to)
        {
            if (from == null || to == null) return;

            to.glyphPairAdjustmentRecords  = Clone(from.glyphPairAdjustmentRecords);
            to.ligatureRecords             = Clone(from.ligatureRecords);
            to.multipleSubstitutionRecords = Clone(from.multipleSubstitutionRecords);
            to.MarkToBaseAdjustmentRecords = Clone(from.MarkToBaseAdjustmentRecords);
            to.MarkToMarkAdjustmentRecords = Clone(from.MarkToMarkAdjustmentRecords);
        }

        static List<T> Clone<T>(List<T> src) => src != null ? new List<T>(src) : new List<T>();

        static bool HasFeatures(TMP_FontFeatureTable t)
        {
            if (t == null) return false;
            return Any(t.glyphPairAdjustmentRecords) || Any(t.ligatureRecords)
                || Any(t.MarkToBaseAdjustmentRecords) || Any(t.MarkToMarkAdjustmentRecords)
                || Any(t.multipleSubstitutionRecords);
        }

        static bool Any<T>(List<T> list) => list != null && list.Count > 0;
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>The shipped material that keeps the BEVEL alive in a player build.
    ///
    /// The bevel needs `TextMeshPro/Distance Field` — the only stock TMP shader that shades at all, since every
    /// Mobile variant strips `_Bevel`, `BEVEL_ON` and the specular term outright. Two separate things get stripped
    /// from a build, and shipping this material is what prevents BOTH:
    ///
    ///   • the SHADER. It sits in no Resources folder and no project's Always Included list, so unless a material in
    ///     the build references it, it is not there at all and `Shader.Find` returns null.
    ///   • the VARIANT. `BEVEL_ON` is a `shader_feature`, and a shader_feature variant is only compiled when some
    ///     INCLUDED MATERIAL has that keyword enabled. Adding the shader to Always Included Shaders does not do it:
    ///     the shader would ship with its bevel-less variant only, and the letters would still render flat.
    ///
    /// So the package carries a material that uses that shader with that keyword on, in its OWN Resources folder —
    /// the same trick, and the same reason, as the presentation shader <see cref="SplashPixelRig"/> loads. It travels
    /// with Laubrary and needs no per-project setup, which matters because this package is copied into consumer
    /// projects that will never know to configure it.
    ///
    /// The material is a KEEP-ALIVE and a shader source, not something to render with: it carries no font atlas, so
    /// a pass must keep painting through its own font-derived material instance and only take the SHADER from here
    /// (see <see cref="GetShader"/>). Note this pins the bevel variant only — a splash that later wants TMP's glow or
    /// underlay would need those keywords enabled here too, for exactly the same reason.</summary>
    public static class SplashBevelMaterial
    {
        /// The one stock TMP shader that can shade. The preset uses it; `Shader.Find` on this name is the fallback.
        public const string ShaderName = "TextMeshPro/Distance Field";

        /// File name (no extension) inside Runtime/TextSplash/Resources.
        const string ResourceName = "SplashBevelPreset";

        static readonly int k_Bevel = Shader.PropertyToID("_Bevel");

        static Material _preset;
        static Shader _shader;
        static bool _resolved;

        /// <summary>The shipped preset material, or null when it is missing from the package. Loaded once per domain.
        /// Callers should not write to it — it is a shared asset, and its only job is to exist.</summary>
        public static Material GetPreset()
        {
            Resolve();
            return _preset;
        }

        /// <summary>The shader a bevelled pass should run: the preset's own, so the build keeps the reference and the
        /// keyword variant that come with it. Falls back to `Shader.Find` (which covers a project that added the
        /// shader to Always Included Shaders, and the Editor, where nothing is stripped), and returns null with a
        /// one-time warning when neither is reachable.</summary>
        public static Shader GetShader()
        {
            Resolve();
            return _shader;
        }

        static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            _preset = Resources.Load<Material>(ResourceName);

            // A material whose shader did not survive comes back pointing at Unity's internal error shader rather
            // than at null, so asking the material for a bevel property is the honest test of "did the shader make
            // it into this build".
            if (_preset != null && _preset.HasProperty(k_Bevel)) _shader = _preset.shader;

            if (_shader == null) _shader = Shader.Find(ShaderName);

            if (_shader == null)
            {
                Debug.LogWarning($"[TextSplash] The bevel needs the \"{ShaderName}\" shader and neither the shipped " +
                    $"preset material (Runtime/TextSplash/Resources/{ResourceName}.mat) nor Shader.Find could " +
                    "produce it, so the letters render flat. Restore the preset material, or add the shader to " +
                    "Project Settings > Graphics > Always Included Shaders AND ship a material that enables " +
                    "BEVEL_ON — the shader alone is not enough, since the keyword is a shader_feature.");
            }
            else if (_preset == null)
            {
                Debug.LogWarning($"[TextSplash] The bevel preset material is missing from the package " +
                    $"(Runtime/TextSplash/Resources/{ResourceName}.mat). The bevel still works in the Editor, where " +
                    "nothing is stripped, but a player build will drop both the shader and its BEVEL_ON variant and " +
                    "the letters will render flat there. Restore the material to make builds match the Editor.");
            }
        }
    }
}
