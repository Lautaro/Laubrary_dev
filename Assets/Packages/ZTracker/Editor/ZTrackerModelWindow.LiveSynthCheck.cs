using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Midi;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        public static string LiveSynthUiCheckReport {get;private set;}="";
        public static void StartLiveSynthUiCheck()
        {
            LiveSynthUiCheckReport="RUNNING";var previous=focusedWindow;
            var asset=LiveSynthAsset();var song=ColdSong(asset);var window=CreateInstance<ZTrackerModelWindow>();
            window.song=song;window.instrument=asset;window.entryInstrument=0;window.pane=3;window.midiSimulatedOnly=true;
            window.position=new Rect(120,120,1100,760);window.Show();window.CreateGUI();
            TrackerSapGenerator original=null;double energy=0;long frames=0;int step=0;
            double deadline=EditorApplication.timeSinceStartup+.15;
            EditorApplication.CallbackFunction update=null;
            void Need(bool ok,string message){if(!ok)throw new Exception(message);}
            void Cleanup(){EditorApplication.update-=update;window.StopPreview();window.DisposeMidiPreview();window.Close();DestroyImmediate(song);DestroyImmediate(asset);if(previous!=null)previous.Focus();}
            update=()=>{
                if(EditorApplication.timeSinceStartup<deadline)return;
                try{
                    switch(step++){
                        case 0:window.Play(false);original=window.host;Need(original!=null,"No transport renderer");break;
                        case 1:
                            Need(original.RenderedFrames>0&&original.RenderedEnergy>.0001,"No baseline actual SAP PCM");
                            window.InstrumentEdit("live waveform check",()=>asset.model.parameters.waveA=1,true);
                            window.InstrumentEdit("live transpose check",()=>asset.model.parameters.transpose=7);
                            window.InstrumentEdit("live volume check",()=>asset.model.parameters.volume=0);
                            Need(window.host==original&&!window.pendingLive&&window.lastError==null,"Instrument edits replaced renderer or became pending: "+window.lastError);break;
                        case 2:energy=original.RenderedEnergy;frames=original.RenderedFrames;break;
                        case 3:
                            Need(original.RenderedFrames>frames,"Muted transport stopped rendering");
                            Need(Math.Abs(original.RenderedEnergy-energy)<.00001,"Live zero volume did not silence actual SAP PCM");
                            window.InstrumentEdit("live restore volume check",()=>asset.model.parameters.volume=.25f);
                            Need(window.host==original&&window.playing&&window.lastError==null,"Restore restarted or failed transport");energy=original.RenderedEnergy;break;
                        default:
                            Need(original.RenderedEnergy>energy+.0001,"Live restore produced no actual SAP PCM");
                            LiveSynthUiCheckReport="PASS same SAP host, continued transport, waveform/transpose edits accepted, live volume silenced/restored actual PCM";Cleanup();return;
                    }
                    deadline=EditorApplication.timeSinceStartup+.25;
                }catch(Exception ex){LiveSynthUiCheckReport="FAIL "+ex.Message;Cleanup();}
            };
            EditorApplication.update+=update;
        }
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
                            Need(engine.Snapshot.samplePosition==before.samplePosition+(running?1024:0),"Transport clock does not match playback mode");
                            Need(Held(engine).age==voice.age+1024,"Held audition did not continue rendering its voice");
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
            Case("Modern FM Amplifier ADSR controls initial and held envelopes independently of operator ADSR",()=>{
                var asset=LiveSynthAsset(true);var q=asset.model.parameters;q.attack=.04f;q.decay=.02f;q.sustain=.3f;q.release=.1f;var song=ColdSong(asset);
                try{using(var snapshot=PlaybackSnapshot.Create(song,null,-1))using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(snapshot.Data)))
                    using(var left=new NativeArray<float>(4096,Allocator.TempJob))using(var right=new NativeArray<float>(4096,Allocator.TempJob)){
                        engine.SendCommand(TrackerCommand.Audition(0,69));engine.Render(left,right,1024);var attack=Held(engine);
                        Need(Math.Abs(attack.envelope-1024/(.04*engine.Snapshot.sampleRate))<.001,"FM main Attack ignored visible Amplifier stage");
                        engine.Render(left,right,4096);var sustained=Held(engine);Need(sustained.stage==3&&Math.Abs(sustained.envelope-.3f)<.00001,"FM main Sustain followed operator instead of Amplifier");
                        q.sustain=.8f;using(var candidate=snapshot.CreateRefresh(asset)){
                            var next=TrackerPreparedSong.Prepare(candidate.Data);if(!engine.RefreshPrepared(next,out var reason)){next.Dispose();throw new Exception(reason);}
                        }
                        Need(Math.Abs(Held(engine).envelope-.8f)<.00001,"FM held Amplifier Sustain did not update");
                        engine.Render(left,right,1024);Need(left.ToArray().Take(1024).Sum(x=>(double)x*x)>.0001,"FM Amplifier produced no actual PCM");
                        engine.SendCommand(new TrackerCommand{kind=TrackerCommandKind.AuditionOff,a=0,b=0});var released=engine.Snapshot.voices.ToArray().First(v=>v.active);
                        Need(Math.Abs(released.releaseStep-released.envelope/(.1*engine.Snapshot.sampleRate))<1e-6,"FM main Release ignored Amplifier");
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
