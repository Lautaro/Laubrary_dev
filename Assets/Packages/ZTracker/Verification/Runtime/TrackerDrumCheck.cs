using System;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    // Direct invocation: no Test Runner, project assets, file writes or audio device required.
    public static class TrackerDrumCheck
    {
        static void Need(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        public static string Execute()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Check(string name, Action action) { try { action(); passed++; report.AppendLine("PASS " + name); } catch (Exception e) { failed++; report.AppendLine("FAIL " + name + ": " + e); } }
            var kit = TrackerP4Check.Synth(); var synth = TrackerP4Check.Synth();
            var clip = AudioClip.Create("drum-check", 48000, 1, 48000, false);
            var pcm = new float[48000]; for (int i = 0; i < pcm.Length; i++) pcm[i] = .1f * Mathf.Sin(i * .03f); clip.SetData(pcm, 0);
            kit.model.family = InstrumentFamily.DrumKit;
            kit.model.drumKit.mappings.Add(new DrumMapping { id = "sample", noteMin = 36, noteMax = 48, baseNote = 36, pcm = clip, tag = " Bass Drum ", sourceUrl = "file:///original/Kick.wav" });
            kit.model.drumKit.mappings.Add(new DrumMapping { id = "synth", noteMin = 60, noteMax = 72, baseNote = 45, synth = synth, tag = "Snare", gateSeconds = .01f });
            synth.model.parameters.sustain = 1; synth.model.parameters.release = .01f;
            using (var left = new NativeArray<float>(8192, Allocator.TempJob))
            using (var right = new NativeArray<float>(8192, Allocator.TempJob))
            try
            {
                Check("all aliases normalize without spaces or case", () => {
                    string[][] aliases = { new[]{"KD","Kick","Kick Drum","BD","BassDrum"}, new[]{"Snare","SnareDrum"}, new[]{"Hats","HH","HiHat","HiHats"}, new[]{"Percussion"}, new[]{"Shaker","Shakers"}, new[]{"Misc"}, new[]{"FX","SFX","Effects"} };
                    for (int c = 0; c < aliases.Length; c++) foreach (string alias in aliases[c]) Need((int)DrumTags.Category(" " + alias.ToUpperInvariant() + " ") == c, alias);
                    Need(DrumTags.Matches("Bass Drum", "kick"), "Alias search mismatch");
                });
                Check("one source, disjoint inclusive ranges and no recursive kits", () => {
                    Need(ZTrackerModelValidation.Validate(kit.model) == null, "Valid kit rejected");
                    var m = kit.model.drumKit.mappings[1]; int min = m.noteMin;
                    m.noteMin = 48; Need(ZTrackerModelValidation.Validate(kit.model) != null, "Overlap accepted"); m.noteMin = min;
                    m.synth = kit; Need(ZTrackerModelValidation.Validate(kit.model) != null, "Kit recursion accepted"); m.synth = synth;
                    m.pcm = clip; Need(ZTrackerModelValidation.Validate(kit.model) != null, "Dual source accepted"); m.pcm = null;
                });
                Check("fixed samples overlap, remain after key release and retain source metadata", () => {
                    string before = ZTrackerMigration.Json(kit.model);
                    using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(kit)))) {
                        engine.SendCommand(TrackerCommand.Audition(0,36)); engine.Render(left,right,64);
                        engine.SendCommand(TrackerCommand.Audition(0,48)); engine.Render(left,right,64);
                        engine.SendCommand(new TrackerCommand { kind = TrackerCommandKind.AuditionOff }); engine.Render(left,right,64);
                        var voices = engine.Snapshot.voices.ToArray().Where(v=>v.active).ToArray();
                        Need(voices.Length == 2 && voices.All(v=>!v.released && Math.Abs(v.step-1)<1e-8), "Fixed/overlap/one-shot mismatch");
                        Need(engine.Compiled && left.ToArray().Take(64).Any(v=>Math.Abs(v)>.0001f), "Burst or sound missing");
                    }
                    Need(before == ZTrackerMigration.Json(kit.model), "Preparation changed source or metadata");
                });
                Check("sample range tracks pitch only when selected", () => {
                    kit.model.drumKit.mappings[0].keyTracking = true;
                    try { using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(kit)))) {
                        engine.SendCommand(TrackerCommand.Audition(0,48)); engine.Render(left,right,64);
                        Need(Math.Abs(engine.Snapshot.voices.ToArray().First(v=>v.active).step-2)<1e-8,"Octave range not doubled");
                    }} finally { kit.model.drumKit.mappings[0].keyTracking = false; }
                });
                Check("synth range uses configured pitch and automatically releases", () => {
                    using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(kit)))) {
                        engine.SendCommand(TrackerCommand.Audition(0,60)); engine.Render(left,right,64);
                        engine.SendCommand(TrackerCommand.Audition(0,72)); engine.Render(left,right,64);
                        var voices = engine.Snapshot.voices.ToArray().Where(v=>v.active).ToArray();
                        Need(voices.Length == 2 && voices.All(v=>v.note==45) && Math.Abs(voices[0].step-voices[1].step)<1e-10, "Synth trigger changed pitch");
                        engine.Render(left,right,4096); Need(!engine.Snapshot.voices.ToArray().Any(v=>v.active),"Sustained drum voices leaked: "+string.Join("; ",engine.Snapshot.voices.ToArray().Where(v=>v.active).Select(v=>"age="+v.age+", stage="+v.stage+", released="+v.released+", envelope="+v.envelope+", step="+v.releaseStep+", gate="+engine.Snapshot.samples[v.sample].drumGateSeconds+", release="+engine.Snapshot.samples[v.sample].release)));
                    }
                });
                Check("synth parameter edits preserve refresh identity and sound changes", () => {
                    var song = TrackerEngineCheck.FixtureSong(kit);
                    using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(song))) {
                        synth.model.parameters.volume = .3f;
                        var next = TrackerPreparedSong.Prepare(song);
                        if (!engine.RefreshPrepared(next,out string error)) { next.Dispose(); throw new Exception(error); }
                        engine.SendCommand(TrackerCommand.Audition(0,60)); engine.Render(left,right,64);
                        var v = engine.Snapshot.voices.ToArray().First(x=>x.active);
                        Need(Math.Abs(engine.Snapshot.samples[v.sample].volume-.3f)<1e-6,"Synth dependency stayed stale");
                    }
                });
                Check("FM drum sources retain their engine and configured pitch", () => {
                    var fm = TrackerP4Check.Synth(true);
                    try {
                        kit.model.drumKit.mappings[1].synth = fm;
                        using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(kit)))) {
                            engine.SendCommand(TrackerCommand.Audition(0,72)); engine.Render(left,right,64);
                            var voice = engine.Snapshot.voices.ToArray().First(v=>v.active);
                            Need(voice.note==45 && engine.Snapshot.tones[voice.sample].kind==2,"FM source or pitch changed");
                        }
                    } finally { kit.model.drumKit.mappings[1].synth = synth; UnityEngine.Object.DestroyImmediate(fm); }
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(kit); UnityEngine.Object.DestroyImmediate(synth); UnityEngine.Object.DestroyImmediate(clip); }
            report.Append("TOTAL passed=" + passed + " failed=" + failed); return report.ToString();
        }
    }
}
