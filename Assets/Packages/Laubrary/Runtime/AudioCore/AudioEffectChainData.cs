using System;
using System.Collections.Generic;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Audio {
    /// <summary>Portable authoring data with the same serialized field shape as a Zounds chain. Never read by the audio thread.</summary>
    [Serializable]
    public sealed class AudioEffectChainData {
        public List<AudioEffectNodeData> nodes = new List<AudioEffectNodeData>();
        public List<AudioModifierData> modifiers = new List<AudioModifierData>();
        public List<AudioModifierBindingData> bindings = new List<AudioModifierBindingData>();
    }

    [Serializable]
    public sealed class AudioEffectNodeData {
        public ZoundEffectType type;
        public bool enabled = true;
        public float[] p = Array.Empty<float>();
        public string uid = "";
    }

    [Serializable]
    public sealed class AudioModifierData {
        public ZoundModifierType type;
        public bool enabled = true;
        public string name = "";
        public float[] p = Array.Empty<float>();
        public AudioEnvelopeData curve = new AudioEnvelopeData();
        public float[] steps = Array.Empty<float>();
        public string zpocId = "";
        public int zpocMode;
        public float zpocRest = -1f, zpocSmoothMs = 30f;
        public string uid = "";
    }

    [Serializable]
    public sealed class AudioModifierBindingData {
        public int modifierIndex, paramIndex, schema;
        public int nodeIndex = -1;
        public ModulationCombine combine;
        public float depth = 0.5f;
        public ModifierOp op;
    }

    /// <summary>Field names preserve the saved envelope shape. Editing permissions are integers; there is no UI dependency.</summary>
    [Serializable]
    public sealed class AudioEnvelopeData {
        public bool m_enabled;
        public float m_xMin, m_yMin;
        public float m_xMax = 1f, m_yMax = 1f;
        public List<AudioEnvelopePointData> m_points = new List<AudioEnvelopePointData>();
    }

    [Serializable]
    public struct AudioEnvelopePointData {
        public float time, value, exponent;
        public int editState;
        public float randomX, randomY, randomBias;
    }
}
