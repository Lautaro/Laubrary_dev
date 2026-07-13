using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class NormalizationEffect : AudioEffect {

        public override string Name => "Normalization";
        public override string ToggleLabel => "Normalize";

        public override bool IsEnabled(Klip klip) => klip.normalizationEnabled;
        public override void SetEnabled(Klip klip, bool enabled) => klip.normalizationEnabled = enabled;

        public override bool IsActive(Klip klip) {
            return klip.normalizationEnabled;
        }

        public override float[] Process(float[] samples, int channels, int sampleRate, Klip klip) {
            float targetLinear = Mathf.Pow(10f, klip.normalizeTargetDB / 20f);

            float peak = 0f;
            for (int i = 0; i < samples.Length; i++) {
                float abs = Mathf.Abs(samples[i]);
                if (abs > peak) peak = abs;
            }

            if (peak > 0.0001f) {
                float scale = targetLinear / peak;
                for (int i = 0; i < samples.Length; i++) {
                    samples[i] = Mathf.Clamp(samples[i] * scale, -1f, 1f);
                }
            }

            return samples;
        }

        public override bool DrawUI(Klip klip, ref bool isDragging, AudioClip sourceClip) {
            EditorGUI.BeginChangeCheck();
            float newTargetDB = ZUI.Slider(klip.normalizeTargetDB, -6f, 0f, $"Peak Target {klip.normalizeTargetDB:F1} dB", ZUI.SliderStyle.BigSlider);
            if (EditorGUI.EndChangeCheck()) {
                klip.normalizeTargetDB = newTargetDB;
                return true;
            }
            return false;
        }
    }

}
