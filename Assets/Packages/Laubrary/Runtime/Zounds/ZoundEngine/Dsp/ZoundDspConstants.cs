namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Every sizing and threshold constant of the per-voice DSP engine, in one place.
    /// The live DSP buffer size is a Unity audio setting (default 1024); scratch buffers are sized to
    /// MAX_DSP_BUFFER once so changing that setting never allocates on the audio thread.
    /// </summary>
    public static class ZoundDspConstants {
        public const int MAX_VOICES = 64;
        public const int HEAVY_VOICES = 16;
        public const int MAX_GROUPS = 32;
        public const int HEAVY_GROUPS = 8;
        public const int MAX_BUSES = 8;
        public const int MAX_DSP_BUFFER = 4096;
        public const int MAX_SOURCE_SLOTS = 4;
        public const int MAX_MODIFIERS = 8;
        public const int MAX_NODES = 16;
        public const int MAX_BINDINGS = 32;

        public const float SILENCE_DB = -60f;
        public const float SILENCE_LINEAR = 0.001f;
        public const int HANGOVER_MS = 250;
        public const float MAX_TAIL_SEC = 10f;

        /// <summary>Samples between modifier re-evaluations (750 Hz at 48 kHz). Values are ramped to
        /// per-sample resolution between these points; this is never the rate a DSP node sees.</summary>
        public const int CONTROL_BLOCK = 64;
        public const float INV_CONTROL_BLOCK = 1f / CONTROL_BLOCK;

        /// <summary>Arena sizes in floats. Heavy: 1 MB. Light: 32 KB.</summary>
        public const int HEAVY_ARENA_FLOATS = 256 * 1024;
        public const int LIGHT_ARENA_FLOATS = 8 * 1024;

        public const int EVENT_RING_SIZE = 4096;
        public const int RESERVE_VOICES = 2;
        public const float DECLICK_MS = 2f;
        public const float STOP_FADE_MS = 4f;
    }

}
