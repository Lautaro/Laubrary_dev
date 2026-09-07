using UnityEngine;

namespace Laubrary.PixelScale
{
    [CreateAssetMenu(fileName = "PixelScaleProjectSettings", menuName = "Laubrary/Pixel Scale Project Settings")]
    public sealed class PixelScaleProjectSettings : ScriptableObject
    {
        const string ResourceName = "PixelScaleProjectSettings";
        static PixelScaleProjectSettings _instance;

        [Tooltip("How many source-art pixels occupy one world unit. Keep this at the project's tile unit.")]
        [Min(1)] public int pixelsPerUnit = 16;

        [Tooltip("The low-resolution world canvas. UI and text do not use this resolution.")]
        public Vector2Int targetResolution = new Vector2Int(320, 200);

        [Tooltip("A proportion reference for authored characters. A typical human is 32 pixels high at this scale.")]
        [Min(1)] public int referenceHumanHeightPx = 32;

        [Tooltip("The display used as a whole-number reference for this project. Steam Deck is 1280 by 800, exactly four times the default world canvas.")]
        public Vector2Int referenceDisplayResolution = new Vector2Int(1280, 800);

        public static PixelScaleProjectSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<PixelScaleProjectSettings>(ResourceName);
                    if (_instance == null)
                    {
                        _instance = CreateInstance<PixelScaleProjectSettings>();
                        _instance.hideFlags = HideFlags.HideAndDontSave;
                    }
                }

                return _instance;
            }
        }

        public static void ClearCache() => _instance = null;
    }
}
