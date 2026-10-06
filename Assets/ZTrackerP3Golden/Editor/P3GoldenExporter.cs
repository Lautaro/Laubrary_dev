using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Audio;
using Laubrary.Zounds;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Verification
{
    // Internal repeatable verification entry point. It neither saves authored assets nor adds UI.
    public static class P3GoldenExporter
    {
        [Serializable] public sealed class Corpus { public Fixture[] cases; }
        [Serializable] public sealed class Sample { public int rate, loop, start, end; public float[] left, right; }
        [Serializable] public sealed class Instrument { public int id; public string kind; public float[] values, filter, vibrato, glide; public Entry[] entries; public Envelope[] envelopes; public Arp arp; }
        [Serializable] public sealed class Envelope { public int target,mode; public bool loop; public float start,end; public Entry[] points; }
        [Serializable] public sealed class Arp { public int[] notes; public float[] times,values; public bool perNote; }
        [Serializable] public sealed class Entry { public float[] values; }
        [Serializable] public sealed class Action { public int frame, a, b, expectedVoice; public string kind; public float value; }
        [Serializable] public sealed class Block { public int hostFrame, frames; }
        [Serializable] public sealed class Fixture
        {
            public string id;
            public int frames, buffer, rows;
            public bool sequenced, everyRow;
            public string timeline;
            public float gain, pan;
            public float[] masterFilter, delay, reverb;
            public Sample[] samples;
            public Instrument[] instruments;
            public Action[] actions;
            public Block[] blocks;
        }
        [Serializable] public sealed class Event
        {
            public string kind;
            public long samplePosition;
            public int pattern, row, track, note, instrument;
        }
        [Serializable] public sealed class Trace
        {
            public bool sequenced, compiled;
            public long overflow;
            public List<string> failures = new List<string>();
            public List<Event> events = new List<Event>();
            public List<Observation> observations = new List<Observation>();
        }
        [Serializable] public sealed class Observation { public int frame, member,note; public float detune; public double glide, step; public int algorithm, wave, loop; }

        public static string Execute(string outputDirectory)
        {
            var fixtures = JsonUtility.FromJson<Corpus>(File.ReadAllText(Path.Combine(outputDirectory, "fixtures.json")));
            var result = new StringBuilder();
            Directory.CreateDirectory(Path.Combine(outputDirectory, "candidate"));
            foreach (var fixture in fixtures.cases)
            {
                var temporary = new List<UnityEngine.Object>();
                try { result.AppendLine(Render(fixture, outputDirectory, temporary)); }
                finally { for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]); }
            }
            return result.ToString();
        }

        static string Render(Fixture fixture, string output, List<UnityEngine.Object> temporary)
        {
            string candidateDirectory=Path.Combine(output,fixture.timeline=="p5-policy"?"policy-candidate":"candidate");Directory.CreateDirectory(candidateDirectory);
            var clips = new AudioClip[fixture.samples.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                var sample = fixture.samples[i];
                bool stereo = sample.right != null && sample.right.Length != 0;
                var clip = AudioClip.Create(fixture.id + "-" + i, sample.left.Length, stereo ? 2 : 1, sample.rate, false);
                var pcm = new float[sample.left.Length * (stereo ? 2 : 1)];
                for (int frame = 0; frame < sample.left.Length; frame++)
                {
                    pcm[frame * (stereo ? 2 : 1)] = sample.left[frame];
                    if (stereo) pcm[frame * 2 + 1] = sample.right[frame];
                }
                if (!clip.SetData(pcm, 0)) throw new InvalidOperationException("PCM upload failed");
                clips[i] = clip;
                temporary.Add(clip);
            }
            var song = new SongData { id = fixture.id, voiceCapacity = 128, beatTicks = true };
            foreach (var source in fixture.instruments)
            {
                if (source.id != song.instruments.Count) throw new InvalidOperationException("Sparse fixture instrument IDs");
                var instrument = BuildInstrument(source, clips, fixture.samples, fixture.sequenced);
                temporary.Add(instrument);
                song.instruments.Add(instrument);
            }
            song.tracks.Add(new TrackData { id = "notes", visibleNoteColumns = 1 });
            song.tracks.Add(new TrackData { id = "master", kind = TrackKind.Master, visibleNoteColumns = 0 });
            var master = song.tracks[1];
            if (fixture.masterFilter != null && fixture.masterFilter.Length != 0)
            {
                var v = fixture.masterFilter;
                if (v[0] == 2)
                {
                    // AudioCore has no band-pass insert. This explicitly labeled
                    // characterization uses an HP/LP cascade at the native center.
                    master.devices.nodes.Add(new AudioEffectNodeData { type = ZoundEffectType.HighPass, p = new[] { v[1] * 24000, v[2] } });
                    master.devices.nodes.Add(new AudioEffectNodeData { type = ZoundEffectType.LowPass, p = new[] { v[1] * 24000, v[2] } });
                }
                else master.devices.nodes.Add(new AudioEffectNodeData { type = v[0] == 0 ? ZoundEffectType.LowPass : ZoundEffectType.HighPass, p = new[] { v[1] * 24000, v[2] } });
            }
            if (fixture.delay != null && fixture.delay.Length != 0) master.devices.nodes.Add(Delay(fixture.delay));
            if (fixture.reverb != null && fixture.reverb.Length != 0) master.devices.nodes.Add(Reverb(fixture.reverb));
            bool delaySend = fixture.instruments.Any(i => i.filter != null && i.filter.Length > 5 && i.filter[5] != 0);
            bool reverbSend = fixture.instruments.Any(i => i.filter != null && i.filter.Length > 6 && i.filter[6] != 0);
            if (delaySend) song.tracks.Add(new TrackData { id = "delay-send", kind = TrackKind.Send, visibleNoteColumns = 0, devices = new AudioEffectChainData { nodes = new List<AudioEffectNodeData> { Delay(new[] { .25f, .4f, .3f }) } } });
            if (reverbSend) song.tracks.Add(new TrackData { id = "reverb-send", kind = TrackKind.Send, visibleNoteColumns = 0, devices = new AudioEffectChainData { nodes = new List<AudioEffectNodeData> { Reverb(new[] { .5f, .5f, .3f }) } } });
            var pattern = new PatternData { id = "pattern", lineCount = fixture.rows };
            var notes = new PatternTrack { trackId = "notes" };
            pattern.tracks.Add(notes);
            pattern.tracks.Add(new PatternTrack { trackId = "master" });
            if (delaySend) pattern.tracks.Add(new PatternTrack { trackId = "delay-send" });
            if (reverbSend) pattern.tracks.Add(new PatternTrack { trackId = "reverb-send" });
            if (fixture.sequenced)
            {
                for (int row = 0; row < fixture.rows; row++)
                    if (fixture.everyRow || row == 0)
                        notes.WriteLine(new PatternLine
                        {
                            line = row,
                            notes = new List<NoteCell> { new NoteCell
                            {
                                note = NoteKind.Note, pitch = 69, instrumentPresent = true, instrument = 0,
                                volume = new ColumnValue { kind = ValueKind.Value, value = Mathf.RoundToInt(fixture.gain * 128) },
                                pan = new ColumnValue { kind = ValueKind.Value, value = Mathf.RoundToInt((fixture.pan + 1) * 64) }
                            } }
                        });
            }
            if(fixture.timeline=="portamento"||fixture.timeline=="tremolo"){
                notes.lines.Clear();pattern.lineCount=fixture.timeline=="portamento"?5:6;
                notes.WriteLine(new PatternLine{line=0,notes=new List<NoteCell>{new NoteCell{note=NoteKind.Note,pitch=60,instrumentPresent=true,instrument=0}}});
                if(fixture.timeline=="portamento")notes.WriteLine(new PatternLine{line=1,notes=new List<NoteCell>{new NoteCell{note=NoteKind.Note,pitch=72,instrumentPresent=true,instrument=0}}});
                notes.WriteLine(new PatternLine{line=3,notes=new List<NoteCell>{new NoteCell{note=NoteKind.Off}}});
            }
            if(fixture.timeline=="p5-policy"){
                song.ticksPerLine=4;song.beatTicks=false;
                var instrument=song.instruments[0];instrument.model.provenance="";
                instrument.model.macros[0].value=0;instrument.model.macros[0].mappings.Clear();instrument.model.macros[0].mappings.Add(TrackerP4Check.Route("volume",0,1));
                var device=new SourceDeviceData{id="policy-macro",ordinal=1,kind=SourceDeviceKind.InstrumentMacros,instrumentId=instrument.model.id};device.parameters.Add(new SourceParameterData{ordinal=1});song.tracks[0].sourceDevices.Add(device);
                notes.lines.Clear();notes.WriteLine(new PatternLine{line=0,notes=new List<NoteCell>{new NoteCell{note=NoteKind.Note,pitch=69,instrumentPresent=true,instrument=0,delayPresent=true,delay=128,sampleFx=TrackerP5Check.Fx("0C",128)}},effects=new List<EffectCell>{new EffectCell{command=TrackerP5Check.Fx("11",255)}}});
                notes.automation.Add(new AutomationLane{id="policy-lane",target=new ParameterTarget{kind=ParameterKind.Device,deviceId="policy-macro",index=0},interpolation=AutomationInterpolation.Linear,points=new List<AutomationPoint>{new AutomationPoint{line=0,value=0},new AutomationPoint{line=1,value=1}}});
            }
            song.patterns.Add(pattern);
            song.sequence.Add(new SequenceSlot { id = "slot", patternId = "pattern" });
            var trace = new Trace { sequenced = fixture.sequenced };
            var handles = new Dictionary<int, long>();
            int actionIndex = 0, position = 0;
            using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(song, 48000, Math.Max(4096, fixture.buffer))))
            using (var left = new NativeArray<float>(Math.Max(4096, fixture.buffer), Allocator.Persistent))
            using (var right = new NativeArray<float>(left.Length, Allocator.Persistent))
            using (var stream = File.Create(Path.Combine(candidateDirectory, fixture.id + ".f32")))
            using (var writer = new BinaryWriter(stream))
            {
                foreach (var block in fixture.blocks)
                {
                    if (block.hostFrame != position) throw new InvalidOperationException("Noncontiguous native render schedule");
                    while (actionIndex < fixture.actions.Length && fixture.actions[actionIndex].frame == position)
                    {
                        var action = fixture.actions[actionIndex++];
                        if (action.kind == "play") engine.SendCommand(TrackerCommand.Play());
                        else if (action.kind == "off")
                        {
                            if (!handles.TryGetValue(action.a, out long generation)) throw new InvalidOperationException("Missing native voice handle");
                            engine.SendCommand(TrackerCommand.ReleaseVoice(action.a, generation));
                        }
                        else
                        {
                            engine.SendCommand(TrackerCommand.Audition(action.a, action.b, action.value));
                            int slot = -1;
                            long generation = -1;
                            var voices = engine.Snapshot.voices;
                            for (int i = 0; i < voices.Length; i++)
                                if (voices[i].active && voices[i].instrument == action.a && voices[i].note == action.b && voices[i].cohort > generation)
                                { generation = voices[i].cohort; slot = i; }
                            if (slot != action.expectedVoice) trace.failures.Add("voice at " + position + ": expected " + action.expectedVoice + " actual " + slot);
                            if (slot >= 0) handles[slot] = generation;
                        }
                        Drain(engine, trace);
                    }
                    // A correction adapter invokes the exposed operation; raw command decoding remains P5.
                    bool correction=fixture.id=="command_07"&&position==0;
                    if(correction){engine.Render(left,right,1,1);writer.Write(left[0]);writer.Write(right[0]);engine.SendCommand(TrackerCommand.AmplitudeModifier(0,0,8f/15f,3f));}
                    engine.Render(left, right, block.frames-(correction?1:0), block.frames);
                    for (int frame = 0; frame < block.frames-(correction?1:0); frame++)
                    {
                        if (float.IsNaN(left[frame]) || float.IsInfinity(left[frame]) || float.IsNaN(right[frame]) || float.IsInfinity(right[frame]))
                            throw new InvalidOperationException("Nonfinite candidate audio");
                        writer.Write(left[frame]); writer.Write(right[frame]);
                    }
                    position += block.frames;
                    var snapshot=engine.Snapshot;
                    for(int member=0;member<snapshot.voices.Length;member++){var voice=snapshot.voices[member];if(!voice.active)continue;var tone=snapshot.tones[voice.sample];int loop=-1;for(int env=0;env<5;env++)if(snapshot.toneEnvelopes[tone.envelopes+env].loopEnabled)loop=(int)snapshot.toneEnvelopes[tone.envelopes+env].loop;trace.observations.Add(new Observation{frame=position,member=member,note=voice.note,detune=voice.detuneCurrent,glide=voice.glideCurrent,step=voice.step,algorithm=tone.algorithm,wave=tone.waveA,loop=loop});}
                    Drain(engine, trace);
                }
                while(actionIndex<fixture.actions.Length&&fixture.actions[actionIndex].frame==position){var action=fixture.actions[actionIndex++];if(action.kind!="off"||!handles.TryGetValue(action.a,out long generation))throw new InvalidOperationException("Unsupported endpoint action");engine.SendCommand(TrackerCommand.ReleaseVoice(action.a,generation));Drain(engine,trace);}
                if (position != fixture.frames || actionIndex != fixture.actions.Length) throw new InvalidOperationException(fixture.id+": Incomplete timeline");
                trace.compiled = engine.Compiled;
                trace.overflow = engine.EventOverflow;
            }
            File.WriteAllText(Path.Combine(candidateDirectory, fixture.id + ".engine-events.json"), JsonUtility.ToJson(trace, true));
            return fixture.id + " rendered=" + position + " compiled=" + trace.compiled + " overflow=" + trace.overflow + " failures=" + trace.failures.Count;
        }

        static void Drain(TrackerOffline engine, Trace trace)
        {
            while (engine.ReadEvent(out var value))
                trace.events.Add(new Event { kind = value.kind.ToString(), samplePosition = value.samplePosition,
                    pattern = value.pattern, row = value.row, track = value.track, note = value.note, instrument = value.instrument });
        }

        static ZTrackerInstrument BuildInstrument(Instrument source, AudioClip[] clips, Sample[] samples, bool sequenced)
        {
            var instrument = ScriptableObject.CreateInstance<ZTrackerInstrument>();
            instrument.name = "native-fixture-" + source.id;
            if (source.kind == "sample")
            {
                var v = source.values;
                instrument.type = InstrumentType.Sample;
                instrument.sampleClip = clips[(int)v[1]];
                instrument.baseNote = (int)v[2]; instrument.fineTune = v[3];
                instrument.volume = v[4]; instrument.pan = v[5];
                instrument.attack = v[6]; instrument.decay = v[7]; instrument.sustain = v[8]; instrument.release = v[9];
            }
            else
            {
                if(source.kind=="synth"){
                    var v=source.values;instrument.type=InstrumentType.Synth;instrument.waveA=(int)v[1];instrument.waveB=(int)v[2];instrument.blendMode=(int)v[3];instrument.blend=v[4];instrument.pmDepth=v[5];instrument.waveBRatio=v[6];instrument.blendEnvelope=v[7]!=0;instrument.blendAttack=v[8];instrument.blendDecay=v[9];instrument.blendSustain=v[10];instrument.blendRelease=v[11];instrument.unisonVoices=(int)v[12];instrument.unisonDetune=v[13];instrument.unisonSpread=v[14];instrument.volume=v[15];instrument.pan=v[16];instrument.attack=v[17];instrument.decay=v[18];instrument.sustain=v[19];instrument.release=v[20];instrument.pulseWidth=v[21];
                }else if(source.kind=="fm"){
                    var v=source.values;instrument.type=InstrumentType.FM;instrument.fmAlgorithm=(int)v[1];instrument.fmFeedback=v[2];instrument.volume=v[3];instrument.pan=v[4];instrument.fmOperators=new ZTrackerInstrument.FMOperatorData[4];
                    foreach(var entry in source.entries){var o=entry.values;instrument.fmOperators[(int)o[1]]=new ZTrackerInstrument.FMOperatorData{freqRatio=o[2],freqFixed=o[3],level=o[4],waveform=(int)o[5],attack=o[6],decay=o[7],sustain=o[8],release=o[9]};}
                }else{
                instrument.type = InstrumentType.Kit;
                instrument.kitOverlap = true;
                instrument.kitEntries = new ZTrackerInstrument.KitEntry[source.entries.Length];
                for (int i = 0; i < source.entries.Length; i++)
                {
                    var v = source.entries[i].values;
                    instrument.kitEntries[i] = new ZTrackerInstrument.KitEntry
                    {
                        midiNote = (int)v[1], clip = clips[(int)v[2]], baseNote = (int)v[3], volume = v[4],
                        attack = v[6], decay = v[7], sustain = v[8], release = v[9]
                    };
                }
                }
            }
            if (source.filter != null && source.filter.Length != 0)
            {
                var v = source.filter;
                instrument.instFilterEnabled = v[1] != 0; instrument.instFilterMode = (int)v[2];
                instrument.instFilterCutoff = v[3]; instrument.instFilterResonance = v[4];
                instrument.instDelaySend = v[5]; instrument.instReverbSend = v[6];
            }
            if (!ZTrackerMigration.Upgrade(instrument, out string error)) throw new InvalidOperationException(error);
            var q=instrument.model.parameters;q.enumDomain=SoundEnumDomain.NativeDirect;q.envelopeEnumDomain=source.envelopes!=null&&source.envelopes.Any(e=>e.mode==2)?SoundEnumDomain.SavedAuthoring:SoundEnumDomain.NativeDirect;
            if(source.vibrato!=null&&source.vibrato.Length>0){q.vibratoDepth=source.vibrato[1];q.vibratoRate=source.vibrato[2];q.vibratoFadeIn=source.vibrato[3];q.vibratoRandomness=source.vibrato[4];}
            if(source.glide!=null&&source.glide.Length>0){q.glideEnabled=source.glide[1]!=0;q.glideSeconds=source.glide[2];q.glideLegato=source.glide[3]!=0;}
            if(source.arp!=null&&source.arp.times!=null&&source.arp.times.Length>0){q.arpeggioEnabled=true;q.arpeggioNotes=source.arp.notes;q.arpeggioSpeedIsPerNote=source.arp.perNote;q.arpeggioSpeedPoints=new List<ModulationPoint>();for(int n=0;n<source.arp.times.Length;n++)q.arpeggioSpeedPoints.Add(new ModulationPoint{time=source.arp.times[n],value=source.arp.values[n]});}
            if(source.envelopes!=null)foreach(var src in source.envelopes){var env=new ZUIEnvelopeData{requiresEndPoint=false,enabled=true,loopEnabled=src.loop,loopMode=src.mode,loopStart=src.start,loopEnd=src.end};foreach(var pt in src.points)env.points.Add(new ZUIEnvelopePoint(pt.values[0],pt.values[1],pt.values[2]));switch(src.target){case 0:q.blendEnvelopeData=env;break;case 1:q.pulseWidthEnvelopeData=env;break;case 2:q.waveBRatioEnvelopeData=env;break;case 3:q.pmDepthEnvelopeData=env;break;case 4:q.unisonDetuneEnvelopeData=env;break;}}
            if(source.kind=="sample"&&source.values.Length>10&&source.values[10]>=0){var v=source.values;var loop=samples[(int)v[10]];instrument.model.sampler.zones[0].blend=new SampleBlendExtension{pcmB=clips[(int)v[10]],baseNoteB=(int)v[11],fineTuneBCents=v[12],mode=(int)v[13],amount=v[14],pmDepth=v[15],envelopeEnabled=v[16]!=0,attack=v[17],decay=v[18],sustain=v[19],release=v[20],loopB=loop.loop==1?SampleLoop.Forward:SampleLoop.Off,loopStartFrameB=loop.start,loopEndFrameB=loop.end,blendEnvelope=q.blendEnvelopeData,pmEnvelope=q.pmDepthEnvelopeData};q.blendMode=(int)v[13];q.blend=v[14];q.pmDepth=v[15];q.blendEnvelope=v[16]!=0;q.blendAttack=v[17];q.blendDecay=v[18];q.blendSustain=v[19];q.blendRelease=v[20];}
            if(!sequenced)instrument.model.sampler.nna=NewNoteAction.Continue;
            for (int i = 0; i < instrument.model.sampler.samples.Count; i++)
            {
                var sample = instrument.model.sampler.samples[i];
                int pcm = Array.IndexOf(clips, sample.pcm);
                var loop = samples[pcm];
                sample.loop = loop.loop == 1 ? SampleLoop.Forward : loop.loop == 2 ? SampleLoop.PingPong : SampleLoop.Off;
                sample.loopStartFrame = sample.loop == SampleLoop.Off ? 0 : loop.start;
                sample.loopEndFrame = sample.loop == SampleLoop.PingPong ? Math.Min(loop.end + 1, sample.pcm.samples) : Math.Min(loop.end, sample.pcm.samples);
                if (!sequenced) sample.nna = NewNoteAction.Continue;
                if (source.kind == "kit") sample.pan = source.entries[i].values[5];
            }
            return instrument;
        }

        // Deliberate nearest-parameter mappings for measured characterization,
        // not an assertion that two independently designed effect algorithms match.
        static AudioEffectNodeData Delay(float[] v) => new AudioEffectNodeData { type = ZoundEffectType.Delay, p = new[] { v[0] * 1000, v[1], v[2], 2000f, 0f } };
        static AudioEffectNodeData Reverb(float[] v) => new AudioEffectNodeData { type = ZoundEffectType.Reverb, p = new[] { v[0], v[1], 1f, v[2] } };
    }
}
