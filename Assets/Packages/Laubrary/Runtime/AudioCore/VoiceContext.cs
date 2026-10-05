using Unity.Collections;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Mathf = Laubrary.Audio.AudioMath;

namespace Laubrary.Audio {
    public struct VoiceContext {
        public int sampleRate;
        public float elapsedSeconds;     // since the source started, at block start
        public float sourceDuration;     // resolved play length of the source material
        public float sourcePeak;         // peak of the source PCM (for Normalize)
        public bool sourceExhausted;
    }
}
