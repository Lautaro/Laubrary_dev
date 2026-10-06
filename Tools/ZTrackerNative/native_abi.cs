using System;
using System.Runtime.InteropServices;

public static class ZTrackerNative
{
    const string DLL = "ZTrackerEngine";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_GetABIVersion();

    // Pipeline test
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_Add(int a, int b);

    // Engine lifecycle
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ZT_Create(int sampleRate);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_Destroy(IntPtr ctx);

    // Audio thread
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_Process(IntPtr ctx, float[] outL, float[] outR, int numFrames);

    // Sample bank
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_LoadSample(IntPtr ctx, float[] dataL, float[] dataR,
                                           int frameCount, int sampleRate, int channels);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetSampleLoop(IntPtr ctx, int sampleID, int loopMode,
                                               int loopStart, int loopEnd);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetSampleSustainLoop(IntPtr ctx, int sampleID,
                                                      int start, int end);

    // Instruments
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetSampleInstrument(IntPtr ctx, int id,
                                                     int sampleID, int baseNote, float fineTune,
                                                     float volume, float pan,
                                                     float attack, float decay,
                                                     float sustain, float release,
                                                     int sampleIDB, int baseNoteB, float fineTuneB,
                                                     int blendMode, float blendDefault, float pmDepth,
                                                     int blendEnvelopeEnabled,
                                                     float blendAttack, float blendDecay,
                                                     float blendSustain, float blendRelease);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetSynthInstrument(IntPtr ctx, int id,
                                                    int waveA, int waveB,
                                                    int blendMode, float blendDefault, float pmDepth, float waveBRatio,
                                                    int blendEnvelopeEnabled,
                                                    float blendAttack, float blendDecay,
                                                    float blendSustain, float blendRelease,
                                                    int unisonVoices, float unisonDetune, float unisonSpread,
                                                    float volume, float pan,
                                                    float attack, float decay,
                                                    float sustain, float release,
                                                    float pulseWidth);

    // Voice Player
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_NoteOn(IntPtr ctx, int instrumentID, int midiNote, float velocity);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_NoteOff(IntPtr ctx, int voiceID);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_AllNotesOff(IntPtr ctx);

    // Live voice parameters
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetVoicePulseWidth(IntPtr ctx, int voiceID, float pw);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetVoiceWaveBRatio(IntPtr ctx, int voiceID, float ratio);

    // FM Instruments
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetFMInstrument(IntPtr ctx, int id, int algorithm, float feedback,
                                                  float volume, float pan);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetFMOperator(IntPtr ctx, int instrumentID, int opIndex,
                                                float freqRatio, float freqFixed, float level,
                                                int waveform,
                                                float attack, float decay, float sustain, float release);

    // Kit Instruments
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetKitInstrument(IntPtr ctx, int id);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetKitEntry(IntPtr ctx, int instrumentID, int midiNote,
                                             int sampleID, int baseNote,
                                             float volume, float pan,
                                             float attack, float decay, float sustain, float release);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetKitOverlap(IntPtr ctx, int instrumentID, int overlap);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetVoiceBlend(IntPtr ctx, int voiceID, float blend);

    // Modulation
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetInstrumentPortamento(IntPtr ctx, int instrumentID,
                                                         int enabled, float glideTime, int legato);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetInstrumentVibrato(IntPtr ctx, int instrumentID,
                                                      float depth, float rate, float fadeIn, float randomness);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetInstrumentArpeggio(IntPtr ctx, int instrumentID,
                                                       int enabled, int[] notes, int noteCount,
                                                       int speedIsPerNote,
                                                       float[] curveTimes, float[] curveValues, int curvePointCount);

    // Sequencer
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetSongTempo(IntPtr ctx, int bpm, int ticksPerRow);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetLinesPerBeat(IntPtr ctx, int lpb);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetChannelCount(IntPtr ctx, int count);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetPatternData(IntPtr ctx, int patternID, TrackerCellData[] cells,
                                                int rowCount, int channelCount);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetOrderList(IntPtr ctx, int[] order, int length);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_MuteChannel(IntPtr ctx, int channel, int muted);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetChannelVolume(IntPtr ctx, int channel, float volume);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetChannelPan(IntPtr ctx, int channel, float pan);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_Play(IntPtr ctx, int fromOrder, int fromRow);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_Stop(IntPtr ctx);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_GetCurrentRow(IntPtr ctx);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_GetCurrentOrder(IntPtr ctx);

    // Event tracks & queries
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetChannelType(IntPtr ctx, int channel, int type);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetEventString(IntPtr ctx, int patternID, int row, int channel, string str);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetBeatTickInterval(IntPtr ctx, int rows);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern double ZT_GetVoiceCurrentPitch(IntPtr ctx, int voiceID);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_GetVoiceCurrentNote(IntPtr ctx, int voiceID);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_IsVoiceActive(IntPtr ctx, int voiceID);

    // Instrument envelopes
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetInstrumentEnvelope(IntPtr ctx, int instrumentID, int paramID,
                                                       int enabled, NativeEnvPointData[] points, int pointCount,
                                                       float duration, int loopEnabled, int loopMode,
                                                       float loopStart, float loopEnd);

    // Instrument effects
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetInstrumentEffects(IntPtr ctx, int instrumentID,
                                                       int filterEnabled, int filterMode, float cutoff, float resonance,
                                                       float delaySend, float reverbSend);

    // Effects
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetChannelFilter(IntPtr ctx, int channel, int mode, float cutoff, float resonance);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetChannelDelaySend(IntPtr ctx, int channel, float send);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetChannelReverbSend(IntPtr ctx, int channel, float send);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetDelayParams(IntPtr ctx, float time, float feedback, float wet);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetReverbParams(IntPtr ctx, float roomSize, float damp, float wet);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetMasterFilter(IntPtr ctx, int mode, float cutoff, float resonance);

    // Voice parameter modifiers
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetVoicePitchMod(IntPtr ctx, int voiceID, int macroIndex, double ratio);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetVoiceVolumeMod(IntPtr ctx, int voiceID, int macroIndex, double ratio);

    // Macros
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_GetChannelVoiceID(IntPtr ctx, int channel);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern float ZT_GetChannelMacro(IntPtr ctx, int channel, int macroIndex);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_GetChannelPreset(IntPtr ctx, int channel);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_GetChannelInstrument(IntPtr ctx, int channel);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_SetPresetMap(IntPtr ctx, int userInstrumentIndex, int baseSlot, int variantCount);

    // Events
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZT_PollEvent(IntPtr ctx, out ZTrackerEventData outEvent);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZT_FlushEvents(IntPtr ctx);
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct TrackerCellData
{
    public sbyte note;       // -1 = empty, 127 = note off
    public short instrument; // -1 = use previous
    public byte volume;      // 0xFF = default
    public byte effectCmd;
    public byte effectParam;
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct NativeEnvPointData
{
    public float time;
    public float value;
    public float exponent;
}

public enum EnvParamID
{
    Blend = 0,
    PulseWidth = 1,
    WaveBRatio = 2,
    PMDepth = 3,
    Detune = 4
}

public enum ZTrackerEventType : byte
{
    ROW_CHANGED,
    PATTERN_CHANGED,
    SONG_STARTED,
    SONG_STOPPED,
    SONG_LOOPED,
    CHANNEL_NOTE_ON,
    CHANNEL_NOTE_OFF,
    TEMPO_CHANGED,
    EVENT_TRACK_FIRED,
    BEAT_TICK
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct ZTrackerEventData
{
    public byte   type;
    public ulong  samplePosition;
    public int    patternIndex;
    public int    rowIndex;
    public int    channelIndex;
    public int    noteValue;
    public int    instrumentID;
    public int    intParam;
    public float  floatParam;
    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 64)]
    public string stringPayload;
}
