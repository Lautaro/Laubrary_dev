using Unity.Collections;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Mathf = Laubrary.Audio.AudioMath;

namespace Laubrary.Audio {
    /// <summary>Native prepared tables. The owner disposes them only after all renderers borrowing them have stopped.</summary>
    public struct SapChainLayout : System.IDisposable {

        // ── nodes ──
        public int nodeCount;
        public NativeArray<ZoundEffectType> nodeType;
        public NativeArray<bool> enabled;
        public NativeArray<int> stateOffset;
        public NativeArray<int> paramOffset;
        public NativeArray<int> paramCountOf;
        public NativeArray<int> derivedFlat;
        public NativeArray<int> derivedOffset;
        public NativeArray<int> derivedCountOf;

        // ── flat parameters (source stage first) ──
        public int paramCount;
        public NativeArray<float> pBase;
        public NativeArray<float> pMin;
        public NativeArray<float> pMax;
        /// <summary>Per parameter: is its control spaced by ratio? Lets the render modulate by a fraction of the control.</summary>
        public NativeArray<bool> pRatio;
        public int rampedCount;
        public NativeArray<int> ramped;

        // ── modifiers ──
        public int modCount;
        public NativeArray<ZoundModifierType> modType;
        public NativeArray<int> modStateOffset;
        public NativeArray<float> modParamFlat;
        public NativeArray<int> modParamOffset;
        public NativeArray<int> modParamCountOf;
        public NativeArray<EnvPoint> modCurveFlat;
        public NativeArray<int> modCurveOffset;
        public NativeArray<int> modCurveCountOf;
        public NativeArray<float> modStepFlat;
        public NativeArray<int> modStepOffset;
        public NativeArray<int> modStepCountOf;
        public NativeArray<float> modExtraSeconds;
        public NativeArray<int> modAnchor;
        /// <summary>ZPOC: each modifier's starting control value, and how much of the gap to a sent value closes per block.</summary>
        public NativeArray<float> modCtlInit;
        public NativeArray<float> modCtlCoef;

        // ── bindings ──
        public int bindCount;
        public NativeArray<int> bindModifier;
        public NativeArray<int> bindTarget;
        public NativeArray<ModifierOp> bindOp;
        /// <summary>How each binding combines, already translated from whatever form the saved chain used.</summary>
        public NativeArray<ModulationCombine> bindCombine;
        public NativeArray<float> bindDepth;

        // ── totals the render path reads ──
        public int stateFloats;
        public float tailSeconds;
        public bool pitchModulated;

        /// <summary>Flat parameter index of (node, param); nodeIndex -1 is the source stage. Source stage indexing is interpreted by the host.</summary>
        public readonly int FlatIndex(int nodeIndex, int paramIndex) {
            if (nodeIndex < 0) return paramIndex;
            return paramOffset[nodeIndex] + paramIndex;
        }

        public readonly bool IsCreated => nodeType.IsCreated;

        /// <summary>Copies plain managed arrays into native buffers on the preparation thread. The input is not retained.</summary>
        public static SapChainLayout Create(AudioChainTables L, Allocator allocator) {
            var s = new SapChainLayout {
                nodeCount = L.nodeCount,
                paramCount = L.paramCount,
                rampedCount = L.rampedCount,
                modCount = L.modCount,
                bindCount = L.bindCount,
                stateFloats = L.stateFloats,
                tailSeconds = L.tailSeconds,
                pitchModulated = L.pitchModulated,

                nodeType = Copy(L.nodeType, allocator),
                enabled = Copy(L.enabled, allocator),
                stateOffset = Copy(L.stateOffset, allocator),
                paramOffset = Copy(L.paramOffset, allocator),
                paramCountOf = Copy(L.paramCountOf, allocator),
                derivedFlat = Copy(L.derivedFlat, allocator),
                derivedOffset = Copy(L.derivedOffset, allocator),
                derivedCountOf = Copy(L.derivedCountOf, allocator),

                pBase = Copy(L.pBase, allocator),
                pMin = Copy(L.pMin, allocator),
                pMax = Copy(L.pMax, allocator),
                pRatio = Copy(L.pRatio, allocator),
                ramped = Copy(L.ramped, allocator),

                modType = Copy(L.modType, allocator),
                modStateOffset = Copy(L.modStateOffset, allocator),
                modParamFlat = Copy(L.modParamFlat, allocator),
                modParamOffset = Copy(L.modParamOffset, allocator),
                modParamCountOf = Copy(L.modParamCountOf, allocator),
                modCurveFlat = Copy(L.modCurveFlat, allocator),
                modCurveOffset = Copy(L.modCurveOffset, allocator),
                modCurveCountOf = Copy(L.modCurveCountOf, allocator),
                modStepFlat = Copy(L.modStepFlat, allocator),
                modStepOffset = Copy(L.modStepOffset, allocator),
                modStepCountOf = Copy(L.modStepCountOf, allocator),
                modExtraSeconds = Copy(L.modExtraSeconds, allocator),
                modAnchor = Copy(L.modAnchor, allocator),
                modCtlInit = Copy(L.modCtlInit, allocator),
                modCtlCoef = Copy(L.modCtlCoef, allocator),

                bindModifier = Copy(L.bindModifier, allocator),
                bindTarget = Copy(L.bindTarget, allocator),
                bindOp = Copy(L.bindOp, allocator),
                bindCombine = Copy(L.bindCombine, allocator),
                bindDepth = Copy(L.bindDepth, allocator),
            };
            return s;
        }

        /// <summary>
        /// A zero-length buffer is a valid buffer, but NativeArray rejects a length of zero for some
        /// allocators, so an empty source becomes a one-element buffer that nothing reads (every reader
        /// is bounded by the matching count, which is zero). This keeps every field non-null so readers
        /// can index unconditionally, exactly as they can with the managed layout.
        /// </summary>
        static NativeArray<T> Copy<T>(T[] src, Allocator allocator) where T : unmanaged {
            int n = src != null && src.Length > 0 ? src.Length : 1;
            var a = new NativeArray<T>(n, allocator, NativeArrayOptions.ClearMemory);
            if (src != null && src.Length > 0) a.CopyFrom(src);
            return a;
        }

        public void Dispose() {
            if (nodeType.IsCreated) nodeType.Dispose();
            if (enabled.IsCreated) enabled.Dispose();
            if (stateOffset.IsCreated) stateOffset.Dispose();
            if (paramOffset.IsCreated) paramOffset.Dispose();
            if (paramCountOf.IsCreated) paramCountOf.Dispose();
            if (derivedFlat.IsCreated) derivedFlat.Dispose();
            if (derivedOffset.IsCreated) derivedOffset.Dispose();
            if (derivedCountOf.IsCreated) derivedCountOf.Dispose();

            if (pBase.IsCreated) pBase.Dispose();
            if (pMin.IsCreated) pMin.Dispose();
            if (pMax.IsCreated) pMax.Dispose();
            if (pRatio.IsCreated) pRatio.Dispose();
            if (ramped.IsCreated) ramped.Dispose();

            if (modType.IsCreated) modType.Dispose();
            if (modStateOffset.IsCreated) modStateOffset.Dispose();
            if (modParamFlat.IsCreated) modParamFlat.Dispose();
            if (modParamOffset.IsCreated) modParamOffset.Dispose();
            if (modParamCountOf.IsCreated) modParamCountOf.Dispose();
            if (modCurveFlat.IsCreated) modCurveFlat.Dispose();
            if (modCurveOffset.IsCreated) modCurveOffset.Dispose();
            if (modCurveCountOf.IsCreated) modCurveCountOf.Dispose();
            if (modStepFlat.IsCreated) modStepFlat.Dispose();
            if (modStepOffset.IsCreated) modStepOffset.Dispose();
            if (modStepCountOf.IsCreated) modStepCountOf.Dispose();
            if (modExtraSeconds.IsCreated) modExtraSeconds.Dispose();
            if (modAnchor.IsCreated) modAnchor.Dispose();
            if (modCtlInit.IsCreated) modCtlInit.Dispose();
            if (modCtlCoef.IsCreated) modCtlCoef.Dispose();

            if (bindModifier.IsCreated) bindModifier.Dispose();
            if (bindTarget.IsCreated) bindTarget.Dispose();
            if (bindOp.IsCreated) bindOp.Dispose();
            if (bindCombine.IsCreated) bindCombine.Dispose();
            if (bindDepth.IsCreated) bindDepth.Dispose();
            this = default;
        }
    }
}
