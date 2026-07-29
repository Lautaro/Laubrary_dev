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

            if (_spec.perLetter)
            {
                // The whole line stays at rest; every letter carries its own staggered slide/fade/spin.
                _tmp.rectTransform.anchoredPosition = Vector2.zero;
                _tmp.alpha = 1f;
                ApplyPerLetter(_spec, _tmp, _elapsed, _hold, cs.x, cs.y);
                if (_elapsed >= _spec.SequenceDuration(_tmp.textInfo.characterCount)) Finish();
            }
            else
            {
                Evaluate(_spec, _elapsed, _hold, cs.x, cs.y, out var pos, out var a, out bool done);
                _tmp.rectTransform.anchoredPosition = pos;
                _tmp.alpha = a;
                if (_spec.cycleFill) ApplyCycle(_spec, _tmp, _elapsed);
                if (done) Finish();
            }
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

        /// Per-letter pose: the same slide/fade as <see cref="Evaluate"/>, PLUS a spin angle (fully wound at the
        /// transition extremes, 0 at rest). `elapsed` is the LETTER's local time (global − letterIndex·stagger),
        /// `hold` its local hold.
        public static void LetterAnim(TextSplash s, float elapsed, float hold, float w, float h,
            out Vector2 pos, out float alpha, out float spinDeg, out bool done)
        {
            Evaluate(s, elapsed, hold, w, h, out pos, out alpha, out done);
            float inD = s.slideInDuration, outD = s.slideOutDuration;
            if (elapsed < 0f) spinDeg = s.spinDegrees;                       // not yet started → fully wound
            else if (elapsed < inD)
                spinDeg = s.spinDegrees * (1f - Ease(s.ease, inD > 0f ? elapsed / inD : 1f));
            else if (elapsed < inD + hold) spinDeg = 0f;
            else if (elapsed < inD + hold + outD)
                spinDeg = s.spinDegrees * Ease(s.ease, outD > 0f ? (elapsed - inD - hold) / outD : 1f);
            else spinDeg = s.spinDegrees;
        }

        /// Drive each character of `tmp` independently (staggered slide/fade + spin + optional colour cycle) at global
        /// time `time`. `w,h` are the transition distances in the TMP's LOCAL vertex units (canvas px for a UGUI text;
        /// world÷scale for a 3D preview text). Regenerates + rewrites the mesh, so call it every frame while playing.
        public static void ApplyPerLetter(TextSplash s, TMP_Text tmp, float time, float hold, float w, float h)
        {
            if (tmp == null) return;
            tmp.ForceMeshUpdate();
            var info = tmp.textInfo;
            int n = info.characterCount;
            float stg = Mathf.Max(0f, s.letterStagger);
            float localHold = Mathf.Max(0, n - 1) * stg + hold;
            Vector3 axis = AxisVec(s.spinAxis);

            for (int i = 0; i < n; i++)
            {
                var ci = info.characterInfo[i];
                if (!ci.isVisible) continue;
                int mi = ci.materialReferenceIndex;
                int vi = ci.vertexIndex;
                var verts = info.meshInfo[mi].vertices;
                var cols = info.meshInfo[mi].colors32;

                LetterAnim(s, time - i * stg, localHold, w, h, out var off, out float a, out float spin, out _);

                Vector3 c = (verts[vi] + verts[vi + 2]) * 0.5f;             // char centre = (bottom-left + top-right)/2
                Quaternion rot = Quaternion.AngleAxis(spin, axis);
                Vector3 delta = new Vector3(off.x, off.y, 0f);
                for (int k = 0; k < 4; k++)
                    verts[vi + k] = c + rot * (verts[vi + k] - c) + delta;

                byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                if (s.cycleFill && s.fill != null)
                {
                    Color32 c32 = s.fill.Evaluate(Frac(time * s.cycleSpeed - i * s.cyclePerLetter), 0f, 0f);
                    c32.a = alpha;
                    for (int k = 0; k < 4; k++) cols[vi + k] = c32;
                }
                else
                {
                    for (int k = 0; k < 4; k++) { var cc = cols[vi + k]; cc.a = alpha; cols[vi + k] = cc; }
                }
            }

            tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }

        /// Whole-line colour cycle: scroll the fill gradient's phase over time into a fresh top→bottom vertex gradient.
        /// Face only — the TMP outline (border) is untouched. No-op on a solid or textured fill.
        public static void ApplyCycle(TextSplash s, TMP_Text tmp, float time)
        {
            var fill = s.fill;
            if (fill == null || fill.mode == ZuiFill.Mode.Solid || fill.texture != ZuiFill.TextureKind.None) return;
            float p = Frac(time * s.cycleSpeed);
            Color top = fill.Evaluate(p, 0f, 0f);
            Color bot = fill.Evaluate(Frac(p + 0.5f), 0f, 0f);
            tmp.enableVertexGradient = true;
            tmp.colorGradient = new VertexGradient(top, top, bot, bot);
        }

        static Vector3 AxisVec(SplashAxis a) => a switch
        {
            SplashAxis.X => Vector3.right,
            SplashAxis.Z => Vector3.forward,
            _ => Vector3.up,
        };

        static float Frac(float x) => x - Mathf.Floor(x);

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
