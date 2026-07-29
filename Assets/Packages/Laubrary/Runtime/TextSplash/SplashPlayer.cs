using UnityEngine;
using TMPro;

namespace Laubrary.TextSplash
{
    /// <summary>A live splash you can await or stop early. `Show()` returns one.</summary>
    public sealed class SplashHandle
    {
        public bool IsDone { get; internal set; }
        public bool IsCancelled { get; private set; }
        /// Fires once when the splash finishes (or is cancelled).
        public event System.Action Completed;
        /// Stop the splash on its next frame.
        public void Cancel() => IsCancelled = true;
        internal void Complete() { if (IsDone) return; IsDone = true; Completed?.Invoke(); }
    }

    /// <summary>The runtime host for one <see cref="TextSplash"/>: a screen-overlay Canvas + a TMP line it drives
    /// through slide/fade in → hold → out, then self-destroys. Spawned by <see cref="TextSplash.Show"/>; not added
    /// by hand. The look-application (<see cref="ApplyLook"/>) and pose (<see cref="Evaluate"/>) are public statics so
    /// the editor window's live preview drives the SAME math against a scrub time.</summary>
    [AddComponentMenu("")]
    public sealed class SplashPlayer : MonoBehaviour
    {
        TextSplash _spec;
        TextMeshProUGUI _tmp;
        float _hold, _elapsed;
        SplashHandle _handle;

        public static SplashHandle Play(TextSplash spec, string overrideText, float? overrideHold)
        {
            var handle = new SplashHandle();
            if (spec == null) { handle.Complete(); return handle; }

            var go = new GameObject("[TextSplash] " + spec.name) { hideFlags = HideFlags.HideAndDontSave };
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;   // above gameplay UI

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = spec.anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(4000f, 800f);   // generous; the text overflows freely
            ApplyLook(spec, tmp, overrideText);

            var player = go.AddComponent<SplashPlayer>();
            player._spec = spec; player._tmp = tmp;
            player._hold = overrideHold ?? spec.holdDuration;
            player._handle = handle;
            return handle;
        }

        void Update()
        {
            if (_spec == null || _tmp == null || _handle == null) { Finish(); return; }
            if (_handle.IsCancelled) { Finish(); return; }

            _elapsed += Time.unscaledDeltaTime;
            var canvasRT = _tmp.canvas != null ? _tmp.canvas.transform as RectTransform : null;
            Vector2 cs = canvasRT != null ? canvasRT.rect.size : new Vector2(Screen.width, Screen.height);

            Evaluate(_spec, _elapsed, _hold, cs.x, cs.y, out var pos, out var a, out bool done);
            _tmp.rectTransform.anchoredPosition = pos;
            _tmp.alpha = a;
            if (done) Finish();
        }

        void Finish()
        {
            _handle?.Complete();
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        // ── shared with the editor preview ────────────────────────────────────────────

        /// Apply a spec's static LOOK (text / font / size / fill / border / alpha) to any TMP text (the runtime's
        /// UGUI text OR the editor preview's 3D text — both are <see cref="TMP_Text"/>). `text` null = the spec's own
        /// default. Movement + play-time alpha come from <see cref="Evaluate"/>, not here.
        public static void ApplyLook(TextSplash spec, TMP_Text tmp, string text)
        {
            if (spec == null || tmp == null) return;
            tmp.text = text ?? spec.text;
            if (spec.font != null) tmp.font = spec.font;
            tmp.fontSize = spec.fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;

            var fill = spec.fill;
            bool gradient = fill != null && fill.mode != ZuiFill.Mode.Solid && fill.texture == ZuiFill.TextureKind.None;
            if (gradient)
            {
                // Sample the ramp top (life 1) → bottom (life 0) as a vertical TMP vertex gradient.
                Color top = fill.Evaluate(1f, 0f, 0f);
                Color bot = fill.Evaluate(0f, 0f, 0f);
                tmp.enableVertexGradient = true;
                tmp.colorGradient = new VertexGradient(top, top, bot, bot);
                tmp.color = Color.white;
            }
            else
            {
                tmp.enableVertexGradient = false;
                tmp.color = fill != null ? fill.color : Color.white;
            }

            tmp.outlineColor = spec.borderColor;
            tmp.outlineWidth = spec.borderWidth;
            tmp.alpha = spec.alpha;
        }

        /// The splash POSE at a given elapsed time, for a canvas of size (w,h): the rect's anchored offset from its
        /// rest anchor, plus the alpha, plus whether it has finished. Pure — drives both the runtime and the preview.
        public static void Evaluate(TextSplash s, float elapsed, float hold, float w, float h,
            out Vector2 pos, out float alpha, out bool done)
        {
            pos = Vector2.zero; alpha = s.alpha; done = false;
            float inD = s.slideInDuration, outD = s.slideOutDuration;
            Vector2 offIn = DirOffset(s.inFrom, w, h) * s.slideDistance;
            Vector2 offOut = DirOffset(s.outTo, w, h) * s.slideDistance;

            if (elapsed < inD)
            {
                float e = Ease(s.ease, inD > 0f ? elapsed / inD : 1f);
                if (s.inFrom == SplashDir.None) alpha = s.alpha * e;
                else pos = Vector2.Lerp(offIn, Vector2.zero, e);
            }
            else if (elapsed < inD + hold) { pos = Vector2.zero; alpha = s.alpha; }
            else if (elapsed < inD + hold + outD)
            {
                float e = Ease(s.ease, outD > 0f ? (elapsed - inD - hold) / outD : 1f);
                if (s.outTo == SplashDir.None) alpha = s.alpha * (1f - e);
                else pos = Vector2.Lerp(Vector2.zero, offOut, e);
            }
            else { done = true; alpha = 0f; }
        }

        static Vector2 DirOffset(SplashDir d, float w, float h) => d switch
        {
            SplashDir.Left => new Vector2(-w, 0f),
            SplashDir.Right => new Vector2(w, 0f),
            SplashDir.Top => new Vector2(0f, h),
            SplashDir.Bottom => new Vector2(0f, -h),
            _ => Vector2.zero,
        };

        static float Ease(SplashEase e, float t)
        {
            t = Mathf.Clamp01(t);
            return e switch
            {
                SplashEase.SmoothStep => t * t * (3f - 2f * t),
                SplashEase.EaseOut => 1f - (1f - t) * (1f - t),
                SplashEase.EaseIn => t * t,
                _ => t,
            };
        }
    }
}
