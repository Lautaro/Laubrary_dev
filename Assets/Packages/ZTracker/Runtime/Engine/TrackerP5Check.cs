using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>Internal conformance fixtures for the proposed command profile. No authoring writes or audio device.</summary>
    public static class TrackerP5Check
    {
        public static CommandData Fx(string id,int value=0,bool present=true)=>new CommandData{present=true,valuePresent=present,identifier=id,value=value};
        static ColumnValue Col(string id)=>new ColumnValue{kind=ValueKind.Command,command=Fx(id)};
        static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
        static void Near(double a,double b,string why)=>Need(Math.Abs(a-b)<=1e-6,why+": "+a+" expected "+b);
        sealed class Fixture:IDisposable
        {
            public ZTrackerInstrument instrument;
            public SongData song;
            public int block;
            public NativeArray<float> left,right;
            public Fixture(AudioClip pcm,int block)
            {
                this.block=block;instrument=TrackerEngineCheck.FixtureInstrument(pcm);instrument.model.provenance="";
                instrument.model.sampler.samples[0].loop=SampleLoop.Forward;instrument.model.sampler.samples[0].loopStartFrame=0;instrument.model.sampler.samples[0].loopEndFrame=pcm.samples;
                song=TrackerEngineCheck.FixtureSong(instrument,2,32);song.ticksPerLine=4;
                left=new NativeArray<float>(32768,Allocator.Persistent);right=new NativeArray<float>(32768,Allocator.Persistent);
            }
            public NoteCell Note(int pitch=60,int col=0)=>TrackerEngineCheck.Note(pitch,col);
            public void Row(int row,NoteCell note=null,params CommandData[] effects)
            {
                var line=new PatternLine{line=row};if(note!=null)line.notes.Add(note);for(int i=0;i<effects.Length;i++)line.effects.Add(new EffectCell{column=i,command=effects[i]});song.patterns[0].tracks[0].WriteLine(line);
            }
            public TrackerOffline Open(){var e=new TrackerOffline(TrackerPreparedSong.Prepare(song));e.SendCommand(TrackerCommand.Play());return e;}
            public void Render(TrackerOffline e,int frames){while(frames>0){int n=Math.Min(frames,left.Length);e.Render(left,right,n,block);frames-=n;}}
            public void MacrosDevice(int ordinal=1)
            {
                var d=new SourceDeviceData{id="macros",ordinal=ordinal,kind=SourceDeviceKind.InstrumentMacros,instrumentId=instrument.model.id};for(int m=0;m<8;m++)d.parameters.Add(new SourceParameterData{ordinal=m+1,defaultValue=instrument.model.macros[m].value});song.tracks[0].sourceDevices.Add(d);
            }
            public AutomationLane Lane(int parameter=0)
            {
                var lane=new AutomationLane{id="lane",interpolation=AutomationInterpolation.Linear,target=new ParameterTarget{kind=ParameterKind.Device,deviceId="macros",index=parameter,parameter="slot"}};lane.points.Add(new AutomationPoint{line=0,value=0});lane.points.Add(new AutomationPoint{line=1,value=1});song.patterns[0].tracks[0].automation.Add(lane);return lane;
            }
            public TrackerVoice Foreground(TrackerOffline e,int column=0){long cohort=e.Snapshot.columns[column].cohort;return e.Snapshot.voices.ToArray().First(v=>v.active&&v.cohort==cohort);}
            public void Dispose(){left.Dispose();right.Dispose();UnityEngine.Object.DestroyImmediate(instrument);}
        }
        public static string Execute()
        {
            int pass=0,fail=0;var report=new StringBuilder();var pcm=AudioClip.Create("P5 conformance",48000,1,48000,false);var raw=new float[48000];for(int i=0;i<raw.Length;i++)raw[i]=.1f;pcm.SetData(raw,0);
            Action<string,Action<Fixture>> check=(name,run)=>{try{foreach(int block in new[]{64,333,1024})using(var f=new Fixture(pcm,block))run(f);pass++;report.AppendLine("PASS "+name+" [64/333/1024]");}catch(Exception ex){fail++;report.AppendLine("FAIL "+name+": "+ex.Message);}};
            try
            {
                check("01 decode omission and base36",f=>{
                    f.MacrosDevice(10);var n=f.Note();n.sampleFx=Fx("0U",16);f.Row(0,n);n=new NoteCell{sampleFx=Fx("0U",0,false)};f.Row(1,n,Fx("A1",0,false));
                    using(var e=f.Open()){f.Render(e,12000);Near(f.Foreground(e).commandPitch,2,"complete zero-repeat");Near(e.ObserveMacro(0,0),0,"base36 parameter omitted zero");}
                    using(var prepared=TrackerPreparedSong.Prepare(f.song))Need(prepared.diagnostics.Count(d=>d.Contains("IMPLICIT_ZERO"))==2,"omission diagnostics");
                });
                check("02 numeric80 and malformed81",f=>{
                    var n=f.Note();n.pan=new ColumnValue{kind=ValueKind.Value,value=64};f.Row(0,n);using(var e=f.Open()){f.Render(e,1);Near(f.Foreground(e).volume,1,"gain80");Near(f.Foreground(e).pan,0,"pan40");}
                    n.volume=new ColumnValue{kind=ValueKind.Legacy,legacyValue=129,hasLegacy=true};using(var p=TrackerPreparedSong.Prepare(f.song))Need(n.volume.legacyValue==129,"raw81 lost");
                });
                check("03 Kahan clock stamps and million-row limits",f=>{
                    f.song.bpm=137.5;f.song.ticksPerLine=6;using(var e=f.Open()){f.Render(e,63000);var stamps=new List<long>();while(e.ReadEvent(out var ev))if(ev.kind==TrackerEventKind.Row)stamps.Add(ev.samplePosition);Need(stamps.Take(6).SequenceEqual(new long[]{0,5236,10472,15709,20945,26181})&&stamps[11]==57600&&stamps[12]==62836,"Kahan frames");}
                    long last=-1;double start=0,c=0,d=(48000*60d)/(137.5*4);for(int row=0;row<1000000;row++){long q=TrackerRealtime.Quantize(start);Need(q>last,"million monotonic");last=q;double y=d-c,next=start+y;c=(next-start)-y;start=next;}Near(TrackerRealtime.Quantize(57600-1e-11),57600,"ULP boundary snap");
                });
                check("04 pitch saturation and recovery",f=>{
                    f.song.patterns[0].lineCount=512;for(int row=0;row<512;row++)f.Row(row,row==0?f.Note():null,Fx("0U",255));var next=ZTrackerMigration.Copy(f.song.patterns[0]);next.id="continued";next.tracks[0].lines[0].notes.Clear();f.song.patterns.Add(next);f.song.sequence.Add(new SequenceSlot{id="continue",patternId="continued"});using(var e=f.Open()){f.Render(e,772*6000);var v=f.Foreground(e);Near(v.commandPitch,1536,"772 UFF bounded");Need(v.active&&v.pitchLimited&&v.position>=0&&v.position<48000,"bounded loop cursor");e.SendCommand(TrackerCommand.SetParameter(0,TrackerParameter.FineTune,12000));f.Render(e,1);Need(e.Snapshot.voices[0].active,"finite extreme pitch");}
                });
                check("05 slide then glide tracker coordinate",f=>{
                    f.Row(0,f.Note(),Fx("0U",32));f.Row(1,new NoteCell{note=NoteKind.Note,pitch=64},Fx("0G",16));f.Row(2,null,Fx("0G",16));using(var e=f.Open()){f.Render(e,12000);Near(f.Foreground(e).commandPitch,3,"glide starts62 ends63");f.Render(e,6000);Near(f.Foreground(e).commandPitch,4,"glide reaches64");Need(f.Foreground(e).note==60,"immutable launch note");}
                });
                check("06 first tick and delayed slide",f=>{
                    var n=f.Note();n.sampleFx=Fx("0U",16);f.Row(0,n);using(var e=f.Open()){f.Render(e,6000);Near(f.Foreground(e).commandPitch,1,"four active ticks");}n.delayPresent=true;n.delay=128;using(var e=f.Open()){f.Render(e,6000);Near(f.Foreground(e).commandPitch,.5,"late two ticks");var note=new List<long>();while(e.ReadEvent(out var ev))if(ev.kind==TrackerEventKind.NoteOn)note.Add(ev.samplePosition);Need(note.Single()==3000,"exact delayed frame");}
                });
                check("07 Q precedence plus fractional delay",f=>{
                    var n=f.Note();n.volume=Col("Q1");n.pan=Col("Q0");n.delayPresent=true;n.delay=64;f.Row(0,n,Fx("0Q",2));using(var e=f.Open()){f.Render(e,1501);Need(f.Foreground(e).age==1,"pan Q0 frame1500");}n.pan=new ColumnValue();using(var e=f.Open()){f.Render(e,3001);Need(f.Foreground(e).age==1,"volumeQ1 frame3000");}n.volume=Col("Q4");using(var e=f.Open()){f.Render(e,6000);Need(!e.Snapshot.voices.ToArray().Any(v=>v.active),"outside delay launches");}
                });
                check("08 cut binding boundaries and OFF",f=>{
                    f.Row(0,f.Note());var n=f.Note(64);n.delayPresent=true;n.delay=128;n.pan=Col("C1");f.Row(1,n);using(var e=f.Open()){f.Render(e,12000);Need(f.Foreground(e).note==60,"cut waiting successor cut predecessor");}n.pan=new ColumnValue();n.sampleFx=Fx("0C",0xf1);using(var e=f.Open()){f.Render(e,10000);Near(f.Foreground(e).commandGain,1,"early C initializes successor");}
                });
                check("09 conflict families and valid writer precedence",f=>{
                    var n=f.Note();n.sampleFx=Fx("0U",48);n.volume=Col("D2");n.pan=Col("G1");f.Row(0,n,Fx("0D",32),Fx("ZT",144),Fx("ZT",160));f.song.patterns[0].tracks[1].WriteLine(new PatternLine{line=0,effects=new List<EffectCell>{new EffectCell{command=Fx("0U",16)}}});using(var e=f.Open()){f.Render(e,4500);Near(e.Snapshot.bpm,160,"last timing writer");Near(f.Foreground(e).commandPitch,0,"G suppresses U/D");Need(e.Snapshot.commandMemory.ToArray().Count(v=>v>0)>=4,"suppressed source memory not resolved");}
                });
                check("10 complete memory banks and LFO blank freeze",f=>{
                    f.Row(0,f.Note(),Fx("0V",0x47));f.Row(1,null,Fx("0V",0));f.Row(2,null,Fx("0V",0x30));using(var e=f.Open()){f.Render(e,18000);Near(f.Foreground(e).commandVibrato,0,"V30 depth zero");float phase=f.Foreground(e).phaseV;f.Render(e,6000);Near(f.Foreground(e).phaseV,phase,"blank phase freeze");Near(f.Foreground(e).commandVibrato,0,"blank neutral");}
                });
                check("11 fresh note, pause, stop resets",f=>{
                    var n=f.Note();n.volume=new ColumnValue{kind=ValueKind.Value,value=32};n.pan=new ColumnValue{kind=ValueKind.Value,value=0};f.Row(0,n,Fx("0U",16));f.Row(1,f.Note(64));using(var e=f.Open()){f.Render(e,6001);Near(f.Foreground(e).volume,1,"fresh gain");Near(f.Foreground(e).pan,0,"fresh pan");Near(f.Foreground(e).commandPitch,0,"fresh pitch");long age=f.Foreground(e).age,pos=e.Snapshot.samplePosition;e.SendCommand(new TrackerCommand{kind=TrackerCommandKind.Pause});f.Render(e,6000);Need(e.Snapshot.samplePosition==pos&&f.Foreground(e).age==age,"pause advances");e.SendCommand(new TrackerCommand{kind=TrackerCommandKind.Resume});f.Render(e,1);Need(f.Foreground(e).age==age+1,"resume");e.SendCommand(TrackerCommand.Stop());Need(e.Snapshot.commandMemory.ToArray().All(x=>x==0)&&e.Snapshot.columns[0].instrument==-1,"stop state retained");}
                });
                check("12 layered NNA foreground and tail isolation",f=>{
                    var s=ZTrackerMigration.Copy(f.instrument.model.sampler.samples[0]);s.id="layer";s.nna=NewNoteAction.NoteOff;f.instrument.model.sampler.samples[0].nna=NewNoteAction.Continue;f.instrument.model.sampler.samples.Add(s);var z=ZTrackerMigration.Copy(f.instrument.model.sampler.zones[0]);z.id="layer-zone";z.sample=1;f.instrument.model.sampler.zones.Add(z);f.Row(0,f.Note());var n=f.Note(64);n.delayPresent=true;n.delay=128;n.sampleFx=Fx("0U",16);f.Row(1,n);using(var e=f.Open()){f.Render(e,9001);Need(e.Snapshot.voices.ToArray().Count(v=>v.active)==4,"layers not retained");f.Render(e,3000);Need(e.Snapshot.voices.ToArray().Where(v=>v.active&&v.note==60).All(v=>v.commandPitch==0),"detached tails changed");}
                });
                check("13 V T N independent formulas",f=>{
                    f.Row(0,f.Note(),Fx("0V",0x48),Fx("0T",0x48),Fx("0N",0x48));using(var e=f.Open()){f.Render(e,3001);var v=f.Foreground(e);Near(v.commandVibrato,.5*Math.Sin(Math.PI/4),"vibrato");Near(v.commandTremolo,1-(8d/15)*(1-Math.Cos(Math.PI/4))/2,"tremolo");Near(v.commandPan,(8d/15)*Math.Sin(Math.PI/4),"autopan");}
                });
                check("14 arpeggio and additive gain",f=>{
                    f.Row(0,f.Note(),Fx("0A",0x37),Fx("0C",0x80),Fx("0O",16));using(var e=f.Open()){f.Render(e,1501);Near(f.Foreground(e).commandArp,3,"arp phase1");f.Render(e,1500);Near(f.Foreground(e).commandArp,7,"arp phase2");f.Render(e,1500);Near(f.Foreground(e).commandArp,0,"arp phase0");Near(f.Foreground(e).commandGain,8d/15-1d/16,"additive O");}
                });
                check("15 slices offsets and reverse",f=>{
                    var sample=f.instrument.model.sampler.samples[0];sample.sliceMarkers=new List<int>{0,200,600};f.Row(0,f.Note(),Fx("0B",0),Fx("0S",2));using(var e=f.Open()){f.Render(e,1);var v=e.Snapshot.voices[0];Need(v.regionStart==200&&v.regionEnd==600&&v.position<200,"slice region reverse starts marker");}sample.sliceMarkers.Clear();f.Row(0,f.Note(),Fx("0S",128));using(var e=f.Open()){f.Render(e,1);Need(f.Foreground(e).selectedStart==24000,"half PCM");}
                });
                check("16 E independent clocks and Stepper preservation",f=>{
                    var set=f.instrument.model.modulation[0];set.devices[0].attack=.2f;set.devices[0].hold=.1f;set.devices[0].decay=.3f;set.devices.Add(new ModulationDevice{kind=ModulationDeviceKind.Multipoint,target=ModulationTarget.Pan,points=new List<ModulationPoint>{new ModulationPoint{time=0,value=0},new ModulationPoint{time=2,value=1}}});set.devices.Add(new ModulationDevice{kind=ModulationDeviceKind.Fader,target=ModulationTarget.Pitch,duration=4,min=0,max=1});set.devices.Add(new ModulationDevice{kind=ModulationDeviceKind.Stepper,target=ModulationTarget.Pitch});f.Row(0,f.Note(),Fx("0E",128));using(var e=f.Open()){f.Render(e,1);Near(f.Foreground(e).envelopePosition,.3+1d/48000,"held AHDSR");Near(e.Snapshot.modulationState[1].position,1+1d/48000,"multi point");Near(e.Snapshot.modulationState[2].position,2+1d/48000,"fader");Near(e.Snapshot.modulationState[3].position,0,"Stepper unchanged");}
                });
                check("17 retrigger interval boundaries",f=>{
                    f.song.ticksPerLine=12;f.Row(0,f.Note(),Fx("0R",4));using(var e=f.Open()){f.Render(e,4001);Need(f.Foreground(e).age==1,"retriggers at4 and8");f.Render(e,1999);Need(f.Foreground(e).age==2000,"no tick12 retrigger");}
                    f.Row(0,f.Note(),Fx("0R",0));using(var p=TrackerPreparedSong.Prepare(f.song))Need(p.diagnostics.Any(d=>d.Contains("ZERO_RETRIGGER_INTERVAL")),"R00 not retained");
                });
                check("18 retrigger factors",f=>{int[] x={1,6,7,14,15};double[] expected={.47,.335,.25,.75,1};for(int i=0;i<x.Length;i++)Near(TrackerRealtime.RetriggerFactor(.5f,x[i]),expected[i],"factor"+x[i]);});
                check("19 deterministic random and exclusive weights",f=>{
                    Need(TrackerRealtime.CounterHash(0,0,0,0,0,0,ulong.MaxValue,3)==0x75b233c5098d5a1bUL,"hash oracle");Need(TrackerRealtime.CounterHash(0,0,0,0,1,0,ulong.MaxValue,3)==0x352f77727ec8a36dUL,"occurrence oracle");var row=new PatternLine{line=0};for(int c=0;c<2;c++){var n=f.Note(60,c);n.pan=Col(c==0?"Y0":"Y1");row.notes.Add(n);}row.effects.Add(new EffectCell{command=Fx("0Y",0)});f.song.patterns[0].tracks[0].WriteLine(row);using(var e=f.Open()){f.Render(e,1);Need(e.Snapshot.voices.ToArray().Count(v=>v.active)==1&&f.Foreground(e,1).column==1,"exclusive weights");}
                });
                check("20 M L P curves and synth domain",f=>{
                    f.Row(0,f.Note(),Fx("0M",0),Fx("0L",0),Fx("0P",128));using(var e=f.Open()){f.Render(e,1);Near(e.Snapshot.tracks[0].instrumentGain,.001,"M00");Near(e.Snapshot.tracks[0].preGain,0,"L00");Near(e.Snapshot.tracks[0].prePan,0,"P80");}f.Row(0,f.Note(),Fx("0M",255),Fx("0L",255));using(var e=f.Open()){f.Render(e,1);Near(e.Snapshot.tracks[0].instrumentGain,Math.Pow(10,.15),"MFF");}
                });
                check("21 X00 preserves entering row notes",f=>{
                    f.Row(0,f.Note());var n=f.Note(64);n.delayPresent=true;n.delay=128;f.Row(1,n,Fx("0X",0));using(var e=f.Open()){f.Render(e,7000);Need(!e.Snapshot.voices.ToArray().Any(v=>v.active),"old voices not cut");f.Render(e,2001);Need(f.Foreground(e).note==64,"entering delayed launch lost");}
                });
                check("22 unsupported hardware width phrase groove",f=>{
                    f.Row(0,f.Note(),Fx("0J",1),Fx("0W",255),Fx("0Z",1),Fx("ZG",1));using(var p=TrackerPreparedSong.Prepare(f.song))Need(p.diagnostics.Count>=4,"unsupported source not diagnosed");using(var e=f.Open()){f.Render(e,1);Need(e.Snapshot.tracks[0].output==1&&e.Snapshot.bpm==120,"unsupported writes graph");}
                });
                check("23 ZD and raw ZB row-end flow",f=>{
                    var p=ZTrackerMigration.Copy(f.song.patterns[0]);p.id="next";f.song.patterns.Add(p);f.song.sequence.Add(new SequenceSlot{id="next-slot",patternId="next"});f.Row(0,f.Note(),Fx("ZB",0x12),Fx("ZD",2));using(var e=f.Open()){f.Render(e,18001);Need(e.Snapshot.sequenceIndex==1&&e.Snapshot.row==18,"hold/break destination");var notes=new List<TrackerEvent>();while(e.ReadEvent(out var ev))if(ev.kind==TrackerEventKind.NoteOn)notes.Add(ev);Need(notes.Count==1,"held repeated note");}
                });
                check("24 external slot73 identity",f=>{
                    f.instrument.model.family=InstrumentFamily.Synth;f.instrument.model.modulation.Clear();f.instrument.model.sampler.samples[0].modulationSet=-1;f.song.tracks[0].externalSources.Add(new ExternalSourceDevice{id="external",sourceOrdinal=1,instrumentId=f.instrument.model.id,pluginId="plugin",parameterNumbers=new List<string>{"73"}});f.instrument.model.externalParameters.Add(new ExternalParameterMapping{externalId="73",mapping=TrackerP4Check.Route("volume",0,1)});f.Row(0,f.Note(),Fx("11",128));using(var e=f.Open()){f.Render(e,1);Near(e.Snapshot.parameterLive[(int)TrackerParameter.Volume],128d/255,"external73");}
                });
                check("25 reversed explicit curve and quantum",f=>{
                    f.MacrosDevice();var p=f.song.tracks[0].sourceDevices[0].parameters[0];p.min=.8f;p.max=.2f;p.quantum=.1f;f.Row(0,f.Note(),Fx("11",64));using(var e=f.Open()){f.Render(e,1);Near(e.ObserveMacro(0,0),.6,"reversed quantized byte64");}p.min=0;p.max=1;p.quantum=0;p.curvePoints=new List<ModulationPoint>{new ModulationPoint{time=0,value=0},new ModulationPoint{time=.5,value=.25f},new ModulationPoint{time=1,value=1}};using(var e=f.Open()){f.Render(e,1);Near(e.ObserveMacro(0,0),(64d/255)*.5,"explicit curve");}
                });
                check("26 tick automation and same-instant setters",f=>{
                    f.MacrosDevice();f.Lane();f.Row(0,f.Note(),Fx("11",255));using(var e=f.Open()){f.Render(e,1);Near(e.ObserveMacro(0,0),1,"tick0 effect wins");f.Render(e,1500);Near(e.ObserveMacro(0,0),.25,"tick1 lane resumes");f.Render(e,1500);Near(e.ObserveMacro(0,0),.5,"tick2 lane");f.Render(e,1500);Near(e.ObserveMacro(0,0),.75,"tick3 lane");}
                });
                check("27 disabled latent then enable",f=>{
                    f.MacrosDevice();f.Lane();f.Row(0,f.Note(),Fx("10",0));f.Row(1,null,Fx("10",1));using(var e=f.Open()){f.Render(e,4501);Near(e.ObserveMacro(0,0),0,"disabled emitted changes");Near(e.Snapshot.deviceParameters[0].latent,.75,"disabled latent");f.Render(e,1500);Near(e.ObserveMacro(0,0),1,"enable emits current latent");}
                });
                check("28 seek reconstructs disabled latent/emitted",f=>{
                    f.instrument.model.macros[0].value=.2f;f.MacrosDevice();var lane=f.Lane();lane.points=new List<AutomationPoint>{new AutomationPoint{line=0,value=.2f},new AutomationPoint{line=2,value=.9f}};f.Row(0,f.Note(),Fx("11",153));f.Row(1,null,Fx("10",0));using(var e=f.Open()){e.SendCommand(new TrackerCommand{kind=TrackerCommandKind.Seek,a=0,b=2});Near(e.ObserveMacro(0,0),.55,"reconstructed emitted before disable");Need(!e.Snapshot.devices[0].enabled&&e.Snapshot.deviceParameters[0].latent>.8f,"latent replay");Need(!e.Snapshot.voices.ToArray().Any(v=>v.active),"seek retained voices");}
                });
                check("29 decimalhex legacy reversible preservation",f=>{
                    foreach(int command in new[]{16,17,18,0x16,0x17,0x18,0xb}){var source=ZTrackerMigration.ConvertCommand(command,127,0,"fixture");string before=ZTrackerMigration.Json(source);f.Row(0,f.Note(),source);using(var p=TrackerPreparedSong.Prepare(f.song))Need(p.diagnostics.Any(d=>d.Contains("LEGACY_")),"legacy mapping guessed");Need(before==ZTrackerMigration.Json(source),"legacy source changed");}var timing=ZTrackerMigration.ConvertCommand(15,0,0,"fixture");Need(timing.identifier=="ZK"&&timing.value==1,"F00");
                });
                check("30 defect and shared macro ownership flag",f=>{
                    var command=ZTrackerMigration.ConvertCommand(16,0x03,0,"channel0");f.Row(0,f.Note(),command);using(var p=TrackerPreparedSong.Prepare(f.song))Need(p.diagnostics.Any(d=>d.Contains("MACRO_OWNERSHIP")),"shared native channel macro guessed");Need(command.hasLegacy&&command.legacyCommand==16,"reversible bytes");f.Row(0,f.Note(),Fx("0T",0x48));using(var e=f.Open()){f.Render(e,3001);Need(f.Foreground(e).commandTremolo<1,"tremolo remains native no-op");}
                });
            }
            finally{UnityEngine.Object.DestroyImmediate(pcm);}
            return "TrackerP5Check "+pass+" passed / "+fail+" failed\n"+report;
        }
    }
}

