using Laubrary.Audio;
namespace Laubrary.Zounds {

    public static class KlipEffectChain {

        public static readonly AudioEffect[] Effects = new AudioEffect[] {
            new EQEffect(),
            new GainEffect(),
            new CompressionEffect(),
            new NormalizationEffect(),
            new FadeEffect(),
        };

        public static bool HasActiveEffects(Klip klip) {
            for (int i = 0; i < Effects.Length; i++) {
                if (Effects[i].IsActive(klip)) return true;
            }
            return false;
        }

        public static float[] ProcessChain(float[] samples, int channels, int sampleRate, Klip klip) {
            for (int i = 0; i < Effects.Length; i++) {
                if (Effects[i].IsActive(klip)) {
                    samples = Effects[i].Process(samples, channels, sampleRate, klip);
                }
            }
            return samples;
        }
    }

}
