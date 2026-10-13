using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    // AutoArp (T-0642). For an instrument with Legato on, scope Poly and AutoArp on, the notes of that instrument held on a
    // track are played one at a time, in turn, upwards by pitch. Every note keeps its own voice (pattern notes in any column
    // and live keys alike); the track's arpeggio decides which one is heard, fading 1.5 ms between steps so a step change
    // cannot click. State machine (also on the ticket and in Documentation~/OVERHAUL_VERIFICATION.md):
    //   Idle -> a key starts a group: that note sounds at once and its envelopes start.
    //   Held: the arpeggio steps through the notes in it; a new key joins the order by its pitch.
    //   Window: a key released while other keys are held waits (keeps its place) and opens the release window (a later
    //     release does not restart it). When the window ends, every waiting note leaves the arpeggio.
    //   Release: the last held key's release releases every note still in the arpeggio (the last held combination, waiting
    //     notes included), and the arpeggio keeps cycling through that combination until the release has ended.
    //   A key pressed during the release starts a new group; the releasing one fades out.
    // Retrigger: 0 = envelopes start only when a group starts (a joining note takes over the running envelopes, as Poly
    // legato does); 1 (Single) = every step restarts the envelopes of the note it plays (not during the release);
    // 2 (All) = every key press restarts the envelopes of the whole arpeggio. Fixed-size per-track state, no allocation.
    public unsafe partial struct TrackerRealtime
    {
        /// <summary>Seconds a note fades in or out of the arpeggio at a step change.</summary>
        public const float ArpFadeSeconds = .0015f;

        bool IsArpVoice(in TrackerVoice v) => v.active && state.tones[v.sample].autoArp;
        // One voice per note: unison members share the note's cohort; the first member stands for the note.
        bool IsLead(in TrackerVoice v) { int members = state.tones[v.sample].members; return members <= 1 || v.memberSpread == -1f; }
        bool InArp(in TrackerVoice v, int track, in TrackerArpState a) => v.active && !v.arpDropped && v.track == track && v.instrument == a.instrument && state.tones[v.sample].autoArp;

        double ArpStepSamples(in TrackerTone tone, int notes) => math.max(1d, tone.arpStepSeconds * (double)state.sampleRate / (tone.arpPerCycle ? math.max(1, notes) : 1));

        /// A note has just started (NoteOn): it starts a new arpeggio on its track, or joins the one there.
        void ArpNoteStarted(int track, int instrument, long cohort, int note)
        {
            int lead = -1;
            for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.cohort == cohort && v.track == track && IsArpVoice(in v)) { lead = i; break; } }
            if (lead < 0) return;
            var tone = state.tones[state.voices[lead].sample];
            var a = state.arps[track];
            bool join = false;
            if (a.active && !a.releasing && a.instrument == instrument)
                for (int i = 0; i < state.voices.Length && !join; i++) { var v = state.voices[i]; if (v.cohort != cohort && InArp(in v, track, in a) && !v.released) join = true; }
            if (!join)
            {
                // A new arpeggio: whatever was arpeggiating on this track (a release tail, another instrument) fades out.
                for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.track == track && v.cohort != cohort && IsArpVoice(in v)) { v.arpDropped = true; state.voices[i] = v; } }
                a.active = true; a.releasing = false; a.windowOpen = false; a.windowLeft = 0; a.instrument = instrument; a.current = cohort; a.note = note;
                a.stepLeft = ArpStepSamples(in tone, 1);
                for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.cohort == cohort && v.active) { v.arpGain = 1; v.arpWaiting = v.arpDropped = false; state.voices[i] = v; } }
            }
            else
            {
                for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.cohort == cohort && v.active) { v.arpGain = 0; v.arpWaiting = v.arpDropped = false; state.voices[i] = v; } }
                // Per cycle: the whole cycle keeps its length, so the step under way shrinks in proportion (keys of a
                // chord pressed together make the first step a share of the cycle, not the whole of it).
                if (tone.arpPerCycle)
                {
                    int notes = 0; for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (InArp(in v, track, in a) && IsLead(in v)) notes++; }
                    if (notes > 1 && a.stepLeft > 0) a.stepLeft *= (notes - 1) / (double)notes;
                }
                // All: a key press restarts the whole arpeggio's envelopes, from the new note's fresh start.
                if (tone.arpRetrigger == 2)
                    for (int i = 0; i < state.voices.Length; i++)
                    {
                        var v = state.voices[i]; if (v.cohort == cohort || !InArp(in v, track, in a) || v.released) continue;
                        for (int j = 0; j < state.voices.Length; j++) { var s = state.voices[j]; if (s.active && s.cohort == cohort && s.sample == v.sample && s.memberSpread == v.memberSpread) { CopyEnvelopes(ref v, in s); break; } }
                        state.voices[i] = v;
                    }
            }
            state.arps[track] = a;
        }

        /// A legato note took over a held note (its voices moved to a new cohort): the arpeggio follows it, and the note
        /// is held again (a pattern note after an OFF in the same column).
        void ArpCohortMoved(int track, long from, long to, int note)
        {
            var a = state.arps[track];
            if (a.active && a.current == from) { a.current = to; a.note = note; state.arps[track] = a; }
            for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.active && v.cohort == to && v.arpWaiting) { v.arpWaiting = false; state.voices[i] = v; } }
        }

        /// A key (or a pattern OFF) let go of the note <paramref name="cohort"/>. Returns false when it is not an arpeggio
        /// note, so the caller releases it as usual.
        bool ArpKeyOff(long cohort)
        {
            int lead = -1;
            for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.cohort == cohort && IsArpVoice(in v) && !v.released) { lead = i; break; } }
            if (lead < 0) return false;
            var voice = state.voices[lead]; int track = voice.track; var a = state.arps[track];
            if (!a.active || a.releasing || a.instrument != voice.instrument || voice.arpDropped) return false;
            if (voice.arpWaiting) return true;
            bool othersHeld = false;
            for (int i = 0; i < state.voices.Length && !othersHeld; i++) { var v = state.voices[i]; if (v.cohort != cohort && InArp(in v, track, in a) && !v.released && !v.arpWaiting) othersHeld = true; }
            if (othersHeld)
            {
                // Other keys are still held: this note waits out the release window in its place.
                for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.active && v.cohort == cohort) { v.arpWaiting = true; state.voices[i] = v; } }
                if (!a.windowOpen) { a.windowOpen = true; a.windowLeft = state.tones[voice.sample].arpWindowSeconds * (double)state.sampleRate; }
            }
            else
            {
                // The last held key: the release starts on the last held combination (the waiting notes too), and the
                // arpeggio keeps cycling through it until the release ends.
                for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (InArp(in v, track, in a) && !v.released) Release(i); }
                a.releasing = true; a.windowOpen = false;
            }
            state.arps[track] = a;
            return true;
        }

        /// Before a rendered slice: ends a release window that is due, moves to the next note when the step is over (or
        /// the sounding note left), and limits the slice so the next step or window end falls on its boundary.
        int ArpSlice(int n)
        {
            if (!state.arpEnabled) return n;
            for (int t = 0; t < state.trackCount && t < state.arps.Length; t++)
            {
                var a = state.arps[t]; if (!a.active) continue;
                if (a.windowOpen && a.windowLeft <= 0)
                {
                    for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.arpWaiting && InArp(in v, t, in a)) { v.arpDropped = true; state.voices[i] = v; } }
                    a.windowOpen = false;
                }
                bool any = false, current = false;
                for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (!InArp(in v, t, in a)) continue; any = true; if (v.cohort == a.current) current = true; }
                if (!any) { a.active = false; state.arps[t] = a; continue; }
                if (!current) { if (a.stepLeft > 0) a.stepLeft = 0; ArpAdvance(t, ref a); }
                else if (a.stepLeft <= 0) ArpAdvance(t, ref a);
                n = (int)math.min(n, math.max(1, math.ceil(a.stepLeft)));
                if (a.windowOpen) n = (int)math.min(n, math.max(1, math.ceil(a.windowLeft)));
                state.arps[t] = a;
            }
            return math.max(1, n);
        }

        void ArpElapse(int n)
        {
            if (!state.arpEnabled) return;
            for (int t = 0; t < state.trackCount && t < state.arps.Length; t++)
            {
                var a = state.arps[t]; if (!a.active) continue;
                a.stepLeft -= n; if (a.windowOpen) a.windowLeft -= n;
                state.arps[t] = a;
            }
        }

        /// The next note upwards (wrapping to the lowest), its step length, and for Single its fresh envelopes.
        void ArpAdvance(int track, ref TrackerArpState a)
        {
            int next = -1, lowest = -1, count = 0;
            for (int i = 0; i < state.voices.Length; i++)
            {
                var v = state.voices[i]; if (!InArp(in v, track, in a) || !IsLead(in v)) continue;
                count++;
                if (lowest < 0 || v.note < state.voices[lowest].note || v.note == state.voices[lowest].note && v.cohort < state.voices[lowest].cohort) lowest = i;
                bool above = v.note > a.note || v.note == a.note && v.cohort > a.current;
                if (above && (next < 0 || v.note < state.voices[next].note || v.note == state.voices[next].note && v.cohort < state.voices[next].cohort)) next = i;
            }
            if (next < 0) next = lowest;
            if (next < 0) return;
            var chosen = state.voices[next];
            a.current = chosen.cohort; a.note = chosen.note;
            a.stepLeft += ArpStepSamples(state.tones[chosen.sample], count);
            if (state.tones[chosen.sample].arpRetrigger == 1 && !a.releasing)
                for (int i = 0; i < state.voices.Length; i++) { var v = state.voices[i]; if (v.active && v.cohort == a.current) { RetriggerEnvelopes(ref v, i); state.voices[i] = v; } }
        }

        /// Single: the note the step plays starts its envelopes again, as at its note-on.
        void RetriggerEnvelopes(ref TrackerVoice v, int index)
        {
            v.stage = 0; v.envelope = 0; v.envelopePosition = 0; v.envTime = 0; v.envRows = 0;
            v.blendStage = 0; v.blendLevel = 0; v.fmStage = default; v.fmLevel = default;
            var sample = state.samples[v.sample];
            for (int m = 0; m < state.modStride; m++) state.modulationState[v.modulationOffset + m] = default;
            for (int m = 0; m < sample.modCount; m++) { var mod = state.mods[sample.modStart + m]; if (mod.parameterEnvelope) state.modulationState[v.modulationOffset + m] = new TrackerModState { output = Curve(in mod, 0) }; }
        }

        /// How much of a voice the arpeggio lets through this sample (fading towards 1 for the sounding note, 0 for the
        /// others); a note that left the arpeggio stops once it has faded out.
        float ArpGain(ref TrackerVoice v)
        {
            var a = state.arps[v.track];
            // With no arpeggio running on the track (AutoArp switched on while notes were held) a note simply sounds.
            float target = v.arpDropped ? 0f : !a.active || v.cohort == a.current ? 1f : 0f, step = 1f / (ArpFadeSeconds * state.sampleRate);
            v.arpGain = target > v.arpGain ? math.min(target, v.arpGain + step) : math.max(target, v.arpGain - step);
            if (v.arpDropped && v.arpGain <= 0) v.active = false;
            return v.arpGain;
        }
    }
}
