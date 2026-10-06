using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Laubrary.Audio;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.Zounds.Checks;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Audio;

namespace Laubrary.ZTracker.Proof
{
    // Opt-in independent player proof. No overlay and no managed audio callback.
    public sealed class TrackerPlayerProof : MonoBehaviour
    {
        [Serializable] public class Interval
        {
            public string name;
            public double seconds,expectedFrames,trackerRate,zoundsRate,maxGcSeconds;
            public long trackerFrames,zoundsFrames,trackerBlocks,zoundsBlocks,guardBlocks,managedBlocks;
            public int collections;
            public bool trackerCompiled,passed;
        }
        [Serializable] public class Result
        {
            public int sampleRate,dspFrames,dspBuffers,errors,gcCount,swaps,retiredDisposed,repeatedCycles,quitFrames,gcObjects;
            public double quitSeconds;
            public bool offlineWitness,zoundsWitness,lifecyclePassed,timeoutRetained,timeoutReclaimed,eventContinuity,quitPassed,passed;
            public bool timeoutHandleBeforeStop,timeoutHandleAfterStop,pendingSwapAccepted,pendingSwapDisposed,queuedSwapsCompiled;
            public string checkReport,errorText;
            public string implementationWitness;
            public bool p4ToneCoverage,macroOperationObserved,p5CommandAutomationCoverage;
            public List<Interval> intervals=new List<Interval>();
        }
        public static Result result=new Result();
        static string output;
        static double quitStarted;
        static int quitFrame;
        TrackerSapGenerator tracker;
        ZoundSapVoiceGenerator zounds;
        AudioClip clip;
        ZTrackerInstrument instrument;
        ZTrackerInstrument synth,fm;
        TrackerPreparedSong current;
        readonly List<TrackerPreparedSong> prepared=new List<TrackerPreparedSong>();
        byte[][] retainedHeap;
        long lastEvent=-1;
        long lastTick=-1;
        bool accounting=true;
        int rate;
        double processStarted;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--tracker-p3-proof");if(at<0)return;
            output=at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"tracker-p3-proof.json");
            Application.runInBackground=true;Application.targetFrameRate=120;
            var root=new GameObject("Independent tracker player proof");DontDestroyOnLoad(root);root.AddComponent<TrackerPlayerProof>();
        }
        void Awake(){processStarted=Time.realtimeSinceStartupAsDouble;Application.logMessageReceivedThreaded+=Log;Application.quitting+=Finished;}
        void Log(string text,string stack,LogType kind){if(kind!=LogType.Error&&kind!=LogType.Exception&&kind!=LogType.Assert)return;Interlocked.Increment(ref result.errors);lock(result){result.errorText+=(result.errorText==null?"":"\n")+text;}}
        IEnumerator Start()
        {
            rate=AudioSettings.outputSampleRate;result.sampleRate=rate;AudioSettings.GetDSPBufferSize(out result.dspFrames,out result.dspBuffers);
            result.implementationWitness=TrackerPreparedSong.ImplementationWitness;
            result.checkReport=TrackerEngineCheck.Execute()+"\n"+TrackerIntegrationCheck.Execute()+"\n"+TrackerP4Check.Execute()+"\n"+TrackerP5Check.Execute();result.offlineWitness=!result.checkReport.Contains("FAIL");
            result.zoundsWitness=ZoundsSapCompiledWitness.Run().compiled;
            var listener=new GameObject("Proof listener");listener.AddComponent<AudioListener>();
            clip=AudioClip.Create("proof-source",rate,1,rate,false);var pcm=new float[rate];for(int i=0;i<pcm.Length;i++)pcm[i]=.12f*(float)Math.Sin(i*2*Math.PI*330/rate);clip.SetData(pcm,0);
            instrument=TrackerEngineCheck.FixtureInstrument(clip);instrument.model.sampler.samples[0].loop=SampleLoop.Forward;instrument.model.sampler.samples[0].loopEndFrame=rate;instrument.model.sampler.samples[0].nna=NewNoteAction.Cut;
            instrument.model.sampler.zones[0].blend=new SampleBlendExtension{pcmB=clip,baseNoteB=64,mode=3,amount=.4f,pmDepth=17,loopB=SampleLoop.PingPong,loopStartFrameB=100,loopEndFrameB=rate-100};
            synth=TrackerP4Check.Synth();synth.model.parameters.unisonVoices=8;synth.model.parameters.waveB=3;synth.model.parameters.blendMode=3;synth.model.parameters.blend=.3f;synth.model.parameters.unisonDetune=19;
            fm=TrackerP4Check.Synth(true);fm.model.parameters.fmAlgorithm=2;fm.model.parameters.fmFeedback=.23f;
            foreach(var ins in new[]{instrument,synth,fm})for(int macro=0;macro<8;macro++){ins.model.macros[macro].value=macro/8f;ins.model.macros[macro].mappings.Add(TrackerP4Check.Route("volume",.1f,.3f));}
            tracker=NewTracker(true);
            var zg=new GameObject("Compiled Zounds proof voice");var zs=zg.AddComponent<AudioSource>();zounds=zg.AddComponent<ZoundSapVoiceGenerator>();
            var effects=new ZoundEffectChain();var gain=new ZoundEffectNode(ZoundEffectType.Gain);gain.p[0]=.5f;effects.nodes.Add(gain);
            var layout=ChainLayout.Build(effects,rate);zounds.SetPlay(new PcmClip{channels=1,frequency=rate,frames=rate,samples=pcm,valid=true,peak=.12f},layout,0,rate,1,1,1,true,90212,true);zs.generator=zounds;zs.Play();
            var args=Environment.GetCommandLineArgs();int gcAt=Array.IndexOf(args,"--tracker-p3-gc-objects");int objectCount=1500000;
            if(gcAt>=0&&(gcAt+1>=args.Length||!int.TryParse(args[gcAt+1],out objectCount)||objectCount<10000||objectCount>5000000))throw new Exception("GC object count must be 10000..5000000");
            result.gcObjects=objectCount;retainedHeap=new byte[objectCount][];for(int i=0;i<retainedHeap.Length;i++)retainedHeap[i]=new byte[256];
            yield return new WaitForSecondsRealtime(2);
            bool paired=false,sub=false,operatorVoice=false;for(int i=0;i<current.state.voices.Length;i++){var voice=current.state.voices[i];if(!voice.active)continue;var tone=current.state.tones[voice.sample];paired|=tone.pcmB>=0;sub|=tone.kind==1&&tone.members==8;operatorVoice|=tone.kind==2;}
            result.p4ToneCoverage=paired&&sub&&operatorVoice;result.p5CommandAutomationCoverage=current.state.opCount>0&&current.state.automationCount>0&&current.state.voices.ToArray().Any(v=>v.active&&v.commandPitch>0);
            tracker.SetMacro(1,7,.31f);tracker.TargetMacro(2,7,.6f,.1f);tracker.SendCommand(TrackerCommand.AmplitudeModifier(0,1,.3f,4));
            yield return new WaitForSecondsRealtime(.2f);result.macroOperationObserved=Math.Abs(tracker.ObserveMacro(1,7)-.31f)<1e-6&&Math.Abs(tracker.ObserveMacro(2,7)-.6f)<1e-6;
            DrainEvents();lastEvent=-1;
            yield return Measure("baseline",3,false);
            yield return Measure("forced-full-GC",6,true);
            yield return Measure("recovery",3,false);
            result.eventContinuity=lastEvent>0&&result.errors==0;
            accounting=false;
            // Two queued replacements can retire an owner that rendered no block. It must not taint the native witness.
            var queuedA=Prepare();var queuedB=Prepare();
            if(!tracker.SwapPrepared(queuedA,true)||!tracker.SwapPrepared(queuedB,true))throw new Exception("Queued swaps refused");
            current=queuedB;yield return new WaitForSecondsRealtime(.2f);
            result.queuedSwapsCompiled=tracker.RenderCompiled&&queuedA.Disposed;
            // Real between-block swaps. Keep old owners so actual retirement is observable.
            for(int i=0;i<8;i++){var next=Prepare();bool sent=tracker.SwapPrepared(next,true);if(!sent)throw new Exception("Live swap refused");current=next;result.swaps++;yield return new WaitForSecondsRealtime(.16f);DrainEvents();}
            foreach(var p in prepared)if(p!=current&&p.Disposed)result.retiredDisposed++;
            bool timeout=tracker.StopAndConfirm(0);result.timeoutRetained=!timeout&&!current.Disposed;
            bool stopped=tracker.StopAndConfirm(.5);long frames=tracker.RenderedFrames;yield return new WaitForSecondsRealtime(.1f);bool frozen=tracker.RenderedFrames==frames;
            Destroy(tracker.gameObject);yield return null;bool disposed=current.Disposed;
            for(int i=0;i<3;i++){tracker=NewTracker(false);tracker.SendCommand(TrackerCommand.Audition(0,60));yield return new WaitForSecondsRealtime(.12f);bool active=tracker.RenderedFrames>0&&tracker.RenderCompiled;bool quiet=tracker.StopAndConfirm(.5);Destroy(tracker.gameObject);yield return null;if(active&&quiet&&current.Disposed)result.repeatedCycles++;}
            // Adversarial timeout: stop publication, flush control, wait until stable, then make the external ticket
            // look in-flight. This tests retention and later handle disposal without racing a live renderer.
            tracker=NewTracker(true);yield return new WaitForSecondsRealtime(.15f);var timeoutOwner=current;
            result.timeoutHandleBeforeStop=HandleExists(tracker);
            tracker.GetComponent<AudioSource>().Stop();ControlContext.WaitForBuiltInQueueFlush();yield return new WaitForSecondsRealtime(.12f);
            result.timeoutHandleAfterStop=HandleExists(tracker);
            TrackerPreparedSong pendingOwner=null;if(result.timeoutHandleAfterStop){pendingOwner=Prepare();result.pendingSwapAccepted=tracker.SwapPrepared(pendingOwner,true);}
            long previous=Read(timeoutOwner.state.ticket,0);if((previous&1)!=0)throw new Exception("Silenced ticket not even");Write(timeoutOwner.state.ticket,0,previous+1);
            Destroy(tracker.gameObject);yield return null;result.timeoutRetained&=!timeoutOwner.Disposed;
            Write(timeoutOwner.state.ticket,0,previous);yield return new WaitForSecondsRealtime(.35f);result.timeoutReclaimed=timeoutOwner.Disposed;
            if(pendingOwner!=null&&!result.pendingSwapAccepted)pendingOwner.Dispose();result.pendingSwapDisposed=pendingOwner==null||pendingOwner.Disposed;
            result.lifecyclePassed=stopped&&frozen&&disposed&&result.swaps==8&&result.retiredDisposed>=8&&result.repeatedCycles==3&&result.timeoutRetained&&result.timeoutReclaimed&&result.pendingSwapDisposed&&result.queuedSwapsCompiled;
            // Leave both voices running when requesting quit: registries must silence and drain across real updates.
            tracker=NewTracker(true);yield return new WaitForSecondsRealtime(.15f);
            Application.wantsToQuit+=ObserveQuit;quitStarted=Time.realtimeSinceStartupAsDouble;quitFrame=Time.frameCount;Save();Application.Quit();
        }
        TrackerPreparedSong Prepare()
        {
            var song=TrackerEngineCheck.FixtureSong(instrument,3,64);song.voiceCapacity=16;song.instruments.Add(synth);song.instruments.Add(fm);var a=TrackerEngineCheck.Note(69,0);var b=TrackerEngineCheck.Note(60,1);b.instrument=1;var c=TrackerEngineCheck.Note(57,2);c.instrument=2;song.patterns[0].tracks[0].WriteLine(new PatternLine{line=0,notes=new List<NoteCell>{a,b,c}});
            instrument.model.provenance="";
            var device=new SourceDeviceData{id="proof-macro",ordinal=1,kind=SourceDeviceKind.InstrumentMacros,instrumentId=instrument.model.id};device.parameters.Add(new SourceParameterData{ordinal=1,defaultValue=.2f});song.tracks[0].sourceDevices.Add(device);
            var track=song.patterns[0].tracks[0];for(int row=0;row<64;row++){var line=track.lines.Find(l=>l.line==row)??new PatternLine{line=row};line.effects.Add(new EffectCell{column=0,command=TrackerP5Check.Fx("0U",16)});line.effects.Add(new EffectCell{column=1,command=TrackerP5Check.Fx("0V",0x48)});line.effects.Add(new EffectCell{column=2,command=TrackerP5Check.Fx("0T",0x48)});track.WriteLine(line);}
            track.automation.Add(new AutomationLane{id="proof-lane",target=new ParameterTarget{kind=ParameterKind.Device,deviceId="proof-macro",index=0},interpolation=AutomationInterpolation.Linear,points=new List<AutomationPoint>{new AutomationPoint{line=0,value=.2f},new AutomationPoint{line=32,value=.8f},new AutomationPoint{line=64,value=.2f}}});
            var p=TrackerPreparedSong.Prepare(song,rate);prepared.Add(p);return p;
        }
        TrackerSapGenerator NewTracker(bool play)
        {
            var root=new GameObject("Tracker proof voice");var source=root.AddComponent<AudioSource>();var host=root.AddComponent<TrackerSapGenerator>();current=Prepare();host.Configure(current,play);source.generator=host;source.Play();return host;
        }
        static unsafe long Read(NativeArray<long> ticket,int index)=>Volatile.Read(ref ((long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(ticket))[index]);
        static unsafe void Write(NativeArray<long> ticket,int index,long value)=>Volatile.Write(ref ((long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(ticket))[index],value);
        long ZFrames(){var ticket=(NativeArray<long>)typeof(ZoundSapVoiceGenerator).GetField("renderTicket",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(zounds);return ticket.IsCreated?Read(ticket,1):0;}
        static bool HandleExists(TrackerSapGenerator host){var handle=(GeneratorInstance)typeof(TrackerSapGenerator).GetField("instance",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(host);return ControlContext.builtIn.Exists(handle);}
        IEnumerator Measure(string name,double duration,bool forceGc)
        {
            var s=new Interval{name=name};double start=Time.realtimeSinceStartupAsDouble;long tf=tracker.RenderedFrames,tb=tracker.RenderedBlocks,zf=ZFrames(),zb=zounds.RenderTicket/2,gb=AudioThreadGuard.Blocks,gm=AudioThreadGuard.ManagedBlocks;
            double nextGc=start;
            while(Time.realtimeSinceStartupAsDouble-start<duration){DrainEvents();if(forceGc&&Time.realtimeSinceStartupAsDouble>=nextGc){double begin=Time.realtimeSinceStartupAsDouble;GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,true,true);GC.WaitForPendingFinalizers();GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,true,true);double took=Time.realtimeSinceStartupAsDouble-begin;s.maxGcSeconds=Math.Max(s.maxGcSeconds,took);s.collections++;result.gcCount++;nextGc=Time.realtimeSinceStartupAsDouble+.15;}yield return null;}
            s.seconds=Time.realtimeSinceStartupAsDouble-start;s.expectedFrames=s.seconds*rate;s.trackerFrames=tracker.RenderedFrames-tf;s.zoundsFrames=ZFrames()-zf;s.trackerBlocks=tracker.RenderedBlocks-tb;s.zoundsBlocks=zounds.RenderTicket/2-zb;s.guardBlocks=AudioThreadGuard.Blocks-gb;s.managedBlocks=AudioThreadGuard.ManagedBlocks-gm;s.trackerRate=s.trackerFrames/s.seconds;s.zoundsRate=s.zoundsFrames/s.seconds;s.trackerCompiled=tracker.RenderCompiled;
            // Wall-clock expectation allows device startup/observation jitter of three DSP blocks, capped at 3%.
            double tolerance=Math.Max(3*result.dspFrames,s.expectedFrames*.03);s.passed=Math.Abs(s.trackerFrames-s.expectedFrames)<=tolerance&&Math.Abs(s.zoundsFrames-s.expectedFrames)<=tolerance&&s.managedBlocks==0&&s.trackerCompiled&&s.trackerBlocks>0&&s.zoundsBlocks>0;
            result.intervals.Add(s);Save();
        }
        void DrainEvents(){while(tracker!=null&&tracker.ReadEvent(out var ev)){if(lastEvent>ev.samplePosition)throw new Exception("Event counter moved backwards");lastEvent=ev.samplePosition;if(accounting&&ev.kind==TrackerEventKind.Tick){long expected=(long)(rate*60d/(120*4*6));if(lastTick>=0&&ev.samplePosition-lastTick!=expected)throw new Exception("Dropped/duplicate tick during live render");lastTick=ev.samplePosition;}}}
        bool ObserveQuit(){return true;}
        void Update(){if(quitStarted>0){result.quitFrames=Time.frameCount-quitFrame;result.quitSeconds=Time.realtimeSinceStartupAsDouble-quitStarted;}if(quitStarted==0&&(result.errors>0||Time.realtimeSinceStartupAsDouble-processStarted>90)){quitStarted=Time.realtimeSinceStartupAsDouble;quitFrame=Time.frameCount;Save();Application.Quit();}}
        static void Save(){File.WriteAllText(output,JsonUtility.ToJson(result,true));}
        static void Finished(){result.quitPassed=result.quitFrames>=2&&result.quitSeconds>=.3;result.passed=result.offlineWitness&&result.zoundsWitness&&result.p4ToneCoverage&&result.p5CommandAutomationCoverage&&result.macroOperationObserved&&result.errors==0&&result.eventContinuity&&result.lifecyclePassed&&result.quitPassed&&result.intervals.TrueForAll(s=>s.passed);Save();}
    }
}
