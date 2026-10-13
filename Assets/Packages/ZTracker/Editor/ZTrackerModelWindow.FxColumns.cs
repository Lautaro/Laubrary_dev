using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    // Track FX columns as an editing view (T-0597 / T-0598). Every command stays in the column it was typed in,
    // because the engine keeps per-column command memory: the column count, the +N expander and bound columns
    // only decide how those stored cells are shown and typed, never where they live.
    public sealed partial class ZTrackerModelWindow
    {
        internal const int MaxEffectColumns=8;
        const float OverflowGutter=14;
        // Per track index: rows of the shown pattern whose playing commands sit beyond the shown FX columns
        // (row → how many), and how many columns would show them all.
        readonly Dictionary<int,Dictionary<int,int>> overflowRows=new Dictionary<int,Dictionary<int,int>>();
        readonly Dictionary<int,int> overflowColumns=new Dictionary<int,int>();
        readonly Dictionary<int,float> trackEndX=new Dictionary<int,float>();
        ZuiPopover bindMenu;
        bool drawing;int drawTrack,drawColumn,drawLastRow,drawLastValue,drawGroup;

        void MeasureOverflow()
        {
            overflowRows.Clear();overflowColumns.Clear();if(Pattern==null||Data==null)return;
            for(int ti=0;ti<Data.tracks.Count;ti++)
            {
                var t=Data.tracks[ti];if(t.kind==TrackKind.Event)continue;var pt=PatternTrack(ti);if(pt==null)continue;
                int need=t.visibleEffectColumns;Dictionary<int,int> rows=null;
                foreach(var line in pt.lines)
                {
                    if(line==null||line.line<0||line.line>=Pattern.lineCount||line.effects==null)continue;int hidden=0;
                    foreach(var e in line.effects)if(e!=null&&!e.inactive&&e.command!=null&&e.command.HasPayload&&e.column>=t.visibleEffectColumns){hidden++;need=Math.Max(need,Math.Min(MaxEffectColumns,e.column+1));}
                    if(hidden>0){if(rows==null)rows=new Dictionary<int,int>();rows[line.line]=hidden;}
                }
                if(rows!=null){overflowRows[ti]=rows;overflowColumns[ti]=need;}
            }
        }

        static EffectColumnView ColumnView(TrackData t,int column)=>t?.effectColumnViews?.Find(v=>v!=null&&v.column==column);
        static bool IsFx(FieldKind kind)=>kind==FieldKind.EffectId||kind==FieldKind.EffectValue;
        /// The command id a bound column shows, or null when the column is plain or its command was removed.
        string BoundId(int trackIndex,int column)
        {
            if(Data==null||trackIndex<0||trackIndex>=Data.tracks.Count)return null;var v=ColumnView(Data.tracks[trackIndex],column);if(v==null||string.IsNullOrEmpty(v.command))return null;
            if(v.command.Length==2)return ZTrackerCommandRegistry.Normalize(v.command);
            var p=ZTrackerCommandRegistry.FindByUid(Data,v.command);return p==null?null:ZTrackerCommandRegistry.Normalize(p.id);
        }
        bool BoundBroken(int trackIndex,int column){var v=ColumnView(Data.tracks[trackIndex],column);return v!=null&&!string.IsNullOrEmpty(v.command)&&BoundId(trackIndex,column)==null;}
        bool BarView(int trackIndex,int column)=>BoundId(trackIndex,column)!=null&&ColumnView(Data.tracks[trackIndex],column).bar;
        string BoundId(FieldSlot f)=>IsFx(f.kind)?BoundId(f.track,f.column):null;
        bool IsBarCell(FieldSlot f)=>IsFx(f.kind)&&BarView(f.track,f.column);
        string BoundName(int trackIndex,int column)
        {
            var v=ColumnView(Data.tracks[trackIndex],column);string id=BoundId(trackIndex,column);if(id==null)return null;
            var p=v.command.Length==2?null:ZTrackerCommandRegistry.FindByUid(Data,v.command);
            if(p!=null)return p.name+" (00 = "+p.min.ToString("0.##")+", FF = "+p.max.ToString("0.##")+")";
            var entry=FxCommandCatalog.Find(id,FieldKind.EffectId,Data.tracks[trackIndex]);return entry==null?"not available on this track":entry.description+" ("+entry.range+")";
        }

        /// Column title text for the field-title row: FX1… or the bound command's id.
        string FxTitle(FieldSlot f)
        {
            if(f.kind!=FieldKind.EffectId)return f.title;
            if(BoundBroken(f.track,f.column))return "??";
            string id=BoundId(f.track,f.column);return id==null?f.title:id+(BarView(f.track,f.column)?" ▮":"");
        }
        string FxTitleTooltip(FieldSlot f)
        {
            string name="FX"+(f.column+1);
            if(BoundBroken(f.track,f.column))return name+" was bound to a command that no longer exists; its cells show as typed. Right-click (or Alt+B in the column) to bind it again or unbind.";
            string id=BoundId(f.track,f.column);
            if(id==null)return "Track effect column "+name+". Right-click (or Alt+B in the column) to bind it to one command, so typing only needs the value and it can be drawn as bars.";
            return name+" shows only "+id+": "+BoundName(f.track,f.column)+". Right-click (or Alt+B in the column) for bar view, another command or Unbind; Alt+V toggles bar view.";
        }

        // ───────────── column count and the +N expander ─────────────

        internal void SetFxColumns(int trackIndex,int count)
        {
            if(Data==null||trackIndex<0||trackIndex>=Data.tracks.Count)return;var t=Data.tracks[trackIndex];count=Mathf.Clamp(count,0,MaxEffectColumns);if(count==t.visibleEffectColumns)return;
            FieldSlot? cursor=fields.Count>0?fields[sub]:(FieldSlot?)null;
            SongEdit("effect columns",()=>t.visibleEffectColumns=count,true);
            if(cursor.HasValue){var c=cursor.Value;int at=fields.FindIndex(f=>f.track==c.track&&f.column==c.column&&f.kind==c.kind);if(at<0)at=fields.FindLastIndex(f=>f.track==c.track);if(at>=0&&at!=sub){sub=at;RefreshCells();RefreshCellDetails();}}
        }

        Label HeaderGlyph(string text,string name,string tooltip,Action click)
        {
            var l=Z.Text(text,tooltip:tooltip);l.name=name;l.AddToClassList("tracker-cell");l.AddToClassList("tracker-header-glyph");l.pickingMode=PickingMode.Position;
            l.AddManipulator(new Clickable(()=>{click();grid?.Focus();}));return l;
        }

        /// −, + and +N at the right end of a track's name header. They are siblings laid over the header, so a
        /// press on them never starts the header's reorder drag; they travel with the header while it is dragged.
        void AddFxHeaderControls(int ti,float x,float width,float y,Label title,List<VisualElement> column)
        {
            var t=Data.tracks[ti];if(t.kind==TrackKind.Event)return;float right=x+width;
            // + over − in one 10 px column, so even a one-column track keeps most of its name visible.
            var plus=HeaderGlyph("+","pattern-fx-add-"+ti,"Show one more FX column on this track ("+t.visibleEffectColumns+" of "+MaxEffectColumns+" shown).",()=>SetFxColumns(ti,t.visibleEffectColumns+1));
            var minus=HeaderGlyph("−","pattern-fx-remove-"+ti,"Show one FX column fewer on this track. Commands in a hidden column stay stored, keep playing and show as +N.",()=>SetFxColumns(ti,t.visibleEffectColumns-1));
            right-=10;Place(plus,right,y,10);Place(minus,right,y+10,10);plus.style.height=minus.style.height=10;plus.style.minHeight=minus.style.minHeight=10;plus.AddToClassList("tracker-header-glyph-half");minus.AddToClassList("tracker-header-glyph-half");
            plus.style.visibility=t.visibleEffectColumns<MaxEffectColumns?UnityEngine.UIElements.Visibility.Visible:UnityEngine.UIElements.Visibility.Hidden;
            minus.style.visibility=t.visibleEffectColumns>0?UnityEngine.UIElements.Visibility.Visible:UnityEngine.UIElements.Visibility.Hidden;
            gridCanvas.Add(minus);gridCanvas.Add(plus);column.Add(minus);column.Add(plus);float reserved=10;
            if(overflowColumns.TryGetValue(ti,out int need)&&need>t.visibleEffectColumns)
            {
                int rows=overflowRows[ti].Count;
                var more=HeaderGlyph("+"+(need-t.visibleEffectColumns),"pattern-fx-expand-"+ti,Plural(rows,"row")+" of this pattern "+(rows==1?"has":"have")+" commands beyond the shown FX columns; they still play. Click to show "+need+" FX columns.",()=>SetFxColumns(ti,need));
                more.AddToClassList("tracker-fx-overflow");right-=16;Place(more,right,y,16);gridCanvas.Add(more);column.Add(more);reserved+=16;
            }
            title.style.paddingRight=reserved+1;title.style.textOverflow=TextOverflow.Ellipsis;
        }

        void AddOverflowBadges(int r,List<VisualElement> added)
        {
            foreach(var pair in overflowRows)
            {
                if(!pair.Value.TryGetValue(r,out int hidden)||!trackEndX.TryGetValue(pair.Key,out float end))continue;int ti=pair.Key,need=overflowColumns[ti];
                var badge=HeaderGlyph("+"+hidden,"pattern-fx-overflow-"+r+"-"+ti,Plural(hidden,"command")+" on this row "+(hidden==1?"sits":"sit")+" beyond the shown FX columns and still "+(hidden==1?"plays":"play")+". Click to show "+need+" FX columns.",()=>SetFxColumns(ti,need));
                badge.AddToClassList("tracker-fx-overflow");Place(badge,end+1,(r+2)*20,OverflowGutter-1);gridCanvas.Add(badge);Column(ti).Add(badge);added?.Add(badge);
            }
        }

        // ───────────── bound columns ─────────────

        internal void Bind(int trackIndex,int column,string key)
        {
            if(Data==null||trackIndex<0||trackIndex>=Data.tracks.Count)return;var t=Data.tracks[trackIndex];
            SongEdit(key==null?"unbind FX column":"bind FX column",()=>{
                if(t.effectColumnViews==null)t.effectColumnViews=new List<EffectColumnView>();var v=ColumnView(t,column);
                if(key==null){if(v!=null)t.effectColumnViews.Remove(v);return;}
                if(v==null){v=new EffectColumnView{column=column};t.effectColumnViews.Add(v);}v.command=key;
            },true);
        }

        internal void SetBarView(int trackIndex,int column,bool bar)
        {
            var t=Data.tracks[trackIndex];var v=ColumnView(t,column);if(v==null||v.bar==bar)return;
            SongEdit(bar?"bar view":"character view",()=>ColumnView(t,column).bar=bar);BuildVisibleCells();RefreshCellDetails();
        }

        Label FxTitleLabel(int trackIndex,int column)=>gridCanvas?.Q<Label>("pattern-fx-title-"+trackIndex+"-"+column);

        internal void ShowBindMenu(int trackIndex,int column,VisualElement anchor=null,bool retried=false)
        {
            CloseFxSuggestions();bindMenu?.Close();if(Data==null||trackIndex<0||trackIndex>=Data.tracks.Count)return;
            // Right after a rebuild the column title may not be laid out yet: open once the grid has its size.
            if(anchor==null&&FxTitleLabel(trackIndex,column)==null&&!retried&&grid!=null){grid.schedule.Execute(()=>ShowBindMenu(trackIndex,column,null,true));return;}
            if(anchor==null)anchor=FxTitleLabel(trackIndex,column);if(anchor==null&&labels.TryGetValue(new Vector2Int(row,sub),out var cell))anchor=cell;if(anchor==null)anchor=grid;if(anchor==null)return;
            var t=Data.tracks[trackIndex];var view=ColumnView(t,column);string bound=BoundId(trackIndex,column);string key=view?.command??"";
            var menu=Z.Menu(anchor).Width(320).Search("Type a command id, then Enter",300);
            if(key!="")
            {
                menu.Section("FX"+(column+1)+(bound!=null?" · "+bound:" · removed command"));
                if(bound!=null)menu.Toggle("Bar view","Draw this column's values as bars: 00 at the left, FF at the right. Typing hex still works, and in Edit mode dragging across the bars draws values (Alt+V).",view.bar,v=>SetBarView(trackIndex,column,v));
                menu.Item("Unbind","Show this column as plain characters again. Nothing in the pattern changes.",()=>Bind(trackIndex,column,null));
            }
            menu.Section("Song commands","Settings this song has given a command id. Right-click a slider (or Song › Commands › Add…) to give one an id.");
            var defined=(Data.dynamicParameters??new List<DynamicParameterData>()).Where(p=>p!=null&&ZTrackerCommandRegistry.ValidateId(null,p.id,null,out _)==null).OrderBy(p=>ZTrackerCommandRegistry.Normalize(p.id),StringComparer.Ordinal).ToList();
            if(defined.Count==0)menu.Item("None defined yet","Right-click any slider (or Song › Commands › Add…) to give a setting a command id; it can then be bound here.",null,enabled:false);
            foreach(var p in defined){var uid=p.uid;menu.Item(ZTrackerCommandRegistry.Normalize(p.id)+"  "+p.name,"Bind FX"+(column+1)+" to "+ZTrackerCommandRegistry.Normalize(p.id)+": 00 sets "+p.min.ToString("0.##")+", FF sets "+p.max.ToString("0.##")+".",()=>Bind(trackIndex,column,uid),@checked:key==uid);}
            menu.Section("Built-in","The track commands ZTracker understands.");
            foreach(var entry in FxCommandCatalog.For(FieldKind.EffectId,t)){var id=entry.id;menu.Item(id+"  "+entry.description,"Bind FX"+(column+1)+" to "+entry.Display+".",()=>Bind(trackIndex,column,id),@checked:key==id);}
            menu.OnClosed(()=>{bindMenu=null;grid?.Focus();});bindMenu=menu.Show();
        }

        void ShowBindMenuAtCursor(){if(fields.Count==0)return;var f=fields[sub];if(!IsFx(f.kind)){patternNotice="Put the cursor in a track FX column to bind it to a command (Alt+B).";RefreshHelp();return;}ShowBindMenu(f.track,f.column);}
        void ToggleBarAtCursor(){if(fields.Count==0)return;var f=fields[sub];if(!IsFx(f.kind)||BoundId(f.track,f.column)==null){patternNotice="Bar view needs a bound column: Alt+B binds the FX column under the cursor.";RefreshHelp();return;}SetBarView(f.track,f.column,!ColumnView(Data.tracks[f.track],f.column).bar);}

        static void SetBoundValue(EffectCell x,string id,int value)
        {
            if(!(x.command.present&&ZTrackerCommandRegistry.Normalize(x.command.identifier)==id))x.command=new CommandData{present=true,identifier=id,scope=id[0]=='Z'?CommandScope.Global:CommandScope.Track};
            x.command.valuePresent=true;x.command.value=Mathf.Clamp(value,0,255);
        }

        /// Typing in a bound column: hex digits are the value of the bound command; anything else is refused.
        bool EnterBoundCharacter(FieldSlot f,string id,int nibble)
        {
            if(nibble<0){patternNotice="FX"+(f.column+1)+" shows only "+id+": type its value in hex (00–FF). Alt+B changes or removes the binding.";RefreshHelp();return true;}
            var current=Effect(row,f).command;int previous=current.present&&ZTrackerCommandRegistry.Normalize(current.identifier)==id&&current.valuePresent?current.value:0;
            int value=hexDigit==0?nibble<<4:(previous&0xF0)|nibble;
            SongEdit("enter bound value",()=>EditField(row,f,(n,x,v)=>SetBoundValue(x,id,value)));hexDigit++;if(hexDigit>=2)Advance();return true;
        }

        /// Draws a bound column's cell: a dim id when it holds the bound command, an amber warning when it holds
        /// another command (kept and still played, never hidden), or a bar in bar view.
        void DecorateFxCell(Label label,int line,FieldSlot f)
        {
            string id=BoundId(f);var bar=label.Q<VisualElement>("fx-bar");
            if(id==null){bar?.RemoveFromHierarchy();label.RemoveFromClassList("tracker-bound-id");label.RemoveFromClassList("tracker-bound-other");label.RemoveFromClassList("tracker-bound-plot");return;}
            var c=Effect(line,f).command;bool match=c.present&&ZTrackerCommandRegistry.Normalize(c.identifier)==id,other=c.HasPayload&&!match;
            label.EnableInClassList("tracker-bound-other",other);
            if(other)label.tooltip=(c.present?c.identifier:"..")+(c.valuePresent?c.value.ToString("X2"):"..")+" sits in "+"FX"+(f.column+1)+", which shows only "+id+". It is kept and still plays; it is shown so nothing is hidden. "+FieldTooltip(f);
            bool plot=!other&&BarView(f.track,f.column);label.EnableInClassList("tracker-bound-plot",plot);
            label.EnableInClassList("tracker-bound-id",!plot&&match&&f.kind==FieldKind.EffectId);
            if(!plot){bar?.RemoveFromHierarchy();return;}
            label.text="";
            if(!match){bar?.RemoveFromHierarchy();return;}
            float total=Width(FieldKind.EffectId)+Width(FieldKind.EffectValue),start=f.kind==FieldKind.EffectId?0:Width(FieldKind.EffectId);
            float u=c.valuePresent?c.value/255f:0f,w=Mathf.Clamp(u*total-start,0,Width(f.kind));if(f.kind==FieldKind.EffectId)w=Mathf.Max(w,1.5f);
            if(bar==null){bar=new VisualElement{name="fx-bar",pickingMode=PickingMode.Ignore};bar.AddToClassList("tracker-fx-bar");bar.style.position=Position.Absolute;bar.style.left=0;bar.style.top=4;bar.style.height=12;label.Add(bar);}
            bar.style.width=w;bar.style.display=w>0?DisplayStyle.Flex:DisplayStyle.None;
            label.tooltip=id+(c.valuePresent?c.value.ToString("X2"):"..")+" ("+Mathf.RoundToInt(u*100)+"% of the range). "+FieldTooltip(f);
        }

        // ───────────── drawing values in bar view ─────────────

        void BeginDraw(int r,int c,Vector2 world)
        {
            var f=fields[c];Select(r,c,false);
            Undo.IncrementCurrentGroup();drawGroup=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Tracker: draw values");
            try{CompleteUndo(song,"Tracker: draw values");}catch(Exception ex){lastError=ex.Message;RefreshTransport();return;}
            drawing=true;drawTrack=f.track;drawColumn=f.column;drawLastRow=-1;DrawTo(world);
        }

        void DrawTo(Vector2 world)
        {
            if(!drawing||gridCanvas==null||Pattern==null)return;string id=BoundId(drawTrack,drawColumn);int idField=fields.FindIndex(x=>x.track==drawTrack&&x.column==drawColumn&&x.kind==FieldKind.EffectId);if(id==null||idField<0)return;
            var p=gridCanvas.WorldToLocal(world);int r=Mathf.Clamp(Mathf.FloorToInt(p.y/20)-2,0,Pattern.lineCount-1);
            int value=Mathf.RoundToInt(Mathf.Clamp01((p.x-fieldX[idField])/(Width(FieldKind.EffectId)+Width(FieldKind.EffectValue)))*255);
            var f=fields[idField];int from=drawLastRow<0?r:drawLastRow;
            // A fast drag skips rows between pointer events; fill them along the straight line between the two.
            for(int at=Math.Min(from,r);at<=Math.Max(from,r);at++){int v=drawLastRow<0||from==r?value:Mathf.RoundToInt(Mathf.Lerp(drawLastValue,value,(float)Math.Abs(at-from)/Math.Abs(r-from)));int rr=at;EditField(rr,f,(n,x,ev)=>SetBoundValue(x,id,v));}
            drawLastRow=r;drawLastValue=value;RefreshCells();
        }

        void EndDraw()
        {
            if(!drawing)return;drawing=false;EditorUtility.SetDirty(song);CollapseUndo(drawGroup,song);RefreshLive();RefreshTransport();
        }
    }
}
