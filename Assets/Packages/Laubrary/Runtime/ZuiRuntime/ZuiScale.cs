// One proportional scale for ALL runtime immediate-mode UI. uGUI menus scale via CanvasScaler; OnGUI
// draws in raw pixels so it must scale itself — and it must scale FONT SIZES, never GUI.matrix (matrix
// scaling rasterizes text small and stretches it: blurry). 1x at an 800px-tall panel (Steam Deck),
// growing on bigger screens (1080p ≈1.35x, 1440p ≈1.8x, 4K ≈2.7x) so nothing is ever tiny.
// Every ZuiRuntime helper takes sizes in "points" (px at 800p) and applies this factor internally.

using UnityEngine;

namespace ZuiRuntime
{
    public static class UIScale
    {
        /// <summary>Screen height at which UI renders 1:1. Steam Deck panel by default.</summary>
        public static float ReferenceHeight = 800f;

        /// <summary>Scale ceiling, so 8K monitors don't produce comedy buttons.</summary>
        public static float MaxFactor = 3f;

        public static float Factor => Mathf.Clamp(Screen.height / ReferenceHeight, 1f, MaxFactor);

        /// <summary>Scales a point size (px at the reference height) to the current screen.</summary>
        public static float S(float points) => points * Factor;

        /// <summary>Scaled font size, rounded for crisp glyph rendering.</summary>
        public static int Font(float points) => Mathf.Max(1, Mathf.RoundToInt(points * Factor));
    }
}
