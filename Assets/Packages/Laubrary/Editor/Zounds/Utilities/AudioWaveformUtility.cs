using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Laubrary.Zounds {

    public static class AudioWaveformUtility {

        private static readonly Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, int> sourceClipIdCache = new Dictionary<string, int>();


        public static void ClearCache() {
            textureCache.Clear();
        }

        public static void ClearCache(AudioClip clip) {
            if (clip == null) return;
            string key = clip.GetInstanceID().ToString();
            if (textureCache.ContainsKey(key)) {
                textureCache.Remove(key);
            }
        }

        public static void ClearCache(Klip klip) {
            if (klip == null) return;
            string key = klip.id.ToString();
            if (textureCache.ContainsKey(key)) {
                textureCache.Remove(key);
            }
            if (sourceClipIdCache.ContainsKey(key)) {
                sourceClipIdCache.Remove(key);
            }
        }


        public static readonly Vector2 playerHeadSize = new Vector2(6f, 8f);
        public static Color playerHeadColor => ZoundsProject.Instance.projectSettings.editorStyle.playerHeadColor;
        public static float playerHeadThickness => ZoundsProject.Instance.projectSettings.editorStyle.playerHeadThickness;

        private static Texture m_playerHeadTexture;
        public static Texture playerHeadTexture {
            get {
                if (m_playerHeadTexture == null) {
                    m_playerHeadTexture = Resources.Load("AudioSpectrumIcons/TimeCursor") as Texture;
                }
                return m_playerHeadTexture;
            }
        }


        public static bool HasCachedTexture(string key) {
            return textureCache.ContainsKey(key) && textureCache[key] != null;
        }

        /// <summary>
        /// Get a waveform texture of the specified audio clip.
        /// </summary>
        /// <param name="audioClip"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="_color"></param>
        /// <returns></returns>
        public static Texture2D GetWaveformSpectrumTexture(AudioClip audioClip, int width, int height, Color _color, string customKey = null) {
            Texture2D tex;
            bool createNew = false;
            string key = string.IsNullOrEmpty(customKey) ? audioClip.GetInstanceID().ToString() : customKey;

            if (textureCache.TryGetValue(key, out tex)) {
                if (tex == null && !ReferenceEquals(tex, null)) {
                    createNew = true;
                }
                else if (tex.width != width || tex.height != height) {
                    createNew = true;
                }
                // If the key is a Klip ID, check if the underlying memory clip instance changed
                else if (sourceClipIdCache.TryGetValue(key, out int lastId) && lastId != audioClip.GetInstanceID()) {
                    // Debug.Log($"[WaveUtility] Forcing Re-render for Key {key}: AudioClip Instance ID changed from {lastId} to {audioClip.GetInstanceID()}");
                    createNew = true;
                }
            }
            else {
                createNew = true;
            }

            if (createNew) {
                // Debug.Log($"[WaveUtility] Generating New Texture for Key {key} using Clip {audioClip.name}");
                bool hq = ZoundsProject.Instance?.browserSettings?.highQualityWaveform ?? false;
                tex = hq
                    ? CreateNewTextureHQ(audioClip, width, height, _color, key)
                    : CreateNewTexture(audioClip, width, height, _color, key);
            }

            return tex;
        }

        private static Texture2D CreateNewTexture(AudioClip audioClip, int width, int height, Color color, string key) {
            if (width < 1 || height < 1) {
                return null;
            }
            try {
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                //int sampleCount = _audioClip.samples * _audioClip.channels;
                int sampleCount = (int)(audioClip.length * audioClip.frequency) * audioClip.channels;
                float[] samples = new float[sampleCount];
                float[] waveform = new float[width];
                audioClip.GetData(samples, 0);
                int packSize = (sampleCount / width) + 1;
                int s = 0;
                for (int i = 0; i < sampleCount; i += packSize) {
                    waveform[s] = Mathf.Abs(samples[i]);
                    s++;
                }

                for (int x = 0; x < width; x++) {
                    for (int y = 0; y < height; y++) {
                        tex.SetPixel(x, y, new Color(0, 0, 0, 0));
                    }
                }

                for (int x = 0; x < waveform.Length; x++) {
                    for (int y = 0; y <= waveform[x] * ((float)height * .75f); y++) {
                        tex.SetPixel(x, (height / 2) + y, color);
                        tex.SetPixel(x, (height / 2) - y, color);
                    }
                }
                tex.Apply();
                if (textureCache.ContainsKey(key)) {
                    textureCache[key] = tex;
                }
                else {
                    textureCache.Add(key, tex);
                }
                
                // Track the instance ID for this key
                sourceClipIdCache[key] = audioClip.GetInstanceID();

                return tex;
            }
            catch (System.Exception e) {
                Debug.LogError(e);
                return null;
            }
        }

        // High-quality variant: peak-tracking downsampling + Bilinear filter for smooth scaling.
        private static Texture2D CreateNewTextureHQ(AudioClip audioClip, int width, int height, Color color, string key) {
            if (width < 1 || height < 1) return null;
            try {
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;

                int sampleCount = (int)(audioClip.length * audioClip.frequency) * audioClip.channels;
                float[] samples = new float[sampleCount];
                float[] waveform = new float[width];
                audioClip.GetData(samples, 0);

                int packSize = Mathf.Max(1, sampleCount / width);
                for (int x = 0; x < width; x++) {
                    int start = x * packSize;
                    int end   = Mathf.Min(start + packSize, sampleCount);
                    float peak = 0f;
                    for (int i = start; i < end; i++) {
                        float abs = Mathf.Abs(samples[i]);
                        if (abs > peak) peak = abs;
                    }
                    waveform[x] = peak;
                }

                for (int x = 0; x < width; x++)
                    for (int y = 0; y < height; y++)
                        tex.SetPixel(x, y, new Color(0, 0, 0, 0));

                for (int x = 0; x < waveform.Length; x++) {
                    for (int y = 0; y <= waveform[x] * ((float)height * .75f); y++) {
                        tex.SetPixel(x, (height / 2) + y, color);
                        tex.SetPixel(x, (height / 2) - y, color);
                    }
                }
                tex.Apply();

                if (textureCache.ContainsKey(key)) textureCache[key] = tex;
                else textureCache.Add(key, tex);
                sourceClipIdCache[key] = audioClip.GetInstanceID();
                return tex;
            }
            catch (System.Exception e) {
                Debug.LogError(e);
                return null;
            }
        }

        /// <summary>
        /// A helper function to immediately draw the waveform texture into a rect.
        /// </summary>
        /// <param name="rect"></param>
        /// <param name="audioClip"></param>
        /// <param name="backgroundColor"></param>
        /// <param name="waveformColor"></param>
        public static void DrawWaveformRect(Rect rect, AudioClip audioClip, Color backgroundColor, Color waveformColor, string customKey = null) {
            var guiColor = GUI.color;
            GUI.color = backgroundColor;
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;
            var audioTexture = GetWaveformSpectrumTexture(audioClip, Mathf.FloorToInt(rect.width), Mathf.FloorToInt(rect.height), waveformColor, customKey);
            if (audioTexture != null) {
                GUI.DrawTexture(rect, audioTexture);
            }
        }

        public static void DrawPlayerHead(Rect rect, float timePercentage, Color color) {
            var guiColor = GUI.color;
            GUI.color = color;
            float posX = timePercentage * rect.width;
            GUI.DrawTexture(new Rect(rect.x + posX - playerHeadSize.x / 2f, rect.y, playerHeadSize.x, playerHeadSize.x * 1.82f), playerHeadTexture);
            
            var handlesColor = Handles.color;
            Handles.color = color;
            Handles.BeginGUI();
            float halfThickness = playerHeadThickness * 0.5f;
            float centerX = rect.x + posX;
            
            for (float offset = -halfThickness; offset < halfThickness; offset += 0.5f) {
                float xPos = centerX + offset;
                Handles.DrawLine(new Vector3(xPos, rect.y + playerHeadSize.y, 0), new Vector3(xPos, rect.yMax, 0));
            }
            
            Handles.EndGUI();
            Handles.color = handlesColor;
            GUI.color = guiColor;
        }

        public static void DrawPlayerHead(Rect rect, float timePercentage) {
            DrawPlayerHead(rect, timePercentage, playerHeadColor);
        }

    }

}