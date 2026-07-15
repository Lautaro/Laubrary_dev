using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.PreviewStage
{
    /// An editor-only, reusable "test backdrop" for any tool's preview viewport: a fill colour plus a set of placed
    /// sprites you arrange to mock up how an animation/effect will look in context (a floor, a wall, some props).
    /// Saved as an asset and recalled across tools. It deliberately stores NO tool-specific frame/camera position —
    /// each tool owns where its own subject sits over this shared backdrop.
    public class PreviewBackground : ScriptableObject
    {
        [Tooltip("Fill drawn behind the sprites. Alpha 0 = keep the tool's own backdrop, just overlay the sprites.")]
        public Color fill = new Color(0f, 0f, 0f, 0f);

        public List<StageSprite> sprites = new List<StageSprite>();

        /// A whole-viewport flat backdrop STYLE (Solid/Gradient/Image), saved/recalled alongside the sprites so a
        /// preset carries the WHOLE background, not just the props on top of it. None = this asset doesn't own a
        /// style at all — the tool keeps drawing whatever backdrop it already had (its own default, or a
        /// per-asset value it stores itself). Any tool wanting this style is responsible for reading/drawing it —
        /// this class only stores it; see Pyre's PyreWindow.DrawBackdrop for a reference implementation.
        public enum Mode { None, Solid, Gradient, Image }
        public Mode mode = Mode.None;
        public Color solid = new Color(0.08f, 0.08f, 0.10f);
        public Gradient gradient;
        public Texture2D image;
        public Color imageTint = Color.white;
        public float imageZoom = 1f;
        public Vector2 imagePos;
    }

    /// One placed sprite in a PreviewBackground.
    [System.Serializable]
    public class StageSprite
    {
        public Sprite sprite;
        [Tooltip("Offset from the viewport centre, in canvas pixels (scaled by the preview zoom).")]
        public Vector2 position;
        public float scale = 1f;
        public Color tint = Color.white;
        [Tooltip("Draw IN FRONT of the tool's subject (a foreground decoration that occludes it) instead of behind.")]
        public bool front = false;
    }
}
