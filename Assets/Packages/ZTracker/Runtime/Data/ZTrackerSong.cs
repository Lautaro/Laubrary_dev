using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ZTracker/Song", fileName = "NewSong")]
public class ZTrackerSong : ScriptableObject
{
    public string songName = "Untitled";
    public int bpm = 120;
    public int linesPerBeat = 4;
    public int ticksPerRow = 6;
    public int channelCount = 4;

    // Instruments referenced by this song
    public List<ZTrackerInstrument> instruments = new List<ZTrackerInstrument>();

    // Patterns
    public List<ZTrackerPattern> patterns = new List<ZTrackerPattern>();

    // Order list
    public List<int> orderList = new List<int>();

    // Channel config
    public List<ZTrackerChannelConfig> channels = new List<ZTrackerChannelConfig>();

    public void EnsureDefaults()
    {
        if (patterns.Count == 0)
            patterns.Add(new ZTrackerPattern("Pattern 0", 64, channelCount));
        if (orderList.Count == 0)
            orderList.Add(0);
        while (channels.Count < channelCount)
            channels.Add(new ZTrackerChannelConfig());
    }

    // Multi-column fan-out: each logical track claims a *band* of native
    // channels, one per note column. Fx columns are broadcast across the
    // band so each fx gets its own native channel when possible (wraps
    // `i % N` if there are more fx columns than note columns). Instrument
    // and volume are shared — every note column of a track triggers the
    // same instrument. Mute/pan/volume apply to every native channel in
    // the band.
    public int GetTrackNativeBase(int logicalTrack)
    {
        int baseCh = 0;
        for (int t = 0; t < logicalTrack && t < channels.Count; t++)
            baseCh += Mathf.Clamp(channels[t].noteColumnCount, 1, 12);
        return baseCh;
    }

    public int GetTotalNativeChannels()
    {
        int total = 0;
        for (int t = 0; t < channelCount; t++)
        {
            int cols = (t < channels.Count)
                ? Mathf.Clamp(channels[t].noteColumnCount, 1, 12)
                : 1;
            total += cols;
        }
        return total;
    }

    public void PushToNative(IntPtr ctx)
    {
        ZTrackerNative.ZT_SetSongTempo(ctx, bpm, ticksPerRow);
        ZTrackerNative.ZT_SetLinesPerBeat(ctx, linesPerBeat);

        int totalNative = GetTotalNativeChannels();
        ZTrackerNative.ZT_SetChannelCount(ctx, totalNative);

        // Push instruments as *bands* of native slots — one Base + one per preset.
        // The tracker's `I<n>` command switches the channel's active slot within
        // the band natively, so preset switching is sample-accurate instead of
        // being one row behind (which was the case when C# had to re-push).
        //
        // Layout: native slots are allocated contiguously in song-instrument
        // order. Each user instrument gets (1 + presets.Count) slots. The
        // user-facing cell.instrument stays a user index; Sequencer translates
        // via SetPresetMap at note-on.
        int slotCursor = 0;
        for (int i = 0; i < instruments.Count; i++)
        {
            var inst = instruments[i];
            if (inst == null) continue;

            int presetCount = inst.presets != null ? inst.presets.Count : 0;
            int baseSlot = slotCursor;

            // Slot 0 of the band = Base.
            inst.PushToNative(ctx, baseSlot, -1);

            // Slots 1..N of the band = preset variants (0..N-1 in the list).
            for (int p = 0; p < presetCount; p++)
                inst.PushToNative(ctx, baseSlot + 1 + p, p);

            ZTrackerNative.ZT_SetPresetMap(ctx, i, baseSlot, presetCount);
            slotCursor += 1 + presetCount;
        }

        // Push patterns (flattened across multi-column tracks)
        for (int p = 0; p < patterns.Count; p++)
        {
            var pat = patterns[p];
            var data = pat.BuildFlattenedCellData(this, totalNative);
            ZTrackerNative.ZT_SetPatternData(ctx, p, data, pat.rowCount, totalNative);
        }

        // Push order list
        ZTrackerNative.ZT_SetOrderList(ctx, orderList.ToArray(), orderList.Count);

        // Push channel config — mirror each logical track's config onto every
        // native channel in its band.
        for (int t = 0; t < channelCount && t < channels.Count; t++)
        {
            var cfg = channels[t];
            int bandBase = GetTrackNativeBase(t);
            int bandSize = Mathf.Clamp(cfg.noteColumnCount, 1, 12);
            for (int k = 0; k < bandSize; k++)
            {
                int nc = bandBase + k;
                ZTrackerNative.ZT_MuteChannel(ctx, nc, cfg.muted ? 1 : 0);
                ZTrackerNative.ZT_SetChannelVolume(ctx, nc, cfg.volume);
                ZTrackerNative.ZT_SetChannelPan(ctx, nc, cfg.pan);
            }
        }
    }
}

[Serializable]
public class ZTrackerPattern
{
    public string name;
    public int rowCount;
    public List<ZTrackerCellSerialized> cells; // flat: [row * maxChannels + ch]

    public ZTrackerPattern() { }

    public ZTrackerPattern(string name, int rows, int channels)
    {
        this.name = name;
        this.rowCount = rows;
        this.cells = new List<ZTrackerCellSerialized>(rows * channels);
        for (int i = 0; i < rows * channels; i++)
            cells.Add(new ZTrackerCellSerialized());
    }

    public ZTrackerCellSerialized GetCell(int row, int ch, int channelCount)
    {
        int idx = row * channelCount + ch;
        if (idx >= 0 && idx < cells.Count)
            return cells[idx];
        return new ZTrackerCellSerialized();
    }

    public void SetCell(int row, int ch, int channelCount, ZTrackerCellSerialized cell)
    {
        int idx = row * channelCount + ch;
        if (idx >= 0 && idx < cells.Count)
            cells[idx] = cell;
    }

    public TrackerCellData[] GetCellData()
    {
        var data = new TrackerCellData[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            data[i].note = (sbyte)cells[i].note;
            data[i].instrument = (short)cells[i].instrument;
            data[i].volume = (byte)cells[i].volume;
            data[i].effectCmd = (byte)cells[i].effectCmd;
            data[i].effectParam = (byte)cells[i].effectParam;
        }
        return data;
    }

    // Fan out logical cells onto a wider native channel grid. Each logical
    // track `t` occupies N contiguous native channels (one per note column).
    // Note slot k -> native channel bandBase + k. Fx slot i -> native channel
    // bandBase + (i % N) so every fx lands somewhere even if fx columns
    // outnumber note columns (later fx on the same native channel win if
    // they collide — acceptable until we either widen fx slots or add a
    // per-channel fx array natively).
    public TrackerCellData[] BuildFlattenedCellData(ZTrackerSong song, int totalNativeChannels)
    {
        int logicalChannels = song.channelCount;
        var data = new TrackerCellData[rowCount * totalNativeChannels];
        for (int i = 0; i < data.Length; i++)
        {
            data[i].note = -1;
            data[i].instrument = -1;
            data[i].volume = 0xFF;
            data[i].effectCmd = 0;
            data[i].effectParam = 0;
        }

        for (int t = 0; t < logicalChannels; t++)
        {
            int noteCols = 1, fxCols = 1;
            if (t < song.channels.Count)
            {
                noteCols = Mathf.Clamp(song.channels[t].noteColumnCount, 1, 12);
                fxCols   = Mathf.Clamp(song.channels[t].fxColumnCount, 1, 8);
            }
            int bandBase = song.GetTrackNativeBase(t);

            for (int r = 0; r < rowCount; r++)
            {
                var cell = GetCell(r, t, logicalChannels);

                // Notes: one native channel per note column.
                for (int k = 0; k < noteCols; k++)
                {
                    int n = cell.GetNote(k);
                    int dstIdx = r * totalNativeChannels + (bandBase + k);
                    data[dstIdx].note       = (sbyte)n;
                    data[dstIdx].instrument = (short)cell.instrument;
                    data[dstIdx].volume     = (byte)cell.volume;
                }

                // Fx: broadcast across the band, wrapping.
                for (int i = 0; i < fxCols; i++)
                {
                    int cmd = cell.GetEffectCmd(i);
                    int prm = cell.GetEffectParam(i);
                    if (cmd == 0) continue;
                    int dstIdx = r * totalNativeChannels + (bandBase + (i % noteCols));
                    data[dstIdx].effectCmd   = (byte)cmd;
                    data[dstIdx].effectParam = (byte)prm;
                }
            }
        }

        return data;
    }

    public void Resize(int newRows, int newChannels, int oldChannels)
    {
        var newCells = new List<ZTrackerCellSerialized>(newRows * newChannels);
        for (int i = 0; i < newRows * newChannels; i++)
            newCells.Add(new ZTrackerCellSerialized());

        int copyRows = Mathf.Min(rowCount, newRows);
        int copyCh = Mathf.Min(oldChannels, newChannels);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCh; c++)
            {
                int oldIdx = r * oldChannels + c;
                int newIdx = r * newChannels + c;
                if (oldIdx < cells.Count)
                    newCells[newIdx] = cells[oldIdx];
            }

        cells = newCells;
        rowCount = newRows;
    }
}

[Serializable]
public class ZTrackerCellSerialized : ISerializationCallbackReceiver
{
    // Slot 0 stays as scalar fields for back-compat with existing assets. The
    // note-column / fx-column arrays hold slots 1..N when a track is widened
    // past one column. Slot 0 lives here rather than notes[0] / effectCmds[0]
    // so old saves deserialize correctly without a migration pass on every
    // read. Arrays may be empty if the track only has one column.
    public int note = -1;         // slot 0 note. -1=empty, 127=off
    public int instrument = -1;   // -1=none (shared across all note slots)
    public int volume = 0xFF;     // 0xFF=default (shared across all note slots)
    public int effectCmd = 0;     // slot 0 fx command
    public int effectParam = 0;   // slot 0 fx param

    // Slots 1..N for extra note columns (slot 0 == `note` scalar above).
    public List<int> extraNotes = new List<int>();

    // Slots 1..N for extra fx columns (slot 0 == `effectCmd`/`effectParam`).
    public List<int> extraEffectCmds = new List<int>();
    public List<int> extraEffectParams = new List<int>();

    public int GetNote(int slot)
    {
        if (slot <= 0) return note;
        int i = slot - 1;
        return i < extraNotes.Count ? extraNotes[i] : -1;
    }

    public void SetNote(int slot, int value)
    {
        if (slot <= 0) { note = value; return; }
        int i = slot - 1;
        while (extraNotes.Count <= i) extraNotes.Add(-1);
        extraNotes[i] = value;
    }

    public int GetEffectCmd(int slot)
    {
        if (slot <= 0) return effectCmd;
        int i = slot - 1;
        return i < extraEffectCmds.Count ? extraEffectCmds[i] : 0;
    }

    public int GetEffectParam(int slot)
    {
        if (slot <= 0) return effectParam;
        int i = slot - 1;
        return i < extraEffectParams.Count ? extraEffectParams[i] : 0;
    }

    public void SetEffect(int slot, int cmd, int param)
    {
        if (slot <= 0) { effectCmd = cmd; effectParam = param; return; }
        int i = slot - 1;
        while (extraEffectCmds.Count <= i)   extraEffectCmds.Add(0);
        while (extraEffectParams.Count <= i) extraEffectParams.Add(0);
        extraEffectCmds[i]   = cmd;
        extraEffectParams[i] = param;
    }

    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize()
    {
        if (extraNotes == null) extraNotes = new List<int>();
        if (extraEffectCmds == null) extraEffectCmds = new List<int>();
        if (extraEffectParams == null) extraEffectParams = new List<int>();
    }
}

[Serializable]
public class ZTrackerChannelConfig
{
    public float volume = 1f;
    public float pan = 0f;
    public bool muted = false;

    // Multi-column tracks (staged rollout — fields only for now, no UI or
    // behavior hooked up). Track UI still renders 1 note + 1 fx per cell.
    [Range(1, 12)] public int noteColumnCount = 1;
    [Range(1, 8)]  public int fxColumnCount   = 1;
}
