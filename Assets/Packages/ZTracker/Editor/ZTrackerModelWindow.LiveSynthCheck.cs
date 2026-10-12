using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Midi;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        static ZTrackerInstrument LiveSynthAsset(bool fm=false)
        {
            var asset=CreateInstance<ZTrackerInstrument>();asset.schemaVersion=1;asset.model=NewInstrumentData();
            asset.model.synthMode=fm?SynthMode.FM:SynthMode.Subtractive;
            var q=asset.model.parameters;q.type=fm?InstrumentType.FM:InstrumentType.Synth;q.enumDomain=SoundEnumDomain.NativeDirect;q.waveA=q.waveB=0;q.attack=q.decay=0;q.sustain=1;
            for(int n=0;n<q.fmOperators.Length;n++){var op=q.fmOperators[n];op.attack=op.decay=0;op.sustain=1;q.fmOperators[n]=op;}
            return asset;
        }
        static TrackerVoice Held(TrackerOffline engine)=>engine.Snapshot.voices.ToArray().First(v=>v.active&&!v.released);
        static bool SameFmPhase(TrackerVoice a,TrackerVoice b)
        {
            var field=typeof(TrackerVoice).GetField("fmPhase");return Equals(field.GetValue(a),field.GetValue(b));
        }
        public static string CheckLiveSynthUpdates()
        {
            int pass=0,fail=0;var report=new StringBuilder();
            void Need(bool condition,string message){if(!condition)throw new Exception(message);}
            void Case(string label,Action action){try{action();pass++;report.AppendLine("PASS "+label);}catch(Exception ex){fail++;report.AppendLine("FAIL "+label+": "+ex.Message);}}
            var edits=new Dictionary<string,Action<InstrumentParameters>>{
                ["Wave A"]=q=>q.waveA=1,["Wave B"]=q=>q.waveB=2,["Blend mode"]=q=>q.blendMode=1,
                ["FM routing"]=q=>q.fmAlgorithm=5,["Volume"]=q=>q.volume=.6f,["Sustain"]=q=>q.sustain=.3f,
                ["Retained filter enable"]=q=>{q.instFilterEnabled=true;q.instFilterCutoff=.05f;},["Transpose"]=q=>q.transpose=12};
            foreach(var edit in edits)foreach(bool transport in new[]{false,true})
            {
                var chosen=edit;bool running=transport;
                Case(chosen.Key+(running?" during transport":" on held audition"),()=>{
                    var asset=LiveSynthAsset(chosen.Key=="FM routing");var song=ColdSong(asset);
                    try{
                        var q=asset.model.parameters;q.blend=.4f;q.waveB=1;
                        using(var snapshot=PlaybackSnapshot.Create(song,null,-1))using(var controlSnapshot=PlaybackSnapshot.Create(song,null,-1))
                        using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(snapshot.Data)))using(var control=new TrackerOffline(TrackerPreparedSong.Prepare(controlSnapshot.Data)))
                        using(var left=new NativeArray<float>(1024,Allocator.TempJob))using(var right=new NativeArray<float>(1024,Allocator.TempJob))
                        using(var dryL=new NativeArray<float>(1024,Allocator.TempJob))using(var dryR=new NativeArray<float>(1024,Allocator.TempJob))
                        {
                            var start=running?TrackerCommand.Play():TrackerCommand.Audition(0,69);engine.SendCommand(start);control.SendCommand(start);
                            engine.Render(left,right,1024);control.Render(dryL,dryR,1024);var before=engine.Snapshot;var voice=Held(engine);int activeCount=before.voices.ToArray().Count(v=>v.active);
                            chosen.Value(q);
                            using(var nextSnapshot=snapshot.CreateRefresh(asset)){
                                var next=TrackerPreparedSong.Prepare(nextSnapshot.Data);
                                if(!engine.RefreshPrepared(next,out var reason)){next.Dispose();throw new Exception(reason);}
                            }
                            var after=engine.Snapshot;var held=Held(engine);
                            Need(after.samplePosition==before.samplePosition&&after.row==before.row&&after.tick==before.tick&&after.playing==before.playing,"Refresh moved transport");
                            Need(held.cohort==voice.cohort&&held.age==voice.age&&held.phaseA==voice.phaseA&&SameFmPhase(held,voice),"Refresh retriggered held phase/age");
                            Need(after.voices.ToArray().Count(v=>v.active)==activeCount,"Refresh replaced voice membership");
                            engine.Render(left,right,1024);control.Render(dryL,dryR,1024);
                            double difference=0,energy=0;for(int n=0;n<1024;n++){difference+=Math.Abs(left[n]-dryL[n])+Math.Abs(right[n]-dryR[n]);energy+=(double)left[n]*left[n]+(double)right[n]*right[n];}
                            Need(difference>.001&&energy>.0001,"Next PCM block did not audibly change");
                            Need(engine.Snapshot.samplePosition==before.samplePosition+1024,"Timeline did not continue");
                            Need(engine.Compiled,"Render was not actual Burst PCM");
                        }
                    }finally{DestroyImmediate(song);DestroyImmediate(asset);}
                });
            }
            foreach(bool switchEngine in new[]{false,true})
            {
                bool engineSwitch=switchEngine;
                Case(engineSwitch?"Engine switch initializes absent FM operators live and returns to Synth":"Unison expands and contracts held members live",()=>{
                    var asset=LiveSynthAsset();if(engineSwitch)asset.model.parameters.fmOperators=null;var song=ColdSong(asset);
                    try{using(var snapshot=PlaybackSnapshot.Create(song,null,-1))using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(snapshot.Data)))
                        using(var left=new NativeArray<float>(1024,Allocator.TempJob))using(var right=new NativeArray<float>(1024,Allocator.TempJob)){
                            engine.SendCommand(TrackerCommand.Play());engine.Render(left,right,1024);var initial=Held(engine);long position=engine.Snapshot.samplePosition;
                            foreach(int target in new[]{3,1}){
                                if(engineSwitch){asset.model.synthMode=target==3?SynthMode.FM:SynthMode.Subtractive;asset.model.parameters.type=target==3?InstrumentType.FM:InstrumentType.Synth;asset.model.parameters.fmOperators=NewInstrumentData().parameters.fmOperators;}
                                else asset.model.parameters.unisonVoices=target;
                                using(var candidate=snapshot.CreateRefresh(asset)){
                                    var prepared=TrackerPreparedSong.Prepare(candidate.Data);if(!engine.RefreshPrepared(prepared,out var reason)){prepared.Dispose();throw new Exception(reason);}
                                }
                                var active=engine.Snapshot.voices.ToArray().Where(v=>v.active).ToArray();
                                Need(active.Length==(engineSwitch?1:target),"Wrong retained member count");
                                Need(active.All(v=>v.cohort==initial.cohort&&v.age==initial.age),"Engine/member edit retriggered held envelope");
                                Need(engine.Snapshot.samplePosition==position&&engine.Snapshot.playing,"Structural scalar edit restarted transport");
                                engine.Render(left,right,1024);Need(left.ToArray().Sum(x=>(double)x*x)>.0001,"Edited engine/members produced silent PCM");
                                initial=Held(engine);position=engine.Snapshot.samplePosition;
                            }
                        }}finally{DestroyImmediate(song);DestroyImmediate(asset);}
                });
            }
            Case("Transpose remains additive to per-note fine cents curve",()=>{
                var asset=LiveSynthAsset();asset.model.parameters.transpose=12;
                var curve=new ZUIEnvelopeData(0,1,-1200,1200,false){enabled=true};curve.points.Add(new ZUIEnvelopePoint(0,120));curve.points.Add(new ZUIEnvelopePoint(1,120));asset.model.parameters.SetParameterEnvelope("fineTune",curve);
                var song=ColdSong(asset);
                try{using(var snapshot=PlaybackSnapshot.Create(song,null,-1))using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(snapshot.Data)))
                    using(var left=new NativeArray<float>(64,Allocator.TempJob))using(var right=new NativeArray<float>(64,Allocator.TempJob)){
                        engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(left,right,1);var voice=Held(engine);
                        double wanted=880*Math.Pow(2,120/1200d)/engine.Snapshot.sampleRate;
                        Need(Math.Abs(voice.phaseA-wanted)<1e-6,"Fine cents curve cancelled transpose");
                        Need(!float.IsNaN(left[0])&&engine.Compiled,"No compiled render");
                    }}finally{DestroyImmediate(song);DestroyImmediate(asset);}
            });
            foreach(bool glide in new[]{false,true})foreach(bool legato in new[]{false,true}){
                bool slide=glide,overlap=legato;
                Case("Glide="+slide+" Legato="+overlap+" overlapping notes and released retrigger",()=>{
                    var asset=LiveSynthAsset();var q=asset.model.parameters;q.attack=.1f;q.glideEnabled=slide;q.legatoEnabled=overlap;q.glideLegato=false;var song=ColdSong(asset);
                    try{using(var snapshot=PlaybackSnapshot.Create(song,null,-1))using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(snapshot.Data)))
                        using(var left=new NativeArray<float>(128,Allocator.TempJob))using(var right=new NativeArray<float>(128,Allocator.TempJob)){
                            engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(left,right,128);var first=Held(engine);
                            engine.SendCommand(TrackerCommand.Audition(0,72));var second=Held(engine);
                            Need(second.note==72&&second.cohort!=first.cohort,"Replacement note was not started");
                            Need(overlap?second.age==first.age&&second.envelope==first.envelope&&second.phaseA==first.phaseA:second.age==0&&second.envelope==0,"Envelope continuity does not match Legato mode");
                            Need(slide?Math.Abs(second.step*second.glideCurrent-first.step)<1e-7:Math.Abs(second.step/first.step-Math.Pow(2,3/12d))<1e-6,"Pitch slide does not match Glide mode");
                            engine.Render(left,right,128);Need(left.ToArray().Any(x=>Math.Abs(x)>.00001f),"Modes produced silent PCM");
                            engine.SendCommand(new TrackerCommand{kind=TrackerCommandKind.AuditionOff,a=0,b=0});engine.SendCommand(TrackerCommand.Audition(0,76));var released=Held(engine);
                            Need(released.age==0&&released.envelope==0,"Released tail incorrectly reused its envelope");
                        }}finally{DestroyImmediate(song);DestroyImmediate(asset);}
                });
            }
            Case("Unified live keys reuse legato envelope and old note-off leaves new note sounding",()=>{
                var asset=LiveSynthAsset();asset.model.parameters.legatoEnabled=true;asset.model.parameters.attack=.1f;var song=ColdSong(asset);
                try{using(var snapshot=PlaybackSnapshot.Create(song,null,-1))using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(snapshot.Data)))
                    using(var left=new NativeArray<float>(128,Allocator.TempJob))using(var right=new NativeArray<float>(128,Allocator.TempJob)){
                        var router=new ZTrackerMidiRouter();router.SetRoute(engine.LiveInput,0,0,0);
                        router.ComputerKeyDown(69);engine.Render(left,right,128);var first=Held(engine);
                        router.ComputerKeyDown(72);engine.Render(left,right,1);var second=Held(engine);
                        Need(second.note==72&&second.age==first.age+1&&second.envelope>=first.envelope,"Live legato retriggered envelope");
                        Need(engine.Snapshot.voices.ToArray().Count(v=>v.active&&!v.released)==1,"Live legato allocated another column");
                        router.ComputerKeyUp(69);engine.Render(left,right,1);Need(Held(engine).note==72,"Old key-off released replacement note");
                        router.ComputerKeyUp(72);engine.Render(left,right,1);Need(!engine.Snapshot.voices.ToArray().Any(v=>v.active&&!v.released),"Replacement key-off failed to release");
                    }}finally{DestroyImmediate(song);DestroyImmediate(asset);}
            });
            Case("Legato does not inherit another instrument's envelope",()=>{
                var asset=LiveSynthAsset();var other=LiveSynthAsset();asset.model.parameters.legatoEnabled=other.model.parameters.legatoEnabled=true;asset.model.parameters.attack=other.model.parameters.attack=.1f;var song=ColdSong(asset);song.model.instruments.Add(other);
                try{using(var snapshot=PlaybackSnapshot.Create(song,null,-1))using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(snapshot.Data)))
                    using(var left=new NativeArray<float>(128,Allocator.TempJob))using(var right=new NativeArray<float>(128,Allocator.TempJob)){
                        engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(left,right,128);engine.SendCommand(TrackerCommand.Audition(1,72));var voice=Held(engine);
                        Need(voice.instrument==1&&voice.age==0&&voice.envelope==0,"Different instrument reused held envelope");
                    }}finally{DestroyImmediate(song);DestroyImmediate(asset);DestroyImmediate(other);}
            });
            return "Live synth: "+pass+" passed / "+fail+" failed\n"+report;
        }
    }
}
