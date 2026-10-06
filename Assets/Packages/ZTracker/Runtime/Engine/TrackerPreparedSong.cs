using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>Exclusive main-thread owner of one immutable compiled song and its render state. Never dispose a published song without a quiet ticket.</summary>
    public sealed unsafe class TrackerPreparedSong : IDisposable
    {
        public TrackerState state;
        public readonly List<string> diagnostics = new List<string>();
        public readonly List<string> eventPayloads = new List<string>();
        public bool Published { get; internal set; }
        public bool Disposed { get; private set; }
        public const string ImplementationWitness = "P3-native-tracker-core-v1";
        public int PcmCount => state.pcmCount;
        public static TrackerPreparedSong Prepare(ZTrackerSong song, int rate = 48000, int maxFrames = 16384)
        {
            if(song==null||song.schemaVersion!=1)throw new ArgumentException("Engine requires an explicitly migrated schema-1 song");
            return Prepare(song.model,rate,maxFrames);
        }
        public static TrackerPreparedSong Prepare(SongData song, int rate = 48000, int maxFrames = 16384)
        {
            string error=ZTrackerModelValidation.Validate(song);
            if(error!=null)throw new ArgumentException(error);
            if(rate<8000||rate>192000||maxFrames<64||maxFrames>65536||song.voiceCapacity>4096||song.tracks.Count>128||song.instruments.Count>4096||song.patterns.Count>4096||song.sequence.Count>65536)throw new ArgumentException("Engine preparation capacity exceeded");
            if(song.bpm>999||song.linesPerBeat>256||song.ticksPerLine>256||rate*60d/(song.bpm*song.linesPerBeat*song.ticksPerLine)<1)throw new ArgumentException("INVALID_CLOCK");
            var owner=new TrackerPreparedSong();var chains=new List<TrackerChain>();
            try {
                owner.Build(song,rate,maxFrames,chains);
                return owner;
            }
            catch {
                // The pointer owns chains only after Build publishes it.
                if(owner.state.chains==null)foreach(var c in chains){var p=c.processor;var l=c.layout;p.Dispose();l.Dispose();}
                owner.Dispose();throw;
            }
        }
        static NativeArray<T> Native<T>(IList<T> list) where T:unmanaged
        {
            var a=new NativeArray<T>(Math.Max(1,list.Count),Allocator.Persistent);
            for(int i=0;i<list.Count;i++)a[i]=list[i];return a;
        }
        static NativeArray<T> Buffer<T>(int count) where T:unmanaged => new NativeArray<T>(Math.Max(1,count),Allocator.Persistent);
        void Build(SongData song,int rate,int maxFrames,List<TrackerChain> chains)
        {
            state.sampleRate=rate;state.maxFrames=maxFrames;state.bpm=song.bpm;state.linesPerBeat=song.linesPerBeat;state.ticksPerLine=song.ticksPerLine;state.beatTicks=song.beatTicks;state.beatInterval=song.beatIntervalLines;state.breakRow=-1;state.normalization=1;state.rowPending=true;
            var trackMap=song.tracks.Select((t,i)=>(t.id,i)).ToDictionary(x=>x.id,x=>x.i);
            state.master=song.tracks.FindIndex(t=>t.kind==TrackKind.Master);state.trackCount=song.tracks.Count;
            var tracks=new List<TrackerTrack>();var sends=new List<TrackerSend>();int col=0;
            bool solo=song.tracks.Any(t=>t.solo);
            // Solo includes ancestors and downstream routing, so a solo child is never muted by its parent bus.
            var soloSet=new HashSet<int>();
            Action<int> include=null;include=i=>{if(!soloSet.Add(i))return;var t=song.tracks[i];int dest=Output(t,trackMap,state.master);if(dest>=0)include(dest);foreach(var s in t.sends)include(trackMap[s.trackId]);};
            for(int i=0;i<song.tracks.Count;i++)if(song.tracks[i].solo){include(i);if(song.tracks[i].kind==TrackKind.Group)for(int j=0;j<song.tracks.Count;j++){string p=song.tracks[j].parentGroupId;while(p!=""){if(p==song.tracks[i].id){include(j);break;}p=song.tracks[trackMap[p]].parentGroupId;}}}
            for(int i=0;i<song.tracks.Count;i++) {
                var t=song.tracks[i];var tr=new TrackerTrack{kind=t.kind,columns=col,columnCount=t.visibleNoteColumns,output=Output(t,trackMap,state.master),preGain=t.preVolume,prePan=t.prePan,width=t.preWidth,postGain=t.postVolume,postPan=t.postPan,triggerMute=t.triggerMute,outputMute=t.outputMute,soloEnabled=!solo||soloSet.Contains(i),beatTicks=t.beatTicks,beatInterval=t.beatIntervalLines,sendStart=sends.Count,sendCount=t.sends.Count,chainStart=chains.Count};
                if(t.preVolume<0||t.postVolume<0||math.abs(t.prePan)>1||math.abs(t.postPan)>1||t.preWidth<0||t.preWidth>4)throw new ArgumentException("Mixer value outside runtime range");
                col+=t.visibleNoteColumns;
                var cuts=new SortedSet<int>{0,t.devices.nodes.Count};foreach(var s in t.sends){cuts.Add(s.devicePosition);sends.Add(new TrackerSend{destination=trackMap[s.trackId],gain=s.gain,position=s.devicePosition,postfader=s.postfader});}
                int prev=0;
                foreach(int cut in cuts){if(cut>prev){chains.Add(TrackerFxCompiler.Compile(t.devices,prev,cut,rate));prev=cut;}}
                tr.chainCount=chains.Count-tr.chainStart;tracks.Add(tr);
            }
            state.columnCount=col;state.tracks=Native(tracks);state.sends=Native(sends);state.columns=Buffer<TrackerColumn>(col);
            for(int i=0;i<col;i++)state.columns[i]=new TrackerColumn{instrument=-1,volume=1,pan=0,due=-1,pendingCell=-1};
            // Kahn order over output AND sends; process every source before every destination.
            var degree=new int[tracks.Count];var edges=new List<int>[tracks.Count];
            for(int i=0;i<tracks.Count;i++){edges[i]=new List<int>();if(tracks[i].output>=0)edges[i].Add(tracks[i].output);for(int j=0;j<tracks[i].sendCount;j++)edges[i].Add(sends[tracks[i].sendStart+j].destination);foreach(int d in edges[i])degree[d]++;}
            var queue=new Queue<int>();for(int i=0;i<degree.Length;i++)if(degree[i]==0)queue.Enqueue(i);var order=new List<int>();
            while(queue.Count>0){int i=queue.Dequeue();order.Add(i);foreach(int d in edges[i])if(--degree[d]==0)queue.Enqueue(d);}
            if(order.Count!=tracks.Count)throw new ArgumentException("Mixer routing cycle");state.order=Native(order);
            var pcm=new List<float>();var clips=new List<TrackerPcm>();var pcmMap=new Dictionary<AudioClip,int>();var samples=new List<TrackerSample>();var zones=new List<TrackerZone>();var insts=new List<TrackerInstrument>();var mods=new List<TrackerMod>();var points=new List<TrackerModPoint>();int stride=0;
            for(int i=0;i<song.instruments.Count;i++) {
                var asset=song.instruments[i];var ins=new TrackerInstrument{zoneStart=zones.Count,chainStart=chains.Count};
                if(asset==null){diagnostics.Add("EMPTY_INSTRUMENT slot="+i);insts.Add(ins);continue;}
                if(asset.schemaVersion!=1)throw new ArgumentException("Instrument requires schema 1 at slot "+i);
                var data=asset.model;string e=ZTrackerModelValidation.Validate(data);if(e!=null)throw new ArgumentException("Instrument "+i+": "+e);
                if(data.family==InstrumentFamily.Synth){diagnostics.Add("P4_SYNTH_SILENCE slot="+i);insts.Add(ins);continue;}
                if(data.sampler.samples.Count>4096||data.sampler.zones.Count>4096||data.modulation.Count>256||data.fxChains.Count>64)throw new ArgumentException("Sampler capacity exceeded");
                ins.nna=data.sampler.nna;
                foreach(var fx in data.fxChains)chains.Add(TrackerFxCompiler.Compile(fx,0,fx.nodes.Count,rate));
                ins.chainCount=chains.Count-ins.chainStart;
                int sampleBase=samples.Count;
                for(int j=0;j<data.sampler.samples.Count;j++) {
                    var s=data.sampler.samples[j];int clipIndex=-1;
                    if(s.pcm!=null&&!s.inactive){
                        if(s.pcm.channels<1||s.pcm.channels>2||s.pcm.samples<1||s.pcm.loadType!=AudioClipLoadType.DecompressOnLoad)throw new ArgumentException("PCM must be mono/stereo Decompress On Load");
                        if(!pcmMap.TryGetValue(s.pcm,out clipIndex)){
                            if((long)pcm.Count+(long)s.pcm.samples*s.pcm.channels>67108864)throw new ArgumentException("PCM pool exceeds 256 MiB");
                            var raw=new float[checked(s.pcm.samples*s.pcm.channels)];if(!s.pcm.GetData(raw,0))throw new ArgumentException("PCM read refused");
                            float peak=0;foreach(float v in raw){if(!math.isfinite(v))throw new ArgumentException("Nonfinite PCM");peak=math.max(peak,math.abs(v));}
                            clipIndex=clips.Count;pcmMap.Add(s.pcm,clipIndex);clips.Add(new TrackerPcm{offset=pcm.Count,frames=s.pcm.samples,channels=s.pcm.channels,frequency=s.pcm.frequency,peak=peak});pcm.AddRange(raw);
                        }
                        if(s.loop!=SampleLoop.Off&&(s.loopStartFrame<0||s.loopEndFrame>s.pcm.samples||s.loopEndFrame-s.loopStartFrame<2))throw new ArgumentException("Loop must contain at least two frames and fit PCM");
                    }
                    var sm=new TrackerSample{pcm=clipIndex,instrument=i,volume=s.volume*data.sampler.volume,pan=CombinePan(data.sampler.pan,s.pan),tune=s.transpose+data.sampler.transpose+(s.fineTuneCents+data.sampler.fineTuneCents)/100,loop=s.loop,loopStart=s.loopStartFrame,loopEnd=s.loopEndFrame,releaseExitsLoop=s.releaseExitsLoop,interpolation=s.interpolation,oneShot=s.oneShot,nna=s.nna,fxChain=s.fxChain,muteGroup=s.muteGroup,modStart=mods.Count,cutoff=20000,resonance=.707f,attack=0,decay=0,sustain=1,release=.05f,delayDestination=-1,reverbDestination=-1};
                    bool amplitudeEnvelope=false;
                    if(s.modulationSet>=0){var set=data.modulation[s.modulationSet];sm.filterType=set.filterType;if(set.filterType<0||set.filterType>3)throw new ArgumentException("Unknown sampler filter");foreach(var m in set.devices)if(m.enabled){if(!amplitudeEnvelope&&m.kind==ModulationDeviceKind.AHDSR&&m.target==ModulationTarget.Volume&&m.operation==ModulationOperation.Multiply){sm.attack=m.attack;sm.hold=m.hold;sm.decay=m.decay;sm.sustain=math.saturate(m.sustain);sm.release=m.release;amplitudeEnvelope=true;}else AddMod(m,mods,points);}}
                    // Typed retained extension is part of v1 authority; conversion is preparation-only.
                    var legacy=data.parameters;
                    if(!string.IsNullOrEmpty(data.provenance)&&legacy!=null){
                        if(!amplitudeEnvelope){
                            sm.attack=math.max(0,legacy.attack);sm.decay=math.max(0,legacy.decay);sm.sustain=math.saturate(legacy.sustain);sm.release=math.max(0,legacy.release);
                        }
                        if(legacy.instFilterEnabled){sm.legacyFilter=1;sm.filterType=legacy.instFilterMode+1;sm.cutoff=math.clamp(legacy.instFilterCutoff*(rate*.5f),20,rate*.5f-100);sm.resonance=math.clamp(legacy.instFilterResonance,.1f,10);diagnostics.Add("LEGACY_VOICE_FILTER_COMPILED slot="+i+" sample="+j);}
                        if(legacy.vibratoDepth!=0){AddMod(new ModulationDevice{kind=ModulationDeviceKind.LFO,target=ModulationTarget.Pitch,operation=ModulationOperation.Add,depth=legacy.vibratoDepth,rate=legacy.vibratoRate},mods,points);diagnostics.Add("LEGACY_VIBRATO_COMPILED slot="+i);}
                        if(legacy.arpeggioEnabled&&legacy.arpeggioNotes!=null&&legacy.arpeggioNotes.Length>0){var arp=new ModulationDevice{kind=ModulationDeviceKind.Multipoint,target=ModulationTarget.Pitch,operation=ModulationOperation.Add,loopEnabled=true,loop=SampleLoop.Forward,loopStart=0,loopEnd=legacy.arpeggioSpeed*legacy.arpeggioNotes.Length};for(int k=0;k<=legacy.arpeggioNotes.Length;k++)arp.points.Add(new ModulationPoint{time=k*legacy.arpeggioSpeed,value=legacy.arpeggioNotes[k%legacy.arpeggioNotes.Length],exponent=100000});AddMod(arp,mods,points);diagnostics.Add("LEGACY_ARPEGGIO_COMPILED slot="+i);}
                        if(legacy.instDelaySend!=0||legacy.instReverbSend!=0){
                            sm.delaySend=legacy.instDelaySend;sm.reverbSend=legacy.instReverbSend;
                            sm.delayDestination=song.tracks.FindIndex(t=>t.kind==TrackKind.Send&&t.devices.nodes.Any(n=>n.type==Laubrary.Zounds.ZoundEffectType.Delay));
                            sm.reverbDestination=song.tracks.FindIndex(t=>t.kind==TrackKind.Send&&t.devices.nodes.Any(n=>n.type==Laubrary.Zounds.ZoundEffectType.Reverb));
                            if((sm.delaySend!=0&&sm.delayDestination<0)||(sm.reverbSend!=0&&sm.reverbDestination<0))throw new ArgumentException("Legacy instrument sends require explicit song Send tracks with matching Delay/Reverb devices");
                            diagnostics.Add("LEGACY_SEND_COMPILED_TO_EXPLICIT_TRACK slot="+i);
                        }
                    }
                    if(data.sampler.nna!=NewNoteAction.NoteOff)sm.nna=data.sampler.nna;
                    sm.modCount=mods.Count-sm.modStart;stride=Math.Max(stride,sm.modCount);if(stride>64)throw new ArgumentException("More than 64 modulation devices per sample");samples.Add(sm);
                }
                foreach(var z in data.sampler.zones)if(!z.inactive){zones.Add(new TrackerZone{sample=sampleBase+z.sample,minNote=z.noteMin,maxNote=z.noteMax,minVelocity=z.velocityMin,maxVelocity=z.velocityMax,baseNote=z.baseNote,tracking=z.keyTracking});if(z.blend!=null&&z.blend.pcmB!=null)diagnostics.Add("P4_SAMPLE_B_DEFERRED slot="+i+" zone="+z.id);}
                ins.zoneCount=zones.Count-ins.zoneStart;insts.Add(ins);
                if(data.macros.Any(m=>m.mappings.Count>0))diagnostics.Add("P4_MACROS_DEFERRED slot="+i);
            }
            state.pcm=Native(pcm);state.clips=Native(clips);state.pcmCount=clips.Count;state.samples=Native(samples);state.sampleCount=samples.Count;state.zones=Native(zones);state.instruments=Native(insts);state.instrumentCount=insts.Count;state.mods=Native(mods);state.points=Native(points);state.modStride=Math.Max(1,stride);state.modulationState=Buffer<TrackerModState>(checked(song.voiceCapacity*state.modStride));state.voices=Buffer<TrackerVoice>(song.voiceCapacity);
            // Every (track,instrument,FX-chain) partition has independent effect history.
            var buses=new List<TrackerBus>();
            for(int t=0;t<tracks.Count;t++)if(tracks[t].kind==TrackKind.Sequencer)for(int i=0;i<insts.Count;i++)for(int f=0;f<insts[i].chainCount;f++){
                var original=song.instruments[i].model.fxChains[f];int chain=chains.Count;chains.Add(TrackerFxCompiler.Compile(original,0,original.nodes.Count,rate));buses.Add(new TrackerBus{track=t,instrument=i,fx=f,chain=chain});
            }
            if(buses.Count>8192)throw new ArgumentException("Instrument FX routing exceeds 8192 partitions");state.buses=Native(buses);state.busCount=buses.Count;
            state.left=Buffer<float>(checked(tracks.Count*maxFrames));state.right=Buffer<float>(checked(tracks.Count*maxFrames));state.busLeft=Buffer<float>(checked(buses.Count*maxFrames));state.busRight=Buffer<float>(checked(buses.Count*maxFrames));state.outputLeft=Buffer<float>(maxFrames);state.outputRight=Buffer<float>(maxFrames);
            var patterns=new List<TrackerPattern>();var rows=new List<TrackerRow>();var cells=new List<TrackerCell>();var cmds=new List<TrackerClockCommand>();var events=new List<TrackerAuthoredEvent>();var pmap=new Dictionary<string,int>();
            foreach(var p in song.patterns){pmap.Add(p.id,patterns.Count);patterns.Add(new TrackerPattern{rows=rows.Count,lineCount=p.lineCount});
                for(int line=0;line<p.lineCount;line++){
                    var rr=new TrackerRow{cells=cells.Count,commands=cmds.Count,events=events.Count};
                    foreach(var pt in p.tracks){int ti=trackMap[pt.trackId];var src=pt.lines.Find(l=>l.line==line);if(src==null)continue;
                        foreach(var c in src.notes)if(!c.inactive){var cc=new TrackerCell{track=ti,column=c.column,note=c.note==NoteKind.Note?c.pitch:c.note==NoteKind.Off?-2:-1,instrument=c.instrument,instrumentPresent=c.instrumentPresent,volume=c.volume.kind==ValueKind.Value?c.volume.value:-1,pan=c.pan.kind==ValueKind.Value?c.pan.value:-1,delay=c.delayPresent?c.delay:0};cells.Add(cc);
                            if(c.note==NoteKind.Legacy)diagnostics.Add("UNSUPPORTED_LEGACY_NOTE pattern="+p.id+" row="+line);
                            if(c.volume.kind==ValueKind.Command||c.pan.kind==ValueKind.Command||c.sampleFx.HasPayload||c.parameterSetId!="")diagnostics.Add("P5_COLUMN_COMMAND_DEFERRED pattern="+p.id+" row="+line+" track="+ti+" column="+c.column);
                        }
                        foreach(var fx in src.effects)if(!fx.inactive&&fx.command.HasPayload){var c=fx.command;int kind=c.identifier=="ZT"?1:c.identifier=="ZL"?2:c.identifier=="ZK"?3:c.identifier=="ZB"?4:0;int value=c.valuePresent?c.value:0;
                            if(kind==0||c.unsupported||!c.present){diagnostics.Add("P5_COMMAND_IGNORED pattern="+p.id+" row="+line+" "+c.identifier);continue;}
                            if(!c.valuePresent)diagnostics.Add("IMPLICIT_ZERO "+c.identifier+" pattern="+p.id+" row="+line);
                            if((kind==1&&(value<20||value>255))||(kind==2&&(value<1||value>256))||(kind==3&&(value<1||value>16))||(kind==4&&(value<0||value>255))) {diagnostics.Add("INVALID_CLOCK_COMMAND "+c.identifier);continue;}
                            cmds.Add(new TrackerClockCommand{kind=kind,value=value});
                        }
                        foreach(var ev in src.events)if(ev.present){int payload=eventPayloads.Count;eventPayloads.Add(ev.payload);events.Add(new TrackerAuthoredEvent{track=ti,column=ev.column,payload=payload});}
                    }
                    rr.cellCount=cells.Count-rr.cells;rr.commandCount=cmds.Count-rr.commands;rr.eventCount=events.Count-rr.events;rows.Add(rr);
                }
                if(p.tracks.Any(pt=>pt.automation.Any(a=>a.enabled)))diagnostics.Add("P5_AUTOMATION_DEFERRED pattern="+p.id);
            }
            var seq=new List<TrackerSequence>();var mutes=new List<byte>();foreach(var slot in song.sequence){seq.Add(new TrackerSequence{pattern=pmap[slot.patternId],muteOffset=mutes.Count});for(int i=0;i<tracks.Count;i++)mutes.Add(slot.mutedTrackIds.Contains(song.tracks[i].id)?(byte)1:(byte)0);}
            state.patterns=Native(patterns);state.rows=Native(rows);state.cells=Native(cells);state.commands=Native(cmds);state.authoredEvents=Native(events);state.sequence=Native(seq);state.sequenceMutes=Native(mutes);
            if(chains.Count>16384)throw new ArgumentException("Chain count exceeds 16384");state.chainCount=chains.Count;state.chains=(TrackerChain*)UnsafeUtility.Malloc(Math.Max(1,chains.Count)*(long)sizeof(TrackerChain),16,Allocator.Persistent);for(int i=0;i<chains.Count;i++)state.chains[i]=chains[i];
            state.ticket=SapRenderTicket.Create(Allocator.Persistent);
        }
        static int Output(TrackData t,Dictionary<string,int> map,int master)=>t.kind==TrackKind.Master?-1:t.outputTrackId!=""?map[t.outputTrackId]:t.parentGroupId!=""?map[t.parentGroupId]:master;
        internal static float CombinePan(float a,float b){float r=(a+1)*.5f;r=b<0?r*(1+b):r+(1-r)*b;return r*2-1;}
        static void AddMod(ModulationDevice m,List<TrackerMod> mods,List<TrackerModPoint> points)
        {
            if(m.units!="seconds"&&m.units!="normalized"&&m.units!="semitones"&&m.units!="Hz")throw new ArgumentException("Unsupported sampler modulation units "+m.units);
            mods.Add(new TrackerMod{target=m.target,kind=m.kind,operation=m.operation,attack=m.attack,hold=m.hold,decay=m.decay,sustain=m.sustain,release=m.release,rate=m.rate,depth=m.depth,phase=m.phase,min=m.min,max=m.max,curve=m.curve,points=points.Count,pointCount=m.points.Count,shape=m.lfoShape,sustainPosition=m.sustainPosition,sustainEnabled=m.sustainEnabled,loopStart=m.loopStart,loopEnd=m.loopEnd,loopEnabled=m.loopEnabled,loop=m.loop});
            foreach(var p in m.points)points.Add(new TrackerModPoint{time=p.time,value=p.value,exponent=p.exponent});
        }
        public bool WaitUntilQuiet(double settle=.06,double timeout=.5)=>SapLifetime.WaitUntilQuiet(1,_=>SapRenderTicket.Read(state.ticket),settle,timeout);
        internal void DisposeAfterQuiet(){Published=false;Dispose();}
        public void Dispose()
        {
            if(Disposed)return;if(Published)throw new InvalidOperationException("Stop host publication and confirm ticket quiet before freeing a published song");Disposed=true;
            for(int i=0;i<state.chainCount;i++){state.chains[i].processor.Dispose();state.chains[i].layout.Dispose();}
            if(state.chains!=null)UnsafeUtility.Free(state.chains,Allocator.Persistent);
            Free(ref state.pcm);Free(ref state.left);Free(ref state.right);Free(ref state.busLeft);Free(ref state.busRight);Free(ref state.outputLeft);Free(ref state.outputRight);Free(ref state.clips);Free(ref state.samples);Free(ref state.instruments);Free(ref state.zones);Free(ref state.mods);Free(ref state.points);Free(ref state.modulationState);Free(ref state.voices);Free(ref state.columns);Free(ref state.tracks);Free(ref state.sends);Free(ref state.order);Free(ref state.patterns);Free(ref state.rows);Free(ref state.cells);Free(ref state.commands);Free(ref state.authoredEvents);Free(ref state.sequence);Free(ref state.sequenceMutes);Free(ref state.buses);Free(ref state.ticket);state=default;
        }
        static void Free<T>(ref NativeArray<T> a)where T:struct{if(a.IsCreated)a.Dispose();a=default;}
    }
}
