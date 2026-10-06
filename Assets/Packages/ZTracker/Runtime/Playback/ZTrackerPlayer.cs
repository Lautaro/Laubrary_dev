using System;
using UnityEngine;

namespace Laubrary.ZTracker
{
    [AddComponentMenu("Laubrary/ZTracker Player")]
    public sealed class ZTrackerPlayer : MonoBehaviour
    {
        [Tooltip("The song played only when game code calls Play.")]
        public ZTrackerSong song;
        public event Action<ZTrackerEventData> EventReceived;
        public event Action<Engine.TrackerEvent> EngineEventReceived;
        ZTrackerPlayback playback;
        bool stopping;
        public bool IsPlaying => playback != null && playback.IsPlaying;
        string lastError;
        public string LastError => !ReferenceEquals(playback, null) && playback.LastError != null ? playback.LastError : lastError;
        public ZTrackerPlayback Playback => playback;

        public bool Play()
        {
            if (stopping) return false;
            Stop();
            bool success = ZTrackerPlayback.TryPlay(song, out playback, out string error);
            lastError = error;
            if (success) playback.EngineEventReceived += ForwardEngineEvent;
            return success;
        }

        public void Stop()
        {
            if (stopping || ReferenceEquals(playback, null)) return;
            stopping = true;
            try
            {
                // Subscriber exceptions must never leave the graph orphaned.
                try { PumpEvents(); }
                finally
                {
                    playback.EngineEventReceived -= ForwardEngineEvent;
                    if (playback != null) playback.Stop();
                    lastError = playback.LastError;
                }
                var stopped = new ZTrackerEventData { type = (byte)ZTrackerEventType.SONG_STOPPED, samplePosition = (ulong)playback.RenderedFrames, channelIndex = -1, noteColumn = -1, noteValue = -1, instrumentID = -1 };
                EventReceived?.Invoke(stopped);
            }
            finally { playback = null; stopping = false; }
        }

        void Update()
            => PumpEvents();

        void ForwardEngineEvent(Engine.TrackerEvent value) => EngineEventReceived?.Invoke(value);
        public void PumpEvents()
        {
            if (!IsPlaying) return;
            for (int n = 0; n < 4096 && IsPlaying; n++)
            {
                if (!playback.TryReadEvent(out var ev)) break;
                EventReceived?.Invoke(ev);
            }
        }

        void OnDisable() => Stop();
    }
}
