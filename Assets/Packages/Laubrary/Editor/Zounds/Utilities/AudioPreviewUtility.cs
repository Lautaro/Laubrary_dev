using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {
    internal static class AudioPreviewUtility {
        static readonly Dictionary<EditorWindow, EditorWindow> parents = new Dictionary<EditorWindow, EditorWindow>();
        static readonly Dictionary<EditorWindow, AudioSource> sources = new Dictionary<EditorWindow, AudioSource>();

        [InitializeOnLoadMethod]
        static void HookReload() {
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
            EditorApplication.update += Sweep;
        }

        public static void PlayPreviewClip(AudioClip clip, EditorWindow owner, EditorWindow launchingOwner = null) {
            if (clip == null || owner == null) return;
            if (!sources.TryGetValue(owner, out var source) || source == null) {
                var go = new GameObject("ZoundACBrowserPreviewer") { hideFlags = HideFlags.HideAndDontSave };
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                sources[owner] = source;
            }
            if (launchingOwner != null) parents[owner] = launchingOwner; else parents.Remove(owner);
            source.Stop(); source.clip = clip; source.Play();
        }

        public static void StopPreviewClip(EditorWindow owner) {
            if (ReferenceEquals(owner, null)) return;
            var owned = new List<EditorWindow>();
            foreach (var key in sources.Keys)
                if (ReferenceEquals(key, owner) || (parents.TryGetValue(key, out var parent) && ReferenceEquals(parent, owner))) owned.Add(key);
            foreach (var key in owned) {
                var source = sources[key]; sources.Remove(key); parents.Remove(key);
                if (source != null) { source.Stop(); Object.DestroyImmediate(source.gameObject); }
            }
        }

        static void Sweep() {
            if (sources.Count == 0) return;
            var gone = new List<EditorWindow>();
            foreach (var pair in sources) if (pair.Key == null || (parents.TryGetValue(pair.Key, out var parent) && parent == null)) gone.Add(pair.Key);
            foreach (var owner in gone) StopPreviewClip(owner);
        }

        static void StopAll() {
            foreach (var source in sources.Values) if (source != null) { source.Stop(); Object.DestroyImmediate(source.gameObject); }
            sources.Clear(); parents.Clear();
        }
    }
}
