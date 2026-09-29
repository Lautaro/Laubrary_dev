namespace Laubrary.Zounds.Dsp {

    /// <summary>What a <see cref="SapVoiceCommand"/> asks a playing voice to do.</summary>
    public enum SapVoiceCommandKind {
        None = 0,
        /// <summary>Set one chain parameter, named by its flat index in the layout.</summary>
        SetParameter = 1,
        /// <summary>Set the base pitch multiplier.</summary>
        SetPitch = 2,
        /// <summary>Set the output gain.</summary>
        SetGain = 3,
        /// <summary>Stop hard at the next block: declick and flush, do not wait for the tail.</summary>
        Stop = 4,
        /// <summary>Stop feeding new source material and let the tail ring out naturally.</summary>
        Release = 5,
        SetSpeed = 6,
        /// <summary>The Looper (T-0474): the loop region's start / end, in source frames (carried in index).</summary>
        SetLoopStart = 7,
        SetLoopEnd = 8,
        /// <summary>The Looper: the crossmix range's bottom / top, in source frames (carried in index).</summary>
        SetCrossmixMin = 9,
        SetCrossmixMax = 10,
        /// <summary>ZPOC: one modifier's control value (modifier index in index, value already converted by the layout).</summary>
        SetModifierControl = 11,
        /// <summary>Snapshot glide (T-0498), sent as: Clear, then targets, then Begin. Targets carry an index and a value.</summary>
        GlideClear = 12,
        /// <summary>An effect or own-value parameter (flat index) glides to value.</summary>
        GlideParam = 13,
        /// <summary>An on/off, whole-number or choice parameter (flat index) switches to value at the glide's midpoint.</summary>
        GlideParamSwitch = 14,
        /// <summary>A modifier parameter (index into the flat modifier-parameter array) glides to value.</summary>
        GlideModParam = 15,
        GlideModParamSwitch = 16,
        /// <summary>A binding's depth (layout binding index) glides to value.</summary>
        GlideDepth = 17,
        /// <summary>An effect (node index) fades in (1) or out (0).</summary>
        GlidePresence = 18,
        /// <summary>Starts the glide from wherever every targeted value is now, over index samples (0: at once).</summary>
        GlideBegin = 19,
    }

    /// <summary>
    /// One live change to a sound that is already playing, sent from the main thread to the audio thread
    /// through the audio graph's own value channel.
    ///
    /// **Why this is one small fixed-size structure rather than several typed messages.** The channel copies
    /// whatever it is given, and a voice may receive a burst of these in a single block (a slider being
    /// dragged produces one per frame). Keeping every change the same size and shape means the receiving side
    /// is a single switch with no allocation and no branching on type, which is what lets it run inside
    /// compiled code on the audio thread.
    ///
    /// Everything not named here — which effect a parameter belongs to, what its range is, whether a modifier
    /// is already driving it — is resolved from the voice's own layout at the receiving end, so a sender needs
    /// to know only the flat index. That also means a stale or nonsensical index is ignored rather than
    /// corrupting anything.
    /// </summary>
    public struct SapVoiceCommand {

        public SapVoiceCommandKind kind;

        /// <summary>For a parameter change: its flat index in the chain layout. Unused otherwise.</summary>
        public int index;

        /// <summary>The new value, for the three kinds that carry one.</summary>
        public float value;

        public static SapVoiceCommand Parameter(int flatIndex, float value) =>
            new SapVoiceCommand { kind = SapVoiceCommandKind.SetParameter, index = flatIndex, value = value };

        public static SapVoiceCommand Pitch(float value) =>
            new SapVoiceCommand { kind = SapVoiceCommandKind.SetPitch, value = value };

        public static SapVoiceCommand Gain(float value) =>
            new SapVoiceCommand { kind = SapVoiceCommandKind.SetGain, value = value };

        /// <summary>The live base speed of a voice with live speed (T-0409); ignored by any other voice.</summary>
        public static SapVoiceCommand Speed(float value) =>
            new SapVoiceCommand { kind = SapVoiceCommandKind.SetSpeed, value = value };

        public static SapVoiceCommand LoopStart(int frame) => new SapVoiceCommand { kind = SapVoiceCommandKind.SetLoopStart, index = frame };
        public static SapVoiceCommand LoopEnd(int frame) => new SapVoiceCommand { kind = SapVoiceCommandKind.SetLoopEnd, index = frame };
        public static SapVoiceCommand CrossmixMin(int frames) => new SapVoiceCommand { kind = SapVoiceCommandKind.SetCrossmixMin, index = frames };
        public static SapVoiceCommand CrossmixMax(int frames) => new SapVoiceCommand { kind = SapVoiceCommandKind.SetCrossmixMax, index = frames };

        public static SapVoiceCommand ModifierControl(int modifier, float value) =>
            new SapVoiceCommand { kind = SapVoiceCommandKind.SetModifierControl, index = modifier, value = value };

        public static SapVoiceCommand Glide(SapVoiceCommandKind kind, int index, float value) =>
            new SapVoiceCommand { kind = kind, index = index, value = value };

        public static SapVoiceCommand Stop() => new SapVoiceCommand { kind = SapVoiceCommandKind.Stop };

        public static SapVoiceCommand Release() => new SapVoiceCommand { kind = SapVoiceCommandKind.Release };
    }
}
