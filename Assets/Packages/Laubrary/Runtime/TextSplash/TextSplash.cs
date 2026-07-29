using UnityEngine;
using TMPro;
using Laubrary.PreviewKit;

namespace Laubrary.TextSplash
{
    /// Direction a splash slides in FROM / out TO. None = no slide (fade instead).
    public enum SplashDir { None, Left, Right, Top, Bottom }

    /// Easing applied to a slide/fade's 0..1 progress.
    public enum SplashEase { Linear, SmoothStep, EaseOut, EaseIn }

    /// <summary>
    /// A reusable "splash this text on screen" asset — everything needed to fly a line of text in, hold it, and fly
    /// it back out, authored once and played with one call. Carries DEFAULT text + durations, both overridable per
    /// call (<see cref="Show"/>). Slice 1: a single TMP line with a <see cref="ZuiFill"/> face, a border, alpha, size,
    /// and slide/fade in→hold→out transitions. (Per-letter FX + fill colour-cycling are a later slice.)
    /// </summary>
    [CreateAssetMenu(menuName = "Laubrary/Text Splash", fileName = "TextSplash")]
    public class TextSplash : ScriptableObject, IVisualPreview
    {
        [Header("Content")]
        [TextArea] public string text = "SPLASH!";
        [Min(1f)] public float fontSize = 96f;
        [Tooltip("Optional TMP font. Null = TMP's default font.")]
        public TMP_FontAsset font;

        [Header("Look")]
        [Tooltip("The text FACE fill — a solid colour or a ZUI gradient (the same fill system Pyre uses).")]
        public ZuiFill fill = new ZuiFill { color = Color.white };
        [Range(0f, 1f)] public float alpha = 1f;
        public Color borderColor = new Color(0f, 0f, 0f, 1f);
        [Range(0f, 1f)] [Tooltip("Border (TMP outline) thickness, 0 = none.")]
        public float borderWidth = 0.2f;

        [Header("Timing (seconds)")]
        [Min(0f)] public float slideInDuration = 0.35f;
        [Min(0f)] public float holdDuration = 1.5f;
        [Min(0f)] public float slideOutDuration = 0.35f;

        [Header("Transition")]
        public SplashDir inFrom = SplashDir.Bottom;
        public SplashDir outTo = SplashDir.Top;
        public SplashEase ease = SplashEase.SmoothStep;
        [Tooltip("Where the text rests, in viewport space (0.5,0.5 = centre).")]
        public Vector2 anchor = new Vector2(0.5f, 0.5f);
        [Range(0.25f, 1.5f)] [Tooltip("How far off-screen the slide starts/ends, as a fraction of the screen.")]
        public float slideDistance = 1f;

        /// Total play length (in + hold + out), seconds.
        public float TotalDuration => slideInDuration + holdDuration + slideOutDuration;

        /// <summary>Splash this text on screen. Null args use the asset's own defaults; pass either to override just
        /// this play. Spawns a self-cleaning overlay, runs slide/fade in → hold → out, then despawns.</summary>
        /// <returns>A handle to await completion or cancel early.</returns>
        public SplashHandle Show(string overrideText = null, float? overrideHold = null)
            => SplashPlayer.Play(this, overrideText, overrideHold);

        // ── IVisualPreview (browser thumbnail) — not rendered yet; the editor window's live preview is where you
        //    author it. A real held-state thumbnail is a later slice. ──
        public Texture2D RenderPreviewTexture() => null;
        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}
