using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>
        /// FX column count, the +N expander, stacked commands, bound columns and bar view (T-0597 / T-0598), driven through
        /// the real attached window with panel pointer and key events, on a disposable in-memory song.
        /// </summary>
        public static string CheckFxColumns()
        {
            var report=new StringBuilder();int passed=0,failed=0;
            var window=CreateInstance<ZTrackerModelWindow>();var song=CreateInstance<ZTrackerSong>();
            var clip=AudioClip.Create("fx-columns-check",48000,1,48000,false);var raw=new float[48000];for(int i=0;i<raw.Length;i++)raw[i]=.1f;clip.SetData(raw,0);
            var instrument=TrackerEngineCheck.FixtureInstrument(clip);
            void Need(bool okay,string why){if(!okay)throw new InvalidOperationException(why);}
            void Check(string title,Action test){try{test();passed++;report.AppendLine("PASS "+title);}catch(Exception e){failed++;report.AppendLine("FAIL "+title+": "+e.Message);}}
            void Key(KeyCode code,char ch='\0',EventModifiers mods=EventModifiers.None){window.grid.Focus();using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=code,character=ch,modifiers=mods})){e.target=window.grid;window.grid.SendEvent(e);}}
            void Type(string text){foreach(char c in text){var code=char.IsDigit(c)?KeyCode.Alpha0+(c-'0'):KeyCode.A+(char.ToUpperInvariant(c)-'A');Key(code,char.ToLowerInvariant(c));}}
            void Rebuild(){window.BuildPane();LayoutChain(window);}
            Label Cell(string name){LayoutChain(window);return window.gridCanvas.Q<Label>(name);}
            int Field(int t,int column,FieldKind kind)=>window.fields.FindIndex(f=>f.track==t&&f.column==column&&f.kind==kind);
            PatternTrack Records(int t)=>window.Pattern.tracks.Find(x=>x.trackId==song.model.tracks[t].id);
            EffectCell Fx(int t,int r,int column)=>Records(t).ReadLine(r).effects.Find(e=>e.column==column);
            void Put(int t,int r,int column,string id,int value,bool inactive=false){var line=Records(t).ReadLine(r);line.effects.RemoveAll(e=>e.column==column);line.effects.Add(new EffectCell{column=column,inactive=inactive,command=new CommandData{present=true,identifier=id,valuePresent=true,value=value,scope=CommandScope.Track}});Records(t).WriteLine(line);}
            TextField Search(){return window.rootVisualElement.panel.visualTree.Query<TextField>(className:"zui-menu__search").ToList().LastOrDefault();}
            void Enter(TextField field,string text){field.value=text;using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.Return,character='\n'})){e.target=field;field.SendEvent(e);}}
            void RightClick(VisualElement target){using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=1,mousePosition=target.worldBound.center})){e.target=target;target.SendEvent(e);}using(var e=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=1,mousePosition=target.worldBound.center}))target.panel.visualTree.SendEvent(e);}
            var l=new NativeArray<float>(24000,Allocator.Persistent);var r=new NativeArray<float>(24000,Allocator.Persistent);
            float[] Render(SongData data){using(var e=new TrackerOffline(TrackerPreparedSong.Prepare(data))){e.SendCommand(TrackerCommand.Play());e.Render(l,r,24000,512);}return l.ToArray().Concat(r.ToArray()).ToArray();}
            try
            {
                song.schemaVersion=1;song.model=TrackerEngineCheck.FixtureSong(instrument,lines:24);song.model.tracks[0].name="Lead";song.model.tracks[1].name="Master";
                window.song=song;window.instrument=instrument;window.pane=0;window.editMode=true;window.Show();window.position=new Rect(60,60,1200,820);window.CreateGUI();Rebuild();

                Check("header − and + set the track's shown FX columns, keep the cursor on its track, and Undo",()=>{
                    var plus=Cell("pattern-fx-add-0");Need(plus!=null&&plus.worldBound.width>0,"No + on the Lead header");
                    Need(window.gridCanvas.Q<Label>("pattern-fx-add-1")!=null,"No + on the Master header");
                    PointerClick(plus);Need(song.model.tracks[0].visibleEffectColumns==2,"+ did not add a column");Need(window.fields.Count(f=>f.track==0&&f.kind==FieldKind.EffectId)==2,"Grid did not show FX2");
                    Undo.PerformUndo();Rebuild();Need(song.model.tracks[0].visibleEffectColumns==1,"Undo did not remove the column");Undo.PerformRedo();Rebuild();Need(song.model.tracks[0].visibleEffectColumns==2,"Redo failed");
                    window.Select(0,Field(0,1,FieldKind.EffectValue),false);
                    PointerClick(Cell("pattern-fx-remove-0"));Need(song.model.tracks[0].visibleEffectColumns==1,"− did not remove a column");Need(window.fields[window.sub].track==0,"Cursor left the track whose column was removed");
                    PointerClick(Cell("pattern-fx-remove-0"));Need(song.model.tracks[0].visibleEffectColumns==0,"− did not reach zero");
                    Need(Cell("pattern-fx-remove-0").resolvedStyle.visibility==UnityEngine.UIElements.Visibility.Hidden,"− still offered at zero columns");
                    window.SetFxColumns(0,MaxEffectColumns);Rebuild();Need(Cell("pattern-fx-add-0").resolvedStyle.visibility==UnityEngine.UIElements.Visibility.Hidden,"+ still offered at eight columns");
                    window.SetFxColumns(0,1);Rebuild();
                });

                Check("+N marks rows with commands beyond the shown columns and expands to the widest row",()=>{
                    var t=song.model.tracks[0];t.visibleEffectColumns=1;Records(0).lines.Clear();
                    Put(0,3,0,"0L",0x10);Put(0,3,1,"0P",0x20);Put(0,3,2,"0M",0x30);
                    for(int c=0;c<5;c++)Put(0,6,c,"0L",c);
                    Put(0,7,5,"0L",0x77,inactive:true);
                    Rebuild();
                    var b3=Cell("pattern-fx-overflow-3-0");var b6=Cell("pattern-fx-overflow-6-0");var header=Cell("pattern-fx-expand-0");
                    Need(b3!=null&&b3.text=="+2","Row 3 should say +2: "+b3?.text);Need(b6!=null&&b6.text=="+4","Row 6 should say +4: "+b6?.text);
                    Need(Cell("pattern-fx-overflow-7-0")==null,"An imported, inactive cell was counted as playing");
                    Need(header!=null&&header.text=="+4","Header should offer +4 columns: "+header?.text);
                    var lastValue=Cell("cell-3-"+Field(0,0,FieldKind.EffectValue));var master=Cell("cell-3-"+window.fields.FindIndex(f=>f.track==1));
                    Need(b3.worldBound.xMin>=lastValue.worldBound.xMax-.5f&&b3.worldBound.xMax<=master.worldBound.xMin,"Row marker does not sit in the gutter between the tracks");
                    PointerClick(b3);Need(t.visibleEffectColumns==5,"Clicking +2 should show five columns (the widest row): "+t.visibleEffectColumns);
                    Need(Cell("pattern-fx-overflow-3-0")==null&&Cell("pattern-fx-expand-0")==null,"Markers remain after expanding");
                    Need(Records(0).ReadLine(6).effects.Count==5&&Records(0).ReadLine(7).effects.Count==1,"Expanding changed stored commands");
                    Undo.PerformUndo();Rebuild();Need(t.visibleEffectColumns==1&&Cell("pattern-fx-overflow-6-0")!=null,"Undo did not restore the column count and markers");
                    PointerClick(Cell("pattern-fx-expand-0"));Need(t.visibleEffectColumns==5,"Header +N did not expand");
                });

                Check("stacked commands type, edit, select, copy, cut, paste and Paste Insert across several FX columns",()=>{
                    var t=song.model.tracks[0];window.SetFxColumns(0,3);Records(0).lines.Clear();Rebuild();
                    string[] ids={"0l","0p","0m"};string[] values={"40","80","c0"};
                    for(int c=0;c<3;c++){window.Select(2,Field(0,c,FieldKind.EffectId),false);Type(ids[c]);window.Select(2,Field(0,c,FieldKind.EffectValue),false);Type(values[c]);}
                    var row2=Records(0).ReadLine(2).effects.OrderBy(e=>e.column).ToList();
                    Need(row2.Count==3&&row2[0].command.identifier=="0L"&&row2[0].command.value==0x40&&row2[1].command.identifier=="0P"&&row2[1].command.value==0x80&&row2[2].command.identifier=="0M"&&row2[2].command.value==0xC0,"Three stacked commands were not entered");
                    window.Select(2,Field(0,1,FieldKind.EffectValue),false);Type("7f");Need(Fx(0,2,1).command.value==0x7F&&Fx(0,2,0).command.value==0x40,"Editing FX2 disturbed its neighbours");
                    window.Select(2,Field(0,0,FieldKind.EffectId),false);for(int i=0;i<5;i++)Key(KeyCode.RightArrow,'\0',EventModifiers.Shift);
                    Need(window.anchorSub==Field(0,0,FieldKind.EffectId)&&window.sub==Field(0,2,FieldKind.EffectValue),"Shift+Right did not span the three FX columns");
                    Key(KeyCode.C,'\0',EventModifiers.Control);window.Select(5,Field(0,0,FieldKind.EffectId),false);Key(KeyCode.V,'\0',EventModifiers.Control);
                    Need(Enumerable.Range(0,3).All(c=>Fx(0,5,c)?.command.identifier==Fx(0,2,c).command.identifier&&Fx(0,5,c).command.value==Fx(0,2,c).command.value),"Paste did not copy all three stacked commands");
                    Need(Records(0).ReadLine(5).notes.Count==0,"Paste touched the note column");
                    string before=JsonUtility.ToJson(window.Pattern);
                    window.Select(4,Field(0,0,FieldKind.EffectId),false);Key(KeyCode.V,'\0',EventModifiers.Control|EventModifiers.Shift);
                    Need(Enumerable.Range(0,3).All(c=>Fx(0,4,c)?.command.value==Fx(0,2,c).command.value&&Fx(0,6,c)?.command.value==Fx(0,2,c).command.value)&&Fx(0,5,0)==null,"Paste Insert did not push the stacked row down by one");
                    Undo.PerformUndo();Need(JsonUtility.ToJson(window.Pattern)==before,"One Undo did not restore the Paste Insert");
                    window.Select(5,Field(0,0,FieldKind.EffectId),false);window.Select(5,Field(0,2,FieldKind.EffectValue),true);Key(KeyCode.X,'\0',EventModifiers.Control);
                    Need(Records(0).ReadLine(5).effects.Count==0,"Cut left commands behind");window.Select(9,Field(0,0,FieldKind.EffectId),false);Key(KeyCode.V,'\0',EventModifiers.Control);
                    Need(Records(0).ReadLine(9).effects.Count==3,"Cut commands did not paste");
                    window.Select(9,Field(0,1,FieldKind.EffectId),false);window.Select(9,Field(0,1,FieldKind.EffectValue),true);Key(KeyCode.Delete);Need(Fx(0,9,1)==null&&Fx(0,9,0)!=null&&Fx(0,9,2)!=null,"Clearing FX2 disturbed FX1 or FX3");
                });

                Check("hidden commands keep playing: the column count and bound views never change the render",()=>{
                    SongData Fixture(){var s=TrackerEngineCheck.FixtureSong(instrument);s.patterns[0].tracks[0].WriteLine(new PatternLine{line=0,notes={TrackerEngineCheck.Note()},effects={new EffectCell{column=2,command=new CommandData{present=true,identifier="0L",valuePresent=true,value=0x40,scope=CommandScope.Track}}}});return s;}
                    var shown=Fixture();shown.tracks[0].visibleEffectColumns=3;var a=Render(shown);
                    var hidden=Fixture();hidden.tracks[0].visibleEffectColumns=1;var b=Render(hidden);
                    var viewed=Fixture();viewed.tracks[0].visibleEffectColumns=3;viewed.tracks[0].effectColumnViews.Add(new EffectColumnView{column=2,command="0L",bar=true});viewed.tracks[0].effectColumnViews.Add(new EffectColumnView{column=0,command="0P"});var c=Render(viewed);
                    var none=Fixture();none.patterns[0].tracks[0].lines[0].effects.Clear();var d=Render(none);
                    Need(a.SequenceEqual(b),"Hiding the column changed the audio");Need(a.SequenceEqual(c),"Bound or bar view changed the audio");Need(!a.SequenceEqual(d),"Fixture command is inaudible, so the comparison proves nothing");
                });

                Check("old songs read unchanged and building the view never writes",()=>{
                    var old=JsonUtility.FromJson<TrackData>("{\"id\":\"old\",\"name\":\"Old\",\"visibleEffectColumns\":2,\"columns\":[]}");
                    Need(old.effectColumnViews!=null&&old.effectColumnViews.Count==0&&old.visibleEffectColumns==2,"A track saved before views did not read as plain columns");
                    var bound=song.model.tracks[0];bound.effectColumnViews.Add(new EffectColumnView{column=0,command="0L",bar=true});string before=JsonUtility.ToJson(song.model);Rebuild();
                    Need(before==JsonUtility.ToJson(song.model),"Building a bound, barred view changed the song");bound.effectColumnViews.Clear();Rebuild();
                });

                Check("right-click a column title, type an id and Enter binds it; it takes hex values, refuses other ids, flags other commands and unbinds without changing the pattern",()=>{
                    window.SetFxColumns(0,2);Records(0).lines.Clear();Rebuild();
                    var title=Cell("pattern-fx-title-0-0");Need(title!=null&&title.text=="FX1","FX1 title missing");RightClick(title);
                    var search=Search();Need(window.bindMenu?.IsOpen==true&&search!=null,"Right-click did not open the bind menu");Enter(search,"0l");
                    Need(ColumnView(song.model.tracks[0],0)?.command=="0L","Enter did not bind FX1 to 0L");Need(window.bindMenu==null||!window.bindMenu.IsOpen,"Menu stayed open after binding");
                    Need(Cell("pattern-fx-title-0-0").text=="0L","Bound title does not show the id");
                    window.Select(4,Field(0,0,FieldKind.EffectId),false);Type("4");Type("0");Need(Fx(0,4,0)?.command.identifier=="0L"&&Fx(0,4,0).command.value==0x40,"Typing hex in the bound column did not write 0L40");
                    Need(Cell("cell-4-"+Field(0,0,FieldKind.EffectId)).ClassListContains("tracker-bound-id"),"Bound id is not drawn dim");
                    window.Select(5,Field(0,0,FieldKind.EffectValue),false);Type("g");Need(Fx(0,5,0)==null&&window.help.text.Contains("shows only 0L"),"A non-hex key in a bound column was not refused with a notice: "+window.help.text);
                    window.Select(4,Field(0,0,FieldKind.EffectValue),false);Key(KeyCode.Delete);Need(Fx(0,4,0)==null,"Delete on a bound value left the id behind");
                    Undo.PerformUndo();Need(Fx(0,4,0)?.command.value==0x40,"Undo did not restore the cleared command");
                    Put(0,6,0,"0P",0x20);Rebuild();var other=Cell("cell-6-"+Field(0,0,FieldKind.EffectId));Need(other.text=="0P"&&other.ClassListContains("tracker-bound-other"),"Another command in a bound column is not shown and flagged");
                    string pattern=JsonUtility.ToJson(window.Pattern);
                    RightClick(Cell("pattern-fx-title-0-0"));var unbind=window.rootVisualElement.panel.visualTree.Query(className:"zui-menu__item").ToList().FirstOrDefault(x=>x.Q<Label>(className:"zui-menu__label")?.text=="Unbind");Need(unbind!=null,"No Unbind item");PointerClick(unbind);
                    Need(ColumnView(song.model.tracks[0],0)==null&&Cell("pattern-fx-title-0-0").text=="FX1","Unbind did not restore the plain column");Need(JsonUtility.ToJson(window.Pattern)==pattern,"Binding or unbinding changed the pattern");
                    Undo.PerformUndo();Rebuild();Need(ColumnView(song.model.tracks[0],0)?.command=="0L","Undo did not restore the binding");
                });

                Check("Alt+B binds from the keyboard to a song command by its permanent key, and a rename keeps the binding",()=>{
                    var p=ZTrackerCommandRegistry.Define(song.model,DynamicScope.Track,new ParameterTarget{kind=ParameterKind.Mixer,trackId=song.model.tracks[0].id,parameter="postVolume"},"vl","Lead level",0,2,out var error);Need(p!=null,"Fixture command: "+error);
                    window.SetFxColumns(0,2);Records(0).lines.Clear();Rebuild();window.Select(0,Field(0,1,FieldKind.EffectId),false);
                    Key(KeyCode.B,'\0',EventModifiers.Alt);var search=Search();Need(window.bindMenu?.IsOpen==true&&search!=null,"Alt+B did not open the bind menu");
                    Need(window.rootVisualElement.panel.visualTree.Query<Label>(className:"zui-menu__label").ToList().Any(x=>x.text.StartsWith("VL  Lead level")),"Song command not listed");
                    Enter(search,"vl");Need(ColumnView(song.model.tracks[0],1)?.command==p.uid,"Binding did not store the command's permanent key");Need(Cell("pattern-fx-title-0-1").text=="VL","Title does not show VL");
                    Need(ZTrackerCommandRegistry.Rename(song.model,p,"lv","Lead level")==null,"Rename refused");Rebuild();Need(Cell("pattern-fx-title-0-1").text=="LV","Binding did not follow the rename");
                    window.Select(1,Field(0,1,FieldKind.EffectId),false);Type("80");Need(Fx(0,1,1)?.command.identifier=="LV"&&Fx(0,1,1).command.value==0x80,"Typing in the renamed binding did not write LV80");
                });

                Check("bar view draws value bars, still takes typed values, and a mouse drag draws values in one Undo step",()=>{
                    window.Select(1,Field(0,1,FieldKind.EffectId),false);Key(KeyCode.V,'\0',EventModifiers.Alt);Need(ColumnView(song.model.tracks[0],1).bar,"Alt+V did not switch on bar view");
                    var id=Cell("cell-1-"+Field(0,1,FieldKind.EffectId));var value=Cell("cell-1-"+Field(0,1,FieldKind.EffectValue));var idBar=id.Q("fx-bar");
                    Need(id.text==""&&value.text==""&&idBar!=null&&Mathf.Abs(idBar.resolvedStyle.width-16f)<.5f,"80 should fill half the bar (16 px of 32): "+idBar?.resolvedStyle.width);
                    Need(Cell("pattern-fx-title-0-1").text.StartsWith("LV"),"Bar title lost the id");
                    window.Select(2,Field(0,1,FieldKind.EffectValue),false);Type("ff");Need(Fx(0,2,1)?.command.value==255,"Typing in bar view failed");
                    var full=Cell("cell-2-"+Field(0,1,FieldKind.EffectValue)).Q("fx-bar");Need(full!=null&&Mathf.Abs(full.resolvedStyle.width-16f)<.5f,"FF should fill the whole bar");
                    string before=JsonUtility.ToJson(window.Pattern);
                    var start=Cell("cell-4-"+Field(0,1,FieldKind.EffectId));var end=Cell("cell-7-"+Field(0,1,FieldKind.EffectValue));Need(start!=null&&end!=null,"Draw rows not laid out");
                    PointerDrag(start,new Vector2(end.worldBound.xMax-.5f,end.worldBound.center.y),9,finish:false);
                    var drawn=Enumerable.Range(4,4).Select(rr=>Fx(0,rr,1)).ToList();
                    Need(drawn.All(x=>x!=null&&x.command.identifier=="LV"&&x.command.valuePresent),"Drag did not draw every row: "+string.Join(",",drawn.Select(x=>x==null?"-":x.command.value.ToString("X2"))));
                    Need(drawn[0].command.value>=0x40&&drawn[0].command.value<0x80&&drawn[3].command.value>=0xF0,"Drawn values do not follow the pointer: "+string.Join(",",drawn.Select(x=>x.command.value.ToString("X2"))));
                    for(int i=1;i<4;i++)Need(drawn[i].command.value>=drawn[i-1].command.value,"Drawn values are not monotonic");
                    Need(window.row==4,"Drawing moved the cursor away from where it started");
                    Undo.PerformUndo();Need(JsonUtility.ToJson(window.Pattern)==before,"One Undo did not remove the whole drawn stroke");
                    PointerClick(Cell("cell-9-"+Field(0,1,FieldKind.EffectId)));Need(Fx(0,9,1)!=null&&Math.Abs(Fx(0,9,1).command.value-64)<=4,"A click a quarter of the way along the bar should set 40: "+Fx(0,9,1)?.command.value.ToString("X2"));Undo.PerformUndo();Need(JsonUtility.ToJson(window.Pattern)==before,"Undo of a one-click draw failed");
                    var shiftStart=Cell("cell-4-"+Field(0,1,FieldKind.EffectId));using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,modifiers=EventModifiers.Shift,mousePosition=shiftStart.worldBound.center})){e.target=shiftStart;shiftStart.SendEvent(e);}
                    using(var e=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,mousePosition=shiftStart.worldBound.center}))shiftStart.panel.visualTree.SendEvent(e);
                    Need(Fx(0,4,1)==null&&window.anchorRow>=0,"Shift+press in bar view drew instead of selecting");
                    Key(KeyCode.V,'\0',EventModifiers.Alt);Need(!ColumnView(song.model.tracks[0],1).bar&&Cell("cell-1-"+Field(0,1,FieldKind.EffectValue)).text=="80","Alt+V did not return to characters");
                    Undo.PerformUndo();Rebuild();Need(ColumnView(song.model.tracks[0],1).bar,"Undo did not restore bar view");
                });

                Check("copy and paste between bound and plain columns carry the commands themselves",()=>{
                    song.model.tracks[0].effectColumnViews.RemoveAll(v=>v.column==0);Records(0).lines.Clear();for(int rr=1;rr<=4;rr++)Put(0,rr,1,"LV",rr*0x30);Rebuild();
                    window.Select(1,Field(0,1,FieldKind.EffectId),false);window.Select(4,Field(0,1,FieldKind.EffectValue),true);Key(KeyCode.C,'\0',EventModifiers.Control);
                    window.Select(1,Field(0,0,FieldKind.EffectId),false);Key(KeyCode.V,'\0',EventModifiers.Control);
                    Need(Enumerable.Range(1,4).All(rr=>Fx(0,rr,0)?.command.identifier=="LV"&&Fx(0,rr,0).command.value==rr*0x30),"Bound column did not paste into the plain column");
                    Need(Cell("cell-2-"+Field(0,0,FieldKind.EffectId)).text=="LV","Plain column does not show the pasted id as characters");
                    window.Select(1,Field(0,0,FieldKind.EffectId),false);window.Select(2,Field(0,1,FieldKind.EffectValue),true);Key(KeyCode.C,'\0',EventModifiers.Control);
                    window.Select(10,Field(0,0,FieldKind.EffectId),false);Key(KeyCode.V,'\0',EventModifiers.Control|EventModifiers.Shift);
                    Need(Fx(0,10,0)?.command.value==0x30&&Fx(0,11,1)?.command.value==0x60&&Fx(0,10,1)?.command.identifier=="LV","Selection spanning plain and bound columns did not Paste Insert");
                });
            }
            finally{if(window.drawing)window.EndDraw();window.bindMenu?.Close();window.StopPreview();window.Close();DestroyImmediate(window);Undo.ClearUndo(song);DestroyImmediate(song);DestroyImmediate(instrument);UnityEngine.Object.DestroyImmediate(clip);l.Dispose();r.Dispose();}
            report.AppendLine($"TOTAL passed={passed} failed={failed}");return report.ToString();
        }
    }
}
