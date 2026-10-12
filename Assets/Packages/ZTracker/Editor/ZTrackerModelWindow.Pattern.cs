using System;
using System.Collections.Generic;
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
        internal const int DefaultPatternOctave=5,DefaultPatternStep=1;
        internal enum FieldKind{Note,Instrument,Volume,Pan,Delay,LocalId,LocalValue,EffectId,EffectValue,Event}
        struct FieldSlot{public int track,column;public FieldKind kind;public string title;public FieldSlot(int t,int c,FieldKind k,string label=""){track=t;column=c;kind=k;title=label;}}
        sealed class CopiedField{public int r,c;public FieldKind kind;public NoteCell note;public EffectCell effect;public EventCell evt;}
        readonly List<FieldSlot> fields=new List<FieldSlot>();
        readonly List<float> fieldX=new List<float>();
        VisualElement gridCanvas;
        Font patternFont;
        int playingRow=-1;
        readonly Dictionary<Vector2Int,Label> labels=new Dictionary<Vector2Int,Label>();
        readonly List<CopiedField> clipboard=new List<CopiedField>();
        int anchorRow=-1,anchorSub,hexDigit;
        VisualElement cellDetails;
        string pendingId="";
        static readonly string[] noteNames={"C-","C#","D-","D#","E-","F-","F#","G-","G#","A-","A#","B-"};
        void BuildPattern()
        {
            if(stage==null)return;CloseFxSuggestions();Vector2 offset=grid?.scrollOffset??Vector2.zero;stage.Clear();fields.Clear();fieldX.Clear();labels.Clear();if(Pattern==null)return;
            // The pattern pane: a narrow tool column on the left, the grid filling the rest.
            var pane=new VisualElement{name="pattern-pane"};pane.style.flexDirection=FlexDirection.Row;pane.style.flexGrow=1;pane.style.minHeight=0;pane.style.minWidth=0;stage.Add(pane);
            var tools=new VisualElement{name="pattern-tools"};tools.AddToClassList("tracker-tools");tools.style.flexDirection=FlexDirection.Column;tools.style.flexShrink=0;tools.style.width=146;tools.style.marginRight=6;pane.Add(tools);
            var edit=Named(Z.IconButton("pencil","Toggle pattern Edit mode. When on, note keys write into the pattern; when off, they only audition.",()=>{editMode=!editMode;BuildPane();}),"edit-mode");edit.EnableInClassList("tracker-picked",editMode);edit.style.alignSelf=Align.FlexStart;tools.Add(edit);
            BuildPatternSettings(tools);
            tools.Add(Named(Z.ScrubNumber("Oct",octave,0,9,"Octave used by the QWERTY piano. Drag, click to type or double-click to reset.",v=>octave=v,width:115,defaultValue:DefaultPatternOctave),"octave"));
            tools.Add(Named(Z.ScrubNumber("Step",step,0,32,"Rows advanced after entering a note or field; Tab uses the same step.",v=>step=v,width:115,defaultValue:DefaultPatternStep),"edit-step"));
            tools.Add(ToolRow(Named(Z.IconButton("copy","Copy the selected rectangle of visible fields (Ctrl+C; Ctrl+X cuts).",CopyBlock),"copy"),Named(Z.IconButton("clipboard","Paste copied fields at the cursor, overwriting what is there (Ctrl+V).",PasteBlock),"paste"),Named(Z.IconButton("rows-plus-top","Paste Insert: push the rows a paste would overwrite down by the copied height in the pasted columns, then paste into the gap. Rows pushed past the pattern end are dropped (Ctrl+Shift+V).",PasteInsertBlock),"paste-insert")));
            tools.Add(ToolRow(Named(Z.IconButton("list-plus","Insert a blank line at the cursor; Undo restores the shifted notes and automation.",()=>SongEdit("insert line",()=>ZTrackerPatternOperations.InsertLine(Pattern,row),true)),"insert-line"),Named(Z.IconButton("trash","Delete the line at the cursor and shift notes and automation upward.",()=>SongEdit("delete line",()=>ZTrackerPatternOperations.DeleteLine(Pattern,row),true)),"delete-line")));
            tools.Add(ToolRow(Button("−12","Transpose selected notes down an octave.",()=>Transpose(-12)),Button("+12","Transpose selected notes up an octave.",()=>Transpose(12))));
            tools.Add(ToolRow(Button("−1","Transpose selected notes down a semitone.",()=>Transpose(-1)),Button("+1","Transpose selected notes up a semitone.",()=>Transpose(1))));
            grid=new ScrollView(ScrollViewMode.VerticalAndHorizontal);grid.name="pattern-grid";grid.focusable=true;grid.style.flexGrow=1;grid.style.minHeight=60;grid.style.minWidth=0;
            grid.RegisterCallback<KeyDownEvent>(KeyDown);grid.RegisterCallback<KeyUpEvent>(e=>{if(PianoOffset(e.keyCode)>=0)AuditionOff();});grid.RegisterCallback<PointerDownEvent>(e=>grid.Focus());pane.Add(grid);
            // Mouse block selection: a press on a cell anchors, dragging extends to the cell under the pointer.
            grid.RegisterCallback<PointerMoveEvent>(e=>{if(!grid.HasPointerCapture(e.pointerId)||(e.pressedButtons&1)==0)return;var at=CellAt(e.position);if(at.x>=0&&(at.x!=row||at.y!=sub))Select(at.x,at.y,true);e.StopPropagation();});
            grid.RegisterCallback<PointerUpEvent>(e=>{if(grid.HasPointerCapture(e.pointerId))grid.ReleasePointer(e.pointerId);});
            var header=GridRow();header.Add(RowLabel("Row"));
            for(int ti=0;ti<Data.tracks.Count;ti++){
                var t=Data.tracks[ti];if(t.kind==TrackKind.Event){AddField(header,ti,0,FieldKind.Event,"Event");}
                if(t.kind==TrackKind.Sequencer)for(int c=0;c<t.visibleNoteColumns;c++){
                    var v=c<t.columns.Count?t.columns[c]:new ColumnVisibility();AddField(header,ti,c,FieldKind.Note,t.visibleNoteColumns>1?(c+1).ToString():"N");AddField(header,ti,c,FieldKind.Instrument,"I");if(v.volume)AddField(header,ti,c,FieldKind.Volume,"V");if(v.pan)AddField(header,ti,c,FieldKind.Pan,"P");if(v.delay)AddField(header,ti,c,FieldKind.Delay,"D");if(v.sampleFx){AddField(header,ti,c,FieldKind.LocalId,"FX");AddField(header,ti,c,FieldKind.LocalValue,"");}}
                for(int c=0;c<t.visibleEffectColumns;c++){AddField(header,ti,c,FieldKind.EffectId,"FX"+(c+1));AddField(header,ti,c,FieldKind.EffectValue,"");}
            }
            sub=Mathf.Clamp(sub,0,Math.Max(0,fields.Count-1));row=Mathf.Clamp(row,0,Pattern.lineCount-1);float width=28;for(int i=0;i<fields.Count;i++){var f=fields[i];if(i>0&&f.track!=fields[i-1].track)width+=8;else if(i>0&&f.kind==FieldKind.Note)width+=4;fieldX.Add(width);width+=Width(f.kind)+(f.kind==FieldKind.LocalId||f.kind==FieldKind.EffectId?0:1);}
            gridCanvas=new VisualElement();gridCanvas.name="pattern-grid-canvas";gridCanvas.style.width=width;gridCanvas.style.height=(Pattern.lineCount+2)*20;gridCanvas.style.flexShrink=0;grid.Add(gridCanvas);
            grid.verticalScroller.valueChanged+=v=>BuildVisibleCells();grid.horizontalScroller.valueChanged+=v=>BuildVisibleCells();grid.RegisterCallback<GeometryChangedEvent>(e=>BuildVisibleCells());BuildVisibleCells();
            cellDetails=Z.BoxKeyed("Cell","Parameters for the selected field.","tracker.model.cell");controls.Add(cellDetails);RefreshCellDetails();
            RefreshCells();var built=grid;grid.schedule.Execute(()=>{built.scrollOffset=offset;
                // A rebuild replaces the grid, which drops keyboard focus; hand it back unless the user is in another control.
                var focused=built.focusController?.focusedElement as VisualElement;if(focused==null||focused.panel==null)built.Focus();});
        }
        void AddField(VisualElement header,int t,int c,FieldKind kind,string title){fields.Add(new FieldSlot(t,c,kind,title));}
        // Per visible track: the grid elements drawn for it, which travel with its header while it is dragged.
        readonly Dictionary<int,List<VisualElement>> trackColumns=new Dictionary<int,List<VisualElement>>();
        readonly List<int> headerTracks=new List<int>();
        ZuiReorderList headerOrder;
        List<VisualElement> Column(int trackIndex){if(!trackColumns.TryGetValue(trackIndex,out var list))trackColumns[trackIndex]=list=new List<VisualElement>();return list;}
        void BuildVisibleCells()
        {
            if(patternFont==null){patternFont=Font.CreateDynamicFontFromOSFont("Consolas",11);patternFont.hideFlags=HideFlags.HideAndDontSave;}
            if(gridCanvas==null||Pattern==null)return;gridCanvas.Clear();labels.Clear();trackColumns.Clear();headerTracks.Clear();
            // Track headers drag sideways to reorder; the whole visible column slides with its header.
            headerOrder=new ZuiReorderList(ZuiReorderLayout.Horizontal,(a,b)=>ReorderTracks(headerTracks[a],headerTracks[b]));
            var offset=grid.scrollOffset;float width=Math.Max(200,grid.contentViewport.layout.width),height=Math.Max(150,grid.contentViewport.layout.height);
            int start=Mathf.Clamp((int)(offset.y/20)-2,0,Pattern.lineCount-1),end=Mathf.Min(Pattern.lineCount-1,start+(int)(height/20)+5);
            var visible=new List<int>();for(int c=0;c<fields.Count;c++)if(fieldX[c]+Width(fields[c].kind)>=offset.x-80&&fieldX[c]<=offset.x+width+80)visible.Add(c);
            for(int r=start;r<=end;r++){var number=RowLabel(r.ToString());Place(number,0,(r+2)*20,28);gridCanvas.Add(number);foreach(int c in visible){int rr=r,cc=c;var f=fields[c];var label=Z.Text("",tooltip:FieldTooltip(f));label.name=$"cell-{r}-{c}";label.AddToClassList("tracker-cell");label.EnableInClassList("tracker-beat",r%Math.Max(1,Data.linesPerBeat)==0);Place(label,fieldX[c],(r+2)*20,Width(f.kind));label.userData=new Vector2Int(r,c);label.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;Select(rr,cc,e.shiftKey);grid.Focus();grid.CapturePointer(e.pointerId);e.StopPropagation();});labels.Add(new Vector2Int(r,c),label);gridCanvas.Add(label);Column(f.track).Add(label);}}
            var headerBacking=new VisualElement{pickingMode=PickingMode.Ignore};Place(headerBacking,0,offset.y,gridCanvas.style.width.value.value);headerBacking.style.height=40;headerBacking.style.backgroundColor=new Color(.1f,.13f,.17f,1);gridCanvas.Add(headerBacking);
            foreach(int c in visible){var f=fields[c];if(f.kind==FieldKind.EffectValue||f.kind==FieldKind.LocalValue)continue;var l=Z.Text(f.title,tooltip:FieldTooltip(f));l.AddToClassList("tracker-cell");float w=Width(f.kind)+(f.kind==FieldKind.EffectId||f.kind==FieldKind.LocalId?Width(FieldKind.EffectValue):0);Place(l,fieldX[c],offset.y+20,w);l.style.textOverflow=TextOverflow.Ellipsis;l.style.backgroundColor=new Color(.14f,.14f,.14f,1);gridCanvas.Add(l);Column(f.track).Add(l);}
            for(int i=0;i<fields.Count;i++){if(i>0&&fields[i].track==fields[i-1].track)continue;int endField=i;while(endField+1<fields.Count&&fields[endField+1].track==fields[i].track)endField++;float x=fieldX[i],w=fieldX[endField]+Width(fields[endField].kind)-x;int at=fields[i].track,first=i;var t=Data.tracks[at];var title=Z.Text(t.name,tooltip:"Select this track; drag it sideways to reorder the tracks.");title.name="pattern-track-header-"+at;title.AddToClassList("tracker-track-header");Place(title,x,offset.y,w);title.style.backgroundColor=new Color(t.color.r*.65f+.035f,t.color.g*.65f+.0455f,t.color.b*.65f+.0595f,1);title.RegisterCallback<PointerDownEvent>(e=>{if(e.button==0){track=at;sub=first;anchorRow=-1;grid.Focus();RefreshPatternTrackSettings();RefreshCells();RefreshCellDetails();}});var separator=new VisualElement{pickingMode=PickingMode.Ignore};Place(separator,x-3,offset.y+20,1);separator.style.height=height;separator.style.backgroundColor=t.color;gridCanvas.Add(separator);gridCanvas.Add(title);var column=Column(at);column.Add(separator);headerTracks.Add(at);headerOrder.Add(title,followers:()=>column);}
            foreach(var label in gridCanvas.Query<Label>().ToList())label.style.unityFontDefinition=FontDefinition.FromFont(patternFont);
            RefreshCells();
        }
        /// Row and field under a panel position in the grid (clamped to the pattern), or (-1,-1) without a grid.
        Vector2Int CellAt(Vector2 world)
        {
            if(gridCanvas==null||fields.Count==0)return new Vector2Int(-1,-1);var p=gridCanvas.WorldToLocal(world);
            int r=Mathf.Clamp(Mathf.FloorToInt(p.y/20)-2,0,Pattern.lineCount-1),c=0;
            for(int i=0;i<fields.Count;i++)if(p.x>=fieldX[i])c=i;
            return new Vector2Int(r,c);
        }
        static void Place(VisualElement element,float x,float y,float width){element.style.position=Position.Absolute;element.style.left=x;element.style.top=y;element.style.width=width;element.style.minWidth=width;element.style.height=20;}
        static float Width(FieldKind k)=>k==FieldKind.Event?120:k==FieldKind.Note?23:16;
        static VisualElement ToolRow(params VisualElement[] children){var r=new VisualElement();r.style.flexDirection=FlexDirection.Row;r.style.flexShrink=0;r.style.marginTop=2;foreach(var c in children){c.style.marginRight=2;r.Add(c);}return r;}
        static VisualElement GridRow(){var row=Z.Row();row.AddToClassList("tracker-grid-row");return row;}
        static Label RowLabel(string text){var l=Z.Text(text,tooltip:"Pattern line number.");l.AddToClassList("tracker-row-number");return l;}
        string FieldTooltip(FieldSlot f)=>$"{Data.tracks[f.track].name}, column {f.column+1}, {FieldName(f.kind)}. "+(f.kind==FieldKind.Note?"QWERTY piano; Backspace writes OFF; Delete clears the note and its instrument and steps down; Shift+arrows, Shift+click or dragging across cells selects a block.":f.kind==FieldKind.EffectId||f.kind==FieldKind.LocalId?"Type the first command character to see supported options and their argument ranges. The hex argument is in the next field; imported unsupported commands remain stored.":f.kind==FieldKind.Volume||f.kind==FieldKind.Pan?"Hex 00–80 or a two-character column command; Delete restores empty.":f.kind==FieldKind.Event?"Choose this cell and edit its payload in the Cell controls.":"Hex entry; Delete restores empty. Empty differs from zero.");
        static string FieldName(FieldKind kind){switch(kind){case FieldKind.LocalId:return "Sample effect";case FieldKind.LocalValue:return "Sample effect value";case FieldKind.EffectId:return "Track effect";case FieldKind.EffectValue:return "Track effect value";default:return kind.ToString();}}
        PatternTrack PatternTrack(int ti,bool create=false){var t=Pattern.tracks.Find(x=>x.trackId==Data.tracks[ti].id);if(t==null&&create){t=new PatternTrack{trackId=Data.tracks[ti].id};Pattern.tracks.Add(t);}return t;}
        PatternLine Read(int r,FieldSlot f)=>PatternTrack(f.track)?.ReadLine(r)??new PatternLine{line=r};
        NoteCell Note(int r,FieldSlot f)=>Read(r,f).notes.Find(n=>n.column==f.column)??new NoteCell{column=f.column};
        EffectCell Effect(int r,FieldSlot f)=>Read(r,f).effects.Find(n=>n.column==f.column)??new EffectCell{column=f.column};
        EventCell Event(int r,FieldSlot f)=>Read(r,f).events.Find(n=>n.column==f.column)??new EventCell{column=f.column};
        string Text(int r,FieldSlot f){var n=Note(r,f);switch(f.kind){case FieldKind.Note:return n.note==NoteKind.Note?noteNames[Mathf.Clamp(n.pitch,0,119)%12]+(n.pitch/12):n.note==NoteKind.Off?"OFF":n.note==NoteKind.Legacy?"RAW":"---";case FieldKind.Instrument:return n.instrumentPresent?n.instrument.ToString("X2"):"..";case FieldKind.Volume:return ValueText(n.volume);case FieldKind.Pan:return ValueText(n.pan);case FieldKind.Delay:return n.delayPresent?n.delay.ToString("X2"):"..";case FieldKind.LocalId:return n.sampleFx.present?n.sampleFx.identifier:"..";case FieldKind.LocalValue:return n.sampleFx.valuePresent?n.sampleFx.value.ToString("X2"):"..";case FieldKind.EffectId:return Effect(r,f).command.present?Effect(r,f).command.identifier:"..";case FieldKind.EffectValue:return Effect(r,f).command.valuePresent?Effect(r,f).command.value.ToString("X2"):"..";default:var e=Event(r,f);return e.present?e.payload:"·";}}
        static string ValueText(ColumnValue v)=>v.kind==ValueKind.Value?v.value.ToString("X2"):v.kind==ValueKind.Command?v.command.identifier:v.kind==ValueKind.Legacy?"RAW":"..";
        void RefreshCells(){if(Pattern==null)return;foreach(var pair in labels){var p=pair.Key;if(p.y>=fields.Count||p.x>=Pattern.lineCount)continue;RefreshNoteDisplay(pair.Value,p.x,fields[p.y]);pair.Value.EnableInClassList("tracker-picked",p.x==row&&p.y==sub);pair.Value.EnableInClassList("tracker-playing",p.x==playingRow);pair.Value.EnableInClassList("tracker-selected",anchorRow>=0&&p.x>=Math.Min(row,anchorRow)&&p.x<=Math.Max(row,anchorRow)&&p.y>=Math.Min(sub,anchorSub)&&p.y<=Math.Max(sub,anchorSub));}RefreshHelp();}
        internal DrumMapping ResolveDrumMapping(int line,int trackIndex,int column,int pitch)
        {
            if(Pattern==null||trackIndex<0||trackIndex>=Data.tracks.Count)return null;
            int instrument=-1;
            string trackId=Data.tracks[trackIndex].id;
            int selectedOrder=Mathf.Clamp(order,0,Data.sequence.Count-1);
            // Instrument memory belongs to a column and survives ordinary pattern boundaries.
            // Search the selected sequence occurrence, not just the reusable pattern asset.
            for(int slot=selectedOrder;slot>=0&&instrument<0;slot--)
            {
                var pattern=Data.patterns.Find(p=>p.id==Data.sequence[slot].patternId);
                var records=pattern?.tracks.Find(t=>t.trackId==trackId);
                if(records==null)continue;
                int latest=-1,limit=slot==selectedOrder?Math.Min(line,pattern.lineCount-1):pattern.lineCount-1;
                foreach(var entry in records.lines)
                {
                    if(entry==null||entry.line<0||entry.line>limit||entry.line<latest)continue;
                    var note=entry.notes?.Find(n=>n!=null&&!n.inactive&&n.column==column&&n.instrumentPresent);
                    if(note==null||note.instrument<0||note.instrument>=Data.instruments.Count||Data.instruments[note.instrument]==null)continue;
                    latest=entry.line;instrument=note.instrument;
                }
            }
            if(instrument<0||instrument>=Data.instruments.Count)return null;
            var data=ReadInstrument(Data.instruments[instrument]);
            return data?.family==InstrumentFamily.DrumKit?data.drumKit?.Find(pitch):null;
        }
        void RefreshNoteDisplay(Label label,int line,FieldSlot field)
        {
            label.text=Text(line,field);label.tooltip=FieldTooltip(field);
            var previous=label.Q<VisualElement>("drum-icon");
            var note=Note(line,field);
            bool drum=field.kind==FieldKind.Note&&Data.tracks[field.track].drumTrack&&note.note==NoteKind.Note;
            if(!drum){previous?.RemoveFromHierarchy();return;}
            var mapping=ResolveDrumMapping(line,field.track,field.column,note.pitch);
            if(mapping==null)
            {
                previous?.RemoveFromHierarchy();label.text="?";
                label.tooltip=$"{Text(line,field)}: no drum sound mapped for this key in this column's instrument. "+FieldTooltip(field);return;
            }
            string category=DrumTags.Category(mapping.tag).ToString();
            if(previous==null||previous.userData as string!=category)
            {
                previous?.RemoveFromHierarchy();previous=ZTrackerDrumIcons.Create(mapping.tag,18);previous.userData=category;
                previous.style.position=Position.Absolute;previous.style.left=2;previous.style.top=1;label.Add(previous);
            }
            label.text="";label.tooltip=$"{mapping.name} ({DrumTags.Canonical(mapping.tag)}) · {Text(line,field)}. "+FieldTooltip(field);
        }
        void Select(int r,int c,bool extend){patternNotice="";if(extend){if(anchorRow<0){anchorRow=row;anchorSub=sub;}}else anchorRow=-1;row=Mathf.Clamp(r,0,Pattern.lineCount-1);sub=Mathf.Clamp(c,0,Math.Max(0,fields.Count-1));if(fields.Count>0)track=fields[sub].track;hexDigit=0;pendingId="";CloseFxSuggestions();EnsureCursorVisible();RefreshCells();RefreshCellDetails();RefreshPatternTrackSettings();}
        void EnsureCursorVisible(){if(grid==null||fields.Count==0)return;var offset=grid.scrollOffset;float x=fieldX[sub],y=(row+2)*20,width=grid.contentViewport.layout.width,height=grid.contentViewport.layout.height;if(width<=0||height<=0)return;if(x<offset.x)offset.x=x;if(x+Width(fields[sub].kind)>offset.x+width)offset.x=x+Width(fields[sub].kind)-width;if(y<offset.y+40)offset.y=Mathf.Max(0,y-40);if(y+20>offset.y+height)offset.y=y+20-height;grid.scrollOffset=offset;}
        void HighlightPlaying(int r){playingRow=r;foreach(var pair in labels)pair.Value.EnableInClassList("tracker-playing",pair.Key.x==r);if(pane==0&&grid!=null){float y=(r+2)*20;var offset=grid.scrollOffset;if(y<offset.y+40)offset.y=Mathf.Max(0,y-40);else if(y+20>offset.y+grid.contentViewport.layout.height)offset.y=y+20-grid.contentViewport.layout.height;grid.scrollOffset=offset;}}
        void EditField(int r,FieldSlot f,Action<NoteCell,EffectCell,EventCell> edit)
        {
            var pt=PatternTrack(f.track,true);var line=Clone(pt.ReadLine(r));var n=line.notes.Find(x=>x.column==f.column);if(n==null)n=new NoteCell{column=f.column};var e=line.effects.Find(x=>x.column==f.column);if(e==null)e=new EffectCell{column=f.column};var evt=line.events.Find(x=>x.column==f.column);if(evt==null)evt=new EventCell{column=f.column};edit(n,e,evt);
            line.notes.RemoveAll(x=>x.column==f.column);if(n.HasPayload)line.notes.Add(n);line.effects.RemoveAll(x=>x.column==f.column);if(e.command.HasPayload)line.effects.Add(e);line.events.RemoveAll(x=>x.column==f.column);if(evt.present)line.events.Add(evt);pt.WriteLine(line);
        }
        void Advance(){row=Mathf.Min(Pattern.lineCount-1,row+step);hexDigit=0;pendingId="";EnsureCursorVisible();RefreshCells();RefreshCellDetails();}
        void KeyDown(KeyDownEvent e)
        {
            if(Pattern==null||fields.Count==0)return;bool handled=true,command=e.ctrlKey||e.commandKey;
            if(!command&&HandlePatternShortcut(e)){e.StopPropagation();e.PreventDefault();return;}
            if(command){switch(e.keyCode){case KeyCode.C:CopyBlock();break;case KeyCode.V:if(e.shiftKey)PasteInsertBlock();else PasteBlock();break;case KeyCode.X:CopyBlock();ClearBlock();break;case KeyCode.A:anchorRow=0;anchorSub=0;row=Pattern.lineCount-1;sub=fields.Count-1;RefreshCells();break;case KeyCode.Z:if(e.shiftKey)Undo.PerformRedo();else Undo.PerformUndo();break;case KeyCode.Y:Undo.PerformRedo();break;default:handled=false;break;}}
            else switch(e.keyCode){
                case KeyCode.Return:Play(true);break;
                case KeyCode.LeftArrow:Select(row,sub-1,e.shiftKey);break;case KeyCode.RightArrow:Select(row,sub+1,e.shiftKey);break;case KeyCode.UpArrow:Select(row-1,sub,e.shiftKey);break;case KeyCode.DownArrow:Select(row+1,sub,e.shiftKey);break;
                case KeyCode.Home:Select(0,sub,e.shiftKey);break;case KeyCode.End:Select(Pattern.lineCount-1,sub,e.shiftKey);break;case KeyCode.PageUp:Select(row-16,sub,e.shiftKey);break;case KeyCode.PageDown:Select(row+16,sub,e.shiftKey);break;
                case KeyCode.Insert:if(editMode)SongEdit("insert line",()=>ZTrackerPatternOperations.InsertLine(Pattern,row),true);break;
                case KeyCode.Delete:if(editMode){if(e.shiftKey)SongEdit("delete line",()=>ZTrackerPatternOperations.DeleteLine(Pattern,row),true);else if(anchorRow<0){var f=fields[sub];SongEdit("clear cell",()=>EditField(row,f,(n,x,v)=>{CopyField(f.kind,n,x,v,new NoteCell(),new EffectCell(),new EventCell());if(f.kind==FieldKind.Note)CopyField(FieldKind.Instrument,n,x,v,new NoteCell(),new EffectCell(),new EventCell());}));Advance();}else ClearBlock();}break;
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
                ShowFxSuggestions(f,pendingId);
                if(pendingId.Length<2)return true;string entered=pendingId.Substring(0,2);CloseFxSuggestions();SetCommandIdentifier(f,entered);Advance();return true;
            }
            if(nibble<0)return false;
            int previous=0;var note=Note(row,f);switch(f.kind){case FieldKind.Instrument:previous=note.instrument;break;case FieldKind.Volume:previous=note.volume.value;break;case FieldKind.Pan:previous=note.pan.value;break;case FieldKind.Delay:previous=note.delay;break;case FieldKind.LocalValue:previous=note.sampleFx.value;break;case FieldKind.EffectValue:previous=Effect(row,f).command.value;break;}
            int value=hexDigit==0?nibble<<4:(previous&0xF0)|nibble;int max=column?128:255;if(value>max){lastError="Volume and pan values must be 00–80";RefreshTransport();return true;}
            SongEdit("enter hex value",()=>EditField(row,f,(n,x,v)=>{switch(f.kind){case FieldKind.Instrument:n.instrumentPresent=true;n.instrument=value;break;case FieldKind.Volume:n.volume.kind=ValueKind.Value;n.volume.value=value;break;case FieldKind.Pan:n.pan.kind=ValueKind.Value;n.pan.value=value;break;case FieldKind.Delay:n.delayPresent=true;n.delay=value;break;case FieldKind.LocalValue:n.sampleFx.valuePresent=true;n.sampleFx.value=value;break;case FieldKind.EffectValue:x.command.valuePresent=true;x.command.value=value;break;}}));hexDigit++;if(hexDigit>=2)Advance();return true;
        }
        void ForBlock(Action<int,int> edit){int r0=anchorRow<0?row:Math.Min(row,anchorRow),r1=anchorRow<0?row:Math.Max(row,anchorRow),c0=anchorRow<0?sub:Math.Min(sub,anchorSub),c1=anchorRow<0?sub:Math.Max(sub,anchorSub);for(int r=r0;r<=r1;r++)for(int c=c0;c<=c1;c++)edit(r,c);}
        void CopyBlock(){clipboard.Clear();int r0=anchorRow<0?row:Math.Min(row,anchorRow),c0=anchorRow<0?sub:Math.Min(sub,anchorSub);ForBlock((r,c)=>{var f=fields[c];clipboard.Add(new CopiedField{r=r-r0,c=c-c0,kind=f.kind,note=Clone(Note(r,f)),effect=Clone(Effect(r,f)),evt=Clone(Event(r,f))});});}
        void PasteBlock(){if(clipboard.Count==0||fields.Count==0)return;SongEdit("paste block",()=>{foreach(var p in clipboard){int rr=row+p.r,cc=sub+p.c;if(rr>=Pattern.lineCount||cc>=fields.Count||fields[cc].kind!=p.kind)continue;var f=fields[cc];EditField(rr,f,(n,x,v)=>CopyField(p.kind,n,x,v,p.note,p.effect,p.evt));}});}
        string patternNotice="";
        /// Paste Insert: in the columns the clipboard lands on, rows from the cursor down move down by the
        /// clipboard's height (rows pushed past the end are dropped and counted), then the clipboard is pasted
        /// into the gap. A track's automation moves too when every visible field of that track is pasted.
        internal void PasteInsertBlock()
        {
            if(clipboard.Count==0||fields.Count==0||Pattern==null)return;
            int height=clipboard.Max(p=>p.r)+1,width=clipboard.Max(p=>p.c)+1,c0=sub,c1=Math.Min(fields.Count-1,sub+width-1),lines=Pattern.lineCount,start=row;
            var droppedRows=new HashSet<int>();int droppedPoints=0;
            SongEdit("paste insert",()=>
            {
                // Read every moved value from a copy taken before any write: a command's identifier and its
                // value are separate fields over one cell, so shifting one must not feed the other.
                var original=new Dictionary<int,PatternTrack>();
                for(int c=c0;c<=c1;c++){int ti=fields[c].track;if(!original.ContainsKey(ti))original[ti]=Clone(PatternTrack(ti,true));}
                for(int c=c0;c<=c1;c++)
                {
                    var f=fields[c];var old=original[f.track];
                    for(int r=Math.Max(start,lines-height);r<lines;r++)if(HasPayload(r,f))droppedRows.Add(r);
                    for(int r=lines-1;r>=start;r--){int from=r-height;var line=from>=start?old.ReadLine(from):new PatternLine();
                        var n=line.notes.Find(q=>q.column==f.column)??new NoteCell();var x=line.effects.Find(q=>q.column==f.column)??new EffectCell();var v=line.events.Find(q=>q.column==f.column)??new EventCell();
                        EditField(r,f,(nn,xx,vv)=>CopyField(f.kind,nn,xx,vv,n,x,v));}
                }
                for(int t=fields[c0].track;t<=fields[c1].track;t++)
                {
                    int ti=t;if(fields.Any(f=>f.track==ti&&(fields.IndexOf(f)<c0||fields.IndexOf(f)>c1)))continue;
                    var pt=PatternTrack(ti);if(pt?.automation==null)continue;
                    foreach(var lane in pt.automation){if(lane?.points==null)continue;foreach(var point in lane.points)if(point!=null&&point.line>=start)point.line+=height;droppedPoints+=lane.points.RemoveAll(q=>q!=null&&q.line>lines);}
                }
                foreach(var p in clipboard){int rr=start+p.r,cc=c0+p.c;if(rr>=lines||cc>=fields.Count||fields[cc].kind!=p.kind)continue;var f=fields[cc];EditField(rr,f,(n,x,v)=>CopyField(p.kind,n,x,v,p.note,p.effect,p.evt));}
            });
            int pastedOff=Math.Max(0,start+height-lines);
            patternNotice=droppedRows.Count==0&&droppedPoints==0&&pastedOff==0?"":"⚠ Paste Insert dropped "+Plural(droppedRows.Count,"row")+(droppedPoints>0?" and "+Plural(droppedPoints,"automation point"):"")+" pushed past the pattern end"+(pastedOff>0?"; "+Plural(pastedOff,"copied row")+" did not fit":"")+".";
            RefreshHelp();
        }
        static string Plural(int n,string noun)=>n+" "+noun+(n==1?"":"s");
        bool HasPayload(int r,FieldSlot f){var n=Note(r,f);switch(f.kind){case FieldKind.Note:return n.note!=NoteKind.Empty;case FieldKind.Instrument:return n.instrumentPresent;case FieldKind.Volume:return n.volume.kind!=ValueKind.Empty;case FieldKind.Pan:return n.pan.kind!=ValueKind.Empty;case FieldKind.Delay:return n.delayPresent;case FieldKind.LocalId:return n.sampleFx.present;case FieldKind.LocalValue:return n.sampleFx.valuePresent;case FieldKind.EffectId:return Effect(r,f).command.present;case FieldKind.EffectValue:return Effect(r,f).command.valuePresent;default:return Event(r,f).present;}}
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
                var view=ReadInstrument(asset);
                if(view!=null){var sets=ZTrackerMigration.ResolveParameterSets(view);var names=new List<string>{"Base"};names.AddRange(view.parameters.presets.Select(p=>p.name));int selected=sets.FindIndex(s=>s.id==n.parameterSetId);cellDetails.Add(Z.MiniRadio(Math.Max(0,selected),names.ToArray(),"Choose the instrument variation used by this note column.",v=>SongEdit("note variation",()=>EditField(row,f,(note,e,ev)=>note.parameterSetId=v==0?"":sets[v].id)),wrap:true));}}
            else if(f.kind==FieldKind.EffectId||f.kind==FieldKind.EffectValue||f.kind==FieldKind.LocalId||f.kind==FieldKind.LocalValue||f.kind==FieldKind.Volume||f.kind==FieldKind.Pan){var cmd=CommandAt(row,f);
                cellDetails.Add(Z.MiniRadio((int)cmd.scope,new[]{"Column","Track","Global","Unresolved"},"Choose the command's intended scope; unsupported placements remain flagged.",v=>SongEdit("command scope",()=>EditField(row,f,(n,e,ev)=>CommandOf(f,n,e).scope=(CommandScope)v)),wrap:true));
                var names=new List<string>{"Automatic"};names.AddRange(Enumerable.Range(0,Math.Max(0,Data.tracks[f.track].visibleNoteColumns)).Select(i=>"Column "+(i+1)));cellDetails.Add(Z.MiniRadio(cmd.targetColumn+1,names.ToArray(),"Select a note-column target, preserving its explicit identity.",v=>SongEdit("command column",()=>EditField(row,f,(n,e,ev)=>CommandOf(f,n,e).targetColumn=v-1)),wrap:true));}
            controlScroll.schedule.Execute(()=>controlScroll.scrollOffset=offset);
        }
        CommandData CommandAt(int r,FieldSlot f)=>CommandOf(f,Note(r,f),Effect(r,f));
        static CommandData CommandOf(FieldSlot f,NoteCell n,EffectCell e)=>f.kind==FieldKind.EffectId||f.kind==FieldKind.EffectValue?e.command:f.kind==FieldKind.Volume?n.volume.command:f.kind==FieldKind.Pan?n.pan.command:n.sampleFx;
        void RefreshHelp()
        {
            if(help==null||fields.Count==0)return;
            if(patternNotice!=""){help.text=patternNotice;help.tooltip=patternNotice;return;}
            var f=fields[sub];string id=f.kind==FieldKind.EffectId||f.kind==FieldKind.EffectValue?Effect(row,f).command.identifier:f.kind==FieldKind.LocalId||f.kind==FieldKind.LocalValue?Note(row,f).sampleFx.identifier:f.kind==FieldKind.Volume&&Note(row,f).volume.kind==ValueKind.Command?Note(row,f).volume.command.identifier:f.kind==FieldKind.Pan&&Note(row,f).pan.kind==ValueKind.Command?Note(row,f).pan.command.identifier:"";
            var command=FxCommandCatalog.Find(id,f.kind,Data.tracks[f.track],Data);var authored=CommandAt(row,f);help.text=string.IsNullOrEmpty(id)?"":command==null?id+" — unsupported or unavailable here; retained":authored.valuePresent&&!FxCommandCatalog.SupportsValue(id,authored.value)?command.Display+" — argument outside supported range":command.Display;help.tooltip=help.text;
        }
    }
}
