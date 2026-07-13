using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Laubrary.Zounds {

    [System.Serializable]
    public class ZoundPool {

        [SerializeField] private List<AudioSource> sourcePool = new List<AudioSource>();
        [SerializeField] private List<AudioSource> allAudioSources = new List<AudioSource>();

        public AudioSource RequestAudioSource() {
            AudioSource audioSource = null;
            if (sourcePool.Count > 0) {
                audioSource = sourcePool.Last();
                sourcePool.RemoveAt(sourcePool.Count - 1);
            }

            if (audioSource == null) {
                var go = new GameObject("ZoundSource");
                go.transform.parent = ZoundEngine.Instance.transform;
                audioSource = go.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                allAudioSources.Add(audioSource);
            }

            if (audioSource != null) {
                audioSource.gameObject.SetActive(true);
                audioSource.enabled = true;
                audioSource.transform.parent = ZoundEngine.Instance.transform;
                audioSource.mute = false;
                audioSource.volume = 1f;
                audioSource.pitch = 1f;
                audioSource.outputAudioMixerGroup = null;
                audioSource.playOnAwake = false;
            }

            return audioSource;
        }

        public void ReturnAudioSource(AudioSource audioSource) {
            if (audioSource != null) {
                audioSource.Stop();
                audioSource.clip = null;
                audioSource.gameObject.SetActive(false);
            }
            if (!sourcePool.Contains(audioSource)) {
                sourcePool.Add(audioSource);
            }
        }

        private HashSet<AudioSource> tempAudioSourceSet = new HashSet<AudioSource>();
        public void StopAllSources(bool cleanupPool = false) {
            if (cleanupPool) {
                foreach (var source in allAudioSources) {
                    source.Stop();
                }
                CleanupAllSources();
            }
            else {
                foreach (var source in sourcePool) {
                    tempAudioSourceSet.Add(source);
                }
                foreach (var source in allAudioSources) {
                    source.Stop();
                    if (!tempAudioSourceSet.Contains(source)) {
                        ReturnAudioSource(source);
                        tempAudioSourceSet.Add(source);
                    }
                }
                tempAudioSourceSet.Clear();
            }
        }

        public void CleanupUnusedSources() {
            Action<UnityEngine.Object> destroyHandler;
            if (Application.isPlaying) destroyHandler = GameObject.Destroy;
            else destroyHandler = GameObject.DestroyImmediate;

            foreach (var source in sourcePool) {
                allAudioSources.Remove(source);
                if (source == null) continue; // already destroyed
                destroyHandler(source.gameObject);
            }
            sourcePool.Clear();
        }

        public void CleanupAllSources() {
            Action<UnityEngine.Object> destroyHandler;
            if (Application.isPlaying) destroyHandler = GameObject.Destroy;
            else destroyHandler = GameObject.DestroyImmediate;

            foreach (var source in allAudioSources) {
                destroyHandler(source.gameObject);
            }
            sourcePool.Clear();
            allAudioSources.Clear();
        }

    }
}
