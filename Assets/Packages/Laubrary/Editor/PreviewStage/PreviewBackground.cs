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
