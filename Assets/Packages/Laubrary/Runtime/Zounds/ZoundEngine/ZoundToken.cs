using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

namespace Laubrary.Zounds {

    public class ZoundToken {

        public event System.Action onFrameUpdate;
        public event System.Action onComplete;

        public enum State {
            Playing, Paused, Killed, FadeToKill
        }

        private Zound m_zound;
        private IZoundHandler m_handler;
        private AudioSource m_audioSource;
        private bool m_started;
        private State m_state = State.Paused;
        private double m_lastDspTime;
        private bool m_isChildZound;

        private CompositeZound.ZoundEntry m_soloOverride;

        public Zound zound => m_zound;
        public State state => m_state;
        public AudioSource audioSource => m_audioSource;
        internal List<AudioSource> audioSources => m_handler.GetAudioSources();
        public float duration => m_handler.totalDuration;
        public float time => m_handler.currentTime;
        public bool isDelayFinished => m_handler.isDelayFinished;
        public bool isChildZound => m_isChildZound;
        public int playedEntryIndex => m_handler.playedEntryIndex;

        internal float parentVolume { set => m_handler.parentVolume = value; }
        internal bool isRealtime => m_handler.isRealtime;

        internal CompositeZound.ZoundEntry soloOverride => m_soloOverride;

        internal bool TryGetEntryToken(CompositeZound.ZoundEntry entry, out ZoundToken token) {
            if (m_handler is ZequenceHandler zeqHandler) {
                token = zeqHandler.GetEntryToken(entry);
            }
            else {
                token = null;
            }
            return token != null;
        }

        internal bool IsEntryMuted(CompositeZound.ZoundEntry entry) {
            if (m_handler is ZequenceHandler zeqHandler) {
                return zeqHandler.IsEntryMuted(entry);
            }
            return true;
        }

        public ZoundToken(Zound zound, AudioSource audioSource, ZoundArgs zoundArgs) {
            m_zound = zound;
            m_audioSource = audioSource;
            m_state = State.Paused;
            m_isChildZound = zoundArgs.isChild;
            m_soloOverride = zoundArgs.soloOverride;

            if (zound is Klip klip) {
                m_handler = new KlipHandler(klip, audioSource, zoundArgs);
            }
            else if (zound is Zequence zequence) {
                m_handler = new ZequenceHandler(zequence, audioSource, zoundArgs);
            }
            else if (zound is ClipZound clipZound) {
                m_handler = new ClipZoundHandler(clipZound, audioSource, zoundArgs);
            }
            else {
                Debug.LogError("Invalid Zound type: " + zound.GetType()); // actually impossible, but need to fill "else"
                m_handler = null;
            }

            if (m_handler != null) {
                m_handler.Init();
                if (!isChildZound) {
                    ApplyMixerGroupToChildren(audioSource.outputAudioMixerGroup);
                }
            }
        }

        internal void ApplyMixerGroupToChildren(AudioMixerGroup mixerGroup) {
            m_handler?.ApplyMixerGroupToChildren(mixerGroup);
        }

        internal void Start(float timeOffset = 0f, float fadeDuration = 0f, System.Action onFadeComplete = null) {
            if (m_state == State.Killed || m_state == State.FadeToKill) {
                Debug.LogError("Invalid token to start: The token has been killed.");
                return;
            }
            m_started = true;
            m_lastDspTime = AudioSettings.dspTime;
            m_state = State.Playing;
            m_handler.OnStart(timeOffset, fadeDuration, onFadeComplete);
        }

        public bool IsMutedOrExcluded() {
            return m_handler.IsMutedOrExcluded();
        }

        public void Play(float fadeDuration = 0f, System.Action onFadeComplete = null) {
            Start(0f, fadeDuration, onFadeComplete);
        }

        public Task PlayAsync(float fadeDuration) {
            if (fadeDuration <= Mathf.Epsilon) {
                Play();
                return Task.CompletedTask;
            }
            var tcs = new TaskCompletionSource<bool>();
            Play(fadeDuration, () => {
                tcs.SetResult(true);
            });
            return tcs.Task;
        }

        public void Pause(float fadeDuration = 0f, System.Action onFadeComplete = null) {
            if (fadeDuration > Mathf.Epsilon) {
                if (m_state == State.Killed || m_state == State.FadeToKill) {
                    Debug.LogError("Invalid token to pause: The token has been killed.");
                    return;
                }
                if (m_state == State.Paused) return;
                m_state = State.Playing;
                m_handler.OnFadeAndPause(fadeDuration, onFadeComplete);
            }
            else {
                if (m_state == State.Killed || m_state == State.FadeToKill) {
                    Debug.LogError("Invalid token to pause: The token has been killed.");
                    return;
                }
                if (m_state == State.Paused) return;
                m_state = State.Paused;
                m_handler.OnPause();
            }
        }

        public Task PauseAsync(float fadeDuration) {
            if (fadeDuration <= Mathf.Epsilon) {
                Pause();
                return Task.CompletedTask;
            }
            var tcs = new TaskCompletionSource<bool>();
            Pause(fadeDuration, () => {
                tcs.SetResult(true);
            });
            return tcs.Task;
        }

        public void Unpause(float fadeDuration = 0f, System.Action onFadeComplete = null) {
            if (!m_started) {
                Play(fadeDuration, onFadeComplete);
                return;
            }
            if (m_state == State.Killed || m_state == State.FadeToKill) {
                Debug.LogError("Invalid token to resume: The token has been killed.");
                return;
            }
            if (m_state == State.Playing) return;
            m_lastDspTime = AudioSettings.dspTime;
            m_state = State.Playing;
            m_handler.OnResume(fadeDuration, onFadeComplete);
        }

        public Task UnpauseAsync(float fadeDuration) {
            if (m_started) {
                if (fadeDuration <= Mathf.Epsilon) {
                    Unpause();
                    return Task.CompletedTask;
                }
                var tcs = new TaskCompletionSource<bool>();
                Unpause(fadeDuration, () => {
                    tcs.SetResult(true);
                });
                return tcs.Task;
            }
            else {
                return PlayAsync(fadeDuration);
            }
        }

        public void Kill(float fadeDuration = 0f, System.Action onFadeComplete = null) {
            if (fadeDuration > Mathf.Epsilon) {
                if (m_state == State.Killed) return;
                m_state = State.FadeToKill;
                m_handler.OnFadeAndKill(fadeDuration, onFadeComplete);
            }
            else {
                if (m_state == State.Killed) return;
                m_state = State.Killed;
                m_handler.OnKill();
            }
        }

        public Task KillAsync(float fadeDuration) {
            if (fadeDuration <= Mathf.Epsilon) {
                Kill();
                return Task.CompletedTask;
            }
            var tcs = new TaskCompletionSource<bool>();
            Kill(fadeDuration, () => {
                tcs.SetResult(true);
            });
            return tcs.Task;
        }

        internal void OnUpdate() {
            double currentDspTime = AudioSettings.dspTime;
            float deltaDspTime = (float)(currentDspTime - m_lastDspTime);
            m_lastDspTime = currentDspTime;
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                if (audioSource == null) {
                    m_state = State.Killed;
                    return;
                }
            }
#endif

            if (m_state == State.Playing || m_state == State.FadeToKill) {
                ZoundUpdateResult nextTreatment = m_handler.OnUpdate(deltaDspTime);
                if (nextTreatment == ZoundUpdateResult.Kill) {
                    m_state = State.Killed;
                    onComplete?.Invoke();
                }
                else if (nextTreatment == ZoundUpdateResult.Pause) {
                    m_state = State.Paused;
                    onFrameUpdate?.Invoke();
                }
                else {
                    onFrameUpdate?.Invoke();
                }
            }
            else {
                onFrameUpdate?.Invoke();
            }

        }

    }

}
