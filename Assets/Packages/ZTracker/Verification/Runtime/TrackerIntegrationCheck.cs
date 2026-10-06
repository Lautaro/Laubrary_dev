using System;
using System.Linq;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Proof
{
    public static class TrackerIntegrationCheck
    {
        public static string Execute()
        {
            var clip=AudioClip.Create("independent-proof",1024,1,48000,false);clip.SetData(Enumerable.Repeat(.2f,1024).ToArray(),0);
            var instrument=TrackerEngineCheck.FixtureInstrument(clip);instrument.model.sampler.samples[0].nna=NewNoteAction.Continue;
            var left=new NativeArray<float>(64,Allocator.Persistent);var right=new NativeArray<float>(64,Allocator.Persistent);
            try{using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument)))){
                engine.SendCommand(TrackerCommand.Audition(0,60,.625f));engine.Render(left,right,64);
                var a=engine.Snapshot.voices[0];if(a.volume!=.625f)throw new Exception("Audition float gain was quantized");
                if(!engine.Compiled||engine.Snapshot.ticket[3]!=1)throw new Exception("Render witness failed");
                engine.SendCommand(TrackerCommand.Audition(0,64,.05f));engine.Render(left,right,64);
                var b=engine.Snapshot.voices[1];if(b.volume!=.05f)throw new Exception("Small audition gain was quantized");
                engine.SendCommand(TrackerCommand.ReleaseVoice(0,a.cohort));engine.Render(left,right,64);
                if(!engine.Snapshot.voices[0].released||engine.Snapshot.voices[1].released)throw new Exception("Explicit release affected another cohort");
                engine.SendCommand(TrackerCommand.ReleaseVoice(1,a.cohort));engine.Render(left,right,64);
                if(engine.Snapshot.voices[1].released)throw new Exception("Stale release generation affected replacement");
                engine.SendCommand(TrackerCommand.Stop());engine.SendCommand(new TrackerCommand{kind=TrackerCommandKind.TrackMute,a=0,b=1});
                engine.SendCommand(TrackerCommand.Audition(0,60));engine.Render(left,right,64);
                if(!engine.Snapshot.voices[0].active||left[2]!=0)throw new Exception("Output mute blocked launch or leaked output");
                engine.SendCommand(new TrackerCommand{kind=TrackerCommandKind.TrackMute,a=0,b=0});engine.Render(left,right,64);
                if(Math.Abs(left[2])<.001f)throw new Exception("Voice launched under output mute did not survive unmute");
                engine.SendCommand(TrackerCommand.Play());if(!engine.ReadEvent(out var first))throw new Exception("Missing events");bool start=false;while(engine.ReadEvent(out var ev))start|=ev.kind==TrackerEventKind.Started;
                if(!start)throw new Exception("Play start event missing");
            }
            using(var first=TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument)))using(var next=TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument))){
                var ring=TrackerEventRing.Create(8);try{var realtime=new TrackerRealtime{state=first.state,events=ring};var on=TrackerCommand.Audition(0,60);realtime.Apply(in on);long stale=realtime.state.voices[0].cohort;
                    var swap=new TrackerCommand{kind=TrackerCommandKind.Swap,replacement=next.state};realtime.Apply(in swap);realtime.Apply(in on);
                    var off=TrackerCommand.ReleaseVoice(0,stale);realtime.Apply(in off);
                    if(realtime.state.voices[0].cohort<=stale||realtime.state.voices[0].released)throw new Exception("Prepared swap reused a stale voice generation");
                }finally{ring.Dispose();}
            }
            string nativeProvenance=instrument.model.provenance;
            foreach(string provenance in new[]{"","authored by independent check"}){instrument.model.provenance=provenance;using(var p=TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument))){if(p.state.legacyMix||p.state.samples[0].legacyPan||p.state.samples[0].legacySamplePitch)throw new Exception("Unrelated provenance enabled legacy DSP");}}
            instrument.model.provenance=nativeProvenance;
            using(var e=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument)))){e.SendCommand(TrackerCommand.Audition(0,60));e.Render(left,right,1);if(e.Snapshot.voices[0].step!=.9999999403953552)throw new Exception("Retained MSVC native float pitch profile changed");}
            float[] ramp=Enumerable.Range(0,1024).Select(i=>i*.0001f).ToArray();clip.SetData(ramp,0);
            foreach(string provenance in new[]{"",nativeProvenance}){
                instrument.model.provenance=provenance;var sample=instrument.model.sampler.samples[0];sample.loop=SampleLoop.Off;float[] reference;
                using(var e=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument)))){e.SendCommand(TrackerCommand.Audition(0,60));e.Render(left,right,64);reference=left.ToArray();}
                sample.loop=SampleLoop.PingPong;sample.loopStartFrame=128;sample.loopEndFrame=512;
                using(var e=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument)))){e.SendCommand(TrackerCommand.Audition(0,60));e.Render(left,right,64);if(reference.Zip(left.ToArray(),(a,b)=>Math.Abs(a-b)).Max()>1e-7)throw new Exception("PCM PingPong skipped the intro");}
            }
            instrument.model.provenance="";instrument.model.sampler.samples[0].loop=SampleLoop.Off;clip.SetData(Enumerable.Repeat(.2f,1024).ToArray(),0);
            var devices=instrument.model.modulation[0].devices;var env=devices[0];env.attack=env.hold=env.decay=0;env.sustain=.5f;
            foreach(var op in new[]{ModulationOperation.Replace,ModulationOperation.Add}){
                devices.Add(new ModulationDevice{id="ordered-fader",target=ModulationTarget.Volume,kind=ModulationDeviceKind.Fader,operation=op,depth=.3f});
                using(var e=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument)))){e.SendCommand(TrackerCommand.Audition(0,60));e.Render(left,right,64);float expected=.2f*(op==ModulationOperation.Replace?.3f:.8f)*(float)Math.Sqrt(.5);if(Math.Abs(left[63]-expected)>1e-6)throw new Exception("AHDSR moved outside authored device order");}
                devices.RemoveAt(devices.Count-1);
            }
            var multi=new ModulationDevice{id="loop-intro",kind=ModulationDeviceKind.Multipoint,target=ModulationTarget.Volume,operation=ModulationOperation.Replace,loop=SampleLoop.PingPong,loopStart=.01,loopEnd=.02};multi.points.Add(new ModulationPoint{time=0,value=.1f});multi.points.Add(new ModulationPoint{time=.03,value=.9f});devices.Add(multi);float[] plain;
            using(var e=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument)))){e.SendCommand(TrackerCommand.Audition(0,60));e.Render(left,right,64);plain=left.ToArray();}
            multi.loopEnabled=true;using(var e=new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(instrument)))){e.SendCommand(TrackerCommand.Audition(0,60));e.Render(left,right,64);if(plain.Zip(left.ToArray(),(a,b)=>Math.Abs(a-b)).Max()>1e-7)throw new Exception("Multipoint PingPong skipped the intro");}
            return "Independent integration checks PASS: exact float audition gain; render-path BurstDiscard; specific/stale release handles including prepared swaps; output-muted launch; Started event; generic provenance; measured native float pitch; modern/migrated PCM intro; AHDSR Replace/Add order; multipoint intro.";
            }finally{left.Dispose();right.Dispose();UnityEngine.Object.DestroyImmediate(instrument);UnityEngine.Object.DestroyImmediate(clip);}
        }
    }
}
