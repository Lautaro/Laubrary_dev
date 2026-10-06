using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zui;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        enum FieldKind{Note,Instrument,Volume,Pan,Delay,LocalId,LocalValue,EffectId,EffectValue,Event}
        struct FieldSlot{public int track,column;public FieldKind kind;public string title;public FieldSlot(int t,int c,FieldKind k,string label=""){track=t;column=c;kind=k;title=label;}}
        sealed class CopiedField{public int r,c;public FieldKind kind;public NoteCell note;public EffectCell effect;public EventCell evt;}
        readonly List<FieldSlot> fields=new List<FieldSlot>();
        readonly List<float> fieldX=new List<float>();
        VisualElement gridCanvas;
        int playingRow=-1;
        readonly Dictionary<Vector2Int,Label> labels=new Dictionary<Vector2Int,Label>();
        readonly List<CopiedField> clipboard=new List<CopiedField>();
        int anchorRow=-1,anchorSub,hexDigit;
        VisualElement cellDetails;
        string pendingId="";
        static readonly string[] noteNames={"C-","C#","D-","D#","E-","F-","F#","G-","G#","A-","A#","B-"};
        void BuildPattern()
        {
            if(stage==null)return;Vector2 offset=grid?.scrollOffset??Vector2.zero;stage.Clear();fields.Clear();fieldX.Clear();labels.Clear();if(Pattern==null)return;
            var keys=Flow(Named(Z.Toggle("Edit","Record note entry into the pattern; off auditions without writing.",editMode,v=>editMode=v),"edit-mode"),Named(Z.MicroSlider("Oct",octave,0,9,"Octave used by the QWERTY piano.",v=>octave=(int)v,95,decimals:0),"octave"),Named(Z.MicroSlider("Step",step,0,32,"Rows advanced after entering a note or field.",v=>step=(int)v,95,decimals:0),"edit-step"));stage.Add(keys);
            stage.Add(Flow(Button("Copy","Copy the selected rectangle of visible fields.",CopyBlock,"copy"),Button("Paste","Paste copied fields at the cursor.",PasteBlock,"paste"),Button("−12","Transpose selected notes down an octave.",()=>Transpose(-12)),Button("−1","Transpose selected notes down a semitone.",()=>Transpose(-1)),Button("+1","Transpose selected notes up a semitone.",()=>Transpose(1)),Button("+12","Transpose selected notes up an octave.",()=>Transpose(12)),Button("Insert line","Insert a blank line; Undo restores the shifted notes and automation.",()=>SongEdit("insert line",()=>ZTrackerPatternOperations.InsertLine(Pattern,row),true)),Button("Delete line","Delete this line and shift notes and automation upward.",()=>SongEdit("delete line",()=>ZTrackerPatternOperations.DeleteLine(Pattern,row),true))));
            help=Z.Text("Command",tooltip:"Meaning of the command under the cursor, from the committed command profile.");help.style.height=22;help.style.whiteSpace=WhiteSpace.NoWrap;help.style.overflow=Overflow.Hidden;help.name="command-help";
            var helpRow=Flow(Button("?","Open the committed command profile.",()=>EditorUtility.RevealInFinder(CommandPath())),help);stage.Add(helpRow);
            grid=new ScrollView(ScrollViewMode.VerticalAndHorizontal);grid.name="pattern-grid";grid.focusable=true;grid.style.flexGrow=1;grid.style.minHeight=60;
            grid.RegisterCallback<KeyDownEvent>(KeyDown);grid.RegisterCallback<KeyUpEvent>(e=>{if(PianoOffset(e.keyCode)>=0)AuditionOff();});stage.Add(grid);
            var header=GridRow();header.Add(RowLabel("Row"));
            for(int ti=0;ti<Data.tracks.Count;ti++){
                var t=Data.tracks[ti];if(t.kind==TrackKind.Event){AddField(header,ti,0,FieldKind.Event,t.name+" Event");}
                if(t.kind==TrackKind.Sequencer)for(int c=0;c<t.visibleNoteColumns;c++){
                    var v=c<t.columns.Count?t.columns[c]:new ColumnVisibility();AddField(header,ti,c,FieldKind.Note,t.name+" "+(c+1));AddField(header,ti,c,FieldKind.Instrument,"Ins");if(v.volume)AddField(header,ti,c,FieldKind.Volume,"Vol");if(v.pan)AddField(header,ti,c,FieldKind.Pan,"Pan");if(v.delay)AddField(header,ti,c,FieldKind.Delay,"Dly");if(v.sampleFx){AddField(header,ti,c,FieldKind.LocalId,"FX");AddField(header,ti,c,FieldKind.LocalValue,"Val");}}
                for(int c=0;c<t.visibleEffectColumns;c++){AddField(header,ti,c,FieldKind.EffectId,"FX"+(c+1));AddField(header,ti,c,FieldKind.EffectValue,"Val");}
            }
            sub=Mathf.Clamp(sub,0,Math.Max(0,fields.Count-1));row=Mathf.Clamp(row,0,Pattern.lineCount-1);float width=34;foreach(var f in fields){fieldX.Add(width);width+=Width(f.kind)+3;}
            gridCanvas=new VisualElement();gridCanvas.style.width=width;gridCanvas.style.height=(Pattern.lineCount+1)*22;gridCanvas.style.flexShrink=0;grid.Add(gridCanvas);
            grid.verticalScroller.valueChanged+=v=>BuildVisibleCells();grid.horizontalScroller.valueChanged+=v=>BuildVisibleCells();grid.RegisterCallback<GeometryChangedEvent>(e=>BuildVisibleCells());BuildVisibleCells();
            cellDetails=Z.BoxKeyed("Cell","Parameters for the selected field.","tracker.model.cell");cellDetails.style.minHeight=120;controls.Add(cellDetails);RefreshCellDetails();
            RefreshCells();grid.schedule.Execute(()=>grid.scrollOffset=offset);
        }
        void AddField(VisualElement header,int t,int c,FieldKind kind,string title){fields.Add(new FieldSlot(t,c,kind,title));}
        void BuildVisibleCells()
        {
            if(gridCanvas==null||Pattern==null)return;gridCanvas.Clear();labels.Clear();var offset=grid.scrollOffset;float width=Math.Max(200,grid.contentViewport.layout.width),height=Math.Max(150,grid.contentViewport.layout.height);
            int start=Mathf.Clamp((int)(offset.y/22)-2,0,Pattern.lineCount-1),end=Mathf.Min(Pattern.lineCount-1,start+(int)(height/22)+5);
            var visible=new List<int>();for(int c=0;c<fields.Count;c++)if(fieldX[c]+Width(fields[c].kind)>=offset.x-80&&fieldX[c]<=offset.x+width+80)visible.Add(c);
            for(int r=start;r<=end;r++){var number=RowLabel(r.ToString("X3"));Place(number,0,(r+1)*22,34);gridCanvas.Add(number);foreach(int c in visible){int rr=r,cc=c;var f=fields[c];var label=Z.Text("",tooltip:FieldTooltip(f));label.name=$"cell-{r}-{c}";label.AddToClassList("tracker-cell");Place(label,fieldX[c],(r+1)*22,Width(f.kind));label.userData=new Vector2Int(r,c);label.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;Select(rr,cc,e.shiftKey);grid.Focus();e.StopPropagation();});labels.Add(new Vector2Int(r,c),label);gridCanvas.Add(label);}}
            foreach(int c in visible){var l=Z.Text(fields[c].title,tooltip:FieldTooltip(fields[c]));l.AddToClassList("tracker-cell");Place(l,fieldX[c],offset.y,Width(fields[c].kind));l.style.textOverflow=TextOverflow.Ellipsis;l.style.backgroundColor=new Color(.14f,.14f,.14f,1);gridCanvas.Add(l);}
            RefreshCells();
        }
        static void Place(VisualElement element,float x,float y,float width){element.style.position=Position.Absolute;element.style.left=x;element.style.top=y;element.style.width=width;element.style.minWidth=width;element.style.height=22;}
        static float Width(FieldKind k)=>k==FieldKind.Event?150:k==FieldKind.Note?76:40;
        static VisualElement GridRow(){var row=Z.Row();row.AddToClassList("tracker-grid-row");return row;}
        static Label RowLabel(string text){var l=Z.Text(text,tooltip:"Pattern line number in hexadecimal.");l.AddToClassList("tracker-row-number");return l;}
        string FieldTooltip(FieldSlot f)=>$"{Data.tracks[f.track].name}, column {f.column+1}, {f.kind}. "+(f.kind==FieldKind.Note?"QWERTY piano; Backspace writes OFF; Delete clears; Shift+arrows selects a block.":f.kind==FieldKind.EffectId||f.kind==FieldKind.LocalId?"Two-character command identifier; hex argument is in the next field.":f.kind==FieldKind.Volume||f.kind==FieldKind.Pan?"Hex 00–80 or a two-character column command; Delete restores empty.":f.kind==FieldKind.Event?"Choose this cell and edit its payload below the grid.":"Hex entry; Delete restores empty. Empty differs from zero.");
        PatternTrack PatternTrack(int ti,bool create=false){var t=Pattern.tracks.Find(x=>x.trackId==Data.tracks[ti].id);if(t==null&&create){t=new PatternTrack{trackId=Data.tracks[ti].id};Pattern.tracks.Add(t);}return t;}
        PatternLine Read(int r,FieldSlot f)=>PatternTrack(f.track)?.ReadLine(r)??new PatternLine{line=r};
        NoteCell Note(int r,FieldSlot f)=>Read(r,f).notes.Find(n=>n.column==f.column)??new NoteCell{column=f.column};
        EffectCell Effect(int r,FieldSlot f)=>Read(r,f).effects.Find(n=>n.column==f.column)??new EffectCell{column=f.column};
        EventCell Event(int r,FieldSlot f)=>Read(r,f).events.Find(n=>n.column==f.column)??new EventCell{column=f.column};
        string Text(int r,FieldSlot f){var n=Note(r,f);switch(f.kind){case FieldKind.Note:return n.note==NoteKind.Note?noteNames[Mathf.Clamp(n.pitch,0,119)%12]+(n.pitch/12):n.note==NoteKind.Off?"OFF":n.note==NoteKind.Legacy?"RAW":"---";case FieldKind.Instrument:return n.instrumentPresent?n.instrument.ToString("X2"):"..";case FieldKind.Volume:return ValueText(n.volume);case FieldKind.Pan:return ValueText(n.pan);case FieldKind.Delay:return n.delayPresent?n.delay.ToString("X2"):"..";case FieldKind.LocalId:return n.sampleFx.present?n.sampleFx.identifier:"..";case FieldKind.LocalValue:return n.sampleFx.valuePresent?n.sampleFx.value.ToString("X2"):"..";case FieldKind.EffectId:return Effect(r,f).command.present?Effect(r,f).command.identifier:"..";case FieldKind.EffectValue:return Effect(r,f).command.valuePresent?Effect(r,f).command.value.ToString("X2"):"..";default:var e=Event(r,f);return e.present?e.payload:"·";}}
        static string ValueText(ColumnValue v)=>v.kind==ValueKind.Value?v.value.ToString("X2"):v.kind==ValueKind.Command?v.command.identifier:v.kind==ValueKind.Legacy?"RAW":"..";
        void RefreshCells(){if(Pattern==null)return;foreach(var pair in labels){var p=pair.Key;if(p.y>=fields.Count||p.x>=Pattern.lineCount)continue;pair.Value.text=Text(p.x,fields[p.y]);pair.Value.EnableInClassList("tracker-picked",p.x==row&&p.y==sub);pair.Value.EnableInClassList("tracker-playing",p.x==playingRow);pair.Value.EnableInClassList("tracker-selected",anchorRow>=0&&p.x>=Math.Min(row,anchorRow)&&p.x<=Math.Max(row,anchorRow)&&p.y>=Math.Min(sub,anchorSub)&&p.y<=Math.Max(sub,anchorSub));}RefreshHelp();}
        void Select(int r,int c,bool extend){if(extend){if(anchorRow<0){anchorRow=row;anchorSub=sub;}}else anchorRow=-1;row=Mathf.Clamp(r,0,Pattern.lineCount-1);sub=Mathf.Clamp(c,0,Math.Max(0,fields.Count-1));if(fields.Count>0)track=fields[sub].track;hexDigit=0;pendingId="";EnsureCursorVisible();RefreshCells();RefreshCellDetails();}
        void EnsureCursorVisible(){if(grid==null||fields.Count==0)return;var offset=grid.scrollOffset;float x=fieldX[sub],y=(row+1)*22,width=grid.contentViewport.layout.width,height=grid.contentViewport.layout.height;if(width<=0||height<=0)return;if(x<offset.x)offset.x=x;if(x+Width(fields[sub].kind)>offset.x+width)offset.x=x+Width(fields[sub].kind)-width;if(y<offset.y)offset.y=y;if(y+22>offset.y+height)offset.y=y+22-height;grid.scrollOffset=offset;}
        void HighlightPlaying(int r){playingRow=r;foreach(var pair in labels)pair.Value.EnableInClassList("tracker-playing",pair.Key.x==r);if(pane==0&&grid!=null){float y=(r+1)*22;var offset=grid.scrollOffset;if(y<offset.y||y+22>offset.y+grid.contentViewport.layout.height)grid.scrollOffset=new Vector2(offset.x,y);}}
        void EditField(int r,FieldSlot f,Action<NoteCell,EffectCell,EventCell> edit)
        {
            var pt=PatternTrack(f.track,true);var line=Clone(pt.ReadLine(r));var n=line.notes.Find(x=>x.column==f.column);if(n==null)n=new NoteCell{column=f.column};var e=line.effects.Find(x=>x.column==f.column);if(e==null)e=new EffectCell{column=f.column};var evt=line.events.Find(x=>x.column==f.column);if(evt==null)evt=new EventCell{column=f.column};edit(n,e,evt);
            line.notes.RemoveAll(x=>x.column==f.column);if(n.HasPayload)line.notes.Add(n);line.effects.RemoveAll(x=>x.column==f.column);if(e.command.HasPayload)line.effects.Add(e);line.events.RemoveAll(x=>x.column==f.column);if(evt.present)line.events.Add(evt);pt.WriteLine(line);
        }
        void Advance(){row=Mathf.Min(Pattern.lineCount-1,row+step);hexDigit=0;pendingId="";EnsureCursorVisible();RefreshCells();RefreshCellDetails();}
        void KeyDown(KeyDownEvent e)
        {
            if(Pattern==null||fields.Count==0)return;bool handled=true,command=e.ctrlKey||e.commandKey;
            if(command){switch(e.keyCode){case KeyCode.C:CopyBlock();break;case KeyCode.V:PasteBlock();break;case KeyCode.X:CopyBlock();ClearBlock();break;case KeyCode.A:anchorRow=0;anchorSub=0;row=Pattern.lineCount-1;sub=fields.Count-1;RefreshCells();break;case KeyCode.Z:if(e.shiftKey)Undo.PerformRedo();else Undo.PerformUndo();break;case KeyCode.Y:Undo.PerformRedo();break;default:handled=false;break;}}
            else switch(e.keyCode){
                case KeyCode.Space:if(playing)StopPreview();else Play(false);break;
                case KeyCode.Return:Play(true);break;
                case KeyCode.Escape:anchorRow=-1;RefreshCells();break;
                case KeyCode.LeftArrow:Select(row,sub-1,e.shiftKey);break;case KeyCode.RightArrow:Select(row,sub+1,e.shiftKey);break;case KeyCode.UpArrow:Select(row-1,sub,e.shiftKey);break;case KeyCode.DownArrow:Select(row+1,sub,e.shiftKey);break;
                case KeyCode.Home:Select(0,sub,e.shiftKey);break;case KeyCode.End:Select(Pattern.lineCount-1,sub,e.shiftKey);break;case KeyCode.PageUp:Select(row-16,sub,e.shiftKey);break;case KeyCode.PageDown:Select(row+16,sub,e.shiftKey);break;
                case KeyCode.Tab:Select(row,sub+(e.shiftKey?-1:1),false);break;
                case KeyCode.Insert:if(editMode)SongEdit("insert line",()=>ZTrackerPatternOperations.InsertLine(Pattern,row),true);break;
                case KeyCode.Delete:if(editMode){if(e.shiftKey)SongEdit("delete line",()=>ZTrackerPatternOperations.DeleteLine(Pattern,row),true);else ClearBlock();}break;
                case KeyCode.Backspace:if(editMode&&fields[sub].kind==FieldKind.Note){var f=fields[sub];SongEdit("note off",()=>EditField(row,f,(n,x,v)=>n.note=NoteKind.Off));Advance();}break;
                default:
                    var slot=fields[sub];int piano=PianoOffset(e.keyCode);
                    if(slot.kind==FieldKind.Note&&piano>=0){int pitch=Mathf.Clamp(octave*12+piano,0,119);Audition(pitch);if(editMode){SongEdit("enter note",()=>EditField(row,slot,(n,x,v)=>{n.note=NoteKind.Note;n.pitch=pitch;n.instrumentPresent=Data.instruments.Count>0;n.instrument=entryInstrument;}));Advance();}}
                    else if(editMode&&e.character!=0)handled=EnterCharacter(char.ToUpperInvariant(e.character));else handled=false;break;
            }
            if(handled){e.StopPropagation();e.PreventDefault();}
        }
        internal static int PianoOffset(KeyCode key)
        {
            switch(key){case KeyCode.Z:return 0;case KeyCode.S:return 1;case KeyCode.X:return 2;case KeyCode.D:return 3;case KeyCode.C:return 4;case KeyCode.V:return 5;case KeyCode.G:return 6;case KeyCode.B:return 7;case KeyCode.H:return 8;case KeyCode.N:return 9;case KeyCode.J:return 10;case KeyCode.M:return 11;case KeyCode.Q:return 12;case KeyCode.Alpha2:return 13;case KeyCode.W:return 14;case KeyCode.Alpha3:return 15;case KeyCode.E:return 16;case KeyCode.R:return 17;case KeyCode.Alpha5:return 18;case KeyCode.T:return 19;case KeyCode.Alpha6:return 20;case KeyCode.Y:return 21;case KeyCode.Alpha7:return 22;case KeyCode.U:return 23;case KeyCode.I:return 24;default:return -1;}
        }
        bool EnterCharacter(char ch)
        {
            var f=fields[sub];if(f.kind==FieldKind.Event||f.kind==FieldKind.Note)return false;
            int nibble=ch>='0'&&ch<='9'?ch-'0':ch>='A'&&ch<='F'?ch-'A'+10:-1;
            bool id=f.kind==FieldKind.LocalId||f.kind==FieldKind.EffectId;
            bool column=f.kind==FieldKind.Volume||f.kind==FieldKind.Pan;
            if(id||column&&(pendingId!=""||nibble<0||hexDigit==0&&char.IsLetter(ch))){
                if(!char.IsLetterOrDigit(ch))return false;pendingId+=ch;
                if(pendingId.Length<2)return true;string entered=pendingId.Substring(0,2);SongEdit("enter command",()=>EditField(row,f,(n,x,v)=>{var c=new CommandData{present=true,identifier=entered,scope=id&&f.kind==FieldKind.EffectId?(entered[0]=='Z'?CommandScope.Global:CommandScope.Track):CommandScope.Column};if(id){var before=f.kind==FieldKind.LocalId?n.sampleFx:x.command;c.value=before.value;c.valuePresent=before.valuePresent;if(f.kind==FieldKind.LocalId)n.sampleFx=c;else x.command=c;}else{var value=f.kind==FieldKind.Volume?n.volume:n.pan;value.command=c;value.kind=ValueKind.Command;}}));Advance();return true;
            }
            if(nibble<0)return false;
            int previous=0;var note=Note(row,f);switch(f.kind){case FieldKind.Instrument:previous=note.instrument;break;case FieldKind.Volume:previous=note.volume.value;break;case FieldKind.Pan:previous=note.pan.value;break;case FieldKind.Delay:previous=note.delay;break;case FieldKind.LocalValue:previous=note.sampleFx.value;break;case FieldKind.EffectValue:previous=Effect(row,f).command.value;break;}
            int value=hexDigit==0?nibble<<4:(previous&0xF0)|nibble;int max=column?128:255;if(value>max){lastError="Volume and pan values must be 00–80";RefreshTransport();return true;}
            SongEdit("enter hex value",()=>EditField(row,f,(n,x,v)=>{switch(f.kind){case FieldKind.Instrument:n.instrumentPresent=true;n.instrument=value;break;case FieldKind.Volume:n.volume.kind=ValueKind.Value;n.volume.value=value;break;case FieldKind.Pan:n.pan.kind=ValueKind.Value;n.pan.value=value;break;case FieldKind.Delay:n.delayPresent=true;n.delay=value;break;case FieldKind.LocalValue:n.sampleFx.valuePresent=true;n.sampleFx.value=value;break;case FieldKind.EffectValue:x.command.valuePresent=true;x.command.value=value;break;}}));hexDigit++;if(hexDigit>=2)Advance();return true;
        }
        void ForBlock(Action<int,int> edit){int r0=anchorRow<0?row:Math.Min(row,anchorRow),r1=anchorRow<0?row:Math.Max(row,anchorRow),c0=anchorRow<0?sub:Math.Min(sub,anchorSub),c1=anchorRow<0?sub:Math.Max(sub,anchorSub);for(int r=r0;r<=r1;r++)for(int c=c0;c<=c1;c++)edit(r,c);}
        void CopyBlock(){clipboard.Clear();int r0=anchorRow<0?row:Math.Min(row,anchorRow),c0=anchorRow<0?sub:Math.Min(sub,anchorSub);ForBlock((r,c)=>{var f=fields[c];clipboard.Add(new CopiedField{r=r-r0,c=c-c0,kind=f.kind,note=Clone(Note(r,f)),effect=Clone(Effect(r,f)),evt=Clone(Event(r,f))});});}
        void PasteBlock(){if(clipboard.Count==0||fields.Count==0)return;SongEdit("paste block",()=>{foreach(var p in clipboard){int rr=row+p.r,cc=sub+p.c;if(rr>=Pattern.lineCount||cc>=fields.Count||fields[cc].kind!=p.kind)continue;var f=fields[cc];EditField(rr,f,(n,x,v)=>CopyField(p.kind,n,x,v,p.note,p.effect,p.evt));}});}
        static void CopyField(FieldKind kind,NoteCell n,EffectCell e,EventCell v,NoteCell source,EffectCell effect,EventCell evt)
        {
            switch(kind){case FieldKind.Note:n.note=source.note;n.pitch=source.pitch;n.legacyNote=source.legacyNote;n.parameterSetId=source.parameterSetId;n.legacyExtras=source.legacyExtras;break;
                case FieldKind.Instrument:n.instrumentPresent=source.instrumentPresent;n.instrument=source.instrument;break;case FieldKind.Volume:n.volume=Clone(source.volume);break;case FieldKind.Pan:n.pan=Clone(source.pan);break;case FieldKind.Delay:n.delayPresent=source.delayPresent;n.delay=source.delay;break;
                // An identifier carries its complete versioned command payload, including archived metadata.
                case FieldKind.LocalId:n.sampleFx=Clone(source.sampleFx);break;case FieldKind.LocalValue:n.sampleFx.valuePresent=source.sampleFx.valuePresent;n.sampleFx.value=source.sampleFx.value;n.sampleFx.rawValue=source.sampleFx.rawValue;break;
                case FieldKind.EffectId:e.command=Clone(effect.command);break;case FieldKind.EffectValue:e.command.valuePresent=effect.command.valuePresent;e.command.value=effect.command.value;e.command.rawValue=effect.command.rawValue;break;
                case FieldKind.Event:v.present=evt.present;v.payload=evt.payload;v.profile=evt.profile;v.provenance=evt.provenance;break;}
        }
        void ClearBlock(){if(fields.Count==0)return;SongEdit("clear block",()=>ForBlock((r,c)=>EditField(r,fields[c],(n,x,v)=>CopyField(fields[c].kind,n,x,v,new NoteCell(),new EffectCell(),new EventCell()))));}
        void Transpose(int amount){if(fields.Count==0)return;SongEdit("transpose block",()=>ForBlock((r,c)=>{if(fields[c].kind==FieldKind.Note)EditField(r,fields[c],(n,x,v)=>{if(n.note==NoteKind.Note)n.pitch=Mathf.Clamp(n.pitch+amount,0,119);});}));}
        string EventUnderCursor()=>fields.Count>0&&fields[sub].kind==FieldKind.Event?Event(row,fields[sub]).payload:"";
        void SetEvent(string payload){if(fields.Count==0||fields[sub].kind!=FieldKind.Event)return;var f=fields[sub];SongEdit("event payload",()=>EditField(row,f,(n,e,v)=>{v.present=!string.IsNullOrEmpty(payload);v.payload=payload;}));}
        void RefreshCellDetails()
        {
            if(cellDetails==null||fields.Count==0)return;Vector2 offset=controlScroll.scrollOffset;cellDetails.Clear();var f=fields[sub];
            if(f.kind==FieldKind.Event)cellDetails.Add(Z.Field("Event","Event payload at this row.",Named(Z.TextInput(EventUnderCursor(),"Enter this row's event payload; empty removes it.",SetEvent,210),"event-payload")));
            else if(f.kind==FieldKind.Note||f.kind==FieldKind.Instrument){var n=Note(row,f);int ii=n.instrumentPresent?n.instrument:entryInstrument;var asset=ii>=0&&ii<Data.instruments.Count?Data.instruments[ii]:null;
                if(asset!=null){var sets=ZTrackerMigration.ResolveParameterSets(asset.model);var names=new List<string>{"Base"};names.AddRange(asset.model.parameters.presets.Select(p=>p.name));int selected=sets.FindIndex(s=>s.id==n.parameterSetId);cellDetails.Add(Z.MiniRadio(Math.Max(0,selected),names.ToArray(),"Choose the instrument variation used by this note column.",v=>SongEdit("note variation",()=>EditField(row,f,(note,e,ev)=>note.parameterSetId=v==0?"":sets[v].id)),wrap:true));}}
            else if(f.kind==FieldKind.EffectId||f.kind==FieldKind.EffectValue||f.kind==FieldKind.LocalId||f.kind==FieldKind.LocalValue||f.kind==FieldKind.Volume||f.kind==FieldKind.Pan){var cmd=CommandAt(row,f);
                cellDetails.Add(Z.MiniRadio((int)cmd.scope,new[]{"Column","Track","Global","Unresolved"},"Choose the command's intended scope; unsupported placements remain flagged.",v=>SongEdit("command scope",()=>EditField(row,f,(n,e,ev)=>CommandOf(f,n,e).scope=(CommandScope)v)),wrap:true));
                var names=new List<string>{"Automatic"};names.AddRange(Enumerable.Range(0,Math.Max(0,Data.tracks[f.track].visibleNoteColumns)).Select(i=>"Column "+(i+1)));cellDetails.Add(Z.MiniRadio(cmd.targetColumn+1,names.ToArray(),"Select a note-column target, preserving its explicit identity.",v=>SongEdit("command column",()=>EditField(row,f,(n,e,ev)=>CommandOf(f,n,e).targetColumn=v-1)),wrap:true));}
            controlScroll.schedule.Execute(()=>controlScroll.scrollOffset=offset);
        }
        CommandData CommandAt(int r,FieldSlot f)=>CommandOf(f,Note(r,f),Effect(r,f));
        static CommandData CommandOf(FieldSlot f,NoteCell n,EffectCell e)=>f.kind==FieldKind.EffectId||f.kind==FieldKind.EffectValue?e.command:f.kind==FieldKind.Volume?n.volume.command:f.kind==FieldKind.Pan?n.pan.command:n.sampleFx;
        static string CommandPath()=>Path.GetFullPath("Assets/Packages/ZTracker/Documentation~/COMMANDS.md");
        static readonly Dictionary<string,string> commandHelp=new Dictionary<string,string>();
        void RefreshHelp()
        {
            if(help==null||fields.Count==0)return;if(commandHelp.Count==0&&File.Exists(CommandPath()))foreach(var line in File.ReadLines(CommandPath())){if(!line.StartsWith("|"))continue;var pieces=line.Split('|');if(pieces.Length<3)continue;foreach(var token in pieces[1].Split(new[]{'`',' ','/','(',')',','},StringSplitOptions.RemoveEmptyEntries))if(token.Length>=2&&char.IsLetterOrDigit(token[0])&&char.IsLetter(token[1])){string key=token.Substring(0,2).ToUpperInvariant();if(!commandHelp.ContainsKey(key))commandHelp[key]=line.Trim('|').Replace('`',' ');}}
            var f=fields[sub];string id=f.kind==FieldKind.EffectId||f.kind==FieldKind.EffectValue?Effect(row,f).command.identifier:f.kind==FieldKind.LocalId||f.kind==FieldKind.LocalValue?Note(row,f).sampleFx.identifier:f.kind==FieldKind.Volume?Note(row,f).volume.command.identifier:f.kind==FieldKind.Pan?Note(row,f).pan.command.identifier:"";
            help.text=string.IsNullOrEmpty(id)?"Command":id;help.tooltip=commandHelp.TryGetValue(id,out var meaning)?meaning:"No supported command selected. Unsupported commands remain stored and are flagged by playback.";
        }
    }
}
