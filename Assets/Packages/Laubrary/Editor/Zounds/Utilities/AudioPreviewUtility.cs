using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Plays audio clips for AC browser previews using a dedicated AudioSource that is
    /// completely decoupled from the ZoundEngine pool. Unaffected by mute/solo state.
    /// </summary>
    internal static class AudioPreviewUtility {

        private static AudioSource s_audioSource;

        // The hidden previewer is HideAndDontSave, so it outlives a domain reload while the static
        // reference does not. Destroy it before the reload instead of leaking one per recompile.
        [InitializeOnLoadMethod]
        private static void HookReload() {
            AssemblyReloadEvents.beforeAssemblyReload += () => {
                if (s_audioSource != null) Object.DestroyImmediate(s_audioSource.gameObject);
                s_audioSource = null;
            };
        }

        private static AudioSource AudioSource {
            get {
                if (s_audioSource == null) {
                    var go = new GameObject("ZoundACBrowserPreviewer");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    s_audioSource = go.AddComponent<AudioSource>();
                    s_audioSource.playOnAwake = false;
                }
                return s_audioSource;
            }
        }

        /// <summary>
        /// Plays an audio clip through the dedicated preview AudioSource.
        /// Completely decoupled from the ZoundEngine and unaffected by mute/solo state.
        /// </summary>
        public static void PlayPreviewClip(AudioClip clip) {
            if (clip == null) return;
            var source = AudioSource;
            source.Stop();
            source.clip = clip;
            source.Play();
        }

        /// <summary>
        /// Stops the currently playing preview clip.
        /// </summary>
        public static void StopPreviewClip() {
            s_audioSource?.Stop();
        }

    }

}
