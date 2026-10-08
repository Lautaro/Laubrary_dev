using Unity.Collections;
using Laubrary.Audio;

namespace Laubrary.Zounds.Dsp {
    public static class ZoundsAudioCoreLayout {
        public static SapChainLayout Create(ChainLayout layout, Allocator allocator) {
            return SapChainLayout.Create(new AudioChainTables {
                nodeCount = layout.nodeCount,
                nodeType = layout.nodeType,
                enabled = layout.enabled,
                stateOffset = layout.stateOffset,
                paramOffset = layout.paramOffset,
                paramCountOf = layout.paramCountOf,
                derivedFlat = layout.derivedFlat,
                derivedOffset = layout.derivedOffset,
                derivedCountOf = layout.derivedCountOf,
                paramCount = layout.paramCount,
                pBase = layout.pBase,
                pMin = layout.pMin,
                pMax = layout.pMax,
                pRatio = layout.pRatio,
                rampedCount = layout.rampedCount,
                ramped = layout.ramped,
                modCount = layout.modCount,
                modType = layout.modType,
                modStateOffset = layout.modStateOffset,
                modParamFlat = layout.modParamFlat,
                modParamOffset = layout.modParamOffset,
                modParamCountOf = layout.modParamCountOf,
                modCurveFlat = layout.modCurveFlat,
                modCurveOffset = layout.modCurveOffset,
                modCurveCountOf = layout.modCurveCountOf,
                modStepFlat = layout.modStepFlat,
                modStepOffset = layout.modStepOffset,
                modStepCountOf = layout.modStepCountOf,
                modExtraSeconds = layout.modExtraSeconds,
                modAnchor = layout.modAnchor,
                modCtlInit = layout.modCtlInit,
                modCtlCoef = layout.modCtlCoef,
                bindCount = layout.bindCount,
                bindModifier = layout.bindModifier,
                bindTarget = layout.bindTarget,
                bindOp = layout.bindOp,
                bindCombine = layout.bindCombine,
                bindDepth = layout.bindDepth,
                stateFloats = layout.stateFloats,
                tailSeconds = layout.tailSeconds,
                pitchModulated = layout.pitchModulated,
            }, allocator);
        }
    }
}
