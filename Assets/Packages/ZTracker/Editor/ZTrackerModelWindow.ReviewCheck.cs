using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Laubrary.Audio;
using Laubrary.Zounds;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>Independent P6 regressions: nullable imports, reference edits and held compiled audio. Disposable fixtures only.</summary>
        public static string CheckReviewWorkflow()
        {
            var report=new StringBuilder();int passed=0,failed=0;
            var w=CreateInstance<ZTrackerModelWindow>();var asset=CreateInstance<ZTrackerSong>();var inst=CreateInstance<ZTrackerInstrument>();
            asset.schemaVersion=inst.schemaVersion=1;inst.model=NewInstrumentData();asset.model=TrackerEngineCheck.FixtureSong(inst);w.song=asset;w.instrument=inst;w.pane=1;w.Show();w.CreateGUI();
            void Need(bool ok,string why){if(!ok)throw new Exception(why);}
            void Fresh(){w.StopPreview();Undo.ClearUndo(asset);Undo.ClearUndo(inst);asset.serializedNulls=inst.serializedNulls=null;inst.model=NewInstrumentData();asset.model=TrackerEngineCheck.FixtureSong(inst);w.track=0;w.pane=1;w.mixerTab=0;w.BuildPane();}
            void Check(string name,Action run){try{Fresh();run();passed++;report.AppendLine("PASS "+name);}catch(Exception e){failed++;report.AppendLine("FAIL "+name+": "+(e.InnerException??e).Message);}}
            void Click(string name){var button=w.rootVisualElement.Q<Button>(name);Need(button!=null,"Missing "+name);typeof(Clickable).GetMethod("SimulateSingleClick",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(button.clickable,new object[]{null,0});}
            void Malformed()
            {
                var t=asset.model.tracks[0];t.sourceDevices.Add(null);t.sourceDevices.Add(new SourceDeviceData{id="raw",ordinal=20,kind=SourceDeviceKind.Unsupported,parameters=null,rawSource="opaque raw source"});
                t.sourceDevices.Add(new SourceDeviceData{id="target-null",ordinal=21,kind=SourceDeviceKind.AudioChain,parameters=new List<SourceParameterData>{null,new SourceParameterData{ordinal=1,target=null}}});
                t.sourceDevices.Add(new SourceDeviceData{id="macro-null",ordinal=22,kind=SourceDeviceKind.InstrumentMacros,instrumentId=inst.model.id,parameters=null});
                var pt=asset.model.patterns[0].tracks[0];pt.automation.Add(new AutomationLane{id="raw-lane",unsupported=true,target=null,points=null,rawSource="opaque raw lane"});pt.automation.Add(null);
            }
            string Snapshot()=>ZTrackerMigration.Json(asset.model)+"|"+string.Join(";",ZTrackerMigration.NullPaths(asset.model));
            try
            {
                Check("nullable source and unsupported automation views are read-only",()=>{Malformed();string before=Snapshot();for(int pane=1;pane<=2;pane++)for(int tab=0;tab<3;tab++){w.pane=pane;w.mixerTab=tab;w.BuildPane();}Need(Snapshot()==before,"View normalized raw/null payload");w.pane=2;w.automationLane=0;w.BuildPane();Click("remove-automation-lane");Need(asset.model.patterns[0].tracks[0].automation.Count==1,"Raw lane removal failed");Undo.PerformUndo();Need(asset.model.patterns[0].tracks[0].automation[0].target==null&&asset.model.patterns[0].tracks[0].automation[0].points==null,"Undo lost raw null paths");});
                Check("clone remove and chain repair preserve nullable imported records",()=>{Malformed();var t=asset.model.tracks[0];t.devices.nodes.Add(new AudioEffectNodeData{uid="gain",type=ZoundEffectType.Gain,p=new[]{1f}});w.CloneTrack();var copy=w.SelectedTrack;Need(copy.sourceDevices.Count==4&&copy.sourceDevices[0]==null&&copy.sourceDevices[1].parameters==null&&copy.sourceDevices[2].parameters[1].target==null,"Clone normalized raw fields");var pt=w.Pattern.tracks.Find(p=>p.trackId==copy.id);Need(pt.automation[0].target==null&&pt.automation[0].points==null&&pt.automation[1]==null,"Clone normalized lane metadata");w.RepairChainReferences(copy,copy.devices.nodes.ToArray());w.FlagRemovedDevice(copy,"gain");w.pane=1;w.mixerTab=2;w.BuildPane();string raw=copy.sourceDevices[1].id;Click("remove-source-"+raw);Need(!w.SelectedTrack.sourceDevices.Any(s=>s!=null&&s.id==raw),"Nullable neighbors prevented source removal");Undo.PerformUndo();Need(w.SelectedTrack.sourceDevices.Any(s=>s!=null&&s.id==raw&&s.parameters==null),"Source Undo lost null metadata");w.RemoveTrack();Need(asset.model.tracks.Count==2,"Nullable lanes prevented track removal");Undo.PerformUndo();Need(asset.model.tracks.Count==3,"Track Undo failed");});
                Check("preset deletion retains later variation identity and joint Undo",()=>{var q=inst.model.parameters;for(int i=0;i<3;i++)q.presets.Add(new ZTrackerInstrument.InstrumentPreset{name="Preset "+i,volume=.2f*(i+1),ovrVolPan=true});var pt=asset.model.patterns[0].tracks[0];for(int i=0;i<3;i++){var note=TrackerEngineCheck.Note();note.parameterSetId=inst.model.id+"/preset-"+i;pt.WriteLine(new PatternLine{line=i,notes=new List<NoteCell>{note}});}w.pane=3;w.instrumentTab=5;w.presetIndex=1;w.BuildPane();Click("remove-instrument-preset");Need(inst.model.parameters.presets.Count==2&&pt.ReadLine(1).notes[0].parameterSetId==""&&pt.ReadLine(2).notes[0].parameterSetId==inst.model.id+"/preset-1","Deletion retargeted notes");Need(Math.Abs(ZTrackerMigration.ResolveParameterSets(inst.model)[2].data.parameters.volume-.6f)<1e-6,"Later preset sound changed");Undo.PerformUndo();Need(inst.model.parameters.presets.Count==3&&asset.model.patterns[0].tracks[0].ReadLine(2).notes[0].parameterSetId==inst.model.id+"/preset-2","Joint Undo did not restore references");Undo.PerformRedo();Need(inst.model.parameters.presets.Count==2&&asset.model.patterns[0].tracks[0].ReadLine(2).notes[0].parameterSetId==inst.model.id+"/preset-1","Joint Redo failed");});
                Check("shared chain scalar gesture collapses callbacks into one Undo",()=>
                {
                    asset.model.tracks[0].devices.nodes.Add(new AudioEffectNodeData{uid="gesture-gain",type=ZoundEffectType.Gain,p=new[]{1f}});w.SongEdit("prior independent edit",()=>asset.model.tracks[0].name="Keep prior edit");w.mixerTab=1;w.BuildPane();var dial=w.rootVisualElement.Q<ZuiMicroSlider>("chain-param-0-0");Need(dial!=null,"Shared chain dial missing");var flags=BindingFlags.Instance|BindingFlags.NonPublic;var pd=ZoundEffectDescriptors.Get(ZoundEffectType.Gain).parameters[0];typeof(ZuiMicroSlider).GetMethod("OpenGesture",flags).Invoke(dial,null);
                    try{foreach(float gain in new[]{.8f,.4f,.2f})typeof(ZuiMicroSlider).GetMethod("SetValue",flags).Invoke(dial,new object[]{Laubrary.Audio.Editor.AudioChainEditor.DisplayValue(pd,gain),true});}finally{typeof(ZuiMicroSlider).GetMethod("CloseGesture",flags).Invoke(dial,null);}
                    Need(Math.Abs(asset.model.tracks[0].devices.nodes[0].p[0]-.2f)<1e-6,"Gesture did not edit gain");Undo.PerformUndo();Need(asset.model.tracks[0].devices.nodes[0].p[0]==1&&asset.model.tracks[0].name=="Keep prior edit","One Undo did not restore the entire gesture independently");Undo.PerformRedo();Need(Math.Abs(asset.model.tracks[0].devices.nodes[0].p[0]-.2f)<1e-6,"Gesture Redo failed");
                });
                Check("line operations keep unsupported null lane payload and Undo",()=>{Malformed();string raw=asset.model.patterns[0].tracks[0].automation[0].rawSource;w.SongEdit("insert malformed lane fixture",()=>ZTrackerPatternOperations.InsertLine(w.Pattern,0));w.SongEdit("delete malformed lane fixture",()=>ZTrackerPatternOperations.DeleteLine(w.Pattern,0));w.SongEdit("resize malformed lane fixture",()=>ZTrackerPatternOperations.Resize(w.Pattern,4));Need(w.Pattern.lineCount==4&&w.Pattern.tracks[0].automation[0].points==null&&w.Pattern.tracks[0].automation[0].rawSource==raw,"Line operation lost raw nullable lane");Undo.PerformUndo();Need(w.Pattern.lineCount==8&&w.Pattern.tracks[0].automation[0].target==null&&w.Pattern.tracks[0].automation[1]==null,"Line Undo normalized raw nullable lane");});
                foreach(int partition in new[]{64,333,1024})
                {
                    Check("held unison authored detune and repeated restoring refresh / "+partition,()=>
                    {
                        var q=inst.model.parameters;q.unisonVoices=2;q.unisonDetune=0;q.attack=q.decay=0;q.sustain=1;
                        var l=new NativeArray<float>(1024,Allocator.TempJob);var r=new NativeArray<float>(1024,Allocator.TempJob);
                        try{using(var e=new TrackerOffline(TrackerPreparedSong.Prepare(asset.model)))
                        {
                            e.SendCommand(TrackerCommand.Audition(0,69));e.SendCommand(TrackerCommand.SetParameter(0,TrackerParameter.Pan,-.3f));e.Render(l,r,1024,partition);double[] initial={e.Snapshot.voices[0].step,e.Snapshot.voices[1].step};
                            for(int repeat=0;repeat<4;repeat++)foreach(float cents in new[]{1200f,0f}){q.unisonDetune=cents;long frame=e.Snapshot.samplePosition,age=e.Snapshot.voices[0].age;var next=TrackerPreparedSong.Prepare(asset.model);if(!e.RefreshPrepared(next,out var reason)){next.Dispose();throw new Exception(reason);}for(int v=0;v<2;v++)Need(Math.Abs(e.Snapshot.voices[v].step/initial[v]-Math.Pow(2,e.Snapshot.voices[v].memberSpread*cents/1200))<1e-6,"Held detune base stale or drifting");Need(e.Snapshot.samplePosition==frame&&e.Snapshot.voices[0].age==age,"Refresh restarted held voice");Need(Math.Abs(e.Snapshot.parameterDirect[(int)TrackerParameter.Pan]+.3f)<1e-6,"Unrelated game pan lost");e.Render(l,r,1024,partition);Need(e.Compiled&&float.IsFinite(l[1023]),"Renderer not compiled/finite");}
                        }}finally{l.Dispose();r.Dispose();}
                    });
                    Check("held sampler zero gain pan and instrument chain refresh / "+partition,()=>
                    {
                        var clip=AudioClip.Create("P6 review constant PCM",48000,1,48000,false);clip.SetData(Enumerable.Repeat(.1f,48000).ToArray(),0);
                        var temporary=TrackerEngineCheck.FixtureInstrument(clip);inst.model=temporary.model;DestroyImmediate(temporary);inst.model.provenance="";inst.model.sampler.volume=1;inst.model.sampler.pan=0;var sample=inst.model.sampler.samples[0];sample.volume=0;sample.pan=0;sample.fxChain=0;inst.model.fxChains.Add(new AudioEffectChainData());inst.model.fxChains[0].nodes.Add(new AudioEffectNodeData{uid="sample-gain",type=ZoundEffectType.Gain,p=new[]{1f}});
                        var l=new NativeArray<float>(1024,Allocator.TempJob);var r=new NativeArray<float>(1024,Allocator.TempJob);
                        try{using(var e=new TrackerOffline(TrackerPreparedSong.Prepare(asset.model)))
                        {
                            e.SendCommand(TrackerCommand.Audition(0,60));e.Render(l,r,1024,partition);Need(Math.Abs(l[1023])<1e-9,"Zero gain not silent");long age=e.Snapshot.voices[0].age;
                            void Refresh(){var next=TrackerPreparedSong.Prepare(asset.model);if(!e.RefreshPrepared(next,out var reason)){next.Dispose();throw new Exception(reason);}}
                            sample.volume=1;sample.pan=-1;Refresh();Need(e.Snapshot.voices[0].age==age,"Sample scalar restarted voice");e.Render(l,r,1024,partition);float baseline=l[1023];Need(baseline>.01f&&Math.Abs(r[1023])<1e-6,"Held zero→one gain/pan not audible");inst.model.fxChains[0].nodes[0].p[0]=.25f;Refresh();e.Render(l,r,1024,partition);Need(Math.Abs(l[1023]/baseline-.25f)<1e-5,"Instrument chain scalar ignored");sample.volume=0;Refresh();e.Render(l,r,1024,partition);Need(Math.Abs(l[1023])<1e-9,"Held restore to zero not audible");Need(e.Compiled,"Managed render fallback");
                        }}finally{l.Dispose();r.Dispose();DestroyImmediate(clip);}
                    });
                }
            }
            finally{w.StopPreview();w.Close();DestroyImmediate(w);Undo.ClearUndo(asset);Undo.ClearUndo(inst);DestroyImmediate(asset);DestroyImmediate(inst);}
            report.AppendLine($"TOTAL passed={passed} failed={failed}");return report.ToString();
        }
    }
}
