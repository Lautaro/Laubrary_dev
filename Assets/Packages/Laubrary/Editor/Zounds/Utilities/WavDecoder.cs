using System;
using System.IO;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Reads WAV files from absolute disk paths outside the Unity project.
    /// Supports PCM 16-bit, 24-bit, and 32-bit float WAVs (mono and stereo).
    /// Returns a Unity AudioClip for use in the editor (waveform preview, rendering).
    /// </summary>
    public static class WavDecoder {

        public static AudioClip LoadFromDisk(string absolutePath) {
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                return null;

            byte[] fileBytes = File.ReadAllBytes(absolutePath);
            return Decode(fileBytes, Path.GetFileNameWithoutExtension(absolutePath));
        }

        private static AudioClip Decode(byte[] wav, string clipName) {
            if (wav.Length < 44) return null;

            // Verify RIFF header.
            if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') return null;
            if (wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E') return null;

            // Walk chunks to find "fmt " and "data".
            int fmtOffset = -1;
            int dataOffset = -1;
            int dataSize = 0;

            int pos = 12; // After "WAVE"
            while (pos + 8 <= wav.Length) {
                string chunkId = "" + (char)wav[pos] + (char)wav[pos + 1] + (char)wav[pos + 2] + (char)wav[pos + 3];
                int chunkSize = BitConverter.ToInt32(wav, pos + 4);

                if (chunkId == "fmt ") {
                    fmtOffset = pos + 8;
                }
                else if (chunkId == "data") {
                    dataOffset = pos + 8;
                    dataSize = chunkSize;
                }

                pos += 8 + chunkSize;
                // Chunks are word-aligned.
                if (chunkSize % 2 != 0) pos++;
            }

            if (fmtOffset < 0 || dataOffset < 0) return null;

            // Parse fmt chunk.
            ushort audioFormat = BitConverter.ToUInt16(wav, fmtOffset);       // 1 = PCM, 3 = IEEE float
            ushort channels = BitConverter.ToUInt16(wav, fmtOffset + 2);
            int sampleRate = BitConverter.ToInt32(wav, fmtOffset + 4);
            ushort bitsPerSample = BitConverter.ToUInt16(wav, fmtOffset + 14);

            bool isPCM = audioFormat == 1;
            bool isFloat = audioFormat == 3;

            if (!isPCM && !isFloat) {
                Debug.LogWarning($"[WavDecoder] Unsupported audio format {audioFormat} in '{clipName}'. Only PCM and IEEE float are supported.");
                return null;
            }

            int bytesPerSample = bitsPerSample / 8;
            int totalSamples = dataSize / bytesPerSample; // Total across all channels.
            int samplesPerChannel = totalSamples / channels;

            float[] audioData = new float[totalSamples];
            int readEnd = Math.Min(dataOffset + dataSize, wav.Length);

            int sampleIndex = 0;
            int bytePos = dataOffset;

            if (isPCM && bitsPerSample == 16) {
                while (bytePos + 1 < readEnd && sampleIndex < audioData.Length) {
                    short sample = BitConverter.ToInt16(wav, bytePos);
                    audioData[sampleIndex++] = sample / 32768f;
                    bytePos += 2;
                }
            }
            else if (isPCM && bitsPerSample == 24) {
                while (bytePos + 2 < readEnd && sampleIndex < audioData.Length) {
                    // 24-bit signed: read 3 bytes, sign-extend to int.
                    int sample = wav[bytePos] | (wav[bytePos + 1] << 8) | (wav[bytePos + 2] << 16);
                    if ((sample & 0x800000) != 0) sample |= unchecked((int)0xFF000000); // Sign extend.
                    audioData[sampleIndex++] = sample / 8388608f;
                    bytePos += 3;
                }
            }
            else if (isPCM && bitsPerSample == 32) {
                while (bytePos + 3 < readEnd && sampleIndex < audioData.Length) {
                    int sample = BitConverter.ToInt32(wav, bytePos);
                    audioData[sampleIndex++] = sample / 2147483648f;
                    bytePos += 4;
                }
            }
            else if (isFloat && bitsPerSample == 32) {
                while (bytePos + 3 < readEnd && sampleIndex < audioData.Length) {
                    audioData[sampleIndex++] = BitConverter.ToSingle(wav, bytePos);
                    bytePos += 4;
                }
            }
            else {
                Debug.LogWarning($"[WavDecoder] Unsupported bit depth: {bitsPerSample}-bit {(isFloat ? "float" : "PCM")} in '{clipName}'.");
                return null;
            }

            AudioClip clip = AudioClip.Create(clipName, samplesPerChannel, channels, sampleRate, false);
            clip.SetData(audioData, 0);
            return clip;
        }
    }
}
