using System;
using Laubrary.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    [BurstCompile(CompileSynchronously=true,FloatMode=FloatMode.Strict)]
    public unsafe struct TrackerRenderJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public TrackerRealtime* realtime;
        public NativeArray<float> left,right;
        public NativeArray<int> witness;
        public int frames,blockFrames;
        [BurstDiscard] void Managed(){witness[0]=0;}
        public void Execute()
        {
            witness[0]=1;Managed();
            int at=0;while(at<frames){int n=math.min(math.min(blockFrames,realtime->state.maxFrames),frames-at);realtime->Render(n);for(int i=0;i<n;i++){left[at+i]=realtime->state.outputLeft[i];right[at+i]=realtime->state.outputRight[i];}at+=n;}
        }
    }
    /// <summary>Owned synchronous offline engine. Render executes the actual Burst realtime struct and preserves every scalar across calls.</summary>
    public sealed unsafe class TrackerOffline : IDisposable
    {
        [NativeDisableUnsafePtrRestriction] TrackerRealtime* realtime;
        TrackerPreparedSong prepared;
        readonly NativeArray<int> witness;
        public TrackerOffline(TrackerPreparedSong song,int eventCapacity=65536)
        {
            if(song==null||song.Disposed||song.Published)throw new ArgumentException("Prepared song is already owned");
            if(eventCapacity<2||eventCapacity>1048576)throw new ArgumentOutOfRangeException(nameof(eventCapacity));
            prepared=song;prepared.Published=true;witness=new NativeArray<int>(1,Allocator.Persistent);
            realtime=(TrackerRealtime*)UnsafeUtility.Malloc(sizeof(TrackerRealtime),16,Allocator.Persistent);
            *realtime=new TrackerRealtime{state=song.state,events=TrackerEventRing.Create(eventCapacity)};
        }
        void RequireAlive(){if(realtime==null)throw new ObjectDisposedException(nameof(TrackerOffline));}
        public bool Compiled{get{RequireAlive();return witness[0]==1;}}
        public long EventOverflow{get{RequireAlive();return realtime->events.OverflowCount;}}
        public TrackerState Snapshot{get{RequireAlive();return realtime->state;}}
        public void SetMacro(int instrument,int macro,float value)=>SendCommand(TrackerCommand.SetMacro(instrument,macro,value));
        public void TargetMacro(int instrument,int macro,float value,float step)=>SendCommand(TrackerCommand.TargetMacro(instrument,macro,value,step));
        public float ObserveMacro(int instrument,int macro){RequireAlive();if(instrument<0||instrument>=realtime->state.instrumentCount||macro<0||macro>=8)throw new ArgumentOutOfRangeException();return realtime->state.macros[instrument*8+macro].value;}
        public bool SetExternal(string track,string device,int slot,float normalized){RequireAlive();if(!prepared.TryExternalCommand(track,device,slot,normalized,out var command))return false;SendCommand(command);return true;}
        public void SendCommand(TrackerCommand command){RequireAlive();if(command.kind==TrackerCommandKind.Swap||command.kind==TrackerCommandKind.PreserveSwap)throw new ArgumentException("Use the owned refresh API for live updates");realtime->Apply(in command);}
        public bool RefreshPrepared(TrackerPreparedSong next,out string reason)
        {
            RequireAlive();if(next==null){reason="Prepared song missing";return false;}if(!next.PreparePreserving(prepared,out reason))return false;
            var command=new TrackerCommand{kind=TrackerCommandKind.PreserveSwap,replacement=next.state};realtime->Apply(in command);
            prepared.DisposeAfterQuiet();next.Published=true;prepared=next;return true;
        }
        public bool ReadEvent(out TrackerEvent value){RequireAlive();return realtime->events.TryRead(out value);}
        public void Render(NativeArray<float> left,NativeArray<float> right,int frames,int blockFrames=1024)
        {
            RequireAlive();if(!left.IsCreated||!right.IsCreated||frames<0||frames>left.Length||frames>right.Length||blockFrames<1||blockFrames>prepared.state.maxFrames)throw new ArgumentOutOfRangeException(nameof(frames));
            new TrackerRenderJob{realtime=realtime,left=left,right=right,witness=witness,frames=frames,blockFrames=blockFrames}.Run();
        }
        public void RenderManaged(int frames)
        {
            RequireAlive();if(frames<1||frames>prepared.state.maxFrames)throw new ArgumentOutOfRangeException(nameof(frames));realtime->Render(frames);
        }
        public void Dispose()
        {
            if(realtime==null)return;prepared.state=realtime->state;realtime->events.Dispose();UnsafeUtility.Free(realtime,Allocator.Persistent);realtime=null;witness.Dispose();prepared.DisposeAfterQuiet();
        }
    }
}
