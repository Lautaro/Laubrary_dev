using System;
using System.Collections.Generic;
using System.Text;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>Live-refresh regression checks using the actual Burst renderer; no Test Runner or user assets.</summary>
    public static unsafe class TrackerLiveCheck
    {
        public static string Execute()
        {
            var report=new StringBuilder();int pass=0,fail=0;
            Action<string,Action> check=(name,run)=>{try{run();pass++;report.AppendLine("PASS "+name);}catch(Exception ex){fail++;report.AppendLine("FAIL "+name+": "+ex.Message);}};
            var clip=AudioClip.Create("live-refresh-check",48000,1,48000,false);var data=new float[48000];for(int i=0;i<data.Length;i++)data[i]=.1f;clip.SetData(data,0);
            var inst=TrackerEngineCheck.FixtureInstrument(clip);var left=new NativeArray<float>(48000,Allocator.Persistent);var right=new NativeArray<float>(48000,Allocator.Persistent);
            try{foreach(int partition in new[]{64,333,1024}){
                check("held voice, clock, inserted notes and safe retirement / "+partition,()=>{
                    var song=TrackerEngineCheck.FixtureSong(inst);Write(song,0,TrackerEngineCheck.Note());
                    var original=TrackerPreparedSong.Prepare(song);using(var engine=new TrackerOffline(original)){
                        engine.SendCommand(TrackerCommand.Play());engine.Render(left,right,1000,partition);var before=engine.Snapshot;var voice=before.voices[0];long frame=before.samplePosition;double pos=voice.position,rowStart=before.rowStart;long cohort=voice.cohort;
                        Write(song,1,TrackerEngineCheck.Note(72));var next=TrackerPreparedSong.Prepare(song);Need(engine.RefreshPrepared(next,out var reason),reason);
                        var after=engine.Snapshot;Need(after.samplePosition==frame&&after.rowStart==rowStart&&after.voices[0].position==pos&&after.voices[0].cohort==cohort,"Refresh reset clock or held voice");Need(original.Disposed,"Terminal owner was not retired safely");
                        // Control disposal can receive the original realtime copy
                        // after a swap has retired its ticket. It must acknowledge
                        // the graph without reading that released song storage.
                        using(var graphDone=new NativeArray<long>(1,Allocator.Persistent)){
                            var type=typeof(TrackerSapGenerator).GetNestedType("Control",System.Reflection.BindingFlags.NonPublic);
                            object control=Activator.CreateInstance(type);type.GetField("disposed").SetValue(control,graphDone);
                            var stale=new TrackerRealtime{state=before};Need(graphDone[0]==0,"Graph acknowledgement was not initially pending");
                            type.GetMethod("Dispose").Invoke(control,new object[]{UnityEngine.Audio.ControlContext.builtIn,stale});
                            Need(graphDone[0]==1,"Deferred graph consumer did not acknowledge disposal");
                        }
                        engine.Render(left,right,6000,partition);bool changed=false;while(engine.ReadEvent(out var e))if(e.kind==TrackerEventKind.NoteOn&&e.note==72&&e.samplePosition==6000)changed=true;Need(changed,"Inserted next-row note was not rendered at its original deadline");Need(engine.Compiled,"Managed renderer used");
                    }});
                check("chain scalar changes rendered held output without reset / "+partition,()=>{
                    var song=TrackerEngineCheck.FixtureSong(inst);song.tracks[0].devices.nodes.Add(new AudioEffectNodeData{type=Laubrary.Zounds.ZoundEffectType.Gain,uid="gain",p=new[]{1f}});Write(song,0,TrackerEngineCheck.Note());
                    using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(song))){engine.SendCommand(TrackerCommand.Play());engine.Render(left,right,2000,partition);float before=left[1999];var snapshot=engine.Snapshot;long frame=snapshot.samplePosition,age=snapshot.voices[0].age;long elapsed=snapshot.chains[0].processor.elapsedSamples;
                        song.tracks[0].devices.nodes[0].p[0]=.25f;var next=TrackerPreparedSong.Prepare(song);Need(engine.RefreshPrepared(next,out var reason),reason);Need(engine.Snapshot.voices[0].age==age&&engine.Snapshot.samplePosition==frame&&engine.Snapshot.chains[0].processor.elapsedSamples==elapsed,"Held state reset by chain scalar");engine.Render(left,right,2000,partition);Need(Math.Abs(left[1999]/before-.25)<.0001,"New chain gain not audible in rendered output: "+left[1999]/before);
                    }});
                check("delayed launch keeps original payload across sparse edit / "+partition,()=>{
                    var song=TrackerEngineCheck.FixtureSong(inst);var n=TrackerEngineCheck.Note(65);n.delayPresent=true;n.delay=128;Write(song,0,n);
                    using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(song))){engine.SendCommand(TrackerCommand.Play());engine.Render(left,right,1000,partition);song.patterns[0].tracks[0].lines.Clear();Write(song,1,TrackerEngineCheck.Note(70));var next=TrackerPreparedSong.Prepare(song);Need(engine.RefreshPrepared(next,out var reason),reason);engine.Render(left,right,2500,partition);bool note=false;while(engine.ReadEvent(out var e))if(e.kind==TrackerEventKind.NoteOn&&e.note==65&&e.samplePosition==3000)note=true;Need(note,"Delayed payload lost or retargeted to edited data");}
                });
                check("command memory banks survive insertion and removal / "+partition,()=>{
                    var song=TrackerEngineCheck.FixtureSong(inst);var row=new PatternLine{line=0,notes=new List<NoteCell>{TrackerEngineCheck.Note()},effects=new List<EffectCell>{new EffectCell{column=0,command=Command("0U",12)}}};song.patterns[0].tracks[0].WriteLine(row);
                    using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(song))){engine.SendCommand(TrackerCommand.Play());engine.Render(left,right,2000,partition);int memory=engine.Snapshot.commandMemory[0];Need(memory>0,"Memory fixture not initialized");song.patterns[0].tracks[0].lines.Clear();var another=new PatternLine{line=1,effects=new List<EffectCell>{new EffectCell{command=Command("0D",5)}}};song.patterns[0].tracks[0].WriteLine(another);var next=TrackerPreparedSong.Prepare(song);Need(engine.RefreshPrepared(next,out var reason),reason);Need(engine.Snapshot.commandMemory[0]==memory,"Historical bank dropped or retargeted");engine.Render(left,right,5000,partition);Need(engine.Compiled,"Native refresh path unavailable");}
                });
            }
                check("unsafe structural change refused without mutation",()=>{var song=TrackerEngineCheck.FixtureSong(inst);Write(song,0,TrackerEngineCheck.Note());using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(song))){engine.SendCommand(TrackerCommand.Play());engine.Render(left,right,500);long frame=engine.Snapshot.samplePosition;song.tracks[0].id="different";song.patterns[0].tracks[0].trackId="different";using(var next=TrackerPreparedSong.Prepare(song)){Need(!engine.RefreshPrepared(next,out var reason)&&reason.Contains("stop and play"),"Unsafe identity accepted");Need(engine.Snapshot.samplePosition==frame,"Refusal changed active renderer");}}});
                check("tempo refresh respects current-row clock",()=>{var song=TrackerEngineCheck.FixtureSong(inst);Write(song,0,TrackerEngineCheck.Note());using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(song))){engine.SendCommand(TrackerCommand.Play());engine.Render(left,right,1000);song.bpm=240;var next=TrackerPreparedSong.Prepare(song);Need(engine.RefreshPrepared(next,out var reason),reason);Need(engine.Snapshot.bpm==120,"Timing changed mid-row");engine.Render(left,right,6000);Need(engine.Snapshot.bpm==240&&engine.Snapshot.rowStart==6000&&engine.Snapshot.rowEnd==9000,"Timing refresh altered current deadline or was not applied next row");}});
            }
            finally{left.Dispose();right.Dispose();UnityEngine.Object.DestroyImmediate(inst);UnityEngine.Object.DestroyImmediate(clip);}
            report.AppendLine($"TOTAL passed={pass} failed={fail}");return report.ToString();
        }
        static CommandData Command(string id,int value)=>new CommandData{present=true,valuePresent=true,identifier=id,value=value,scope=CommandScope.Track};
        static void Write(SongData s,int row,NoteCell note)=>s.patterns[0].tracks[0].WriteLine(new PatternLine{line=row,notes=new List<NoteCell>{note}});
        static void Need(bool condition,string message){if(!condition)throw new Exception(message);}
    }
}
