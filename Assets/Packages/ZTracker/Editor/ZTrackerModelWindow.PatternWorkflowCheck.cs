using System;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>Disposable direct checks for the pattern entry points; the PM performs the attached visual walk.</summary>
        public static string CheckPatternOverhaul()
        {
            var report=new StringBuilder();int passed=0,failed=0;
            var window=CreateInstance<ZTrackerModelWindow>();var song=CreateInstance<ZTrackerSong>();var instrument=CreateInstance<ZTrackerInstrument>();
            void Need(bool okay,string why){if(!okay)throw new InvalidOperationException(why);}
            void Check(string title,Action test){try{test();passed++;report.AppendLine("PASS "+title);}catch(Exception e){failed++;report.AppendLine("FAIL "+title+": "+e.Message);}}
            bool Shortcut(KeyCode code,VisualElement target=null)
            {
                using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=code}))
                {e.target=target??window.rootVisualElement;return window.HandleTrackerShortcut(e);}
            }
            try
            {
                instrument.schemaVersion=1;instrument.model=NewInstrumentData();song.schemaVersion=1;song.model=TrackerEngineCheck.FixtureSong(instrument);
                window.song=song;window.instrument=instrument;window.pane=0;window.Show();window.CreateGUI();
                Check("pattern counts and selected track settings exist",()=>{Need(window.rootVisualElement.Q("pattern-length")!=null,"Missing pattern length");Need(window.rootVisualElement.Q("pattern-track-name")!=null,"Missing track name");Need(window.rootVisualElement.Q("pattern-note-columns")!=null,"Missing note count");Need(window.rootVisualElement.Q("pattern-fx-columns")!=null,"Missing FX count");});
                Check("Tab advances by Step; Escape toggles Edit; text input keeps shortcuts",()=>{
                    window.row=2;window.step=3;Need(Shortcut(KeyCode.Tab),"Tab unhandled");Need(window.row==5,"Tab did not use Step");bool before=window.editMode;Need(Shortcut(KeyCode.Escape)&&window.editMode!=before,"Escape did not toggle Edit");
                    var text=window.rootVisualElement.Q("pattern-track-name")?.Q<TextField>();Need(text!=null,"Track name input missing");text.Focus();Need(!Shortcut(KeyCode.Escape,text)&&!Shortcut(KeyCode.Space,text)&&!Shortcut(KeyCode.KeypadPlus,text),"Text entry lost normal keys");Need(window.editMode!=before,"Typing changed Edit mode");window.rootVisualElement.Focus();
                });
                Check("octave shortcuts work in every tab",()=>{
                    for(int pane=0;pane<=4;pane++){window.pane=pane;window.BuildPane();window.octave=5;Need(Shortcut(KeyCode.KeypadPlus),"Plus unhandled in tab "+pane);Need(window.octave==6,"Plus missed tab "+pane);Need(Shortcut(KeyCode.KeypadMinus),"Minus unhandled in tab "+pane);Need(window.octave==5,"Minus missed tab "+pane);bool edit=window.editMode;Need(Shortcut(KeyCode.Escape)&&window.editMode!=edit,"Escape missed tab "+pane);}
                    window.pane=0;window.BuildPane();
                });
                Check("drum kit octave shortcuts use drum octave on repeated presses",()=>{
                    instrument.model.family=InstrumentFamily.DrumKit;window.pane=3;window.drumFirstOctave=3;window.octave=5;window.BuildPane();
                    Need(Shortcut(KeyCode.KeypadPlus)&&Shortcut(KeyCode.KeypadPlus),"Repeated kit plus unhandled");Need(window.drumFirstOctave==5&&window.octave==5,"Kit plus read the pitched octave");
                    Need(Shortcut(KeyCode.KeypadMinus)&&window.drumFirstOctave==4,"Kit minus missed drum octave");instrument.model.family=InstrumentFamily.Sampler;window.pane=0;window.BuildPane();
                });
                Check("pattern length cropping and Undo restore rows and automation",()=>{
                    var p=window.Pattern;int original=p.lineCount;var pt=p.tracks[0];pt.WriteLine(new PatternLine{line=original-1,events={new EventCell{column=0,present=true,payload="tail"}}});
                    pt.automation.Add(new AutomationLane{id="length-lane",target=new ParameterTarget{kind=ParameterKind.Mixer,trackId=song.model.tracks[0].id,parameter="postVolume"},points={new AutomationPoint{line=original-1,value=.5f}}});
                    window.SongEdit("pattern length",()=>ZTrackerPatternOperations.Resize(window.Pattern,Math.Max(1,original-2)),true);
                    Need(window.Pattern.lineCount==original-2&&!window.Pattern.tracks[0].lines.Any(l=>l.line==original-1),"Resize did not crop rows");Undo.PerformUndo();
                    Need(window.Pattern.lineCount==original&&window.Pattern.tracks[0].lines.Any(l=>l.line==original-1)&&window.Pattern.tracks[0].automation.Any(l=>l.id=="length-lane"&&l.points.Any(point=>point.line==original-1)),"Undo did not restore rows and automation");
                });
                Check("track move keeps IDs, routing, automation, selection and Undo/Redo",()=>{
                    window.AddTrack(TrackKind.Send);window.track=0;var selected=window.SelectedTrack;var before=song.model.tracks.Select(t=>t.id).ToArray();var send=song.model.tracks.Find(t=>t.kind==TrackKind.Send);
                    selected.sends.Add(new SendDestination{trackId=send.id});var record=window.Pattern.tracks.Find(t=>t.trackId==selected.id);record.automation.Add(new AutomationLane{id="move-lane",target=new ParameterTarget{kind=ParameterKind.Mixer,trackId=selected.id,parameter="postVolume"}});
                    window.ReorderTracks(0,1);Need(window.SelectedTrack.id==selected.id&&song.model.tracks[1].id==selected.id,"Selection changed owner");Need(record.automation.Last().target.trackId==selected.id&&selected.sends[0].trackId==send.id,"References followed an index");
                    Undo.PerformUndo();Need(song.model.tracks.Select(t=>t.id).SequenceEqual(before),"Undo did not restore order");Undo.PerformRedo();Need(song.model.tracks[1].id==selected.id,"Redo did not restore order");
                });
                Check("retained cell click selects after reorder and first FX character opens suggestions",()=>{
                    foreach(var t in song.model.tracks)t.visibleEffectColumns=Math.Max(1,t.visibleEffectColumns);
                    window.pane=0;window.row=0;window.BuildPane();LayoutChain(window);
                    var cell=window.gridCanvas.Q<Label>("cell-0-3");Need(cell!=null&&cell.worldBound.width>0,"Nonfirst cell was not laid out");
                    using(var click=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=cell.worldBound.center})){click.target=cell;cell.SendEvent(click);}
                    Need(window.sub==3,"Retained cell pointer did not select column 3");
                    int fx=window.fields.FindIndex(f=>f.kind==FieldKind.EffectId||f.kind==FieldKind.LocalId);Need(fx>0,"No FX identifier field");
                    window.grid.scrollOffset=new Vector2(Mathf.Max(0,window.fieldX[fx]-30),0);window.BuildVisibleCells();LayoutChain(window);
                    cell=window.gridCanvas.Q<Label>("cell-0-"+fx);Need(cell!=null&&cell.worldBound.width>0,"FX cell was not laid out");
                    using(var click=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=cell.worldBound.center})){click.target=cell;cell.SendEvent(click);}
                    Need(window.sub==fx,"Retained FX cell pointer did not select it");
                    window.grid.Focus();using(var key=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.Alpha0,character='0'})){key.target=window.grid;window.grid.SendEvent(key);}
                    Need(window.pendingId=="0"&&window.fxSuggestions?.IsOpen==true,"First typed FX character did not open suggestions");window.CloseFxSuggestions();
                });
                Check("FX catalog matches the compiler's supported locations",()=>{
                    string Ids(FieldKind kind)=>string.Join(",",FxCommandCatalog.For(kind).Select(e=>e.id).OrderBy(id=>id));
                    string Canonical(string ids)=>string.Join(",",ids.Split(' ').OrderBy(id=>id));
                    Need(Ids(FieldKind.LocalId)==Canonical("0A 0B 0C 0D 0E 0G 0I 0N 0O 0S 0T 0U 0V 0Y"),"Local catalog drifted from supported commands");
                    Need(Ids(FieldKind.EffectId)==Canonical("0A 0B 0C 0D 0E 0G 0I 0J 0L 0M 0N 0O 0P 0Q 0R 0S 0T 0U 0V 0X 0Y ZB ZD ZG ZK ZL ZT"),"Track catalog drifted from supported commands");
                    Need(Ids(FieldKind.Volume)==Canonical("B C D G I O Q R U Y"),"Volume shorthand catalog drifted");
                    Need(Ids(FieldKind.Pan)==Canonical("B C D G J K Q R U Y"),"Pan shorthand catalog drifted");
                    Need(FxCommandCatalog.Find("ZT",FieldKind.LocalId)==null,"Global command offered locally");Need(FxCommandCatalog.Find("R0",FieldKind.Volume)==null,"Invalid zero retrigger presented as supported");
                });
            }
            finally{window.StopPreview();window.Close();DestroyImmediate(window);Undo.ClearUndo(song);DestroyImmediate(song);DestroyImmediate(instrument);}
            report.AppendLine($"TOTAL passed={passed} failed={failed}");return report.ToString();
        }
    }
}
