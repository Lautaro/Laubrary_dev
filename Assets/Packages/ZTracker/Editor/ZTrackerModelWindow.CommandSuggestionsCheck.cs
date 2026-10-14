using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.Audio;
using Laubrary.Zounds;
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
        /// Context-aware command suggestions (T-0599) and command colours (T-0600), driven through the real attached
        /// window with panel key and pointer events, on a disposable in-memory song. Also reports the measured contrast
        /// ratios of every command colour and the cost of typing with hundreds of commands.
        /// </summary>
        public static string CheckCommandSuggestions()
        {
            var report=new StringBuilder();int passed=0,failed=0;
            var window=CreateInstance<ZTrackerModelWindow>();var song=CreateInstance<ZTrackerSong>();
            var clip=AudioClip.Create("suggestions-check",48000,1,48000,false);var raw=new float[48000];for(int i=0;i<raw.Length;i++)raw[i]=.1f;clip.SetData(raw,0);
            var lead=TrackerEngineCheck.FixtureInstrument(clip);var bass=TrackerEngineCheck.FixtureInstrument(clip);lead.model.id="inst-lead";lead.model.name="Lead synth";bass.model.id="inst-bass";bass.model.name="Bass";
            void Need(bool okay,string why){if(!okay)throw new InvalidOperationException(why);}
            void Check(string title,Action test){try{test();passed++;report.AppendLine("PASS "+title);}catch(Exception e){failed++;report.AppendLine("FAIL "+title+": "+e.Message);}}
            void Key(KeyCode code,char ch='\0',EventModifiers mods=EventModifiers.None){using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=code,character=ch,modifiers=mods})){e.target=window.grid;window.grid.SendEvent(e);}}
            void Type(string text){foreach(char c in text){var code=char.IsDigit(c)?KeyCode.Alpha0+(c-'0'):KeyCode.A+(char.ToUpperInvariant(c)-'A');Key(code,char.ToLowerInvariant(c));}}
            void Rebuild(){window.BuildPane();LayoutChain(window);}
            Label Cell(int r,int field){LayoutChain(window);return window.gridCanvas.Q<Label>("cell-"+r+"-"+field);}
            int Field(int t,int column,FieldKind kind)=>window.fields.FindIndex(f=>f.track==t&&f.column==column&&f.kind==kind);
            PatternTrack Records(int t,int pattern=0)=>song.model.patterns[pattern].tracks.Find(x=>x.trackId==song.model.tracks[t].id);
            EffectCell Fx(int t,int r,int column)=>Records(t).ReadLine(r).effects.Find(e=>e.column==column);
            void PutNote(int t,int r,int instrument,int pattern=0){var line=Records(t,pattern).ReadLine(r);line.notes.RemoveAll(n=>n.column==0);var n=TrackerEngineCheck.Note();n.instrument=instrument;line.notes.Add(n);Records(t,pattern).WriteLine(line);}
            List<string> Offered(){return Enumerable.Range(0,window.suggestionCount).Select(i=>window.suggestions[window.suggestionOrder[i]].id).ToList();}
            List<string> OpenAt(int r,int t){window.CancelSuggestions();window.Select(r,Field(t,0,FieldKind.EffectId),false);window.grid.Focus();window.pendingId="";window.ShowFxSuggestionsForCheck(window.fields[window.sub]);Need(window.fxSuggestions?.IsOpen==true,"Suggestions did not open");var all=window.suggestions.Select(s=>s.id).ToList();window.CancelSuggestions();return all;}
            Color Back(Label l)=>l.resolvedStyle.backgroundColor;Color Fore(Label l)=>l.resolvedStyle.color;
            bool Same(Color a,Color b)=>Mathf.Abs(a.r-b.r)<.01f&&Mathf.Abs(a.g-b.g)<.01f&&Mathf.Abs(a.b-b.b)<.01f&&Mathf.Abs(a.a-b.a)<.01f;
            var l=new NativeArray<float>(24000,Allocator.Persistent);var rr=new NativeArray<float>(24000,Allocator.Persistent);
            float[] Render(SongData data){using(var e=new TrackerOffline(TrackerPreparedSong.Prepare(data))){e.SendCommand(TrackerCommand.Play());e.Render(l,rr,24000,512);}return l.ToArray().Concat(rr.ToArray()).ToArray();}
            DynamicParameterData Define(DynamicScope scope,ParameterTarget target,string id,string name){var p=ZTrackerCommandRegistry.Define(song.model,scope,target,id,name,0,1,out var error);if(p==null)throw new InvalidOperationException("Fixture "+id+": "+error);return p;}
            try
            {
                song.schemaVersion=1;song.model=TrackerEngineCheck.FixtureSongNew(lead,lines:32);song.model.instruments.Add(bass);
                song.model.tracks[0].name="Lead";song.model.tracks[1].name="Master";
                song.model.tracks.Insert(1,new TrackData{id="other",name="Other",visibleNoteColumns=1,visibleEffectColumns=1});song.model.patterns[0].tracks.Add(new PatternTrack{trackId="other"});
                song.model.tracks[0].devices.nodes.Add(new AudioEffectNodeData{type=ZoundEffectType.Gain,uid="lead-gain",p=new float[]{1}});
                var second=new PatternData{id="pattern-2",name="Second",lineCount=16};foreach(var t in song.model.tracks)second.tracks.Add(new PatternTrack{trackId=t.id});song.model.patterns.Add(second);song.model.sequence.Add(new SequenceSlot{id="slot-2",patternId="pattern-2"});
                var ac=Define(DynamicScope.Instrument,new ParameterTarget{kind=ParameterKind.Synth,instrumentId="inst-lead",parameter="volume"},"ac","Cutoff lead");
                var bv=Define(DynamicScope.Instrument,new ParameterTarget{kind=ParameterKind.Synth,instrumentId="inst-bass",parameter="volume"},"bv","Bass volume");
                var eg=Define(DynamicScope.Effect,new ParameterTarget{kind=ParameterKind.Device,trackId="notes",deviceId="lead-gain",index=0,parameter="1"},"eg","Echo gain");
                var lv=Define(DynamicScope.Track,new ParameterTarget{kind=ParameterKind.Mixer,trackId="notes",parameter="postVolume"},"lv","Lead level");
                var ov=Define(DynamicScope.Track,new ParameterTarget{kind=ParameterKind.Mixer,trackId="other",parameter="postVolume"},"ov","Other level");
                var bt=Define(DynamicScope.Song,new ParameterTarget{kind=ParameterKind.Timing,parameter="bpm"},"bt","Tempo");
                PutNote(0,0,0);PutNote(0,8,1);
                window.song=song;window.instrument=lead;window.pane=0;window.editMode=true;window.Show();window.position=new Rect(60,60,1200,820);window.CreateGUI();Rebuild();

                Check("context: the instrument playing on the row, the track's effect and mixer, the song, built-ins; not another instrument or another track",()=>{
                    var at2=OpenAt(2,0);Need(at2.Contains("AC")&&at2.Contains("EG")&&at2.Contains("LV")&&at2.Contains("BT")&&at2.Contains("0L")&&at2.Contains("ZT"),"Row 2 misses commands that belong: "+string.Join(",",at2.Take(12)));
                    Need(!at2.Contains("BV")&&!at2.Contains("OV"),"Row 2 offers the other instrument's or the other track's command: "+string.Join(",",at2.Take(12)));
                    var at10=OpenAt(10,0);Need(at10.Contains("BV")&&!at10.Contains("AC"),"Row 10 (Bass playing) should offer BV, not AC: "+string.Join(",",at10.Take(12)));
                    Need(at2.IndexOf("AC")<at2.IndexOf("0L"),"Song commands should come before the built-ins");
                    Need(new[]{ac,bv,eg,lv,ov,bt}.Select(x=>ZTrackerCommandRegistry.ColorOf(x)).Distinct().Count()==6,"Six commands of one song did not get six different automatic colours");Need(window.suggestions.Count(s=>s.id=="BV")==1&&window.suggestions.First(s=>s.id=="BV").scope=="Instrument"&&window.suggestions.First(s=>s.id=="BV").name=="Bass volume","A suggestion lacks its friendly name or scope");
                });

                Check("context: no instrument playing on the row (an empty track) offers no instrument commands; the next pattern keeps the last instrument",()=>{
                    var other=OpenAt(3,1);Need(!other.Contains("AC")&&!other.Contains("BV")&&!other.Contains("EG")&&!other.Contains("LV"),"An empty track offers instrument or other-track commands: "+string.Join(",",other.Take(12)));
                    Need(other.Contains("OV")&&other.Contains("BT")&&other.Contains("0L"),"An empty track lacks its own, the song's or the built-in commands");
                    window.order=1;Rebuild();var next=OpenAt(0,0);Need(next.Contains("BV")&&!next.Contains("AC"),"Pattern 2 row 0 should inherit Bass from slot 0's last selection: "+string.Join(",",next.Take(12)));
                    window.order=0;Rebuild();
                });

                Check("typing narrows by id and by name; Down, Enter, Tab and Escape work while the grid keeps focus",()=>{
                    window.Select(4,Field(0,0,FieldKind.EffectId),false);window.grid.Focus();Type("c");
                    Need(window.fxSuggestions?.IsOpen==true,"The first character did not open the list");
                    Need(window.rootVisualElement.focusController.focusedElement==window.grid,"The list took keyboard focus");
                    Type("u");Need(window.fxSuggestions?.IsOpen==true&&Fx(0,4,0)==null,"CU (no id, but a name matches) should keep the list open");
                    var offered=Offered();Need(offered.Count>=1&&offered[0]=="AC","Name match did not rank Cutoff lead first: "+string.Join(",",offered.Take(5)));
                    Need(window.suggestionTypedRow&&window.suggestionHighlight==1,"The typed row should be first with the best match highlighted");
                    Type("t");Need(Offered().Contains("AC"),"CUT lost the name match");
                    Key(KeyCode.Return);Need(Fx(0,4,0)?.command.identifier=="AC"&&window.fxSuggestions==null,"Enter did not enter the highlighted AC");
                    Need(window.row==5&&window.rootVisualElement.focusController.focusedElement==window.grid,"Entering did not advance or the grid lost focus");
                    window.Select(6,Field(0,0,FieldKind.EffectId),false);Type("e");Key(KeyCode.DownArrow);int h=window.suggestionHighlight;Need(h==1,"Down did not move the highlight");
                    string expected=window.suggestions[window.suggestionOrder[h]].id;Key(KeyCode.Tab);Need(Fx(0,6,0)?.command.identifier==expected,"Tab did not enter the highlighted command "+expected);
                    bool edit=window.editMode;window.Select(7,Field(0,0,FieldKind.EffectId),false);Type("e");Key(KeyCode.Escape);
                    Need(window.fxSuggestions==null&&window.pendingId==""&&Fx(0,7,0)==null&&window.editMode==edit,"Escape should cancel the list only (nothing written, Edit mode unchanged)");
                    window.Select(7,Field(0,0,FieldKind.EffectId),false);Type("ec");Key(KeyCode.Backspace);Need(window.pendingId=="E"&&window.fxSuggestions?.IsOpen==true,"Backspace did not remove one typed character");window.CancelSuggestions();
                });

                Check("two characters that name a command enter at once; anything else not offered still enters as typed and an undefined one is flagged",()=>{
                    window.Select(10,Field(0,0,FieldKind.EffectId),false);Type("0l");Need(Fx(0,10,0)?.command.identifier=="0L"&&window.fxSuggestions==null,"0L did not enter at once");
                    window.Select(11,Field(0,0,FieldKind.EffectId),false);Type("ac");Need(Fx(0,11,0)?.command.identifier=="AC","AC (known, not offered on a Bass row) was not entered as typed");
                    window.Select(11,Field(0,0,FieldKind.EffectId),false);Need(window.help.text.Contains("not in context"),"An out-of-context command is not explained: "+window.help.text);
                    window.Select(12,Field(0,0,FieldKind.EffectId),false);Type("qq");Need(Fx(0,12,0)?.command.identifier=="QQ","QQ (nothing matches) did not enter as typed");
                    window.Select(13,Field(0,0,FieldKind.EffectId),false);var cell=Cell(12,Field(0,0,FieldKind.EffectId));
                    Need(Same(Back(cell),CommandColors.UndefinedBack)&&Same(Fore(cell),Color.white),"Undefined QQ is not white on red: "+Back(cell));
                    window.Select(12,Field(0,0,FieldKind.EffectId),false);Need(window.help.text.StartsWith("⚠ QQ")&&window.help.text.Contains("not defined"),"Undefined QQ is not flagged in the help line: "+window.help.text);
                    window.Select(14,Field(0,0,FieldKind.EffectId),false);Type("le");Need(window.fxSuggestions?.IsOpen==true,"LE matches the name 'Lead level' and should stay open");Key(KeyCode.UpArrow);Key(KeyCode.Return);
                    Need(Fx(0,14,0)?.command.identifier=="LE","The typed row did not enter LE as typed");
                });

                Check("mouse: clicking a suggestion enters it; the list sits beside the cell, never over it",()=>{
                    window.Select(15,Field(0,0,FieldKind.EffectId),false);Type("b");LayoutChain(window);
                    var panel=window.rootVisualElement.Q("fx-suggestions");var cell=Cell(15,Field(0,0,FieldKind.EffectId));Need(panel!=null&&panel.worldBound.height>0,"No laid-out list");
                    Need(!panel.worldBound.Overlaps(cell.worldBound),"The list covers the cell being typed");
                    int target=Offered().IndexOf("BT");Need(target>=0&&target<SuggestionRows,"BT not in the first page");
                    var view=window.rootVisualElement.Q("fx-suggestion-"+target);PointerClick(view);
                    Need(Fx(0,15,0)?.command.identifier=="BT"&&window.fxSuggestions==null,"Clicking BT did not enter it");
                });

                Check("colours: built-ins by kind on the dark grid, song commands dark on their own colour, the same in the list, bound columns and bars",()=>{
                    Records(0).lines.RemoveAll(x=>x.line>=16);
                    void Put(int r,string id,int v){var line=Records(0).ReadLine(r);line.effects.Clear();line.effects.Add(new EffectCell{column=0,command=new CommandData{present=true,identifier=id,valuePresent=true,value=v,scope=CommandScope.Track}});Records(0).WriteLine(line);}
                    Put(16,"0L",0x40);Put(17,"ZT",0x80);Put(18,"0P",0x80);Put(19,"LV",0x40);window.Select(25,Field(0,0,FieldKind.EffectId),false);Rebuild();
                    int fid=Field(0,0,FieldKind.EffectId),fval=Field(0,0,FieldKind.EffectValue);
                    Need(Same(Fore(Cell(16,fid)),CommandColors.KindColor(CommandKind.Volume))&&Same(Fore(Cell(16,fval)),CommandColors.KindColor(CommandKind.Volume)),"0L is not drawn in the Volume colour");
                    Need(Same(Fore(Cell(17,fid)),CommandColors.KindColor(CommandKind.Song))&&Same(Fore(Cell(18,fid)),CommandColors.KindColor(CommandKind.Pan)),"ZT/0P not in their kind colours");
                    Need(Back(Cell(16,fid)).a<.2f,"A built-in got a coloured background");
                    var lvColor=ZTrackerCommandRegistry.ColorOf(lv);Need(Same(Back(Cell(19,fid)),lvColor)&&Same(Back(Cell(19,fval)),lvColor)&&Same(Fore(Cell(19,fid)),CommandColors.DarkText),"LV is not dark text on its colour");
                    var auto=ZTrackerCommandRegistry.AutoColors;Need(auto.Contains(lvColor),"LV's colour is not one of the automatic colours");
                    Need(ZTrackerCommandRegistry.Rename(song.model,lv,"lw","Lead level")==null,"Rename refused");Rebuild();
                    Need(Same(ZTrackerCommandRegistry.ColorOf(lv),lvColor)&&Same(Back(Cell(19,fid)),lvColor)&&Cell(19,fid).text=="LW","Rename changed the colour");
                    window.Select(20,fid,false);Type("l");LayoutChain(window);int at=Offered().IndexOf("LW");var chip=window.rootVisualElement.Q("fx-suggestion-"+at).Q<Label>(className:"tracker-suggest__chip");
                    Need(Same(Back(chip),lvColor)&&Same(Fore(chip),CommandColors.DarkText),"The list's LW chip is not in LW's colours");
                    int zl=Offered().IndexOf("0L");var bchip=zl>=0&&zl<SuggestionRows?window.rootVisualElement.Q("fx-suggestion-"+zl)?.Q<Label>(className:"tracker-suggest__chip"):null;
                    Need(bchip==null||Same(Fore(bchip),CommandColors.KindColor(CommandKind.Volume)),"The list's 0L chip is not in the Volume colour");window.CancelSuggestions();
                    window.Bind(0,0,lv.uid);window.SetBarView(0,0,true);LayoutChain(window);
                    var bar=Cell(19,fid).Q("fx-bar");Need(bar!=null&&Same(bar.resolvedStyle.backgroundColor,lvColor),"LW's bar is not in its colour");
                    var title=window.gridCanvas.Q<Label>("pattern-fx-title-0-0");Need(Same(Back(title),lvColor),"The bound title is not in LW's colour");
                    window.SetBarView(0,0,false);LayoutChain(window);Need(Same(Back(Cell(19,fid)),lvColor)&&Fore(Cell(19,fid)).a<.7f,"A bound id should keep its colour, dimmed");
                    window.Bind(0,0,"0L");LayoutChain(window);Need(Cell(19,fid).ClassListContains("tracker-bound-other")&&Same(Back(Cell(19,fid)),lvColor),"Another command in a bound column lost its colour or flag");
                    window.Bind(0,0,null);
                });

                Check("a chosen colour is stored with the command, undoes, and never changes the pattern or the sound",()=>{
                    var before=Render(song.model);string pattern=JsonUtility.ToJson(song.model.patterns);
                    var lvColorBefore=ZTrackerCommandRegistry.ColorOf(lv);window.SetCommandColor(lv.uid,new Color(.2f,.3f,.9f,1));Rebuild();int fid=Field(0,0,FieldKind.EffectId);
                    Need(Same(Back(Cell(19,fid)),new Color(.2f,.3f,.9f,1))&&Same(Fore(Cell(19,fid)),Color.white),"A dark chosen colour should get white text");
                    var after=Render(song.model);Need(before.SequenceEqual(after),"A colour changed the sound");Need(pattern==JsonUtility.ToJson(song.model.patterns),"A colour changed the pattern");
                    Undo.PerformUndo();Rebuild();Need(Same(ZTrackerCommandRegistry.ColorOf(ZTrackerCommandRegistry.FindByUid(song.model,lv.uid)),lvColorBefore),"Undo did not restore the colour");
                    window.pane=4;window.BuildPane();LayoutChain(window);var chip=window.rootVisualElement.Q<Label>("command-chip-LW");var swatch=window.rootVisualElement.Q("command-color-LW");
                    Need(chip!=null&&swatch!=null&&Same(Back(chip),ZTrackerCommandRegistry.ColorOf(ZTrackerCommandRegistry.FindByUid(song.model,lv.uid))),"Song › Commands lacks the coloured chip or the colour field");
                    window.SetCommandColor(lv.uid,new Color(.9f,.5f,.5f,1));window.BuildPane();LayoutChain(window);chip=window.rootVisualElement.Q<Label>("command-chip-LW");
                    var chipRoot=chip.panel.visualTree;var chipAt=chip.worldBound.center;
                    using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,clickCount=2,mousePosition=chipAt})){e.target=chip;chip.SendEvent(e);}
                    PointerRelease(chipRoot,chipAt);
                    var picked=ZTrackerCommandRegistry.FindByUid(song.model,lv.uid).color;Need(ZTrackerCommandRegistry.AutoColors.Contains(picked),"Double-clicking the chip did not pick an automatic colour: "+picked);
                    window.pane=0;Rebuild();
                });

                Check("every built-in has a kind colour; contrast meets 4.5:1 for every colour on the grid, a selection and the cursor rows",()=>{
                    foreach(var e in FxCommandCatalog.For(FieldKind.EffectId).Concat(FxCommandCatalog.For(FieldKind.Volume)).Concat(FxCommandCatalog.For(FieldKind.Pan)).Concat(FxCommandCatalog.For(FieldKind.LocalId)))Need(e.kind!=CommandKind.None,e.id+" has no kind");
                    var lines=new List<string>();var grid=CommandColors.GridBack;var sel=CommandColors.Over(CommandColors.SelectionTint,grid);var beat=CommandColors.Over(new Color(130/255f,130/255f,130/255f,.08f),grid);
                    foreach(CommandKind k in Enum.GetValues(typeof(CommandKind))){if(k==CommandKind.None)continue;var c=CommandColors.KindColor(k);float a=CommandColors.Contrast(c,grid),bb=CommandColors.Contrast(c,beat),b=CommandColors.Contrast(c,sel);lines.Add(k+" "+a.ToString("0.0")+"/"+bb.ToString("0.0")+"/"+b.ToString("0.0"));Need(a>=4.5f&&bb>=4.5f&&b>=4.5f,k+" contrast too low");}
                    foreach(var c in ZTrackerCommandRegistry.AutoColors){float a=CommandColors.Contrast(CommandColors.DarkText,c),b=CommandColors.Contrast(CommandColors.TextOn(CommandColors.Selected(c)),CommandColors.Selected(c));lines.Add("#"+ColorUtility.ToHtmlStringRGB(c)+" "+a.ToString("0.0")+"/"+b.ToString("0.0"));Need(a>=4.5f&&b>=4.5f,"#"+ColorUtility.ToHtmlStringRGB(c)+" contrast too low");}
                    float u=CommandColors.Contrast(Color.white,CommandColors.UndefinedBack),us=CommandColors.Contrast(Color.white,CommandColors.Selected(CommandColors.UndefinedBack));lines.Add("undefined "+u.ToString("0.0")+"/"+us.ToString("0.0"));Need(u>=4.5f,"Undefined contrast too low");
                    foreach(var c in ZTrackerCommandRegistry.AutoColors)Need(Mathf.Abs(c.r-CommandColors.UndefinedBack.r)+Mathf.Abs(c.g-CommandColors.UndefinedBack.g)+Mathf.Abs(c.b-CommandColors.UndefinedBack.b)>.5f,"An automatic colour is close to the undefined red");
                    report.AppendLine("  contrast (built-ins: grid/beat row/selected; song colours: plain/selected): "+string.Join("; ",lines));
                });

                Check("fast with 400 commands: opening and each keystroke stay small, filtering allocates nothing",()=>{
                    for(int i=0;i<400;i++){string id="1ABCDEFGHIJKLMNOPQRSTUVWXY"[1+i/36%25].ToString()+"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[i%36];ZTrackerCommandRegistry.Define(song.model,DynamicScope.Song,new ParameterTarget{kind=ParameterKind.Timing,parameter="bpm",index=i},id,"Bulk "+i,0,1,out _);}
                    int defined=song.model.dynamicParameters.Count;Need(defined>350,"Bulk fixture defined only "+defined);Rebuild();
                    window.Select(21,Field(0,0,FieldKind.EffectId),false);var sw=System.Diagnostics.Stopwatch.StartNew();Type("b");double open=sw.Elapsed.TotalMilliseconds;Need(window.suggestions.Count>350,"Bulk commands not offered");
                    window.pendingId="BU";sw.Restart();Type("l");double key=sw.Elapsed.TotalMilliseconds;Need(window.fxSuggestions?.IsOpen==true&&window.pendingId=="BUL","Third character closed the list");
                    window.FilterSuggestions("BU");long a0=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<50;i++)window.FilterSuggestions(i%2==0?"BUL":"BU");long filterBytes=GC.GetAllocatedBytesForCurrentThread()-a0;
                    report.AppendLine("  "+window.suggestions.Count+" suggestions: open "+open.ToString("0.0")+" ms, narrowing keystroke "+key.ToString("0.0")+" ms, 50 re-rankings allocated "+filterBytes+" bytes");
                    Need(filterBytes==0,"Re-ranking allocated "+filterBytes+" bytes");Need(key<8,"A keystroke took "+key.ToString("0.0")+" ms");Need(open<250,"Opening took "+open.ToString("0.0")+" ms");
                    window.CancelSuggestions();
                });

                Check("narrow window: the list fits inside the window and still leaves the cell visible",()=>{
                    window.position=new Rect(60,60,420,520);LayoutChain(window);Rebuild();window.Select(2,Field(0,0,FieldKind.EffectId),false);Type("l");LayoutChain(window);
                    var panel=window.rootVisualElement.Q("fx-suggestions");var root=window.rootVisualElement.worldBound;var cell=Cell(2,Field(0,0,FieldKind.EffectId));
                    Need(panel.worldBound.xMin>=root.xMin-.5f&&panel.worldBound.xMax<=root.xMax+.5f,"The list runs off the narrow window: "+panel.worldBound+" in "+root);
                    Need(!panel.worldBound.Overlaps(cell.worldBound),"The list covers the cell in a narrow window");window.CancelSuggestions();
                });
            }
            finally{window.CloseFxSuggestions();window.StopPreview();window.Close();DestroyImmediate(window);Undo.ClearUndo(song);DestroyImmediate(song);DestroyImmediate(lead);DestroyImmediate(bass);UnityEngine.Object.DestroyImmediate(clip);l.Dispose();rr.Dispose();}
            report.AppendLine($"TOTAL passed={passed} failed={failed}");return report.ToString();
        }

        /// Opens the list for the cursor cell with nothing typed yet (the check's way to read the whole context list).
        void ShowFxSuggestionsForCheck(FieldSlot slot){pendingId="";ShowFxSuggestions(slot,"");}
    }
}
