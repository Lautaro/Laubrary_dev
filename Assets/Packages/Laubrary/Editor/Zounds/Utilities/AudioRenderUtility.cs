using UnityEngine;
using UnityEditor;
using System.IO;

#if ADDRESSABLES_INSTALLED
using UnityEngine.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
#endif

namespace Laubrary.Zounds {

    public static class AudioRenderUtility {

        #region TRIM

        /// <summary>Trim a float[] sample buffer. Returns a new array with only the trimmed region.</summary>
        public static float[] Trim(float[] sourceData, int channels, int sampleRate, float startTime, float endTime, out int outSampleCount) {
            int totalSamples = sourceData.Length / channels;
            int startSample = Mathf.FloorToInt(startTime * sampleRate);
            int endSample = Mathf.Min(Mathf.FloorToInt(endTime * sampleRate), totalSamples);
            int lengthSamples = endSample - startSample;

            if (lengthSamples <= 0) {
                Debug.LogError($"[Zounds] Trim failed: start ({startTime}s) is >= end ({endTime}s).");
                outSampleCount = totalSamples;
                return sourceData;
            }

            outSampleCount = lengthSamples;
            float[] outputData = new float[lengthSamples * channels];
            System.Array.Copy(sourceData, startSample * channels, outputData, 0, lengthSamples * channels);
            return outputData;
        }

        /// <summary>AudioClip wrapper — used by Combine and legacy paths.</summary>
        public static AudioClip Trim(AudioClip clip, float startTime, float endTime) {
            if (clip == null) return null;

            int channels = clip.channels;
            int sampleRate = clip.frequency;
            float[] fullData = new float[clip.samples * channels];
            bool success = clip.GetData(fullData, 0);
            if (!success) {
                Debug.LogError($"[Zounds] Trim: GetData failed for {clip.name}");
            }

            int outSampleCount;
            float[] outputData = Trim(fullData, channels, sampleRate, startTime, endTime, out outSampleCount);

            AudioClip newClip = AudioClip.Create(clip.name + "_Trimmed", outSampleCount, channels, sampleRate, false);
            newClip.SetData(outputData, 0);
            return newClip;
        }

        #endregion

        #region AMPLITUDE

        private static float GetMaxAmplitude(float[] data, int start, int end) {
            float max = 0;
            for(int i = Mathf.Max(0, start); i < Mathf.Min(data.Length, end); i++) {
                max = Mathf.Max(max, Mathf.Abs(data[i]));
                if (max > 0.99f) break;
            }
            return max;
        }

        #endregion

        #region COMBINE

        public static AudioClip Combine(AudioClip[] clips, float[] startTimes) {
            if (clips == null || clips.Length == 0 || startTimes == null || startTimes.Length != clips.Length) {
                Debug.LogError("Invalid input: Ensure clips and startTimes array have the same length.");
                return null;
            }

            int channels = clips[0].channels;
            int sampleRate = clips[0].frequency;

            float maxDuration = 0f;
            for (int i = 0; i < clips.Length; i++) {
                float clipEndTime = startTimes[i] + clips[i].length;
                if (clipEndTime > maxDuration)
                    maxDuration = clipEndTime;
            }

            int totalSamples = Mathf.CeilToInt(maxDuration * sampleRate * channels);
            float[] outputData = new float[totalSamples];

            for (int i = 0; i < clips.Length; i++) {
                AudioClip clip = clips[i];
                int startSample = Mathf.FloorToInt(startTimes[i] * sampleRate * channels);
                float[] clipData = new float[clip.samples * channels];
                bool success = clip.GetData(clipData, 0);
                if (!success) {
                    Debug.LogError($"[Zounds] Combine: GetData failed for {clip.name}");
                }

                for (int j = 0; j < clipData.Length; j++) {
                    int targetIndex = startSample + j;
                    if (targetIndex < totalSamples) {
                        outputData[targetIndex] += clipData[j];
                    }
                }
            }

            // normalize
            float maxAmplitude = 1f;
            for (int i = 0; i < outputData.Length; i++) {
                maxAmplitude = Mathf.Max(maxAmplitude, Mathf.Abs(outputData[i]));
            }
            if (maxAmplitude > 1f) {
                for (int i = 0; i < outputData.Length; i++) {
                    outputData[i] /= maxAmplitude;
                }
            }

            AudioClip newClip = AudioClip.Create(clips[0] + "_Combined", totalSamples / channels, channels, sampleRate, false);
            newClip.SetData(outputData, 0);
            return newClip;
        }

        #endregion

        #region VOLUME-ENVELOPE

        /// <summary>Apply volume envelope to a float[] buffer in-place.</summary>
        public static void VolumeEnvelope(float[] samples, int channels, int sampleRate, int sampleCount, Envelope envelope, float startTime = 0, float endTime = 0) {
            bool useClamping = startTime != 0 || endTime != 0;

            for (int i = 0; i < sampleCount; i++) {
                float t;

                if (useClamping) {
                    float currentTime = (float)i / sampleRate;
                    float trimDuration = endTime - startTime;
                    if (currentTime < startTime || currentTime > endTime) {
                        t = -1;
                    } else {
                        t = (currentTime - startTime) / trimDuration;
                    }
                } else {
                    t = (float)i / (sampleCount - 1);
                }

                if (t >= 0 && t <= 1) {
                    float volumeFactor = envelope.Evaluate(t);
                    for (int c = 0; c < channels; c++) {
                        int index = i * channels + c;
                        samples[index] *= volumeFactor;
                    }
                }
            }
        }

        /// <summary>AudioClip wrapper — used by legacy paths.</summary>
        public static AudioClip VolumeEnvelope(AudioClip clip, Envelope envelope, float startTime = 0, float endTime = 0) {
            if (clip == null || envelope == null) return null;

            int channels = clip.channels;
            int sampleRate = clip.frequency;
            int totalSamples = clip.samples;

            float[] outputData = new float[totalSamples * channels];
            bool success = clip.GetData(outputData, 0);
            if (!success) {
                Debug.LogError($"[Zounds] VolumeEnvelope: GetData failed for {clip.name}");
            }

            VolumeEnvelope(outputData, channels, sampleRate, totalSamples, envelope, startTime, endTime);

            AudioClip newClip = AudioClip.Create(clip.name + "_VolumeEnveloped", totalSamples, channels, sampleRate, false);
            newClip.SetData(outputData, 0);
            return newClip;
        }

        #endregion

        #region PITCH-ENVELOPE

        /// <summary>
        /// Apply pitch envelope to a float[] buffer. Returns a new array (output length differs from input).
        /// Uses Catmull-Rom cubic interpolation for higher quality resampling.
        /// </summary>
        public static float[] PitchEnvelope(float[] sourceSamples, int channels, int sampleRate, int sourceSampleCount,
                                            Envelope envelope, float clipLength, out int outSampleCount,
                                            float startTime = 0, float endTime = 0) {
            bool useClamping = startTime != 0 || endTime != 0;
            float totalOutputDuration;

            if (useClamping) {
                float durationInRange = endTime - startTime;
                float outputDurationInRange = CalculateOutputDuration(durationInRange, envelope);
                totalOutputDuration = (clipLength - durationInRange) + outputDurationInRange;
            } else {
                totalOutputDuration = CalculateOutputDuration(clipLength, envelope);
            }

            outSampleCount = Mathf.CeilToInt(totalOutputDuration * sampleRate);
            if (outSampleCount <= 0) outSampleCount = 1;

            float[] outputSamples = new float[outSampleCount * channels];

            float currentSourceTime = 0f;
            float dt = 1f / sampleRate;

            for (int i = 0; i < outSampleCount; i++) {
                float pitch = 1f;

                if (useClamping) {
                    float trimDuration = endTime - startTime;
                    if (currentSourceTime >= startTime && currentSourceTime <= endTime) {
                        float t = (currentSourceTime - startTime) / trimDuration;
                        pitch = envelope.Evaluate(t);
                    }
                } else {
                    float t = currentSourceTime / clipLength;
                    pitch = envelope.Evaluate(t);
                }

                pitch = Mathf.Max(0.01f, pitch);

                float sourceSamplePos = currentSourceTime * sampleRate;

                if (sourceSamplePos >= sourceSampleCount - 1) {
                    break;
                }

                // Cubic Hermite (Catmull-Rom) interpolation for higher quality resampling
                float normalizedPos = sourceSamplePos / (float)(sourceSampleCount - 1);
                for (int c = 0; c < channels; c++) {
                    outputSamples[i * channels + c] = GetInterpolatedSample(
                        sourceSamples, normalizedPos, sourceSampleCount, channels, c);
                }

                currentSourceTime += dt * pitch;
            }

            return outputSamples;
        }

        /// <summary>AudioClip wrapper — used by legacy paths.</summary>
        public static AudioClip PitchEnvelope(AudioClip clip, Envelope envelope, float startTime = 0, float endTime = 0) {
            if (clip == null || envelope == null) return null;

            float[] sourceSamples = new float[clip.samples * clip.channels];
            bool success = clip.GetData(sourceSamples, 0);
            if (!success) {
                Debug.LogError($"[Zounds] PitchEnvelope: GetData failed for {clip.name}");
            }

            int outSampleCount;
            float[] outputSamples = PitchEnvelope(sourceSamples, clip.channels, clip.frequency, clip.samples,
                                                   envelope, clip.length, out outSampleCount, startTime, endTime);

            AudioClip newClip = AudioClip.Create(clip.name + "_PitchEnveloped", outSampleCount, clip.channels, clip.frequency, false);
            newClip.SetData(outputSamples, 0);
            return newClip;
        }

        public static float GetOutputTimeForSourceTime(float sourceTime, Envelope pitchEnvelope, float totalSourceDuration) {
            const int integrationSteps = 1000;
            float stepSize = totalSourceDuration / integrationSteps;
            float accumulatedOutputTime = 0f;

            for (int i = 0; i < integrationSteps; i++) {
                float currentSourceTime = i * stepSize;
                if (currentSourceTime >= sourceTime) break;

                float t = currentSourceTime / totalSourceDuration;
                float pitch = Mathf.Max(0.01f, pitchEnvelope.Evaluate(t));
                accumulatedOutputTime += stepSize / pitch;
            }

            return accumulatedOutputTime;
        }

        private static float CalculateOutputDuration(float sourceDuration, Envelope pitchEnvelope) {
            const int integrationSteps = 1000;
            float stepSize = 1f / integrationSteps;
            float totalTime = 0f;

            for (int i = 0; i < integrationSteps; i++) {
                float t = i * stepSize;
                float pitch = pitchEnvelope.Evaluate(t);
                pitch = Mathf.Max(pitch, 0.01f);
                totalTime += stepSize * sourceDuration / pitch;
            }

            return totalTime;
        }

        private static float GetSourceTimeForOutputTime(float outputTime, Envelope pitchEnvelope, float sourceDuration) {
            int integrationSteps = GetOptimalIntegrationSteps(sourceDuration);
            float stepSize = sourceDuration / integrationSteps;
            float accumulatedOutputTime = 0f;
            float accumulatedSourceTime = 0f;

            while (accumulatedSourceTime < sourceDuration && accumulatedOutputTime < outputTime) {
                float progress = accumulatedSourceTime / sourceDuration;
                float pitch = pitchEnvelope.Evaluate(progress);
                pitch = Mathf.Max(pitch, 0.01f);

                float segmentSourceTime = stepSize;
                float segmentOutputTime = segmentSourceTime / pitch;

                if (accumulatedOutputTime + segmentOutputTime >= outputTime) {
                    float remainingOutputTime = outputTime - accumulatedOutputTime;
                    float correspondingSourceTime = remainingOutputTime * pitch;
                    accumulatedSourceTime += correspondingSourceTime;
                    break;
                }

                accumulatedOutputTime += segmentOutputTime;
                accumulatedSourceTime += segmentSourceTime;
            }

            return Mathf.Min(accumulatedSourceTime, sourceDuration);
        }

        public static int GetOptimalIntegrationSteps(float sourceDuration) {
            return Mathf.Clamp(Mathf.CeilToInt(sourceDuration / 0.01f), 100, 10000);
        }

        /// <summary>Catmull-Rom cubic Hermite interpolation for high-quality sample lookup.</summary>
        private static float GetInterpolatedSample(float[] samples, float position, int sampleCount, int channelCount, int channel) {
            float exactSampleIndex = position * (sampleCount - 1) * channelCount + channel;
            int i = Mathf.FloorToInt(exactSampleIndex / channelCount) * channelCount + channel;
            float t = (exactSampleIndex - i) / channelCount;

            int i0 = Mathf.Max(i - channelCount, channel);
            int i1 = i;
            int i2 = Mathf.Min(i + channelCount, samples.Length - channelCount + channel);
            int i3 = Mathf.Min(i + 2 * channelCount, samples.Length - channelCount + channel);

            float p0 = samples[i0];
            float p1 = samples[i1];
            float p2 = samples[i2];
            float p3 = samples[i3];

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t
            );
        }

        #endregion

        #region BIQUAD-FILTERS

        public abstract class BiQuadFilter {
            protected float a0, a1, a2, b1, b2;
            protected float in1, in2, out1, out2;
            protected float sampleRate;

            public abstract void SetCutoffFrequency(float cutoff, float resonance);

            public float Process(float input) {
                float output = (a0 * input) + (a1 * in1) + (a2 * in2) - (b1 * out1) - (b2 * out2);

                in2 = in1;
                in1 = input;
                out2 = out1;
                out1 = output;

                return output;
            }

            public float Bypass(float input) {
                in1 = in2 = out1 = out2 = 0;
                return input;
            }
        }

        public class LowPassFilter : BiQuadFilter {
            public LowPassFilter(float sampleRate, float cutoff, float resonance = 1f) {
                this.sampleRate = sampleRate;
                SetCutoffFrequency(cutoff, resonance);
            }

            public override void SetCutoffFrequency(float cutoff, float resonance = 1f) {
                cutoff = Mathf.Clamp(cutoff, 10f, 22000f);
                float w = 2f * Mathf.PI * cutoff / sampleRate;
                float alpha = Mathf.Sin(w) / (2f * resonance);
                float cosW = Mathf.Cos(w);

                float norm = 1f / (1f + alpha);
                a0 = ((1f - cosW) / 2f) * norm;
                a1 = (1f - cosW) * norm;
                a2 = ((1f - cosW) / 2f) * norm;
                b1 = (-2f * cosW) * norm;
                b2 = (1f - alpha) * norm;
            }
        }

        public class HighPassFilter : BiQuadFilter {
            public HighPassFilter(float sampleRate, float cutoff, float resonance = 1f) {
                this.sampleRate = sampleRate;
                SetCutoffFrequency(cutoff, resonance);
            }

            public override void SetCutoffFrequency(float cutoff, float resonance = 1f) {
                cutoff = Mathf.Clamp(cutoff, 10f, 22000f);
                float w = 2f * Mathf.PI * cutoff / sampleRate;
                float alpha = Mathf.Sin(w) / (2f * resonance);
                float cosW = Mathf.Cos(w);

                float norm = 1f / (1f + alpha);
                a0 = ((1f + cosW) / 2f) * norm;
                a1 = -(1f + cosW) * norm;
                a2 = ((1f + cosW) / 2f) * norm;
                b1 = (-2f * cosW) * norm;
                b2 = (1f - alpha) * norm;
            }
        }

        public class PeakingEQFilter : BiQuadFilter {
            public PeakingEQFilter(float sampleRate, float frequency, float gainDB, float q = 1.0f) {
                this.sampleRate = sampleRate;
                SetParameters(frequency, gainDB, q);
            }

            public override void SetCutoffFrequency(float cutoff, float resonance) {
                SetParameters(cutoff, 0f, resonance);
            }

            public void SetParameters(float frequency, float gainDB, float q) {
                frequency = Mathf.Clamp(frequency, 10f, 22000f);
                float a = Mathf.Pow(10f, gainDB / 40f);
                float w = 2f * Mathf.PI * frequency / sampleRate;
                float alpha = Mathf.Sin(w) / (2f * q);
                float cosW = Mathf.Cos(w);

                float norm = 1f / (1f + alpha / a);
                a0 = (1f + alpha * a) * norm;
                a1 = (-2f * cosW) * norm;
                a2 = (1f - alpha * a) * norm;
                b1 = (-2f * cosW) * norm;
                b2 = (1f - alpha / a) * norm;
            }
        }

        #endregion

        #region GAIN

        /// <summary>Apply gain to a float[] buffer in-place.</summary>
        public static void ApplyGain(float[] samples, float gain) {
            if (Mathf.Abs(gain) < 0.0001f) gain = 1f;
            if (Mathf.Approximately(gain, 1f)) return;

            for (int i = 0; i < samples.Length; i++) {
                samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
            }
        }

        /// <summary>AudioClip wrapper — used by legacy paths.</summary>
        public static AudioClip ApplyGain(AudioClip clip, float gain) {
            if (clip == null) return null;

            if (Mathf.Abs(gain) < 0.0001f) gain = 1f;
            if (Mathf.Approximately(gain, 1f)) return clip;

            float[] samples = new float[clip.samples * clip.channels];
            bool success = clip.GetData(samples, 0);
            if (!success) {
                Debug.LogError($"[Zounds] ApplyGain: GetData failed for {clip.name}");
            }

            ApplyGain(samples, gain);

            AudioClip newClip = AudioClip.Create(clip.name + "_Gain", clip.samples, clip.channels, clip.frequency, false);
            newClip.SetData(samples, 0);
            return newClip;
        }

        #endregion

        #region SAVE

        public static AudioClip SaveAudio(AudioClip result, string filePath) {
            if (result == null) return null;

            float[] checkData = new float[result.samples * result.channels];
            result.GetData(checkData, 0);
            bool hasSound = false;
            float maxAmplitude = 0f;
            for(int i = 0; i < checkData.Length; i++) {
                float abs = Mathf.Abs(checkData[i]);
                if (abs > 0.0001f) {
                    hasSound = true;
                }
                if (abs > maxAmplitude) maxAmplitude = abs;
            }

            if (!hasSound) {
                Debug.LogError($"[Zounds] Render Alert: The generated buffer for {result.name} is COMPLETELY SILENT (Max Amp: {maxAmplitude}). Path: {filePath}");
            }

            SavWav.Save(GetAbsolutePath(filePath), result);
            AssetDatabase.ImportAsset(filePath);
            var reloaded = AssetDatabase.LoadAssetAtPath<AudioClip>(filePath);
            return reloaded;
        }

        private static string GetAbsolutePath(string assetPath) {
            if (string.IsNullOrEmpty(assetPath)) {
                Debug.LogWarning("GetAbsolutePath: assetPath is null or empty.");
                return null;
            }

            if (assetPath.StartsWith("Assets/")) {
                return Path.GetFullPath(Path.Combine(Application.dataPath, assetPath.Substring(7)));
            }
            else if (assetPath.StartsWith("Packages/")) {
                // TODO: Handle Packages/ folder
                Debug.LogError("GetAbsolutePath for Packages is not yet implemented.");
            }
            else {
                Debug.LogWarning("GetAbsolutePath: Unsupported asset path " + assetPath);
            }

            return null;
        }

        #endregion

        #region ADDRESSABLES

#if ADDRESSABLES_INSTALLED
        public static AssetReferenceT<AudioClip> GetAudioReference(AudioClip audioClip) {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) {
                Debug.Log("Please create Addressable Asset Settings first.");
                return null;
            }

            if (audioClip == null) return null;
            string path = AssetDatabase.GetAssetPath(audioClip);
            if (string.IsNullOrEmpty(path)) return null;
            var guid = AssetDatabase.GUIDFromAssetPath(path).ToString();
            AddressableAssetEntry entry = settings.FindAssetEntry(guid);

            // Every caller is about to point a Klip at this clip. A Sources clip only becomes Addressable once a Klip
            // references it (ZoundsClipClassifier), so a freshly copied or rendered clip has no entry yet; register it
            // here, in the same group the asset post-processor uses, instead of returning null for the caller to crash on.
            if (entry == null) {
                const string groupName = "Zounds Default Local Group";
                var group = settings.FindGroup(groupName) ?? settings.CreateGroup(groupName, false, false, false, null, typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema), typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));
                entry = settings.CreateOrMoveEntry(guid, group);
                if (entry == null) return null;
                entry.address = path;
                AssetDatabase.SaveAssets();
            }

            return new AssetReferenceT<AudioClip>(guid);
        }
#endif

        #endregion

        #region EQ-LEGACY

        /// <summary>
        /// Legacy AudioClip-based EQ — kept for backward compatibility.
        /// New code should use EQEffect.Process() which operates on float[] and fixes stereo crosstalk.
        /// </summary>
        public static AudioClip ApplyEqualizer(AudioClip clip, float subGain, float lowGain, float lowMidGain, float midGain, float highMidGain, float highGain, float airGain, float lpFreq, float hpFreq) {
            if (clip == null) return null;

            float[] samples = new float[clip.samples * clip.channels];
            bool success = clip.GetData(samples, 0);
            if (!success) {
                Debug.LogError($"[Zounds] ApplyEqualizer: GetData failed for {clip.name}");
            }

            int channels = clip.channels;
            float sampleRate = clip.frequency;

            // Per-channel filter instances to fix stereo crosstalk
            LowPassFilter[] lp = new LowPassFilter[channels];
            HighPassFilter[] hp = new HighPassFilter[channels];
            PeakingEQFilter[] subBand = new PeakingEQFilter[channels];
            PeakingEQFilter[] lowBand = new PeakingEQFilter[channels];
            PeakingEQFilter[] lowMidBand = new PeakingEQFilter[channels];
            PeakingEQFilter[] midBand = new PeakingEQFilter[channels];
            PeakingEQFilter[] highMidBand = new PeakingEQFilter[channels];
            PeakingEQFilter[] highBand = new PeakingEQFilter[channels];
            PeakingEQFilter[] airBand = new PeakingEQFilter[channels];

            for (int c = 0; c < channels; c++) {
                lp[c] = new LowPassFilter(sampleRate, lpFreq, 0.707f);
                hp[c] = new HighPassFilter(sampleRate, hpFreq, 0.707f);
                subBand[c] = new PeakingEQFilter(sampleRate, 60f, subGain, 0.7f);
                lowBand[c] = new PeakingEQFilter(sampleRate, 150f, lowGain, 0.8f);
                lowMidBand[c] = new PeakingEQFilter(sampleRate, 400f, lowMidGain, 1.0f);
                midBand[c] = new PeakingEQFilter(sampleRate, 1000f, midGain, 1.0f);
                highMidBand[c] = new PeakingEQFilter(sampleRate, 2500f, highMidGain, 1.0f);
                highBand[c] = new PeakingEQFilter(sampleRate, 6000f, highGain, 0.8f);
                airBand[c] = new PeakingEQFilter(sampleRate, 12000f, airGain, 0.7f);
            }

            for (int i = 0; i < clip.samples; i++) {
                for (int c = 0; c < channels; c++) {
                    int index = i * channels + c;
                    float sample = samples[index];

                    if (lpFreq < 21900f) sample = lp[c].Process(sample);
                    if (hpFreq > 20f) sample = hp[c].Process(sample);

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

            AudioClip newClip = AudioClip.Create(clip.name + "_EQ", clip.samples, channels, (int)sampleRate, false);
            newClip.SetData(samples, 0);
            return newClip;
        }

        #endregion
    }

}
