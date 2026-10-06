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
        readonly TrackerPreparedSong prepared;
        readonly NativeArray<int> witness;
        public TrackerOffline(TrackerPreparedSong song,int eventCapacity=65536)
        {
            if(song==null||song.Disposed||song.Published)throw new ArgumentException("Prepared song is already owned");
            prepared=song;prepared.Published=true;witness=new NativeArray<int>(1,Allocator.Persistent);
            realtime=(TrackerRealtime*)UnsafeUtility.Malloc(sizeof(TrackerRealtime),16,Allocator.Persistent);
            *realtime=new TrackerRealtime{state=song.state,events=TrackerEventRing.Create(eventCapacity)};
        }
        public bool Compiled=>witness[0]==1;
        public long EventOverflow=>realtime->events.OverflowCount;
        public TrackerState Snapshot=>realtime->state;
        public void SendCommand(TrackerCommand command){if(command.kind==TrackerCommandKind.Swap)throw new ArgumentException("Offline swap requires a new owned engine");realtime->Apply(in command);}
        public bool ReadEvent(out TrackerEvent value)=>realtime->events.TryRead(out value);
        public void Render(NativeArray<float> left,NativeArray<float> right,int frames,int blockFrames=1024)
        {
            if(frames<0||frames>left.Length||frames>right.Length||blockFrames<1||blockFrames>prepared.state.maxFrames)throw new ArgumentOutOfRangeException(nameof(frames));
            new TrackerRenderJob{realtime=realtime,left=left,right=right,witness=witness,frames=frames,blockFrames=blockFrames}.Run();
        }
        public void RenderManaged(int frames)
        {
            if(frames<1||frames>prepared.state.maxFrames)throw new ArgumentOutOfRangeException(nameof(frames));realtime->Render(frames);
        }
        public void Dispose()
        {
            if(realtime==null)return;prepared.state=realtime->state;realtime->events.Dispose();UnsafeUtility.Free(realtime,Allocator.Persistent);realtime=null;witness.Dispose();prepared.DisposeAfterQuiet();
        }
    }
}
