using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class FadeEffect : AudioEffect {

        public override string Name => "Fade";
        public override string ToggleLabel => "Fade";

        public override bool IsEnabled(Klip klip) => klip.fadeEnabled;
        public override void SetEnabled(Klip klip, bool enabled) => klip.fadeEnabled = enabled;

        public override bool IsActive(Klip klip) {
            return klip.fadeEnabled && (klip.fadeInDuration > 0.001f || klip.fadeOutDuration > 0.001f);
        }

        public override float[] Process(float[] samples, int channels, int sampleRate, Klip klip) {
            int sampleCount = samples.Length / channels;
            bool sCurve = klip.fadeUseSCurve;

            int fadeInSamples = Mathf.Min(Mathf.FloorToInt(klip.fadeInDuration * sampleRate), sampleCount);
            if (fadeInSamples > 0) {
                for (int i = 0; i < fadeInSamples; i++) {
                    float t = (float)i / fadeInSamples;
                    float factor = sCurve ? t * t * (3f - 2f * t) : t;
                    for (int c = 0; c < channels; c++) {
                        samples[i * channels + c] *= factor;
                    }
                }
            }

            int fadeOutSamples = Mathf.Min(Mathf.FloorToInt(klip.fadeOutDuration * sampleRate), sampleCount);
            if (fadeOutSamples > 0) {
                int fadeOutStart = sampleCount - fadeOutSamples;
                for (int i = 0; i < fadeOutSamples; i++) {
                    float t = 1f - (float)i / fadeOutSamples;
                    float factor = sCurve ? t * t * (3f - 2f * t) : t;
                    for (int c = 0; c < channels; c++) {
                        samples[(fadeOutStart + i) * channels + c] *= factor;
                    }
                }
            }

            return samples;
        }

        public override bool DrawUI(Klip klip, ref bool isDragging, AudioClip sourceClip) {
            float maxFade = sourceClip != null ? sourceClip.length * 0.5f : 1f;
            EditorGUI.BeginChangeCheck();
            float newFadeIn = ZUI.Slider(klip.fadeInDuration, 0f, maxFade, $"Fade In {klip.fadeInDuration:F3}s", ZUI.SliderStyle.BigSlider);
            float newFadeOut = ZUI.Slider(klip.fadeOutDuration, 0f, maxFade, $"Fade Out {klip.fadeOutDuration:F3}s", ZUI.SliderStyle.BigSlider);
            bool newSCurve = ZUI.Toggle(klip.fadeUseSCurve, klip.fadeUseSCurve ? "S-Curve" : "Linear", ZUI.Style.RichToggle, ZUICornerMask.All, GUILayout.Height(18f), GUILayout.Width(70f));
            if (EditorGUI.EndChangeCheck()) {
                klip.fadeInDuration = newFadeIn;
                klip.fadeOutDuration = newFadeOut;
                klip.fadeUseSCurve = newSCurve;
                return true;
            }
            return false;
        }
    }

}
