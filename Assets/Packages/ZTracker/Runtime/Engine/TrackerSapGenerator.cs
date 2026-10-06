using System;
using System.Collections.Generic;
using System.Threading;
using Laubrary.Audio;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>Opt-in SAP host. Existing legacy playback is unchanged until P7. One generator instance consumes one prepared song.</summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed unsafe class TrackerSapGenerator : MonoBehaviour,IAudioGenerator
    {
        public bool isFinite=>false;
        public bool isRealtime=>false;
        public DiscreteTime? length=>null;
        TrackerPreparedSong initial,current;
        TrackerEventRing ring;
        GeneratorInstance instance;
        bool hasInstance,hasRing,accepting=true,transferred,autoplay;
        readonly List<TrackerPreparedSong> owned=new List<TrackerPreparedSong>();
        readonly List<TrackerCommand> pending=new List<TrackerCommand>();
        public void Configure(TrackerPreparedSong song,bool playSong=true)
        {
            if(initial!=null||hasInstance||song==null||song.Disposed||song.Published)throw new InvalidOperationException("Host needs a fresh prepared song");
            initial=current=song;owned.Add(song);autoplay=playSong;ring=TrackerEventRing.Create(65536);hasRing=true;TrackerSapRegistry.Register(this);
        }
        public GeneratorInstance CreateInstance(ControlContext context,AudioFormat? nestedConfiguration,CreationParameters creationParameters)
        {
            if(!accepting||TrackerSapRegistry.RefuseNewRendering||initial==null||transferred)return default;
            var realtime=new TrackerRealtime{state=initial.state,events=ring};
            if(autoplay){var play=TrackerCommand.Play();realtime.Apply(in play);}
            foreach(var command in pending)realtime.Apply(in command);pending.Clear();
            initial.Published=true;transferred=true;
            instance=context.AllocateGenerator(realtime,new Control{rate=initial.state.sampleRate});hasInstance=true;return instance;
        }
        public bool SendCommand(TrackerCommand command)
        {
            if(!accepting||TrackerSapRegistry.RefuseNewRendering||initial==null||command.kind==TrackerCommandKind.Swap)return false;
            if(!hasInstance){pending.Add(command);return true;}
            if(!ControlContext.builtIn.Exists(instance))return false;
            return ControlContext.builtIn.SendMessage(instance,ref command)==Response.Handled;
        }
        public bool SwapPrepared(TrackerPreparedSong next,bool playSong=true)
        {
            if(next==null||next.Published||next.Disposed||next.state.sampleRate!=initial.state.sampleRate||!accepting||!hasInstance||TrackerSapRegistry.RefuseNewRendering)return false;
            if(!ControlContext.builtIn.Exists(instance))return false;
            var replacement=next.state;replacement.playing=playSong;replacement.loopSong=true;
            var command=new TrackerCommand{kind=TrackerCommandKind.Swap,replacement=replacement};
            if(ControlContext.builtIn.SendMessage(instance,ref command)!=Response.Handled)return false;
            // Ownership is transferred before the graph can use the queued replacement. No main-thread reads of its arrays follow.
            next.Published=true;owned.Add(next);current=next;return true;
        }
        public long EventOverflow=>hasRing?ring.OverflowCount:0;
        public bool ReadEvent(out TrackerEvent value){if(!hasRing){value=default;return false;}return ring.TryRead(out value);}
        public long RenderedFrames
        {
            get{long total=0;foreach(var p in owned)if(!p.Disposed&&p.state.ticket.IsCreated)total+=Volatile.Read(ref ((long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(p.state.ticket))[1]);return total;}
        }
        public bool StopAndConfirm(double timeout=.5)
        {
            accepting=false;var carrier=GetComponent<AudioSource>();if(carrier!=null)carrier.Stop();
            if(!transferred)return true;
            double settle=SapLifetime.DefaultSettleSeconds(1024,initial.state.sampleRate);
            bool quiet=SapLifetime.WaitUntilQuiet(owned.Count,i=>SapRenderTicket.Read(owned[i].state.ticket),settle,timeout);
            if(!quiet)return false;
            if(hasInstance&&ControlContext.builtIn.Exists(instance)){ControlContext.WaitForBuiltInQueueFlush();ControlContext.builtIn.Destroy(instance);}hasInstance=false;
            return true;
        }
        void Update(){CollectRetired();}
        void CollectRetired()
        {
            for(int i=owned.Count-1;i>=0;i--){var p=owned[i];if(p==current||p.Disposed)continue;
                var ptr=(long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(p.state.ticket);
                if(Volatile.Read(ref ptr[2])==0)continue;
                if(p.WaitUntilQuiet(.06,.07)){p.DisposeAfterQuiet();owned.RemoveAt(i);}
            }
        }
        internal void Silence(){accepting=false;var source=GetComponent<AudioSource>();if(source!=null)source.Stop();}
        void OnDestroy()
        {
            TrackerSapRegistry.Unregister(this);
            bool quiet=!transferred||StopAndConfirm(.25);
            if(quiet){foreach(var p in owned)if(!p.Disposed)p.DisposeAfterQuiet();if(hasRing)ring.Dispose();}
            else TrackerSapRegistry.Retain(owned,ring,hasRing);
            owned.Clear();hasRing=false;
        }
        struct Control:GeneratorInstance.IControl<TrackerRealtime>
        {
            public int rate;
            public void Configure(ControlContext context,ref TrackerRealtime realtime,in AudioFormat format,out GeneratorInstance.Setup setup,ref GeneratorInstance.Properties properties){setup=new GeneratorInstance.Setup(speakerMode:AudioSpeakerMode.Stereo,sampleRate:rate);}
            public void Update(ControlContext context,Pipe pipe){}
            public Response OnMessage(ControlContext context,Pipe pipe,Message message){if(!message.Is<TrackerCommand>())return Response.Unhandled;var c=message.Get<TrackerCommand>();pipe.SendData(context,c);return Response.Handled;}
            public void Dispose(ControlContext context,ref TrackerRealtime realtime){SapRenderTicket.Enter(realtime.state.ticket);SapRenderTicket.Exit(realtime.state.ticket,0,true);}
        }
    }
    /// <summary>Shared generator retirement and real-frame quit drain. Refuses new publications once shutdown starts.</summary>
    internal static class TrackerSapRegistry
    {
        static readonly List<TrackerSapGenerator> hosts=new List<TrackerSapGenerator>();
        sealed class Retained { public List<TrackerPreparedSong> songs;public TrackerEventRing ring;public bool hasRing;public SapQuietWindow quiet; }
        static readonly List<Retained> retained=new List<Retained>();
        static SapQuitDrain drain;
        static bool installed;
        public static bool RefuseNewRendering=>drain.RefuseNewRendering;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Init(){drain=default;Install();}
        static void Install(){if(installed)return;installed=true;Application.wantsToQuit+=WantsQuit;}
        public static void Register(TrackerSapGenerator host){Install();hosts.Add(host);}
        public static void Unregister(TrackerSapGenerator host){hosts.Remove(host);}
        public static void Retain(List<TrackerPreparedSong> songs,TrackerEventRing ring,bool hasRing){retained.Add(new Retained{songs=new List<TrackerPreparedSong>(songs),ring=ring,hasRing=hasRing});}
        static bool WantsQuit(){if(drain.complete)return true;if(!drain.draining){drain.Begin(Time.realtimeSinceStartupAsDouble);foreach(var host in hosts)if(host!=null)host.Silence();EnsurePump();}return false;}
        static void EnsurePump(){var obj=new GameObject("Tracker SAP lifetime drain"){hideFlags=HideFlags.HideAndDontSave};UnityEngine.Object.DontDestroyOnLoad(obj);obj.AddComponent<TrackerSapDrainPump>();}
        internal static void Tick()
        {
            if(drain.Tick(Time.realtimeSinceStartupAsDouble)){
                foreach(var host in hosts)if(host!=null)host.StopAndConfirm(.5);
                Application.Quit();
            }
            // A timeout retains the full ownership bundle. It is never freed on an optimistic stable snapshot.
            // Terminal tickets prove the graph disposal/swap has prevented any further reader.
            for(int i=retained.Count-1;i>=0;i--){var bundle=retained[i];bool terminal=true;foreach(var p in bundle.songs){unsafe {var ptr=(long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(p.state.ticket);if(Volatile.Read(ref ptr[2])==0)terminal=false;}}
                if(!terminal)continue;bool quiet=true;foreach(var p in bundle.songs)if(!p.WaitUntilQuiet(.06,.07))quiet=false;
                if(!quiet)continue;foreach(var p in bundle.songs)p.DisposeAfterQuiet();if(bundle.hasRing)bundle.ring.Dispose();retained.RemoveAt(i);
            }
        }
    }
    internal sealed class TrackerSapDrainPump:MonoBehaviour{void Update()=>TrackerSapRegistry.Tick();}
}
