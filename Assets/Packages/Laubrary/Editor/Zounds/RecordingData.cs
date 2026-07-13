using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class RecordingData {

        private const int frameCount = 1024;

        public System.Action<AudioClip> onClipRecorded;

        private NativeArray<float> audioBuffer;
        private float[] recordedSamples;
        private int channels;
        private int writePosition;
        private int totalSamples;

        private AudioClip clip;

        public RecordingData(ZoundToken token, System.Action<AudioClip> onClipRecorded) {
            this.onClipRecorded = onClipRecorded;
            channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;

            int sampleRate = AudioSettings.outputSampleRate;
            totalSamples = sampleRate * channels * Mathf.CeilToInt(token.duration);

            recordedSamples = new float[totalSamples];
            audioBuffer = new NativeArray<float>(frameCount * channels, Allocator.Persistent);

            token.onFrameUpdate += RenderAudio;
            token.onComplete += StopRecording;
            AudioRenderer.Start();

            EditorUtility.DisplayProgressBar("Rendering Audio", "Start recording...", 0f);
        }

        private void RenderAudio() {
            try {
                if (writePosition < totalSamples) {
                    if (AudioRenderer.Render(audioBuffer)) {
                        int samplesToCopy = Mathf.Min(audioBuffer.Length, totalSamples - writePosition);

                        for (int i = 0; i < samplesToCopy; i++) {
                            recordedSamples[writePosition++] = audioBuffer[i];
                        }
                    }
                }
                EditorUtility.DisplayProgressBar("Rendering Audio", "Recording...", (float)writePosition / (float)totalSamples);
            }
            catch {
                EditorUtility.ClearProgressBar();
            }
        }

        private void StopRecording() {
            try {
                int sampleRate = AudioSettings.outputSampleRate;

                clip = AudioClip.Create("CapturedAudio", totalSamples / channels, channels, sampleRate, false);
                clip.SetData(recordedSamples, 0);

                onClipRecorded?.Invoke(clip);
                AudioRenderer.Stop();
                EditorUtility.ClearProgressBar();
            }
            catch {
                AudioRenderer.Stop();
                EditorUtility.ClearProgressBar();
            }
        }

    }

}
