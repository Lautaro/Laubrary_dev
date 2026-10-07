using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    // Invoked directly through reflection, without Test Runner or asset writes.
    public static class TrackerInstrumentReworkCheck
    {
        static void Need(bool value,string why) { if(!value)throw new Exception(why); }
        static ZUIEnvelopeData Curve(float start,float end,float seconds=.01f)
        {
            var curve=new ZUIEnvelopeData(0,seconds,Math.Min(start,end),Math.Max(start,end),false);
            curve.points.Add(new ZUIEnvelopePoint(0,start));curve.points.Add(new ZUIEnvelopePoint(seconds,end));return curve;
        }
        static TrackerVoice Voice(TrackerOffline engine)=>engine.Snapshot.voices.ToArray().First(v=>v.active&&!v.released);
        static void InitFm(ZTrackerInstrument instrument)
        {
            if(instrument.model.synthMode!=SynthMode.FM)return;
            instrument.model.parameters.fmOperators=Enumerable.Range(0,4).Select(_=>new ZTrackerInstrument.FMOperatorData{freqRatio=1,level=1,sustain=1,release=1}).ToArray();
        }
        static float Output(TrackerOffline engine,TrackerParameter parameter)
        {
            var voice=Voice(engine);var sample=engine.Snapshot.samples[voice.sample];
            for(int i=0;i<sample.modCount;i++){var mod=engine.Snapshot.mods[sample.modStart+i];if(mod.parameterEnvelope&&mod.parameter==parameter)return engine.Snapshot.modulationState[voice.modulationOffset+i].output;}
            throw new Exception("Missing compiled envelope "+parameter);
        }
        public static string Execute()
        {
            int passed=0,failed=0;var report=new StringBuilder();
            void Check(string label,Action action){try{action();passed++;report.AppendLine("PASS "+label);}catch(Exception ex){failed++;report.AppendLine("FAIL "+label+": "+ex.Message);}}
            using(var l=new NativeArray<float>(8192,Allocator.TempJob))using(var r=new NativeArray<float>(8192,Allocator.TempJob)){
                Check("schema-zero and schema-one arpeggio archived without aliasing or source loss",()=>{
                    var legacy=ScriptableObject.CreateInstance<ZTrackerInstrument>();var modern=TrackerP4Check.Synth();
                    try{
                        foreach(var asset in new[]{legacy,modern}){
                            if(asset.schemaVersion==0){asset.arpeggioEnabled=true;asset.arpeggioNotes=new[]{0,3,7};asset.arpeggioSpeed=.07f;}
                            else{asset.model.parameters.arpeggioEnabled=true;asset.model.parameters.arpeggioNotes=new[]{0,3,7};asset.model.parameters.arpeggioSpeed=.07f;}
                            Need(ZTrackerMigration.Upgrade(asset,out string error),error);
                            Need(!asset.model.parameters.arpeggioEnabled&&asset.model.archivedArpeggio.enabled&&asset.model.archivedArpeggio.notes.SequenceEqual(new[]{0,3,7})&&asset.model.archivedArpeggio.speed==.07f,"Archive lost authored settings");
                            asset.model.parameters.arpeggioNotes[1]=9;Need(asset.model.archivedArpeggio.notes[1]==3,"Archive aliased live data");
                            string json=ZTrackerMigration.Json(asset.model);ZTrackerMigration.Upgrade(asset,out _);Need(json==ZTrackerMigration.Json(asset.model),"Migration not idempotent");
                        }
                        Need(legacy.legacyArchive.arpeggioEnabled&&legacy.legacyArchive.arpeggioNotes[1]==3,"Schema-zero archive changed");
                    }finally{UnityEngine.Object.DestroyImmediate(legacy);UnityEngine.Object.DestroyImmediate(modern);}
                });
                Check("retained enabled instrument arpeggio cannot play without pattern command",()=>{
                    var ins=TrackerP4Check.Synth();try{var q=ins.model.parameters;q.arpeggioEnabled=true;q.arpeggioNotes=new[]{0,4,7};q.arpeggioSpeedPoints=new List<ModulationPoint>{new ModulationPoint{time=0,value=.015f},new ModulationPoint{time=.1,value=.035f}};q.vibratoDepth=35;q.vibratoRate=5;q.vibratoFadeIn=.03f;float[] enabled;
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(ins)))){engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(l,r,6000);Need(engine.Snapshot.tones[0].arpNoteCount==0&&Voice(engine).commandArp==0,"Instrument arp still compiled");Need(Math.Abs(Voice(engine).step-440d/48000)<1e-6,"Instrument arp transposed launch");enabled=l.ToArray().Take(6000).ToArray();Need(q.arpeggioEnabled,"Preparation mutated authored archive");}
                        q.arpeggioEnabled=false;using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(ins)))){engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(l,r,6000);Need(enabled.SequenceEqual(l.ToArray().Take(6000)),"Obsolete arp altered PCM or remaining vibrato");}
                    }finally{UnityEngine.Object.DestroyImmediate(ins);}
                });
                foreach(bool fm in new[]{false,true})Check((fm?"FM":"synth")+" A37 changes rendered pitch by row ticks and blank row stops it",()=>{
                    var ins=TrackerP4Check.Synth(fm);InitFm(ins);try{ins.model.parameters.waveA=0;var song=TrackerEngineCheck.FixtureSong(ins);song.ticksPerLine=6;
                        song.patterns[0].tracks[0].WriteLine(new PatternLine{line=0,notes=new List<NoteCell>{TrackerEngineCheck.Note(69)},effects=new List<EffectCell>{new EffectCell{column=0,command=TrackerP5Check.Fx("0A",0x37)}}});
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(song))){engine.SendCommand(TrackerCommand.Play());engine.Render(l,r,1000);var before=Voice(engine);engine.Render(l,r,1000);var after=Voice(engine);Need(after.commandArp==3,"First offset missing");float prior=fm?before.fmPhase.x:before.phaseA,phase=fm?after.fmPhase.x:after.phaseA;float ratio=fm?ins.model.parameters.fmOperators[0].freqRatio:1;double expected=(prior+1000*440d/48000*Math.Pow(2,.25)*ratio)%1;Need(Math.Abs(phase-expected)<.001,"A37 did not affect oscillator phase");engine.Render(l,r,1000);Need(Voice(engine).commandArp==7,"Second offset missing");engine.Render(l,r,3001);Need(Voice(engine).commandArp==0,"Blank row retained arpeggio");}
                    }finally{UnityEngine.Object.DestroyImmediate(ins);}
                });
                Check("parameter envelope resets on note-on, loop leaves on note-off",()=>{
                    var ins=TrackerP4Check.Synth();try{ins.model.parameters.release=1;var curve=Curve(0,1200,.03f);curve.loopEnabled=true;curve.loopMode=1;curve.loopStart=.005f;curve.loopEnd=.01f;ins.model.parameters.SetParameterEnvelope("fineTune",curve);
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(ins)))){engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(l,r,1200);Need(Output(engine,TrackerParameter.FineTune)<450,"Loop did not hold");engine.SendCommand(TrackerCommand.Audition(0,72));engine.Render(l,r,1);Need(Math.Abs(Output(engine,TrackerParameter.FineTune))<.001,"New note did not retrigger");engine.Render(l,r,1200);var foreground=Voice(engine);engine.SendCommand(new TrackerCommand{kind=TrackerCommandKind.AuditionOff});engine.Render(l,r,1200);var sample=engine.Snapshot.samples[foreground.sample];var mod=engine.Snapshot.mods[sample.modStart];Need(engine.Snapshot.modulationState[foreground.modulationOffset].output>1100,"Release stayed looped");}
                    }finally{UnityEngine.Object.DestroyImmediate(ins);}
                });
                Check("cutoff/resonance/volume/pan/vibrato/FM levels use per-voice outputs",()=>{
                    foreach(bool fm in new[]{false,true}){var ins=TrackerP4Check.Synth(fm);InitFm(ins);try{var q=ins.model.parameters;q.instFilterEnabled=true;
                        q.SetParameterEnvelope("volume",Curve(.2f,.4f));q.SetParameterEnvelope("pan",Curve(-.5f,.5f));q.SetParameterEnvelope("instFilterCutoff",Curve(.1f,.2f));q.SetParameterEnvelope("instFilterResonance",Curve(.5f,2));q.SetParameterEnvelope("vibratoDepth",Curve(0,50));if(fm)q.SetParameterEnvelope("fmOperators.0.level",Curve(.1f,.8f));
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(ins)))){engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(l,r,1000);Need(Math.Abs(Output(engine,TrackerParameter.Volume)-.4f)<.001&&Math.Abs(Output(engine,TrackerParameter.FilterCutoff)-4800)<1,"Physical units wrong");Need(Math.Abs(Voice(engine).filterCutoff-4800)<1&&Math.Abs(Voice(engine).filterQ-2)<.001,"Filter did not consume curve");Need(Math.Abs(Voice(engine).amplitude-.4f)<.001,"Global amplitude multiplied twice");if(fm)Need(Math.Abs(Output(engine,TrackerParameter.Op0Level)-.8f)<.001,"FM level absent");}
                    }finally{UnityEngine.Object.DestroyImmediate(ins);}}
                });
                Check("curve live edits accepted with same topology and added curves refused",()=>{
                    var ins=TrackerP4Check.Synth();try{ins.model.parameters.SetParameterEnvelope("pan",Curve(0,1));var song=TrackerEngineCheck.FixtureSong(ins);
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(song))){engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(l,r,1000);long age=Voice(engine).age;ins.model.parameters.GetParameterEnvelope("pan").points[1].value=.5f;var after=TrackerPreparedSong.Prepare(song);if(!engine.RefreshPrepared(after,out string reason)){after.Dispose();throw new Exception(reason);}engine.Render(l,r,64);Need(Voice(engine).age==age+64&&Math.Abs(Output(engine,TrackerParameter.Pan)-.5f)<.001,"Held curve edit not applied");ins.model.parameters.SetParameterEnvelope("volume",Curve(.1f,.5f));using(var structural=TrackerPreparedSong.Prepare(song))Need(!engine.RefreshPrepared(structural,out _),"Structural addition accepted");}
                    }finally{UnityEngine.Object.DestroyImmediate(ins);}
                });
                Check("sustain holds both existing and new curves until note-off",()=>{
                    var ins=TrackerP4Check.Synth();try{ins.model.parameters.release=1;var pitch=Curve(0,1200,.03f);pitch.sustainEnabled=true;pitch.sustainPosition=.01f;var pulse=Curve(.1f,.9f,.03f);pulse.sustainEnabled=true;pulse.sustainPosition=.01f;ins.model.parameters.SetParameterEnvelope("fineTune",pitch);ins.model.parameters.SetParameterEnvelope("pulseWidth",pulse);
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(ins)))){engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(l,r,2400);var voice=Voice(engine);Need(Math.Abs(Output(engine,TrackerParameter.FineTune)-400)<1&&Math.Abs(voice.pulseCurrent-(.1f+.8f/3))<.002,"Sustain did not hold");engine.SendCommand(new TrackerCommand{kind=TrackerCommandKind.AuditionOff});engine.Render(l,r,2400);var after=engine.Snapshot.voices.ToArray().First(v=>v.active&&v.cohort==voice.cohort);Need(after.pulseCurrent>.89f,"Legacy curve failed to release");Need(engine.Snapshot.modulationState[voice.modulationOffset].output>1199,"Generic curve failed to release");}
                    }finally{UnityEngine.Object.DestroyImmediate(ins);}
                });
                Check("requested note-off semantics exit original ping-pong curve loop",()=>{
                    var ins=TrackerP4Check.Synth();try{var q=ins.model.parameters;q.release=1;q.envelopeEnumDomain=SoundEnumDomain.NativeDirect;var curve=Curve(.2f,.2f,.02f);curve.points.Insert(1,new ZUIEnvelopePoint(.01f,.8f));curve.loopEnabled=true;curve.loopMode=1;curve.loopStart=0;curve.loopEnd=.01f;q.SetParameterEnvelope("pulseWidth",curve);
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(ins)))){engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(l,r,1200);var voice=Voice(engine);Need(voice.pulseCurrent>.35f,"Held ping-pong loop did not run");engine.SendCommand(new TrackerCommand{kind=TrackerCommandKind.AuditionOff});engine.Render(l,r,2400);var released=engine.Snapshot.voices.ToArray().First(v=>v.cohort==voice.cohort&&v.active);Need(Math.Abs(released.pulseCurrent-.2f)<.001,"Released old curve continued looping instead of reaching end");}
                    }finally{UnityEngine.Object.DestroyImmediate(ins);}
                });
                Check("sampler global curves preserve local gain and move both paired pitches",()=>{
                    var clip=AudioClip.Create("instrument-rework-sampler",48000,1,48000,false);var raw=new float[48000];for(int i=0;i<raw.Length;i++)raw[i]=.1f;clip.SetData(raw,0);var ins=TrackerEngineCheck.FixtureInstrument(clip);
                    try{ins.model.sampler.volume=.3f;ins.model.sampler.samples[0].volume=.25f;ins.model.sampler.zones[0].blend=new SampleBlendExtension{pcmB=clip,baseNoteB=60,amount=.5f};ins.model.parameters.SetParameterEnvelope("volume",Curve(.2f,.8f));ins.model.parameters.SetParameterEnvelope("fineTune",Curve(0,1200));ins.model.parameters.SetParameterEnvelope("pan",Curve(0,.5f));
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(ins)))){engine.SendCommand(TrackerCommand.Audition(0,60));engine.Render(l,r,1000);Need(Math.Abs(Voice(engine).amplitude-.2f)<.0001,"Sampler global gain was applied twice");double before=Voice(engine).position,beforeB=Voice(engine).positionB;engine.Render(l,r,100);Need(Math.Abs(Voice(engine).position-before-200)<.01&&Math.Abs(Voice(engine).positionB-beforeB-200)<.01,"Paired sampler pitch curve not consumed by both clips");Need(r[99]>l[99],"Sampler pan curve not consumed");}
                        ins.model.parameters.GetParameterEnvelope("fineTune").enabled=false;ins.model.parameters.SetParameterEnvelope("vibratoDepth",Curve(100,100));
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(ins)))){engine.SendCommand(TrackerCommand.Audition(0,60));engine.Render(l,r,3000);var voice=Voice(engine);Need(Math.Abs(voice.position-voice.positionB)<.01,"New vibrato curve detuned only one member of pair");Need(Math.Abs(voice.position-3000)>1,"Vibrato fixture inaudible");}
                    }finally{UnityEngine.Object.DestroyImmediate(ins);UnityEngine.Object.DestroyImmediate(clip);}
                });
                Check("preset curve overrides and inheritance preserve independent authored points",()=>{
                    var ins=TrackerP4Check.Synth();try{ins.model.parameters.SetParameterEnvelope("pan",Curve(-1,1));var preset=new ZTrackerInstrument.InstrumentPreset{ovrVolPan=true};preset.parameterEnvelopes.Add(new ParameterEnvelope{parameter="pan",envelope=Curve(.2f,.4f)});ins.model.parameters.presets.Add(preset);
                        var sets=ZTrackerMigration.ResolveParameterSets(ins.model);Need(sets[1].data.parameters.GetParameterEnvelope("pan").points[1].value==.4f,"Preset curve ignored");sets[1].data.parameters.GetParameterEnvelope("pan").points[1].value=0;Need(preset.parameterEnvelopes[0].envelope.points[1].value==.4f,"Preset curve aliased");preset.ovrVolPan=false;sets=ZTrackerMigration.ResolveParameterSets(ins.model);Need(sets[1].data.parameters.GetParameterEnvelope("pan").points[1].value==1,"Disabled override lost inheritance");}
                    finally{UnityEngine.Object.DestroyImmediate(ins);}
                });
                Check("trigger mute blocks pattern launch but explicit gameplay note still plays",()=>{
                    var ins=TrackerP4Check.Synth();try{var song=TrackerEngineCheck.FixtureSong(ins);song.tracks[0].triggerMute=true;song.patterns[0].tracks[0].WriteLine(new PatternLine{line=0,notes=new List<NoteCell>{TrackerEngineCheck.Note(69)}});
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(song))){engine.SendCommand(TrackerCommand.Play());engine.Render(l,r,64);Need(!engine.Snapshot.voices.ToArray().Any(v=>v.active),"Gated pattern launched");engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(l,r,64);Need(engine.Snapshot.voices.ToArray().Any(v=>v.active),"Explicit gameplay launch gated");}
                    }finally{UnityEngine.Object.DestroyImmediate(ins);}
                });
                Check("Burst per-note curves render allocation-free",()=>{
                    var ins=TrackerP4Check.Synth();try{ins.model.parameters.SetParameterEnvelope("fineTune",Curve(0,100));ins.model.parameters.SetParameterEnvelope("volume",Curve(.1f,.4f));using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(ins)))){engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(l,r,1024);long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<32;i++)engine.Render(l,r,333,333);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;Need(allocated==0&&engine.Compiled,"Allocated or non-Burst: "+allocated);report.AppendLine("MEASURE allocatedBytes="+allocated+" compiled="+engine.Compiled);}}
                    finally{UnityEngine.Object.DestroyImmediate(ins);}
                });
            }
            return "TrackerInstrumentReworkCheck "+passed+" passed / "+failed+" failed\n"+report;
        }
    }
}
