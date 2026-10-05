namespace Laubrary.Zounds {
    public enum ZoundEffectType {
        Gain = 0,
        Limiter = 1,
        Compressor = 2,
        Delay = 3,
        Reverb = 4,
        LowPass = 5,
        HighPass = 6,
        Flanger = 7,
        Chorus = 8,
        Phaser = 9,
        BitCrush = 10,
        Distortion = 11,
        EQ = 12,
        Normalize = 13,
        Fade = 14,
        TransientShaper = 15,
    }

    public enum ZoundModifierType {
        Envelope = 0,
        Lfo = 1,
        Random = 2,
        Step = 3,
        /// <summary>Outputs whatever game code sends to its ZPOC id (0..1), eased; its own Value is where it rests.</summary>
        Code = 4,
    }

    public enum ModifierOp {
        Multiply = 0,
        Add = 1,
        Replace = 2,
    }

    public enum LfoShape { Sine = 0, Triangle = 1, Saw = 2, Square = 3 }

    public enum LfoMode { Oscillate = 0, Random = 1 }

    public enum StepTiming { PerTrigger = 0, PerInterval = 1 }

    public enum StepOrder { Sequential = 0, RoundRobinNoRepeat = 1 }
}

namespace Laubrary.Zounds.Dsp {
    public enum ModulationCombine {
        /// <summary>Move away from the value that was set, by a fraction of the control's travel.</summary>
        Shift = 0,
        /// <summary>Hand the control to the modulator outright, blended in by depth.</summary>
        Set = 1,
        /// <summary>Multiply the value that was set. Only meaningful where the parameter is a level that does not rest at nought.</summary>
        Scale = 2,
        /// <summary>
        /// Shift as it was before depth became a share of the available room (bindings saved with schema 1 or earlier):
        /// a share of the parameter's whole control. Never offered in the interface; an old binding shows as Shift and
        /// becomes the current Shift the first time its mode or depth is changed.
        /// </summary>
        ShiftWholeRange = 3,
        /// <summary>
        /// Set, for a modifier whose output runs 0..1 rather than -1..1 (an envelope): nought is the bottom of the control,
        /// one the top. Never stored; chosen when a chain is laid out, from the kind of modifier bound. The interface shows
        /// it as Set.
        /// </summary>
        SetFromZero = 4,
        /// <summary>
        /// Multiply the value that was set by a ratio on a symmetric, evenly spaced scale (for an envelope, whose output
        /// runs 0..1): the middle is no change, the top x4, the bottom x1/4. What a Klip's pitch and time curves use, so a
        /// flat curve in the middle is exactly "unchanged" and up and down are the same distance (T-0479).
        /// </summary>
        Ratio = 5,
        /// <summary>
        /// Shift, for a modifier whose output runs 0..1 around a middle meaning "no change" (a Code modifier): nought is as
        /// far down as the depth allows, one half is unchanged, one as far up. Never stored; chosen when a chain is laid out.
        /// </summary>
        ShiftFromCentre = 6,
    }
    public enum ParamCurve { Linear = 0, Logarithmic = 1, Decibel = 2, Integer = 3, Toggle = 4 }
}
