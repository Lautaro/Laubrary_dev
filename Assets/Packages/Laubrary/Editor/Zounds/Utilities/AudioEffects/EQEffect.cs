using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class EQEffect : AudioEffect {

        public override string Name => "EQ";
        public override string ToggleLabel => "EQ";

        public override bool IsEnabled(Klip klip) => klip.eqEnabled;
        public override void SetEnabled(Klip klip, bool enabled) => klip.eqEnabled = enabled;

        public override bool IsActive(Klip klip) {
            if (!klip.eqEnabled) return false;
            return Mathf.Abs(klip.subGain) > 0.1f ||
                   Mathf.Abs(klip.lowGain) > 0.1f ||
                   Mathf.Abs(klip.lowMidGain) > 0.1f ||
                   Mathf.Abs(klip.midGain) > 0.1f ||
                   Mathf.Abs(klip.highMidGain) > 0.1f ||
                   Mathf.Abs(klip.highGain) > 0.1f ||
                   Mathf.Abs(klip.airGain) > 0.1f ||
                   klip.lpFrequency < 21900f ||
                   klip.hpFrequency > 20f;
        }

        public override float[] Process(float[] samples, int channels, int sampleRate, Klip klip) {
            int sampleCount = samples.Length / channels;

            var lp = new AudioRenderUtility.LowPassFilter[channels];
            var hp = new AudioRenderUtility.HighPassFilter[channels];
            var subBand = new AudioRenderUtility.PeakingEQFilter[channels];
            var lowBand = new AudioRenderUtility.PeakingEQFilter[channels];
            var lowMidBand = new AudioRenderUtility.PeakingEQFilter[channels];
            var midBand = new AudioRenderUtility.PeakingEQFilter[channels];
            var highMidBand = new AudioRenderUtility.PeakingEQFilter[channels];
            var highBand = new AudioRenderUtility.PeakingEQFilter[channels];
            var airBand = new AudioRenderUtility.PeakingEQFilter[channels];

            for (int c = 0; c < channels; c++) {
                lp[c] = new AudioRenderUtility.LowPassFilter(sampleRate, klip.lpFrequency, 0.707f);
                hp[c] = new AudioRenderUtility.HighPassFilter(sampleRate, klip.hpFrequency, 0.707f);
                subBand[c] = new AudioRenderUtility.PeakingEQFilter(sampleRate, 60f, klip.subGain, 0.7f);
                lowBand[c] = new AudioRenderUtility.PeakingEQFilter(sampleRate, 150f, klip.lowGain, 0.8f);
                lowMidBand[c] = new AudioRenderUtility.PeakingEQFilter(sampleRate, 400f, klip.lowMidGain, 1.0f);
                midBand[c] = new AudioRenderUtility.PeakingEQFilter(sampleRate, 1000f, klip.midGain, 1.0f);
                highMidBand[c] = new AudioRenderUtility.PeakingEQFilter(sampleRate, 2500f, klip.highMidGain, 1.0f);
                highBand[c] = new AudioRenderUtility.PeakingEQFilter(sampleRate, 6000f, klip.highGain, 0.8f);
                airBand[c] = new AudioRenderUtility.PeakingEQFilter(sampleRate, 12000f, klip.airGain, 0.7f);
            }

            bool applyLP = klip.lpFrequency < 21900f;
            bool applyHP = klip.hpFrequency > 20f;

            for (int i = 0; i < sampleCount; i++) {
                for (int c = 0; c < channels; c++) {
                    int index = i * channels + c;
                    float sample = samples[index];

                    if (applyLP) sample = lp[c].Process(sample);
                    if (applyHP) sample = hp[c].Process(sample);

                    sample = subBand[c].Process(sample);
                    sample = lowBand[c].Process(sample);
                    sample = lowMidBand[c].Process(sample);
                    sample = midBand[c].Process(sample);
                    sample = highMidBand[c].Process(sample);
                    sample = highBand[c].Process(sample);
                    sample = airBand[c].Process(sample);

                    samples[index] = sample;
                }
            }

            return samples;
        }

        private static GUIStyle s_centeredMiniLabel;
        private static GUIStyle CenteredMiniLabel =>
            s_centeredMiniLabel ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };

        public override bool DrawUI(Klip klip, ref bool isDragging, AudioClip sourceClip) {
            EditorGUI.BeginChangeCheck();

            // EQ Curve Visualization
            Rect curveRect = GUILayoutUtility.GetRect(10, 80f, GUILayout.ExpandWidth(true));
            KlipEditorWindow.DrawEQCurve(curveRect, klip);

            GUILayout.Space(5f);

            float newHpFreq = klip.hpFrequency;
            float newLpFreq = klip.lpFrequency;
            float newSubGain = klip.subGain;
            float newLowGain = klip.lowGain;
            float newLowMidGain = klip.lowMidGain;
            float newMidGain = klip.midGain;
            float newHighMidGain = klip.highMidGain;
            float newHighGain = klip.highGain;
            float newAirGain = klip.airGain;

            // EQ bands + filter layout: [LP Filter | 7 bands | HP Filter]
            const float k_BandSliderH = 150f;
            const float k_BandLabelH  = 17f;
            const float k_BandValueH  = 17f;
            const float k_BandColH    = k_BandSliderH + k_BandLabelH + k_BandValueH;

            float filterLabelH  = EditorGUIUtility.singleLineHeight;
            float filterSliderH = EditorGUIUtility.singleLineHeight + 2f;
            float filterH       = filterLabelH + filterSliderH;

            Rect blockRect = GUILayoutUtility.GetRect(10f, k_BandColH, GUILayout.ExpandWidth(true));

            float thirdW  = blockRect.width / 3f;
            var   lpRect  = new Rect(blockRect.x,               blockRect.y, thirdW, blockRect.height);
            var   midRect = new Rect(blockRect.x + thirdW,      blockRect.y, thirdW, blockRect.height);
            var   hpRect  = new Rect(blockRect.x + thirdW * 2f, blockRect.y, thirdW, blockRect.height);

            var style = CenteredMiniLabel;
            float bandW = midRect.width / 7f;
            newSubGain     = KlipEditorWindow.DrawEQBandSlider(new Rect(midRect.x + bandW * 0f, midRect.y, bandW, midRect.height), "Sub",   klip.subGain, style);
            newLowGain     = KlipEditorWindow.DrawEQBandSlider(new Rect(midRect.x + bandW * 1f, midRect.y, bandW, midRect.height), "Low",   klip.lowGain, style);
            newLowMidGain  = KlipEditorWindow.DrawEQBandSlider(new Rect(midRect.x + bandW * 2f, midRect.y, bandW, midRect.height), "L-Mid", klip.lowMidGain, style);
            newMidGain     = KlipEditorWindow.DrawEQBandSlider(new Rect(midRect.x + bandW * 3f, midRect.y, bandW, midRect.height), "Mid",   klip.midGain, style);
            newHighMidGain = KlipEditorWindow.DrawEQBandSlider(new Rect(midRect.x + bandW * 4f, midRect.y, bandW, midRect.height), "H-Mid", klip.highMidGain, style);
            newHighGain    = KlipEditorWindow.DrawEQBandSlider(new Rect(midRect.x + bandW * 5f, midRect.y, bandW, midRect.height), "High",  klip.highGain, style);
            newAirGain     = KlipEditorWindow.DrawEQBandSlider(new Rect(midRect.x + bandW * 6f, midRect.y, bandW, midRect.height), "Air",   klip.airGain, style);

            float filterOffsetY = (k_BandColH - filterH) * 0.5f;
            var lpFilterRect = new Rect(lpRect.x + 4f,  lpRect.y  + filterOffsetY, lpRect.width  - 8f, filterH);
            var hpFilterRect = new Rect(hpRect.x + 4f,  hpRect.y  + filterOffsetY, hpRect.width  - 8f, filterH);

            newLpFreq = KlipEditorWindow.DrawFilterSlider(lpFilterRect, "Low Pass Filter",  klip.lpFrequency, 100f,  22000f, 22000f, style);
            newHpFreq = KlipEditorWindow.DrawFilterSlider(hpFilterRect, "High Pass Filter", klip.hpFrequency, 10f,   10000f, 10f, style);

            if (EditorGUI.EndChangeCheck()) {
                klip.hpFrequency   = newHpFreq;
                klip.lpFrequency   = newLpFreq;
                klip.subGain       = newSubGain;
                klip.lowGain       = newLowGain;
                klip.lowMidGain    = newLowMidGain;
                klip.midGain       = newMidGain;
                klip.highMidGain   = newHighMidGain;
                klip.highGain      = newHighGain;
                klip.airGain       = newAirGain;
                return true;
            }

            GUILayout.Space(3f);

            // Clear button — uses ModifyAndSaveZoundsProject for proper undo
            if (ZUI.Button("Clear EQ", ZUI.Style.Default, ZUICornerMask.All, GUILayout.Height(18f), GUILayout.Width(70f))) {
                ZoundsWindow.ModifyAndSaveZoundsProject("clear klip eq", () => {
                    klip.subGain = 0f;
                    klip.lowGain = 0f;
                    klip.lowMidGain = 0f;
                    klip.midGain = 0f;
                    klip.highMidGain = 0f;
                    klip.highGain = 0f;
                    klip.airGain = 0f;
                    klip.lpFrequency = 22000f;
                    klip.hpFrequency = 10f;
                    klip.needsRender = true;
                });
            }

            return false;
        }
    }

}
