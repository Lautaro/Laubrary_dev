using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Laubrary.ZTracker.Editor {
    /// <summary>Loads one external drum sample asynchronously for audition without importing it.</summary>
    public sealed class ZTrackerDrumPreview : IDisposable {
        UnityWebRequest request;
        AudioClip cachedClip;
        Action<AudioClip> onReady;
        Action<string> onError;
        bool disposed;

        /// <summary>Starts or replaces the pending preview. The returned clip is owned by this instance.</summary>
        public void Preview(string path, Action<AudioClip> ready, Action<string> error = null) {
            if (disposed) {
                error?.Invoke("This drum preview has been disposed.");
                return;
            }
            CancelPending();
            ClearClip();
            onReady = ready;
            onError = error;
            if (string.IsNullOrWhiteSpace(path)) {
                Fail("No audio file was selected.");
                return;
            }

            try {
                string fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) {
                    Fail("Audio file not found: " + fullPath);
                    return;
                }
                AudioType type = GetAudioType(Path.GetExtension(fullPath));
                request = UnityWebRequestMultimedia.GetAudioClip(new Uri(fullPath).AbsoluteUri, type);
                request.SendWebRequest();
                EditorApplication.update += Poll;
            }
            catch (Exception e) {
                EditorApplication.update -= Poll;
                if (request != null) {
                    try { request.Abort(); } catch { }
                    request.Dispose();
                    request = null;
                }
                Fail(e.Message);
            }
        }

        /// <summary>Cancels a pending request. A previously loaded clip remains available until replaced or disposed.</summary>
        public void Cancel() => CancelPending();

        /// <summary>Stops loading and destroys the owned preview clip.</summary>
        public void Dispose() {
            if (disposed) return;
            disposed = true;
            CancelPending();
            ClearClip();
            onReady = null;
            onError = null;
        }

        void Poll() {
            if (request == null || !request.isDone) return;
            EditorApplication.update -= Poll;
            var completed = request;
            request = null;
            if (completed.result != UnityWebRequest.Result.Success) {
                string message = completed.error ?? "Unity could not decode this audio file.";
                completed.Dispose();
                Fail(message);
                return;
            }

            try {
                cachedClip = DownloadHandlerAudioClip.GetContent(completed);
                completed.Dispose();
                if (cachedClip == null) {
                    Fail("Unity downloaded the file but did not decode an AudioClip.");
                    return;
                }
                cachedClip.hideFlags = HideFlags.HideAndDontSave;
                var callback = onReady;
                onReady = null;
                onError = null;
                callback?.Invoke(cachedClip);
            }
            catch (Exception e) {
                completed.Dispose();
                Fail(e.Message);
            }
        }

        void CancelPending() {
            EditorApplication.update -= Poll;
            onReady = null;
            onError = null;
            if (request == null) return;
            try { request.Abort(); } catch { }
            request.Dispose();
            request = null;
        }

        void ClearClip() {
            if (cachedClip == null) return;
            UnityEngine.Object.DestroyImmediate(cachedClip);
            cachedClip = null;
        }

        void Fail(string message) {
            var callback = onError;
            onReady = null;
            onError = null;
            callback?.Invoke(message);
        }

        static AudioType GetAudioType(string extension) {
            switch ((extension ?? string.Empty).ToLowerInvariant()) {
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".mp3": return AudioType.MPEG;
                case ".aiff":
                case ".aif": return AudioType.AIFF;
                default: throw new NotSupportedException("Unsupported preview format: " + extension);
            }
        }
    }
}
