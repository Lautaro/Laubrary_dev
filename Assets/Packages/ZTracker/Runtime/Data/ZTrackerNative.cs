// Retained managed migration and public event data. No native tracker ABI remains.
public struct TrackerCellData
{
    public sbyte note;       // -1 = empty, 127 = note off
    public short instrument; // -1 = use previous
    public byte volume;      // 0xFF = default
    public byte effectCmd;
    public byte effectParam;
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
    public string stringPayload;
    public string trackId;
    public int noteColumn;
    public int sequenceIndex;
}
