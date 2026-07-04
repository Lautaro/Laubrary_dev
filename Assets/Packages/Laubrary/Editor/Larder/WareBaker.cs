using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Larder.Editor
{
    /// Bakes a WareSpec's damage-stage sprites out to real PNG assets in the HOST project (never into the package).
    /// It leans on the same WareGenerator the preview uses, so a baked sprite is pixel-for-pixel what you saw. GUARD
    /// RAIL: it only ever creates files. If a target path already exists it versions the name (_1, _2…) rather than
    /// clobbering the user's asset, and it logs exactly what it wrote.
    public static class WareBaker
    {
        public const string Root = "Assets/Larder";

        /// Bake all stages of one spec. subfolder (optional) groups a variation grid under Assets/Larder/<subfolder>.
        /// Returns the project-relative paths created.
        public static List<string> Bake(WareSpec spec, string subfolder = null)
        {
            var created = new List<string>();
            if (spec == null) { Debug.LogWarning("[Larder] Bake skipped: null spec."); return created; }

            string dir = string.IsNullOrEmpty(subfolder) ? Root : Root + "/" + subfolder;
            EnsureFolder(dir);

            int stages = Mathf.Clamp(spec.damageStages, 1, 4);
            string baseName = $"{spec.kind}_{spec.seed}";

            for (int s = 0; s < stages; s++)
            {
                var px = WareGenerator.Render(spec, s, out int w, out int h);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                tex.SetPixels32(px);
                tex.Apply();
                byte[] png = tex.EncodeToPNG();
                Object.DestroyImmediate(tex);

                string assetPath = UniqueAssetPath(dir, $"{baseName}_dmg{s}", "png");
                File.WriteAllBytes(ToAbsolute(assetPath), png);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureImporter(assetPath, spec);
                created.Add(assetPath);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Larder] Baked {created.Count} sprite(s) for {baseName} → {dir}");
            return created;
        }

        /// Roll `count` variations off a base spec and bake each into Assets/Larder/Variations. Each variation reseeds
        /// itself in Randomize, so names never collide; the guard rail still versions any that somehow would.
        public static List<string> BakeVariationGrid(WareSpec baseSpec, int count)
        {
            var all = new List<string>();
            if (baseSpec == null) { Debug.LogWarning("[Larder] Variation bake skipped: null spec."); return all; }
            string sub = "Variations";
            var rng = new System.Random(baseSpec.seed);
            for (int i = 0; i < count; i++)
            {
                var v = baseSpec.Clone();
                v.Randomize(new System.Random(rng.Next()));
                all.AddRange(Bake(v, sub));
                Object.DestroyImmediate(v); // the clone is a throwaway SO, not an asset
            }
            Debug.Log($"[Larder] Baked variation grid of {count} → {Root}/{sub}");
            return all;
        }

        static void ConfigureImporter(string assetPath, WareSpec spec)
        {
            var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (imp == null) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.filterMode = FilterMode.Point;
            imp.spritePixelsPerUnit = Mathf.Max(1f, spec.pixelsPerUnit);
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
        }

        // ── folders & guard-railed naming ────────────────────────────────────────────────────────────

        static void EnsureFolder(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
            string leaf = Path.GetFileName(dir);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // Never overwrite: if <name>.<ext> exists, append _1, _2, … until the path is free.
        static string UniqueAssetPath(string dir, string name, string ext)
        {
            string path = $"{dir}/{name}.{ext}";
            if (!File.Exists(ToAbsolute(path))) return path;
            for (int i = 1; i < 10000; i++)
            {
                string candidate = $"{dir}/{name}_{i}.{ext}";
                if (!File.Exists(ToAbsolute(candidate)))
                {
                    Debug.Log($"[Larder] {name}.{ext} already existed — writing {Path.GetFileName(candidate)} instead (guard rail).");
                    return candidate;
                }
            }
            return path;
        }

        static string ToAbsolute(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
