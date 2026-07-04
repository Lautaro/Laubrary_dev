using ImpossibleRobert.Common;
#if UNITY_EDITOR_WIN && NET_4_6
using System.Drawing;
#else
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
#endif
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>
    /// Validation and error detection utilities for preview generation.
    /// Consolidates validation logic from PreviewManager and CustomPrefabPreviewGenerator.
    /// </summary>
    public static class PreviewValidation
    {
        private static ulong _textureIconHash;
        private static ulong _audioIconHash;

#if UNITY_EDITOR_WIN && NET_4_6
        public static bool IsErrorShader(Bitmap image) => ImageUtils.IsErrorPreview(image);
        
        public static bool IsDefaultIcon(Bitmap image)
        {
            if (_textureIconHash == 0)
            {
                Bitmap textureIcon = ((Texture2D)EditorGUIUtility.IconContent("d_texture icon").image).MakeReadable().ToImage();
                _textureIconHash = ImageUtils.ComputePerceptualHash(textureIcon);
            }
            if (_audioIconHash == 0)
            {
                Bitmap audioIcon = ((Texture2D)EditorGUIUtility.IconContent("audioclip icon").image).MakeReadable().ToImage();
                _audioIconHash = ImageUtils.ComputePerceptualHash(audioIcon);
            }
#else
        public static bool IsErrorShader(Image<Rgba32> image) => ImageUtils.IsErrorPreview(image);
        
        public static bool IsDefaultIcon(Image<Rgba32> image)
        {
            if (_textureIconHash == 0)
            {
                Image<Rgba32> textureIcon = ((Texture2D)EditorGUIUtility.IconContent("d_texture icon").image).MakeReadable().ToImage();
                _textureIconHash = ImageUtils.ComputePerceptualHash(textureIcon);
            }
            if (_audioIconHash == 0)
            {
                Image<Rgba32> audioIcon = ((Texture2D)EditorGUIUtility.IconContent("audioclip icon").image).MakeReadable().ToImage();
                _audioIconHash = ImageUtils.ComputePerceptualHash(audioIcon);
            }
#endif
            return ImageUtils.HasDominantColor(image, new UnityEngine.Color(128f / 255f, 216f / 255f, 255f / 255f))
                || ImageUtils.AreSimilar(image, _textureIconHash)
                || ImageUtils.AreSimilar(image, _audioIconHash);
        }
    }
}
