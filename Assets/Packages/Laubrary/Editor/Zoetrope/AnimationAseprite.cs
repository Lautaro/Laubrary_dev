using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.Zoetrope;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// Per-animation Aseprite round-trip (ASEPRITE_PLAN.md → Self-containment, Phase 2). Promotes one
    /// <see cref="AnimationDef"/> to an editable <c>.aseprite</c> living in the version's own <c>Source/</c>
    /// folder, and syncs the user's edits back into an owned source PNG the recipe points at.
    ///
    /// Design choices for this first cut (kept deliberately simple + low-risk):
    /// - The <c>.aseprite</c> is the editable SOURCE; on sync its frames are composited and baked into an owned
    ///   PNG, and the recipe stays the ordinary rect-into-texture form — so <see cref="AtlasBaker"/> and the data
    ///   model need no changes (no special "aseprite-backed" bake path).
    /// - The Aseprite canvas becomes the animation's FIXED frame box (WYSIWYG): move pixels down in Aseprite and
    ///   they render lower in game, while the pivot (e.g. the feet) stays put.
    /// - SPRITE pixels only. Authored <c>events</c> and <c>metaLayers</c> are PRESERVED untouched across the
    ///   round-trip (author those in the Builder's meta painter). Composable sprite layers collapse to a single
    ///   composite on sync (fine for pixel edits like "make the swing hit low"; weapon-swap layering is Phase 2b).
    /// </summary>
    public static class AnimationAseprite
    {
        /// <summary>
        /// Build a <c>Source/&lt;anim&gt;.aseprite</c> from the animation's composed uniform frames (one Aseprite
        /// frame per animation frame, single "art" layer), record it on the def, and open it in Aseprite. The
        /// caller persists the def (e.g. via <see cref="ZoeRepo.SaveAnimationToDraft"/>).
        /// </summary>
        public static bool Promote(AnimationDef def, string versionFolder, out string status)
        {
            if (def == null || def.recipe == null || def.recipe.Count == 0)
            { status = "Nothing to promote — the animation has no frames."; return false; }

            var key = new RegionSlicer.ColorKey { enabled = def.bgKeyEnabled, color = def.bgKey, tolerance = def.bgKeyTolerance };
            var box = new AtlasBaker.FrameBox { fixedSize = def.fixedFrame, w = def.frameWidth, h = def.frameHeight, pivot = def.framePivot };
            if (!AtlasBaker.Compose(def.recipe, key, box, out var comp, out string err))
            { status = $"Can't compose '{def.name}': {err}"; return false; }

            int fw = comp.fw, fh = comp.fh, n = def.recipe.Count;
            var doc = new AseDoc { width = fw, height = fh, frameCount = n };
            doc.layers.Add(new AseLayer { name = "art" });
            doc.pixels = new Color32[n][][];
            for (int f = 0; f < n; f++)
            {
                var canvas = new Color32[fw * fh];
                for (int y = 0; y < fh; y++)
                    for (int x = 0; x < fw; x++)
                        canvas[y * fw + x] = comp.strip[y * comp.stripW + (f * fw + x)]; // both bottom-up
                doc.pixels[f] = new[] { canvas };
            }

            string folder = ZoeSources.SourceFolder(versionFolder);
            ZoeBuilder.EnsureFolder(folder);
            string path = $"{folder}/{ZoeBuilder.Sanitize(def.name)}.aseprite";
            File.WriteAllBytes(ToSystemPath(path), AsepriteIO.Write(doc));
            AssetDatabase.ImportAsset(path);

            // Lock the frame box to the composed canvas + its registration pivot. The canvas the user edits IS the
            // animation's frame box now, so the promote→edit→sync round-trip neither shifts nor wobbles the art.
            def.fixedFrame = true; def.frameWidth = fw; def.frameHeight = fh; def.framePivot = comp.uniformPivot;
            def.asepriteSourcePath = path;
            bool opened = AsepriteLauncher.Open(path);
            status = opened
                ? $"Editing '{def.name}' in Aseprite — edit, Save, then 'Sync from Aseprite'."
                : $"Wrote {path}, but Aseprite didn't launch. Set its path via Tools ▸ Zoetrope ▸ Set Aseprite Path.";
            return true;
        }

        /// <summary>
        /// Read the edited <c>.aseprite</c> back, composite each frame, write an owned <c>Source/&lt;anim&gt;_src.png</c>,
        /// and rewrite the recipe to point at it as a fixed-frame box (the Aseprite canvas). Preserves events,
        /// meta-layers, zones, fps. The caller persists + rebuilds (via <see cref="ZoeRepo.SaveAnimationToDraft"/>).
        /// </summary>
        public static bool Sync(AnimationDef def, string versionFolder, out string status)
        {
            if (def == null || string.IsNullOrEmpty(def.asepriteSourcePath) || !File.Exists(ToSystemPath(def.asepriteSourcePath)))
            { status = "No .aseprite source for this animation — use 'Edit in Aseprite' first."; return false; }

            AseDoc doc;
            try { doc = AsepriteIO.Read(File.ReadAllBytes(ToSystemPath(def.asepriteSourcePath))); }
            catch (Exception e) { status = $"Couldn't read the .aseprite: {e.Message}"; return false; }

            int W = doc.width, H = doc.height, n = doc.frameCount;
            if (n <= 0 || W <= 0 || H <= 0) { status = "The .aseprite has no usable frames."; return false; }

            int stripW = W * n;
            var strip = new Color32[stripW * H];
            for (int f = 0; f < n; f++)
            {
                var comp = CompositeFrame(doc, f);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        strip[y * stripW + (f * W + x)] = comp[y * W + x];
            }

            string folder = ZoeSources.SourceFolder(versionFolder);
            ZoeBuilder.EnsureFolder(folder);
            string ownedPath = $"{folder}/{ZoeBuilder.Sanitize(def.name)}_src.png";
            WritePng(strip, stripW, H, ownedPath);
            AssetDatabase.ImportAsset(ownedPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureSource(ownedPath);
            string guid = AssetDatabase.AssetPathToGUID(ownedPath);

            def.recipe = new List<FrameRef>(n);
            for (int f = 0; f < n; f++)
                def.recipe.Add(new FrameRef
                {
                    sourceTextureGuid = guid,
                    cell = new Rect(f * W, 0, W, H),
                    pivot = def.framePivot,
                    transform = CellTransform.Identity
                });
            def.sourceTextureGuid = guid;
            def.bgKeyEnabled = false;
            def.fixedFrame = true; def.frameWidth = W; def.frameHeight = H;
            def.spriteLayers = new List<SpriteLayer>(); // composite-only after sync (events/meta preserved)

            status = $"Synced '{def.name}' from Aseprite ({n} frame(s)).";
            return true;
        }

        /// <summary>Composite a frame's non-meta layers (bottom→top, straight-alpha over), bottom-up RGBA.</summary>
        private static Color32[] CompositeFrame(AseDoc doc, int f)
        {
            int W = doc.width, H = doc.height;
            var outp = new Color32[W * H];
            var layers = doc.pixels[f];
            for (int l = 0; l < layers.Length; l++)
            {
                if (l < doc.layers.Count && doc.layers[l].name != null &&
                    doc.layers[l].name.StartsWith("meta:", StringComparison.OrdinalIgnoreCase)) continue;
                var src = layers[l];
                if (src == null) continue;
                for (int i = 0; i < outp.Length; i++) outp[i] = Over(src[i], outp[i]);
            }
            return outp;
        }

        private static Color32 Over(Color32 src, Color32 dst)
        {
            float sa = src.a / 255f, da = dst.a / 255f, oa = sa + da * (1f - sa);
            if (oa <= 0f) return new Color32(0, 0, 0, 0);
            float r = (src.r * sa + dst.r * da * (1f - sa)) / oa;
            float g = (src.g * sa + dst.g * da * (1f - sa)) / oa;
            float b = (src.b * sa + dst.b * da * (1f - sa)) / oa;
            return new Color32(
                (byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255),
                (byte)Mathf.Clamp(b, 0, 255), (byte)Mathf.Clamp(oa * 255f, 0, 255));
        }

        private static void WritePng(Color32[] px, int w, int h, string assetPath)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try { tex.SetPixels32(px); tex.Apply(); File.WriteAllBytes(ToSystemPath(assetPath), tex.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        private static void ConfigureSource(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Default;
            ti.isReadable = true;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
        }

        private static string ToSystemPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
