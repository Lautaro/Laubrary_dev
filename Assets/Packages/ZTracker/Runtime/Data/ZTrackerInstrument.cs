using System;
using UnityEngine;

[CreateAssetMenu(menuName = "ZTracker/Instrument", fileName = "NewInstrument")]
public class ZTrackerInstrument : ScriptableObject, ISerializationCallbackReceiver
{
    public int schemaVersion;
    public Laubrary.ZTracker.Model.InstrumentData model;
    public Laubrary.ZTracker.Model.InstrumentParameters legacyArchive;
    public System.Collections.Generic.List<string> legacyArchiveNulls = new System.Collections.Generic.List<string>();
    [HideInInspector] public System.Collections.Generic.List<string> serializedNulls;
    public void OnBeforeSerialize()
    {
        if (schemaVersion < 0 || schemaVersion > Laubrary.ZTracker.Model.ZTrackerMigration.CurrentVersion) return;
        if (schemaVersion == Laubrary.ZTracker.Model.ZTrackerMigration.CurrentVersion) Laubrary.ZTracker.Model.ZTrackerMigration.RestoreNulls(legacyArchive,legacyArchiveNulls);
        serializedNulls = Laubrary.ZTracker.Model.ZTrackerMigration.NullPaths(this); serializedNulls.Remove(nameof(serializedNulls));
    }
    public void OnAfterDeserialize()
    {
        if (schemaVersion < 0 || schemaVersion > Laubrary.ZTracker.Model.ZTrackerMigration.CurrentVersion) return;
        Laubrary.ZTracker.Model.ZTrackerMigration.RestoreNulls(this,serializedNulls);
        if (schemaVersion == Laubrary.ZTracker.Model.ZTrackerMigration.CurrentVersion) Laubrary.ZTracker.Model.ZTrackerMigration.RestoreNulls(legacyArchive,legacyArchiveNulls);
    }
    [NonSerialized] internal bool legacyPrepared;
    // Main-thread detached playback wrappers retain the authored source identity.
    [NonSerialized] public int playbackSourceIdentity;
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

        // Per-parameter curves are selected by the same section override as
        // their scalar. Empty/absent entries mean the overridden scalar is static.
        public System.Collections.Generic.List<Laubrary.ZTracker.Model.ParameterEnvelope> parameterEnvelopes = new System.Collections.Generic.List<Laubrary.ZTracker.Model.ParameterEnvelope>();

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
