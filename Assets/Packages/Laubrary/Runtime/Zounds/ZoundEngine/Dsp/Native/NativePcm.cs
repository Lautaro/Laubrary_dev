using System.Collections.Generic;

namespace Laubrary.Zounds.Dsp.Native {

    /// <summary>
    /// Hands each decoded clip an id in the native engine, uploading it the first time it
    /// is played.
    ///
    /// The native side keeps its own copy of the samples, because a voice reads them on
    /// the audio thread for as long as it plays and a managed array cannot be trusted to
    /// stay put: pinning one for the lifetime of a sound would fragment the heap, and not
    /// pinning it would let the GC move it under the mixer thread. Copying once per clip
    /// is the cheap, safe answer — the cost is one extra copy of audio that is already
    /// resident, paid on first play.
    /// </summary>
    public static class NativePcm {

        // 1 is reserved for the offline renderer's scratch clip (see ZoundNativeOffline).
        private const int FIRST_ID = 2;

        private static readonly Dictionary<PcmClip, int> ids = new Dictionary<PcmClip, int>();
        private static int nextId = FIRST_ID;

        /// <summary>The native id for a clip, uploading it on first use. 0 means it could not be uploaded.</summary>
        public static int IdFor(PcmClip pcm) {
            if (pcm == null || !pcm.valid || pcm.samples == null || !ZoundsNative.Available) return 0;
            if (ids.TryGetValue(pcm, out int id)) return id;
            id = nextId++;
            if (ZoundsNative.Zounds_UploadPcm(id, pcm.samples, pcm.frames, pcm.channels, pcm.frequency, pcm.peak) == 0) {
                UnityEngine.Debug.LogError("[Zounds] the native engine refused a clip of " + pcm.frames + " frames x "
                    + pcm.channels + " channels; it will not play.");
                return 0;
            }
            ids[pcm] = id;
            return id;
        }

        /// <summary>Drops every mapping (an engine teardown or a PCM cache clear).</summary>
        public static void Clear() {
            if (ZoundsNative.Available) {
                foreach (var kv in ids) ZoundsNative.Zounds_ReleasePcm(kv.Value);
            }
            ids.Clear();
            nextId = FIRST_ID;
        }
    }
}
