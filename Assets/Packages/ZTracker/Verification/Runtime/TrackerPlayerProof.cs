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
            public bool publicClipless,publicEvents,liveEdits,pcmPending,pcmApplied,controlledRates,configurationStopped,hardwareVerified,quitRefused;
            public double trackerEnergy; public int publicStarts,publicStops;
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
        ZTrackerPlayer publicPlayer;
        ZTrackerSong activeSong;
        AudioClip replacementClip;
        bool publicMode;
        int starts,stops,rows,notes,beats,authored,editedNotes;
        readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
        static TrackerPlayerProof active;
        double processStarted;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--tracker-p7-proof");if(at<0)at=Array.IndexOf(args,"--tracker-p3-proof");if(at<0)return;
            output=at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"tracker-p7-proof.json");
            Application.runInBackground=true;Application.targetFrameRate=120;
            var root=new GameObject("Independent tracker player proof");DontDestroyOnLoad(root);root.AddComponent<TrackerPlayerProof>();
        }
        void Awake(){active=this;processStarted=Time.realtimeSinceStartupAsDouble;Application.logMessageReceivedThreaded+=Log;Application.quitting+=Finished;}
        void Log(string text,string stack,LogType kind){if(kind!=LogType.Error&&kind!=LogType.Exception&&kind!=LogType.Assert)return;Interlocked.Increment(ref result.errors);lock(result){result.errorText+=(result.errorText==null?"":"\n")+text;}}
        IEnumerator Start()
        {
            rate=AudioSettings.outputSampleRate;result.sampleRate=rate;AudioSettings.GetDSPBufferSize(out result.dspFrames,out result.dspBuffers);
            result.implementationWitness=TrackerPreparedSong.ImplementationWitness;
            result.checkReport=TrackerEngineCheck.Execute()+"\n"+TrackerIntegrationCheck.Execute()+"\n"+TrackerP4Check.Execute()+"\n"+TrackerP5Check.Execute();result.offlineWitness=!result.checkReport.Contains("FAIL");
            var listener=new GameObject("Proof listener");listener.AddComponent<AudioListener>();
            CreateFixture(rate,out clip,out var pcm,out instrument,out synth,out fm);
            tracker=NewPublicTracker();
            var zg=new GameObject("Compiled Zounds proof voice");var zs=zg.AddComponent<AudioSource>();zounds=zg.AddComponent<ZoundSapVoiceGenerator>();
            var effects=new ZoundEffectChain();var gain=new ZoundEffectNode(ZoundEffectType.Gain);gain.p[0]=.5f;effects.nodes.Add(gain);
            var layout=ChainLayout.Build(effects,rate);zounds.SetPlay(new PcmClip{channels=1,frequency=rate,frames=rate,samples=pcm,valid=true,peak=.12f},layout,0,rate,1,1,1,true,90212,true);zs.generator=zounds;zs.Play();
            var args=Environment.GetCommandLineArgs();int gcAt=Array.IndexOf(args,"--tracker-p7-gc-objects");if(gcAt<0)gcAt=Array.IndexOf(args,"--tracker-p3-gc-objects");int objectCount=3000000;
            if(gcAt>=0&&(gcAt+1>=args.Length||!int.TryParse(args[gcAt+1],out objectCount)||objectCount<10000||objectCount>5000000))throw new Exception("GC object count must be 10000..5000000");
            result.gcObjects=objectCount;retainedHeap=new byte[objectCount][];for(int i=0;i<retainedHeap.Length;i++)retainedHeap[i]=new byte[256];
            yield return RunProof();
        }
        static void CreateFixture(int rate,out AudioClip clip,out float[] pcm,out ZTrackerInstrument instrument,out ZTrackerInstrument synth,out ZTrackerInstrument fm)
        {
            clip=AudioClip.Create("proof-source",rate,1,rate,false);pcm=new float[rate];for(int i=0;i<pcm.Length;i++)pcm[i]=.12f*(float)Math.Sin(i*2*Math.PI*330/rate);clip.SetData(pcm,0);
            instrument=TrackerEngineCheck.FixtureInstrument(clip);instrument.model.sampler.samples[0].loop=SampleLoop.Forward;instrument.model.sampler.samples[0].loopEndFrame=rate;instrument.model.sampler.samples[0].nna=NewNoteAction.Cut;
            instrument.model.sampler.zones[0].blend=new SampleBlendExtension{pcmB=clip,baseNoteB=64,mode=3,amount=.4f,pmDepth=17,loopB=SampleLoop.PingPong,loopStartFrameB=100,loopEndFrameB=rate-100};
            synth=TrackerP4Check.Synth();synth.model.parameters.unisonVoices=8;synth.model.parameters.waveB=3;synth.model.parameters.blendMode=3;synth.model.parameters.blend=.3f;synth.model.parameters.unisonDetune=19;
            fm=TrackerP4Check.Synth(true);fm.model.parameters.fmAlgorithm=2;fm.model.parameters.fmFeedback=.23f;
            foreach(var ins in new[]{instrument,synth,fm})for(int macro=0;macro<8;macro++){ins.model.macros[macro].value=macro/8f;ins.model.macros[macro].mappings.Add(TrackerP4Check.Route("volume",.1f,.3f));}
        }
        IEnumerator RunProof()
        {
            yield return new WaitForSecondsRealtime(2);
            bool paired=false,sub=false,operatorVoice=false;for(int i=0;i<current.state.voices.Length;i++){var voice=current.state.voices[i];if(!voice.active)continue;var tone=current.state.tones[voice.sample];paired|=tone.pcmB>=0;sub|=tone.kind==1&&tone.members==8;operatorVoice|=tone.kind==2;}
            result.p4ToneCoverage=paired&&sub&&operatorVoice;result.p5CommandAutomationCoverage=current.state.opCount>0&&current.state.automationCount>0&&current.state.voices.ToArray().Any(v=>v.active&&v.commandPitch>0);
            tracker.SetMacro(1,7,.31f);tracker.TargetMacro(2,7,.6f,.1f);tracker.SendCommand(TrackerCommand.AmplitudeModifier(0,1,.3f,4));
            yield return new WaitForSecondsRealtime(.2f);result.macroOperationObserved=Math.Abs(tracker.ObserveMacro(1,7)-.31f)<1e-6&&Math.Abs(tracker.ObserveMacro(2,7)-.6f)<1e-6;
            DrainEvents();lastEvent=-1;
            yield return Measure("baseline",3,false);
            yield return Measure("forced-full-GC",6,true);
            yield return Measure("recovery",3,false);
            result.zoundsWitness=result.intervals.Count==3&&result.intervals.TrueForAll(s=>s.zoundsBlocks>0&&s.managedBlocks==0&&s.guardBlocks>=s.trackerBlocks+s.zoundsBlocks-2);
            result.eventContinuity=lastEvent>0&&result.errors==0&&tracker.EventOverflow==0;
            result.publicEvents=starts>0&&rows>0&&notes>=3&&beats>0&&authored>0;
            result.trackerEnergy=tracker.RenderedEnergy;
            yield return PublicLiveChecks();
            StopPublic();yield return null;
            tracker=NewTracker(true);
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
            for(int i=0;i<12;i++){int startCount=starts,stopCount=stops;tracker=NewPublicTracker();yield return new WaitForSecondsRealtime(.16f);DrainEvents();bool rendered=tracker.RenderedFrames>0&&tracker.RenderCompiled&&tracker.RenderedEnergy>0;var owner=current;var facade=publicPlayer.Playback;StopPublic();long stoppedFrames=facade.RenderedFrames;yield return null;yield return new WaitForSecondsRealtime(.08f);bool quiet=facade.LastStopConfirmed&&facade.RenderedFrames==stoppedFrames;if(rendered&&quiet&&owner.Disposed&&starts==startCount+1&&stops==stopCount+1&&ZTrackerPlayback.Current==null)result.repeatedCycles++;}
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
            result.lifecyclePassed=stopped&&frozen&&disposed&&result.swaps==8&&result.retiredDisposed>=8&&result.repeatedCycles==12&&result.timeoutRetained&&result.timeoutReclaimed&&result.pendingSwapDisposed&&result.queuedSwapsCompiled;
            // Leave both voices running when requesting quit: registries must silence and drain across real updates.
            yield return ControlledRateChecks();
            tracker=NewPublicTracker();yield return new WaitForSecondsRealtime(.15f);
            if(tracker.RenderedFrames<=0||ZFrames()<=0||!tracker.RenderCompiled)throw new Exception("Quit requires two live compiled streams");
            Application.wantsToQuit+=ObserveQuit;quitStarted=Time.realtimeSinceStartupAsDouble;quitFrame=Time.frameCount;Save();Application.Quit();
        }
        SongData BuildSong() => BuildSong(instrument,synth,fm);
        static SongData BuildSong(ZTrackerInstrument instrument,ZTrackerInstrument synth,ZTrackerInstrument fm)
        {
            var song=TrackerEngineCheck.FixtureSong(instrument,3,64);song.beatTicks=true;song.voiceCapacity=16;song.instruments.Add(synth);song.instruments.Add(fm);var a=TrackerEngineCheck.Note(69,0);var b=TrackerEngineCheck.Note(60,1);b.instrument=1;var c=TrackerEngineCheck.Note(57,2);c.instrument=2;song.patterns[0].tracks[0].WriteLine(new PatternLine{line=0,notes=new List<NoteCell>{a,b,c}});
            instrument.model.provenance="";
            song.tracks[1].devices.nodes.Add(new AudioEffectNodeData{type=ZoundEffectType.Gain,p=new[]{.9f}});
            song.patterns[0].tracks[0].lines[0].events.Add(new EventCell{present=true,payload="proof-event"});
            var device=new SourceDeviceData{id="proof-macro",ordinal=1,kind=SourceDeviceKind.InstrumentMacros,instrumentId=instrument.model.id};device.parameters.Add(new SourceParameterData{ordinal=1,defaultValue=.2f});song.tracks[0].sourceDevices.Add(device);
            var track=song.patterns[0].tracks[0];for(int row=0;row<64;row++){var line=track.lines.Find(l=>l.line==row)??new PatternLine{line=row};line.effects.Add(new EffectCell{column=0,command=TrackerP5Check.Fx("0U",16)});line.effects.Add(new EffectCell{column=1,command=TrackerP5Check.Fx("0V",0x48)});line.effects.Add(new EffectCell{column=2,command=TrackerP5Check.Fx("0T",0x48)});track.WriteLine(line);}
            track.automation.Add(new AutomationLane{id="proof-lane",target=new ParameterTarget{kind=ParameterKind.Device,deviceId="proof-macro",index=0},interpolation=AutomationInterpolation.Linear,points=new List<AutomationPoint>{new AutomationPoint{line=0,value=.2f},new AutomationPoint{line=32,value=.8f},new AutomationPoint{line=64,value=.2f}}});
            return song;
        }
        public static string CheckFixture()
        {
            CreateFixture(48000,out var source,out var pcm,out var sample,out var subtractive,out var operators);
            try
            {
                foreach(int sampleRate in new[]{44100,48000})
                {
                    using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(BuildSong(sample,subtractive,operators),sampleRate)))
                    using(var left=new NativeArray<float>(4096,Allocator.Persistent))
                    using(var right=new NativeArray<float>(4096,Allocator.Persistent))
                    {
                        engine.SendCommand(TrackerCommand.Play());engine.Render(left,right,4096);
                        int starts=0,notes=0,beats=0,authored=0;
                        while(engine.ReadEvent(out var ev)){if(ev.kind==TrackerEventKind.Started)starts++;if(ev.kind==TrackerEventKind.NoteOn)notes++;if(ev.kind==TrackerEventKind.Beat)beats++;if(ev.kind==TrackerEventKind.Authored)authored++;}
                        var state=engine.Snapshot;
                        bool paired=false,sub=false,fm=false;
                        foreach(var voice in state.voices){if(!voice.active)continue;var tone=state.tones[voice.sample];paired|=tone.pcmB>=0;sub|=tone.kind==1&&tone.members==8;fm|=tone.kind==2;}
                        if(!engine.Compiled||starts!=1||notes<3||beats==0||authored!=1||!paired||!sub||!fm||state.opCount==0||state.automationCount==0||!left.Any(v=>Math.Abs(v)>.001f))throw new Exception("Standalone fixture lacks required coverage at "+sampleRate);
                    }
                }
                return "PASS exact standalone fixture at 44100/48000: compiled nonzero PCM; sampler/B/unison/FM; starts/notes/beats/authored events; commands/automation. Live public host and GC require player execution.";
            }
            finally {DestroyImmediate(sample);DestroyImmediate(subtractive);DestroyImmediate(operators);DestroyImmediate(source);}
        }
        TrackerPreparedSong Prepare()
        {
            var p=TrackerPreparedSong.Prepare(BuildSong(),rate);prepared.Add(p);return p;
        }
        TrackerSapGenerator NewTracker(bool play)
        {
            publicMode=false;lastEvent=-1;lastTick=-1;
            var root=new GameObject("Tracker proof voice");var source=root.AddComponent<AudioSource>();var host=root.AddComponent<TrackerSapGenerator>();current=Prepare();host.Configure(current,play);source.generator=host;source.Play();return host;
        }
        TrackerSapGenerator NewPublicTracker()
        {
            publicMode=true;lastEvent=-1;lastTick=-1;
            activeSong=ScriptableObject.CreateInstance<ZTrackerSong>();activeSong.schemaVersion=1;activeSong.model=BuildSong();temporary.Add(activeSong);
            var root=new GameObject("Public tracker proof component");publicPlayer=root.AddComponent<ZTrackerPlayer>();publicPlayer.song=activeSong;
            publicPlayer.EngineEventReceived+=Account;
            publicPlayer.EventReceived+=e=>{switch((ZTrackerEventType)e.type){case ZTrackerEventType.SONG_STARTED:starts++;break;case ZTrackerEventType.SONG_STOPPED:stops++;break;case ZTrackerEventType.ROW_CHANGED:rows++;break;case ZTrackerEventType.CHANNEL_NOTE_ON:notes++;break;case ZTrackerEventType.BEAT_TICK:beats++;break;case ZTrackerEventType.EVENT_TRACK_FIRED:if(e.stringPayload!="proof-event")throw new Exception("Public event catalog mismatch");authored++;break;}};
            if(!publicPlayer.Play())throw new Exception(publicPlayer.LastError);
            var host=publicPlayer.Playback.Generator;current=host.CurrentPrepared;prepared.Add(current);result.publicStarts++;
            result.publicClipless=host.GetComponent<AudioSource>().clip==null&&host.GetComponent<AudioSource>().generator==host;
            return host;
        }
        void StopPublic()
        {
            if(publicPlayer==null)return;
            publicPlayer.Stop();result.publicStops++;Destroy(publicPlayer.gameObject);publicPlayer=null;publicMode=false;
        }
        IEnumerator PublicLiveChecks()
        {
            var facade=publicPlayer.Playback;
            long before=tracker.RenderedFrames;
            double energyStart=tracker.RenderedEnergy;yield return new WaitForSecondsRealtime(.1f);
            double baselineEnergyRate=(tracker.RenderedEnergy-energyStart)/(tracker.RenderedFrames-before);
            tracker.SendCommand(new TrackerCommand{kind=TrackerCommandKind.TrackGain,a=0,value=.35f});
            tracker.SetMacro(1,7,.23f);yield return new WaitForSecondsRealtime(.1f);
            synth.model.parameters.fineTune+=17;instrument.model.sampler.fineTuneCents+=13;
            activeSong.model.tracks[0].preVolume=.8f;
            activeSong.model.tracks[1].devices.nodes[0].p[0]=.6f;
            if(!facade.TryGetPosition(out var order,out var row))throw new Exception("Public transport position unavailable");
            var nextLine=activeSong.model.patterns[0].tracks[0].lines.Find(l=>l.line==(row+1)%64);
            nextLine.notes.RemoveAll(n=>n.column==0);nextLine.notes.Add(TrackerEngineCheck.Note(74));int oldEditedNotes=editedNotes;
            if(!facade.Refresh(out var reason))throw new Exception("Public live edit refused: "+reason);
            current=tracker.CurrentPrepared;before=tracker.RenderedFrames;energyStart=tracker.RenderedEnergy;yield return new WaitForSecondsRealtime(.2f);DrainEvents();
            double changedEnergyRate=(tracker.RenderedEnergy-energyStart)/(tracker.RenderedFrames-before);
            result.liveEdits=tracker.RenderCompiled&&tracker.RenderedFrames>before&&Math.Abs(tracker.ObserveMacro(1,7)-.23f)<1e-5&&Math.Abs(current.state.tracks[0].postGain-.35f)<1e-5&&editedNotes>oldEditedNotes&&baselineEnergyRate>0&&changedEnergyRate<baselineEnergyRate*.75;
            if(!result.liveEdits)throw new Exception("Public refresh lost transport or game override");
            // Authored catalog strings change only after the accepted block boundary.
            activeSong.model.patterns[0].tracks[0].lines[0].events[0].payload="proof-event";
            replacementClip=AudioClip.Create("replacement-primary-and-B",rate,1,rate,false);var pcm=new float[rate];for(int i=0;i<pcm.Length;i++)pcm[i]=-.08f;replacementClip.SetData(pcm,0);temporary.Add(replacementClip);
            instrument.model.sampler.samples[0].pcm=replacementClip;instrument.model.sampler.zones[0].blend.pcmB=replacementClip;
            var previous=current;before=tracker.RenderedFrames;
            bool accepted=facade.Refresh(out reason);yield return new WaitForSecondsRealtime(.2f);DrainEvents();
            result.pcmPending=!accepted&&reason!=null&&reason.Contains("stop and play")&&!previous.Disposed&&tracker.RenderedFrames>before;
            if(!result.pcmPending)throw new Exception("PCM replacement contract failed");
            StopPublic();yield return null;
            bool retired=previous.Disposed;
            tracker=NewPublicTracker();yield return new WaitForSecondsRealtime(.2f);DrainEvents();
            result.pcmApplied=retired&&current.state.pcm[0]==-.08f&&tracker.RenderCompiled&&tracker.RenderedEnergy>0;
            if(!result.pcmApplied)throw new Exception("Replacement PCM did not reach compiled public renderer");
        }
        IEnumerator ControlledRateChecks()
        {
            int saved=rate;bool both=true;
            foreach(int configured in new[]{44100,48000})
            {
                rate=configured;tracker=NewTracker(true);yield return new WaitForSecondsRealtime(.25f);
                both&=tracker.ConfiguredRate==configured&&tracker.RenderedFrames>0&&tracker.RenderCompiled;
                bool quiet=tracker.StopAndConfirm(.5);Destroy(tracker.gameObject);yield return null;
                both&=quiet&&current.Disposed;
            }
            rate=saved;result.controlledRates=both;
            tracker=NewPublicTracker();yield return new WaitForSecondsRealtime(.15f);
            var owner=current;publicPlayer.Playback.ReconfigureOutput(false);yield return null;
            result.configurationStopped=ZTrackerPlayback.Current==null&&owner.Disposed&&!publicPlayer.IsPlaying;
            Destroy(publicPlayer.gameObject);publicPlayer=null;publicMode=false;
            result.hardwareVerified=false; // Controlled SAP setup and lifecycle, no system AudioSettings.Reset/device manipulation.
            if(!both||!result.configurationStopped)throw new Exception("Controlled rate/configuration lifecycle failed");
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
        void DrainEvents(){if(publicMode&&publicPlayer!=null){publicPlayer.PumpEvents();return;}while(tracker!=null&&tracker.ReadEvent(out var ev))Account(ev);}
        void Account(TrackerEvent ev){if(lastEvent>ev.samplePosition)throw new Exception("Event counter moved backwards");lastEvent=ev.samplePosition;if(ev.kind==TrackerEventKind.NoteOn&&ev.note==74)editedNotes++;if(accounting&&ev.kind==TrackerEventKind.Tick){long expected=(long)(rate*60d/(120*4*6));if(lastTick>=0&&ev.samplePosition-lastTick!=expected)throw new Exception("Dropped/duplicate tick during actual callbacks");lastTick=ev.samplePosition;}}
        bool ObserveQuit(){result.quitRefused=TrackerSapRegistry.RefuseNewRendering&&!tracker.SendCommand(TrackerCommand.Play());return true;}
        void Update(){if(quitStarted>0){result.quitFrames=Time.frameCount-quitFrame;result.quitSeconds=Time.realtimeSinceStartupAsDouble-quitStarted;}if(quitStarted==0&&(result.errors>0||Time.realtimeSinceStartupAsDouble-processStarted>180)){quitStarted=Time.realtimeSinceStartupAsDouble;quitFrame=Time.frameCount;Save();Application.Quit();}}
        static void Save(){File.WriteAllText(output,JsonUtility.ToJson(result,true));}
        static void Finished(){result.quitPassed=result.quitFrames>=2&&result.quitSeconds>=.3;result.passed=result.publicClipless&&result.publicEvents&&result.liveEdits&&result.pcmPending&&result.pcmApplied&&result.controlledRates&&result.configurationStopped&&result.quitRefused&&result.trackerEnergy>0&&result.offlineWitness&&result.zoundsWitness&&result.p4ToneCoverage&&result.p5CommandAutomationCoverage&&result.macroOperationObserved&&result.errors==0&&result.eventContinuity&&result.lifecyclePassed&&result.quitPassed&&result.intervals.TrueForAll(s=>s.passed);Save();}
    }
}
