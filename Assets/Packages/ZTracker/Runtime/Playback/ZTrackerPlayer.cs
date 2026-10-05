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
        ZTrackerPlayback playback;
        public bool IsPlaying => playback != null && playback.IsPlaying;
        public string LastError { get; private set; }

        public bool Play()
        {
            Stop();
            bool success = ZTrackerPlayback.TryPlay(song, out playback, out string error);
            LastError = error;
            return success;
        }

        public void Stop()
        {
            if (playback != null) playback.Stop();
            playback = null;
        }

        void Update()
        {
            if (!IsPlaying) return;
            for (int n = 0; n < 4096 && IsPlaying; n++)
            {
                if (ZTrackerNative.ZT_PollEvent(playback.NativeContext, out var ev) != 1) break;
                EventReceived?.Invoke(ev);
            }
        }

        void OnDisable() => Stop();
    }
}
