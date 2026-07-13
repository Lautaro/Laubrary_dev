using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
#if ADDRESSABLES_INSTALLED
using UnityEngine.AddressableAssets;
#endif

namespace Laubrary.Zounds {

    /// <summary>
    /// Imports an external WAV into the project's Sources folder while baking a trim
    /// (and an optional volume envelope) so only the kept region lands on disk — avoiding
    /// the source-project bloat of importing a full file when a Klip only uses a slice of it.
    ///
    /// Provenance (original file path + trim region + baked envelope) is embedded in a custom
    /// "ZSRC" RIFF chunk inside the saved WAV, so the link back to the original survives with
    /// the file itself. See <see cref="TryReadSourceMeta"/> to read it back.
    /// </summary>
    public static class ZoundsSourceImport {

        public const string ChunkId = "ZSRC";
        private const string FeatureVersion = "0.4.0";

        [Serializable]
        public class ZoundsSourceMeta {
            public string originalPath;
            public string originalName;
            public float trimStart;            // seconds into the original file
            public float trimEnd;              // seconds into the original file
            public bool volumeEnvelopeBaked;
            public string volumeEnvelopeJson;  // serialized Envelope (for full re-creatability)
            public string importedAt;          // ISO-8601
            public string zoundsVersion;
        }

        /// <summary>
        /// Extracts the [trimStart, trimEnd] region of <paramref name="src"/> and bakes the
        /// volume envelope (evaluated 0..1 across the kept region) into a new AudioClip.
        /// </summary>
        public static AudioClip BakeTrimmedClip(AudioClip src, float trimStart, float trimEnd, Envelope volumeEnvelope, bool volumeEnabled, string name) {
            if (src == null) return null;

            int ch = src.channels;
            int sr = src.frequency;
            int totalFrames = src.samples;

            float[] all = new float[totalFrames * ch];
            src.GetData(all, 0);

            int startFrame = Mathf.Clamp(Mathf.RoundToInt(trimStart * sr), 0, totalFrames);
            int endFrame = Mathf.Clamp(Mathf.RoundToInt(trimEnd * sr), startFrame, totalFrames);
            int outFrames = endFrame - startFrame;
            if (outFrames <= 0) return null;

            float[] outData = new float[outFrames * ch];
            for (int f = 0; f < outFrames; f++) {
                float gain = 1f;
                if (volumeEnabled && volumeEnvelope != null) {
                    float norm = outFrames > 1 ? (float)f / (outFrames - 1) : 0f;
                    gain = volumeEnvelope.Evaluate(norm);
                    if (gain < 0f) gain = 0f;
                }
                int srcBase = (startFrame + f) * ch;
                int dstBase = f * ch;
                for (int c = 0; c < ch; c++) {
                    outData[dstBase + c] = all[srcBase + c] * gain;
                }
            }

            var clip = AudioClip.Create(name, outFrames, ch, sr, false);
            clip.SetData(outData, 0);
            return clip;
        }

        /// <summary>
        /// Full pipeline: decode external WAV → bake trim+envelope → write to Sources/ with
        /// embedded provenance → create a Klip referencing the (already-baked) in-project source.
        /// The resulting Klip carries no further edits, so the baked file ships as-is.
        /// </summary>
        public static void ImportTrimmed(string externalPath, float trimStart, float trimEnd, Envelope volumeEnvelope, string nameOverride, Action<Klip> onKlipAdded) {
            var src = WavDecoder.LoadFromDisk(externalPath);
            if (src == null) {
                EditorUtility.DisplayDialog("Import Failed", "Could not decode WAV file:\n" + externalPath, "OK");
                return;
            }

            bool volEnabled = volumeEnvelope != null && volumeEnvelope.enabled;
            string baseName = string.IsNullOrEmpty(nameOverride)
                ? Path.GetFileNameWithoutExtension(externalPath)
                : nameOverride;

            var baked = BakeTrimmedClip(src, trimStart, trimEnd, volumeEnvelope, volEnabled, baseName);
            if (baked == null) {
                EditorUtility.DisplayDialog("Import Failed", "The trimmed region is empty.", "OK");
                return;
            }

            var projectSettings = ZoundsProject.Instance.projectSettings;
            string sourcesFolder = projectSettings.sourcesFolderPath;
            if (string.IsNullOrEmpty(sourcesFolder)) {
                EditorUtility.DisplayDialog("Import Failed", "No Sources folder is configured in the project settings.", "OK");
                return;
            }
            if (!Directory.Exists(sourcesFolder)) {
                Directory.CreateDirectory(sourcesFolder);
                AssetDatabase.Refresh();
            }

            string destRel = AssetDatabase.GenerateUniqueAssetPath(sourcesFolder + "/" + baseName + ".wav");
            string destAbs = Path.Combine(Application.dataPath, destRel.Substring("Assets/".Length));

            var meta = new ZoundsSourceMeta {
                originalPath = externalPath,
                originalName = Path.GetFileNameWithoutExtension(externalPath),
                trimStart = trimStart,
                trimEnd = trimEnd,
                volumeEnvelopeBaked = volEnabled,
                volumeEnvelopeJson = volEnabled ? JsonUtility.ToJson(volumeEnvelope) : "",
                importedAt = DateTime.Now.ToString("o"),
                zoundsVersion = FeatureVersion,
            };
            byte[] metaBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(meta));

            SavWav.Save(destAbs, baked, metaBytes, ChunkId);
            AssetDatabase.ImportAsset(destRel, ImportAssetOptions.ForceSynchronousImport);

            var importedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(destRel);
            if (importedClip == null) {
                EditorUtility.DisplayDialog("Import Failed", "Unity could not import the baked WAV:\n" + destRel, "OK");
                return;
            }

#if ADDRESSABLES_INSTALLED
            var audioRef = new AssetReferenceT<AudioClip>(AssetDatabase.AssetPathToGUID(destRel));
            ZoundsWindow.ModifyZoundsProject("import trimmed external file", () => {
                var newKlip = new Klip(ZoundLibrary.GetUniqueZoundId());
                newKlip.audioClipRef = audioRef;
                newKlip.name = ZoundDictionary.EnsureUniqueZoundName(baseName);
                // The file on disk is already trimmed + enveloped, so the Klip itself has no
                // active edits — output = byte-copy of this source, nothing to re-render.
                newKlip.trimEnabled = false;
                newKlip.trimStart = 0f;
                newKlip.trimEnd = importedClip.length;
                newKlip.volumeEnvelope = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
                newKlip.pitchEnvelope = new Envelope(Zound.MinPitchRange, Zound.MaxPitchRange);
                newKlip.externalSourcePath = ""; // It's an in-project source now.
                if (ZoundEngine.IsInitialized()) ZoundDictionary.ValidateZoundRuntime(newKlip);
                onKlipAdded?.Invoke(newKlip);
            }, true);
#else
            Debug.LogError("[Zounds] Addressables is required to create a Klip from the imported source.");
#endif
        }

        /// <summary>
        /// Reads the embedded "ZSRC" provenance chunk from a WAV on disk, if present.
        /// </summary>
        public static bool TryReadSourceMeta(string absolutePath, out ZoundsSourceMeta meta) {
            meta = null;
            try {
                if (!File.Exists(absolutePath)) return false;
                byte[] wav = File.ReadAllBytes(absolutePath);
                if (wav.Length < 12) return false;
                if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') return false;

                int pos = 12; // after "WAVE"
                while (pos + 8 <= wav.Length) {
                    string id = "" + (char)wav[pos] + (char)wav[pos + 1] + (char)wav[pos + 2] + (char)wav[pos + 3];
                    int size = BitConverter.ToInt32(wav, pos + 4);
                    if (id == ChunkId) {
                        int payloadStart = pos + 8;
                        if (size < 0 || payloadStart + size > wav.Length) size = wav.Length - payloadStart;
                        string json = Encoding.UTF8.GetString(wav, payloadStart, size);
                        meta = JsonUtility.FromJson<ZoundsSourceMeta>(json);
                        return meta != null;
                    }
                    pos += 8 + size;
                    if ((size & 1) == 1) pos++; // word alignment
                }
            }
            catch (Exception e) {
                Debug.LogWarning("[Zounds] Failed to read source metadata from " + absolutePath + ": " + e.Message);
            }
            return false;
        }
    }
}
