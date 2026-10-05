using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Audio {
    /// <summary>Main-thread array input for a prepared native layout. No assets or editor types are retained.</summary>
    public sealed class AudioChainTables {
        // ── nodes ──
        public int nodeCount;
        public ZoundEffectType[] nodeType;
        public bool[] enabled;
        public int[] stateOffset;
        public int[] paramOffset;
        public int[] paramCountOf;
        public int[] derivedFlat;
        public int[] derivedOffset;
        public int[] derivedCountOf;

        // ── flat parameters (source stage first) ──
        public int paramCount;
        public float[] pBase;
        public float[] pMin;
        public float[] pMax;
        /// <summary>Per parameter: is its control spaced by ratio? Lets the render modulate by a fraction of the control.</summary>
        public bool[] pRatio;
        public int rampedCount;
        public int[] ramped;

        // ── modifiers ──
        public int modCount;
        public ZoundModifierType[] modType;
        public int[] modStateOffset;
        public float[] modParamFlat;
        public int[] modParamOffset;
        public int[] modParamCountOf;
        public EnvPoint[] modCurveFlat;
        public int[] modCurveOffset;
        public int[] modCurveCountOf;
        public float[] modStepFlat;
        public int[] modStepOffset;
        public int[] modStepCountOf;
        public float[] modExtraSeconds;
        /// <summary>ZPOC: each modifier's starting control value, and how much of the gap to a sent value closes per block.</summary>
        public float[] modCtlInit;
        public float[] modCtlCoef;

        // ── bindings ──
        public int bindCount;
        public int[] bindModifier;
        public int[] bindTarget;
        public ModifierOp[] bindOp;
        /// <summary>How each binding combines, already translated from whatever form the saved chain used.</summary>
        public ModulationCombine[] bindCombine;
        public float[] bindDepth;

        // ── totals the render path reads ──
        public int stateFloats;
        public float tailSeconds;
        public bool pitchModulated;

    }
}
