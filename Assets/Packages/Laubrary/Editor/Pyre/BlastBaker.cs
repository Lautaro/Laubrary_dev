using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Laubrary.Pyre;

namespace Laubrary.Pyre.Editor
{
    /// Bakes a BlastSpec into the SAME folder as the spec asset: a horizontal-ish sprite sheet PNG sliced into
    /// per-frame sprites, plus an AnimationClip that cycles them. Renders through the shared BlastRenderer, so a
    /// bake is byte-identical to the editor preview and the runtime player. No dedicated Assets/Pyre/ folder —
    /// the outputs sit right next to the asset you baked (following the Laubrary flat-folder convention).
    ///
    /// GUARD RAIL: never overwrites an existing user asset. If a target path is taken, the name is versioned
    /// (blast_1, blast_2, …) and every created path is logged.
    public static class BlastBaker
    {
        public static void Bake(BlastSpec spec, int fps = 24)
        {
            if (spec == null) { Debug.LogWarning("[Pyre] Bake skipped: no BlastSpec."); return; }

            // Bake beside the spec asset; fall back to "Assets" if the spec is unsaved.
            string specPath = AssetDatabase.GetAssetPath(spec);
            string dir = string.IsNullOrEmpty(specPath) ? "Assets" : Path.GetDirectoryName(specPath);
            if (string.IsNullOrEmpty(dir)) dir = "Assets";
            dir = dir.Replace('\\', '/');

            int size = Mathf.Max(1, spec.canvasSize);
            var sheet = BlastRenderer.RenderSheet(spec, out int cols, out int rows, 8);

            // 1) write the PNG (never clobbering an existing file)
            string baseName = SanitizeName(spec.name.Length > 0 ? spec.name : "Blast");
            string pngPath = UniquePath(dir, baseName, "png");
            File.WriteAllBytes(pngPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);

            // 2) import as a multiple-sprite, point-filtered sheet and slice one rect per frame
            var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = spec.pixelsPerUnit;

            int frames = Mathf.Max(1, spec.frameCount);
            var meta = new SpriteMetaData[frames];
            for (int f = 0; f < frames; f++)
            {
                Rect r = BlastRenderer.FrameRect(f, cols, rows, size);
                meta[f] = new SpriteMetaData
                {
                    name = "blast_" + f,
                    rect = r,
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f)
                };
            }
#pragma warning disable CS0618 // TextureImporter.spritesheet is legacy but is the documented slice-from-code path
            importer.spritesheet = meta;
#pragma warning restore CS0618
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            // 3) collect the sliced sprites in frame order
            var sprites = new List<Sprite>();
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(pngPath))
                if (obj is Sprite s) sprites.Add(s);
            sprites.Sort((a, b) => FrameIndexOf(a.name).CompareTo(FrameIndexOf(b.name)));
            if (sprites.Count == 0) { Debug.LogWarning("[Pyre] Bake produced no sprites from " + pngPath); return; }

            // 4) build an AnimationClip that steps the SpriteRenderer through the frames
            var clip = new AnimationClip { frameRate = Mathf.Max(1f, fps) };
            var keys = new ObjectReferenceKeyframe[sprites.Count];
            for (int i = 0; i < sprites.Count; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / clip.frameRate, value = sprites[i] };

            var binding = new EditorCurveBinding { type = typeof(SpriteRenderer), path = "", propertyName = "m_Sprite" };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string clipPath = UniquePath(dir, baseName, "anim");
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Pyre] Baked '{spec.name}' → sheet: {pngPath}  ·  clip: {clipPath}  ({sprites.Count} frames @ {fps}fps)");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(pngPath));
        }

        // Return a path under dir for baseName.ext that does not exist yet (versioning the name if needed).
        static string UniquePath(string dir, string baseName, string ext)
        {
            string path = $"{dir}/{baseName}.{ext}";
            if (!File.Exists(path)) return path;
            int i = 1;
            while (File.Exists($"{dir}/{baseName}_{i}.{ext}")) i++;
            string versioned = $"{dir}/{baseName}_{i}.{ext}";
            Debug.Log($"[Pyre] '{path}' exists — writing '{versioned}' instead (guard rail: never overwrite user assets).");
            return versioned;
        }

        static int FrameIndexOf(string spriteName)
        {
            int u = spriteName.LastIndexOf('_');
            return u >= 0 && int.TryParse(spriteName.Substring(u + 1), out int n) ? n : 0;
        }

        static string SanitizeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }
    }
}
