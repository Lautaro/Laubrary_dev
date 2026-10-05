using Laubrary.Audio;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {
    /// <summary>Main-thread authoring boundary. Copies preserve the saved field shape, identities and envelope editing data.</summary>
    public static class ZoundsAudioCoreData {
        public static AudioEffectChainData ToAudioData(ZoundEffectChain chain) => chain == null ? null : JsonUtility.FromJson<AudioEffectChainData>(JsonUtility.ToJson(chain));
        public static ZoundEffectChain ToZoundsData(AudioEffectChainData chain) => chain == null ? null : JsonUtility.FromJson<ZoundEffectChain>(JsonUtility.ToJson(chain));
    }
}
