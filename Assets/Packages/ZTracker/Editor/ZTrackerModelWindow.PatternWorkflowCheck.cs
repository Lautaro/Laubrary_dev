using System;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEditor;
using Laubrary.Zui;
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
                {e.target=target??window.rootVisualElement;bool handled=window.HandleTrackerShortcut(e);
                    using(var up=KeyUpEvent.GetPooled(new Event{type=EventType.KeyUp,keyCode=code}))window.TrackerShortcutKeyUp(up);
                    return handled;}
            }
            try
            {
                instrument.schemaVersion=1;instrument.model=NewInstrumentData();song.schemaVersion=1;song.model=TrackerEngineCheck.FixtureSong(instrument);
                window.song=song;window.instrument=instrument;window.pane=0;window.Show();window.position=new Rect(60,60,1300,820);window.CreateGUI();
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
                Check("dispatched octave keys count physical presses once across character, repeat, focus and kit",()=>{
                    void Send(KeyCode code,char character='\0',bool shift=false,bool up=false,VisualElement target=null){var key=new Event{type=up?EventType.KeyUp:EventType.KeyDown,keyCode=code,character=character,modifiers=shift?EventModifiers.Shift:EventModifiers.None};if(up){using(var e=KeyUpEvent.GetPooled(key))(target??window.rootVisualElement).SendEvent(e);}else{using(var e=KeyDownEvent.GetPooled(key))(target??window.rootVisualElement).SendEvent(e);}}
                    window.pane=0;window.octave=5;window.BuildPane();
                    Send(KeyCode.Equals,'+',true,target:window.grid);Send(KeyCode.None,'+',true,target:window.grid);Send(KeyCode.Equals,'+',true,target:window.grid);
                    Need(window.octave==6,"One shifted plus dispatched twice or key-repeat stepped the octave");Send(KeyCode.Equals,up:true);
                    Send(KeyCode.Equals,'+',true);Need(window.octave==7,"Second physical plus press was swallowed");Send(KeyCode.Equals,up:true);
                    Send(KeyCode.Minus,'-');Send(KeyCode.None,'-');Send(KeyCode.Minus,'-');Need(window.octave==6,"One minus dispatched twice or key-repeat stepped the octave");Send(KeyCode.Minus,up:true);
                    var text=window.rootVisualElement.Q("pattern-track-name")?.Q<TextField>();Need(text!=null,"Text fixture missing");text.Focus();Send(KeyCode.Equals,'+',true,target:text);Need(window.octave==6,"Typing a plus changed octave");Send(KeyCode.Equals,up:true,target:text);window.rootVisualElement.Focus();
                    instrument.model.family=InstrumentFamily.DrumKit;window.pane=3;window.drumFirstOctave=3;window.BuildPane();Send(KeyCode.KeypadPlus);Send(KeyCode.None,'+');Need(window.drumFirstOctave==4&&window.octave==6,"Kit octave consumed a second event or changed pitched octave");Send(KeyCode.KeypadPlus,up:true);Send(KeyCode.KeypadMinus);Need(window.drumFirstOctave==3,"Kit minus missed the drum octave");Send(KeyCode.KeypadMinus,up:true);
                    instrument.model.family=InstrumentFamily.Sampler;window.pane=3;window.BuildPane();Send(KeyCode.KeypadPlus);Need(window.octave==7,"Instrument octave diverged from pattern octave");Send(KeyCode.KeypadPlus,up:true);window.pane=0;window.BuildPane();
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
                Check("pattern header drag slides its column live, drops once and Undoes in one step",()=>{
                    window.pane=0;window.track=0;window.row=0;window.BuildPane();LayoutChain(window);var before=song.model.tracks.Select(t=>t.id).ToArray();
                    var h0=window.gridCanvas.Q<Label>("pattern-track-header-0");var h1=window.gridCanvas.Q<Label>("pattern-track-header-1");Need(h0!=null&&h1!=null&&h0.worldBound.width>0,"Pattern headers not laid out");
                    int cellField=window.fields.FindIndex(f=>f.track==0);var cell=window.gridCanvas.Q<Label>("cell-0-"+cellField);Need(cell!=null,"Track 0 cell missing");
                    float pitch=h1.worldBound.xMin-h0.worldBound.xMin;var dropAt=new Vector2(h1.worldBound.xMax-2,h0.worldBound.center.y);float travel=dropAt.x-h0.worldBound.center.x;
                    PointerDrag(h0,dropAt,8,midDrag:()=>{Need(Mathf.Abs(TranslateX(h0)-travel)<.5f,"Header does not follow the pointer: "+TranslateX(h0)+" vs "+travel);Need(Mathf.Abs(TranslateX(cell)-TranslateX(h0))<.01f,"Column cells did not travel with their header");Need(TranslateX(h1)<-1f&&Mathf.Abs(TranslateX(h1)+(h0.worldBound.width+(pitch-h0.worldBound.width)))<1.5f,"Next header did not slide over to make room: "+TranslateX(h1));Need(Mathf.Abs(TranslateY(h0))<.01f,"Horizontal drag moved vertically");Need(song.model.tracks.Select(t=>t.id).SequenceEqual(before),"Data changed before the drop");},finish:false);
                    Need(song.model.tracks.Select(t=>t.id).SequenceEqual(before),"Data changed before the header settled");ZuiReorder.FinishPending();
                    Need(song.model.tracks[1].id==before[0]&&song.model.tracks[0].id==before[1]&&window.SelectedTrack.id==before[0],"Drop did not swap the tracks or lost selection");
                    Undo.PerformUndo();Need(song.model.tracks.Select(t=>t.id).SequenceEqual(before),"One Undo did not restore the order");window.BuildPane();
                });
                Check("block selection paints every row, beat rows included, for keys, shift-click and mouse drag on every column type",()=>{
                    var t0=song.model.tracks.First(t=>t.kind==TrackKind.Sequencer);t0.columns=new System.Collections.Generic.List<ColumnVisibility>{new ColumnVisibility{volume=true,pan=true,delay=true,sampleFx=true}};t0.visibleEffectColumns=1;if(t0.envelopeColumns.Count==0)t0.envelopeColumns.Add(new EnvelopeColumnData{uid="selection-envelope",command="0P"});
                    if(!song.model.tracks.Any(t=>t.kind==TrackKind.Event))window.AddTrack(TrackKind.Event);
                    window.pane=0;window.BuildPane();LayoutChain(window);
                    var kinds=window.fields.Select(f=>f.kind).Distinct().ToList();Need(kinds.Count==Enum.GetValues(typeof(FieldKind)).Length,"Fixture lacks a column type: "+string.Join(",",kinds));
                    int lastVisible=window.labels.Keys.Where(k=>k.x==0).Max(k=>k.y);int bottom=Math.Max(5,song.model.linesPerBeat+1);
                    void Painted(string how){LayoutChain(window);Color selected=default;bool found=false;
                        foreach(var pair in window.labels){var p=pair.Key;bool inside=p.x<=bottom&&p.y<=lastVisible;bool cursor=p.x==window.row&&p.y==window.sub;var l=pair.Value;
                            if(!inside){Need(!l.ClassListContains("tracker-selected"),how+": cell outside the block is selected "+p);continue;}
                            Need(l.ClassListContains("tracker-selected"),how+": row "+p.x+" field "+p.y+" ("+window.fields[p.y].kind+") is not selected");
                            if(cursor||p.x%Math.Max(1,song.model.linesPerBeat)==0)continue;if(!found){selected=l.resolvedStyle.backgroundColor;found=true;}}
                        Need(found&&selected.a>.2f,how+": no selection colour resolved");
                        foreach(var pair in window.labels){var p=pair.Key;if(p.x>bottom||p.y>lastVisible||(p.x==window.row&&p.y==window.sub))continue;
                            Need(pair.Value.resolvedStyle.backgroundColor==selected,how+": row "+p.x+" field "+p.y+" ("+window.fields[p.y].kind+") draws "+pair.Value.resolvedStyle.backgroundColor+" instead of the selection colour");}}
                    // Keyboard: Shift+arrows from the first cell.
                    window.Select(0,0,false);
                    for(int i=0;i<bottom;i++)using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.DownArrow,modifiers=EventModifiers.Shift})){e.target=window.grid;window.grid.SendEvent(e);}
                    for(int i=0;i<lastVisible;i++)using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.RightArrow,modifiers=EventModifiers.Shift})){e.target=window.grid;window.grid.SendEvent(e);}
                    Need(window.anchorRow==0&&window.row==bottom&&window.sub==lastVisible,"Shift+arrows did not extend the block");Painted("keyboard");
                    // Shift-click: click the first cell, shift-click the last.
                    window.Select(3,3,false);LayoutChain(window);
                    var first=window.gridCanvas.Q<Label>("cell-0-0");using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=first.worldBound.center})){e.target=first;first.SendEvent(e);}
                    using(var e=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,mousePosition=first.worldBound.center}))window.rootVisualElement.panel.visualTree.SendEvent(e);
                    var last=window.gridCanvas.Q<Label>("cell-"+bottom+"-"+lastVisible);using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,modifiers=EventModifiers.Shift,mousePosition=last.worldBound.center})){e.target=last;last.SendEvent(e);}
                    using(var e=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,mousePosition=last.worldBound.center}))window.rootVisualElement.panel.visualTree.SendEvent(e);
                    Need(window.anchorRow==0&&window.anchorSub==0&&window.row==bottom&&window.sub==lastVisible,"Shift-click did not extend the block");Painted("shift-click");
                    // Mouse drag from the first cell to the last.
                    window.Select(3,3,false);LayoutChain(window);first=window.gridCanvas.Q<Label>("cell-0-0");last=window.gridCanvas.Q<Label>("cell-"+bottom+"-"+lastVisible);var end=last.worldBound.center;
                    PointerDrag(first,end,10,finish:false);
                    Need(window.anchorRow==0&&window.anchorSub==0&&window.row==bottom&&window.sub==lastVisible,"Mouse drag did not select the block: anchor "+window.anchorRow+","+window.anchorSub+" cursor "+window.row+","+window.sub);Painted("mouse drag");
                    window.Select(0,0,false);t0.envelopeColumns.Clear();window.BuildPane();
                });
                Check("Paste Insert pushes rows down in every column type, drops the overflow with a notice, carries commands and automation, and Undoes in one step",()=>{
                    var a=song.model.tracks.First(t=>t.kind==TrackKind.Sequencer);a.columns=new System.Collections.Generic.List<ColumnVisibility>{new ColumnVisibility{volume=true,pan=true,delay=true,sampleFx=true}};a.visibleEffectColumns=1;a.visibleNoteColumns=1;
                    if(!song.model.tracks.Any(t=>t.kind==TrackKind.Event))window.AddTrack(TrackKind.Event);var ev=song.model.tracks.First(t=>t.kind==TrackKind.Event);
                    window.pane=0;window.BuildPane();var p=window.Pattern;int L=p.lineCount;Need(L>=8,"Pattern too short");
                    var pa=p.tracks.Find(t=>t.trackId==a.id);var pe=p.tracks.Find(t=>t.trackId==ev.id);pa.lines.Clear();pa.automation.Clear();pe.lines.Clear();
                    for(int r=0;r<L;r++){pa.WriteLine(new PatternLine{line=r,notes={new NoteCell{column=0,note=NoteKind.Note,pitch=40+r,instrumentPresent=true,instrument=r%2,volume=new ColumnValue{kind=ValueKind.Value,value=r},pan=new ColumnValue{kind=ValueKind.Value,value=r+1},delayPresent=true,delay=r+2,sampleFx=new CommandData{present=true,identifier="0S",valuePresent=true,value=r}}},effects={new EffectCell{column=0,command=new CommandData{present=true,identifier="0C",valuePresent=true,value=r}}}});
                        pe.WriteLine(new PatternLine{line=r,events={new EventCell{column=0,present=true,payload="e"+r}}});}
                    pa.automation.Add(new AutomationLane{id="insert-lane",target=new ParameterTarget{kind=ParameterKind.Mixer,trackId=a.id,parameter="postVolume"},points={new AutomationPoint{line=2,value=.2f},new AutomationPoint{line=3,value=.3f},new AutomationPoint{line=L-1,value=.9f}}});
                    string before=JsonUtility.ToJson(p);
                    int a0=window.fields.FindIndex(f=>f.track==song.model.tracks.IndexOf(a)),a1=window.fields.FindLastIndex(f=>f.track==song.model.tracks.IndexOf(a));
                    Need(Enumerable.Range(a0,a1-a0+1).Select(i=>window.fields[i].kind).Distinct().Count()==9,"Sequencer fixture lacks a note/effect column type");
                    window.Select(0,a0,false);window.Select(1,a1,true);window.CopyBlock();window.Select(3,a0,false);
                    using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.V,modifiers=EventModifiers.Control|EventModifiers.Shift})){e.target=window.grid;window.grid.SendEvent(e);}
                    NoteCell N(int r)=>pa.ReadLine(r).notes.Find(n=>n.column==0);EffectCell X(int r)=>pa.ReadLine(r).effects.Find(n=>n.column==0);
                    for(int r=0;r<3;r++)Need(N(r).pitch==40+r,"Row above the cursor moved: "+r);
                    for(int r=3;r<5;r++){var n=N(r);Need(n.pitch==40+r-3&&n.instrument==(r-3)%2&&n.volume.value==r-3&&n.pan.value==r-2&&n.delay==r-1&&n.sampleFx.value==r-3&&X(r).command.identifier=="0C"&&X(r).command.value==r-3,"Pasted row "+r+" wrong");}
                    for(int r=5;r<L;r++){var n=N(r);Need(n.pitch==40+r-2&&n.instrument==(r-2)%2&&n.volume.value==r-2&&n.pan.value==r-1&&n.delay==r&&n.sampleFx.identifier=="0S"&&n.sampleFx.value==r-2&&X(r).command.value==r-2,"Pushed row "+r+" did not carry every column of row "+(r-2)+": "+JsonUtility.ToJson(n)+" fx "+JsonUtility.ToJson(X(r)));}
                    Need(!pa.lines.Any(l=>l.line>=L),"Rows kept past the pattern end");
                    var pts=pa.automation[0].points;Need(pts.Any(q=>q.line==2)&&pts.Any(q=>q.line==5&&q.value==.3f)&&pts.Count==2,"Automation did not move with the pasted track: "+string.Join(",",pts.Select(q=>q.line)));
                    Need(pe.ReadLine(3).events[0].payload=="e3","Unpasted track moved");
                    Need(window.help.text.Contains("2 rows")&&window.help.text.Contains("1 automation point"),"No visible notice for dropped rows: "+window.help.text);
                    Undo.PerformUndo();Need(JsonUtility.ToJson(window.Pattern)==before,"One Undo did not restore the pattern");
                    // Event column, and pasting into one field of a wider track moves only that column.
                    window.BuildPane();int evField=window.fields.FindIndex(f=>f.kind==FieldKind.Event);window.Select(0,evField,false);window.CopyBlock();window.Select(2,evField,false);window.PasteInsertBlock();
                    pe=window.Pattern.tracks.Find(t=>t.trackId==ev.id);Need(pe.ReadLine(2).events[0].payload=="e0"&&pe.ReadLine(3).events[0].payload=="e2"&&pe.ReadLine(L-1).events[0].payload=="e"+(L-2),"Event column was not pushed");Need(window.help.text.Contains("1 row"),"Event overflow notice missing");Undo.PerformUndo();
                    window.Select(0,a0+1,false);window.CopyBlock();window.Select(4,a0+1,false);window.PasteInsertBlock();pa=window.Pattern.tracks.Find(t=>t.trackId==a.id);
                    Need(pa.ReadLine(5).notes[0].instrument==0&&pa.ReadLine(5).notes[0].pitch==45&&pa.automation[0].points.Any(q=>q.line==3),"Single-column insert moved other columns or automation");
                    Undo.PerformUndo();Need(JsonUtility.ToJson(window.Pattern)==before,"Undo after single-column insert failed");
                    window.Select(0,0,false);Need(window.help.text=="",""+window.help.text);
                });
                Check("retained cell click selects after reorder and first FX character opens suggestions",()=>{
                    foreach(var t in song.model.tracks)t.visibleEffectColumns=Math.Max(1,t.visibleEffectColumns);
                    window.pane=0;window.row=0;window.BuildPane();LayoutChain(window);
                    var cell=window.gridCanvas.Q<Label>("cell-0-3");Need(cell!=null&&cell.worldBound.width>0,"Nonfirst cell was not laid out");
                    using(var click=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=cell.worldBound.center})){click.target=cell;cell.SendEvent(click);}
                    PointerRelease(window.rootVisualElement.panel.visualTree,cell.worldBound.center);
                    Need(window.sub==3,"Retained cell pointer did not select column 3");
                    int fx=window.fields.FindIndex(1,f=>f.kind==FieldKind.EffectId||f.kind==FieldKind.LocalId);Need(fx>0,"No nonfirst FX identifier field");
                    window.grid.scrollOffset=new Vector2(Mathf.Max(0,window.fieldX[fx]-30),0);window.BuildVisibleCells();LayoutChain(window);
                    cell=window.gridCanvas.Q<Label>("cell-0-"+fx);Need(cell!=null&&cell.worldBound.width>0,"FX cell was not laid out");
                    using(var click=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=cell.worldBound.center})){click.target=cell;cell.SendEvent(click);}
                    PointerRelease(window.rootVisualElement.panel.visualTree,cell.worldBound.center);
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
