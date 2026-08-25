using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Pyre.Editor
{
    /// Bakes a Pyre into the SAME folder as the spec asset: a horizontal-ish sprite sheet PNG sliced into
    /// per-frame sprites, plus an AnimationClip that cycles them. Renders through the shared PyreRenderer, so
    /// a bake is byte-identical to the editor preview. No dedicated Assets/Pyre/ folder — the outputs sit
    /// right next to the asset you baked (following the Laubrary flat-folder convention). Near-verbatim port of
    /// Pyre1's Editor/Pyre/BlastBaker.cs — same technique, same guard rails.
    ///
    /// Pyre has no per-shape `origin` field the way Pyre1's spec does (that field drives its baked sprite
    /// pivot), so every baked sprite pivots at texture centre (0.5, 0.5) for now — the sane default for a blast
    /// that's centred in its own canvas. If a future Pyre effect wants an off-centre pivot, that's a spec
    /// field + UI to add then, not a reason to block this first bake pass.
    ///
    /// GUARD RAIL: never overwrites an existing user asset. If a target path is taken, the name is versioned
    /// (pyreplus_1, pyreplus_2, …) and every created path is logged.
    public static class PyreBaker
    {
        static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);

        // Written to the baked PNG's TextureImporter.userData — the shared "LauAssetBrowser.BakedMarkerPrefix"
        // convention (see LauAssetBrowser.cs) any baker opts into so a general Sprite browse can exclude derived
        // bake output. Pyre is already the correct pickable unit for other tools (via
        // PyreChunkAnimation's IChunkAnimation adapter) — this bake's own sliced sub-sprites are a
        // standalone drag-and-drop export, not meant to clutter a general Sprite browse.
        public const string BakedMarker = Laubrary.AssetKit.Editor.LauAssetBrowser.BakedMarkerPrefix + "Pyre";

        public static void Bake(Pyre spec)
        {
            if (spec == null) { Debug.LogWarning("[Pyre] Bake skipped: no Pyre."); return; }

            // Bake beside the spec asset; fall back to "Assets" if the spec is unsaved.
            string specPath = AssetDatabase.GetAssetPath(spec);
            string dir = string.IsNullOrEmpty(specPath) ? "Assets" : Path.GetDirectoryName(specPath);
            if (string.IsNullOrEmpty(dir)) dir = "Assets";
            dir = dir.Replace('\\', '/');

            int cw = spec.Width, ch = spec.Height;
            var sheet = PyreRenderer.RenderSheet(spec, out int cols, out int rows, 8);

            // 1) write the PNG (never clobbering an existing file)
            string baseName = SanitizeName(spec.name.Length > 0 ? spec.name : "Pyre");
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
            importer.userData = BakedMarker;   // lets LauAssetBrowser (and anything else) filter this out of a raw-Sprite browse

            int frames = Mathf.Max(1, spec.frameCount);
            var meta = new SpriteMetaData[frames];
            for (int f = 0; f < frames; f++)
            {
                Rect r = PyreRenderer.FrameRect(f, cols, rows, cw, ch);
                meta[f] = new SpriteMetaData
                {
                    name = "pyreplus_" + f,
                    rect = r,
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = CenterPivot
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

            // 4) build an AnimationClip that steps the SpriteRenderer through the frames, at the spec's own
            //    authored preview rate — so the baked clip actually plays back at the speed it was designed at.
            float fps = Mathf.Max(1f, spec.previewFps);
            var clip = new AnimationClip { frameRate = fps };
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
