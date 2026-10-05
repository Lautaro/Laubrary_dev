using System;
using UnityEngine;

[CreateAssetMenu(menuName = "ZTracker/Instrument", fileName = "NewInstrument")]
public class ZTrackerInstrument : ScriptableObject
{
    public InstrumentType type = InstrumentType.Sample;

    // Name comes from the asset filename
    public string instrumentName => name;

    // Sample instrument
    public AudioClip sampleClip;
    public int baseNote = 60;
    public float fineTune = 0f;

    // Sample blend — when sampleClipB is set, the voice blends two PCM
    // sources using the shared blendMode/blend fields below (same enum as
    // synth: Mix/Ring/Sync/PM). sampleClipB == null disables blend cleanly.
    public AudioClip sampleClipB;
    public int baseNoteB = 60;
    public float fineTuneB = 0f;

    // Synth instrument
    public int waveA = 2;       // Sawtooth
    public int waveB = 0;       // Sine
    public int blendMode = 0;   // 0=Mix, 1=Ring, 2=Sync, 3=PM
    [Range(0f, 1f)] public float blend = 0f;
    public float pmDepth = 1f;
    public float waveBRatio = 2f;
    public bool blendEnvelope = false;
    public float blendAttack = 0.01f;
    public float blendDecay = 0.1f;
    [Range(0f, 1f)] public float blendSustain = 0.5f;
    public float blendRelease = 0.3f;

    // Unison
    [Range(1, 8)] public int unisonVoices = 1;
    public float unisonDetune = 10f;
    [Range(0f, 1f)] public float unisonSpread = 0.8f;

    // Pulse width (square wave duty cycle)
    [Range(0.01f, 0.99f)] public float pulseWidth = 0.5f;

    // Shared params
    [Range(0f, 1f)] public float volume = 1f;
    [Range(-1f, 1f)] public float pan = 0f;

    // ADSR
    public float attack = 0.01f;
    public float decay = 0.2f;
    [Range(0f, 1f)] public float sustain = 0.7f;
    public float release = 0.5f;

    // Vibrato
    [Range(0f, 200f)] public float vibratoDepth = 0f;
    [Range(0f, 20f)] public float vibratoRate = 5f;
    public float vibratoFadeIn = 0f;
    [Range(0f, 1f)] public float vibratoRandomness = 0f;

    // Arpeggio
    public bool arpeggioEnabled = false;
    public int[] arpeggioNotes = { 0, 4, 7 };
    public float arpeggioSpeed = 0.05f;

    // Optional parameter envelopes (slider by default, toggle to curve)
    public ZUIEnvelopeData blendEnvelopeData;
    public ZUIEnvelopeData pulseWidthEnvelopeData;
    public ZUIEnvelopeData waveBRatioEnvelopeData;
    public ZUIEnvelopeData pmDepthEnvelopeData;
    public ZUIEnvelopeData unisonDetuneEnvelopeData;

    // Per-instrument effects
    public bool instFilterEnabled = false;
    public int instFilterMode = 0; // 0=LP, 1=HP, 2=BP
    [Range(0f, 1f)] public float instFilterCutoff = 1f;
    [Range(0.1f, 10f)] public float instFilterResonance = 0.707f;
    [Range(0f, 1f)] public float instDelaySend = 0f;
    [Range(0f, 1f)] public float instReverbSend = 0f;

    // FM synthesis
    public int fmAlgorithm = 0;
    [Range(0f, 2f)] public float fmFeedback = 0f;
    public FMOperatorData[] fmOperators;

    [Serializable]
    public struct FMOperatorData
    {
        public float freqRatio;
        public float freqFixed;
        [Range(0f, 2f)] public float level;
        public int waveform;
        public float attack;
        public float decay;
        [Range(0f, 1f)] public float sustain;
        public float release;
    }

    // Kit (drum map)
    public bool kitOverlap = true; // true = drums ring out on same track
    public KitEntry[] kitEntries;

    [Serializable]
    public struct KitEntry
    {
        public int midiNote;
        public string displayName;  // e.g. "BD", "SD", "CH"
        public AudioClip clip;
        public int baseNote;
        public float volume;
        public float attack;
        public float decay;
        public float sustain;
        public float release;
    }

    // ── Macros ──────────────────────────────────────────────────────
    // A macro is a 0-1 knob that controls multiple instrument parameters at once.
    // Track command 10xx sets macro 0, 11xx slides it.

    public MacroDef[] macros;

    [Serializable]
    public struct MacroDef
    {
        public string name;
        [Range(0f, 1f)] public float defaultValue;
        public MacroLink[] links;
    }

    [Serializable]
    public struct MacroLink
    {
        public string parameterName; // e.g. "blend", "pulseWidth", "vibratoDepth", "volume", "pan", "pmDepth", "waveBRatio"
        public float minValue;       // param value when macro = 0
        public float maxValue;       // param value when macro = 1
    }

    // ── Presets ─────────────────────────────────────────────────────
    // Section-based override model. Each preset carries an `ovrX` flag and
    // payload per overridable section. The active preset is resolved at
    // playback time: if its section flag is true, the preset's payload wins;
    // otherwise the instrument's Base values are used.
    //
    // Base is this instrument itself (fields above). Presets never mutate Base.
    // Track command 12xx switches the active preset. Out-of-range indices fall
    // back to Base.

    public System.Collections.Generic.List<InstrumentPreset> presets = new System.Collections.Generic.List<InstrumentPreset>();

    /// <summary>-1 = Base, 0..presets.Count-1 = preset index. Serialized so the
    /// editor reopens on the same preset. Runtime playback routes through this
    /// too (tracker 12xx command sets the channel preset separately per voice).</summary>
    public int activePresetIndex = -1;

    [Serializable]
    public class InstrumentPreset
    {
        public string name = "Preset";

        // Per-section override flags. "Ignore when disabled" is enforced at
        // resolution time (see Resolve* methods) — disabled overrides keep
        // their last-edited values but don't take effect.
        public bool ovrVolPan;
        public bool ovrSampleParams;
        public bool ovrSynthParams;
        public bool ovrBlend;
        public bool ovrPulseWidth;
        public bool ovrBRatio;
        public bool ovrPMDepth;
        public bool ovrDetune;
        public bool ovrAdsr;
        public bool ovrVibrato;
        public bool ovrEffects;

        // ── Override payloads ────────────────────────────────────────────
        // Only read when the corresponding ovrX flag is true.

        // Vol/Pan
        [Range(0f, 1f)]  public float volume = 1f;
        [Range(-1f, 1f)] public float pan = 0f;

        // Sample params
        public AudioClip sampleClip;
        public int   baseNote = 60;
        public float fineTune = 0f;
        // Sample-B side of the blend. Gated by ovrSampleParams along with A.
        public AudioClip sampleClipB;
        public int   baseNoteB = 60;
        public float fineTuneB = 0f;

        // Synth params
        public int   waveA = 2;
        public int   waveB = 0;
        public int   blendMode = 0;
        [Range(1, 8)] public int unisonVoices = 1;
        [Range(0f, 1f)] public float unisonSpread = 0.8f;

        // Parameter scalars (used when that parameter's override is on)
        public float blend = 0f;
        public float pulseWidth = 0.5f;
        public float waveBRatio = 2f;
        public float pmDepth = 1f;
        public float unisonDetune = 10f;

        // Parameter envelopes. Deep-copied from Base when the override is
        // toggled on (see InstrumentEditorWindow.EnableOverride). Never
        // shared with Base so edits stay local to the preset.
        public ZUIEnvelopeData blendEnvelopeData;
        public ZUIEnvelopeData pulseWidthEnvelopeData;
        public ZUIEnvelopeData waveBRatioEnvelopeData;
        public ZUIEnvelopeData pmDepthEnvelopeData;
        public ZUIEnvelopeData unisonDetuneEnvelopeData;

        // ADSR
        public float attack = 0.01f;
        public float decay = 0.2f;
        [Range(0f, 1f)] public float sustain = 0.7f;
        public float release = 0.5f;

        // Vibrato
        [Range(0f, 200f)] public float vibratoDepth = 0f;
        [Range(0f, 20f)]  public float vibratoRate = 5f;
        public float vibratoFadeIn = 0f;
        [Range(0f, 1f)]   public float vibratoRandomness = 0f;

        // Effects
        public bool  instFilterEnabled = false;
        public int   instFilterMode = 0;
        [Range(0f, 1f)]   public float instFilterCutoff = 1f;
        [Range(0.1f, 10f)] public float instFilterResonance = 0.707f;
        [Range(0f, 1f)]   public float instDelaySend = 0f;
        [Range(0f, 1f)]   public float instReverbSend = 0f;
    }

    /// <summary>Returns the active preset, or null if Base is active / index out of range.</summary>
    public InstrumentPreset ActivePreset
    {
        get
        {
            if (presets == null) return null;
            if (activePresetIndex < 0 || activePresetIndex >= presets.Count) return null;
            return presets[activePresetIndex];
        }
    }

    /// <summary>Returns the preset at `idx`, or null for Base / out-of-range.</summary>
    public InstrumentPreset GetPreset(int idx)
    {
        if (presets == null || idx < 0 || idx >= presets.Count) return null;
        return presets[idx];
    }

    // ── Macro evaluation ────────────────────────────────────────────

    public float GetMacroParam(int macroIndex, string paramName, float baseValue)
    {
        if (macros == null || macroIndex < 0 || macroIndex >= macros.Length) return baseValue;
        var macro = macros[macroIndex];
        if (macro.links == null) return baseValue;
        // This is for preview only — actual runtime uses native macro values
        foreach (var link in macro.links)
        {
            if (link.parameterName == paramName)
                return Mathf.Lerp(link.minValue, link.maxValue, macro.defaultValue);
        }
        return baseValue;
    }

    public float EvaluateParamWithMacros(string paramName, float baseValue, float[] macroValues)
    {
        if (macros == null || macroValues == null) return baseValue;
        float result = baseValue;
        for (int m = 0; m < macros.Length && m < macroValues.Length; m++)
        {
            if (macros[m].links == null) continue;
            foreach (var link in macros[m].links)
            {
                if (link.parameterName == paramName)
                    result = Mathf.Lerp(link.minValue, link.maxValue, macroValues[m]);
            }
        }
        return result;
    }

    // ── Preset resolver ─────────────────────────────────────────────
    // Resolve* methods take a preset index (or -1 for Base) and return the
    // effective value for a section. Callers push the resolved values to
    // native, so the audio thread doesn't need to know about presets.

    public void ResolveVolPan(int idx, out float volume, out float pan)
    {
        var p = GetPreset(idx);
        if (p != null && p.ovrVolPan) { volume = p.volume; pan = p.pan; return; }
        volume = this.volume; pan = this.pan;
    }

    public void ResolveAdsr(int idx, out float a, out float d, out float s, out float r)
    {
        var p = GetPreset(idx);
        if (p != null && p.ovrAdsr) { a = p.attack; d = p.decay; s = p.sustain; r = p.release; return; }
        a = attack; d = decay; s = sustain; r = release;
    }

    public float ResolveBlend(int idx)      { var p = GetPreset(idx); return (p != null && p.ovrBlend)      ? p.blend      : blend; }
    public float ResolvePulseWidth(int idx) { var p = GetPreset(idx); return (p != null && p.ovrPulseWidth) ? p.pulseWidth : pulseWidth; }
    public float ResolveWaveBRatio(int idx) { var p = GetPreset(idx); return (p != null && p.ovrBRatio)     ? p.waveBRatio : waveBRatio; }
    public float ResolvePMDepth(int idx)    { var p = GetPreset(idx); return (p != null && p.ovrPMDepth)    ? p.pmDepth    : pmDepth; }
    public float ResolveDetune(int idx)     { var p = GetPreset(idx); return (p != null && p.ovrDetune)     ? p.unisonDetune : unisonDetune; }

    public ZUIEnvelopeData ResolveEnvelope(int idx, EnvParamID paramId)
    {
        var p = GetPreset(idx);
        switch (paramId)
        {
            case EnvParamID.Blend:      return (p != null && p.ovrBlend)      ? p.blendEnvelopeData         : blendEnvelopeData;
            case EnvParamID.PulseWidth: return (p != null && p.ovrPulseWidth) ? p.pulseWidthEnvelopeData    : pulseWidthEnvelopeData;
            case EnvParamID.WaveBRatio: return (p != null && p.ovrBRatio)     ? p.waveBRatioEnvelopeData    : waveBRatioEnvelopeData;
            case EnvParamID.PMDepth:    return (p != null && p.ovrPMDepth)    ? p.pmDepthEnvelopeData       : pmDepthEnvelopeData;
            case EnvParamID.Detune:     return (p != null && p.ovrDetune)     ? p.unisonDetuneEnvelopeData  : unisonDetuneEnvelopeData;
        }
        return null;
    }

    public void ResolveVibrato(int idx, out float depth, out float rate, out float fadeIn, out float randomness)
    {
        var p = GetPreset(idx);
        if (p != null && p.ovrVibrato) { depth = p.vibratoDepth; rate = p.vibratoRate; fadeIn = p.vibratoFadeIn; randomness = p.vibratoRandomness; return; }
        depth = vibratoDepth; rate = vibratoRate; fadeIn = vibratoFadeIn; randomness = vibratoRandomness;
    }

    public void ResolveEffects(int idx, out bool filterEnabled, out int filterMode,
                               out float filterCutoff, out float filterResonance,
                               out float delaySend, out float reverbSend)
    {
        var p = GetPreset(idx);
        if (p != null && p.ovrEffects)
        {
            filterEnabled = p.instFilterEnabled; filterMode = p.instFilterMode;
            filterCutoff = p.instFilterCutoff;   filterResonance = p.instFilterResonance;
            delaySend = p.instDelaySend;         reverbSend = p.instReverbSend;
            return;
        }
        filterEnabled = instFilterEnabled; filterMode = instFilterMode;
        filterCutoff = instFilterCutoff;   filterResonance = instFilterResonance;
        delaySend = instDelaySend;         reverbSend = instReverbSend;
    }

    public void ResolveSynthParams(int idx, out int waveA, out int waveB, out int blendMode,
                                   out int unisonVoices, out float unisonSpread)
    {
        var p = GetPreset(idx);
        if (p != null && p.ovrSynthParams)
        {
            waveA = p.waveA; waveB = p.waveB; blendMode = p.blendMode;
            unisonVoices = p.unisonVoices; unisonSpread = p.unisonSpread;
            return;
        }
        waveA = this.waveA; waveB = this.waveB; blendMode = this.blendMode;
        unisonVoices = this.unisonVoices; unisonSpread = this.unisonSpread;
    }

    public void ResolveSampleParams(int idx, out AudioClip clip, out int baseNote, out float fineTune)
    {
        var p = GetPreset(idx);
        if (p != null && p.ovrSampleParams)
        {
            clip = p.sampleClip; baseNote = p.baseNote; fineTune = p.fineTune;
            return;
        }
        clip = sampleClip; baseNote = this.baseNote; fineTune = this.fineTune;
    }

    public void ResolveSampleBParams(int idx, out AudioClip clipB, out int baseNoteB, out float fineTuneB)
    {
        var p = GetPreset(idx);
        if (p != null && p.ovrSampleParams)
        {
            clipB = p.sampleClipB; baseNoteB = p.baseNoteB; fineTuneB = p.fineTuneB;
            return;
        }
        clipB = sampleClipB; baseNoteB = this.baseNoteB; fineTuneB = this.fineTuneB;
    }

    /// <summary>Push this instrument to native, resolved through the given preset index
    /// (-1 = Base). Tracker pattern command calls this with a per-channel preset index.
    /// Intentionally no no-arg overload: every caller must decide Base (-1) vs. a
    /// specific preset, so editor-preview choices never leak into playback.</summary>
    public void PushToNative(IntPtr ctx, int slotID, int presetIdx)
    {
        switch (type)
        {
            case InstrumentType.Sample:
                PushSampleToNative(ctx, slotID, presetIdx);
                break;
            case InstrumentType.Synth:
                PushSynthToNative(ctx, slotID, presetIdx);
                break;
            case InstrumentType.Kit:
                PushKitToNative(ctx, slotID);
                break;
            case InstrumentType.FM:
                PushFMToNative(ctx, slotID, presetIdx);
                break;
        }

        // Push envelopes (resolver picks preset's copy when override is on)
        PushEnvelope(ctx, slotID, EnvParamID.Blend,      ResolveEnvelope(presetIdx, EnvParamID.Blend));
        PushEnvelope(ctx, slotID, EnvParamID.PulseWidth, ResolveEnvelope(presetIdx, EnvParamID.PulseWidth));
        PushEnvelope(ctx, slotID, EnvParamID.WaveBRatio, ResolveEnvelope(presetIdx, EnvParamID.WaveBRatio));
        PushEnvelope(ctx, slotID, EnvParamID.PMDepth,    ResolveEnvelope(presetIdx, EnvParamID.PMDepth));
        PushEnvelope(ctx, slotID, EnvParamID.Detune,     ResolveEnvelope(presetIdx, EnvParamID.Detune));

        // Effects
        ResolveEffects(presetIdx, out bool filterEnabled, out int filterMode,
                       out float filterCutoff, out float filterResonance,
                       out float delaySend, out float reverbSend);
        ZTrackerNative.ZT_SetInstrumentEffects(ctx, slotID,
            filterEnabled ? 1 : 0, filterMode, filterCutoff, filterResonance,
            delaySend, reverbSend);
    }

    // Called only for already-uploaded sample/synth scalar edits under the playback owner's gate.
    // It does not allocate curves, upload samples or refresh unrelated modulation/effect settings.
    public bool RefreshScalarDefinition(IntPtr ctx, int slotID, int presetIdx)
    {
        if (type == InstrumentType.Synth) { PushSynthToNative(ctx, slotID, presetIdx, false); return true; }
        if (type != InstrumentType.Sample) return false;
        ResolveSampleParams(presetIdx, out AudioClip a, out _, out _);
        ResolveSampleBParams(presetIdx, out AudioClip b, out _, out _);
        if (ctx != s_clipCacheCtx || a == null || !s_clipCache.ContainsKey(a) || (b != null && !s_clipCache.ContainsKey(b))) return false;
        PushSampleToNative(ctx, slotID, presetIdx, false);
        return true;
    }

    void PushSampleToNative(IntPtr ctx, int slotID, int presetIdx, bool modulation = true)
    {
        ResolveSampleParams(presetIdx, out AudioClip clip, out int note, out float tune);
        if (clip == null) return;
        int sampleID = LoadClipToNative(ctx, clip);

        // Sample B is optional. -1 disables blend on the native side.
        ResolveSampleBParams(presetIdx, out AudioClip clipB, out int noteB, out float tuneB);
        int sampleIDB = (clipB != null) ? LoadClipToNative(ctx, clipB) : -1;

        ResolveVolPan(presetIdx, out float v, out float pn);
        ResolveAdsr(presetIdx, out float a, out float d, out float s, out float rl);

        // Blend params reuse the same fields the synth already exposes —
        // blend amount, blendMode, blend ADSR, pmDepth — so a single editor
        // section can drive either instrument type.
        float blendAmt = ResolveBlend(presetIdx);
        float pmd      = ResolvePMDepth(presetIdx);

        ZTrackerNative.ZT_SetSampleInstrument(ctx, slotID, sampleID, note, tune,
            v, pn, a, d, s, rl,
            sampleIDB, noteB, tuneB,
            blendMode, blendAmt, pmd,
            blendEnvelope ? 1 : 0,
            blendAttack, blendDecay, blendSustain, blendRelease);

        if (modulation) PushModulation(ctx, slotID, presetIdx);
    }

    // Per-native-context clip cache. The editor re-pushes the instrument on
    // every field change; without this cache, each push leaks a slot in the
    // native sample bank (MAX_SAMPLES=512), so users hit the cap after a few
    // hundred edits and instruments silently stop playing.
    //
    // Keyed by AudioClip; invalidated when the native context changes (e.g.
    // engine reinit / domain reload), so stale slot IDs from a destroyed
    // context never get handed back.
    static IntPtr s_clipCacheCtx = IntPtr.Zero;
    static System.Collections.Generic.Dictionary<AudioClip, int> s_clipCache
        = new System.Collections.Generic.Dictionary<AudioClip, int>();

    public static void InvalidateClipCache()
    {
        s_clipCache.Clear();
        s_clipCacheCtx = IntPtr.Zero;
    }

    static int LoadClipToNative(IntPtr ctx, AudioClip clip)
    {
        if (ctx != s_clipCacheCtx)
        {
            s_clipCache.Clear();
            s_clipCacheCtx = ctx;
        }
        if (s_clipCache.TryGetValue(clip, out int cachedID))
            return cachedID;

        float[] raw = new float[clip.samples * clip.channels];
        if (!clip.GetData(raw, 0))
            throw new InvalidOperationException("The sample must be readable (Decompress On Load): " + clip.name);

        float[] left, right;
        if (clip.channels >= 2)
        {
            left = new float[clip.samples];
            right = new float[clip.samples];
            for (int i = 0; i < clip.samples; i++)
            {
                left[i]  = raw[i * clip.channels];
                right[i] = raw[i * clip.channels + 1];
            }
        }
        else
        {
            left = raw;
            right = null;
        }

        int id = ZTrackerNative.ZT_LoadSample(ctx, left, right,
            clip.samples, clip.frequency, clip.channels);
        if (id >= 0) s_clipCache[clip] = id;
        return id;
    }

    void PushSynthToNative(IntPtr ctx, int slotID, int presetIdx, bool modulation = true)
    {
        ResolveSynthParams(presetIdx, out int wa, out int wb, out int bm, out int uv, out float us);
        ResolveVolPan(presetIdx, out float v, out float pn);
        ResolveAdsr(presetIdx, out float a, out float d, out float s, out float rl);
        ZTrackerNative.ZT_SetSynthInstrument(ctx, slotID,
            wa, wb, bm,
            ResolveBlend(presetIdx),
            ResolvePMDepth(presetIdx),
            ResolveWaveBRatio(presetIdx),
            blendEnvelope ? 1 : 0,
            blendAttack, blendDecay, blendSustain, blendRelease,
            uv, ResolveDetune(presetIdx), us,
            v, pn, a, d, s, rl,
            ResolvePulseWidth(presetIdx));

        if (modulation) PushModulation(ctx, slotID, presetIdx);
    }

    void PushKitToNative(IntPtr ctx, int slotID)
    {
        if (kitEntries == null || kitEntries.Length == 0) return;

        ZTrackerNative.ZT_SetKitInstrument(ctx, slotID);
        ZTrackerNative.ZT_SetKitOverlap(ctx, slotID, kitOverlap ? 1 : 0);

        for (int i = 0; i < kitEntries.Length; i++)
        {
            var entry = kitEntries[i];
            if (entry.clip == null) continue;

            float[] raw = new float[entry.clip.samples * entry.clip.channels];
            entry.clip.GetData(raw, 0);

            float[] left, right;
            if (entry.clip.channels >= 2)
            {
                left = new float[entry.clip.samples];
                right = new float[entry.clip.samples];
                for (int s = 0; s < entry.clip.samples; s++)
                {
                    left[s] = raw[s * entry.clip.channels];
                    right[s] = raw[s * entry.clip.channels + 1];
                }
            }
            else
            {
                left = raw;
                right = null;
            }

            int sampleID = ZTrackerNative.ZT_LoadSample(ctx, left, right,
                entry.clip.samples, entry.clip.frequency, entry.clip.channels);

            ZTrackerNative.ZT_SetKitEntry(ctx, slotID, entry.midiNote,
                sampleID, entry.baseNote > 0 ? entry.baseNote : 60,
                entry.volume > 0 ? entry.volume : 1f, 0f,
                entry.attack, entry.decay, entry.sustain, entry.release);
        }
    }

    void PushFMToNative(IntPtr ctx, int slotID, int presetIdx)
    {
        ResolveVolPan(presetIdx, out float v, out float pn);
        ZTrackerNative.ZT_SetFMInstrument(ctx, slotID, fmAlgorithm, fmFeedback, v, pn);

        if (fmOperators == null || fmOperators.Length == 0)
        {
            // Default: simple 2-op setup
            ZTrackerNative.ZT_SetFMOperator(ctx, slotID, 0, 1f, 0f, 1f, 0, 0.001f, 0.3f, 0.5f, 0.3f);
            ZTrackerNative.ZT_SetFMOperator(ctx, slotID, 1, 1f, 0f, 1f, 0, 0.001f, 0.2f, 0.7f, 0.3f);
            ZTrackerNative.ZT_SetFMOperator(ctx, slotID, 2, 1f, 0f, 0f, 0, 0.001f, 0.1f, 0f, 0.1f);
            ZTrackerNative.ZT_SetFMOperator(ctx, slotID, 3, 1f, 0f, 1f, 0, 0.001f, 0.2f, 0.7f, 0.3f);
        }
        else
        {
            for (int i = 0; i < fmOperators.Length && i < 4; i++)
            {
                var op = fmOperators[i];
                ZTrackerNative.ZT_SetFMOperator(ctx, slotID, i,
                    op.freqRatio, op.freqFixed, op.level, op.waveform,
                    op.attack, op.decay, op.sustain, op.release);
            }
        }

        PushModulation(ctx, slotID, presetIdx);
    }

    void PushEnvelope(IntPtr ctx, int slotID, EnvParamID paramID, ZUIEnvelopeData envData)
    {
        if (envData == null || !envData.enabled || envData.Count == 0)
        {
            ZTrackerNative.ZT_SetInstrumentEnvelope(ctx, slotID, (int)paramID,
                0, null, 0, 1f, 0, 0, 0f, 1f);
            return;
        }

        var points = new NativeEnvPointData[envData.Count];
        for (int i = 0; i < envData.Count; i++)
        {
            var p = envData.GetPoint(i);
            points[i].time = p.time;
            points[i].value = p.value;
            points[i].exponent = p.exponent;
        }

        ZTrackerNative.ZT_SetInstrumentEnvelope(ctx, slotID, (int)paramID,
            1, points, points.Length, envData.xMax,
            envData.loopEnabled ? 1 : 0, envData.loopMode,
            envData.loopStart, envData.loopEnd);
    }

    void PushModulation(IntPtr ctx, int slotID, int presetIdx)
    {
        ResolveVibrato(presetIdx, out float depth, out float rate, out float fade, out float rnd);
        if (depth > 0)
            ZTrackerNative.ZT_SetInstrumentVibrato(ctx, slotID, depth, rate, fade, rnd);

        if (arpeggioEnabled && arpeggioNotes != null && arpeggioNotes.Length >= 2)
        {
            float[] times = { 0f };
            float[] values = { arpeggioSpeed };
            ZTrackerNative.ZT_SetInstrumentArpeggio(ctx, slotID,
                1, arpeggioNotes, arpeggioNotes.Length, 1, times, values, 1);
        }
    }

    public string GetNoteDisplayName(int midiNote)
    {
        if (type != InstrumentType.Kit || kitEntries == null) return null;
        for (int i = 0; i < kitEntries.Length; i++)
            if (kitEntries[i].midiNote == midiNote)
                return kitEntries[i].displayName;
        return null;
    }
}

public enum InstrumentType
{
    Sample,
    Synth,
    Kit,
    FM
}
