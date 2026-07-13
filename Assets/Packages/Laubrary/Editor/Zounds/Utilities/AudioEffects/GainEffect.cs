using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class GainEffect : AudioEffect {

        public override string Name => "Gain";
        public override string ToggleLabel => "Gain";

        public override bool IsEnabled(Klip klip) => klip.gainEnabled;
        public override void SetEnabled(Klip klip, bool enabled) => klip.gainEnabled = enabled;

        public override bool IsActive(Klip klip) {
            if (!klip.gainEnabled) return false;
            if (Mathf.Abs(klip.gain) < 0.0001f) return false;
            return !Mathf.Approximately(klip.gain, 1f);
        }

        public override float[] Process(float[] samples, int channels, int sampleRate, Klip klip) {
            float gain = klip.gain;
            if (Mathf.Abs(gain) < 0.0001f) gain = 1f;

            for (int i = 0; i < samples.Length; i++) {
                samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
            }
            return samples;
        }

        public override bool DrawUI(Klip klip, ref bool isDragging, AudioClip sourceClip) {
            EditorGUI.BeginChangeCheck();
            float gainDB = 20f * Mathf.Log10(Mathf.Max(klip.gain, 0.001f));
            float newGain = ZUI.Slider(klip.gain, 1f, 20f, $"Gain Boost {gainDB:F1}dB", ZUI.SliderStyle.BigSlider);
            if (EditorGUI.EndChangeCheck()) {
                klip.gain = newGain;
                return true;
            }
            return false;
        }
    }

}
