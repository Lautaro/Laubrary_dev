using UnityEditor;
using UnityEngine;
using Laubrary.AssetKit.Editor;
using Laubrary.Chunks;

namespace Laubrary.Launimator.Editor
{
    /// Registers Open + Create for SpriteChunkAnimation — Open re-opens the wrapped sprite's source PNG in
    /// Aseprite for further editing; Create draws a brand new one from scratch. Same starter-PNG pattern as
    /// Laumination Builder's own "New (Aseprite)" button (AsepriteLauncher, Point/Uncompressed/PPU16 import).
    [InitializeOnLoad]
    static class SpriteChunkAnimationEditorLink
    {
        static SpriteChunkAnimationEditorLink()
        {
            LauAssetEditors.RegisterOpen<SpriteChunkAnimation>(a =>
            {
                if (a.sprite == null) return;
                string path = AssetDatabase.GetAssetPath(a.sprite);
                if (!string.IsNullOrEmpty(path)) AsepriteLauncher.Open(path);
            });
            LauAssetEditors.RegisterCreate<SpriteChunkAnimation>(Create);
        }

        static SpriteChunkAnimation Create(string suggestedName, string folder)
        {
            folder = AssetFolders.EnsureFolder(folder);
            string pngPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{suggestedName}.png");

            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 0);
            int c = size / 2;
            for (int yy = c - 1; yy <= c; yy++) for (int xx = c - 1; xx <= c; xx++) px[yy * size + xx] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            System.IO.File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(pngPath);

            if (AssetImporter.GetAtPath(pngPath) is TextureImporter imp)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.isReadable = true;
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.spritePixelsPerUnit = 16f;
                imp.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
            var visual = ScriptableObject.CreateInstance<SpriteChunkAnimation>();
            visual.sprite = sprite;
            string soPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{suggestedName}.asset");
            AssetDatabase.CreateAsset(visual, soPath);
            AssetDatabase.SaveAssets();

            AsepriteLauncher.Open(pngPath);
            return visual;
        }
    }
}
