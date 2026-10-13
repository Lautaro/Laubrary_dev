using System.Threading;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    // Live performance layer (a MIDI keyboard, the editor's computer keyboard). It is the engine's only held-note path. Events arrive through the host's live ring and are applied at
    // the start of each rendered block, before any sample of that block is produced. A live note plays through a real
    // track and note column, exactly like a pattern note there: it may cut the column's current note, and the track's
    // mute, solo, volume, pan and chains apply. A chord spreads over the track's note columns, starting at the cursor's
    // column; the number of columns is the polyphony, and the oldest live note in the track gives way when more keys are
    // held than there are columns.
    public unsafe partial struct TrackerRealtime
    {
        public TrackerLiveRing live;

        void DrainLive()
        {
            if (live.control == null) return;
            live.control[7]++;
            // A full ring dropped something (possibly a key-up): release every live note rather than risk a stuck one.
            long dropped = Volatile.Read(ref live.control[2]);
            if (dropped != live.control[6]) { live.control[6] = dropped; LiveReleaseAll(false); }
            int budget = live.capacity;
            while (budget-- > 0 && live.TryRead(out var e))
            {
                ApplyLive(in e);
                Volatile.Write(ref live.control[4], e.serial);
            }
        }

        void ApplyLive(in TrackerLiveEvent e)
        {
            switch (e.kind)
            {
                case TrackerLiveKind.NoteOn: LiveNoteOn(in e); break;
                case TrackerLiveKind.NoteOff:
                {
                    if (e.note >= TrackerLiveRing.NotesPerSource || (int)e.source >= TrackerLiveRing.Sources) break;
                    int key = TrackerLiveRing.Key(e.source, e.note);
                    if (live.control[9] != 0) { if (live.slots[key].active != 0) live.slots[key].held = 0; }
                    else LiveReleaseSlot(key);
                    break;
                }
                case TrackerLiveKind.Sustain:
                    live.control[9] = e.value >= .5f ? 1 : 0;
                    if (live.control[9] == 0) for (int n = 0; n < TrackerLiveRing.NoteSlots; n++) if (live.slots[n].active != 0 && live.slots[n].held == 0) LiveReleaseSlot(n);
                    break;
                case TrackerLiveKind.AllNotesOff: live.control[9] = 0; LiveReleaseAll(false); break;
                case TrackerLiveKind.AllSoundOff: live.control[9] = 0; live.control[10] = 0; LiveReleaseAll(true); break;
                case TrackerLiveKind.PitchBend: if (math.isfinite(e.value)) live.control[10] = math.asint(math.clamp(e.value, -48f, 48f)); break;
                case TrackerLiveKind.Macro:
                    if (e.instrument >= 0 && e.instrument < state.instrumentCount && e.note < 8 && math.isfinite(e.value)) { var c = TrackerCommand.SetMacro(e.instrument, e.note, math.saturate(e.value)); Apply(in c); }
                    break;
            }
        }

        void LiveNoteOn(in TrackerLiveEvent e)
        {
            int track = e.track, note = e.note;
            if (track < 0 || track >= state.trackCount || note > 119 || e.velocity < 1 || e.velocity > 127 || (int)e.source >= TrackerLiveRing.Sources) return;
            if (e.instrument < 0 || e.instrument >= state.instrumentCount || !math.isfinite(e.value)) return;
            var t = state.tracks[track]; int count = t.columnCount; if (count <= 0) return;
            int key = TrackerLiveRing.Key(e.source, note);
            ref var slot = ref live.slots[key];
            int column = -1;
            // The same key again while it still sounds (held or pedal-sustained) retriggers in its own column.
            if (slot.active != 0 && slot.track == track && slot.column < count) column = slot.column;
            else
            {
                if (slot.active != 0) LiveReleaseSlot(key);
                // Explicit synth legato is monophonic within this live route (Poly legato is not: a chord keeps its own columns,
                // and each added note joins the running envelopes, T-0633). Reuse
                // its held column; the old key's note-off cannot release the new cohort.
                long latest=-1;int predecessor=-1;
                for(int n=0;n<TrackerLiveRing.NoteSlots;n++)
                {
                    var candidate=live.slots[n];if(candidate.active==0||candidate.held==0||candidate.track!=track||candidate.column>=count||candidate.order<=latest)continue;
                    for(int i=0;i<state.voices.Length;i++){var voice=state.voices[i];if(voice.active&&!voice.released&&voice.cohort==candidate.cohort&&voice.instrument==e.instrument&&state.tones[voice.sample].legatoEnvelopes&&!state.tones[voice.sample].legatoPoly){column=candidate.column;latest=candidate.order;predecessor=n;break;}}
                }
                if(predecessor>=0){live.slots[predecessor].active=0;live.slots[predecessor].held=0;}
                int start = math.clamp((int)e.column, 0, count - 1);
                for (int k = 0; k < count && column < 0; k++) { int c = (start + k) % count; if (!LiveColumnTaken(track, c)) column = c; }
                if (column < 0)
                {
                    // More keys than columns: the oldest live note in this track gives its column to the new one.
                    int oldest = -1; long first = long.MaxValue;
                    for (int n = 0; n < TrackerLiveRing.NoteSlots; n++) { var s = live.slots[n]; if (s.active != 0 && s.track == track && s.column < count && s.order < first) { first = s.order; oldest = n; } }
                    if (oldest < 0) return;
                    column = live.slots[oldest].column; live.slots[oldest].active = 0;
                }
            }
            int ci = t.columns + column; var col = state.columns[ci]; long before = col.cohort;
            col.volume = math.saturate(e.value); state.columns[ci] = col;
            NoteOn(track, column, e.instrument, note, e.velocity, -1, true);
            long after = state.columns[ci].cohort;
            if (after == before) { slot.active = 0; return; }
            slot.track = track; slot.column = column; slot.cohort = after; slot.order = ++live.control[8]; slot.held = 1; slot.active = 1;
        }

        bool LiveColumnTaken(int track, int column)
        {
            for (int n = 0; n < TrackerLiveRing.NoteSlots; n++) { var s = live.slots[n]; if (s.active != 0 && s.track == track && s.column == column) return true; }
            return false;
        }

        // Releases only the voices the live note started; a pattern note that later took the column is left alone.
        void LiveReleaseSlot(int key)
        {
            ref var slot = ref live.slots[key]; if (slot.active == 0) return;
            long cohort = slot.cohort; slot.active = 0; slot.held = 0;
            // An AutoArp note waits out the release window, or releases the whole arpeggio (T-0642).
            if (state.arpEnabled && ArpKeyOff(cohort)) return;
            for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.active && v.cohort == cohort && !state.samples[v.sample].oneShot) Release(i); }
        }

        // Release: every held or sustained live note. Cut (panic): also silences live notes already in their release tail.
        void LiveReleaseAll(bool cut)
        {
            for (int n = 0; n < TrackerLiveRing.NoteSlots; n++)
            {
                ref var slot = ref live.slots[n];
                if (!cut) { if (slot.active != 0) LiveReleaseSlot(n); continue; }
                long cohort = slot.cohort; slot.active = 0; slot.held = 0; if (cohort <= 0) continue;
                for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.active && v.cohort == cohort) { v.active = false; state.voices[i] = v; } }
            }
        }

        /// <summary>Live pitch-bend offset in semitones for a voice: only voices started by a live note bend, including
        /// their release tail (a released slot keeps its cohort until that key plays again).</summary>
        float LiveBend(long cohort)
        {
            if (live.control == null || live.control[10] == 0 || cohort <= 0) return 0;
            for (int n = 0; n < TrackerLiveRing.NoteSlots; n++) if (live.slots[n].cohort == cohort) return math.asfloat((int)live.control[10]);
            return 0;
        }
    }
}
