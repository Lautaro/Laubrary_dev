using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Laubrary.Launimator;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Turns an Aseprite document into a runtime <see cref="ReelVersion"/> the existing player consumes:
    /// - layer name `meta:foo`  → an invisible collision/point shape (binary mask `foo`).
    /// - layer name `hybrid:foo` → rendered AND a mask `foo` (its visible pixels ARE the hitbox).
    /// - any other layer        → a plain visible sprite layer.
    /// When <c>layered</c>, the renderable layers (sprite + hybrid) are kept as separate composable
    /// <see cref="SpriteLayer"/> tracks (stacked + toggleable at runtime — weapon swap); they're ALSO flattened
    /// into a composite per frame (for collision mask mapping + fallback). meta + hybrid layers become binary
    /// <see cref="MetaLayer"/> masks. Each Aseprite TAG becomes its own animation.
    /// </summary>
    public static class AsepriteReelImport
    {
        enum Kind { Sprite, Meta, Hybrid }

        static (Kind kind, string id) Classify(string n)
        {
            if (n.StartsWith("meta:", System.StringComparison.OrdinalIgnoreCase)) return (Kind.Meta, n.Substring(5).Trim());
            if (n.StartsWith("hybrid:", System.StringComparison.OrdinalIgnoreCase)) return (Kind.Hybrid, n.Substring(7).Trim());
            return (Kind.Sprite, n);
        }

        static readonly Color[] MaskColors = { new Color(1f, 0.3f, 0.3f), new Color(0.4f, 0.8f, 1f), new Color(0.4f, 0.9f, 0.45f), new Color(1f, 0.82f, 0.28f) };

        public static ReelVersion Build(AseDoc doc, string outFolder, string charName, float ppu, Vector2 pivot, bool layered = true)
        {
            Directory.CreateDirectory(Path.GetFullPath(outFolder));
            var kinds = doc.layers.Select(l => Classify(l.name)).ToList();
            int W = doc.width, H = doc.height, F = doc.frameCount;

            // ── collect all sprite PNGs to write (composite per frame + per renderable layer if layered) ──
            var specs = new List<(string path, Color32[] px)>();
            var compositePaths = new string[F];
            for (int f = 0; f < F; f++)
            {
                var comp = new Color32[W * H];
                for (int l = 0; l < doc.layers.Count; l++)
                {
                    if (kinds[l].kind == Kind.Meta) continue;
                    var src = doc.pixels[f][l]; if (src == null) continue;
                    for (int i = 0; i < comp.Length; i++) comp[i] = Over(src[i], comp[i]);
                }
                string p = $"{outFolder}/{charName}_f{f:D2}.png";
                compositePaths[f] = p; specs.Add((p, comp));
            }

            var layerPaths = new List<(int layer, string[] paths)>();
            if (layered)
                for (int l = 0; l < doc.layers.Count; l++)
                {
                    if (kinds[l].kind == Kind.Meta) continue;   // meta layers aren't rendered
                    var arr = new string[F];
                    for (int f = 0; f < F; f++)
                    {
                        var px = doc.pixels[f][l] ?? new Color32[W * H];
                        string p = $"{outFolder}/{charName}_{Safe(kinds[l].id)}_f{f:D2}.png";
                        arr[f] = p; specs.Add((p, px));
                    }
                    layerPaths.Add((l, arr));
                }

            foreach (var s in specs) WritePng(s.px, W, H, s.path);
            AssetDatabase.Refresh();
            foreach (var s in specs) ConfigureSprite(s.path, ppu, pivot);
            Sprite Load(string p) => AssetDatabase.LoadAssetAtPath<Sprite>(p);

            var frameSprites = compositePaths.Select(Load).ToList();
            var spriteLayers = layerPaths.Select(lp => new SpriteLayer { id = kinds[lp.layer].id, frames = lp.paths.Select(Load).ToList() }).ToList();

            // ── binary masks for meta + hybrid layers ──
            var metaLayers = new List<MetaLayer>();
            int mc = 0;
            for (int l = 0; l < doc.layers.Count; l++)
            {
                if (kinds[l].kind == Kind.Sprite) continue;
                var ml = new MetaLayer { id = kinds[l].id, color = MaskColors[mc++ % MaskColors.Length] };
                for (int f = 0; f < F; f++)
                {
                    var mf = new MetaFrame(); mf.EnsureSize(W, H);
                    var src = doc.pixels[f][l];
                    if (src != null) for (int i = 0; i < src.Length; i++) if (src[i].a > 0) mf.cells[i] = 1;
                    ml.frames.Add(mf);
                }
                metaLayers.Add(ml);
            }

            // ── one animation per tag (else one over all frames) ──
            var ver = ScriptableObject.CreateInstance<ReelVersion>();
            ver.versionNumber = 0; ver.createdUtc = "";
            var ranges = doc.tags.Count > 0
                ? doc.tags.Select(t => (t.name, t.from, Mathf.Clamp(t.to, t.from, F - 1))).ToList()
                : new List<(string, int, int)> { (charName, 0, F - 1) };
            foreach (var (name, from, to) in ranges)
                ver.animations.Add(MakeAnim(name, frameSprites, spriteLayers, metaLayers, from, to));

            AssetDatabase.CreateAsset(ver, $"{outFolder}/{charName}.asset");
            AssetDatabase.SaveAssets();
            return ver;
        }

        static AnimationDef MakeAnim(string name, List<Sprite> frames, List<SpriteLayer> layers, List<MetaLayer> meta, int from, int to)
        {
            var def = new AnimationDef { name = name, fps = 10f };
            for (int f = from; f <= to; f++) def.frames.Add(frames[f]);
            foreach (var L in layers)
            {
                var s = new SpriteLayer { id = L.id };
                for (int f = from; f <= to; f++) s.frames.Add(L.frames[f]);
                def.spriteLayers.Add(s);
            }
            if (meta.Count > 0)
            {
                def.metaLayersEnabled = true;
                foreach (var ml in meta)
                {
                    var s = new MetaLayer { id = ml.id, color = ml.color };
                    for (int f = from; f <= to; f++) s.frames.Add(ml.frames[f].Clone());
                    def.metaLayers.Add(s);
                }
            }
            return def;
        }

        static Color32 Over(Color32 src, Color32 dst)
        {
            float sa = src.a / 255f, da = dst.a / 255f, oa = sa + da * (1f - sa);
            if (oa <= 0f) return new Color32(0, 0, 0, 0);
            float r = (src.r * sa + dst.r * da * (1f - sa)) / oa;
            float g = (src.g * sa + dst.g * da * (1f - sa)) / oa;
            float b = (src.b * sa + dst.b * da * (1f - sa)) / oa;
            return new Color32((byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255), (byte)Mathf.Clamp(b, 0, 255), (byte)Mathf.Clamp(oa * 255f, 0, 255));
        }

        static void WritePng(Color32[] px, int W, int H, string path)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void ConfigureSprite(string path, float ppu, Vector2 pivot)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
            st.spriteAlignment = (int)SpriteAlignment.Custom;
            st.spritePivot = pivot;
            st.spritePixelsPerUnit = ppu;
            ti.SetTextureSettings(st);
            ti.SaveAndReimport();
        }

        static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.Length == 0 ? "layer" : sb.ToString();
        }
    }
}
