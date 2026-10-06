using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Laubrary.ZTracker.Model;
using Laubrary.ZTracker.Engine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>Exercises the real attached UITK keyboard path and complete Undo on disposable in-memory fixtures.</summary>
        public static string CheckPatternWorkflow()
        {
            var report=new StringBuilder();int pass=0,fail=0;var window=CreateInstance<ZTrackerModelWindow>();var asset=CreateInstance<ZTrackerSong>();asset.schemaVersion=1;
            var inst=CreateInstance<ZTrackerInstrument>();ZTrackerMigration.Upgrade(inst,out _);asset.model=TrackerEngineCheck.FixtureSong(inst);asset.model.tracks[0].columns=new List<ColumnVisibility>{new ColumnVisibility{volume=true,pan=true,delay=true,sampleFx=true}};
            var pattern=asset.model.patterns[0];window.song=asset;window.Show();window.CreateGUI();
            Action<string,Action> check=(name,run)=>{try{run();pass++;report.AppendLine("PASS "+name);}catch(Exception ex){fail++;report.AppendLine("FAIL "+name+": "+ex.Message);}};
            Action<bool,string> need=(condition,message)=>{if(!condition)throw new Exception(message);};
            Action<KeyCode,char> key=(code,ch)=>{window.grid.Focus();using(var e=KeyDownEvent.GetPooled(new UnityEngine.Event{type=EventType.KeyDown,keyCode=code,character=ch}))window.grid.SendEvent(e);};
            try{
                check("attached keyboard piano records independent column",()=>{need(window.grid.panel!=null,"No real UITK panel");window.Select(0,0,false);key(KeyCode.Z,'z');need(pattern.tracks[0].ReadLine(0).notes[0].pitch==60,"Key dispatch never reached note entry");need(window.row==1,"Edit step ignored");window.AuditionOff();});
                check("hex volume pan delay and effect identifiers",()=>{window.Select(0,2,false);key(KeyCode.Alpha4,'4');key(KeyCode.Alpha0,'0');window.Select(0,3,false);key(KeyCode.Alpha2,'2');key(KeyCode.Alpha0,'0');window.Select(0,4,false);key(KeyCode.Alpha8,'8');key(KeyCode.Alpha0,'0');window.Select(0,7,false);key(KeyCode.Alpha0,'0');key(KeyCode.U,'u');window.Select(0,8,false);key(KeyCode.Alpha0,'0');key(KeyCode.C,'c');var line=pattern.tracks[0].ReadLine(0);need(line.notes[0].volume.value==64&&line.notes[0].pan.value==32&&line.notes[0].delay==128,"Hex cell value lost");need(line.effects[0].command.identifier=="0U"&&line.effects[0].command.value==12,"Effect identifier or argument not entered");});
                check("block copy paste transpose",()=>{window.Select(0,0,false);window.Select(0,8,true);window.CopyBlock();window.Select(2,0,false);window.PasteBlock();window.Select(2,0,false);window.Transpose(12);need(pattern.tracks[0].ReadLine(2).notes[0].pitch==72,"Block notes not copied/transposed");need(pattern.tracks[0].ReadLine(2).effects[0].command.identifier=="0U","Real effect columns not copied");});
                check("OFF and clear preserve numeric zero distinct from empty",()=>{window.Select(3,0,false);key(KeyCode.Backspace,'\0');need(pattern.tracks[0].ReadLine(3).notes[0].note==NoteKind.Off,"OFF missing");window.Select(3,2,false);key(KeyCode.Alpha0,'0');key(KeyCode.Alpha0,'0');need(pattern.tracks[0].ReadLine(3).notes[0].volume.kind==ValueKind.Value,"Zero became empty");window.Select(3,2,false);key(KeyCode.Delete,'\0');need(pattern.tracks[0].ReadLine(3).notes[0].volume.kind==ValueKind.Empty,"Delete did not clear numeric presence");});
                check("resize complete Undo restores cropped notes and automation",()=>{var lane=new AutomationLane{id="undo-lane",points=new List<AutomationPoint>{new AutomationPoint{line=7,value=.5f}}};pattern.tracks[0].automation.Add(lane);pattern.tracks[0].WriteLine(new PatternLine{line=7,notes=new List<NoteCell>{TrackerEngineCheck.Note(70)}});window.SongEdit("resize check",()=>ZTrackerPatternOperations.Resize(window.Pattern,4),true);need(asset.model.patterns[0].lineCount==4,"Resize failed");Undo.PerformUndo();need(asset.model.patterns[0].lineCount==8&&asset.model.patterns[0].tracks[0].ReadLine(7).notes.Count==1&&asset.model.patterns[0].tracks[0].automation[0].points.Count==1,"Complete Undo did not restore removed payload");Undo.PerformRedo();need(asset.model.patterns[0].lineCount==4,"Redo failed");});
                check("UI rebuild is a read of column visibility",()=>{window.StopPreview();asset.model.tracks[0].columns.Clear();string before=JsonUtility.ToJson(asset.model);window.BuildPane();need(before==JsonUtility.ToJson(asset.model),"Building controls mutated authored model");});
                check("maximum-sized pattern recycles only viewport labels",()=>{asset.model.tracks.Clear();for(int t=0;t<128;t++){var track=new TrackData{id="virtual-"+t,name="Track "+t,visibleNoteColumns=12,visibleEffectColumns=8};for(int c=0;c<12;c++)track.columns.Add(new ColumnVisibility{volume=true,pan=true,delay=true,sampleFx=true});asset.model.tracks.Add(track);}asset.model.patterns[0].lineCount=512;window.BuildPane();need(window.fields.Count>10000,"Stress fixture did not cover maximum field capacity");need(window.labels.Count<3000,"Off-screen rows/columns were materialized: "+window.labels.Count);window.Select(511,window.fields.Count-1,false);need(window.row==511&&window.sub==window.fields.Count-1,"Virtual cursor cannot reach far end");});
            }
            finally{window.StopPreview();window.Close();DestroyImmediate(window);Undo.ClearUndo(asset);DestroyImmediate(asset);DestroyImmediate(inst);}
            report.AppendLine($"TOTAL passed={pass} failed={fail}");return report.ToString();
        }
    }
}
