using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Laubrary.Audio;
using Laubrary.Audio.Editor;
using Laubrary.Zounds;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>Disposable authoring fixtures exercise actual controls, identity remapping, references and Undo. No Test Runner or user assets.</summary>
        public static string CheckMixerWorkflow()
        {
            var report=new StringBuilder();int passed=0,failed=0;
            var window=CreateInstance<ZTrackerModelWindow>();var asset=CreateInstance<ZTrackerSong>();asset.schemaVersion=1;var inst=CreateInstance<ZTrackerInstrument>();ZTrackerMigration.Upgrade(inst,out _);
            asset.model=TrackerEngineCheck.FixtureSong(inst);window.song=asset;window.pane=1;window.Show();window.CreateGUI();
            void Need(bool condition,string message){if(!condition)throw new Exception(message);}
            void Check(string label,Action run){try{run();passed++;report.AppendLine("PASS "+label);}catch(Exception e){failed++;report.AppendLine("FAIL "+label+": "+e.Message);}}
            void Click(string name){var button=window.rootVisualElement.Q<Button>(name);Need(button!=null,"Missing action "+name);var invoke=typeof(Clickable).GetMethod("SimulateSingleClick",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);Need(invoke!=null,"Clickable simulator unavailable");invoke.Invoke(button.clickable,new object[]{null,0});}
            try
            {
                Check("all five track roles reachable with pattern records and complete Undo",()=>{window.newTrackKind=(int)TrackKind.Group;Click("add-track");window.newTrackKind=(int)TrackKind.Send;Click("add-track");window.newTrackKind=(int)TrackKind.Event;Click("add-track");Need(asset.model.tracks.Select(t=>t.kind).Distinct().Count()==5,"Missing a track role");Need(asset.model.patterns.All(p=>p.tracks.Count==asset.model.tracks.Count),"Pattern records not updated");int count=asset.model.tracks.Count;Undo.PerformUndo();Need(asset.model.tracks.Count==count-1&&asset.model.patterns[0].tracks.Count==count-1,"Undo did not restore track topology");Undo.PerformRedo();Need(asset.model.tracks.Count==count,"Redo did not restore track topology");window.newTrackKind=(int)TrackKind.Master;Click("add-track");Need(asset.model.tracks.Count==count,"Duplicate Master accepted");});
                Check("routing pickers exclude group ancestry and send feedback cycles",()=>{var song=asset.model;var group=song.tracks.Find(t=>t.kind==TrackKind.Group);var send=song.tracks.Find(t=>t.kind==TrackKind.Send);var seq=song.tracks.Find(t=>t.kind==TrackKind.Sequencer);seq.parentGroupId=group.id;Need(!window.RouteAllowed(group,seq.id,true),"Non-group or ancestry cycle allowed");send.outputTrackId=group.id;Need(!window.SendAllowed(group,send.id),"Send feedback cycle allowed");send.outputTrackId="";Need(window.SendAllowed(seq,send.id),"Valid send excluded");});
                Check("clone remaps device declarations and sparse automation without aliasing",()=>{window.track=0;var t=window.SelectedTrack;t.devices.nodes.Add(new AudioEffectNodeData{type=ZoundEffectType.Gain,uid="clone-gain",p=new[]{.7f}});window.DeclareNativeSource(t,t.devices.nodes[0]);var pt=window.Pattern.tracks.Find(p=>p.trackId==t.id);pt.automation.Add(new AutomationLane{id="clone-lane",target=new ParameterTarget{kind=ParameterKind.Device,trackId=t.id,deviceId="clone-gain",index=0,parameter="1",units="linear"},points=new List<AutomationPoint>{new AutomationPoint{line=.375,value=.25f}}});window.CloneTrack();var copy=window.SelectedTrack;Need(copy.id!=t.id&&copy.devices.nodes[0].uid!=t.devices.nodes[0].uid,"Clone identities aliased");Need(copy.sourceDevices[0].id==copy.devices.nodes[0].uid&&copy.sourceDevices[0].parameters[0].target.deviceId==copy.devices.nodes[0].uid,"Source identity not remapped");var lane=window.Pattern.tracks.Find(p=>p.trackId==copy.id).automation[0];Need(lane.target.trackId==copy.id&&lane.target.deviceId==copy.devices.nodes[0].uid,"Lane retargeted to original");copy.devices.nodes[0].p[0]=.4f;Need(t.devices.nodes[0].p[0]==.7f,"Clone data aliases original");Need(ZTrackerModelValidation.Validate(asset.model)==null,ZTrackerModelValidation.Validate(asset.model));});
                Check("device reorder carries send taps and stable command ordinals",()=>{var t=window.SelectedTrack;var send=asset.model.tracks.Find(x=>x.kind==TrackKind.Send);t.devices.nodes.Add(new AudioEffectNodeData{type=ZoundEffectType.Gain,uid="tap-two",p=new[]{1f}});t.sends.Add(new SendDestination{trackId=send.id,devicePosition=1});int ordinal=t.sourceDevices[0].ordinal;var before=t.devices.nodes.ToArray();Move(t.devices.nodes,0,1);window.RepairChainReferences(t,before);Need(t.sends[0].devicePosition==2,"Send silently moved to another node");Need(t.sourceDevices[0].ordinal==ordinal&&t.sourceDevices[0].parameters[0].target.deviceId==before[0].uid,"Literal command declaration retargeted");});
                Check("remove track flags cross-track lanes and restores via Undo",()=>{var target=window.SelectedTrack;var other=asset.model.tracks[0];var pt=window.Pattern.tracks.Find(p=>p.trackId==other.id);pt.automation.Add(new AutomationLane{id="cross",target=new ParameterTarget{kind=ParameterKind.Mixer,trackId=target.id,parameter="postVolume"}});asset.model.sequence[0].mutedTrackIds.Add(target.id);window.RemoveTrack();Need(!asset.model.tracks.Contains(target)&&pt.automation.Last().unsupported,"Deletion did not flag cross-track reference");Need(!asset.model.sequence[0].mutedTrackIds.Contains(target.id),"Sequence mute left dangling");Undo.PerformUndo();Need(asset.model.tracks.Any(t=>t.id==target.id)&&asset.model.patterns[0].tracks.Find(p=>p.trackId==other.id).automation.Last().unsupported==false,"Undo failed to restore references");});
                Check("shared chain controls add effect modifier binding and Undo",()=>{window.track=0;window.mixerTab=1;window.BuildPane();int nodes=window.SelectedTrack.devices.nodes.Count;Click("chain-add-effect");Need(window.SelectedTrack.devices.nodes.Count==nodes+1,"Shared effect action did not author data");Click("chain-add-modifier");Need(window.SelectedTrack.devices.modifiers.Count==1,"Modifier missing");Click("chain-bind-0");Need(window.SelectedTrack.devices.bindings.Count==1&&window.SelectedTrack.devices.bindings[0].schema==2,"Binding missing/current combine schema not used");Undo.PerformUndo();Need(window.SelectedTrack.devices.bindings.Count==0,"Undo failed to remove binding");Undo.PerformRedo();Need(window.SelectedTrack.devices.bindings.Count==1,"Redo failed to restore binding");});
                Check("automation creates precise points through named controls and Undo",()=>{window.pane=2;window.automationTarget=0;window.BuildPane();Click("add-automation-lane");Click("add-automation-point");var lane=window.Pattern.tracks.Find(p=>p.trackId==window.SelectedTrack.id).automation.Last();var field=window.rootVisualElement.Q<FloatField>("automation-point-line");Need(field!=null,"Precise point input missing");field.value=.375f;Need(lane.points[0].line==.375,"Fractional position rounded to an integer");Click("delete-automation-point");Need(lane.points.Count==0,"Point deletion failed");Undo.PerformUndo();Need(window.Pattern.tracks.Find(p=>p.trackId==window.SelectedTrack.id).automation.Last().points[0].line==.375,"Point Undo lost fractional time");});
                Check("every mixer automation view is a read and preserves unsupported payload",()=>{window.pane=2;var pt=window.Pattern.tracks[0];pt.automation.Add(new AutomationLane{id="opaque",unsupported=true,diagnostic="preserve",rawSource="raw unsupported",target=new ParameterTarget{unresolved=true,parameter="foreign"}});string before=JsonUtility.ToJson(asset.model);for(int pane=1;pane<=2;pane++){window.pane=pane;for(int tab=0;tab<3;tab++){window.mixerTab=tab;window.BuildPane();}}Need(JsonUtility.ToJson(asset.model)==before,"UI build mutated authored data");});
            }
            finally{window.StopPreview();window.Close();DestroyImmediate(window);Undo.ClearUndo(asset);DestroyImmediate(asset);DestroyImmediate(inst);}
            report.AppendLine($"TOTAL passed={passed} failed={failed}");return report.ToString();
        }
    }
}
