using System;
using System.Text;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    public static class TrackerRetriggerCheck
    {
        public static string Execute()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var instrument = TrackerP4Check.Synth();
            try {
                foreach (var nna in new[] { NewNoteAction.Cut, NewNoteAction.NoteOff, NewNoteAction.Continue })
                foreach (int block in new[] { 64, 333, 1024 }) {
                    try {
                        using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument))))
                        using (var l = new NativeArray<float>(4096, Allocator.Persistent))
                        using (var r = new NativeArray<float>(4096, Allocator.Persistent)) {
                            var state = e.Snapshot;
                            for (int i = 0; i < state.samples.Length; i++) { var s = state.samples[i]; s.nna = nna; state.samples[i] = s; }
                            e.SendCommand(TrackerCommand.Play());
                            e.SendCommand(new TrackerCommand {kind=TrackerCommandKind.TrackMute,a=0,c=1});
                            int peak = 0, events = 0;
                            for (int press = 0; press < 512; press++) {
                                var c = TrackerCommand.Audition(0, 48 + press % 12, 1f, 0); c.kind = TrackerCommandKind.RetriggerNote;
                                e.SendCommand(c); e.Render(l, r, 8, block);
                                int active = 0; long cohort = -1;
                                for (int i = 0; i < state.voices.Length; i++) if (state.voices[i].active) {
                                    active++; var v = state.voices[i];
                                    if (v.note != c.b || cohort >= 0 && cohort != v.cohort) throw new Exception("Old note/group survived retrigger");
                                    cohort = v.cohort;
                                }
                                peak = Math.Max(peak, active);
                                if (active != 1) throw new Exception("Expected one synth voice, got " + active);
                                while (e.ReadEvent(out var ev)) if (ev.kind == TrackerEventKind.NoteOn) events++;
                            }
                            if (events != 512) throw new Exception("Lost note attacks: " + events);
                            e.SendCommand(new TrackerCommand {kind=TrackerCommandKind.AuditionOff,a=0,b=0}); e.Render(l,r,4096,block);
                            for (int i=0;i<state.voices.Length;i++) if(state.voices[i].active) throw new Exception("Release did not finish");
                            if(!e.Compiled)throw new Exception("Expected native renderer");
                            report.AppendLine("PASS " + nna + " block=" + block + ": 512 attacks at 6000/sec; peak voices=" + peak + "; release complete"); passed++;
                        }
                    } catch(Exception ex) {report.AppendLine("FAIL " + nna + " block=" + block + ": " + ex.Message);failed++;}
                }
            } finally { UnityEngine.Object.DestroyImmediate(instrument); }
            return report.AppendLine("TrackerRetriggerCheck: " + passed + " passed, " + failed + " failed").ToString();
        }
    }
}
