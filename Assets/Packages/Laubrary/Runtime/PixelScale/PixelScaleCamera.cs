using System;
using System.Reflection;
using UnityEngine;

namespace Laubrary.PixelScale
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class PixelScaleCamera : MonoBehaviour
    {
        [Tooltip("Use the values below for this scene instead of the project's Pixel Scale Project Settings asset.")]
        [SerializeField] bool overrideProjectSettings;
        [Tooltip("How many source-art pixels occupy one world unit when this scene overrides the project setting.")]
        [SerializeField, Min(1)] int overridePixelsPerUnit = 16;
        [Tooltip("The low-resolution world canvas when this scene overrides the project setting. UI and text remain native-resolution.")]
        [SerializeField] Vector2Int overrideTargetResolution = new Vector2Int(320, 200);
        [Tooltip("A proportion reference when this scene overrides the project setting.")]
        [SerializeField, Min(1)] int overrideReferenceHumanHeightPx = 32;

        Camera _camera;

        void OnEnable() => Apply();

        public void Apply()
        {
            _camera ??= GetComponent<Camera>();
            if (_camera == null) return;
            var settings = PixelScaleProjectSettings.Instance;
            var pixelsPerUnit = overrideProjectSettings ? overridePixelsPerUnit : settings.pixelsPerUnit;
            var targetResolution = overrideProjectSettings ? overrideTargetResolution : settings.targetResolution;
            _camera.orthographic = true;
            _camera.orthographicSize = targetResolution.y / (2f * Mathf.Max(1, pixelsPerUnit));
            ConfigureInstalledPixelPerfectCamera(pixelsPerUnit, targetResolution);
        }

        void ConfigureInstalledPixelPerfectCamera(int pixelsPerUnit, Vector2Int targetResolution)
        {
            var type = Type.GetType("UnityEngine.Rendering.Universal.PixelPerfectCamera, Unity.RenderPipelines.Universal.Runtime") ?? Type.GetType("UnityEngine.U2D.PixelPerfectCamera, Unity.2D.PixelPerfect");
            if (type == null) return;
            var pixelPerfectCamera = GetComponent(type);
            if (pixelPerfectCamera == null) return;
            Set(pixelPerfectCamera, type, "assetsPPU", pixelsPerUnit);
            Set(pixelPerfectCamera, type, "refResolutionX", targetResolution.x);
            Set(pixelPerfectCamera, type, "refResolutionY", targetResolution.y);
            Set(pixelPerfectCamera, type, "upscaleRT", false);
            Set(pixelPerfectCamera, type, "cropFrameX", false);
            Set(pixelPerfectCamera, type, "cropFrameY", true);
            Set(pixelPerfectCamera, type, "stretchFill", false);
            Set(pixelPerfectCamera, type, "pixelSnapping", true);
        }

        static void Set(object target, Type type, string member, object value)
        {
            var property = type.GetProperty(member, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite) { property.SetValue(target, value); return; }
            type.GetField(member, BindingFlags.Instance | BindingFlags.Public)?.SetValue(target, value);
        }
    }
}
