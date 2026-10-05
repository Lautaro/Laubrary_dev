using System;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Laubrary.ZTracker
{
    [AddComponentMenu("")]
    [ExecuteAlways]
    [RequireComponent(typeof(AudioSource))]
    public sealed class ZTrackerPlayback : MonoBehaviour
    {
        static ZTrackerPlayback current;
        public static ZTrackerPlayback Current => current;
        public bool IsPlaying => context != IntPtr.Zero;
        public IntPtr NativeContext => context;
        IntPtr context;
        // 0 = idle, 1 = rendering, 2 = main-thread ownership. The audio thread never waits.
        int gate;
        readonly float[] left = new float[4096];
        readonly float[] right = new float[4096];
        AudioSource source;
        AudioClip silence;

        public static bool TryPlay(ZTrackerSong song, out ZTrackerPlayback playback, out string error)
            => TryStart(song, null, 69, out playback, out error);

        public static bool TryAudition(ZTrackerInstrument instrument, int note,
            out ZTrackerPlayback playback, out string error)
            => TryStart(null, instrument, note, out playback, out error);

        static bool TryStart(ZTrackerSong song, ZTrackerInstrument instrument, int note,
            out ZTrackerPlayback playback, out string error)
        {
            playback = null;
            error = null;
            if (song == null && instrument == null) { error = "Choose a song or instrument first."; return false; }
            if (current != null) { error = "Stop the current tracker playback first."; return false; }
            if (!ZTrackerCapability.CheckNative(out error)) return false;
            if (song != null && !Validate(song, out error)) return false;

            var go = new GameObject("ZTracker playback");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            else go.hideFlags = HideFlags.HideAndDontSave;
            var host = go.AddComponent<ZTrackerPlayback>();
            try
            {
                host.context = ZTrackerNative.ZT_Create(AudioSettings.outputSampleRate);
                if (host.context == IntPtr.Zero) throw new InvalidOperationException("Tracker engine creation failed.");
                ZTrackerInstrument.InvalidateClipCache();
                // Upload finishes before the source can enter the renderer.
                if (song != null)
                {
                    song.PushToNative(host.context);
                    ZTrackerNative.ZT_Play(host.context, 0, 0);
                }
                else
                {
                    instrument.PushToNative(host.context, 0, -1);
                    ZTrackerNative.ZT_NoteOn(host.context, 0, Mathf.Clamp(note, 0, 126), 1f);
                }
                host.source = go.GetComponent<AudioSource>();
                host.silence = AudioClip.Create("Tracker clock", 4096, 1, AudioSettings.outputSampleRate, false);
                host.silence.hideFlags = HideFlags.HideAndDontSave;
                host.source.clip = host.silence;
                host.source.loop = true;
                host.source.playOnAwake = false;
                host.source.volume = 1f;
                current = host;
                host.Subscribe();
                host.source.Play();
                playback = host;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                host.Stop();
                return false;
            }
        }

        static bool Validate(ZTrackerSong song, out string error)
        {
            error = null;
            if (song.channelCount < 1 || song.GetTotalNativeChannels() > 32 ||
                song.patterns.Count < 1 || song.patterns.Count > 256 || song.orderList.Count < 1 ||
                song.orderList.Count > 256 || song.bpm < 1 || song.ticksPerRow < 1 || song.linesPerBeat < 1)
                error = "The song needs valid tempo, channels, patterns and an order list.";
            foreach (var p in song.patterns)
                if (p == null || p.rowCount < 1 || p.rowCount > 256 || p.cells == null ||
                    p.cells.Count < p.rowCount * song.channelCount)
                    error = "A pattern has missing rows or cells.";
            foreach (int p in song.orderList)
                if (p < 0 || p >= song.patterns.Count) error = "The order list references a missing pattern.";
            int slots = 0;
            foreach (var i in song.instruments) if (i != null) slots += 1 + (i.presets?.Count ?? 0);
            if (slots > 512) error = "The song uses more than 512 instrument and preset slots.";
            return error == null;
        }

        void Subscribe()
        {
            AudioSettings.OnAudioConfigurationChanged += ConfigurationChanged;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += ModeChanged;
            EditorApplication.quitting += Stop;
            EditorApplication.update += TickEditor;
#endif
        }

        void Unsubscribe()
        {
            AudioSettings.OnAudioConfigurationChanged -= ConfigurationChanged;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.playModeStateChanged -= ModeChanged;
            EditorApplication.quitting -= Stop;
            EditorApplication.update -= TickEditor;
#endif
        }

        void ConfigurationChanged(bool _) => Stop();
#if UNITY_EDITOR
        void TickEditor() { if (!Application.isPlaying) EditorApplication.QueuePlayerLoopUpdate(); }
        void ModeChanged(PlayModeStateChange mode)
        {
            if (mode == PlayModeStateChange.ExitingEditMode || mode == PlayModeStateChange.ExitingPlayMode) Stop();
        }
#endif

        public void Stop()
        {
            Release();
            if (this != null)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
            }
        }

        void Release()
        {
            Unsubscribe();
            if (source != null) source.Stop();
            // Once gate 2 is held, no callback can snapshot or dereference this context.
            var spin = new SpinWait();
            while (Interlocked.CompareExchange(ref gate, 2, 0) != 0) spin.SpinOnce();
            try
            {
                var doomed = context;
                context = IntPtr.Zero;
                if (doomed != IntPtr.Zero) ZTrackerNative.ZT_Destroy(doomed);
                ZTrackerInstrument.InvalidateClipCache();
                if (current == this) current = null;
            }
            finally { Volatile.Write(ref gate, 0); }
            if (silence != null)
            {
                if (Application.isPlaying) Destroy(silence);
                else DestroyImmediate(silence);
                silence = null;
            }
        }

        void OnDisable() => Release();
        void OnApplicationQuit() => Release();

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels < 1 || Interlocked.CompareExchange(ref gate, 1, 0) != 0) return;
            try
            {
                if (context == IntPtr.Zero) return;
                int frames = data.Length / channels;
                for (int offset = 0; offset < frames; offset += left.Length)
                {
                    int count = Math.Min(left.Length, frames - offset);
                    ZTrackerNative.ZT_Process(context, left, right, count);
                    for (int f = 0; f < count; f++)
                    {
                        int dst = (offset + f) * channels;
                        if (channels == 1) data[dst] += (left[f] + right[f]) * 0.5f;
                        else { data[dst] += left[f]; data[dst + 1] += right[f]; }
                    }
                }
            }
            finally { Volatile.Write(ref gate, 0); }
        }
    }
}
