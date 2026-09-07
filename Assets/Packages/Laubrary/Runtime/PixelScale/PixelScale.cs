using UnityEngine;

namespace Laubrary.PixelScale
{
    public static class PixelScale
    {
        public static float OrthographicCameraSize(PixelScaleProjectSettings settings = null)
        {
            settings ??= PixelScaleProjectSettings.Instance;
            return settings.targetResolution.y / (2f * Mathf.Max(1, settings.pixelsPerUnit));
        }

        public static int IntegerUpscale(int displayHeight, PixelScaleProjectSettings settings = null)
        {
            settings ??= PixelScaleProjectSettings.Instance;
            return Mathf.Max(1, Mathf.FloorToInt(displayHeight / (float)Mathf.Max(1, settings.targetResolution.y)));
        }

        public static int PreviewZoomOneMultiplier(PixelScaleProjectSettings settings = null) => IntegerUpscale(Screen.currentResolution.height, settings);
    }
}
