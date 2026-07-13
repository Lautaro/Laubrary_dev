using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class CompressionEffect : AudioEffect {

        public override string Name => "Compression";
        public override string ToggleLabel => "Compress";

        public override bool IsEnabled(Klip klip) => klip.compressionEnabled;
        public override void SetEnabled(Klip klip, bool enabled) => klip.compressionEnabled = enabled;

        public override bool IsActive(Klip klip) {
            return klip.compressionEnabled;
        }

        public override float[] Process(float[] samples, int channels, int sampleRate, Klip klip) {
            float thresholdDB = klip.compThreshold;
            float ratio = Mathf.Max(klip.compRatio, 1f);
            float attackMs = Mathf.Max(klip.compAttack, 0.1f);
            float releaseMs = Mathf.Max(klip.compRelease, 1f);
            float makeupDB = klip.compMakeupGain;

            int sampleCount = samples.Length / channels;

            float attackCoeff = Mathf.Exp(-1f / (attackMs * 0.001f * sampleRate));
            float releaseCoeff = Mathf.Exp(-1f / (releaseMs * 0.001f * sampleRate));

            float thresholdLin = Mathf.Pow(10f, thresholdDB / 20f);
            float makeupLin = Mathf.Pow(10f, makeupDB / 20f);

            float[] gainReduction = new float[sampleCount];
            float envelope = 0f;

            for (int i = 0; i < sampleCount; i++) {
                float peak = 0f;
                for (int c = 0; c < channels; c++) {
                    float abs = Mathf.Abs(samples[i * channels + c]);
                    if (abs > peak) peak = abs;
                }

                if (peak > envelope)
                    envelope = attackCoeff * envelope + (1f - attackCoeff) * peak;
                else
                    envelope = releaseCoeff * envelope + (1f - releaseCoeff) * peak;

                if (envelope > thresholdLin && thresholdLin > 0f) {
                    float envelopeDB = 20f * Mathf.Log10(envelope);
                    float overDB = envelopeDB - thresholdDB;
                    float reductionDB = overDB * (1f - 1f / ratio);
                    gainReduction[i] = Mathf.Pow(10f, -reductionDB / 20f);
                } else {
                    gainReduction[i] = 1f;
                }
            }

            for (int i = 0; i < sampleCount; i++) {
                float gr = gainReduction[i] * makeupLin;
                for (int c = 0; c < channels; c++) {
                    int idx = i * channels + c;
                    samples[idx] = Mathf.Clamp(samples[idx] * gr, -1f, 1f);
                }
            }

            return samples;
        }

        public override bool DrawUI(Klip klip, ref bool isDragging, AudioClip sourceClip) {
            EditorGUI.BeginChangeCheck();
            float newThreshold = ZUI.Slider(klip.compThreshold, -60f, 0f, $"Threshold {klip.compThreshold:F1} dB", ZUI.SliderStyle.BigSlider);
            float newRatio = ZUI.Slider(klip.compRatio, 1f, 20f, $"Ratio {klip.compRatio:F1}:1", ZUI.SliderStyle.BigSlider);
            float newAttack = ZUI.Slider(klip.compAttack, 0.1f, 100f, $"Attack {klip.compAttack:F1} ms", ZUI.SliderStyle.BigSlider);
            float newRelease = ZUI.Slider(klip.compRelease, 10f, 1000f, $"Release {klip.compRelease:F0} ms", ZUI.SliderStyle.BigSlider);
            float newMakeup = ZUI.Slider(klip.compMakeupGain, 0f, 24f, $"Makeup {klip.compMakeupGain:F1} dB", ZUI.SliderStyle.BigSlider);
            if (EditorGUI.EndChangeCheck()) {
                klip.compThreshold = newThreshold;
                klip.compRatio = newRatio;
                klip.compAttack = newAttack;
                klip.compRelease = newRelease;
                klip.compMakeupGain = newMakeup;
                return true;
            }
            return false;
        }
    }

}
